using System.Globalization;
using System.Text.RegularExpressions;

namespace ShotAI.Core.Json;

/// <summary>
/// JavaScript <c>Date</c> text: <c>toISOString</c> and the ISO subset of <c>Date.parse</c>
/// (spec 01 7.2.3, D-17).
/// </summary>
public static partial class IsoTime
{
    /// <summary>
    /// <c>new Date(t).toISOString()</c>: UTC, always three fractional digits and <c>Z</c>.
    /// </summary>
    /// <remarks>Never the <c>"o"</c> format, which writes seven fractional digits.</remarks>
    public static string ToIsoString(DateTimeOffset t) =>
        t.UtcDateTime.ToString("yyyy'-'MM'-'dd'T'HH':'mm':'ss'.'fff'Z'", CultureInfo.InvariantCulture);

    /// <summary>
    /// The ECMAScript Date Time String Format subset of <c>Date.parse</c>.
    /// </summary>
    /// <remarks>
    /// Accepts <c>YYYY</c>, <c>YYYY-MM</c> or <c>YYYY-MM-DD</c> (UTC), optionally followed by
    /// <c>THH:mm</c>, <c>:ss</c> and <c>.s</c> with one or more digits (truncated to
    /// milliseconds), and then optionally <c>Z</c>, <c>+HH:mm</c> or <c>-HH:mm</c>. A
    /// date-time with no offset is local time in <paramref name="local"/>: an ambiguous
    /// local time takes the earlier instant and a skipped one moves forward, as ECMAScript's
    /// disambiguation does. <c>T24:00</c> is the end of that day. Anything else fails.
    /// IMPROVEMENT: V8 also accepts legacy formats, which no shotAI writer produces.
    /// </remarks>
    public static bool TryParseJsDate(string s, TimeZoneInfo local, out DateTimeOffset t)
    {
        ArgumentNullException.ThrowIfNull(local);
        t = default;
        if (s is null) return false;

        var m = DateTimeFormat().Match(s);
        if (!m.Success) return false;

        var year = Int(m.Groups["y"]);
        var month = m.Groups["mo"].Success ? Int(m.Groups["mo"]) : 1;
        var day = m.Groups["d"].Success ? Int(m.Groups["d"]) : 1;
        if (year < 1 || month is < 1 or > 12 || day < 1 || day > DateTime.DaysInMonth(year, month)) return false;

        var hasTime = m.Groups["h"].Success;
        var hour = hasTime ? Int(m.Groups["h"]) : 0;
        var minute = hasTime ? Int(m.Groups["mi"]) : 0;
        var second = m.Groups["s"].Success ? Int(m.Groups["s"]) : 0;
        var ms = m.Groups["f"].Success ? Int(m.Groups["f"].Value.PadRight(3, '0')[..3]) : 0;
        if (minute > 59 || second > 59) return false;
        var endOfDay = hour == 24;
        if (hour > 24 || (endOfDay && (minute != 0 || second != 0 || ms != 0))) return false;

        var clock = new DateTime(year, month, day, endOfDay ? 0 : hour, minute, second, ms, DateTimeKind.Unspecified);
        if (endOfDay)
        {
            if (clock.Date == DateTime.MaxValue.Date) return false;
            clock = clock.AddDays(1);
        }

        TimeSpan offset;
        var zone = m.Groups["z"];
        if (!hasTime || zone.Value == "Z")
        {
            offset = TimeSpan.Zero;                   // date-only forms and Z are UTC
        }
        else if (zone.Success)
        {
            var oh = Int(m.Groups["oh"]);
            var om = Int(m.Groups["om"]);
            if (oh > 23 || om > 59) return false;
            offset = new TimeSpan(oh, om, 0);
            if (zone.Value[0] == '-') offset = -offset;
        }
        else
        {
            offset = LocalOffset(clock, local, out clock);
        }

        try
        {
            t = new DateTimeOffset(clock, offset);
            return true;
        }
        catch (ArgumentOutOfRangeException)
        {
            return false;                             // the instant falls outside DateTimeOffset
        }
    }

    /// <summary>
    /// A local clock time in <paramref name="local"/> as the instant <c>new Date(y, m, d, ...)</c>
    /// gives it: the earlier instant when the time occurs twice, and when it was skipped, the
    /// offset in force before the transition, which moves the clock forward by the gap.
    /// </summary>
    /// <remarks>
    /// The same rule <see cref="TryParseJsDate"/> applies to a date-time with no offset; spec 06's
    /// date buckets resolve each local midnight with it (added in WP-A16).
    /// </remarks>
    /// <param name="clock">The wall-clock time; its kind is ignored.</param>
    /// <param name="local">The zone the clock time is read in.</param>
    /// <exception cref="ArgumentOutOfRangeException">The instant falls outside <see cref="DateTimeOffset"/>.</exception>
    public static DateTimeOffset FromLocalTime(DateTime clock, TimeZoneInfo local)
    {
        ArgumentNullException.ThrowIfNull(local);
        var offset = LocalOffset(DateTime.SpecifyKind(clock, DateTimeKind.Unspecified), local, out var adjusted);
        return new DateTimeOffset(adjusted, offset);
    }

    // ECMAScript's disambiguation of a local clock time: the earlier instant when the time
    // occurs twice, and when it was skipped, the offset in force before the transition,
    // which moves the clock forward by the gap.
    private static TimeSpan LocalOffset(DateTime clock, TimeZoneInfo local, out DateTime adjusted)
    {
        adjusted = clock;
        if (local.IsAmbiguousTime(clock)) return local.GetAmbiguousTimeOffsets(clock).Max();
        if (!local.IsInvalidTime(clock)) return local.GetUtcOffset(clock);

        var before = local.GetUtcOffset(clock.AddHours(-3));
        var after = local.GetUtcOffset(clock.AddHours(3));
        adjusted = clock + (after - before);
        return after;
    }

    private static int Int(Group g) => Int(g.Value);

    private static int Int(string digits) => int.Parse(digits, NumberStyles.None, CultureInfo.InvariantCulture);

    [GeneratedRegex(
        @"^(?<y>[0-9]{4})(?:-(?<mo>[0-9]{2})(?:-(?<d>[0-9]{2}))?)?(?:T(?<h>[0-9]{2}):(?<mi>[0-9]{2})(?::(?<s>[0-9]{2})(?:\.(?<f>[0-9]+))?)?(?<z>Z|[+-](?<oh>[0-9]{2}):(?<om>[0-9]{2}))?)?\z",
        RegexOptions.CultureInvariant)]
    private static partial Regex DateTimeFormat();
}
