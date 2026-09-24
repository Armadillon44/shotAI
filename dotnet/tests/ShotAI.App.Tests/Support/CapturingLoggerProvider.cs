using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;

namespace ShotAI.App.Tests.Support;

/// <summary>One log call captured by <see cref="CapturingLoggerProvider"/>.</summary>
internal sealed record LogEntry(string Category, LogLevel Level, string Message, Exception? Exception);

/// <summary>An <see cref="ILoggerProvider"/> and <see cref="ILoggerFactory"/> that records every line.</summary>
internal sealed class CapturingLoggerProvider : ILoggerProvider, ILoggerFactory
{
    private readonly ConcurrentQueue<LogEntry> _entries = new();

    /// <summary>Called with each entry as it is logged, for tests that interleave log lines with other events.</summary>
    public Action<LogEntry>? OnEntry { get; set; }

    /// <summary>Every entry so far, in the order it was logged.</summary>
    public IReadOnlyList<LogEntry> Entries => _entries.ToArray();

    public ILogger CreateLogger(string categoryName) => new CapturingLogger(categoryName, this);

    public void AddProvider(ILoggerProvider provider) => throw new NotSupportedException();

    public void Dispose() { }

    private sealed class CapturingLogger(string category, CapturingLoggerProvider owner) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => logLevel != LogLevel.None;

        public void Log<TState>(
            LogLevel logLevel, EventId eventId, TState state, Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            ArgumentNullException.ThrowIfNull(formatter);
            var entry = new LogEntry(category, logLevel, formatter(state, exception), exception);
            owner._entries.Enqueue(entry);
            owner.OnEntry?.Invoke(entry);
        }
    }
}
