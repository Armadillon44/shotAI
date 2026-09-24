using System.Text.RegularExpressions;

namespace ShotAI.Core.Report;

/// <summary>
/// Which failed opens the user hears about (spec 05 7.3, EDGE-REP-39): a project that is gone is
/// not worth a notice, as in Electron (<c>store.ts:134</c>); any other failure is shown on Home.
/// </summary>
public static partial class OpenFailure
{
    [GeneratedRegex("ENOENT|no such file|not found", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex GoneMessage();

    /// <summary>
    /// True when <paramref name="e"/>'s message matches Electron's
    /// <c>/ENOENT|no such file|not found/i</c>, or when it, or an exception it wraps, is a
    /// <see cref="FileNotFoundException"/> or <see cref="DirectoryNotFoundException"/>. The store
    /// reports a missing <c>project.json</c> as a <c>ManifestCorruptException</c> around the
    /// file-system one (spec 01 7.13), where Electron's message carried <c>ENOENT</c>, so the
    /// wrapped exceptions are looked through.
    /// </summary>
    public static bool IsGone(Exception e)
    {
        ArgumentNullException.ThrowIfNull(e);
        if (GoneMessage().IsMatch(e.Message)) return true;
        for (var inner = e; inner is not null; inner = inner.InnerException)
        {
            if (inner is FileNotFoundException or DirectoryNotFoundException) return true;
        }
        return false;
    }
}
