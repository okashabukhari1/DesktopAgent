using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading.Channels;
using DesktopAgent.Core.Developer;
using DesktopAgent.Core.Interfaces;
using DesktopAgent.Core.Permissions;
using DesktopAgent.Core.Projects;
using DesktopAgent.Core.Automation.Interfaces;
using DesktopAgent.Core.Automation.Enums;
using DesktopAgent.Core.Automation.Models;
using DesktopAgent.Infrastructure.Projects;
using Microsoft.Extensions.Logging;

namespace DesktopAgent.Infrastructure.Developer;

public sealed class DeveloperSupervisor : IDeveloperEventBridge, IDeveloperSessionManager, IDisposable
{
    private sealed record Envelope(DeveloperEvent Event, DeveloperRisk Risk, TaskCompletionSource<DeveloperBridgeReply> Reply);
    private sealed class Session(DeveloperEvent start)
    {
        public DeveloperEvent Start = start;
        public List<DeveloperEvent> Events = [];
        public DeveloperSessionState State = DeveloperSessionState.Starting;
        public DateTimeOffset? End;
    }
    private sealed record Pending(DeveloperPermission Card, DeveloperRisk Risk, DeveloperEvent Event, TaskCompletionSource<DeveloperBridgeReply> Reply);
    private readonly object _sync = new();
    private readonly Channel<Envelope> _queue = Channel.CreateBounded<Envelope>(new BoundedChannelOptions(256) { SingleReader = true, FullMode = BoundedChannelFullMode.Wait });
    private readonly Dictionary<string, Session> _sessions = new(StringComparer.Ordinal);
    private readonly Dictionary<Guid, Pending> _pending = [];
    private readonly CancellationTokenSource _lifetime = new();
    private readonly DeveloperEventNormalizer _normalizer;
    private readonly ISettingsService _settings;
    private readonly IPolicyService _policy;
    private readonly IProjectCatalog _projects;
    private readonly ICharacterService _character;
    private readonly IActionHistoryService _history;
    private readonly ISecretProtection _secrets;
    private readonly ILogger<DeveloperSupervisor> _logger;
    private readonly string _storage;
    private Task? _consumer;
    private Task? _flushTask;
    private int _flushScheduled;
    private long _revision;
    private Guid? _lastReaction;
    private DateTimeOffset _rateWindow;
    private int _rateCount;
    public event EventHandler? Changed;
    public DateTimeOffset? LastReceivedAt { get; private set; }
    public DeveloperSupervisor(DeveloperEventNormalizer normalizer, ISettingsService settings, IPolicyService policy,
        IProjectCatalog projects, ICharacterService character, IActionHistoryService history, ISecretProtection secrets,
        ILogger<DeveloperSupervisor> logger, string? baseDirectory = null)
    {
        _normalizer = normalizer; _settings = settings; _policy = policy; _projects = projects; _character = character;
        _history = history; _secrets = secrets; _logger = logger;
        _storage = Path.Combine(baseDirectory ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "DesktopAgent"), "developer-events.json");
    }
    public IReadOnlyList<DeveloperSession> Sessions { get { lock (_sync) return _sessions.Select(p => Snapshot(p.Key, p.Value)).OrderByDescending(s => s.LastActivity).ToArray(); } }
    public IReadOnlyList<DeveloperPermission> PendingPermissions { get { lock (_sync) return _pending.Values.Select(p => p.Card).ToArray(); } }
    public async Task StartAsync(CancellationToken ct = default)
    {
        if (_consumer is not null) return;
        await _projects.InitializeAsync(ct);
        try
        {
            if (File.Exists(_storage))
            {
                await using var stream = new FileStream(_storage, FileMode.Open, FileAccess.Read, FileShare.Read, 4096, true);
                if (stream.Length <= 8388608)
                {
                    var events = await JsonSerializer.DeserializeAsync<DeveloperEvent[]>(stream, cancellationToken: ct) ?? [];
                    lock (_sync)
                    {
                        foreach (var e in events.TakeLast(2048).Where(ValidStored).OrderBy(e => e.Timestamp))
                        {
                            var cleaned = e with { CommandSummary = e.CommandSummary is null ? null : _secrets.Redact(e.CommandSummary, out _) };
                            Track(cleaned, recovery: true);
                        }
                        foreach (var s in _sessions.Values)
                            if (s.State is not (DeveloperSessionState.Completed or DeveloperSessionState.Failed or DeveloperSessionState.Cancelled)) s.State = DeveloperSessionState.Disconnected;
                        Prune();
                    }
                }
            }
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException or ArgumentException) { _logger.LogWarning("DeveloperRecoveryFailed Type={Type}", ex.GetType().Name); }
        _consumer = Task.Run(ConsumeAsync);
        Notify();
    }
    public async Task<DeveloperBridgeReply> ReceiveEventAsync(string source, ReadOnlyMemory<byte> payload, CancellationToken cancellationToken = default)
    {
        if (_consumer is null || !_settings.Current.Developer.Enabled || _policy.Evaluate(DeveloperCapabilityIds.Observe).Outcome != PolicyOutcome.Allowed) return new(false);
        var normalized = _normalizer.Normalize(source, payload);
        if (normalized is not { } n) return new(false);
        lock (_sync)
        {
            if (DateTimeOffset.UtcNow - _rateWindow > TimeSpan.FromSeconds(1)) { _rateWindow = DateTimeOffset.UtcNow; _rateCount = 0; }
            if (++_rateCount > 100) return new(false);
        }
        var reply = new TaskCompletionSource<DeveloperBridgeReply>(TaskCreationOptions.RunContinuationsAsynchronously);
        if (!_queue.Writer.TryWrite(new(n.Event, n.Risk, reply))) return new(false);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _lifetime.Token);
        linked.CancelAfter(n.Event.EventType == DeveloperEventType.PermissionRequested ? TimeSpan.FromSeconds(45) : TimeSpan.FromSeconds(2));
        try { return await reply.Task.WaitAsync(linked.Token); }
        catch (OperationCanceledException)
        {
            // A cancelled relay can never leave an approvable stale permission card.
            lock (_sync)
            {
                foreach (var item in _pending.Where(p => p.Value.Reply == reply).ToArray()) _pending.Remove(item.Key);
                reply.TrySetResult(new(false));
                foreach (var s in _sessions.Values.Where(s => s.State == DeveloperSessionState.WaitingForPermission))
                    if (!_pending.Values.Any(p => p.Card.SessionId == Key(s.Start))) s.State = DeveloperSessionState.WaitingForUser;
            }
            Notify(); return new(false);
        }
    }
    private async Task ConsumeAsync()
    {
        try
        {
            await foreach (var item in _queue.Reader.ReadAllAsync(_lifetime.Token))
            {
                lock (_sync)
                {
                    if (item.Reply.Task.IsCompleted) continue;
                    var e = item.Event; var key = Key(e);
                    if (!Track(e)) { item.Reply.TrySetResult(new(false)); continue; }
                    LastReceivedAt = DateTimeOffset.UtcNow;
                    if (e.EventType == DeveloperEventType.PermissionRequested)
                    {
                        var canAllow = !item.Risk.Blocked && e.ProjectId is not null &&
                            e.ToolName is "Read" or "Edit" or "Write" or "Bash" or "PowerShell" &&
                            (e.ToolName is "Bash" or "PowerShell" || e.FilePath is not null) && PolicyAllows(item.Risk) && SafeFilePermission(e) && _pending.Count < 8;
                        if (!canAllow)
                        {
                            _sessions[key].State = DeveloperSessionState.WaitingForUser;
                            // Do not claim a native denial when our bridge declines to participate.
                            item.Reply.TrySetResult(new(true));
                        }
                        else
                        {
                            var card = new DeveloperPermission(e.Id, key, e.ToolName!, e.CommandSummary ?? e.FilePath ?? "Tool permission",
                                item.Risk.Risk, item.Risk.Capabilities.ToArray(), item.Risk.Reason, true, DateTimeOffset.UtcNow.AddSeconds(45));
                            _pending.Add(card.Id, new(card, item.Risk, e, item.Reply));
                        }
                    }
                    else item.Reply.TrySetResult(new(true));
                    Prune();
                }
                Notify();
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception ex) { _logger.LogWarning("DeveloperConsumerFailed Type={Type}", ex.GetType().Name); }
    }
    public bool Decide(Guid requestId, bool allowOnce)
    {
        lock (_sync)
        {
            if (!_pending.Remove(requestId, out var p)) return false;
            var s = _sessions.GetValueOrDefault(p.Card.SessionId);
            if (s is null || p.Card.ExpiresAt <= DateTimeOffset.UtcNow || p.Reply.Task.IsCompleted || !_settings.Current.Developer.Enabled ||
                _policy.Evaluate(DeveloperCapabilityIds.Observe).Outcome != PolicyOutcome.Allowed) { p.Reply.TrySetResult(new(false)); Notify(); return false; }
            // Re-evaluate catalog identity and policy at click time. Never accept an LLM decision.
            var project = s.Start.ProjectId is { } id ? _projects.Find(id) : null;
            if (allowOnce && (!p.Card.CanAllow || !PolicyAllows(p.Risk) || !SafeFilePermission(p.Event) || project is null ||
                !(s.Start.ProjectPath.Equals(project.Path, StringComparison.OrdinalIgnoreCase) || s.Start.ProjectPath.StartsWith(project.Path.TrimEnd('\\') + "\\", StringComparison.OrdinalIgnoreCase))))
            { p.Reply.TrySetResult(new(true)); s.State = DeveloperSessionState.WaitingForUser; Notify(); return false; }
            var e = p.Event with { Id = Guid.NewGuid(), Timestamp = DateTimeOffset.UtcNow,
                EventType = allowOnce ? DeveloperEventType.PermissionApproved : DeveloperEventType.PermissionDenied };
            s.Events.Add(e); if (s.Events.Count > 128) s.Events.RemoveAt(0);
            s.State = _pending.Values.Any(other => other.Card.SessionId == p.Card.SessionId) ? DeveloperSessionState.WaitingForPermission : allowOnce ? DeveloperSessionState.Working : DeveloperSessionState.WaitingForUser;
            p.Reply.TrySetResult(new(true, allowOnce ? "allow" : "deny"));
            Notify(); return true;
        }
    }
    private bool PolicyAllows(DeveloperRisk risk) => risk.Capabilities.All(c => _policy.Evaluate(c).Outcome is PolicyOutcome.Allowed or PolicyOutcome.ConfirmationRequired);
    private bool SafeFilePermission(DeveloperEvent e)
    {
        if (e.ToolName != "Read") return true;
        try
        {
            if (e.ProjectId is not { } id || _projects.Find(id) is not { } project || e.FilePath is null) return false;
            bool Excluded(string name) => ProjectDiscoveryService.Exclusions.Contains(name, StringComparer.OrdinalIgnoreCase) ||
                _settings.Current.FileIntelligence.AdditionalExcludedDirectories.Contains(name, StringComparer.OrdinalIgnoreCase);
            using var guard = new ProjectPathGuard(project.Path, e.FilePath, false, Excluded, _secrets);
            using var handle = guard.OpenRead(); // validates physical path/hard links; never reads content
            return true;
        }
        catch (Exception ex) when (ex is ProjectFileException or IOException or UnauthorizedAccessException or ArgumentException) { return false; }
    }
    private bool Track(DeveloperEvent e, bool recovery = false)
    {
        var key = Key(e);
        if (!_sessions.TryGetValue(key, out var s))
        {
            if (e.EventType != DeveloperEventType.SessionStarted) return false;
            if (_sessions.Count >= 64) { var old = _sessions.Where(p => !_pending.Values.Any(x => x.Card.SessionId == p.Key)).OrderBy(p => p.Value.Events.LastOrDefault()?.Timestamp).FirstOrDefault(); if (old.Key is null) return false; _sessions.Remove(old.Key); }
            s = new(e); _sessions.Add(key, s);
        }
        if (!s.Start.ProjectPath.Equals(e.ProjectPath, StringComparison.OrdinalIgnoreCase) || s.Start.ProjectId != e.ProjectId) return false;
        if (!recovery && s.End is not null && e.EventType != DeveloperEventType.SessionStarted) return false;
        if (e.EventType == DeveloperEventType.SessionStarted)
        {
            s.End = null;
            foreach (var p in _pending.Where(p => p.Value.Card.SessionId == key).ToArray()) { p.Value.Reply.TrySetResult(new(false)); _pending.Remove(p.Key); }
        }
        // A completion/result cannot invent a command or read/modify operation that was never requested.
        if (!recovery && e.EventType is DeveloperEventType.CommandCompleted or DeveloperEventType.CommandFailed or DeveloperEventType.FileRead or DeveloperEventType.FileModified or DeveloperEventType.FileCreated)
            if (e.ToolUseId is null || !s.Events.Any(x => x.ToolUseId == e.ToolUseId && x.ToolName == e.ToolName && x.FilePath == e.FilePath && x.CommandSummary == e.CommandSummary && x.EventType is DeveloperEventType.CommandRequested or DeveloperEventType.ToolRequested)) return false;
        if (!recovery && e.ToolUseId is not null && s.Events.Any(x => x.ToolUseId == e.ToolUseId && x.EventType == e.EventType)) return false;
        s.Events.Add(e); if (s.Events.Count > 128) s.Events.RemoveRange(0, s.Events.Count - 128);
        s.State = e.EventType switch {
            DeveloperEventType.SessionStarted => DeveloperSessionState.Starting,
            DeveloperEventType.PermissionRequested => DeveloperSessionState.WaitingForPermission,
            DeveloperEventType.TurnCompleted or DeveloperEventType.WaitingForUser or DeveloperEventType.PermissionDenied => DeveloperSessionState.WaitingForUser,
            DeveloperEventType.SessionCompleted => DeveloperSessionState.Completed,
            DeveloperEventType.SessionFailed => DeveloperSessionState.Failed,
            DeveloperEventType.SessionCancelled => DeveloperSessionState.Cancelled,
            _ => DeveloperSessionState.Working };
        if (s.State is DeveloperSessionState.Completed or DeveloperSessionState.Failed or DeveloperSessionState.Cancelled) s.End = e.Timestamp;
        if (e.EventType != DeveloperEventType.PermissionRequested)
            foreach (var p in _pending.Where(p => p.Value.Card.SessionId == key && e.EventType is DeveloperEventType.SessionCompleted or DeveloperEventType.SessionFailed or DeveloperEventType.SessionCancelled).ToArray())
            { p.Value.Reply.TrySetResult(new(false)); _pending.Remove(p.Key); }
        return true;
    }
    public static string Key(DeveloperEvent e) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(e.SourceTool + ":" + e.SessionId)));
    private static DeveloperSession Snapshot(string key, Session s)
    {
        var events = s.Events.ToArray();
        var commands = events.Where(e => e.CommandSummary is not null && e.ToolUseId is not null && e.EventType is DeveloperEventType.CommandRequested or DeveloperEventType.CommandCompleted or DeveloperEventType.CommandFailed)
            .GroupBy(e => e.ToolUseId!).Select(g => { var last = g.Last(); return new DeveloperCommand(g.Key, last.CommandSummary!, last.Category ?? "Unknown", last.EventType == DeveloperEventType.CommandFailed ? DeveloperOperationStatus.Failed :
                last.EventType == DeveloperEventType.CommandRequested ? DeveloperOperationStatus.Requested : last.ExitCode == 0 ? DeveloperOperationStatus.Succeeded : last.ExitCode.HasValue ? DeveloperOperationStatus.Failed : DeveloperOperationStatus.Unknown, last.ExitCode); }).ToArray();
        string[] Files(DeveloperEventType kind) => events.Where(e => e.EventType == kind && e.FilePath is not null).Select(e => e.FilePath!).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        var modified = Files(DeveloperEventType.FileModified); var created = Files(DeveloperEventType.FileCreated);
        var summary = $"{s.Start.SourceTool}: {s.State}. Tracked window: {modified.Length} modified, {created.Length} created, {commands.Length} commands. " +
            "Build: " + (commands.LastOrDefault(c => c.Category == "Build")?.Status.ToString() ?? "Unknown") + ". Tests: " + (commands.LastOrDefault(c => c.Category == "Tests")?.Status.ToString() ?? "Unknown") + ". Counts are bounded, not a whole-session audit.";
        return new(key, s.Start.SourceTool, s.Start.SessionId, s.Start.ProjectPath, s.Start.ProjectId, s.Start.Timestamp,
            events.LastOrDefault()?.Timestamp ?? s.Start.Timestamp, s.End, s.State, events, Files(DeveloperEventType.FileRead), modified, created, commands, summary);
    }
    private void Prune()
    {
        var cutoff = DateTimeOffset.UtcNow.AddDays(-_settings.Current.Developer.RetentionDays);
        foreach (var p in _sessions.Where(p => (p.Value.Events.LastOrDefault()?.Timestamp ?? p.Value.Start.Timestamp) < cutoff && !_pending.Values.Any(x => x.Card.SessionId == p.Key)).ToArray()) _sessions.Remove(p.Key);
    }
    private static bool ValidStored(DeveloperEvent e) => e is not null && e.SourceTool is "claude-code" or "opencode" && e.SessionId is { Length: > 0 and <= 128 } &&
        e.SessionId.All(c => char.IsAsciiLetterOrDigit(c) || c is '_' or '-' or '.' or ':') && DeveloperEventNormalizer.ValidAbsolute(e.ProjectPath) &&
        Enum.IsDefined(e.EventType) && Enum.IsDefined(e.Risk) && e.Timestamp <= DateTimeOffset.UtcNow.AddMinutes(1) &&
        e.CommandSummary?.Length is null or <= 600 && e.FilePath?.Length is null or <= 1024 &&
        (e.FilePath is null || !Path.IsPathRooted(e.FilePath) && !e.FilePath.Split('\\', '/').Any(p => p is ".." or "."));
    private void Notify()
    {
        lock (_sync)
        {
            _character.SetDeveloperState(_pending.Count > 0 || _sessions.Values.Any(s => s.State == DeveloperSessionState.WaitingForUser &&
                s.Events.LastOrDefault()?.EventType == DeveloperEventType.PermissionRequested), _sessions.Values.Any(s => s.State == DeveloperSessionState.Working));
            _revision++;
            if (Interlocked.CompareExchange(ref _flushScheduled, 1, 0) != 0) return;
            _flushTask = FlushAsync();
        }
    }
    private async Task FlushAsync()
    {
        long revision = 0;
        try
        {
            await Task.Delay(500, _lifetime.Token);
            DeveloperEvent[] events;
            lock (_sync)
            {
                revision = _revision;
                var starts = _sessions.Values.Select(s => s.Start).ToArray();
                events = starts.Concat(_sessions.Values.SelectMany(s => s.Events.Where(e => e.Id != s.Start.Id))
                    .OrderBy(e => e.Timestamp).TakeLast(2048 - starts.Length)).OrderBy(e => e.Timestamp).ToArray();
            }
            Changed?.Invoke(this, EventArgs.Empty);
            Directory.CreateDirectory(Path.GetDirectoryName(_storage)!);
            var temp = _storage + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                var bytes = JsonSerializer.SerializeToUtf8Bytes(events);
                while (bytes.Length > 8388608)
                {
                    var starts = events.Where(e => e.EventType == DeveloperEventType.SessionStarted).Take(64).ToArray();
                    var recent = events.Where(e => e.EventType != DeveloperEventType.SessionStarted).TakeLast(Math.Max(1, (events.Length - starts.Length) / 2));
                    events = starts.Concat(recent).OrderBy(e => e.Timestamp).ToArray();
                    bytes = JsonSerializer.SerializeToUtf8Bytes(events);
                }
                await using (var stream = new FileStream(temp, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                    await stream.WriteAsync(bytes, _lifetime.Token);
                File.Move(temp, _storage, true);
            }
            finally { if (File.Exists(temp)) File.Delete(temp); }
            // One aggregated activity record per burst, not one per file read.
            var significant = events.LastOrDefault(e => e.Timestamp > DateTimeOffset.UtcNow.AddSeconds(-2) &&
                e.EventType is DeveloperEventType.PermissionRequested or DeveloperEventType.PermissionApproved or DeveloperEventType.PermissionDenied or DeveloperEventType.SessionCompleted or DeveloperEventType.CommandFailed);
            if (significant is not null)
            {
                var message = "Developer · " + significant.SourceTool + " · " + significant.EventType;
                _history.Add(new(DateTimeOffset.Now, ComputerCommandType.DeveloperEvent, "Developer session", significant.EventType == DeveloperEventType.CommandFailed ? ActionStatus.Failed : ActionStatus.Success, message, "Developer"));
                if (_settings.Current.Developer.Notifications && significant.EventType is DeveloperEventType.PermissionRequested or DeveloperEventType.SessionCompleted or DeveloperEventType.CommandFailed)
                    _character.ShowMessage(message + " · Open Developer to view the session.");
            }
            var reaction = events.LastOrDefault(e => e.Timestamp > DateTimeOffset.UtcNow.AddSeconds(-3) &&
                (e.EventType is DeveloperEventType.CommandFailed or DeveloperEventType.SessionFailed or DeveloperEventType.TurnCompleted ||
                 e.EventType == DeveloperEventType.CommandCompleted && e.ExitCode.HasValue));
            if (reaction is not null && _lastReaction != reaction.Id)
            {
                _lastReaction = reaction.Id;
                _character.ReactToDeveloperResult(reaction.EventType == DeveloperEventType.TurnCompleted || reaction.EventType == DeveloperEventType.CommandCompleted && reaction.ExitCode == 0);
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception ex) { _logger.LogWarning("DeveloperFlushFailed Type={Type}", ex.GetType().Name); }
        finally
        {
            lock (_sync)
            {
                Interlocked.Exchange(ref _flushScheduled, 0);
                if (_revision != revision && !_lifetime.IsCancellationRequested) Notify();
            }
        }
    }
    public void Dispose()
    {
        _lifetime.Cancel(); _queue.Writer.TryComplete();
        lock (_sync) { foreach (var p in _pending.Values) p.Reply.TrySetResult(new(false)); _pending.Clear(); }
        _character.SetDeveloperState(false, false);
    }
    public async Task StopAsync()
    {
        Dispose();
        try { if (_consumer is not null) await _consumer.WaitAsync(TimeSpan.FromSeconds(2)); } catch (TimeoutException) { }
        try { if (_flushTask is not null) await _flushTask.WaitAsync(TimeSpan.FromSeconds(2)); } catch (TimeoutException) { }
    }
}
