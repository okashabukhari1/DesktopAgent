using System.Windows;
using System.Windows.Controls;
using DesktopAgent.Core.Projects;
using DesktopAgent.Core.Interfaces;
using DesktopAgent.Core.Applications.Interfaces;
using DesktopAgent.Core.Applications.Models;
using DesktopAgent.Infrastructure.Projects;
using UserControl = System.Windows.Controls.UserControl;
using ComboBox = System.Windows.Controls.ComboBox;
using Button = System.Windows.Controls.Button;
using DataGrid = System.Windows.Controls.DataGrid;

namespace DesktopAgent.App.Views;

public sealed class ProjectSettingsView : UserControl
{
    private readonly ISettingsService _settings;
    private readonly ProjectSettings _working;
    private readonly DataGrid _locations = new() { AutoGenerateColumns = false, CanUserAddRows = false, MinHeight = 130, MaxHeight = 230 };
    private readonly TextBlock _status = new() { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 10, 0, 8) };
    private readonly Dictionary<string, ComboBox> _defaults = [];
    private readonly IProjectCatalog _catalog;
    private CancellationTokenSource? _scan;
    public ProjectSettingsView(ISettingsService settings, IApplicationCatalog applications, IProjectEditorLauncher editors, IProjectCatalog catalog)
    {
        _settings = settings; _working = settings.Current.Projects.Copy(); _catalog = catalog;
        var panel = new StackPanel { Margin = new Thickness(14) };
        panel.Children.Add(new TextBlock { Text = "Project Locations", FontSize = 22, FontWeight = FontWeights.SemiBold });
        panel.Children.Add(new TextBlock { Text = "Only these folders are scanned. No automatic whole-drive search. Settings here save when you choose Save, or Scan Now.", TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 8, 0, 12) });
        _locations.Columns.Add(new DataGridTextColumn { Header = "Folder", Binding = new System.Windows.Data.Binding("Path") { Mode = System.Windows.Data.BindingMode.OneWay }, IsReadOnly = true, Width = new DataGridLength(1, DataGridLengthUnitType.Star) });
        _locations.Columns.Add(new DataGridCheckBoxColumn { Header = "Subfolders", Binding = new System.Windows.Data.Binding("IncludeSubfolders") });
        _locations.Columns.Add(new DataGridTextColumn { Header = "Depth 0–8", Binding = new System.Windows.Data.Binding("MaxDepth") });
        panel.Children.Add(_locations); RefreshLocations();
        var actions = new WrapPanel(); panel.Children.Add(actions);
        void Button(string title, Action action) { var b = new Button { Content = title }; b.Click += (_, _) => action(); actions.Children.Add(b); }
        Button("Add Folder", () =>
        {
            using var dialog = new System.Windows.Forms.FolderBrowserDialog { Description = "Choose a project location (not a drive root)", UseDescriptionForTitle = true };
            if (dialog.ShowDialog() != System.Windows.Forms.DialogResult.OK) return;
            try { var path = ProjectDiscoveryService.ValidatePath(dialog.SelectedPath); if (!_working.Locations.Any(l => l.Path.Equals(path, StringComparison.OrdinalIgnoreCase))) _working.Locations.Add(new() { Path = path }); RefreshLocations(); }
            catch (ArgumentException e) { _status.Text = e.Message; }
        });
        Button("Remove", () => { if (_locations.SelectedItem is ProjectLocation location) { _working.Locations.Remove(location); RefreshLocations(); } });
        Button("Scan Now", async () => await ScanAsync());
        Button("Cancel Scan", () => _scan?.Cancel());
        panel.Children.Add(_status);
        panel.Children.Add(new TextBlock { Text = "Development defaults", FontSize = 19, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 16, 0, 8) });
        var appList = applications.Applications.OrderBy(a => a.DisplayName).ToArray();
        AddDefault(".NET editor", _working.DotNetEditorId, appList.Where(a => editors.CanOpen(a.Id)).ToArray());
        AddDefault("Web editor", _working.WebEditorId, appList.Where(a => editors.CanOpen(a.Id)).ToArray());
        AddDefault("Android editor", _working.AndroidEditorId, appList.Where(a => editors.CanOpen(a.Id)).ToArray());
        AddDefault("Terminal preference", _working.TerminalId, appList);
        panel.Children.Add(new TextBlock { Text = "Terminal preferences are stored only. Terminal commands and project variables are not enabled in this phase.", TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 10, 0, 0) });
        Content = new ScrollViewer { Content = panel, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        void AddDefault(string title, string? selected, InstalledApplication[] entries)
        {
            panel.Children.Add(new TextBlock { Text = title, Margin = new Thickness(0, 9, 0, 3) });
            var choices = new[] { new InstalledApplication { Id = "", DisplayName = "Automatic / none" } }.Concat(entries).ToArray();
            var combo = new ComboBox { ItemsSource = choices, DisplayMemberPath = "DisplayName", SelectedValuePath = "Id", SelectedValue = selected ?? "" };
            _defaults[title] = combo; panel.Children.Add(combo);
        }
        Unloaded += (_, _) => _scan?.Cancel();
    }
    private void RefreshLocations() { _locations.ItemsSource = null; _locations.ItemsSource = _working.Locations; }
    public async Task<bool> TrySaveAsync()
    {
        try
        {
            _locations.CommitEdit(); _locations.CommitEdit(DataGridEditingUnit.Row, true);
            foreach (var location in _working.Locations)
            { location.Path = ProjectDiscoveryService.ValidatePath(location.Path); if (location.MaxDepth is < 0 or > 8) throw new ArgumentException("Maximum depth must be 0–8."); }
            if (_working.Locations.Count > 32) throw new ArgumentException("Choose up to 32 project locations.");
            string? Id(string label) => _defaults[label].SelectedValue is string { Length: > 0 } value ? value : null;
            _working.DotNetEditorId = Id(".NET editor"); _working.WebEditorId = Id("Web editor"); _working.AndroidEditorId = Id("Android editor"); _working.TerminalId = Id("Terminal preference");
            var saved = _settings.Current.Clone(); saved.Projects = _working.Copy(); await _settings.SaveAsync(saved);
            _status.Text = "Project settings saved."; return true;
        }
        catch (Exception e) when (e is ArgumentException or System.IO.IOException or UnauthorizedAccessException) { _status.Text = e.Message; return false; }
    }
    private async Task ScanAsync()
    {
        if (_scan is not null || _catalog.IsScanning) { _status.Text = "A scan is already running."; return; }
        if (!await TrySaveAsync()) return;
        using var source = new CancellationTokenSource(); _scan = source;
        try
        {
            _status.Text = "Scanning configured locations…";
            var result = await _catalog.RefreshAsync(source.Token);
            _status.Text = result.Projects.Count + " projects detected." + (result.LimitReached ? " Scan limit reached; narrow locations." : "") +
                (result.Warnings.Count > 0 ? "\n" + string.Join("\n", result.Warnings.Take(3)) : "");
        }
        catch (OperationCanceledException) { _status.Text = "Scan cancelled. Previous catalog retained."; }
        catch (Exception e) { _status.Text = "Scan failed: " + e.Message; }
        finally { _scan = null; }
    }
}
