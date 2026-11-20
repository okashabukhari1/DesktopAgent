using DesktopAgent.Core.Workspaces.Interfaces;
using DesktopAgent.Core.Workspaces.Models;

namespace DesktopAgent.Core.Workspaces.Services;

public sealed class WorkspaceValidator : IWorkspaceValidator
{
    public WorkspaceValidationResult Validate(WorkspaceDefinition workspace, IReadOnlyList<WorkspaceDefinition>? existing = null)
        => ValidateCore(workspace, existing, checkResourceExistence: true);

    public WorkspaceValidationResult ValidateForLaunch(WorkspaceDefinition workspace)
        => ValidateCore(workspace, null, checkResourceExistence: false);

    private static WorkspaceValidationResult ValidateCore(WorkspaceDefinition workspace, IReadOnlyList<WorkspaceDefinition>? existing, bool checkResourceExistence)
    {
        var issues = new List<WorkspaceValidationIssue>();
        if (string.IsNullOrWhiteSpace(workspace.Name)) issues.Add(new("Name", "Workspace name is required."));
        else if (workspace.Name.Trim().Length > 80) issues.Add(new("Name", "Workspace name must be 80 characters or fewer."));
        if (workspace.Description.Length > 300) issues.Add(new("Description", "Description must be 300 characters or fewer."));
        if (workspace.LaunchTimeoutSeconds is < 3 or > 300) issues.Add(new("LaunchTimeoutSeconds", "Launch timeout must be between 3 and 300 seconds."));
        if (existing?.Any(item => item.Id != workspace.Id && item.Name.Equals(workspace.Name.Trim(), StringComparison.OrdinalIgnoreCase)) == true)
            issues.Add(new("Name", "A workspace with this name already exists."));

        ValidateApplications(workspace.Applications, issues);
        ValidateResources(workspace.Folders, "Folders", Directory.Exists, "Folder does not exist", issues, checkResourceExistence);
        ValidateResources(workspace.Files, "Files", File.Exists, "File does not exist", issues, checkResourceExistence);
        ValidateUrls(workspace.Urls, issues);
        ValidateActions(workspace.PreLaunchActions, "Before launch actions", issues);
        ValidateActions(workspace.PostLaunchActions, "After launch actions", issues);
        return new(issues);
    }

    private static void ValidateApplications(IReadOnlyList<WorkspaceApplication> items, ICollection<WorkspaceValidationIssue> issues)
    {
        foreach (var item in items)
        {
            if (string.IsNullOrWhiteSpace(item.ApplicationIdentifier)) issues.Add(new("Applications", "Every application needs an application name or executable."));
            if (item.DelayAfterLaunchMs is < 0 or > 120000) issues.Add(new("Applications", $"Delay for '{item.DisplayName}' must be between 0 and 120000 ms."));
            if (item.TargetMonitor is < 1) issues.Add(new("Applications", $"Monitor for '{item.DisplayName}' must be 1 or greater."));
            if (item.Layout.HasValue && !Enum.IsDefined(item.Layout.Value)) issues.Add(new("Applications", $"Layout for '{item.DisplayName}' is invalid."));
        }
        AddDuplicateIssues(items.Select(item => item.CatalogApplicationId ?? item.ApplicationIdentifier), "Applications", issues);
    }

    private static void ValidateResources(IReadOnlyList<WorkspaceResource> items, string field, Func<string, bool> exists, string error, ICollection<WorkspaceValidationIssue> issues, bool checkExistence)
    {
        foreach (var item in items)
        {
            if (string.IsNullOrWhiteSpace(item.Value)) issues.Add(new(field, $"Every {field.ToLowerInvariant().TrimEnd('s')} needs a value."));
            else if (checkExistence && !exists(Environment.ExpandEnvironmentVariables(item.Value))) issues.Add(new(field, $"{error}: {item.Value}"));
        }
        AddDuplicateIssues(items.Select(item => item.Value), field, issues);
    }

    private static void ValidateUrls(IReadOnlyList<WorkspaceResource> items, ICollection<WorkspaceValidationIssue> issues)
    {
        foreach (var item in items)
        {
            if (!Uri.TryCreate(item.Value, UriKind.Absolute, out var uri) || (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
                issues.Add(new("URLs", $"Only absolute HTTP or HTTPS URLs are allowed: {item.Value}"));
        }
        AddDuplicateIssues(items.Select(item => item.Value), "URLs", issues);
    }

    private static void ValidateActions(IEnumerable<WorkspaceSafeAction> actions, string field, ICollection<WorkspaceValidationIssue> issues)
    {
        if (actions.Any(item => string.IsNullOrWhiteSpace(item.Target))) issues.Add(new(field, "Every safe action needs an application or window target."));
        if (actions.Any(item => !Enum.IsDefined(item.Type))) issues.Add(new(field, "One or more safe actions have an invalid type."));
    }

    private static void AddDuplicateIssues(IEnumerable<string> values, string field, ICollection<WorkspaceValidationIssue> issues)
    {
        if (values.Where(value => !string.IsNullOrWhiteSpace(value)).GroupBy(value => value.Trim(), StringComparer.OrdinalIgnoreCase).Any(group => group.Count() > 1))
            issues.Add(new(field, $"Duplicate {field.ToLowerInvariant()} are not allowed."));
    }
}
