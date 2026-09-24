using System.Globalization;
using Microsoft.Extensions.Logging;

namespace ShotAI.Core.Logging;

/// <summary>
/// One line of <c>shotai.log</c> in electron-log's file format (spec 10 7.5.2), so support reads
/// the Electron build's lines and the native app's the same way.
/// </summary>
/// <remarks>
/// <c>[yyyy-MM-dd HH:mm:ss.fff] [level] (label)    message</c>: local time, the level and its
/// bracket padded to 6, the label in parentheses after a space padded to 11 (11 spaces when
/// there is none), a space, the message verbatim, a space and <see cref="Exception.ToString"/>
/// when there is an exception, and <c>\r\n</c> on every OS. The message is never read as a format
/// string (EDGE-INFRA-49). Numbers and separators are invariant, whatever the current culture.
/// </remarks>
public static class FileLogLineFormatter
{
    /// <summary>
    /// The width of the label text: Electron's longest label, <c>projects</c>, plus 3. Fixed, not
    /// computed at run time, so a label longer than <see cref="LogCategories.MaxLabelLength"/> would
    /// widen the line.
    /// </summary>
    public const int ScopeWidth = LogCategories.MaxLabelLength + 3;

    /// <summary>The line end, the same on every OS, so Linux tests see the bytes Windows writes.</summary>
    public const string LineEnd = "\r\n";

    private static readonly string NoScope = new(' ', ScopeWidth);

    /// <summary>The whole line, <see cref="LineEnd"/> included.</summary>
    /// <param name="time">The local time of the call (electron-log writes local time).</param>
    /// <param name="level">Any level but <see cref="LogLevel.None"/>.</param>
    /// <param name="label">A label of <see cref="LogCategories"/>, or the empty banner label.</param>
    /// <param name="message">The text, written as it is.</param>
    /// <param name="exception">Appended after a space when not null.</param>
    public static string Format(DateTimeOffset time, LogLevel level, string label, string? message, Exception? exception) =>
        FormatWithScope(time, level, ScopeText(label), message, exception);

    /// <summary>
    /// electron-log's name for a level: <c>error</c> (for <see cref="LogLevel.Critical"/> too),
    /// <c>warn</c>, <c>info</c>, <c>debug</c> and <c>silly</c> for <see cref="LogLevel.Trace"/>,
    /// electron-log's level below <c>debug</c> (its <c>verbose</c> is above <c>debug</c>).
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="level"/> is <see cref="LogLevel.None"/> or undefined.</exception>
    public static string LevelName(LogLevel level) => level switch
    {
        LogLevel.Critical or LogLevel.Error => "error",
        LogLevel.Warning => "warn",
        LogLevel.Information => "info",
        LogLevel.Debug => "debug",
        LogLevel.Trace => "silly",
        _ => throw new ArgumentOutOfRangeException(nameof(level), level, "Only a level that is written has a name."),
    };

    /// <summary>
    /// <c>" (" + label + ")"</c> padded right to <see cref="ScopeWidth"/>, or that many spaces for
    /// the empty label.
    /// </summary>
    public static string ScopeText(string label)
    {
        ArgumentNullException.ThrowIfNull(label);
        return label.Length == 0 ? NoScope : (" (" + label + ")").PadRight(ScopeWidth);
    }

    /// <summary>
    /// <see cref="Format"/> with the label text already built; the logger builds it once. The
    /// result is the only allocation besides the exception text.
    /// </summary>
    internal static string FormatWithScope(DateTimeOffset time, LogLevel level, string scopeText, string? message, Exception? exception)
    {
        var bracket = LevelBracket(level);
        var exceptionText = exception?.ToString();
        return exceptionText is null
            ? string.Create(CultureInfo.InvariantCulture, $"[{time:yyyy-MM-dd HH:mm:ss.fff}] [{bracket}{scopeText} {message}{LineEnd}")
            : string.Create(CultureInfo.InvariantCulture, $"[{time:yyyy-MM-dd HH:mm:ss.fff}] [{bracket}{scopeText} {message} {exceptionText}{LineEnd}");
    }

    // (name + "]") padded right to 6, as electron-log pads "{level}]".
    private static string LevelBracket(LogLevel level) => level switch
    {
        LogLevel.Critical or LogLevel.Error => "error]",
        LogLevel.Warning => "warn] ",
        LogLevel.Information => "info] ",
        LogLevel.Debug => "debug]",
        LogLevel.Trace => "silly]",
        _ => throw new ArgumentOutOfRangeException(nameof(level), level, "Only a level that is written has a name."),
    };
}
