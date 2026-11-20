using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Themes.Fluent;
using Avalonia.Threading;
using Avalonia.Styling;
using DesktopAgent.Core.Enums;
using DesktopAgent.Desktop.ViewModels;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace DesktopAgent.Desktop;

public sealed class App(IServiceProvider services, bool qualify) : Application
{
    public override void Initialize() => Styles.Add(new FluentTheme());

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            var shell = services.GetRequiredService<ShellViewModel>();
            desktop.MainWindow = qualify ? new QualificationWindow() : new MainWindow(shell);
            void ApplyTheme() => RequestedThemeVariant = shell.Theme switch
            {
                AppTheme.Dark => ThemeVariant.Dark,
                AppTheme.Light => ThemeVariant.Light,
                _ => ThemeVariant.Default
            };
            System.ComponentModel.PropertyChangedEventHandler themeChanged = (_, e) =>
            {
                if (e.PropertyName == nameof(ShellViewModel.Theme)) ApplyTheme();
            };
            shell.PropertyChanged += themeChanged;
            ApplyTheme();
            DispatcherUnhandledExceptionEventHandler failed = (_, e) =>
            {
                QualificationLog.Write("ui-failed", new { type = e.Exception.GetType().FullName });
                e.Handled = true;
                desktop.Shutdown(1);
            };
            Dispatcher.UIThread.UnhandledException += failed;
            var stopping = services.GetRequiredService<IHostApplicationLifetime>().ApplicationStopping.Register(() =>
                Dispatcher.UIThread.Post(() => desktop.Shutdown()));
            desktop.Exit += (_, _) =>
            {
                stopping.Dispose();
                shell.PropertyChanged -= themeChanged;
                Dispatcher.UIThread.UnhandledException -= failed;
            };
            desktop.Exit += (_, args) => QualificationLog.Write("desktop-exit", new { code = args.ApplicationExitCode });
            var args = desktop.Args ?? [];
            var index = Array.IndexOf(args, "--auto-close-seconds");
            if (index >= 0 && index + 1 < args.Length && int.TryParse(args[index + 1], out var seconds) && seconds is >= 1 and <= 180)
            {
                var timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(seconds) };
                timer.Tick += (_, _) => { timer.Stop(); desktop.Shutdown(); };
                timer.Start();
            }
        }
        base.OnFrameworkInitializationCompleted();
    }
}
