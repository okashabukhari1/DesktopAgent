using System.Text;

namespace DesktopAgent.Core.Applications.Services;

public static class ApplicationNameNormalizer
{
    private static readonly string[] NoiseSuffixes = [" shortcut", " application", " app", " exe"];

    public static string Normalize(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return string.Empty;
        var builder = new StringBuilder(value.Length);
        var pendingSpace = false;
        foreach (var character in value.Trim().ToLowerInvariant())
        {
            if (char.IsLetterOrDigit(character))
            {
                if (pendingSpace && builder.Length > 0) builder.Append(' ');
                builder.Append(character);
                pendingSpace = false;
            }
            else pendingSpace = true;
        }
        var result = builder.ToString();
        foreach (var suffix in NoiseSuffixes)
            if (result.EndsWith(suffix, StringComparison.Ordinal) && result.Length > suffix.Length) result = result[..^suffix.Length];
        return result.Trim();
    }

    public static IReadOnlyList<string> Tokens(string? value) => Normalize(value).Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
}
