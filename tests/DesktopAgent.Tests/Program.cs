using DesktopAgent.Core.Enums;
using DesktopAgent.Core.Models;
using DesktopAgent.Core.Services;
using DesktopAgent.Core.Interfaces;
using DesktopAgent.Infrastructure.Settings;
using Microsoft.Extensions.Logging.Abstractions;
using DesktopAgent.Core.Automation.Enums;
using DesktopAgent.Core.Automation.Interfaces;
using DesktopAgent.Core.Automation.Models;
using DesktopAgent.Core.Automation.Services;
using DesktopAgent.Infrastructure.Windows.Automation;
using DesktopAgent.Core.Workspaces.Enums;
using DesktopAgent.Core.Workspaces.Interfaces;
using DesktopAgent.Core.Workspaces.Models;
using DesktopAgent.Core.Workspaces.Services;
using DesktopAgent.Infrastructure.Workspaces;
using DesktopAgent.Core.Applications.Interfaces;
using DesktopAgent.Core.Applications.Models;
using DesktopAgent.Core.Applications.Services;
using DesktopAgent.Core.Applications.Enums;
using DesktopAgent.Infrastructure.Applications;
using DesktopAgent.Core.Permissions;
using DesktopAgent.Infrastructure.Permissions;
using DesktopAgent.Core.Ai;
using DesktopAgent.Infrastructure.Ai;
using DesktopAgent.Infrastructure.Automation;

var tests = new (string Name, Func<Task> Run)[]
{
    ("settings round trip", SettingsRoundTrip),
    ("corrupted settings fallback", CorruptedSettingsFallback),
    ("position clamps to monitor", PositionClamps),
    ("missing monitor falls back to primary", MissingMonitorFallback),
    ("state transitions only notify changes", StateTransitions),
    ("parser opens application", ParserOpensApplication),
    ("parser parses window layout", ParserParsesLayout),
    ("parser parses URL", ParserParsesUrl),
    ("parser parses monitor movement", ParserParsesMonitor),
    ("parser rejects unknown command", ParserRejectsUnknown),
    ("application aliases resolve", ApplicationAliasesResolve),
    ("URL validation permits only web URLs", UrlValidation),
    ("path normalization expands full path", PathNormalization),
    ("layout calculations respect work area", LayoutCalculations),
    ("center layout remains visible", CenterLayout),
    ("structured results preserve status", StructuredResult),
    ("workspace command parsing", WorkspaceCommandParsing),
    ("workspace serialization round trip", WorkspaceSerializationRoundTrip),
    ("corrupted workspace fallback", CorruptedWorkspaceFallback),
    ("workspace validation", WorkspaceValidation),
    ("workspace CRUD and duplication", WorkspaceCrud),
    ("workspace launch preserves order", WorkspaceLaunchOrder),
    ("missing workspace monitor falls back", WorkspaceMonitorFallback),
    ("workspace partial result", WorkspacePartialResult),
    ("workspace cancellation", WorkspaceCancellation),
    ("workspace rejects invalid saved configuration", WorkspaceRejectsInvalidSavedConfiguration),
    ("workspace recovers a stale catalog identity", WorkspaceRecoversStaleCatalogIdentity),
    ("new workspace launch keeps cancellation available", WorkspaceRelaunchCancellation),
    ("application name normalization", ApplicationNormalization),
    ("application exact and alias matching", ApplicationExactAndAliasMatching),
    ("discovered Edge and VS Code resolve by common names", DiscoveredCommonAliasesResolve),
    ("application prefix and fuzzy ranking", ApplicationPrefixAndFuzzyRanking),
    ("application ambiguity", ApplicationAmbiguity),
    ("application deduplication", ApplicationDeduplication),
    ("application catalog serialization", ApplicationCatalogSerialization),
    ("invalid application cache fallback", InvalidApplicationCache),
    ("stable application identity", StableApplicationIdentity),
    ("application launch strategy selection", ApplicationLaunchStrategySelection),
    ("system application filtering", SystemApplicationFiltering),
    ("uninstaller filtering", UninstallerFiltering),
    ("application catalog commands", ApplicationCatalogCommands),
    ("application catalog starts from cache without scanning", ApplicationCatalogUsesCacheAtStartup),
    ("failed application scan preserves cache", FailedApplicationScanPreservesCache),
    ("permission defaults and policy outcomes", PermissionDefaultsAndOutcomes)
    ,("permission policies persist and reset", PermissionPoliciesPersistAndReset)
    ,("computer commands map to explicit capabilities", CommandCapabilitiesMap)
    ,("future capabilities stay unavailable", FutureCapabilitiesStayUnavailable)
    ,("computer tools validate inputs and call typed services", ComputerToolsValidateAndRoute)
    ,("unknown computer tools fail closed", UnknownComputerToolFailsClosed)
    ,("AI actions stop at denied permission", AiActionsRespectDeniedPermission)
    ,("AI provider parses streamed text and structured tools", AiProviderParsesStream)
    ,("AI provider fetches provider models", AiProviderFetchesModels)
    ,("provider credentials are scoped to API URL", ProviderCredentialsAreScoped)
    ,("cloud AI can be disabled without disabling direct commands", CloudAiCanBeDisabled)
    ,("activity history persists recent entries", ActivityHistoryPersists)
}.Concat(Part2Tests.Cases).Concat(Part3Tests.Cases).Concat(Part4Tests.Cases).Concat(Part5Tests.Cases).Concat(Part6Tests.Cases).Concat(Part62Tests.Cases).Concat(Part7Tests.Cases).Concat(Part71Tests.Cases).ToArray();

if (Array.IndexOf(args, "--python-worker-health") is var pythonHealthIndex && pythonHealthIndex >= 0 && pythonHealthIndex + 1 < args.Length)
    return await Part72Diagnostics.Run(args[pythonHealthIndex + 1], false);
if (Array.IndexOf(args, "--python-worker-smoke") is var pythonSmokeIndex && pythonSmokeIndex >= 0 && pythonSmokeIndex + 1 < args.Length)
    return await Part72Diagnostics.Run(args[pythonSmokeIndex + 1], true);
if (args.Contains("--provider-allowance-diagnostic", StringComparer.Ordinal)) return await Part71Tests.ProviderAllowanceDiagnostic();
if (Array.IndexOf(args, "--ui-live") is var uiLiveIndex && uiLiveIndex >= 0 && uiLiveIndex + 1 < args.Length)
    return await UiLiveDiagnostics.Run(args[uiLiveIndex + 1]);
if (Array.IndexOf(args, "--windows-diagnostics") is var diagIndex && diagIndex >= 0 && diagIndex + 1 < args.Length)
    return await UiLiveDiagnostics.Raw(args[diagIndex + 1]);
var failures = 0;
if (Array.IndexOf(args, "--filter") is var filterIndex && filterIndex >= 0 && filterIndex + 1 < args.Length)
    tests = tests.Where(t => t.Name.Contains(args[filterIndex + 1], StringComparison.OrdinalIgnoreCase)).ToArray();
foreach (var test in tests)
{
    try { await test.Run(); Console.WriteLine($"PASS {test.Name}"); }
    catch (Exception exception)
    {
        if (exception.Message.Contains("Application Control policy", StringComparison.OrdinalIgnoreCase))
        {
            Console.Error.WriteLine("BLOCKED: Windows Application Control prevented the unsigned test assemblies from loading. Tests were not executed.");
            return 2;
        }
        failures++; Console.Error.WriteLine($"FAIL {test.Name}: {exception.Message}");
    }
}
Console.WriteLine($"{tests.Length - failures}/{tests.Length} tests passed.");
if (failures == 0 && args.Contains("--integration", StringComparer.OrdinalIgnoreCase))
    failures += await RunWindowsIntegrationTests(args.Contains("--windows-only", StringComparer.OrdinalIgnoreCase));
if (failures == 0 && args.Contains("--catalog-probe", StringComparer.OrdinalIgnoreCase))
    failures += await RunCatalogProbe();
return failures == 0 ? 0 : 1;

