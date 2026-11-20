using DesktopAgent.Core.UiAwareness;
using System.Collections.Concurrent;

namespace DesktopAgent.Infrastructure.UiAwareness;

/// <summary>A single MTA provider lane. Cancellation bounds the caller; a stuck native call retains the lane until it returns.</summary>
public sealed class UiObservationWorker : IDisposable
{
    private readonly object _sync = new();
    private readonly TimeSpan _timeout;
    private CancellationTokenSource? _active;
    private int _busy;
    private bool _disposed;
    private readonly BlockingCollection<Action> _jobs = new(1);
    private readonly Thread _thread;
    public UiObservationWorker(TimeSpan? timeout = null)
    {
        _timeout = timeout ?? TimeSpan.FromSeconds(3);
        if (_timeout < TimeSpan.FromMilliseconds(10) || _timeout > TimeSpan.FromSeconds(3)) throw new ArgumentOutOfRangeException(nameof(timeout));
        _thread = new Thread(() => { foreach (var job in _jobs.GetConsumingEnumerable()) job(); })
            { IsBackground = true, Name = "Persistent Windows UIA MTA" };
        _thread.SetApartmentState(ApartmentState.MTA);
        _thread.Start();
    }
    public void Cancel() { lock (_sync) _active?.Cancel(); }
    public async Task<T> RunAsync<T>(Func<CancellationToken, T> action, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        if (_disposed) throw new UiObservationException("Unavailable");
        if (Interlocked.CompareExchange(ref _busy, 1, 0) != 0) throw new UiObservationException("ProviderBusy");
        var deadline = CancellationTokenSource.CreateLinkedTokenSource(ct);
        deadline.CancelAfter(_timeout); var token = deadline.Token;
        var completion = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
        lock (_sync) _active = deadline;
        Action work = () =>
        {
            try { token.ThrowIfCancellationRequested(); completion.TrySetResult(action(token)); }
            catch (OperationCanceledException) { completion.TrySetCanceled(); }
            catch (Exception e) { completion.TrySetException(e); }
            finally
            {
                lock (_sync) { if (ReferenceEquals(_active, deadline)) _active = null; }
                deadline.Dispose(); Interlocked.Exchange(ref _busy, 0);
            }
        };
        try { lock (_sync) { if (_disposed) throw new ObjectDisposedException(nameof(UiObservationWorker)); _jobs.Add(work); } }
        catch { lock (_sync) _active = null; deadline.Dispose(); Interlocked.Exchange(ref _busy, 0); throw; }
        _ = completion.Task.ContinueWith(t => _ = t.Exception, TaskContinuationOptions.OnlyOnFaulted);
        try { return await completion.Task.WaitAsync(token); }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested) { throw new UiObservationException("Timeout"); }
    }
    public void Dispose() { lock (_sync) { if (_disposed) return; _disposed = true; _active?.Cancel(); _jobs.CompleteAdding(); } }
}
