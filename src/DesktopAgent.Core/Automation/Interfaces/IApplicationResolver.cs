using DesktopAgent.Core.Automation.Models;
using DesktopAgent.Core.Applications.Models;

namespace DesktopAgent.Core.Automation.Interfaces;

public interface IApplicationResolver
{
    ApplicationDefinition? Resolve(string input);
    ApplicationResolution ResolveInstalled(string input);
    bool Matches(ApplicationDefinition definition, OpenWindowInfo window);
}
