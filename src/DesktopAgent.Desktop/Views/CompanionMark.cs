using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace DesktopAgent.Desktop.Views;

/// <summary>Static translation of the existing WPF smiling purple orb; no floating desktop window.</summary>
internal sealed class CompanionMark : Control
{
    private static readonly IBrush Body = new RadialGradientBrush
    {
        Center = new RelativePoint(.35, .3, RelativeUnit.Relative),
        GradientStops = [new(Color.Parse("#A9A3FF"), 0), new(Color.Parse("#6256E8"), .62), new(Color.Parse("#332B78"), 1)]
    };
    public CompanionMark() { Width = 70; Height = 70; }
    public override void Render(DrawingContext context)
    {
        base.Render(context);
        context.DrawEllipse(Body, null, new Point(35, 35), 33, 33);
        context.DrawEllipse(Brushes.White, null, new Point(25, 30), 3, 5);
        context.DrawEllipse(Brushes.White, null, new Point(45, 30), 3, 5);
        context.DrawGeometry(null, new Pen(Brushes.White, 3), Geometry.Parse("M 24,43 Q 35,53 46,43"));
    }
}
