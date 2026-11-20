using System.Windows;
using DesktopAgent.App.ViewModels;
using DesktopAgent.Core.Interfaces;
using DesktopAgent.Core.Automation.Interfaces;
using Microsoft.Extensions.DependencyInjection;
using DesktopAgent.Core.Ai;
using DesktopAgent.Core.Workspaces.Interfaces;

namespace DesktopAgent.App.Views;

public partial class DesktopAppWindow : Window
{
    private readonly IWindowService _windows;
    private readonly IActionHistoryService _history;
    private readonly IServiceProvider _services;
    private WorkspacesView? _workspacesView;
    private ApplicationsView? _applicationsView;
    private PermissionsView? _permissionsView;
    private AssistantView? _assistantView;
    private ActivityView? _activityView;
    private ProjectsView? _projectsView;
    private DeveloperView? _developerView;
    private readonly DesktopAgent.Core.Developer.IDeveloperSessionManager _developer;
    private readonly DesktopAgent.Core.Projects.IProjectCatalog _projects;
    private readonly DesktopAgent.Core.Projects.CurrentProjectContext _currentProject;
    private readonly IAssistantConversationService _assistant;
    private readonly IAiConnectionService _connection;
    private readonly ISettingsService _settings;
    private readonly IWorkspaceService _workspaceService;

    public DesktopAppWindow(CharacterViewModel viewModel, IWindowService windows, IActionHistoryService history, IServiceProvider services)
    {
        InitializeComponent();
        DataContext = viewModel;
        _windows = windows;
        _history = history;
        _services = services;
        _developer = services.GetRequiredService<DesktopAgent.Core.Developer.IDeveloperSessionManager>();
        _developer.Changed += RefreshHomeEvent;
        _assistant = services.GetRequiredService<IAssistantConversationService>();
        _connection = services.GetRequiredService<IAiConnectionService>();
        _settings = services.GetRequiredService<ISettingsService>();
        _workspaceService = services.GetRequiredService<IWorkspaceService>();
        _projects = services.GetRequiredService<DesktopAgent.Core.Projects.IProjectCatalog>();
        _currentProject = services.GetRequiredService<DesktopAgent.Core.Projects.CurrentProjectContext>();
        _projects.Changed += RefreshHomeEvent;
        _currentProject.Changed += RefreshHomeEvent;
        HistoryList.ItemsSource = history.GetRecent();
        history.EntryAdded += OnHistoryAdded;
        _assistant.Changed += RefreshHomeEvent;
        _connection.Changed += RefreshHomeEvent;
        _workspaceService.WorkspacesChanged += RefreshHomeEvent;
        Loaded += (_, _) => RefreshHome();
        Activated += (_, _) => RefreshHome();
    }

    private void Home_Click(object sender, RoutedEventArgs e) => NavigateHome();
    private void Projects_Click(object sender, RoutedEventArgs e) => NavigateProjects();
    private void Developer_Click(object sender, RoutedEventArgs e) => NavigateDeveloper();
    private void ClearProject_Click(object sender, RoutedEventArgs e) => _currentProject.Clear();
    private async void OpenRecentProject_Click(object sender, RoutedEventArgs e)
    {
        if (sender is System.Windows.Controls.Button { DataContext: DesktopAgent.Core.Projects.ProjectProfile p })
        {
            try { CurrentProjectText.Text = (await _services.GetRequiredService<DesktopAgent.Core.Projects.IProjectOpenService>().OpenAsync(p.Id.ToString())).Message; }
            catch { CurrentProjectText.Text = "Project opening failed. Check Projects preferences."; }
        }
    }
    private void Workspaces_Click(object sender, RoutedEventArgs e) => NavigateWorkspaces();
    private void Applications_Click(object sender, RoutedEventArgs e) => NavigateApplications();
    private void Permissions_Click(object sender, RoutedEventArgs e) => NavigatePermissions();
    private void Activity_Click(object sender, RoutedEventArgs e) => NavigateActivity();
    private void Assistant_Click(object sender, RoutedEventArgs e) => NavigateAssistant();
    private void Settings_Click(object sender, RoutedEventArgs e) => _windows.ShowSettings();
    private void ShowCharacter_Click(object sender, RoutedEventArgs e) => _windows.ShowCharacter();
    private void HideCharacter_Click(object sender, RoutedEventArgs e) => _windows.HideCharacter();
    private void OnHistoryAdded(object? sender, Core.Automation.Models.ActionHistoryEntry entry) =>
        Dispatcher.BeginInvoke(() => HistoryList.ItemsSource = _history.GetRecent().Take(8));
    private void RefreshHomeEvent(object? sender, EventArgs e) => Dispatcher.BeginInvoke(RefreshHome);
    private void RefreshHome()
    {
        var verified = _connection.Status.State == AiConnectionState.Connected &&
            _connection.Status.ConfigurationId == Infrastructure.Ai.AssistantConversationService.ConfigurationId(_settings);
        AgentConnectionText.Text = !_settings.Current.Ai.Enabled ? "AI off · Direct commands available" :
            verified ? "AI connected · " + _settings.Current.Ai.Model : "AI connection not verified · Test in Settings";
        AgentStateText.Text = _assistant.IsBusy ? _assistant.State.ToString() : "Ready";
        DeveloperActivityText.Text = string.Join("\n", _developer.Sessions.Take(3).Select(s => s.SourceTool + " · " + s.State + " · " + s.Summary));
        if (_developer.Sessions.Count == 0) DeveloperActivityText.Text = "No active developer sessions";
        RecentWorkspaces.ItemsSource = _workspaceService.Workspaces.OrderByDescending(w =>
            _workspaceService.Sessions.TryGetValue(w.Id, out var session) ? session.StartedAt : w.UpdatedAt).Take(4);
        NoWorkspacesText.Visibility = _workspaceService.Workspaces.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        HistoryList.ItemsSource = _history.GetRecent().Take(8);
        CurrentProjectText.Text = _currentProject.Current is { } project ? "Current: " + project.Name : "No current project selected.";
        RecentProjects.ItemsSource = _projects.Projects.Where(p => p.LastOpenedAt.HasValue).OrderByDescending(p => p.LastOpenedAt).Take(4);
    }

