using DesktopAgent.Core.Models;

namespace DesktopAgent.Core.Interfaces;

public interface IMonitorService
{
    IReadOnlyList<MonitorWorkArea> GetWorkAreas();
}
