using DesktopAgent.Core.Permissions;

namespace DesktopAgent.Core.Developer;

public enum DeveloperSessionState { Starting, Working, WaitingForPermission, WaitingForUser, Completed, Failed, Cancelled, Disconnected }
public enum DeveloperEventType { SessionStarted, Working, ToolRequested, FileRead, FileModified, FileCreated, CommandRequested, CommandCompleted, CommandFailed, PermissionRequested, PermissionApproved, PermissionDenied, WaitingForUser, TurnCompleted, SessionCompleted, SessionFailed, SessionCancelled }
public enum DeveloperOperationStatus { Unknown, Requested, Running, Succeeded, Failed }
public sealed class DeveloperSettings
{
    public bool Enabled { get; set; }
    public int RetentionDays { get; set; } = 30;
    public bool Notifications { get; set; }
    public DeveloperSettings Copy() => (DeveloperSettings)MemberwiseClone();
    public void Sanitize() { if (RetentionDays is not (7 or 30 or 90)) RetentionDays = 30; }
}
public sealed record IntegrationStatus(string ToolId, string DisplayName, bool Detected, bool Configured, bool Connected, string State, string Message);
public sealed record DeveloperEvent(Guid Id, string SourceTool, DeveloperEventType EventType, string SessionId,
    DateTimeOffset Timestamp, Guid? ProjectId, string ProjectPath, string? ToolName = null, string? ToolUseId = null,
    string? FilePath = null, string? CommandSummary = null, string? Category = null, int? ExitCode = null,
    PermissionRisk Risk = PermissionRisk.Medium);
public sealed record DeveloperCommand(string ToolUseId, string Summary, string Category, DeveloperOperationStatus Status, int? ExitCode);
public sealed record DeveloperSession(string Id, string SourceTool, string ExternalId, string ProjectPath, Guid? ProjectId,
    DateTimeOffset StartedAt, DateTimeOffset LastActivity, DateTimeOffset? CompletedAt, DeveloperSessionState State,
    IReadOnlyList<DeveloperEvent> Events, IReadOnlyList<string> FilesRead, IReadOnlyList<string> FilesModified,
    IReadOnlyList<string> FilesCreated, IReadOnlyList<DeveloperCommand> Commands, string Summary);
public sealed record DeveloperPermission(Guid Id, string SessionId, string ToolName, string Summary, PermissionRisk Risk,
    IReadOnlyList<string> Capabilities, string Reason, bool CanAllow, DateTimeOffset ExpiresAt);
public sealed record DeveloperRisk(PermissionRisk Risk, string Category, IReadOnlyList<string> Capabilities, bool Blocked, string Reason);
public sealed record DeveloperBridgeReply(bool Accepted, string? Decision = null);
public interface IDeveloperEventBridge
{
    Task<DeveloperBridgeReply> ReceiveEventAsync(string source, ReadOnlyMemory<byte> payload, CancellationToken cancellationToken = default);
}
public interface IDeveloperToolAdapter
{
    string ToolId { get; }
    string DisplayName { get; }
    Task StartAsync(CancellationToken cancellationToken);
    Task StopAsync(CancellationToken cancellationToken);
    Task<IntegrationStatus> GetStatusAsync(CancellationToken cancellationToken);
}
public interface IDeveloperSessionManager
{
    event EventHandler? Changed;
    IReadOnlyList<DeveloperSession> Sessions { get; }
    IReadOnlyList<DeveloperPermission> PendingPermissions { get; }
    bool Decide(Guid requestId, bool allowOnce);
}
public static class DeveloperCapabilityIds
{
    public const string Observe = "Developer.Observe", ReadFile = "Developer.ReadFile", ModifyFile = "Developer.ModifyFile",
        RunBuild = "Developer.RunBuild", RunTests = "Developer.RunTests", RunCommand = "Developer.RunCommand",
        NetworkAccess = "Developer.NetworkAccess", InstallPackage = "Developer.InstallPackage";
    public static IReadOnlyList<AgentCapability> All { get; } = new[] {
        (Observe, "Observe developer sessions", PermissionRisk.Low, PermissionPolicy.Allow),
        (ReadFile, "Developer file reads", PermissionRisk.Medium, PermissionPolicy.Ask),
        (ModifyFile, "Developer file modifications", PermissionRisk.High, PermissionPolicy.Ask),
        (RunBuild, "Developer builds", PermissionRisk.High, PermissionPolicy.Ask),
        (RunTests, "Developer tests", PermissionRisk.High, PermissionPolicy.Ask),
        (RunCommand, "Developer commands", PermissionRisk.High, PermissionPolicy.Ask),
        (NetworkAccess, "Developer network access", PermissionRisk.High, PermissionPolicy.Ask),
        (InstallPackage, "Developer package installation", PermissionRisk.High, PermissionPolicy.Ask)
    }.Select(x => new AgentCapability(x.Item1, x.Item2,
        "Structured hooks only; never grants our assistant shell execution. External approvals require an explicit Allow Once.",
        "Developer supervision", x.Item3, x.Item4, CapabilityAvailability.Available, "Opt-in integration and registered project required.")).ToArray();
}
