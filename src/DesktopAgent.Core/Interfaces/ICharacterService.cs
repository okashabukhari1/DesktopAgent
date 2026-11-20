using DesktopAgent.Core.Enums;

namespace DesktopAgent.Core.Interfaces;

public interface ICharacterService
{
    CharacterState State { get; }
    event EventHandler<CharacterState>? StateChanged;
    event EventHandler<string>? MessageRequested;
    void SetState(CharacterState state);
    void ShowMessage(string message);
    void SetDeveloperState(bool permissionPending, bool working) { }
    void ReactToDeveloperResult(bool success) { }
}
