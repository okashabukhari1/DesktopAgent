using System.Windows;
using System.Windows.Media;
using DesktopAgent.App.Views;
using DesktopAgent.Core.Enums;
using DesktopAgent.Core.Interfaces;
using DesktopAgent.Core.Models;
using DesktopAgent.Core.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using MediaColor = System.Windows.Media.Color;
using MediaColorConverter = System.Windows.Media.ColorConverter;

namespace DesktopAgent.App.Services;

public sealed class WindowService : IWindowService
{
    private readonly IServiceProvider _services;
    private readonly ISettingsService _settings;
    private readonly IMonitorService _monitors;
    private readonly ICharacterService _character;
    private readonly ILogger<WindowService> _logger;
    private CharacterWindow? _characterWindow;
    private InteractionPanelWindow? _panel;
    private NotificationBubbleWindow? _bubble;
    private SettingsWindow? _settingsWindow;
    private DesktopAppWindow? _desktopAppWindow;
    private bool _shuttingDown;

    public WindowService(IServiceProvider services, ISettingsService settings, IMonitorService monitors,
        ICharacterService character, ILogger<WindowService> logger)
    {
        _services = services;
        _settings = settings;
        _monitors = monitors;
        _character = character;
        _logger = logger;
        _character.MessageRequested += OnMessageRequested;
        _character.StateChanged += (_, state) => _logger.LogInformation("CharacterStateChanged State={State}", state);
    }

    public void ShowCharacter()
    {
        EnsureCharacter();
        _characterWindow!.Show();
        _characterWindow.WindowState = WindowState.Normal;
        _characterWindow.ApplySettings();
        _characterWindow.RestorePosition();
        _characterWindow.Activate();
        _logger.LogInformation("CharacterShown Left={Left} Top={Top} Width={Width} Height={Height} Visible={Visible}",
            _characterWindow.Left, _characterWindow.Top, _characterWindow.ActualWidth,
            _characterWindow.ActualHeight, _characterWindow.IsVisible);
    }

    public void HideCharacter()
    {
        _panel?.Close();
        _bubble?.Close();
        _characterWindow?.Hide();
        _logger.LogInformation("CharacterHidden");
    }

    public void TogglePanel()
    {
        EnsureCharacter();
        if (_panel is { IsVisible: true }) { _panel.Close(); return; }
        _panel = _services.GetRequiredService<InteractionPanelWindow>();
        _panel.Closed += (_, _) => _panel = null;
        PositionRelative(_panel, 14);
        _panel.Topmost = _settings.Current.AlwaysOnTop;
        _panel.Show();
    }

    public void ShowSettings()
    {
        if (_settingsWindow is { IsVisible: true }) { _settingsWindow.Activate(); return; }
        _settingsWindow = _services.GetRequiredService<SettingsWindow>();
        _settingsWindow.Closed += (_, _) =>
        {
            _settingsWindow = null;
            ApplyTheme();
            _characterWindow?.ApplySettings();
        };
        _settingsWindow.Show();
        _settingsWindow.Activate();
    }

    public void ShowDesktopApp()
    {
        if (_desktopAppWindow is { IsVisible: true })
        {
            if (_desktopAppWindow.WindowState == WindowState.Minimized)
                _desktopAppWindow.WindowState = WindowState.Normal;
            _desktopAppWindow.Activate();
            return;
        }

        _desktopAppWindow = _services.GetRequiredService<DesktopAppWindow>();
        _desktopAppWindow.Closed += (_, _) => _desktopAppWindow = null;
        _desktopAppWindow.Show();
        _desktopAppWindow.Activate();
        _logger.LogInformation("DesktopAppOpened");
    }

    public void ShowWorkspaces()
    {
        ShowDesktopApp();
        _desktopAppWindow?.NavigateWorkspaces();
    }

    public void ShowDeveloper(string? sessionId = null)
    {
        ShowDesktopApp();
        _desktopAppWindow?.NavigateDeveloper(sessionId);
    }

    public void ShowProjects()
    {
        ShowDesktopApp();
        _desktopAppWindow?.NavigateProjects();
    }

    public void ShowApplications()
    {
        ShowDesktopApp();
        _desktopAppWindow?.NavigateApplications();
    }

    public void ShowAssistant()
    {
        ShowDesktopApp();
        _desktopAppWindow?.NavigateAssistant();
    }