static async Task SettingsRoundTrip()
{
    using var folder = new TemporaryFolder();
    var service = new JsonSettingsService(NullLogger<JsonSettingsService>.Instance, folder.Path);
    var expected = new AppSettings
    {
        AlwaysOnTop = false,
        CharacterSize = 192,
        Theme = AppTheme.Dark,
        Position = new CharacterPosition(120, 240, "DISPLAY2")
        ,OnboardingCompleted = true
        ,OnboardingVersion = 1
        ,Ai = new AiProviderSettings { Model = "test-model", BaseUrl = "https://api.example.test/v1" }
    };
    await service.SaveAsync(expected);
    var second = new JsonSettingsService(NullLogger<JsonSettingsService>.Instance, folder.Path);
    var actual = await second.LoadAsync();
    Assert(actual.CharacterSize == 192 && actual.Theme == AppTheme.Dark && actual.Position?.X == 120
        && actual.OnboardingCompleted && actual.OnboardingVersion == 1 && actual.Ai.Model == "test-model", "Round-trip values differ.");
}

static async Task PermissionDefaultsAndOutcomes()
{
    using var folder = new TemporaryFolder();
    var policy = new JsonPolicyService(NullLogger<JsonPolicyService>.Instance, folder.Path);
    await policy.InitializeAsync();
    Assert(policy.GetPolicy(CapabilityIds.ApplicationClose) == PermissionPolicy.Ask, "Closing applications should default to Ask.");
    Assert(policy.Evaluate(CapabilityIds.ApplicationClose).Outcome == PolicyOutcome.ConfirmationRequired, "Ask did not require confirmation.");
    await policy.SetPolicyAsync(CapabilityIds.ApplicationOpen, PermissionPolicy.Deny);
    Assert(policy.Evaluate(CapabilityIds.ApplicationOpen).Outcome == PolicyOutcome.Denied, "Deny policy did not block the capability.");
}

static async Task PermissionPoliciesPersistAndReset()
{
    using var folder = new TemporaryFolder();
    var first = new JsonPolicyService(NullLogger<JsonPolicyService>.Instance, folder.Path);
    await first.InitializeAsync();
    await first.SetPolicyAsync(CapabilityIds.WorkspaceLaunch, PermissionPolicy.Deny);
    var second = new JsonPolicyService(NullLogger<JsonPolicyService>.Instance, folder.Path);
    await second.InitializeAsync();
    Assert(second.GetPolicy(CapabilityIds.WorkspaceLaunch) == PermissionPolicy.Deny, "Configured policy did not persist.");
    await second.ResetAsync();
    Assert(second.GetPolicy(CapabilityIds.WorkspaceLaunch) == PermissionPolicy.Allow, "Reset did not restore the safe default.");
}

static Task CommandCapabilitiesMap()
{
    var required = ComputerCommandCapabilities.RequiredFor(ComputerCommandType.LaunchWorkspace);
    Assert(required.SequenceEqual(new[] { CapabilityIds.WorkspaceView, CapabilityIds.WorkspaceLaunch }), "Workspace launch is not mapped to explicit capabilities.");
    Assert(ComputerCommandCapabilities.RequiredFor(ComputerCommandType.CloseApplication).Single() == CapabilityIds.ApplicationClose, "Close app capability mapping is missing.");
    return Task.CompletedTask;
}

static async Task FutureCapabilitiesStayUnavailable()
{
    using var folder = new TemporaryFolder();
    var policy = new JsonPolicyService(NullLogger<JsonPolicyService>.Instance, folder.Path);
    await policy.InitializeAsync();
    Assert(policy.Evaluate(CapabilityIds.ScreenCapture).Outcome == PolicyOutcome.Unavailable, "Screen capture must remain unavailable.");
    Assert(policy.Evaluate(CapabilityIds.DeveloperTerminal).Outcome == PolicyOutcome.Unavailable, "Terminal execution must remain unavailable.");
}

static async Task ComputerToolsValidateAndRoute()
{
    var controller = new RecordingController();
    var catalog = await CreateCatalog(new InstalledApplication { DisplayName = "Calculator", ExecutablePath = @"C:\Apps\Calculator.exe", Sources = ["Test"] });
    var router = Part2Tests.Router(catalog, controller);
    var call = new AiToolCall("call-1", "open_application", "{\"application\":\"Calculator\"}");
    var result = await router.InvokeAsync(call);
    Assert(controller.Commands.Single().Type == ComputerCommandType.OpenApplication && controller.Commands.Single().Target == catalog.Applications.Single().Id, "AI tool did not call the typed controller with catalog identity.");
    Assert(result.Success, "Real tool result was not returned.");
    Assert(router.Definitions.Count >= 10 && router.Definitions.All(tool => tool.Parameters.ValueKind == System.Text.Json.JsonValueKind.Object), "Structured tool schemas were not exposed.");
}

static async Task UnknownComputerToolFailsClosed()
{
    var router = Part2Tests.Router(await CreateCatalog(), new RecordingController());
    var unknown = await router.InvokeAsync(new AiToolCall("call-2", "execute_anything", "{}"));
    Assert(unknown.Status == "UnknownTool", "Unknown tool request was accepted.");
    var invalid = await router.InvokeAsync(new AiToolCall("call-3", "open_application", "{\"application\":\"Calculator\",\"extra\":true}"));
    Assert(invalid.Status == "InvalidArguments", "Unexpected tool argument was accepted.");
}

static async Task AiActionsRespectDeniedPermission()
{
    using var folder = new TemporaryFolder();
    var policy = new JsonPolicyService(NullLogger<JsonPolicyService>.Instance, folder.Path);
    await policy.InitializeAsync();
    var controller = new FakeComputerController();
    var approval = new FakeAgentActionApprovalService(PermissionApproval.Deny);
    var service = new ComputerCommandService(new ComputerCommandParser(), controller, new ActionHistoryService(), new CharacterService(),
        new FakeWorkspaceService(), policy, approval, NullLogger<ComputerCommandService>.Instance);
    var denied = await service.ExecuteAsync("close chrome", source: "AI");
    Assert(denied.Status == ActionStatus.PermissionDenied && controller.Calls == 0 && approval.Calls == 1,
        "AI action did not stop for the Ask policy confirmation.");
    var direct = await service.ExecuteAsync("close chrome", source: "Text");
    Assert(direct.Success && controller.Calls == 1, "Direct user commands were unexpectedly changed by agent policy.");
}

static async Task AiProviderParsesStream()
{
    using var folder = new TemporaryFolder();
    var settings = new JsonSettingsService(NullLogger<JsonSettingsService>.Instance, folder.Path);
    await settings.LoadAsync();
    var updated = settings.Current.Clone();
    updated.Ai.BaseUrl = "https://provider.example.test/v1";
    await settings.SaveAsync(updated);
    var payload = """
        data: {"choices":[{"delta":{"content":"Opening"}}]}

        data: {"choices":[{"delta":{"tool_calls":[{"index":0,"id":"call_1","type":"function","function":{"name":"open_application","arguments":"{\"application\":"}}]}}]}

        data: {"choices":[{"delta":{"tool_calls":[{"index":0,"function":{"arguments":"\"Calculator\"}"}}]}}]}

        data: [DONE]

        """;
    var handler = new FakeHttpMessageHandler(payload);
    using var client = new HttpClient(handler);
    var credentials = new FakeCredentialStore();
    credentials.Save(ProviderCredentialNames.ForBaseUrl(settings.Current.Ai.BaseUrl), "test-secret");
    var provider = new OpenAiCompatibleProvider(client, settings, credentials);
    var deltas = new List<string>();
    var response = await provider.CompleteAsync([new AiMessage("user", "Open calculator")], [], new InlineProgress<string>(deltas.Add));
    Assert(response.Text == "Opening" && response.ToolCalls.Count == 1, "Streamed text or tool call was not parsed.");
    Assert(response.ToolCalls[0].Name == "open_application" && response.ToolCalls[0].ArgumentsJson == "{\"application\":\"Calculator\"}", "Partial tool arguments were not assembled.");
    Assert(string.Concat(deltas) == "Opening" && handler.LastRequestUri == "https://provider.example.test/v1/chat/completions", "Streaming progress or endpoint is incorrect.");
}

