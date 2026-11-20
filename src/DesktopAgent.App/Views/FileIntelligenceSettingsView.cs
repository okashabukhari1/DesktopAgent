using System.Windows;
using System.Windows.Controls;
using DesktopAgent.Core.Interfaces;
using UserControl = System.Windows.Controls.UserControl;
using TextBox = System.Windows.Controls.TextBox;

namespace DesktopAgent.App.Views;

public sealed class FileIntelligenceSettingsView : UserControl
{
    private readonly ISettingsService settings;
    private readonly Dictionary<string, TextBox> inputs = new();
    private readonly TextBox exclusions = new() { MinHeight = 55, AcceptsReturn = true, TextWrapping = TextWrapping.Wrap };
    private readonly TextBlock status = new() { TextWrapping = TextWrapping.Wrap };
    public FileIntelligenceSettingsView(ISettingsService settings)
    {
        this.settings = settings; var limits = settings.Current.FileIntelligence;
        var panel = new StackPanel { Margin = new Thickness(16) }; Content = new ScrollViewer { Content = panel, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        panel.Children.Add(new TextBlock { Text = "File intelligence · registered projects only", FontSize = 21 });
        panel.Children.Add(new TextBlock { Text = "Source is locally redacted. Built-in security exclusions cannot be disabled. Extra exclusions are directory names, one per line.", TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 8, 0, 8) });
        foreach (var p in typeof(Core.Projects.FileIntelligenceSettings).GetProperties().Where(p => p.PropertyType == typeof(int)))
        { panel.Children.Add(new TextBlock { Text = p.Name }); var input = new TextBox { Text = p.GetValue(limits)!.ToString(), MaxLength = 9, Margin = new Thickness(0, 0, 0, 7) }; inputs[p.Name] = input; panel.Children.Add(input); }
        panel.Children.Add(new TextBlock { Text = "Additional excluded directories" }); exclusions.Text = string.Join("\n", limits.AdditionalExcludedDirectories); panel.Children.Add(exclusions); panel.Children.Add(status);
    }
    public async Task<bool> TrySaveAsync()
    {
        var updated = settings.Current.Clone(); var limits = updated.FileIntelligence;
        foreach (var (name, input) in inputs)
        { if (!int.TryParse(input.Text, out var number) || number < 1) { status.Text = "All limits must be positive integers."; return false; } typeof(Core.Projects.FileIntelligenceSettings).GetProperty(name)!.SetValue(limits, number); }
        limits.AdditionalExcludedDirectories = exclusions.Text.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList();
        if (limits.AdditionalExcludedDirectories.Any(s => s.IndexOfAny(['/', '\\', ':']) >= 0 || s.Any(char.IsControl) || s.Length > 80)) { status.Text = "Use simple directory names, not paths or patterns."; return false; }
        limits.Sanitize();
        try { await settings.SaveAsync(updated); status.Text = "Saved (limits clamped to safe ranges)."; return true; }
        catch (Exception e) when (e is System.IO.IOException or UnauthorizedAccessException) { status.Text = "Settings could not be saved. Check file access and try again."; return false; }
    }
}
