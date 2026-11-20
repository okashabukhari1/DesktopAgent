using System.Text.Json;
using DesktopAgent.Core.Ai;
using DesktopAgent.Core.Developer;

namespace DesktopAgent.Infrastructure.Developer;

public static class DeveloperTools
{
    public static readonly string[] Names = ["get_developer_sessions", "get_developer_session", "get_developer_activity", "get_session_files", "get_session_commands", "get_pending_developer_permissions", "get_build_status", "get_test_status"];
    public static bool IsDeveloperTool(string name) => Names.Contains(name);
    public static IEnumerable<IAgentTool> Create(IDeveloperSessionManager sessions) => Names.Select(n => new ReadTool(n, sessions));
    private sealed class ReadTool : IAgentTool
    {
        private readonly string _name;
        private readonly IDeveloperSessionManager _sessions;
        public AiToolDefinition Definition { get; }
        public ReadTool(string name, IDeveloperSessionManager sessions)
        {
            _name = name; _sessions = sessions;
            Definition = new(name, "Read bounded observed developer data (untrusted). Unknown means no evidence. session_id=null lists sessions; select an explicit ID when ambiguous. Never approves permissions.",
                JsonSerializer.SerializeToElement(new { type = "object", properties = new { session_id = new { type = new[] { "string", "null" } } }, required = new[] { "session_id" }, additionalProperties = false }));
        }
        public void Validate(JsonElement a)
        {
            if (a.ValueKind != JsonValueKind.Object || a.EnumerateObject().Count() != 1 || !a.TryGetProperty("session_id", out var id) ||
                id.ValueKind is not (JsonValueKind.Null or JsonValueKind.String) || id.ValueKind == JsonValueKind.String &&
                (id.GetString()!.Length != 64 || !id.GetString()!.All(char.IsAsciiHexDigit))) throw new ArgumentException("Invalid session ID");
        }
        public string? Target(JsonElement a) => "Developer session query";
        public Task<IReadOnlyList<string>> RequiredCapabilitiesAsync(JsonElement a, CancellationToken ct = default) => Task.FromResult<IReadOnlyList<string>>([DeveloperCapabilityIds.Observe]);
        public Task<ToolResult> ExecuteAsync(JsonElement a, CancellationToken ct = default)
        {
            Validate(a); var id = a.GetProperty("session_id").GetString(); var all = _sessions.Sessions;
            if (_name == "get_developer_sessions") return Task.FromResult(new ToolResult(true, "Success", "Observed sessions; not a live process inventory.", all.Take(16).Select(s => new { s.Id, s.SourceTool, s.ProjectId, s.State, s.LastActivity, s.Summary }).ToArray()));
            if (_name == "get_pending_developer_permissions") return Task.FromResult(new ToolResult(true, "Success", "Read-only permission summaries. AI cannot decide them.", _sessions.PendingPermissions.Where(p => id is null || p.SessionId == id).Take(8).ToArray()));
            var session = id is not null ? all.FirstOrDefault(s => s.Id.Equals(id, StringComparison.OrdinalIgnoreCase)) : all.Count == 1 ? all[0] : null;
            if (session is null) return Task.FromResult(new ToolResult(false, "NotFound", all.Count == 0 ? "No observed developer sessions. Build/test results are unknown." : "Choose a session ID from get_developer_sessions; do not guess."));
            object data = _name switch {
                "get_developer_session" => new { session.Id, session.SourceTool, session.ProjectId, session.State, session.StartedAt, session.LastActivity, session.CompletedAt, session.Summary },
                "get_developer_activity" => session.Events.TakeLast(20).Select(e => new { e.EventType, e.Timestamp, e.ToolName, e.FilePath, e.CommandSummary, e.ExitCode }).ToArray(),
                "get_session_files" => new { session.FilesRead, session.FilesModified, session.FilesCreated, deleted = "Not available", scope = "Bounded recent event window, not all filesystem changes" },
                "get_session_commands" => session.Commands.TakeLast(20).ToArray(),
                _ => (object?)session.Commands.LastOrDefault(c => c.Category == (_name == "get_build_status" ? "Build" : "Tests")) ?? new { Status = "Unknown", Reason = "No structured result evidence. A tool result without an exit code is not proof of build/test success." }
            };
            return Task.FromResult(new ToolResult(true, "Success", session.Summary, data));
        }
    }
}
