using System.Text.Json;
using System.Xml;
using System.Xml.Linq;
using DesktopAgent.Core.Projects;

namespace DesktopAgent.Infrastructure.Projects;

/// <summary>Local metadata only. Never invokes build tools, package scripts, or Git.</summary>
public sealed class ProjectDiscoveryService : IProjectDiscoveryService
{
    public static readonly HashSet<string> Exclusions = new(StringComparer.OrdinalIgnoreCase)
    { "node_modules", "bin", "obj", ".git", ".vs", "dist", "build", "coverage", "packages", "vendor", ".next", ".nuxt", "target", ".venv", "venv", "__pycache__" };
    private static readonly EnumerationOptions Options = new() { RecurseSubdirectories = false,
        IgnoreInaccessible = false, AttributesToSkip = FileAttributes.ReparsePoint | FileAttributes.System };
    public const int MaxManifestBytes = 131072;
    public static string ValidatePath(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || path.Length > 1024 || path.Any(char.IsControl) || !Path.IsPathFullyQualified(path))
            throw new ArgumentException("Choose an absolute project folder.");
        var full = Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));
        if (Exclusions.Contains(Path.GetFileName(full))) throw new ArgumentException("Dependency/generated folders are excluded from project discovery.");
        if (string.Equals(full, Path.TrimEndingDirectorySeparator(Path.GetPathRoot(full)!), StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("Choose a project directory, not a drive root.");
        // Reject junction/symlink ancestors too: selected roots must not escape through links.
        for (var directory = new DirectoryInfo(full); directory is not null; directory = directory.Parent)
            if (directory.Exists && directory.Attributes.HasFlag(FileAttributes.ReparsePoint))
                throw new ArgumentException("Linked folders are not scanned. Choose the physical folder instead.");
        return full;
    }

    public Task<ProjectScanResult> ScanAsync(IReadOnlyList<ProjectLocation> locations, CancellationToken cancellationToken = default) => Task.Run(() =>
    {
        var found = new List<ProjectProfile>(); var warnings = new List<string>();
        var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var watch = System.Diagnostics.Stopwatch.StartNew(); bool limit = false;
        foreach (var location in locations.Take(32))
        {
            cancellationToken.ThrowIfCancellationRequested();
            string root;
            try { root = ValidatePath(location.Path); }
            catch (ArgumentException) { warnings.Add("A location is invalid or linked; select a physical non-root folder."); continue; }
            if (!Directory.Exists(root)) { warnings.Add(root + ": folder unavailable."); continue; }
            var queue = new Queue<(string Path, int Depth)>(); queue.Enqueue((root, 0));
            while (queue.Count > 0)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (visited.Count >= 10000 || found.Count >= 2000 || watch.Elapsed > TimeSpan.FromSeconds(60)) { limit = true; break; }
                var item = queue.Dequeue(); if (!visited.Add(item.Path)) continue;
                try
                {
                    var project = Detect(item.Path, cancellationToken);
                    if (project.Technologies.Count > 0) found.Add(project);
                    if (!location.IncludeSubfolders || item.Depth >= Math.Clamp(location.MaxDepth, 0, 8)) continue;
                    var children = Directory.EnumerateDirectories(item.Path, "*", Options).Take(513).ToArray();
                    if (children.Length > 512) limit = true;
                    foreach (var child in children.Take(512))
                        if (!Exclusions.Contains(Path.GetFileName(child)))
                        { if (queue.Count + visited.Count >= 10000) { limit = true; break; } queue.Enqueue((child, item.Depth + 1)); }
                }
                catch (Exception e) when (e is IOException or UnauthorizedAccessException or ArgumentException)
                { if (warnings.Count < 20) warnings.Add(item.Path + ": metadata unavailable."); }
            }
            if (limit) break;
        }
        // Consolidate nested manifests beneath detected roots. Sibling backend/frontend roots
        // are grouped only when BOTH common convention names are detected.
        foreach (var group in found.Where(p => new[] { "backend", "frontend", "client", "server" }.Contains(Path.GetFileName(p.Path).ToLowerInvariant()))
            .GroupBy(p => Path.GetDirectoryName(p.Path), StringComparer.OrdinalIgnoreCase).ToArray())
        {
            if (group.Count() < 2 || group.Key is null || found.Any(p => p.Path.Equals(group.Key, StringComparison.OrdinalIgnoreCase))) continue;
            var parent = Detect(group.Key, cancellationToken);
            found.Add(parent);
        }
        var merged = new List<ProjectProfile>();
        foreach (var project in found.OrderBy(p => p.Path.Length).ThenBy(p => p.Path, StringComparer.OrdinalIgnoreCase))
        {
            var parent = merged.FirstOrDefault(p => project.Path.StartsWith(p.Path + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase));
            if (parent is null) merged.Add(project);
            else
            {
                parent.Technologies = parent.Technologies.Concat(project.Technologies).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
                parent.Frameworks = parent.Frameworks.Concat(project.Frameworks).Distinct().Take(32).ToList();
                parent.Languages = parent.Languages.Concat(project.Languages).Distinct().ToList();
                parent.DetectedFiles = parent.DetectedFiles.Concat(project.DetectedFiles.Select(f => Path.GetRelativePath(parent.Path, Path.Combine(project.Path, f)))).Distinct(StringComparer.OrdinalIgnoreCase).Take(32).ToList();
            }
        }
        return new ProjectScanResult(merged, visited.Count, warnings, limit);
    }, cancellationToken);

    public async Task<ProjectProfile> DetectAsync(string path, CancellationToken cancellationToken = default)
    {
        var root = ValidatePath(path);
        var profile = await Task.Run(() => Detect(root, cancellationToken), cancellationToken);
        if (!profile.IsAvailable) return profile;
        // Manual addition/refresh treats the explicitly chosen folder as primary and
        // includes only two levels of local manifests, never dependency trees.
        var nested = await ScanAsync([new ProjectLocation { Path = root, MaxDepth = 2 }], cancellationToken);
        foreach (var project in nested.Projects)
        {
            profile.Technologies = profile.Technologies.Concat(project.Technologies).Distinct(StringComparer.OrdinalIgnoreCase).Take(32).ToList();
            profile.Frameworks = profile.Frameworks.Concat(project.Frameworks).Distinct().Take(32).ToList();
            profile.Languages = profile.Languages.Concat(project.Languages).Distinct().Take(32).ToList();
            profile.DetectedFiles = profile.DetectedFiles.Concat(project.DetectedFiles.Select(f => Path.GetRelativePath(root, Path.Combine(project.Path, f)))).Distinct(StringComparer.OrdinalIgnoreCase).Take(32).ToList();
        }
        return profile;
    }

    private static ProjectProfile Detect(string path, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        var project = new ProjectProfile { Path = path, Name = Path.GetFileName(path), IsAvailable = Directory.Exists(path) };
        if (!project.IsAvailable) return project;
        var files = Directory.EnumerateFiles(path, "*", Options).Take(512).ToArray();
        var names = files.Select(Path.GetFileName).ToHashSet(StringComparer.OrdinalIgnoreCase);
        void Add(string technology, string? language = null)
        { if (!project.Technologies.Contains(technology)) project.Technologies.Add(technology);
          if (language is not null && !project.Languages.Contains(language)) project.Languages.Add(language); }
        void Signature(string file) { if (project.DetectedFiles.Count < 32) project.DetectedFiles.Add(Path.GetFileName(file)); }
        foreach (var file in files)
        {
            ct.ThrowIfCancellationRequested(); var name = Path.GetFileName(file).ToLowerInvariant();
            var ext = Path.GetExtension(name);
            if (ext is ".sln" or ".slnx") { Add(".NET"); Signature(file); }
            if (ext is ".csproj" or ".fsproj")
            {
                Add(".NET", ext == ".csproj" ? "C#" : "F#"); Signature(file);
                var text = SmallText(file);
                if (text is not null) try
                {
                    using var reader = XmlReader.Create(new StringReader(text), new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null, MaxCharactersInDocument = MaxManifestBytes });
                    var xml = XDocument.Load(reader);
                    if (xml.Root?.Attribute("Sdk")?.Value.Contains("Web", StringComparison.OrdinalIgnoreCase) == true) Add("ASP.NET");
                    foreach (var value in xml.Descendants().Where(e => e.Name.LocalName is "TargetFramework" or "TargetFrameworks").Take(8))
                        project.Frameworks.AddRange(value.Value.Split(';').Where(v => v.Length <= 64 && !v.Any(char.IsControl)));
                } catch (XmlException) { /* Signature still useful; never execute MSBuild. */ }
            }
            if (name is "package.json" or "composer.json")
            {
                Add(name == "package.json" ? "Node.js" : "PHP", name == "package.json" ? "JavaScript" : "PHP"); Signature(file);
                var text = SmallText(file); if (text is null) continue;
                try
                {
                    using var json = JsonDocument.Parse(text, new JsonDocumentOptions { MaxDepth = 16 });
                    foreach (var field in new[] { "dependencies", "devDependencies", "require", "require-dev" })
                        if (json.RootElement.ValueKind == JsonValueKind.Object && json.RootElement.TryGetProperty(field, out var dependencies) && dependencies.ValueKind == JsonValueKind.Object)
                            foreach (var dependency in dependencies.EnumerateObject().Take(256))
                                switch (dependency.Name)
                                {
                                    case "react": Add("React"); break; case "next": Add("Next.js"); break;
                                    case "vite": Add("Vite"); break; case "express": Add("Express"); break;
                                    case "@nestjs/core": Add("NestJS"); break; case "typescript": Add("TypeScript", "TypeScript"); break;
                                    case "laravel/framework": Add("Laravel"); break;
                                }
                } catch (JsonException) { }
            }
            if (name == "artisan" && names.Contains("composer.json")) { Add("Laravel", "PHP"); Signature(file); }
            if (name is "pyproject.toml" or "requirements.txt" or "pipfile") { Add("Python", "Python"); Signature(file); }
            if (name is "pom.xml") { Add("Java", "Java"); Signature(file); }
            if (name is "settings.gradle" or "settings.gradle.kts" or "build.gradle" or "build.gradle.kts")
            {
                Add("Gradle", name.EndsWith(".kts") ? "Kotlin" : null); Signature(file);
                var text = SmallText(file);
                if (text?.Contains("com.android.", StringComparison.Ordinal) == true || names.Contains("AndroidManifest.xml")) Add("Android");
                if (text?.Contains("org.jetbrains.kotlin", StringComparison.Ordinal) == true) Add("Kotlin", "Kotlin");
                if (text?.Contains("java", StringComparison.OrdinalIgnoreCase) == true) Add("Java", "Java");
            }
            if (name == "androidmanifest.xml") { Add("Android"); Signature(file); }
        }
        if (project.Technologies.Count == 0 && names.Contains("index.html") && files.Any(f => Path.GetExtension(f).ToLowerInvariant() is ".css" or ".js"))
        { Add("Static Web", "HTML/CSS/JavaScript"); Signature(Path.Combine(path, "index.html")); }
        if (project.Technologies.Count == 0 && files.Any(f => Path.GetExtension(f).Equals(".php", StringComparison.OrdinalIgnoreCase))) Add("PHP", "PHP");
        var git = Path.Combine(path, ".git");
        if (Directory.Exists(git) && !File.GetAttributes(git).HasFlag(FileAttributes.ReparsePoint))
        {
            project.IsGitRepository = true; project.RepositoryRoot = path;
            var head = SmallText(Path.Combine(git, "HEAD"), 4096)?.Trim();
            if (head?.StartsWith("ref: refs/heads/", StringComparison.Ordinal) == true)
            { var branch = head[16..]; if (branch.Length <= 200 && !branch.Any(char.IsControl)) project.GitBranch = branch; }
            else if (head is { Length: >= 7 } && head.All(Uri.IsHexDigit)) project.GitBranch = "Detached " + head[..7];
        }
        else if (File.Exists(git)) { project.IsGitRepository = true; project.RepositoryRoot = path; } // Do not follow external worktree pointers.
        project.Frameworks = project.Frameworks.Distinct().Take(32).ToList();
        return project;
    }

    private static string? SmallText(string path, int limit = MaxManifestBytes)
    {
        try
        {
            var info = new FileInfo(path);
            if (!info.Exists || info.Length > limit || info.Attributes.HasFlag(FileAttributes.ReparsePoint)) return null;
            // Bounded stream protects against a file growing between stat and read.
            using var stream = File.OpenRead(path); var bytes = new byte[limit + 1];
            var count = 0; int read;
            while (count < bytes.Length && (read = stream.Read(bytes, count, bytes.Length - count)) > 0) count += read;
            return count > limit ? null : System.Text.Encoding.UTF8.GetString(bytes, 0, count).TrimStart('\uFEFF');
        } catch (Exception e) when (e is IOException or UnauthorizedAccessException) { return null; }
    }
}