    public void ShowPermissions()
    {
        ShowDesktopApp();
        _desktopAppWindow?.NavigatePermissions();
    }

    public void ShowActivity()
    {
        ShowDesktopApp();
        _desktopAppWindow?.NavigateActivity();
    }

    public void ResetPosition()
    {
        _settings.Current.Position = null;
        EnsureCharacter();
        _characterWindow!.RestorePosition();
        _ = SaveSafelyAsync();
    }

    public void Shutdown()
    {
        if (_shuttingDown) return;
        _shuttingDown = true;
        _logger.LogInformation("ApplicationExited");
        System.Windows.Application.Current.Shutdown();
    }

    public void ShowMessage(string message, bool autoClose = true)
    {
        EnsureCharacter();
        _bubble?.Close();
        _bubble = _services.GetRequiredService<NotificationBubbleWindow>();
        _bubble.SetMessage(message, autoClose);
        _bubble.Closed += (_, _) => _bubble = null;
        PositionRelative(_bubble, 10);
        _bubble.Topmost = _settings.Current.AlwaysOnTop;
        _bubble.Show();
    }

    public void ApplyTheme()
    {
        var theme = _settings.Current.Theme;
        if (theme == AppTheme.System)
        {
            try
            {
                using var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
                theme = Convert.ToInt32(key?.GetValue("AppsUseLightTheme", 1)) == 0 ? AppTheme.Dark : AppTheme.Light;
            }
            catch { theme = AppTheme.Light; }
        }
        var resources = System.Windows.Application.Current.Resources;
        resources["SurfaceColor"] = theme == AppTheme.Dark ? (MediaColor)MediaColorConverter.ConvertFromString("#171A23") : (MediaColor)MediaColorConverter.ConvertFromString("#F4F6FA");
        resources["CardColor"] = theme == AppTheme.Dark ? (MediaColor)MediaColorConverter.ConvertFromString("#242936") : Colors.White;
        resources["TextColor"] = theme == AppTheme.Dark ? (MediaColor)MediaColorConverter.ConvertFromString("#F4F6FB") : (MediaColor)MediaColorConverter.ConvertFromString("#182033");
        resources["MutedColor"] = theme == AppTheme.Dark ? (MediaColor)MediaColorConverter.ConvertFromString("#B0B8CA") : (MediaColor)MediaColorConverter.ConvertFromString("#667085");
    }

    private void EnsureCharacter()
    {
        if (_characterWindow is not null) return;
        _characterWindow = _services.GetRequiredService<CharacterWindow>();
        _characterWindow.Closing += (_, e) => { if (!_shuttingDown) { e.Cancel = true; HideCharacter(); } };
    }

    private void PositionRelative(Window window, double gap)
    {
        if (_characterWindow is null) return;
        var monitors = _monitors.GetWorkAreas();
        var monitor = monitors.FirstOrDefault(m => m.Contains(
            _characterWindow.Left + _characterWindow.ActualWidth / 2,
            _characterWindow.Top + _characterWindow.ActualHeight / 2));
        if (string.IsNullOrEmpty(monitor.DeviceName)) monitor = monitors.FirstOrDefault(m => m.IsPrimary);
        window.Measure(new System.Windows.Size(window.Width, double.PositiveInfinity));
        var width = double.IsNaN(window.Width) ? window.DesiredSize.Width : window.Width;
        var height = double.IsNaN(window.Height) ? window.DesiredSize.Height : window.Height;
        var preferredX = _characterWindow.Left + (_characterWindow.ActualWidth - width) / 2;
        var preferredY = _characterWindow.Top - height - gap;
        if (preferredY < monitor.Top + 8) preferredY = _characterWindow.Top + _characterWindow.ActualHeight + gap;
        window.Left = Math.Clamp(preferredX, monitor.Left + 8, Math.Max(monitor.Left + 8, monitor.Right - width - 8));
        window.Top = Math.Clamp(preferredY, monitor.Top + 8, Math.Max(monitor.Top + 8, monitor.Bottom - height - 8));
    }

    private void OnMessageRequested(object? sender, string message) => System.Windows.Application.Current.Dispatcher.Invoke(() => ShowMessage(message));
    private async Task SaveSafelyAsync()
    {
        try { await _settings.SaveAsync(_settings.Current); }
        catch (Exception exception) { _logger.LogWarning(exception, "Position reset could not be saved"); }
    }
}
