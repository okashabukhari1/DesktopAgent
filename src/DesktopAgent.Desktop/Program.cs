using Avalonia;
using DesktopAgent.Desktop.Services;

namespace DesktopAgent.Desktop;

internal static class Program
{
    [STAThread]
    public static int Main(string[] args)
    {
        // Native-only foundation: no WPF bootstrap, user repositories, credentials or worker.
        if (!OperatingSystem.IsLinux())
        {
            Console.Error.WriteLine("This qualification host requires Linux and native Wayland.");
            return 2;
        }
        try
        {
            using var host = DesktopHost.Build();
            host.StartAsync().GetAwaiter().GetResult();
            QualificationLog.Write("starting", new
            {
                avalonia = typeof(Application).Assembly.GetName().Version?.ToString(),
                session = Environment.GetEnvironmentVariable("XDG_SESSION_TYPE"),
                requestedBackend = "native-wayland-only",
                pid = Environment.ProcessId
            });
            // Deliberately no UsePlatformDetect(), UseX11(), or fallback path.
            try
            {
                return AppBuilder.Configure(() => new App(host.Services, args.Contains("--qualify", StringComparer.Ordinal)))
                    .UseSkia().UseHarfBuzz().UseWayland().StartWithClassicDesktopLifetime(args);
            }
            finally
            {
                using var stop = new CancellationTokenSource(TimeSpan.FromSeconds(5));
                host.StopAsync(stop.Token).GetAwaiter().GetResult();
            }
        }
        catch (Exception exception)
        {
            StartupFailure.Report(exception, Console.Error);
            QualificationLog.Write("startup-failed", new { type = exception.GetType().FullName });
            return 1;
        }
        finally { QualificationLog.Write("process-ended"); }
    }
}
