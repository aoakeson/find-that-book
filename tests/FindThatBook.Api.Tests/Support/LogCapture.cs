using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;

namespace FindThatBook.Api.Tests.Support;

/// <summary>Captures every log entry (all levels) with its rendered message and structured properties.</summary>
public sealed class LogCapture : ILoggerProvider
{
    public sealed record Entry(string Category, LogLevel Level, EventId EventId, string Message, IReadOnlyDictionary<string, object?> State, Exception? Exception)
    {
        /// <summary>Everything a log sink could write: message, property values and exception text.</summary>
        public string AllText => string.Join(" | ", [Message, .. State.Values.Select(v => v?.ToString() ?? ""), Exception?.ToString() ?? ""]);
    }

    public ConcurrentQueue<Entry> Entries { get; } = new();

    public IEnumerable<Entry> Named(string eventName) => Entries.Where(e => e.EventId.Name == eventName);

    public ILogger CreateLogger(string categoryName) => new Logger(this, categoryName);

    public void Dispose() { }

    private sealed class Logger(LogCapture owner, string category) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            var props = state is IEnumerable<KeyValuePair<string, object?>> kv
                ? kv.ToDictionary(p => p.Key, p => p.Value)
                : new Dictionary<string, object?>();
            owner.Entries.Enqueue(new Entry(category, logLevel, eventId, formatter(state, exception), props, exception));
        }
    }
}
