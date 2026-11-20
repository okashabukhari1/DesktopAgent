using System.Windows;
using System.Windows.Media.Animation;
using System.Windows.Threading;

namespace DesktopAgent.App.Views;

public partial class NotificationBubbleWindow : Window
{
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromSeconds(6) };
    public NotificationBubbleWindow()
    {
        InitializeComponent();
        _timer.Tick += (_, _) => { _timer.Stop(); Close(); };
        Loaded += (_, _) => BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(180)));
    }
    public void SetMessage(string message, bool autoClose = true)
    {
        MessageText.Text = message;
        if (autoClose) _timer.Start();
    }
    private void Close_Click(object sender, RoutedEventArgs e) => Close();
    protected override void OnClosed(EventArgs e) { _timer.Stop(); base.OnClosed(e); }
}
