using Microsoft.Extensions.Logging;

namespace ShotAI.Platform.Tests.Support;

/// <summary>An <see cref="ILogger{TCategoryName}"/> that keeps each entry, for the tests of what Platform logs.</summary>
internal sealed class ListLogger<T> : ILogger<T>
{
    private readonly List<(LogLevel Level, string Message)> _entries = [];

    public IReadOnlyList<(LogLevel Level, string Message)> Entries
    {
        get
        {
            lock (_entries) return [.. _entries];
        }
    }

    public IDisposable? BeginScope<TState>(TState state)
        where TState : notnull => null;

    public bool IsEnabled(LogLevel logLevel) => true;

    public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
    {
        ArgumentNullException.ThrowIfNull(formatter);
        lock (_entries) _entries.Add((logLevel, formatter(state, exception)));
    }
}
