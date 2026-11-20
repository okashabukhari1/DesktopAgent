using System.Net;
using DesktopAgent.Core.Ai;

namespace DesktopAgent.Infrastructure.Ai;

public sealed class AiProviderException(AiConnectionState state, string message) : Exception(message)
{
    public AiConnectionState State { get; } = state;
}
public static class AiProviderErrors
{
    public static AiConnectionState State(Exception ex) => ex switch {
        AiProviderException e => e.State, OperationCanceledException => AiConnectionState.Timeout,
        HttpRequestException { StatusCode: HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden } => AiConnectionState.AuthenticationFailed,
        HttpRequestException => AiConnectionState.NetworkError, _ => AiConnectionState.ProviderError };
    public static string Friendly(Exception ex) => State(ex) switch {
        AiConnectionState.NotConfigured => "AI is not configured. Add a key in Settings → AI.",
        AiConnectionState.AuthenticationFailed => "Authentication failed. Replace the provider key or check its permissions.",
        AiConnectionState.InvalidModel => "The configured model is unavailable. Choose a model supported by your provider.",
        AiConnectionState.Timeout => "The AI provider timed out.",
        AiConnectionState.NetworkError => "I couldn't connect to the AI provider.",
        _ => "The AI provider returned an error or an incomplete response." };
}
