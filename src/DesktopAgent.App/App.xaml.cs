using System.Windows;
using System.Net.Http;
using System.Windows.Threading;
using DesktopAgent.App.Services;
using DesktopAgent.App.ViewModels;
using DesktopAgent.App.Views;
using DesktopAgent.Core.Interfaces;
using DesktopAgent.Core.Services;
using DesktopAgent.Core.Automation.Interfaces;
using DesktopAgent.Core.Automation.Services;
using DesktopAgent.Core.Automation.Enums;
using DesktopAgent.Core.Automation.Models;
using DesktopAgent.Infrastructure.Automation;
using DesktopAgent.Infrastructure.Logging;
using DesktopAgent.Infrastructure.Settings;
using DesktopAgent.Infrastructure.Windows;
using DesktopAgent.Infrastructure.Windows.Automation;
using DesktopAgent.Core.Workspaces.Interfaces;
using DesktopAgent.Core.Workspaces.Services;
using DesktopAgent.Core.Workspaces.Models;
using DesktopAgent.Infrastructure.Workspaces;
using DesktopAgent.Core.Applications.Interfaces;
using DesktopAgent.Core.Applications.Services;
using DesktopAgent.Infrastructure.Applications;
using DesktopAgent.Core.Permissions;
using DesktopAgent.Infrastructure.Permissions;
using DesktopAgent.Core.Ai;
using DesktopAgent.Infrastructure.Ai;
using DesktopAgent.Core.Projects;
using DesktopAgent.Infrastructure.Projects;
using DesktopAgent.Core.Developer;
using DesktopAgent.Infrastructure.Developer;
using DesktopAgent.Core.UiAwareness;
using DesktopAgent.Infrastructure.UiAwareness;
using DesktopAgent.Core.ComputerUse;
using DesktopAgent.Infrastructure.ComputerUse;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace DesktopAgent.App;

