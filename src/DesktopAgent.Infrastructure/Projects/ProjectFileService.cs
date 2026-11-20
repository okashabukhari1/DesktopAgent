using System.Collections.Concurrent;
using System.Text;
using DesktopAgent.Core.Interfaces;
using DesktopAgent.Core.Projects;

namespace DesktopAgent.Infrastructure.Projects;

public sealed class ProjectFileService(IProjectCatalog catalog, ISettingsService settings, ISecretProtection secrets) : IProjectFileService
{
    private sealed record Cache(string Root, string Settings, DateTimeOffset At, ProjectTree Tree);
    private readonly ConcurrentDictionary<Guid, Cache> _trees = new();
    private static readonly HashSet<string> Generated = new(ProjectDiscoveryService.Exclusions, StringComparer.OrdinalIgnoreCase)
    { ".idea", "out", ".cache", ".tmp", "tmp", "temp", ".ssh", ".aws", ".codex", ".azure", ".vscode", "logs", "__pycache__", ".pytest_cache", ".gradle" };
    private static readonly HashSet<string> BinaryExtensions = new(StringComparer.OrdinalIgnoreCase)
    { ".exe", ".dll", ".pdb", ".zip", ".rar", ".7z", ".png", ".jpg", ".jpeg", ".gif", ".webp", ".ico", ".mp4", ".mp3", ".pdf", ".db", ".sqlite", ".sqlite3", ".mdb", ".woff", ".woff2", ".ttf", ".jar", ".class", ".pyc", ".so", ".a", ".lib", ".bin" };
    private static readonly HashSet<string> TextExtensions = new(StringComparer.OrdinalIgnoreCase)
    { ".cs", ".fs", ".vb", ".js", ".jsx", ".ts", ".tsx", ".php", ".kt", ".kts", ".java", ".py", ".html", ".htm", ".css", ".scss", ".sass", ".less", ".razor", ".cshtml", ".json", ".xml", ".xaml", ".yml", ".yaml", ".toml", ".ini", ".config", ".txt", ".md", ".rst", ".sql", ".graphql", ".sh", ".ps1", ".bat", ".cmd", ".csproj", ".fsproj", ".sln", ".slnx", ".props", ".targets", ".gradle", ".go", ".rs", ".c", ".cpp", ".h", ".hpp", ".vue", ".svelte" };
    private FileIntelligenceSettings Limits => settings.Current.FileIntelligence.Copy();
    private bool Excluded(string name) => Generated.Contains(name) || settings.Current.FileIntelligence.AdditionalExcludedDirectories.Contains(name, StringComparer.OrdinalIgnoreCase);
    public static bool IsBinaryExtension(string path) => BinaryExtensions.Contains(Path.GetExtension(path));
    public static bool SupportedText(string path) => TextExtensions.Contains(Path.GetExtension(path)) || new[] { "Dockerfile", "Makefile", "README", ".gitignore", "Pipfile", "requirements.txt" }.Contains(Path.GetFileName(path), StringComparer.OrdinalIgnoreCase);
    private ProjectProfile Project(Guid id) => catalog.Find(id) ?? throw new ProjectFileException("ProjectNotFound", "Choose a registered project first.");
    private ProjectPathGuard Guard(Guid id, string path, bool directory = false) => new(Project(id).Path, path, directory, Excluded, secrets);
    public string ResolveFilePath(Guid projectId, string relativePath)
    { using var guard = Guard(projectId, relativePath); using var stream = guard.OpenRead(); ValidateTextType(guard.FullPath); return guard.FullPath; }
    internal async Task<DesktopAgent.Core.Ai.ToolResult> WithPinnedFileAsync(Guid id, string relative, Func<string, Task<DesktopAgent.Core.Ai.ToolResult>> open, CancellationToken ct)
    {
        await catalog.InitializeAsync(ct); ct.ThrowIfCancellationRequested();
        using var guard = Guard(id, relative); ValidateTextType(guard.FullPath); await using var stream = guard.OpenRead();
        return await open(guard.FullPath);
    }
    private static void ValidateTextType(string path)
    { if (IsBinaryExtension(path)) throw new ProjectFileException("BinaryFile", "Binary/compiled files are not source retrieval targets.");
      if (!SupportedText(path)) throw new ProjectFileException("UnsupportedFile", "This file type is not supported for source retrieval."); }

