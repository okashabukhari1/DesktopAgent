using System.Text.Json;
using System.Security.Cryptography;
using DesktopAgent.Core.Developer;

namespace DesktopAgent.Infrastructure.Developer;

// Deliberately limited to the documented v1 plugin event shape. No server API, token access or permissions.
public sealed class OpenCodeAdapter : IDeveloperToolAdapter
{
    private readonly DeveloperSupervisor _supervisor;
    public string ToolId => "opencode";
    public string DisplayName => "OpenCode (v1 session events)";
    public string PluginPath { get; }
    public string RelayPath { get; }
    public OpenCodeAdapter(DeveloperSupervisor supervisor, string? pluginPath = null, string? relayPath = null)
    {
        _supervisor = supervisor;
        PluginPath = pluginPath ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".config", "opencode", "plugins", "desktop-agent-events.js");
        RelayPath = relayPath ?? Path.Combine(AppContext.BaseDirectory, "agent-dev-hook.exe");
    }
    public string PluginSource => """
        // Desktop Agent read-only OpenCode v1 event bridge. No permission decisions or source/output capture.
        import { spawn } from "node:child_process";
        export const DesktopAgentEvents = async ({ directory }) => {
          const sessions = new Map(); const queue = []; let busy = false;
          const relay = RELAY_PATH;
          const drain = () => {
            if (busy || !queue.length) return; busy = true;
            const payload = queue.shift();
            let child;
            try { child = spawn(relay, ["--desktop-agent-opencode-v1"], { shell: false, windowsHide: true, stdio: ["pipe", "ignore", "ignore"] }); }
            catch { busy = false; return; }
            let done = false;
            const finish = () => { if (done) return; done = true; clearTimeout(timer); busy = false; setTimeout(drain, 200).unref?.(); };
            const timer = setTimeout(() => { child.kill(); finish(); }, 3000); timer.unref?.();
            child.on("error", finish); child.on("exit", finish); child.stdin.on("error", () => {});
            child.stdin.end(JSON.stringify(payload));
          };
          const send = (event_type, session_id, cwd) => {
            if (typeof session_id !== "string" || !/^[A-Za-z0-9_.:-]{1,128}$/.test(session_id) || typeof cwd !== "string" || cwd.length > 1024) return;
            if (queue.length >= 16) return;
            queue.push({ event_type, session_id, cwd }); drain();
          };
          return { event: async ({ event }) => {
            try {
              const p = event.properties;
              if (event.type === "session.created" || event.type === "session.updated") {
                const id = p?.info?.id; const cwd = p?.info?.directory;
                if (!sessions.has(id) && typeof id === "string" && /^[A-Za-z0-9_.:-]{1,128}$/.test(id) && typeof cwd === "string" && cwd.length <= 1024) {
                  if (sessions.size >= 64) sessions.delete(sessions.keys().next().value);
                  sessions.set(id, cwd); send("session.created", id, cwd);
                }
              } else if (sessions.has(p?.sessionID)) {
                if (event.type === "session.status" && p.status?.type === "busy") send("session.busy", p.sessionID, sessions.get(p.sessionID));
                if (event.type === "session.idle" || event.type === "session.status" && p.status?.type === "idle") send("session.idle", p.sessionID, sessions.get(p.sessionID));
                if (event.type === "session.error") send("session.error", p.sessionID, sessions.get(p.sessionID));
              }
              // file.edited lacks sessionID: do not falsely associate files with whichever session is active.
            } catch { /* Never break OpenCode's event loop. */ }
          } };
        };
        """.Replace("RELAY_PATH", JsonSerializer.Serialize(RelayPath), StringComparison.Ordinal);
    public Task StartAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    public Task<IntegrationStatus> GetStatusAsync(CancellationToken cancellationToken)
    {
        var configured = false;
        try { configured = File.Exists(RelayPath) && File.Exists(PluginPath) && new FileInfo(PluginPath).Length < 32768 && File.ReadAllText(PluginPath) == PluginSource; } catch (IOException) { } catch (UnauthorizedAccessException) { }
        var recent = configured && _supervisor.Sessions.Any(s => s.SourceTool == ToolId && s.LastActivity > DateTimeOffset.UtcNow.AddMinutes(-2));
        return Task.FromResult(new IntegrationStatus(ToolId, DisplayName, DeveloperDetection.HasExecutable("opencode.exe", "opencode.cmd", "opencode.ps1"), configured, recent,
            recent ? "Limited / recent session events" : configured ? "Limited / configured" : "Not configured",
            "Opt-in v1 plugin: session start/busy/idle/error only. No file attribution, command results or approval. v2 plugin APIs are not supported."));
    }
    public async Task InstallConfirmedAsync(CancellationToken ct = default)
    {
        CheckPath(); if (!File.Exists(RelayPath)) throw new InvalidOperationException("Relay missing");
        if (File.Exists(PluginPath))
        {
            if (new FileInfo(PluginPath).Length > 32768 || await File.ReadAllTextAsync(PluginPath, ct) != PluginSource) throw new InvalidOperationException("Existing plugin differs; refusing to overwrite user content.");
            return;
        }
        Directory.CreateDirectory(Path.GetDirectoryName(PluginPath)!); CheckPath();
        var temp = PluginPath + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            await using (var file = new FileStream(temp, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            { var bytes = System.Text.Encoding.UTF8.GetBytes(PluginSource); await file.WriteAsync(bytes, ct); await file.FlushAsync(ct); }
            CheckPath(); File.Move(temp, PluginPath);
        }
        finally { if (File.Exists(temp)) File.Delete(temp); }
    }
    public async Task RemoveConfirmedAsync(CancellationToken ct = default)
    {
        CheckPath(); if (!File.Exists(PluginPath)) return;
        if (new FileInfo(PluginPath).Length > 32768 || await File.ReadAllTextAsync(PluginPath, ct) != PluginSource) throw new InvalidOperationException("Plugin differs; refusing to remove user content.");
        File.Copy(PluginPath, PluginPath + ".backup-" + DateTime.UtcNow.ToString("yyyyMMddHHmmssfffffff"));
        File.Delete(PluginPath);
    }
    private void CheckPath()
    {
        if (!DeveloperEventNormalizer.ValidAbsolute(PluginPath) || !DeveloperEventNormalizer.ValidAbsolute(RelayPath)) throw new InvalidOperationException("Invalid plugin path");
        for (var p = PluginPath; !string.IsNullOrEmpty(p); p = Path.GetDirectoryName(p))
            if ((File.Exists(p) || Directory.Exists(p)) && File.GetAttributes(p).HasFlag(FileAttributes.ReparsePoint)) throw new InvalidOperationException("Linked plugin path not supported");
    }
}
