using ShotAI.Core.Shell;
using ShotAI.Core.Tests.Support;
using Xunit;

namespace ShotAI.Core.Tests.Shell;

/// <summary>Spec 03 8.3: the About dialog's text (2.8.4, D11, AC-SHELL-24).</summary>
public sealed class AboutTextTests
{
    [Fact]
    public void MessageIsTheNameAndVersion() => Assert.Equal("shotAI 2.0.0", AboutText.Message("2.0.0"));

    [Fact]
    public void DetailIsTheTaglineThenTheRuntimeLines() =>
        Assert.Equal(
            "Local-first SOP builder \u2014 capture a process and let Claude write the guide.\n\n.NET 10.0.12 \u00b7 WebView2 140.0.3485.54\nwin32/arm64",
            AboutText.Detail("10.0.12", "140.0.3485.54", "arm64"));

    /// <summary>R-ARCH-12: the probe leaves the version null when no runtime is installed.</summary>
    [Fact]
    public void NullWebView2VersionShowsNotInstalled() =>
        Assert.EndsWith(".NET 10.0.12 \u00b7 WebView2 not installed\nwin32/x64", AboutText.Detail("10.0.12", null, "x64"), StringComparison.Ordinal);

    /// <summary>The first line is Electron's, its em dash included; only the runtime line after it is native (D11).</summary>
    [Fact]
    public void TheTaglineIsElectrons()
    {
        var menu = ElectronSource.Read("src/main/menu.ts").ReplaceLineEndings("\n");
        Assert.Contains("        '" + ShellStrings.AboutTagline + "\\n\\n' +\n", menu, StringComparison.Ordinal);
        Assert.Contains("      message: `${app.getName()} ${app.getVersion()}`,\n", menu, StringComparison.Ordinal);
    }

    [Fact]
    public void NullsAreRefused()
    {
        Assert.Throws<ArgumentNullException>(() => AboutText.Message(null!));
        Assert.Throws<ArgumentNullException>(() => AboutText.Detail(null!, "1", "x64"));
        Assert.Throws<ArgumentNullException>(() => AboutText.Detail("10.0.12", "1", null!));
    }
}
