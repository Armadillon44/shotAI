using ShotAI.Core.Shell;
using ShotAI.Core.Tests.Support;
using Xunit;

namespace ShotAI.Core.Tests.Shell;

/// <summary>
/// Spec 03 8.3 (INV-SHELL-22): each string equals its 2.11 text. The strings arrive with the
/// windows that show them: the main window's title with WP-A13, the menu's and About's with
/// WP-A15; the em-dash window titles join with the pill and the overlay.
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
            ["View", "Actual Size", "Zoom In", "Zoom Out", "Toggle Full Screen"],
            [ShellStrings.ViewMenu, ShellStrings.ActualSize, ShellStrings.ZoomIn, ShellStrings.ZoomOut, ShellStrings.ToggleFullScreen]);
        Assert.Equal(["Window", "Minimize", "Close"], [ShellStrings.WindowMenu, ShellStrings.Minimize, ShellStrings.Close]);
        Assert.Equal(["Help", "About shotAI"], [ShellStrings.HelpMenu, ShellStrings.About]);
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
}
