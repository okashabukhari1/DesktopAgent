using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using DesktopAgent.Core.Automation.Enums;
using DesktopAgent.Core.Automation.Interfaces;
using DesktopAgent.Core.Automation.Models;
using DesktopAgent.Core.Automation.Services;
using Microsoft.Extensions.Logging;

namespace DesktopAgent.Infrastructure.Windows.Automation;

public sealed class WindowsExternalWindowService(
    IApplicationResolver resolver,
    ILogger<WindowsExternalWindowService> logger, DesktopAgent.Core.UiAwareness.IWin32WindowProbe? probe = null) : IExternalWindowService
{
    public IReadOnlyList<OpenWindowInfo> GetOpenWindows()
    {
        var windows = new List<OpenWindowInfo>();
        NativeMethods.EnumWindows((handle, _) =>
        {
            var window = ReadWindow(handle);
            if (window is not null) windows.Add(window);
            return true;
        }, 0);
        return windows
            .OrderBy(window => window.ProcessName, StringComparer.OrdinalIgnoreCase)
            .ThenBy(window => window.Title, StringComparer.OrdinalIgnoreCase)
            .ThenBy(window => window.ProcessId)
            .ToArray();
    }

    public IReadOnlyList<DisplayMonitorInfo> GetMonitors()
    {
        var monitors = new List<(string Name, WindowRectangle Bounds, WindowRectangle Work, bool Primary)>();
        NativeMethods.EnumDisplayMonitors(0, 0, (nint monitor, nint deviceContext, ref NativeMethods.Rect monitorRect, nint data) =>
        {
            var info = new NativeMethods.MonitorInfoEx { Size = Marshal.SizeOf<NativeMethods.MonitorInfoEx>(), DeviceName = string.Empty };
            if (NativeMethods.GetMonitorInfo(monitor, ref info))
                monitors.Add((info.DeviceName, ToRectangle(info.Monitor), ToRectangle(info.Work), (info.Flags & 1) != 0));
            return true;
        }, 0);
        return monitors.OrderByDescending(item => item.Primary).ThenBy(item => item.Bounds.X).ThenBy(item => item.Bounds.Y)
            .Select((item, index) => new DisplayMonitorInfo(index + 1, item.Name, item.Bounds, item.Work, item.Primary)).ToArray();
    }

    public OpenWindowInfo? FindWindow(string target)
    {
        if (string.IsNullOrWhiteSpace(target)) return null;
        var definition = resolver.Resolve(target);
        return GetOpenWindows()
            .Select(window => new { Window = window, Rank = Rank(window, target, definition) })
            .Where(candidate => candidate.Rank < int.MaxValue)
            .OrderBy(candidate => candidate.Rank)
            .ThenBy(candidate => candidate.Window.ProcessId)
            .ThenBy(candidate => candidate.Window.Handle)
            .Select(candidate => candidate.Window)
            .FirstOrDefault();
    }

    public ComputerActionResult Focus(string target)
    {
        var window = FindWindow(target);
        if (window is null) return NotFound(target);
        return FocusWindow(window);
    }
    public ComputerActionResult FocusWindow(OpenWindowInfo window)
    {
        var current = ReadWindow(window.Handle);
        if (current is null || current.ProcessId != window.ProcessId || window.ProcessStartTicks is null ||
            current.ProcessStartTicks != window.ProcessStartTicks || current.HostedProcessId != window.HostedProcessId || current.Title != window.Title)
            return ComputerActionResult.Fail(ActionStatus.Failed, "The selected application window changed. Resolve it again.", window.ProcessName, "WindowIdentityChanged");
        if (window.State == ExternalWindowState.Minimized) NativeMethods.ShowWindowAsync(window.Handle, NativeMethods.SwRestore);
        if (!TrySetForeground(window.Handle) || NativeMethods.GetForegroundWindow() != window.Handle)
            return ComputerActionResult.Fail(ActionStatus.Failed,
                "Windows did not focus the selected application. Select it from the taskbar.", window.ProcessName, "SetForegroundWindowFailed");
        logger.LogInformation("ApplicationFocused Handle={Handle} ProcessId={ProcessId} HostedProcessId={HostedProcessId} ProcessName={ProcessName}", window.Handle, window.ProcessId, window.HostedProcessId, window.ProcessName);
        return ComputerActionResult.Ok($"{Display(window)} focused.", window.HostedProcessName ?? window.ProcessName);
    }

    public ComputerActionResult Close(string target)
    {
        var window = FindWindow(target);
        if (window is null) return NotFound(target);
        if (!NativeMethods.PostMessage(window.Handle, NativeMethods.WmClose, 0, 0)) return NativeFailure("close", target);
        logger.LogInformation("ApplicationClosed Target={Target} ProcessId={ProcessId}", target, window.ProcessId);
        return ComputerActionResult.Ok($"Close requested for {Display(window)}. Any save prompt is left for you.", target);
    }

    public ComputerActionResult Minimize(string target) => SetShowState(target, NativeMethods.SwShowMinimized, "minimized");
    public ComputerActionResult Maximize(string target) => SetShowState(target, NativeMethods.SwShowMaximized, "maximized");
    public ComputerActionResult Restore(string target) => SetShowState(target, NativeMethods.SwRestore, "restored");

    public ComputerActionResult Move(string target, int x, int y)
    {
        var window = FindWindow(target);
        if (window is null) return NotFound(target);
        var monitors = GetMonitors();
        if (monitors.Count == 0) return ComputerActionResult.Fail(ActionStatus.Failed, "No display monitors were found.", target);
        var virtualLeft = monitors.Min(monitor => monitor.Bounds.X);
        var virtualTop = monitors.Min(monitor => monitor.Bounds.Y);
        var virtualRight = monitors.Max(monitor => monitor.Bounds.Right);
        var virtualBottom = monitors.Max(monitor => monitor.Bounds.Bottom);
        if (x < virtualLeft - window.Bounds.Width + 100 || x > virtualRight - 100 ||
            y < virtualTop - window.Bounds.Height + 80 || y > virtualBottom - 80)
            return ComputerActionResult.Fail(ActionStatus.InvalidInput, "The requested position would make the window inaccessible.", target);
        return SetBounds(window, new WindowRectangle(x, y, window.Bounds.Width, window.Bounds.Height), "moved");
    }

    public ComputerActionResult Resize(string target, int width, int height)
    {
        var window = FindWindow(target);
        if (window is null) return NotFound(target);
        if (width < 160 || height < 100 || width > 10000 || height > 10000)
            return ComputerActionResult.Fail(ActionStatus.InvalidInput, "Window size must be between 160×100 and 10000×10000.", target);
        return SetBounds(window, new WindowRectangle(window.Bounds.X, window.Bounds.Y, width, height), "resized");
    }

    public ComputerActionResult ApplyLayout(string target, WindowLayout layout)
    {
        if (layout == WindowLayout.Maximized) return Maximize(target);
        var window = FindWindow(target);
        if (window is null) return NotFound(target);
        var monitor = GetMonitorFor(window);
        if (monitor is null) return ComputerActionResult.Fail(ActionStatus.Failed, "The target monitor could not be determined.", target);
        NativeMethods.ShowWindowAsync(window.Handle, NativeMethods.SwRestore);
        var bounds = WindowLayoutCalculator.Calculate(layout, monitor.WorkingArea, window.Bounds);
        var result = SetBounds(window, bounds, $"moved to {layout}");
        if (result.Success) logger.LogInformation("WindowMoved Target={Target} Layout={Layout}", target, layout);
        return result;
    }

    public ComputerActionResult MoveToMonitor(string target, int monitorNumber)
    {
        var window = FindWindow(target);
        if (window is null) return NotFound(target);
        var monitor = GetMonitors().FirstOrDefault(item => item.Number == monitorNumber);
        if (monitor is null) return ComputerActionResult.Fail(ActionStatus.InvalidInput, $"Monitor {monitorNumber} was not found.", target);
        NativeMethods.ShowWindowAsync(window.Handle, NativeMethods.SwRestore);
        var width = Math.Min(window.Bounds.Width, monitor.WorkingArea.Width);
        var height = Math.Min(window.Bounds.Height, monitor.WorkingArea.Height);
        var bounds = new WindowRectangle(monitor.WorkingArea.X + (monitor.WorkingArea.Width - width) / 2,
            monitor.WorkingArea.Y + (monitor.WorkingArea.Height - height) / 2, width, height);
        return SetBounds(window, bounds, $"moved to monitor {monitorNumber}");
    }

    private ComputerActionResult SetShowState(string target, int state, string verb)
    {
        var window = FindWindow(target);
        if (window is null) return NotFound(target);
        NativeMethods.ShowWindowAsync(window.Handle, state);
        logger.LogInformation("WindowStateChanged Target={Target} State={State}", target, verb);
        return ComputerActionResult.Ok($"{Display(window)} {verb}.", target);
    }

    private ComputerActionResult SetBounds(OpenWindowInfo window, WindowRectangle bounds, string action)
    {
        NativeMethods.ShowWindowAsync(window.Handle, NativeMethods.SwRestore);
        if (!NativeMethods.SetWindowPos(window.Handle, 0, bounds.X, bounds.Y, bounds.Width, bounds.Height,
                NativeMethods.SwpNoActivate | NativeMethods.SwpNoZOrder))
            return NativeFailure(action, window.Title);
        logger.LogInformation("WindowBoundsChanged ProcessId={ProcessId} X={X} Y={Y} Width={Width} Height={Height}",
            window.ProcessId, bounds.X, bounds.Y, bounds.Width, bounds.Height);
        return ComputerActionResult.Ok($"{Display(window)} {action}.", window.ProcessName, data:
            new Dictionary<string, string>
            {
                ["X"] = bounds.X.ToString(), ["Y"] = bounds.Y.ToString(),
                ["Width"] = bounds.Width.ToString(), ["Height"] = bounds.Height.ToString()
            });
    }

    private DisplayMonitorInfo? GetMonitorFor(OpenWindowInfo window)
    {
        var monitorHandle = NativeMethods.MonitorFromWindow(window.Handle, NativeMethods.MonitorDefaultToNearest);
        var info = new NativeMethods.MonitorInfoEx { Size = Marshal.SizeOf<NativeMethods.MonitorInfoEx>(), DeviceName = string.Empty };
        if (monitorHandle == 0 || !NativeMethods.GetMonitorInfo(monitorHandle, ref info)) return null;
        return GetMonitors().FirstOrDefault(monitor => string.Equals(monitor.DeviceName, info.DeviceName, StringComparison.OrdinalIgnoreCase));
    }

    private OpenWindowInfo? ReadWindow(nint handle)
    {
        if (!NativeMethods.IsWindowVisible(handle)) return null;
        var length = NativeMethods.GetWindowTextLength(handle);
        if (length <= 0) return null;
        if (NativeMethods.DwmGetWindowAttribute(handle, NativeMethods.DwmwaCloaked, out var cloaked, sizeof(int)) == 0 && cloaked != 0) return null;
        var title = new StringBuilder(length + 1);
        NativeMethods.GetWindowText(handle, title, title.Capacity);
        if (string.IsNullOrWhiteSpace(title.ToString())) return null;
        NativeMethods.GetWindowThreadProcessId(handle, out var processIdValue);
        if (processIdValue == 0 || processIdValue > int.MaxValue || !NativeMethods.GetWindowRect(handle, out var rectangle)) return null;
        var bounds = ToRectangle(rectangle);
        if (bounds.Width <= 0 || bounds.Height <= 0) return null;
        try
        {
            using var process = Process.GetProcessById((int)processIdValue);
            var state = NativeMethods.IsIconic(handle) ? ExternalWindowState.Minimized :
                NativeMethods.IsZoomed(handle) ? ExternalWindowState.Maximized : ExternalWindowState.Normal;
            var monitorHandle = NativeMethods.MonitorFromWindow(handle, NativeMethods.MonitorDefaultToNearest);
            var monitorInfo = new NativeMethods.MonitorInfoEx { Size = Marshal.SizeOf<NativeMethods.MonitorInfoEx>(), DeviceName = string.Empty };
            NativeMethods.GetMonitorInfo(monitorHandle, ref monitorInfo);
            var hosted = process.ProcessName.Equals("ApplicationFrameHost", StringComparison.OrdinalIgnoreCase) ? probe?.Read((long)handle) : null;
            return new OpenWindowInfo(handle, title.ToString().Trim(), process.Id, process.ProcessName,
                bounds, monitorInfo.DeviceName ?? string.Empty, state, true) { ProcessStartTicks = process.StartTime.ToUniversalTime().Ticks,
                    HostedProcessId = hosted?.HostedProcessId, HostedProcessName = hosted?.HostedProcessName };
        }
        catch (Exception exception) when (exception is ArgumentException or InvalidOperationException or Win32Exception or DesktopAgent.Core.UiAwareness.UiObservationException)
        {
            return null;
        }
    }

    private static int Rank(OpenWindowInfo window, string target, ApplicationDefinition? definition)
    {
        if (window.ProcessName.Equals("ApplicationFrameHost", StringComparison.OrdinalIgnoreCase) && target.Equals("ApplicationFrameHost", StringComparison.OrdinalIgnoreCase)) return int.MaxValue;
        if (definition is not null && string.Equals(window.HostedProcessName, definition.ProcessName, StringComparison.OrdinalIgnoreCase)) return 0;
        if (definition is not null && string.Equals(window.ProcessName, definition.ProcessName, StringComparison.OrdinalIgnoreCase)) return 0;
        if (string.Equals(window.ProcessName, target, StringComparison.OrdinalIgnoreCase)) return 1;
        if (definition is not null && definition.Aliases.Any(alias => window.Title.StartsWith(alias, StringComparison.OrdinalIgnoreCase))) return 2;
        if (window.Title.StartsWith(target, StringComparison.OrdinalIgnoreCase)) return 3;
        if (definition is not null && definition.Aliases.Any(alias => window.Title.Contains(alias, StringComparison.OrdinalIgnoreCase))) return 4;
        return window.Title.Contains(target, StringComparison.OrdinalIgnoreCase) ? 5 : int.MaxValue;
    }

    private static bool TrySetForeground(nint handle)
    {
        if (NativeMethods.SetForegroundWindow(handle)) return true;
        var currentThread = NativeMethods.GetCurrentThreadId();
        var targetThread = NativeMethods.GetWindowThreadProcessId(handle, out _);
        var foregroundHandle = NativeMethods.GetForegroundWindow();
        var foregroundThread = foregroundHandle == 0 ? 0 : NativeMethods.GetWindowThreadProcessId(foregroundHandle, out _);
        var attachedTarget = targetThread != 0 && targetThread != currentThread &&
            NativeMethods.AttachThreadInput(currentThread, targetThread, true);
        var attachedForeground = foregroundThread != 0 && foregroundThread != currentThread && foregroundThread != targetThread &&
            NativeMethods.AttachThreadInput(currentThread, foregroundThread, true);
        try
        {
            NativeMethods.BringWindowToTop(handle);
            return NativeMethods.SetForegroundWindow(handle);
        }
        finally
        {
            if (attachedForeground) NativeMethods.AttachThreadInput(currentThread, foregroundThread, false);
            if (attachedTarget) NativeMethods.AttachThreadInput(currentThread, targetThread, false);
        }
    }

    private static WindowRectangle ToRectangle(NativeMethods.Rect rectangle) =>
        new(rectangle.Left, rectangle.Top, rectangle.Right - rectangle.Left, rectangle.Bottom - rectangle.Top);

    private static string Display(OpenWindowInfo window) => string.IsNullOrWhiteSpace(window.Title) ? window.ProcessName : window.Title;
    private static ComputerActionResult NotFound(string target) =>
        ComputerActionResult.Fail(ActionStatus.NotFound, $"No usable window matching '{target}' was found.", target);

    private static ComputerActionResult NativeFailure(string operation, string target)
    {
        var error = Marshal.GetLastWin32Error();
        var status = error == 5 ? ActionStatus.PermissionDenied : ActionStatus.Failed;
        var message = error == 5
            ? "Windows denied the operation. The target may be running with higher privileges."
            : $"Windows could not {operation} the target window.";
        return ComputerActionResult.Fail(status, message, target, $"Win32:{error}");
    }
}
