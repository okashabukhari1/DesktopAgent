namespace DesktopAgent.Core.Projects;

public sealed class ProjectProfile
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = "";
    public string Path { get; set; } = "";
    public List<string> Technologies { get; set; } = [];
    public List<string> Frameworks { get; set; } = [];
    public List<string> Languages { get; set; } = [];
    public List<string> DetectedFiles { get; set; } = [];
    public string? PreferredEditorId { get; set; }
    public string? PreferredTerminalId { get; set; }
    public Guid? AssociatedWorkspaceId { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset LastDetectedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? LastOpenedAt { get; set; }
    public int OpenCount { get; set; }
    public bool IsFavorite { get; set; }
    public bool IsAvailable { get; set; }
    public bool IsGitRepository { get; set; }
    public string? GitBranch { get; set; }
    public string? RepositoryRoot { get; set; }
    public string TechnologySummary => string.Join(" · ", Technologies);
    public ProjectProfile Copy()
    {
        var copy = (ProjectProfile)MemberwiseClone();
        copy.Technologies = [.. Technologies]; copy.Frameworks = [.. Frameworks];
        copy.Languages = [.. Languages]; copy.DetectedFiles = [.. DetectedFiles];
        return copy;
    }
}

public sealed class ProjectLocation
{
    public string Path { get; set; } = "";
    public bool IncludeSubfolders { get; set; } = true;
    public int MaxDepth { get; set; } = 4;
    public ProjectLocation Copy() => (ProjectLocation)MemberwiseClone();
}

public sealed class ProjectSettings
{
    public List<ProjectLocation> Locations { get; set; } = [];
    public string? DotNetEditorId { get; set; }
    public string? WebEditorId { get; set; }
    public string? AndroidEditorId { get; set; }
    public string? TerminalId { get; set; }
    public ProjectSettings Copy() => new() { Locations = Locations.Select(x => x.Copy()).ToList(),
        DotNetEditorId = DotNetEditorId, WebEditorId = WebEditorId,
        AndroidEditorId = AndroidEditorId, TerminalId = TerminalId };
}

public sealed record ProjectSearchMatch(ProjectProfile Project, int Score);
public sealed record ProjectResolution(ProjectProfile? Project, IReadOnlyList<ProjectProfile> Choices);
public sealed record ProjectScanResult(IReadOnlyList<ProjectProfile> Projects, int DirectoriesVisited,
    IReadOnlyList<string> Warnings, bool LimitReached);
public sealed record ProjectSession(Guid ProjectId, string Name, string Path, Guid? AssociatedWorkspaceId, DateTimeOffset OpenedAt);
public sealed class CurrentProjectContext
{
    public event EventHandler? Changed;
    public ProjectSession? Current { get; private set; }
    public void Set(ProjectProfile project, Guid? workspaceId = null)
    {
        Current = new(project.Id, project.Name, project.Path, workspaceId ?? project.AssociatedWorkspaceId, DateTimeOffset.UtcNow);
        Changed?.Invoke(this, EventArgs.Empty);
    }
    public void Clear() { Current = null; Changed?.Invoke(this, EventArgs.Empty); }
}

public interface IProjectDiscoveryService
{
    Task<ProjectScanResult> ScanAsync(IReadOnlyList<ProjectLocation> locations, CancellationToken cancellationToken = default);
    Task<ProjectProfile> DetectAsync(string path, CancellationToken cancellationToken = default);
}

public interface IProjectCatalog
{
    event EventHandler? Changed;
    event EventHandler<string>? ScanProgress;
    IReadOnlyList<ProjectProfile> Projects { get; }
    bool IsScanning { get; }
    string? LoadWarning { get; }
    Task InitializeAsync(CancellationToken cancellationToken = default);
    ProjectProfile? Find(Guid id);
    IReadOnlyList<ProjectSearchMatch> Search(string query, string? technology = null, int limit = 20);
    ProjectResolution Resolve(string nameOrId);
    Task<ProjectScanResult> RefreshAsync(CancellationToken cancellationToken = default);
    Task<ProjectProfile> AddAsync(string path, Guid? replaceId = null, CancellationToken cancellationToken = default);
    Task UpdateAsync(ProjectProfile project, CancellationToken cancellationToken = default);
    Task RemoveAsync(Guid id, CancellationToken cancellationToken = default);
    Task MarkOpenedAsync(Guid id, CancellationToken cancellationToken = default);
}

public interface IProjectEditorLauncher
{
    bool CanOpen(string applicationId);
    Task<DesktopAgent.Core.Ai.ToolResult> OpenAsync(string applicationId, ProjectProfile project, CancellationToken cancellationToken = default);
}

public interface IProjectOpenService
{
    Task<DesktopAgent.Core.Ai.ToolResult> OpenAsync(string project, string? editorId = null, bool useWorkspace = true, CancellationToken cancellationToken = default);
}