static async Task AiProviderFetchesModels()
{
    using var folder = new TemporaryFolder();
    var settings = new JsonSettingsService(NullLogger<JsonSettingsService>.Instance, folder.Path);
    await settings.LoadAsync();
    var updated = settings.Current.Clone();
    updated.Ai.BaseUrl = "https://openrouter.ai/api/v1";
    await settings.SaveAsync(updated);
    var handler = new FakeHttpMessageHandler("""{"data":[{"id":"z-ai/glm-4.7"},{"id":"openai/gpt-4o-mini"}]}""");
    using var client = new HttpClient(handler);
    var credentials = new FakeCredentialStore();
    credentials.Save(ProviderCredentialNames.ForBaseUrl(settings.Current.Ai.BaseUrl), "test-secret");
    var provider = new OpenAiCompatibleProvider(client, settings, credentials);
    var models = await provider.GetModelsAsync();
    Assert(models.SequenceEqual(["openai/gpt-4o-mini", "z-ai/glm-4.7"]), "Provider models were not parsed and sorted.");
    Assert(handler.LastRequestUri == "https://openrouter.ai/api/v1/models", "Provider model discovery used the wrong API endpoint.");
}

static Task ProviderCredentialsAreScoped()
{
    var credentials = new FakeCredentialStore();
    credentials.Save(ProviderCredentialNames.LegacyName, "legacy-secret");
    ProviderCredentialNames.MigrateLegacy(credentials, "https://api.openai.com/v1");
    Assert(credentials.Read(ProviderCredentialNames.LegacyName) is null &&
        credentials.Read(ProviderCredentialNames.ForBaseUrl("https://api.openai.com/v1")) == "legacy-secret" &&
        credentials.Read(ProviderCredentialNames.ForBaseUrl("https://openrouter.ai/api/v1")) is null,
        "The saved provider key could be reused against a different API endpoint.");
    return Task.CompletedTask;
}

static async Task CloudAiCanBeDisabled()
{
    using var folder = new TemporaryFolder();
    var settings = new JsonSettingsService(NullLogger<JsonSettingsService>.Instance, folder.Path);
    await settings.LoadAsync();
    var changed = settings.Current.Clone();
    changed.Ai.Enabled = false;
    await settings.SaveAsync(changed);
    using var client = new HttpClient(new FakeHttpMessageHandler(string.Empty));
    var provider = new OpenAiCompatibleProvider(client, settings, new FakeCredentialStore());
    var commands = new FakeComputerCommandService();
    var assistant = new AssistantConversationService(provider, Part2Tests.Router(await CreateCatalog(), new RecordingController()),
        new ComputerCommandParser(), commands, new ActionHistoryService(), settings,
        NullLogger<AssistantConversationService>.Instance);
    var natural = await assistant.SendAsync("Plan my day");
    var direct = await assistant.SendAsync("help");
    Assert(!natural.UsedAi && natural.Text.Contains("turned off") && !direct.UsedAi && commands.Inputs.Single() == "help",
        "Disabling cloud AI also disabled the deterministic command path.");
}

static async Task ActivityHistoryPersists()
{
    using var folder = new TemporaryFolder();
    var first = new JsonActionHistoryService(NullLogger<JsonActionHistoryService>.Instance, folder.Path);
    await first.InitializeAsync();
    first.Add(new ActionHistoryEntry(DateTimeOffset.Now, ComputerCommandType.OpenApplication, "Calculator", ActionStatus.Success, "Opened", "AI"));
    var second = new JsonActionHistoryService(NullLogger<JsonActionHistoryService>.Instance, folder.Path);
    await second.InitializeAsync();
    Assert(second.GetRecent().Single().Source == "AI" && second.GetRecent().Single().Target == "Calculator", "Activity entry did not persist.");
}

static async Task CorruptedSettingsFallback()
{
    using var folder = new TemporaryFolder();
    await File.WriteAllTextAsync(Path.Combine(folder.Path, "settings.json"), "{ definitely not json");
    var service = new JsonSettingsService(NullLogger<JsonSettingsService>.Instance, folder.Path);
    var settings = await service.LoadAsync();
    Assert(settings.CharacterSize == AppSettings.DefaultCharacterSize && settings.AlwaysOnTop, "Defaults were not restored.");
}

static Task PositionClamps()
{
    var screens = new[] { new MonitorWorkArea("PRIMARY", 0, 0, 1920, 1040, true) };
    var result = PositionValidator.Validate(new CharacterPosition(4000, -500, "PRIMARY"), 150, 150, screens);
    Assert(result.X == 1762 && result.Y == 8, $"Unexpected clamped point: {result}");
    return Task.CompletedTask;
}

static Task MissingMonitorFallback()
{
    var screens = new[]
    {
        new MonitorWorkArea("PRIMARY", 0, 0, 1920, 1040, true),
        new MonitorWorkArea("SECONDARY", -1280, 0, 1280, 984, false)
    };
    var result = PositionValidator.Validate(new CharacterPosition(9000, 9000, "REMOVED"), 100, 100, screens);
    Assert(result.MonitorDeviceName == "PRIMARY" && result.X <= 1812 && result.Y <= 932, "Did not recover to primary screen.");
    return Task.CompletedTask;
}

static Task StateTransitions()
{
    var service = new CharacterService();
    var changes = 0;
    service.StateChanged += (_, _) => changes++;
    service.SetState(CharacterState.Working);
    service.SetState(CharacterState.Working);
    service.SetState(CharacterState.Thinking);
    Assert(service.State == CharacterState.Thinking && changes == 2, "State transition behavior is invalid.");
    return Task.CompletedTask;
}

static Task ParserOpensApplication()
{
    var command = new ComputerCommandParser().Parse("OPEN Chrome");
    Assert(command.Type == ComputerCommandType.OpenApplication && command.Target == "Chrome", "Open command parsed incorrectly.");
    return Task.CompletedTask;
}

static Task ParserParsesLayout()
{
    var command = new ComputerCommandParser().Parse("move visual studio code top-left");
    Assert(command.Type == ComputerCommandType.ApplyLayout && command.Target == "visual studio code" && command.Layout == WindowLayout.TopLeft,
        "Layout command parsed incorrectly.");
    return Task.CompletedTask;
}

static Task ParserParsesUrl()
{
    var command = new ComputerCommandParser().Parse("open url https://github.com/openai");
    Assert(command.Type == ComputerCommandType.OpenUrl && command.Target == "https://github.com/openai", "URL command parsed incorrectly.");
    return Task.CompletedTask;
}

static Task ParserParsesMonitor()
{
    var command = new ComputerCommandParser().Parse("move chrome monitor 2");
    Assert(command.Type == ComputerCommandType.MoveToMonitor && command.Target == "chrome" && command.MonitorNumber == 2,
        "Monitor command parsed incorrectly.");
    return Task.CompletedTask;
}

static Task ParserRejectsUnknown()
{
    var command = new ComputerCommandParser().Parse("please do everything");
    Assert(command.Type == ComputerCommandType.Invalid && !string.IsNullOrWhiteSpace(command.ErrorMessage), "Invalid command was accepted.");
    return Task.CompletedTask;
}

static Task ApplicationAliasesResolve()
{
    var resolver = new ApplicationResolver(new FakeExecutableLocator());
    var first = resolver.Resolve("VS Code");
    var second = resolver.Resolve("google chrome");
    Assert(first?.ProcessName == "code" && first.Executable == "C:\\Tools\\code.exe", "VS Code alias did not resolve.");
    Assert(second?.ProcessName == "chrome", "Chrome alias did not resolve.");
    return Task.CompletedTask;
}

static Task UrlValidation()
{
    Assert(WindowsShellService.TryValidateWebUri("https://github.com", out _), "HTTPS URL was rejected.");
    Assert(WindowsShellService.TryValidateWebUri("http://example.com/path", out _), "HTTP URL was rejected.");
    Assert(!WindowsShellService.TryValidateWebUri("file:///C:/Windows", out _), "File URI was accepted.");
    Assert(!WindowsShellService.TryValidateWebUri("javascript:alert(1)", out _), "Script URI was accepted.");
    return Task.CompletedTask;
}

static Task PathNormalization()
{
    var normalized = WindowsShellService.NormalizePath(".");
    Assert(normalized is not null && Path.IsPathFullyQualified(normalized), "Path was not normalized.");
    Assert(WindowsShellService.NormalizePath(string.Empty) is null, "Empty path was accepted.");
    return Task.CompletedTask;
}

