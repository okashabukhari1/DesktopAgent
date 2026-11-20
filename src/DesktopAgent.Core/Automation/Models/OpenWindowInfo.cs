using DesktopAgent.Core.Automation.Enums;

namespace DesktopAgent.Core.Automation.Models;

public sealed record OpenWindowInfo(
    nint Handle,
    string Title,
    int ProcessId,
    string ProcessName,
    WindowRectangle Bounds,
    string MonitorDeviceName,
    ExternalWindowState State,
    bool IsVisible)
{
    public long? ProcessStartTicks { get; init; }
    public int? HostedProcessId { get; init; }
    public string? HostedProcessName { get; init; }
}
