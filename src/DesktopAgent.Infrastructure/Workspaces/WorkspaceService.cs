using DesktopAgent.Core.Workspaces.Interfaces;
using DesktopAgent.Core.Workspaces.Models;
using DesktopAgent.Core.Workspaces.Enums;
using DesktopAgent.Core.Automation.Enums;
using DesktopAgent.Core.Automation.Interfaces;
using DesktopAgent.Core.Automation.Models;
using Microsoft.Extensions.Logging;

namespace DesktopAgent.Infrastructure.Workspaces;

public sealed class WorkspaceService : IWorkspaceService
{
    private readonly IWorkspaceRepository _repository;
    private readonly IWorkspaceValidator _validator;
    private readonly IWorkspaceLaunchEngine _launchEngine;
    private readonly ILogger<WorkspaceService> _logger;
    private readonly IActionHistoryService? _history;
    private readonly DesktopAgent.Core.Projects.IProjectCatalog? _projects;
    private readonly DesktopAgent.Core.Projects.CurrentProjectContext? _projectContext;
    private readonly List<WorkspaceDefinition> _workspaces = [];
    private readonly System.Collections.Concurrent.ConcurrentDictionary<Guid, WorkspaceSession> _sessions = new();
    private readonly Dictionary<Guid, CancellationTokenSource> _launches = [];
    private readonly object _launchSync = new();
    private readonly SemaphoreSlim _gate = new(1, 1);
    private bool _initialized;

    public WorkspaceService(IWorkspaceRepository repository, IWorkspaceValidator validator, IWorkspaceLaunchEngine launchEngine, ILogger<WorkspaceService> logger,
        IActionHistoryService? history = null, DesktopAgent.Core.Projects.IProjectCatalog? projects = null,
        DesktopAgent.Core.Projects.CurrentProjectContext? projectContext = null)
    {
        _repository = repository;
        _validator = validator;
        _launchEngine = launchEngine;
        _logger = logger;
        _history = history;
        _projects = projects; _projectContext = projectContext;
        _launchEngine.ProgressChanged += (_, progress) => ProgressChanged?.Invoke(this, progress);
    }

