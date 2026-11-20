namespace DesktopAgent.Core.Automation.Enums;

public enum ComputerCommandType
{
    Invalid,
    Help,
    ListApplications,
    ListWindows,
    OpenApplication,
    FocusApplication,
    CloseApplication,
    MinimizeWindow,
    MaximizeWindow,
    RestoreWindow,
    MoveWindow,
    ResizeWindow,
    ApplyLayout,
    MoveToMonitor,
    OpenFolder,
    OpenFile,
    OpenUrl,
    ListWorkspaces,
    WorkspaceStatus,
    AiProviderRequest,
    AiToolRequest,
    LaunchWorkspace,
    StopWorkspace
    ,SearchApplications
    ,RefreshApplications
    ,OpenExplicitApplication
    ,DeveloperEvent
}
