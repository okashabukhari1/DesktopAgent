using DesktopAgent.Core.Ai;
using DesktopAgent.Core.Applications.Interfaces;
using DesktopAgent.Core.Interfaces;
using DesktopAgent.Core.Projects;
using DesktopAgent.Core.Workspaces.Interfaces;
using DesktopAgent.Core.Automation.Interfaces;
using DesktopAgent.Core.Automation.Models;
using DesktopAgent.Core.Automation.Enums;

namespace DesktopAgent.Infrastructure.Projects;

public sealed class ProjectOpenService(IProjectCatalog catalog, IApplicationCatalog applications,
    IProjectEditorLauncher editor, ISettingsService settings, IWorkspaceService workspaces,
    CurrentProjectContext context, IActionHistoryService history) : IProjectOpenService
{
    public async Task<ToolResult> OpenAsync(string project, string? editorId = null, bool useWorkspace = true, CancellationToken cancellationToken = default)
    {
        await catalog.InitializeAsync(cancellationToken);
        var resolution = catalog.Resolve(project);
        if (resolution.Choices.Count > 0) return new(false, "Ambiguous", "Choose a project; no project was opened.",
            Choices: resolution.Choices.Select(p => new ApplicationChoice(p.Id.ToString(), p.Name + " · " + p.TechnologySummary + " · " + p.Path, "project")).ToArray());
        if (resolution.Project is not { } p) return new(false, "NotFound", "No matching project is in the catalog. Add its folder on Projects.");
        try
        {
            ProjectDiscoveryService.ValidatePath(p.Path);
            if (!Directory.Exists(p.Path)) return Failure(p, "Its folder is no longer available. Use Locate Project on Projects.");
            await workspaces.InitializeAsync(cancellationToken);
            var messages = new List<string>(); bool partial = false;
            if (useWorkspace && !p.AssociatedWorkspaceId.HasValue && workspaces.Workspaces.Count(w => w.ProjectId == p.Id) > 1)
                return new(false, "Ambiguous", "Multiple workspaces reference this project. Choose its preferred workspace on Projects before opening.");
            var workspace = p.AssociatedWorkspaceId.HasValue ? workspaces.Find(p.AssociatedWorkspaceId.Value.ToString()) :
                workspaces.Workspaces.FirstOrDefault(w => w.ProjectId == p.Id);
            bool workspaceStarted = false;
            if (useWorkspace && workspace is not null)
            {
                var result = await workspaces.LaunchAsync(workspace.Id, cancellationToken);
                messages.Add(result?.Message ?? "Associated workspace is unavailable.");
                workspaceStarted = result?.Success == true;
                partial = result is null || result.Session.Status != DesktopAgent.Core.Workspaces.Enums.WorkspaceLaunchStatus.Ready;
            }
            else if (useWorkspace && p.AssociatedWorkspaceId.HasValue) { messages.Add("Associated workspace is unavailable; trying the configured editor."); partial = true; }
            var requested = editorId;
            if (!string.IsNullOrWhiteSpace(requested) && applications.FindById(requested) is null)
            {
                var resolvedEditor = applications.Resolve(requested);
                if (resolvedEditor.Application is null) return Failure(p, string.Join("\n", messages.Append("Editor not found or ambiguous. Choose an installed editor from Projects.")));
                requested = resolvedEditor.Application.Id;
            }
            var defaults = settings.Current.Projects;
            var configured = p.Technologies.Contains("Android") ? defaults.AndroidEditorId : p.Technologies.Contains(".NET") ? defaults.DotNetEditorId : defaults.WebEditorId;
            var candidates = requested is not null ? new[] { requested } : new[] { p.PreferredEditorId, configured }
                .Concat(applications.Applications.Where(a => editor.CanOpen(a.Id) &&
                    (System.IO.Path.GetFileName(a.ExecutablePath ?? "").Equals("Code.exe", StringComparison.OrdinalIgnoreCase) ||
                     (p.Technologies.Contains(".NET") && System.IO.Path.GetFileName(a.ExecutablePath ?? "").Equals("devenv.exe", StringComparison.OrdinalIgnoreCase)) ||
                     (p.Technologies.Contains("Android") && System.IO.Path.GetFileName(a.ExecutablePath ?? "").ToLowerInvariant() is "studio64.exe" or "studio.exe"))).OrderBy(a =>
                    p.Technologies.Contains("Android") && a.DisplayName.Contains("Android", StringComparison.OrdinalIgnoreCase) ? 0 :
                    p.Technologies.Contains(".NET") && a.ProcessName?.Equals("devenv", StringComparison.OrdinalIgnoreCase) == true ? 0 :
                    a.ProcessName?.Equals("Code", StringComparison.OrdinalIgnoreCase) == true ? 1 : 2).ThenBy(a => a.Id).Select(a => (string?)a.Id));
            var chosen = candidates.FirstOrDefault(id => id is not null && editor.CanOpen(id));
            if (chosen is null)
            {
                messages.Add("No supported installed editor is available. Select VS Code, Visual Studio, or Android Studio in Projects preferences.");
                if (!workspaceStarted) return Failure(p, string.Join("\n", messages));
                partial = true;
            }
            else
            {
                var opened = await editor.OpenAsync(chosen, p, cancellationToken);
                messages.Add(opened.Message);
                if (!opened.Success && !workspaceStarted) return Failure(p, string.Join("\n", messages));
                partial |= !opened.Success;
            }
            cancellationToken.ThrowIfCancellationRequested();
            await catalog.MarkOpenedAsync(p.Id, cancellationToken); context.Set(catalog.Find(p.Id)!, useWorkspace ? workspace?.Id : null);
            var message = p.Name + ": " + string.Join("\n", messages);
            history.Add(new(DateTimeOffset.Now, ComputerCommandType.AiToolRequest, p.Name, partial ? ActionStatus.Partial : ActionStatus.Success, message, "Projects"));
            return new(true, partial ? "Partial" : "Success", message, ProjectTools.Metadata(catalog.Find(p.Id)!), Target: p.Name);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or ArgumentException or InvalidOperationException)
        { return Failure(p, "The project could not be opened. Check its location and configured editor."); }
    }
    private ToolResult Failure(ProjectProfile p, string message)
    {
        history.Add(new(DateTimeOffset.Now, ComputerCommandType.AiToolRequest, p.Name, ActionStatus.Failed, message, "Projects"));
        return new(false, "Failed", p.Name + ": " + message, Target: p.Name);
    }
}
