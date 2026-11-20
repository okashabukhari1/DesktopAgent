using DesktopAgent.Core.Automation.Enums;
using DesktopAgent.Core.Automation.Models;

namespace DesktopAgent.Core.Automation.Services;

public static class WindowLayoutCalculator
{
    public static WindowRectangle Calculate(WindowLayout layout, WindowRectangle workArea, WindowRectangle current)
    {
        var halfWidth = workArea.Width / 2;
        var halfHeight = workArea.Height / 2;
        return layout switch
        {
            WindowLayout.Left => new(workArea.X, workArea.Y, halfWidth, workArea.Height),
            WindowLayout.Right => new(workArea.X + halfWidth, workArea.Y, workArea.Width - halfWidth, workArea.Height),
            WindowLayout.Top => new(workArea.X, workArea.Y, workArea.Width, halfHeight),
            WindowLayout.Bottom => new(workArea.X, workArea.Y + halfHeight, workArea.Width, workArea.Height - halfHeight),
            WindowLayout.TopLeft => new(workArea.X, workArea.Y, halfWidth, halfHeight),
            WindowLayout.TopRight => new(workArea.X + halfWidth, workArea.Y, workArea.Width - halfWidth, halfHeight),
            WindowLayout.BottomLeft => new(workArea.X, workArea.Y + halfHeight, halfWidth, workArea.Height - halfHeight),
            WindowLayout.BottomRight => new(workArea.X + halfWidth, workArea.Y + halfHeight, workArea.Width - halfWidth, workArea.Height - halfHeight),
            WindowLayout.Center => Center(workArea, current),
            WindowLayout.Maximized => workArea,
            _ => workArea
        };
    }

    private static WindowRectangle Center(WindowRectangle workArea, WindowRectangle current)
    {
        var width = Math.Clamp(current.Width, 200, workArea.Width);
        var height = Math.Clamp(current.Height, 120, workArea.Height);
        return new(workArea.X + (workArea.Width - width) / 2, workArea.Y + (workArea.Height - height) / 2, width, height);
    }
}
