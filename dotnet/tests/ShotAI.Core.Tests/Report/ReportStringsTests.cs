using ShotAI.Core.Errors;
using ShotAI.Core.Report;
using ShotAI.Core.Tests.Support;
using Xunit;

namespace ShotAI.Core.Tests.Report;

/// <summary>
/// The report's strings (spec 05 2.22, 8.2), by code point: the expected values spell every
/// character outside ASCII as an escape, and each Electron string is also found in its source.
/// </summary>
public sealed class ReportStringsTests
{
    [Fact]
    public void TheDisplayStringsAreElectrons()
    {
        Assert.Equal("\u2190 Back", ReportStrings.Back);
        Assert.Equal("Loading\u2026", ReportStrings.Loading);
        Assert.Equal("Add a caption\u2026", ReportStrings.CaptionEmpty);
        Assert.Equal("Empty \u2014 click to add text.", ReportStrings.CalloutEmpty);
        Assert.Equal("Empty \u2014 click to add a section heading.", ReportStrings.SectionEmpty);
        Assert.Equal("No steps yet. Resume capturing, Import an image, or Add a text step.", ReportStrings.EmptyHint);
        Assert.Equal("note callout \u2014 not a numbered step", ReportStrings.CalloutBadgeTip("note"));
        Assert.Equal("Import failed: ", ReportStrings.ImportFailed);
        Assert.Equal("Export failed: ", ReportStrings.ExportFailed);
    }

    /// <summary><c>`${n} step${n === 1 ? '' : 's'}`</c>: every step counted, and only 1 is singular.</summary>
    [Theory]
    [InlineData(0, "0 steps")]
    [InlineData(1, "1 step")]
    [InlineData(2, "2 steps")]
    [InlineData(21, "21 steps")]
    [InlineData(1000, "1000 steps")]
    public void StepCountLabel(int count, string label) => Assert.Equal(label, ReportStrings.StepCount(count));

    [Fact]
    public void TheNativeStrings()
    {
        Assert.Equal("Image missing: shots/step-0001.png", ReportStrings.ImageMissing("shots/step-0001.png"));
        Assert.Equal("Step 3, Click Save", ReportStrings.StepName(3, "Click Save"));
        Assert.Equal("Step 3", ReportStrings.StepName(3, ""));
        Assert.Equal("Step 3", ReportStrings.StepName(3, null));
        Assert.Equal("Note callout", ReportStrings.CalloutName("note"));
        Assert.Equal("Caution callout", ReportStrings.CalloutName("caution"));
        Assert.Equal("Warning callout", ReportStrings.CalloutName("warning"));
        Assert.Equal("Section", ReportStrings.SectionName);
    }

    /// <summary>A section is named by its heading, and anything but a note, caution or warning is not a callout name.</summary>
    [Theory]
    [InlineData("section")]
    [InlineData("tip")]
    [InlineData("Note")]
    [InlineData("")]
    public void OnlyTheThreeBoxedKindsHaveACalloutName(string kind) =>
        Assert.Throws<ArgumentOutOfRangeException>(() => ReportStrings.CalloutName(kind));

    /// <summary>04's load failure, which the UI shows bare (<c>sop-prepare.ts:17</c> with the path for the URL).</summary>
    [Fact]
    public void TheScreenshotLoadMessage()
    {
        var e = new ScreenshotLoadException("shots/step-0003.png");
        Assert.Equal("Could not load a screenshot to flatten (shots/step-0003.png).", e.Message);
        Assert.Equal("shots/step-0003.png", e.RelativePath);
        Assert.Equal(e.Message, UserMessage.From(e));
        Assert.Throws<ArgumentNullException>(() => new ScreenshotLoadException(null!));
    }

    [Fact]
    public void TheStringsAreInTheElectronSource()
    {
        var detail = ElectronSource.Read("src/renderer/project/ProjectDetail.tsx");
        Assert.Contains(ReportStrings.Back, detail, StringComparison.Ordinal);
        Assert.Contains(ReportStrings.Loading, detail, StringComparison.Ordinal);
        Assert.Contains("{steps.length} step{steps.length === 1 ? '' : 's'}", detail, StringComparison.Ordinal);
        Assert.Contains(ReportStrings.ImportFailed + "{importErr}", detail, StringComparison.Ordinal);
        Assert.Contains(ReportStrings.ExportFailed + "{exportErr}", detail, StringComparison.Ordinal);

        var report = ElectronSource.Read("src/renderer/project/Report.tsx");
        Assert.Contains(ReportStrings.CaptionEmpty, report, StringComparison.Ordinal);
        Assert.Contains(ReportStrings.CalloutEmpty, report, StringComparison.Ordinal);
        Assert.Contains(ReportStrings.SectionEmpty, report, StringComparison.Ordinal);
        Assert.Contains(ReportStrings.EmptyHint, report, StringComparison.Ordinal);
        Assert.Contains(ReportStrings.CalloutBadgeTip("${s.callout}"), report, StringComparison.Ordinal);

        var prepare = ElectronSource.Read("src/renderer/project/sop-prepare.ts");
        Assert.Contains("Could not load a screenshot to flatten (", prepare, StringComparison.Ordinal);
    }
}
