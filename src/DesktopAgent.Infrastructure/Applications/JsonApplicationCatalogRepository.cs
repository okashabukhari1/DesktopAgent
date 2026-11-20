using System.Text.Json;
using System.Text.Json.Serialization;
using DesktopAgent.Core.Applications.Interfaces;
using DesktopAgent.Core.Applications.Models;
using Microsoft.Extensions.Logging;

namespace DesktopAgent.Infrastructure.Applications;

public sealed class JsonApplicationCatalogRepository : IApplicationCatalogRepository
{
    private readonly ILogger<JsonApplicationCatalogRepository> _logger;
    private readonly string _filePath;
    private readonly JsonSerializerOptions _options = new() { WriteIndented = true, PropertyNameCaseInsensitive = true, Converters = { new JsonStringEnumConverter() } };

    public JsonApplicationCatalogRepository(ILogger<JsonApplicationCatalogRepository> logger, string? dataDirectory = null)
    {
        _logger = logger;
        var directory = dataDirectory ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "DesktopAgent");
        _filePath = Path.Combine(directory, "applications.json");
    }

    public async Task<ApplicationCatalogDocument> LoadAsync(CancellationToken cancellationToken = default)
    {
        if (!File.Exists(_filePath)) return new();
        try
        {
            await using var stream = File.OpenRead(_filePath);
            var document = await JsonSerializer.DeserializeAsync<ApplicationCatalogDocument>(stream, _options, cancellationToken);
            if (document is null || document.Version != ApplicationCatalogDocument.CurrentVersion) return new();
            document.Applications ??= [];
            _logger.LogInformation("ApplicationCatalogLoaded Count={Count}", document.Applications.Count);
            return document;
        }
        catch (Exception exception) when (exception is IOException or JsonException or UnauthorizedAccessException)
        {
            _logger.LogWarning(exception, "ApplicationCatalogLoadFailed");
            return new();
        }
    }

    public async Task SaveAsync(ApplicationCatalogDocument document, CancellationToken cancellationToken = default)
    {
        var directory = Path.GetDirectoryName(_filePath)!;
        Directory.CreateDirectory(directory);
        var temporary = _filePath + ".tmp";
        try
        {
            await using (var stream = new FileStream(temporary, FileMode.Create, FileAccess.Write, FileShare.None, 8192, true))
                await JsonSerializer.SerializeAsync(stream, document, _options, cancellationToken);
            File.Move(temporary, _filePath, true);
            _logger.LogInformation("ApplicationCatalogSaved Count={Count}", document.Applications.Count);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            _logger.LogWarning(exception, "ApplicationCatalogSaveFailed Count={Count}", document.Applications.Count);
        }
        finally
        {
            try { if (File.Exists(temporary)) File.Delete(temporary); }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException) { }
        }
    }
}
