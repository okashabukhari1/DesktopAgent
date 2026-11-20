using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using DesktopAgent.App.ViewModels;
using DesktopAgent.Core.Enums;
using DesktopAgent.Core.Interfaces;
using DesktopAgent.Core.Models;
using DesktopAgent.Core.Services;
using Microsoft.Extensions.Logging;

namespace DesktopAgent.App.Views;

public partial class CharacterWindow : Window
{
    private readonly ISettingsService _settings;
    private readonly IMonitorService _monitors;
    private readonly ICharacterService _characterService;
    private readonly IWindowService _windows;
    private readonly ILogger<CharacterWindow> _logger;
    private System.Windows.Point _mouseDown;
    private bool _dragging;
    private bool _moved;

    public CharacterWindow(CharacterWindowViewModel viewModel, ISettingsService settings, IMonitorService monitors,
        ICharacterService characterService, IWindowService windows, ILogger<CharacterWindow> logger)
    {
        InitializeComponent();
        DataContext = viewModel;
        _settings = settings;
        _monitors = monitors;
        _characterService = characterService;
        _windows = windows;
        _logger = logger;
        Loaded += OnLoaded;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        Topmost = _settings.Current.AlwaysOnTop;
        RestorePosition();
    }

    public void RestorePosition()
    {
        var position = PositionValidator.Validate(_settings.Current.RememberPosition ? _settings.Current.Position : null,
            ActualWidth > 0 ? ActualWidth : _settings.Current.CharacterSize,
            ActualHeight > 0 ? ActualHeight : _settings.Current.CharacterSize,
            _monitors.GetWorkAreas());
        Left = position.X;
        Top = position.Y;
    }

    public void ApplySettings()
    {
        Topmost = _settings.Current.AlwaysOnTop;
        ((CharacterWindowViewModel)DataContext).Refresh();
        Dispatcher.BeginInvoke(RestorePosition, System.Windows.Threading.DispatcherPriority.Loaded);
    }

    private void Character_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        _mouseDown = e.GetPosition(this);
        _dragging = true;
        _moved = false;
        Character.CaptureMouse();
        e.Handled = true;
    }

    private void Character_MouseMove(object sender, System.Windows.Input.MouseEventArgs e)
    {
        if (!_dragging || e.LeftButton != MouseButtonState.Pressed) return;
        var screen = PointToScreen(e.GetPosition(this));
        var source = PresentationSource.FromVisual(this);
        if (source?.CompositionTarget is not null)
            screen = source.CompositionTarget.TransformFromDevice.Transform(screen);
        if (Math.Abs(screen.X - (Left + _mouseDown.X)) + Math.Abs(screen.Y - (Top + _mouseDown.Y)) > 3) _moved = true;
        Left = screen.X - _mouseDown.X;
        Top = screen.Y - _mouseDown.Y;
    }

    private async void Character_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (!_dragging) return;
        _dragging = false;
        Character.ReleaseMouseCapture();
        if (!_moved)
        {
            _windows.TogglePanel();
            return;
        }

        var validated = PositionValidator.Validate(new CharacterPosition(Left, Top, null), ActualWidth, ActualHeight, _monitors.GetWorkAreas());
        Left = validated.X;
        Top = validated.Y;
        if (_settings.Current.RememberPosition)
        {
            _settings.Current.Position = new CharacterPosition(Left, Top, validated.MonitorDeviceName);
            try { await _settings.SaveAsync(_settings.Current); }
            catch (Exception exception) { _logger.LogWarning(exception, "Character position could not be saved"); }
        }
        _logger.LogInformation("CharacterMoved X={X} Y={Y} Monitor={Monitor}", Left, Top, validated.MonitorDeviceName);
    }

    private void Character_MouseRightButtonUp(object sender, MouseButtonEventArgs e)
    {
        var menu = new System.Windows.Controls.ContextMenu();
        Add(menu, "Open Desktop App", _windows.ShowDesktopApp);
        Add(menu, "Workspaces", _windows.ShowWorkspaces);
        Add(menu, "Applications", _windows.ShowApplications);
        Add(menu, "Open Mini Panel", _windows.TogglePanel);
        Add(menu, "Settings", _windows.ShowSettings);
        var topmost = new System.Windows.Controls.MenuItem { Header = "Always on Top", IsCheckable = true, IsChecked = _settings.Current.AlwaysOnTop };
        topmost.Click += async (_, _) => { _settings.Current.AlwaysOnTop = topmost.IsChecked; ApplySettings(); await _settings.SaveAsync(_settings.Current); };
        menu.Items.Add(topmost);
        var states = new System.Windows.Controls.MenuItem { Header = "Character State" };
        foreach (CharacterState state in Enum.GetValues(typeof(CharacterState)))
        {
            var stateItem = new System.Windows.Controls.MenuItem { Header = state.ToString(), IsCheckable = true, IsChecked = state == _characterService.State };
            stateItem.Click += (_, _) => _characterService.SetState(state);
            states.Items.Add(stateItem);
        }
        menu.Items.Add(states);
        Add(menu, "Reset Position", _windows.ResetPosition);
        menu.Items.Add(new Separator());
        Add(menu, "Hide Character", _windows.HideCharacter);
        Add(menu, "Exit", _windows.Shutdown);
        menu.IsOpen = true;
        e.Handled = true;
    }

    private static void Add(ItemsControl menu, string header, Action action)
    {
        var item = new System.Windows.Controls.MenuItem { Header = header };
        item.Click += (_, _) => action();
        menu.Items.Add(item);
    }
}