static Task LayoutCalculations()
{
    var work = new WindowRectangle(10, 20, 1900, 1000);
    var current = new WindowRectangle(100, 100, 800, 600);
    var right = WindowLayoutCalculator.Calculate(WindowLayout.Right, work, current);
    var bottomLeft = WindowLayoutCalculator.Calculate(WindowLayout.BottomLeft, work, current);
    Assert(right == new WindowRectangle(960, 20, 950, 1000), $"Right layout was {right}.");
    Assert(bottomLeft == new WindowRectangle(10, 520, 950, 500), $"Bottom-left layout was {bottomLeft}.");
    return Task.CompletedTask;
}

static Task CenterLayout()
{
    var work = new WindowRectangle(-1280, 0, 1280, 984);
    var current = new WindowRectangle(0, 0, 2000, 1200);
    var centered = WindowLayoutCalculator.Calculate(WindowLayout.Center, work, current);
    Assert(centered == work, $"Oversized centered window was not constrained: {centered}");
    return Task.CompletedTask;
}

static Task StructuredResult()
{
    var result = ComputerActionResult.Fail(ActionStatus.PermissionDenied, "Denied", "admin app", "Win32:5");
    Assert(!result.Success && result.Status == ActionStatus.PermissionDenied && result.ErrorCode == "Win32:5" &&
        result.Timestamp <= DateTimeOffset.Now, "Structured result fields were not preserved.");
    return Task.CompletedTask;
}

static Task WorkspaceCommandParsing()
{
    var parser = new ComputerCommandParser();
    Assert(parser.Parse("workspaces").Type == ComputerCommandType.ListWorkspaces, "Workspace listing was not parsed.");
    var launch = parser.Parse("launch workspace Morning Focus");
    var stop = parser.Parse("stop workspace Morning Focus");
    Assert(launch.Type == ComputerCommandType.LaunchWorkspace && launch.Target == "Morning Focus", "Workspace launch was not parsed.");
    Assert(stop.Type == ComputerCommandType.StopWorkspace && stop.Target == "Morning Focus", "Workspace stop was not parsed.");
    return Task.CompletedTask;
}

static async Task WorkspaceSerializationRoundTrip()
{
    using var folder = new TemporaryFolder();
    var repository = new JsonWorkspaceRepository(NullLogger<JsonWorkspaceRepository>.Instance, folder.Path);
    var source = new WorkspaceDefinition
    {
        Name = "Morning Focus",
        Description = "Daily setup",
        Applications = [new() { DisplayName = "Code", ApplicationIdentifier = "visual studio code", CatalogApplicationId = "exe:c:\\tools\\code.exe", Layout = WindowLayout.Left, TargetMonitor = 2 }],
        Urls = [new() { DisplayName = "Example", Value = "https://example.com" }],
        PreLaunchActions = [new() { Type = WorkspaceActionType.Minimize, Target = "chrome" }]
    };
    await repository.SaveAsync([source]);
    var loaded = await repository.LoadAsync();
    Assert(loaded.Count == 1 && loaded[0].Name == source.Name && loaded[0].Applications[0].Layout == WindowLayout.Left && loaded[0].Applications[0].CatalogApplicationId == "exe:c:\\tools\\code.exe" &&
        loaded[0].PreLaunchActions[0].Type == WorkspaceActionType.Minimize, "Workspace JSON round trip changed values.");
}

static async Task CorruptedWorkspaceFallback()
{
    using var folder = new TemporaryFolder();
    await File.WriteAllTextAsync(Path.Combine(folder.Path, "workspaces.json"), "{ invalid workspace data");
    var repository = new JsonWorkspaceRepository(NullLogger<JsonWorkspaceRepository>.Instance, folder.Path);
    Assert((await repository.LoadAsync()).Count == 0, "Corrupt workspace data did not fall back to an empty collection.");
}

static Task WorkspaceValidation()
{
    var validator = new WorkspaceValidator();
    var existing = new WorkspaceDefinition { Name = "Focus" };
    var invalid = new WorkspaceDefinition
    {
        Name = "Focus",
        LaunchTimeoutSeconds = 1,
        Applications = [new() { ApplicationIdentifier = "chrome", DelayAfterLaunchMs = -1 }, new() { ApplicationIdentifier = "Chrome" }],
        Urls = [new() { Value = "file:///C:/Windows" }]
    };
    var result = validator.Validate(invalid, [existing]);
    Assert(!result.IsValid && result.Issues.Any(item => item.Field == "Name") && result.Issues.Any(item => item.Field == "URLs") &&
        result.Issues.Any(item => item.Field == "Applications"), "Workspace validation missed invalid or duplicate values.");
    return Task.CompletedTask;
}

static async Task WorkspaceCrud()
{
    var repository = new MemoryWorkspaceRepository();
    var service = new WorkspaceService(repository, new WorkspaceValidator(), new FakeWorkspaceLaunchEngine(), NullLogger<WorkspaceService>.Instance);
    var item = new WorkspaceDefinition { Name = "Development" };
    Assert((await service.SaveAsync(item)).IsValid && service.Workspaces.Count == 1, "Workspace create failed.");
    var saved = service.Workspaces[0];
    saved.Description = "Updated";
    Assert((await service.SaveAsync(saved)).IsValid && service.Workspaces[0].Description == "Updated", "Workspace update failed.");
    var copy = await service.DuplicateAsync(saved.Id);
    Assert(copy is not null && copy.Id != saved.Id && service.Workspaces.Count == 2 && copy.Name != saved.Name, "Workspace duplication failed.");
    Assert(await service.DeleteAsync(saved.Id) && service.Workspaces.Count == 1, "Workspace delete failed.");
}

static async Task WorkspaceLaunchOrder()
{
    var controller = new RecordingController();
    var engine = CreateLaunchEngine(controller);
    var workspace = new WorkspaceDefinition
    {
        Name = "Ordered",
        Applications =
        [
            new() { ApplicationIdentifier = "second", LaunchOrder = 2 },
            new() { ApplicationIdentifier = "first", LaunchOrder = 1 }
        ],
        Folders = [new() { Value = Environment.CurrentDirectory, Order = 2 }],
        Urls = [new() { Value = "https://example.com", Order = 1 }]
    };
    var result = await engine.LaunchAsync(workspace);
    Assert(result.Success, "Ordered workspace failed unexpectedly.");
    Assert(controller.Commands.Select(item => item.Target).SequenceEqual(new[] { "first", "second", "https://example.com", Environment.CurrentDirectory }), "Workspace launch order was not preserved.");
}

static async Task WorkspaceMonitorFallback()
{
    var controller = new RecordingController(command => command.Type == ComputerCommandType.MoveToMonitor && command.MonitorNumber == 9
        ? ComputerActionResult.Fail(ActionStatus.NotFound, "Monitor missing", command.Target)
        : ComputerActionResult.Ok("Done", command.Target));
    var engine = CreateLaunchEngine(controller);
    var workspace = new WorkspaceDefinition { Name = "Fallback", Applications = [new() { ApplicationIdentifier = "chrome", TargetMonitor = 9 }] };
    var result = await engine.LaunchAsync(workspace);
    Assert(result.Session.Status == WorkspaceLaunchStatus.Partial &&
        controller.Commands.Count(item => item.Type == ComputerCommandType.MoveToMonitor) == 1 &&
        controller.Commands.Last().MonitorNumber == 1 &&
        result.Session.Results.Any(step => !step.Success && step.Message.Contains("unavailable")),
        "Missing monitor did not report the issue and fall back to primary.");
}

static async Task WorkspacePartialResult()
{
    var controller = new RecordingController(command => command.Target == "missing"
        ? ComputerActionResult.Fail(ActionStatus.NotFound, "Missing", command.Target)
        : ComputerActionResult.Ok("Done", command.Target));
    var engine = CreateLaunchEngine(controller);
    var workspace = new WorkspaceDefinition { Name = "Partial", ContinueOnFailure = true, Applications = [new() { ApplicationIdentifier = "missing" }, new() { ApplicationIdentifier = "working" }] };
    var result = await engine.LaunchAsync(workspace);
    Assert(result.Session.Status == WorkspaceLaunchStatus.Partial && result.Session.Results.Count == 2, "Partial workspace status was not reported.");
}

static async Task WorkspaceCancellation()
{
    var controller = new RecordingController(async (_, token) => { await Task.Delay(Timeout.Infinite, token); return ComputerActionResult.Ok("Unexpected"); });
    var engine = CreateLaunchEngine(controller);
    using var cancellation = new CancellationTokenSource(80);
    var result = await engine.LaunchAsync(new WorkspaceDefinition { Name = "Cancel", Applications = [new() { ApplicationIdentifier = "slow" }] }, cancellation.Token);
    Assert(result.Session.Status == WorkspaceLaunchStatus.Cancelled, "Cancellation was not reflected in the workspace session.");
}

