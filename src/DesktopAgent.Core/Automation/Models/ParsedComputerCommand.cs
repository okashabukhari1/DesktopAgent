using DesktopAgent.Core.Automation.Enums;

namespace DesktopAgent.Core.Automation.Models;

public sealed record ParsedComputerCommand(
    ComputerCommandType Type,
    string? Target = null,
    WindowLayout? Layout = null,
    int? X = null,
    int? Y = null,
    int? Width = null,
    int? Height = null,
    int? MonitorNumber = null,
    string? ErrorMessage = null);
