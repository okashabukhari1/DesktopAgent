using DesktopAgent.Core.Interfaces;
using DesktopAgent.Core.Models;
using System.Windows.Forms;
using System.Runtime.InteropServices;

namespace DesktopAgent.Infrastructure.Windows;

public sealed class WindowsMonitorService : IMonitorService
{
    public IReadOnlyList<MonitorWorkArea> GetWorkAreas()
    {
        // Screen reports physical pixels in a PerMonitorV2 process, while WPF Window.Left/Top
        // use device-independent units. Use the system (primary-monitor) DPI as the virtual
        // desktop coordinate scale so monitor origins remain in one consistent coordinate space.
        var scale = GetSystemScale();
        return Screen.AllScreens
            .Select(screen => new MonitorWorkArea(
                screen.DeviceName,
                screen.WorkingArea.Left / scale,
                screen.WorkingArea.Top / scale,
                screen.WorkingArea.Width / scale,
                screen.WorkingArea.Height / scale,
                screen.Primary))
            .ToArray();
    }

    private static double GetSystemScale()
    {
        try
        {
            var dpi = GetDpiForSystem();
            return dpi == 0 ? 1 : dpi / 96d;
        }
        catch (EntryPointNotFoundException)
        {
            return 1;
        }
    }

    [DllImport("user32.dll")]
    private static extern uint GetDpiForSystem();
}
