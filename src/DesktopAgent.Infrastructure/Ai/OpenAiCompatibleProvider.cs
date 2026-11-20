using System.Diagnostics;
using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using DesktopAgent.Core.Ai;
using DesktopAgent.Core.Interfaces;
using Microsoft.Extensions.Logging;

namespace DesktopAgent.Infrastructure.Ai;

public sealed class OpenAiCompatibleProvider(HttpClient http, ISettingsService settings, ICredentialStore credentials,
    IAiConnectionService? connection = null, ILogger<OpenAiCompatibleProvider>? logger = null) : IAiProvider
{
    public const string CredentialName = ProviderCredentialNames.LegacyName;
    public string Name => "OpenAI-compatible";

    public async Task<AiProviderResponse> CompleteAsync(IReadOnlyList<AiMessage> messages, IReadOnlyList<AiToolDefinition> tools,
        IProgress<string>? textDeltaProgress = null, CancellationToken cancellationToken = default)
    {
        var configuration = settings.Current.Ai.Clone();
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(configuration.TimeoutSeconds));
        var watch = Stopwatch.StartNew();
        try
        {
            using var response = await SendAsync(configuration.BaseUrl, "chat/completions", HttpMethod.Post,
                BuildBody(configuration.Model, messages, tools, true), timeout.Token);
            var text = new StringBuilder();
            var calls = new SortedDictionary<int, ToolCallBuffer>();
            int? inputTokens = null, outputTokens = null;
            string? model = null;
            var done = false;
            await using var stream = await response.Content.ReadAsStreamAsync(timeout.Token);
            using var reader = new StreamReader(stream, Encoding.UTF8);
            while (await reader.ReadLineAsync(timeout.Token) is { } line)
            {
                if (line.Length > 65536) throw Malformed();
                if (!line.StartsWith("data:", StringComparison.Ordinal)) continue;
                var payload = line[5..].Trim();
                if (payload.Length == 0) continue;
                if (payload == "[DONE]") { done = true; break; }
                using var chunk = JsonDocument.Parse(payload, new JsonDocumentOptions { MaxDepth = 24 });
                var root = chunk.RootElement;
                if (root.TryGetProperty("error", out _)) throw Malformed();
                if (root.TryGetProperty("model", out var modelValue)) model = modelValue.GetString();
                if (root.TryGetProperty("usage", out var usage) && usage.ValueKind == JsonValueKind.Object)
                {
                    if (usage.TryGetProperty("prompt_tokens", out var p) && p.TryGetInt32(out var n)) inputTokens = n;
                    if (usage.TryGetProperty("completion_tokens", out var c) && c.TryGetInt32(out var m)) outputTokens = m;
                }
                if (!root.TryGetProperty("choices", out var choices) || choices.ValueKind != JsonValueKind.Array) throw Malformed();
                if (choices.GetArrayLength() == 0) continue;
                var choice = choices[0];
                if (choice.TryGetProperty("finish_reason", out var finish) && finish.ValueKind == JsonValueKind.String &&
                    finish.GetString() is "length" or "content_filter") throw Malformed();
                if (!choice.TryGetProperty("delta", out var delta) || delta.ValueKind != JsonValueKind.Object) throw Malformed();
                if (delta.TryGetProperty("content", out var fragment) && fragment.ValueKind == JsonValueKind.String)
                {
                    text.Append(fragment.GetString());
                    if (text.Length > 32000) throw Malformed();
                    textDeltaProgress?.Report(fragment.GetString() ?? "");
                }
                if (delta.TryGetProperty("tool_calls", out var toolDeltas) && toolDeltas.ValueKind == JsonValueKind.Array)
                    foreach (var d in toolDeltas.EnumerateArray())
                    {
                        var index = d.GetProperty("index").GetInt32();
                        if (index < 0 || index > 15) throw Malformed();
                        if (!calls.TryGetValue(index, out var b)) calls[index] = b = new();
                        if (d.TryGetProperty("id", out var id)) b.Id += id.GetString();
                        if (d.TryGetProperty("function", out var function))
                        {
                            if (function.TryGetProperty("name", out var name)) b.Name += name.GetString();
                            if (function.TryGetProperty("arguments", out var arguments)) b.Arguments.Append(arguments.GetString());
                        }
                        if (b.Id.Length > 160 || b.Name.Length > 100 || b.Arguments.Length > 8192) throw Malformed();
                    }
            }
            if (!done || text.Length == 0 && calls.Count == 0) throw Malformed();
            var completed = calls.Values.Select(b => new AiToolCall(b.Id, b.Name, b.Arguments.ToString())).ToArray();
            if (completed.Any(c => string.IsNullOrWhiteSpace(c.Id) || string.IsNullOrWhiteSpace(c.Name)) ||
                completed.Select(c => c.Id).Distinct().Count() != completed.Length) throw Malformed();
            foreach (var call in completed) { using var arguments = JsonDocument.Parse(call.ArgumentsJson); }
            connection?.Set(new(AiConnectionState.Connected, "Connected", configuration.BaseUrl.TrimEnd('/') + "|" + configuration.Model));
            return new(text.ToString(), completed, new(inputTokens, outputTokens, model ?? configuration.Model, watch.ElapsedMilliseconds));
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested) { throw new AiProviderException(AiConnectionState.Timeout, "Provider timeout."); }
        catch (Exception ex) when (ex is JsonException or InvalidOperationException or KeyNotFoundException)
        { throw Malformed(); }
    }
    public async Task<string> TestConnectionAsync(CancellationToken cancellationToken = default)
    {
        connection?.Set(new(AiConnectionState.Testing, "Testing the configured model…"));
        var configuration = settings.Current.Ai.Clone();
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(configuration.TimeoutSeconds));
        try
        {
            // Verify a real completion and function calling with this model, without performing an action.
            var probe = new AiToolDefinition("connection_probe", "Check connectivity. No computer action is performed.",
                JsonSerializer.SerializeToElement(new { type = "object", properties = new { }, required = Array.Empty<string>(), additionalProperties = false }));
            var requestBody = JsonNode.Parse(BuildBody(configuration.Model, [new("user", "Call connection_probe with no arguments.")], [probe], false))!.AsObject();
            requestBody["tool_choice"] = new JsonObject { ["type"] = "function", ["function"] = new JsonObject { ["name"] = "connection_probe" } };
            using var response = await SendAsync(configuration.BaseUrl, "chat/completions", HttpMethod.Post,
                requestBody.ToJsonString(), timeout.Token);
            var body = await ReadBoundedAsync(response.Content, timeout.Token);
            using var document = JsonDocument.Parse(body);
            if (!document.RootElement.TryGetProperty("choices", out var choices) || choices.ValueKind != JsonValueKind.Array ||
                choices.GetArrayLength() == 0 || !choices[0].TryGetProperty("message", out var message) ||
                !message.TryGetProperty("tool_calls", out var calls) || calls.ValueKind != JsonValueKind.Array ||
                calls.GetArrayLength() != 1 || calls[0].GetProperty("function").GetProperty("name").GetString() != "connection_probe")
                throw Malformed();
            using var arguments = JsonDocument.Parse(calls[0].GetProperty("function").GetProperty("arguments").GetString()!);
            if (arguments.RootElement.ValueKind != JsonValueKind.Object || arguments.RootElement.EnumerateObject().Any()) throw Malformed();
            connection?.Set(new(AiConnectionState.Connected, "Connected · " + configuration.Model,
                configuration.BaseUrl.TrimEnd('/') + "|" + configuration.Model));
            return "Connected · " + configuration.Model;
        }
        catch (Exception ex)
        {
            var actual = ex is OperationCanceledException && !cancellationToken.IsCancellationRequested
                ? new AiProviderException(AiConnectionState.Timeout, "Provider timeout.") : ex;
            connection?.Set(new(AiProviderErrors.State(actual), AiProviderErrors.Friendly(actual)));
            throw actual;
        }
    }
    public async Task<IReadOnlyList<string>> GetModelsAsync(CancellationToken cancellationToken = default)
    {
        var configuration = settings.Current.Ai.Clone();
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(configuration.TimeoutSeconds));
        using var response = await SendAsync(configuration.BaseUrl, "models", HttpMethod.Get, null, timeout.Token);
        var body = await ReadBoundedAsync(response.Content, timeout.Token);
        using var document = JsonDocument.Parse(body);
        var root = document.RootElement;
        var items = root.TryGetProperty("data", out var data) ? data : root.TryGetProperty("models", out var models) ? models : default;
        if (items.ValueKind != JsonValueKind.Array) throw Malformed();
        return items.EnumerateArray().Where(x => x.ValueKind == JsonValueKind.Object && x.TryGetProperty("id", out _))
            .Select(x => x.GetProperty("id").GetString()).Where(x => !string.IsNullOrWhiteSpace(x)).Select(x => x!)
            .Distinct(StringComparer.OrdinalIgnoreCase).Order(StringComparer.OrdinalIgnoreCase).Take(500).ToArray();
    }
    private async Task<HttpResponseMessage> SendAsync(string baseUrl, string suffix, HttpMethod method, string? body, CancellationToken ct)
    {
        var endpoint = GetEndpoint(baseUrl, suffix);
        var secret = credentials.Read(ProviderCredentialNames.ForBaseUrl(baseUrl));
        if (string.IsNullOrWhiteSpace(secret)) throw new AiProviderException(AiConnectionState.NotConfigured, "Provider key missing.");
        for (var attempt = 0; ; attempt++)
        {
            using var request = new HttpRequestMessage(method, endpoint);
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", secret);
            if (body is not null) request.Content = new StringContent(body, Encoding.UTF8, "application/json");
            var response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);
            if (response.IsSuccessStatusCode) return response;
            if (attempt == 0 && response.StatusCode is HttpStatusCode.TooManyRequests or HttpStatusCode.BadGateway or HttpStatusCode.ServiceUnavailable or HttpStatusCode.GatewayTimeout)
            {
                response.Dispose(); await Task.Delay(500, ct); continue;
            }
            var status = response.StatusCode;
            var error = await ReadBoundedAsync(response.Content, ct);
            response.Dispose();
            // Diagnose schema/protocol rejections without printing provider-controlled messages, user text, or credentials.
            var diagnostic = SafeFailureShape(error);
            logger?.LogWarning("Provider rejection HTTP={Status} Parameter={Parameter} SchemaTool={Tool} SchemaKeywords={Keywords}",
                (int)status, diagnostic.Parameter, diagnostic.Tool, diagnostic.Keywords);
            var state = status is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden ? AiConnectionState.AuthenticationFailed : AiConnectionState.ProviderError;
            try
            {
                using var doc = JsonDocument.Parse(error);
                if (doc.RootElement.TryGetProperty("error", out var e) && e.ValueKind == JsonValueKind.Object &&
                    e.TryGetProperty("code", out var code) && code.ValueKind == JsonValueKind.String &&
                    code.GetString() is "model_not_found" or "invalid_model" or "model_not_available") state = AiConnectionState.InvalidModel;
            }
            catch (JsonException) { }
            // Never propagate provider-controlled error text, which may contain credential fragments.
            throw new AiProviderException(state, "Provider returned HTTP " + (int)status + ".");
        }
    }
    internal static (string Parameter, string Tool, string Keywords) SafeFailureShape(string error)
    {
        var messages = new List<string>(); var parameter = "Unavailable";
        void Read(JsonElement value, int depth)
        {
            if (depth > 4 || value.ValueKind != JsonValueKind.Object) return;
            foreach (var property in value.EnumerateObject())
            {
                if (property.Name == "param" && property.Value.ValueKind == JsonValueKind.String &&
                    System.Text.RegularExpressions.Regex.IsMatch(property.Value.GetString()!, @"^tools\[[0-9]{1,3}\]\.function\.(parameters|name)$")) parameter = property.Value.GetString()!;
                if (property.Name == "message" && property.Value.ValueKind == JsonValueKind.String) messages.Add(property.Value.GetString()!);
                if (property.Name is "error" or "metadata") Read(property.Value, depth + 1);
                if (property.Name == "raw" && property.Value.ValueKind == JsonValueKind.String)
                    try { using var nested = JsonDocument.Parse(property.Value.GetString()!); Read(nested.RootElement, depth + 1); } catch (JsonException) { }
            }
        }
        try { using var document = JsonDocument.Parse(error); Read(document.RootElement, 0); } catch (JsonException) { }
        var message = string.Join(" ", messages);
        var tool = UiToolsName(message);
        var keywords = new[] { "schema", "anyOf", "oneOf", "allOf", "not", "root", "top level", "strict", "required", "additionalProperties", "pattern", "maxLength", "properties", "null", "array", "object", "not permitted", "not allowed", "not supported", "missing", "extra", "tool_call_id", "messages", "function" }
            .Where(word => message.Contains(word, StringComparison.OrdinalIgnoreCase));
        return (parameter, tool, string.Join(",", keywords));
    }
    private static string UiToolsName(string message) => DesktopAgent.Infrastructure.UiAwareness.UiTools.Names.FirstOrDefault(name => message.Contains(name, StringComparison.Ordinal)) ?? "Unavailable";
    private static async Task<string> ReadBoundedAsync(HttpContent content, CancellationToken ct)
    {
        await using var stream = await content.ReadAsStreamAsync(ct);
        using var reader = new StreamReader(stream);
        var result = new StringBuilder();
        var buffer = new char[4096];
        while (await reader.ReadAsync(buffer.AsMemory(), ct) is var count && count > 0)
        {
            result.Append(buffer, 0, count);
            if (result.Length > 2000000) throw Malformed();
        }
        return result.ToString();
    }
    private static string BuildBody(string model, IReadOnlyList<AiMessage> messages, IReadOnlyList<AiToolDefinition> tools, bool stream)
    {
        var root = new JsonObject { ["model"] = model, ["messages"] = new JsonArray(messages.Select(ToJson).ToArray()), ["stream"] = stream };
        root["max_completion_tokens"] = stream ? 1024 : 256;
        if (stream) root["stream_options"] = new JsonObject { ["include_usage"] = true };
        if (tools.Count > 0)
        {
            root["tools"] = new JsonArray(tools.Select(t => (JsonNode?)new JsonObject {
                ["type"] = "function", ["function"] = new JsonObject {
                    ["name"] = t.Name, ["description"] = t.Description, ["parameters"] = JsonNode.Parse(t.Parameters.GetRawText()),
                    ["strict"] = t.Parameters.TryGetProperty("required", out var required) && t.Parameters.TryGetProperty("properties", out var properties) &&
                        required.GetArrayLength() == properties.EnumerateObject().Count() }
            }).ToArray());
            root["tool_choice"] = "auto"; root["parallel_tool_calls"] = false;
        }
        return root.ToJsonString();
    }
    private static JsonNode ToJson(AiMessage message)
    {
        var node = new JsonObject { ["role"] = message.Role, ["content"] = message.Content };
        if (message.Role == "tool") { node["tool_call_id"] = message.ToolCallId; return node; }
        if (message.ToolCalls is { Count: > 0 })
            node["tool_calls"] = new JsonArray(message.ToolCalls.Select(c => (JsonNode?)new JsonObject {
                ["id"] = c.Id, ["type"] = "function", ["function"] = new JsonObject { ["name"] = c.Name, ["arguments"] = c.ArgumentsJson }
            }).ToArray());
        return node;
    }
    private static string GetEndpoint(string baseUrl, string suffix)
    {
        if (!Uri.TryCreate(baseUrl, UriKind.Absolute, out var uri) ||
            uri.Scheme != "https" && !(uri.Scheme == "http" && uri.IsLoopback) ||
            !string.IsNullOrEmpty(uri.UserInfo) || !string.IsNullOrEmpty(uri.Query) || !string.IsNullOrEmpty(uri.Fragment))
            throw new AiProviderException(AiConnectionState.NotConfigured, "Invalid API base URL.");
        return uri.ToString().TrimEnd('/') + "/" + suffix;
    }
    private static AiProviderException Malformed() => new(AiConnectionState.ProviderError, "Malformed or incomplete provider response.");
    private sealed class ToolCallBuffer
    {
        public string Id { get; set; } = "";
        public string Name { get; set; } = "";
        public StringBuilder Arguments { get; } = new();
    }
}
