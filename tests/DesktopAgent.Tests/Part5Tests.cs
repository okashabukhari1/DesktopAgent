using System.IO.Pipes;
using System.Text;
using System.Text.Json;
using DesktopAgent.Core.Ai;
using DesktopAgent.Core.Developer;
using DesktopAgent.Core.Enums;
using DesktopAgent.Core.Permissions;
using DesktopAgent.Core.Projects;
using DesktopAgent.Core.Services;
using DesktopAgent.Core.Automation.Services;
using DesktopAgent.Infrastructure.Developer;
using DesktopAgent.Infrastructure.Projects;
using DesktopAgent.Infrastructure.Settings;
using Microsoft.Extensions.Logging.Abstractions;

internal static class Part5Tests
{
    public static (string Name, Func<Task> Run)[] Cases => [
        ("developer normalization rejects malformed duplicate unknown huge data", Invalid),
        ("developer paths and secrets fail closed", Paths),
        ("developer risk classifier never labels scripts low risk", Risks),
        ("developer multiple sessions project association and fake-session rejection", Sessions),
        ("developer file activity requires correlated real tool result", Files),
        ("developer build tests require exit result not successful tool envelope", Results),
        ("developer explicit permission allow once deny and stale decision", Permissions),
        ("developer policy denial and dangerous commands fall back to native", Policy),
        ("developer cancelled permission clears card without killing external process", Cancel),
        ("developer event stress bounds and duplicate suppression", Stress),
        ("developer persistence restart marks active disconnected", Recovery),
        ("developer all eight tools validate and ambiguity stays explicit", Tools),
        ("developer character priority preserves own assistant state", Character),
        ("developer hook install backup preserve idempotent remove", Installer),
        ("developer hook concurrent config changes cannot apply stale preview", InstallerRace),
        ("developer pipe framing rejects huge negative truncated frames", Frames)
        ,("developer real current-user named pipe roundtrip and disconnect", Pipe)
        ,("developer OpenCode v1 maps only supported bounded session events", OpenCode)
        ,("developer retrieval injection cannot cause an unrelated computer action", Injection)
    ];
    private static void Check(bool condition, string message) { if (!condition) throw new Exception(message); }
    private sealed class Fixture : IDisposable
    {
        public string Root = Path.Combine(AppContext.BaseDirectory, "DeveloperTest-" + Guid.NewGuid().ToString("N"));
        public JsonSettingsService Settings;
        public ProjectCatalog Projects;
        public MemoryPolicy Policy = new();
        public ActionHistoryService History = new();
        public CharacterService Character = new();
        public SecretProtection Secrets = new();
        public DeveloperSupervisor Supervisor;
        public DeveloperEventNormalizer Normalizer;
        public ProjectProfile Project = null!;
        public Fixture()
        {
            Directory.CreateDirectory(Root); Settings = new(NullLogger<JsonSettingsService>.Instance, Path.Combine(Root, "cache"));
            Settings.Current.Developer.Enabled = true;
            Projects = new(new ProjectDiscoveryService(), Settings, History);
            Normalizer = new(Projects, Secrets); Supervisor = NewSupervisor();
        }
        public DeveloperSupervisor NewSupervisor() => new(Normalizer, Settings, Policy, Projects, Character, History, Secrets, NullLogger<DeveloperSupervisor>.Instance, Path.Combine(Root, "cache"));
        public async Task Init() { var path = Path.Combine(Root, "PowerLens"); Directory.CreateDirectory(path); Project = await Projects.AddAsync(path); await Supervisor.StartAsync(); }
        public byte[] Payload(string hook, string session = "session-one", string? tool = null, string? call = null, string? command = null, string? file = null, int? exit = null, string? cwd = null, string? responseType = null) =>
            JsonSerializer.SerializeToUtf8Bytes(new { hook_event_name = hook, session_id = session, cwd = cwd ?? Project.Path, tool_name = tool,
                tool_use_id = call, tool_input = new { command, file_path = file }, tool_response = new { exit_code = exit, type = responseType } });
        public Task<DeveloperBridgeReply> Send(string hook, string session = "session-one", string? tool = null, string? call = null, string? command = null, string? file = null, int? exit = null, string? cwd = null, string? responseType = null, CancellationToken ct = default) =>
            Supervisor.ReceiveEventAsync("claude-code", Payload(hook, session, tool, call, command, file, exit, cwd, responseType), ct);
        public void Dispose()
        {
            Supervisor.StopAsync().GetAwaiter().GetResult();
            var target = Path.GetFullPath(Root); var prefix = Path.GetFullPath(AppContext.BaseDirectory).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
            if (!target.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) || !Path.GetFileName(target).StartsWith("DeveloperTest-")) throw new Exception("Unsafe fixture cleanup");
            Directory.Delete(target, true);
        }
    }
    private static async Task Wait(Func<bool> condition) { for (var i = 0; i < 100 && !condition(); i++) await Task.Delay(20); Check(condition(), "Timed out waiting for developer state"); }
    private static async Task Invalid()
    {
        using var f = new Fixture(); await f.Init();
        foreach (var text in new[] { "{", "{}", "[]", "{\"hook_event_name\":\"SessionStart\",\"hook_event_name\":\"Stop\"}" }) Check(f.Normalizer.Normalize("claude-code", Encoding.UTF8.GetBytes(text)) is null, "Invalid JSON accepted");
        Check(f.Normalizer.Normalize("claude-code", new byte[DeveloperEventNormalizer.MaxPayloadBytes + 1]) is null, "Huge accepted");
        Check(f.Normalizer.Normalize("claude-code", f.Payload("FakeEvent")) is null, "Unknown accepted");
        Check(f.Normalizer.Normalize("fake-tool", f.Payload("SessionStart")) is null, "Unknown source accepted");
    }
    private static async Task Paths()
    {
        using var f = new Fixture(); await f.Init();
        foreach (var path in new[] { @"C:\other\file.cs", f.Project.Path + @"\..\other.cs", f.Project.Path + @"\file.cs:secret", @"\\server\share\f.cs", f.Project.Path + @"\.env" })
            Check(f.Normalizer.Normalize("claude-code", f.Payload("PreToolUse", tool: "Read", call: "r", file: path)) is null, "Bad file path accepted");
        var e = f.Normalizer.Normalize("claude-code", f.Payload("PreToolUse", tool: "Bash", call: "c", command: "dotnet test --password abc"));
        Check(e is not null && !e.Value.Event.CommandSummary!.Contains("abc") && e.Value.Risk.Blocked, "Secret command leaked or approvable");
        Check(f.Normalizer.Normalize("claude-code", f.Payload("SessionStart", session: "bad session")) is null, "Invalid session accepted");
    }
    private static Task Risks()
    {
        foreach (var command in new[] { "rm -rf src", "Remove-Item -Recurse .", "git reset --hard", "curl example.com", "npm install", "dotnet test; taskkill", "unknown-command", "dotnet test $(x)" })
            Check(DeveloperRiskClassifier.Classify("Bash", command).Blocked, "Unsafe command allowed: " + command);
        foreach (var command in new[] { "dotnet build", "npm run build", "dotnet test", "pytest" })
            Check(DeveloperRiskClassifier.Classify("Bash", command).Risk == PermissionRisk.High, "Executable script mislabeled low risk");
        return Task.CompletedTask;
    }
    private static async Task Sessions()
    {
        using var f = new Fixture(); await f.Init();
        Check(!(await f.Send("UserPromptSubmit", "fake")).Accepted, "Fake unknown session accepted");
        var otherPath = Path.Combine(f.Root, "OtherProject"); Directory.CreateDirectory(otherPath); var other = await f.Projects.AddAsync(otherPath);
        await f.Send("SessionStart"); await f.Send("SessionStart", "two", cwd: other.Path);
        Check(f.Supervisor.Sessions.Count == 2 && f.Supervisor.Sessions.Select(s => s.ProjectId).Distinct().Count() == 2, "Session mapping failed");
        Check(!(await f.Send("UserPromptSubmit", cwd: Path.Combine(f.Root, "Elsewhere"))).Accepted, "Session rebound to another project");
        await f.Send("Stop"); Check(f.Supervisor.Sessions.First(s => s.ProjectId == f.Project.Id).State == DeveloperSessionState.WaitingForUser, "Stop falsely completed session");
    }
    private static async Task Files()
    {
        using var f = new Fixture(); await f.Init(); await f.Send("SessionStart");
        var file = Path.Combine(f.Project.Path, "Service.cs");
        Check(!(await f.Send("PostToolUse", tool: "Write", call: "fake", file: file)).Accepted, "Unrequested edit accepted");
        await f.Send("PreToolUse", tool: "Read", call: "read", file: file); await f.Send("PostToolUse", tool: "Read", call: "read", file: file);
        await f.Send("PreToolUse", tool: "Write", call: "write", file: file); await f.Send("PostToolUse", tool: "Write", call: "write", file: file, responseType: "create");
        var s = f.Supervisor.Sessions.Single(); Check(s.FilesRead.SequenceEqual(new[] { "Service.cs" }) && s.FilesCreated.SequenceEqual(new[] { "Service.cs" }), "File evidence absent");
    }
    private static async Task Results()
    {
        using var f = new Fixture(); await f.Init(); await f.Send("SessionStart");
        await f.Send("PreToolUse", tool: "Bash", call: "build", command: "dotnet build");
        Check(f.Supervisor.Sessions.Single().Commands.Single().Status == DeveloperOperationStatus.Requested, "Pre falsely running/success");
        await f.Send("PostToolUse", tool: "Bash", call: "build", command: "dotnet build");
        Check(f.Supervisor.Sessions.Single().Commands.Single().Status == DeveloperOperationStatus.Unknown, "Missing exit falsely passed");
        await f.Send("PreToolUse", tool: "Bash", call: "test", command: "dotnet test");
        await f.Send("PostToolUse", tool: "Bash", call: "test", command: "dotnet test", exit: 1);
        Check(f.Supervisor.Sessions.Single().Commands.Last().Status == DeveloperOperationStatus.Failed, "Exit 1 not failed");
        await f.Send("PreToolUse", tool: "Bash", call: "test-pass", command: "dotnet test");
        await f.Send("PostToolUse", tool: "Bash", call: "test-pass", command: "dotnet test", exit: 0);
        Check(f.Supervisor.Sessions.Single().Commands.Last().Status == DeveloperOperationStatus.Succeeded, "Actual exit 0 not passed");
    }
    private static async Task Permissions()
    {
        using var f = new Fixture(); await f.Init(); await f.Send("SessionStart");
        foreach (var allow in new[] { true, false })
        {
            var task = f.Send("PermissionRequest", tool: "Bash", command: "dotnet test");
            await Wait(() => f.Supervisor.PendingPermissions.Count > 0); var id = f.Supervisor.PendingPermissions.Single().Id;
            Check(f.Character.State == CharacterState.Attention, "Pending not Attention");
            Check(f.Supervisor.Decide(id, allow), "Decision rejected"); Check((await task).Decision == (allow ? "allow" : "deny"), "Wrong bridge response");
            Check(!f.Supervisor.Decide(id, true), "Reused permission accepted");
        }
    }
    private static async Task Policy()
    {
        using var f = new Fixture(); await f.Init(); await f.Send("SessionStart");
        await f.Policy.SetPolicyAsync(DeveloperCapabilityIds.RunTests, PermissionPolicy.Deny);
        var denied = await f.Send("PermissionRequest", tool: "Bash", command: "dotnet test");
        Check(denied.Decision is null && f.Supervisor.PendingPermissions.Count == 0, "Denied policy escalated");
        var danger = await f.Send("PermissionRequest", tool: "Bash", command: "rm -rf src"); Check(danger.Decision is null, "Danger automatically approved");
        await f.Policy.SetPolicyAsync(DeveloperCapabilityIds.RunTests, PermissionPolicy.Ask);
        var pending = f.Send("PermissionRequest", tool: "Bash", command: "dotnet test"); await Wait(() => f.Supervisor.PendingPermissions.Count > 0);
        var id = f.Supervisor.PendingPermissions.Single().Id; await f.Policy.SetPolicyAsync(DeveloperCapabilityIds.RunTests, PermissionPolicy.Deny);
        Check(!f.Supervisor.Decide(id, true) && (await pending).Decision is null, "Policy change raced approval");
    }
    private static async Task Cancel()
    {
        using var f = new Fixture(); await f.Init(); await f.Send("SessionStart"); using var ct = new CancellationTokenSource();
        var task = f.Send("PermissionRequest", tool: "Bash", command: "dotnet build", ct: ct.Token); await Wait(() => f.Supervisor.PendingPermissions.Count > 0);
        var id = f.Supervisor.PendingPermissions.Single().Id; ct.Cancel(); await task;
        Check(f.Supervisor.PendingPermissions.Count == 0 && !f.Supervisor.Decide(id, true), "Cancelled request remained active");
    }
    private static async Task Stress()
    {
        using var f = new Fixture(); await f.Init(); await f.Send("SessionStart");
        var tasks = Enumerable.Range(0, 2000).Select(_ => f.Send("UserPromptSubmit")).ToArray(); await Task.WhenAll(tasks);
        Check(f.Supervisor.Sessions.Single().Events.Count <= 128 && tasks.Count(t => !t.Result.Accepted) > 0, "Flood not bounded");
    }
    private static async Task Recovery()
    {
        using var f = new Fixture(); await f.Init(); await f.Send("SessionStart"); await f.Send("UserPromptSubmit");
        await Wait(() => File.Exists(Path.Combine(f.Root, "cache", "developer-events.json")));
        using var restart = f.NewSupervisor(); await restart.StartAsync();
        Check(restart.Sessions.Single().State == DeveloperSessionState.Disconnected && restart.PendingPermissions.Count == 0, "Recovery fabricated success/approval");
    }
    private static async Task Tools()
    {
        using var f = new Fixture(); await f.Init(); await f.Send("SessionStart"); await f.Send("SessionStart", "two");
        var tools = DeveloperTools.Create(f.Supervisor).ToArray(); Check(tools.Length == 8, "Missing typed tools");
        foreach (var tool in tools)
        {
            var args = JsonSerializer.SerializeToElement(new { session_id = (string?)null }); tool.Validate(args);
            var result = await tool.ExecuteAsync(args); if (tool.Definition.Name == "get_test_status") Check(!result.Success, "Ambiguous guessed result");
            try { tool.Validate(JsonSerializer.SerializeToElement(new { session_id = (string?)null, approve = true })); throw new Exception("Unknown arg accepted"); } catch (ArgumentException) { }
        }
    }
    private static Task Character()
    {
        var c = new CharacterService(); c.SetDeveloperState(false, true); Check(c.State == CharacterState.Working, "Developer not working");
        c.SetState(CharacterState.Thinking); Check(c.State == CharacterState.Thinking, "Dev overwrote assistant");
        c.SetDeveloperState(true, true); Check(c.State == CharacterState.Attention, "Permission lacks priority");
        c.SetState(CharacterState.Idle); Check(c.State == CharacterState.Attention, "Assistant idle cleared permission");
        c.SetDeveloperState(false, true); Check(c.State == CharacterState.Working, "Work not restored"); c.SetDeveloperState(false, false); Check(c.State == CharacterState.Idle, "Idle not restored"); return Task.CompletedTask;
    }
    private static async Task Installer()
    {
        using var f = new Fixture(); var config = Path.Combine(f.Root, "settings.json"); var relay = Path.Combine(f.Root, "agent-dev-hook.exe"); File.WriteAllBytes(relay, []);
        var original = "{\"theme\":\"dark\",\"hooks\":{\"Stop\":[{\"hooks\":[{\"type\":\"command\",\"command\":\"user-hook\"}]}]}}";
        File.WriteAllText(config, original); var installer = new ClaudeHookInstaller(config, relay);
        var preview = await installer.PreviewAsync(false); Check(File.ReadAllText(config) == original, "Preview mutated config");
        string? backup;
        try { backup = await installer.ApplyConfirmedAsync(preview.Id); }
        catch (Exception ex) { throw new Exception("Install apply: " + ex); }
        Check(backup is not null && File.ReadAllText(backup) == original, "Backup missing or altered");
        Check(await installer.IsConfiguredAsync(), "Installed config not detected");
        var repair = await installer.PreviewAsync(false); Check(!repair.Changed, "Repair not idempotent");
        var remove = await installer.PreviewAsync(true); await installer.ApplyConfirmedAsync(remove.Id);
        Check(System.Text.Json.Nodes.JsonNode.DeepEquals(System.Text.Json.Nodes.JsonNode.Parse(original), System.Text.Json.Nodes.JsonNode.Parse(File.ReadAllText(config))), "User settings/hooks changed on removal");
    }
    private static async Task InstallerRace()
    {
        using var f = new Fixture(); var config = Path.Combine(f.Root, "settings.json"); var relay = Path.Combine(f.Root, "agent-dev-hook.exe"); File.WriteAllBytes(relay, []); File.WriteAllText(config, "{}");
        var installer = new ClaudeHookInstaller(config, relay); var p = await installer.PreviewAsync(false); File.WriteAllText(config, "{\"theme\":\"changed\"}");
        try { await installer.ApplyConfirmedAsync(p.Id); throw new Exception("Stale preview applied"); } catch (InvalidOperationException) { }
        Check(File.ReadAllText(config).Contains("changed"), "Concurrent edit clobbered");
    }
    private static async Task Frames()
    {
        foreach (var size in new[] { -1, 0, DeveloperPipeProtocol.MaxBytes + 1 })
        {
            using var stream = new MemoryStream(BitConverter.GetBytes(size));
            try { await DeveloperPipeProtocol.ReadAsync(stream, DeveloperPipeProtocol.MaxBytes, default); throw new Exception("Invalid frame accepted"); } catch (InvalidDataException) { }
        }
        using var valid = new MemoryStream(); await DeveloperPipeProtocol.WriteAsync(valid, "{}"u8.ToArray(), default); valid.Position = 0;
        Check(Encoding.UTF8.GetString(await DeveloperPipeProtocol.ReadAsync(valid, 1024, default)) == "{}", "Frame did not roundtrip");
    }
    private static async Task Pipe()
    {
        using var f = new Fixture(); await f.Init();
        using var server = new DeveloperPipeServer(f.Supervisor, NullLogger<DeveloperPipeServer>.Instance); server.Start();
        using var ct = new CancellationTokenSource(TimeSpan.FromSeconds(3));
        using var client = new NamedPipeClientStream(".", DeveloperPipeProtocol.PipeName, PipeDirection.InOut, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
        await client.ConnectAsync(1000, ct.Token);
        using var payload = JsonDocument.Parse(f.Payload("SessionStart"));
        await DeveloperPipeProtocol.WriteAsync(client, JsonSerializer.SerializeToUtf8Bytes(new { source = "claude-code", payload = payload.RootElement }), ct.Token);
        using var reply = JsonDocument.Parse(await DeveloperPipeProtocol.ReadAsync(client, 1024, ct.Token));
        Check(reply.RootElement.GetProperty("Accepted").GetBoolean() && f.Supervisor.Sessions.Count == 1, "Named pipe failed actual roundtrip");
    }
    private static async Task OpenCode()
    {
        using var f = new Fixture(); await f.Init();
        byte[] Payload(string type, string id = "open-one") => JsonSerializer.SerializeToUtf8Bytes(new { event_type = type, session_id = id, cwd = f.Project.Path });
        Check((await f.Supervisor.ReceiveEventAsync("opencode", Payload("session.created"))).Accepted, "OpenCode session start missing");
        await f.Supervisor.ReceiveEventAsync("opencode", Payload("session.busy"));
        Check(f.Supervisor.Sessions.Single().SourceTool == "opencode" && f.Supervisor.Sessions.Single().State == DeveloperSessionState.Working, "OpenCode active state missing");
        await f.Supervisor.ReceiveEventAsync("opencode", Payload("session.idle"));
        Check(f.Supervisor.Sessions.Single().State == DeveloperSessionState.WaitingForUser, "Idle falsely successful task");
        Check(!(await f.Supervisor.ReceiveEventAsync("opencode", Payload("permission.asked"))).Accepted, "Unsupported OpenCode approval accepted");
        var plugin = new OpenCodeAdapter(f.Supervisor, Path.Combine(f.Root, "plugins", "desktop-agent-events.js"), Path.Combine(f.Root, "agent-dev-hook.exe"));
        File.WriteAllBytes(plugin.RelayPath, []); await plugin.InstallConfirmedAsync();
        Check((await plugin.GetStatusAsync(default)).Configured, "Plugin config not detected");
        await plugin.RemoveConfirmedAsync(); Check(!File.Exists(plugin.PluginPath), "Own plugin not removed");
    }
    private static async Task Injection()
    {
        using var f = new Fixture(); await f.Init(); await f.Send("SessionStart");
        var fakeController = new RecordingController(); var computerTools = DesktopAgent.Infrastructure.Ai.WindowsAgentTools.Create(fakeController, null!, null!, null!);
        var registry = new DesktopAgent.Infrastructure.Ai.AgentToolRegistry(DeveloperTools.Create(f.Supervisor).Concat(computerTools));
        var router = new DesktopAgent.Infrastructure.Ai.ComputerAgentToolRouter(registry, f.Policy, new FakeAgentActionApprovalService(PermissionApproval.AllowOnce), f.History, f.Character,
            NullLogger<DesktopAgent.Infrastructure.Ai.ComputerAgentToolRouter>.Instance);
        var provider = new ScriptedProvider(new AiProviderResponse("", [new("query", "get_developer_sessions", "{\"session_id\":null}")]),
            new AiProviderResponse("", [new("injected", "open_application", "{\"application\":\"powershell\"}")]));
        var assistant = new DesktopAgent.Infrastructure.Ai.AssistantConversationService(provider, router, new ComputerCommandParser(), new FakeComputerCommandService(), f.History, f.Settings,
            NullLogger<DesktopAgent.Infrastructure.Ai.AssistantConversationService>.Instance);
        var answer = await assistant.SendAsync("What is Claude doing?");
        Check(answer.Text.Contains("Stopped") && fakeController.Commands.Count == 0, "Developer retrieval allowed action");
        Check(assistant.Context.LastToolResult?.Data is null, "Raw developer details retained in reference context");
    }
}
