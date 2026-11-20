using System.Text.Json;
using DesktopAgent.Core.Ai;
using DesktopAgent.Core.Projects;
using DesktopAgent.Core.Permissions;
using DesktopAgent.Core.Workspaces.Interfaces;

namespace DesktopAgent.Infrastructure.Projects;

public static class ProjectTools
{
    // Deliberate projection: raw manifests, file trees, and source never enter tool responses.
    public static object Metadata(ProjectProfile p) => new { p.Id, p.Name, p.Path, Technologies = p.Technologies.Take(16),
        Frameworks = p.Frameworks.Take(16), Languages = p.Languages.Take(16), DetectedFiles = p.DetectedFiles.Take(8), p.PreferredEditorId, p.AssociatedWorkspaceId,
        p.IsFavorite, p.IsAvailable, p.LastOpenedAt, p.OpenCount, p.IsGitRepository, p.GitBranch, p.RepositoryRoot };
    public static IEnumerable<IAgentTool> Create(IProjectCatalog catalog, IProjectOpenService open,
        IWorkspaceService workspaces, CurrentProjectContext current)
    {
        foreach (var name in new[] { "list_projects", "search_projects", "get_project", "get_recent_projects", "get_project_technologies", "open_project", "associate_project_workspace", "get_current_project" })
            yield return new ProjectTool(name, catalog, open, workspaces, current);
    }
    private sealed class ProjectTool : IAgentTool
    {
        private readonly string _name;
        private readonly IProjectCatalog _catalog;
        private readonly IProjectOpenService _open;
        private readonly IWorkspaceService _workspaces;
        private readonly CurrentProjectContext _current;
        private readonly Dictionary<string, string> _fields;
        public AiToolDefinition Definition { get; }
        public ProjectTool(string name, IProjectCatalog catalog, IProjectOpenService open, IWorkspaceService workspaces, CurrentProjectContext current)
        {
            _name = name; _catalog = catalog; _open = open; _workspaces = workspaces; _current = current;
            _fields = name switch
            {
                "search_projects" => new() { ["query"] = "string", ["technology"] = "nullable" },
                "get_project" or "get_project_technologies" => new() { ["project"] = "string" },
                "open_project" => new() { ["project"] = "string", ["editor"] = "nullable", ["use_workspace"] = "boolean" },
                "associate_project_workspace" => new() { ["project"] = "string", ["workspace"] = "nullable" }, _ => new()
            };
            var properties = _fields.ToDictionary(f => f.Key, f => (object)new { type = f.Value == "nullable" ? (object)new[] { "string", "null" } : f.Value });
            var description = name switch
            {
                "list_projects" => "List up to 20 cached projects. No filesystem scan. Narrow with search_projects.",
                "search_projects" => "Search cached names/paths or filter technologies. query can be empty; technology is null if unused. Never infer last modified from last opened.",
                "get_project" => "Get safe local metadata for a project ID or unambiguous name.",
                "get_recent_projects" => "Get up to 10 projects previously opened through this agent, newest first. These are not observed IDE activity.",
                "get_project_technologies" => "Get locally detected technologies, frameworks, and languages, never source code.",
                "open_project" => "Open a catalogued project using its workspace and configured editor. editor is null for preferences, or an application catalog ID/name. use_workspace should normally be true, false for explicit editor-only requests. Ambiguous results require user selection.",
                "associate_project_workspace" => "Update project workspace association (confirmation required). workspace is an existing workspace ID/name or null to clear. Does not launch anything.",
                _ => "Get the actual session's current project; null means none. Use for it/its folder/technology/workspace, never infer from windows."
            };
            Definition = new(name, description, JsonSerializer.SerializeToElement(new { type = "object", properties, required = _fields.Keys.ToArray(), additionalProperties = false }));
        }
        public void Validate(JsonElement args)
        {
            if (args.ValueKind != JsonValueKind.Object) throw new ArgumentException();
            var seen = new HashSet<string>();
            foreach (var field in args.EnumerateObject())
            {
                if (!seen.Add(field.Name) || !_fields.TryGetValue(field.Name, out var type)) throw new ArgumentException();
                if (type == "boolean") { if (field.Value.ValueKind is not (JsonValueKind.True or JsonValueKind.False)) throw new ArgumentException(); }
                else if (type == "nullable" && field.Value.ValueKind == JsonValueKind.Null) { }
                else if (field.Value.ValueKind != JsonValueKind.String || field.Value.GetString()!.Length > 200 || field.Value.GetString()!.Any(char.IsControl)
                    || (field.Name == "project" && string.IsNullOrWhiteSpace(field.Value.GetString()))) throw new ArgumentException();
            }
            if (seen.Count != _fields.Count) throw new ArgumentException();
        }
        private static string? Text(JsonElement a, string key) => a.TryGetProperty(key, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;
        public string? Target(JsonElement args) => Text(args, "project") ?? Text(args, "query");
        private ProjectProfile? Resolve(JsonElement args) => _catalog.Resolve(Text(args, "project") ?? "").Project;
        private string Stamp(JsonElement args)
        {
            var p = Resolve(args);
            var w = p?.AssociatedWorkspaceId is { } id ? _workspaces.Find(id.ToString()) : _workspaces.Workspaces.FirstOrDefault(w => w.ProjectId == p?.Id);
            return JsonSerializer.Serialize(new { project = p, workspace = w });
        }
        public string? CurrentSnapshot(JsonElement args) => _name is "open_project" or "associate_project_workspace" ? Stamp(args) : null;
        public async Task<IReadOnlyList<string>> RequiredCapabilitiesAsync(JsonElement args, CancellationToken cancellationToken = default) =>
            (await PrepareAsync(args, cancellationToken)).Capabilities;
        public async Task<AgentToolPreparation> PrepareAsync(JsonElement args, CancellationToken cancellationToken = default)
        {
            await _catalog.InitializeAsync(cancellationToken);
            if (_name is "open_project" or "associate_project_workspace") await _workspaces.InitializeAsync(cancellationToken);
            var snapshot = CurrentSnapshot(args);
            var ids = new HashSet<string> { _name is "list_projects" or "search_projects" or "get_recent_projects" ? CapabilityIds.ProjectSearch : CapabilityIds.ProjectViewMetadata };
            ids.Add(CapabilityIds.ProjectViewMetadata);
            if (_name == "associate_project_workspace") { ids.Add(CapabilityIds.ProjectModifyCatalog); ids.Add(CapabilityIds.WorkspaceView); }
            if (_name == "open_project")
            {
                ids.Add(CapabilityIds.ProjectOpen); ids.Add(CapabilityIds.ApplicationOpen); ids.Add(CapabilityIds.ApplicationSearch);
                await _workspaces.InitializeAsync(cancellationToken);
                var p = Resolve(args);
                var w = p?.AssociatedWorkspaceId is { } id ? _workspaces.Find(id.ToString()) : _workspaces.Workspaces.FirstOrDefault(w => w.ProjectId == p?.Id);
                if (args.GetProperty("use_workspace").GetBoolean() && w is not null)
                    foreach (var capability in WorkspaceCapabilities.RequiredFor(_workspaces, w.Id.ToString())) ids.Add(capability);
            }
            return new(ids.ToArray(), snapshot);
        }
        public async Task<ToolResult> ExecuteAsync(JsonElement args, CancellationToken cancellationToken = default)
        {
            await _catalog.InitializeAsync(cancellationToken);
            if (_name == "list_projects") return new(true, "Success", _catalog.Projects.Count + " projects in catalog.",
                new { total = _catalog.Projects.Count, projects = _catalog.Search("", limit: 20).Select(x => Metadata(x.Project)).ToArray() });
            if (_name == "search_projects")
            {
                var matches = _catalog.Search(Text(args, "query") ?? "", Text(args, "technology"));
                return new(true, "Success", matches.Count + " matching projects (up to 20).", matches.Select(x => Metadata(x.Project)).ToArray());
            }
            if (_name == "get_recent_projects") return new(true, "Success", "Projects opened through this agent, newest first.",
                _catalog.Projects.Where(p => p.LastOpenedAt.HasValue).OrderByDescending(p => p.LastOpenedAt).Take(10).Select(Metadata).ToArray());
            if (_name == "get_current_project") return new(true, "Success", _current.Current?.Name ?? "No current project has been selected this session.",
                _current.Current is { } session && _catalog.Find(session.ProjectId) is { } project ? new { project = Metadata(project), session } : null);
            if (_name == "open_project") return await _open.OpenAsync(Text(args, "project")!, Text(args, "editor"), args.GetProperty("use_workspace").GetBoolean(), cancellationToken);
            var resolution = _catalog.Resolve(Text(args, "project")!);
            if (resolution.Project is not { } p)
                return new(false, resolution.Choices.Count > 0 ? "Ambiguous" : "NotFound", "Project not found or ambiguous; choose a project.",
                    Choices: resolution.Choices.Select(p => new ApplicationChoice(p.Id.ToString(), p.Name + " · " + p.Path, "project")).ToArray());
            if (_name == "get_project_technologies") return new(true, "Success", p.TechnologySummary.Length > 0 ? p.TechnologySummary : "Technology unknown.", new { p.Id, p.Name, p.Technologies, p.Frameworks, p.Languages });
            if (_name == "associate_project_workspace")
            {
                await _workspaces.InitializeAsync(cancellationToken);
                var workspace = Text(args, "workspace"); var w = workspace is null ? null : _workspaces.Find(workspace);
                if (workspace is not null && w is null) return new(false, "NotFound", "That workspace is unavailable.");
                p.AssociatedWorkspaceId = w?.Id;
                await _catalog.UpdateAsync(p, cancellationToken);
                if (_current.Current?.ProjectId == p.Id) _current.Set(p);
                return new(true, "Success", w is null ? "Project workspace association cleared." : "Associated " + p.Name + " with " + w.Name + ".", Metadata(p));
            }
            return new(true, "Success", p.Name + " · " + p.TechnologySummary, Metadata(p));
        }
    }
}
