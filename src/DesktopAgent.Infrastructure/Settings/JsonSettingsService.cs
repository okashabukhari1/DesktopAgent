using System.Text.Json;
using System.Text.Json.Serialization;
using DesktopAgent.Core.Interfaces;
using DesktopAgent.Core.Models;
using Microsoft.Extensions.Logging;

namespace DesktopAgent.Infrastructure.Settings;

public sealed class JsonSettingsService : ISettingsService
{
    private readonly ILogger<JsonSettingsService> _logger;
    private readonly JsonSerializerOptions _jsonOptions = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() }
    };

    public JsonSettingsService(ILogger<JsonSettingsService> logger, string? baseDirectory = null)
    {
        _logger = logger;
        var root = baseDirectory ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "DesktopAgent");
        SettingsPath = Path.Combine(root, "settings.json");
    }

    public AppSettings Current { get; private set; } = new();
    public string SettingsPath { get; }

    public async Task<AppSettings> LoadAsync(CancellationToken cancellationToken = default)
    {
        if (!File.Exists(SettingsPath)) return Current = new AppSettings();

        try
        {
            await using var stream = File.OpenRead(SettingsPath);
            Current = await JsonSerializer.DeserializeAsync<AppSettings>(stream, _jsonOptions, cancellationToken)
                ?? new AppSettings();
            Sanitize(Current);
        }
        catch (Exception exception) when (exception is JsonException or IOException or UnauthorizedAccessException)
        {
            _logger.LogWarning(exception, "Settings file was invalid; defaults will be used");
            Current = new AppSettings();
        }

        return Current;
    }

    public async Task SaveAsync(AppSettings settings, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(settings);
        Sanitize(settings);
        var directory = Path.GetDirectoryName(SettingsPath)!;
        Directory.CreateDirectory(directory);
        var temporaryPath = SettingsPath + ".tmp";

        try
        {
            await using (var stream = File.Create(temporaryPath))
                await JsonSerializer.SerializeAsync(stream, settings, _jsonOptions, cancellationToken);
            File.Move(temporaryPath, SettingsPath, true);
            Current = settings;
            _logger.LogInformation("SettingsChanged");
        }
        finally
        {
            if (File.Exists(temporaryPath)) File.Delete(temporaryPath);
        }
    }

    private static void Sanitize(AppSettings settings)
    {
        settings.Ai ??= new AiProviderSettings();
        settings.Projects ??= new();
        settings.FileIntelligence ??= new();
        settings.Developer ??= new();
        settings.ComputerUse ??= new();
        settings.ComputerUse.Sanitize();
        settings.Developer.Sanitize();
        settings.FileIntelligence.Sanitize();
        settings.Projects.Locations ??= [];
        settings.Projects.Locations = settings.Projects.Locations.Where(x => x is not null && !string.IsNullOrWhiteSpace(x.Path)).Take(32).ToList();
        foreach (var location in settings.Projects.Locations) location.MaxDepth = Math.Clamp(location.MaxDepth, 0, 8);
        settings.CharacterSize = Math.Clamp(settings.CharacterSize, 96, 240);
        settings.AnimationIntensity = Math.Clamp(settings.AnimationIntensity, 0, 1);
        settings.Ai.Provider = string.IsNullOrWhiteSpace(settings.Ai.Provider) ? "OpenAI-compatible" : settings.Ai.Provider.Trim();
        settings.Ai.Model = string.IsNullOrWhiteSpace(settings.Ai.Model) ? "gpt-4o-mini" : settings.Ai.Model.Trim();
        settings.Ai.BaseUrl = Uri.TryCreate(settings.Ai.BaseUrl, UriKind.Absolute, out var providerUri)
            && (providerUri.Scheme == Uri.UriSchemeHttps || (providerUri.Scheme == Uri.UriSchemeHttp && providerUri.IsLoopback))
            && string.IsNullOrEmpty(providerUri.UserInfo) && string.IsNullOrEmpty(providerUri.Query) && string.IsNullOrEmpty(providerUri.Fragment)
            ? providerUri.ToString().TrimEnd('/') : "https://api.openai.com/v1";
        settings.Ai.TimeoutSeconds = Math.Clamp(settings.Ai.TimeoutSeconds, 10, 180);
    }
}
