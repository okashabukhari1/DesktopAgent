using System.Collections.Concurrent;
using System.Text.Json;
using DesktopAgent.Core.ComputerUse;
using DesktopAgent.Core.Interfaces;
using DesktopAgent.Core.UiAwareness;
using DesktopAgent.Infrastructure.UiAwareness;

namespace DesktopAgent.Infrastructure.ComputerUse;

/// <summary>Validated Notepad execution lane beneath the existing ComputerActionExecutor policy.</summary>
public sealed class PythonComputerBackend(WindowsUiAutomationService native, ISettingsService settings) : IComputerInteractionBackend, IAsyncDisposable, IDisposable
{
    private readonly ConcurrentDictionary<Guid, PythonWindowTarget> pins = new();
    private readonly SemaphoreSlim clientLane = new(1, 1);
    private PythonWorkerClient? client;
    private string? interpreter;
    public string Status { get; private set; } = "Not checked";
    public string? PythonVersion { get; private set; }
    public string? LastError { get; private set; }
    public static string WorkerPath
    {
        get
        {
            var packaged = Path.Combine(AppContext.BaseDirectory, "python-worker", "worker.py");
            if (File.Exists(packaged)) return packaged;
            var directory = new DirectoryInfo(AppContext.BaseDirectory);
            for (var depth = 0; directory is not null && depth < 10; depth++, directory = directory.Parent)
            {
                var source = Path.Combine(directory.FullName, "src", "DesktopAgent.PythonWorker", "worker.py");
                if (File.Exists(source)) return source;
            }
            return packaged;
        }
    }
    private async Task<PythonWorkerResult> Send(PythonWorkerAction action, PythonWindowTarget? target, string? text, CancellationToken ct, string? key = null, int scrollDirection = 0)
    {
        await clientLane.WaitAsync(ct);
        try
        {
            var path = PythonWorkerClient.DiscoverPython(settings.Current.ComputerUse.PythonExecutable);
            if (path is null)
            {
                LastError = "MissingPython"; Status = "Unavailable";
                return new("local", "Failed", false, false, LastError);
            }
            if (client is null || interpreter != path)
            {
                if (client is not null) await client.DisposeAsync();
                interpreter = path;
                client = new(path, WorkerPath, allowDevelopmentInput: true);
            }
            Status = "Running";
            var result = await client.SendAsync(action, target, text, ct: ct, key: key, scrollDirection: scrollDirection);
            LastError = result.Error;
            Status = result.Verified ? "Ready" : result.Status;
            return result;
        }
        finally { clientLane.Release(); }
    }
    public async Task<PythonWorkerResult> HealthAsync(CancellationToken ct = default)
    {
        var result = await Send(PythonWorkerAction.Health, null, null, ct);
        if (result.Verified && result.Data is { } data && data.TryGetProperty("python_version", out var version)) PythonVersion = version.GetString();
        return result;
    }
    public async Task<PreparedComputerTarget> PrepareAsync(ComputerAction action, CancellationToken ct)
    {
        var shortcut = action.Type is ComputerActionType.PressKey or ComputerActionType.PressShortcut;
        var scroll = action.Type == ComputerActionType.ScrollWindow;
        if (action.Type != ComputerActionType.TypeText && !shortcut && !scroll) throw new UiObservationException("PythonUnsupportedAction");
        if (shortcut && !PythonWorkerClient.ShortcutKeys.Contains(action.Key ?? "")) throw new UiObservationException("PythonUnsupportedShortcut");
        var chrome = action.WindowReference.Equals("window:Google Chrome", StringComparison.OrdinalIgnoreCase) || action.WindowReference.Equals("window:Chrome", StringComparison.OrdinalIgnoreCase);
        var observed = shortcut || scroll || chrome ? native.GetWindowActionTarget(action) : native.GetObservedActionTarget(action, usePinnedNotepadObservation: true);
        if (observed.Window.ApplicationName is not ("Google Chrome" or "Notepad") || scroll && !chrome) throw new UiObservationException("PythonUnsupportedApplication");
        if (observed.Element.IsPassword == true) throw new UiObservationException("PythonProtectedEditor");
        if (!shortcut && !scroll && !chrome && observed.Element.ControlType is not ("Edit" or "Document")) throw new UiObservationException("PythonEditorRequired");
        if (pins.Count >= 16) pins.Clear();
        var target = new PythonWindowTarget(Application: chrome ? "Chrome" : "Notepad", Hwnd: observed.Window.Handle, Pid: observed.Window.ProcessId,
            Title: observed.Window.Title, EditorRuntime: shortcut || scroll || chrome ? null : observed.RuntimeId);
        var inspectDocument = scroll || chrome && shortcut;
        var result = await Send(inspectDocument ? PythonWorkerAction.InspectScroll : PythonWorkerAction.InspectEditor, target, null, ct);
        if (!result.Verified || result.Data is not { } data) throw new UiObservationException(result.Error ?? "PythonInspectionFailed");
        var identity = data.GetProperty("target");
        if (!data.GetProperty("is_visible").GetBoolean() || !data.GetProperty("is_enabled").GetBoolean() ||
            data.GetProperty("is_password").GetBoolean() || !inspectDocument && data.GetProperty("is_readonly").GetBoolean() ||
            !shortcut && !scroll && !chrome && (data.GetProperty("control_type").GetString() != observed.Element.ControlType ||
            data.GetProperty("class_name").GetString() != observed.Element.ClassName ||
            !data.GetProperty("editor_runtime").EnumerateArray().Select(x => x.GetInt32()).SequenceEqual(observed.RuntimeId)) ||
            native.WindowIdentity(observed.Window.Handle) != observed.Identity)
            throw new UiObservationException("PythonEditorIdentityChanged");
        var runtime = data.GetProperty("editor_runtime").EnumerateArray().Select(x => x.GetInt32()).ToArray();
        pins[action.Id] = target with { Created = identity.GetProperty("created").GetInt64(), EditorRuntime = runtime };
        // Unknown native password state is replaced only by an explicit raw Python UIA false
        // for this exact runtime/class/type identity, never by a default or top-level assumption.
        return observed with { RuntimeId = runtime, Element = observed.Element with { IsPassword = false,
            ControlType = data.GetProperty("control_type").GetString(), ClassName = data.GetProperty("class_name").GetString() } };
    }
    public async Task<ComputerInteractionResult> ExecuteAsync(ComputerAction action, PreparedComputerTarget target, Action guard, CancellationToken ct)
    {
        if (!pins.TryRemove(action.Id, out var pin)) throw new UiObservationException("PythonPreparedTargetMissing");
        guard();
        if (native.WindowIdentity(target.Window.Handle) != target.Identity) throw new UiObservationException("StaleElement");
        var foreground = native.Probe.ForegroundHandle;
        if (foreground != target.Window.Handle && !native.Self.IsAgentWindow(foreground)) throw new UiObservationException("UserFocusChanged");
        using var interruption = CancellationTokenSource.CreateLinkedTokenSource(ct);
        async Task Monitor()
        {
            try
            {
                while (!interruption.IsCancellationRequested)
                {
                    guard();
                    await Task.Delay(10, interruption.Token);
                }
            }
            catch (OperationCanceledException) { interruption.Cancel(); }
            catch (Exception) { interruption.Cancel(); }
        }
        var monitor = Monitor();
        PythonWorkerResult result;
        try { result = await Send(action.Type == ComputerActionType.ScrollWindow ? PythonWorkerAction.Scroll : action.Type == ComputerActionType.TypeText ? PythonWorkerAction.TypeText : PythonWorkerAction.PressShortcut,
            pin, action.Text, interruption.Token, action.Key, action.Type == ComputerActionType.ScrollWindow ? action.Amount : 0); }
        finally { interruption.Cancel(); await monitor; }
        native.Invalidate();
        return new(action.Id, result.Verified, result.Status,
            result.Verified ? action.Key == "Ctrl+C" ? "Clipboard update observed after Ctrl+C; exact copied content was not read or verified."
                : "Verified " + action.Type + " in " + target.Window.ApplicationName + " using Python." : "Python action stopped: " + (result.Error ?? result.Status) + ". No automatic retry.",
            target.Window.ApplicationName, Verified: result.Verified, ErrorCode: result.Error, InputMayHaveOccurred: result.InputDispatched);
    }
    public async ValueTask DisposeAsync()
    {
        await clientLane.WaitAsync();
        try { if (client is not null) await client.DisposeAsync(); client = null; }
        finally { clientLane.Release(); }
    }
    public void Dispose() => DisposeAsync().AsTask().GetAwaiter().GetResult();
}
