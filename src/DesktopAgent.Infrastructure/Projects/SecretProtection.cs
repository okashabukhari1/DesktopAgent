using System.Text.RegularExpressions;
using DesktopAgent.Core.Projects;

namespace DesktopAgent.Infrastructure.Projects;

/// <summary>Conservative local masking. Not a guarantee of detecting every possible secret.</summary>
public sealed class SecretProtection : ISecretProtection
{
    private static readonly TimeSpan Timeout = TimeSpan.FromMilliseconds(250);
    private static readonly Regex[] Patterns =
    [
        new(@"-----BEGIN [^\r\n]*PRIVATE KEY-----[\s\S]*?(?:-----END [^\r\n]*PRIVATE KEY-----|\z)", RegexOptions.CultureInvariant, Timeout),
        new(@"\b(?:sk-[A-Za-z0-9_-]{16,}|(?:ghp_|github_pat_|glpat-)[A-Za-z0-9_-]{16,}|AKIA[A-Z0-9]{16}|eyJ[A-Za-z0-9_-]{8,}\.[A-Za-z0-9_-]{8,}\.[A-Za-z0-9_-]{8,})\b", RegexOptions.CultureInvariant, Timeout),
        new(@"(?i)\bBearer\s+[A-Za-z0-9._~+/-]{8,}", RegexOptions.CultureInvariant, Timeout),
        new(@"(?i)(?:https?://)[^\s/@:]+:[^\s/@]+@", RegexOptions.CultureInvariant, Timeout),
        // Mask complete quoted literals assigned to secret-like labels, including multiline values.
        new("(?i)(?:api[_-]?key|access[_-]?key|client[_-]?secret|password|passwd|pwd|secret|token|connection[_-]?string|credential|authorization)[\\w.-]*[\\\"']?\\s*[:=]\\s*@?([\\\"'])(?:\\\\.|(?!\\1)[\\s\\S])*?\\1", RegexOptions.CultureInvariant, Timeout),
        new("(?i)(?:api[_-]?key|access[_-]?key|client[_-]?secret|password|passwd|pwd|secret|token|connection[_-]?string|credential|authorization)[\\w.-]*[\\\"']?\\s*[:=]\\s*@?([\\\"'])(?:\\\\.|(?!\\1)[\\s\\S])*\\z", RegexOptions.CultureInvariant, Timeout),
        // Unquoted secret assignments / headers are masked through the end of their line.
        new(@"(?im)^.*\b(?:api[_-]?key|access[_-]?key|client[_-]?secret|password|passwd|pwd|secret|token|connection[_-]?string|credential|authorization)[\w.-]*[""']?\s*[:=].*$", RegexOptions.CultureInvariant, Timeout),
        new(@"(?im)^.*(?:Server|Data Source|Host)\s*=.*(?:Password|Pwd|User ID)\s*=.*$", RegexOptions.CultureInvariant, Timeout),
        // High-entropy-looking quoted opaque values: deliberately prefer false positives to leakage.
        new(@"[""'][A-Za-z0-9_+/=-]{32,}[""']", RegexOptions.CultureInvariant, Timeout)
    ];
    public bool IsSensitiveName(string name)
    {
        var value = name.ToLowerInvariant();
        return value == ".env" || value.StartsWith(".env.") || value.StartsWith("credentials") || value.StartsWith("secrets")
            || value.StartsWith("service-account") || value.StartsWith("appsettings") || value is "id_rsa" or "id_ed25519" or "id_dsa" or "id_ecdsa"
            || new[] { ".pfx", ".p12", ".pem", ".key", ".keystore", ".jks" }.Contains(Path.GetExtension(value))
            || Patterns[1].IsMatch(name);
    }
    public string Redact(string content, out bool changed)
    {
        var result = content;
        try
        {
            // Secret-labelled objects/arrays can hide credentials under ordinary nested field names.
            var containers = new Regex("(?i)(?:credential|secret|connection[_-]?string|authorization)[\\w.-]*[\\\"']?\\s*[:=]\\s*([\\{\\[])", RegexOptions.CultureInvariant, Timeout);
            var matches = containers.Matches(result).Cast<Match>().Reverse().ToArray();
            if (matches.Length > 32) { changed = true; return PreserveLines(content); } // Fail closed on adversarial nesting/volume.
            foreach (var match in matches)
            {
                int depth = 0, last = result.Length; char quote = '\0'; bool escaped = false;
                for (var i = match.Index + match.Length - 1; i < result.Length; i++)
                {
                    var c = result[i];
                    if (quote != '\0') { if (escaped) escaped = false; else if (c == '\\') escaped = true; else if (c == quote) quote = '\0'; continue; }
                    if (c is '\"' or '\'') { quote = c; continue; }
                    if (c is '{' or '[') depth++;
                    if (c is '}' or ']') { if (--depth == 0) { last = i + 1; break; } }
                }
                result = result[..match.Index] + PreserveLines(result[match.Index..last]) + result[last..];
            }
            foreach (var pattern in Patterns)
                result = pattern.Replace(result, match => PreserveLines(match.Value));
        }
        catch (RegexMatchTimeoutException) { result = PreserveLines(content); }
        changed = !string.Equals(result, content, StringComparison.Ordinal); return result;
    }
    private static string PreserveLines(string value)
    {
        var normalized = value.Replace("\r\n", "\n");
        return string.Join("\n", Enumerable.Repeat("[REDACTED]", normalized.Count(c => c == '\n') + 1));
    }
}
