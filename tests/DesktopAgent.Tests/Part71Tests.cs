using System.Text.Json;
using DesktopAgent.Core.Ai;
using DesktopAgent.Core.Automation.Enums;
using DesktopAgent.Core.Automation.Models;
using DesktopAgent.Core.Automation.Services;
using DesktopAgent.Core.Applications.Services;
using DesktopAgent.Core.Permissions;
using DesktopAgent.Core.Services;
using DesktopAgent.Core.UiAwareness;
using DesktopAgent.Infrastructure.Ai;
using DesktopAgent.Infrastructure.Settings;
using DesktopAgent.Infrastructure.UiAwareness;
using DesktopAgent.Infrastructure.Windows.Automation;
using DesktopAgent.Infrastructure.Projects;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Logging;

internal static class Part71Tests
{
    // Explicitly user-authorized account read: exact OpenRouter destination, no redirects/settings writes or key output.
    public static async Task<int> ProviderAllowanceDiagnostic()
    {
        var settings = new JsonSettingsService(NullLogger<JsonSettingsService>.Instance); await settings.LoadAsync();
        if (!Uri.TryCreate(settings.Current.Ai.BaseUrl, UriKind.Absolute, out var configured) || configured.Scheme != "https" || configured.Host != "openrouter.ai")
        { Console.WriteLine("ALLOWANCE NOT CHECKED: configured provider is not OpenRouter."); return 2; }
        var credentials = new WindowsCredentialStore(); var key = credentials.Read(ProviderCredentialNames.ForBaseUrl(settings.Current.Ai.BaseUrl));
        if (string.IsNullOrWhiteSpace(key)) { Console.WriteLine("ALLOWANCE NOT CHECKED: no saved credential."); return 2; }
        using var client = new HttpClient(new HttpClientHandler { AllowAutoRedirect = false }) { Timeout = TimeSpan.FromSeconds(20) };
        using var request = new HttpRequestMessage(HttpMethod.Get, "https://openrouter.ai/api/v1/key");
        request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", key);
        try
        {
            using var response = await client.SendAsync(request);
            Console.WriteLine("ALLOWANCE HTTP=" + (int)response.StatusCode);
            if (!response.IsSuccessStatusCode) return 1;
            if (response.Content.Headers.ContentLength is > 65536) return 2;
            var body = await response.Content.ReadAsStringAsync(); if (body.Length > 65536) return 2;
            using var document = JsonDocument.Parse(body); var data = document.RootElement.GetProperty("data");
            foreach (var field in new[] { "limit", "limit_remaining", "usage", "usage_daily", "usage_weekly", "usage_monthly", "is_free_tier" })
                if (data.TryGetProperty(field, out var value) && value.ValueKind is JsonValueKind.Number or JsonValueKind.Null or JsonValueKind.True or JsonValueKind.False)
                    Console.WriteLine(field + "=" + value.GetRawText());
            return 0;
        }
        catch (Exception error) when (error is HttpRequestException or TaskCanceledException or JsonException)
        { Console.WriteLine("ALLOWANCE NOT CHECKED: " + error.GetType().Name); return 2; }
    }
    private sealed class ShapeLogger : ILogger<OpenAiCompatibleProvider>
    {
        public List<string> Entries { get; } = [];
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel level) => true;
        public void Log<TState>(LogLevel level, EventId eventId, TState state, Exception? error, Func<TState, Exception?, string> formatter)
        { var message = formatter(state, error); Entries.Add(message); Console.WriteLine(message); }
    }
    public static (string Name, Func<Task> Run)[] Cases => UiTools.Names.Select(name =>
        ($"Part71 provider JSON routes {name}", (Func<Task>)(() => ProviderRoute(name)))).Concat([
        ("Part71 optional omitted and null fields share one contract", Optional),
        ("Part71 invalid provider shapes never execute a read", Invalid),
        ("Part71 hosted Calculator is not Settings or generic frame host", Hosted),
        ("Part71 already-running launch focuses selected HWND without process re-search", PinnedFocus),
        ("Part71 no-argument foreground read stays pinned through approval and rejects PID reuse", ApprovalTarget),
        ("Part71 provider rejection diagnostic contains schema shape but no sensitive values", ProviderSafeRejection)
    ]).ToArray();
    private static void Check(bool value, string message) { if (!value) throw new Exception(message); }
    private static ComputerAgentToolRouter Router(Part6Tests.FakeUi ui) => new(new AgentToolRegistry(UiTools.Create(ui)), new MemoryPolicy(),
        new FakeAgentActionApprovalService(PermissionApproval.AllowOnce), new ActionHistoryService(), new CharacterService(), NullLogger<ComputerAgentToolRouter>.Instance);
    private static async Task ProviderRoute(string name)
    {
        using var folder = new TemporaryFolder();
        var settings = new JsonSettingsService(NullLogger<JsonSettingsService>.Instance, folder.Path);
        await settings.LoadAsync(); var updated = settings.Current.Clone(); updated.Ai.BaseUrl = "https://provider.example.test/v1"; await settings.SaveAsync(updated);
        var input = name == "find_ui_element" ? "{\"query\":\"Save\"}" : "{}";
        var delta = JsonSerializer.Serialize(new { choices = new[] { new { delta = new { tool_calls = new[] {
            new { index = 0, id = "provider_call", type = "function", function = new { name, arguments = input } } } } } } });
        using var handler = new CaptureHandler("data: " + delta + "\n\ndata: [DONE]\n\n"); using var client = new HttpClient(handler);
        var credentials = new FakeCredentialStore(); credentials.Save(ProviderCredentialNames.ForBaseUrl(updated.Ai.BaseUrl), "fake-test-credential");
        var ui = new Part6Tests.FakeUi(); var router = Router(ui);
        var response = await new OpenAiCompatibleProvider(client, settings, credentials).CompleteAsync([new("user", "Observe controls")], router.Definitions);
        Check(response.ToolCalls.Count == 1 && response.ToolCalls[0].ArgumentsJson == input, "Provider call not assembled faithfully");
        Check((await router.InvokeAsync(response.ToolCalls[0])).Success, "Provider JSON rejected before read execution");
        using var body = JsonDocument.Parse(handler.Body!);
        foreach (var tool in body.RootElement.GetProperty("tools").EnumerateArray())
        {
            var f = tool.GetProperty("function"); var noArgs = f.GetProperty("name").GetString() is "get_foreground_window" or "list_open_windows";
            Check(f.GetProperty("strict").GetBoolean() == noArgs, "Optional schema was exposed as all-required strict JSON");
            Check(f.GetProperty("parameters").GetProperty("required").GetArrayLength() == (f.GetProperty("name").GetString() == "find_ui_element" ? 1 : 0), "Required fields differ from the search contract");
            Check(!f.GetProperty("parameters").TryGetProperty("anyOf", out _), "Provider-incompatible top-level union exposed");
        }
    }
    private static async Task Optional()
    {
        foreach (var name in UiTools.Names)
        {
            var shapes = name is "get_foreground_window" or "list_open_windows" ? new[] { "{}" } : name == "find_ui_element" ?
                new[] { "{\"query\":\"Save\"}", "{\"window_reference\":null,\"query\":\"Save\",\"control_type\":null}", "{\"query\":\"Save\",\"control_type\":\"Button\"}" } :
                new[] { "{}", "{\"window_reference\":null,\"query\":null,\"control_type\":null}", "{\"window_reference\":\"window:test\"}" };
            foreach (var json in shapes) Check((await Router(new()).InvokeAsync(new("provider", name, json))).Success, name + " optional shape rejected");
        }
    }
    private static async Task Invalid()
    {
        foreach (var name in UiTools.Names)
        {
            foreach (var json in new[] { "[]", "{\"window_handle\":\"123\"}", "{\"unknown\":true}", "{\"query\":false}", "{\"window_reference\":\"Calculator\"}", "{\"query\":null,\"query\":null}" })
            {
                var ui = new Part6Tests.FakeUi(); var result = await Router(ui).InvokeAsync(new("provider", name, json));
                Check(!result.Success && result.Status == "InvalidArguments" && ui.Reads == 0, "Invalid shape reached observation: " + name);
            }
        }
        foreach (var json in new[] { "{}", "{\"query\":null}", "{\"control_type\":\"Button\"}", "{\"query\":\"  \"}", JsonSerializer.Serialize(new { query = new string('x', 129) }) })
            Check(!(await Router(new()).InvokeAsync(new("provider", "find_ui_element", json))).Success, "Blank or oversized search accepted");
    }
    private static Task Hosted()
    {
        var resolver = new ApplicationResolver(new FakeExecutableLocator()); var definition = resolver.Resolve("Calculator")!;
        var calculator = new OpenWindowInfo(200, "Calculator", 10, "ApplicationFrameHost", new(0, 0, 400, 600), "monitor", ExternalWindowState.Normal, true)
            { HostedProcessId = 20, HostedProcessName = "CalculatorApp", ProcessStartTicks = 100 };
        var settings = calculator with { Handle = 100, Title = "Settings", HostedProcessId = 30, HostedProcessName = "SystemSettings" };
        Check(resolver.Matches(definition, calculator) && !resolver.Matches(definition, settings), "Shared owner process confused app identity");
        Check(!resolver.Matches(definition, calculator with { HostedProcessId = null, HostedProcessName = null }), "Unknown host selected by title alone");
        return Task.CompletedTask;
    }
    private static async Task PinnedFocus()
    {
        var calculator = new OpenWindowInfo(200, "Calculator", 10, "ApplicationFrameHost", new(0, 0, 400, 600), "monitor", ExternalWindowState.Normal, true)
            { HostedProcessId = 20, HostedProcessName = "CalculatorApp", ProcessStartTicks = 100 };
        var settings = calculator with { Handle = 100, Title = "Settings", HostedProcessId = 30, HostedProcessName = "SystemSettings" };
        var windows = new FakeExternalWindowService([settings, calculator]);
        var catalog = new ApplicationCatalog([], new MemoryApplicationCatalogRepository());
        var service = new WindowsApplicationControlService(new ApplicationResolver(new FakeExecutableLocator()), catalog, windows, NullLogger<WindowsApplicationControlService>.Instance);
        Check((await service.LaunchAsync("Calculator")).Success && windows.FocusedWindow?.Handle == calculator.Handle && windows.StringFocusCalls == 0,
            "Launch replaced the matched HWND with a generic process search");
        var ambiguous = new WindowsApplicationControlService(new ApplicationResolver(new FakeExecutableLocator()), catalog,
            new FakeExternalWindowService([calculator, calculator with { Handle = 300 }]), NullLogger<WindowsApplicationControlService>.Instance);
        Check(!(await ambiguous.LaunchAsync("Calculator")).Success, "Ambiguous Calculator windows selected arbitrarily");
    }
    private sealed class CaptureHandler(string response, System.Net.HttpStatusCode status = System.Net.HttpStatusCode.OK) : HttpMessageHandler
    {
        public string? Body;
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        { Body = await request.Content!.ReadAsStringAsync(ct); return new(status) { Content = new StringContent(response, System.Text.Encoding.UTF8, "text/event-stream") }; }
    }
    private static async Task ProviderSafeRejection()
    {
        using var folder = new TemporaryFolder(); var settings = new JsonSettingsService(NullLogger<JsonSettingsService>.Instance, folder.Path); await settings.LoadAsync();
        var updated = settings.Current.Clone(); updated.Ai.BaseUrl = "https://provider.example.test/v1"; await settings.SaveAsync(updated);
        var credentials = new FakeCredentialStore(); credentials.Save(ProviderCredentialNames.ForBaseUrl(updated.Ai.BaseUrl), "pretend-secret-never-log");
        var nested = JsonSerializer.Serialize(new { error = new { param = "tools[2].function.parameters", message = "Invalid schema for function find_ui_element: top level anyOf is not permitted. pretend-secret-never-log Private document title." } });
        using var client = new HttpClient(new CaptureHandler(JsonSerializer.Serialize(new { error = new { metadata = new { raw = nested } } }), System.Net.HttpStatusCode.BadRequest));
        var log = new ShapeLogger();
        try { await new OpenAiCompatibleProvider(client, settings, credentials, logger: log).CompleteAsync([new("user", "synthetic probe")], []); throw new Exception("Rejection ignored"); }
        catch (AiProviderException) { }
        var output = string.Join("\n", log.Entries);
        Check(output.Contains("find_ui_element") && output.Contains("anyOf") && output.Contains("not permitted") && output.Contains("tools[2].function.parameters") &&
            !output.Contains("pretend-secret-never-log") && !output.Contains("Private document title"), "Provider diagnostic leaked values or lost schema category");
    }
    private static async Task ApprovalTarget()
    {
        foreach (var reuse in new[] { false, true })
        {
            var probe = new Part62Tests.Probe(); var self = new Part62Tests.Self(); var tracker = new ActiveUserWindowTracker(probe, self); tracker.RefreshForeground();
            using var native = new WindowsUiAutomationService(new ApplicationCatalog([], new MemoryApplicationCatalogRepository()), new SecretProtection(),
                windowProbe: probe, selfRegistry: self, targetResolver: new WindowTargetResolver(probe, self, tracker));
            var approval = new CallbackApproval(() => { probe.ForegroundHandle = 3; tracker.RefreshForeground(); if (reuse) probe.Windows[1] = probe.Windows[1] with { ProcessId = 999 }; });
            var router = new ComputerAgentToolRouter(new AgentToolRegistry(UiTools.Create(native)), new MemoryPolicy(), approval,
                new ActionHistoryService(), new CharacterService(), NullLogger<ComputerAgentToolRouter>.Instance);
            var result = await router.InvokeAsync(new("provider", "get_foreground_window", "{}"));
            Check(reuse ? !result.Success && result.Status == "InvalidConfiguration" : result.Success && JsonSerializer.Serialize(result.Data).Contains("Calculator"),
                "No-argument approval lost its invocation target or accepted PID reuse");
        }
    }
    private sealed class CallbackApproval(Action action) : IAgentActionApprovalService
    {
        public Task<PermissionApproval> RequestAsync(AgentCapability capability, string target, CancellationToken ct = default)
        { action(); return Task.FromResult(PermissionApproval.AllowOnce); }
    }
}
