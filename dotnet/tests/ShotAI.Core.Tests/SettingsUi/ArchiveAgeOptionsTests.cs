using ShotAI.Core.SettingsUi;
using ShotAI.Core.Tests.Support;
using Xunit;

namespace ShotAI.Core.Tests.SettingsUi;

/// <summary>Spec 06 8.4, 2.28 and EDGE-HOME-37: Electron's five ages, and an entry for a stored age that is none of them.</summary>
public sealed class ArchiveAgeOptionsTests
{
    [Fact]
    public void TheFiveStandardOptions() =>
        Assert.Equal(
            [new(0, "Never"), new(30, "After 1 month"), new(90, "After 3 months"), new(180, "After 6 months"), new(365, "After 1 year")],
            ArchiveAgeOptions.Standard);

    /// <summary>A standard age gets the standard list itself.</summary>
    [Theory]
    [InlineData(0)]
    [InlineData(30)]
    [InlineData(90)]
    [InlineData(180)]
    [InlineData(365)]
    public void AStandardAgeAddsNothing(int stored) => Assert.Same(ArchiveAgeOptions.Standard, ArchiveAgeOptions.For(stored));

    /// <summary>D-HOME-20: any other age is appended as <c>After N days</c>, so the list never shows a blank.</summary>
    [Theory]
    [InlineData(45, "After 45 days")]
    [InlineData(1, "After 1 days")]
    [InlineData(1825, "After 1825 days")]
    [InlineData(31, "After 31 days")]
    public void AnOffListAgeIsAppended(int stored, string label)
    {
        var options = ArchiveAgeOptions.For(stored);

        Assert.Equal([.. ArchiveAgeOptions.Standard, new ArchiveAgeOption(stored, label)], options);
        Assert.Equal(6, options.Count);
    }

    /// <summary>2.37, 7.13: an option's text is its label, which is what WPF names its list item after.</summary>
    [Fact]
    public void AnOptionReadsAsItsLabel()
    {
        Assert.Equal(["Never", "After 1 month", "After 3 months", "After 6 months", "After 1 year"], ArchiveAgeOptions.Standard.Select(o => o.ToString()));
        Assert.Equal("After 45 days", ArchiveAgeOptions.For(45)[^1].ToString());
    }

    /// <summary>The standard options as <c>Settings.tsx</c> writes them.</summary>
    [Fact]
    public void TheOptionsMatchTheElectronSource()
    {
        var source = ElectronSource.Read("src/renderer/project/Settings.tsx");
        foreach (var option in ArchiveAgeOptions.Standard)
            Assert.Contains($"<option value={{{option.Days}}}>{option.Label}</option>", source, StringComparison.Ordinal);
    }
}
