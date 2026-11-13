using System.Text;

namespace Manajoy.GameClass.Bridge.Hosting;

/// <summary>Minimal daily-rolling file logger (logs/&lt;prefix&gt;-YYYYMMDD.log); never throws into the caller.</summary>
internal sealed class FileLoggerProvider(string logDirectory, string filePrefix = "bridge") : ILoggerProvider
{
    private readonly object _sync = new();
    private static readonly TimeSpan RetryAfter = TimeSpan.FromMinutes(1);

    private StreamWriter? _writer;
    private string? _currentPath;
    private DateTime _pausedUntil;

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
            if (DateTime.UtcNow < _pausedUntil)
            {
                return;
            }

            try
            {
                var path = Path.Combine(BridgePaths.Resolve(logDirectory), $"{filePrefix}-{DateTime.Now:yyyyMMdd}.log");
                if (_writer is null || path != _currentPath)
                {
                    _writer?.Dispose();
                    Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                    var stream = new FileStream(path, FileMode.Append, FileAccess.Write, FileShare.ReadWrite);
                    _writer = new StreamWriter(stream, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false)) { AutoFlush = true };
                    _currentPath = path;
                }

                _writer.WriteLine(line);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // e.g. a sharing violation or a full disk: drop this line, reopen the file after a pause.
                _writer?.Dispose();
                _writer = null;
                _pausedUntil = DateTime.UtcNow + RetryAfter;
                Console.Error.WriteLine($"File logging paused for {RetryAfter.TotalSeconds:0}s: {ex.Message}");
            }
        }
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
                .Append(DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff"))
                .Append(' ')
                .Append(Level(logLevel))
                .Append(' ')
                .Append(categoryName[(categoryName.LastIndexOf('.') + 1)..])
                .Append(": ")
                .Append(formatter(state, exception));
            if (exception is not null)
            {
                line.Append(" | ").Append(exception.GetType().Name).Append(": ").Append(exception.Message);
            }

            provider.Write(line.ToString());
        }

        private static string Level(LogLevel level) => level switch
        {
            LogLevel.Trace => "TRC",
            LogLevel.Debug => "DBG",
            LogLevel.Information => "INF",
            LogLevel.Warning => "WRN",
            LogLevel.Error => "ERR",
            LogLevel.Critical => "CRT",
            _ => "???",
        };
    }
}
