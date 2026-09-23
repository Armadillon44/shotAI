using ShotAI.Core.Store;
using Xunit;

namespace ShotAI.Core.Tests.Store;

/// <summary>
/// Every rule of <see cref="PathConfine.HasHostileSegment"/> (spec 01 7.5, EDGE-MODEL-20,
/// AC-MODEL-27), true and false, on every platform.
/// </summary>
public sealed class HostileSegmentTests
{
    private static readonly string Nul = ((char)0).ToString();
    private static readonly string Sup1 = ((char)0x00B9).ToString();
    private static readonly string Sup2 = ((char)0x00B2).ToString();
    private static readonly string Sup3 = ((char)0x00B3).ToString();

    public static TheoryData<string> Hostile() =>
    [
        // Reserved characters, including a stream or drive colon and the DOS wildcards.
        "shots/a.png:evil", "C:x", "D:\\x", "a*b", "a?b", "a\"b", "a<b", "a>b", "a|b", "shots/a" + Nul + ".png",
        // Verbatim and device prefixes, with either separator.
        "\\\\?\\C:\\x", "\\\\.\\COM1", "//?/C:/x", "//./PhysicalDrive0",
        // A segment ending in a dot or a space, wherever it is.
        "shots/a.png.", "shots/a.png ", "shots./a.png", "shots /a.png", "...", "a\\b.",
        // Device names, any case, with or without an extension, in any segment.
        "CON", "con", "Con.txt", "shots/NUL", "nul.tar.gz", "PRN", "AUX.png", "shots\\aux",
        "COM1", "com9.log", "LPT1", "lpt9", "COM" + Sup1, "LPT" + Sup2 + ".txt", "com" + Sup3,
        // Win32 drops the stem's trailing spaces before it compares (added in WP-A5).
        "NUL .txt", "shots/CON  .png",
    ];

    [Theory]
    [MemberData(nameof(Hostile))]
    public void Rejects(string rel) => Assert.True(PathConfine.HasHostileSegment(rel), rel);

    public static TheoryData<string> Plain() =>
    [
        "shots/step-0001.png", "export/.render/9a1b3c5d.png", "project.json", ".hidden", "a.b.c",
        "name with spaces.png", " leading.png", "shots/../x.png", "./x.png", "..", ".", "",
        // Near misses of the device names.
        "COM0", "COM10", "LPT0", "LPT10", "COM", "LPT", "CONSOLE", "CONx.txt", "NULL", "AUXILIARY",
        "PRNs", "xCON", "COM1x", "COM" + ((char)0x2074).ToString(),
    ];

    [Theory]
    [MemberData(nameof(Plain))]
    public void Allows(string rel) => Assert.False(PathConfine.HasHostileSegment(rel), rel);

    /// <summary>AC-MODEL-27.</summary>
    [Fact]
    public void ConfineRefusesAStreamAndADeviceName()
    {
        var dir = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "project"));
        Assert.Null(PathConfine.Confine(dir, "shots/a.png:x"));
        Assert.Null(PathConfine.Confine(dir, "shots/CON.png"));
    }

    [Fact]
    public void NullIsRefused() => Assert.Throws<ArgumentNullException>(() => PathConfine.HasHostileSegment(null!));
}
