using DesktopAgent.Core.Automation.Enums;
using DesktopAgent.Core.Automation.Models;

namespace DesktopAgent.Core.Automation.Interfaces;

public interface IExternalWindowService
{
    IReadOnlyList<OpenWindowInfo> GetOpenWindows();
    IReadOnlyList<DisplayMonitorInfo> GetMonitors();
    OpenWindowInfo? FindWindow(string target);
    ComputerActionResult Focus(string target);
    ComputerActionResult FocusWindow(OpenWindowInfo window) => ComputerActionResult.Fail(ActionStatus.Unsupported, "Pinned window focus is unavailable.", window.Title);
    ComputerActionResult Close(string target);
    ComputerActionResult Minimize(string target);
    ComputerActionResult Maximize(string target);
    ComputerActionResult Restore(string target);
    ComputerActionResult Move(string target, int x, int y);
    ComputerActionResult Resize(string target, int width, int height);
    ComputerActionResult ApplyLayout(string target, WindowLayout layout);
    ComputerActionResult MoveToMonitor(string target, int monitorNumber);
}
