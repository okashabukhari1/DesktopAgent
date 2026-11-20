using DesktopAgent.Core.Automation.Enums;
using DesktopAgent.Core.Workspaces.Enums;

namespace DesktopAgent.Core.Workspaces.Models;

public sealed class WorkspaceStoreDocument
{
    public const int CurrentVersion = 1;
    public int Version { get; set; } = CurrentVersion;
    public List<WorkspaceDefinition> Workspaces { get; set; } = [];
}

public sealed record WorkspaceValidationIssue(string Field, string Message);

public sealed record WorkspaceValidationResult(IReadOnlyList<WorkspaceValidationIssue> Issues)
{
    public bool IsValid => Issues.Count == 0;
    public static WorkspaceValidationResult Valid { get; } = new([]);
}

public sealed record WorkspaceStepResult(
    WorkspaceStepKind Kind,
    string Target,
    ActionStatus Status,
    string Message,
    DateTimeOffset Timestamp)
{
    public bool Success => Status is ActionStatus.Success or ActionStatus.AlreadyRunning;
}

public sealed record WorkspaceProgress(
    Guid WorkspaceId,
    string WorkspaceName,
    WorkspaceLaunchStatus Status,
    int CompletedSteps,
    int TotalSteps,
    string Message,
    WorkspaceStepResult? LatestStep = null);

public sealed class WorkspaceSession
{
    public Guid SessionId { get; init; } = Guid.NewGuid();
    public Guid WorkspaceId { get; init; }
    public string WorkspaceName { get; init; } = string.Empty;
    public DateTimeOffset StartedAt { get; init; } = DateTimeOffset.Now;
    public DateTimeOffset? CompletedAt { get; set; }
    public WorkspaceLaunchStatus Status { get; set; } = WorkspaceLaunchStatus.Starting;
    public List<string> LaunchedApplications { get; } = [];
    public List<string> AlreadyRunningApplications { get; } = [];
    public List<WorkspaceStepResult> Results { get; } = [];
}

public sealed record WorkspaceLaunchResult(WorkspaceSession Session, string Message)
{
    public bool Success => Session.Status is WorkspaceLaunchStatus.Ready or WorkspaceLaunchStatus.Partial;
}
