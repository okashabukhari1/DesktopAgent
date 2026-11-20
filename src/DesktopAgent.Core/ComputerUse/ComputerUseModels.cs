using DesktopAgent.Core.Permissions;
using DesktopAgent.Core.UiAwareness;

namespace DesktopAgent.Core.ComputerUse;

public enum ComputerUseMode { Observe, Assist, TrustedActions }
public enum ComputerTaskState { Idle, Planning, Observing, Acting, Verifying, WaitingForUser, Paused, Completed, Failed, Cancelled }
public enum ComputerActionType { Invoke, SetValue, Toggle, Select, Expand, Collapse, Focus, ScrollElement, ClickElement, TypeText, PressKey, PressShortcut, ScrollWindow }
public sealed class ComputerUseSettings
{
    public bool Enabled { get; set; }
    public bool OnboardingAcknowledged { get; set; }
    public ComputerUseMode Mode { get; set; } = ComputerUseMode.Assist;
    public bool PauseOnUserInput { get; set; } = true;
    public int MaximumActions { get; set; } = 16;
    public int MaximumFailures { get; set; } = 2;
    public int MaximumTaskSeconds { get; set; } = 120;
    public int MaximumPlanningRounds { get; set; } = 8;
    public List<string> TrustedApplications { get; set; } = [];
    public string? PythonExecutable { get; set; }
    public ComputerUseSettings Copy() => new() { Enabled = Enabled, OnboardingAcknowledged = OnboardingAcknowledged,
        Mode = Mode, PauseOnUserInput = PauseOnUserInput, MaximumActions = MaximumActions, MaximumFailures = MaximumFailures,
        MaximumTaskSeconds = MaximumTaskSeconds, MaximumPlanningRounds = MaximumPlanningRounds, TrustedApplications = [.. TrustedApplications], PythonExecutable = PythonExecutable };
    public void Sanitize()
    {
        if (!Enum.IsDefined(Mode)) Mode = ComputerUseMode.Observe;
        Enabled &= OnboardingAcknowledged;
        MaximumActions = Math.Clamp(MaximumActions, 1, 16);
        MaximumFailures = Math.Clamp(MaximumFailures, 1, 2);
        MaximumTaskSeconds = Math.Clamp(MaximumTaskSeconds, 10, 180);
        MaximumPlanningRounds = Math.Clamp(MaximumPlanningRounds, 1, 12);
        TrustedApplications = (TrustedApplications ?? []).Where(x => !string.IsNullOrWhiteSpace(x)).Take(16).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
    }
}
public sealed record ComputerAction(Guid Id, ComputerActionType Type, string WindowReference, Guid SnapshotVersion,
    string ElementReference, string? Text = null, string? Key = null, int Amount = 0,
    DateTimeOffset CreatedAt = default);
// Prepared identities are issued locally, never accepted as tool arguments. No native UIA objects leave the MTA lane.
public sealed record PreparedComputerTarget(WindowContext Window, UiElement Element, string Identity, int[] RuntimeId);
public sealed record ComputerInteractionResult(Guid ActionId, bool Success, string Status, string Message,
    string? Target = null, string? BeforeState = null, string? AfterState = null, bool Verified = false,
    long DurationMilliseconds = 0, string? ErrorCode = null, bool InputMayHaveOccurred = false);
public interface IComputerInteractionBackend
{
    Task<PreparedComputerTarget> PrepareAsync(ComputerAction action, CancellationToken ct);
    Task<ComputerInteractionResult> ExecuteAsync(ComputerAction action, PreparedComputerTarget target, Action guard, CancellationToken ct);
}
public interface IComputerActionExecutor
{
    Task<ComputerInteractionResult> ExecuteAsync(ComputerAction action, CancellationToken ct = default);
}
public static class ComputerUseCapabilities
{
    public const string Actions = "ComputerUse.Actions";
    public static readonly AgentCapability Capability = new(Actions, "Controlled computer actions",
        "Interact with verified application controls during an explicitly requested task. Consequential actions require separate approval.",
        "Computer Use", PermissionRisk.Medium, PermissionPolicy.Allow, CapabilityAvailability.Available,
        "Requires explicit onboarding, a registered emergency STOP and a visible assistant.");
}
