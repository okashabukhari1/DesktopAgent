using System.Text.Json;
using System.Text.Json.Serialization;
using DesktopAgent.Core.Workspaces.Interfaces;
using DesktopAgent.Core.Workspaces.Models;
using Microsoft.Extensions.Logging;

namespace DesktopAgent.Infrastructure.Workspaces;

public sealed class JsonWorkspaceRepository : IWorkspaceRepository
{
    private readonly ILogger<JsonWorkspaceRepository> _logger;
    private readonly string _filePath;
    private readonly JsonSerializerOptions _jsonOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter() }
    };

    public JsonWorkspaceRepository(ILogger<JsonWorkspaceRepository> logger, string? dataDirectory = null)
    {
        _logger = logger;
        var directory = dataDirectory ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "DesktopAgent");
        _filePath = Path.Combine(directory, "workspaces.json");
    }

    public async Task<IReadOnlyList<WorkspaceDefinition>> LoadAsync(CancellationToken cancellationToken = default)
    {
        if (!File.Exists(_filePath)) return [];
        try
        {
            await using var stream = File.OpenRead(_filePath);
            var document = await JsonSerializer.DeserializeAsync<WorkspaceStoreDocument>(stream, _jsonOptions, cancellationToken);
            if (document is null || document.Version is < 1 or > WorkspaceStoreDocument.CurrentVersion)
            {
                _logger.LogWarning("WorkspaceStoreUnsupported Version={Version}", document?.Version);
                return [];
            }
            var unique = new List<WorkspaceDefinition>();
            var ids = new HashSet<Guid>();
            foreach (var workspace in document.Workspaces ?? [])
            {
                NormalizeWorkspace(workspace);
                if (workspace.Id == Guid.Empty || !ids.Add(workspace.Id))
                {
                    do workspace.Id = Guid.NewGuid(); while (!ids.Add(workspace.Id));
                }
                NormalizeIds(workspace);
                unique.Add(workspace);
            }
            return unique;
        }
        catch (Exception exception) when (exception is JsonException or IOException or UnauthorizedAccessException)
        {
            _logger.LogWarning(exception, "WorkspaceStoreLoadFailed");
            return [];
        }
    }

    public async Task SaveAsync(IReadOnlyList<WorkspaceDefinition> workspaces, CancellationToken cancellationToken = default)
    {
        var directory = Path.GetDirectoryName(_filePath)!;
        Directory.CreateDirectory(directory);
        var temporaryPath = _filePath + ".tmp";
        var document = new WorkspaceStoreDocument { Workspaces = workspaces.Select(item => item.DeepCopy()).ToList() };
        try
        {
            await using (var stream = new FileStream(temporaryPath, FileMode.Create, FileAccess.Write, FileShare.None, 4096, true))
            {
                await JsonSerializer.SerializeAsync(stream, document, _jsonOptions, cancellationToken);
                await stream.FlushAsync(cancellationToken);
            }
            File.Move(temporaryPath, _filePath, true);
            _logger.LogInformation("WorkspaceStoreSaved Count={Count}", workspaces.Count);
        }
        finally
        {
            if (File.Exists(temporaryPath)) File.Delete(temporaryPath);
        }
    }

    private static void NormalizeIds(WorkspaceDefinition workspace)
    {
        foreach (var id in workspace.Applications.Where(item => item.Id == Guid.Empty)) id.Id = Guid.NewGuid();
        foreach (var id in workspace.Folders.Concat(workspace.Files).Concat(workspace.Urls).Where(item => item.Id == Guid.Empty)) id.Id = Guid.NewGuid();
        foreach (var id in workspace.PreLaunchActions.Concat(workspace.PostLaunchActions).Where(item => item.Id == Guid.Empty)) id.Id = Guid.NewGuid();
    }

    private static void NormalizeWorkspace(WorkspaceDefinition workspace)
    {
        workspace.Name ??= string.Empty;
        workspace.Description ??= string.Empty;
        workspace.IconKey ??= "Briefcase";
        workspace.Applications ??= [];
        workspace.Folders ??= [];
        workspace.Files ??= [];
        workspace.Urls ??= [];
        workspace.PreLaunchActions ??= [];
        workspace.PostLaunchActions ??= [];
        foreach (var app in workspace.Applications)
        {
            app.DisplayName ??= string.Empty;
            app.ApplicationIdentifier ??= string.Empty;
        }
        foreach (var resource in workspace.Folders.Concat(workspace.Files).Concat(workspace.Urls))
        {
            resource.DisplayName ??= string.Empty;
            resource.Value ??= string.Empty;
        }
        foreach (var action in workspace.PreLaunchActions.Concat(workspace.PostLaunchActions)) action.Target ??= string.Empty;
    }
}
