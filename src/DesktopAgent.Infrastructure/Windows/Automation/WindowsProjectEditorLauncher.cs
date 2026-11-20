using System.Diagnostics;
using System.ComponentModel;
using DesktopAgent.Core.Ai;
using DesktopAgent.Core.Applications.Interfaces;
using DesktopAgent.Core.Projects;
using DesktopAgent.Infrastructure.Projects;

namespace DesktopAgent.Infrastructure.Windows.Automation;

public sealed class WindowsProjectEditorLauncher(IApplicationCatalog catalog) : IProjectEditorLauncher
{
    public bool CanOpen(string applicationId)
    {
        var app = catalog.FindById(applicationId);
        return app is { IsInstalled: true, IsLaunchable: true, ExecutablePath: not null }
            && File.Exists(app.ExecutablePath) && SupportedExecutable(app.ExecutablePath);
    }
    public static bool SupportedExecutable(string path) => Path.GetFileName(path).ToLowerInvariant() is "code.exe" or "devenv.exe" or "studio64.exe" or "studio.exe";
    public static ProcessStartInfo CreateStartInfo(string executable, ProjectProfile project)
    {
        if (!SupportedExecutable(executable)) throw new ArgumentException("Unsupported project editor.");
        var root = ProjectDiscoveryService.ValidatePath(project.Path);
        if (!Directory.Exists(root)) throw new ArgumentException("Project folder is unavailable.");
        var info = new ProcessStartInfo(executable) { UseShellExecute = false, WorkingDirectory = root };
        var name = Path.GetFileName(executable).ToLowerInvariant();
        if (name == "code.exe") { info.ArgumentList.Add("--new-window"); info.ArgumentList.Add(root); }
        else if (name == "devenv.exe")
        {
            var solution = project.DetectedFiles.Where(f => Path.GetExtension(f).ToLowerInvariant() is ".sln" or ".slnx" or ".csproj" or ".fsproj")
                .Select(f => Path.GetFullPath(Path.Combine(root, f))).FirstOrDefault(f => f.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)
                    && File.Exists(f) && !File.GetAttributes(f).HasFlag(FileAttributes.ReparsePoint));
            if (solution is null) info.ArgumentList.Add(root);
            else info.ArgumentList.Add(solution);
        }
        else info.ArgumentList.Add(root);
        return info;
    }
    public Task<ToolResult> OpenAsync(string applicationId, ProjectProfile project, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var app = catalog.FindById(applicationId);
        if (app is null || !CanOpen(applicationId)) return Task.FromResult(new ToolResult(false, "NotFound", "Configured editor is no longer available."));
        try
        {
            using var process = Process.Start(CreateStartInfo(app.ExecutablePath!, project));
            // A process start is not proof the editor loaded this project. Say precisely what happened.
            return Task.FromResult(process is null ? new ToolResult(false, "Failed", "The editor could not be started.") :
                new ToolResult(true, "Success", "Sent " + project.Name + " to " + app.DisplayName + ".", Target: app.DisplayName));
        }
        catch (Exception e) when (e is Win32Exception or IOException or UnauthorizedAccessException or ArgumentException or InvalidOperationException)
        { return Task.FromResult(new ToolResult(false, "Failed", "Windows could not start the project editor. Check installation and application-control policy.")); }
    }
}