public partial class App : System.Windows.Application
{
    private IHost? _host;
    private TrayIconService? _tray;
    private DispatcherTimer? _smokeTimer;
    private bool _smokeTest;

    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        _smokeTest = e.Args.Any(a => string.Equals(a, "--smoke-test", StringComparison.OrdinalIgnoreCase));
        _host = Host.CreateDefaultBuilder(e.Args)
            .ConfigureLogging(logging =>
            {
                logging.ClearProviders();
                logging.SetMinimumLevel(LogLevel.Information);
                logging.AddProvider(new FileLoggerProvider());
            })
            .ConfigureServices(services =>
            {
                services.AddSingleton<ISettingsService, JsonSettingsService>();
                services.AddSingleton<IStartupService, WindowsStartupService>();
                services.AddSingleton<IMonitorService, WindowsMonitorService>();
                services.AddSingleton<ICharacterService, CharacterService>();
                services.AddSingleton<IWindowService, WindowService>();
                services.AddSingleton<IExecutableLocator, WindowsExecutableLocator>();
                services.AddSingleton<IApplicationCatalogRepository, JsonApplicationCatalogRepository>();
                services.AddSingleton<IApplicationDiscoveryProvider, StartMenuApplicationDiscoveryProvider>();
                services.AddSingleton<IApplicationDiscoveryProvider, AppPathsApplicationDiscoveryProvider>();
                services.AddSingleton<IApplicationDiscoveryProvider, InstalledWin32ApplicationDiscoveryProvider>();
                services.AddSingleton<IApplicationDiscoveryProvider, PackagedApplicationDiscoveryProvider>();
                services.AddSingleton<IApplicationDiscoveryProvider, RunningApplicationDiscoveryProvider>();
                services.AddSingleton<IApplicationCatalog, ApplicationCatalog>();
                services.AddSingleton<IApplicationResolver, ApplicationResolver>();
                services.AddSingleton<IComputerCommandParser, ComputerCommandParser>();
                services.AddSingleton<JsonActionHistoryService>();
                services.AddSingleton<IActionHistoryService>(provider => provider.GetRequiredService<JsonActionHistoryService>());
                services.AddSingleton<IExternalWindowService, WindowsExternalWindowService>();
                services.AddSingleton<ISelfWindowRegistry, WindowsSelfWindowRegistry>();
                services.AddSingleton<IWin32WindowProbe, WindowsWindowProbe>();
                services.AddSingleton<ActiveUserWindowTracker>();
                services.AddSingleton<IActiveUserWindowTracker>(p => p.GetRequiredService<ActiveUserWindowTracker>());
                services.AddSingleton<IWindowTargetResolver, WindowTargetResolver>();
                services.AddSingleton<WindowsForegroundEvents>();
                services.AddSingleton<WindowsUiAutomationService>();
                services.AddSingleton<WindowsDiagnosticsService>();
                services.AddSingleton<IWindowsUiAutomationService>(p => p.GetRequiredService<WindowsUiAutomationService>());
                services.AddSingleton<IUiSnapshotService, UiSnapshotService>();
                services.AddSingleton<ComputerUseSession>();
                services.AddSingleton<PythonComputerBackend>();
                services.AddSingleton<IComputerInteractionBackend>(p => new HybridComputerBackend(p.GetRequiredService<WindowsUiAutomationService>(), p.GetRequiredService<PythonComputerBackend>()));
                services.AddSingleton<IComputerActionExecutor, ComputerActionExecutor>();
                services.AddSingleton<ComputerUseSafety>();
                services.AddSingleton<IApplicationControlService, WindowsApplicationControlService>();
                services.AddSingleton<IShellService, WindowsShellService>();
                services.AddSingleton<IComputerController, ComputerController>();
                services.AddSingleton<IWorkspaceRepository, JsonWorkspaceRepository>();
                services.AddSingleton<IWorkspaceValidator, WorkspaceValidator>();
                services.AddSingleton<IWorkspaceLaunchEngine, WorkspaceLaunchEngine>();
                services.AddSingleton<IWorkspaceService, WorkspaceService>();
                services.AddSingleton<IProjectDiscoveryService, ProjectDiscoveryService>();
                services.AddSingleton<IProjectCatalog, ProjectCatalog>();
                services.AddSingleton<CurrentProjectContext>();
                services.AddSingleton<IProjectEditorLauncher, WindowsProjectEditorLauncher>();
                services.AddSingleton<IProjectOpenService, ProjectOpenService>();
                services.AddSingleton<ISecretProtection, SecretProtection>();
                services.AddSingleton<DeveloperEventNormalizer>();
                services.AddSingleton<DeveloperSupervisor>();
                services.AddSingleton<IDeveloperSessionManager>(p => p.GetRequiredService<DeveloperSupervisor>());
                services.AddSingleton<IDeveloperEventBridge>(p => p.GetRequiredService<DeveloperSupervisor>());
                services.AddSingleton<DeveloperPipeServer>();
                services.AddSingleton<ClaudeHookInstaller>();
                services.AddSingleton<IDeveloperToolAdapter, ClaudeCodeAdapter>();
                services.AddSingleton<IDeveloperToolAdapter>(new DetectionOnlyDeveloperAdapter("cursor", "Cursor", "cursor.cmd", "Cursor.exe"));
                services.AddSingleton<OpenCodeAdapter>();
                services.AddSingleton<IDeveloperToolAdapter>(p => p.GetRequiredService<OpenCodeAdapter>());
                services.AddSingleton<IDeveloperToolAdapter>(new DetectionOnlyDeveloperAdapter("vscode", "VS Code", "code.cmd", "Code.exe"));
                services.AddSingleton<IDeveloperToolAdapter>(new DetectionOnlyDeveloperAdapter("terminal", "Terminal (structured hooks only)", "wt.exe"));
                services.AddSingleton<ProjectFileService>();
                services.AddSingleton<IProjectFileService>(p => p.GetRequiredService<ProjectFileService>());
                services.AddSingleton<IProjectSearchService, ProjectSearchService>();
                services.AddSingleton<IProjectFileOpenService, ProjectFileOpenService>();
                services.AddSingleton<IComputerCommandService, ComputerCommandService>();
                services.AddSingleton<JsonPolicyService>();
                services.AddSingleton<IPolicyService>(provider => provider.GetRequiredService<JsonPolicyService>());
                services.AddSingleton<IAgentActionApprovalService, AgentActionApprovalService>();
                services.AddSingleton<ICredentialStore, WindowsCredentialStore>();
                services.AddSingleton<IAiConnectionService, AiConnectionService>();
                services.AddHttpClient<OpenAiCompatibleProvider>(client => client.Timeout = Timeout.InfiniteTimeSpan)
                    .ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler { AllowAutoRedirect = false });
                services.AddSingleton<IAiProvider>(provider => provider.GetRequiredService<OpenAiCompatibleProvider>());
                services.AddSingleton(provider => new AgentToolRegistry(WindowsAgentTools.Create(
                    provider.GetRequiredService<IComputerController>(), provider.GetRequiredService<IApplicationCatalog>(),
                    provider.GetRequiredService<IApplicationControlService>(), provider.GetRequiredService<IWorkspaceService>()).Concat(ProjectTools.Create(
                    provider.GetRequiredService<IProjectCatalog>(), provider.GetRequiredService<IProjectOpenService>(),
                    provider.GetRequiredService<IWorkspaceService>(), provider.GetRequiredService<CurrentProjectContext>())).Concat(ProjectFileTools.Create(
                    provider.GetRequiredService<IProjectCatalog>(), provider.GetRequiredService<CurrentProjectContext>(),
                    provider.GetRequiredService<IProjectFileService>(), provider.GetRequiredService<IProjectSearchService>(), provider.GetRequiredService<IProjectFileOpenService>())).Concat(DeveloperTools.Create(
                    provider.GetRequiredService<IDeveloperSessionManager>())).Concat(UiTools.Create(
                    provider.GetRequiredService<IWindowsUiAutomationService>())).Concat(ComputerActionTools.Create(
                    provider.GetRequiredService<IComputerActionExecutor>()))));
                services.AddSingleton<IAgentToolRouter, ComputerAgentToolRouter>();
                services.AddSingleton<IAssistantConversationService, AssistantConversationService>();
                services.AddSingleton<CharacterViewModel>();
                services.AddSingleton<CharacterWindowViewModel>();
                services.AddSingleton<TrayIconService>();
                services.AddTransient<CharacterWindow>();
                services.AddTransient<InteractionPanelWindow>();
                services.AddTransient<NotificationBubbleWindow>();
                services.AddTransient<SettingsViewModel>();
                services.AddTransient<SettingsWindow>();
                services.AddTransient<DesktopAppWindow>();
                services.AddTransient<WorkspacesView>();
                services.AddTransient<WorkspaceEditorWindow>();
                services.AddTransient<ApplicationsView>();
                services.AddTransient<PermissionsView>();
                services.AddTransient<AssistantView>();
                services.AddTransient<AiSettingsView>();
                services.AddTransient<OnboardingWindow>();
                services.AddTransient<ActivityView>();
                services.AddTransient<ApplicationPickerWindow>();
                services.AddTransient<ProjectsView>();
                services.AddTransient<ProjectSettingsView>();
                services.AddTransient<DeveloperView>();
                services.AddTransient<DeveloperSettingsView>();
            }).Build();

        DispatcherUnhandledException += OnDispatcherUnhandledException;
        AppDomain.CurrentDomain.UnhandledException += OnUnhandledException;
        var logger = _host.Services.GetRequiredService<ILogger<App>>();
        try { await _host.StartAsync(); }
        catch (Exception exception)
        {
            logger.LogCritical(exception, "StartupHostFailed");
            System.Windows.MessageBox.Show("Desktop Agent could not start. Check the local log for details.", "Desktop Agent", MessageBoxButton.OK, MessageBoxImage.Error);
            Shutdown(-1);
            return;
        }
        var settings = _host.Services.GetRequiredService<ISettingsService>();
        await InitializeSafelyAsync("Settings", () => settings.LoadAsync());
        await InitializeSafelyAsync("AI credential migration", () =>
        {
            ProviderCredentialNames.MigrateLegacy(_host.Services.GetRequiredService<ICredentialStore>(), settings.Current.Ai.BaseUrl);
            return Task.CompletedTask;
        });
        await InitializeSafelyAsync("Permissions", () => _host.Services.GetRequiredService<JsonPolicyService>().InitializeAsync());
        await InitializeSafelyAsync("Activity", () => _host.Services.GetRequiredService<JsonActionHistoryService>().InitializeAsync());
        await InitializeSafelyAsync("Applications", () => _host.Services.GetRequiredService<IApplicationCatalog>().InitializeAsync());
        await InitializeSafelyAsync("Workspaces", () => _host.Services.GetRequiredService<IWorkspaceService>().InitializeAsync());
        await InitializeSafelyAsync("Project cache", () => _host.Services.GetRequiredService<IProjectCatalog>().InitializeAsync());
        var windows = (WindowService)_host.Services.GetRequiredService<IWindowService>();
        _host.Services.GetRequiredService<WindowsForegroundEvents>().Start();
        EventManager.RegisterClassHandler(typeof(Window), FrameworkElement.LoadedEvent, new RoutedEventHandler((sender, _) =>
        {
            if (sender is Window own) _host.Services.GetRequiredService<ISelfWindowRegistry>().Register(new System.Windows.Interop.WindowInteropHelper(own).Handle);
        }));
        var diagnosticIndex = Array.IndexOf(e.Args, "--diagnostic-session");
        if (diagnosticIndex >= 0 && diagnosticIndex + 1 < e.Args.Length && e.Args[diagnosticIndex + 1] is "Calculator" or "Notepad" or "Settings")
        {
            var requestedApp = e.Args[diagnosticIndex + 1];
            var diagnostics = _host.Services.GetRequiredService<WindowsDiagnosticsService>();
            var outputDirectory = System.IO.Path.Combine(Environment.CurrentDirectory, "artifacts", "windows-diagnostics");
            diagnostics.ReportCompleted += (_, report) =>
            {
                // Opt-in diagnostic session only. Ordinary diagnostics do not persist raw UI metadata.
                System.IO.Directory.CreateDirectory(outputDirectory);
                System.IO.File.WriteAllText(System.IO.Path.Combine(outputDirectory, requestedApp + "-tracking.json"),
                    System.Text.Json.JsonSerializer.Serialize(report, new System.Text.Json.JsonSerializerOptions { WriteIndented = true }));
            };
            var diagnosticWindow = new Window { Title = "Part 6.2 Windows Diagnostics — " + requestedApp, Width = 780, Height = 740,
                Content = ActivatorUtilities.CreateInstance<WindowsDiagnosticsView>(_host.Services) };
            diagnosticWindow.Closed += (_, _) => Shutdown();
            diagnosticWindow.Show();
            return;
        }
        windows.ApplyTheme();
        try { _tray = _host.Services.GetRequiredService<TrayIconService>(); _tray.Start(); }
        catch (Exception exception) { logger.LogWarning(exception, "TrayUnavailable"); }
        logger.LogInformation("ApplicationStarted");

        if (_smokeTest)
        {
            windows.ShowCharacter();
            windows.TogglePanel();
            windows.ShowDesktopApp();
            windows.ShowWorkspaces();
            windows.ShowApplications();
            windows.ShowAssistant();
            windows.ShowPermissions();
            windows.ShowActivity();
            windows.ShowSettings();
            windows.ShowProjects();
            windows.ShowDeveloper();
            var projectPicker = new ProjectPickerWindow(_host.Services.GetRequiredService<IProjectCatalog>());
            projectPicker.Show();
            var workspaceEditor = ActivatorUtilities.CreateInstance<WorkspaceEditorWindow>(_host.Services, new WorkspaceDefinition { Name = "Smoke verification" });
            workspaceEditor.Show();
            var commandResult = await _host.Services.GetRequiredService<IComputerCommandService>().ExecuteAsync("help");
            if (!commandResult.Success)
            {
                Shutdown(-1);
                return;
            }
            _smokeTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
            _smokeTimer.Tick += (_, _) => { _smokeTimer.Stop(); windows.Shutdown(); };
            _smokeTimer.Start();
            return;
        }

        if (settings.Current.ShowCharacterOnStartup && !settings.Current.StartMinimized)
            windows.ShowCharacter();
        if (e.Args.Any(argument => argument.Equals("--open-app", StringComparison.OrdinalIgnoreCase)) ||
            !settings.Current.StartMinimized && !settings.Current.ComputerUse.OnboardingAcknowledged)
            windows.ShowDesktopApp();
        _ = RefreshCatalogSafelyAsync();
        _host.Services.GetRequiredService<WindowsUiAutomationService>().StartEvents();
        _host.Services.GetRequiredService<ComputerUseSafety>().Start();
        _host.Services.GetRequiredService<WindowsUiAutomationService>().ActionVerifying += (_, _) =>
            _host.Services.GetRequiredService<ComputerUseSession>().Update(ComputerTaskState.Verifying, "Checking the observed effect");
        if (settings.Current.Developer.Enabled) _ = Task.Run(async () =>
        {
            await InitializeSafelyAsync("Developer sessions", () => _host.Services.GetRequiredService<DeveloperSupervisor>().StartAsync());
            foreach (var adapter in _host.Services.GetServices<IDeveloperToolAdapter>())
                await InitializeSafelyAsync("Developer adapter " + adapter.ToolId, () => adapter.StartAsync(CancellationToken.None));
        });
        if (!settings.Current.OnboardingCompleted)
        {
            try
            {
                var onboarding = _host.Services.GetRequiredService<OnboardingWindow>();
                onboarding.ShowDialog();
                if (onboarding.DialogResult == true && Equals(onboarding.Tag, "Assistant")) windows.ShowAssistant();
                else if (onboarding.DialogResult == true && Equals(onboarding.Tag, "Permissions")) windows.ShowPermissions();
            }
            catch (Exception exception)
            {
                logger.LogWarning(exception, "OnboardingUnavailable");
                windows.ShowDesktopApp();
            }
        }
        else if (settings.Current.OnboardingVersion < OnboardingWindow.CurrentVersion)
        {
            settings.Current.OnboardingVersion = OnboardingWindow.CurrentVersion;
            await settings.SaveAsync(settings.Current);
            windows.ShowPermissions();
        }
    }

    private async Task InitializeSafelyAsync(string subsystem, Func<Task> initialize)
    {
        try { await initialize(); }
        catch (Exception exception)
        {
            _host?.Services.GetService<ILogger<App>>()?.LogWarning(exception, "StartupSubsystemUnavailable Subsystem={Subsystem}", subsystem);
        }
    }

    private async Task RefreshCatalogSafelyAsync()
    {
        try
        {
            var catalog = _host!.Services.GetRequiredService<IApplicationCatalog>();
            if (catalog.LastRefresh is null || catalog.LastRefresh < DateTimeOffset.Now.AddHours(-24))
            {
                var results = await catalog.RefreshAsync();
                var failed = results.Count(item => item.Status == DesktopAgent.Core.Applications.Enums.DiscoveryProviderStatus.Failed);
                _host.Services.GetRequiredService<IActionHistoryService>().Add(new ActionHistoryEntry(
                    DateTimeOffset.Now, ComputerCommandType.RefreshApplications, "Application catalog",
                    failed == results.Count ? ActionStatus.Failed : ActionStatus.Success,
                    failed == 0 ? $"Discovered {catalog.Applications.Count} applications." :
                        $"Discovered {catalog.Applications.Count} applications; {failed} source(s) failed.", "Startup"));
            }
        }
        catch (Exception exception)
        {
            _host?.Services.GetService<ILogger<App>>()?.LogWarning(exception, "StartupCatalogRefreshFailed");
        }
    }

    protected override async void OnExit(ExitEventArgs e)
    {
        _tray?.Dispose();
        if (_host is not null)
        {
            await _host.Services.GetRequiredService<PythonComputerBackend>().DisposeAsync();
            await _host.Services.GetRequiredService<DeveloperSupervisor>().StopAsync();
            await _host.StopAsync(TimeSpan.FromSeconds(3));
            _host.Dispose();
        }
        base.OnExit(e);
    }

    private void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        _host?.Services.GetService<ILogger<App>>()?.LogError(e.Exception, "UnhandledException");
        if (_smokeTest)
        {
            try { System.IO.File.WriteAllText(System.IO.Path.Combine(System.IO.Path.GetTempPath(), "DesktopAgent-smoke-error.txt"), e.Exception.ToString()); }
            catch (Exception) { }
            e.Handled = true;
            Shutdown(-1);
            return;
        }
        System.Windows.MessageBox.Show("Desktop Agent encountered an unexpected error and recovered. Details were written to the local log.",
            "Desktop Agent", MessageBoxButton.OK, MessageBoxImage.Warning);
        e.Handled = true;
    }

    private void OnUnhandledException(object sender, UnhandledExceptionEventArgs e) =>
        _host?.Services.GetService<ILogger<App>>()?.LogCritical(e.ExceptionObject as Exception, "UnhandledException");
}
