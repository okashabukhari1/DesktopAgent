using DesktopAgent.Core.Automation.Enums;

namespace DesktopAgent.Core.Automation.Models;

public sealed record ActionHistoryEntry(
    DateTimeOffset Timestamp,
    ComputerCommandType Action,
    string Target,
    ActionStatus Status,
    string Message,
    string Source = "Text");
