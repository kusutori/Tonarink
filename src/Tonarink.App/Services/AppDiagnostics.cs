using System.Diagnostics;
using Microsoft.Extensions.Logging;

namespace Tonarink.Services;

static class AppDiagnostics
{
    private static readonly Lock Gate = new();
    private static bool _sessionStarted;

    public static string LogFilePath => Path.Combine(AppPlatform.DataDirectory, "tonarink.log");

    public static ILoggerFactory LoggerFactory { get; } =
        Microsoft.Extensions.Logging.LoggerFactory.Create(builder =>
            builder
                .SetMinimumLevel(LogLevel.Information)
                .AddProvider(new DiagnosticsLoggerProvider()));

    public static void Initialize() => EnsureLogFile();

    public static void EnsureLogFile()
    {
        lock (Gate)
        {
            if (_sessionStarted)
                return;

            try
            {
                Directory.CreateDirectory(AppPlatform.DataDirectory);
                File.AppendAllText(
                    LogFilePath,
                    $"{Environment.NewLine}{DateTimeOffset.Now:O} [Information] [Tonarink] Session started{Environment.NewLine}");
                _sessionStarted = true;
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                Debug.WriteLine($"[Tonarink] Could not initialize the application log: {exception.Message}");
            }
        }
    }

    public static void Report(string operation, Exception exception) =>
        Write(LogLevel.Warning, "Tonarink", operation, exception);

    public static void Write(
        LogLevel level,
        string category,
        string message,
        Exception? exception = null)
    {
        var summary = $"[{level}] [{category}] {message}";
        Trace.WriteLine(exception is null ? summary : $"{summary} {exception}");

        lock (Gate)
        {
            try
            {
                Directory.CreateDirectory(AppPlatform.DataDirectory);
                File.AppendAllText(
                    LogFilePath,
                    $"{DateTimeOffset.Now:O} {summary}" +
                    (exception is null
                        ? Environment.NewLine
                        : $"{Environment.NewLine}{exception}{Environment.NewLine}"));
            }
            catch (Exception logException) when (logException is IOException or UnauthorizedAccessException)
            {
                Debug.WriteLine($"[Tonarink] Could not write the application log: {logException.Message}");
            }
        }
    }

    private sealed class DiagnosticsLoggerProvider : ILoggerProvider
    {
        public ILogger CreateLogger(string categoryName) => new DiagnosticsLogger(categoryName);

        public void Dispose()
        {
        }
    }

    private sealed class DiagnosticsLogger(string categoryName) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => logLevel >= LogLevel.Information;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            if (IsEnabled(logLevel))
                Write(logLevel, categoryName, formatter(state, exception), exception);
        }
    }
}
