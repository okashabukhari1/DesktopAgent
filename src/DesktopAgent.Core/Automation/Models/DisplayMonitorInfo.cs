namespace DesktopAgent.Core.Automation.Models;

public sealed record DisplayMonitorInfo(
    int Number,
    string DeviceName,
    WindowRectangle Bounds,
    WindowRectangle WorkingArea,
    bool IsPrimary);
