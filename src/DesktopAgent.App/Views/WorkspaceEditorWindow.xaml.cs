using System.Windows;
using DesktopAgent.Core.Automation.Enums;
using DesktopAgent.Core.Automation.Interfaces;
using DesktopAgent.Core.Workspaces.Enums;
using DesktopAgent.Core.Workspaces.Interfaces;
using DesktopAgent.Core.Workspaces.Models;
using Microsoft.Win32;
using DesktopAgent.Core.Applications.Interfaces;

namespace DesktopAgent.App.Views;

public partial class WorkspaceEditorWindow : Window
{
    private readonly IWorkspaceService _workspaces;
    private readonly IApplicationControlService _applications;
    private readonly IApplicationCatalog _catalog;
    private readonly WorkspaceDefinition _workspace;
    private readonly DesktopAgent.Core.Projects.IProjectCatalog? _projects;

    public WorkspaceEditorWindow(WorkspaceDefinition workspace, IWorkspaceService workspaces, IApplicationControlService applications, IApplicationCatalog catalog,
        DesktopAgent.Core.Projects.IProjectCatalog? projects = null)
    {
        InitializeComponent();
        _workspace = workspace.DeepCopy();
        _workspaces = workspaces;
        _applications = applications;
        _catalog = catalog;
        _projects = projects;
        UpdateProjectName();
        EditorTitle.Text = workspace.Name.Length == 0 ? "New workspace" : $"Edit {workspace.Name}";
        NameInput.Text = _workspace.Name;
        DescriptionInput.Text = _workspace.Description;
        IconInput.ItemsSource = new[] { "Briefcase", "Code", "Study", "Meeting", "Creative" };
        IconInput.SelectedItem = _workspace.IconKey;
        FinalFocusInput.Text = _workspace.FinalFocusApplication;
        TimeoutInput.Text = _workspace.LaunchTimeoutSeconds.ToString();
        ContinueInput.IsChecked = _workspace.ContinueOnFailure;
        ProgressInput.IsChecked = _workspace.ShowDetailedProgress;
        LayoutColumn.ItemsSource = Enum.GetValues<WindowLayout>().Cast<WindowLayout?>().Prepend(null);
        PreActionTypeColumn.ItemsSource = Enum.GetValues<WorkspaceActionType>();
        PostActionTypeColumn.ItemsSource = Enum.GetValues<WorkspaceActionType>();
        RefreshBindings();
    }

    private void RefreshBindings()
    {
        ApplicationsGrid.ItemsSource = null; ApplicationsGrid.ItemsSource = _workspace.Applications;
        FoldersList.ItemsSource = null; FoldersList.ItemsSource = _workspace.Folders.OrderBy(item => item.Order);
        FilesList.ItemsSource = null; FilesList.ItemsSource = _workspace.Files.OrderBy(item => item.Order);
        UrlsList.ItemsSource = null; UrlsList.ItemsSource = _workspace.Urls.OrderBy(item => item.Order);
        PreActionsGrid.ItemsSource = null; PreActionsGrid.ItemsSource = _workspace.PreLaunchActions;
        PostActionsGrid.ItemsSource = null; PostActionsGrid.ItemsSource = _workspace.PostLaunchActions;
    }

    private void UpdateProjectName() => ProjectName.Text = _workspace.ProjectId is { } id ? _projects?.Find(id)?.Name ?? "Associated project unavailable" : "No associated project";
    private void ChooseProject_Click(object sender, RoutedEventArgs e)
    {
        if (_projects is null) return;
        var picker = new ProjectPickerWindow(_projects, _workspace.ProjectId) { Owner = this };
        if (picker.ShowDialog() == true) { _workspace.ProjectId = picker.SelectedProject?.Id; UpdateProjectName(); }
    }
    private void ClearProject_Click(object sender, RoutedEventArgs e) { _workspace.ProjectId = null; UpdateProjectName(); }

    private void ChooseApplication_Click(object sender, RoutedEventArgs e)
    {
        var picker = new ApplicationPickerWindow(_catalog) { Owner = this };
        if (picker.ShowDialog() == true && picker.SelectedApplication is { } app)
        {
            var entry = NewApplication(app.DisplayName, app.DisplayName);
            entry.CatalogApplicationId = app.Id;
            _workspace.Applications.Add(entry);
            RefreshBindings();
        }
    }

