using DesktopAgent.Core.Interfaces;
using Microsoft.Extensions.Logging;
using Microsoft.Win32;

namespace DesktopAgent.Infrastructure.Windows;

public sealed class WindowsStartupService(ILogger<WindowsStartupService> logger) : IStartupService
{
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "DesktopAgent";

    public bool IsEnabled()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKey, false);
            return key?.GetValue(ValueName) is string value && !string.IsNullOrWhiteSpace(value);
        }
        catch (Exception exception)
        {
            logger.LogWarning(exception, "Could not read startup registration");
            return false;
        }
    }

    public void SetEnabled(bool enabled)
    {
        try
        {
            using var key = Registry.CurrentUser.CreateSubKey(RunKey, true);
            if (enabled)
            {
                var executable = Environment.ProcessPath ?? throw new InvalidOperationException("Executable path is unavailable.");
                key.SetValue(ValueName, $"\"{executable}\" --startup", RegistryValueKind.String);
            }
            else
            {
                key.DeleteValue(ValueName, false);
            }
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "Startup registration failed");
            throw new InvalidOperationException("Windows startup registration could not be changed.", exception);
        }
    }
}
