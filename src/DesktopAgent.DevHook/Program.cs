using System.IO.Pipes;
using System.Text.Json;
using DesktopAgent.Infrastructure.Developer;

// Nothing on stdout and exit 0 means no decision: Claude's native permission flow remains in control.
// A whole-process watchdog also bounds stdin and stalled native I/O, not just cancellation-aware operations.
if (args is not ["--desktop-agent-hook-v1"] and not ["--desktop-agent-opencode-v1"]) return;
var source = args[0] == "--desktop-agent-opencode-v1" ? "opencode" : "claude-code";
var classified = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
var operation = Task.Run(() => RelayAsync(classified, source));
var first = await Task.WhenAny(operation, classified.Task, Task.Delay(TimeSpan.FromSeconds(3)));
if (first == classified.Task) await Task.WhenAny(operation, Task.Delay(TimeSpan.FromSeconds(await classified.Task ? 47 : 2)));
Environment.Exit(0);

static async Task RelayAsync(TaskCompletionSource<bool> classified, string source)
{
    try
    {
        using var inputDeadline = new CancellationTokenSource(TimeSpan.FromSeconds(2));
        using var input = Console.OpenStandardInput();
        using var buffer = new MemoryStream();
        var chunk = new byte[4096];
        int n;
        while ((n = await input.ReadAsync(chunk, inputDeadline.Token)) > 0)
        {
            if (buffer.Length + n > DeveloperPipeProtocol.MaxBytes) return;
            await buffer.WriteAsync(chunk.AsMemory(0, n), inputDeadline.Token);
        }
        var raw = buffer.ToArray();
        var offset = raw.AsSpan().StartsWith(new byte[] { 239, 187, 191 }) ? 3 : 0;
        using var json = JsonDocument.Parse(raw.AsMemory(offset), new() { MaxDepth = 16 });
        var field = source == "claude-code" ? "hook_event_name" : "event_type";
        if (!json.RootElement.TryGetProperty(field, out var hook) || hook.ValueKind != JsonValueKind.String) return;
        var permission = source == "claude-code" && hook.GetString() == "PermissionRequest";
        classified.TrySetResult(permission);
        using var deadline = new CancellationTokenSource(permission ? TimeSpan.FromSeconds(47) : TimeSpan.FromSeconds(2));
        using var pipe = new NamedPipeClientStream(".", DeveloperPipeProtocol.PipeName, PipeDirection.InOut, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
        await pipe.ConnectAsync(300, deadline.Token);
        if (!DeveloperPipeProtocol.PeerInCurrentSession(pipe, false)) return;
        await DeveloperPipeProtocol.WriteAsync(pipe, JsonSerializer.SerializeToUtf8Bytes(new { source, payload = json.RootElement }), deadline.Token);
        var response = await DeveloperPipeProtocol.ReadAsync(pipe, 1024, deadline.Token);
        using var reply = JsonDocument.Parse(response);
        if (!permission || !reply.RootElement.TryGetProperty("Decision", out var decision)) return;
        var behavior = decision.ValueKind == JsonValueKind.String ? decision.GetString() : null;
        if (behavior is not ("allow" or "deny")) return;
        // Never update persistent permissions, rewrite tool input, or turn on bypass mode.
        Console.WriteLine(JsonSerializer.Serialize(new { hookSpecificOutput = new { hookEventName = "PermissionRequest",
            decision = new { behavior, message = behavior == "deny" ? "Denied once by Desktop Agent user." : null } } }));
    }
    catch (Exception) { /* failure-safe: no payload, credentials or exception text in stdout/stderr */ }
}
