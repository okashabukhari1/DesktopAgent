using System.ComponentModel;
using System.Diagnostics;
using DesktopAgent.Core.Automation.Enums;
using DesktopAgent.Core.Automation.Interfaces;
using DesktopAgent.Core.Automation.Models;
using Microsoft.Extensions.Logging;

namespace DesktopAgent.Infrastructure.Windows.Automation;

public sealed class WindowsShellService(ILogger<WindowsShellService> logger) : IShellService
{
    private static readonly HashSet<string> UnsafeExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".exe", ".com", ".bat", ".cmd", ".ps1", ".psm1", ".vbs", ".vbe", ".js", ".jse",
        ".wsf", ".wsh", ".msi", ".msp", ".scr", ".cpl", ".lnk", ".url", ".reg"
    };

    public ComputerActionResult OpenFolder(string path)
    {
        var normalized = NormalizePath(path);
        if (normalized is null || !Directory.Exists(normalized))
            return ComputerActionResult.Fail(ActionStatus.NotFound, "Folder not found.", path);
        return OpenShellTarget(normalized, "Folder opened.", "FolderOpened");
    }

    public ComputerActionResult OpenFile(string path)
    {
        var normalized = NormalizePath(path);
        if (normalized is null || !File.Exists(normalized))
            return ComputerActionResult.Fail(ActionStatus.NotFound, "File not found.", path);
        var extension = Path.GetExtension(normalized);
        if (string.IsNullOrWhiteSpace(extension) || UnsafeExtensions.Contains(extension))
            return ComputerActionResult.Fail(ActionStatus.Unsupported,
                $"Opening '{extension}' files is blocked by the Part 2 safety policy.", Path.GetFileName(normalized));
        return OpenShellTarget(normalized, "File opened with its default application.", "FileOpened");
    }

    public ComputerActionResult OpenUrl(string url)
    {
        if (!TryValidateWebUri(url, out var uri))
            return ComputerActionResult.Fail(ActionStatus.InvalidInput, "Enter a valid HTTP or HTTPS URL.", url);
        return OpenShellTarget(uri.AbsoluteUri, "URL opened in the default browser.", "UrlOpened");
    }

    public static bool TryValidateWebUri(string input, out Uri uri)
    {
        if (Uri.TryCreate(input.Trim(), UriKind.Absolute, out var parsed) &&
            (parsed.Scheme == Uri.UriSchemeHttp || parsed.Scheme == Uri.UriSchemeHttps) &&
            !string.IsNullOrWhiteSpace(parsed.Host))
        {
            uri = parsed;
            return true;
        }
        uri = null!;
        return false;
    }

    public static string? NormalizePath(string input)
    {
        if (string.IsNullOrWhiteSpace(input)) return null;
        try { return Path.GetFullPath(Environment.ExpandEnvironmentVariables(input.Trim().Trim('"'))); }
        catch (Exception exception) when (exception is ArgumentException or NotSupportedException or PathTooLongException) { return null; }
    }

    private ComputerActionResult OpenShellTarget(string target, string successMessage, string eventName)
    {
        try
        {
            var process = Process.Start(new ProcessStartInfo { FileName = target, UseShellExecute = true });
            process?.Dispose();
            logger.LogInformation("{EventName} Target={Target}", eventName, target);
            return ComputerActionResult.Ok(successMessage, target);
        }
        catch (Win32Exception exception)
        {
            logger.LogWarning(exception, "Shell operation failed Event={EventName} Target={Target}", eventName, target);
            var status = exception.NativeErrorCode == 5 ? ActionStatus.PermissionDenied : ActionStatus.Failed;
            return ComputerActionResult.Fail(status, exception.NativeErrorCode == 5
                ? "Windows denied permission to open the target." : "Windows could not open the target.", target, $"Win32:{exception.NativeErrorCode}");
        }
        catch (Exception exception) when (exception is InvalidOperationException or FileNotFoundException)
        {
            logger.LogWarning(exception, "Shell operation failed Event={EventName} Target={Target}", eventName, target);
            return ComputerActionResult.Fail(ActionStatus.Failed, exception.Message, target, exception.GetType().Name);
        }
    }
}
