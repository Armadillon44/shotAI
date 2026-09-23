using System.Buffers;

namespace ShotAI.Core.Store;

/// <summary>
/// The path boundary of a project folder (spec 01 2.10 and 7.5, ARCHITECTURE 9.2 S12):
/// <c>confinePath</c> and <c>confinePathNoSymlinks</c> of <c>src/main/path-confine.ts</c>, plus
/// the rejection of names Windows treats specially, which Electron lacks (IMPROVEMENT, D-6).
/// </summary>
/// <remarks>
/// Every read of a manifest-sourced path goes through <see cref="Confine"/>, and every write or
/// delete inside a project through <see cref="ConfineNoLinks"/> (INV-MODEL-13, INV-MODEL-14,
/// INV-MODEL-34).
/// </remarks>
public static class PathConfine
{
    // Windows-reserved in a name, and the DOS wildcards FindFirstFileExW would expand.
    private static readonly SearchValues<char> HostileChars =
        SearchValues.Create([(char)0, ':', '*', '?', '"', '<', '>', '|']);

    private static readonly char[] Separators = ['/', '\\'];

    // COM and LPT with a superscript digit are devices too.
    private const char Superscript1 = (char)0x00B9;
    private const char Superscript2 = (char)0x00B2;
    private const char Superscript3 = (char)0x00B3;

    /// <summary>
    /// <c>confinePath</c>: <paramref name="rel"/> resolved against <paramref name="dir"/>, or null
    /// when it is empty, hostile, the folder itself, outside the folder or on another root.
    /// </summary>
    /// <remarks>
    /// An absolute path that lands inside the folder is accepted, as in Electron, unless it is
    /// hostile, which a drive letter's colon is. <see cref="Path.GetFullPath(string)"/> applies
    /// the normalization <c>CreateFileW</c> will apply, so the checked path is the opened path.
    /// </remarks>
    public static string? Confine(string dir, string? rel)
    {
        ArgumentNullException.ThrowIfNull(dir);
        if (string.IsNullOrEmpty(rel) || HasHostileSegment(rel)) return null;
        string baseFull;
        string abs;
        try
        {
            baseFull = Path.GetFullPath(dir);
            // Path.Combine returns rel itself when it is rooted, which is what path.resolve does.
            abs = Path.GetFullPath(Path.Combine(baseFull, rel));
        }
        catch (Exception e) when (e is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return null;
        }
        var within = Path.GetRelativePath(baseFull, abs);
        if (within == "." || within.StartsWith("..", StringComparison.Ordinal) || Path.IsPathRooted(within)) return null;
        return abs;
    }

    /// <summary>
    /// <c>confinePathNoSymlinks</c>: <see cref="Confine"/>, then each component below
    /// <paramref name="dir"/> is probed in turn and the first link refuses, even one that points
    /// back inside the folder, because a link can be repointed between the check and the write
    /// (#82).
    /// </summary>
    /// <remarks>
    /// A missing component ends the walk with the path, which is the normal case for a write
    /// whose caller creates it. A probe that cannot tell refuses, and so does a file used as a
    /// directory, on every platform. <paramref name="dir"/> and its ancestors are the user's own
    /// layout and are not probed.
    /// </remarks>
    public static string? ConfineNoLinks(string dir, string? rel, IPathProbe probe)
    {
        ArgumentNullException.ThrowIfNull(probe);
        var abs = Confine(dir, rel);
        if (abs is null) return null;
        var baseFull = Path.GetFullPath(dir);
        var parts = Path.GetRelativePath(baseFull, abs).Split(Path.DirectorySeparatorChar);
        var current = baseFull;
        for (var i = 0; i < parts.Length; i++)
        {
            if (parts[i].Length == 0) continue;
            current = Path.Join(current, parts[i]);
            switch (probe.Probe(current))
            {
                case PathKind.Missing:
                    return abs;
                case PathKind.Directory:
                    continue;
                case PathKind.File:
                    if (i < parts.Length - 1) return null;
                    continue;
                default:
                    return null;
            }
        }
        return abs;
    }

    /// <summary>
    /// True when <paramref name="rel"/> holds a name Windows treats as something other than a
    /// plain file (EDGE-MODEL-20): a stream or drive colon, a wildcard or other reserved
    /// character, a device or verbatim prefix, a segment ending in a dot or a space, or a device
    /// name with or without an extension.
    /// </summary>
    /// <remarks>
    /// Applied on every platform, so the Linux tests cover it. The segments <c>.</c> and
    /// <c>..</c> are left to the lexical check.
    /// </remarks>
    public static bool HasHostileSegment(string rel)
    {
        ArgumentNullException.ThrowIfNull(rel);
        if (rel.AsSpan().ContainsAny(HostileChars)) return true;
        // The verbatim prefixes \\?\ and //?/ hold a '?', so only the device prefixes need a check.
        if (rel.StartsWith(@"\\.\", StringComparison.Ordinal) || rel.StartsWith("//./", StringComparison.Ordinal)) return true;
        foreach (var segment in rel.Split(Separators))
        {
            if (segment.Length == 0 || segment is "." or "..") continue;
            if (segment[^1] is '.' or ' ') return true;
            if (IsDeviceName(segment)) return true;
        }
        return false;
    }

    // Win32 compares the name before the first dot, without its trailing spaces, so "NUL.txt"
    // and "NUL .txt" both open the device.
    private static bool IsDeviceName(string segment)
    {
        var dot = segment.IndexOf('.');
        var stem = (dot < 0 ? segment.AsSpan() : segment.AsSpan(0, dot)).TrimEnd(' ');
        if (stem.Length == 3)
        {
            return stem.Equals("CON", StringComparison.OrdinalIgnoreCase)
                || stem.Equals("PRN", StringComparison.OrdinalIgnoreCase)
                || stem.Equals("AUX", StringComparison.OrdinalIgnoreCase)
                || stem.Equals("NUL", StringComparison.OrdinalIgnoreCase);
        }
        if (stem.Length == 4
            && (stem.StartsWith("COM", StringComparison.OrdinalIgnoreCase) || stem.StartsWith("LPT", StringComparison.OrdinalIgnoreCase)))
        {
            var n = stem[3];
            return n is >= '1' and <= '9' or Superscript1 or Superscript2 or Superscript3;
        }
        return false;
    }
}
