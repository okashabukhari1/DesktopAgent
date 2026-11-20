using DesktopAgent.Core.Models;

namespace DesktopAgent.Core.Interfaces;

public interface ISettingsService
{
    AppSettings Current { get; }
    string SettingsPath { get; }
    Task<AppSettings> LoadAsync(CancellationToken cancellationToken = default);
    Task SaveAsync(AppSettings settings, CancellationToken cancellationToken = default);
}
