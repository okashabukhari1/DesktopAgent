using System.Text.Json;
using DesktopAgent.Core.Projects;
using DesktopAgent.Core.Ai;
using DesktopAgent.Core.Applications.Interfaces;
using DesktopAgent.Core.Applications.Models;
using DesktopAgent.Core.Applications.Services;
using DesktopAgent.Core.Automation.Services;
using DesktopAgent.Core.Automation.Enums;
using DesktopAgent.Core.Interfaces;
using DesktopAgent.Core.Permissions;
using DesktopAgent.Core.Services;
using DesktopAgent.Core.Workspaces.Models;
using DesktopAgent.Infrastructure.Projects;
using DesktopAgent.Infrastructure.Ai;
using DesktopAgent.Infrastructure.Settings;
using DesktopAgent.Infrastructure.Windows.Automation;
using DesktopAgent.Infrastructure.Workspaces;
using Microsoft.Extensions.Logging.Abstractions;

internal static class Part3Tests
{
    public static (string Name, Func<Task> Run)[] Cases =>
    [
        ("project .NET solution and web SDK metadata", DotNet),
        ("project React local dependency detection", () => Node("react", "React")),
        ("project Next.js local dependency detection", () => Node("next", "Next.js")),
        ("project Vite Express NestJS detection", NodeFrameworks),
        ("project Laravel and PHP detection", Laravel),
        ("project Android Kotlin Gradle detection", Android),
        ("project Python detection", Python),
        ("project Java Maven detection", Java),
        ("static web requires a second indicator", StaticWeb),
        ("unknown manual project remains valid", Unknown),
        ("project generated directories are excluded", Exclusions),
        ("project scan honors depth and configured boundaries", Boundaries),
        ("project scanning cancellation preserves cache", Cancellation),
        ("project nested frontend backend merge", MultiTech),
        ("project malformed and oversized manifests stay bounded", Malformed),
        ("project metadata never reads secret/source payloads", Privacy),
        ("project Git metadata is read-only", Git),
        ("project search exact prefix technology fuzzy ambiguity missing", Search),
        ("project duplicate exact names stay ambiguous", DuplicateNames),
        ("project catalog persistence preserves identity preferences recent", Persistence),
        ("project cache startup never scans", CachedStartup),
        ("corrupt project cache is preserved read-only", Corruption),
        ("future project schema is preserved read-only", Schema),
        ("missing project remains catalogued and cannot open", Missing),
        ("project removal never deletes disk files", Remove),
        ("project settings clone preserves independent locations", Settings),
        ("project opening selects configured editor and updates context", Open),
        ("project opening prefers workspace and reports partial failure", Workspace),
        ("missing editor never reports false opening or current context", MissingEditor),
        ("missing workspace falls back without losing project", MissingWorkspace),
        ("project typed editor arguments cannot become commands", EditorArguments),
        ("all eight project tools execute via typed services", Tools),
        ("project tool validation rejects unknown duplicate and invalid args", ToolValidation),
        ("project permissions gate embedded workspace operations", Permissions),
        ("project association requires permission and is persisted", Association),
        ("project permission wait revalidates workspace before execution", ApprovalRace),
        ("assistant project ambiguity keeps original project action", AssistantAmbiguity),
        ("assistant current project supports its folder without source access", ContextFolder),
        ("workspace project association survives persistence", WorkspacePersistence)
    ];
    private static void Check(bool value, string message) { if (!value) throw new Exception(message); }
    private sealed class Fixture : IDisposable
    {
        public TemporaryFolder Folder { get; } = new();
        public string Root => Folder.Path;
        public ProjectDiscoveryService Discovery { get; } = new();
        public JsonSettingsService Settings { get; }
        public ProjectCatalog Catalog { get; }
        public CurrentProjectContext Context { get; } = new();
        public TestWorkspaces Workspaces { get; } = new();
        public ActionHistoryService History { get; } = new();
        public FakeEditor Editor { get; } = new();
        public IApplicationCatalog Applications { get; }
        public ProjectOpenService Open { get; }
        public Fixture()
        {
            Settings = new(NullLogger<JsonSettingsService>.Instance, System.IO.Path.Combine(Root, "cache"));
            Catalog = new(Discovery, Settings, History);
            Applications = new ApplicationCatalog([], new MemoryApplicationCatalogRepository());
            Open = new(Catalog, Applications, Editor, Settings, Workspaces, Context, History);
        }
        public string Project(string name, params (string File, string Text)[] files)
        {
            var path = System.IO.Path.Combine(Root, name); Directory.CreateDirectory(path);
            foreach (var file in files) { var target = System.IO.Path.Combine(path, file.File); Directory.CreateDirectory(System.IO.Path.GetDirectoryName(target)!); File.WriteAllText(target, file.Text); }
            return path;
        }
        public ComputerAgentToolRouter Router(MemoryPolicy? policy = null, IAgentActionApprovalService? approval = null) => new(
            new AgentToolRegistry(ProjectTools.Create(Catalog, Open, Workspaces, Context)), policy ?? new(),
            approval ?? new FakeAgentActionApprovalService(PermissionApproval.AllowOnce), History, new CharacterService(), NullLogger<ComputerAgentToolRouter>.Instance, Workspaces);
        public AssistantConversationService Assistant(IAgentToolRouter router, IAiProvider provider, FakeComputerCommandService? commands = null) => new(provider, router,
            new ComputerCommandParser(), commands ?? new(), History, Settings, NullLogger<AssistantConversationService>.Instance,
            catalog: Applications, workspaces: Workspaces, projects: Catalog, currentProject: Context);
        public void Dispose() => Folder.Dispose();
    }
    private sealed class FakeEditor : IProjectEditorLauncher
    {
        public bool Available { get; set; } = true;
        public int Calls { get; private set; }
        public bool CanOpen(string id) => Available && id == "code";
        public Task<ToolResult> OpenAsync(string id, ProjectProfile project, CancellationToken cancellationToken = default)
        { cancellationToken.ThrowIfCancellationRequested(); Calls++; return Task.FromResult(new ToolResult(true, "Success", "Sent project to editor.")); }
    }
    private static AiToolCall Call(string name, object args) => new(Guid.NewGuid().ToString("N"), name, JsonSerializer.Serialize(args));
    private static async Task<ProjectProfile> Add(Fixture f, string name = "PowerLens")
    { var p = await f.Catalog.AddAsync(f.Project(name, ("package.json", "{\"dependencies\":{\"react\":\"1\"}}"))); p.PreferredEditorId = "code"; await f.Catalog.UpdateAsync(p); return p; }
    private static async Task DotNet()
    {
        using var f = new Fixture(); var p = await f.Discovery.DetectAsync(f.Project("DotNet", ("App.sln", ""), ("App.csproj", "<Project Sdk='Microsoft.NET.Sdk.Web'><PropertyGroup><TargetFramework>net8.0</TargetFramework></PropertyGroup></Project>")));
        Check(p.Technologies.Contains(".NET") && p.Technologies.Contains("ASP.NET") && p.Frameworks.Contains("net8.0"), "Missing .NET metadata.");
    }
    private static async Task Node(string dependency, string expected)
    { using var f = new Fixture(); var p = await f.Discovery.DetectAsync(f.Project("Node", ("package.json", "{\"dependencies\":{\"" + dependency + "\":\"1\"}}"))); Check(p.Technologies.Contains(expected), "Missing dependency classification."); }
    private static async Task NodeFrameworks()
    { using var f = new Fixture(); var p = await f.Discovery.DetectAsync(f.Project("Node", ("package.json", "{\"devDependencies\":{\"vite\":\"1\",\"express\":\"1\",\"@nestjs/core\":\"1\"}}"))); Check(new[] { "Vite", "Express", "NestJS" }.All(p.Technologies.Contains), "Framework metadata missing."); }
    private static async Task Laravel()
    { using var f = new Fixture(); var p = await f.Discovery.DetectAsync(f.Project("PHP", ("composer.json", "{\"require\":{\"laravel/framework\":\"1\"}}"), ("artisan", "DO NOT EXECUTE"))); Check(p.Technologies.Contains("Laravel") && p.Languages.Contains("PHP"), "Laravel missing."); }
    private static async Task Android()
    { using var f = new Fixture(); var p = await f.Discovery.DetectAsync(f.Project("Android", ("build.gradle.kts", "plugins { id(\"com.android.application\"); id(\"org.jetbrains.kotlin.android\") }"))); Check(p.Technologies.Contains("Android") && p.Languages.Contains("Kotlin"), "Android missing."); }
    private static async Task Python()
    { using var f = new Fixture(); Check((await f.Discovery.DetectAsync(f.Project("Py", ("pyproject.toml", "")))).Technologies.Contains("Python"), "Python missing."); }
    private static async Task Java()
    { using var f = new Fixture(); Check((await f.Discovery.DetectAsync(f.Project("Java", ("pom.xml", "")))).Technologies.Contains("Java"), "Java missing."); }
    private static async Task StaticWeb()
    {
        using var f = new Fixture(); var root = f.Project("Site", ("index.html", "secret code"));
        Check((await f.Discovery.DetectAsync(root)).Technologies.Count == 0, "index.html alone overclassified.");
        File.WriteAllText(System.IO.Path.Combine(root, "site.css"), "secret css");
        Check((await f.Discovery.DetectAsync(root)).Technologies.Contains("Static Web"), "Static project not detected.");
    }
    private static async Task Unknown()
    { using var f = new Fixture(); var p = await f.Catalog.AddAsync(f.Project("Unknown")); Check(p.Technologies.Count == 0 && p.IsAvailable, "Unknown manual project invalid."); }
    private static async Task Exclusions()
    {
        using var f = new Fixture(); foreach (var name in ProjectDiscoveryService.Exclusions) f.Project("locations/" + name + "/Hidden", ("package.json", "{}"));
        var result = await f.Discovery.ScanAsync([new() { Path = System.IO.Path.Combine(f.Root, "locations"), MaxDepth = 4 }]);
        Check(result.Projects.Count == 0 && result.DirectoriesVisited == 1, "Dependency directories scanned.");
    }
    private static async Task Boundaries()
    {
        using var f = new Fixture(); var root = f.Project("locations/a/b", ("package.json", "{}")); f.Project("outside", ("package.json", "{}"));
        var shallow = await f.Discovery.ScanAsync([new() { Path = System.IO.Path.Combine(f.Root, "locations"), MaxDepth = 1 }]);
        Check(shallow.Projects.Count == 0, "Depth exceeded.");
        var scan = await f.Discovery.ScanAsync([new() { Path = System.IO.Path.Combine(f.Root, "locations"), MaxDepth = 2 }]);
        Check(scan.Projects.Count == 1 && scan.Projects[0].Path == System.IO.Path.GetFullPath(root), "Scanned outside configured location.");
        var drive = await f.Discovery.ScanAsync([new() { Path = System.IO.Path.GetPathRoot(f.Root)! }]);
        Check(drive.DirectoriesVisited == 0 && drive.Warnings.Count > 0, "Drive root allowed.");
    }
    private static async Task Cancellation()
    {
        using var f = new Fixture(); var p = await Add(f); using var source = new CancellationTokenSource(); source.Cancel();
        try { await f.Catalog.RefreshAsync(source.Token); throw new Exception("Cancellation ignored."); } catch (OperationCanceledException) { }
        Check(f.Catalog.Find(p.Id) is not null && !f.Catalog.IsScanning, "Cancellation discarded catalog.");
    }
    private static async Task MultiTech()
    {
        using var f = new Fixture(); var root = f.Project("PowerLens", ("App.sln", ""), ("backend/App.csproj", "<Project/>"), ("frontend/package.json", "{\"dependencies\":{\"react\":\"1\"}}"));
        var result = await f.Discovery.ScanAsync([new() { Path = root }]);
        Check(result.Projects.Count == 1 && result.Projects[0].Technologies.Contains("React") && result.Projects[0].Technologies.Contains(".NET"), "Multi-tech not merged.");
        var manual = await f.Catalog.AddAsync(root);
        Check(manual.Technologies.Contains("React") && manual.Technologies.Contains(".NET"), "Manual addition did not merge nested metadata.");
    }
    private static async Task Malformed()
    {
        using var f = new Fixture(); var p = await f.Discovery.DetectAsync(f.Project("Broken", ("package.json", "{"), ("App.csproj", "<!DOCTYPE x [<!ENTITY ex SYSTEM 'file:///secret'>]><Project>&ex;</Project>")));
        Check(p.Technologies.Contains("Node.js") && p.Frameworks.Count == 0, "Malformed manifest crashed or XML expanded.");
        var large = await f.Discovery.DetectAsync(f.Project("Large", ("package.json", new string('x', ProjectDiscoveryService.MaxManifestBytes + 1))));
        Check(large.Technologies.Contains("Node.js"), "Oversized signature lost.");
    }
    private static async Task Privacy()
    {
        using var f = new Fixture(); var p = await f.Discovery.DetectAsync(f.Project("Private", ("package.json", "{}"), (".env", "SECRET_SENTINEL"), ("appsettings.Development.json", "SECRET_SENTINEL"), ("main.cs", "SECRET_SENTINEL")));
        var json = JsonSerializer.Serialize(ProjectTools.Metadata(p)); Check(!json.Contains("SECRET_SENTINEL") && !json.Contains(".env") && !json.Contains("main.cs"), "Private content exposed.");
    }
    private static async Task Git()
    {
        using var f = new Fixture(); var root = f.Project("Git", (".git/HEAD", "ref: refs/heads/main\n"), (".git/config", "SECRET_SENTINEL"));
        var before = File.ReadAllText(System.IO.Path.Combine(root, ".git/HEAD")); var p = await f.Discovery.DetectAsync(root);
        Check(p.IsGitRepository && p.GitBranch == "main" && File.ReadAllText(System.IO.Path.Combine(root, ".git/HEAD")) == before, "Git metadata wrong or modified.");
    }
    private static async Task Search()
    {
        using var f = new Fixture(); var p = await Add(f); await Add(f, "PowerLens-Frontend");
        Check(f.Catalog.Resolve("PowerLens").Project?.Id == p.Id && f.Catalog.Search("Power").Count == 2 && f.Catalog.Search("", "React").Count == 2, "Exact/prefix/technology search failed.");
        Check(f.Catalog.Search("PowerLenz").Count > 0 && f.Catalog.Resolve("Power").Choices.Count == 2 && f.Catalog.Search("zzz").Count == 0, "Fuzzy/ambiguous/missing failed.");
    }
    private static async Task DuplicateNames()
    { using var f = new Fixture(); await Add(f, "a/Nizaliah"); await Add(f, "b/Nizaliah"); Check(f.Catalog.Resolve("Nizaliah").Choices.Count == 2, "Duplicate names silently resolved."); }
    private static async Task Persistence()
    {
        using var f = new Fixture(); var p = await Add(f); p.IsFavorite = true; await f.Catalog.UpdateAsync(p); await f.Catalog.MarkOpenedAsync(p.Id);
        var next = new ProjectCatalog(f.Discovery, f.Settings); await next.InitializeAsync(); var restored = next.Find(p.Id)!;
        Check(restored.IsFavorite && restored.PreferredEditorId == "code" && restored.OpenCount == 1 && restored.LastOpenedAt.HasValue, "Persistence lost state.");
        var refreshed = await next.AddAsync(p.Path); Check(refreshed.Id == p.Id && refreshed.IsFavorite, "Refresh changed ID or preferences.");
    }
    private static async Task CachedStartup()
    {
        using var f = new Fixture(); var p = await Add(f); f.Settings.Current.Projects.Locations.Add(new() { Path = f.Root });
        var never = new NeverDiscovery(); var next = new ProjectCatalog(never, f.Settings); await next.InitializeAsync();
        Check(next.Find(p.Id) is not null && never.Calls == 0, "Startup scanned filesystem.");
    }
    private sealed class NeverDiscovery : IProjectDiscoveryService
    {
        public int Calls { get; private set; }
        public Task<ProjectScanResult> ScanAsync(IReadOnlyList<ProjectLocation> locations, CancellationToken cancellationToken = default) { Calls++; throw new Exception("Unexpected scan"); }
        public Task<ProjectProfile> DetectAsync(string path, CancellationToken cancellationToken = default) { Calls++; throw new Exception("Unexpected detection"); }
    }
    private static async Task CacheInvalid(string content)
    {
        using var f = new Fixture(); var cache = System.IO.Path.GetDirectoryName(f.Settings.SettingsPath)!; Directory.CreateDirectory(cache);
        var file = System.IO.Path.Combine(cache, "projects.json"); File.WriteAllText(file, content);
        await f.Catalog.InitializeAsync(); Check(f.Catalog.LoadWarning is not null, "Cache warning missing.");
        try { await f.Catalog.AddAsync(f.Project("Unknown")); throw new Exception("Invalid cache overwritten."); } catch (InvalidOperationException) { }
        Check(File.ReadAllText(file) == content, "Original cache changed.");
    }
    private static Task Corruption() => CacheInvalid("{");
    private static Task Schema() => CacheInvalid("{\"Version\":999,\"Projects\":[]}");
    private static async Task Missing()
    {
        using var f = new Fixture(); var p = await Add(f); Directory.Move(p.Path, p.Path + "-moved");
        var result = await f.Open.OpenAsync(p.Id.ToString()); Check(!result.Success && f.Catalog.Find(p.Id) is not null && f.Editor.Calls == 0 && f.Context.Current is null, "Missing path launched or disappeared.");
        var cached = new ProjectCatalog(f.Discovery, f.Settings); await cached.InitializeAsync();
        Check(cached.Find(p.Id)?.IsAvailable == false, "Unavailable cached project reported available.");
    }
    private static async Task Remove()
    { using var f = new Fixture(); var p = await Add(f); await f.Catalog.RemoveAsync(p.Id); Check(Directory.Exists(p.Path) && File.Exists(System.IO.Path.Combine(p.Path, "package.json")) && f.Catalog.Find(p.Id) is null, "Removal deleted files."); }
    private static async Task Settings()
    { using var f = new Fixture(); f.Settings.Current.Projects.Locations.Add(new() { Path = f.Root }); var copy = f.Settings.Current.Clone(); copy.Projects.Locations[0].MaxDepth = 7; Check(f.Settings.Current.Projects.Locations[0].MaxDepth == 4, "Settings clone shares locations."); await f.Settings.SaveAsync(copy); Check((await f.Settings.LoadAsync()).Projects.Locations[0].MaxDepth == 7, "Project settings lost."); }
    private static async Task Open()
    { using var f = new Fixture(); var p = await Add(f); var result = await f.Open.OpenAsync(p.Id.ToString()); Check(result.Success && f.Editor.Calls == 1 && f.Context.Current?.ProjectId == p.Id && f.Catalog.Find(p.Id)!.OpenCount == 1, "Open workflow failed."); }
    private static async Task Workspace()
    {
        using var f = new Fixture(); var p = await Add(f); p.AssociatedWorkspaceId = f.Workspaces.Workspace.Id; await f.Catalog.UpdateAsync(p); f.Workspaces.Outcome = DesktopAgent.Core.Workspaces.Enums.WorkspaceLaunchStatus.Partial;
        var result = await f.Open.OpenAsync(p.Id.ToString()); Check(result.Success && result.Status == "Partial" && f.Workspaces.Launches == 1 && f.Editor.Calls == 1 && f.Context.Current?.AssociatedWorkspaceId == p.AssociatedWorkspaceId, "Workspace priority/partial failed.");
    }
    private static async Task MissingEditor()
    { using var f = new Fixture(); var p = await Add(f); f.Editor.Available = false; var result = await f.Open.OpenAsync(p.Id.ToString()); Check(!result.Success && f.Context.Current is null && f.Catalog.Find(p.Id)!.OpenCount == 0, "Missing editor fake success."); }
    private static async Task MissingWorkspace()
    { using var f = new Fixture(); var p = await Add(f); p.AssociatedWorkspaceId = Guid.NewGuid(); await f.Catalog.UpdateAsync(p); var result = await f.Open.OpenAsync(p.Id.ToString()); Check(result.Success && result.Status == "Partial" && f.Editor.Calls == 1, "Missing workspace failed fallback."); }
    private static Task EditorArguments()
    {
        using var f = new Fixture(); var p = new ProjectProfile { Path = f.Project("Project & echo unsafe") };
        var info = WindowsProjectEditorLauncher.CreateStartInfo(@"C:\Tools\Code.exe", p);
        Check(!info.UseShellExecute && info.Arguments.Length == 0 && info.ArgumentList.Count == 2 && info.ArgumentList[1] == p.Path, "Editor arguments became commands.");
        Check(!WindowsProjectEditorLauncher.SupportedExecutable("powershell.exe"), "Terminal allowed as editor."); return Task.CompletedTask;
    }
    private static async Task Tools()
    {
        using var f = new Fixture(); var p = await Add(f); await f.Catalog.MarkOpenedAsync(p.Id); f.Context.Set(p); var router = f.Router();
        var cases = new (string, object)[] { ("list_projects", new { }), ("search_projects", new { query = "", technology = "React" }), ("get_project", new { project = p.Id.ToString() }), ("get_recent_projects", new { }),
            ("get_project_technologies", new { project = p.Id.ToString() }), ("get_current_project", new { }), ("open_project", new { project = p.Id.ToString(), editor = (string?)null, use_workspace = false }),
            ("associate_project_workspace", new { project = p.Id.ToString(), workspace = f.Workspaces.Workspace.Id.ToString() }) };
        Check(router.Definitions.Count == 8, "Tools missing."); foreach (var (name, args) in cases) Check((await router.InvokeAsync(Call(name, args))).Success, "Tool failed: " + name);
    }
    private static async Task ToolValidation()
    {
        using var f = new Fixture(); var router = f.Router();
        foreach (var json in new[] { "{\"project\":\"x\",\"project\":\"y\"}", "{\"project\":\"x\",\"command\":\"evil\"}", "{\"project\":3}" })
            Check((await router.InvokeAsync(new("x", "get_project", json))).Status == "InvalidArguments", "Invalid args accepted.");
    }
    private static async Task Permissions()
    {
        using var f = new Fixture(); var p = await Add(f); p.AssociatedWorkspaceId = f.Workspaces.Workspace.Id; await f.Catalog.UpdateAsync(p);
        f.Workspaces.Workspace.PostLaunchActions.Add(new() { Type = DesktopAgent.Core.Workspaces.Enums.WorkspaceActionType.Close, Target = "chrome" });
        var policy = new MemoryPolicy(); await policy.SetPolicyAsync(CapabilityIds.ApplicationClose, PermissionPolicy.Deny);
        var result = await f.Router(policy).InvokeAsync(Call("open_project", new { project = p.Id.ToString(), editor = (string?)null, use_workspace = true }));
        Check(result.Status == "PermissionDenied" && f.Editor.Calls == 0 && f.Workspaces.Launches == 0, "Embedded permission bypass.");
    }
    private static async Task Association()
    {
        using var f = new Fixture(); var p = await Add(f); var deny = f.Router(approval: new FakeAgentActionApprovalService(PermissionApproval.Deny));
        var call = Call("associate_project_workspace", new { project = p.Id.ToString(), workspace = f.Workspaces.Workspace.Id.ToString() });
        Check(!(await deny.InvokeAsync(call)).Success && f.Catalog.Find(p.Id)!.AssociatedWorkspaceId is null, "Denied mutation executed.");
        Check((await f.Router().InvokeAsync(call)).Success && f.Catalog.Find(p.Id)!.AssociatedWorkspaceId == f.Workspaces.Workspace.Id, "Association did not persist.");
    }
    private sealed class MutatingApproval(Action mutate) : IAgentActionApprovalService
    { public Task<PermissionApproval> RequestAsync(AgentCapability capability, string target, CancellationToken cancellationToken = default) { mutate(); return Task.FromResult(PermissionApproval.AllowOnce); } }
    private static async Task ApprovalRace()
    {
        using var f = new Fixture(); var p = await Add(f); p.AssociatedWorkspaceId = f.Workspaces.Workspace.Id; await f.Catalog.UpdateAsync(p);
        var policy = new MemoryPolicy(); await policy.SetPolicyAsync(CapabilityIds.ProjectOpen, PermissionPolicy.Ask);
        var result = await f.Router(policy, new MutatingApproval(() => f.Workspaces.Workspace.PostLaunchActions.Add(new() { Type = DesktopAgent.Core.Workspaces.Enums.WorkspaceActionType.Close, Target = "chrome" })))
            .InvokeAsync(Call("open_project", new { project = p.Id.ToString(), editor = (string?)null, use_workspace = true }));
        Check(result.Status == "InvalidConfiguration" && f.Editor.Calls == 0 && f.Workspaces.Launches == 0, "Changed workspace bypassed policy.");
    }
    private static async Task AssistantAmbiguity()
    {
        using var f = new Fixture(); var p = await Add(f); await Add(f, "PowerLens-Frontend");
        var provider = new ScriptedProvider(new AiProviderResponse("", [Call("open_project", new { project = "Power", editor = (string?)null, use_workspace = false })]));
        var conversation = f.Assistant(f.Router(), provider); await conversation.SendAsync("Open the Power project.");
        var choice = conversation.Entries.Single(e => e.Action is not null).Action!.Choices!.First(c => c.Id == p.Id.ToString());
        Check(choice.Kind == "project", "Project choice kind missing."); await conversation.ChooseApplicationAsync(choice.Id);
        Check(f.Context.Current?.ProjectId == p.Id && f.Editor.Calls == 1, "Project choice changed original action.");
    }
    private static async Task ContextFolder()
    {
        using var f = new Fixture(); var p = await Add(f); await f.Open.OpenAsync(p.Id.ToString());
        var controller = new RecordingController(); var commands = new FakeComputerCommandService();
        var router = new ComputerAgentToolRouter(new AgentToolRegistry(ProjectTools.Create(f.Catalog, f.Open, f.Workspaces, f.Context)
            .Concat(WindowsAgentTools.Create(controller, f.Applications, new TestApplications(), f.Workspaces))), new MemoryPolicy(), new FakeAgentActionApprovalService(PermissionApproval.AllowOnce), f.History, new CharacterService(), NullLogger<ComputerAgentToolRouter>.Instance);
        var provider = new ScriptedProvider(new AiProviderResponse("", [Call("get_current_project", new { })]), new("", [Call("open_folder", new { path = p.Path })]), new("done", []));
        var conversation = f.Assistant(router, provider, commands); await conversation.SendAsync("Open its folder.");
        Check(controller.Commands.Single().Type == ComputerCommandType.OpenFolder && controller.Commands.Single().Target == p.Path && provider.Requests[0].Any(m => m.Content.Contains(p.Id.ToString())), "Project reference lost.");
    }
    private static async Task WorkspacePersistence()
    {
        using var f = new Fixture(); var repository = new JsonWorkspaceRepository(NullLogger<JsonWorkspaceRepository>.Instance, f.Root);
        var w = new WorkspaceDefinition { Name = "Development", ProjectId = Guid.NewGuid() }; await repository.SaveAsync([w]);
        Check((await repository.LoadAsync()).Single().ProjectId == w.ProjectId && w.DeepCopy().ProjectId == w.ProjectId, "Workspace lost ProjectId.");
    }
}
