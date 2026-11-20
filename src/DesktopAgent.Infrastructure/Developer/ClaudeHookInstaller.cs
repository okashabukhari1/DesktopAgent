using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Security.AccessControl;

namespace DesktopAgent.Infrastructure.Developer;

public sealed record HookInstallPreview(Guid Id, string ConfigPath, string Operation, string Changes, int ExistingHandlers, int ResultHandlers, bool Changed);
public sealed class ClaudeHookInstaller
{
    private sealed record Plan(HookInstallPreview Preview, byte[] Before, byte[] After, bool Existed);
    private readonly string _config, _relay;
    private Plan? _plan;
    private readonly SemaphoreSlim _gate = new(1, 1);
    public ClaudeHookInstaller(string? configPath = null, string? relayPath = null)
    {
        _config = Path.GetFullPath(configPath ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".claude", "settings.json"));
        _relay = Path.GetFullPath(relayPath ?? Path.Combine(AppContext.BaseDirectory, "agent-dev-hook.exe"));
    }
    public string ConfigPath => _config;
    public string RelayPath => _relay;
    private string Command => "\"" + _relay + "\" --desktop-agent-hook-v1";
    private JsonObject Group(string eventName) => new() { ["matcher"] = "", ["hooks"] = new JsonArray(new JsonObject {
        ["type"] = "command", ["command"] = Command, ["timeout"] = eventName == "PermissionRequest" ? 50 : 3 }) };
    private static JsonObject Parse(byte[] bytes)
    {
        using var document = JsonDocument.Parse(bytes, new() { MaxDepth = 32 });
        ValidateUnique(document.RootElement);
        var root = JsonNode.Parse(bytes) as JsonObject ?? throw new InvalidDataException("Settings must be a JSON object.");
        if (root["hooks"] is not null && root["hooks"] is not JsonObject) throw new InvalidDataException("The existing hooks section is invalid.");
        if (root["hooks"] is JsonObject hooks)
            foreach (var item in hooks)
                if (item.Value is not JsonArray array || array.Any(group => group is not JsonObject obj || obj["hooks"] is not JsonArray handlers || handlers.Any(h => h is not JsonObject)))
                    throw new InvalidDataException("Existing hook structure is invalid; repair it in Claude first.");
        return root;
    }
    private static void ValidateUnique(JsonElement e)
    {
        if (e.ValueKind == JsonValueKind.Object)
        {
            var names = new HashSet<string>();
            foreach (var p in e.EnumerateObject()) { if (!names.Add(p.Name)) throw new InvalidDataException("Duplicate setting keys."); ValidateUnique(p.Value); }
        }
        else if (e.ValueKind == JsonValueKind.Array) foreach (var p in e.EnumerateArray()) ValidateUnique(p);
    }
    private static int Count(JsonObject root) => root["hooks"] is JsonObject h ? h.Sum(p => ((JsonArray)p.Value!).Sum(g => ((JsonArray)g!["hooks"]!).Count)) : 0;
    private async Task<byte[]> ReadAsync(CancellationToken ct)
    {
        CheckPath();
        if (!File.Exists(_config)) return "{}"u8.ToArray();
        await using var file = new FileStream(_config, FileMode.Open, FileAccess.Read, FileShare.Read, 4096, true);
        if (file.Length > 1048576) throw new InvalidDataException("Claude settings exceed the 1 MiB safety limit.");
        var bytes = new byte[checked((int)file.Length)]; await file.ReadExactlyAsync(bytes, ct); return bytes;
    }
    private void CheckPath()
    {
        if (!DeveloperEventNormalizer.ValidAbsolute(_config) || !DeveloperEventNormalizer.ValidAbsolute(_relay) || _relay.Contains('"')) throw new InvalidDataException("Invalid integration path.");
        for (var p = _config; !string.IsNullOrEmpty(p); p = Path.GetDirectoryName(p))
            if ((Directory.Exists(p) || File.Exists(p)) && File.GetAttributes(p).HasFlag(FileAttributes.ReparsePoint)) throw new InvalidDataException("Linked settings paths are not supported.");
    }
    public async Task<HookInstallPreview> PreviewAsync(bool remove, CancellationToken ct = default)
    {
        await _gate.WaitAsync(ct);
        try
        {
            if (!remove && !File.Exists(_relay)) throw new InvalidDataException("Build the hook relay first; agent-dev-hook.exe is missing beside the app.");
            var before = await ReadAsync(ct); var existed = File.Exists(_config); var root = Parse(before); var count = Count(root);
            var hooks = root["hooks"] as JsonObject;
            if (hooks is null && !remove) { hooks = new(); root["hooks"] = hooks; }
            var changes = new JsonObject();
            if (hooks is not null)
                foreach (var name in DeveloperEventNormalizer.ClaudeEvents)
                {
                    if (hooks[name] is null && remove) continue;
                    var groups = hooks[name] as JsonArray ?? new JsonArray();
                    // Exact handler identity only. Mixed groups keep every unrelated handler and matcher.
                    var expected = Group(name)["hooks"]![0];
                    for (var i = groups.Count - 1; i >= 0; i--)
                    {
                        var group = (JsonObject)groups[i]!; var handlers = (JsonArray)group["hooks"]!;
                        var exactGroup = JsonNode.DeepEquals(group, Group(name));
                        var removed = false;
                        for (var j = handlers.Count - 1; j >= 0; j--) if (JsonNode.DeepEquals(handlers[j], expected)) { handlers.RemoveAt(j); removed = true; }
                        if (removed && handlers.Count == 0 && exactGroup) groups.RemoveAt(i);
                    }
                    if (!remove) groups.Add(Group(name));
                    if (groups.Count == 0) hooks.Remove(name); else hooks[name] = groups;
                    changes[name] = remove ? JsonValue.Create("Remove only exact Desktop Agent handler") : Group(name);
                }
            var after = JsonSerializer.SerializeToUtf8Bytes(root, new JsonSerializerOptions { WriteIndented = true });
            _ = Parse(after);
            var changed = !JsonNode.DeepEquals(JsonNode.Parse(before), root);
            var preview = new HookInstallPreview(Guid.NewGuid(), _config, remove ? "Remove" : "Install / Repair",
                changes.ToJsonString(new() { WriteIndented = true }), count, Count(root), changed);
            _plan = new(preview, before, after, existed); return preview;
        }
        finally { _gate.Release(); }
    }
    // Only call after the UI has shown this preview and the human explicitly confirmed it.
    public async Task<string?> ApplyConfirmedAsync(Guid previewId, CancellationToken ct = default)
    {
        await _gate.WaitAsync(ct);
        try
        {
            var plan = _plan; _plan = null;
            if (plan is null || plan.Preview.Id != previewId) throw new InvalidOperationException("Preview expired; preview again.");
            var current = await ReadAsync(ct);
            if (plan.Existed != File.Exists(_config) || !CryptographicOperations.FixedTimeEquals(SHA256.HashData(current), SHA256.HashData(plan.Before)))
                throw new InvalidOperationException("Claude settings changed after preview; preview again.");
            if (!plan.Preview.Changed) return null;
            Directory.CreateDirectory(Path.GetDirectoryName(_config)!); CheckPath();
            var originalSecurity = plan.Existed ? new FileInfo(_config).GetAccessControl(AccessControlSections.Access) : null;
            var backup = _config + ".desktop-agent-backup-" + DateTime.UtcNow.ToString("yyyyMMdd-HHmmss-fffffff") + ".json";
            // Back up exact original bytes before writing the replacement, including first-install {}.
            await using (var b = new FileStream(backup, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            { if (originalSecurity is not null) new FileInfo(backup).SetAccessControl(originalSecurity); await b.WriteAsync(plan.Before, ct); await b.FlushAsync(ct); }
            var temp = _config + ".desktop-agent-" + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                await using (var file = new FileStream(temp, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                { if (originalSecurity is not null) new FileInfo(temp).SetAccessControl(originalSecurity); await file.WriteAsync(plan.After, ct); await file.FlushAsync(ct); }
                _ = Parse(await File.ReadAllBytesAsync(temp, ct));
                // Check again immediately before the atomic replacement; never silently clobber a known concurrent edit.
                current = await ReadAsync(ct);
                if (plan.Existed != File.Exists(_config) || !CryptographicOperations.FixedTimeEquals(SHA256.HashData(current), SHA256.HashData(plan.Before))) throw new InvalidOperationException("Settings changed during installation.");
                // Match the app's existing same-directory atomic rename persistence pattern.
                File.Move(temp, _config, plan.Existed);
                _ = Parse(await ReadAsync(ct));
                return backup;
            }
            finally { if (File.Exists(temp)) File.Delete(temp); }
        }
        finally { _gate.Release(); }
    }
    public async Task<bool> IsConfiguredAsync(CancellationToken ct = default)
    {
        try
        {
            var root = Parse(await ReadAsync(ct));
            return root["disableAllHooks"]?.GetValue<bool>() != true && File.Exists(_relay) && root["hooks"] is JsonObject h &&
                DeveloperEventNormalizer.ClaudeEvents.All(name => h[name] is JsonArray groups && groups.Any(g => JsonNode.DeepEquals(g, Group(name))));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or InvalidOperationException) { return false; }
    }
}
