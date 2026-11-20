using DesktopAgent.Core.ComputerUse;
using DesktopAgent.Core.Interfaces;
using DesktopAgent.Core.UiAwareness;
using DesktopAgent.Core.Applications.Interfaces;

namespace DesktopAgent.Infrastructure.ComputerUse;

/// <summary>One user-request scope; STOP and takeover revoke it, including the planner's linked cancellation.</summary>
public sealed class ComputerUseSession(ISettingsService settings, IWindowTargetResolver? targets = null, IApplicationCatalog? catalog = null)
{
    private readonly object _sync = new();
    private CancellationTokenSource? _task;
    private long _inputEpoch;
    private int _actions, _failures, _visibleViews;
    private WindowReference? _initialWindow;
    private HashSet<string> _requestedApplications = new(StringComparer.OrdinalIgnoreCase);
    public Guid Id { get; private set; }
    public DateTimeOffset StartedAt { get; private set; }
    public string? Goal { get; private set; }
    public ComputerAction? CurrentAction { get; private set; }
    public int ActionCount => Volatile.Read(ref _actions);
    public int FailureCount => Volatile.Read(ref _failures);
    public event EventHandler? Changed;
    public ComputerTaskState State { get; private set; }
    public string Summary { get; private set; } = "Computer Use off";
    public bool EmergencyStopReady { get; set; }
    public bool InterruptionMonitorReady { get; set; }
    public bool IndicatorVisible { get; set; }
    public bool IsActive { get { lock (_sync) return _task is { IsCancellationRequested: false }; } }
    public CancellationToken Token { get { lock (_sync) return _task?.Token ?? new CancellationToken(true); } }
    public long InputEpoch => Interlocked.Read(ref _inputEpoch);
    public bool CanAct => settings.Current.ComputerUse is { Enabled: true, OnboardingAcknowledged: true, Mode: not ComputerUseMode.Observe }
        && EmergencyStopReady && InterruptionMonitorReady && Volatile.Read(ref _visibleViews) > 0 && (!IsActive || IndicatorVisible);
    public void ViewVisible(bool visible) { Interlocked.Add(ref _visibleViews, visible ? 1 : -1); if (!visible && Volatile.Read(ref _visibleViews) <= 0) Stop(); }
    public void Begin(CancellationToken parent, string? goal = null)
    {
        lock (_sync)
        {
            _task?.Cancel(); _task?.Dispose(); _task = CancellationTokenSource.CreateLinkedTokenSource(parent);
            _task.CancelAfter(TimeSpan.FromSeconds(settings.Current.ComputerUse.MaximumTaskSeconds)); _actions = _failures = 0;
            Id = Guid.NewGuid(); StartedAt = DateTimeOffset.UtcNow; Goal = goal; CurrentAction = null;
            _requestedApplications.Clear();
            foreach (var pair in new (string Alias, string Name)[] { ("Notepad", "Notepad"), ("Calculator", "Calculator"), ("Settings", "Settings"),
                ("Chrome", "Google Chrome"), ("VS Code", "Visual Studio Code"), ("Visual Studio Code", "Visual Studio Code"), ("File Explorer", "File Explorer") })
                if (Mentions(goal, pair.Alias)) _requestedApplications.Add(pair.Name);
            if (catalog is not null) foreach (var app in catalog.Applications)
                if (app.ProcessName is { } process && Mentions(goal, app.DisplayName)) _requestedApplications.Add(WindowContext.FriendlyName(process));
            try { _initialWindow = targets?.Resolve().Reference; } catch (UiObservationException) { _initialWindow = null; }
        }
        Update(ComputerTaskState.Planning, "Planning the requested computer task");
    }
    public void End()
    {
        if (IsActive && State is not (ComputerTaskState.Completed or ComputerTaskState.Failed or ComputerTaskState.Paused)) Update(ComputerTaskState.Failed, "Task ended without a verified completion report");
        lock (_sync) { _task?.Cancel(); }
    }
    public void Stop() { lock (_sync) _task?.Cancel(); Update(ComputerTaskState.Cancelled, "STOP — no further actions will be scheduled"); }
    public void UserInput()
    {
        Interlocked.Increment(ref _inputEpoch);
        // Approval interactions are expected. At the mutation boundary a new baseline is taken.
        if (IsActive && settings.Current.ComputerUse.PauseOnUserInput && State is not ComputerTaskState.WaitingForUser)
        { lock (_sync) _task?.Cancel(); Update(ComputerTaskState.Paused, "Paused for your input — send a new request to resume"); }
    }
    public void ReserveAction()
    {
        lock (_sync)
        {
            if (!CanAct || _task is null) throw new InvalidOperationException("ComputerUseUnavailable");
            _task.Token.ThrowIfCancellationRequested();
            if (++_actions > settings.Current.ComputerUse.MaximumActions || _failures >= settings.Current.ComputerUse.MaximumFailures)
                throw new InvalidOperationException("ComputerUseBudget");
        }
    }
    public void Guard(long epoch)
    {
        Token.ThrowIfCancellationRequested();
        if (!CanAct || settings.Current.ComputerUse.PauseOnUserInput && InputEpoch != epoch) throw new OperationCanceledException("User takeover");
    }
    public void Record(ComputerInteractionResult result)
    {
        lock (_sync) { if (!result.Success) _failures++; else _failures = 0; }
        if (!result.Success && result.InputMayHaveOccurred) { lock (_sync) _task?.Cancel(); }
        if (State is ComputerTaskState.Paused or ComputerTaskState.Cancelled) { Changed?.Invoke(this, EventArgs.Empty); return; }
        Update(result.Success ? ComputerTaskState.Observing : ComputerTaskState.Failed, result.Message);
    }
    public void SetAction(ComputerAction action) => CurrentAction = action with { Text = null };
    public bool Authorizes(PreparedComputerTarget target, ComputerAction? action = null)
    {
        if (string.IsNullOrWhiteSpace(Goal)) return false;
        if (action?.Text is { Length: > 0 } text && !Goal.Contains(text, StringComparison.OrdinalIgnoreCase)) return false;
        var name = target.Window.ApplicationName;
        // Explicitly named applications are allowed; otherwise pin the original external window, not a later focus change.
        if (_requestedApplications.Count > 0) return _requestedApplications.Contains(name);
        return _initialWindow is { } initial && initial.Handle == target.Window.Handle && initial.ProcessId == target.Window.ProcessId;
    }
    private static bool Mentions(string? goal, string name) => !string.IsNullOrWhiteSpace(goal) &&
        System.Text.RegularExpressions.Regex.IsMatch(goal, @"(?<!\w)" + System.Text.RegularExpressions.Regex.Escape(name) + @"(?!\w)", System.Text.RegularExpressions.RegexOptions.IgnoreCase, TimeSpan.FromMilliseconds(100));
    public void Update(ComputerTaskState state, string summary)
    {
        if (!IsActive && State is ComputerTaskState.Paused or ComputerTaskState.Cancelled && state is not (ComputerTaskState.Idle or ComputerTaskState.Planning or ComputerTaskState.Paused or ComputerTaskState.Cancelled)) return;
        State = state; Summary = summary; Changed?.Invoke(this, EventArgs.Empty);
    }
}
