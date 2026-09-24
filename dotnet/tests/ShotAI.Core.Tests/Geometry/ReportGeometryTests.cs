using ShotAI.Core.Geometry;
using ShotAI.Core.Tests.Support;
using Xunit;

namespace ShotAI.Core.Tests.Geometry;

/// <summary>
/// <c>src/renderer/project/report-geometry.test.ts</c> (spec 05 8.1): the no-crop fit of
/// <c>746b012</c> (INV-REP-4 to INV-REP-8).
/// </summary>
public sealed class ReportGeometryTests
{
    /// <summary>
    /// Electron's real column: the 820 card less its border and padding. It is the width the
    /// Electron tests measure against, not the native layout's (<see cref="ReportLayoutTests"/>).
    /// </summary>
    private const double RealColumn = 786;

    private static readonly ImageSize[] NoCropShapes =
    [
        new(1920, 1200), new(3840, 2160), new(1366, 768),
        new(800, 1400), new(786, 500), new(120, 90),
        new(5000, 100), new(100, 5000), new(1, 1),
    ];

    public static TheoryData<double, double, double> NoCropGrid()
    {
        var data = new TheoryData<double, double, double>();
        foreach (var avail in new[] { RealColumn, 820, 500, 300, 120 })
        {
            foreach (var s in NoCropShapes) data.Add(s.Width, s.Height, avail);
        }
        return data;
    }

    public static TheoryData<double, double, double> WrapGrid()
    {
        var data = new TheoryData<double, double, double>();
        foreach (var avail in new[] { RealColumn, 820, 640, 300 })
        {
            foreach (var s in new ImageSize[] { new(1920, 1200), new(900, 300), new(4000, 1000) }) data.Add(s.Width, s.Height, avail);
        }
        return data;
    }

    // --- the no-crop invariant ---

    [Theory]
    [MemberData(nameof(NoCropGrid))]
    public void NeverGivesTheWrapAContentBoxNarrowerThanItsImage(double w, double h, double avail)
    {
        var slack = ReportGeometry.WrapContentSlack(ReportGeometry.Fit(new ImageSize(w, h), avail));
        Assert.True(slack.X >= 0, $"x slack for {w}x{h} @ {avail}");
        Assert.True(slack.Y >= 0, $"y slack for {w}x{h} @ {avail}");
    }

    /// <summary>The original failure was the wrap clamped by <c>max-width: 100%</c> while the image kept its width.</summary>
    [Theory]
    [MemberData(nameof(WrapGrid))]
    public void KeepsTheWholeWrapInsideTheMeasuredColumn(double w, double h, double avail) =>
        Assert.True(ReportGeometry.Fit(new ImageSize(w, h), avail).WrapW <= avail);

    // --- reportFit ---

    [Fact]
    public void FitsADesktopToTheRealColumnNotToTheCardConstant()
    {
        var fit = ReportGeometry.Fit(new ImageSize(1920, 1200), RealColumn);
        Assert.Equal(784, fit.BaseW);
        Assert.Equal(Math.Floor(1200 * (784.0 / 1920)), fit.BaseH);
        Assert.Equal(490, fit.BaseH);
        Assert.True(fit.BaseW < ReportGeometry.BaseWidth);
    }

    [Fact]
    public void IsBoundedByHeightWhenTheCaptureIsTall() =>
        Assert.True(ReportGeometry.Fit(new ImageSize(800, 1600), RealColumn).BaseH <= ReportGeometry.BaseHeight - ReportGeometry.WrapBorder * 2);

    [Fact]
    public void NeverUpscalesACaptureSmallerThanTheColumn()
    {
        var fit = ReportGeometry.Fit(new ImageSize(300, 200), RealColumn);
        Assert.Equal((300.0, 200.0), (fit.BaseW, fit.BaseH));
    }

    /// <summary>The caller multiplies the image, not the base, by the zoom.</summary>
    [Fact]
    public void HoldsTheBoxAtTheZoomOneFit()
    {
        var one = ReportGeometry.Fit(new ImageSize(1920, 1200), RealColumn, 1);
        var three = ReportGeometry.Fit(new ImageSize(1920, 1200), RealColumn, 3);
        Assert.Equal(one.WrapW, three.WrapW);
        Assert.Equal(one.WrapH, three.WrapH);
        Assert.Equal(one.BaseW, three.BaseW);
    }

    [Theory]
    [InlineData(1920, 1200)]
    [InlineData(1366, 768)]
    [InlineData(1777, 999)]
    public void ReturnsWholePixels(double w, double h)
    {
        var f = ReportGeometry.Fit(new ImageSize(w, h), RealColumn);
        Assert.True(double.IsInteger(f.BaseW));
        Assert.True(double.IsInteger(f.BaseH));
        Assert.True(double.IsInteger(f.WrapW));
        Assert.True(double.IsInteger(f.WrapH));
    }

