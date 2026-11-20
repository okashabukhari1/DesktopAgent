using DesktopAgent.Core.Automation.Models;

namespace DesktopAgent.Core.Automation.Interfaces;

public interface IComputerCommandService
{
    Task<ComputerActionResult> ExecuteAsync(string input, CancellationToken cancellationToken = default, string source = "Text");
}
