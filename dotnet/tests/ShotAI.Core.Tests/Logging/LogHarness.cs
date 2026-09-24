using System.Text;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Time.Testing;
using ShotAI.Core.Logging;
using ShotAI.Core.Tests.Support;

namespace ShotAI.Core.Tests.Logging;

/// <summary>
/// A logs folder under a temp directory with the sinks and providers a test makes over it, all
/// disposed with it. The clock is fake and its local time zone is UTC, so every line is stamped
/// <see cref="Stamp"/> until a test moves it.
/// </summary>
internal sealed class LogHarness : IDisposable
{
    /// <summary>The clock's time, the first example of spec 10 7.5.2.</summary>
    public static readonly DateTimeOffset Now = new(2026, 9, 23, 9, 15, 2, 114, TimeSpan.Zero);

    /// <summary>The timestamp every line starts with.</summary>
    public const string Stamp = "[2026-09-23 09:15:02.114]";

    private readonly List<IDisposable> _owned = [];

    public TempDir Temp { get; } = new("logs-");

    public FakeTimeProvider Time { get; } = new(Now);

    public string LogsDirectory => Temp.Combine("userData", "logs");

    public string LogFile => Path.Combine(LogsDirectory, FileLogOptions.FileName);

    public string OldLogFile => Path.Combine(LogsDirectory, FileLogOptions.OldFileName);

    /// <summary>Options over <see cref="LogsDirectory"/>; the bounds are the spec's unless given.</summary>
    public FileLogOptions Options(
        long maxBytes = FileLogOptions.DefaultMaxBytes,
        int capacity = FileLogOptions.DefaultCapacity,
        int batchBytes = FileLogOptions.DefaultBatchBytes,
        LogLevel minimum = LogLevel.Trace) =>
        new(LogsDirectory) { MaxBytes = maxBytes, Capacity = capacity, BatchBytes = batchBytes, MinimumLevel = minimum };

    /// <summary>A sink over <paramref name="options"/>; without its writer unless asked, so only a flush writes.</summary>
    public RotatingFileSink Sink(FileLogOptions? options = null, bool startWriter = false) =>
        Own(new RotatingFileSink(options ?? Options(), Time, startWriter));

    /// <summary>A provider over <paramref name="options"/>; without its writer unless asked.</summary>
    public FileLoggerProvider Provider(FileLogOptions? options = null, bool startWriter = false) =>
        Own(new FileLoggerProvider(options ?? Options(), Time, startWriter));

    /// <summary><c>shotai.log</c> as UTF-8, or null when it does not exist.</summary>
    public string? Text() => File.Exists(LogFile) ? File.ReadAllText(LogFile, new UTF8Encoding(false, true)) : null;

    /// <summary><c>shotai.old.log</c> as UTF-8, or null when it does not exist.</summary>
    public string? OldText() => File.Exists(OldLogFile) ? File.ReadAllText(OldLogFile, new UTF8Encoding(false, true)) : null;

    /// <summary>The lines of <c>shotai.log</c> without their <c>\r\n</c>; empty when it does not exist.</summary>
    public string[] Lines()
    {
        var text = Text();
        if (string.IsNullOrEmpty(text)) return [];
        Xunit.Assert.EndsWith("\r\n", text, StringComparison.Ordinal);
        return text[..^2].Split("\r\n");
    }

    /// <summary>The names of the entries in the logs folder, sorted.</summary>
    public string[] Entries() =>
        Directory.Exists(LogsDirectory)
            ? [.. Directory.EnumerateFileSystemEntries(LogsDirectory).Select(Path.GetFileName).Order(StringComparer.Ordinal)!]
            : [];

    /// <summary>
    /// A line of exactly <paramref name="bytes"/> UTF-8 bytes, its <c>\r\n</c> included, starting
    /// with <paramref name="tag"/>: what the sink stores is opaque to it, so tests size lines freely.
    /// </summary>
    public static string Line(string tag, int bytes)
    {
        var body = bytes - 2 - Encoding.UTF8.GetByteCount(tag);
        Xunit.Assert.True(body >= 0, $"a {bytes}-byte line cannot start with {tag}");
        return tag + new string('.', body) + "\r\n";
    }

    private T Own<T>(T disposable) where T : IDisposable
    {
        _owned.Add(disposable);
        return disposable;
    }

    public void Dispose()
    {
        try
        {
            foreach (var d in _owned) d.Dispose();
        }
        finally
        {
            Temp.Dispose();
        }
    }
}
