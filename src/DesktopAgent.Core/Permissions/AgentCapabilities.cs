namespace DesktopAgent.Core.Permissions;

public static class AgentCapabilities
{
    public static IReadOnlyList<AgentCapability> All { get; } = new AgentCapability[]
    {
        new(CapabilityIds.ProjectFilesList, "List project files", "List bounded project-relative filenames, excluding private/generated paths.", "Project Files", PermissionRisk.Low, PermissionPolicy.Allow, CapabilityAvailability.Available, "Registered project roots only."),
        new(CapabilityIds.ProjectFilesSearch, "Search project files/code", "Search local filenames or redacted code snippets within a selected project.", "Project Files", PermissionRisk.Medium, PermissionPolicy.Allow, CapabilityAvailability.Available, "Code search also requires Read."),
        new(CapabilityIds.ProjectFilesRead, "Read project source", "Retrieve bounded, locally redacted source ranges for the assistant/cloud provider.", "Project Files", PermissionRisk.Medium, PermissionPolicy.Allow, CapabilityAvailability.Available, "Secret/private files and outside-root paths remain denied."),
        new(CapabilityIds.ProjectFilesOpen, "Open project files", "Send validated project files to a catalogued supported editor; no scripts are executed.", "Project Files", PermissionRisk.Low, PermissionPolicy.Allow, CapabilityAvailability.Available, "Editor launch also requires Application.Open."),
        new(CapabilityIds.ProjectViewMetadata, "View project metadata", "View locally detected technologies and basic Git branch, never source code.", "Projects", PermissionRisk.Low, PermissionPolicy.Allow, CapabilityAvailability.Available, "Project queries."),
        new(CapabilityIds.ProjectSearch, "Search projects", "Search the cached catalog, not the entire filesystem.", "Projects", PermissionRisk.Low, PermissionPolicy.Allow, CapabilityAvailability.Available, "Project search."),
        new(CapabilityIds.ProjectOpen, "Open projects", "Open a catalogued project in a supported editor or associated workspace.", "Projects", PermissionRisk.Low, PermissionPolicy.Allow, CapabilityAvailability.Available, "Child application/workspace permissions also apply."),
        new(CapabilityIds.ProjectScanLocations, "Scan project locations", "Bounded manifest discovery in user-configured locations only.", "Projects", PermissionRisk.Low, PermissionPolicy.Allow, CapabilityAvailability.Available, "No drive-wide search."),
        new(CapabilityIds.ProjectModifyCatalog, "Modify project catalog", "Change project preferences or workspace associations, never delete project files.", "Projects", PermissionRisk.Medium, PermissionPolicy.Ask, CapabilityAvailability.Available, "Confirmation recommended."),
        new(CapabilityIds.ApplicationDiscover, "Discover applications", "Find installed and currently running applications in the Windows application catalog.", "Applications", PermissionRisk.Low, PermissionPolicy.Allow, CapabilityAvailability.Available, "Required for application commands."),
        new(CapabilityIds.ApplicationSearch, "Search applications", "Search the application catalog by name and return matching entries.", "Applications", PermissionRisk.Low, PermissionPolicy.Allow, CapabilityAvailability.Available, "Required to resolve application names."),
        new(CapabilityIds.ApplicationOpen, "Open applications", "Launch an application selected from the application catalog.", "Applications", PermissionRisk.Low, PermissionPolicy.Allow, CapabilityAvailability.Available, "Recommended for application commands."),
        new(CapabilityIds.ApplicationFocus, "Focus applications", "Bring an already running application window to the foreground.", "Applications", PermissionRisk.Low, PermissionPolicy.Allow, CapabilityAvailability.Available, "Recommended for application commands."),
        new(CapabilityIds.ApplicationWindowControl, "Move and resize windows", "Minimize, maximize, restore, move, resize, or arrange supported application windows.", "Applications", PermissionRisk.Medium, PermissionPolicy.Allow, CapabilityAvailability.Available, "Optional window-management capability."),
        new(CapabilityIds.ApplicationClose, "Close applications", "Send a normal close request to an application window. Unsaved work may be affected.", "Applications", PermissionRisk.Medium, PermissionPolicy.Ask, CapabilityAvailability.Available, "Optional; confirmation is recommended."),
        new(CapabilityIds.WorkspaceView, "View workspaces", "List saved workspace names and inspect workspace definitions.", "Workspaces", PermissionRisk.Low, PermissionPolicy.Allow, CapabilityAvailability.Available, "Required for workspace commands."),
        new(CapabilityIds.WorkspaceLaunch, "Launch workspaces", "Start the applications and other configured entries in a saved workspace.", "Workspaces", PermissionRisk.Medium, PermissionPolicy.Allow, CapabilityAvailability.Available, "Recommended for workspace commands."),
        new(CapabilityIds.WorkspaceStop, "Stop workspace sessions", "Cancel a workspace launch that is currently in progress.", "Workspaces", PermissionRisk.Medium, PermissionPolicy.Ask, CapabilityAvailability.Available, "Optional session control."),
        new(CapabilityIds.WorkspaceModify, "Modify workspaces", "Create, edit, duplicate, or delete saved workspace definitions.", "Workspaces", PermissionRisk.Medium, PermissionPolicy.Ask, CapabilityAvailability.Available, "Optional workspace editing."),
        new(CapabilityIds.ShellOpenFolder, "Open folders", "Open a folder through Windows Explorer when a user request specifies it.", "Files", PermissionRisk.Low, PermissionPolicy.Allow, CapabilityAvailability.Available, "Opens the selected folder; does not read its contents."),
        new(CapabilityIds.ShellOpenFile, "Open files", "Open a supported non-executable document with its registered Windows application.", "Files", PermissionRisk.Medium, PermissionPolicy.Ask, CapabilityAvailability.Available, "Does not grant file reading or editing."),
        new(CapabilityIds.InternetOpenUrl, "Open web links", "Open an HTTP or HTTPS URL in the user's default browser.", "Internet", PermissionRisk.Low, PermissionPolicy.Allow, CapabilityAvailability.Available, "Does not grant web search or browser control."),
        new(CapabilityIds.InternetSearchWeb, "Search the web", "Search the public internet and return web results.", "Internet", PermissionRisk.Medium, PermissionPolicy.Deny, CapabilityAvailability.ComingSoon, "Not available in this phase."),
        new(CapabilityIds.BrowserControl, "Control browser pages", "Read or interact with browser tabs, page content, forms, or downloads.", "Internet", PermissionRisk.High, PermissionPolicy.Deny, CapabilityAvailability.ComingSoon, "Not available in this phase."),
        new(CapabilityIds.FilesSearch, "Search filenames", "Search filenames and folders on this computer.", "Files", PermissionRisk.Medium, PermissionPolicy.Deny, CapabilityAvailability.ComingSoon, "Not available in this phase."),
        new(CapabilityIds.FilesReadMetadata, "Read file metadata", "Read file names, types, sizes, and timestamps.", "Files", PermissionRisk.Medium, PermissionPolicy.Deny, CapabilityAvailability.ComingSoon, "Not available in this phase."),
        new(CapabilityIds.FilesRead, "Read file contents", "Allow the assistant to read files and extract their contents.", "Files", PermissionRisk.High, PermissionPolicy.Deny, CapabilityAvailability.ComingSoon, "Not available in this phase."),
        new(CapabilityIds.FilesCreate, "Create files", "Create new files in locations selected by the user.", "Files", PermissionRisk.High, PermissionPolicy.Deny, CapabilityAvailability.ComingSoon, "Not available in this phase."),
        new(CapabilityIds.FilesModify, "Create or modify files", "Allow the assistant to create, edit, move, or rename files.", "Files", PermissionRisk.High, PermissionPolicy.Deny, CapabilityAvailability.ComingSoon, "Not available in this phase."),
        new(CapabilityIds.FilesMove, "Move files", "Move files between locations.", "Files", PermissionRisk.High, PermissionPolicy.Deny, CapabilityAvailability.ComingSoon, "Not available in this phase."),
        new(CapabilityIds.FilesRename, "Rename files", "Rename files and folders.", "Files", PermissionRisk.High, PermissionPolicy.Deny, CapabilityAvailability.ComingSoon, "Not available in this phase."),
        new(CapabilityIds.FilesDelete, "Delete files", "Allow the assistant to delete files.", "Files", PermissionRisk.High, PermissionPolicy.Deny, CapabilityAvailability.ComingSoon, "Not available in this phase."),
        new(CapabilityIds.ScreenCapture, "Capture screen", "Capture or inspect the desktop or a selected application.", "Screen", PermissionRisk.High, PermissionPolicy.Deny, CapabilityAvailability.ComingSoon, "Not available in this phase."),
        new(CapabilityIds.ScreenInspectUi, "Inspect UI elements (read-only)", "Read bounded accessibility metadata from a selected window. Requested results may go to your configured AI provider; no screenshots or input actions.", "Screen", PermissionRisk.High, PermissionPolicy.Ask, CapabilityAvailability.Available, "Passwords and sensitive windows stay protected. Approve each observation or explicitly configure Permissions."),
        new(CapabilityIds.ScreenViewSelectedApp, "View selected application", "Inspect the visible content of a user-selected application.", "Screen", PermissionRisk.High, PermissionPolicy.Deny, CapabilityAvailability.ComingSoon, "Not available in this phase."),
        new(CapabilityIds.ScreenInteract, "Interact with application UI", "Click or enter text in a selected application's controls.", "Screen", PermissionRisk.Critical, PermissionPolicy.Deny, CapabilityAvailability.ComingSoon, "Not available in this phase."),
        new(CapabilityIds.DeveloperProjectRead, "Read project files", "Inspect source code and project files in a user-selected project.", "Developer Tools", PermissionRisk.High, PermissionPolicy.Deny, CapabilityAvailability.ComingSoon, "Not available in this phase."),
        new(CapabilityIds.DeveloperBuild, "Run project builds", "Run a build for a selected project.", "Developer Tools", PermissionRisk.High, PermissionPolicy.Deny, CapabilityAvailability.ComingSoon, "Not available in this phase."),
        new(CapabilityIds.DeveloperTests, "Run project tests", "Run tests for a selected project.", "Developer Tools", PermissionRisk.Medium, PermissionPolicy.Deny, CapabilityAvailability.ComingSoon, "Not available in this phase."),
        new(CapabilityIds.DeveloperGitRead, "Read Git state", "Read status, branches, and diffs in a selected repository.", "Developer Tools", PermissionRisk.Medium, PermissionPolicy.Deny, CapabilityAvailability.ComingSoon, "Not available in this phase."),
        new(CapabilityIds.DeveloperGitWrite, "Change Git state", "Create commits or change branches in a selected repository.", "Developer Tools", PermissionRisk.High, PermissionPolicy.Deny, CapabilityAvailability.ComingSoon, "Not available in this phase."),
        new(CapabilityIds.DeveloperTerminal, "Run terminal commands", "Run commands or scripts in a terminal on the user's computer.", "Developer Tools", PermissionRisk.Critical, PermissionPolicy.Deny, CapabilityAvailability.ComingSoon, "Not available in this phase.")
    }.Concat(DesktopAgent.Core.Developer.DeveloperCapabilityIds.All).Append(DesktopAgent.Core.ComputerUse.ComputerUseCapabilities.Capability).ToArray();

