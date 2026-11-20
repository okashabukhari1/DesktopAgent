using System.Text.Json;
using System.Text.Json.Serialization;
using DesktopAgent.Core.Automation.Interfaces;
using DesktopAgent.Core.Automation.Models;
using Microsoft.Extensions.Logging;

namespace DesktopAgent.Infrastructure.Automation;

public sealed class JsonActionHistoryService : IActionHistoryService
{
    private const int Capacity = 200;
    private readonly object _sync = new();
    private readonly ILogger<JsonActionHistoryService> _logger;
    private readonly List<ActionHistoryEntry> _entries = [];
    private readonly JsonSerializerOptions _jsonOptions = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() }
    };

    public JsonActionHistoryService(ILogger<JsonActionHistoryService> logger, string? baseDirectory = null)
    {
        _logger = logger;
        var root = baseDirectory ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "DesktopAgent");
        HistoryPath = Path.Combine(root, "activity.json");
    }

    public string HistoryPath { get; }
    public event EventHandler<ActionHistoryEntry>? EntryAdded;

    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        if (!File.Exists(HistoryPath)) return;
        try
        {
            await using var input = File.OpenRead(HistoryPath);
            var saved = await JsonSerializer.DeserializeAsync<List<ActionHistoryEntry>>(input, _jsonOptions, cancellationToken).ConfigureAwait(false);
            if (saved is null) return;
            lock (_sync)
            {
                _entries.Clear();
                _entries.AddRange(saved.TakeLast(Capacity));
            }
        }
        catch (Exception exception) when (exception is IOException or JsonException or UnauthorizedAccessException)
        {
            _logger.LogWarning(exception, "Activity history could not be read; it will restart empty");
        }
    }

    public IReadOnlyList<ActionHistoryEntry> GetRecent(int count = 50)
    {
        lock (_sync) return _entries.TakeLast(Math.Clamp(count, 0, Capacity)).Reverse().ToArray();
    }

    public void Add(ActionHistoryEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);
        lock (_sync)
        {
            _entries.Add(entry);
            if (_entries.Count > Capacity) _entries.RemoveRange(0, _entries.Count - Capacity);
            try { SaveUnderLock(); }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException)
            {
                _logger.LogWarning(exception, "Activity entry could not be persisted");
            }
        }
        EntryAdded?.Invoke(this, entry);
    }

    private void SaveUnderLock()
    {
        var directory = Path.GetDirectoryName(HistoryPath)!;
        Directory.CreateDirectory(directory);
        var temporaryPath = HistoryPath + ".tmp";
        try
        {
            File.WriteAllText(temporaryPath, JsonSerializer.Serialize(_entries, _jsonOptions));
            File.Move(temporaryPath, HistoryPath, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporaryPath)) File.Delete(temporaryPath);
        }
    }
}
