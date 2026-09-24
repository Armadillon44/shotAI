using System.Globalization;
using ShotAI.Core.Capture;
using Xunit;

namespace ShotAI.Core.Tests.Capture;

/// <summary>Spec 02 2.8.1, 8.1 and 8.4: <c>captureModeFor</c>, ported from <c>src/main/capture-geometry.test.ts</c>.</summary>
public sealed class AutoClassifierTests
{
    [Fact]
    public void UnknownFocusIsFullscreen() => Assert.Equal(AutoMode.Fullscreen, AutoClassifier.Classify((ForegroundInfo?)null));

    [Fact]
    public void TheDesktopIsFullscreen() => Assert.Equal(AutoMode.Fullscreen, AutoClassifier.Classify("Windows Explorer", "Program Manager"));

    /// <summary>The taskbar and the tray: Explorer with a blank title, blank by JavaScript's <c>trim</c> (which removes a no-break space too).</summary>
    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("\u00A0\t")]
    [InlineData("\uFEFF")]
    [InlineData("\u2028\u3000")]
    public void ExplorerWithABlankTitleIsARegion(string title) => Assert.Equal(AutoMode.Region, AutoClassifier.Classify("Windows Explorer", title));

    /// <summary>Only Explorer's blank title is the taskbar; any other app with no title is framed as a window.</summary>
    [Theory]
    [InlineData("Notepad", "")]
    [InlineData("Code", "   ")]
    public void ABlankTitleOutsideExplorerIsAWindow(string app, string title) => Assert.Equal(AutoMode.Window, AutoClassifier.Classify(app, title));

    /// <summary>A next-line character is white space to .NET's <c>Trim</c> but not to JavaScript's, so this title is not blank.</summary>
    [Fact]
    public void ANextLineTitleIsNotBlank() => Assert.Equal(AutoMode.Window, AutoClassifier.Classify("Windows Explorer", "\u0085"));

    [Theory]
    [InlineData("SearchHost", "Search")]
    [InlineData("StartMenuExperienceHost", "Start")]
    [InlineData("SearchHost.exe", "Search")]
    [InlineData("Windows Shell Experience Host", "")]
    [InlineData("ShellExperienceHost.exe", "Notification Centre")]
    [InlineData("TextInputHost", "Windows Input Experience")]
    [InlineData("SearchApp", "Search")]
    [InlineData("Cortana", "Cortana")]
    [InlineData("STARTMENUEXPERIENCEHOST", "Start")]
    public void ShellHostsAreRegions(string app, string title) => Assert.Equal(AutoMode.Region, AutoClassifier.Classify(app, title));

    [Theory]
    [InlineData("Notepad", "Untitled")]
    [InlineData("Windows Explorer", "Documents")]
    public void ANormalAppIsAWindow(string app, string title) => Assert.Equal(AutoMode.Window, AutoClassifier.Classify(app, title));

    /// <summary>The Explorer rules compare exactly, case and all.</summary>
    [Theory]
    [InlineData("windows explorer", "Program Manager")]
    [InlineData("Windows Explorer", "program manager")]
    [InlineData("Windows Explorer ", "Program Manager")]
    public void TheExplorerRulesAreCaseSensitive(string app, string title) => Assert.Equal(AutoMode.Window, AutoClassifier.Classify(app, title));

    /// <summary>
    /// The shell host test folds ASCII case only, as a JavaScript <c>/i</c> without <c>u</c> does:
    /// neither the long s (U+017F), which <c>ToUpperInvariant</c> turns into <c>S</c>, nor the
    /// dotted capital I (U+0130) matches.
    /// </summary>
    [Theory]
    [InlineData("\u017FearchHost")]
    [InlineData("TEXT\u0130NPUTHOST")]
    public void OnlyAsciiCaseFolds(string app) => Assert.Equal(AutoMode.Window, AutoClassifier.Classify(app, "Search"));

    /// <summary>A Turkish culture lowers <c>I</c> to a dotless i; the classifier does not use the culture.</summary>
    [Fact]
    public void TheCultureDoesNotMatter()
    {
        var saved = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = new CultureInfo("tr-TR");
            Assert.Equal(AutoMode.Region, AutoClassifier.Classify("STARTMENUEXPERIENCEHOST", "Start"));
            Assert.Equal(AutoMode.Region, AutoClassifier.Classify("TEXTINPUTHOST", "Input"));
        }
        finally
        {
            CultureInfo.CurrentCulture = saved;
        }
    }

    [Fact]
    public void TheForegroundWindowIsClassifiedByItsAppAndTitle()
    {
        Assert.Equal(AutoMode.Region, AutoClassifier.Classify(Foreground("SearchHost", "Search")));
        Assert.Equal(AutoMode.Fullscreen, AutoClassifier.Classify(Foreground("Windows Explorer", "Program Manager")));
        Assert.Equal(AutoMode.Window, AutoClassifier.Classify(Foreground("Notepad", "Untitled")));
    }

    [Fact]
    public void ArgumentsAreChecked()
    {
        Assert.Throws<ArgumentNullException>(() => AutoClassifier.Classify(null!, "t"));
        Assert.Throws<ArgumentNullException>(() => AutoClassifier.Classify("a", null!));
    }

    private static ForegroundInfo Foreground(string app, string title) => new(1, 2, app, title, null, null, false);
}
