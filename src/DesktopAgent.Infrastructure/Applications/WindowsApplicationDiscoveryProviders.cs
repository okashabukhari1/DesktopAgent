using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using DesktopAgent.Core.Applications.Enums;
using DesktopAgent.Core.Applications.Interfaces;
using DesktopAgent.Core.Applications.Models;
using DesktopAgent.Core.Applications.Services;
using DesktopAgent.Core.Automation.Interfaces;
using Microsoft.Extensions.Logging;
using Microsoft.Win32;
using Microsoft.CSharp.RuntimeBinder;

namespace DesktopAgent.Infrastructure.Applications;

public sealed class StartMenuApplicationDiscoveryProvider : IApplicationDiscoveryProvider
{
    public string Name => "Start Menu";

    public Task<DiscoveryProviderResult> DiscoverAsync(CancellationToken cancellationToken = default) => Task.Run(() =>
    {
        var applications = new List<InstalledApplication>();
        var errors = 0;
        var roots = new[]
        {
            Environment.GetFolderPath(Environment.SpecialFolder.StartMenu),
            Environment.GetFolderPath(Environment.SpecialFolder.CommonStartMenu)
        }.Where(Directory.Exists).Distinct(StringComparer.OrdinalIgnoreCase);
        foreach (var root in roots)
        {
            var entries = EnumerateLaunchEntries(root, cancellationToken, ref errors);
            foreach (var shortcut in entries)
            {
                cancellationToken.ThrowIfCancellationRequested();
                try
                {
                    var displayName = Path.GetFileNameWithoutExtension(shortcut);
                    if (ApplicationCatalog.IsUninstaller(displayName, shortcut)) continue;
                    var target = TryResolveShortcut(shortcut);
                    applications.Add(new()
                    {
                        DisplayName = displayName,
                        ApplicationType = InstalledApplicationType.Shortcut,
                        ExecutablePath = target,
                        LaunchIdentifier = shortcut,
                        ProcessName = target is null ? null : Path.GetFileNameWithoutExtension(target),
                        IconPath = target ?? shortcut,
                        Sources = [Name]
                    });
                }
                catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or COMException or RuntimeBinderException) { errors++; }
            }
        }
        return new DiscoveryProviderResult(Name, errors == 0 ? DiscoveryProviderStatus.Success : DiscoveryProviderStatus.Partial, applications, errors == 0 ? null : $"{errors} entries could not be read");
    }, cancellationToken);

    private static IReadOnlyList<string> EnumerateLaunchEntries(string root, CancellationToken cancellationToken, ref int errors)
    {
        var found = new List<string>();
        var pending = new Stack<string>(); pending.Push(root);
        while (pending.Count > 0)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var directory = pending.Pop();
            try
            {
                foreach (var file in Directory.EnumerateFiles(directory))
                {
                    var extension = Path.GetExtension(file);
                    if (extension.Equals(".lnk", StringComparison.OrdinalIgnoreCase) || extension.Equals(".appref-ms", StringComparison.OrdinalIgnoreCase)) found.Add(file);
                }
                foreach (var child in Directory.EnumerateDirectories(directory)) pending.Push(child);
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException) { errors++; }
        }
        return found;
    }

    private static string? TryResolveShortcut(string path)
    {
        if (!path.EndsWith(".lnk", StringComparison.OrdinalIgnoreCase)) return null;
        var type = Type.GetTypeFromProgID("WScript.Shell");
        if (type is null) return null;
        dynamic? shell = null;
        dynamic? shortcut = null;
        try
        {
            shell = Activator.CreateInstance(type);
            shortcut = shell?.CreateShortcut(path);
            var target = shortcut?.TargetPath as string;
            return !string.IsNullOrWhiteSpace(target) && File.Exists(target) ? target : null;
        }
        finally
        {
            if (shortcut is not null && Marshal.IsComObject(shortcut)) Marshal.FinalReleaseComObject(shortcut);
            if (shell is not null && Marshal.IsComObject(shell)) Marshal.FinalReleaseComObject(shell);
        }
    }
}

