using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using DesktopAgent.Core.Applications.Interfaces;
using DesktopAgent.Core.Projects;
using DesktopAgent.Core.UiAwareness;
using Microsoft.Extensions.Logging;

namespace DesktopAgent.Infrastructure.UiAwareness;

public sealed class WindowsSelfWindowRegistry : ISelfWindowRegistry
{
    private readonly System.Collections.Concurrent.ConcurrentDictionary<long, byte> _registered = new();
    public void Register(long handle) { if (WindowNative.ProcessId(handle) == Environment.ProcessId) _registered[handle] = 0; }
    public void Unregister(long handle) => _registered.TryRemove(handle, out _);
    // PID covers unregistered WPF dialogs, native menus, popups, floating and inspector windows.
    // Recheck ownership even for registered HWNDs: Windows can reuse a destroyed handle.
    public bool IsAgentWindow(long handle) => handle != 0 && WindowNative.ProcessId(handle) == Environment.ProcessId;
}

public sealed class WindowsWindowProbe(IApplicationCatalog catalog, ISecretProtection secrets,
    ILogger<WindowsWindowProbe>? logger = null) : IWin32WindowProbe
{
    public long ForegroundHandle => (long)WindowNative.GetForegroundWindow();
    public WindowReference? Reference(long handle)
    {
        if (handle <= 0 || !WindowNative.IsWindow((nint)handle)) return null;
        var pid = WindowNative.ProcessId(handle);
        if (pid <= 0) return null;
        long? start = null;
        try { using var process = Process.GetProcessById(pid); start = process.StartTime.ToUniversalTime().Ticks; }
        catch (Exception e) when (e is System.ComponentModel.Win32Exception or InvalidOperationException or ArgumentException) { }
        return WindowNative.IsWindow((nint)handle) && WindowNative.ProcessId(handle) == pid ? new(handle, pid, start) : null;
    }
    public WindowContext? Read(long handle)
    {
        WindowNative.EnsureDesktop();
        var reference = Reference(handle);
        if (reference is null) { logger?.LogInformation("UiWin32 Category=InvalidHandle Handle={Handle}", handle); return null; }
        var hwnd = (nint)handle;
        var processName = "Unknown";
        try { using var process = Process.GetProcessById(reference.ProcessId); processName = process.ProcessName; }
        catch (ArgumentException) { return null; }
        catch (Exception e) when (e is System.ComponentModel.Win32Exception or InvalidOperationException)
        { logger?.LogWarning("UiWin32 Stage=ProcessName Category=AccessDenied Handle={Handle} ProcessId={ProcessId}", handle, reference.ProcessId); }
        var text = new StringBuilder(512);
        WindowNative.GetWindowText(hwnd, text, text.Capacity);
        var sensitive = WindowsUiAutomationService.IsSensitiveWindow(processName, text.ToString());
        if (!WindowNative.GetWindowRect(hwnd, out var rect)) throw new UiObservationException("InvalidHandle");
        var title = sensitive ? "[PROTECTED WINDOW]" : secrets.Redact(text.ToString(), out _);
        if (Reference(handle) != reference) throw new UiObservationException("WindowClosed");
        var apps = catalog.Applications.Where(a => a.ProcessName?.Equals(processName, StringComparison.OrdinalIgnoreCase) == true).Take(2).ToArray();
        var result = new WindowContext(handle, reference.ProcessId, processName, apps.Length == 1 ? apps[0].Id : null,
            title, new(rect.Left, rect.Top, rect.Right - rect.Left, rect.Bottom - rect.Top), WindowNative.IsWindowVisible(hwnd),
            WindowNative.IsWindowEnabled(hwnd), WindowNative.IsIconic(hwnd), WindowNative.IsZoomed(hwnd),
            ForegroundHandle == handle, null, DateTimeOffset.UtcNow);
        if (processName.Equals("ApplicationFrameHost", StringComparison.OrdinalIgnoreCase))
        {
            var hosted = new Dictionary<int, string>(); var visited = 0;
            WindowNative.EnumChildWindows(hwnd, (child, _) =>
            {
                if (++visited > 64) return false;
                var childPid = WindowNative.ProcessId((long)child);
                if (childPid > 0 && childPid != reference.ProcessId && !hosted.ContainsKey(childPid))
                {
                    try { using var process = Process.GetProcessById(childPid); hosted[childPid] = process.ProcessName; }
                    catch (Exception e) when (e is ArgumentException or InvalidOperationException or System.ComponentModel.Win32Exception) { }
                }
                return true;
            }, 0);
            // Do not infer application identity from a spoofable window title or arbitrarily choose among apps.
            if (hosted.Count == 1 && visited <= 64)
            {
                var app = hosted.Single();
                result = result with { HostedProcessId = app.Key, HostedProcessName = app.Value,
                    ApplicationName = WindowContext.FriendlyName(app.Value),
                    Title = WindowsUiAutomationService.IsSensitiveWindow(app.Value, text.ToString()) ? "[PROTECTED WINDOW]" : result.Title };
            }
        }
        logger?.LogInformation("UiWin32 Category=Success Handle={Handle} ProcessId={ProcessId} ProcessName={ProcessName} TitleState={TitleState} Visible={Visible} Bounds={Bounds}",
            handle, result.ProcessId, processName, sensitive ? "Protected" : "OmittedForPrivacy", result.IsVisible, result.Bounds);
        return result;
    }
    public IReadOnlyList<WindowContext> List()
    {
        WindowNative.EnsureDesktop();
        var result = new List<WindowContext>();
        WindowNative.EnumWindows((hwnd, _) =>
        {
            if (result.Count >= 64) return false;
            if (WindowNative.IsWindowVisible(hwnd))
            {
                try { if (Read((long)hwnd) is { } w) result.Add(w); }
                catch (UiObservationException e) when (e.Code != "SecureDesktop") { }
            }
            return true;
        }, 0);
        return result;
    }
}

