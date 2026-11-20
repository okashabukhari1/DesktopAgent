using System.Drawing;
using System.IO;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media.Imaging;
using DesktopAgent.Core.Applications.Interfaces;
using DesktopAgent.Core.Applications.Models;
using DesktopAgent.Core.Automation.Enums;
using DesktopAgent.Core.Automation.Interfaces;
using DesktopAgent.Core.Automation.Models;
using WpfButton = System.Windows.Controls.Button;
using WpfContextMenu = System.Windows.Controls.ContextMenu;
using WpfMenuItem = System.Windows.Controls.MenuItem;

namespace DesktopAgent.App.Views;

public partial class ApplicationsView : System.Windows.Controls.UserControl
{
    private readonly IApplicationCatalog _catalog;
    private readonly IApplicationControlService _runningApps;
    private readonly IComputerCommandService _commands;
    private readonly Dictionary<string, BitmapSource?> _iconCache = new(StringComparer.OrdinalIgnoreCase);

    public ApplicationsView(IApplicationCatalog catalog, IApplicationControlService runningApps, IComputerCommandService commands)
    {
        InitializeComponent(); _catalog = catalog; _runningApps = runningApps; _commands = commands;
        Loaded += (_, _) => { _catalog.CatalogChanged -= OnCatalogChanged; _catalog.RefreshProgress -= OnProgress; _catalog.CatalogChanged += OnCatalogChanged; _catalog.RefreshProgress += OnProgress; RefreshList(); };
        Unloaded += (_, _) => { _catalog.CatalogChanged -= OnCatalogChanged; _catalog.RefreshProgress -= OnProgress; };
    }

    private void RefreshList()
    {
        var running = _runningApps.GetRunningApplications().Select(item => item.ProcessName).ToHashSet(StringComparer.OrdinalIgnoreCase);
        IEnumerable<InstalledApplication> applications = string.IsNullOrWhiteSpace(SearchInput.Text)
            ? _catalog.Applications.Where(item => item.IsLaunchable && !item.IsSystemApplication).OrderByDescending(item => item.IsFavorite).ThenByDescending(item => item.LastUsed).ThenBy(item => item.DisplayName)
            : _catalog.SearchApplications(SearchInput.Text, false, 100).Select(match => match.Application);
        var cards = applications.Select(app => new ApplicationCard(app, IsRunning(app, running), LoadIcon(app.IconPath))).ToList();
        ApplicationItems.ItemsSource = cards;
        SectionTitle.Text = string.IsNullOrWhiteSpace(SearchInput.Text) ? $"Installed · {cards.Count}" : $"Search results · {cards.Count}";
        CatalogStatus.Text = _catalog.LastRefresh is null ? "Catalog has not been refreshed" : $"Last refreshed {_catalog.LastRefresh:MMM d, h:mm tt}";
    }

    private async void Refresh_Click(object sender, RoutedEventArgs e)
    {
        RefreshButton.IsEnabled = false; CatalogStatus.Text = "Refreshing applications…";
        try { var result = await _commands.ExecuteAsync("refresh apps", source: "UI"); CatalogStatus.Text = result.Message; }
        catch (Exception exception) { CatalogStatus.Text = $"Application refresh failed: {exception.Message}"; }
        finally { RefreshButton.IsEnabled = true; RefreshList(); }
    }
    private void SearchInput_TextChanged(object sender, System.Windows.Controls.TextChangedEventArgs e) => RefreshList();
    private async void Primary_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not WpfButton { Tag: ApplicationCard card }) return;
        var result = await _commands.ExecuteAsync($"{(card.IsRunning ? "focus" : "open")} {card.Id}", source: "UI");
        CatalogStatus.Text = result.Message;
        RefreshList();
    }
    private async void Favorite_Click(object sender, RoutedEventArgs e) { if (sender is WpfButton { Tag: ApplicationCard card }) await _catalog.ToggleFavoriteAsync(card.Id); }
    private void More_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not WpfButton { Tag: ApplicationCard card } button) return;
        var menu = new WpfContextMenu();
        Add(menu, "Focus", ComputerCommandType.FocusApplication, card);
        Add(menu, "Minimize", ComputerCommandType.MinimizeWindow, card);
        Add(menu, "Maximize", ComputerCommandType.MaximizeWindow, card);
        Add(menu, "Restore", ComputerCommandType.RestoreWindow, card);
        Add(menu, "Close", ComputerCommandType.CloseApplication, card);
        button.ContextMenu = menu; menu.IsOpen = true;
    }
    private void Add(WpfContextMenu menu, string label, ComputerCommandType type, ApplicationCard card)
    {
        var item = new WpfMenuItem { Header = label };
        item.Click += async (_, _) =>
        {
            var verb = type switch
            {
                ComputerCommandType.FocusApplication => "focus",
                ComputerCommandType.MinimizeWindow => "minimize",
                ComputerCommandType.MaximizeWindow => "maximize",
                ComputerCommandType.RestoreWindow => "restore",
                ComputerCommandType.CloseApplication => "close",
                _ => string.Empty
            };
            if (verb.Length == 0) return;
            var result = await _commands.ExecuteAsync($"{verb} {card.Id}", source: "UI");
            CatalogStatus.Text = result.Message;
            RefreshList();
        };
        menu.Items.Add(item);
    }
    private void OnCatalogChanged(object? sender, EventArgs e) => Dispatcher.Invoke(RefreshList);
    private void OnProgress(object? sender, ApplicationDiscoveryProgress progress) => Dispatcher.Invoke(() => CatalogStatus.Text = $"{progress.Message} · Found {progress.FoundApplications}");
    private static bool IsRunning(InstalledApplication app, HashSet<string> running) => app.ProcessName is not null && running.Contains(app.ProcessName);
    private BitmapSource? LoadIcon(string? path)
    {
        if (path is not { Length: > 0 } iconPath || !File.Exists(iconPath)) return null;
        if (_iconCache.TryGetValue(iconPath, out var cached)) return cached;
        try { using var icon = Icon.ExtractAssociatedIcon(iconPath); if (icon is null) return _iconCache[iconPath] = null; var source = Imaging.CreateBitmapSourceFromHIcon(icon.Handle, Int32Rect.Empty, BitmapSizeOptions.FromWidthAndHeight(32, 32)); source.Freeze(); return _iconCache[iconPath] = source; }
        catch (Exception) { return _iconCache[iconPath] = null; }
    }

    private sealed class ApplicationCard
    {
        public ApplicationCard(InstalledApplication app, bool running, BitmapSource? icon) { Id = app.Id; DisplayName = app.DisplayName; Secondary = $"{app.ApplicationType} · {string.Join(", ", app.Sources.Take(2))}"; ProcessTarget = app.ProcessName ?? app.DisplayName; IsRunning = running; Icon = icon; FavoriteGlyph = app.IsFavorite ? "★" : "☆"; }
        public string Id { get; } public string DisplayName { get; } public string Secondary { get; } public string ProcessTarget { get; } public bool IsRunning { get; } public BitmapSource? Icon { get; } public string FavoriteGlyph { get; }
        public string PrimaryAction => IsRunning ? "Focus" : "Open";
        public Visibility RunningVisibility => IsRunning ? Visibility.Visible : Visibility.Collapsed;
        public Visibility FallbackVisibility => Icon is null ? Visibility.Visible : Visibility.Collapsed;
    }
}
