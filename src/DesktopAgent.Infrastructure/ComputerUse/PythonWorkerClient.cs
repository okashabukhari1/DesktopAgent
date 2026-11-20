using System.Diagnostics;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace DesktopAgent.Infrastructure.ComputerUse;

public sealed record PythonWindowTarget(string Application = "Notepad", long? Hwnd = null,
    int? Pid = null, long? Created = null, string? Title = null, int[]? EditorRuntime = null);
public sealed record PythonWorkerResult(string Id, string Status, bool InputDispatched, bool Verified,
    string? Error = null, JsonElement? Data = null);
public enum PythonWorkerAction { Health, FindWindow, FocusWindow, GetWindowInfo, GetFocusedControl, InspectEditor, TypeText, PressShortcut, InspectScroll, Scroll }

/// <summary>Private child-process transport. Not registered as a production execution backend yet.</summary>
public sealed class PythonWorkerClient(string pythonExecutable, string workerPath,
    bool allowDevelopmentInput = false) : IAsyncDisposable
{
    private readonly SemaphoreSlim lane = new(1, 1);
    private Process? process;
    private bool disposed;
    private static readonly JsonSerializerOptions Json = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        MaxDepth = 12
    };
    private static readonly HashSet<string> Statuses = ["CompletedAndVerified", "InputDispatchedUnverified",
        "Failed", "Cancelled", "TimedOut", "TargetAmbiguous", "PermissionDenied"];
    public static readonly HashSet<string> ShortcutKeys = ["Ctrl+A", "Ctrl+C", "Ctrl+F", "Ctrl+S", "Escape", "Enter", "Tab", "Shift+Tab", "Left", "Right", "Up", "Down"];

    public static string? DiscoverPython(string? configured = null)
    {
        if (!string.IsNullOrWhiteSpace(configured))
            return Path.IsPathFullyQualified(configured) && File.Exists(configured) ? Path.GetFullPath(configured) : null;
        var root = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Python");
        try
        {
            if (Directory.Exists(root))
                foreach (var candidate in Directory.EnumerateDirectories(root).OrderDescending())
                {
                    var executable = Path.Combine(candidate, "python.exe");
                    if (File.Exists(executable)) return executable;
                }
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
        foreach (var entry in (Environment.GetEnvironmentVariable("PATH") ?? "").Split(Path.PathSeparator))
        {
            if (!Path.IsPathFullyQualified(entry) || entry.Contains("WindowsApps", StringComparison.OrdinalIgnoreCase)) continue;
            var candidate = Path.Combine(entry, "python.exe");
            if (File.Exists(candidate)) return candidate;
        }
        return null;
    }

    public async Task<PythonWorkerResult> SendAsync(PythonWorkerAction action, PythonWindowTarget? target = null,
        string? text = null, int timeoutMs = 10000, CancellationToken ct = default, string? key = null, int scrollDirection = 0)
    {
        var id = Guid.NewGuid().ToString("N");
        var mutation = action is PythonWorkerAction.TypeText or PythonWorkerAction.FocusWindow or PythonWorkerAction.PressShortcut or PythonWorkerAction.Scroll;
        if (!Enum.IsDefined(action) || timeoutMs is < 100 or > 15000 ||
            (action == PythonWorkerAction.Health && (target is not null || text is not null)) ||
            (action != PythonWorkerAction.Health && (target is null || target.Application is not ("Notepad" or "Chrome"))) ||
            (target is not null && (target.Hwnd is <= 0 || target.Pid is <= 0 || target.Created is <= 0 || target.Title?.Length > 256)) ||
            (action == PythonWorkerAction.TypeText ? !ValidText(text) : text is not null) ||
            (action == PythonWorkerAction.PressShortcut ? !ShortcutKeys.Contains(key ?? "") : key is not null) ||
            (action == PythonWorkerAction.Scroll ? scrollDirection is not (-1 or 1) : scrollDirection != 0))
            return new(id, "Failed", false, false, "InvalidArguments");
        if (mutation && !allowDevelopmentInput) return new(id, "PermissionDenied", false, false, "DevelopmentInputDisabled");
        try { await lane.WaitAsync(ct); }
        catch (OperationCanceledException) { return new(id, "Cancelled", false, false, "Cancelled"); }
        var sent = false;
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(ct);
        deadline.CancelAfter(timeoutMs + 1000);
        try
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            if (!File.Exists(pythonExecutable)) return new(id, "Failed", false, false, "MissingPython");
            if (!File.Exists(workerPath)) return new(id, "Failed", false, false, "MissingWorker");
            if (process is null || process.HasExited)
            {
                CloseWorker();
                var start = new ProcessStartInfo(pythonExecutable)
                {
                    UseShellExecute = false, CreateNoWindow = true,
                    RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true,
                    StandardInputEncoding = new UTF8Encoding(false), StandardOutputEncoding = Encoding.UTF8,
                    StandardErrorEncoding = Encoding.UTF8, WorkingDirectory = Path.GetDirectoryName(Path.GetFullPath(workerPath))!
                };
                start.ArgumentList.Add("-u");
                start.ArgumentList.Add(Path.GetFullPath(workerPath));
                if (allowDevelopmentInput) start.ArgumentList.Add("--allow-input-for-development");
                process = Process.Start(start) ?? throw new IOException();
                // Drain, never retain/log raw diagnostics or request content.
                _ = DrainAsync(process.StandardError);
            }
            var name = action switch
            {
                PythonWorkerAction.Health => "health", PythonWorkerAction.FindWindow => "find_window",
                PythonWorkerAction.FocusWindow => "focus_window", PythonWorkerAction.GetWindowInfo => "get_window_info",
                PythonWorkerAction.GetFocusedControl => "get_focused_control", PythonWorkerAction.InspectEditor => "inspect_editor",
                PythonWorkerAction.PressShortcut => "press_shortcut", PythonWorkerAction.InspectScroll => "inspect_scroll",
                PythonWorkerAction.Scroll => "scroll", _ => "type_text"
            };
            var request = JsonSerializer.Serialize(new { id, action = name,
                target = (object?)target ?? new { }, arguments = action == PythonWorkerAction.Scroll
                    ? (object)new { direction = scrollDirection == 1 ? "down" : "up", amount = 3 }
                    : key is not null ? (object)new { key } : text is null ? (object)new { } : new { text }, timeout_ms = timeoutMs }, Json);
            if (request.Length > 8192) return new(id, "Failed", false, false, "RequestTooLarge");
            sent = true; // Conservative: partial pipe writes may have delivered a request.
            await process.StandardInput.WriteLineAsync(request.AsMemory(), deadline.Token);
            await process.StandardInput.FlushAsync(deadline.Token);
            var line = await ReadBoundedAsync(process.StandardOutput, deadline.Token);
            var result = JsonSerializer.Deserialize<PythonWorkerResult>(line, Json);
            if (result is null || result.Id != id || !Statuses.Contains(result.Status) ||
                result.Verified != (result.Status == "CompletedAndVerified") ||
                result.Status == "InputDispatchedUnverified" && !result.InputDispatched)
                throw new JsonException();
            return result;
        }
        catch (OperationCanceledException)
        {
            // Kill only our child; never retry a potentially dispatched request.
            CloseWorker();
            return new(id, ct.IsCancellationRequested ? "Cancelled" : "TimedOut", sent && mutation, false, "WorkerTerminatedNoRetry");
        }
        catch (Exception error) when (error is IOException or JsonException or InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            CloseWorker();
            return new(id, "Failed", sent && mutation, false, "WorkerTransportFailure");
        }
        finally { lane.Release(); }
    }

    private static bool ValidText(string? text)
    {
        if (string.IsNullOrEmpty(text) || text.Length > 2000) return false;
        for (var index = 0; index < text.Length; index++)
        {
            var c = text[index];
            if (char.IsControl(c) && c is not ('\n' or '\t')) return false;
            if (char.IsHighSurrogate(c))
            {
                if (++index >= text.Length || !char.IsLowSurrogate(text[index])) return false;
            }
            else if (char.IsLowSurrogate(c)) return false;
        }
        return true;
    }
    private static async Task<string> ReadBoundedAsync(StreamReader reader, CancellationToken ct)
    {
        var result = new StringBuilder();
        var character = new char[1];
        while (await reader.ReadAsync(character.AsMemory(), ct) != 0)
        {
            if (character[0] == '\n') return result.ToString();
            if (result.Length >= 16384) throw new IOException("WorkerResponseTooLarge");
            result.Append(character[0]);
        }
        throw new IOException("WorkerExited");
    }
    private static async Task DrainAsync(StreamReader reader)
    {
        try { var buffer = new char[1024]; while (await reader.ReadAsync(buffer) > 0) { } }
        catch (Exception error) when (error is IOException or ObjectDisposedException) { }
    }
    private void CloseWorker()
    {
        if (process is null) return;
        try { if (!process.HasExited) process.Kill(); }
        catch (InvalidOperationException) { }
        catch (System.ComponentModel.Win32Exception) { }
        process.Dispose(); process = null;
    }
    public async ValueTask DisposeAsync()
    {
        await lane.WaitAsync();
        try
        {
            disposed = true;
            if (process is { HasExited: false })
            {
                process.StandardInput.Close();
                try { await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(1)); }
                catch (TimeoutException) { }
            }
            CloseWorker();
        }
        finally { lane.Release(); }
    }
}
