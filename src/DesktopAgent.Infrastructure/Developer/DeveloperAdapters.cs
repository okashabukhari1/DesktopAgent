using DesktopAgent.Core.Developer;

namespace DesktopAgent.Infrastructure.Developer;

public sealed class ClaudeCodeAdapter(ClaudeHookInstaller installer, DeveloperPipeServer pipe, DeveloperSupervisor supervisor) : IDeveloperToolAdapter
{
    public string ToolId => "claude-code";
    public string DisplayName => "Claude Code";
    public Task StartAsync(CancellationToken cancellationToken) { pipe.Start(); return Task.CompletedTask; }
    public Task StopAsync(CancellationToken cancellationToken) { pipe.Dispose(); return Task.CompletedTask; }
    public async Task<IntegrationStatus> GetStatusAsync(CancellationToken cancellationToken)
    {
        var configured = await installer.IsConfiguredAsync(cancellationToken);
        var recent = pipe.Available && supervisor.LastReceivedAt > DateTimeOffset.UtcNow.AddMinutes(-2);
        return new(ToolId, DisplayName, DeveloperDetection.HasExecutable("claude.exe", "claude.cmd"), configured, recent,
            recent ? "Connected (recent events)" : configured ? "Configured / waiting for events" : "Not configured",
            "Local Windows hooks only. Recent events are not a process heartbeat. Restart/resume Claude after installing hooks. WSL/cloud sessions are not supported.");
    }
}
public sealed class DetectionOnlyDeveloperAdapter(string id, string name, params string[] executableNames) : IDeveloperToolAdapter
{
    public string ToolId => id;
    public string DisplayName => name;
    public Task StartAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    public Task<IntegrationStatus> GetStatusAsync(CancellationToken cancellationToken)
    {
        var detected = DeveloperDetection.HasExecutable(executableNames);
        return Task.FromResult(new IntegrationStatus(id, name, detected, false, false, detected ? "Detected / integration not configured" : "Not detected",
            "Detection is limited to PATH executables. No session observation, approval, log scraping or process control is enabled for this tool."));
    }
}
public static class DeveloperDetection
{
    public static bool HasExecutable(params string[] names)
    {
        var paths = (Environment.GetEnvironmentVariable("PATH") ?? "").Split(Path.PathSeparator).Take(128)
            .Concat([Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".local", "bin")]);
        foreach (var p in paths)
            foreach (var name in names)
                try { if (Path.IsPathFullyQualified(p) && File.Exists(Path.Combine(p.Trim('"'), name))) return true; }
                catch (Exception ex) when (ex is ArgumentException or IOException or UnauthorizedAccessException) { }
        return false;
    }
}
