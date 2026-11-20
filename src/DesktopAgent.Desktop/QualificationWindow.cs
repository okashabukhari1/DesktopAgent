using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;

namespace DesktopAgent.Desktop;

internal sealed class QualificationWindow : Window
{
    public QualificationWindow()
    {
        Title = "Desktop Agent — Native Wayland Qualification";
        Width = 760; Height = 520; MinWidth = 540; MinHeight = 400;
        var input = new TextBox { PlaceholderText = "Type a disposable test phrase here", AcceptsReturn = true, MinHeight = 90 };
        var confirm = new Button { Content = "Confirm visible rendering", HorizontalAlignment = HorizontalAlignment.Left };
        var close = new Button { Content = "Close qualification window", HorizontalAlignment = HorizontalAlignment.Left };
        Content = new StackPanel
        {
            Margin = new Thickness(28), Spacing = 16,
            Children =
            {
                new TextBlock { Text = "Desktop Agent", FontSize = 28, FontWeight = FontWeight.SemiBold, Foreground = Brush.Parse("#7467EF") },
                new TextBlock { Text = "Native Wayland qualification · Avalonia 12.1.6", FontSize = 18 },
                new TextBlock { Text = "Resize and move this window using GNOME. Click the button, focus the field, and type a test phrase. Close the window when finished.", TextWrapping = TextWrapping.Wrap },
                new TextBlock { Text = "Only event counts are reported. No existing settings, credentials, conversations or workspaces are loaded.", TextWrapping = TextWrapping.Wrap },
                input, confirm, close
            }
        };
        Opened += (_, _) => QualificationLog.Write("window-opened", new { implementation = PlatformImpl?.GetType().AssemblyQualifiedName, handle = TryGetPlatformHandle()?.HandleDescriptor, width = ClientSize.Width, height = ClientSize.Height });
        Activated += (_, _) => QualificationLog.Write("window-activated");
        Deactivated += (_, _) => QualificationLog.Write("window-deactivated");
        SizeChanged += (_, _) => QualificationLog.Write("window-size", new { width = ClientSize.Width, height = ClientSize.Height });
        Closed += (_, _) => QualificationLog.Write("window-closed");
        input.GotFocus += (_, _) => QualificationLog.Write("input-focused");
        input.KeyDown += (_, _) => QualificationLog.Write("keyboard-event");
        input.TextChanged += (_, _) => QualificationLog.Write("text-changed", new { length = input.Text?.Length ?? 0 });
        confirm.Click += (_, _) => QualificationLog.Write("rendering-confirmed-by-user");
        confirm.PointerPressed += (_, _) => QualificationLog.Write("pointer-pressed");
        close.Click += (_, _) => Close();
    }
}
