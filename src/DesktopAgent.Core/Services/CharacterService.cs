using DesktopAgent.Core.Enums;
using DesktopAgent.Core.Interfaces;

namespace DesktopAgent.Core.Services;

public sealed class CharacterService : ICharacterService
{
    private readonly object _sync = new();
    private CharacterState _agentState = CharacterState.Idle;
    private bool _developerPermission, _developerWorking;
    private CharacterState? _developerReaction;
    private CancellationTokenSource? _reactionCancellation;
    public CharacterState State { get; private set; } = CharacterState.Idle;
    public event EventHandler<CharacterState>? StateChanged;
    public event EventHandler<string>? MessageRequested;

    public void SetState(CharacterState state)
    {
        lock (_sync) { _agentState = state; Reconcile(); }
    }

    public void SetDeveloperState(bool permissionPending, bool working)
    {
        lock (_sync) { _developerPermission = permissionPending; _developerWorking = working; Reconcile(); }
    }
    private void Reconcile()
    {
        var state = _developerPermission ? CharacterState.Attention : _agentState == CharacterState.Attention ? CharacterState.Attention :
            _agentState is CharacterState.Working or CharacterState.Thinking ? _agentState : _developerReaction ?? (_developerWorking ? CharacterState.Working : _agentState);
        if (State == state) return;
        State = state;
        StateChanged?.Invoke(this, state);
    }
    public void ReactToDeveloperResult(bool success)
    {
        lock (_sync)
        {
            _reactionCancellation?.Cancel(); _reactionCancellation?.Dispose();
            var timer = new CancellationTokenSource(); _reactionCancellation = timer;
            _developerReaction = success ? CharacterState.DeveloperSuccess : CharacterState.DeveloperError;
            Reconcile(); _ = EndReactionAsync(timer, timer.Token);
        }
    }
    private async Task EndReactionAsync(CancellationTokenSource timer, CancellationToken token)
    {
        try
        {
            await Task.Delay(2000, token);
            lock (_sync) { if (_reactionCancellation != timer) return; _developerReaction = null; _reactionCancellation = null; Reconcile(); timer.Dispose(); }
        }
        catch (OperationCanceledException) { }
    }

    public void ShowMessage(string message)
    {
        if (!string.IsNullOrWhiteSpace(message)) MessageRequested?.Invoke(this, message.Trim());
    }
}
