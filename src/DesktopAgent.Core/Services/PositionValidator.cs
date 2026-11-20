using DesktopAgent.Core.Models;

namespace DesktopAgent.Core.Services;

public static class PositionValidator
{
    public const double EdgeMargin = 8;

    public static ValidatedPosition Validate(
        CharacterPosition? saved,
        double windowWidth,
        double windowHeight,
        IReadOnlyList<MonitorWorkArea> monitors)
    {
        if (monitors.Count == 0)
            return new ValidatedPosition(EdgeMargin, EdgeMargin, string.Empty);

        var primary = monitors.FirstOrDefault(m => m.IsPrimary);
        if (string.IsNullOrEmpty(primary.DeviceName)) primary = monitors[0];

        var monitor = saved is null
            ? primary
            : monitors.FirstOrDefault(m => string.Equals(m.DeviceName, saved.MonitorDeviceName, StringComparison.OrdinalIgnoreCase));

        if (string.IsNullOrEmpty(monitor.DeviceName))
        {
            monitor = saved is null
                ? primary
                : monitors.FirstOrDefault(m => m.Contains(saved.X + (windowWidth / 2), saved.Y + (windowHeight / 2)));
            if (string.IsNullOrEmpty(monitor.DeviceName)) monitor = primary;
        }

        var desiredX = saved?.X ?? monitor.Right - windowWidth - 36;
        var desiredY = saved?.Y ?? monitor.Bottom - windowHeight - 48;
        var minX = monitor.Left + EdgeMargin;
        var minY = monitor.Top + EdgeMargin;
        var maxX = Math.Max(minX, monitor.Right - windowWidth - EdgeMargin);
        var maxY = Math.Max(minY, monitor.Bottom - windowHeight - EdgeMargin);

        return new ValidatedPosition(
            Math.Clamp(desiredX, minX, maxX),
            Math.Clamp(desiredY, minY, maxY),
            monitor.DeviceName);
    }
}
