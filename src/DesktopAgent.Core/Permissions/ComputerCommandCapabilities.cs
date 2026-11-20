using DesktopAgent.Core.Automation.Enums;

namespace DesktopAgent.Core.Permissions;

public static class ComputerCommandCapabilities
{
    public static IReadOnlyList<string> RequiredFor(ComputerCommandType command) => command switch
    {
        ComputerCommandType.ListApplications or ComputerCommandType.ListWindows or ComputerCommandType.RefreshApplications => [CapabilityIds.ApplicationDiscover],
        ComputerCommandType.SearchApplications => [CapabilityIds.ApplicationDiscover, CapabilityIds.ApplicationSearch],
        ComputerCommandType.OpenApplication or ComputerCommandType.OpenExplicitApplication => [CapabilityIds.ApplicationDiscover, CapabilityIds.ApplicationSearch, CapabilityIds.ApplicationOpen],
        ComputerCommandType.FocusApplication => [CapabilityIds.ApplicationDiscover, CapabilityIds.ApplicationFocus],
        ComputerCommandType.CloseApplication => [CapabilityIds.ApplicationClose],
        ComputerCommandType.MinimizeWindow or ComputerCommandType.MaximizeWindow or ComputerCommandType.RestoreWindow
            or ComputerCommandType.MoveWindow or ComputerCommandType.ResizeWindow or ComputerCommandType.ApplyLayout
            or ComputerCommandType.MoveToMonitor => [CapabilityIds.ApplicationWindowControl],
        ComputerCommandType.ListWorkspaces or ComputerCommandType.WorkspaceStatus => [CapabilityIds.WorkspaceView],
        ComputerCommandType.LaunchWorkspace => [CapabilityIds.WorkspaceView, CapabilityIds.WorkspaceLaunch],
        ComputerCommandType.StopWorkspace => [CapabilityIds.WorkspaceStop],
        ComputerCommandType.OpenFolder => [CapabilityIds.ShellOpenFolder],
        ComputerCommandType.OpenFile => [CapabilityIds.ShellOpenFile],
        ComputerCommandType.OpenUrl => [CapabilityIds.InternetOpenUrl],
        _ => []
    };
}
