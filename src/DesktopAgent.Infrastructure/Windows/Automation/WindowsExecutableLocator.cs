using DesktopAgent.Core.Automation.Interfaces;
using Microsoft.Win32;

namespace DesktopAgent.Infrastructure.Windows.Automation;

public sealed class WindowsExecutableLocator : IExecutableLocator
{
    public string? Locate(string executable)
    {
        if (Path.IsPathFullyQualified(executable) && File.Exists(executable)) return executable;
        var name = Path.GetFileName(executable);
        foreach (var hive in new[] { Registry.CurrentUser, Registry.LocalMachine })
        {
            foreach (var keyPath in new[]
            {
                $@"Software\Microsoft\Windows\CurrentVersion\App Paths\{name}",
                $@"Software\WOW6432Node\Microsoft\Windows\CurrentVersion\App Paths\{name}"
            })
            {
                try
                {
                    using var key = hive.OpenSubKey(keyPath);
                    if (key?.GetValue(null) is string value && File.Exists(value.Trim('"'))) return value.Trim('"');
                }
                catch (UnauthorizedAccessException) { }
            }
        }

        var directories = (Environment.GetEnvironmentVariable("PATH") ?? string.Empty)
            .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Concat([Environment.SystemDirectory]);
        foreach (var directory in directories.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            try
            {
                var candidate = Path.Combine(directory.Trim('"'), name);
                if (File.Exists(candidate)) return candidate;
            }
            catch (Exception exception) when (exception is ArgumentException or NotSupportedException) { }
        }
        return null;
    }
}
