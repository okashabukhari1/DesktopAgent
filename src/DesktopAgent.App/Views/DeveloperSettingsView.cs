using System.Windows;
using System.Windows.Controls;
using DesktopAgent.Core.Developer;
using DesktopAgent.Core.Interfaces;
using DesktopAgent.Infrastructure.Developer;
using Button = System.Windows.Controls.Button;
using ComboBox = System.Windows.Controls.ComboBox;
using CheckBox = System.Windows.Controls.CheckBox;
using UserControl = System.Windows.Controls.UserControl;

namespace DesktopAgent.App.Views;

public sealed class DeveloperSettingsView : UserControl
{
    private readonly ISettingsService _settings;
    private readonly ClaudeHookInstaller _installer;
    private readonly IDeveloperToolAdapter[] _adapters;
    private readonly CheckBox _enabled = new() { Content = "Enable developer supervision (opt-in)" };
    private readonly CheckBox _notify = new() { Content = "Show selective companion notifications" };
    private readonly ComboBox _retention = new() { ItemsSource = new[] { 7, 30, 90 } };
    private readonly StackPanel _statuses = new();
    private readonly TextBlock _result = new() { TextWrapping = TextWrapping.Wrap };
    private readonly List<Button> _buttons = [];
    public DeveloperSettingsView(ISettingsService settings, ClaudeHookInstaller installer, IEnumerable<IDeveloperToolAdapter> adapters)
    {
        _settings = settings; _installer = installer; _adapters = adapters.ToArray();
        var panel = new StackPanel { Margin = new Thickness(14) };
        panel.Children.Add(new TextBlock { Text = "Developer Tools · Experimental / unreliable", FontSize = 22 });
        panel.Children.Add(new TextBlock { Text = "Disabled by default. Enable/disable requires an app restart. Part 6 does not repair these integrations; unavailable status is not proof of a connected agent.", TextWrapping = TextWrapping.Wrap });
        _enabled.IsChecked = settings.Current.Developer.Enabled; _notify.IsChecked = settings.Current.Developer.Notifications;
        _retention.SelectedItem = settings.Current.Developer.RetentionDays;
        panel.Children.Add(_enabled); panel.Children.Add(_notify);
        panel.Children.Add(new TextBlock { Text = "Activity retention (days)", Margin = new Thickness(0, 8, 0, 0) }); panel.Children.Add(_retention);
        panel.Children.Add(_statuses);
        AddButton(panel, "Refresh integration status", async () => await RefreshAsync());
        AddButton(panel, "Preview Install / Repair Claude hooks", () => PreviewAsync(false));
        AddButton(panel, "Preview Remove Claude hooks", () => PreviewAsync(true));
        AddButton(panel, "Preview Install OpenCode v1 event plugin", () => OpenCodeAsync(false));
        AddButton(panel, "Preview Remove OpenCode v1 event plugin", () => OpenCodeAsync(true));
        panel.Children.Add(new TextBlock { Text = "No live Claude settings change until you confirm the preview. Existing hooks are preserved. Backup files can contain your original sensitive settings: keep them private. Save enables observation. Unsupported tools remain detection-only.", TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 12, 0, 12) });
        panel.Children.Add(_result); Content = new ScrollViewer { Content = panel, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        Loaded += async (_, _) => await RefreshAsync();
    }
    private void AddButton(StackPanel panel, string text, Func<Task> action)
    {
        var button = new Button { Content = text, HorizontalAlignment = System.Windows.HorizontalAlignment.Left };
        _buttons.Add(button); panel.Children.Add(button);
        button.Click += async (_, _) => { foreach (var b in _buttons) b.IsEnabled = false; try { await action(); } catch { _result.Text = "Integration operation failed safely. Check configuration permissions, valid JSON and relay location. No automatic retry."; } finally { foreach (var b in _buttons) b.IsEnabled = true; } };
    }
    private async Task RefreshAsync()
    {
        _statuses.Children.Clear();
        if (!_settings.Current.Developer.Enabled) { _statuses.Children.Add(new TextBlock { Text = "Developer Integrations: OFF · Unavailable until explicitly enabled and restarted" }); return; }
        foreach (var adapter in _adapters)
        {
            try { var s = await adapter.GetStatusAsync(CancellationToken.None); _statuses.Children.Add(new TextBlock { Text = s.DisplayName + " · " + s.State + "\n" + s.Message, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 8, 0, 8) }); }
            catch { _statuses.Children.Add(new TextBlock { Text = adapter.DisplayName + " · Status unavailable" }); }
        }
    }
    private async Task PreviewAsync(bool remove)
    {
        var p = await _installer.PreviewAsync(remove);
        _result.Text = p.Changes;
        if (!p.Changed) { _result.Text = "Already up to date. No write or backup needed."; return; }
        var preview = new Window { Title = p.Operation + " · Confirm hook changes", Width = 650, Height = 540, Owner = Window.GetWindow(this), WindowStartupLocation = WindowStartupLocation.CenterOwner };
        var content = new DockPanel { Margin = new Thickness(16) };
        var confirm = new Button { Content = "Confirm " + p.Operation };
        DockPanel.SetDock(confirm, Dock.Bottom); content.Children.Add(confirm);
        var description = new TextBlock { Text = "File: " + p.ConfigPath + "\nHandlers: " + p.ExistingHandlers + " → " + p.ResultHandlers + "\n\n" + p.Changes + "\n\nOnly exact Desktop Agent handlers change. A timestamped backup is created first. Restart/resume Claude after installation. Closing this preview cancels.", TextWrapping = TextWrapping.Wrap };
        content.Children.Add(new ScrollViewer { Content = description, VerticalScrollBarVisibility = ScrollBarVisibility.Auto }); preview.Content = content;
        confirm.Click += (_, _) => { preview.DialogResult = true; preview.Close(); };
        if (preview.ShowDialog() != true) return;
        var backup = await _installer.ApplyConfirmedAsync(p.Id);
        _result.Text = backup is null ? "No changes needed." : "Integration changed. Backup: " + backup;
        await RefreshAsync();
    }
    private async Task OpenCodeAsync(bool remove)
    {
        var adapter = _adapters.OfType<OpenCodeAdapter>().Single();
        _result.Text = "Target: " + adapter.PluginPath + "\n" + (remove ? "Remove only the exact Desktop Agent plugin, backing it up first." : adapter.PluginSource);
        // User sees the complete bounded script/path before explicitly accepting this local plugin write.
        if (System.Windows.MessageBox.Show(Window.GetWindow(this), "The plugin preview is shown in this panel. " +
            (remove ? "Remove the exact Desktop Agent OpenCode plugin?" : "Install this read-only OpenCode v1 session-event plugin? Existing differing content will never be overwritten."),
            "Confirm OpenCode integration", MessageBoxButton.YesNo, MessageBoxImage.Question, MessageBoxResult.No) != MessageBoxResult.Yes) return;
        if (remove) await adapter.RemoveConfirmedAsync(); else await adapter.InstallConfirmedAsync();
        _result.Text = "OpenCode plugin updated. Restart OpenCode; only the documented v1 plugin event schema is supported.";
        await RefreshAsync();
    }
    public async Task<bool> TrySaveAsync()
    {
        try
        {
            var copy = _settings.Current.Clone(); copy.Developer.Enabled = _enabled.IsChecked == true;
            copy.Developer.Notifications = _notify.IsChecked == true; copy.Developer.RetentionDays = (int)(_retention.SelectedItem ?? 30);
            await _settings.SaveAsync(copy); return true;
        }
        catch { _result.Text = "Could not save developer settings."; return false; }
    }
}
