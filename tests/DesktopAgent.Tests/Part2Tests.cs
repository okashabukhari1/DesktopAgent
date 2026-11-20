using System.Net;
using System.Text;
using System.Text.Json;
using DesktopAgent.Core.Ai;
using DesktopAgent.Core.Applications.Interfaces;
using DesktopAgent.Core.Applications.Models;
using DesktopAgent.Core.Applications.Services;
using DesktopAgent.Core.Automation.Enums;
using DesktopAgent.Core.Automation.Interfaces;
using DesktopAgent.Core.Automation.Models;
using DesktopAgent.Core.Automation.Services;
using DesktopAgent.Core.Interfaces;
using DesktopAgent.Core.Models;
using DesktopAgent.Core.Permissions;
using DesktopAgent.Core.Services;
using DesktopAgent.Core.Workspaces.Enums;
using DesktopAgent.Core.Workspaces.Interfaces;
using DesktopAgent.Core.Workspaces.Models;
using DesktopAgent.Infrastructure.Ai;
using DesktopAgent.Infrastructure.Settings;
using Microsoft.Extensions.Logging.Abstractions;

internal static class Part2Tests
{
    public static (string Name, Func<Task> Run)[] Cases =>
    [
        ("provider authentication failure is classified without leaking error text", ProviderAuthentication),
        ("provider invalid model is classified", ProviderInvalidModel),
        ("provider network failure is handled", ProviderNetwork),
        ("provider timeout is classified", ProviderTimeout),
        ("provider cancellation is propagated", ProviderCancellation),
        ("provider rejects malformed and truncated streams", ProviderMalformed),
        ("provider usage comes from actual response", ProviderUsage),
        ("connection test verifies the configured completion model", ConnectionTestsModel),
        ("provider retries only a bounded transient HTTP failure", ProviderRetry),
        ("tool registry rejects invalid schemas before execution", ToolArguments),
        ("application tools search, focus, report missing and ambiguous identities", ApplicationTools),
        ("close application Allow Ask Deny policy is enforced", ClosePolicy),
        ("workspace tools list, inspect, launch and report missing workspaces", WorkspaceTools),
        ("workspace launch enforces permissions of embedded actions", WorkspacePolicy),
        ("workspace partial result is returned as structured data", WorkspacePartial),
        ("workspace create and update use validated catalog identities", WorkspaceEditing),
        ("natural language multi-step requests use typed services in order", MultiStep),
        ("exact direct commands do not call the cloud", OfflineFastPath),
        ("multi-step phrasing is not consumed by the greedy parser", CompoundUsesAi),
        ("context preserves application references and actual tool results", ReferenceContext),
        ("conversation refuses a false success summary after failure", FailureSummary),
        ("denied actions cannot be automatically retried", DeniedLoop),
        ("tool loop and repeated calls are bounded", BoundedLoop),
        ("Stop cancels pending provider and prevents queued actions", StopProvider),
        ("Stop cancels an active workspace launch", StopWorkspace),
        ("Stop cancels pending permission approval", StopApproval),
        ("ambiguity selection preserves the originally requested action", ChoicePreservesAction),
        ("conversation history budget retains complete protocol turns", HistoryBudget),
        ("new conversation clears reference context and transcript", ResetContext)
    ];
    public static ComputerAgentToolRouter Router(IApplicationCatalog catalog, IComputerController controller,
        IWorkspaceService? workspaces = null, MemoryPolicy? policy = null, IAgentActionApprovalService? approval = null,
        IActionHistoryService? activity = null) =>
        new(new AgentToolRegistry(WindowsAgentTools.Create(controller, catalog, new TestApplications(), workspaces ?? new TestWorkspaces())),
            policy ?? new MemoryPolicy(), approval ?? new FakeAgentActionApprovalService(PermissionApproval.AllowOnce),
            activity ?? new ActionHistoryService(), new CharacterService(), NullLogger<ComputerAgentToolRouter>.Instance, workspaces);
    private static void Check(bool condition, string message) { if (!condition) throw new Exception(message); }
    private static AiToolCall Call(string tool, object args) => new(Guid.NewGuid().ToString("N"), tool, JsonSerializer.Serialize(args));
    private static async Task<ApplicationCatalog> Catalog()
    {
        var apps = new[] { "Spotify", "Google Chrome", "Visual Studio", "Visual Studio Code" }.Select(name =>
            new InstalledApplication { DisplayName = name, ExecutablePath = @"C:\Test\" + name + ".exe",
                Aliases = name == "Google Chrome" ? ["chrome"] : name == "Visual Studio Code" ? ["vscode"] : [],
                Sources = ["Test"] }).ToArray();
        var c = new ApplicationCatalog([new FakeApplicationDiscoveryProvider(apps)], new MemoryApplicationCatalogRepository());
        await c.RefreshAsync(); return c;
    }
    private sealed class ProviderFixture : IDisposable
    {
        private readonly TemporaryFolder _folder = new();
        public JsonSettingsService Settings { get; }
        public FakeCredentialStore Credentials { get; } = new();
        public HttpClient Client { get; }
        public OpenAiCompatibleProvider Provider { get; }
        public AiConnectionService Connection { get; } = new();
        public ProviderFixture(HttpMessageHandler handler, int timeout = 1)
        {
            Settings = new(NullLogger<JsonSettingsService>.Instance, _folder.Path);
            Settings.Current.Ai.BaseUrl = "https://provider.example.test/v1";
            Settings.Current.Ai.Model = "test-model"; Settings.Current.Ai.TimeoutSeconds = timeout;
            Credentials.Save(ProviderCredentialNames.ForBaseUrl(Settings.Current.Ai.BaseUrl), "PRIVATE_TEST_SECRET");
            Client = new(handler) { Timeout = Timeout.InfiniteTimeSpan };
            Provider = new(Client, Settings, Credentials, Connection);
        }
        public void Dispose() { Client.Dispose(); _folder.Dispose(); }
    }
    private static async Task<AiConnectionState> ProviderFailure(HttpMessageHandler handler)
    {
        using var f = new ProviderFixture(handler);
        try { await f.Provider.CompleteAsync([new("user", "Hello")], []); }
        catch (Exception ex) { Check(!ex.Message.Contains("PRIVATE_TEST_SECRET"), "Secret leaked."); return AiProviderErrors.State(ex); }
        throw new Exception("Expected provider failure.");
    }
    private static async Task ProviderAuthentication() => Check(await ProviderFailure(new HttpStub((_, _) =>
        Task.FromResult(Response(HttpStatusCode.Unauthorized, "PRIVATE_TEST_SECRET")))) == AiConnectionState.AuthenticationFailed, "Authentication state wrong.");
    private static async Task ProviderInvalidModel() => Check(await ProviderFailure(new HttpStub((_, _) =>
        Task.FromResult(Response(HttpStatusCode.NotFound, "{\"error\":{\"code\":\"model_not_found\"}}")))) == AiConnectionState.InvalidModel, "Model state wrong.");
    private static async Task ProviderNetwork() => Check(await ProviderFailure(new HttpStub((_, _) =>
        throw new HttpRequestException("Network unavailable"))) == AiConnectionState.NetworkError, "Network state wrong.");
    private static async Task ProviderTimeout() => Check(await ProviderFailure(new HttpStub(async (_, ct) => {
        await Task.Delay(Timeout.Infinite, ct); return Response(HttpStatusCode.OK, ""); })) == AiConnectionState.Timeout, "Timeout not classified.");
    private static async Task ProviderCancellation()
    {
        using var f = new ProviderFixture(new HttpStub(async (_, ct) => { await Task.Delay(Timeout.Infinite, ct); return Response(HttpStatusCode.OK, ""); }), 30);
        using var cancellation = new CancellationTokenSource(30);
        try { await f.Provider.CompleteAsync([new("user", "Hello")], [], cancellationToken: cancellation.Token); }
        catch (OperationCanceledException) { return; }
        throw new Exception("Cancellation lost.");
    }
    private static async Task ProviderMalformed()
    {
        foreach (var payload in new[] { "data: not-json\n", "data: {\"choices\":[{\"delta\":{\"content\":\"Oops\"}}]}\n",
            "data: {\"choices\":[{\"delta\":{},\"finish_reason\":\"length\"}]}\ndata: [DONE]\n" })
            Check(await ProviderFailure(new HttpStub((_, _) => Task.FromResult(Response(HttpStatusCode.OK, payload)))) == AiConnectionState.ProviderError, "Incomplete stream accepted.");
    }
    private static async Task ProviderUsage()
    {
        using var f = new ProviderFixture(new HttpStub((_, _) => Task.FromResult(Response(HttpStatusCode.OK,
            "data: {\"model\":\"actual-model\",\"choices\":[{\"delta\":{\"content\":\"Ready\"}}]}\n" +
            "data: {\"choices\":[],\"usage\":{\"prompt_tokens\":9,\"completion_tokens\":2}}\ndata: [DONE]\n"))));
        var r = await f.Provider.CompleteAsync([new("user", "Hi")], []);
        Check(r.Usage?.InputTokens == 9 && r.Usage.OutputTokens == 2 && r.Usage.Model == "actual-model", "Actual usage missing.");
    }
    private static async Task ConnectionTestsModel()
    {
        string? uri = null, model = null;
        using var f = new ProviderFixture(new HttpStub(async (request, ct) => {
            uri = request.RequestUri!.AbsolutePath;
            using var body = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(ct));
            model = body.RootElement.GetProperty("model").GetString();
            return Response(HttpStatusCode.OK, "{\"choices\":[{\"message\":{\"tool_calls\":[{\"function\":{\"name\":\"connection_probe\",\"arguments\":\"{}\"}}]}}]}");
        }));
        await f.Provider.TestConnectionAsync();
        Check(uri == "/v1/chat/completions" && model == "test-model" && f.Connection.Status.State == AiConnectionState.Connected, "Connection was not actually tested.");
    }
    private static async Task ProviderRetry()
    {
        var count = 0;
        using var f = new ProviderFixture(new HttpStub((_, _) => Task.FromResult(++count == 1
            ? Response(HttpStatusCode.ServiceUnavailable, "{}") : Response(HttpStatusCode.OK, "data: {\"choices\":[{\"delta\":{\"content\":\"Ready\"}}]}\ndata: [DONE]\n"))), 10);
        await f.Provider.CompleteAsync([new("user", "Hi")], []);
        Check(count == 2, "Retry was not bounded.");
    }
    private static HttpResponseMessage Response(HttpStatusCode status, string content) => new(status) { Content = new StringContent(content, Encoding.UTF8, "text/event-stream") };
    private static async Task ToolArguments()
    {
        var c = new RecordingController(); var router = Router(await Catalog(), c);
        foreach (var call in new[] {
            new AiToolCall("a", "open_application", "{\"application\":\"Spotify\",\"application\":\"Chrome\"}"),
            Call("move_application_window", new { application = "Spotify", layout = "anything" }),
            Call("move_to_monitor", new { application = "Spotify", monitor = 0 }),
            Call("resize_application_window", new { application = "Spotify", width = "big", height = 100 }),
            Call("open_url", new { url = "file:///C:/secret" }), Call("open_file", new { path = "relative.txt" }) })
            Check((await router.InvokeAsync(call)).Status == "InvalidArguments", "Invalid arguments accepted.");
        Check(c.Commands.Count == 0, "Invalid tool ran controller.");
    }
    private static async Task ApplicationTools()
    {
        var c = new RecordingController(); var router = Router(await Catalog(), c);
        var search = await router.InvokeAsync(Call("search_applications", new { query = "Spotify" }));
        var focus = await router.InvokeAsync(Call("focus_application", new { application = "Spotify" }));
        var missing = await router.InvokeAsync(Call("open_application", new { application = "DoesNotExist" }));
        var ambiguous = await router.InvokeAsync(Call("open_application", new { application = "Visual" }));
        Check(search.Success && focus.Success && c.Commands.Single().Type == ComputerCommandType.FocusApplication &&
            missing.Status == "NotFound" && ambiguous.Choices?.Count == 2, "App behavior incorrect.");
    }
    private static async Task ClosePolicy()
    {
        foreach (var (mode, answer, allowed, approvals) in new[] {
            (PermissionPolicy.Allow, PermissionApproval.Deny, true, 0), (PermissionPolicy.Deny, PermissionApproval.AllowOnce, false, 0),
            (PermissionPolicy.Ask, PermissionApproval.Deny, false, 1), (PermissionPolicy.Ask, PermissionApproval.AllowOnce, true, 1) })
        {
            var c = new RecordingController(); var p = new MemoryPolicy(); await p.SetPolicyAsync(CapabilityIds.ApplicationClose, mode);
            var a = new FakeAgentActionApprovalService(answer);
            var result = await Router(await Catalog(), c, policy: p, approval: a).InvokeAsync(Call("close_application", new { application = "vscode" }));
            Check(result.Success == allowed && a.Calls == approvals && c.Commands.Count == (allowed ? 1 : 0), "Close policy bypassed.");
        }
    }
    private static async Task WorkspaceTools()
    {
        var w = new TestWorkspaces(); var r = Router(await Catalog(), new RecordingController(), w);
        Check((await r.InvokeAsync(Call("list_workspaces", new { }))).Success, "List failed.");
        Check((await r.InvokeAsync(Call("get_workspace", new { workspace = "Programming" }))).Success, "Inspect failed.");
        Check((await r.InvokeAsync(Call("launch_workspace", new { workspace = "Programming" }))).Success && w.Launches == 1, "Launch failed.");
        Check((await r.InvokeAsync(Call("get_workspace_status", new { workspace = "Programming" }))).Status == "Ready", "Status wrong.");
        Check((await r.InvokeAsync(Call("launch_workspace", new { workspace = "Missing" }))).Status == "NotFound", "Missing workspace invented.");
    }
    private static async Task WorkspacePolicy()
    {
        var w = new TestWorkspaces();
        w.Workspace.PreLaunchActions.Add(new() { Type = WorkspaceActionType.Close, Target = "vscode" });
        var p = new MemoryPolicy(); await p.SetPolicyAsync(CapabilityIds.ApplicationClose, PermissionPolicy.Deny);
        var result = await Router(await Catalog(), new RecordingController(), w, p).InvokeAsync(Call("launch_workspace", new { workspace = "Programming" }));
        Check(result.Status == "PermissionDenied" && w.Launches == 0, "Workspace bypassed close denial.");
    }
    private static async Task WorkspacePartial()
    {
        var w = new TestWorkspaces { Outcome = WorkspaceLaunchStatus.Partial };
        var result = await Router(await Catalog(), new RecordingController(), w).InvokeAsync(Call("launch_workspace", new { workspace = "Programming" }));
        Check(result.Success && result.Status == "Partial" && JsonSerializer.Serialize(result.Data).Contains("missing"), "Partial result lost.");
    }
    private static async Task WorkspaceEditing()
    {
        var w = new TestWorkspaces();
        var policy = new MemoryPolicy();
        await policy.SetPolicyAsync(CapabilityIds.WorkspaceModify, PermissionPolicy.Allow);
        var router = Router(await Catalog(), new RecordingController(), w, policy);
        var updated = await router.InvokeAsync(Call("update_workspace", new { workspace = "Programming", applications = new[] { "Spotify", "chrome" } }));
        Check(updated.Success && w.LastSaved?.Applications.All(x => !string.IsNullOrWhiteSpace(x.CatalogApplicationId)) == true, "Workspace used unstable identities.");
        var created = await router.InvokeAsync(Call("create_workspace", new { workspace = "Music", applications = new[] { "Spotify" } }));
        Check(created.Success && w.LastSaved?.Name == "Music" && w.Launches == 0, "Create launched or failed.");
        var invalid = await router.InvokeAsync(Call("create_workspace", new { workspace = "Unknown", applications = new[] { "Missing App" } }));
        Check(!invalid.Success && w.LastSaved?.Name == "Music", "Missing application saved.");
    }
    private sealed class ConversationFixture : IDisposable
    {
        private readonly TemporaryFolder _folder = new();
        public JsonSettingsService Settings { get; }
        public FakeComputerCommandService Commands { get; } = new();
        public ScriptedProvider Provider { get; }
        public AssistantConversationService Conversation { get; }
        public ConversationFixture(IAgentToolRouter router, ScriptedProvider provider)
        {
            Settings = new(NullLogger<JsonSettingsService>.Instance, _folder.Path);
            Provider = provider;
            Conversation = new(provider, router, new ComputerCommandParser(), Commands, new ActionHistoryService(),
                Settings, NullLogger<AssistantConversationService>.Instance);
        }
        public void Dispose() => _folder.Dispose();
    }
    private static async Task MultiStep()
    {
        var controller = new RecordingController();
        var provider = new ScriptedProvider(
            new("", [Call("open_application", new { application = "chrome" }), Call("open_application", new { application = "vscode" })]),
            new("", [Call("move_application_window", new { application = "vscode", layout = "left" }), Call("move_application_window", new { application = "chrome", layout = "right" })]),
            new("Both apps are arranged.", []));
        using var f = new ConversationFixture(Router(await Catalog(), controller), provider);
        await f.Conversation.SendAsync("Can you open Chrome and VS Code and put them side by side?");
        Check(controller.Commands.Select(c => c.Type).SequenceEqual(new[] { ComputerCommandType.OpenApplication, ComputerCommandType.OpenApplication,
            ComputerCommandType.ApplyLayout, ComputerCommandType.ApplyLayout }) && controller.Commands[2].Layout == WindowLayout.Left &&
            controller.Commands[3].Layout == WindowLayout.Right && f.Commands.Inputs.Count == 0, "Multi-step order or typed path wrong.");
        Check(provider.Requests[1].Count(m => m.Role == "tool") == 2, "Tool results not returned to AI.");
    }
    private static async Task OfflineFastPath()
    {
        var provider = new ScriptedProvider();
        using var f = new ConversationFixture(Router(await Catalog(), new RecordingController()), provider);
        await f.Conversation.SendAsync("open chrome");
        Check(f.Commands.Inputs.Single() == "open chrome" && provider.Requests.Count == 0, "Cloud used for exact command.");
    }
    private static async Task CompoundUsesAi()
    {
        using var f = new ConversationFixture(Router(await Catalog(), new RecordingController()), new ScriptedProvider(new AiProviderResponse("Need tools", [])));
        await f.Conversation.SendAsync("Open Chrome and VS Code and put them side by side.");
        Check(f.Commands.Inputs.Count == 0 && f.Provider.Requests.Count == 1, "Parser swallowed multi-step phrasing.");
    }
    private static async Task ReferenceContext()
    {
        var controller = new RecordingController();
        var provider = new ScriptedProvider(new AiProviderResponse("", [Call("open_application", new { application = "Spotify" })]), new("Spotify opened.", []),
            new("", [Call("minimize_application", new { application = "Spotify" })]), new("Spotify minimized.", []));
        using var f = new ConversationFixture(Router(await Catalog(), controller), provider);
        await f.Conversation.SendAsync("Open Spotify.");
        await f.Conversation.SendAsync("Minimize it.");
        Check(f.Conversation.Context.LastReferencedApplication == "Spotify" &&
            provider.Requests[2].Any(m => m.Role == "system" && m.Content.Contains("Spotify")) &&
            provider.Requests[2].Any(m => m.Role == "tool") && controller.Commands[1].Type == ComputerCommandType.MinimizeWindow, "Reference/result context missing.");
    }
    private static async Task FailureSummary()
    {
        var controller = new RecordingController(_ => ComputerActionResult.Fail(ActionStatus.Failed, "Windows rejected the launch."));
        using var f = new ConversationFixture(Router(await Catalog(), controller),
            new ScriptedProvider(new AiProviderResponse("", [Call("open_application", new { application = "Spotify" })]), new("Spotify opened successfully.", [])));
        var result = await f.Conversation.SendAsync("Can you open Spotify?");
        Check(result.Text == "Windows rejected the launch.", "Provider false success reached UI.");
    }
    private static async Task DeniedLoop()
    {
        var p = new MemoryPolicy(); await p.SetPolicyAsync(CapabilityIds.ApplicationOpen, PermissionPolicy.Deny);
        var c = new RecordingController();
        using var f = new ConversationFixture(Router(await Catalog(), c, policy: p),
            new ScriptedProvider(new AiProviderResponse("", [Call("open_application", new { application = "Spotify" })]), new("Retry", [Call("open_application", new { application = "Spotify" })])));
        var reply = await f.Conversation.SendAsync("Can you open Spotify?");
        Check(c.Commands.Count == 0 && f.Provider.Requests.Count == 1 && reply.Text.Contains("blocked"), "Denied action retried.");
    }
    private static async Task BoundedLoop()
    {
        var calls = Enumerable.Range(0, 12).Select(i => new AiProviderResponse("", [Call("search_applications", new { query = "app" + i })])).ToArray();
        using var f = new ConversationFixture(Router(await Catalog(), new RecordingController()), new ScriptedProvider(calls));
        var reply = await f.Conversation.SendAsync("Find apps for me");
        Check(f.Provider.Requests.Count == 8 && reply.Text.Contains("action limit"), "Loop was unbounded.");
        using var repeated = new ConversationFixture(Router(await Catalog(), new RecordingController()),
            new ScriptedProvider(new AiProviderResponse("", [Call("list_applications", new { })]), new("", [Call("list_applications", new { })])));
        Check((await repeated.Conversation.SendAsync("Show available apps")).Text.Contains("repeated"), "Repeated call not blocked.");
    }
    private static async Task StopProvider()
    {
        var provider = new ScriptedProvider { Block = true };
        using var f = new ConversationFixture(Router(await Catalog(), new RecordingController()), provider);
        var request = f.Conversation.SendAsync("Can you help?");
        await provider.Started.Task.WaitAsync(TimeSpan.FromSeconds(2)); f.Conversation.Stop();
        Check((await request.WaitAsync(TimeSpan.FromSeconds(2))).Cancelled && f.Conversation.Entries.All(e => e.Action is null), "Stop scheduled actions.");
    }
    private static async Task StopWorkspace()
    {
        var w = new TestWorkspaces { Block = true };
        using var f = new ConversationFixture(Router(await Catalog(), new RecordingController(), w),
            new ScriptedProvider(new AiProviderResponse("", [Call("launch_workspace", new { workspace = "Programming" })])));
        var request = f.Conversation.SendAsync("Start my programming workspace.");
        await w.Started.Task.WaitAsync(TimeSpan.FromSeconds(2)); f.Conversation.Stop();
        Check((await request).Cancelled && w.Cancelled, "Workspace did not cancel.");
    }
    private static async Task StopApproval()
    {
        var approval = new BlockingApproval(); var controller = new RecordingController();
        using var f = new ConversationFixture(Router(await Catalog(), controller, approval: approval),
            new ScriptedProvider(new AiProviderResponse("", [Call("close_application", new { application = "vscode" })])));
        var request = f.Conversation.SendAsync("Close VS Code.");
        await approval.Started.Task.WaitAsync(TimeSpan.FromSeconds(2)); f.Conversation.Stop();
        Check((await request).Cancelled && controller.Commands.Count == 0, "Approval cancellation executed action.");
    }
    private static async Task ChoicePreservesAction()
    {
        var controller = new RecordingController(); var catalog = await Catalog();
        using var f = new ConversationFixture(Router(catalog, controller),
            new ScriptedProvider(new AiProviderResponse("", [Call("minimize_application", new { application = "Visual" })])));
        await f.Conversation.SendAsync("Minimize Visual.");
        var choices = f.Conversation.Entries.Single(e => e.Action is not null).Action!.Choices!;
        await f.Conversation.ChooseApplicationAsync(choices[0].Id);
        Check(controller.Commands.Single().Type == ComputerCommandType.MinimizeWindow, "Choice changed action to open.");
    }
    private static async Task HistoryBudget()
    {
        var provider = new ScriptedProvider(Enumerable.Range(0, 30).Select(_ => new AiProviderResponse("Answer", [])).ToArray());
        using var f = new ConversationFixture(Router(await Catalog(), new RecordingController()), provider);
        for (var i = 0; i < 20; i++) await f.Conversation.SendAsync("Question " + i);
        Check(provider.Requests.Last().Count <= 23, "Unlimited history sent.");
    }
    private static async Task ResetContext()
    {
        using var f = new ConversationFixture(Router(await Catalog(), new RecordingController()),
            new ScriptedProvider(new AiProviderResponse("", [Call("open_application", new { application = "Spotify" })]), new("Done", [])));
        await f.Conversation.SendAsync("Open Spotify.");
        var id = f.Conversation.Context.ActiveConversationId; f.Conversation.Reset();
        Check(f.Conversation.Entries.Count == 0 && f.Conversation.Context.LastToolResult is null &&
            f.Conversation.Context.ActiveConversationId != id, "New conversation retained context.");
    }
}

