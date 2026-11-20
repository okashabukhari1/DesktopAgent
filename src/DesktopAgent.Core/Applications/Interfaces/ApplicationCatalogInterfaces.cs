using DesktopAgent.Core.Applications.Models;

namespace DesktopAgent.Core.Applications.Interfaces;

public interface IApplicationCatalog
{
    event EventHandler? CatalogChanged;
    event EventHandler<ApplicationDiscoveryProgress>? RefreshProgress;
    IReadOnlyList<InstalledApplication> Applications { get; }
    DateTimeOffset? LastRefresh { get; }
    bool IsRefreshing { get; }
    Task InitializeAsync(CancellationToken cancellationToken = default);
    Task<IReadOnlyList<DiscoveryProviderResult>> RefreshAsync(CancellationToken cancellationToken = default);
    IReadOnlyList<ApplicationSearchMatch> SearchApplications(string query, bool includeSystem = true, int limit = 20);
    ApplicationResolution Resolve(string query);
    InstalledApplication? FindById(string id);
    Task ToggleFavoriteAsync(string id, CancellationToken cancellationToken = default);
    Task RecordUsageAsync(string id, CancellationToken cancellationToken = default);
}

public interface IApplicationDiscoveryProvider
{
    string Name { get; }
    Task<DiscoveryProviderResult> DiscoverAsync(CancellationToken cancellationToken = default);
}

public interface IApplicationCatalogRepository
{
    Task<ApplicationCatalogDocument> LoadAsync(CancellationToken cancellationToken = default);
    Task SaveAsync(ApplicationCatalogDocument document, CancellationToken cancellationToken = default);
}
