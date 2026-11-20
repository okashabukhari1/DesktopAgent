using System.Text.Json;
using System.Text.RegularExpressions;
using System.Text;
using System.Security.Cryptography;
using DesktopAgent.Core.Developer;
using DesktopAgent.Core.Permissions;
using DesktopAgent.Core.Projects;

namespace DesktopAgent.Infrastructure.Developer;

public sealed class DeveloperEventNormalizer(IProjectCatalog projects, ISecretProtection secrets)
{
    public const int MaxPayloadBytes = 262144;
    public static readonly string[] ClaudeEvents = ["SessionStart", "UserPromptSubmit", "PreToolUse", "PostToolUse", "PostToolUseFailure", "PermissionRequest", "Notification", "Stop", "SessionEnd"];
    private static readonly Regex Identifier = new(@"^[A-Za-z0-9_.:-]{1,128}$", RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(50));
    public (DeveloperEvent Event, DeveloperRisk Risk)? Normalize(string source, ReadOnlyMemory<byte> payload)
    {
        if (source is not ("claude-code" or "opencode") || payload.Length is 0 or > MaxPayloadBytes) return null;
        try
        {
            using var doc = JsonDocument.Parse(payload, new() { MaxDepth = 16 });
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object) return null;
            // Duplicate keys are ambiguous and must not cross the trust boundary.
            if (!Unique(root)) return null;
            if (source == "opencode")
            {
                if (root.EnumerateObject().Count() != 3) return null;
                var type = Text(root, "event_type", 40);
                var mapped = type switch { "session.created" => "SessionStart", "session.busy" => "UserPromptSubmit", "session.idle" => "Stop", "session.error" => "SessionEnd", _ => null };
                if (mapped is null) return null;
                var normalized = Normalize("claude-code", JsonSerializer.SerializeToUtf8Bytes(new { hook_event_name = mapped,
                    session_id = Text(root, "session_id", 128), cwd = Text(root, "cwd", 1024) }));
                if (normalized is not { } n) return null;
                return (n.Event with { SourceTool = "opencode", EventType = type == "session.error" ? DeveloperEventType.SessionFailed : n.Event.EventType }, n.Risk);
            }
            var hook = Text(root, "hook_event_name", 80); var session = Text(root, "session_id", 128); var cwd = Text(root, "cwd", 1024);
            if (hook is null || !ClaudeEvents.Contains(hook) || session is null || !Identifier.IsMatch(session) || !ValidAbsolute(cwd)) return null;
            cwd = Path.GetFullPath(cwd!).TrimEnd(Path.DirectorySeparatorChar);
            var project = projects.Projects.Where(p => cwd.Equals(p.Path.TrimEnd(Path.DirectorySeparatorChar), StringComparison.OrdinalIgnoreCase) ||
                cwd.StartsWith(p.Path.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                .OrderByDescending(p => p.Path.Length).FirstOrDefault();
            var tool = Text(root, "tool_name", 80);
            var call = Text(root, "tool_use_id", 128);
            if (call is not null && !Identifier.IsMatch(call)) return null;
            var input = root.TryGetProperty("tool_input", out var ti) && ti.ValueKind == JsonValueKind.Object ? ti : default;
            var command = Text(input, "command", 2048);
            var file = Text(input, "file_path", 1024);
            if (tool is "Bash" or "PowerShell" && command is null || tool is "Read" or "Edit" or "Write" && file is null) return null;
            var risk = DeveloperRiskClassifier.Classify(tool, command);
            if (command is not null && tool is not ("Bash" or "PowerShell")) return null;
            string? relative = null;
            if (file is not null)
            {
                if (!ValidAbsolute(file) || project is null) return null;
                var full = Path.GetFullPath(file);
                var prefix = project.Path.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
                if (!full.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) return null;
                relative = Path.GetRelativePath(project.Path, full);
                if (relative.Split(Path.DirectorySeparatorChar).Any(secrets.IsSensitiveName) || HasLinkedAncestor(full)) return null;
            }
            var kind = hook switch {
                "SessionStart" => DeveloperEventType.SessionStarted,
                "UserPromptSubmit" => DeveloperEventType.Working,
                "PreToolUse" when command is not null => DeveloperEventType.CommandRequested,
                "PreToolUse" => DeveloperEventType.ToolRequested,
                "PostToolUse" when tool == "Read" && relative is not null => DeveloperEventType.FileRead,
                "PostToolUse" when tool == "Edit" && relative is not null => DeveloperEventType.FileModified,
                "PostToolUse" when tool == "Write" && relative is not null => DeveloperEventType.FileModified,
                "PostToolUse" when command is not null => DeveloperEventType.CommandCompleted,
                "PostToolUse" => DeveloperEventType.Working,
                "PostToolUseFailure" => DeveloperEventType.CommandFailed,
                "PermissionRequest" => DeveloperEventType.PermissionRequested,
                "Notification" => DeveloperEventType.WaitingForUser,
                "Stop" => DeveloperEventType.TurnCompleted,
                "SessionEnd" => DeveloperEventType.SessionCompleted,
                _ => DeveloperEventType.Working };
            if (hook is "PreToolUse" or "PostToolUse" or "PostToolUseFailure" && (tool is null || call is null)) return null;
            if (hook == "PermissionRequest" && tool is null) return null;
            if (hook == "Notification" && Text(root, "notification_type", 80) is not ("permission_prompt" or "idle_prompt" or "agent_needs_input")) return null;
            if (hook == "PostToolUse" && tool == "Write" && relative is not null && root.TryGetProperty("tool_response", out var response) && Text(response, "type", 30) == "create") kind = DeveloperEventType.FileCreated;
            int? exit = null;
            if (root.TryGetProperty("tool_response", out var result) && result.ValueKind == JsonValueKind.Object &&
                result.TryGetProperty("exit_code", out var value) && value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out var number)) exit = number;
            // Never retain tool_input content, transcript, prompts, stdout, stderr, or environment data.
            var summary = command is null ? null : secrets.Redact(command, out _);
            if (command is not null && (DeveloperRiskClassifier.HasCredential(command) || summary != command))
            {
                summary = "[command contains sensitive data; use the native tool prompt]";
                risk = risk with { Blocked = true, Risk = PermissionRisk.Critical, Reason = "Sensitive command; native review required." };
            }
            if (summary is not null && summary.Length > 512) summary = summary[..512] + "…";
            // IDs are opaque identity keys, not text to display/store; hashing also prevents a hostile
            // sender disguising credential text as a session/tool-call identifier.
            string Opaque(string value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)));
            var safeTool = tool is null ? null : secrets.Redact(tool, out _);
            return (new(Guid.NewGuid(), source, kind, Opaque(session), DateTimeOffset.UtcNow, project?.Id, cwd, safeTool,
                call is null ? null : Opaque(call), relative, summary, risk.Category, exit, risk.Risk), risk);
        }
        catch (Exception ex) when (ex is JsonException or ArgumentException or InvalidOperationException or NotSupportedException or IOException or UnauthorizedAccessException or RegexMatchTimeoutException) { return null; }
    }
    private static bool Unique(JsonElement e)
    {
        if (e.ValueKind == JsonValueKind.Object)
        {
            var names = new HashSet<string>(StringComparer.Ordinal);
            foreach (var p in e.EnumerateObject()) if (!names.Add(p.Name) || !Unique(p.Value)) return false;
        }
        else if (e.ValueKind == JsonValueKind.Array) foreach (var child in e.EnumerateArray()) if (!Unique(child)) return false;
        return true;
    }
    private static string? Text(JsonElement e, string name, int max)
    {
        if (e.ValueKind != JsonValueKind.Object || !e.TryGetProperty(name, out var p)) return null;
        if (p.ValueKind == JsonValueKind.Null) return null;
        if (p.ValueKind != JsonValueKind.String || p.GetString() is not { } s || s.Length > max || s.Any(char.IsControl)) throw new ArgumentException("Invalid hook field");
        return s;
    }
    public static bool ValidAbsolute(string? path) => path is { Length: > 3 and <= 1024 } && Path.IsPathFullyQualified(path) &&
        !path.StartsWith(@"\\", StringComparison.Ordinal) && !path.Any(char.IsControl) &&
        !path[2..].Contains(':') && !path.Split('\\', '/').Any(p => p is "." or ".." || p.EndsWith(' ') || p.EndsWith('.'));
    private static bool HasLinkedAncestor(string file)
    {
        for (var p = file; !string.IsNullOrEmpty(p); p = Path.GetDirectoryName(p))
            if ((File.Exists(p) || Directory.Exists(p)) && File.GetAttributes(p).HasFlag(FileAttributes.ReparsePoint)) return true;
        return false;
    }
}