    public void NavigateWorkspaces()
    {
        _workspacesView ??= ActivatorUtilities.CreateInstance<WorkspacesView>(_services);
        PageHost.Content = _workspacesView;
        PageHost.Visibility = Visibility.Visible;
        HomePanel.Visibility = Visibility.Collapsed;
        HomeButton.Background = System.Windows.Media.Brushes.Transparent;
        ApplicationsButton.Background = System.Windows.Media.Brushes.Transparent;
        WorkspacesButton.Background = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(102, 92, 246));
        ClearNewNavigation();
    }

    public void NavigateProjects()
    {
        _projectsView ??= ActivatorUtilities.CreateInstance<ProjectsView>(_services);
        PageHost.Content = _projectsView; PageHost.Visibility = Visibility.Visible; HomePanel.Visibility = Visibility.Collapsed;
        ClearLegacyNavigation(); ClearNewNavigation();
        ProjectsButton.Background = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(102, 92, 246));
    }

    public void NavigateDeveloper(string? sessionId = null)
    {
        _developerView ??= ActivatorUtilities.CreateInstance<DeveloperView>(_services);
        PageHost.Content = _developerView; PageHost.Visibility = Visibility.Visible; HomePanel.Visibility = Visibility.Collapsed;
        ClearLegacyNavigation(); ClearNewNavigation(); _developerView.SelectSession(sessionId);
        DeveloperButton.Background = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(102, 92, 246));
    }

    public void NavigateApplications()
    {
        _applicationsView ??= ActivatorUtilities.CreateInstance<ApplicationsView>(_services);
        PageHost.Content = _applicationsView;
        PageHost.Visibility = Visibility.Visible;
        HomePanel.Visibility = Visibility.Collapsed;
        HomeButton.Background = System.Windows.Media.Brushes.Transparent;
        WorkspacesButton.Background = System.Windows.Media.Brushes.Transparent;
        ApplicationsButton.Background = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(102, 92, 246));
        ClearNewNavigation();
    }

    public void NavigatePermissions()
    {
        _permissionsView ??= ActivatorUtilities.CreateInstance<PermissionsView>(_services);
        PageHost.Content = _permissionsView;
        PageHost.Visibility = Visibility.Visible;
        HomePanel.Visibility = Visibility.Collapsed;
        ClearLegacyNavigation();
        ClearNewNavigation();
        PermissionsButton.Background = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(102, 92, 246));
    }

    public void NavigateAssistant()
    {
        _assistantView ??= ActivatorUtilities.CreateInstance<AssistantView>(_services);
        PageHost.Content = _assistantView;
        PageHost.Visibility = Visibility.Visible;
        HomePanel.Visibility = Visibility.Collapsed;
        ClearLegacyNavigation();
        ClearNewNavigation();
        AssistantButton.Background = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(102, 92, 246));
    }

    public void NavigateActivity()
    {
        _activityView ??= ActivatorUtilities.CreateInstance<ActivityView>(_services);
        PageHost.Content = _activityView;
        PageHost.Visibility = Visibility.Visible;
        HomePanel.Visibility = Visibility.Collapsed;
        ClearLegacyNavigation();
        ClearNewNavigation();
        ActivityButton.Background = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(102, 92, 246));
    }


    private void NavigateHome()
    {
        RefreshHome();
        PageHost.Visibility = Visibility.Collapsed;
        HomePanel.Visibility = Visibility.Visible;
        WorkspacesButton.Background = System.Windows.Media.Brushes.Transparent;
        ApplicationsButton.Background = System.Windows.Media.Brushes.Transparent;
        ClearNewNavigation();
        HomeButton.Background = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(102, 92, 246));
    }

    private void ClearLegacyNavigation()
    {
        HomeButton.Background = System.Windows.Media.Brushes.Transparent;
        ApplicationsButton.Background = System.Windows.Media.Brushes.Transparent;
        WorkspacesButton.Background = System.Windows.Media.Brushes.Transparent;
    }

    private void ClearNewNavigation()
    {
        DeveloperButton.Background = System.Windows.Media.Brushes.Transparent;
        ProjectsButton.Background = System.Windows.Media.Brushes.Transparent;
        AssistantButton.Background = System.Windows.Media.Brushes.Transparent;
        PermissionsButton.Background = System.Windows.Media.Brushes.Transparent;
        ActivityButton.Background = System.Windows.Media.Brushes.Transparent;
    }
    protected override void OnClosed(EventArgs e)
    {
        _history.EntryAdded -= OnHistoryAdded;
        _developer.Changed -= RefreshHomeEvent;
        _assistant.Changed -= RefreshHomeEvent;
        _connection.Changed -= RefreshHomeEvent;
        _workspaceService.WorkspacesChanged -= RefreshHomeEvent;
        _projects.Changed -= RefreshHomeEvent;
        _currentProject.Changed -= RefreshHomeEvent;
        base.OnClosed(e);
    }
}
