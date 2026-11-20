namespace DesktopAgent.Core.UiAwareness;

// Tracking stores identity only, never titles, control names or values.
public sealed record WindowReference(long Handle, int ProcessId, long? ProcessStartTicks);
public interface IWin32WindowProbe
{
    long ForegroundHandle { get; }
    WindowReference? Reference(long handle);
    WindowContext? Read(long handle);
    IReadOnlyList<WindowContext> List();
}
public interface ISelfWindowRegistry
{
    bool IsAgentWindow(long handle);
    void Register(long handle);
    void Unregister(long handle);
}
public interface IActiveUserWindowTracker
{
    WindowReference? CurrentForegroundWindow { get; }
    WindowReference? LastExternalForegroundWindow { get; }
    void RefreshForeground();
}
public sealed class ActiveUserWindowTracker(IWin32WindowProbe probe, ISelfWindowRegistry self) : IActiveUserWindowTracker
{
    private readonly object _sync = new();
    private WindowReference? _current, _external;
    public WindowReference? CurrentForegroundWindow { get { lock (_sync) return _current; } }
    public WindowReference? LastExternalForegroundWindow { get { lock (_sync) return _external; } }
    public void RefreshForeground() => Observe(probe.ForegroundHandle);
    public void Observe(long handle)
    {
        var reference = probe.Reference(handle);
        lock (_sync)
        {
            _current = reference;
            if (reference is not null && !self.IsAgentWindow(handle)) _external = reference;
        }
    }
}
public sealed record WindowTargetResolution(WindowContext Window, WindowReference Reference, string Source);
public interface IWindowTargetResolver
{
    WindowTargetResolution Resolve(long? explicitHandle = null, string? application = null,
        WindowReference? conversationWindow = null, bool explicitlyInspectAgent = false);
}
public sealed class WindowTargetResolver(IWin32WindowProbe probe, ISelfWindowRegistry self,
    IActiveUserWindowTracker tracker) : IWindowTargetResolver
{
    public WindowTargetResolution Resolve(long? explicitHandle = null, string? application = null,
        WindowReference? conversationWindow = null, bool explicitlyInspectAgent = false)
    {
        tracker.RefreshForeground();
        if (explicitHandle is { } handle)
        {
            if (handle <= 0) throw new UiObservationException("InvalidHandle");
            if (!explicitlyInspectAgent && self.IsAgentWindow(handle)) throw new UiObservationException("AgentWindowSelected");
            return Try(probe.Reference(handle), "ExplicitWindow", explicitlyInspectAgent)
                ?? throw new UiObservationException("WindowClosed");
        }
        if (!string.IsNullOrWhiteSpace(application))
        {
            var matches = probe.List().Where(w => (explicitlyInspectAgent || !self.IsAgentWindow(w.Handle)) &&
                (w.ProcessName.Equals(application, StringComparison.OrdinalIgnoreCase) ||
                 w.ApplicationName.Equals(application, StringComparison.OrdinalIgnoreCase) ||
                 w.Title?.Equals(application, StringComparison.OrdinalIgnoreCase) == true)).Take(2).ToArray();
            if (matches.Length > 1) throw new UiObservationException("AmbiguousWindow");
            if (matches.Length == 0) throw new UiObservationException("WindowNotFound");
            return Try(probe.Reference(matches[0].Handle), "ExplicitApplication", explicitlyInspectAgent)
                ?? throw new UiObservationException("WindowClosed");
        }
        return Try(tracker.CurrentForegroundWindow, "CurrentExternalForeground")
            ?? Try(tracker.LastExternalForegroundWindow, "LastExternalForeground")
            ?? Try(conversationWindow, "ConversationWindow")
            ?? throw new UiObservationException("WindowNotFound");
    }
    private WindowTargetResolution? Try(WindowReference? expected, string source, bool allowSelf = false)
    {
        if (expected is null || !allowSelf && self.IsAgentWindow(expected.Handle)) return null;
        var current = probe.Reference(expected.Handle);
        if (current is null || current.ProcessId != expected.ProcessId ||
            expected.ProcessStartTicks is { } start && current.ProcessStartTicks != start) return null;
        var window = probe.Read(expected.Handle);
        if (window is null || window.ProcessId != current.ProcessId || !window.IsVisible) return null;
        return new(window, current, source);
    }
}