public static class DeveloperRiskClassifier
{
    private static bool Match(string command, string expression) => Regex.IsMatch(command, expression, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(50));
    public static bool HasCredential(string command) => Match(command, @"password|passwd|secret|api[_-]?key|token|credential|\.env|\.ssh|\.aws|private.?key|BEGIN .*PRIVATE");
    public static DeveloperRisk Classify(string? tool, string? command)
    {
        if (command is null)
            return tool switch {
                "Read" => new(PermissionRisk.Medium, "File read", [DeveloperCapabilityIds.ReadFile], false, "Reads project content."),
                "Edit" or "Write" => new(PermissionRisk.High, "File modification", [DeveloperCapabilityIds.ModifyFile], true, "Review the full diff in the native prompt; this summary-only bridge cannot authorize unseen content."),
                _ => new(PermissionRisk.High, "Unknown tool", [DeveloperCapabilityIds.RunCommand], true, "Unsupported tool permissions remain in the native tool.") };
        if (HasCredential(command) || Match(command, @"\brm\b|\bdel\b|remove-item|rmdir|format|diskpart|reg(\.exe)?\s|registry|sudo|runas|Verb\s+RunAs|taskkill|stop-process|kill\b|git\s+(reset|clean|push|rebase)|--force|invoke-expression|\biex\b|encodedcommand|[;&|<>`\r\n]|\$\(|%|\$\{|start-process"))
            return new(PermissionRisk.Critical, "Restricted command", [DeveloperCapabilityIds.RunCommand], true, "Destructive, elevated, sensitive, compound or dynamically evaluated commands cannot be approved here.");
        if (Match(command, @"\b(curl|wget|invoke-webrequest|fetch)\b"))
            return new(PermissionRisk.High, "Network", [DeveloperCapabilityIds.RunCommand, DeveloperCapabilityIds.NetworkAccess], true, "Network commands require native review.");
        if (Match(command, @"\b(install|add|restore)\b"))
            return new(PermissionRisk.High, "Package installation", [DeveloperCapabilityIds.RunCommand, DeveloperCapabilityIds.InstallPackage, DeveloperCapabilityIds.NetworkAccess], true, "Installation may execute downloaded code; use native review.");
        var category = Match(command, @"^(dotnet build|npm run build|pnpm build|yarn build|gradle build)(\s+[A-Za-z0-9_./:\\=-]+)*$") ? "Build" :
            Match(command, @"^(dotnet test|npm test|pnpm test|pytest|gradle test)(\s+[A-Za-z0-9_./:\\=-]+)*$") ? "Tests" : "Unknown command";
        // Project scripts and test runners execute arbitrary repository code. They are never low risk.
        var simple = new[] { "dotnet build", "npm run build", "pnpm build", "yarn build", "gradle build", "dotnet test", "npm test", "pnpm test", "pytest", "gradle test" }.Contains(command, StringComparer.OrdinalIgnoreCase);
        return new(PermissionRisk.High, category, category == "Unknown command" ? [DeveloperCapabilityIds.RunCommand] :
            [DeveloperCapabilityIds.RunCommand, category == "Build" ? DeveloperCapabilityIds.RunBuild : DeveloperCapabilityIds.RunTests, DeveloperCapabilityIds.NetworkAccess],
            !simple, !simple ? "Unknown commands or arguments require native review; no outside-project path can be approved here." : "Executes project scripts/code and may restore packages or access the network. Explicit approval required.");
    }
}
