namespace DesktopAgent.Core.Interfaces;

public interface IStartupService
{
    bool IsEnabled();
    void SetEnabled(bool enabled);
}