public sealed class AppPathsApplicationDiscoveryProvider(ILogger<AppPathsApplicationDiscoveryProvider> logger) : IApplicationDiscoveryProvider
{
    public string Name => "Registered Applications";
    public Task<DiscoveryProviderResult> DiscoverAsync(CancellationToken cancellationToken = default) => Task.Run(() =>
    {
        var applications = new List<InstalledApplication>(); var errors = 0;
        foreach (var hive in new[] { RegistryHive.CurrentUser, RegistryHive.LocalMachine })
        foreach (var view in new[] { RegistryView.Registry64, RegistryView.Registry32 })
        {
            try
            {
                using var baseKey = RegistryKey.OpenBaseKey(hive, view);
                using var paths = baseKey.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\App Paths");
                if (paths is null) continue;
                foreach (var name in paths.GetSubKeyNames())
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    try
                    {
                        using var key = paths.OpenSubKey(name);
                        var executable = (key?.GetValue(null) as string)?.Trim().Trim('"');
                        if (executable is null || !File.Exists(executable) || ApplicationCatalog.IsUninstaller(name, executable)) continue;
                        var version = FileVersionInfo.GetVersionInfo(executable);
                        var display = string.IsNullOrWhiteSpace(version.FileDescription) ? Path.GetFileNameWithoutExtension(executable) : version.FileDescription;
                        applications.Add(new() { DisplayName = display, ApplicationType = InstalledApplicationType.Win32, ExecutablePath = executable, LaunchIdentifier = executable, ProcessName = Path.GetFileNameWithoutExtension(executable), IconPath = executable, Aliases = [Path.GetFileNameWithoutExtension(name)], Sources = [Name] });
                    }
                    catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or System.Security.SecurityException) { errors++; }
                }
            }
            catch (Exception exception) when (exception is UnauthorizedAccessException or System.Security.SecurityException) { logger.LogWarning(exception, "ApplicationDiscoveryProviderFailed Provider={Provider}", Name); errors++; }
        }
        return new DiscoveryProviderResult(Name, errors == 0 ? DiscoveryProviderStatus.Success : DiscoveryProviderStatus.Partial, applications);
    }, cancellationToken);
}

public sealed class InstalledWin32ApplicationDiscoveryProvider : IApplicationDiscoveryProvider
{
    public string Name => "Desktop Applications";
    public Task<DiscoveryProviderResult> DiscoverAsync(CancellationToken cancellationToken = default) => Task.Run(() =>
    {
        var applications = new List<InstalledApplication>(); var errors = 0;
        foreach (var hive in new[] { RegistryHive.CurrentUser, RegistryHive.LocalMachine })
        foreach (var view in new[] { RegistryView.Registry64, RegistryView.Registry32 })
        {
            try
            {
                using var baseKey = RegistryKey.OpenBaseKey(hive, view);
                using var uninstall = baseKey.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Uninstall");
                if (uninstall is null) continue;
                foreach (var name in uninstall.GetSubKeyNames())
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    try
                    {
                        using var key = uninstall.OpenSubKey(name);
                        var display = key?.GetValue("DisplayName") as string;
                        var icon = CleanIconPath(key?.GetValue("DisplayIcon") as string);
                        if (string.IsNullOrWhiteSpace(display) || icon is null || !File.Exists(icon) || ApplicationCatalog.IsUninstaller(display, icon)) continue;
                        applications.Add(new() { DisplayName = display, ApplicationType = InstalledApplicationType.Win32, ExecutablePath = icon, LaunchIdentifier = icon, ProcessName = Path.GetFileNameWithoutExtension(icon), IconPath = icon, Sources = [Name] });
                    }
                    catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or System.Security.SecurityException) { errors++; }
                }
            }
            catch (Exception exception) when (exception is UnauthorizedAccessException or System.Security.SecurityException) { errors++; }
        }
        return new DiscoveryProviderResult(Name, errors == 0 ? DiscoveryProviderStatus.Success : DiscoveryProviderStatus.Partial, applications);
    }, cancellationToken);

    private static string? CleanIconPath(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        var path = value.Trim().Trim('"');
        var comma = path.LastIndexOf(',');
        if (comma > 2 && int.TryParse(path[(comma + 1)..], out _)) path = path[..comma].Trim().Trim('"');
        return Path.GetExtension(path).Equals(".exe", StringComparison.OrdinalIgnoreCase) ? path : null;
    }
}

