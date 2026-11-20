using DesktopAgent.Core.Applications.Enums;

namespace DesktopAgent.Core.Applications.Models;

public sealed class InstalledApplication
{
    public string Id { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public string NormalizedName { get; set; } = string.Empty;
    public List<string> Aliases { get; set; } = [];
    public InstalledApplicationType ApplicationType { get; set; }
    public string? ExecutablePath { get; set; }
    public string? LaunchIdentifier { get; set; }
    public string? PackageFamilyName { get; set; }
    public string? AppUserModelId { get; set; }
    public string? ProcessName { get; set; }
    public string? IconPath { get; set; }
    public List<string> Sources { get; set; } = [];
    public bool IsInstalled { get; set; } = true;
    public bool IsLaunchable { get; set; } = true;
    public bool IsSystemApplication { get; set; }
    public bool IsFavorite { get; set; }
    public int AgentUseCount { get; set; }
    public DateTimeOffset? LastUsed { get; set; }
    public DateTimeOffset LastDiscovered { get; set; } = DateTimeOffset.Now;

    public InstalledApplication DeepCopy() => new()
    {
        Id = Id,
        DisplayName = DisplayName,
        NormalizedName = NormalizedName,
        Aliases = [.. Aliases],
        ApplicationType = ApplicationType,
        ExecutablePath = ExecutablePath,
        LaunchIdentifier = LaunchIdentifier,
        PackageFamilyName = PackageFamilyName,
        AppUserModelId = AppUserModelId,
        ProcessName = ProcessName,
        IconPath = IconPath,
        Sources = [.. Sources],
        IsInstalled = IsInstalled,
        IsLaunchable = IsLaunchable,
        IsSystemApplication = IsSystemApplication,
        IsFavorite = IsFavorite,
        AgentUseCount = AgentUseCount,
        LastUsed = LastUsed,
        LastDiscovered = LastDiscovered
    };
}

public sealed record ApplicationSearchMatch(InstalledApplication Application, int Score, string MatchReason);

public sealed record ApplicationResolution(ApplicationResolutionStatus Status, InstalledApplication? Application, IReadOnlyList<ApplicationSearchMatch> Matches)
{
    public static ApplicationResolution NotFound { get; } = new(ApplicationResolutionStatus.NotFound, null, []);
}

public sealed record DiscoveryProviderResult(string Provider, DiscoveryProviderStatus Status, IReadOnlyList<InstalledApplication> Applications, string? Error = null);

public sealed record ApplicationDiscoveryProgress(string Provider, DiscoveryProviderStatus Status, int CompletedProviders, int TotalProviders, int FoundApplications, string Message);

public sealed class ApplicationCatalogDocument
{
    public const int CurrentVersion = 1;
    public int Version { get; set; } = CurrentVersion;
    public DateTimeOffset LastRefresh { get; set; }
    public List<InstalledApplication> Applications { get; set; } = [];
}
