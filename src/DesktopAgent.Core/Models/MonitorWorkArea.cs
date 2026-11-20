namespace DesktopAgent.Core.Models;

public readonly record struct MonitorWorkArea(
    string DeviceName,
    double Left,
    double Top,
    double Width,
    double Height,
    bool IsPrimary)
{
    public double Right => Left + Width;
    public double Bottom => Top + Height;
    public bool Contains(double x, double y) => x >= Left && x < Right && y >= Top && y < Bottom;
}
