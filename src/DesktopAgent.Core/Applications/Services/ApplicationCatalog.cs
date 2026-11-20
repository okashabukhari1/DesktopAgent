using DesktopAgent.Core.Applications.Enums;
using DesktopAgent.Core.Applications.Interfaces;
using DesktopAgent.Core.Applications.Models;

namespace DesktopAgent.Core.Applications.Services;

public sealed class ApplicationCatalog(IEnumerable<IApplicationDiscoveryProvider> providers, IApplicationCatalogRepository repository) : IApplicationCatalog
{
    private readonly List<IApplicationDiscoveryProvider> _providers = providers.ToList();
    private List<InstalledApplication> _applications = [];
    private readonly SemaphoreSlim _refreshGate = new(1, 1);
    private bool _initialized;

    public event EventHandler? CatalogChanged;
    public event EventHandler<ApplicationDiscoveryProgress>? RefreshProgress;
    public IReadOnlyList<InstalledApplication> Applications => _applications.Select(item => item.DeepCopy()).ToList();
    public DateTimeOffset? LastRefresh { get; private set; }
    public bool IsRefreshing { get; private set; }

    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        if (_initialized) return;
        var document = await repository.LoadAsync(cancellationToken);
        _applications = Deduplicate(document.Applications ?? []).ToList();
        LastRefresh = document.LastRefresh == default ? null : document.LastRefresh;
        _initialized = true;
    }

    public async Task<IReadOnlyList<DiscoveryProviderResult>> RefreshAsync(CancellationToken cancellationToken = default)
    {
        await _refreshGate.WaitAsync(cancellationToken);
        IsRefreshing = true;
        try
        {
            var results = new List<DiscoveryProviderResult>();
            var discovered = new List<InstalledApplication>();
            for (var index = 0; index < _providers.Count; index++)
            {
                var provider = _providers[index];
                RefreshProgress?.Invoke(this, new(provider.Name, DiscoveryProviderStatus.Running, index, _providers.Count, discovered.Count, $"Scanning {provider.Name}…"));
                DiscoveryProviderResult result;
                try { result = await provider.DiscoverAsync(cancellationToken); }
                catch (OperationCanceledException) { throw; }
                catch (Exception exception) { result = new(provider.Name, DiscoveryProviderStatus.Failed, [], exception.GetType().Name); }
                results.Add(result);
                discovered.AddRange(result.Applications);
                RefreshProgress?.Invoke(this, new(provider.Name, result.Status, index + 1, _providers.Count, discovered.Count, $"{provider.Name}: {result.Status}"));
            }

            // A failed scan must not erase a usable cached catalog.
            if (results.Count == 0 || results.All(result => result.Status == DiscoveryProviderStatus.Failed))
                return results;

            var prior = _applications.ToDictionary(item => item.Id, StringComparer.OrdinalIgnoreCase);
            var merged = Deduplicate(discovered);
            foreach (var item in merged)
                if (prior.TryGetValue(item.Id, out var old))
                {
                    item.IsFavorite = old.IsFavorite;
                    item.AgentUseCount = old.AgentUseCount;
                    item.LastUsed = old.LastUsed;
                }
            _applications = merged.OrderByDescending(item => item.IsFavorite).ThenBy(item => item.DisplayName, StringComparer.OrdinalIgnoreCase).ToList();
            LastRefresh = DateTimeOffset.Now;
            await SaveAsync(cancellationToken);
            CatalogChanged?.Invoke(this, EventArgs.Empty);
            return results;
        }
        finally { IsRefreshing = false; _refreshGate.Release(); }
    }

    public IReadOnlyList<ApplicationSearchMatch> SearchApplications(string query, bool includeSystem = true, int limit = 20)
    {
        var normalized = ApplicationNameNormalizer.Normalize(query);
        if (normalized.Length == 0) return _applications.Where(item => includeSystem || !item.IsSystemApplication).Take(limit).Select(item => new ApplicationSearchMatch(item.DeepCopy(), 1, "All")).ToList();
        return _applications.Where(item => includeSystem || !item.IsSystemApplication)
            .Select(item => Score(item, normalized)).Where(match => match.Score > 0)
            .OrderByDescending(match => match.Score).ThenBy(match => match.Application.DisplayName, StringComparer.OrdinalIgnoreCase)
            .Take(limit).Select(match => match with { Application = match.Application.DeepCopy() }).ToList();
    }

    public ApplicationResolution Resolve(string query)
    {
        if (string.IsNullOrWhiteSpace(query)) return ApplicationResolution.NotFound;
        var byId = FindById(query);
        if (byId is not null) return new(ApplicationResolutionStatus.Found, byId, [new(byId, 1200, "Identity")]);
        var matches = SearchApplications(query, true, 8);
        if (matches.Count == 0) return ApplicationResolution.NotFound;
        var first = matches[0];
        var ambiguous = matches.Count > 1 && (first.Score < 900 || matches[1].Score >= first.Score - 80);
        return ambiguous
            ? new(ApplicationResolutionStatus.Ambiguous, null, matches.Where(match => match.Score >= first.Score - 80).ToList())
            : new(ApplicationResolutionStatus.Found, first.Application, matches);
    }

    public InstalledApplication? FindById(string id) => _applications.FirstOrDefault(item => item.Id.Equals(id, StringComparison.OrdinalIgnoreCase))?.DeepCopy();

    public async Task ToggleFavoriteAsync(string id, CancellationToken cancellationToken = default)
    {
        var item = _applications.FirstOrDefault(app => app.Id.Equals(id, StringComparison.OrdinalIgnoreCase));
        if (item is null) return;
        item.IsFavorite = !item.IsFavorite;
        await SaveAsync(cancellationToken);
        CatalogChanged?.Invoke(this, EventArgs.Empty);
    }

    public async Task RecordUsageAsync(string id, CancellationToken cancellationToken = default)
    {
        var item = _applications.FirstOrDefault(app => app.Id.Equals(id, StringComparison.OrdinalIgnoreCase));
        if (item is null) return;
        item.AgentUseCount++;
        item.LastUsed = DateTimeOffset.Now;
        await SaveAsync(cancellationToken);
        CatalogChanged?.Invoke(this, EventArgs.Empty);
    }

    public static IReadOnlyList<InstalledApplication> Deduplicate(IEnumerable<InstalledApplication> source)
    {
        var result = new List<InstalledApplication>();
        foreach (var raw in source.Where(item => item is not null && item.IsLaunchable &&
                     !string.IsNullOrWhiteSpace(item.DisplayName) && !IsUninstaller(item.DisplayName, item.ExecutablePath)))
        {
            var item = Normalize(raw);
            var existing = result.FirstOrDefault(candidate => SameIdentity(candidate, item));
            if (existing is null) result.Add(item);
            else Merge(existing, item);
        }
        return result;
    }

    public static bool IsUninstaller(string? displayName, string? path = null)
    {
        var display = (displayName ?? string.Empty).ToLowerInvariant();
        var file = Path.GetFileNameWithoutExtension(path ?? string.Empty).ToLowerInvariant();
        return display.StartsWith("uninstall ", StringComparison.Ordinal) || display.EndsWith(" uninstaller", StringComparison.Ordinal) ||
               file.Contains("uninstall", StringComparison.Ordinal) || file.Contains("unins000", StringComparison.Ordinal) ||
               file.Equals("setup", StringComparison.Ordinal) || file.Contains("update helper", StringComparison.Ordinal);
    }

    private async Task SaveAsync(CancellationToken cancellationToken) => await repository.SaveAsync(new()
    {
        LastRefresh = LastRefresh ?? DateTimeOffset.Now,
        Applications = _applications.Select(item => item.DeepCopy()).ToList()
    }, cancellationToken);

    private static InstalledApplication Normalize(InstalledApplication item)
    {
        item.Id ??= string.Empty;
        item.Aliases ??= [];
        item.Sources ??= [];
        item.NormalizedName = ApplicationNameNormalizer.Normalize(item.DisplayName);
        item.Aliases = item.Aliases.Append(item.DisplayName).Append(item.ProcessName ?? string.Empty)
            .Concat(AdditionalAliases(item.NormalizedName))
            .Select(ApplicationNameNormalizer.Normalize).Where(value => value.Length > 0).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        if (item.Id.Length == 0) item.Id = StableIdentity(item);
        return item;
    }

    private static IEnumerable<string> AdditionalAliases(string normalizedName) => normalizedName switch
    {
        "microsoft edge" => ["edge"],
        "google chrome" => ["chrome"],
        "visual studio code" => ["vs code", "vscode"],
        _ => []
    };

    private static string StableIdentity(InstalledApplication item)
    {
        if (!string.IsNullOrWhiteSpace(item.AppUserModelId)) return "appx:" + item.AppUserModelId.ToLowerInvariant();
        if (!string.IsNullOrWhiteSpace(item.ExecutablePath)) return "exe:" + Path.GetFullPath(item.ExecutablePath).ToLowerInvariant();
        if (!string.IsNullOrWhiteSpace(item.LaunchIdentifier)) return "launch:" + item.LaunchIdentifier.ToLowerInvariant();
        return "name:" + ApplicationNameNormalizer.Normalize(item.DisplayName);
    }

    private static bool SameIdentity(InstalledApplication left, InstalledApplication right)
    {
        if (left.Id.Equals(right.Id, StringComparison.OrdinalIgnoreCase)) return true;
        if (BothEqual(left.AppUserModelId, right.AppUserModelId) || BothPathEqual(left.ExecutablePath, right.ExecutablePath) || BothPathEqual(left.LaunchIdentifier, right.LaunchIdentifier)) return true;
        if (left.NormalizedName == right.NormalizedName &&
            ((IsDesktopShellAlias(left) && !string.IsNullOrWhiteSpace(right.ExecutablePath)) ||
             (IsDesktopShellAlias(right) && !string.IsNullOrWhiteSpace(left.ExecutablePath)))) return true;
        if (left.NormalizedName == right.NormalizedName && (SameProcess(left, right) || left.ApplicationType == InstalledApplicationType.Shortcut || right.ApplicationType == InstalledApplicationType.Shortcut ||
            (IsPackaged(left) && right.ProcessName?.Equals(right.NormalizedName.Replace(" ", string.Empty, StringComparison.Ordinal), StringComparison.OrdinalIgnoreCase) == true) ||
            (IsPackaged(right) && left.ProcessName?.Equals(left.NormalizedName.Replace(" ", string.Empty, StringComparison.Ordinal), StringComparison.OrdinalIgnoreCase) == true))) return true;
        return SameProcess(left, right) && (left.Aliases.Contains(right.NormalizedName, StringComparer.OrdinalIgnoreCase) || right.Aliases.Contains(left.NormalizedName, StringComparer.OrdinalIgnoreCase));
    }

    private static bool BothEqual(string? left, string? right) => !string.IsNullOrWhiteSpace(left) && !string.IsNullOrWhiteSpace(right) && left.Equals(right, StringComparison.OrdinalIgnoreCase);
    private static bool BothPathEqual(string? left, string? right)
    {
        if (string.IsNullOrWhiteSpace(left) || string.IsNullOrWhiteSpace(right)) return false;
        try { return Path.GetFullPath(left).Equals(Path.GetFullPath(right), StringComparison.OrdinalIgnoreCase); }
        catch (Exception exception) when (exception is ArgumentException or NotSupportedException) { return left.Equals(right, StringComparison.OrdinalIgnoreCase); }
    }
    private static bool SameProcess(InstalledApplication left, InstalledApplication right) => BothEqual(left.ProcessName, right.ProcessName);
    private static bool IsPackaged(InstalledApplication item) => item.ApplicationType is InstalledApplicationType.Packaged or InstalledApplicationType.Uwp or InstalledApplicationType.Msix or InstalledApplicationType.System;
    private static bool IsDesktopShellAlias(InstalledApplication item) => IsPackaged(item) &&
        item.AppUserModelId is { Length: > 0 } id && !id.Contains('!');

    private static void Merge(InstalledApplication target, InstalledApplication incoming)
    {
        target.Aliases = target.Aliases.Concat(incoming.Aliases).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        target.Sources = target.Sources.Concat(incoming.Sources).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        target.ExecutablePath ??= incoming.ExecutablePath;
        target.LaunchIdentifier ??= incoming.LaunchIdentifier;
        target.AppUserModelId ??= incoming.AppUserModelId;
        target.PackageFamilyName ??= incoming.PackageFamilyName;
        target.ProcessName ??= incoming.ProcessName;
        target.IconPath ??= incoming.IconPath;
        target.IsSystemApplication &= incoming.IsSystemApplication;
        target.IsFavorite |= incoming.IsFavorite;
        target.AgentUseCount = Math.Max(target.AgentUseCount, incoming.AgentUseCount);
        if (incoming.LastUsed > target.LastUsed) target.LastUsed = incoming.LastUsed;
        if (IsDesktopShellAlias(target) && !IsPackaged(incoming) && !string.IsNullOrWhiteSpace(incoming.ExecutablePath))
        {
            target.ApplicationType = incoming.ApplicationType;
            target.Id = "exe:" + Path.GetFullPath(incoming.ExecutablePath).ToLowerInvariant();
            target.ProcessName = incoming.ProcessName;
        }
        else if (!string.IsNullOrWhiteSpace(incoming.AppUserModelId) && !(IsDesktopShellAlias(incoming) && !string.IsNullOrWhiteSpace(target.ExecutablePath)))
        {
            target.Id = "appx:" + incoming.AppUserModelId.ToLowerInvariant();
            target.ApplicationType = incoming.ApplicationType;
        }
        else if (target.ApplicationType == InstalledApplicationType.Shortcut && incoming.ApplicationType != InstalledApplicationType.Shortcut)
        {
            target.ApplicationType = incoming.ApplicationType;
            if (!string.IsNullOrWhiteSpace(incoming.ExecutablePath)) target.Id = "exe:" + Path.GetFullPath(incoming.ExecutablePath).ToLowerInvariant();
        }
    }

    private static ApplicationSearchMatch Score(InstalledApplication item, string query)
    {
        if (item.DisplayName.Equals(query, StringComparison.OrdinalIgnoreCase)) return new(item, 1000, "Exact name");
        if (item.Aliases.Any(alias => alias.Equals(query, StringComparison.OrdinalIgnoreCase))) return new(item, 950, "Alias");
        if (item.NormalizedName == query) return new(item, 900, "Normalized name");
        if (item.NormalizedName.StartsWith(query, StringComparison.Ordinal) || item.Aliases.Any(alias => alias.StartsWith(query, StringComparison.Ordinal))) return new(item, 750, "Prefix");
        var tokens = ApplicationNameNormalizer.Tokens(query);
        var itemTokens = ApplicationNameNormalizer.Tokens(item.NormalizedName);
        if (tokens.Count > 0 && tokens.All(token => itemTokens.Any(candidate => candidate.StartsWith(token, StringComparison.Ordinal)))) return new(item, 620, "Token");
        if (item.NormalizedName.Contains(query, StringComparison.Ordinal)) return new(item, 520, "Contains");
        var similarity = Similarity(item.NormalizedName, query);
        return similarity >= 0.72 ? new(item, 250 + (int)(similarity * 100), "Fuzzy") : new(item, 0, string.Empty);
    }

    private static double Similarity(string left, string right)
    {
        if (left.Length == 0 || right.Length == 0) return 0;
        var previous = Enumerable.Range(0, right.Length + 1).ToArray();
        for (var i = 1; i <= left.Length; i++)
        {
            var current = new int[right.Length + 1]; current[0] = i;
            for (var j = 1; j <= right.Length; j++) current[j] = Math.Min(Math.Min(current[j - 1] + 1, previous[j] + 1), previous[j - 1] + (left[i - 1] == right[j - 1] ? 0 : 1));
            previous = current;
        }
        return 1d - (double)previous[^1] / Math.Max(left.Length, right.Length);
    }
}