    [Fact]
    public void FallsBackToTheConstantBeforeTheFirstMeasurement()
    {
        var pre = ReportGeometry.Fit(new ImageSize(1920, 1200), null);
        Assert.Equal(ReportGeometry.BaseWidth - ReportGeometry.WrapBorder * 2, pre.BaseW);
        var post = ReportGeometry.Fit(new ImageSize(1920, 1200), RealColumn);
        Assert.True(post.BaseW < pre.BaseW);
    }

    [Fact]
    public void IsInertForAMissingOrDegenerateImage()
    {
        foreach (var d in new ImageSize?[] { null, new(0, 0), new(-5, 10) })
            Assert.Equal(new ReportFit(0, 0, 0, 0), ReportGeometry.Fit(d, RealColumn));
    }

    // --- native additions ---

    /// <summary>A size no decoder reports is no image here, where Electron would give NaN.</summary>
    [Theory]
    [InlineData(double.NaN, 10)]
    [InlineData(10, double.NaN)]
    [InlineData(double.PositiveInfinity, 10)]
    [InlineData(10, double.PositiveInfinity)]
    [InlineData(10, 0)]
    [InlineData(10, -1)]
    public void ANonFiniteOrEmptyAxisIsNoImage(double w, double h) =>
        Assert.Equal(default, ReportGeometry.Fit(new ImageSize(w, h), RealColumn));

    /// <summary>
    /// Both caps scale with the project (EDGE-REP-4); with no measurement the width cap is
    /// <c>round(816 s)</c>. At 1.25 the base is 1017, not 1018: <c>1920 * (1018 / 1920)</c> is
    /// 1017.9999999999999 in doubles, as in JavaScript, and the fit floors it.
    /// </summary>
    [Theory]
    [InlineData(0.65, 528, 330)]
    [InlineData(1, 814, 508)]
    [InlineData(1.25, 1017, 636)]
    public void TheCapsScaleWithTheProject(double scale, double baseW, double baseH)
    {
        var fit = ReportGeometry.Fit(new ImageSize(1920, 1200), null, 1, scale);
        Assert.Equal((baseW, baseH), (fit.BaseW, fit.BaseH));
        var tall = ReportGeometry.Fit(new ImageSize(100, 5000), null, 1, scale);
        Assert.Equal(Math.Round(600 * scale, MidpointRounding.AwayFromZero) - 2, tall.BaseH);
    }

    /// <summary>The scale is clamped first: 99 fits as 1.25, NaN as 1.</summary>
    [Fact]
    public void TheScaleIsClamped()
    {
        Assert.Equal(ReportGeometry.Fit(new ImageSize(1920, 1200), null, 1, 1.25), ReportGeometry.Fit(new ImageSize(1920, 1200), null, 1, 99));
        Assert.Equal(ReportGeometry.Fit(new ImageSize(1920, 1200), null, 1, 1), ReportGeometry.Fit(new ImageSize(1920, 1200), null, 1, double.NaN));
    }

    /// <summary>A measured width above the cap is capped; one at or below 2 leaves a 1 DIP budget.</summary>
    [Fact]
    public void TheMeasuredWidthIsBoundedByTheCap()
    {
        Assert.Equal(814, ReportGeometry.Fit(new ImageSize(1920, 1200), 5000).BaseW);
        Assert.Equal(814, ReportGeometry.Fit(new ImageSize(1920, 1200), double.PositiveInfinity).BaseW);
        var tiny = ReportGeometry.Fit(new ImageSize(1920, 1200), 2);
        Assert.Equal((1.0, 0.0), (tiny.BaseW, tiny.BaseH));
        Assert.Equal((3.0, 2.0), (tiny.WrapW, tiny.WrapH));
        Assert.Equal(1, ReportGeometry.Fit(new ImageSize(1920, 1200), -40).BaseW);
    }

    /// <summary>A zoom below 1 (only an older build wrote one) shrinks the box by it, rounded half up.</summary>
    [Fact]
    public void AZoomBelowOneShrinksTheBox()
    {
        var fit = ReportGeometry.Fit(new ImageSize(301, 201), RealColumn, 0.5);
        Assert.Equal((301.0, 201.0), (fit.BaseW, fit.BaseH));
        Assert.Equal((153.0, 103.0), (fit.WrapW, fit.WrapH));
    }

    [Fact]
    public void TheSlackIsTheContentBoxLessTheImage() =>
        Assert.Equal((5.0, 7.0), ReportGeometry.WrapContentSlack(new ReportFit(100, 50, 107, 59)));

    /// <summary>The constants <c>report-geometry.ts</c> declares.</summary>
    [Fact]
    public void TheConstantsAreReportGeometryTs()
    {
        var source = ElectronSource.Read("src/renderer/project/report-geometry.ts").ReplaceLineEndings("\n");
        Assert.Contains("export const REPORT_BASE_W = REPORT_COL_BASE;\n", source, StringComparison.Ordinal);
        Assert.Contains($"export const REPORT_BASE_H = {ReportGeometry.BaseHeight};\n", source, StringComparison.Ordinal);
        Assert.Contains($"export const WRAP_BORDER = {ReportGeometry.WrapBorder};\n", source, StringComparison.Ordinal);
        Assert.Equal(DocScale.ReportColumnBase, ReportGeometry.BaseWidth);
    }
}
