using System.Text;
using Microsoft.Extensions.Logging;

namespace Manajoy.GameClass.Inspector.Infrastructure.Logging;

/// <summary>
/// Minimal daily-rolling file logger (logs/inspector-YYYYMMDD.log). Filtering is done by the
/// logging framework using the "Logging" configuration section.
/// </summary>
internal sealed class FileLoggerProvider(string logDirectory) : ILoggerProvider
{
    private readonly object _sync = new();
    private StreamWriter? _writer;
    private string? _currentPath;
    private bool _disabled;

    public ILogger CreateLogger(string categoryName) => new FileLogger(categoryName, this);

    public void Dispose()
    {
        lock (_sync)
        {
            _writer?.Dispose();
            _writer = null;
        }
    }

    private void Write(string line)
    {
        lock (_sync)
        {
            if (_disabled)
            {
                return;
            }

            try
            {
                GetWriter().WriteLine(line);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // Logging must never stop the inspection; fall back to console-only logging.
                _disabled = true;
                Console.Error.WriteLine($"File logging disabled: {ex.Message}");
            }
        }
    }

    private StreamWriter GetWriter()
    {
        var path = AppPaths.LogFilePath(logDirectory, DateTime.Now);
        if (_writer is not null && path == _currentPath)
        {
            return _writer;
        }

        _writer?.Dispose();
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var stream = new FileStream(path, FileMode.Append, FileAccess.Write, FileShare.ReadWrite);
        _writer = new StreamWriter(stream, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false)) { AutoFlush = true };
        _currentPath = path;
        return _writer;
    }

    private sealed class FileLogger(string categoryName, FileLoggerProvider provider) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => logLevel != LogLevel.None;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            if (!IsEnabled(logLevel))
            {
                return;
            }

            var line = new StringBuilder()
                .Append(DateTimeOffset.Now.ToString("yyyy-MM-dd HH:mm:ss.fff zzz", System.Globalization.CultureInfo.InvariantCulture))
                .Append(" [").Append(ShortLevel(logLevel)).Append("] ")
                .Append(categoryName).Append(": ")
                .Append(formatter(state, exception));

            if (exception is not null)
            {
                line.AppendLine().Append(exception);
            }

            provider.Write(line.ToString());
        }

        private static string ShortLevel(LogLevel level) => level switch
        {
            LogLevel.Trace => "TRC",
            LogLevel.Debug => "DBG",
            LogLevel.Information => "INF",
            LogLevel.Warning => "WRN",
            LogLevel.Error => "ERR",
            LogLevel.Critical => "CRT",
            _ => level.ToString(),
        };
    }
}
