using System.Globalization;
using System.Text.RegularExpressions;
using ShotAI.Core.Shell;
using ShotAI.Core.Tests.Support;
using Xunit;

namespace ShotAI.Core.Tests.Shell;

/// <summary>
/// Spec 03 8.3 (INV-SHELL-22): each string equals its 2.11 text. The strings arrive with the
/// windows that show them: the main window's title with WP-A13, the menu's and About's with
/// WP-A15, the Brand submenu's with WP-A18; the em-dash window titles join with the pill and the
/// overlay.
/// </summary>
public sealed class ShellStringsTests
{
    [Fact]
    public void AppNameIsTheMainWindowTitle() => Assert.Equal("shotAI", ShellStrings.AppName);

    /// <summary>The same text as the Electron window's <c>title</c> (<c>src/main/main.ts:270</c>).</summary>
    [Fact]
    public void AppNameMatchesTheElectronTitle() =>
        Assert.Contains("    title: '" + ShellStrings.AppName + "',\n", ElectronSource.Read("src/main/main.ts").ReplaceLineEndings("\n"), StringComparison.Ordinal);

    /// <summary>2.11's menu rows. The labels menu.ts writes itself are also compared with it below; the rest are Electron 42.5.0's Windows role labels.</summary>
    [Fact]
    public void MenuLabelsAreTheTableOf211()
    {
        Assert.Equal(
            ["File", "Import Project\u2026", "Settings", "Exit"],
            [ShellStrings.FileMenu, ShellStrings.ImportProject, ShellStrings.Settings, ShellStrings.Exit]);
        Assert.Equal(
            ["Edit", "Undo", "Redo", "Cut", "Copy", "Paste", "Delete", "Select All"],
            [ShellStrings.EditMenu, ShellStrings.Undo, ShellStrings.Redo, ShellStrings.Cut, ShellStrings.Copy, ShellStrings.Paste, ShellStrings.Delete, ShellStrings.SelectAll]);
        Assert.Equal(
            ["View", "Actual Size", "Zoom In", "Zoom Out", "Toggle Full Screen", "Brand"],
            [ShellStrings.ViewMenu, ShellStrings.ActualSize, ShellStrings.ZoomIn, ShellStrings.ZoomOut, ShellStrings.ToggleFullScreen, ShellStrings.Brand]);
        Assert.Equal(["Window", "Minimize", "Close"], [ShellStrings.WindowMenu, ShellStrings.Minimize, ShellStrings.Close]);
        Assert.Equal(["Help", "About shotAI"], [ShellStrings.HelpMenu, ShellStrings.About]);
    }

    /// <summary>2.11's Brand rows (added in WP-A18): App default names the brand label it resolves to.</summary>
    [Fact]
    public void BrandRowsAreTheTableOf211()
    {
        Assert.Equal("App default (shotAI)", ShellStrings.BrandAppDefault("shotAI"));
        Assert.Equal("App default (LFI)", ShellStrings.BrandAppDefault("LFI"));
    }

    /// <summary>The Brand submenu's labels as <c>menu.ts</c> writes them (<c>:186</c>, <c>:242</c>).</summary>
    [Fact]
    public void BrandLabelsMatchTheElectronSource()
    {
        var menu = ElectronSource.Read("src/main/menu.ts").ReplaceLineEndings("\n");
        Assert.Contains($"          label: '{ShellStrings.Brand}',\n", menu, StringComparison.Ordinal);
        Assert.Contains("        label: `" + ShellStrings.BrandAppDefault("${BRANDS[brandState.appBrand].label}") + "`,\n", menu, StringComparison.Ordinal);
    }

    /// <summary>The ellipsis is one character, U+2026, as menu.ts writes it, not three dots.</summary>
    [Fact]
    public void ImportProjectEndsWithOneEllipsisCharacter()
    {
        Assert.EndsWith("\u2026", ShellStrings.ImportProject, StringComparison.Ordinal);
        Assert.DoesNotContain("...", ShellStrings.ImportProject, StringComparison.Ordinal);
    }