internal sealed class HttpStub(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> response) : HttpMessageHandler
{
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct) => response(request, ct);
}
internal sealed class ScriptedProvider(params AiProviderResponse[] responses) : IAiProvider
{
    private int _index;
    public bool Block { get; set; }
    public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    public List<IReadOnlyList<AiMessage>> Requests { get; } = [];
    public string Name => "Scripted";
    public async Task<AiProviderResponse> CompleteAsync(IReadOnlyList<AiMessage> messages, IReadOnlyList<AiToolDefinition> tools,
        IProgress<string>? textDeltaProgress = null, CancellationToken cancellationToken = default)
    {
        Requests.Add(messages.ToArray()); Started.TrySetResult();
        if (Block) await Task.Delay(Timeout.Infinite, cancellationToken);
        return _index < responses.Length ? responses[_index++] : throw new HttpRequestException("Offline");
    }
    public Task<string> TestConnectionAsync(CancellationToken cancellationToken = default) => Task.FromResult("Test");
    public Task<IReadOnlyList<string>> GetModelsAsync(CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<string>>([]);
}
internal sealed class MemoryPolicy : IPolicyService
{
    private readonly Dictionary<string, PermissionPolicy> _values = [];
    public IReadOnlyList<AgentCapability> Capabilities => AgentCapabilities.All;
    public PermissionPolicy GetPolicy(string id) => _values.GetValueOrDefault(id, AgentCapabilities.Find(id)?.DefaultPolicy ?? PermissionPolicy.Deny);
    public PolicyEvaluation Evaluate(string id) => new(id, GetPolicy(id) switch {
        PermissionPolicy.Allow => PolicyOutcome.Allowed, PermissionPolicy.Ask => PolicyOutcome.ConfirmationRequired, _ => PolicyOutcome.Denied }, GetPolicy(id), "");
    public Task SetPolicyAsync(string id, PermissionPolicy value, CancellationToken cancellationToken = default) { _values[id] = value; return Task.CompletedTask; }
    public Task ResetAsync(CancellationToken cancellationToken = default) { _values.Clear(); return Task.CompletedTask; }
}
internal sealed class TestApplications : IApplicationControlService
{
    public IReadOnlyList<RunningApplicationInfo> GetRunningApplications() => [];
    public Task<ComputerActionResult> LaunchAsync(string target, CancellationToken cancellationToken = default) => Task.FromResult(ComputerActionResult.Ok("Opened", target));
    public Task<ComputerActionResult> LaunchExplicitAsync(string target, CancellationToken cancellationToken = default) => LaunchAsync(target, cancellationToken);
    public ComputerActionResult Focus(string target) => ComputerActionResult.Ok("Focused", target);
    public ComputerActionResult Close(string target) => ComputerActionResult.Ok("Closed", target);
}
internal sealed class TestWorkspaces : IWorkspaceService
{
    public WorkspaceDefinition Workspace { get; } = new() { Name = "Programming", Applications = [new() { DisplayName = "Chrome", ApplicationIdentifier = "chrome" }] };
    public WorkspaceLaunchStatus Outcome { get; set; } = WorkspaceLaunchStatus.Ready;
    public bool Block { get; set; }
    public bool Cancelled { get; private set; }
    public int Launches { get; private set; }
    public WorkspaceDefinition? LastSaved { get; private set; }
    public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private WorkspaceSession? _session;
    public event EventHandler? WorkspacesChanged { add { } remove { } }
    public event EventHandler<WorkspaceProgress>? ProgressChanged { add { } remove { } }
    public IReadOnlyList<WorkspaceDefinition> Workspaces => [Workspace];
    public IReadOnlyDictionary<Guid, WorkspaceSession> Sessions => _session is null ? new Dictionary<Guid, WorkspaceSession>() : new Dictionary<Guid, WorkspaceSession> { [Workspace.Id] = _session };
    public Task InitializeAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
    public WorkspaceDefinition? Find(string target) => target.Equals(Workspace.Name, StringComparison.OrdinalIgnoreCase) || target == Workspace.Id.ToString() ? Workspace.DeepCopy() : null;
    public async Task<WorkspaceLaunchResult?> LaunchAsync(Guid id, CancellationToken cancellationToken = default)
    {
        Launches++; Started.TrySetResult();
        if (Block)
        {
            try { await Task.Delay(Timeout.Infinite, cancellationToken); }
            catch (OperationCanceledException) { Cancelled = true; throw; }
        }
        _session = new() { WorkspaceId = id, WorkspaceName = Workspace.Name, Status = Outcome, CompletedAt = DateTimeOffset.Now };
        if (Outcome == WorkspaceLaunchStatus.Partial) _session.Results.Add(new(WorkspaceStepKind.Application, "missing", ActionStatus.NotFound, "Not installed", DateTimeOffset.Now));
        return new(_session, Outcome == WorkspaceLaunchStatus.Partial ? "Workspace started with one issue." : "Workspace ready.");
    }
    public bool Cancel(Guid id) { Cancelled = true; return Block; }
    public Task<WorkspaceValidationResult> SaveAsync(WorkspaceDefinition w, CancellationToken cancellationToken = default)
    { LastSaved = w.DeepCopy(); return Task.FromResult(WorkspaceValidationResult.Valid); }
    public Task<WorkspaceDefinition?> DuplicateAsync(Guid id, CancellationToken cancellationToken = default) => Task.FromResult<WorkspaceDefinition?>(null);
    public Task<bool> DeleteAsync(Guid id, CancellationToken cancellationToken = default) => Task.FromResult(false);
}
internal sealed class BlockingApproval : IAgentActionApprovalService
{
    public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    public async Task<PermissionApproval> RequestAsync(AgentCapability capability, string target, CancellationToken cancellationToken = default)
    {
        Started.TrySetResult(); await Task.Delay(Timeout.Infinite, cancellationToken); return PermissionApproval.Deny;
    }
}
