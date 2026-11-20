using DesktopAgent.Core.Automation.Models;

namespace DesktopAgent.Core.Automation.Interfaces;

public interface IComputerCommandParser
{
    ParsedComputerCommand Parse(string input);
}
