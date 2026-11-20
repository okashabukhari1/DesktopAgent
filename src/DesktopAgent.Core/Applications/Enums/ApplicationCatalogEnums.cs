namespace DesktopAgent.Core.Applications.Enums;

public enum InstalledApplicationType
{
    Win32,
    Packaged,
    Uwp,
    Msix,
    Shortcut,
    System,
    Unknown
}

public enum ApplicationResolutionStatus
{
    Found,
    Ambiguous,
    NotFound
}

public enum DiscoveryProviderStatus
{
    Pending,
    Running,
    Success,
    Partial,
    Failed
}

public enum ApplicationLaunchStrategy
{
    Win32,
    Shortcut,
    Packaged,
    Unsupported
}
