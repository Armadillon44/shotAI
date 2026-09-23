using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;

namespace ShotAI.Core.Tests.Support;

/// <summary>
/// An <see cref="ILoggerProvider"/> that records every line, for tests that assert on log
/// text (spec 11 7.11) or on what must never be logged (ARCHITECTURE 8.5).
/// </summary>
public sealed class CapturingLoggerProvider : ILoggerProvider
{
    private readonly ConcurrentQueue<LogEntry> _entries = new();

    /// <summary>Every entry so far, in the order it was logged.</summary>
    public IReadOnlyList<LogEntry> Entries => _entries.ToArray();

    public ILogger CreateLogger(string categoryName) => new CapturingLogger(categoryName, _entries);

    public void Dispose() { }

    private sealed class CapturingLogger(string category, ConcurrentQueue<LogEntry> entries) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => logLevel != LogLevel.None;

        public void Log<TState>(
            LogLevel logLevel, EventId eventId, TState state, Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            ArgumentNullException.ThrowIfNull(formatter);
            entries.Enqueue(new LogEntry(category, logLevel, eventId, formatter(state, exception), exception));
        }
    }
}