    public event EventHandler? WorkspacesChanged;
    public event EventHandler<WorkspaceProgress>? ProgressChanged;
    public IReadOnlyList<WorkspaceDefinition> Workspaces => _workspaces.Select(item => item.DeepCopy()).ToList();
    public IReadOnlyDictionary<Guid, WorkspaceSession> Sessions => new Dictionary<Guid, WorkspaceSession>(_sessions);

    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            if (_initialized) return;
            _workspaces.AddRange((await _repository.LoadAsync(cancellationToken)).Select(item => item.DeepCopy()));
            _initialized = true;
        }
        finally { _gate.Release(); }
    }

    public WorkspaceDefinition? Find(string nameOrId)
    {
        var found = Guid.TryParse(nameOrId, out var id)
            ? _workspaces.FirstOrDefault(item => item.Id == id)
            : _workspaces.FirstOrDefault(item => item.Name.Equals(nameOrId.Trim(), StringComparison.OrdinalIgnoreCase));
        return found?.DeepCopy();
    }

    public async Task<WorkspaceValidationResult> SaveAsync(WorkspaceDefinition workspace, CancellationToken cancellationToken = default)
    {
        await InitializeAsync(cancellationToken);
        var candidate = workspace.DeepCopy();
        candidate.Name = candidate.Name?.Trim() ?? string.Empty;
        await _gate.WaitAsync(cancellationToken);
        try
        {
            var validation = _validator.Validate(candidate, _workspaces);
            if (!validation.IsValid) return validation;
            var index = _workspaces.FindIndex(item => item.Id == candidate.Id);
            var now = DateTimeOffset.Now;
            candidate.UpdatedAt = now;
            var updated = _workspaces.Select(item => item.DeepCopy()).ToList();
            if (index < 0)
            {
                if (candidate.Id == Guid.Empty) candidate.Id = Guid.NewGuid();
                candidate.CreatedAt = now;
                updated.Add(candidate);
            }
            else
            {
                candidate.CreatedAt = _workspaces[index].CreatedAt;
                updated[index] = candidate;
            }
            await _repository.SaveAsync(updated, cancellationToken);
            _workspaces.Clear();
            _workspaces.AddRange(updated);
        }
        finally { _gate.Release(); }
        WorkspacesChanged?.Invoke(this, EventArgs.Empty);
        return WorkspaceValidationResult.Valid;
    }

    public async Task<WorkspaceDefinition?> DuplicateAsync(Guid id, CancellationToken cancellationToken = default)
    {
        await InitializeAsync(cancellationToken);
        var source = _workspaces.FirstOrDefault(item => item.Id == id);
        if (source is null) return null;
        var copy = source.DeepCopy();
        copy.Id = Guid.NewGuid();
        copy.Name = UniqueName(source.Name + " copy");
        copy.CreatedAt = copy.UpdatedAt = DateTimeOffset.Now;
        ResetChildIds(copy);
        var result = await SaveAsync(copy, cancellationToken);
        return result.IsValid ? copy.DeepCopy() : null;
    }

    public async Task<bool> DeleteAsync(Guid id, CancellationToken cancellationToken = default)
    {
        await InitializeAsync(cancellationToken);
        await _gate.WaitAsync(cancellationToken);
        try
        {
            var updated = _workspaces.Where(item => item.Id != id).ToList();
            if (updated.Count == _workspaces.Count) return false;
            await _repository.SaveAsync(updated, cancellationToken);
            _workspaces.Clear();
            _workspaces.AddRange(updated);
            Cancel(id);
        }
        finally { _gate.Release(); }
        WorkspacesChanged?.Invoke(this, EventArgs.Empty);
        return true;
    }

    public async Task<WorkspaceLaunchResult?> LaunchAsync(Guid id, CancellationToken cancellationToken = default)
    {
        await InitializeAsync(cancellationToken);
        var workspace = _workspaces.FirstOrDefault(item => item.Id == id)?.DeepCopy();
        if (workspace is null) return null;
        var validation = _validator.ValidateForLaunch(workspace);
        if (!validation.IsValid)
        {
            var invalid = new WorkspaceSession { WorkspaceId = id, WorkspaceName = workspace.Name,
                Status = WorkspaceLaunchStatus.InvalidConfiguration, CompletedAt = DateTimeOffset.Now };
            _sessions[id] = invalid;
            var message = "Workspace configuration needs repair: " + string.Join(" ", validation.Issues.Select(issue => issue.Message));
            _history?.Add(new ActionHistoryEntry(DateTimeOffset.Now, ComputerCommandType.LaunchWorkspace,
                workspace.Name, ActionStatus.InvalidInput, message, "Workspace UI"));
            return new(invalid, message);
        }
        var source = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        lock (_launchSync)
        {
            if (_launches.TryGetValue(id, out var previous)) previous.Cancel();
            _launches[id] = source;
        }
        try
        {
            var result = await _launchEngine.LaunchAsync(workspace, source.Token);
            _sessions[id] = result.Session;
            if (result.Success && workspace.ProjectId is { } projectId && _projects?.Find(projectId) is { } project && Directory.Exists(project.Path))
                _projectContext?.Set(project, workspace.Id);
            return result;
        }
        finally
        {
            lock (_launchSync)
            {
                if (_launches.TryGetValue(id, out var current) && ReferenceEquals(current, source)) _launches.Remove(id);
            }
            source.Dispose();
        }
    }

    public bool Cancel(Guid id)
    {
        lock (_launchSync)
        {
            if (!_launches.TryGetValue(id, out var source)) return false;
            source.Cancel();
        }
        _logger.LogInformation("WorkspaceLaunchCancellationRequested WorkspaceId={WorkspaceId}", id);
        return true;
    }

    private string UniqueName(string name)
    {
        if (_workspaces.All(item => !item.Name.Equals(name, StringComparison.OrdinalIgnoreCase))) return name;
        for (var number = 2; ; number++)
        {
            var candidate = $"{name} {number}";
            if (_workspaces.All(item => !item.Name.Equals(candidate, StringComparison.OrdinalIgnoreCase))) return candidate;
        }
    }

    private static void ResetChildIds(WorkspaceDefinition workspace)
    {
        foreach (var item in workspace.Applications) item.Id = Guid.NewGuid();
        foreach (var item in workspace.Folders.Concat(workspace.Files).Concat(workspace.Urls)) item.Id = Guid.NewGuid();
        foreach (var item in workspace.PreLaunchActions.Concat(workspace.PostLaunchActions)) item.Id = Guid.NewGuid();
    }
}
