using ShotAI.Platform.Export;
using Xunit;

namespace ShotAI.Platform.Tests.Export;

/// <summary>
/// Spec 11 7.3.5 (R-ARCH-12): the probe reads the installed runtime's version through the
/// package's loader. Windows 11 and the runners have the Evergreen runtime installed.
/// </summary>
public sealed class WebView2RuntimeInfoTests
{
    /// <summary>A version like <c>140.0.3485.54</c>: four numeric parts, the first at least 86, the first Evergreen release.</summary>
    [Fact]
    public void ReturnsTheInstalledVersion()
    {
        var version = new WebView2RuntimeInfo().GetVersion();
        Assert.NotNull(version);
        Assert.Matches(@"^\d+\.\d+\.\d+\.\d+$", version);
        Assert.True(int.Parse(version.Split('.')[0], System.Globalization.CultureInfo.InvariantCulture) >= 86, version);
    }

    /// <summary>The probe is cheap and stable: a second read gives the same answer.</summary>
    [Fact]
    public void RepeatedReadsAgree()
    {
        var probe = new WebView2RuntimeInfo();
        Assert.Equal(probe.GetVersion(), probe.GetVersion());
    }
}
