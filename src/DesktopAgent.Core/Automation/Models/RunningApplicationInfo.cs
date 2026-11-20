namespace DesktopAgent.Core.Automation.Models;

public sealed record RunningApplicationInfo(
    string ApplicationName,
    string ProcessName,
    int ProcessId,
    string? ExecutablePath,
    nint MainWindowHandle,
    string WindowTitle,
    bool IsRunning);
