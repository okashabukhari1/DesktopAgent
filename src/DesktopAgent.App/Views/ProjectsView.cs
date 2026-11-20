using System.Windows;
using System.Windows.Controls;
using DesktopAgent.Core.Projects;
using DesktopAgent.Core.Interfaces;
using DesktopAgent.Core.Automation.Interfaces;
using DesktopAgent.Core.Applications.Interfaces;
using DesktopAgent.Core.Applications.Models;
using DesktopAgent.Core.Workspaces.Interfaces;
using Microsoft.Extensions.DependencyInjection;
using UserControl = System.Windows.Controls.UserControl;
using Button = System.Windows.Controls.Button;
using ComboBox = System.Windows.Controls.ComboBox;

namespace DesktopAgent.App.Views;

public sealed class ProjectsView : UserControl
{
    private readonly IProjectCatalog _catalog;
    private readonly IProjectOpenService _open;
    private readonly CurrentProjectContext _current;
    private readonly IWorkspaceService _workspaces;
    private readonly IShellService _shell;
    private readonly ICharacterService _character;
    private readonly ProjectPicker _picker;
    private readonly TextBlock _details = new() { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 8, 0, 12) };
    private readonly TextBlock _status = new() { TextWrapping = TextWrapping.Wrap };
    private readonly ComboBox _editor = new(), _terminal = new(), _workspace = new();
    private readonly StackPanel _right = new();
    private readonly ProjectFilesView _files;
    private bool _busy;
    private bool _actionFailed;
    public ProjectsView(IProjectCatalog catalog, IProjectOpenService open, CurrentProjectContext current,
        IWorkspaceService workspaces, IShellService shell, ICharacterService character, IApplicationCatalog applications,
        IProjectEditorLauncher editors, IServiceProvider services)
    {
        _catalog = catalog; _open = open; _current = current; _workspaces = workspaces; _shell = shell; _character = character;
        var grid = new Grid(); grid.RowDefinitions.Add(new() { Height = GridLength.Auto }); grid.RowDefinitions.Add(new()); grid.RowDefinitions.Add(new() { Height = GridLength.Auto });
        var header = new StackPanel(); header.Children.Add(new TextBlock { Text = "Projects", FontSize = 28, FontWeight = FontWeights.SemiBold });
        header.Children.Add(new TextBlock { Text = "Development folders, technologies, and preferred environments.", Margin = new Thickness(0, 5, 0, 12) });
        var actions = new WrapPanel(); header.Children.Add(actions); grid.Children.Add(header);
        var body = new Grid { Margin = new Thickness(0, 12, 0, 16) }; body.ColumnDefinitions.Add(new() { Width = new GridLength(1, GridUnitType.Star) }); body.ColumnDefinitions.Add(new() { Width = new GridLength(1.1, GridUnitType.Star) }); Grid.SetRow(body, 1); grid.Children.Add(body);
        _picker = new ProjectPicker(catalog); body.Children.Add(_picker);
        _files = ActivatorUtilities.CreateInstance<ProjectFilesView>(services);
        var tabs = new System.Windows.Controls.TabControl { Margin = new Thickness(22, 0, 0, 0) };
        tabs.Items.Add(new TabItem { Header = "Overview", Content = new ScrollViewer { Content = _right, VerticalScrollBarVisibility = ScrollBarVisibility.Auto } });
        tabs.Items.Add(new TabItem { Header = "Files", Content = new ScrollViewer { Content = _files, VerticalScrollBarVisibility = ScrollBarVisibility.Auto } });
        Grid.SetColumn(tabs, 1); body.Children.Add(tabs);
        _right.Children.Add(_details);
        void AddChoice(string label, ComboBox combo) { _right.Children.Add(new TextBlock { Text = label, Margin = new Thickness(0, 8, 0, 3) }); _right.Children.Add(combo); }
        AddChoice("Preferred editor", _editor); AddChoice("Preferred terminal (stored only)", _terminal); AddChoice("Associated workspace", _workspace);
        var appChoices = new[] { new InstalledApplication { Id = "", DisplayName = "Use defaults" } }.Concat(applications.Applications.Where(a => editors.CanOpen(a.Id)).OrderBy(a => a.DisplayName)).ToArray();
        _editor.ItemsSource = appChoices; _editor.DisplayMemberPath = "DisplayName"; _editor.SelectedValuePath = "Id";
        _terminal.ItemsSource = new[] { new InstalledApplication { Id = "", DisplayName = "Use defaults" } }.Concat(applications.Applications.OrderBy(a => a.DisplayName)).ToArray();
        _terminal.DisplayMemberPath = "DisplayName"; _terminal.SelectedValuePath = "Id";
        _workspace.DisplayMemberPath = "Name"; _workspace.SelectedValuePath = "Id";
        var projectActions = new WrapPanel { Margin = new Thickness(0, 14, 0, 0) }; _right.Children.Add(projectActions);
        void AddButton(WrapPanel panel, string label, Func<Task> run)
        {
            var b = new Button { Content = label }; b.Click += async (_, _) => await RunAsync(run); panel.Children.Add(b);
        }
        AddButton(actions, "Add Project Folder", () => { var window = new Window { Title = "Project Locations", Width = 650, Height = 680, Owner = Window.GetWindow(this), Content = services.GetRequiredService<ProjectSettingsView>(), WindowStartupLocation = WindowStartupLocation.CenterOwner };
            var root = new DockPanel(); var save = new Button { Content = "Save project locations", HorizontalAlignment = System.Windows.HorizontalAlignment.Right }; DockPanel.SetDock(save, Dock.Bottom); root.Children.Add(save);
            var settingsView = (ProjectSettingsView)window.Content; root.Children.Add(settingsView); window.Content = root;
            save.Click += async (_, _) => { if (await settingsView.TrySaveAsync()) window.Close(); }; window.ShowDialog(); return Task.CompletedTask; });
        AddButton(actions, "Add Existing Project", async () => { var path = ChooseFolder(); if (path is not null) { var p = await catalog.AddAsync(path); _picker.Refresh(); _picker.Select(p.Id); _status.Text = "Project added. Source files were not read."; } });
        AddButton(actions, "Clear current project", () => { current.Clear(); ShowSelection(); return Task.CompletedTask; });
        AddButton(projectActions, "Open", async () => { if (Selected() is { } p) ShowResult(await open.OpenAsync(p.Id.ToString())); });
        AddButton(projectActions, "Folder", () => { if (Selected() is { } p) _status.Text = shell.OpenFolder(p.Path).Message; return Task.CompletedTask; });
        AddButton(projectActions, "Editor only", async () => { if (Selected() is { } p) ShowResult(await open.OpenAsync(p.Id.ToString(), useWorkspace: false)); });
        AddButton(projectActions, "Workspace", async () => { if (Selected() is { } p) { var w = p.AssociatedWorkspaceId is { } id ? workspaces.Find(id.ToString()) : workspaces.Workspaces.FirstOrDefault(w => w.ProjectId == p.Id);
            if (w is null) { _status.Text = "No associated workspace is available."; _actionFailed = true; } else ShowResult(await open.OpenAsync(p.Id.ToString())); } });
        AddButton(projectActions, "Save preferences", async () => { if (Selected() is { } p) { p.PreferredEditorId = _editor.SelectedValue as string; p.PreferredTerminalId = _terminal.SelectedValue as string;
            p.AssociatedWorkspaceId = _workspace.SelectedValue is Guid id && id != Guid.Empty ? id : null; await catalog.UpdateAsync(p); if (current.Current?.ProjectId == p.Id) current.Set(p); _status.Text = "Preferences saved."; } });
        AddButton(projectActions, "Favorite", async () => { if (Selected() is { } p) { p.IsFavorite = !p.IsFavorite; await catalog.UpdateAsync(p); } });
        AddButton(projectActions, "Refresh metadata", async () => { if (Selected() is { } p) { await catalog.AddAsync(p.Path, p.Id); _status.Text = "Metadata refreshed."; } });
        AddButton(projectActions, "Locate Project", async () => { if (Selected() is { } p) { var path = ChooseFolder(); if (path is not null) { await catalog.AddAsync(path, p.Id); if (current.Current?.ProjectId == p.Id) current.Set(catalog.Find(p.Id)!); } } });
        AddButton(projectActions, "Remove from catalog", async () => { if (Selected() is { } p && System.Windows.MessageBox.Show("Remove " + p.Name + " from the agent catalog?\nProject files will NOT be deleted.", "Remove from catalog", MessageBoxButton.YesNo) == MessageBoxResult.Yes)
            { await catalog.RemoveAsync(p.Id); if (current.Current?.ProjectId == p.Id) current.Clear(); _status.Text = "Removed from catalog. Files were not deleted."; } });
        Grid.SetRow(_status, 2); grid.Children.Add(_status); Content = grid;
        _picker.SelectionChanged += (_, _) => ShowSelection();
        Loaded += (_, _) => { current.Changed += ContextChanged; catalog.Changed += ContextChanged; ShowSelection(); if (catalog.Projects.Count == 0) _status.Text = catalog.LoadWarning ?? "Add a folder where you keep your development projects, then Scan Now."; };
        Unloaded += (_, _) => { current.Changed -= ContextChanged; catalog.Changed -= ContextChanged; };
        Loaded += (_, _) => workspaces.WorkspacesChanged += ContextChanged;
        Unloaded += (_, _) => workspaces.WorkspacesChanged -= ContextChanged;
    }
    private ProjectProfile? Selected() => _picker.Selected;
    private void ShowResult(DesktopAgent.Core.Ai.ToolResult result) { _status.Text = result.Message; _actionFailed = !result.Success || result.Status == "Partial"; }
    private void ContextChanged(object? sender, EventArgs e) => Dispatcher.BeginInvoke(ShowSelection);
    private void ShowSelection()
    {
        _files.SelectProject(Selected()?.Id);
        _right.IsEnabled = Selected() is not null && !_busy;
        if (Selected() is not { } p) { _details.Text = "Select a project to view its details."; return; }
        _details.Text = p.Name + (_current.Current?.ProjectId == p.Id ? " · Current project" : "") + "\n\n" + p.Path + "\n" +
            (System.IO.Directory.Exists(p.Path) ? "Folder available" : "Folder unavailable — use Locate Project") + "\n\n" +
            (p.TechnologySummary.Length > 0 ? p.TechnologySummary : "Unknown technology") + "\n" + string.Join(" · ", p.Frameworks) +
            "\nDetected: " + string.Join(", ", p.DetectedFiles.Take(8)) + "\n\nGit: " + (p.IsGitRepository ? p.GitBranch ?? "Repository detected; branch unavailable" : "Not detected") +
            "\nLast agent opening: " + (p.LastOpenedAt?.ToLocalTime().ToString("g") ?? "Never") +
            (p.AssociatedWorkspaceId.HasValue && _workspaces.Find(p.AssociatedWorkspaceId.Value.ToString()) is null ? "\nAssociated workspace is unavailable." : "");
        _editor.SelectedValue = p.PreferredEditorId ?? ""; _terminal.SelectedValue = p.PreferredTerminalId ?? "";
        _workspace.ItemsSource = new[] { new DesktopAgent.Core.Workspaces.Models.WorkspaceDefinition { Id = Guid.Empty, Name = "No workspace" } }.Concat(_workspaces.Workspaces).ToArray();
        _workspace.SelectedValue = p.AssociatedWorkspaceId ?? Guid.Empty;
    }
    private static string? ChooseFolder()
    {
        using var dialog = new System.Windows.Forms.FolderBrowserDialog { Description = "Choose the existing project folder", UseDescriptionForTitle = true };
        return dialog.ShowDialog() == System.Windows.Forms.DialogResult.OK ? dialog.SelectedPath : null;
    }
    private async Task RunAsync(Func<Task> run)
    {
        if (_busy) return; _busy = true; _actionFailed = false; _right.IsEnabled = false; _character.SetState(Core.Enums.CharacterState.Working);
        try { await run(); _character.SetState(_actionFailed ? Core.Enums.CharacterState.Attention : Core.Enums.CharacterState.Idle); }
        catch (Exception e) { _status.Text = "Project action failed: " + e.Message; _character.SetState(Core.Enums.CharacterState.Attention); }
        finally { _busy = false; _right.IsEnabled = Selected() is not null; }
    }
}
