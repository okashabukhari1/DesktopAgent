using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;

namespace DesktopAgent.Infrastructure.Logging;

public sealed class FileLoggerProvider : ILoggerProvider
{
    private readonly string _logDirectory;
    private readonly ConcurrentDictionary<string, FileLogger> _loggers = new();

    public FileLoggerProvider(string? logDirectory = null)
    {
        _logDirectory = logDirectory ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "DesktopAgent", "logs");
    }

    public ILogger CreateLogger(string categoryName) =>
        _loggers.GetOrAdd(categoryName, name => new FileLogger(name, _logDirectory));

    public void Dispose() => _loggers.Clear();

    private sealed class FileLogger(string category, string directory) : ILogger
    {
        private static readonly object Sync = new();
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => logLevel >= LogLevel.Information;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            if (!IsEnabled(logLevel)) return;
            try
            {
                Directory.CreateDirectory(directory);
                var line = $"{DateTimeOffset.Now:O} [{logLevel}] {category}: {formatter(state, exception)}";
                if (exception is not null) line += $" | {exception.GetType().Name}: {exception.Message}";
                lock (Sync) File.AppendAllText(Path.Combine(directory, $"desktop-agent-{DateTime.Now:yyyy-MM-dd}.log"), line + Environment.NewLine);
            }
            catch
            {
                // Logging must never terminate the desktop companion.
            }
        }
    }
}