    [Fact]
    public void MenuTsLabelsMatchTheElectronSource()
    {
        var menu = ElectronSource.Read("src/main/menu.ts").ReplaceLineEndings("\n");
        foreach (var label in new[] { ShellStrings.FileMenu, ShellStrings.ViewMenu })
            Assert.Contains($"      label: '{label}',\n", menu, StringComparison.Ordinal);
        foreach (var label in new[] { ShellStrings.ImportProject, ShellStrings.Settings })
            Assert.Contains($"          label: '{label}',\n", menu, StringComparison.Ordinal);
        Assert.Contains($"      submenu: [{{ label: '{ShellStrings.About}', click: showAbout }}],\n", menu, StringComparison.Ordinal);
    }

    [Fact]
    public void AboutStringsMatchTheElectronSource()
    {
        var menu = ElectronSource.Read("src/main/menu.ts").ReplaceLineEndings("\n");
        Assert.Contains($"      title: '{ShellStrings.AboutTitle}',\n", menu, StringComparison.Ordinal);
        Assert.Contains($"      buttons: ['{ShellStrings.Ok}'],\n", menu, StringComparison.Ordinal);
        Assert.Equal("Local-first SOP builder \u2014 capture a process and let Claude write the guide.", ShellStrings.AboutTagline);
    }

    /// <summary>The tagline carries U+2014 exactly once and no U+2013 (INV-SHELL-22).</summary>
    [Fact]
    public void TheTaglineHasOneEmDashAndNoEnDash()
    {
        Assert.Single(ShellStrings.AboutTagline, c => c == '\u2014');
        Assert.DoesNotContain('\u2013', ShellStrings.AboutTagline);
    }

    /// <summary>2.11's pill rows, and the Discard confirmation's (7.6.3).</summary>
    [Fact]
    public void PillStringsAreTheTableOf211()
    {
        Assert.Equal("shotAI \u2014 Capture", ShellStrings.PillTitle);
        Assert.Equal("shotAI", ShellStrings.PillIdleLabel);
        Assert.Equal("Capturing \u00B7 3", ShellStrings.PillActiveLabel(paused: false, 3));
        Assert.Equal("Paused \u00B7 3", ShellStrings.PillActiveLabel(paused: true, 3));
        Assert.Equal(
            ["\u275A\u275A Pause", "\u25B6 Resume", "\u25A0 Stop", "\u2715", "\u26A0", "Dismiss"],
            [ShellStrings.Pause, ShellStrings.Resume, ShellStrings.Stop, ShellStrings.Discard, ShellStrings.ErrorGlyph, ShellStrings.Dismiss]);
        Assert.Equal(
            ["Pause", "Resume", "Stop & finish", "Discard this capture", "Drag to move", "Dismiss this error", "Dismiss this capture error"],
            [ShellStrings.PauseTip, ShellStrings.ResumeTip, ShellStrings.StopTip, ShellStrings.DiscardTip, ShellStrings.DragTip, ShellStrings.DismissTip, ShellStrings.DismissName]);
        Assert.Equal("Click anything to capture a step \u00B7 Ctrl+Shift+S", ShellStrings.HintRecording);
        Assert.Equal("Paused \u2014 press Resume to keep capturing", ShellStrings.HintPaused);
        Assert.Equal("A capture failed \u2014 see the log for details.", ShellStrings.ErrorFallback);
        Assert.Equal("Discard this capture? This is a new project, so the entire project will be deleted.", ShellStrings.DiscardWholeProject);
        Assert.Equal("Discard this capture? Steps recorded in this session will be deleted.", ShellStrings.DiscardSessionSteps);
        Assert.Equal(["Discard", "Cancel"], [ShellStrings.DiscardConfirm, ShellStrings.Cancel]);
    }

