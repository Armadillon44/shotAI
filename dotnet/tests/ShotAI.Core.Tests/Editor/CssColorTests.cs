using ShotAI.Core.Editor;
using Xunit;

namespace ShotAI.Core.Tests.Editor;

/// <summary>
/// The stored colors' one parser (spec 04 7.2, D-EDIT-9; 05 EDGE-REP-13): the four CSS hex
/// forms and nothing else.
/// </summary>
public sealed class CssColorTests
{
    [Theory]
    [InlineData("#e11d48", 0xE1, 0x1D, 0x48, 0xFF)]
    [InlineData("#E11D48", 0xE1, 0x1D, 0x48, 0xFF)]
    [InlineData("#f00", 0xFF, 0x00, 0x00, 0xFF)]
    [InlineData("#F0a", 0xFF, 0x00, 0xAA, 0xFF)]
    [InlineData("#f00c", 0xFF, 0x00, 0x00, 0xCC)]
    [InlineData("#12345678", 0x12, 0x34, 0x56, 0x78)]
    [InlineData("#abcdef00", 0xAB, 0xCD, 0xEF, 0x00)]
    [InlineData("#000", 0, 0, 0, 0xFF)]
    [InlineData("#fff0", 0xFF, 0xFF, 0xFF, 0x00)]
    [InlineData("#09aF", 0x00, 0x99, 0xAA, 0xFF)]
    public void ParsesTheFourHexForms(string text, int r, int g, int b, int a)
    {
        Assert.True(CssColor.TryParse(text, out var color));
        Assert.Equal(new Rgba((byte)r, (byte)g, (byte)b, (byte)a), color);
    }

    /// <summary>CSS whitespace around the value is allowed, as the CSS parser allows it; nothing else is.</summary>
    [Theory]
    [InlineData(" #f00")]
    [InlineData("#f00 ")]
    [InlineData("\t#f00\n")]
    [InlineData("\r\f#f00")]
    public void CssWhitespaceAroundIsTrimmed(string text)
    {
        Assert.True(CssColor.TryParse(text, out var color));
        Assert.Equal(new Rgba(0xFF, 0, 0, 0xFF), color);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("#")]
    [InlineData("#1")]
    [InlineData("#12")]
    [InlineData("#12345")]
    [InlineData("#1234567")]
    [InlineData("#123456789")]
    [InlineData("e11d48")]
    [InlineData("##e11d48")]
    [InlineData("#e11d4g")]
    [InlineData("#e1 1d48")]
    [InlineData("red")]
    [InlineData("rgb(225, 29, 72)")]
    [InlineData("\u00a0#f00")]
    [InlineData("#\uff10\uff10\uff10")]
    [InlineData("#f00\u0000")]
    public void RefusesEverythingElse(string? text)
    {
        Assert.False(CssColor.TryParse(text, out var color));
        Assert.Equal(default, color);
    }

    [Fact]
    public void WithAlphaKeepsTheColor() =>
        Assert.Equal(new Rgba(1, 2, 3, 0x2E), new Rgba(1, 2, 3, 4).WithAlpha(0x2E));
}
