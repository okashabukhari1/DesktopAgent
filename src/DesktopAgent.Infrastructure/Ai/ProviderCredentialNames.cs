using System.Security.Cryptography;
using System.Text;
using DesktopAgent.Core.Ai;

namespace DesktopAgent.Infrastructure.Ai;

public static class ProviderCredentialNames
{
    public const string LegacyName = "DesktopAgent/OpenAICompatible/ApiKey";

    public static string ForBaseUrl(string baseUrl)
    {
        if (!Uri.TryCreate(baseUrl.Trim(), UriKind.Absolute, out var uri) ||
            uri.Scheme != "https" && !(uri.Scheme == "http" && uri.IsLoopback) ||
            !string.IsNullOrEmpty(uri.UserInfo) || !string.IsNullOrEmpty(uri.Query) || !string.IsNullOrEmpty(uri.Fragment))
            throw new ArgumentException("Use a valid provider API URL.", nameof(baseUrl));
        // URI canonicalization normalizes the host but preserves case-sensitive API paths.
        var normalized = uri.AbsoluteUri.TrimEnd('/');
        var digest = SHA256.HashData(Encoding.UTF8.GetBytes(normalized));
        return "DesktopAgent/AI/" + Convert.ToHexString(digest);
    }

    public static void MigrateLegacy(ICredentialStore credentials, string currentBaseUrl)
    {
        var old = credentials.Read(LegacyName);
        if (string.IsNullOrWhiteSpace(old)) return;
        var destination = ForBaseUrl(currentBaseUrl);
        if (string.IsNullOrWhiteSpace(credentials.Read(destination))) credentials.Save(destination, old);
        credentials.Delete(LegacyName);
    }
}
