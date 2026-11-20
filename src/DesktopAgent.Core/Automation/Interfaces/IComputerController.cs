using DesktopAgent.Core.Automation.Models;

namespace DesktopAgent.Core.Automation.Interfaces;

public interface IComputerController
{
    Task<ComputerActionResult> ExecuteAsync(ParsedComputerCommand command, CancellationToken cancellationToken = default);
}
