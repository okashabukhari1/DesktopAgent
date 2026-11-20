using System.Text.Json;
using System.Text.Json.Serialization;
using DesktopAgent.Core.Interfaces;
using DesktopAgent.Core.Projects;
using DesktopAgent.Core.Automation.Interfaces;
using DesktopAgent.Core.Automation.Models;
using DesktopAgent.Core.Automation.Enums;

namespace DesktopAgent.Infrastructure.Projects;

public sealed class ProjectCatalog(IProjectDiscoveryService discovery, ISettingsService settings,
    IActionHistoryService? history = null, string? baseDirectory = null) : IProjectCatalog
{
    private sealed class Document
    {
        public int Version { get; set; } = 1;
        public List<ProjectProfile> Projects { get; set; } = [];
    }
    private readonly string _file = Path.Combine(baseDirectory ?? Path.GetDirectoryName(settings.SettingsPath)!, "projects.json");
    private readonly SemaphoreSlim _gate = new(1, 1);
    private ProjectProfile[] _projects = [];
    private bool _initialized, _readOnly;
    private int _scan;
    private static readonly JsonSerializerOptions Json = new() { WriteIndented = true, MaxDepth = 32, IgnoreReadOnlyProperties = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull };
    public event EventHandler? Changed;
    public event EventHandler<string>? ScanProgress;
    public string? LoadWarning { get; private set; }
    public bool IsScanning => Volatile.Read(ref _scan) != 0;
    public IReadOnlyList<ProjectProfile> Projects => Volatile.Read(ref _projects).Select(p => p.Copy()).ToArray();
    public ProjectProfile? Find(Guid id) => Projects.FirstOrDefault(p => p.Id == id);
    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            if (_initialized) return;
            if (File.Exists(_file))
            {
                try
                {
                    if (new FileInfo(_file).Length > 8 * 1024 * 1024) throw new JsonException();
                    await using var stream = File.OpenRead(_file);
                    var doc = await JsonSerializer.DeserializeAsync<Document>(stream, Json, cancellationToken) ?? throw new JsonException();
                    if (doc.Version != 1) { _readOnly = true; LoadWarning = "Unsupported projects.json version; catalog is read-only until restored or migrated."; }
                    else if (doc.Projects is null) throw new JsonException();
                    else
                    {
                        var cached = doc.Projects.Where(Valid).DistinctBy(p => p.Id).DistinctBy(p => p.Path, StringComparer.OrdinalIgnoreCase).Take(2000).Select(p => p.Copy()).ToArray();
                        // Availability checks are bounded cached-root probes on a worker, never discovery.
                        _projects = await Task.Run(() => { foreach (var p in cached) { cancellationToken.ThrowIfCancellationRequested(); p.IsAvailable = Directory.Exists(p.Path); } return cached; }, cancellationToken);
                    }
                }
                catch (Exception e) when (e is IOException or JsonException or UnauthorizedAccessException)
                { _readOnly = true; LoadWarning = "Project cache could not be read. Original file preserved; restore it before modifying the catalog."; }
            }
            _initialized = true;
        }
        finally { _gate.Release(); }
        Changed?.Invoke(this, EventArgs.Empty);
    }
    private static bool Valid(ProjectProfile p) => p is not null && p.Id != Guid.Empty && !string.IsNullOrWhiteSpace(p.Name) && p.Name.Length <= 200
        && !p.Name.Any(char.IsControl) && !string.IsNullOrWhiteSpace(p.Path) && Path.IsPathFullyQualified(p.Path)
        && p.Path.Length <= 1024 && !p.Path.Any(char.IsControl) && p.Technologies is not null && p.Frameworks is not null
        && p.Languages is not null && p.DetectedFiles is not null
        && SafeList(p.Technologies, 32, 100) && SafeList(p.Frameworks, 32, 100) && SafeList(p.Languages, 32, 100) && SafeList(p.DetectedFiles, 32, 1024);
    private static bool SafeList(List<string> items, int count, int length) => items.Count <= count && items.All(x => x is not null && x.Length <= length && !x.Any(char.IsControl));

    public IReadOnlyList<ProjectSearchMatch> Search(string query, string? technology = null, int limit = 20)
    {
        query = (query ?? "").Trim();
        return Projects.Where(p => string.IsNullOrWhiteSpace(technology) || p.Technologies.Any(t => t.Contains(technology, StringComparison.OrdinalIgnoreCase)))
            .Select(p => new ProjectSearchMatch(p, Score(p, query))).Where(p => p.Score > 0)
            .OrderByDescending(p => p.Score).ThenByDescending(p => p.Project.IsFavorite).ThenByDescending(p => p.Project.LastOpenedAt)
            .ThenBy(p => p.Project.Path, StringComparer.OrdinalIgnoreCase).Take(Math.Clamp(limit, 1, 100)).ToArray();
    }
    private static int Score(ProjectProfile p, string query)
    {
        if (query.Length == 0) return 1;
        if (p.Id.ToString().Equals(query, StringComparison.OrdinalIgnoreCase) || p.Name.Equals(query, StringComparison.OrdinalIgnoreCase)) return 1000;
        if (p.Name.StartsWith(query, StringComparison.OrdinalIgnoreCase)) return 700;
        if (query.Split(' ', StringSplitOptions.RemoveEmptyEntries).All(t => p.Name.Contains(t, StringComparison.OrdinalIgnoreCase))) return 600;
        if (p.Path.Contains(query, StringComparison.OrdinalIgnoreCase)) return 500;
        if (p.Technologies.Any(t => t.Contains(query, StringComparison.OrdinalIgnoreCase))) return 400;
        var a = p.Name.ToLowerInvariant(); var b = query.ToLowerInvariant();
        if (b.Length < 4 || b.Length > 200 || Math.Abs(a.Length - b.Length) > 2) return 0;
        var previous = Enumerable.Range(0, b.Length + 1).ToArray();
        for (var i = 1; i <= a.Length; i++)
        {
            var next = new int[b.Length + 1]; next[0] = i;
            for (var j = 1; j <= b.Length; j++) next[j] = Math.Min(Math.Min(next[j - 1] + 1, previous[j] + 1), previous[j - 1] + (a[i - 1] == b[j - 1] ? 0 : 1));
            previous = next;
        }
        return previous[b.Length] <= 2 ? 200 - previous[b.Length] : 0;
    }
    public ProjectResolution Resolve(string nameOrId)
    {
        if (Guid.TryParse(nameOrId, out var id)) return new(Find(id), []);
        var matches = Search(nameOrId, limit: 8);
        if (matches.Count == 0) return new(null, []);
        var exact = matches.Where(m => m.Score == 1000).ToArray();
        if (exact.Length == 1) return new(exact[0].Project, []);
        // Favorites and recency never silently disambiguate an opening request.
        if (matches.Count == 1) return new(matches[0].Project, []);
        return new(null, matches.Select(m => m.Project).ToArray());
    }

    public async Task<ProjectScanResult> RefreshAsync(CancellationToken cancellationToken = default)
    {
        await InitializeAsync(cancellationToken);
        if (Interlocked.CompareExchange(ref _scan, 1, 0) != 0) throw new InvalidOperationException("A project scan is already running.");
        try
        {
            ScanProgress?.Invoke(this, "Scanning configured project locations…");
            var result = await discovery.ScanAsync(settings.Current.Projects.Locations.Select(l => l.Copy()).ToArray(), cancellationToken);
            var availability = await Task.Run(() => Projects.ToDictionary(p => p.Id, p => Directory.Exists(p.Path)), cancellationToken);
            await MutateAsync(list =>
            {
                foreach (var detected in result.Projects)
                {
                    var previous = list.FirstOrDefault(p => p.Path.Equals(detected.Path, StringComparison.OrdinalIgnoreCase));
                    if (previous is not null) { PreservePreferences(previous, detected); list.Remove(previous); }
                    list.Add(detected);
                }
                foreach (var project in list) if (availability.TryGetValue(project.Id, out var available)) project.IsAvailable = available;
            }, cancellationToken);
            Activity("Project scan", result.Projects.Count + " projects detected." + (result.LimitReached ? " Scan limit reached; narrow locations." : "") + (result.Warnings.Count > 0 ? " Some folders were unavailable." : ""), result.Warnings.Count > 0 || result.LimitReached);
            return result;
        }
        catch (OperationCanceledException) { Activity("Project scan", "Scan cancelled; previous catalog retained.", true); throw; }
        finally { Interlocked.Exchange(ref _scan, 0); ScanProgress?.Invoke(this, "Scan finished."); }
    }
    public async Task<ProjectProfile> AddAsync(string path, Guid? replaceId = null, CancellationToken cancellationToken = default)
    {
        await InitializeAsync(cancellationToken);
        var detected = await discovery.DetectAsync(path, cancellationToken);
        if (!detected.IsAvailable) throw new ArgumentException("Project folder is unavailable.");
        await MutateAsync(list =>
        {
            var existing = list.FirstOrDefault(p => p.Path.Equals(detected.Path, StringComparison.OrdinalIgnoreCase));
            if (existing is not null && replaceId.HasValue && existing.Id != replaceId) throw new ArgumentException("That folder is already catalogued.");
            var old = replaceId.HasValue ? list.FirstOrDefault(p => p.Id == replaceId) : existing;
            if (old is not null) { PreservePreferences(old, detected); list.Remove(old); }
            list.Add(detected);
        }, cancellationToken);
        Activity(detected.Name, "Project metadata added or refreshed.");
        return detected.Copy();
    }
    private static void PreservePreferences(ProjectProfile old, ProjectProfile detected)
    {
        detected.Id = old.Id; detected.Name = old.Name; detected.CreatedAt = old.CreatedAt;
        detected.PreferredEditorId = old.PreferredEditorId; detected.PreferredTerminalId = old.PreferredTerminalId;
        detected.AssociatedWorkspaceId = old.AssociatedWorkspaceId; detected.LastOpenedAt = old.LastOpenedAt;
        detected.OpenCount = old.OpenCount; detected.IsFavorite = old.IsFavorite;
    }
    public async Task UpdateAsync(ProjectProfile project, CancellationToken cancellationToken = default)
    {
        await MutateAsync(list =>
        {
            if (!Valid(project)) throw new ArgumentException("Invalid project metadata.");
            var index = list.FindIndex(p => p.Id == project.Id);
            if (index < 0) throw new ArgumentException("Project not found.");
            // Preferences update cannot grant arbitrary paths; relocation uses the explicit picker.
        var copy = list[index]; copy.IsFavorite = project.IsFavorite;
            copy.PreferredEditorId = project.PreferredEditorId; copy.PreferredTerminalId = project.PreferredTerminalId;
            copy.AssociatedWorkspaceId = project.AssociatedWorkspaceId;
        }, cancellationToken);
        Activity(project.Name, "Project preferences or workspace association updated.");
    }
    public async Task RemoveAsync(Guid id, CancellationToken cancellationToken = default)
    {
        await MutateAsync(list => list.RemoveAll(p => p.Id == id), cancellationToken);
        Activity(id.ToString(), "Removed from catalog only. Project files were not deleted.");
    }
    public Task MarkOpenedAsync(Guid id, CancellationToken cancellationToken = default) => MutateAsync(list =>
    { var p = list.FirstOrDefault(x => x.Id == id); if (p is not null) { p.LastOpenedAt = DateTimeOffset.UtcNow; p.OpenCount++; } }, cancellationToken);
    private async Task MutateAsync(Action<List<ProjectProfile>> change, CancellationToken ct)
    {
        await InitializeAsync(ct); await _gate.WaitAsync(ct);
        try
        {
            if (_readOnly) throw new InvalidOperationException(LoadWarning);
            var next = Projects.Select(p => p.Copy()).ToList(); change(next);
            if (next.Count > 2000) throw new InvalidOperationException("Project catalog limit reached (2000). Remove unused entries.");
            Directory.CreateDirectory(Path.GetDirectoryName(_file)!);
            var temporary = _file + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                await using (var stream = File.Create(temporary)) await JsonSerializer.SerializeAsync(stream, new Document { Projects = next }, Json, ct);
                ct.ThrowIfCancellationRequested(); File.Move(temporary, _file, true);
                Volatile.Write(ref _projects, next.ToArray());
            }
            finally { if (File.Exists(temporary)) File.Delete(temporary); }
        }
        finally { _gate.Release(); }
        Changed?.Invoke(this, EventArgs.Empty);
    }
    private void Activity(string target, string message, bool partial = false) => history?.Add(new ActionHistoryEntry(DateTimeOffset.Now,
        ComputerCommandType.AiToolRequest, target, partial ? ActionStatus.Partial : ActionStatus.Success, message, "Projects"));
}
