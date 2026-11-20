using DesktopAgent.Core.Automation.Models;

namespace DesktopAgent.Core.Automation.Interfaces;

public interface IApplicationControlService
{
    IReadOnlyList<RunningApplicationInfo> GetRunningApplications();
    Task<ComputerActionResult> LaunchAsync(string target, CancellationToken cancellationToken = default);
    Task<ComputerActionResult> LaunchExplicitAsync(string executablePath, CancellationToken cancellationToken = default);
    ComputerActionResult Focus(string target);
    ComputerActionResult Close(string target);
}