public sealed class WindowsForegroundEvents(ActiveUserWindowTracker tracker, ILogger<WindowsForegroundEvents> logger) : IDisposable
{
    private nint _hook;
    private WindowNative.WinEventDelegate? _callback;
    // Call on WPF's message-pump thread BEFORE opening any companion/agent windows.
    public void Start()
    {
        if (_hook != 0) return;
        tracker.RefreshForeground();
        _callback = (_, _, hwnd, _, _, _, _) =>
        {
            try { tracker.Observe((long)hwnd); }
            catch (Exception e) { logger.LogWarning("UiTracking Category=Unknown ErrorType={ErrorType}", e.GetType().Name); }
        };
        _hook = WindowNative.SetWinEventHook(3, 3, 0, _callback, 0, 0, 0); // Include own-process transitions.
        if (_hook == 0) logger.LogWarning("UiTracking Category=AccessDenied Stage=SetWinEventHook LastError={LastError}", Marshal.GetLastWin32Error());
    }
    public void Dispose() { if (_hook != 0) WindowNative.UnhookWinEvent(_hook); _hook = 0; }
}

internal static class WindowNative
{
    internal static int ProcessId(long handle) { GetWindowThreadProcessId((nint)handle, out var pid); return (int)pid; }
    internal static void EnsureDesktop()
    {
        var desktop = OpenInputDesktop(0, false, 1);
        if (desktop == 0) throw new UiObservationException("SecureDesktop");
        try
        {
            var name = new StringBuilder(128);
            if (!GetUserObjectInformation(desktop, 2, name, 256, out _) || name.ToString() != "Default") throw new UiObservationException("SecureDesktop");
        }
        finally { CloseDesktop(desktop); }
    }
    [StructLayout(LayoutKind.Sequential)] internal struct Rect { public int Left, Top, Right, Bottom; }
    internal delegate bool EnumWindowsDelegate(nint handle, nint data);
    internal delegate void WinEventDelegate(nint hook, uint evt, nint window, int obj, int child, uint thread, uint time);
    [DllImport("user32.dll")] internal static extern nint GetForegroundWindow();
    [DllImport("user32.dll", SetLastError = true)] internal static extern uint GetWindowThreadProcessId(nint handle, out uint pid);
    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)] internal static extern int GetWindowText(nint handle, StringBuilder text, int count);
    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)] internal static extern int GetWindowTextLength(nint handle);
    [DllImport("user32.dll", SetLastError = true)] internal static extern bool GetWindowRect(nint handle, out Rect rect);
    [DllImport("user32.dll")] internal static extern bool IsWindow(nint handle);
    [DllImport("user32.dll")] internal static extern bool IsWindowVisible(nint handle);
    [DllImport("user32.dll")] internal static extern bool IsWindowEnabled(nint handle);
    [DllImport("user32.dll")] internal static extern bool IsIconic(nint handle);
    [DllImport("user32.dll")] internal static extern bool IsZoomed(nint handle);
    [DllImport("user32.dll")] internal static extern bool EnumWindows(EnumWindowsDelegate callback, nint data);
    [DllImport("user32.dll")] internal static extern bool EnumChildWindows(nint parent, EnumWindowsDelegate callback, nint data);
    [DllImport("user32.dll", SetLastError = true)] internal static extern nint SetWinEventHook(uint start, uint end, nint module, WinEventDelegate callback, uint pid, uint thread, uint flags);
    [DllImport("user32.dll")] internal static extern bool UnhookWinEvent(nint hook);
    [DllImport("user32.dll")] private static extern nint OpenInputDesktop(uint flags, bool inherit, uint access);
    [DllImport("user32.dll", CharSet = CharSet.Unicode, EntryPoint = "GetUserObjectInformationW")] private static extern bool GetUserObjectInformation(nint obj, int index, StringBuilder info, uint length, out uint needed);
    [DllImport("user32.dll")] private static extern bool CloseDesktop(nint desktop);
}
