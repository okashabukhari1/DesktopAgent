using System.Text.Json;
using System.Text.RegularExpressions;
using System.Xml;
using System.Xml.Linq;
using DesktopAgent.Core.Interfaces;
using DesktopAgent.Core.Projects;

namespace DesktopAgent.Infrastructure.Projects;

public sealed class ProjectSearchService(ProjectFileService files, ISettingsService settings) : IProjectSearchService
{
    private static void ValidateQuery(string query)
    { if (string.IsNullOrWhiteSpace(query) || query.Length > 160 || query.Any(char.IsControl)) throw new ProjectFileException("InvalidQuery", "Enter a literal search query of 1–160 characters."); }
    private async Task<ProjectFileEntry[]> Candidates(Guid id, string? extension, string? directory, CancellationToken ct)
    {
        if (extension is not null && (extension.Length > 16 || !Regex.IsMatch(extension, @"^\.?[a-zA-Z0-9]+$"))) throw new ProjectFileException("InvalidFilter", "Use a simple file extension filter.");
        string? prefix = null;
        if (!string.IsNullOrWhiteSpace(directory))
        {
            var listing = await files.ListDirectoryAsync(id, directory, ct); // same boundary validation as reads
            prefix = directory.Replace('/', '\\').TrimEnd('\\') + "\\";
            if (directory is "." or "") prefix = null;
        }
        var tree = await files.GetProjectTreeAsync(id, cancellationToken: ct);
        return tree.Entries.Where(e => !e.IsDirectory && !e.IsBinary && ProjectFileService.SupportedText(e.RelativePath)
            && (extension is null || Path.GetExtension(e.RelativePath).Equals("." + extension.TrimStart('.'), StringComparison.OrdinalIgnoreCase))
            && (prefix is null || e.RelativePath.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)))
            .OrderByDescending(e => e.RelativePath.Split('\\').Any(p => p.Equals("src", StringComparison.OrdinalIgnoreCase)))
            .ThenBy(e => e.RelativePath, StringComparer.OrdinalIgnoreCase).ToArray();
    }
    private static int NameScore(string path, string query)
    {
        var name = Path.GetFileName(path); var stem = Path.GetFileNameWithoutExtension(path);
        if (name.Equals(query, StringComparison.OrdinalIgnoreCase) || stem.Equals(query, StringComparison.OrdinalIgnoreCase)) return 1000;
        if (name.StartsWith(query, StringComparison.OrdinalIgnoreCase)) return 800;
        if (name.Contains(query, StringComparison.OrdinalIgnoreCase)) return 600;
        if (path.Contains(query, StringComparison.OrdinalIgnoreCase)) return 400;
        return 0;
    }
    public async Task<ProjectSearchResult> SearchFileNamesAsync(Guid projectId, string query, string? extension = null, string? directory = null, CancellationToken cancellationToken = default)
    {
        ValidateQuery(query); var candidates = await Candidates(projectId, extension, directory, cancellationToken);
        var matches = candidates.Select(e => new { e.RelativePath, Score = NameScore(e.RelativePath, query) }).Where(e => e.Score > 0)
            .OrderByDescending(e => e.Score).ThenBy(e => e.RelativePath, StringComparer.OrdinalIgnoreCase).ToArray();
        var limit = settings.Current.FileIntelligence.MaxSearchResults;
        var tree = await files.GetProjectTreeAsync(projectId, cancellationToken: cancellationToken);
        return new(matches.Take(limit).Select(e => new CodeSearchResult(projectId, e.RelativePath, 1, 1, "Filename match", [], [], e.Score)).ToArray(), matches.Length > limit || tree.Truncated, candidates.Length, tree.Skipped,
            tree.Truncated ? tree.Warning : null);
    }
    public Task<ProjectSearchResult> SearchTextAsync(Guid projectId, string query, string? extension = null, string? directory = null, CancellationToken cancellationToken = default) => Task.Run(() => Search(projectId, query, false, extension, directory, cancellationToken), cancellationToken);
    public Task<ProjectSearchResult> SearchSymbolsAsync(Guid projectId, string query, CancellationToken cancellationToken = default) => Task.Run(() => Search(projectId, query, true, null, null, cancellationToken), cancellationToken);
    private async Task<ProjectSearchResult> Search(Guid id, string query, bool symbols, string? extension, string? directory, CancellationToken ct)
    {
        ValidateQuery(query); var candidates = await Candidates(id, extension, directory, ct);
        var limits = settings.Current.FileIntelligence.Copy(); var results = new List<CodeSearchResult>();
        long bytes = 0; int examined = 0, skipped = 0; bool truncated = false;
        var timer = System.Diagnostics.Stopwatch.StartNew();
        foreach (var entry in candidates.OrderByDescending(e => NameScore(e.RelativePath, query)))
        {
            ct.ThrowIfCancellationRequested();
            if (examined >= 1000 || bytes >= limits.MaxSearchBytes || timer.Elapsed > TimeSpan.FromSeconds(15) || results.Count >= limits.MaxSearchResults * 8) { truncated = true; break; }
            try
            {
                var doc = await files.ReadDocumentAsync(id, entry.RelativePath, true, ct, (int)Math.Min(limits.MaxFileBytes, limits.MaxSearchBytes - bytes));
                bytes += doc.BytesRead; examined++; truncated |= doc.Truncated;
                var declarations = symbols ? LightweightSymbols.Extract(entry.RelativePath, doc.Lines, ct).ToDictionary(d => d.Line, d => d) : null;
                truncated |= doc.Lines.Length > 100000;
                for (var n = 0; n < Math.Min(doc.Lines.Length, 100000); n++)
                {
                    ct.ThrowIfCancellationRequested(); var line = doc.Lines[n];
                    if (line.Length > 4096) { truncated = true; continue; }
                    var index = line.IndexOf(query, StringComparison.OrdinalIgnoreCase);
                    LightweightSymbols.Symbol? symbol = null;
                    if (symbols)
                    {
                        if (declarations?.TryGetValue(n + 1, out symbol) != true || symbol is null || !symbol.Name.Contains(query, StringComparison.OrdinalIgnoreCase)) continue;
                        index = Math.Max(0, line.IndexOf(symbol.Name, StringComparison.Ordinal));
                    }
                    if (index < 0) continue;
                    var before = n > 0 ? new[] { new CodeLine(n, Clip(doc.Lines[n - 1])) } : [];
                    var after = n + 1 < doc.Lines.Length ? new[] { new CodeLine(n + 2, Clip(doc.Lines[n + 1])) } : [];
                    var score = 300 + NameScore(entry.RelativePath, query) + (line.Contains(query, StringComparison.Ordinal) ? 100 : 0)
                        + (symbol?.Name.Equals(query, StringComparison.OrdinalIgnoreCase) == true ? 1000 : 0);
                    results.Add(new(id, entry.RelativePath, n + 1, index + 1, Clip(line), before, after, score, symbol?.Kind, symbol?.Name));
                    if (results.Count >= limits.MaxSearchResults * 8) { truncated = true; break; }
                }
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException or ProjectFileException) { skipped++; }
        }
        var tree = await files.GetProjectTreeAsync(id, cancellationToken: ct);
        var ranked = results.OrderByDescending(r => r.Score).ThenBy(r => r.RelativePath, StringComparer.OrdinalIgnoreCase).ThenBy(r => r.LineNumber).Take(limits.MaxSearchResults).ToArray();
        return new(ranked, truncated || results.Count > ranked.Length || tree.Truncated, examined, skipped + tree.Skipped,
            truncated || tree.Truncated ? "Search is bounded/partial; narrow the query, extension or directory. It is not proof that no other references exist." : null);
    }
    private static string Clip(string text) => text.Length <= 240 ? text : text[..240] + "…";
    public Task<ProjectDependencyReport> GetDependenciesAsync(Guid projectId, CancellationToken cancellationToken = default) => Task.Run(() => Dependencies(projectId, cancellationToken), cancellationToken);
    private async Task<ProjectDependencyReport> Dependencies(Guid projectId, CancellationToken cancellationToken)
    {
        var tree = await files.GetProjectTreeAsync(projectId, cancellationToken: cancellationToken); var dependencies = new List<ProjectDependency>();
        var manifests = tree.Entries.Where(e => !e.IsDirectory && (Path.GetFileName(e.RelativePath) is "package.json" or "composer.json" || Path.GetExtension(e.RelativePath) is ".csproj" or ".fsproj")).ToArray();
        int read = 0, skipped = 0;
        foreach (var entry in manifests.Take(12))
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                var doc = await files.ReadDocumentAsync(projectId, entry.RelativePath, true, cancellationToken, 131072);
                if (doc.Truncated) { skipped++; continue; }
                var text = string.Join("\n", doc.Lines);
                void Add(string? name, string? version)
                { if (name is not null && name.Length <= 150 && Regex.IsMatch(name, @"^[A-Za-z0-9@_./-]+$"))
                    dependencies.Add(new(entry.RelativePath, name, version is { Length: <= 80 } && Regex.IsMatch(version, @"^[A-Za-z0-9.*^~+<>=| -]+$") ? version : null)); }
                if (Path.GetExtension(entry.RelativePath).EndsWith("proj"))
                {
                    using var reader = XmlReader.Create(new StringReader(text), new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null });
                    var xml = XDocument.Load(reader);
                    foreach (var package in xml.Descendants().Where(e => e.Name.LocalName == "PackageReference").Take(100)) Add(package.Attribute("Include")?.Value, package.Attribute("Version")?.Value);
                }
                else
                {
                    using var json = JsonDocument.Parse(text, new JsonDocumentOptions { MaxDepth = 16 });
                    foreach (var key in new[] { "dependencies", "devDependencies", "require", "require-dev" })
                        if (json.RootElement.TryGetProperty(key, out var values) && values.ValueKind == JsonValueKind.Object)
                            foreach (var item in values.EnumerateObject().Take(100)) Add(item.Name, item.Value.ValueKind == JsonValueKind.String ? item.Value.GetString() : null);
                }
                read++;
            }
            catch (Exception e) when (e is JsonException or XmlException or IOException or UnauthorizedAccessException or ProjectFileException or InvalidOperationException) { skipped++; }
        }
        return new(dependencies.Distinct().Take(80).ToArray(), read, skipped, tree.Truncated || manifests.Length > 12 || dependencies.Count > 80);
    }
}

