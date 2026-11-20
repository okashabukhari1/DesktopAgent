using System.Text.Json;
using DesktopAgent.Core.Enums;

namespace DesktopAgent.Core.Ai;

public sealed record AiMessage(string Role, string Content, string? ToolCallId = null, string? Name = null, IReadOnlyList<AiToolCall>? ToolCalls = null);
public sealed record AiToolCall(string Id, string Name, string ArgumentsJson);
public sealed record AiToolDefinition(string Name, string Description, JsonElement Parameters);
public sealed record AiUsage(int? InputTokens, int? OutputTokens, string? Model, long LatencyMilliseconds);
public sealed record AiProviderResponse(string Text, IReadOnlyList<AiToolCall> ToolCalls, AiUsage? Usage = null);
public sealed record AssistantProgress(string Message, bool IsComplete = false, bool IsError = false, bool IsStreaming = false);
public sealed record AssistantReply(string Text, bool UsedAi, bool Cancelled = false);
public sealed record ApplicationChoice(string Id, string Name, string Kind = "application");
public sealed record ToolResult(bool Success, string Status, string Message, object? Data = null, string? ErrorCode = null,
    long DurationMilliseconds = 0, string? Target = null, IReadOnlyList<ApplicationChoice>? Choices = null,
    IReadOnlyList<DesktopAgent.Core.Projects.CodeSourceReference>? Sources = null);
public sealed record ConversationEntry(string Speaker, string Text, ToolResult? Action = null);
public sealed class ConversationContext
{
    public Guid ActiveConversationId { get; set; } = Guid.NewGuid();
    public string? LastReferencedApplication { get; set; }
    public string? LastReferencedWorkspace { get; set; }
    public string? LastTool { get; set; }
    public ToolResult? LastToolResult { get; set; }
    public DesktopAgent.Core.Projects.CodeSourceReference? LastReferencedFile { get; set; }
}
public enum AiConnectionState { NotConfigured, NotTested, Testing, Connected, AuthenticationFailed, NetworkError, ProviderError, InvalidModel, Timeout, Disabled }
public sealed record AiConnectionStatus(AiConnectionState State, string Message, string? ConfigurationId = null);

public interface IAiConnectionService
{
    event EventHandler? Changed;
    AiConnectionStatus Status { get; }
    void Set(AiConnectionStatus status);
}

public interface IAiProvider
{
    string Name { get; }
    Task<AiProviderResponse> CompleteAsync(IReadOnlyList<AiMessage> messages, IReadOnlyList<AiToolDefinition> tools,
        IProgress<string>? textDeltaProgress = null, CancellationToken cancellationToken = default);
    Task<string> TestConnectionAsync(CancellationToken cancellationToken = default);
    Task<IReadOnlyList<string>> GetModelsAsync(CancellationToken cancellationToken = default);
}
public interface ICredentialStore
{
    bool IsAvailable { get; }
    bool Exists(string name);
    void Save(string name, string secret);
    string? Read(string name);
    void Delete(string name);
}
public interface IAgentTool
{
    AiToolDefinition Definition { get; }
    Task<IReadOnlyList<string>> RequiredCapabilitiesAsync(JsonElement arguments, CancellationToken cancellationToken = default);
    async Task<AgentToolPreparation> PrepareAsync(JsonElement arguments, CancellationToken cancellationToken = default) =>
        new(await RequiredCapabilitiesAsync(arguments, cancellationToken));
    string? CurrentSnapshot(JsonElement arguments) => null;
    string? CurrentSnapshot(JsonElement arguments, AgentToolPreparation preparation) => CurrentSnapshot(arguments);
    Task<ToolResult> ExecutePreparedAsync(JsonElement arguments, AgentToolPreparation preparation, CancellationToken cancellationToken = default) => ExecuteAsync(arguments, cancellationToken);
    void Validate(JsonElement arguments);
    string? Target(JsonElement arguments);
    Task<ToolResult> ExecuteAsync(JsonElement arguments, CancellationToken cancellationToken = default);
}
public sealed record AgentToolPreparation(IReadOnlyList<string> Capabilities, string? Snapshot = null,
    JsonElement? PreparedArguments = null, object? ExecutionContext = null);
public interface IAgentToolRouter
{
    IReadOnlyList<AiToolDefinition> Definitions { get; }
    Task<ToolResult> InvokeAsync(AiToolCall call, CancellationToken cancellationToken = default);
}
public interface IAssistantConversationService
{
    event EventHandler? Changed;
    IReadOnlyList<ConversationEntry> Entries { get; }
    ConversationContext Context { get; }
    bool IsBusy { get; }
    CharacterState State { get; }
    Task<AssistantReply> SendAsync(string input, IProgress<AssistantProgress>? progress = null, CancellationToken cancellationToken = default);
    Task<AssistantReply> ChooseApplicationAsync(string applicationId, CancellationToken cancellationToken = default);
    Task<AssistantReply> OpenSourceAsync(DesktopAgent.Core.Projects.CodeSourceReference source, CancellationToken cancellationToken = default) =>
        Task.FromResult(new AssistantReply("Source opening is unavailable.", false));
    void Stop();
    void Reset();
}
