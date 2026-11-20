namespace DesktopAgent.Core.Permissions;

public enum PermissionPolicy
{
    Allow,
    Ask,
    Deny
}

public enum PermissionRisk
{
    Low,
    Medium,
    High,
    Critical
}

public enum CapabilityAvailability
{
    Available,
    Unavailable,
    ComingSoon,
    RequiresConfiguration
}

public enum PolicyOutcome
{
    Allowed,
    ConfirmationRequired,
    Denied,
    Unavailable
}

public sealed record AgentCapability(
    string Id,
    string Name,
    string Description,
    string Category,
    PermissionRisk Risk,
    PermissionPolicy DefaultPolicy,
    CapabilityAvailability Availability,
    string Requirement,
    string? WindowsStatus = null);

public sealed record PolicyEvaluation(
    string CapabilityId,
    PolicyOutcome Outcome,
    PermissionPolicy Policy,
    string Message);

public static class CapabilityIds
{
    public const string ProjectFilesList = "Project.Files.List";
    public const string ProjectFilesSearch = "Project.Files.Search";
    public const string ProjectFilesRead = "Project.Files.Read";
    public const string ProjectFilesOpen = "Project.Files.Open";
    public const string ProjectViewMetadata = "Project.ViewMetadata";
    public const string ProjectSearch = "Project.Search";
    public const string ProjectOpen = "Project.Open";
    public const string ProjectScanLocations = "Project.ScanLocations";
    public const string ProjectModifyCatalog = "Project.ModifyCatalog";
    public const string ApplicationDiscover = "Application.Discover";
    public const string ApplicationSearch = "Application.Search";
    public const string ApplicationOpen = "Application.Open";
    public const string ApplicationFocus = "Application.Focus";
    public const string ApplicationWindowControl = "Application.WindowControl";
    public const string ApplicationClose = "Application.Close";
    public const string WorkspaceView = "Workspace.View";
    public const string WorkspaceLaunch = "Workspace.Launch";
    public const string WorkspaceStop = "Workspace.Stop";
    public const string WorkspaceModify = "Workspace.Modify";
    public const string ShellOpenFolder = "Shell.OpenFolder";
    public const string ShellOpenFile = "Shell.OpenFile";
    public const string InternetOpenUrl = "Internet.OpenUrl";
    public const string InternetSearchWeb = "Internet.SearchWeb";
    public const string BrowserControl = "Browser.Control";
    public const string FilesSearch = "Files.Search";
    public const string FilesReadMetadata = "Files.ReadMetadata";
    public const string ScreenCapture = "Screen.Capture";
    public const string ScreenInspectUi = "Screen.InspectUI";
    public const string ScreenViewSelectedApp = "Screen.ViewSelectedApplication";
    public const string ScreenInteract = "Screen.Interact";
    public const string FilesRead = "Files.Read";
    public const string FilesCreate = "Files.Create";
    public const string FilesModify = "Files.Modify";
    public const string FilesMove = "Files.Move";
    public const string FilesRename = "Files.Rename";
    public const string FilesDelete = "Files.Delete";
    public const string DeveloperProjectRead = "Developer.ProjectRead";
    public const string DeveloperBuild = "Developer.Build";
    public const string DeveloperTests = "Developer.Tests";
    public const string DeveloperGitRead = "Developer.GitRead";
    public const string DeveloperGitWrite = "Developer.GitWrite";
    public const string DeveloperTerminal = "Developer.Terminal";
}
