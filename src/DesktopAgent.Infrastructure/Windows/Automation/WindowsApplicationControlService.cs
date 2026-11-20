using System.ComponentModel;
using System.Diagnostics;
using DesktopAgent.Core.Applications.Enums;
using DesktopAgent.Core.Applications.Interfaces;
using DesktopAgent.Core.Applications.Models;
using DesktopAgent.Core.Automation.Enums;
using DesktopAgent.Core.Automation.Interfaces;
using DesktopAgent.Core.Automation.Models;
using DesktopAgent.Core.Automation.Services;
using Microsoft.Extensions.Logging;

namespace DesktopAgent.Infrastructure.Windows.Automation;

public sealed class WindowsApplicationControlService(
    IApplicationResolver resolver,
    IApplicationCatalog catalog,
    IExternalWindowService windows,
    ILogger<WindowsApplicationControlService> logger) : IApplicationControlService
{
    public IReadOnlyList<RunningApplicationInfo> GetRunningApplications() => windows.GetOpenWindows()
        .GroupBy(window => window.ProcessId)
        .Select(group =>
        {
            var window = group.OrderBy(item => item.Handle).First();
            string? path = null;
            try { using var process = Process.GetProcessById(window.ProcessId); path = process.MainModule?.FileName; }
            catch (Exception exception) when (exception is Win32Exception or InvalidOperationException or NotSupportedException) { }
            return new RunningApplicationInfo(FriendlyName(window), window.ProcessName, window.ProcessId, path, window.Handle, window.Title, true);
        }).OrderBy(app => app.ApplicationName, StringComparer.OrdinalIgnoreCase).ThenBy(app => app.ProcessId).ToArray();

    public Task<ComputerActionResult> LaunchAsync(string target, CancellationToken cancellationToken = default) => LaunchCoreAsync(target, false, cancellationToken);
    public Task<ComputerActionResult> LaunchExplicitAsync(string executablePath, CancellationToken cancellationToken = default) => LaunchCoreAsync(executablePath, true, cancellationToken);

    private async Task<ComputerActionResult> LaunchCoreAsync(string target, bool allowExplicitPath, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var resolution = catalog.Resolve(target);
        if (resolution.Status == ApplicationResolutionStatus.Ambiguous) return Ambiguous(target, resolution.Matches);
        var installed = resolution.Application;
        if (installed is null && Path.IsPathFullyQualified(target) && !allowExplicitPath)
            return ComputerActionResult.Fail(ActionStatus.NotFound, "Direct executable paths are only allowed through the explicit workspace file picker.", target);
        var definition = installed is null ? resolver.Resolve(target) : ApplicationResolver.ToDefinition(installed);
        if (definition is null)
            return ComputerActionResult.Fail(ActionStatus.NotFound, $"I couldn't find {target} among the applications available on this PC.", target);

        var matches = windows.GetOpenWindows().Where(window => resolver.Matches(definition, window)).Take(2).ToArray();
        if (matches.Length > 1) return ComputerActionResult.Fail(ActionStatus.InvalidInput, "Several application windows match. Select the intended window explicitly.", target, "AmbiguousWindow");
        var existing = matches.FirstOrDefault();
        if (existing is not null)
        {
            var focused = windows.FocusWindow(existing);
            if (focused.Success && installed is not null) _ = catalog.RecordUsageAsync(installed.Id);
            return focused.Success ? ComputerActionResult.Ok($"{definition.DisplayName} was already running and has been focused.", target, ActionStatus.AlreadyRunning) : focused;
        }

        try
        {
            var strategy = installed is null ? ApplicationLaunchStrategy.Win32 : SelectLaunchStrategy(installed);
            var startInfo = CreateStartInfo(installed, definition, strategy);
            if (startInfo is null) return ComputerActionResult.Fail(ActionStatus.Unsupported, $"{definition.DisplayName} does not expose a supported launch method.", target);
            using var process = Process.Start(startInfo);
            if (process is null) return ComputerActionResult.Fail(ActionStatus.Failed, $"{definition.DisplayName} could not be started.", target);
            logger.LogInformation("ApplicationLaunched Application={Application} Strategy={Strategy}", definition.DisplayName, strategy);
            for (var attempt = 0; attempt < 50; attempt++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (windows.GetOpenWindows().Any(window => resolver.Matches(definition, window)))
                {
                    if (installed is not null) await catalog.RecordUsageAsync(installed.Id, cancellationToken);
                    return ComputerActionResult.Ok($"{definition.DisplayName} opened successfully.", target);
                }
                await Task.Delay(100, cancellationToken);
            }
            if (strategy == ApplicationLaunchStrategy.Packaged)
            {
                if (installed is not null) await catalog.RecordUsageAsync(installed.Id, cancellationToken);
                return ComputerActionResult.Ok($"Windows accepted the request to open {definition.DisplayName}.", target);
            }
            return ComputerActionResult.Fail(ActionStatus.Failed, $"{definition.DisplayName} started, but no usable application window appeared.", target, "WindowNotCreated");
        }
        catch (Win32Exception exception)
        {
            logger.LogWarning(exception, "Application launch failed Target={Target}", definition.DisplayName);
            var status = exception.NativeErrorCode == 5 ? ActionStatus.PermissionDenied : ActionStatus.NotFound;
            return ComputerActionResult.Fail(status, status == ActionStatus.PermissionDenied ? "Windows denied permission to open the application." : $"{definition.DisplayName} could not be found or opened.", target, $"Win32:{exception.NativeErrorCode}");
        }
        catch (Exception exception) when (exception is InvalidOperationException or FileNotFoundException)
        {
            logger.LogWarning(exception, "Application launch failed Target={Target}", definition.DisplayName);
            return ComputerActionResult.Fail(ActionStatus.Failed, exception.Message, target, exception.GetType().Name);
        }
    }

    public ComputerActionResult Focus(string target)
    {
        var resolved = ResolveWindowTarget(target);
        if (resolved.Error is not null) return resolved.Error;
        var selected = windows.FindWindow(resolved.Target);
        var result = selected is null ? ComputerActionResult.Fail(ActionStatus.NotFound, "No matching application window is open.", target) : windows.FocusWindow(selected);
        if (result.Success && resolved.Application is not null) _ = catalog.RecordUsageAsync(resolved.Application.Id);
        if (result.Success) logger.LogInformation("ApplicationFocused Application={Application}", resolved.Application?.DisplayName ?? target);
        return result;
    }

    public ComputerActionResult Close(string target)
    {
        var resolved = ResolveWindowTarget(target);
        return resolved.Error ?? windows.Close(resolved.Target);
    }

    public static ApplicationLaunchStrategy SelectLaunchStrategy(InstalledApplication application) => application.ApplicationType switch
    {
        InstalledApplicationType.Packaged or InstalledApplicationType.Uwp or InstalledApplicationType.Msix or InstalledApplicationType.System when application.AppUserModelId is not null => ApplicationLaunchStrategy.Packaged,
        InstalledApplicationType.Shortcut when application.LaunchIdentifier is not null => ApplicationLaunchStrategy.Shortcut,
        _ when application.ExecutablePath is not null => ApplicationLaunchStrategy.Win32,
        _ => ApplicationLaunchStrategy.Unsupported
    };

    private static ProcessStartInfo? CreateStartInfo(InstalledApplication? app, ApplicationDefinition definition, ApplicationLaunchStrategy strategy) => strategy switch
    {
        ApplicationLaunchStrategy.Packaged when app?.AppUserModelId is { Length: > 0 } id => new("explorer.exe", $"shell:AppsFolder\\{id}") { UseShellExecute = true },
        ApplicationLaunchStrategy.Shortcut when app?.LaunchIdentifier is { Length: > 0 } shortcut => new(shortcut) { UseShellExecute = true },
        ApplicationLaunchStrategy.Win32 => new(app?.ExecutablePath ?? definition.Executable) { UseShellExecute = true },
        _ => null
    };

    private (string Target, InstalledApplication? Application, ComputerActionResult? Error) ResolveWindowTarget(string target)
    {
        var resolution = catalog.Resolve(target);
        if (resolution.Status == ApplicationResolutionStatus.Ambiguous) return (target, null, Ambiguous(target, resolution.Matches));
        var app = resolution.Application;
        return (app?.DisplayName ?? target, app, null);
    }

    private static ComputerActionResult Ambiguous(string target, IReadOnlyList<ApplicationSearchMatch> matches)
    {
        var choices = matches.Take(8).Select(match => $"{match.Application.Id}|{match.Application.DisplayName}").ToList();
        var message = $"Multiple applications match '{target}':\n" + string.Join("\n", choices.Select((choice, index) => $"{index + 1}. {choice[(choice.IndexOf('|') + 1)..]}")) + "\n\nChoose an application.";
        return new(false, ActionStatus.InvalidInput, message, target, DateTimeOffset.Now, "AmbiguousApplication", new Dictionary<string, string> { ["ApplicationChoices"] = string.Join(";;", choices) });
    }

    private static string FriendlyName(OpenWindowInfo window)
    {
        try { using var process = Process.GetProcessById(window.ProcessId); return process.MainModule?.FileVersionInfo.FileDescription is { Length: > 0 } description ? description : window.ProcessName; }
        catch (Exception exception) when (exception is Win32Exception or InvalidOperationException or NotSupportedException) { return window.ProcessName; }
    }
}
