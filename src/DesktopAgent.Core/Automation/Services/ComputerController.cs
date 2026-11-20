using DesktopAgent.Core.Automation.Enums;
using DesktopAgent.Core.Automation.Interfaces;
using DesktopAgent.Core.Automation.Models;
using DesktopAgent.Core.Applications.Interfaces;

namespace DesktopAgent.Core.Automation.Services;

public sealed class ComputerController : IComputerController
{
    private readonly IApplicationControlService applications;
    private readonly IExternalWindowService windows;
    private readonly IShellService shell;
    private readonly IApplicationCatalog? catalog;
    private readonly IApplicationResolver? resolver;

    public ComputerController(IApplicationControlService applications, IExternalWindowService windows, IShellService shell, IApplicationCatalog? catalog = null, IApplicationResolver? resolver = null)
    {
        this.applications = applications;
        this.windows = windows;
        this.shell = shell;
        this.catalog = catalog;
        this.resolver = resolver;
    }

    public async Task<ComputerActionResult> ExecuteAsync(ParsedComputerCommand command, CancellationToken cancellationToken = default)
    {
        var target = command.Target ?? string.Empty;
        return command.Type switch
        {
            ComputerCommandType.Help => ComputerActionResult.Ok(HelpText),
            ComputerCommandType.ListApplications => ListApplications(),
            ComputerCommandType.ListWindows => ListWindows(),
            ComputerCommandType.SearchApplications => SearchApplications(target),
            ComputerCommandType.RefreshApplications => await RefreshApplicationsAsync(cancellationToken),
            ComputerCommandType.OpenApplication => await applications.LaunchAsync(target, cancellationToken),
            ComputerCommandType.OpenExplicitApplication => await applications.LaunchExplicitAsync(target, cancellationToken),
            ComputerCommandType.FocusApplication => applications.Focus(target),
            ComputerCommandType.CloseApplication => applications.Close(target),
            ComputerCommandType.MinimizeWindow => ExecuteResolvedWindow(target, windows.Minimize),
            ComputerCommandType.MaximizeWindow => ExecuteResolvedWindow(target, windows.Maximize),
            ComputerCommandType.RestoreWindow => ExecuteResolvedWindow(target, windows.Restore),
            ComputerCommandType.MoveWindow when command.X.HasValue && command.Y.HasValue => ExecuteResolvedWindow(target, name => windows.Move(name, command.X.Value, command.Y.Value)),
            ComputerCommandType.ResizeWindow when command.Width.HasValue && command.Height.HasValue => ExecuteResolvedWindow(target, name => windows.Resize(name, command.Width.Value, command.Height.Value)),
            ComputerCommandType.ApplyLayout when command.Layout.HasValue => ExecuteResolvedWindow(target, name => windows.ApplyLayout(name, command.Layout.Value)),
            ComputerCommandType.MoveToMonitor when command.MonitorNumber.HasValue => ExecuteResolvedWindow(target, name => windows.MoveToMonitor(name, command.MonitorNumber.Value)),
            ComputerCommandType.OpenFolder => shell.OpenFolder(target),
            ComputerCommandType.OpenFile => shell.OpenFile(target),
            ComputerCommandType.OpenUrl => shell.OpenUrl(target),
            _ => ComputerActionResult.Fail(ActionStatus.InvalidInput,
                command.ErrorMessage ?? "The command is incomplete.", target)
        };
    }

    private ComputerActionResult ExecuteResolvedWindow(string target, Func<string, ComputerActionResult> action)
    {
        var resolution = resolver?.ResolveInstalled(target);
        if (resolution?.Status == Core.Applications.Enums.ApplicationResolutionStatus.Ambiguous)
        {
            var matches = resolution.Matches.Take(8).ToList();
            var choices = matches.Select(match => $"{match.Application.Id}|{match.Application.DisplayName}");
            return new(false, ActionStatus.InvalidInput, $"Multiple applications match '{target}':\n" + string.Join("\n", matches.Select((match, index) => $"{index + 1}. {match.Application.DisplayName}")) + "\n\nChoose an application.", target, DateTimeOffset.Now, "AmbiguousApplication", new Dictionary<string, string> { ["ApplicationChoices"] = string.Join(";;", choices) });
        }
        var resolved = resolution?.Application?.ProcessName ?? resolution?.Application?.DisplayName ?? target;
        return action(resolved);
    }

