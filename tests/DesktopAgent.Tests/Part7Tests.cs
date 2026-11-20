using System.Text.Json;
using DesktopAgent.Core.ComputerUse;
using DesktopAgent.Core.Interfaces;
using DesktopAgent.Core.Models;
using DesktopAgent.Core.Permissions;
using DesktopAgent.Core.UiAwareness;
using DesktopAgent.Infrastructure.ComputerUse;
using DesktopAgent.Core.Ai;
using DesktopAgent.Core.Automation.Services;
using DesktopAgent.Core.Services;
using DesktopAgent.Infrastructure.Ai;
using Microsoft.Extensions.Logging.Abstractions;

internal static class Part7Tests
{
    public static (string Name, Func<Task> Run)[] Cases => [
        ("CU disabled default prevents provider calls", Disabled),
        ("CU Observe prevents actions", Observe),
        ("CU missing emergency STOP prevents actions", NoStop),
        ("CU missing input monitor prevents actions", NoMonitor),
        ("CU hidden assistant prevents actions", Hidden),
        ("CU no user scope prevents actions", NoScope),
        ("CU policy deny prevents observation and mutation", Denied),
        ("CU low-risk focus acts without prompting", Focus),
        ("CU consequential action requires single confirmation", Consequence),
        ("CU denied confirmation prevents mutation", DenyApproval),
        ("CU protected unknown password status prevents mutation", UnknownPassword),
        ("CU terminal actions remain blocked", Terminal),
        ("CU trusted mode never auto-approves high risk", TrustedHigh),
        ("CU STOP during approval cancels queued mutation", StopApproval),
        ("CU user takeover cancels task scope", Takeover),
        ("CU action budget is bounded", Budget),
        ("CU unverified input stops further actions", Unverified),
        ("CU stale target is not automatically retried", Stale),
        ("CU schemas reject raw coordinates and malformed references", Schemas),
        ("CU settings copy and sanitization preserve safe defaults", SafeSettings)
        ,("CU natural language variations enable only computer requests", NaturalLanguage)
        ,("CU explicitly requested app excludes other windows", AppScope)
        ,("CU text not supplied by the user is refused", TextScope)
        ,("CU keyboard save and unknown confirmation are consequential", ConsequentialKeys)
        ,("CU successful action resets consecutive failure budget", FailureReset)
        ,("CU assistant invokes typed action and refuses invented completion", ConversationAction)
        ,("CU user interruption cancels an in-flight AI planning call", ConversationTakeover)
        ,("CU policy revoked during approval prevents execution", RevokePolicy)
        ,("CU incomplete planner cannot end before attempting requested UI", IncompletePlan)
        ,("CU malformed observation gets only one schema repair", ObservationRepair)
        ,("CU malformed mutation never gets schema retry", MutationNoRepair)
          ,("CU editor rejection permits one repair only before input", EditorRepair)
          ,("CU partial typing cannot complete requested shortcuts", PartialSequence)
    ];
    private static void Check(bool condition, string message) { if (!condition) throw new Exception(message); }
    private static ComputerAction Request(ComputerActionType type = ComputerActionType.Focus) => new(Guid.NewGuid(), type, "window:Notepad", Guid.NewGuid(), "ui:2");
    private sealed class Fixture
    {
        public Settings Settings = new(); public Backend Backend = new(); public Policy Policy = new(); public Approval Approval = new();
        public ComputerUseSession Session; public ComputerActionExecutor Executor;
        public Fixture(bool begin = true)
        {
            Session = new(Settings) { EmergencyStopReady = true, InterruptionMonitorReady = true, IndicatorVisible = true }; Session.ViewVisible(true);
            if (begin) Session.Begin(CancellationToken.None, "Click the fixture in Notepad");
            Executor = new(Backend, Session, Settings, Policy, Approval);
        }
        public Task<ComputerInteractionResult> Run(ComputerActionType type = ComputerActionType.Focus) => Executor.ExecuteAsync(Request(type));
    }
    private sealed class Settings : ISettingsService
    {
        public AppSettings Current { get; private set; } = new() { ComputerUse = new() { Enabled = true, OnboardingAcknowledged = true } };
        public string SettingsPath => "fixture";
        public Task<AppSettings> LoadAsync(CancellationToken ct = default) => Task.FromResult(Current);
        public Task SaveAsync(AppSettings settings, CancellationToken ct = default) { Current = settings; return Task.CompletedTask; }
    }
    private sealed class Backend : IComputerInteractionBackend
    {
        public int Reads, Writes; public bool Verified = true; public bool Stale;
        public PreparedComputerTarget Target = new(new(123, 3, "notepad", null, "Fixture", new(0, 0, 300, 200), true, true, false, false, true, null, DateTimeOffset.UtcNow),
            new() { Id = "ui:2", ControlType = "Button", Name = "Fixture", IsPassword = false, IsVisible = true, IsEnabled = true }, "123:3:1", [1, 2]);
        public Task<PreparedComputerTarget> PrepareAsync(ComputerAction action, CancellationToken ct) { Reads++; if (Stale) throw new UiObservationException("StaleElement"); return Task.FromResult(Target); }
        public Task<ComputerInteractionResult> ExecuteAsync(ComputerAction action, PreparedComputerTarget target, Action guard, CancellationToken ct)
        { ct.ThrowIfCancellationRequested(); guard(); Writes++; return Task.FromResult(new ComputerInteractionResult(action.Id, Verified, Verified ? "Verified" : "Unverified", "Fixture result", Verified: Verified, InputMayHaveOccurred: true)); }
    }
    private sealed class Policy : IPolicyService
    {
        public PermissionPolicy Value = PermissionPolicy.Allow;
        public IReadOnlyList<AgentCapability> Capabilities => [ComputerUseCapabilities.Capability];
        public PermissionPolicy GetPolicy(string id) => Value;
        public PolicyEvaluation Evaluate(string id) => new(id, Value == PermissionPolicy.Deny ? PolicyOutcome.Denied : Value == PermissionPolicy.Ask ? PolicyOutcome.ConfirmationRequired : PolicyOutcome.Allowed, Value, "Fixture");
        public Task SetPolicyAsync(string id, PermissionPolicy value, CancellationToken ct = default) { Value = value; return Task.CompletedTask; }
        public Task ResetAsync(CancellationToken ct = default) => Task.CompletedTask;
    }
    private sealed class Approval : IAgentActionApprovalService
    {
        public int Calls; public PermissionApproval Decision = PermissionApproval.AllowOnce; public Action? During;
        public Task<PermissionApproval> RequestAsync(AgentCapability capability, string target, CancellationToken ct = default) { Calls++; During?.Invoke(); ct.ThrowIfCancellationRequested(); return Task.FromResult(Decision); }
    }
    private static async Task Disabled() { var f = new Fixture(); f.Settings.Current.ComputerUse.Enabled = false; Check(!(await f.Run()).Success && f.Backend.Reads == 0, "Disabled read or input"); }
    private static async Task Observe() { var f = new Fixture(); f.Settings.Current.ComputerUse.Mode = ComputerUseMode.Observe; Check(!(await f.Run()).Success && f.Backend.Writes == 0, "Observe acted"); }
    private static async Task NoStop() { var f = new Fixture(); f.Session.EmergencyStopReady = false; Check(!(await f.Run()).Success && f.Backend.Reads == 0, "Unregistered STOP acted"); }
    private static async Task NoMonitor() { var f = new Fixture(); f.Session.InterruptionMonitorReady = false; Check(!(await f.Run()).Success && f.Backend.Reads == 0, "No input monitor acted"); }
    private static async Task Hidden() { var f = new Fixture(); f.Session.ViewVisible(false); Check(!(await f.Run()).Success && f.Backend.Writes == 0, "Hidden automation"); }
    private static async Task NoScope() { var f = new Fixture(false); Check(!(await f.Run()).Success && f.Backend.Writes == 0, "Unrequested action"); }
    private static async Task Denied() { var f = new Fixture(); f.Policy.Value = PermissionPolicy.Deny; Check(!(await f.Run()).Success && f.Backend.Reads == 0, "Deny acted"); }
    private static async Task Focus() { var f = new Fixture(); var r = await f.Run(); Check(r.Verified && r.Success && f.Backend.Writes == 1 && f.Approval.Calls == 0, "Low risk failed"); }
    private static async Task Consequence() { var f = new Fixture(); f.Backend.Target = f.Backend.Target with { Element = f.Backend.Target.Element with { Name = "Send" } }; Check((await f.Run(ComputerActionType.Invoke)).Success && f.Approval.Calls == 1, "Send did not ask"); }
    private static async Task DenyApproval() { var f = new Fixture(); f.Approval.Decision = PermissionApproval.Deny; Check(!(await f.Run(ComputerActionType.Invoke)).Success && f.Backend.Writes == 0, "Denied mutation"); }
    private static async Task UnknownPassword() { var f = new Fixture(); f.Backend.Target = f.Backend.Target with { Element = f.Backend.Target.Element with { IsPassword = null } }; Check(!(await f.Run()).Success && f.Backend.Writes == 0, "Unknown password accepted"); }
    private static async Task Terminal() { var f = new Fixture(); f.Backend.Target = f.Backend.Target with { Window = f.Backend.Target.Window with { ProcessName = "powershell" } }; Check(!(await f.Run()).Success && f.Backend.Writes == 0, "Terminal accepted"); }
    private static async Task TrustedHigh() { var f = new Fixture(); f.Settings.Current.ComputerUse.Mode = ComputerUseMode.TrustedActions; f.Settings.Current.ComputerUse.TrustedApplications.Add("notepad"); f.Backend.Target = f.Backend.Target with { Element = f.Backend.Target.Element with { Name = "Delete" } }; await f.Run(ComputerActionType.Invoke); Check(f.Approval.Calls == 1, "Trusted destructive auto approval"); }
    private static async Task StopApproval() { var f = new Fixture(); f.Approval.During = f.Session.Stop; Check((await f.Run(ComputerActionType.Invoke)).Status == "Cancelled" && f.Backend.Writes == 0, "STOP raced mutation"); }
    private static async Task Takeover() { var f = new Fixture(); f.Session.UserInput(); Check((await f.Run()).Status == "Cancelled" && f.Backend.Writes == 0 && f.Session.State == ComputerTaskState.Paused, "Takeover failed"); }
    private static async Task Budget() { var f = new Fixture(); f.Settings.Current.ComputerUse.MaximumActions = 1; await f.Run(); Check(!(await f.Run()).Success && f.Backend.Writes == 1, "Unbounded actions"); }
    private static async Task Unverified() { var f = new Fixture(); f.Backend.Verified = false; var r = await f.Run(); Check(!r.Success && !r.Verified && r.InputMayHaveOccurred && !f.Session.IsActive, "Unverified success"); await f.Run(); Check(f.Backend.Writes == 1, "Unverified input retried"); }
    private static async Task Stale() { var f = new Fixture(); f.Backend.Stale = true; Check((await f.Run()).ErrorCode == "StaleElement" && f.Backend.Writes == 0 && f.Backend.Reads == 1, "Stale retry"); }
    private static Task Schemas()
    {
        var tools = ComputerActionTools.Create(new Fixture().Executor).ToArray(); Check(tools.Length == 13 && tools.All(x => x.Definition.Parameters.GetProperty("additionalProperties").GetBoolean() == false), "Tool schemas");
        foreach (var t in tools) { using var doc = JsonDocument.Parse("{\"x\":20,\"y\":50}"); bool rejected = false; try { t.Validate(doc.RootElement); } catch (ArgumentException) { rejected = true; } Check(rejected, "Raw coordinates accepted"); }
        foreach (var t in tools)
        {
            var name = t.Definition.Name;
            var text = name is "type_text" or "set_ui_value" ? "hello" : null;
            var key = name is "press_key" or "press_shortcut" ? "Ctrl+A" : null;
            var amount = name is "scroll_ui" or "scroll_window" ? 1 : 0;
            var valid = JsonSerializer.SerializeToElement(new { window = "window:Notepad", snapshot = Guid.NewGuid().ToString("D"), element = "ui:2", text, key, amount });
            t.Validate(valid);
            Check(t.Definition.Parameters.GetProperty("properties").GetProperty("snapshot").TryGetProperty("pattern", out _), "Snapshot rule hidden from provider");
            foreach (var field in new[] { "window", "snapshot", "element", "amount" })
            {
                var malformed = System.Text.Json.Nodes.JsonNode.Parse(valid.GetRawText())!;
                malformed[field] = field == "amount" ? System.Text.Json.Nodes.JsonValue.Create(200) : System.Text.Json.Nodes.JsonValue.Create("invalid");
                try { t.Validate(JsonSerializer.SerializeToElement(malformed)); throw new Exception("Invalid " + field + " accepted"); }
                catch (ArgumentException e) { Check(e.Message.StartsWith("Action.", StringComparison.Ordinal), "Missing safe action failure category"); }
            }
        }
        foreach (var name in new[] { "press_shortcut", "press_key", "scroll_window" })
        {
            var tool = tools.Single(t => t.Definition.Name == name);
            tool.Validate(JsonSerializer.SerializeToElement(new { window = "window:Notepad", snapshot = (string?)null,
                element = (string?)null, text = (string?)null, key = name == "scroll_window" ? null : "Ctrl+A", amount = name == "scroll_window" ? 1 : 0 }));
        }
        return Task.CompletedTask;
    }
    private static Task SafeSettings()
    {
        var s = new ComputerUseSettings { Enabled = true, MaximumActions = 999, MaximumFailures = 99, MaximumTaskSeconds = 999 }; s.Sanitize(); Check(!s.Enabled && s.MaximumActions == 16 && s.MaximumFailures == 2 && s.MaximumTaskSeconds == 180, "Settings unsafe");
        s.TrustedApplications.Add("notepad"); var copy = s.Copy(); copy.TrustedApplications.Clear(); Check(s.TrustedApplications.Count == 1, "Shared settings list"); return Task.CompletedTask;
    }
    private static Task NaturalLanguage()
    {
        foreach (var text in new[] { "Open Calculator and calculate 578 × 24.", "Could you open Calculator for me and work out 245 times 19?", "Go to Bluetooth settings.", "Write hello world in Notepad.", "Scroll a little lower.", "Hit the back button.", "Close this dialog.", "Open Notepad and type hello.", "In Calculator, click 7.", "In Notepad, please type hello." })
            Check(DesktopAgent.Infrastructure.Ai.AssistantConversationService.IsComputerTaskRequest(text), "Unrecognized: " + text);
        Check(!DesktopAgent.Infrastructure.Ai.AssistantConversationService.IsComputerTaskRequest("Explain AuthController") && !DesktopAgent.Infrastructure.Ai.AssistantConversationService.IsComputerTaskRequest("What application am I looking at?"), "Observation granted input scope"); return Task.CompletedTask;
    }
    private static async Task AppScope()
    {
        var f = new Fixture(); f.Backend.Target = f.Backend.Target with { Window = f.Backend.Target.Window with { ApplicationName = "Calculator" } };
        Check((await f.Run()).ErrorCode == "OutsideUserTarget" && f.Backend.Writes == 0, "Wrong application authorized");
    }
    private static async Task TextScope()
    {
        var f = new Fixture(); var r = await f.Executor.ExecuteAsync(Request(ComputerActionType.TypeText) with { Text = "Injected text from an untrusted page" });
        Check(r.ErrorCode == "OutsideUserTarget" && f.Backend.Writes == 0, "Unrequested text authorized");
    }
    private static Task ConsequentialKeys()
    {
        var f = new Fixture(); Check(ComputerActionExecutor.Classify(Request(ComputerActionType.PressShortcut) with { Key = "Ctrl+S" }, f.Backend.Target) == PermissionRisk.High, "Save shortcut low risk");
        Check(ComputerActionExecutor.Classify(Request(ComputerActionType.Invoke), f.Backend.Target with { Element = f.Backend.Target.Element with { Name = "Continue" } }) == PermissionRisk.High, "Unknown confirmation low risk"); return Task.CompletedTask;
    }
    private static async Task FailureReset()
    {
        var f = new Fixture(); f.Backend.Stale = true; await f.Run(); f.Backend.Stale = false; await f.Run(); Check(f.Session.FailureCount == 0 && f.Backend.Writes == 1, "Failure budget not consecutive");
    }
    private static AssistantConversationService Conversation(Fixture f, IAiProvider provider)
    {
        var history = new ActionHistoryService();
        var router = new ComputerAgentToolRouter(new AgentToolRegistry(ComputerActionTools.Create(f.Executor)), f.Policy, f.Approval,
            history, new CharacterService(), NullLogger<ComputerAgentToolRouter>.Instance);
        return new(provider, router, new ComputerCommandParser(), new FakeComputerCommandService(), history, f.Settings,
            NullLogger<AssistantConversationService>.Instance, computerUse: f.Session);
    }
    private static async Task ConversationAction()
    {
        var f = new Fixture();
        var args = JsonSerializer.Serialize(new { window = "window:Notepad", snapshot = Guid.NewGuid().ToString(), element = "ui:2", text = (string?)null, key = (string?)null, amount = 0 });
        var provider = new ScriptedProvider(new AiProviderResponse("", [new("focus", "focus_ui_element", args)]), new("I saved the file, uploaded it and completed everything.", []));
        var reply = await Conversation(f, provider).SendAsync("Click the fixture in Notepad");
        Check(f.Backend.Writes == 1 && reply.Text == "Fixture result" && !reply.Text.Contains("uploaded"), "LLM completion overrode verified results");
    }
    private static async Task ConversationTakeover()
    {
        var f = new Fixture(); var provider = new ScriptedProvider { Block = true }; var conversation = Conversation(f, provider);
        var pending = conversation.SendAsync("Click the fixture in Notepad"); await provider.Started.Task.WaitAsync(TimeSpan.FromSeconds(2)); f.Session.UserInput();
        Check((await pending.WaitAsync(TimeSpan.FromSeconds(2))).Cancelled && f.Backend.Writes == 0 && f.Session.State == ComputerTaskState.Paused, "Planner survived takeover");
    }
    private static async Task RevokePolicy()
    {
        var f = new Fixture(); f.Approval.During = () => f.Policy.Value = PermissionPolicy.Deny;
        Check(!(await f.Run(ComputerActionType.Invoke)).Success && f.Backend.Writes == 0, "Revoked permission acted");
    }
    private static async Task IncompletePlan()
    {
        var f = new Fixture();
        var args = JsonSerializer.Serialize(new { window = "window:Notepad", snapshot = Guid.NewGuid().ToString(), element = "ui:2", text = (string?)null, key = (string?)null, amount = 0 });
        var provider = new ScriptedProvider(new AiProviderResponse("I opened the application.", []), new("", [new("focus", "focus_ui_element", args)]), new("Everything completed.", []));
        var reply = await Conversation(f, provider).SendAsync("Click the fixture in Notepad");
        Check(f.Backend.Writes == 1 && reply.Text == "Fixture result", "Premature text-only plan ended the computer request");
    }
    private sealed class RejectingRouter(string tool) : IAgentToolRouter
    {
        public int Calls;
        public IReadOnlyList<AiToolDefinition> Definitions => [];
        public Task<ToolResult> InvokeAsync(AiToolCall call, CancellationToken cancellationToken = default)
        {
            Check(call.Name == tool, "Unexpected repair tool"); Calls++;
            return Task.FromResult(new ToolResult(false, "InvalidArguments", "Invalid fixture arguments"));
        }
    }
    private static async Task RejectionRepair(string tool, int expectedCalls)
    {
        var f = new Fixture(); var router = new RejectingRouter(tool);
        var response = new AiProviderResponse("", [new("read", tool, "{}")]);
        var provider = new ScriptedProvider(response, response, response);
        var conversation = new AssistantConversationService(provider, router, new ComputerCommandParser(), new FakeComputerCommandService(),
            new ActionHistoryService(), f.Settings, NullLogger<AssistantConversationService>.Instance, computerUse: f.Session);
        await conversation.SendAsync("Click the fixture in Notepad");
        Check(router.Calls == expectedCalls && provider.Requests.Count == expectedCalls && f.Backend.Writes == 0, "Unsafe or unbounded schema repair");
    }
    private static Task ObservationRepair() => RejectionRepair("inspect_window", 2);
    private static Task MutationNoRepair() => RejectionRepair("click_ui_element", 1);
    private sealed class EditorRejectingRouter(string status, bool dispatched) : IAgentToolRouter
    {
        public int Calls;
        public IReadOnlyList<AiToolDefinition> Definitions => [];
        public Task<ToolResult> InvokeAsync(AiToolCall call, CancellationToken cancellationToken = default)
        {
            Calls++;
            return Task.FromResult(new ToolResult(false, status, "Fixture rejection",
                new ComputerInteractionResult(Guid.NewGuid(), false, status, "Fixture rejection", InputMayHaveOccurred: dispatched)));
        }
    }
    private static async Task EditorRepair()
    {
        foreach (var (status, dispatched, expected) in new[] {
            ("PythonEditorRequired", false, 2), ("PythonEditorRequired", true, 1),
            ("PythonProtectedEditor", false, 1), ("PythonUnsupportedApplication", false, 1) })
        {
            var f = new Fixture(); var router = new EditorRejectingRouter(status, dispatched);
            var response = new AiProviderResponse("", [new("type", "type_text", "{}")]);
            var provider = new ScriptedProvider(response, response with { ToolCalls = [new("type-fresh", "type_text", "{\"element\":\"ui:3\"}")] }, response);
            var conversation = new AssistantConversationService(provider, router, new ComputerCommandParser(), new FakeComputerCommandService(),
                new ActionHistoryService(), f.Settings, NullLogger<AssistantConversationService>.Instance, computerUse: f.Session);
            await conversation.SendAsync("Open Notepad and type Hello World");
            Check(router.Calls == expected && f.Backend.Writes == 0, "Unsafe or unbounded editor repair: " + status + " dispatched=" + dispatched + " calls=" + router.Calls);
        }
    }
    private sealed class SuccessfulActionRouter : IAgentToolRouter
    {
        public int Calls;
        public IReadOnlyList<AiToolDefinition> Definitions => [];
        public Task<ToolResult> InvokeAsync(AiToolCall call, CancellationToken cancellationToken = default)
        {
            Calls++;
            return Task.FromResult(new ToolResult(true, "CompletedAndVerified", "Verified fixture action",
                new ComputerInteractionResult(Guid.NewGuid(), true, "CompletedAndVerified", "Verified fixture action", Verified: true, InputMayHaveOccurred: true)));
        }
    }
    private static async Task PartialSequence()
    {
        var f = new Fixture(); var router = new SuccessfulActionRouter();
        var provider = new ScriptedProvider(new AiProviderResponse("", [new("type", "type_text", "{}")]),
            new AiProviderResponse("All done", []), new AiProviderResponse("All done", []));
        var conversation = new AssistantConversationService(provider, router, new ComputerCommandParser(), new FakeComputerCommandService(),
            new ActionHistoryService(), f.Settings, NullLogger<AssistantConversationService>.Instance, computerUse: f.Session);
        var reply = await conversation.SendAsync("Open Notepad, type Hello World, press Ctrl+A then Ctrl+C.");
        Check(reply.Text.Contains("Task incomplete") && reply.Text.Contains("Ctrl+A") && reply.Text.Contains("Ctrl+C") && router.Calls == 1,
            "Partial sequence reported completion or repeated successful typing: calls=" + router.Calls + " reply=" + reply.Text);
    }
}
