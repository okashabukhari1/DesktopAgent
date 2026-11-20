using DesktopAgent.Core.Automation.Models;

namespace DesktopAgent.Core.Automation.Interfaces;

public interface IShellService
{
    ComputerActionResult OpenFolder(string path);
    ComputerActionResult OpenFile(string path);
    ComputerActionResult OpenUrl(string url);
}
