using System.Text.Json;
using DesktopAgent.Core.Ai;
using DesktopAgent.Core.Automation.Services;
using DesktopAgent.Core.Permissions;
using DesktopAgent.Core.Services;
using DesktopAgent.Core.UiAwareness;
using DesktopAgent.Infrastructure.Ai;
using DesktopAgent.Infrastructure.Settings;
using DesktopAgent.Infrastructure.UiAwareness;
using Microsoft.Extensions.Logging.Abstractions;

internal static class Part6Tests
{
    public static (string Name, Func<Task> Run)[] Cases => [
        ("UI element serialization preserves unknown properties", Model),
        ("UI password model and context never serialize protected values", Password),
        ("UI filters visible interactive enabled independently", Filtering),
        ("UI search exact name ranks above partial match", Ranking),
        ("UI search multiple matches requires ambiguity", Ambiguity),
        ("UI search checks type automation id class and value", SearchFields),
        ("UI no match in truncated tree remains incomplete", Partial),
        ("UI traversal respects depth and element budgets", Limits),
        ("UI traversal cancellation propagates", TraversalCancel),
        ("UI snapshot references expire after two seconds", Expiry),
        ("UI context contains no tree or coordinates and at most twelve controls", Context),
        ("UI inspect_window includes non-interactive editor before bounded toolbar context", InspectorEditor),
        ("UI Notepad unknown visibility editor is observable but not authorized", UnknownEditor),
        ("UI all seven tools validate strict schemas", Tools),
        ("UI tools translate stale secure and unsupported errors", Errors),
        ("UI timeout holds single lane until provider returns", Timeout),
        ("UI worker cancellation releases caller promptly", WorkerCancel),
        ("UI sensitive windows fail closed", Sensitive),
        ("UI inspection policy denial prevents any provider call", Policy),
        ("UI activity history omits window and control names", History),
        ("UI input requests explain observation-only without calling AI", NoInput),
        ("UI answer cannot claim controls without fresh tool evidence", Evidence),
        ("UI retrieved injection cannot launch an application", Injection),
        ("UI observation source is not reused as next-turn screen evidence", FreshTurn),
        ("UI STOP cancels current provider inspection", Stop),
        ("UI Win32 metadata works when UIA provider fails", Win32WithoutUia),
        ("UI current application question works without cloud or UIA", LocalApplication),
        ("UI controls fallback identifies app without guessing or retry", ControlsFallback)
    ];
    private static void Check(bool value, string message) { if (!value) throw new Exception(message); }
    private static UiElement Button(string id, string name) => new() { Id = id, Name = name, ControlType = "Button", IsEnabled = true, IsVisible = true, CanInvoke = true };
    private static UiSnapshot Snapshot(params UiElement[] elements) => new(Guid.NewGuid(), DateTimeOffset.UtcNow,
        new(123, 1, "test", null, "Private window name", new(0, 0, 100, 100), true, true, false, false, true, null, DateTimeOffset.UtcNow),
        null, null, [], elements, elements.Where(e => e.IsInteractive).ToArray(), false);
    private static Task Model()
    {
        var e = new UiElement { Id = "ui:1", ControlType = "CustomControl", IsVisible = null };
        var round = JsonSerializer.Deserialize<UiElement>(JsonSerializer.Serialize(e))!;
        Check(round.IsVisible is null && round.ControlType == "CustomControl" && round.CanInvoke is null, "Fabricated properties"); return Task.CompletedTask;
    }
    private static Task Password()
    {
        var e = new UiElement { Id = "ui:1", Name = "real-secret-password", Value = "real-secret-password", IsPassword = true,
            Children = [Button("ui:2", "real-secret-password")] };
        foreach (var json in new[] { JsonSerializer.Serialize(e), JsonSerializer.Serialize(UiContextBuilder.Element(e)) })
            Check(!json.Contains("real-secret-password") && json.Contains("PROTECTED") && json.Contains("Password field"), "Password leaked");
        Check(UiAlgorithms.Search(Snapshot(e), new(Value: "PROTECTED")).Candidates.Count == 0, "Password searchable"); return Task.CompletedTask;
    }
    private static Task Filtering()
    {
        var e = Button("ui:1", "Save"); Check(UiAlgorithms.MatchesFilter(e, new(InteractiveOnly: true, EnabledOnly: true)), "Visible button filtered");
        Check(!UiAlgorithms.MatchesFilter(e with { IsVisible = null }, new()), "Unknown visibility accepted");
        Check(!UiAlgorithms.MatchesFilter(e with { IsEnabled = false }, new(EnabledOnly: true)), "Disabled accepted");
        Check(!UiAlgorithms.MatchesFilter(e with { CanInvoke = null }, new(InteractiveOnly: true)), "Noninteractive accepted"); return Task.CompletedTask;
    }
    private static Task Ranking()
    { var r = UiAlgorithms.Search(Snapshot(Button("ui:1", "Save as"), Button("ui:2", "Save")), new("Save", "Button")); Check(r.Candidates[0].Id == "ui:2", "Partial ranked first"); return Task.CompletedTask; }
    private static Task Ambiguity()
    { var r = UiAlgorithms.Search(Snapshot(Button("ui:1", "Open"), Button("ui:2", "Open")), new("Open")); Check(r.Status == "Ambiguous" && r.Candidates.Count == 2, "Arbitrary choice"); return Task.CompletedTask; }
    private static Task SearchFields()
    {
        var e = Button("ui:1", "Save") with { AutomationId = "commit", ClassName = "NativeButton", Value = "safe-test" };
        Check(UiAlgorithms.Search(Snapshot(e), new("Save", "Button", "commit", "NativeButton", "safe-test")).Status == "Found", "Field match failed");
        Check(UiAlgorithms.Search(Snapshot(e), new(ClassName: "Other")).Status == "NotFound", "Class mismatch"); return Task.CompletedTask;
    }
    private static Task Partial()
    { var r = UiAlgorithms.Search(Snapshot(Button("ui:1", "Save")) with { Truncated = true }, new("Continue")); Check(r.Status == "NotFound" && r.SearchIncomplete, "Partial proved absence"); return Task.CompletedTask; }
    private static Task Limits()
    {
        var tree = Button("ui:1", "Root") with { Children = Enumerable.Range(2, 50).Select(i => Button("ui:" + i, "Child") with { Children = [Button("nested:" + i, "Nested")] }).ToArray() };
        Check(UiAlgorithms.Flatten(UiAlgorithms.LimitTree(tree, new(MaximumElements: 7))).Count() == 7, "Count unbounded");
        Check(UiAlgorithms.Flatten(UiAlgorithms.LimitTree(tree, new(MaximumDepth: 1))).All(e => !e.Id.StartsWith("nested")), "Depth unbounded");
        Check(UiAlgorithms.LimitTree(tree, new(MaximumDepth: 0)).Children.Count == 0, "Depth zero traversed"); return Task.CompletedTask;
    }
    private static Task TraversalCancel()
    {
        using var ct = new CancellationTokenSource(); ct.Cancel();
        try { UiAlgorithms.LimitTree(Button("ui:1", "Root"), new(), ct.Token); throw new Exception("Cancellation ignored"); } catch (OperationCanceledException) { } return Task.CompletedTask;
    }
    private static Task Expiry()
    { var s = Snapshot(); Check(UiAlgorithms.IsFresh(s, s.Timestamp.AddSeconds(1)), "Fresh rejected"); Check(!UiAlgorithms.IsFresh(s, s.Timestamp.AddSeconds(3)) && !UiAlgorithms.IsFresh(s, s.Timestamp.AddSeconds(-1)), "Stale accepted"); return Task.CompletedTask; }
    private static Task Context()
    {
        var json = JsonSerializer.Serialize(UiContextBuilder.Snapshot(Snapshot(Enumerable.Range(1, 80).Select(i => Button("ui:" + i, "Save")).ToArray())));
        using var doc = JsonDocument.Parse(json); Check(doc.RootElement.GetProperty("elements").GetArrayLength() == 12 && !json.Contains("Children") && !json.Contains("RootElement"), "Entire tree transmitted");
        Check(doc.RootElement.GetProperty("contextTruncated").GetBoolean(), "Context limit hidden"); return Task.CompletedTask;
    }
    private const string Args = "{\"window_reference\":\"window:test\",\"query\":\"Save\",\"control_type\":\"Button\"}";
    private static async Task Tools()
    {
        var ui = new FakeUi(); var tools = UiTools.Create(ui).ToArray(); Check(tools.Length == 7 && tools.All(t => !t.Definition.Name.Contains("click")), "Wrong tools");
        using var args = JsonDocument.Parse(Args);
        foreach (var tool in tools)
        {
            var input = tool.Definition.Name is "get_foreground_window" or "list_open_windows" ? JsonSerializer.SerializeToElement(new { }) : args.RootElement;
            tool.Validate(input); Check((await tool.RequiredCapabilitiesAsync(input)).Single() == CapabilityIds.ScreenInspectUi, "No inspection permission");
            Check((await tool.ExecuteAsync(input)).Success, "Read tool failed");
            using var extra = JsonDocument.Parse("{\"window_handle\":null,\"query\":null,\"control_type\":null,\"click\":true}");
            try { tool.Validate(extra.RootElement); throw new Exception("Unknown argument accepted"); } catch (ArgumentException) { }
        }
        foreach (var invalid in new[] { "-1", "1.5", "exe:cmd", "99999999999999999999999999" })
        {
            using var a = JsonDocument.Parse(JsonSerializer.Serialize(new { window_reference = invalid, query = "Save", control_type = "Button" }));
            try { tools[2].Validate(a.RootElement); throw new Exception("Invalid reference accepted"); } catch (ArgumentException) { }
        }
    }
    private static async Task Errors()
    {
        var ui = new FakeUi(); var t = UiTools.Create(ui).First(t => t.Definition.Name == "inspect_window"); using var args = JsonDocument.Parse(Args);
        foreach (var code in new[] { "WindowGone", "ElementUnavailable", "SecureDesktopUnavailable", "SensitiveWindowBlocked", "Timeout", "StaleSnapshot", "ProviderBusy" })
        {
            ui.Error = code; var r = await t.ExecuteAsync(args.RootElement);
            var fallback = code is "ElementUnavailable" or "Timeout" or "ProviderBusy";
            Check(r.Success == fallback && r.ErrorCode == code && (!fallback || r.Status == "WindowOnly"), "Incorrect metadata fallback or protected-window bypass");
        }
    }
    private static async Task Timeout()
    {
        using var lane = new UiObservationWorker(TimeSpan.FromMilliseconds(70)); using var release = new ManualResetEventSlim();
        var watch = System.Diagnostics.Stopwatch.StartNew();
        try { await lane.RunAsync(_ => { release.Wait(TimeSpan.FromSeconds(2)); return 1; }); throw new Exception("No timeout"); }
        catch (UiObservationException e) { Check(e.Code == "Timeout" && watch.Elapsed < TimeSpan.FromSeconds(1), "Caller not bounded"); }
        try { await lane.RunAsync(_ => 2); throw new Exception("Extra hung worker created"); } catch (UiObservationException e) { Check(e.Code == "ProviderBusy", "No busy guard"); }
        release.Set(); await Task.Delay(100); Check(await lane.RunAsync(_ => 3) == 3, "Lane did not recover");
    }
    private static async Task WorkerCancel()
    {
        using var lane = new UiObservationWorker(); using var ct = new CancellationTokenSource(70);
        try { await lane.RunAsync(token => { token.WaitHandle.WaitOne(); token.ThrowIfCancellationRequested(); return 1; }, ct.Token); throw new Exception("No cancellation"); }
        catch (OperationCanceledException) { }
    }
    private static Task Sensitive()
    {
        Check(WindowsUiAutomationService.IsSensitiveWindow("consent", "") && WindowsUiAutomationService.IsSensitiveWindow("browser", "Sign in") &&
            WindowsUiAutomationService.IsSensitiveWindow("keepass", "Vault") && !WindowsUiAutomationService.IsSensitiveWindow("notepad", "Untitled"), "Sensitive classification");
        Check(AgentCapabilities.Find(CapabilityIds.ScreenInspectUi)?.DefaultPolicy == PermissionPolicy.Ask && AgentCapabilities.Find(CapabilityIds.ScreenInteract)?.Availability == CapabilityAvailability.ComingSoon, "Unsafe policy defaults"); return Task.CompletedTask;
    }
    private static ComputerAgentToolRouter Router(FakeUi ui, MemoryPolicy policy, ActionHistoryService history) => new(new AgentToolRegistry(UiTools.Create(ui)), policy,
        new FakeAgentActionApprovalService(PermissionApproval.AllowOnce), history, new CharacterService(), NullLogger<ComputerAgentToolRouter>.Instance);
    private static async Task Policy()
    {
        var ui = new FakeUi(); var p = new MemoryPolicy(); await p.SetPolicyAsync(CapabilityIds.ScreenInspectUi, PermissionPolicy.Deny);
        var r = await Router(ui, p, new()).InvokeAsync(new("ui", "inspect_window", Args)); Check(r.Status == "PermissionDenied" && ui.Reads == 0, "Denied provider read");
    }
    private static async Task History()
    {
        var h = new ActionHistoryService(); await Router(new(), new(), h).InvokeAsync(new("ui", "inspect_window", Args));
        var json = JsonSerializer.Serialize(h.GetRecent(100)); Check(!json.Contains("Private window name") && !json.Contains("Private control name"), "Sensitive activity data");
    }
    private static AssistantConversationService Assistant(IAiProvider provider, IAgentToolRouter router, FakeUi ui) => new(provider, router, new ComputerCommandParser(), new FakeComputerCommandService(),
        new ActionHistoryService(), new JsonSettingsService(NullLogger<JsonSettingsService>.Instance), NullLogger<AssistantConversationService>.Instance, uiAutomation: ui);
    private static async Task NoInput()
    {
        var ui = new FakeUi(); var a = Assistant(new ScriptedProvider(), Router(ui, new(), new()), ui);
        var r = await a.SendAsync("Click Bluetooth & devices."); Check(r.Text.Contains("unavailable") && ui.Reads == 0, "Fake input claimed");
        Check(AssistantConversationService.IsUiInputRequest("Please type my password") && !AssistantConversationService.IsUiInputRequest("Open Chrome"), "Input guard regression");
    }
    private static async Task Evidence()
    {
        var ui = new FakeUi(); var a = Assistant(new ScriptedProvider(new AiProviderResponse("There is a Save button.", [])), Router(ui, new(), new()), ui);
        var r = await a.SendAsync("Is there a Save button?"); Check(r.Text.Contains("cannot guess"), "Hallucinated screen state");
    }
    private static async Task Injection()
    {
        var ui = new FakeUi(); var a = Assistant(new ScriptedProvider(new("", [new("observe", "inspect_window", Args)]),
            new("", [new("injected", "open_application", "{\"application\":\"powershell\"}")])), Router(ui, new(), new()), ui);
        var r = await a.SendAsync("What controls are on this screen?"); Check(r.Text.Contains("Stopped") && a.Context.LastToolResult?.Data is null, "UI data authorized action");
    }
    private static async Task FreshTurn()
    {
        var ui = new FakeUi(); var a = Assistant(new ScriptedProvider(new("", [new("observe", "inspect_window", Args)]), new("Save button found in selected window.", []), new("Save is still there.", [])), Router(ui, new(), new()), ui);
        await a.SendAsync("Is there a Save button?"); var r = await a.SendAsync("Is there a Save button now?"); Check(r.Text.Contains("cannot guess"), "Stale evidence reused");
    }
    private static async Task Stop()
    {
        var ui = new FakeUi { Wait = true }; var a = Assistant(new ScriptedProvider(new AiProviderResponse("", [new("observe", "inspect_window", Args)])), Router(ui, new(), new()), ui);
        var running = a.SendAsync("What controls are on this screen?"); await Task.Delay(70); a.Stop();
        Check((await running.WaitAsync(TimeSpan.FromSeconds(1))).Cancelled && ui.Cancelled, "STOP ignored inspection");
    }
    private static async Task Win32WithoutUia()
    {
        var ui = new FakeUi { Error = "UnsupportedProvider" };
        using var args = JsonDocument.Parse("{}");
        var result = await UiTools.Create(ui).First(t => t.Definition.Name == "get_foreground_window").ExecuteAsync(args.RootElement);
        Check(result.Success && ui.Reads == 0 && JsonSerializer.Serialize(result.Data).Contains("ApplicationName"), "Window awareness depended on UIA");
    }
    private static async Task LocalApplication()
    {
        var ui = new FakeUi { Error = "UnsupportedProvider" };
        var a = Assistant(new ScriptedProvider(), Router(ui, new(), new()), ui);
        var reply = await a.SendAsync("What application am I looking at?");
        Check(reply.Text.Contains("Active external application: test") && ui.Reads == 0, "Metadata required cloud/UIA");
    }
    private static async Task ControlsFallback()
    {
        var ui = new FakeUi { Error = "AccessDenied" };
        var a = Assistant(new ScriptedProvider(new AiProviderResponse("", [new("observe", "inspect_window", Args)])), Router(ui, new(), new()), ui);
        var reply = await a.SendAsync("What buttons are available?");
        Check(reply.Text.Contains("I can identify test") && reply.Text.Contains("aren't currently available") && ui.Reads == 1, "Fallback guessed controls or erased app");
    }
    private static async Task InspectorEditor()
    {
        var editor = new UiElement { Id = "ui:40", ControlType = "Document", ClassName = "RichEditD2DPT",
            Name = "Text editor", IsVisible = true, IsEnabled = true, IsPassword = null };
        var snapshot = Snapshot(Enumerable.Range(1, 24).Select(i => Button("ui:" + i, "Toolbar")).Append(editor).ToArray());
        var ui = new FakeUi { InspectionSnapshot = snapshot };
        using var args = JsonDocument.Parse("{}");
        var result = await UiTools.Create(ui).Single(t => t.Definition.Name == "inspect_window").ExecuteAsync(args.RootElement);
        var data = JsonSerializer.SerializeToElement(result.Data);
        var elements = data.GetProperty("elements");
        Check(result.Success && elements.GetArrayLength() == 12 && data.GetProperty("contextTruncated").GetBoolean(), "Inspection context became unbounded");
        Check(elements[0].GetProperty("Id").GetString() == editor.Id && elements[0].GetProperty("ControlType").GetString() == "Document", "Document editor omitted behind toolbar");
        Check(!editor.IsInteractive && editor.IsPassword is null, "Fixture changed sensitivity or interactivity");
    }
    private static async Task UnknownEditor()
    {
        var editor = new UiElement { Id = "ui:3", ControlType = "Document", ClassName = "RichEditD2DPT",
            Name = "[TEXT OMITTED]", IsVisible = null, IsEnabled = true, IsPassword = null };
        var root = new UiElement { Id = "ui:1", Children = [editor] };
        var original = Snapshot(Enumerable.Range(10, 24).Select(i => Button("ui:" + i, "Toolbar")).ToArray());
        var snapshot = original with { Window = original.Window with { ApplicationName = "Notepad", RootElement = root } };
        var ui = new FakeUi { InspectionSnapshot = snapshot };
        foreach (var json in new[] { "{}", "{\"control_type\":\"Document\"}" })
        {
            using var args = JsonDocument.Parse(json);
            var result = await UiTools.Create(ui).Single(t => t.Definition.Name == "inspect_window").ExecuteAsync(args.RootElement);
            var elements = JsonSerializer.SerializeToElement(result.Data).GetProperty("elements");
            Check(elements.GetArrayLength() <= 12 && elements[0].GetProperty("Id").GetString() == editor.Id, "Unknown editor missing from inspection/query");
            Check(elements[0].GetProperty("IsVisible").ValueKind == JsonValueKind.Null, "Unknown visibility fabricated");
        }
        Check(snapshot.Elements.Count == 24 && editor.IsPassword is null && !UiAlgorithms.MatchesFilter(editor, new()), "Observation relaxed visibility/sensitivity filter");
        foreach (var excluded in new[] { editor with { IsVisible = false }, editor with { IsEnabled = false }, editor with { IsPassword = true }, editor with { ClassName = "UnknownEditor" } })
        {
            var negative = snapshot with { Window = snapshot.Window with { RootElement = root with { Children = [excluded] } } };
            Check(UiContextBuilder.WithNotepadEditorCandidates(negative).Elements.Count == 24, "Unsafe candidate included");
        }
        var other = snapshot with { Window = snapshot.Window with { ApplicationName = "Calculator" } };
        Check(UiContextBuilder.WithNotepadEditorCandidates(other).Elements.Count == 24, "Non-Notepad filter relaxed");
    }
    internal sealed class FakeUi : IWindowsUiAutomationService
    {
        public event EventHandler? Changed { add { } remove { } }
        public bool IsInspecting => false;
        public UiSnapshot? LastSnapshot => null;
        public int Reads; public string? Error; public bool Wait; public bool Cancelled; public UiSnapshot? InspectionSnapshot;
        public void CancelInspection() => Cancelled = true;
        public Task<WindowContext?> GetForegroundWindow(CancellationToken ct = default) => Task.FromResult<WindowContext?>(Snapshot().Window);
        public Task<IReadOnlyList<WindowContext>> ListOpenWindows(CancellationToken ct = default) => Task.FromResult<IReadOnlyList<WindowContext>>([Snapshot().Window]);
        public Task<WindowContext?> GetWindowInfo(long h, CancellationToken ct = default) => GetForegroundWindow(ct);
        public async Task<UiSnapshot> GetWindowTree(long? h, UiInspectionOptions? options = null, CancellationToken ct = default)
        { Reads++; if (Wait) await Task.Delay(System.Threading.Timeout.Infinite, ct); if (Error is not null) throw new UiObservationException(Error); return InspectionSnapshot ?? Snapshot(Button("ui:1", "Save"), Button("ui:2", "Private control name")); }
        public Task<UiElement?> GetFocusedElement(long? h, CancellationToken ct = default) => Task.FromResult<UiElement?>(null);
        public Task<IReadOnlyList<UiElement>> GetUiElements(long? h, UiInspectionOptions options, CancellationToken ct = default) => Task.FromResult<IReadOnlyList<UiElement>>([]);
        public Task<UiSearchResult> FindElement(long? h, UiElementQuery query, CancellationToken ct = default) => Task.FromResult(new UiSearchResult("NotFound", [], true));
        public Task<UiElement> GetElementProperties(Guid s, string r, CancellationToken ct = default) => Task.FromException<UiElement>(new UiObservationException("StaleElement"));
        public Task<IReadOnlyList<DialogContext>> GetDialogs(long? h, CancellationToken ct = default) => Task.FromResult<IReadOnlyList<DialogContext>>([]);
        public Task<IReadOnlyList<UiElement>> GetInteractiveElements(long? h, CancellationToken ct = default) => Task.FromResult<IReadOnlyList<UiElement>>([]);
    }
}
