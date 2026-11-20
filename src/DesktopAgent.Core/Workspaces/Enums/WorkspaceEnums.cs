namespace DesktopAgent.Core.Workspaces.Enums;

public enum WorkspaceLaunchStatus
{
    NotRunning,
    Starting,
    Ready,
    Partial,
    Cancelled,
    Failed,
    InvalidConfiguration
}

public enum WorkspaceActionType
{
    Focus,
    Minimize,
    Maximize,
    Restore,
    Close
}

public enum WorkspaceStepKind
{
    PreLaunchAction,
    Application,
    Folder,
    File,
    Url,
    Layout,
    PostLaunchAction,
    FinalFocus
}
