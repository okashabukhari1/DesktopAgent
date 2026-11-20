using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Automation;
using DesktopAgent.Core.Applications.Interfaces;
using DesktopAgent.Core.Automation.Interfaces;
using DesktopAgent.Core.Automation.Enums;
using DesktopAgent.Core.Automation.Models;
using DesktopAgent.Core.Projects;
using DesktopAgent.Core.UiAwareness;
using Microsoft.Extensions.Logging;

namespace DesktopAgent.Infrastructure.UiAwareness;

/// <summary>Bounded UIA observation plus an opt-in typed action backend. No desktop subtree scans or bulk text/value reads.</summary>
public sealed partial class WindowsUiAutomationService(IApplicationCatalog catalog, ISecretProtection secrets,
    ILogger<WindowsUiAutomationService>? logger = null, IWin32WindowProbe? windowProbe = null,
    ISelfWindowRegistry? selfRegistry = null, IWindowTargetResolver? targetResolver = null)
    : IWindowsUiAutomationService, IDisposable
{
    public IWin32WindowProbe Probe { get; } = windowProbe ?? new WindowsWindowProbe(catalog, secrets);
    public ISelfWindowRegistry Self { get; } = selfRegistry ?? new WindowsSelfWindowRegistry();
    private IWindowTargetResolver? _resolver = targetResolver;
    public IWindowTargetResolver Targets => _resolver ??= new WindowTargetResolver(Probe, Self, new ActiveUserWindowTracker(Probe, Self));
    private readonly UiObservationWorker _worker = new();
    private int _inspectionRequests;
    private readonly object _sync = new();
    private UiSnapshot? _last;
    private string? _lastIdentity;
    private Dictionary<string, int[]> _references = [];
    private long _epoch;
    private long _target;
    private bool _disposed;
    private readonly List<nint> _hooks = [];
    private Native.WinEventDelegate? _eventCallback;
    public event EventHandler? Changed;
    public bool IsInspecting => Volatile.Read(ref _inspectionRequests) > 0;
    public UiSnapshot? LastSnapshot { get { lock (_sync) return _last; } }

    // Must be called on the application's message-pump thread. Events invalidate only; they never read UI content.
    public void StartEvents()
    {
        if (_hooks.Count != 0 || _disposed) return;
        _eventCallback = (_, evt, hwnd, _, _, _, _) => { if (evt == 3 || (long)hwnd == Interlocked.Read(ref _target)) Invalidate(); };
        foreach (var range in new (uint Start, uint End)[] { (3, 3), (0x8005, 0x8005), (0x8000, 0x8001) })
        {
            var hook = Native.SetWinEventHook(range.Start, range.End, 0, _eventCallback, 0, 0, 2); // Out-of-context, skip own process.
            if (hook != 0) _hooks.Add(hook);
        }
    }
    public void Invalidate()
    {
        Interlocked.Increment(ref _epoch);
        lock (_sync) { _last = null; _references.Clear(); }
    }
    public void CancelInspection() => _worker.Cancel();
    public async Task<IReadOnlyList<WindowsDiagnosticStep>> DiagnoseRootAsync(long handle, string correlationId, CancellationToken ct = default)
    {
        var stage = "UIA HWND validation";
        try
        {
            return await _worker.RunAsync<IReadOnlyList<WindowsDiagnosticStep>>(token =>
            {
                var result = new List<WindowsDiagnosticStep>();
                bool Step(string name, Func<object?> call)
                {
                    Volatile.Write(ref stage, name);
                    var watch = Stopwatch.StartNew();
                    try { token.ThrowIfCancellationRequested(); result.Add(new(name, true, call(), null, watch.Elapsed.TotalMilliseconds)); return true; }
                    catch (OperationCanceledException) { throw; }
                    catch (Exception e)
                    { result.Add(new(name, false, null, WindowsDiagnosticsService.DescribeFailure(e, name, secrets), watch.Elapsed.TotalMilliseconds)); return false; }
                }
                string? identity = null;
                AutomationElement? root = null;
                if (!Step("UIA HWND validation", () =>
                {
                    identity = WindowIdentity(handle);
                    var window = Probe.Read(handle) ?? throw new UiObservationException("InvalidHandle");
                    if (window.Title == "[PROTECTED WINDOW]") throw new UiObservationException("SensitiveWindowBlocked");
                    return new { HWND = $"0x{handle:X}", window.ProcessId, Apartment = Thread.CurrentThread.GetApartmentState().ToString(), ThreadId = Environment.CurrentManagedThreadId };
                })) return result;
                if (!Step("UIA Root / AutomationElement.FromHandle", () =>
                {
                    root = AutomationElement.FromHandle((nint)handle);
                    if (root is null) throw new UiObservationException("AutomationElementUnavailable");
                    return new { RootAvailable = true, Apartment = Thread.CurrentThread.GetApartmentState().ToString() };
                })) return result;
                object Describe(AutomationElement element)
                {
                    token.ThrowIfCancellationRequested();
                    // No ValuePattern/TextPattern calls; unknown password status fails closed for names.
                    var password = element.GetCurrentPropertyValue(AutomationElement.IsPasswordProperty, true) as bool?;
                    var type = element.GetCurrentPropertyValue(AutomationElement.ControlTypeProperty, true) as ControlType;
                    var name = password == true ? "Password field" : password is null || type == ControlType.Edit || type == ControlType.Text || type == ControlType.Document
                        ? "[TEXT OMITTED]" : Safe(element.GetCurrentPropertyValue(AutomationElement.NameProperty, true) as string);
                    return new { Name = name, ControlType = type?.ProgrammaticName, AutomationId = password == true ? null : Safe(element.GetCurrentPropertyValue(AutomationElement.AutomationIdProperty, true) as string),
                        ClassName = password == true ? null : Safe(element.GetCurrentPropertyValue(AutomationElement.ClassNameProperty, true) as string) };
                }
                if (!Step("UIA Root properties", () => Describe(root!))) return result;
                Step("Immediate Children", () =>
                {
                    var children = new List<object>();
                    var walker = TreeWalker.ControlViewWalker;
                    var child = walker.GetFirstChild(root);
                    while (child is not null && children.Count < 50)
                    {
                        token.ThrowIfCancellationRequested(); children.Add(Describe(child));
                        child = walker.GetNextSibling(child);
                    }
                    if (identity != WindowIdentity(handle)) throw new UiObservationException("WindowClosed");
                    return new { Count = children.Count, Truncated = child is not null, Children = children };
                });
                return result;
            }, ct);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
        catch (Exception e)
        {
            var failedStage = Volatile.Read(ref stage);
            logger?.LogWarning("UiDiagnostic CorrelationId={CorrelationId} Stage={Stage} ExceptionType={ExceptionType} HResult={HResult}", correlationId, failedStage, e.GetType().FullName, e.HResult);
            return [new(failedStage, false, null, WindowsDiagnosticsService.DescribeFailure(e, failedStage, secrets), e is UiObservationException { Code: "Timeout" } ? 3000 : 0)];
        }
    }
    public string WindowIdentity(long? handle, bool explicitlyInspectAgent = false)
    {
        EnsureDesktop();
        var target = Targets.Resolve(handle, explicitlyInspectAgent: explicitlyInspectAgent);
        return $"{target.Reference.Handle}:{target.Reference.ProcessId}:{target.Reference.ProcessStartTicks?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? "unknown"}:{target.Window.HostedProcessId}";
    }
    private static void EnsureDesktop()
    {
        var desktop = Native.OpenInputDesktop(0, false, 1);
        if (desktop == 0) throw new UiObservationException("SecureDesktopUnavailable");
        try
        {
            var name = new StringBuilder(128);
            if (!Native.GetUserObjectInformation(desktop, 2, name, 256, out _) || name.ToString() != "Default")
                throw new UiObservationException("SecureDesktopUnavailable");
        }
        finally { Native.CloseDesktop(desktop); }
    }
    public Task<IReadOnlyList<WindowContext>> ListOpenWindows(CancellationToken ct = default) => Task.Run<IReadOnlyList<WindowContext>>(() =>
    {
        ct.ThrowIfCancellationRequested(); EnsureDesktop();
        return Probe.List().Where(w => !Self.IsAgentWindow(w.Handle)).ToArray();
    }, ct);
    public async Task<WindowContext?> GetForegroundWindow(CancellationToken ct = default)
    {
        var handle = (long)Native.GetForegroundWindow();
        logger?.LogInformation("UiWin32 Stage=GetForegroundWindow Handle={Handle} Valid={Valid}", handle, Native.IsWindow((nint)handle));
        return await GetWindowInfo(handle, ct);
    }
    public Task<WindowContext?> GetWindowInfo(long handle, CancellationToken ct = default) => Task.Run(() =>
    {
        ct.ThrowIfCancellationRequested(); EnsureDesktop();
        return Probe.Read(handle);
    }, ct);
    private WindowContext ReadWindow(OpenWindowInfo w)
    {
        var apps = catalog.Applications.Where(a => a.ProcessName?.Equals(w.ProcessName, StringComparison.OrdinalIgnoreCase) == true).Take(2).ToArray();
        var restricted = IsSensitiveWindow(w.ProcessName, w.Title);
        return new((long)w.Handle, w.ProcessId, Safe(w.ProcessName) ?? "Unknown", apps.Length == 1 ? apps[0].Id : null,
            restricted ? "[PROTECTED WINDOW]" : Safe(w.Title), w.Bounds, w.IsVisible, Native.IsWindowEnabled(w.Handle),
            w.State == ExternalWindowState.Minimized, w.State == ExternalWindowState.Maximized,
            Native.GetForegroundWindow() == w.Handle, null, DateTimeOffset.UtcNow);
    }
    public static bool IsSensitiveWindow(string process, string title) =>
        new[] { "consent", "credentialuibroker", "logonui", "winlogon", "keepass", "1password", "bitwarden", "sechealthui", "securityhealthhost", "smartscreen",
            "whatsapp", "telegram", "discord", "slack", "ms-teams", "signal", "outlook" }.Contains(process.ToLowerInvariant()) ||
        new[] { "password", "sign in", "sign-in", "log in", "login", "authentication", "api key", "bank", "wallet", "credit card", "private chat", "private messages",
            "whatsapp", "messenger", "gmail", "windows security", "user account control" }
            .Any(word => title.Contains(word, StringComparison.OrdinalIgnoreCase));
    private string? Safe(string? value)
    {
        if (value is null) return null;
        value = value.Length > 256 ? value[..256] : value;
        return secrets.Redact(value, out _);
    }
    public async Task<UiSnapshot> GetWindowTree(long? handle, UiInspectionOptions? options = null, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        var correlation = "UI-" + DateTimeOffset.UtcNow.ToString("yyyyMMdd-HHmmss") + "-" + Guid.NewGuid().ToString("N")[..8];
        var duration = Stopwatch.StartNew();
        var identity = WindowIdentity(handle);
        var target = long.Parse(identity.Split(':')[0], System.Globalization.CultureInfo.InvariantCulture);
        var window = await GetWindowInfo(target, ct) ?? throw new UiObservationException("WindowGone");
        if (window.Title == "[PROTECTED WINDOW]") throw new UiObservationException("SensitiveWindowBlocked");
        var o = (options ?? new()).Bounded();
        if (window.ProcessName.ToLowerInvariant() is "chrome" or "msedge" or "firefox" or "code")
            o = o with { MaximumDepth = Math.Min(o.MaximumDepth, 4), MaximumElements = Math.Min(o.MaximumElements, 100) };
        Interlocked.Exchange(ref _target, target);
        var epoch = Interlocked.Read(ref _epoch);
        logger?.LogInformation("UiInspection CorrelationId={CorrelationId} Stage=TargetResolution Handle={Handle} ProcessId={ProcessId} HostedProcessId={HostedProcessId}", correlation, target, window.ProcessId, window.HostedProcessId);
        var result = await OnWorker(token => Capture(window, o, identity, epoch, token, correlation), ct, correlation);
        logger?.LogInformation("UiInspection CorrelationId={CorrelationId} Stage=Completed DurationMs={DurationMs} Status={Status}", correlation, duration.Elapsed.TotalMilliseconds, result.Status);
        return result;
    }
    private async Task<T> OnWorker<T>(Func<CancellationToken, T> action, CancellationToken ct, string? correlation = null)
    {
        if (_disposed) throw new UiObservationException("Unavailable");
        Interlocked.Increment(ref _inspectionRequests); Changed?.Invoke(this, EventArgs.Empty);
        try { return await _worker.RunAsync(action, ct); }
        catch (ElementNotAvailableException e) { throw new UiObservationException("AutomationElementUnavailable", e); }
        catch (UnauthorizedAccessException e) { throw new UiObservationException("AccessDenied", e); }
        catch (Exception e) when (e is not (OperationCanceledException or UiObservationException))
        {
            var category = e is NotSupportedException ? "UnsupportedProvider" : e.HResult == unchecked((int)0x80070005) ? "AccessDenied" : "Unknown";
            logger?.LogWarning("UiProviderFailure CorrelationId={CorrelationId} Category={Category} ErrorType={ErrorType} HResult={HResult}", correlation, category, e.GetType().Name, e.HResult);
            throw new UiObservationException(category, e);
        }
        catch (UiObservationException e) { logger?.LogWarning("UiInspectionFailure CorrelationId={CorrelationId} Category={Category}", correlation, e.Code); throw; }
        finally { Interlocked.Decrement(ref _inspectionRequests); Changed?.Invoke(this, EventArgs.Empty); }
    }
    private UiSnapshot Capture(WindowContext window, UiInspectionOptions o, string identity, long epoch, CancellationToken ct, string correlation)
    {
        var refs = new Dictionary<string, int[]>(); var count = 0; var truncated = false;
        logger?.LogInformation("UiInspection CorrelationId={CorrelationId} Stage=FromHandle Handle={Handle} ProcessId={ProcessId}", correlation, window.Handle, window.ProcessId);
        var root = AutomationElement.FromHandle((nint)window.Handle);
        logger?.LogInformation("UiInspection CorrelationId={CorrelationId} Stage=FromHandleComplete RootAvailable={RootAvailable}", correlation, root is not null);
        if (root is null) throw new UiObservationException("AutomationElementUnavailable");
        var walker = TreeWalker.ControlViewWalker;
        object? Read(AutomationElement e, AutomationProperty property)
        {
            ct.ThrowIfCancellationRequested();
            object value;
            try { value = e.GetCurrentPropertyValue(property, true); }
            catch (Exception ex) when (ex is InvalidOperationException or NotSupportedException)
            { logger?.LogWarning("UiPropertyFailure Category=UnsupportedProvider PropertyId={PropertyId} ErrorType={ErrorType} HResult={HResult}", property.Id, ex.GetType().Name, ex.HResult); return null; }
            return value == AutomationElement.NotSupported ? null : value;
        }
        bool? Bool(AutomationElement e, AutomationProperty p) => Read(e, p) as bool?;
        string? Text(AutomationElement e, AutomationProperty p) => Safe(Read(e, p) as string);
        UiElement Visit(AutomationElement element, string? parent, int depth)
        {
            ct.ThrowIfCancellationRequested(); EnsureDesktop();
            var id = "ui:" + ++count;
            var password = Bool(element, AutomationElement.IsPasswordProperty);
            var type = (Read(element, AutomationElement.ControlTypeProperty) as ControlType)?.ProgrammaticName.Replace("ControlType.", "");
            var hiddenText = type is "Edit" or "Text" or "Document" || password is null;
            // Password property FIRST. Never access ValuePattern.Value or TextPattern.DocumentRange.
            var name = password == true ? "Password field" : hiddenText ? "[TEXT OMITTED]" : Text(element, AutomationElement.NameProperty);
            var rect = Read(element, AutomationElement.BoundingRectangleProperty) as System.Windows.Rect?;
            var item = new UiElement {
                Id = id, ParentId = parent, ControlType = type, IsPassword = password, Name = name,
                Value = password == true ? "[PROTECTED]" : null,
                AutomationId = password == true ? null : Text(element, AutomationElement.AutomationIdProperty),
                ClassName = password == true ? null : Text(element, AutomationElement.ClassNameProperty),
                Bounds = rect is { IsEmpty: false } r && double.IsFinite(r.X) && double.IsFinite(r.Y) && double.IsFinite(r.Width) && double.IsFinite(r.Height)
                    ? new((int)r.X, (int)r.Y, (int)r.Width, (int)r.Height) : null,
                IsEnabled = Bool(element, AutomationElement.IsEnabledProperty), IsVisible = Bool(element, AutomationElement.IsOffscreenProperty) is { } off ? !off : null,
                IsFocused = Bool(element, AutomationElement.HasKeyboardFocusProperty),
                CanInvoke = Bool(element, AutomationElement.IsInvokePatternAvailableProperty), CanToggle = Bool(element, AutomationElement.IsTogglePatternAvailableProperty),
                CanExpandCollapse = Bool(element, AutomationElement.IsExpandCollapsePatternAvailableProperty), CanSelect = Bool(element, AutomationElement.IsSelectionItemPatternAvailableProperty),
                CanSetValue = Bool(element, AutomationElement.IsValuePatternAvailableProperty), CanScroll = Bool(element, AutomationElement.IsScrollPatternAvailableProperty),
                CanSetRangeValue = Bool(element, AutomationElement.IsRangeValuePatternAvailableProperty)
            };
            if (item.CanSetValue == true)
                item = item with { CanSetValue = Bool(element, ValuePattern.IsReadOnlyProperty) is { } readOnly ? !readOnly : null };
            if (item.CanSelect == true) item = item with { IsSelected = Bool(element, SelectionItemPattern.IsSelectedProperty) };
            if (type == "Window" && Bool(element, WindowPattern.IsModalProperty) == true) item = item with { ControlType = "Dialog" };
            ct.ThrowIfCancellationRequested(); refs[id] = element.GetRuntimeId();
            var children = new List<UiElement>();
            if (password != true && depth < o.MaximumDepth && count < o.MaximumElements)
            {
                ct.ThrowIfCancellationRequested(); var child = walker.GetFirstChild(element);
                while (child is not null)
                {
                    ct.ThrowIfCancellationRequested();
                    if (count >= o.MaximumElements) { truncated = true; break; }
                    children.Add(Visit(child, id, depth + 1));
                    ct.ThrowIfCancellationRequested(); child = walker.GetNextSibling(child);
                }
            }
            else if (password != true) truncated = true; // Cannot assert absence beyond an unvisited depth/count boundary.
            return item with { Children = children };
        }
        logger?.LogInformation("UiInspection CorrelationId={CorrelationId} Stage=TraversalBegin MaxDepth={MaxDepth} MaxElements={MaxElements}", correlation, o.MaximumDepth, o.MaximumElements);
        var tree = Visit(root, null, 0);
        logger?.LogInformation("UiInspection CorrelationId={CorrelationId} Stage=TraversalComplete Elements={Elements}", correlation, count);
        ct.ThrowIfCancellationRequested();
        if (identity != WindowIdentity(window.Handle)) throw new UiObservationException("WindowGone");
        var currentWindow = Probe.Read(window.Handle) ?? throw new UiObservationException("WindowClosed");
        if (currentWindow.Title == "[PROTECTED WINDOW]") throw new UiObservationException("SensitiveWindowBlocked");
        var all = UiAlgorithms.Flatten(tree).ToArray();
        var focused = all.FirstOrDefault(e => e.IsFocused == true);
        // Focus can lie outside a bounded tree. Retrieve it only if it belongs to this target process.
        if (focused is null && count < o.MaximumElements && Native.GetForegroundWindow() == (nint)window.Handle)
        {
            try
            {
            logger?.LogInformation("UiInspection Stage=FocusedElementBegin");
            ct.ThrowIfCancellationRequested(); var nativeFocus = AutomationElement.FocusedElement;
            if (nativeFocus is not null && Read(nativeFocus, AutomationElement.ProcessIdProperty) as int? is { } focusPid &&
                (focusPid == window.ProcessId || focusPid == window.HostedProcessId))
            {
                var ancestor = nativeFocus; var rootRuntime = root.GetRuntimeId(); var belongs = false;
                for (var i = 0; ancestor is not null && i < 12; i++)
                {
                    ct.ThrowIfCancellationRequested();
                    if (ancestor.GetRuntimeId().SequenceEqual(rootRuntime)) { belongs = true; break; }
                    ancestor = walker.GetParent(ancestor);
                }
                if (belongs) focused = Visit(nativeFocus, null, o.MaximumDepth) with { Children = [] };
            }
            }
            catch (Exception ex) when (ex is ElementNotAvailableException or InvalidOperationException or NotSupportedException or UnauthorizedAccessException or COMException)
            { logger?.LogWarning("UiInspection Stage=FocusedElement Category=Unavailable ErrorType={ErrorType} HResult={HResult}", ex.GetType().Name, ex.HResult); }
        }
        var filtered = all.Where(e => UiAlgorithms.MatchesFilter(e, o)).ToArray();
        var dialogs = all.Where(e => e.ControlType == "Dialog" || e.ClassName == "#32770")
            .Select(e => new DialogContext(e.Name, window.ProcessName, "Application dialog", null,
                UiAlgorithms.Flatten(e).Where(x => x.ControlType == "Button").Take(16).ToArray(), null,
                e.ControlType == "Dialog" ? UiDialogRisk.Normal : UiDialogRisk.Unknown)).Take(8).ToArray();
        var snapshot = new UiSnapshot(Guid.NewGuid(), DateTimeOffset.UtcNow, window with { RootElement = tree, CapturedAt = DateTimeOffset.UtcNow, IsForeground = Native.GetForegroundWindow() == (nint)window.Handle },
            Native.GetForegroundWindow() == (nint)window.Handle ? window with { IsForeground = true } : null,
            focused, dialogs, filtered, filtered.Where(e => e.IsInteractive).ToArray(), truncated,
            all.Length <= 1 ? "Limited" : "Success", all.Length <= 1 ? "Limited UI information available." :
                "Text/value content omitted for privacy. Results describe a bounded snapshot, not all controls.");
        ct.ThrowIfCancellationRequested();
        lock (_sync)
        {
            if (epoch != Interlocked.Read(ref _epoch)) throw new UiObservationException("StaleSnapshot");
            _last = snapshot; _lastIdentity = identity; _references = refs;
            RememberActionReferences(snapshot, identity, refs);
        }
        return snapshot;
    }
    public async Task<UiElement?> GetFocusedElement(long? handle, CancellationToken ct = default) => (await GetWindowTree(handle, ct: ct)).FocusedElement;
    public async Task<IReadOnlyList<UiElement>> GetUiElements(long? handle, UiInspectionOptions options, CancellationToken ct = default) => (await GetWindowTree(handle, options, ct)).Elements;
    public async Task<UiSearchResult> FindElement(long? handle, UiElementQuery query, CancellationToken ct = default) => UiAlgorithms.Search(await GetWindowTree(handle, ct: ct), query);
    public async Task<IReadOnlyList<DialogContext>> GetDialogs(long? handle, CancellationToken ct = default) => (await GetWindowTree(handle, ct: ct)).Dialogs;
    public async Task<IReadOnlyList<UiElement>> GetInteractiveElements(long? handle, CancellationToken ct = default) => (await GetWindowTree(handle, new(InteractiveOnly: true), ct)).InteractiveElements;
    public async Task<UiElement> GetElementProperties(Guid snapshot, string reference, CancellationToken ct = default)
    {
        UiSnapshot previous; int[] runtime; string identity;
        lock (_sync)
        {
            if (_last is null || _last.SnapshotVersion != snapshot || !UiAlgorithms.IsFresh(_last, DateTimeOffset.UtcNow) || !_references.TryGetValue(reference, out var value))
                throw new UiObservationException("StaleElement");
            previous = _last; runtime = value; identity = _lastIdentity!;
        }
        if (identity != WindowIdentity(previous.Window.Handle)) throw new UiObservationException("StaleElement");
        var next = await GetWindowTree(previous.Window.Handle, ct: ct);
        lock (_sync)
        {
            if (!UiAlgorithms.IsFresh(previous, DateTimeOffset.UtcNow) || _last?.SnapshotVersion != next.SnapshotVersion) throw new UiObservationException("StaleElement");
            var id = _references.FirstOrDefault(p => p.Value.SequenceEqual(runtime)).Key;
            return next.Elements.Append(next.FocusedElement).FirstOrDefault(e => e?.Id == id) ?? throw new UiObservationException("StaleElement");
        }
    }
    public void Dispose()
    {
        _disposed = true; _worker.Dispose(); Invalidate();
        foreach (var hook in _hooks) Native.UnhookWinEvent(hook);
        _hooks.Clear();
    }
    private static class Native
    {
        internal delegate void WinEventDelegate(nint hook, uint evt, nint window, int obj, int child, uint thread, uint time);
        [DllImport("user32.dll")] internal static extern nint GetForegroundWindow();
        [DllImport("user32.dll")] internal static extern bool IsWindow(nint handle);
        [DllImport("user32.dll")] internal static extern bool IsWindowEnabled(nint handle);
        [DllImport("user32.dll")] internal static extern uint GetWindowThreadProcessId(nint handle, out uint pid);
        [DllImport("user32.dll")] internal static extern nint OpenInputDesktop(uint flags, bool inherit, uint access);
        [DllImport("user32.dll", CharSet = CharSet.Unicode, EntryPoint = "GetUserObjectInformationW")] internal static extern bool GetUserObjectInformation(nint obj, int index, StringBuilder info, uint length, out uint needed);
        [DllImport("user32.dll")] internal static extern bool CloseDesktop(nint desktop);
        [DllImport("user32.dll")] internal static extern nint SetWinEventHook(uint start, uint end, nint module, WinEventDelegate callback, uint pid, uint thread, uint flags);
        [DllImport("user32.dll")] internal static extern bool UnhookWinEvent(nint hook);
    }
}

public sealed class UiSnapshotService(WindowsUiAutomationService automation) : IUiSnapshotService
{
    // Deliberately no automatic reuse: a caller always gets a new bounded observation.
    public Task<UiSnapshot> CaptureAsync(long? handle = null, UiInspectionOptions? options = null, CancellationToken ct = default) => automation.GetWindowTree(handle, options, ct);
    public void Invalidate() => automation.Invalidate();
}
