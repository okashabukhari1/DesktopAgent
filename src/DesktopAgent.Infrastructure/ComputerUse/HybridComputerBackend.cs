using System.Collections.Concurrent;
using DesktopAgent.Core.ComputerUse;
using Microsoft.Extensions.Logging;

namespace DesktopAgent.Infrastructure.ComputerUse;

/// <summary>Select exactly one backend before preparation. Never retry input using another backend.</summary>
public sealed class HybridComputerBackend(IComputerInteractionBackend native, PythonComputerBackend python,
    ILogger<HybridComputerBackend>? logger = null) : IComputerInteractionBackend
{
    private readonly ConcurrentDictionary<Guid, IComputerInteractionBackend> selected = new();
    public async Task<PreparedComputerTarget> PrepareAsync(ComputerAction action, CancellationToken ct)
    {
        var pythonAction = action.Type is ComputerActionType.TypeText or ComputerActionType.PressKey or ComputerActionType.PressShortcut &&
            (action.WindowReference.Equals("window:Notepad", StringComparison.OrdinalIgnoreCase) ||
             action.WindowReference.Equals("window:Google Chrome", StringComparison.OrdinalIgnoreCase) ||
             action.WindowReference.Equals("window:Chrome", StringComparison.OrdinalIgnoreCase)) || action.Type == ComputerActionType.ScrollWindow;
        IComputerInteractionBackend backend = pythonAction ? python : native;
        logger?.LogInformation("ComputerBackend ActionId={ActionId} Type={Type} Backend={Backend} PythonInvoked={PythonInvoked}",
            action.Id, action.Type, pythonAction ? "Python" : "Native", pythonAction);
        var target = await backend.PrepareAsync(action, ct);
        if (selected.Count >= 16) selected.Clear();
        selected[action.Id] = backend;
        return target;
    }
    public Task<ComputerInteractionResult> ExecuteAsync(ComputerAction action, PreparedComputerTarget target, Action guard, CancellationToken ct)
    {
        if (!selected.TryRemove(action.Id, out var backend)) throw new InvalidOperationException("PreparedBackendMissing");
        return backend.ExecuteAsync(action, target, guard, ct);
    }
}
