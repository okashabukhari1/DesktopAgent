using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using DesktopAgent.Core.Automation.Enums;
using DesktopAgent.Core.Automation.Interfaces;
using DesktopAgent.Core.Automation.Models;

namespace DesktopAgent.App.Views;

public partial class ActivityView : System.Windows.Controls.UserControl
{
    private static readonly string[] Filters = ["All", "AI", "Applications", "Workspaces", "Permissions", "Errors"];
    private readonly IActionHistoryService _history;

    public ActivityView(IActionHistoryService history)
    {
        InitializeComponent();
        _history = history;
        FilterBox.ItemsSource = Filters;
        FilterBox.SelectedIndex = 0;
        ActivityItems.ItemsSource = history.GetRecent().Select(ActivityRow.From).ToArray();
        Unloaded += (_, _) => history.EntryAdded -= OnEntryAdded;
        Loaded += (_, _) => { history.EntryAdded -= OnEntryAdded; history.EntryAdded += OnEntryAdded; Refresh(); };
    }

    private void Filter_SelectionChanged(object sender, SelectionChangedEventArgs e) => Refresh();
    private void OnEntryAdded(object? sender, ActionHistoryEntry entry) => Dispatcher.Invoke(Refresh);

    private void Refresh()
    {
        var filter = FilterBox.SelectedItem as string ?? "All";
        var entries = _history.GetRecent().Where(entry => Matches(entry, filter)).Select(ActivityRow.From).ToArray();
        ActivityItems.ItemsSource = entries;
        EmptyState.Visibility = entries.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    private static bool Matches(ActionHistoryEntry entry, string filter) => filter switch
    {
        "AI" => string.Equals(entry.Source, "AI", StringComparison.OrdinalIgnoreCase),
        "Applications" => entry.Action is ComputerCommandType.ListApplications or ComputerCommandType.SearchApplications
            or ComputerCommandType.RefreshApplications or ComputerCommandType.OpenApplication or ComputerCommandType.OpenExplicitApplication
            or ComputerCommandType.FocusApplication or ComputerCommandType.CloseApplication or ComputerCommandType.MinimizeWindow
            or ComputerCommandType.MaximizeWindow or ComputerCommandType.RestoreWindow or ComputerCommandType.MoveWindow
            or ComputerCommandType.ResizeWindow or ComputerCommandType.ApplyLayout or ComputerCommandType.MoveToMonitor,
        "Workspaces" => entry.Action is ComputerCommandType.ListWorkspaces or ComputerCommandType.WorkspaceStatus or ComputerCommandType.LaunchWorkspace or ComputerCommandType.StopWorkspace,
        "Permissions" => entry.Status == ActionStatus.PermissionDenied,
        "Errors" => entry.Status is not (ActionStatus.Success or ActionStatus.AlreadyRunning),
        _ => true
    };

    private sealed record ActivityRow(string Time, string Source, string Status, string Action, string Target, string Message, System.Windows.Media.Brush StatusBrush)
    {
        public static ActivityRow From(ActionHistoryEntry entry) => new(entry.Timestamp.ToLocalTime().ToString("h:mm:ss tt"), entry.Source,
            entry.Status.ToString(), entry.Action.ToString(), entry.Target, entry.Message,
            entry.Successful() ? System.Windows.Media.Brushes.SeaGreen : new SolidColorBrush(System.Windows.Media.Color.FromRgb(193, 65, 77)));
    }
}

file static class ActionHistoryEntryExtensions
{
    public static bool Successful(this ActionHistoryEntry entry) => entry.Status is ActionStatus.Success or ActionStatus.AlreadyRunning;
}