    public async Task<ProjectTree> GetProjectTreeAsync(Guid projectId, bool refresh = false, CancellationToken cancellationToken = default)
    {
        await catalog.InitializeAsync(cancellationToken); var project = Project(projectId); var options = Limits;
        var stamp = System.Text.Json.JsonSerializer.Serialize(options);
        using (Guard(projectId, "", true)) { } // Revalidate even when serving cache; never trust a relocated root.
        if (!refresh && _trees.TryGetValue(projectId, out var cached) && cached.Root == project.Path && cached.Settings == stamp && cached.At > DateTimeOffset.UtcNow.AddSeconds(-30)) return cached.Tree;
        var tree = await Task.Run(() =>
        {
            var entries = new List<ProjectFileEntry>(); int skipped = 0; bool truncated = false;
            var queue = new Queue<(string Relative, int Depth)>(); queue.Enqueue(("", 0));
            var timer = System.Diagnostics.Stopwatch.StartNew();
            while (queue.TryDequeue(out var item))
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (entries.Count >= options.MaxTreeEntries || timer.Elapsed > TimeSpan.FromSeconds(15)) { truncated = true; break; }
                try
                {
                    using var guard = Guard(projectId, item.Relative, true);
                    var children = Directory.EnumerateFileSystemEntries(guard.FullPath).Take(513).ToArray();
                    if (children.Length > 512) truncated = true;
                    foreach (var path in children.Take(512).OrderBy(p => p, StringComparer.OrdinalIgnoreCase))
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        if (entries.Count >= options.MaxTreeEntries) { truncated = true; break; }
                        var name = Path.GetFileName(path);
                        if (Excluded(name) || secrets.IsSensitiveName(name)) { skipped++; continue; }
                        try
                        {
                            var attributes = File.GetAttributes(path);
                            if ((attributes & (FileAttributes.ReparsePoint | FileAttributes.System)) != 0) { skipped++; continue; }
                            var relative = Path.GetRelativePath(project.Path, path); var directory = attributes.HasFlag(FileAttributes.Directory);
                            if (directory)
                            {
                                using var child = Guard(projectId, relative, true);
                                entries.Add(new(relative, true, null, Directory.GetLastWriteTimeUtc(path)));
                                if (item.Depth < 16) queue.Enqueue((relative, item.Depth + 1)); else truncated = true;
                            }
                            else
                            { using var child = Guard(projectId, relative); using var file = child.OpenRead(); entries.Add(new(relative, false, file.Length, File.GetLastWriteTimeUtc(path), IsBinaryExtension(path))); }
                        }
                        catch (Exception e) when (e is IOException or UnauthorizedAccessException or ProjectFileException) { skipped++; }
                    }
                }
                catch (Exception e) when (e is IOException or UnauthorizedAccessException or ProjectFileException) { skipped++; }
            }
            return new ProjectTree(projectId, project.Name, entries.ToArray(), truncated, skipped, truncated ? "Tree limit reached. List a selected directory or narrow the search." : null);
        }, cancellationToken);
        // Metadata only: no source text cache. Bound cache to eight actively requested projects.
        if (_trees.Count >= 8) foreach (var key in _trees.OrderBy(x => x.Value.At).Take(_trees.Count - 7).Select(x => x.Key)) _trees.TryRemove(key, out _);
        _trees[projectId] = new(project.Path, stamp, DateTimeOffset.UtcNow, tree); return tree;
    }
    public async Task<ProjectTree> ListDirectoryAsync(Guid projectId, string relativePath, CancellationToken cancellationToken = default)
    {
        await catalog.InitializeAsync(cancellationToken);
        return await Task.Run(() =>
        {
            using var guard = Guard(projectId, relativePath, true); var entries = new List<ProjectFileEntry>(); int skipped = 0;
            var children = Directory.EnumerateFileSystemEntries(guard.FullPath).Take(513).ToArray();
            foreach (var path in children.Take(512).OrderBy(p => p, StringComparer.OrdinalIgnoreCase))
            {
                cancellationToken.ThrowIfCancellationRequested(); var name = Path.GetFileName(path);
                if (Excluded(name) || secrets.IsSensitiveName(name)) { skipped++; continue; }
                try
                {
                    var attrs = File.GetAttributes(path); if ((attrs & (FileAttributes.ReparsePoint | FileAttributes.System)) != 0) { skipped++; continue; }
                    var directory = attrs.HasFlag(FileAttributes.Directory); var relative = Path.GetRelativePath(guard.Root, path);
                    using var child = Guard(projectId, relative, directory);
                    using var file = directory ? null : child.OpenRead();
                    entries.Add(new(relative, directory, file?.Length, File.GetLastWriteTimeUtc(path), !directory && IsBinaryExtension(path)));
                } catch (Exception e) when (e is IOException or UnauthorizedAccessException or ProjectFileException) { skipped++; }
            }
            return new ProjectTree(projectId, Project(projectId).Name, entries, children.Length > 512, skipped);
        }, cancellationToken);
    }
    public async Task<ProjectFileEntry> GetFileMetadataAsync(Guid projectId, string relativePath, CancellationToken cancellationToken = default)
    {
        await catalog.InitializeAsync(cancellationToken);
        return await Task.Run(() =>
        { using var guard = Guard(projectId, relativePath); using var stream = guard.OpenRead();
          return new ProjectFileEntry(Path.GetRelativePath(guard.Root, guard.FullPath), false, stream.Length, File.GetLastWriteTimeUtc(guard.FullPath), IsBinaryExtension(guard.FullPath)); }, cancellationToken);
    }

    internal sealed record Document(string RelativePath, string[] Lines, long Size, int BytesRead, bool Redacted, bool Truncated);
    internal async Task<Document> ReadDocumentAsync(Guid id, string relative, bool range, CancellationToken ct, int? byteLimit = null)
    {
        await catalog.InitializeAsync(ct).ConfigureAwait(false); ct.ThrowIfCancellationRequested();
        using var guard = Guard(id, relative); ValidateTextType(guard.FullPath);
        await using var stream = guard.OpenRead(); var size = stream.Length; var limit = Math.Min(Limits.MaxFileBytes, byteLimit ?? Limits.MaxFileBytes);
        if (!range && size > 32768) throw new ProjectFileException("RangeRequired", "This file requires a targeted line range. Search first; no source content was returned.");
        var bytes = new byte[(int)Math.Min(size, limit)]; int count = 0;
        while (count < bytes.Length) { ct.ThrowIfCancellationRequested(); var read = await stream.ReadAsync(bytes.AsMemory(count), ct).ConfigureAwait(false); if (read == 0) break; count += read; }
        var truncated = size > count;
        string text;
        try
        {
            if (count >= 2 && bytes[0] == 0xff && bytes[1] == 0xfe) text = new UnicodeEncoding(false, false, true).GetString(bytes, 2, (count - 2) / 2 * 2);
            else if (count >= 2 && bytes[0] == 0xfe && bytes[1] == 0xff) text = new UnicodeEncoding(true, false, true).GetString(bytes, 2, (count - 2) / 2 * 2);
            else
            {
                if (bytes.AsSpan(0, count).Contains((byte)0)) throw new ProjectFileException("BinaryFile", "Binary content cannot be retrieved as code.");
                var offset = count >= 3 && bytes[0] == 0xef && bytes[1] == 0xbb && bytes[2] == 0xbf ? 3 : 0;
                // Do not expose an incomplete trailing line/literal when the byte budget stops reading.
                if (truncated) { var newline = Array.LastIndexOf(bytes, (byte)'\n', count - 1, count); count = newline >= 0 ? newline + 1 : 0; }
                text = new UTF8Encoding(false, true).GetString(bytes, Math.Min(offset, count), Math.Max(0, count - offset));
            }
        }
        catch (DecoderFallbackException) { throw new ProjectFileException("UnsupportedEncoding", "Only valid UTF-8 and BOM-marked UTF-16 source files are supported."); }
        if (truncated) { var newline = text.LastIndexOf('\n'); text = newline >= 0 ? text[..(newline + 1)] : ""; }
        if (text.Contains('\0') || text.Any(c => c < 0x20 && c is not ('\n' or '\r' or '\t'))) throw new ProjectFileException("BinaryFile", "Binary/control-character content is not source text.");
        ct.ThrowIfCancellationRequested(); text = secrets.Redact(text.Replace("\r\n", "\n").Replace('\r', '\n'), out var changed);
        var lines = text.Split('\n'); if (text.EndsWith('\n')) lines = lines[..^1];
        return new(Path.GetRelativePath(guard.Root, guard.FullPath), lines, size, bytes.Length, changed, truncated);
    }
    public async Task<ProjectFileRead> ReadFileAsync(Guid projectId, string relativePath, int? startLine = null, int? endLine = null, CancellationToken cancellationToken = default)
    {
        var limits = Limits;
        if (startLine.HasValue != endLine.HasValue || startLine is < 1 or > 100000 || endLine is < 1 or > 100000 || endLine < startLine
            || endLine - startLine + 1 > limits.MaxLinesPerRead) throw new ProjectFileException("InvalidRange", "Choose a valid bounded range of up to " + limits.MaxLinesPerRead + " lines.");
        var document = await Task.Run(() => ReadDocumentAsync(projectId, relativePath, startLine.HasValue, cancellationToken), cancellationToken);
        var first = startLine ?? 1; var last = endLine ?? Math.Min(document.Lines.Length, limits.MaxLinesPerRead);
        if (first > document.Lines.Length && document.Lines.Length > 0) throw new ProjectFileException("RangeUnavailable", "That line is beyond the available file/read budget. Narrow the search first.");
        var lines = new List<CodeLine>(); var characters = 0; bool truncated = document.Truncated || (!endLine.HasValue && document.Lines.Length > last);
        for (var n = first; n <= Math.Min(last, document.Lines.Length); n++)
        {
            cancellationToken.ThrowIfCancellationRequested(); var line = document.Lines[n - 1];
            if (characters + line.Length > limits.MaxReadCharacters) { truncated = true; break; }
            characters += line.Length; lines.Add(new(n, line));
        }
        return new(projectId, document.RelativePath, lines, document.Size, document.Redacted, truncated,
            truncated ? "Retrieval limit reached; narrow the line range/search. Unreturned text was not sent to AI." : null);
    }
}
