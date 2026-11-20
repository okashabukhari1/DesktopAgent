using DesktopAgent.Core.Automation.Enums;
using DesktopAgent.Core.Workspaces.Enums;

namespace DesktopAgent.Core.Workspaces.Models;

public sealed class WorkspaceDefinition
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public Guid? ProjectId { get; set; }
    public string IconKey { get; set; } = "Briefcase";
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.Now;
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.Now;
    public List<WorkspaceApplication> Applications { get; set; } = [];
    public List<WorkspaceResource> Folders { get; set; } = [];
    public List<WorkspaceResource> Files { get; set; } = [];
    public List<WorkspaceResource> Urls { get; set; } = [];
    public List<WorkspaceSafeAction> PreLaunchActions { get; set; } = [];
    public List<WorkspaceSafeAction> PostLaunchActions { get; set; } = [];
    public string? FinalFocusApplication { get; set; }
    public bool ContinueOnFailure { get; set; } = true;
    public int LaunchTimeoutSeconds { get; set; } = 20;
    public bool ShowDetailedProgress { get; set; } = true;

    public WorkspaceDefinition DeepCopy()
    {
        return new WorkspaceDefinition
        {
            Id = Id,
            Name = Name,
            Description = Description,
            ProjectId = ProjectId,
            IconKey = IconKey,
            CreatedAt = CreatedAt,
            UpdatedAt = UpdatedAt,
            Applications = Applications.Select(item => item.DeepCopy()).ToList(),
            Folders = Folders.Select(item => item.DeepCopy()).ToList(),
            Files = Files.Select(item => item.DeepCopy()).ToList(),
            Urls = Urls.Select(item => item.DeepCopy()).ToList(),
            PreLaunchActions = PreLaunchActions.Select(item => item.DeepCopy()).ToList(),
            PostLaunchActions = PostLaunchActions.Select(item => item.DeepCopy()).ToList(),
            FinalFocusApplication = FinalFocusApplication,
            ContinueOnFailure = ContinueOnFailure,
            LaunchTimeoutSeconds = LaunchTimeoutSeconds,
            ShowDetailedProgress = ShowDetailedProgress
        };
    }
}

public sealed class WorkspaceApplication
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string DisplayName { get; set; } = string.Empty;
    public string ApplicationIdentifier { get; set; } = string.Empty;
    public string? CatalogApplicationId { get; set; }
    public bool Launch { get; set; } = true;
    public bool FocusIfRunning { get; set; } = true;
    public int LaunchOrder { get; set; }
    public int DelayAfterLaunchMs { get; set; }
    public int? TargetMonitor { get; set; }
    public WindowLayout? Layout { get; set; }

    public WorkspaceApplication DeepCopy() => (WorkspaceApplication)MemberwiseClone();
}

public sealed class WorkspaceResource
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string DisplayName { get; set; } = string.Empty;
    public string Value { get; set; } = string.Empty;
    public int Order { get; set; }

    public WorkspaceResource DeepCopy() => (WorkspaceResource)MemberwiseClone();
}

public sealed class WorkspaceSafeAction
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public WorkspaceActionType Type { get; set; }
    public string Target { get; set; } = string.Empty;
    public int Order { get; set; }

    public WorkspaceSafeAction DeepCopy() => (WorkspaceSafeAction)MemberwiseClone();
}
