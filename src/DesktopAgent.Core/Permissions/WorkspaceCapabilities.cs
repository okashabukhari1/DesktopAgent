using DesktopAgent.Core.Workspaces.Enums;
using DesktopAgent.Core.Workspaces.Interfaces;

namespace DesktopAgent.Core.Permissions;

public static class WorkspaceCapabilities
{
    public static IReadOnlyList<string> RequiredFor(IWorkspaceService service, string name)
    {
        var ids = new HashSet<string> { CapabilityIds.WorkspaceView, CapabilityIds.WorkspaceLaunch };
        var w = service.Find(name);
        if (w is null) return ids.ToArray();
        if (w.Applications.Any(x => x.Launch)) ids.Add(CapabilityIds.ApplicationOpen);
        if (w.Applications.Any(x => x.FocusIfRunning) || !string.IsNullOrWhiteSpace(w.FinalFocusApplication)) ids.Add(CapabilityIds.ApplicationFocus);
        if (w.Applications.Any(x => x.Layout.HasValue || x.TargetMonitor.HasValue)) ids.Add(CapabilityIds.ApplicationWindowControl);
        if (w.Folders.Count > 0) ids.Add(CapabilityIds.ShellOpenFolder);
        if (w.Files.Count > 0) ids.Add(CapabilityIds.ShellOpenFile);
        if (w.Urls.Count > 0) ids.Add(CapabilityIds.InternetOpenUrl);
        foreach (var action in w.PreLaunchActions.Concat(w.PostLaunchActions))
            ids.Add(action.Type == WorkspaceActionType.Close ? CapabilityIds.ApplicationClose :
                action.Type == WorkspaceActionType.Focus ? CapabilityIds.ApplicationFocus : CapabilityIds.ApplicationWindowControl);
        return ids.ToArray();
    }
}
