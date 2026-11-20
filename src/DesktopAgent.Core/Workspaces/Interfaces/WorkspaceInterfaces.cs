using DesktopAgent.Core.Workspaces.Models;

namespace DesktopAgent.Core.Workspaces.Interfaces;

public interface IWorkspaceRepository
{
    Task<IReadOnlyList<WorkspaceDefinition>> LoadAsync(CancellationToken cancellationToken = default);
    Task SaveAsync(IReadOnlyList<WorkspaceDefinition> workspaces, CancellationToken cancellationToken = default);
}

public interface IWorkspaceValidator
{
    WorkspaceValidationResult Validate(WorkspaceDefinition workspace, IReadOnlyList<WorkspaceDefinition>? existing = null);
    WorkspaceValidationResult ValidateForLaunch(WorkspaceDefinition workspace);
}

public interface IWorkspaceLaunchEngine
{
    event EventHandler<WorkspaceProgress>? ProgressChanged;
    Task<WorkspaceLaunchResult> LaunchAsync(WorkspaceDefinition workspace, CancellationToken cancellationToken = default);
}

public interface IWorkspaceService
{
    event EventHandler? WorkspacesChanged;
    event EventHandler<WorkspaceProgress>? ProgressChanged;
    IReadOnlyList<WorkspaceDefinition> Workspaces { get; }
    IReadOnlyDictionary<Guid, WorkspaceSession> Sessions { get; }
    Task InitializeAsync(CancellationToken cancellationToken = default);
    WorkspaceDefinition? Find(string nameOrId);
    Task<WorkspaceValidationResult> SaveAsync(WorkspaceDefinition workspace, CancellationToken cancellationToken = default);
    Task<WorkspaceDefinition?> DuplicateAsync(Guid id, CancellationToken cancellationToken = default);
    Task<bool> DeleteAsync(Guid id, CancellationToken cancellationToken = default);
    Task<WorkspaceLaunchResult?> LaunchAsync(Guid id, CancellationToken cancellationToken = default);
    bool Cancel(Guid id);
}
