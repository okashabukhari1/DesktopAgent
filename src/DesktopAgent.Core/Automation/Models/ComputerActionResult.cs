using DesktopAgent.Core.Automation.Enums;

namespace DesktopAgent.Core.Automation.Models;

public sealed record ComputerActionResult(
    bool Success,
    ActionStatus Status,
    string Message,
    string? Target,
    DateTimeOffset Timestamp,
    string? ErrorCode = null,
    IReadOnlyDictionary<string, string>? AdditionalData = null)
{
    public static ComputerActionResult Ok(string message, string? target = null,
        ActionStatus status = ActionStatus.Success, IReadOnlyDictionary<string, string>? data = null) =>
        new(true, status, message, target, DateTimeOffset.Now, null, data);

    public static ComputerActionResult Fail(ActionStatus status, string message, string? target = null,
        string? errorCode = null) =>
        new(false, status, message, target, DateTimeOffset.Now, errorCode);
}
