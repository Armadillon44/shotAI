using Microsoft.Extensions.Logging;

namespace ShotAI.Core.Tests.Support;

/// <summary>An <see cref="ILogger{T}"/> over any provider (a <see cref="CapturingLoggerProvider"/> or the real file provider), for types that take one.</summary>
public static class TypedLogger
{
    public static ILogger<T> CreateLogger<T>(this ILoggerProvider provider)
    {
        ArgumentNullException.ThrowIfNull(provider);
        return new Logger<T>(provider.CreateLogger(typeof(T).FullName!));
    }

    private sealed class Logger<T>(ILogger inner) : ILogger<T>
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => inner.BeginScope(state);

        public bool IsEnabled(LogLevel logLevel) => inner.IsEnabled(logLevel);

        public void Log<TState>(
            LogLevel logLevel, EventId eventId, TState state, Exception? exception,
            Func<TState, Exception?, string> formatter) =>
            inner.Log(logLevel, eventId, state, exception, formatter);
    }
}
