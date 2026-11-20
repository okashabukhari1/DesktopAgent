using System.Text.Json;
using DesktopAgent.Core.UiAwareness;
using DesktopAgent.Infrastructure.UiAwareness;

internal static class Part62Tests
{
    public static (string Name, Func<Task> Run)[] Cases => [
        ("UI target Calculator to Agent preserves external window", () => Tracking()),
        ("UI target self window is excluded even with external-looking title", () => Tracking(true)),
        ("UI target closed external falls back to conversation", Closed),
        ("UI target reused HWND with changed PID is rejected", Reused),
        ("UI target explicit invalid HWND is distinguished", Invalid),
        ("UI target explicit application ambiguity asks rather than guesses", Ambiguous),
        ("UI target explicit selection outranks last external", Explicit),
        ("UI worker keeps same non-UI MTA thread alive", WorkerLifetime),
        ("UI provider failure categories are distinguishable", Categories)
    ];
    private static void Check(bool condition, string message) { if (!condition) throw new Exception(message); }
    private static WindowContext Window(long handle, int pid, string name) => new(handle, pid, name, null, "Calculator",
        new(0, 0, 300, 300), true, true, false, false, false, null, DateTimeOffset.UtcNow);
    internal sealed class Probe : IWin32WindowProbe
    {
        public long ForegroundHandle { get; set; } = 1;
        public Dictionary<long, WindowContext> Windows { get; } = new() { [1] = Window(1, 100, "CalculatorApp"), [2] = Window(2, 200, "DesktopAgent"), [3] = Window(3, 300, "Notepad") };
        public WindowReference? Reference(long h) => Windows.TryGetValue(h, out var w) ? new(h, w.ProcessId, w.ProcessId * 10L) : null;
        public WindowContext? Read(long h) => Windows.GetValueOrDefault(h);
        public IReadOnlyList<WindowContext> List() => Windows.Values.ToArray();
    }
    internal sealed class Self : ISelfWindowRegistry
    {
        public bool IsAgentWindow(long h) => h == 2;
        public void Register(long h) { }
        public void Unregister(long h) { }
    }
    private static (Probe P, ActiveUserWindowTracker T, WindowTargetResolver R) Setup()
    {
        var p = new Probe(); var self = new Self(); var t = new ActiveUserWindowTracker(p, self); t.RefreshForeground();
        return (p, t, new(p, self, t));
    }
    private static Task Tracking(bool sameTitle = false)
    {
        var (p, t, r) = Setup();
        if (sameTitle) p.Windows[2] = p.Windows[2] with { Title = "Calculator" };
        p.ForegroundHandle = 2; t.RefreshForeground();
        Check(t.CurrentForegroundWindow?.Handle == 2 && t.LastExternalForegroundWindow?.Handle == 1, "Agent overwrote external history");
        Check(r.Resolve().Window.ApplicationName == "Calculator" && r.Resolve().Source == "LastExternalForeground", "Wrong target");
        try { r.Resolve(2); throw new Exception("Own window selected"); } catch (UiObservationException e) { Check(e.Code == "AgentWindowSelected", "Wrong self error"); }
        return Task.CompletedTask;
    }
    private static Task Closed()
    {
        var (p, _, r) = Setup(); var context = p.Reference(3); p.ForegroundHandle = 2; p.Windows.Remove(1);
        Check(r.Resolve(conversationWindow: context).Window.Handle == 3, "Closed external did not fall back");
        return Task.CompletedTask;
    }
    private static Task Reused()
    {
        var (p, _, r) = Setup(); p.ForegroundHandle = 2; p.Windows[1] = Window(1, 999, "Other");
        try { r.Resolve(); throw new Exception("Reused HWND selected"); } catch (UiObservationException e) { Check(e.Code == "WindowNotFound", "Wrong reuse error"); }
        return Task.CompletedTask;
    }
    private static Task Invalid()
    {
        var (_, _, r) = Setup();
        foreach (var handle in new long[] { 0, -1, 99 })
        {
            try { r.Resolve(handle); throw new Exception("Invalid handle selected"); }
            catch (UiObservationException e) { Check(e.Code == (handle <= 0 ? "InvalidHandle" : "WindowClosed"), "Wrong invalid error"); }
        }
        return Task.CompletedTask;
    }
    private static Task Ambiguous()
    {
        var (p, _, r) = Setup(); p.Windows[4] = Window(4, 400, "Notepad");
        try { r.Resolve(application: "Notepad"); throw new Exception("Guessed ambiguous target"); }
        catch (UiObservationException e) { Check(e.Code == "AmbiguousWindow", "Wrong ambiguity error"); }
        return Task.CompletedTask;
    }
    private static Task Explicit()
    { var (_, _, r) = Setup(); Check(r.Resolve(3).Window.Handle == 3 && r.Resolve(application: "Notepad").Window.Handle == 3, "Explicit ignored"); return Task.CompletedTask; }
    private static async Task WorkerLifetime()
    {
        using var worker = new UiObservationWorker();
        var a = await worker.RunAsync(_ => (Environment.CurrentManagedThreadId, Thread.CurrentThread.GetApartmentState()));
        var b = await worker.RunAsync(_ => (Environment.CurrentManagedThreadId, Thread.CurrentThread.GetApartmentState()));
        Check(a == b && a.Item2 == ApartmentState.MTA && a.Item1 != Environment.CurrentManagedThreadId, "UIA apartment is transient or runs on caller");
    }
    private static Task Categories()
    {
        foreach (var category in new[] { "WindowNotFound", "WindowClosed", "AgentWindowSelected", "AccessDenied", "AutomationElementUnavailable", "UnsupportedProvider", "Timeout", "InvalidHandle", "SecureDesktop", "Unknown" })
        {
            var result = UiTools.Failure(category);
            Check(result.ErrorCode == category && !result.Message.Contains("the window closed, access was denied"), "Generic failure hid category");
        }
        Check(!JsonSerializer.Serialize(UiTools.Failure("Unknown")).Contains("password-value"), "Unexpected sensitive log"); return Task.CompletedTask;
    }
}
