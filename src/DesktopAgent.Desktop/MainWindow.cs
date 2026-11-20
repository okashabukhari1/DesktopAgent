using System.ComponentModel;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Data;
using Avalonia.Layout;
using Avalonia.Media;
using DesktopAgent.Core.Enums;
using DesktopAgent.Desktop.ViewModels;
using DesktopAgent.Desktop.Views;

namespace DesktopAgent.Desktop;

internal sealed class MainWindow : Window
{
    private readonly ShellViewModel shell;
    private readonly ContentControl page = new();
    private readonly Dictionary<DesktopPage, Button> navigation = [];
    private static readonly IBrush Accent = Brush.Parse("#7467EF");

    public MainWindow(ShellViewModel shell)
    {
        this.shell = shell;
        DataContext = shell;
        Title = "Desktop Agent"; Width = 1100; Height = 760; MinWidth = 820; MinHeight = 560;
        AutomationProperties.SetName(this, "Desktop Agent application");
        var layout = new Grid { ColumnDefinitions = new ColumnDefinitions("210,*") };
        var side = new StackPanel { Spacing = 5, Margin = new Thickness(18, 24) };
        side.Children.Add(new CompanionMark { HorizontalAlignment = HorizontalAlignment.Left });
        side.Children.Add(new TextBlock { Text = "Desktop Agent", FontSize = 21, FontWeight = FontWeight.SemiBold, Foreground = Brushes.White, Margin = new Thickness(0, 8, 0, 0) });
        side.Children.Add(new TextBlock { Text = "Your desktop companion", Foreground = Brush.Parse("#ABB1C9"), FontSize = 12, Margin = new Thickness(0, 0, 0, 24) });
        foreach (var destination in Enum.GetValues<DesktopPage>())
        {
            var button = new Button
            {
                Content = destination.ToString(), HorizontalAlignment = HorizontalAlignment.Stretch,
                HorizontalContentAlignment = HorizontalAlignment.Left,
                Padding = new Thickness(14, 10), Foreground = Brushes.White,
                Background = Brushes.Transparent, BorderThickness = new Thickness(0), CornerRadius = new CornerRadius(8)
            };
            button.Click += (_, _) => shell.Navigate(destination);
            navigation.Add(destination, button); side.Children.Add(button);
        }
        side.Children.Add(new TextBlock { Text = "Linux desktop foundation", Foreground = Brush.Parse("#ABB1C9"), FontSize = 11, Margin = new Thickness(0, 20, 0, 0), TextWrapping = TextWrapping.Wrap });
        layout.Children.Add(new Border { Background = Brush.Parse("#171A2B"), Child = new ScrollViewer { Content = side } });
        Grid.SetColumn(page, 1); page.Margin = new Thickness(30, 26); layout.Children.Add(page);
        Content = layout;
        shell.PropertyChanged += ShellChanged;
        Opened += (_, _) => QualificationLog.Write("shell-opened", new
        {
            implementation = PlatformImpl?.GetType().AssemblyQualifiedName,
            credentialStorageAvailable = shell.CredentialStorageAvailable,
            coreState = shell.CharacterState.ToString()
        });
        SizeChanged += (_, _) => QualificationLog.Write("shell-size", new { width = ClientSize.Width, height = ClientSize.Height });
        Closed += (_, _) => { shell.PropertyChanged -= ShellChanged; QualificationLog.Write("shell-closed"); };
        ShowPage();
    }

    private void ShellChanged(object? sender, PropertyChangedEventArgs args)
    {
        if (args.PropertyName == nameof(ShellViewModel.SelectedPage))
        {
            ShowPage();
            QualificationLog.Write("navigation", new { page = shell.SelectedPage.ToString() });
        }
        if (args.PropertyName == nameof(ShellViewModel.Theme))
            QualificationLog.Write("theme", new { theme = shell.Theme.ToString() });
    }

    private void ShowPage()
    {
        foreach (var (destination, button) in navigation)
            button.Background = destination == shell.SelectedPage ? Accent : Brushes.Transparent;
        page.Content = shell.SelectedPage switch
        {
            DesktopPage.Home => Home(),
            DesktopPage.Assistant => Assistant(),
            DesktopPage.Settings => Settings(),
            _ => Unavailable(shell.SelectedPage)
        };
    }

    private Control Home()
    {
        var body = Page("Welcome back", "Your desktop companion, now on Linux.");
        body.Children.Add(Card("Agent", "Desktop ready · AI disconnected", "Your assistant connection is unavailable in this foundation. Secure Linux credential storage is required before messages can be sent."));
        var ask = new Button { Content = "Open Assistant", Padding = new Thickness(18, 10), Background = Accent, Foreground = Brushes.White };
        ask.Click += (_, _) => shell.Navigate(DesktopPage.Assistant); body.Children.Add(ask);
        body.Children.Add(Card("Projects & workspaces", "No data loaded", "Existing project and workspace files have been left untouched. Linux discovery, launching and data import are not connected."));
        body.Children.Add(Card("Recent activity", "No actions this session", "Desktop control and background agents are unavailable. Your existing activity history has not been loaded."));
        return Scroll(body);
    }

