using System.IO.Pipes;
using System.Text.Json;
using System.Text;
using DesktopAgent.Core.Developer;
using Microsoft.Extensions.Logging;

namespace DesktopAgent.Infrastructure.Developer;

public sealed class DeveloperPipeServer(IDeveloperEventBridge bridge, ILogger<DeveloperPipeServer> logger) : IDisposable
{
    private readonly CancellationTokenSource _stop = new();
    private Task[] _listeners = [];
    public bool Available { get; private set; }
    public void Start()
    {
        if (_listeners.Length > 0) return;
        var first = new NamedPipeServerStream(DeveloperPipeProtocol.PipeName, PipeDirection.InOut, 4,
            PipeTransmissionMode.Byte, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly | PipeOptions.FirstPipeInstance, 4096, 4096);
        _listeners = [Task.Run(() => ListenAsync(first)), .. Enumerable.Range(0, 3).Select(_ => Task.Run(() => ListenAsync(null)))];
    }
    private async Task ListenAsync(NamedPipeServerStream? initial)
    {
        try
        {
            while (!_stop.IsCancellationRequested)
            {
                // CurrentUserOnly checks both the user identity and elevation level on Windows.
                // The name also scopes the endpoint to this interactive Windows session.
                await using var pipe = initial ?? new NamedPipeServerStream(DeveloperPipeProtocol.PipeName, PipeDirection.InOut, 4,
                    PipeTransmissionMode.Byte, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly, 4096, 4096);
                initial = null;
                Available = true;
                await pipe.WaitForConnectionAsync(_stop.Token);
                if (!DeveloperPipeProtocol.PeerInCurrentSession(pipe, true)) continue;
                using var read = CancellationTokenSource.CreateLinkedTokenSource(_stop.Token); read.CancelAfter(TimeSpan.FromSeconds(2));
                try
                {
                    var payload = await DeveloperPipeProtocol.ReadAsync(pipe, DeveloperPipeProtocol.MaxBytes, read.Token);
                    using var envelope = JsonDocument.Parse(payload, new() { MaxDepth = 18 });
                    var root = envelope.RootElement;
                    if (root.ValueKind != JsonValueKind.Object || root.EnumerateObject().Count() != 2 ||
                        !root.TryGetProperty("source", out var source) || source.ValueKind != JsonValueKind.String || source.GetString() is not ("claude-code" or "opencode") ||
                        !root.TryGetProperty("payload", out var body) || body.ValueKind != JsonValueKind.Object) continue;
                    using var processing = CancellationTokenSource.CreateLinkedTokenSource(_stop.Token); processing.CancelAfter(TimeSpan.FromSeconds(46));
                    var response = bridge.ReceiveEventAsync(source.GetString()!, Encoding.UTF8.GetBytes(body.GetRawText()), processing.Token);
                    // Detect a relay that exits while its permission request is pending.
                    var disconnected = pipe.ReadAsync(new byte[1], processing.Token).AsTask();
                    if (await Task.WhenAny(response, disconnected) == disconnected) processing.Cancel();
                    var reply = await response;
                    processing.Cancel();
                    try { await disconnected; } catch (Exception ex) when (ex is OperationCanceledException or IOException) { }
                    using var write = CancellationTokenSource.CreateLinkedTokenSource(_stop.Token); write.CancelAfter(TimeSpan.FromSeconds(1));
                    await DeveloperPipeProtocol.WriteAsync(pipe, JsonSerializer.SerializeToUtf8Bytes(reply), write.Token);
                }
                catch (Exception ex) when (ex is IOException or OperationCanceledException or JsonException or UnauthorizedAccessException) { }
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception ex) { Available = false; logger.LogWarning("DeveloperPipeUnavailable Type={Type}", ex.GetType().Name); }
    }
    public void Dispose() { Available = false; _stop.Cancel(); }
}