    /// <summary>The count is written as JavaScript's template literal writes an integer: digits, no grouping.</summary>
    [Theory]
    [InlineData(0, "Capturing \u00B7 0")]
    [InlineData(1234, "Capturing \u00B7 1234")]
    public void TheCountIsPlainDigits(int count, string label)
    {
        var before = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = new CultureInfo("de-DE");
            Assert.Equal(label, ShellStrings.PillActiveLabel(false, count));
        }
        finally
        {
            CultureInfo.CurrentCulture = before;
        }
    }

    /// <summary>The pill's strings as the toolbar page and its window write them.</summary>
    [Fact]
    public void PillStringsMatchTheElectronSource()
    {
        var main = ElectronSource.Read("src/main/main.ts").ReplaceLineEndings("\n");
        Assert.Contains($"    title: '{ShellStrings.PillTitle}',\n", main, StringComparison.Ordinal);
        var pill = ElectronSource.Read("src/renderer/toolbar/App.tsx").ReplaceLineEndings("\n");
        Assert.Contains($"              ? '{ShellStrings.PillIdleLabel}'\n", pill, StringComparison.Ordinal);
        Assert.Contains("              : `${status === 'paused' ? 'Paused' : 'Capturing'} \u00B7 ${count}`}\n", pill, StringComparison.Ordinal);
        foreach (var text in new[] { ShellStrings.HintPaused, ShellStrings.HintRecording, ShellStrings.ErrorFallback, ShellStrings.DiscardWholeProject, ShellStrings.DiscardSessionSteps })
            Assert.Contains($"'{text}'", pill, StringComparison.Ordinal);
        foreach (var text in new[] { ShellStrings.Pause, ShellStrings.Resume, ShellStrings.Stop, ShellStrings.Discard, ShellStrings.ErrorGlyph, ShellStrings.Dismiss })
            Assert.Matches("\n +" + Regex.Escape(text) + "\n", pill);
        foreach (var tip in new[] { ShellStrings.PauseTip, ShellStrings.ResumeTip, ShellStrings.DiscardTip, ShellStrings.DragTip, ShellStrings.DismissTip })
            Assert.Contains($"title=\"{tip}\"", pill, StringComparison.Ordinal);
        // JSX decodes the entity the source writes.
        Assert.Contains($"title=\"{ShellStrings.StopTip.Replace("&", "&amp;", StringComparison.Ordinal)}\"", pill, StringComparison.Ordinal);
        foreach (var name in new[] { ShellStrings.DiscardTip, ShellStrings.DismissName })
            Assert.Contains($"aria-label=\"{name}\"", pill, StringComparison.Ordinal);
    }

    /// <summary>2.11's overlay rows.</summary>
    [Fact]
    public void OverlayStringsAreTheTableOf211()
    {
        Assert.Equal("shotAI \u2014 Select area", ShellStrings.OverlayTitle);
        Assert.Equal("Drag to select a capture area", ShellStrings.OverlayHint);
        Assert.Equal("Press Esc to cancel", ShellStrings.OverlayHintSub);
        Assert.Equal("1280 \u00D7 720px", ShellStrings.Badge(1280, 720));
    }

    /// <summary>The overlay's strings as its page and its title write them; JSX joins the badge's parts with one space each side of the sign.</summary>
    [Fact]
    public void OverlayStringsMatchTheElectronSource()
    {
        var page = ElectronSource.Read("overlay.html").ReplaceLineEndings("\n");
        Assert.Contains($"    <title>{ShellStrings.OverlayTitle}</title>\n", page, StringComparison.Ordinal);
        var overlay = ElectronSource.Read("src/renderer/overlay/App.tsx").ReplaceLineEndings("\n");
        Assert.Contains($"<div className=\"ov__hint\">\n          {ShellStrings.OverlayHint}\n", overlay, StringComparison.Ordinal);
        Assert.Contains($"<span className=\"ov__hint-sub\">{ShellStrings.OverlayHintSub}</span>", overlay, StringComparison.Ordinal);
        Assert.Contains(
            "{Math.round(rect.width * window.devicePixelRatio)} \u00D7{' '}\n              {Math.round(rect.height * window.devicePixelRatio)}px\n",
            overlay,
            StringComparison.Ordinal);
    }

    /// <summary>The overlay's title carries U+2014 exactly once and no U+2013 (INV-SHELL-22).</summary>
    [Fact]
    public void TheOverlayTitleHasOneEmDashAndNoEnDash()
    {
        Assert.Single(ShellStrings.OverlayTitle, c => c == '\u2014');
        Assert.DoesNotContain('\u2013', ShellStrings.OverlayTitle);
    }

    /// <summary>The pill's em-dash strings carry U+2014 exactly once and no U+2013 (INV-SHELL-22).</summary>
    [Fact]
    public void ThePillsEmDashStringsHaveOneEmDashAndNoEnDash()
    {
        foreach (var text in new[] { ShellStrings.PillTitle, ShellStrings.HintPaused, ShellStrings.ErrorFallback })
        {
            Assert.Single(text, c => c == '\u2014');
            Assert.DoesNotContain('\u2013', text);
        }
    }
}