public sealed class PackagedApplicationDiscoveryProvider(ILogger<PackagedApplicationDiscoveryProvider> logger) : IApplicationDiscoveryProvider
{
    private static readonly string[] SystemNames = ["settings", "windows security", "get help", "tips", "feedback hub", "microsoft store"];
    public string Name => "Packaged Applications";
    public Task<DiscoveryProviderResult> DiscoverAsync(CancellationToken cancellationToken = default) => Task.Run(() =>
    {
        var applications = new List<InstalledApplication>(); dynamic? shell = null; dynamic? folder = null; var errors = 0;
        try
        {
            var type = Type.GetTypeFromProgID("Shell.Application");
            if (type is null) return new DiscoveryProviderResult(Name, DiscoveryProviderStatus.Failed, [], "Windows Shell API unavailable");
            shell = Activator.CreateInstance(type); folder = shell?.NameSpace("shell:AppsFolder");
            if (folder is null) return new DiscoveryProviderResult(Name, DiscoveryProviderStatus.Failed, [], "AppsFolder unavailable");
            foreach (var raw in folder.Items())
            {
                cancellationToken.ThrowIfCancellationRequested(); dynamic item = raw;
                try
                {
                    string display = item.Name as string ?? string.Empty;
                    string path = item.Path as string ?? string.Empty;
                    string aumid = item.ExtendedProperty("System.AppUserModel.ID") as string ?? path;
                    string family = item.ExtendedProperty("System.AppUserModel.PackageFamilyName") as string ?? string.Empty;
                    if (display.Length == 0 || aumid.Length == 0 || ApplicationCatalog.IsUninstaller(display)) continue;
                    var system = SystemNames.Contains(ApplicationNameNormalizer.Normalize(display), StringComparer.OrdinalIgnoreCase);
                    applications.Add(new() { DisplayName = display, ApplicationType = system ? InstalledApplicationType.System : InstalledApplicationType.Packaged, LaunchIdentifier = aumid, AppUserModelId = aumid, PackageFamilyName = family.Length == 0 ? null : family, Sources = [Name], IsSystemApplication = system });
                }
                catch (Exception exception) when (exception is COMException or RuntimeBinderException) { errors++; }
                finally { if (Marshal.IsComObject(raw)) Marshal.FinalReleaseComObject(raw); }
            }
        }
        catch (Exception exception) when (exception is COMException or RuntimeBinderException) { logger.LogWarning(exception, "ApplicationDiscoveryProviderFailed Provider={Provider}", Name); return new(Name, DiscoveryProviderStatus.Failed, applications, exception.GetType().Name); }
        finally { if (folder is not null && Marshal.IsComObject(folder)) Marshal.FinalReleaseComObject(folder); if (shell is not null && Marshal.IsComObject(shell)) Marshal.FinalReleaseComObject(shell); }
        return new DiscoveryProviderResult(Name, errors == 0 ? DiscoveryProviderStatus.Success : DiscoveryProviderStatus.Partial, applications);
    }, cancellationToken);
}

public sealed class RunningApplicationDiscoveryProvider : IApplicationDiscoveryProvider
{
    public string Name => "Running Applications";
    public Task<DiscoveryProviderResult> DiscoverAsync(CancellationToken cancellationToken = default) => Task.Run(() =>
    {
        var items = new List<InstalledApplication>();
        foreach (var process in Process.GetProcesses())
        {
            using (process)
            {
                cancellationToken.ThrowIfCancellationRequested();
                try
                {
                    if (process.MainWindowHandle == 0) continue;
                    var name = process.ProcessName;
                    string? executable = null;
                    string display = name;
                    try
                    {
                        executable = process.MainModule?.FileName;
                        display = process.MainModule?.FileVersionInfo.FileDescription is { Length: > 0 } description ? description : name;
                    }
                    catch (Exception exception) when (exception is Win32Exception or InvalidOperationException or NotSupportedException) { }
                    items.Add(new InstalledApplication { DisplayName = display, ApplicationType = executable is null ? InstalledApplicationType.Unknown : InstalledApplicationType.Win32,
                        ExecutablePath = executable, LaunchIdentifier = executable, ProcessName = Path.GetFileNameWithoutExtension(executable) ?? name,
                        IconPath = executable, IsLaunchable = executable is not null, Sources = [Name] });
                }
                catch (Exception exception) when (exception is Win32Exception or InvalidOperationException) { }
            }
        }
        return new DiscoveryProviderResult(Name, DiscoveryProviderStatus.Success,
            items.GroupBy(item => item.ProcessName, StringComparer.OrdinalIgnoreCase).Select(group => group.First()).ToList());
    }, cancellationToken);
}
