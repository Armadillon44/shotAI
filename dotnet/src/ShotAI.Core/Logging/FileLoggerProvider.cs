using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;

namespace ShotAI.Core.Logging;

/// <summary>
/// The one logging provider (spec 10 7.5.1, ARCHITECTURE 8.4): writes <c>shotai.log</c> in
/// electron-log's format through a <see cref="RotatingFileSink"/>. First-party, and it depends on
/// the logging abstractions only.
/// </summary>
/// <remarks>
/// <para>
/// A logger formats its line on the caller's thread, stamped with the local time, and queues it;
/// a call never waits for the disk and never throws. A line whose message or exception text
/// cannot be built is dropped and counted like a line the full queue refused. Scopes are not
/// written: electron-log's format has no place for them.
/// </para>
/// <para>
/// The App builds the factory with this provider at startup step 1 and sets the factory's minimum
/// to the same <see cref="FileLogOptions.MinimumLevel"/>; the provider applies it too, so a
/// disabled level costs a comparison. The crash handlers call <see cref="Flush"/>, and disposal
/// writes what is queued, waiting at most 2 s.
/// </para>
/// </remarks>
public sealed class FileLoggerProvider : ILoggerProvider
{
    private readonly RotatingFileSink _sink;
    private readonly TimeProvider _time;
    private readonly LogLevel _minimum;
    private readonly ConcurrentDictionary<string, FileLogger> _loggers = new(StringComparer.Ordinal);

    /// <exception cref="ArgumentOutOfRangeException">A bound in <paramref name="options"/> is not positive.</exception>
    public FileLoggerProvider(FileLogOptions options, TimeProvider timeProvider)
        : this(options, timeProvider, startWriter: true)
    {
    }

    /// <summary>See <see cref="RotatingFileSink(FileLogOptions, TimeProvider, bool)"/>.</summary>
    internal FileLoggerProvider(FileLogOptions options, TimeProvider timeProvider, bool startWriter)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(timeProvider);
        _sink = new RotatingFileSink(options, timeProvider, startWriter);
        _time = timeProvider;
        _minimum = options.MinimumLevel;
    }

    /// <summary>The active log file, for the banner's <c>logs: &lt;path&gt;</c> line.</summary>
    public string LogFile => _sink.LogFile;

    /// <summary>A logger whose label is <see cref="LogCategories.LabelFor"/> the category; one per category.</summary>
    public ILogger CreateLogger(string categoryName)
    {
        ArgumentNullException.ThrowIfNull(categoryName);
        return _loggers.GetOrAdd(categoryName, static (category, provider) => new FileLogger(provider, LogCategories.LabelFor(category)), this);
    }

    /// <inheritdoc cref="RotatingFileSink.Flush"/>
    public bool Flush(TimeSpan timeout) => _sink.Flush(timeout);

    /// <inheritdoc cref="RotatingFileSink.Dispose"/>
    public void Dispose() => _sink.Dispose();

    private sealed class FileLogger(FileLoggerProvider provider, string label) : ILogger
    {
        private readonly string _scopeText = FileLogLineFormatter.ScopeText(label);

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => logLevel != LogLevel.None && logLevel >= provider._minimum;

        public void Log<TState>(
            LogLevel logLevel, EventId eventId, TState state, Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            if (!IsEnabled(logLevel)) return;
            string line;
            try
            {
                var message = formatter is null ? state?.ToString() : formatter(state, exception);
                line = FileLogLineFormatter.FormatWithScope(provider._time.GetLocalNow(), logLevel, _scopeText, message, exception);
            }
            catch (Exception)
            {
                provider._sink.CountDropped();
                return;
            }
            provider._sink.Write(line);
        }
    }
}
