using System.Text.Json;
using DesktopAgent.Core.Applications.Services;
using DesktopAgent.Core.Automation.Services;
using DesktopAgent.Core.UiAwareness;
using DesktopAgent.Infrastructure.Projects;
using DesktopAgent.Infrastructure.UiAwareness;
using DesktopAgent.Infrastructure.Windows.Automation;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

internal static class UiLiveDiagnostics
{
    public static async Task<int> Raw(string application)
    {
        if (application is not ("Calculator" or "Notepad" or "Settings")) throw new ArgumentException("Only the three requested diagnostic applications are allowed.");
        var catalog = new ApplicationCatalog([], new MemoryApplicationCatalogRepository());
        var secrets = new SecretProtection();
        using var logging = LoggerFactory.Create(b => b.AddSimpleConsole(o => o.SingleLine = true));
        var probe = new WindowsWindowProbe(catalog, secrets);
        var self = new WindowsSelfWindowRegistry();
        var tracker = new ActiveUserWindowTracker(probe, self);
        var resolver = new WindowTargetResolver(probe, self, tracker);
        using var ui = new WindowsUiAutomationService(catalog, secrets, logging.CreateLogger<WindowsUiAutomationService>(), probe, self, resolver);
        var diagnostics = new WindowsDiagnosticsService(ui, probe, self, tracker, resolver, secrets, logging.CreateLogger<WindowsDiagnosticsService>());
        var report = await diagnostics.RunAsync(application);
        var directory = Path.Combine(Environment.CurrentDirectory, "artifacts", "windows-diagnostics");
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, application + "-direct.json");
        await File.WriteAllTextAsync(path, JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true }));
        Console.WriteLine("WINDOW DIAGNOSTIC\n" + JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true }));
        Console.WriteLine("REPORT FILE " + path);
        // A direct test does not claim that this separate process remembers another process's foreground history.
        return report.Steps.Any(s => s.Operation == "UIA Root / AutomationElement.FromHandle" && s.Success) ? 0 : 1;
    }
    // Explicit read-only diagnostic mode, never runs in the normal regression suite.
    public static async Task<int> Run(string processName)
    {
        if (!new[] { "notepad", "calculatorapp", "calculator", "systemsettings", "explorer", "chrome", "code", "applicationframehost" }.Contains(processName.ToLowerInvariant()))
            throw new ArgumentException("Choose a named acceptance-test application; no arbitrary desktop inspection.");
        var catalog = new ApplicationCatalog([], new MemoryApplicationCatalogRepository());
        var windows = new WindowsExternalWindowService(new ApplicationResolver(new WindowsExecutableLocator(), catalog), NullLogger<WindowsExternalWindowService>.Instance);
        using var logging = LoggerFactory.Create(b => b.AddSimpleConsole(o => o.SingleLine = true));
        using var service = new WindowsUiAutomationService(catalog, new SecretProtection(), logging.CreateLogger<WindowsUiAutomationService>(),
            new WindowsWindowProbe(catalog, new SecretProtection(), logging.CreateLogger<WindowsWindowProbe>()));
        var candidates = (await service.ListOpenWindows()).Where(w => w.ProcessName.Equals(processName, StringComparison.OrdinalIgnoreCase)).ToArray();
        if (candidates.Length == 0) { Console.WriteLine("LIVE WindowNotFound: " + processName); return 1; }
        // Deliberately explicit inventory choice; each returned HWND is revalidated by the product service.
        foreach (var w in candidates.Take(2))
        {
            var win32 = await service.GetWindowInfo(w.Handle);
            Console.WriteLine("LIVE WIN32 " + JsonSerializer.Serialize(UiContextBuilder.Window(win32!)));
            try
            {
                var tree = await service.GetWindowTree(w.Handle, new(MaximumDepth: 4, MaximumElements: 80));
                Console.WriteLine($"LIVE UIA status={tree.Status} root={tree.Window.RootElement?.ControlType} elements={tree.Elements.Count} interactive={tree.InteractiveElements.Count} truncated={tree.Truncated}");
                Console.WriteLine("LIVE CONTROLS " + JsonSerializer.Serialize(tree.InteractiveElements.Take(12).Select(UiContextBuilder.Element)));
            }
            catch (UiObservationException e) { Console.WriteLine("LIVE UIA FAILURE " + e.Code); }
        }
        return 0;
    }
}