    private ComputerActionResult ListApplications()
    {
        if (catalog is not null)
        {
            var discovered = catalog.Applications.Where(item => item.IsLaunchable).OrderByDescending(item => item.IsFavorite).ThenBy(item => item.DisplayName).Take(100).ToList();
            if (discovered.Count == 0) return ComputerActionResult.Ok("No launchable applications have been discovered. Use 'refresh apps' to scan this PC.");
            return ComputerActionResult.Ok($"Discovered applications ({catalog.Applications.Count} total):\n" + string.Join("\n", discovered.Select(app => $"• {app.DisplayName} ({app.ApplicationType})")));
        }
        var items = applications.GetRunningApplications();
        if (items.Count == 0) return ComputerActionResult.Ok("No user-facing applications were found.");
        var lines = items.Select(app => $"• {app.ApplicationName} ({app.ProcessName}, PID {app.ProcessId})");
        return ComputerActionResult.Ok("Running applications:\n" + string.Join("\n", lines), data:
            new Dictionary<string, string> { ["Count"] = items.Count.ToString() });
    }

    private ComputerActionResult SearchApplications(string query)
    {
        if (catalog is null) return ComputerActionResult.Fail(ActionStatus.Unsupported, "The application catalog is not available.", query);
        var matches = catalog.SearchApplications(query, true, 12);
        if (matches.Count == 0) return ComputerActionResult.Fail(ActionStatus.NotFound, $"No applications match '{query}'.", query);
        return ComputerActionResult.Ok($"Search: {query}\n\n" + string.Join("\n", matches.Select((match, index) => $"{index + 1}. {match.Application.DisplayName} ({match.Application.ApplicationType})")), query,
            data: new Dictionary<string, string> { ["ApplicationChoices"] = string.Join(";;", matches.Select(match => $"{match.Application.Id}|{match.Application.DisplayName}")) });
    }

    private async Task<ComputerActionResult> RefreshApplicationsAsync(CancellationToken cancellationToken)
    {
        if (catalog is null) return ComputerActionResult.Fail(ActionStatus.Unsupported, "The application catalog is not available.");
        var results = await catalog.RefreshAsync(cancellationToken);
        var failed = results.Count(result => result.Status == Core.Applications.Enums.DiscoveryProviderStatus.Failed);
        return ComputerActionResult.Ok($"Application refresh completed. Found {catalog.Applications.Count} applications" + (failed == 0 ? "." : $"; {failed} discovery source failed."),
            data: new Dictionary<string, string> { ["Count"] = catalog.Applications.Count.ToString() });
    }

    private ComputerActionResult ListWindows()
    {
        var items = windows.GetOpenWindows();
        if (items.Count == 0) return ComputerActionResult.Ok("No usable application windows were found.");
        var lines = items.Select(window => $"• {window.Title} ({window.ProcessName}, {window.State})");
        return ComputerActionResult.Ok("Open windows:\n" + string.Join("\n", lines), data:
            new Dictionary<string, string> { ["Count"] = items.Count.ToString() });
    }

    public const string HelpText = """
        Available commands:
        apps | search app <query> | refresh apps
        windows | workspaces | workspace status | help
        launch workspace <name> | stop workspace <name>
        open <application>
        focus <application> | close <application>
        minimize <window> | maximize <window> | restore <window>
        move <window> <left|right|top|bottom|top-left|top-right|bottom-left|bottom-right|center|maximized>
        move <window> <x> <y>
        move <window> monitor <number>
        resize <window> <width> <height>
        open folder <path>
        open file <path>
        open url <http-or-https-url>
        """;
}
