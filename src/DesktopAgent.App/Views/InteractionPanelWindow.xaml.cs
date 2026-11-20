using System.ComponentModel;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media.Animation;

namespace DesktopAgent.App.Views;

public partial class InteractionPanelWindow : Window
{
    private bool _allowClose;
    public InteractionPanelWindow(AssistantView assistant)
    {
        InitializeComponent();
        assistant.IsCompact = true;
        AssistantHost.Content = assistant;
        Loaded += (_, _) => ((Storyboard)Resources["OpenAnimation"]).Begin(this);
        PreviewKeyDown += (_, e) => { if (e.Key == Key.Escape) Close(); };
    }
    private void Close_Click(object sender, RoutedEventArgs e) => Close();
    protected override void OnClosing(CancelEventArgs e)
    {
        if (_allowClose) { base.OnClosing(e); return; }
        e.Cancel = true;
        IsEnabled = false;
        var fade = new DoubleAnimation(0, TimeSpan.FromMilliseconds(140));
        fade.Completed += (_, _) => { _allowClose = true; Close(); };
        BeginAnimation(OpacityProperty, fade);
    }
}