    private Control Assistant()
    {
        var layout = new Grid { RowDefinitions = new RowDefinitions("Auto,*,Auto"), RowSpacing = 18 };
        var heading = Page("Assistant", "One place for your conversations and agent tasks.");
        heading.Children.Add(Card("Disconnected", "AI chat is unavailable", "Secure credential storage and the existing conversation pipeline are not connected to this Linux host. No messages will be sent."));
        layout.Children.Add(heading);
        var empty = new StackPanel { VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Center, Spacing = 10 };
        empty.Children.Add(new CompanionMark { HorizontalAlignment = HorizontalAlignment.Center });
        empty.Children.Add(new TextBlock { Text = "No conversation in this session", FontSize = 19, FontWeight = FontWeight.SemiBold, TextWrapping = TextWrapping.Wrap, HorizontalAlignment = HorizontalAlignment.Center });
        empty.Children.Add(Text("Conversation history has not been imported. Your draft stays in this window's session."));
        Grid.SetRow(empty, 1); layout.Children.Add(empty);
        var composer = new StackPanel { Spacing = 10 };
        var input = new TextBox { PlaceholderText = "Write a draft…", AcceptsReturn = true, MinHeight = 84, MaxHeight = 150, MaxLength = 4000, TextWrapping = TextWrapping.Wrap };
        input.Bind(TextBox.TextProperty, new Binding(nameof(ShellViewModel.Draft)) { Mode = BindingMode.TwoWay });
        AutomationProperties.SetName(input, "Message draft. AI disconnected.");
        var send = new Button { Content = "Send unavailable", IsEnabled = false, HorizontalAlignment = HorizontalAlignment.Right };
        composer.Children.Add(input); composer.Children.Add(send);
        Grid.SetRow(composer, 2); layout.Children.Add(composer);
        return layout;
    }

    private Control Settings()
    {
        var body = Page("Settings", "Preferences for this desktop session.");
        var appearance = new StackPanel { Spacing = 10 };
        appearance.Children.Add(Heading("Appearance", 18));
        appearance.Children.Add(Text("Choose System, Light or Dark. This preference resets when the application closes."));
        var theme = new ComboBox { ItemsSource = Enum.GetValues<AppTheme>(), SelectedItem = shell.Theme, MinWidth = 180 };
        AutomationProperties.SetName(theme, "Appearance theme");
        theme.SelectionChanged += (_, _) => { if (theme.SelectedItem is AppTheme selected) shell.SetTheme(selected); };
        appearance.Children.Add(theme); body.Children.Add(Panel(appearance));
        body.Children.Add(Card("AI connection", "Disconnected", "Linux credential storage is not available yet. API keys cannot be entered, read or saved here. Provider settings and existing credentials are unchanged."));
        body.Children.Add(Card("Privacy & permissions", "Existing data preserved", "No existing settings, conversations, workspaces, permission rules or developer hooks are loaded or changed. Computer Use and external actions are unavailable."));
        body.Children.Add(Card("About", "Desktop Agent · Linux foundation", "Avalonia 12.1.6 · Native Wayland. This build includes the initial desktop shell; system integration comes in a later phase."));
        return Scroll(body);
    }

    private static Control Unavailable(DesktopPage destination)
    {
        var body = Page(destination.ToString(), "This feature is not connected in the Linux foundation.");
        var description = destination switch
        {
            DesktopPage.Permissions => "Existing Allow / Ask / Deny rules remain unchanged. No policy files are loaded and no external actions are authorized by this host.",
            DesktopPage.Projects => "Project discovery, source retrieval and editor integration are unavailable. Existing project files and metadata remain untouched.",
            DesktopPage.Workspaces => "Saved workspace definitions remain untouched. Workspace launch, editing and import are not connected.",
            DesktopPage.Developer => "Developer supervision is unavailable. No hooks, relay, worker or background agent is started.",
            DesktopPage.Activity => "No action history has been loaded. This host has not performed any desktop actions.",
            _ => "Linux application discovery, launching and external window management are not connected."
        };
        body.Children.Add(Card("Unavailable", "No operation performed", description));
        return Scroll(body);
    }

    private static StackPanel Page(string title, string subtitle) => new()
    {
        Spacing = 18, Children = { Heading(title, 29), Text(subtitle) }
    };
    private static TextBlock Heading(string value, double size) => new() { Text = value, FontSize = size, FontWeight = FontWeight.SemiBold, TextWrapping = TextWrapping.Wrap };
    private static TextBlock Text(string value) => new() { Text = value, TextWrapping = TextWrapping.Wrap, Opacity = .78, LineHeight = 22 };
    private static Border Card(string heading, string status, string description) => Panel(new StackPanel
    {
        Spacing = 10, Children = { Heading(heading, 18), new TextBlock { Text = status, Foreground = Accent, FontWeight = FontWeight.SemiBold, TextWrapping = TextWrapping.Wrap }, Text(description) }
    });
    private static Border Panel(Control child) => new() { Child = child, Padding = new Thickness(20), CornerRadius = new CornerRadius(12), BorderThickness = new Thickness(1), BorderBrush = Brush.Parse("#487E7E9C") };
    private static ScrollViewer Scroll(Control content) => new() { Content = content, HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled };
}
