using System.ComponentModel;
using System.Diagnostics;
using DesktopAgent.Core.Ai;
using DesktopAgent.Core.Applications.Interfaces;
using DesktopAgent.Core.Interfaces;
using DesktopAgent.Core.Projects;
using DesktopAgent.Infrastructure.Windows.Automation;

namespace DesktopAgent.Infrastructure.Projects;

public sealed class ProjectFileOpenService(ProjectFileService files, IProjectCatalog projects,
    IApplicationCatalog applications, IProjectEditorLauncher editors, ISettingsService settings) : IProjectFileOpenService
{
    public static ProcessStartInfo CreateStartInfo(string executable, string filePath, int line, string projectRoot)
    {
        if (!WindowsProjectEditorLauncher.SupportedExecutable(executable) || line is < 1 or > 100000) throw new ArgumentException("Unsupported editor or line.");
        var info = new ProcessStartInfo(executable) { UseShellExecute = false, WorkingDirectory = projectRoot };
        if (Path.GetFileName(executable).Equals("Code.exe", StringComparison.OrdinalIgnoreCase))
        { info.ArgumentList.Add("--reuse-window"); info.ArgumentList.Add("--goto"); info.ArgumentList.Add(filePath + ":" + line + ":1"); }
        else { if (Path.GetFileName(executable).Equals("devenv.exe", StringComparison.OrdinalIgnoreCase)) info.ArgumentList.Add("/Edit"); info.ArgumentList.Add(filePath); }
        return info;
    }
    public async Task<ToolResult> OpenAsync(Guid projectId, string relativePath, int line = 1, CancellationToken cancellationToken = default)
    {
        if (line is < 1 or > 100000) throw new ProjectFileException("InvalidRange", "Line must be 1–100000.");
        await projects.InitializeAsync(cancellationToken);
        var project = projects.Find(projectId) ?? throw new ProjectFileException("ProjectNotFound", "Choose a registered project.");
        var defaults = settings.Current.Projects;
        var defaultId = project.Technologies.Contains("Android") ? defaults.AndroidEditorId : project.Technologies.Contains(".NET") ? defaults.DotNetEditorId : defaults.WebEditorId;
        var candidate = new[] { project.PreferredEditorId, defaultId }.Concat(applications.Applications
            .Where(a => a.ExecutablePath is not null && (Path.GetFileName(a.ExecutablePath).Equals("Code.exe", StringComparison.OrdinalIgnoreCase)
                || project.Technologies.Contains(".NET") && Path.GetFileName(a.ExecutablePath).Equals("devenv.exe", StringComparison.OrdinalIgnoreCase)
                || project.Technologies.Contains("Android") && Path.GetFileName(a.ExecutablePath).ToLowerInvariant() is "studio64.exe" or "studio.exe"))
            .OrderBy(a => a.Id).Select(a => (string?)a.Id)).FirstOrDefault(id => id is not null && editors.CanOpen(id));
        if (candidate is null || applications.FindById(candidate)?.ExecutablePath is not { } executable)
            return new(false, "EditorUnavailable", "Select a supported installed editor in Projects preferences. No file was opened.");
        return await files.WithPinnedFileAsync(projectId, relativePath, full =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                using var process = Process.Start(CreateStartInfo(executable, full, line, project.Path));
                if (process is null) return Task.FromResult(new ToolResult(false, "Failed", "The editor did not start."));
                var gotoLine = Path.GetFileName(executable).Equals("Code.exe", StringComparison.OrdinalIgnoreCase);
                return Task.FromResult(new ToolResult(true, "Success", gotoLine ? "Sent the project file to the editor at line " + line + "." :
                    "Sent the file to the project editor. Line navigation is not supported by this adapter.",
                    Sources: [new(projectId, Path.GetRelativePath(project.Path, full), line, line)]));
            }
            catch (Exception e) when (e is Win32Exception or IOException or UnauthorizedAccessException or InvalidOperationException)
            { return Task.FromResult(new ToolResult(false, "Failed", "Windows could not open the file in the configured editor. Check installation and application control.")); }
        }, cancellationToken);
    }
}
