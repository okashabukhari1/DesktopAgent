using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using DesktopAgent.Core.Projects;
using DesktopAgent.Core.UiAwareness;
using Microsoft.Extensions.Logging;

namespace DesktopAgent.Infrastructure.UiAwareness;

public sealed record WindowsDiagnosticFailure(string Category, string ExceptionType, string Message, string HResult,
    string? StackLocation, int? Win32Error = null);
public sealed record WindowsDiagnosticStep(string Operation, bool Success, object? Data,
    WindowsDiagnosticFailure? Failure, double DurationMilliseconds);
public sealed record WindowsDiagnosticReport(string CorrelationId, DateTimeOffset Timestamp, string Build,
    IReadOnlyList<WindowsDiagnosticStep> Steps);

/// <summary>Explicit developer diagnostics, not an AI tool. Local metadata only; no screenshots or input actions.</summary>
public sealed class WindowsDiagnosticsService(WindowsUiAutomationService ui, IWin32WindowProbe probe,
    ISelfWindowRegistry self, IActiveUserWindowTracker tracker, IWindowTargetResolver resolver,
    ISecretProtection secrets, ILogger<WindowsDiagnosticsService> logger)
{
    public event EventHandler<WindowsDiagnosticReport>? ReportCompleted;
    public async Task<WindowsDiagnosticReport> RunAsync(string? application = null, CancellationToken ct = default)
    {
        var id = "UI-" + DateTimeOffset.UtcNow.ToString("yyyyMMdd-HHmmss") + "-" + Guid.NewGuid().ToString("N")[..8];
        var steps = new List<WindowsDiagnosticStep>();
        WindowsDiagnosticStep Add(string operation, Func<object?> call)
        {
            var watch = Stopwatch.StartNew();
            WindowsDiagnosticStep step;
            try { ct.ThrowIfCancellationRequested(); step = new(operation, true, call(), null, watch.Elapsed.TotalMilliseconds); }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
            catch (Exception e) { step = new(operation, false, null, Failure(e, operation), watch.Elapsed.TotalMilliseconds); }
            steps.Add(step); Log(id, step); return step;
        }
        long foreground = 0;
        var foregroundTest = Add("GetForegroundWindow", () =>
        {
            WindowNative.EnsureDesktop();
            foreground = (long)WindowNative.GetForegroundWindow();
            if (foreground == 0) throw new UiObservationException("WindowNotFound");
            return new { HWND = Hex(foreground) };
        });
        if (foregroundTest.Failure?.Category is "SecureDesktop" or "SecureDesktopUnavailable")
            return new(id, DateTimeOffset.UtcNow, typeof(WindowsDiagnosticsService).Assembly.ManifestModule.ModuleVersionId.ToString(), steps);
        Raw(foreground, "Current Foreground", Add);
        Add("Self-window detection", () => new { HWND = Hex(foreground), IsOurProcess = self.IsAgentWindow(foreground) });
        Add("Foreground tracking refresh", () => { tracker.RefreshForeground(); return new { tracker.CurrentForegroundWindow, tracker.LastExternalForegroundWindow }; });
        var last = tracker.LastExternalForegroundWindow;
        Add("Last External identity validation", () => new { HWND = Hex(last?.Handle ?? 0), Valid = last is not null && probe.Reference(last.Handle) == last });
        Raw(last?.Handle ?? 0, "Last External", Add);
        WindowTargetResolution? resolved = null;
        if (application is not null)
        {
            Add("Direct application enumeration", () =>
            {
                // Deliberately bypass tracker/resolver defaults for the direct tests.
                var candidates = probe.List().Where(w => !self.IsAgentWindow(w.Handle) &&
                    (w.ApplicationName.Equals(application, StringComparison.OrdinalIgnoreCase) || w.ProcessName.Equals(application, StringComparison.OrdinalIgnoreCase))).Take(2).ToArray();
                if (candidates.Length == 0) throw new UiObservationException("WindowNotFound");
                if (candidates.Length > 1) throw new UiObservationException("AmbiguousWindow");
                resolved = resolver.Resolve(candidates[0].Handle);
                return new { HWND = Hex(resolved.Window.Handle), resolved.Window.ApplicationName, resolved.Window.ProcessName,
                    resolved.Window.HostedProcessId, resolved.Window.HostedProcessName, Reason = "DirectEnumeration (not foreground tracking)" };
            });
        }
        else Add("Target Resolver", () =>
        {
            resolved = resolver.Resolve();
            return new { HWND = Hex(resolved.Window.Handle), resolved.Window.ProcessName, resolved.Window.Title, Reason = resolved.Source };
        });
        if (resolved is not null)
        {
            Raw(resolved.Window.Handle, "Target Win32", Add);
            // Root -> root properties -> immediate children, each with its OWN failure result.
            // No deep traversal if any prerequisite fails; no shared Everything=Unavailable state.
            var root = await ui.DiagnoseRootAsync(resolved.Window.Handle, id, ct);
            steps.AddRange(root);
            foreach (var step in root) Log(id, step);
            if (root.All(s => s.Success))
            {
                var watch = Stopwatch.StartNew();
                WindowsDiagnosticStep tree;
                try
                {
                    var snapshot = await ui.GetWindowTree(resolved.Window.Handle, new(MaximumDepth: 3, MaximumElements: 100), ct);
                    tree = new("Bounded tree / Interactive Elements", true, new { Depth = 3, MaximumElements = 100,
                        Count = snapshot.InteractiveElements.Count, snapshot.Truncated, snapshot.Status,
                        Controls = snapshot.InteractiveElements.Take(12).Select(UiContextBuilder.Element).ToArray() }, null, watch.Elapsed.TotalMilliseconds);
                }
                catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
                catch (Exception e) { tree = new("Bounded tree / Interactive Elements", false, null, Failure(e, "BoundedTree"), watch.Elapsed.TotalMilliseconds); }
                steps.Add(tree); Log(id, tree);
            }
        }
        var report = new WindowsDiagnosticReport(id, DateTimeOffset.UtcNow, typeof(WindowsDiagnosticsService).Assembly.ManifestModule.ModuleVersionId.ToString(), steps);
        ReportCompleted?.Invoke(this, report);
        return report;
    }
    private void Raw(long handle, string prefix, Func<string, Func<object?>, WindowsDiagnosticStep> add)
    {
        var hwnd = (nint)handle;
        add(prefix + ": IsWindow", () => new { HWND = Hex(handle), Valid = WindowNative.IsWindow(hwnd) });
        add(prefix + ": IsWindowVisible", () => new { Visible = WindowNative.IsWindowVisible(hwnd) });
        uint pid = 0; string? processName = null;
        add(prefix + ": GetWindowThreadProcessId", () =>
        {
            Marshal.SetLastPInvokeError(0);
            var thread = WindowNative.GetWindowThreadProcessId(hwnd, out pid);
            if (thread == 0) throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error());
            return new { PID = pid, ThreadId = thread };
        });
        add(prefix + ": Process.GetProcessById / Name", () =>
        {
            if (pid == 0) throw new UiObservationException("InvalidHandle");
            using var process = Process.GetProcessById((int)pid); processName = process.ProcessName;
            return new { PID = pid, ProcessName = processName };
        });
        add(prefix + ": Executable", () =>
        {
            if (pid == 0) throw new UiObservationException("InvalidHandle");
            using var process = Process.GetProcessById((int)pid);
            return new { Executable = secrets.Redact(process.MainModule?.FileName ?? "Unavailable", out _) };
        });
        add(prefix + ": GetWindowTextLength", () =>
        {
            Marshal.SetLastPInvokeError(0); var length = WindowNative.GetWindowTextLength(hwnd); var error = Marshal.GetLastWin32Error();
            if (length == 0 && error != 0) throw new System.ComponentModel.Win32Exception(error);
            if (!WindowNative.IsWindow(hwnd)) throw new UiObservationException("InvalidHandle");
            return new { Length = length, Win32Error = error };
        });
        add(prefix + ": GetWindowText", () =>
        {
            var title = new StringBuilder(512); Marshal.SetLastPInvokeError(0);
            var count = WindowNative.GetWindowText(hwnd, title, title.Capacity); var error = Marshal.GetLastWin32Error();
            if (count == 0 && error != 0) throw new System.ComponentModel.Win32Exception(error);
            if (!WindowNative.IsWindow(hwnd)) throw new UiObservationException("InvalidHandle");
            return new { Title = WindowsUiAutomationService.IsSensitiveWindow(processName ?? "Unknown", title.ToString()) || probe.Read(handle)?.Title == "[PROTECTED WINDOW]"
                ? "[PROTECTED WINDOW]" : secrets.Redact(title.ToString(), out _), CharactersCopied = count, Win32Error = error };
        });
        add(prefix + ": GetWindowRect", () =>
        {
            Marshal.SetLastPInvokeError(0);
            if (!WindowNative.GetWindowRect(hwnd, out var r)) throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error());
            return new { X = r.Left, Y = r.Top, Width = r.Right - r.Left, Height = r.Bottom - r.Top };
        });
    }
    public WindowsDiagnosticFailure Failure(Exception e, string stage) => DescribeFailure(e, stage, secrets);
    internal static WindowsDiagnosticFailure DescribeFailure(Exception e, string stage, ISecretProtection secrets)
    {
        var original = e.InnerException ?? e;
        var category = e is UiObservationException u ? u.Code : e switch
        {
            System.Windows.Automation.ElementNotAvailableException => "AutomationElementUnavailable",
            UnauthorizedAccessException => "AccessDenied", NotSupportedException => "UnsupportedProvider",
            COMException => "COMException", InvalidOperationException => "InvalidOperationException", _ => "Unknown"
        };
        var location = new StackTrace(original, false).GetFrames()?.Take(2).Select(f => f.GetMethod()?.DeclaringType?.FullName + "." + f.GetMethod()?.Name);
        var message = secrets.Redact(original.Message, out _);
        return new(category, original.GetType().FullName ?? original.GetType().Name, message[..Math.Min(message.Length, 512)],
            $"0x{original.HResult:X8}", location is null ? stage : string.Join(" → ", location),
            original is System.ComponentModel.Win32Exception win32 ? win32.NativeErrorCode : null);
    }
    private void Log(string correlation, WindowsDiagnosticStep step) => logger.LogInformation(
        "UiDiagnostic CorrelationId={CorrelationId} Operation={Operation} Success={Success} DurationMs={DurationMs} Category={Category} ExceptionType={ExceptionType} HResult={HResult} Win32Error={Win32Error}",
        correlation, step.Operation, step.Success, step.DurationMilliseconds, step.Failure?.Category, step.Failure?.ExceptionType, step.Failure?.HResult, step.Failure?.Win32Error);
    private static string Hex(long handle) => $"0x{handle:X}";
}
