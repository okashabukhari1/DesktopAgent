using DesktopAgent.Core.Applications.Enums;
using DesktopAgent.Core.Applications.Interfaces;
using DesktopAgent.Core.Applications.Models;
using DesktopAgent.Core.Automation.Interfaces;
using DesktopAgent.Core.Automation.Models;

namespace DesktopAgent.Core.Automation.Services;

public sealed class ApplicationResolver : IApplicationResolver
{
    private readonly IExecutableLocator _executableLocator;
    private readonly IApplicationCatalog? _catalog;
    private static readonly ApplicationDefinition[] KnownApplications =
    [
        new("Notepad", "notepad.exe", "notepad", ["notepad", "text editor"]),
        new("Calculator", "calc.exe", "calculatorapp", ["calculator", "calc"]),
        new("Visual Studio Code", "code.exe", "code", ["vscode", "vs code", "visual studio code", "code"]),
        new("Google Chrome", "chrome.exe", "chrome", ["chrome", "google chrome"]),
        new("Microsoft Edge", "msedge.exe", "msedge", ["edge", "microsoft edge"]),
        new("Windows Terminal", "wt.exe", "windowsterminal", ["terminal", "windows terminal", "wt"]),
        new("File Explorer", "explorer.exe", "explorer", ["explorer", "file explorer"])
    ];

    public ApplicationResolver(IExecutableLocator executableLocator, IApplicationCatalog? catalog = null)
    {
        _executableLocator = executableLocator;
        _catalog = catalog;
    }

    public ApplicationResolution ResolveInstalled(string input) => _catalog?.Resolve(input) ?? ApplicationResolution.NotFound;

    public ApplicationDefinition? Resolve(string input)
    {
        if (string.IsNullOrWhiteSpace(input)) return null;
        var target = input.Trim().Trim('"');
        var catalogResolution = ResolveInstalled(target);
        if (catalogResolution.Status == ApplicationResolutionStatus.Found && catalogResolution.Application is { } installed)
            return ToDefinition(installed);
        if (catalogResolution.Status == ApplicationResolutionStatus.Ambiguous) return null;

        var known = KnownApplications.FirstOrDefault(app => app.Aliases.Any(alias => string.Equals(alias, target, StringComparison.OrdinalIgnoreCase)));
        if (known is not null) return known with { Executable = _executableLocator.Locate(known.Executable) ?? known.Executable };
        if (!Path.IsPathFullyQualified(target) || !File.Exists(target) || !string.Equals(Path.GetExtension(target), ".exe", StringComparison.OrdinalIgnoreCase)) return null;
        var processName = Path.GetFileNameWithoutExtension(target);
        return new(processName, target, processName, [processName]);
    }

    public bool Matches(ApplicationDefinition definition, OpenWindowInfo window)
    {
        if (window.HostedProcessName is { } hosted && string.Equals(hosted, definition.ProcessName, StringComparison.OrdinalIgnoreCase)) return true;
        if (window.ProcessName.Equals("ApplicationFrameHost", StringComparison.OrdinalIgnoreCase))
            return window.HostedProcessName is { } child && (definition.Aliases.Any(a => string.Equals(a, child, StringComparison.OrdinalIgnoreCase)) ||
                string.Equals(window.Title, definition.DisplayName, StringComparison.OrdinalIgnoreCase));
        if (string.Equals(window.ProcessName, definition.ProcessName, StringComparison.OrdinalIgnoreCase)) return true;
        if (definition.Aliases.Any(alias => string.Equals(alias, window.ProcessName, StringComparison.OrdinalIgnoreCase))) return true;
        return definition.Aliases.Any(alias => window.Title.Contains(alias, StringComparison.OrdinalIgnoreCase));
    }

    public static ApplicationDefinition ToDefinition(InstalledApplication app) => new(app.DisplayName,
        app.ExecutablePath ?? app.LaunchIdentifier ?? app.Id,
        app.ProcessName ?? ApplicationNameFromLaunch(app),
        app.Aliases.Append(app.DisplayName).Distinct(StringComparer.OrdinalIgnoreCase).ToList());

    private static string ApplicationNameFromLaunch(InstalledApplication app) =>
        app.ExecutablePath is { Length: > 0 } path ? Path.GetFileNameWithoutExtension(path) : app.NormalizedName.Replace(" ", string.Empty, StringComparison.Ordinal);
}