    public static AgentCapability? Find(string id) => All.FirstOrDefault(capability =>
        string.Equals(capability.Id, id, StringComparison.OrdinalIgnoreCase));

    public static IReadOnlyList<string> RequiredBy(string capabilityId) => capabilityId switch
    {
        CapabilityIds.ApplicationDiscover => ["List applications", "Running application queries"],
        CapabilityIds.ApplicationSearch => ["Search applications", "Resolve application names"],
        CapabilityIds.ApplicationOpen => ["Open an application"],
        CapabilityIds.ApplicationFocus => ["Focus an application"],
        CapabilityIds.ApplicationWindowControl => ["Minimize, maximize, restore, move, resize, or arrange windows"],
        CapabilityIds.ApplicationClose => ["Close an application"],
        CapabilityIds.WorkspaceView => ["List workspaces"],
        CapabilityIds.WorkspaceLaunch => ["Launch a workspace"],
        CapabilityIds.WorkspaceStop => ["Stop a workspace launch"],
        CapabilityIds.WorkspaceModify => ["Create, edit, duplicate, or delete a workspace"],
        CapabilityIds.ShellOpenFolder => ["Open a folder"],
        CapabilityIds.ShellOpenFile => ["Open a file"],
        CapabilityIds.InternetOpenUrl => ["Open a web link"],
        _ => []
    };
}
