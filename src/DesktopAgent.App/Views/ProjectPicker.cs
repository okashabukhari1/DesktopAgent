using System.Windows;
using System.Windows.Controls;
using DesktopAgent.Core.Projects;
using UserControl = System.Windows.Controls.UserControl;
using TextBox = System.Windows.Controls.TextBox;
using ListBox = System.Windows.Controls.ListBox;

namespace DesktopAgent.App.Views;

/// <summary>Shared searchable catalog picker for project navigation, workspace association, and quick actions.</summary>
public sealed class ProjectPicker : UserControl
{
    private readonly IProjectCatalog _catalog;
    private readonly TextBox _search = new() { Margin = new Thickness(0, 0, 0, 10), ToolTip = "Search projects by name, path, or technology" };
    private readonly ListBox _list = new() { BorderThickness = new Thickness(0), Background = System.Windows.Media.Brushes.Transparent };
    public event EventHandler? SelectionChanged;
    public ProjectProfile? Selected => (_list.SelectedItem as Item)?.Project;
    public string Query => _search.Text;
    public ProjectPicker(IProjectCatalog catalog)
    {
        _catalog = catalog;
        var grid = new Grid(); grid.RowDefinitions.Add(new() { Height = GridLength.Auto }); grid.RowDefinitions.Add(new());
        grid.Children.Add(_search); Grid.SetRow(_list, 1); grid.Children.Add(_list); Content = grid;
        _list.DisplayMemberPath = nameof(Item.Label);
        _search.TextChanged += (_, _) => Refresh(); _list.SelectionChanged += (_, _) => SelectionChanged?.Invoke(this, EventArgs.Empty);
        Loaded += (_, _) => { _catalog.Changed += Changed; Refresh(); };
        Unloaded += (_, _) => _catalog.Changed -= Changed;
        System.Windows.Automation.AutomationProperties.SetName(_search, "Search projects");
        System.Windows.Automation.AutomationProperties.SetName(_list, "Projects");
    }
    private void Changed(object? sender, EventArgs e) => Dispatcher.BeginInvoke(Refresh);
    public void Refresh()
    {
        var selected = Selected?.Id;
        _list.ItemsSource = _catalog.Search(_search.Text, limit: 100).Select(m => new Item(m.Project)).ToArray();
        if (selected.HasValue) _list.SelectedItem = _list.Items.Cast<Item>().FirstOrDefault(i => i.Project.Id == selected);
    }
    public void Select(Guid? id) => _list.SelectedItem = _list.Items.Cast<Item>().FirstOrDefault(i => i.Project.Id == id);
    private sealed record Item(ProjectProfile Project)
    {
        public string Label => (Project.IsFavorite ? "★ " : "") + Project.Name + "\n" +
            (Project.TechnologySummary.Length > 0 ? Project.TechnologySummary : "Unknown technology") + "\n" + Project.Path +
            (Project.IsAvailable ? "" : " · cached / unavailable");
    }
}

public sealed class ProjectPickerWindow : Window
{
    public ProjectProfile? SelectedProject { get; private set; }
    public ProjectPickerWindow(IProjectCatalog catalog, Guid? selectedId = null)
    {
        Title = "Choose a project"; Width = 580; Height = 500; WindowStartupLocation = WindowStartupLocation.CenterOwner;
        var picker = new ProjectPicker(catalog);
        var grid = new Grid { Margin = new Thickness(22) };
        grid.RowDefinitions.Add(new()); grid.RowDefinitions.Add(new() { Height = GridLength.Auto });
        grid.Children.Add(picker);
        var buttons = new StackPanel { Orientation = System.Windows.Controls.Orientation.Horizontal, HorizontalAlignment = System.Windows.HorizontalAlignment.Right };
        var choose = new System.Windows.Controls.Button { Content = "Choose", IsEnabled = false };
        var cancel = new System.Windows.Controls.Button { Content = "Cancel", IsCancel = true };
        choose.Click += (_, _) => { SelectedProject = picker.Selected; DialogResult = SelectedProject is not null; };
        picker.SelectionChanged += (_, _) => choose.IsEnabled = picker.Selected is not null;
        buttons.Children.Add(cancel); buttons.Children.Add(choose); Grid.SetRow(buttons, 1); grid.Children.Add(buttons); Content = grid;
        Loaded += (_, _) => picker.Select(selectedId);
    }
}
