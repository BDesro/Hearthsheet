using Microsoft.Extensions.Logging;

namespace Hearthsheet.Cloud;

/// <summary>
/// Keeps the last 2,000 log lines in memory (each already passed through <see cref="SecretRedactor"/>) for the
/// web's "Download diagnostics". The optional sink receives each redacted line, e.g. the browser console.
/// </summary>
public sealed class MemoryLogProvider(Action<string>? sink = null) : ILoggerProvider
{
    private const int MaxLines = 2000;
    private readonly Queue<string> _lines = new();
    private readonly object _gate = new();

    public ILogger CreateLogger(string categoryName) => new Logger(this, categoryName);

    public string Snapshot()
    {
        lock (_gate) return string.Join('\n', _lines);
    }

    public void Dispose() { }

    private void Add(string line)
    {
        line = SecretRedactor.Redact(line);
        lock (_gate)
        {
            _lines.Enqueue(line);
            if (_lines.Count > MaxLines) _lines.Dequeue();
        }
        sink?.Invoke(line);
    }

    private sealed class Logger(MemoryLogProvider owner, string category) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => logLevel != LogLevel.None;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            var text = formatter(state, exception);
            if (exception is not null) text += " " + exception;
            // One line per entry: embedded newlines would break the line cap.
            owner.Add($"{DateTime.UtcNow:O} {logLevel} {category}: {text.ReplaceLineEndings(" | ")}");
        }
    }
}
