using System.Text.Json;
using DesktopAgent.Core.Ai;
using DesktopAgent.Core.Applications.Enums;
using DesktopAgent.Core.Applications.Interfaces;
using DesktopAgent.Core.Automation.Enums;
using DesktopAgent.Core.Automation.Interfaces;
using DesktopAgent.Core.Automation.Models;
using DesktopAgent.Core.Permissions;
using DesktopAgent.Core.Workspaces.Enums;
using DesktopAgent.Core.Workspaces.Interfaces;
using DesktopAgent.Core.Workspaces.Models;

namespace DesktopAgent.Infrastructure.Ai;

// Definitions, validation and execution are registered together. AI actions never enter the text parser.
public static class WindowsAgentTools
{
    public static IEnumerable<IAgentTool> Create(IComputerController controller, IApplicationCatalog catalog,
        IApplicationControlService applications, IWorkspaceService workspaces)
    {
        yield return Tool("list_applications", "List installed applications only when needed.", [], [CapabilityIds.ApplicationDiscover],
            (_, _) => Task.FromResult(new ToolResult(true, "Success", "Installed applications.",
                catalog.Applications.Where(x => x.IsInstalled && x.IsLaunchable).Take(80).Select(x => new { x.Id, x.DisplayName }).ToArray())));
        yield return Tool("search_applications", "Search the real installed catalog; do not guess availability.", [Text("query")],
            [CapabilityIds.ApplicationDiscover, CapabilityIds.ApplicationSearch],
            (a, _) => Task.FromResult(new ToolResult(true, "Success", "Application search results.",
                catalog.SearchApplications(Read(a, "query"), true, 12).Select(x => new { x.Application.Id, x.Application.DisplayName }).ToArray())));
        yield return Tool("get_running_applications", "List actual visible running applications.", [], [CapabilityIds.ApplicationDiscover],
            (_, _) => Task.FromResult(new ToolResult(true, "Success", "Running applications.",
                applications.GetRunningApplications().Take(80).Select(x => new { x.ApplicationName, x.ProcessName, x.ProcessId }).ToArray())));
        foreach (var (name, type) in new[] {
            ("open_application", ComputerCommandType.OpenApplication), ("focus_application", ComputerCommandType.FocusApplication),
            ("close_application", ComputerCommandType.CloseApplication), ("minimize_application", ComputerCommandType.MinimizeWindow),
            ("maximize_application", ComputerCommandType.MaximizeWindow), ("restore_application", ComputerCommandType.RestoreWindow) })
            yield return ApplicationTool(name, name.Replace('_', ' ') + " using a catalog name or ID.", [Text("application")], type,
                a => new(type, Read(a, "application")), controller, catalog);
        yield return ApplicationTool("move_application_window", "Arrange an application in a standard desktop layout.",
            [Text("application"), EnumField("layout", "left", "right", "top", "bottom", "top-left", "top-right", "bottom-left", "bottom-right", "center", "maximized")],
            ComputerCommandType.ApplyLayout, a => new(ComputerCommandType.ApplyLayout, Read(a, "application"),
                Enum.Parse<WindowLayout>(Read(a, "layout").Replace("-", ""), true)), controller, catalog);
        yield return ApplicationTool("resize_application_window", "Resize an application window in pixels.",
            [Text("application"), Number("width", 100, 10000), Number("height", 100, 10000)],
            ComputerCommandType.ResizeWindow, a => new(ComputerCommandType.ResizeWindow, Read(a, "application"),
                Width: a.GetProperty("width").GetInt32(), Height: a.GetProperty("height").GetInt32()), controller, catalog);
        yield return ApplicationTool("move_to_monitor", "Move an application to a numbered monitor.",
            [Text("application"), Number("monitor", 1, 16)], ComputerCommandType.MoveToMonitor,
            a => new(ComputerCommandType.MoveToMonitor, Read(a, "application"), MonitorNumber: a.GetProperty("monitor").GetInt32()), controller, catalog);
        yield return Tool("list_workspaces", "List real saved workspaces; prefer a matching workspace for a work/project request.", [], [CapabilityIds.WorkspaceView],
            async (_, ct) => { await workspaces.InitializeAsync(ct); return new(true, "Success", "Saved workspaces.",
                workspaces.Workspaces.Take(80).Select(x => new { x.Id, x.Name, x.Description }).ToArray()); });
        yield return Tool("get_workspace", "Inspect a saved workspace by exact name or ID.", [Text("workspace")], [CapabilityIds.WorkspaceView],
            async (a, ct) => { await workspaces.InitializeAsync(ct); var w = workspaces.Find(Read(a, "workspace"));
                return w is null ? MissingWorkspace(a) : new(true, "Success", "Workspace: " + w.Name, new {
                    w.Id, w.Name, w.Description, Applications = w.Applications.Select(x => new { x.DisplayName, x.Layout, x.TargetMonitor }),
                    FolderCount = w.Folders.Count, FileCount = w.Files.Count, UrlCount = w.Urls.Count }, Target: w.Name); });
        yield return Tool("launch_workspace", "Launch an existing workspace; returns actual steps and failures.", [Text("workspace")],
            [CapabilityIds.WorkspaceView, CapabilityIds.WorkspaceLaunch], async (a, ct) => {
                await workspaces.InitializeAsync(ct); var w = workspaces.Find(Read(a, "workspace")); if (w is null) return MissingWorkspace(a);
                var result = await workspaces.LaunchAsync(w.Id, ct);
                return result is null ? MissingWorkspace(a) : new(result.Success, result.Session.Status.ToString(), result.Message,
                    new { result.Session.WorkspaceId, result.Session.WorkspaceName, result.Session.Status,
                        SuccessfulActions = result.Session.Results.Count(x => x.Success),
                        FailedActions = result.Session.Results.Count(x => !x.Success),
                        FailedSteps = result.Session.Results.Where(x => !x.Success).Take(12).ToArray(),
                        result.Session.StartedAt, result.Session.CompletedAt }, Target: w.Name);
            }, async (a, ct) => { await workspaces.InitializeAsync(ct); return WorkspaceCapabilities.RequiredFor(workspaces, Read(a, "workspace")); });
        yield return Tool("get_workspace_status", "Inspect the last real launch result, including failed steps.", [Text("workspace")], [CapabilityIds.WorkspaceView],
            async (a, ct) => { await workspaces.InitializeAsync(ct); var w = workspaces.Find(Read(a, "workspace"));
                if (w is null) return MissingWorkspace(a);
                return workspaces.Sessions.TryGetValue(w.Id, out var s)
                    ? new(true, s.Status.ToString(), w.Name + ": " + s.Status, new { s.Status,
                        SuccessfulActions = s.Results.Count(x => x.Success), FailedActions = s.Results.Count(x => !x.Success),
                        FailedSteps = s.Results.Where(x => !x.Success).Take(12).ToArray(), s.StartedAt, s.CompletedAt }, Target: w.Name)
                    : new(true, "NotRunning", w.Name + " has no launch session.", Target: w.Name); });
        yield return Tool("cancel_workspace_launch", "Cancel a workspace launch without closing unrelated apps.", [Text("workspace")],
            [CapabilityIds.WorkspaceView, CapabilityIds.WorkspaceStop], async (a, ct) => {
                await workspaces.InitializeAsync(ct); var w = workspaces.Find(Read(a, "workspace")); if (w is null) return MissingWorkspace(a);
                return workspaces.Cancel(w.Id) ? new(true, "Cancelled", "Workspace cancellation requested.", Target: w.Name)
                    : new(false, "NotRunning", "That workspace is not currently launching.", Target: w.Name);
            });
        foreach (var update in new[] { false, true })
            yield return Tool(update ? "update_workspace" : "create_workspace",
                update ? "Replace only a saved workspace's application list. Preserve its resources and other settings. Requires modification permission."
                    : "Create a saved workspace with exact catalog applications. Does not launch it. Requires modification permission.",
                [Text("workspace"), new("applications", "array", 12)],
                [CapabilityIds.WorkspaceView, CapabilityIds.WorkspaceModify, CapabilityIds.ApplicationSearch],
                async (a, ct) => {
                    await workspaces.InitializeAsync(ct);
                    var name = Read(a, "workspace");
                    var existing = workspaces.Find(name);
                    if (update && existing is null) return MissingWorkspace(a);
                    if (!update && existing is not null) return new(false, "InvalidArguments", "A workspace with that name already exists.", Target: name);
                    var candidate = existing?.DeepCopy() ?? new WorkspaceDefinition { Name = name };
                    var items = new List<WorkspaceApplication>();
                    foreach (var item in a.GetProperty("applications").EnumerateArray())
                    {
                        var match = catalog.Resolve(item.GetString()!);
                        if (match.Application is null)
                            return new(false, match.Status.ToString(), "Specify an exact installed application name or ID for each workspace entry.", Target: item.GetString());
                        if (items.Any(x => x.CatalogApplicationId == match.Application.Id))
                            return new(false, "InvalidArguments", "Duplicate applications are not allowed.", Target: name);
                        var entry = candidate.Applications.FirstOrDefault(x => x.CatalogApplicationId == match.Application.Id)?.DeepCopy() ?? new WorkspaceApplication();
                        entry.DisplayName = match.Application.DisplayName;
                        entry.ApplicationIdentifier = match.Application.DisplayName;
                        entry.CatalogApplicationId = match.Application.Id;
                        entry.LaunchOrder = items.Count + 1;
                        items.Add(entry);
                    }
                    candidate.Applications = items;
                    var saved = await workspaces.SaveAsync(candidate, ct);
                    return saved.IsValid ? new(true, "Success", update ? "Workspace updated." : "Workspace created.",
                        new { candidate.Id, candidate.Name, Applications = items.Select(x => x.DisplayName) }, Target: candidate.Name)
                        : new(false, "InvalidConfiguration", string.Join(" ", saved.Issues.Select(x => x.Message)), Target: name);
                });
        foreach (var (name, type, field, capability) in new[] {
            ("open_folder", ComputerCommandType.OpenFolder, "path", CapabilityIds.ShellOpenFolder),
            ("open_file", ComputerCommandType.OpenFile, "path", CapabilityIds.ShellOpenFile),
            ("open_url", ComputerCommandType.OpenUrl, "url", CapabilityIds.InternetOpenUrl) })
            yield return Tool(name, name.Replace('_', ' ') + " through the existing safe Windows service.",
                [Text(field, field == "url" ? 2048 : 260)], [capability],
                async (a, ct) => From(await controller.ExecuteAsync(new(type, Read(a, field)), ct)));
    }
    private static IAgentTool ApplicationTool(string name, string description, ToolField[] fields, ComputerCommandType type,
        Func<JsonElement, ParsedComputerCommand> command, IComputerController controller, IApplicationCatalog catalog) =>
        Tool(name, description, fields, ComputerCommandCapabilities.RequiredFor(type).Append(CapabilityIds.ApplicationSearch).Distinct().ToArray(),
            async (a, ct) => {
                var target = Read(a, "application");
                var resolution = catalog.Resolve(target);
                if (resolution.Status == ApplicationResolutionStatus.Ambiguous)
                    return new(false, "Ambiguous", "I found multiple applications. Which one do you want?", ErrorCode: "AmbiguousApplication",
                        Target: target, Choices: resolution.Matches.Take(8).Select(x => new ApplicationChoice(x.Application.Id, x.Application.DisplayName)).ToArray());
                if (resolution.Application is null) return new(false, "NotFound", "No installed application matches '" + target + "'.", Target: target);
                var result = await controller.ExecuteAsync(command(a) with { Target = resolution.Application.Id }, ct);
                return From(result) with { Target = resolution.Application.DisplayName, Data = result.AdditionalData };
            });
    public static ToolResult From(ComputerActionResult result) => new(result.Success, result.Status.ToString(), result.Message,
        result.AdditionalData, result.ErrorCode, Target: result.Target, Choices: ParseChoices(result));
    private static IReadOnlyList<ApplicationChoice>? ParseChoices(ComputerActionResult result)
    {
        if (result.ErrorCode != "AmbiguousApplication" || result.AdditionalData is null ||
            !result.AdditionalData.TryGetValue("ApplicationChoices", out var text)) return null;
        return text.Split(";;").Select(x => x.Split('|', 2)).Where(x => x.Length == 2).Select(x => new ApplicationChoice(x[0], x[1])).ToArray();
    }
    private static ToolResult MissingWorkspace(JsonElement a) => new(false, "NotFound", "No matching saved workspace exists.", Target: Read(a, "workspace"));
    private static string Read(JsonElement a, string name) => a.GetProperty(name).GetString()!.Trim();
    private static ToolField Text(string name, int max = 120) => new(name, "string", max);
    private static ToolField Number(string name, int min, int max) => new(name, "integer", max, min);
    private static ToolField EnumField(string name, params string[] values) => new(name, "string", 120, Values: values);
    private static IAgentTool Tool(string name, string description, ToolField[] fields, IReadOnlyList<string> capabilities,
        Func<JsonElement, CancellationToken, Task<ToolResult>> execute, Func<JsonElement, CancellationToken, Task<IReadOnlyList<string>>>? dynamicCapabilities = null) =>
        new RegisteredTool(name, description, fields, capabilities, execute, dynamicCapabilities);
    private sealed record ToolField(string Name, string Type, int Max, int Min = 1, string[]? Values = null);
    private sealed class RegisteredTool : IAgentTool
    {
        private readonly ToolField[] _fields;
        private readonly IReadOnlyList<string> _capabilities;
        private readonly Func<JsonElement, CancellationToken, Task<ToolResult>> _execute;
        private readonly Func<JsonElement, CancellationToken, Task<IReadOnlyList<string>>>? _dynamic;
        public RegisteredTool(string name, string description, ToolField[] fields, IReadOnlyList<string> capabilities,
            Func<JsonElement, CancellationToken, Task<ToolResult>> execute, Func<JsonElement, CancellationToken, Task<IReadOnlyList<string>>>? dynamic)
        {
            _fields = fields; _capabilities = capabilities; _execute = execute; _dynamic = dynamic;
            var properties = fields.ToDictionary(f => f.Name, f => (object)(f.Values is not null
                ? new { type = f.Type, @enum = f.Values }
                : f.Type == "integer" ? (object)new { type = f.Type, minimum = f.Min, maximum = f.Max }
                : f.Type == "array" ? (object)new { type = "array", items = new { type = "string", minLength = 1, maxLength = 120 }, minItems = 1, maxItems = f.Max }
                : new { type = f.Type, minLength = f.Min, maxLength = f.Max }));
            Definition = new(name, description, JsonSerializer.SerializeToElement(new { type = "object", properties,
                required = fields.Select(f => f.Name).ToArray(), additionalProperties = false }));
        }
        public AiToolDefinition Definition { get; }
        public Task<IReadOnlyList<string>> RequiredCapabilitiesAsync(JsonElement a, CancellationToken cancellationToken = default) =>
            _dynamic?.Invoke(a, cancellationToken) ?? Task.FromResult(_capabilities);
        public string? Target(JsonElement a) => _fields.Length == 0 ? null : a.GetProperty(_fields[0].Name).ToString();
        public void Validate(JsonElement a)
        {
            if (a.ValueKind != JsonValueKind.Object) throw new InvalidDataException("Arguments must be an object.");
            var names = a.EnumerateObject().Select(p => p.Name).ToArray();
            if (names.Length != _fields.Length || names.Distinct().Count() != names.Length ||
                names.Any(n => !_fields.Any(f => f.Name == n))) throw new InvalidDataException("Arguments do not match the tool schema.");
            foreach (var f in _fields)
            {
                var v = a.GetProperty(f.Name);
                if (f.Type == "array")
                {
                    if (v.ValueKind != JsonValueKind.Array || v.GetArrayLength() < 1 || v.GetArrayLength() > f.Max ||
                        v.EnumerateArray().Any(x => x.ValueKind != JsonValueKind.String || string.IsNullOrWhiteSpace(x.GetString()) ||
                            x.GetString()!.Length > 120 || x.GetString()!.Any(char.IsControl)))
                        throw new InvalidDataException("Invalid application list.");
                }
                else if (f.Type == "integer")
                {
                    if (v.ValueKind != JsonValueKind.Number || !v.TryGetInt32(out var n) || n < f.Min || n > f.Max) throw new InvalidDataException("Invalid numeric argument.");
                }
                else if (v.ValueKind != JsonValueKind.String || string.IsNullOrWhiteSpace(v.GetString()) ||
                    v.GetString()!.Length > f.Max || v.GetString()!.Any(char.IsControl) ||
                    f.Values is not null && !f.Values.Contains(v.GetString(), StringComparer.Ordinal))
                    throw new InvalidDataException("Invalid text argument.");
                if (f.Name == "url" && (!Uri.TryCreate(v.GetString(), UriKind.Absolute, out var uri) ||
                    uri.Scheme is not ("https" or "http") || !string.IsNullOrEmpty(uri.UserInfo)))
                    throw new InvalidDataException("Only HTTP/HTTPS links without embedded credentials are supported.");
                if (f.Name == "path" && !Path.IsPathFullyQualified(Environment.ExpandEnvironmentVariables(v.GetString()!)))
                    throw new InvalidDataException("Use an absolute path.");
            }
        }
        public Task<ToolResult> ExecuteAsync(JsonElement a, CancellationToken cancellationToken = default) => _execute(a, cancellationToken);
    }
}
