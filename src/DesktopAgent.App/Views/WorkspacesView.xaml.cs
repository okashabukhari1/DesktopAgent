using System.Windows;
using System.Windows.Controls;
using DesktopAgent.Core.Workspaces.Enums;
using DesktopAgent.Core.Workspaces.Interfaces;
using DesktopAgent.Core.Workspaces.Models;
using Microsoft.Extensions.DependencyInjection;
using WpfButton = System.Windows.Controls.Button;
using WpfContextMenu = System.Windows.Controls.ContextMenu;
using WpfMenuItem = System.Windows.Controls.MenuItem;

namespace DesktopAgent.App.Views;

public partial class WorkspacesView : System.Windows.Controls.UserControl
{
    private readonly IWorkspaceService _workspaces;
    private readonly IServiceProvider _services;
    private Guid? _activeLaunch;

    public WorkspacesView(IWorkspaceService workspaces, IServiceProvider services)
    {
        InitializeComponent();
        _workspaces = workspaces;
        _services = services;
        Loaded += async (_, _) =>
        {
            _workspaces.WorkspacesChanged -= OnWorkspacesChanged;
            _workspaces.ProgressChanged -= OnProgressChanged;
            _workspaces.WorkspacesChanged += OnWorkspacesChanged;
            _workspaces.ProgressChanged += OnProgressChanged;
            try { await _workspaces.InitializeAsync(); Refresh(); }
            catch (Exception exception) { ProgressPanel.Visibility = Visibility.Visible; ProgressMessage.Text = $"Could not load workspaces: {exception.Message}"; }
        };
        Unloaded += (_, _) =>
        {
            _workspaces.WorkspacesChanged -= OnWorkspacesChanged;
            _workspaces.ProgressChanged -= OnProgressChanged;
        };
    }

    private void Refresh()
    {
        var cards = _workspaces.Workspaces.OrderByDescending(item => item.UpdatedAt).Select(item => new WorkspaceCard(item)).ToList();
        WorkspaceItems.ItemsSource = cards;
        EmptyState.Visibility = cards.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        WorkspaceItems.Visibility = cards.Count == 0 ? Visibility.Collapsed : Visibility.Visible;
    }

    private void New_Click(object sender, RoutedEventArgs e) => OpenEditor(new WorkspaceDefinition());
    private void Edit_Click(object sender, RoutedEventArgs e)
    {
        if (TryId(sender, out var id) && _workspaces.Find(id.ToString()) is { } workspace) OpenEditor(workspace);
    }

    private void More_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not WpfButton button || !TryId(sender, out var id)) return;
        var menu = new WpfContextMenu();
        var duplicate = new WpfMenuItem { Header = "Duplicate workspace" };
        duplicate.Click += async (_, _) => await _workspaces.DuplicateAsync(id);
        var delete = new WpfMenuItem { Header = "Delete workspace" };
        delete.Click += async (_, _) => await DeleteAsync(id);
        menu.Items.Add(duplicate);
        menu.Items.Add(delete);
        button.ContextMenu = menu;
        menu.IsOpen = true;
    }

    private async void Launch_Click(object sender, RoutedEventArgs e)
    {
        if (!TryId(sender, out var id) || _workspaces.Find(id.ToString()) is not { } workspace) return;
        _activeLaunch = id;
        WorkspaceItems.IsEnabled = false;
        ProgressPanel.Visibility = Visibility.Visible;
        CancelLaunchButton.IsEnabled = true;
        ProgressTitle.Text = $"Launching {workspace.Name}";
        LaunchProgress.Value = 0;
        ProgressMessage.Text = "Preparing workspace…";
        ProgressDetails.Text = string.Empty;
        try
        {
            var result = await _workspaces.LaunchAsync(id);
            if (result is null) { ProgressMessage.Text = "Workspace was not found."; return; }
            ProgressTitle.Text = result.Session.Status switch
            {
                WorkspaceLaunchStatus.Ready => $"{workspace.Name} is ready",
                WorkspaceLaunchStatus.Partial => $"{workspace.Name} is partially ready",
                WorkspaceLaunchStatus.Cancelled => "Launch cancelled",
                WorkspaceLaunchStatus.InvalidConfiguration => "Workspace needs repair",
                _ => "Launch failed"
            };
            ProgressMessage.Text = result.Message;
            ProgressDetails.Text = string.Join(Environment.NewLine, result.Session.Results.Select(step =>
                $"{(step.Success ? "✓" : "✕")} {step.Target}: {step.Message}"));
        }
        catch (Exception exception) { ProgressTitle.Text = "Launch failed"; ProgressMessage.Text = exception.Message; }
        finally { CancelLaunchButton.IsEnabled = false; _activeLaunch = null; WorkspaceItems.IsEnabled = true; }
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        if (_activeLaunch.HasValue) _workspaces.Cancel(_activeLaunch.Value);
    }

    private void OnWorkspacesChanged(object? sender, EventArgs e) => Dispatcher.BeginInvoke(Refresh);
    private void OnProgressChanged(object? sender, WorkspaceProgress progress) => Dispatcher.BeginInvoke(() =>
    {
        if (_activeLaunch != progress.WorkspaceId) return;
        LaunchProgress.Maximum = Math.Max(1, progress.TotalSteps);
        LaunchProgress.Value = progress.CompletedSteps;
        ProgressMessage.Text = progress.Message;
    });

    private void OpenEditor(WorkspaceDefinition workspace)
    {
        var editor = ActivatorUtilities.CreateInstance<WorkspaceEditorWindow>(_services, workspace);
        editor.Owner = Window.GetWindow(this);
        editor.ShowDialog();
    }

    private async Task DeleteAsync(Guid id)
    {
        var workspace = _workspaces.Find(id.ToString());
        if (workspace is null) return;
        var choice = System.Windows.MessageBox.Show($"Delete workspace '{workspace.Name}'?\n\nThis removes only the saved workspace definition. It does not uninstall applications or delete any files or folders.",
            "Delete workspace", MessageBoxButton.YesNo, MessageBoxImage.Warning, MessageBoxResult.No);
        if (choice == MessageBoxResult.Yes) await _workspaces.DeleteAsync(id);
    }

    private static bool TryId(object sender, out Guid id)
    {
        id = Guid.Empty;
        if (sender is not FrameworkElement { Tag: Guid value }) return false;
        id = value;
        return id != Guid.Empty;
    }

    private sealed record WorkspaceCard(Guid Id, string Name, string Description, string Glyph, string Summary)
    {
        public WorkspaceCard(WorkspaceDefinition workspace) : this(workspace.Id, workspace.Name,
            string.IsNullOrWhiteSpace(workspace.Description) ? "No description" : workspace.Description,
            workspace.IconKey switch { "Code" => "</>", "Study" => "▤", "Meeting" => "◫", "Creative" => "◇", _ => "▦" },
            $"{workspace.Applications.Count} apps · {workspace.Folders.Count + workspace.Files.Count + workspace.Urls.Count} resources") { }
    }
}