    private void BrowseApplication_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new Microsoft.Win32.OpenFileDialog { Title = "Choose application executable", Filter = "Applications (*.exe)|*.exe", CheckFileExists = true };
        if (dialog.ShowDialog(this) == true)
        {
            _workspace.Applications.Add(NewApplication(System.IO.Path.GetFileNameWithoutExtension(dialog.FileName), dialog.FileName));
            RefreshBindings();
        }
    }

    private WorkspaceApplication NewApplication(string name, string value) => new() { DisplayName = name, ApplicationIdentifier = value, LaunchOrder = _workspace.Applications.Count };
    private void RemoveApplication_Click(object sender, RoutedEventArgs e) { if (ApplicationsGrid.SelectedItem is WorkspaceApplication item) { _workspace.Applications.Remove(item); RefreshBindings(); } }

    private void AddFolder_Click(object sender, RoutedEventArgs e)
    {
        using var dialog = new System.Windows.Forms.FolderBrowserDialog { Description = "Choose a folder for this workspace", UseDescriptionForTitle = true };
        if (dialog.ShowDialog() == System.Windows.Forms.DialogResult.OK) { _workspace.Folders.Add(NewResource(dialog.SelectedPath, _workspace.Folders.Count)); RefreshBindings(); }
    }
    private void AddFile_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new Microsoft.Win32.OpenFileDialog { Title = "Choose a file", CheckFileExists = true, Filter = "All files (*.*)|*.*" };
        if (dialog.ShowDialog(this) == true) { _workspace.Files.Add(NewResource(dialog.FileName, _workspace.Files.Count)); RefreshBindings(); }
    }
    private void AddUrl_Click(object sender, RoutedEventArgs e)
    {
        var prompt = new TextPromptWindow("Add web page", "HTTP or HTTPS address", "https://") { Owner = this };
        if (prompt.ShowDialog() == true) { _workspace.Urls.Add(NewResource(prompt.Value, _workspace.Urls.Count)); RefreshBindings(); }
    }
    private static WorkspaceResource NewResource(string value, int order) => new() { DisplayName = System.IO.Path.GetFileName(value.TrimEnd(System.IO.Path.DirectorySeparatorChar)), Value = value, Order = order };
    private void RemoveFolder_Click(object sender, RoutedEventArgs e) { if (FoldersList.SelectedItem is WorkspaceResource item) { _workspace.Folders.Remove(item); RefreshBindings(); } }
    private void RemoveFile_Click(object sender, RoutedEventArgs e) { if (FilesList.SelectedItem is WorkspaceResource item) { _workspace.Files.Remove(item); RefreshBindings(); } }
    private void RemoveUrl_Click(object sender, RoutedEventArgs e) { if (UrlsList.SelectedItem is WorkspaceResource item) { _workspace.Urls.Remove(item); RefreshBindings(); } }

    private void AddPreAction_Click(object sender, RoutedEventArgs e) { _workspace.PreLaunchActions.Add(new() { Type = WorkspaceActionType.Minimize, Order = _workspace.PreLaunchActions.Count }); RefreshBindings(); }
    private void AddPostAction_Click(object sender, RoutedEventArgs e) { _workspace.PostLaunchActions.Add(new() { Type = WorkspaceActionType.Focus, Order = _workspace.PostLaunchActions.Count }); RefreshBindings(); }
    private void RemovePreAction_Click(object sender, RoutedEventArgs e) { if (PreActionsGrid.SelectedItem is WorkspaceSafeAction item) { _workspace.PreLaunchActions.Remove(item); RefreshBindings(); } }
    private void RemovePostAction_Click(object sender, RoutedEventArgs e) { if (PostActionsGrid.SelectedItem is WorkspaceSafeAction item) { _workspace.PostLaunchActions.Remove(item); RefreshBindings(); } }

    private async void Save_Click(object sender, RoutedEventArgs e)
    {
        ApplicationsGrid.CommitEdit(); PreActionsGrid.CommitEdit(); PostActionsGrid.CommitEdit();
        _workspace.Name = NameInput.Text;
        _workspace.Description = DescriptionInput.Text;
        _workspace.IconKey = IconInput.SelectedItem?.ToString() ?? "Briefcase";
        _workspace.FinalFocusApplication = string.IsNullOrWhiteSpace(FinalFocusInput.Text) ? null : FinalFocusInput.Text.Trim();
        _workspace.ContinueOnFailure = ContinueInput.IsChecked == true;
        _workspace.ShowDetailedProgress = ProgressInput.IsChecked == true;
        if (!int.TryParse(TimeoutInput.Text, out var timeout)) timeout = 0;
        _workspace.LaunchTimeoutSeconds = timeout;
        SaveButton.IsEnabled = false;
        try
        {
            var result = await _workspaces.SaveAsync(_workspace);
            if (!result.IsValid)
            {
                ValidationText.Text = string.Join("  ", result.Issues.Select(issue => issue.Message));
                return;
            }
            DialogResult = true;
        }
        finally { SaveButton.IsEnabled = true; }
    }

    private void Cancel_Click(object sender, RoutedEventArgs e) => Close();
}
