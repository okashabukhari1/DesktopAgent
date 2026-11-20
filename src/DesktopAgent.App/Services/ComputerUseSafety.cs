using System.Runtime.InteropServices;
using System.Windows.Interop;
using System.Windows;
using DesktopAgent.Core.Ai;
using DesktopAgent.Infrastructure.ComputerUse;

namespace DesktopAgent.App.Services;

/// <summary>Message-pump-owned STOP and input-presence monitoring. No key codes or text are collected.</summary>
public sealed class ComputerUseSafety(ComputerUseSession session, IAssistantConversationService conversation) : IDisposable
{
    private HwndSource? _source;
    private nint _keyboard, _mouse;
    private Hook? _keyboardCallback, _mouseCallback;
    private Window? _indicator;
    private System.Windows.Controls.TextBlock? _indicatorText;
    private const int HotkeyId = 0xDA71;
    public void Start()
    {
        if (_source is not null) return;
        _source = new HwndSource(new HwndSourceParameters("DesktopAgent emergency STOP") { ParentWindow = new nint(-3), WindowStyle = 0 });
        _source.AddHook(Messages);
        session.Changed += SessionChanged;
        // F12 is reserved by Windows for debuggers; registration must succeed, never silently assume availability.
        session.EmergencyStopReady = RegisterHotKey(_source.Handle, HotkeyId, 0x4007, 0x7A); // Ctrl+Alt+Shift+F11, no repeat
        _keyboardCallback = (code, message, data) =>
        {
            if (code >= 0 && ((int)message == 0x100 || (int)message == 0x104) && (Marshal.ReadInt32(data, 8) & 0x10) == 0) session.UserInput();
            return CallNextHookEx(0, code, message, data);
        };
        _mouseCallback = (code, message, data) =>
        {
            if (code >= 0 && (int)message is 0x200 or 0x201 or 0x204 or 0x207 or 0x20A or 0x20E && (Marshal.ReadInt32(data, 12) & 1) == 0) session.UserInput();
            return CallNextHookEx(0, code, message, data);
        };
        _keyboard = SetWindowsHookEx(13, _keyboardCallback, GetModuleHandle(null), 0);
        _mouse = SetWindowsHookEx(14, _mouseCallback, GetModuleHandle(null), 0);
        session.InterruptionMonitorReady = _keyboard != 0 && _mouse != 0;
        session.Update(DesktopAgent.Core.ComputerUse.ComputerTaskState.Idle,
            session.EmergencyStopReady && session.InterruptionMonitorReady ? "Emergency STOP: Ctrl+Alt+Shift+F11 · input-presence monitor ready" : "Computer Use disabled: STOP conflict or input monitor unavailable");
    }
    private nint Messages(nint hwnd, int message, nint wParam, nint lParam, ref bool handled)
    {
        if (message == 0x312 && (int)wParam == HotkeyId) { handled = true; conversation.Stop(); }
        return 0;
    }
    private void SessionChanged(object? sender, EventArgs e)
    {
        var dispatcher = System.Windows.Application.Current.Dispatcher;
        if (!dispatcher.HasShutdownStarted) dispatcher.BeginInvoke(UpdateIndicator);
    }
    private void UpdateIndicator()
    {
        if (!session.IsActive)
        {
            session.IndicatorVisible = false;
            _indicator?.Hide(); return;
        }
        if (_indicator is null)
        {
            _indicatorText = new System.Windows.Controls.TextBlock { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 8, 0) };
            var stop = new System.Windows.Controls.Button { Content = "STOP", MinWidth = 70, Padding = new Thickness(8), Background = System.Windows.Media.Brushes.DarkRed, Foreground = System.Windows.Media.Brushes.White };
            stop.Click += (_, _) => conversation.Stop();
            var panel = new System.Windows.Controls.DockPanel { Margin = new Thickness(12) };
            System.Windows.Controls.DockPanel.SetDock(stop, System.Windows.Controls.Dock.Right); panel.Children.Add(stop); panel.Children.Add(_indicatorText);
            _indicator = new Window { Title = "Computer Use — STOP", Width = 370, Height = 120, Content = panel,
                Topmost = true, ShowActivated = false, ShowInTaskbar = false, ResizeMode = ResizeMode.NoResize,
                WindowStyle = WindowStyle.ToolWindow, Left = SystemParameters.WorkArea.Right - 390, Top = SystemParameters.WorkArea.Top + 20 };
            _indicator.Closing += (_, args) => { args.Cancel = true; conversation.Stop(); _indicator.Hide(); };
            _indicator.IsVisibleChanged += (_, _) => { session.IndicatorVisible = _indicator.IsVisible; if (!_indicator.IsVisible && session.IsActive) conversation.Stop(); };
        }
        _indicatorText!.Text = "Computer Use Active\n" + session.State + " · " + session.Summary + "\nCtrl+Alt+Shift+F11";
        if (!_indicator.IsVisible) _indicator.Show();
        session.IndicatorVisible = _indicator.IsVisible;
    }
    public void Dispose()
    {
        session.Changed -= SessionChanged;
        session.Stop(); session.EmergencyStopReady = session.InterruptionMonitorReady = false;
        session.IndicatorVisible = false;
        _indicator?.Hide();
        if (_keyboard != 0) UnhookWindowsHookEx(_keyboard);
        if (_mouse != 0) UnhookWindowsHookEx(_mouse);
        if (_source is not null) { UnregisterHotKey(_source.Handle, HotkeyId); _source.RemoveHook(Messages); _source.Dispose(); _source = null; }
    }
    private delegate nint Hook(int code, nint message, nint data);
    [DllImport("user32.dll", SetLastError = true)] private static extern bool RegisterHotKey(nint hwnd, int id, uint modifiers, uint key);
    [DllImport("user32.dll")] private static extern bool UnregisterHotKey(nint hwnd, int id);
    [DllImport("user32.dll", SetLastError = true)] private static extern nint SetWindowsHookEx(int hook, Hook callback, nint module, uint thread);
    [DllImport("user32.dll")] private static extern bool UnhookWindowsHookEx(nint hook);
    [DllImport("user32.dll")] private static extern nint CallNextHookEx(nint hook, int code, nint message, nint data);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] private static extern nint GetModuleHandle(string? name);
}
