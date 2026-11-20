namespace DesktopAgent.Core.Automation.Models;

public sealed record ApplicationDefinition(
    string DisplayName,
    string Executable,
    string ProcessName,
    IReadOnlyList<string> Aliases);