static async Task WorkspaceRejectsInvalidSavedConfiguration()
{
    var repository = new MemoryWorkspaceRepository();
    var workspace = new WorkspaceDefinition { Name = "Broken", Urls = [new() { Value = "file:///not-allowed" }] };
    await repository.SaveAsync([workspace]);
    var engine = new FakeWorkspaceLaunchEngine();
    var service = new WorkspaceService(repository, new WorkspaceValidator(), engine, NullLogger<WorkspaceService>.Instance);
    var result = await service.LaunchAsync(workspace.Id);
    Assert(result?.Session.Status == WorkspaceLaunchStatus.InvalidConfiguration && engine.Calls == 0,
        "Malformed saved workspace was executed instead of being rejected before launch.");
}

static async Task WorkspaceRecoversStaleCatalogIdentity()
{
    var controller = new RecordingController(command => command.Target == "old-app-id"
        ? ComputerActionResult.Fail(ActionStatus.NotFound, "Catalog entry no longer exists", command.Target)
        : ComputerActionResult.Ok("Opened", command.Target));
    var workspace = new WorkspaceDefinition { Name = "Recovered", Applications = [new()
        { ApplicationIdentifier = "Chrome", CatalogApplicationId = "old-app-id" }] };
    var result = await CreateLaunchEngine(controller).LaunchAsync(workspace);
    Assert(result.Session.Status == WorkspaceLaunchStatus.Ready &&
        controller.Commands.Select(command => command.Target).SequenceEqual(["old-app-id", "Chrome"]),
        "Workspace did not retry a stale catalog identity by its saved application name.");
}

static async Task WorkspaceRelaunchCancellation()
{
    var repository = new MemoryWorkspaceRepository();
    var workspace = new WorkspaceDefinition { Name = "Concurrent" };
    await repository.SaveAsync([workspace]);
    var engine = new BlockingWorkspaceLaunchEngine();
    var service = new WorkspaceService(repository, new WorkspaceValidator(), engine, NullLogger<WorkspaceService>.Instance);
    var first = service.LaunchAsync(workspace.Id);
    var second = service.LaunchAsync(workspace.Id);
    await engine.SecondStarted.WaitAsync(TimeSpan.FromSeconds(2));
    await first;
    Assert(service.Cancel(workspace.Id), "Completing an older launch removed the active launch cancellation token.");
    Assert((await second)?.Session.Status == WorkspaceLaunchStatus.Cancelled, "Cancelling the replacement launch failed.");
}

static Task ApplicationNormalization()
{
    Assert(ApplicationNameNormalizer.Normalize("  Visual-Studio   CODE! ") == "visual studio code", "Application name normalization failed.");
    Assert(ApplicationNameNormalizer.Normalize("Spotify App") == "spotify", "Application suffix normalization failed.");
    return Task.CompletedTask;
}

static async Task ApplicationExactAndAliasMatching()
{
    var catalog = await CreateCatalog(new InstalledApplication { DisplayName = "Visual Studio Code", Aliases = ["VS Code", "vscode"], ExecutablePath = @"C:\Apps\Code.exe", ProcessName = "code", Sources = ["Test"] });
    Assert(catalog.Resolve("Visual Studio Code").Status == ApplicationResolutionStatus.Found, "Exact application match failed.");
    Assert(catalog.Resolve("vscode").Application?.DisplayName == "Visual Studio Code", "Alias application match failed.");
}

static async Task DiscoveredCommonAliasesResolve()
{
    var catalog = await CreateCatalog(
        new InstalledApplication { DisplayName = "Microsoft Edge", ExecutablePath = @"C:\Apps\msedge.exe", ProcessName = "msedge", Sources = ["Test"] },
        new InstalledApplication { DisplayName = "Microsoft Edge WebView2 Runtime", ExecutablePath = @"C:\Apps\msedgewebview2.exe", ProcessName = "msedgewebview2", Sources = ["Test"] },
        new InstalledApplication { DisplayName = "Visual Studio Code", ExecutablePath = @"C:\Apps\Code.exe", ProcessName = "Code", Sources = ["Test"] });
    Assert(catalog.Resolve("edge").Application?.DisplayName == "Microsoft Edge", "Edge selected a related runtime instead of the browser.");
    Assert(catalog.Resolve("vs code").Application?.DisplayName == "Visual Studio Code", "VS Code did not match the discovered application.");
}

static async Task ApplicationPrefixAndFuzzyRanking()
{
    var catalog = await CreateCatalog(new InstalledApplication { DisplayName = "Spotify", ExecutablePath = @"C:\Apps\Spotify.exe", ProcessName = "spotify", Sources = ["Test"] });
    Assert(catalog.SearchApplications("spot")[0].MatchReason == "Prefix", "Prefix ranking failed.");
    Assert(catalog.SearchApplications("spotfy").FirstOrDefault()?.Application.DisplayName == "Spotify", "Conservative fuzzy ranking failed.");
}

static async Task ApplicationAmbiguity()
{
    var catalog = await CreateCatalog(
        new InstalledApplication { DisplayName = "Visual Studio Code", ExecutablePath = @"C:\Apps\Code.exe", ProcessName = "code", Sources = ["Test"] },
        new InstalledApplication { DisplayName = "Visual Studio 2022", ExecutablePath = @"C:\Apps\VS.exe", ProcessName = "devenv", Sources = ["Test"] },
        new InstalledApplication { DisplayName = "Visual Studio Installer", ExecutablePath = @"C:\Apps\InstallerHub.exe", ProcessName = "vsinstaller", Sources = ["Test"] });
    var result = catalog.Resolve("visual");
    Assert(result.Status == ApplicationResolutionStatus.Ambiguous && result.Matches.Count == 3, "Ambiguous application query selected an arbitrary result.");
}

static Task ApplicationDeduplication()
{
    var items = ApplicationCatalog.Deduplicate([
        new InstalledApplication { DisplayName = "Google Chrome", ExecutablePath = @"C:\Apps\chrome.exe", ProcessName = "chrome", ApplicationType = InstalledApplicationType.Win32, Sources = ["Registry"] },
        new InstalledApplication { DisplayName = "Chrome", LaunchIdentifier = @"C:\Menu\Chrome.lnk", ExecutablePath = @"C:\Apps\chrome.exe", ProcessName = "chrome", ApplicationType = InstalledApplicationType.Shortcut, Sources = ["Start Menu"] }
    ]);
    Assert(items.Count == 1 && items[0].Sources.Count == 2, "Application sources were not deduplicated and merged.");
    var desktopAndShell = ApplicationCatalog.Deduplicate([
        new InstalledApplication { DisplayName = "Google Chrome", ExecutablePath = @"C:\Apps\chrome.exe", ProcessName = "chrome", ApplicationType = InstalledApplicationType.Win32, Sources = ["Registry"] },
        new InstalledApplication { DisplayName = "Google Chrome", AppUserModelId = "Chrome", LaunchIdentifier = "Chrome", ApplicationType = InstalledApplicationType.Packaged, Sources = ["AppsFolder"] }
    ]);
    Assert(desktopAndShell.Count == 1 && desktopAndShell[0].ApplicationType == InstalledApplicationType.Win32,
        "An AppsFolder alias duplicated a desktop application or replaced its executable launch strategy.");
    return Task.CompletedTask;
}

static async Task ApplicationCatalogSerialization()
{
    using var folder = new TemporaryFolder();
    var repository = new JsonApplicationCatalogRepository(NullLogger<JsonApplicationCatalogRepository>.Instance, folder.Path);
    var document = new ApplicationCatalogDocument { LastRefresh = DateTimeOffset.Now, Applications = [new() { Id = "appx:test", DisplayName = "Test App", ApplicationType = InstalledApplicationType.Packaged, AppUserModelId = "Test.App!App", IsFavorite = true }] };
    await repository.SaveAsync(document);
    var loaded = await repository.LoadAsync();
    Assert(loaded.Applications.Count == 1 && loaded.Applications[0].AppUserModelId == "Test.App!App" && loaded.Applications[0].IsFavorite, "Application cache round trip failed.");
}

