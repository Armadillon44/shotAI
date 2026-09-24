using ShotAI.Core.Capture;
using Xunit;

namespace ShotAI.Core.Tests.Capture;

/// <summary>
/// Spec 02 2.2.2 step 6, D11, D22 and 8.4: shot file names, and the orphan rule that seeds the
/// counter past every shot already in <c>shots/</c>, so a capture never overwrites a file
/// (INV-CAP-8).
/// </summary>
public sealed class ShotNamingTests
{
    [Theory]
    [InlineData(0L, "step-0000.png")]
    [InlineData(1L, "step-0001.png")]
    [InlineData(42L, "step-0042.png")]
    [InlineData(9999L, "step-9999.png")]
    [InlineData(10000L, "step-10000.png")]
    [InlineData(12345L, "step-12345.png")]
    [InlineData(1000001L, "step-1000001.png")]
    public void NamesArePaddedToFourDigits(long order, string name) => Assert.Equal(name, ShotNaming.Format(order));

    [Theory]
    [InlineData("step-0007.png", 7L)]
    [InlineData("STEP-0007.PNG", 7L)]
    [InlineData("Step-0007.Png", 7L)]
    [InlineData("step-7.png", 7L)]
    [InlineData("step-0000.png", 0L)]
    [InlineData("step-12345.png", 12345L)]
    [InlineData("step-00000000000000000000001.png", 1L)]
    [InlineData("step-1000000.png", 1000000L)]
    public void AShotNameClaimsItsNumber(string name, long number) => Assert.Equal(number, ShotNaming.OrphanNumber(name));

    [Theory]
    [InlineData("step-.png")]
    [InlineData("step-0007.jpg")]
    [InlineData("step-0007.png.tmp")]
    [InlineData("step-0007.png ")]
    [InlineData(" step-0007.png")]
    [InlineData("xstep-0007.png")]
    [InlineData("step-+7.png")]
    [InlineData("step--7.png")]
    [InlineData("step- 7.png")]
    [InlineData("step-7a.png")]
    [InlineData("step-0x10.png")]
    [InlineData("step0007.png")]
    [InlineData("notes.txt")]
    [InlineData("")]
    public void OtherNamesClaimNothing(string name) => Assert.Null(ShotNaming.OrphanNumber(name));

    /// <summary>ASCII digits only, as JavaScript's <c>\d</c> without <c>u</c>: an Arabic-Indic one is not a digit.</summary>
    [Fact]
    public void OnlyAsciiDigitsCount()
    {
        Assert.Null(ShotNaming.OrphanNumber("step-\u0661.png"));
        Assert.Null(ShotNaming.OrphanNumber("step-\uFF17.png"));
    }

    /// <summary>The letters fold in ASCII only, as JavaScript's <c>/i</c> without <c>u</c>: the long s is not an s.</summary>
    [Fact]
    public void OnlyAsciiLettersFold() => Assert.Null(ShotNaming.OrphanNumber("\u017Ftep-0007.png"));

    [Fact]
    public void AHugeNumberClampsToOneMillion() =>
        Assert.Equal(1000000L, ShotNaming.OrphanNumber("step-9223372036854775807.png"));

    /// <summary>A number too large for a long is ignored rather than clamped (D22).</summary>
    [Theory]
    [InlineData("step-9223372036854775808.png")]
    [InlineData("step-99999999999999999999.png")]
    public void ANumberPastLongIsIgnored(string name) => Assert.Null(ShotNaming.OrphanNumber(name));

    [Fact]
    public void TheSeedIsTheLargerOfTheCountAndTheOrphans()
    {
        Assert.Equal(7L, ShotNaming.Seed(3, ["step-0001.png", "step-0007.png", "notes.txt", "step-0003.png"]));
        Assert.Equal(10L, ShotNaming.Seed(10, ["step-0007.png"]));
        Assert.Equal(0L, ShotNaming.Seed(0, []));
        Assert.Equal(5L, ShotNaming.Seed(5, ["step-99999999999999999999.png"]));
    }

    /// <summary>After step 3 of 5 is deleted, the next shot is <c>step-0006.png</c>, not a second <c>step-0005.png</c>.</summary>
    [Fact]
    public void ADeletedStepsNumberIsNotReused() =>
        Assert.Equal("step-0006.png", ShotNaming.Format(ShotNaming.Seed(4, ["step-0001.png", "step-0002.png", "step-0004.png", "step-0005.png"]) + 1));

    /// <summary>macOS <c>ReviewFixTests.swift:59-70</c>: the clamp keeps the counter usable.</summary>
    [Fact]
    public void AfterTheLargestOrphanTheFirstCaptureIsStep1000001() =>
        Assert.Equal("step-1000001.png", ShotNaming.Format(ShotNaming.Seed(0, ["step-9223372036854775807.png"]) + 1));

    [Fact]
    public void ArgumentsAreChecked()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => ShotNaming.Format(-1));
        Assert.Throws<ArgumentNullException>(() => ShotNaming.OrphanNumber(null!));
        Assert.Throws<ArgumentOutOfRangeException>(() => ShotNaming.Seed(-1, []));
        Assert.Throws<ArgumentNullException>(() => ShotNaming.Seed(0, null!));
    }
}
