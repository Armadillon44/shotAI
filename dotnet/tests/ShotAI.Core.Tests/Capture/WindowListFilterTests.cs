using ShotAI.Core.Capture;
using Xunit;

namespace ShotAI.Core.Tests.Capture;

/// <summary>get-windows' window-list filter (spec 02 7.5, <c>main.cc:238-263</c>) and its FileDescription keys (2.10.1).</summary>
public sealed class WindowListFilterTests
{
    private static readonly WindowListFacts Kept = new(true, true, true, WindowListFilter.WsCaption, 0, false, false);

    [Fact]
    public void KeepsAnEnabledVisibleCaptionedWindow() => Assert.True(WindowListFilter.Keeps(Kept));

    /// <summary>A popup needs no caption; a caption needs both of its bits.</summary>
    [Theory]
    [InlineData(WindowListFilter.WsPopup, true)]
    [InlineData(WindowListFilter.WsPopup | 0x00800000u, true)]
    [InlineData(0x00800000u, false)] // WS_BORDER alone
    [InlineData(0x00400000u, false)] // WS_DLGFRAME alone
    [InlineData(0u, false)]
    public void ACaptionOrAPopupIsNeeded(uint style, bool kept) =>
        Assert.Equal(kept, WindowListFilter.Keeps(Kept with { Style = style }));

    [Fact]
    public void EachConditionDropsTheWindow()
    {
        Assert.False(WindowListFilter.Keeps(Kept with { IsWindow = false }));
        Assert.False(WindowListFilter.Keeps(Kept with { Enabled = false }));
        Assert.False(WindowListFilter.Keeps(Kept with { Visible = false }));
        Assert.False(WindowListFilter.Keeps(Kept with { ExStyle = WindowListFilter.WsExToolWindow }));
        Assert.False(WindowListFilter.Keeps(Kept with { ExStyle = WindowListFilter.WsExToolWindow | WindowListFilter.WsExAppWindow }));
        Assert.False(WindowListFilter.Keeps(Kept with { Style = WindowListFilter.WsCaption | WindowListFilter.WsChild }));
        Assert.False(WindowListFilter.Keeps(Kept with { Style = WindowListFilter.WsPopup | WindowListFilter.WsChild }));
        Assert.False(WindowListFilter.Keeps(Kept with { Cloaked = true }));
        Assert.False(WindowListFilter.Keeps(default));
    }

    /// <summary>An owned window is listed only as an app window.</summary>
    [Fact]
    public void AnOwnedWindowNeedsAppWindow()
    {
        Assert.False(WindowListFilter.Keeps(Kept with { Owned = true }));
        Assert.True(WindowListFilter.Keeps(Kept with { Owned = true, ExStyle = WindowListFilter.WsExAppWindow }));
        Assert.True(WindowListFilter.Keeps(Kept with { ExStyle = WindowListFilter.WsExAppWindow }));
    }

    [Fact]
    public void TheStyleBitsAreWin32s()
    {
        Assert.Equal(0x00C00000u, WindowListFilter.WsCaption);
        Assert.Equal(0x80000000u, WindowListFilter.WsPopup);
        Assert.Equal(0x40000000u, WindowListFilter.WsChild);
        Assert.Equal(0x00000080u, WindowListFilter.WsExToolWindow);
        Assert.Equal(0x00040000u, WindowListFilter.WsExAppWindow);
    }

    /// <summary>The key is lowercase hex, four digits each, language first (<c>%04x%04x</c>).</summary>
    [Fact]
    public void TheFileDescriptionKeyIsLowercaseHex()
    {
        Assert.Equal(@"\StringFileInfo\040904b0\FileDescription", VersionInfoKeys.FileDescription(0x0409, 0x04B0));
        Assert.Equal(@"\StringFileInfo\00070000\FileDescription", VersionInfoKeys.FileDescription(0x0007, 0x0000));
        Assert.Equal(@"\StringFileInfo\ffffffff\FileDescription", VersionInfoKeys.FileDescription(0xFFFF, 0xFFFF));
        Assert.Equal(@"\VarFileInfo\Translation", VersionInfoKeys.Translation);
    }

    /// <summary>get-windows' fallback pair is its little-endian struct literal: language 0x04E4, code page 0x0409.</summary>
    [Fact]
    public void TheFallbackKeyKeepsGetWindowsByteOrder() =>
        Assert.Equal(@"\StringFileInfo\04e40409\FileDescription", VersionInfoKeys.FileDescription(VersionInfoKeys.FallbackLanguage, VersionInfoKeys.FallbackCodePage));
}
