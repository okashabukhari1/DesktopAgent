using System.Globalization;
using System.Text.RegularExpressions;
using DesktopAgent.Core.Automation.Enums;
using DesktopAgent.Core.Automation.Interfaces;
using DesktopAgent.Core.Automation.Models;

namespace DesktopAgent.Core.Automation.Services;

public sealed partial class ComputerCommandParser : IComputerCommandParser
{
    private static readonly IReadOnlyDictionary<string, WindowLayout> Layouts =
        new Dictionary<string, WindowLayout>(StringComparer.OrdinalIgnoreCase)
        {
            ["left"] = WindowLayout.Left,
            ["right"] = WindowLayout.Right,
            ["top"] = WindowLayout.Top,
            ["bottom"] = WindowLayout.Bottom,
            ["top-left"] = WindowLayout.TopLeft,
            ["topleft"] = WindowLayout.TopLeft,
            ["top-right"] = WindowLayout.TopRight,
            ["topright"] = WindowLayout.TopRight,
            ["bottom-left"] = WindowLayout.BottomLeft,
            ["bottomleft"] = WindowLayout.BottomLeft,
            ["bottom-right"] = WindowLayout.BottomRight,
            ["bottomright"] = WindowLayout.BottomRight,
            ["center"] = WindowLayout.Center,
            ["maximized"] = WindowLayout.Maximized
        };

    public ParsedComputerCommand Parse(string input)
    {
        if (string.IsNullOrWhiteSpace(input)) return Invalid("Enter a command. Type 'help' to see available commands.");
        var command = input.Trim();
        if (command.Equals("help", StringComparison.OrdinalIgnoreCase)) return new(ComputerCommandType.Help);
        if (command.Equals("apps", StringComparison.OrdinalIgnoreCase)) return new(ComputerCommandType.ListApplications);
        if (command.Equals("windows", StringComparison.OrdinalIgnoreCase)) return new(ComputerCommandType.ListWindows);
        if (command.Equals("workspaces", StringComparison.OrdinalIgnoreCase)) return new(ComputerCommandType.ListWorkspaces);
        if (command.Equals("workspace status", StringComparison.OrdinalIgnoreCase)) return new(ComputerCommandType.WorkspaceStatus);
        if (command.Equals("refresh apps", StringComparison.OrdinalIgnoreCase)) return new(ComputerCommandType.RefreshApplications);
        if (TryPrefix(command, "search app ", out var applicationQuery)) return Required(ComputerCommandType.SearchApplications, applicationQuery, "Application search text is required.");

        if (TryPrefix(command, "launch workspace ", out var launchWorkspace)) return Required(ComputerCommandType.LaunchWorkspace, launchWorkspace, "Workspace name is required.");
        if (TryPrefix(command, "stop workspace ", out var stopWorkspace)) return Required(ComputerCommandType.StopWorkspace, stopWorkspace, "Workspace name is required.");

        if (TryPrefix(command, "open folder ", out var folder)) return Required(ComputerCommandType.OpenFolder, folder, "Folder path is required.");
        if (TryPrefix(command, "open file ", out var file)) return Required(ComputerCommandType.OpenFile, file, "File path is required.");
        if (TryPrefix(command, "open url ", out var url)) return Required(ComputerCommandType.OpenUrl, url, "URL is required.");
        if (TryPrefix(command, "open ", out var app)) return Required(ComputerCommandType.OpenApplication, app, "Application name is required.");
        if (TryPrefix(command, "focus ", out var focus)) return Required(ComputerCommandType.FocusApplication, focus, "Application name is required.");
        if (TryPrefix(command, "close ", out var close)) return Required(ComputerCommandType.CloseApplication, close, "Application name is required.");
        if (TryPrefix(command, "minimize ", out var minimize)) return Required(ComputerCommandType.MinimizeWindow, minimize, "Window name is required.");
        if (TryPrefix(command, "maximize ", out var maximize)) return Required(ComputerCommandType.MaximizeWindow, maximize, "Window name is required.");
        if (TryPrefix(command, "restore ", out var restore)) return Required(ComputerCommandType.RestoreWindow, restore, "Window name is required.");

        var monitor = MoveMonitorRegex().Match(command);
        if (monitor.Success && int.TryParse(monitor.Groups[2].Value, CultureInfo.InvariantCulture, out var monitorNumber))
            return new(ComputerCommandType.MoveToMonitor, monitor.Groups[1].Value, MonitorNumber: monitorNumber);

        var coordinates = MoveCoordinatesRegex().Match(command);
        if (coordinates.Success &&
            int.TryParse(coordinates.Groups[2].Value, CultureInfo.InvariantCulture, out var x) &&
            int.TryParse(coordinates.Groups[3].Value, CultureInfo.InvariantCulture, out var y))
            return new(ComputerCommandType.MoveWindow, coordinates.Groups[1].Value, X: x, Y: y);

        var resize = ResizeRegex().Match(command);
        if (resize.Success &&
            int.TryParse(resize.Groups[2].Value, CultureInfo.InvariantCulture, out var width) &&
            int.TryParse(resize.Groups[3].Value, CultureInfo.InvariantCulture, out var height))
            return new(ComputerCommandType.ResizeWindow, resize.Groups[1].Value, Width: width, Height: height);

        var layout = MoveLayoutRegex().Match(command);
        if (layout.Success && Layouts.TryGetValue(layout.Groups[2].Value, out var parsedLayout))
            return new(ComputerCommandType.ApplyLayout, layout.Groups[1].Value, parsedLayout);

        return Invalid("Command not recognized. Type 'help' to see available commands.");
    }

    private static ParsedComputerCommand Required(ComputerCommandType type, string target, string error) =>
        string.IsNullOrWhiteSpace(target) ? Invalid(error) : new(type, target.Trim().Trim('"'));

    private static ParsedComputerCommand Invalid(string message) => new(ComputerCommandType.Invalid, ErrorMessage: message);

    private static bool TryPrefix(string input, string prefix, out string remainder)
    {
        if (input.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
        {
            remainder = input[prefix.Length..].Trim();
            return true;
        }
        remainder = string.Empty;
        return false;
    }

    [GeneratedRegex(@"^\s*move\s+(.+?)\s+monitor\s+(\d+)\s*$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex MoveMonitorRegex();
    [GeneratedRegex(@"^\s*move\s+(.+?)\s+(-?\d+)\s+(-?\d+)\s*$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex MoveCoordinatesRegex();
    [GeneratedRegex(@"^\s*resize\s+(.+?)\s+(\d+)\s+(\d+)\s*$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex ResizeRegex();
    [GeneratedRegex(@"^\s*move\s+(.+?)\s+(left|right|top|bottom|top-left|topleft|top-right|topright|bottom-left|bottomleft|bottom-right|bottomright|center|maximized)\s*$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex MoveLayoutRegex();
}