/// <summary>Conservative lexical declarations/routes, not a semantic compiler or reference index.</summary>
internal static class LightweightSymbols
{
    internal sealed record Symbol(int Line, string Name, string Kind);
    private static readonly Regex Declaration = new(@"\b(class|interface|record|enum|struct|function|def|fun)\s+([A-Za-z_$][\w$]*)", RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100));
    private static readonly Regex Method = new(@"^\s*(?:(?:public|private|protected|internal|static|async|export|override|virtual|abstract|final|suspend)\s+)+(?:[\w<>?\[\],.]+\s+)?([A-Za-z_$][\w$]*)\s*\(", RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100));
    private static readonly Regex Arrow = new(@"\b(?:const|let|var)\s+([A-Za-z_$][\w$]*)\s*=.*=>", RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100));
    private static readonly Regex Route = new(@"(?:\b(?:MapGet|MapPost|MapPut|MapDelete|MapPatch|Route::(?:get|post|put|delete|resource)|(?:router|app)\.(?:get|post|put|delete|patch))\s*\(|\[(?:Route|HttpGet|HttpPost|HttpPut|HttpDelete|HttpPatch)\s*\()[""']([^""']+)[""']", RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100));
    public static IReadOnlyList<Symbol> Extract(string path, string[] lines, CancellationToken ct = default)
    {
        if (!new[] { ".cs", ".js", ".jsx", ".ts", ".tsx", ".php", ".kt", ".java", ".py" }.Contains(Path.GetExtension(path), StringComparer.OrdinalIgnoreCase)) return [];
        var symbols = new List<Symbol>(); bool block = false; char quote = '\0';
        for (var n = 0; n < Math.Min(lines.Length, 100000); n++)
        {
            ct.ThrowIfCancellationRequested();
            var line = lines[n]; if (line.Length > 4096) continue;
            var mask = line.ToCharArray();
            for (var i = 0; i < mask.Length; i++)
            {
                var c = line[i]; var next = i + 1 < line.Length ? line[i + 1] : '\0';
                if (block) { mask[i] = ' '; if (c == '*' && next == '/') { mask[++i] = ' '; block = false; } continue; }
                if (quote != '\0') { mask[i] = ' '; if (c == '\\' && i + 1 < mask.Length) mask[++i] = ' '; else if (c == quote) quote = '\0'; continue; }
                if (c == '/' && next == '*') { block = true; mask[i] = mask[++i] = ' '; continue; }
                if ((c == '/' && next == '/') || (Path.GetExtension(path).Equals(".py", StringComparison.OrdinalIgnoreCase) && c == '#')) { Array.Fill(mask, ' ', i, mask.Length - i); break; }
                if (c is '\'' or '"' or '`') { quote = c; mask[i] = ' '; }
            }
            var code = new string(mask);
            try
            {
                var declaration = Declaration.Match(code); var method = Method.Match(code); var arrow = Arrow.Match(code);
                if (declaration.Success) symbols.Add(new(n + 1, declaration.Groups[2].Value, declaration.Groups[1].Value));
                else if (method.Success) symbols.Add(new(n + 1, method.Groups[1].Value, "method"));
                else if (arrow.Success) symbols.Add(new(n + 1, arrow.Groups[1].Value, "function/component"));
                else if (code.Contains("Map", StringComparison.Ordinal) || code.Contains("Route", StringComparison.Ordinal) || code.Contains("router.", StringComparison.Ordinal) || code.Contains("app.", StringComparison.Ordinal) || code.Contains("[Http", StringComparison.Ordinal))
                { var route = Route.Match(line); if (route.Success) symbols.Add(new(n + 1, route.Groups[1].Value, "route (lexical; prefix may be separate)")); }
            }
            catch (RegexMatchTimeoutException) { }
        }
        return symbols;
    }
}
