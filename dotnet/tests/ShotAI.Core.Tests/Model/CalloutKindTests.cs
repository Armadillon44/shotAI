using System.Globalization;
using System.Text.Json.Nodes;
using ShotAI.Core.Model;
using Xunit;

namespace ShotAI.Core.Tests.Model;

/// <summary>
/// Ports the <c>isCalloutKind</c> and <c>CALLOUT_GLYPH</c> cases of <c>src/shared/project.test.ts</c>
/// and the <c>isCalloutKind is exact</c> cases of <c>src/main/unknown-callout.test.ts</c>
/// (spec 01 2.12, INV-MODEL-24, INV-MODEL-25, AC-MODEL-32).
/// </summary>
public sealed class CalloutKindTests
{
    /// <summary>
    /// The Emoji_Presentation code points from U+2000 to U+2BFF, the block the glyphs live in.
    /// .NET has no Emoji_Presentation property. Transcribed from spec 01 8.1 and checked in
    /// WP-A3 against Node 22's <c>\p{Emoji_Presentation}</c> (Unicode 17.0, ICU 78.2), the
    /// oracle the TypeScript test itself uses.
    /// </summary>
    private static readonly (int First, int Last)[] EmojiPresentation =
    [
        (0x231A, 0x231B), (0x23E9, 0x23EC), (0x23F0, 0x23F0), (0x23F3, 0x23F3), (0x25FD, 0x25FE),
        (0x2614, 0x2615), (0x2648, 0x2653), (0x267F, 0x267F), (0x2693, 0x2693), (0x26A1, 0x26A1),
        (0x26AA, 0x26AB), (0x26BD, 0x26BE), (0x26C4, 0x26C5), (0x26CE, 0x26CE), (0x26D4, 0x26D4),
        (0x26EA, 0x26EA), (0x26F2, 0x26F3), (0x26F5, 0x26F5), (0x26FA, 0x26FA), (0x26FD, 0x26FD),
        (0x2705, 0x2705), (0x270A, 0x270B), (0x2728, 0x2728), (0x274C, 0x274C), (0x274E, 0x274E),
        (0x2753, 0x2755), (0x2757, 0x2757), (0x2795, 0x2797), (0x27B0, 0x27B0), (0x27BF, 0x27BF),
        (0x2B1B, 0x2B1C), (0x2B50, 0x2B50), (0x2B55, 0x2B55),
    ];

    private static bool IsEmojiPresentation(int cp) => EmojiPresentation.Any(r => cp >= r.First && cp <= r.Last);

    [Theory]
    [InlineData("note")]
    [InlineData("caution")]
    [InlineData("warning")]
    [InlineData("section")]
    public void AcceptsTheFourKinds(string kind)
    {
        Assert.True(CalloutKinds.IsCalloutKind(kind));
        Assert.True(CalloutKinds.IsCalloutKind(JsonValue.Create(kind)));
    }

    [Theory]
    [InlineData("text")]
    [InlineData("")]
    [InlineData("danger")]
    [InlineData("tip")]
    [InlineData("futurekind")]
    [InlineData("NOTE")]
    [InlineData("Note")]
    [InlineData("toString")]
    public void RejectsOtherStrings(string value)
    {
        Assert.False(CalloutKinds.IsCalloutKind(value));
        Assert.False(CalloutKinds.IsCalloutKind(JsonValue.Create(value)));
    }

    [Fact]
    public void RejectsWhatIsNotAString()
    {
        Assert.False(CalloutKinds.IsCalloutKind(null));
        Assert.False(CalloutKinds.IsCalloutKind(5));
        Assert.False(CalloutKinds.IsCalloutKind(42));
        Assert.False(CalloutKinds.IsCalloutKind(JsonValue.Create(42)));
        Assert.False(CalloutKinds.IsCalloutKind(new JsonObject()));
        Assert.False(CalloutKinds.IsCalloutKind(new JsonArray(JsonValue.Create("note"))));
    }

    [Fact]
    public void EveryKindHasAGlyphEntryAndSectionHasNone()
    {
        Assert.Equal(["note", "caution", "warning", "section"], CalloutKinds.All);
        Assert.NotEmpty(CalloutGlyphs.Note);
        Assert.NotEmpty(CalloutGlyphs.Caution);
        Assert.NotEmpty(CalloutGlyphs.Warning);
        Assert.Equal("", CalloutGlyphs.Section);
        foreach (var kind in CalloutKinds.All) Assert.NotNull(CalloutGlyphs.For(kind));
        Assert.Null(CalloutGlyphs.For("futurekind"));
        Assert.Null(CalloutGlyphs.For(null));
    }

    /// <summary>
    /// The property, not the character: a mark picked on looks alone could bring back an
    /// emoji-presentation glyph, which keeps its colour in a grayscale print.
    /// </summary>
    [Fact]
    public void UsesTypographicMarksNeverEmoji()
    {
        foreach (var kind in CalloutKinds.All)
        {
            var glyph = CalloutGlyphs.For(kind)!;
            foreach (var rune in glyph.EnumerateRunes())
            {
                Assert.False(IsEmojiPresentation(rune.Value), $"{kind} glyph {Cp(rune.Value)} defaults to emoji presentation");
                Assert.NotEqual(0xFE0F, rune.Value);
            }
        }
    }

    /// <summary>The same marks as macOS, so a project reads the same in both apps' exports.</summary>
    [Fact]
    public void MatchesTheMacOsGlyphTableExactly()
    {
        Assert.Equal([0x2139], CodePoints(CalloutGlyphs.Note));
        Assert.Equal([0x26A0], CodePoints(CalloutGlyphs.Caution));
        Assert.Equal([0x2501], CodePoints(CalloutGlyphs.Warning));
        Assert.Empty(CodePoints(CalloutGlyphs.Section));
    }

    /// <summary>The old warning mark, U+26D4 NO ENTRY, is in the set: the check can fail.</summary>
    [Fact]
    public void TheEmbeddedSetCatchesTheOldWarningMark() => Assert.True(IsEmojiPresentation(0x26D4));

    private static int[] CodePoints(string s) => s.EnumerateRunes().Select(r => r.Value).ToArray();

    private static string Cp(int cp) => "U+" + cp.ToString("X4", CultureInfo.InvariantCulture);
}