static async Task InvalidApplicationCache()
{
    using var folder = new TemporaryFolder();
    await File.WriteAllTextAsync(Path.Combine(folder.Path, "applications.json"), "{ broken");
    var loaded = await new JsonApplicationCatalogRepository(NullLogger<JsonApplicationCatalogRepository>.Instance, folder.Path).LoadAsync();
    Assert(loaded.Applications.Count == 0, "Invalid application cache did not fall back safely.");
}

static async Task StableApplicationIdentity()
{
    var catalog = await CreateCatalog(new InstalledApplication { DisplayName = "Photos", ApplicationType = InstalledApplicationType.Packaged, AppUserModelId = "Microsoft.Photos_abc!App", LaunchIdentifier = "Microsoft.Photos_abc!App", Sources = ["Packaged"] });
    Assert(catalog.Applications.Single().Id == "appx:microsoft.photos_abc!app", "Packaged application identity was not stable.");
}

static Task ApplicationLaunchStrategySelection()
{
    Assert(WindowsApplicationControlService.SelectLaunchStrategy(new() { ApplicationType = InstalledApplicationType.Packaged, AppUserModelId = "Pkg!App" }) == ApplicationLaunchStrategy.Packaged, "Packaged launch strategy failed.");
    Assert(WindowsApplicationControlService.SelectLaunchStrategy(new() { ApplicationType = InstalledApplicationType.Shortcut, LaunchIdentifier = @"C:\App.lnk" }) == ApplicationLaunchStrategy.Shortcut, "Shortcut launch strategy failed.");
    Assert(WindowsApplicationControlService.SelectLaunchStrategy(new() { ApplicationType = InstalledApplicationType.Win32, ExecutablePath = @"C:\App.exe" }) == ApplicationLaunchStrategy.Win32, "Win32 launch strategy failed.");
    return Task.CompletedTask;
}

static async Task SystemApplicationFiltering()
{
    var catalog = await CreateCatalog(
        new InstalledApplication { DisplayName = "Settings", AppUserModelId = "Settings!App", ApplicationType = InstalledApplicationType.System, IsSystemApplication = true, Sources = ["Test"] },
        new InstalledApplication { DisplayName = "Spotify", ExecutablePath = @"C:\Spotify.exe", Sources = ["Test"] });
    Assert(catalog.SearchApplications(string.Empty, false).All(item => !item.Application.IsSystemApplication), "System applications were not filtered.");
}

static Task UninstallerFiltering()
{
    Assert(ApplicationCatalog.IsUninstaller("Uninstall Spotify", @"C:\uninstall.exe"), "Uninstaller was not identified.");
    Assert(!ApplicationCatalog.IsUninstaller("Spotify", @"C:\Spotify.exe"), "Normal application was treated as an uninstaller.");
    return Task.CompletedTask;
}

static Task ApplicationCatalogCommands()
{
    var parser = new ComputerCommandParser();
    Assert(parser.Parse("refresh apps").Type == ComputerCommandType.RefreshApplications, "Refresh applications command failed.");
    var search = parser.Parse("search app visual");
    Assert(search.Type == ComputerCommandType.SearchApplications && search.Target == "visual", "Search application command failed.");
    return Task.CompletedTask;
}

static async Task ApplicationCatalogUsesCacheAtStartup()
{
    var repository = new MemoryApplicationCatalogRepository();
    await repository.SaveAsync(new ApplicationCatalogDocument { LastRefresh = DateTimeOffset.Now,
        Applications = [new InstalledApplication { DisplayName = "Cached App", ExecutablePath = @"C:\Apps\Cached.exe", Sources = ["Cache"] }] });
    var provider = new FakeApplicationDiscoveryProvider([]);
    var catalog = new ApplicationCatalog([provider], repository);
    await catalog.InitializeAsync();
    Assert(catalog.Applications.Count == 1 && provider.Calls == 0, "Startup rescanned applications instead of loading cache promptly.");
}

static async Task FailedApplicationScanPreservesCache()
{
    var repository = new MemoryApplicationCatalogRepository();
    await repository.SaveAsync(new ApplicationCatalogDocument { LastRefresh = DateTimeOffset.Now.AddDays(-2),
        Applications = [new InstalledApplication { DisplayName = "Cached App", ExecutablePath = @"C:\Apps\Cached.exe", Sources = ["Cache"] }] });
    var catalog = new ApplicationCatalog([new FailedApplicationDiscoveryProvider()], repository);
    await catalog.InitializeAsync();
    await catalog.RefreshAsync();
    Assert(catalog.Applications.Single().DisplayName == "Cached App", "A failed discovery pass erased the working application catalog.");
}

static async Task<ApplicationCatalog> CreateCatalog(params InstalledApplication[] applications)
{
    var catalog = new ApplicationCatalog([new FakeApplicationDiscoveryProvider(applications)], new MemoryApplicationCatalogRepository());
    await catalog.InitializeAsync();
    await catalog.RefreshAsync();
    return catalog;
}

