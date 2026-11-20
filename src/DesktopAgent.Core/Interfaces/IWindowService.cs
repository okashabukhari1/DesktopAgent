namespace DesktopAgent.Core.Interfaces;

public interface IWindowService
{
    void ShowCharacter();
    void HideCharacter();
    void TogglePanel();
    void ShowDesktopApp();
    void ShowDeveloper(string? sessionId = null) => ShowDesktopApp();
    void ShowWorkspaces();
    void ShowApplications();
    void ShowAssistant();
    void ShowPermissions();
    void ShowActivity();
    void ShowSettings();
    void ResetPosition();
    void Shutdown();
}
