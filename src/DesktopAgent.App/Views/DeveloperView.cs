using System.Windows;
using System.Windows.Controls;
using DesktopAgent.Core.Developer;
using Button = System.Windows.Controls.Button;
using ListBox = System.Windows.Controls.ListBox;
using Orientation = System.Windows.Controls.Orientation;
using UserControl = System.Windows.Controls.UserControl;

namespace DesktopAgent.App.Views;

public sealed class DeveloperView : UserControl
{
    private readonly IDeveloperSessionManager _manager;
    private readonly ListBox _list = new() { MinWidth = 220, MaxHeight = 240 };
    private readonly StackPanel _details = new();
    private readonly StackPanel _permissions = new();
    private string? _selected;
    public DeveloperView(IDeveloperSessionManager manager)
    {
        _manager = manager;
        var page = new StackPanel { Margin = new Thickness(12) };
        page.Children.Add(new TextBlock { Text = "Developer", FontSize = 30, FontWeight = FontWeights.SemiBold });
        page.Children.Add(Text("Observed hook data only · No screenshots, terminal scraping or external session termination."));
        page.Children.Add(_permissions); page.Children.Add(_list); page.Children.Add(_details);
        Content = new ScrollViewer { Content = page, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        _list.SelectionChanged += (_, _) => { if (_list.SelectedItem is SessionItem item) { _selected = item.Id; RenderDetails(); } };
        Loaded += (_, _) => { _manager.Changed += Changed; Refresh(); };
        Unloaded += (_, _) => _manager.Changed -= Changed;
    }
    public void SelectSession(string? sessionId) { _selected = sessionId; Refresh(); }
    private void Changed(object? sender, EventArgs e) { if (!Dispatcher.HasShutdownStarted) Dispatcher.BeginInvoke(Refresh); }
    private void Refresh()
    {
        var sessions = _manager.Sessions;
        _list.ItemsSource = sessions.Select(s => new SessionItem(s.Id, s.SourceTool + " · " + s.State + " · " + (s.ProjectId is null ? "Unregistered project" : s.ProjectPath))).ToArray();
        _list.SelectedItem = _list.Items.Cast<SessionItem>().FirstOrDefault(s => s.Id == _selected);
        _permissions.Children.Clear();
        foreach (var p in _manager.PendingPermissions)
        {
            var card = new StackPanel { Margin = new Thickness(0, 12, 0, 12) };
            card.Children.Add(Text("Permission required · " + p.ToolName + " · " + p.Risk));
            card.Children.Add(Text(p.Summary)); card.Children.Add(Text(p.Reason));
            card.Children.Add(Text("Expires " + p.ExpiresAt.ToLocalTime().ToString("T") + " · No permanent approval."));
            var row = new StackPanel { Orientation = Orientation.Horizontal };
            var allow = new Button { Content = "Allow Once", IsEnabled = p.CanAllow };
            var deny = new Button { Content = "Deny" };
            allow.Click += (_, _) => { _manager.Decide(p.Id, true); Refresh(); };
            deny.Click += (_, _) => { _manager.Decide(p.Id, false); Refresh(); };
            row.Children.Add(allow); row.Children.Add(deny); card.Children.Add(row); _permissions.Children.Add(card);
        }
        RenderDetails();
    }
    private void RenderDetails()
    {
        _details.Children.Clear();
        var s = _manager.Sessions.FirstOrDefault(s => s.Id == _selected);
        if (s is null) { _details.Children.Add(Text(_manager.Sessions.Count == 0 ? "No observed developer sessions. Enable supervision in Settings → Developer Tools." : "Select a session to view activity and summary.")); return; }
        _details.Children.Add(Text(s.Summary));
        _details.Children.Add(Text("Duration: " + ((s.CompletedAt ?? s.LastActivity) - s.StartedAt).ToString(@"hh\:mm\:ss") + " · Recent 128-event window"));
        foreach (var group in s.Events.GroupBy(e => e.EventType)) _details.Children.Add(Text(group.Key + " · " + group.Count()));
        _details.Children.Add(Text("Timeline · newest first"));
        foreach (var e in s.Events.Reverse()) _details.Children.Add(Text(e.Timestamp.ToLocalTime().ToString("T") + " · " + e.EventType + " · " + (e.FilePath ?? e.CommandSummary ?? e.ToolName ?? "") + (e.ExitCode is { } code ? " · exit " + code : "")));
    }
    private static TextBlock Text(string text) => new() { Text = text, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 5, 0, 5) };
    private sealed record SessionItem(string Id, string Label) { public override string ToString() => Label; }
}