static async Task<int> RunWindowsIntegrationTests(bool windowsOnly)
{
    Console.WriteLine("INTEGRATION Windows controller tests");
    using var catalogFolder = new TemporaryFolder();
    var catalogRepository = new JsonApplicationCatalogRepository(NullLogger<JsonApplicationCatalogRepository>.Instance, catalogFolder.Path);
    var catalog = new ApplicationCatalog([
        new StartMenuApplicationDiscoveryProvider(),
        new AppPathsApplicationDiscoveryProvider(NullLogger<AppPathsApplicationDiscoveryProvider>.Instance),
        new InstalledWin32ApplicationDiscoveryProvider(),
        new PackagedApplicationDiscoveryProvider(NullLogger<PackagedApplicationDiscoveryProvider>.Instance),
        new RunningApplicationDiscoveryProvider()
    ], catalogRepository);
    var discovery = await catalog.RefreshAsync();
    foreach (var provider in discovery) Console.WriteLine($"CATALOG {provider.Provider}: {provider.Status} ({provider.Applications.Count})");
    Console.WriteLine($"CATALOG deduplicated: {catalog.Applications.Count}");
    if (catalog.Applications.Count == 0) return 1;
    var reloadedCatalog = new ApplicationCatalog([], catalogRepository);
    await reloadedCatalog.InitializeAsync();
    if (reloadedCatalog.Applications.Count != catalog.Applications.Count) { Console.WriteLine("FAIL catalog cache reload count differs."); return 1; }

    var resolver = new ApplicationResolver(new WindowsExecutableLocator(), catalog);
    var windows = new WindowsExternalWindowService(resolver, NullLogger<WindowsExternalWindowService>.Instance);
    var applications = new WindowsApplicationControlService(resolver, catalog, windows, NullLogger<WindowsApplicationControlService>.Instance);
    var shell = new WindowsShellService(NullLogger<WindowsShellService>.Instance);
    IComputerController controller = new ComputerController(applications, windows, shell, catalog, resolver);
    var parser = new ComputerCommandParser();
    var failures = 0;

    async Task<ComputerActionResult> Run(string command, bool required = true, int delayAfterMs = 350)
    {
        var result = await controller.ExecuteAsync(parser.Parse(command));
        Console.WriteLine($"{(result.Success ? "PASS" : required ? "FAIL" : "INFO")} {command}: {result.Status} — {result.Message.Replace(Environment.NewLine, " ")}");
        if (required && !result.Success) failures++;
        if (delayAfterMs > 0) await Task.Delay(delayAfterMs);
        return result;
    }

    var notepadResult = await Run("open notepad", required: false, delayAfterMs: 600);
    var windowTarget = "notepad";
    var folderTested = false;
    if (!notepadResult.Success || windows.FindWindow("notepad") is null)
    {
        Console.WriteLine("INFO Notepad has no usable window on this host; using an isolated Explorer test window for Win32 operations.");
        await Run($"open folder {Environment.CurrentDirectory}", delayAfterMs: 0);
        for (var attempt = 0; attempt < 50 && windows.FindWindow("explorer") is null; attempt++) await Task.Delay(100);
        folderTested = true;
        windowTarget = "explorer";
    }
    await Run($"focus {windowTarget}", required: false);
    await Run($"minimize {windowTarget}");
    await Run($"restore {windowTarget}");
    await Run($"maximize {windowTarget}");
    await Run($"move {windowTarget} left");
    await Run($"move {windowTarget} right");
    await Run($"move {windowTarget} top-left");
    await Run($"move {windowTarget} bottom-right");
    if (windows.GetMonitors().Count > 1) await Run($"move {windowTarget} monitor 2");
    else Console.WriteLine($"SKIP move {windowTarget} monitor 2: only one monitor is connected.");
    await Run($"close {windowTarget}", delayAfterMs: 800);
    if (!folderTested) await Run($"open folder {Environment.CurrentDirectory}");
    if (!windowsOnly)
    {
        await Run($"open file {Path.Combine(Environment.CurrentDirectory, "README.md")}");
        await Run("open url https://github.com");
    }
    foreach (var candidate in new[] { "calculator", "paint", "vscode", "chrome" })
    {
        var resolved = catalog.Resolve(candidate);
        if (resolved.Status != ApplicationResolutionStatus.Found || resolved.Application is null) { Console.WriteLine($"SKIP open {candidate}: not discovered on this PC."); continue; }
        var result = await Run($"open {resolved.Application.Id}", required: false, delayAfterMs: 500);
        if (result.Success && result.Status != ActionStatus.AlreadyRunning && resolved.Application.ProcessName is { Length: > 0 })
            await controller.ExecuteAsync(new(ComputerCommandType.CloseApplication, resolved.Application.ProcessName));
    }
    var thirdParty = new[] { "spotify", "discord", "postman", "steam", "whatsapp" }.Select(name => catalog.Resolve(name)).FirstOrDefault(result => result.Status == ApplicationResolutionStatus.Found);
    if (thirdParty?.Application is { } thirdPartyApp)
    {
        var result = await Run($"open {thirdPartyApp.Id}", required: false, delayAfterMs: 600);
        Console.WriteLine($"CATALOG third-party tested: {thirdPartyApp.DisplayName} ({thirdPartyApp.ApplicationType})");
        if (result.Success && result.Status != ActionStatus.AlreadyRunning && thirdPartyApp.ProcessName is { Length: > 0 }) await controller.ExecuteAsync(new(ComputerCommandType.CloseApplication, thirdPartyApp.ProcessName));
    }
    else Console.WriteLine("SKIP third-party launch: none of the safe test candidates were discovered.");

    var ambiguousQuery = catalog.Applications.SelectMany(app => ApplicationNameNormalizer.Tokens(app.DisplayName).Take(1)).Distinct().FirstOrDefault(token => catalog.Resolve(token).Status == ApplicationResolutionStatus.Ambiguous);
    if (ambiguousQuery is not null) Console.WriteLine($"PASS ambiguity '{ambiguousQuery}': {catalog.Resolve(ambiguousQuery).Matches.Count} choices and no launch.");
    else Console.WriteLine("SKIP ambiguity: no safely ambiguous token was found in this catalog.");
    var workspaceEngine = new WorkspaceLaunchEngine(controller, windows, new ActionHistoryService(), new CharacterService(), NullLogger<WorkspaceLaunchEngine>.Instance);
    var workspace = new WorkspaceDefinition
    {
        Name = "Manual Part 3 Test",
        ContinueOnFailure = true,
        LaunchTimeoutSeconds = 25,
        Applications =
        [
            new() { DisplayName = "Notepad", ApplicationIdentifier = "notepad", CatalogApplicationId = catalog.Resolve("notepad").Application?.Id, LaunchOrder = 1, Layout = WindowLayout.Left },
            new() { DisplayName = "Calculator", ApplicationIdentifier = "calculator", CatalogApplicationId = catalog.Resolve("calculator").Application?.Id, LaunchOrder = 2, Layout = WindowLayout.Right }
        ],
        Urls = [new() { DisplayName = "Example", Value = "https://example.com", Order = 3 }],
        FinalFocusApplication = "calculator"
    };
    var workspaceResult = await workspaceEngine.LaunchAsync(workspace);
    Console.WriteLine($"{(workspaceResult.Session.Status is WorkspaceLaunchStatus.Ready or WorkspaceLaunchStatus.Partial ? "PASS" : "FAIL")} workspace launch: {workspaceResult.Session.Status} — {workspaceResult.Message}");
    if (workspaceResult.Session.Status is not (WorkspaceLaunchStatus.Ready or WorkspaceLaunchStatus.Partial)) failures++;
    foreach (var launched in workspaceResult.Session.LaunchedApplications)
        await controller.ExecuteAsync(new(ComputerCommandType.CloseApplication, launched));
    Console.WriteLine($"INTEGRATION failures: {failures}");
    return failures;
}

static async Task<int> RunCatalogProbe()
{
    using var folder = new TemporaryFolder();
    var catalog = new ApplicationCatalog([
        new StartMenuApplicationDiscoveryProvider(),
        new AppPathsApplicationDiscoveryProvider(NullLogger<AppPathsApplicationDiscoveryProvider>.Instance),
        new InstalledWin32ApplicationDiscoveryProvider(),
        new PackagedApplicationDiscoveryProvider(NullLogger<PackagedApplicationDiscoveryProvider>.Instance),
        new RunningApplicationDiscoveryProvider()
    ], new JsonApplicationCatalogRepository(NullLogger<JsonApplicationCatalogRepository>.Instance, folder.Path));
    var sources = await catalog.RefreshAsync();
    foreach (var source in sources)
        Console.WriteLine($"CATALOG {source.Provider}: {source.Status}, {source.Applications.Count} entries");
    Console.WriteLine($"CATALOG total: {catalog.Applications.Count}");
    foreach (var name in new[] { "Chrome", "Edge", "Notepad", "Calculator", "Windows Terminal", "VS Code", "Visual Studio", "Postman", "Discord", "Spotify", "Android Studio" })
    {
        var result = catalog.Resolve(name);
        Console.WriteLine($"RESOLVE {name}: {result.Status}" + (result.Application is null ? string.Empty : $" → {result.Application.DisplayName}"));
        if (result.Status == ApplicationResolutionStatus.Ambiguous)
            foreach (var match in result.Matches.Take(5))
                Console.WriteLine($"  CHOICE {match.Application.DisplayName} | {match.Application.Id} | {match.Application.ProcessName} | {match.Application.AppUserModelId} | {string.Join(",", match.Application.Sources)}");
    }
    return catalog.Applications.Count == 0 ? 1 : 0;
}

static void Assert(bool condition, string message)
{
    if (!condition) throw new InvalidOperationException(message);
}

static WorkspaceLaunchEngine CreateLaunchEngine(IComputerController controller) => new(controller, new FakeExternalWindowService(), new ActionHistoryService(), new CharacterService(), NullLogger<WorkspaceLaunchEngine>.Instance);

sealed class TemporaryFolder : IDisposable
{
    public TemporaryFolder()
    {
        Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "DesktopAgent.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path);
    }
    public string Path { get; }
    public void Dispose()
    {
        if (Directory.Exists(Path)) Directory.Delete(Path, true);
    }
}

sealed class FakeExecutableLocator : IExecutableLocator
{
    public string? Locate(string executable) => executable.Equals("code.exe", StringComparison.OrdinalIgnoreCase)
        ? "C:\\Tools\\code.exe" : null;
}

sealed class MemoryWorkspaceRepository : IWorkspaceRepository
{
    private List<WorkspaceDefinition> _items = [];
    public Task<IReadOnlyList<WorkspaceDefinition>> LoadAsync(CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<WorkspaceDefinition>>(_items.Select(item => item.DeepCopy()).ToList());
    public Task SaveAsync(IReadOnlyList<WorkspaceDefinition> workspaces, CancellationToken cancellationToken = default) { _items = workspaces.Select(item => item.DeepCopy()).ToList(); return Task.CompletedTask; }
}

sealed class MemoryApplicationCatalogRepository : IApplicationCatalogRepository
{
    private ApplicationCatalogDocument _document = new();
    public Task<ApplicationCatalogDocument> LoadAsync(CancellationToken cancellationToken = default) => Task.FromResult(_document);
    public Task SaveAsync(ApplicationCatalogDocument document, CancellationToken cancellationToken = default) { _document = document; return Task.CompletedTask; }
}

sealed class FakeWorkspaceLaunchEngine : IWorkspaceLaunchEngine
{
    public event EventHandler<WorkspaceProgress>? ProgressChanged { add { } remove { } }
    public int Calls { get; private set; }
    public Task<WorkspaceLaunchResult> LaunchAsync(WorkspaceDefinition workspace, CancellationToken cancellationToken = default)
    {
        Calls++;
        var session = new WorkspaceSession { WorkspaceId = workspace.Id, WorkspaceName = workspace.Name, Status = WorkspaceLaunchStatus.Ready, CompletedAt = DateTimeOffset.Now };
        return Task.FromResult(new WorkspaceLaunchResult(session, "Ready"));
    }
}

sealed class BlockingWorkspaceLaunchEngine : IWorkspaceLaunchEngine
{
    private int _calls;
    private readonly TaskCompletionSource _secondStarted = new(TaskCreationOptions.RunContinuationsAsynchronously);
    public event EventHandler<WorkspaceProgress>? ProgressChanged { add { } remove { } }
    public Task SecondStarted => _secondStarted.Task;
    public async Task<WorkspaceLaunchResult> LaunchAsync(WorkspaceDefinition workspace, CancellationToken cancellationToken = default)
    {
        if (Interlocked.Increment(ref _calls) == 2) _secondStarted.TrySetResult();
        try { await Task.Delay(Timeout.Infinite, cancellationToken); }
        catch (OperationCanceledException) { }
        var session = new WorkspaceSession { WorkspaceId = workspace.Id, WorkspaceName = workspace.Name,
            Status = WorkspaceLaunchStatus.Cancelled, CompletedAt = DateTimeOffset.Now };
        return new WorkspaceLaunchResult(session, "Cancelled");
    }
}

sealed class FakeApplicationDiscoveryProvider(InstalledApplication[] applications) : IApplicationDiscoveryProvider
{
    public string Name => "Test";
    public int Calls { get; private set; }
    public Task<DiscoveryProviderResult> DiscoverAsync(CancellationToken cancellationToken = default)
    {
        Calls++;
        return Task.FromResult(new DiscoveryProviderResult(Name, DiscoveryProviderStatus.Success, applications));
    }
}

sealed class FailedApplicationDiscoveryProvider : IApplicationDiscoveryProvider
{
    public string Name => "Unavailable";
    public Task<DiscoveryProviderResult> DiscoverAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult(new DiscoveryProviderResult(Name, DiscoveryProviderStatus.Failed, [], "Not available"));
}

sealed class RecordingController : IComputerController
{
    private readonly Func<ParsedComputerCommand, CancellationToken, Task<ComputerActionResult>> _handler;
    public List<ParsedComputerCommand> Commands { get; } = [];
    public RecordingController(Func<ParsedComputerCommand, ComputerActionResult>? handler = null) : this((command, _) => Task.FromResult(handler?.Invoke(command) ?? ComputerActionResult.Ok("Done", command.Target))) { }
    public RecordingController(Func<ParsedComputerCommand, CancellationToken, Task<ComputerActionResult>> handler) => _handler = handler;
    public async Task<ComputerActionResult> ExecuteAsync(ParsedComputerCommand command, CancellationToken cancellationToken = default) { Commands.Add(command); return await _handler(command, cancellationToken); }
}

sealed class FakeComputerCommandService : IComputerCommandService
{
    public List<string> Inputs { get; } = [];
    public List<string> Sources { get; } = [];
    public ComputerActionResult Result { get; set; } = ComputerActionResult.Ok("Done.");
    public Task<ComputerActionResult> ExecuteAsync(string input, CancellationToken cancellationToken = default, string source = "Text")
    {
        Inputs.Add(input);
        Sources.Add(source);
        return Task.FromResult(Result);
    }
}

sealed class FakeComputerController : IComputerController
{
    public int Calls { get; private set; }
    public Task<ComputerActionResult> ExecuteAsync(ParsedComputerCommand command, CancellationToken cancellationToken = default)
    {
        Calls++;
        return Task.FromResult(ComputerActionResult.Ok("Executed", command.Target));
    }
}

sealed class FakeAgentActionApprovalService(PermissionApproval response) : IAgentActionApprovalService
{
    public int Calls { get; private set; }
    public Task<PermissionApproval> RequestAsync(AgentCapability capability, string target, CancellationToken cancellationToken = default)
    {
        Calls++;
        return Task.FromResult(response);
    }
}

sealed class FakeCredentialStore : ICredentialStore
{
    private readonly Dictionary<string, string> _secrets = new(StringComparer.Ordinal);
    public bool IsAvailable => true;
    public bool Exists(string name) => _secrets.ContainsKey(name);
    public void Save(string name, string secret) => _secrets[name] = secret;
    public string? Read(string name) => _secrets.GetValueOrDefault(name);
    public void Delete(string name) => _secrets.Remove(name);
}

sealed class FakeHttpMessageHandler(string responseBody) : HttpMessageHandler
{
    public string? LastRequestUri { get; private set; }
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        LastRequestUri = request.RequestUri?.ToString();
        return Task.FromResult(new HttpResponseMessage(System.Net.HttpStatusCode.OK)
        {
            Content = new StringContent(responseBody, System.Text.Encoding.UTF8, "text/event-stream")
        });
    }
}

sealed class InlineProgress<T>(Action<T> report) : IProgress<T>
{
    public void Report(T value) => report(value);
}

sealed class FakeWorkspaceService : IWorkspaceService
{
    public event EventHandler? WorkspacesChanged;
    public event EventHandler<WorkspaceProgress>? ProgressChanged;
    public IReadOnlyList<WorkspaceDefinition> Workspaces => [];
    public IReadOnlyDictionary<Guid, WorkspaceSession> Sessions => new Dictionary<Guid, WorkspaceSession>();
    public Task InitializeAsync(CancellationToken cancellationToken = default) { WorkspacesChanged?.Invoke(this, EventArgs.Empty); return Task.CompletedTask; }
    public WorkspaceDefinition? Find(string nameOrId) => null;
    public Task<WorkspaceValidationResult> SaveAsync(WorkspaceDefinition workspace, CancellationToken cancellationToken = default) => Task.FromResult(WorkspaceValidationResult.Valid);
    public Task<WorkspaceDefinition?> DuplicateAsync(Guid id, CancellationToken cancellationToken = default) => Task.FromResult<WorkspaceDefinition?>(null);
    public Task<bool> DeleteAsync(Guid id, CancellationToken cancellationToken = default) => Task.FromResult(false);
    public Task<WorkspaceLaunchResult?> LaunchAsync(Guid id, CancellationToken cancellationToken = default) => Task.FromResult<WorkspaceLaunchResult?>(null);
    public bool Cancel(Guid id) { ProgressChanged?.Invoke(this, new WorkspaceProgress(id, "", WorkspaceLaunchStatus.Cancelled, 0, 0, "Cancelled")); return false; }
}

sealed class FakeExternalWindowService(IReadOnlyList<OpenWindowInfo>? openWindows = null) : IExternalWindowService
{
    public OpenWindowInfo? FocusedWindow { get; private set; }
    public int StringFocusCalls { get; private set; }
    public IReadOnlyList<OpenWindowInfo> GetOpenWindows() => openWindows ?? [];
    public IReadOnlyList<DisplayMonitorInfo> GetMonitors() => [new(1, "Primary", new(0, 0, 1920, 1080), new(0, 0, 1920, 1040), true)];
    public OpenWindowInfo? FindWindow(string target) => null;
    public ComputerActionResult Focus(string target) { StringFocusCalls++; return ComputerActionResult.Ok("Done", target); }
    public ComputerActionResult FocusWindow(OpenWindowInfo window) { FocusedWindow = window; return ComputerActionResult.Ok("Done", window.Title); }
    public ComputerActionResult Close(string target) => ComputerActionResult.Ok("Done", target);
    public ComputerActionResult Minimize(string target) => ComputerActionResult.Ok("Done", target);
    public ComputerActionResult Maximize(string target) => ComputerActionResult.Ok("Done", target);
    public ComputerActionResult Restore(string target) => ComputerActionResult.Ok("Done", target);
    public ComputerActionResult Move(string target, int x, int y) => ComputerActionResult.Ok("Done", target);
    public ComputerActionResult Resize(string target, int width, int height) => ComputerActionResult.Ok("Done", target);
    public ComputerActionResult ApplyLayout(string target, WindowLayout layout) => ComputerActionResult.Ok("Done", target);
    public ComputerActionResult MoveToMonitor(string target, int monitorNumber) => ComputerActionResult.Ok("Done", target);
}
