using System.Text.RegularExpressions;
using ShotAI.Core.Json;
using ShotAI.Core.Tests.SourceGuards;
using ShotAI.Core.Tests.Support;
using Xunit;

namespace ShotAI.Core.Tests.Json;

/// <summary>
/// The capture cases for <see cref="JsMath.Round"/> (spec 02 8.4, INV-CAP-18, AC-CAP-4), and
/// the scans that keep the capture code on it.
/// </summary>
public sealed partial class JsMathTests
{
    [Theory]
    [InlineData(0.5, 1.0)]
    [InlineData(1.5, 2.0)]
    [InlineData(2.5, 3.0)]
    [InlineData(-0.5, 0.0)]          // JavaScript gives -0, equal to 0 as a double
    [InlineData(-1.5, -1.0)]
    [InlineData(-2.5, -2.0)]
    [InlineData(1.4999, 1.0)]
    [InlineData(0.49999999999999994, 0.0)]
    [InlineData(4503599627370497.0, 4503599627370497.0)]
    public void RoundMatchesJavaScript(double x, double expected) => Assert.Equal(expected, JsMath.Round(x));

    [Fact]
    public void NoJsMathCopyInCapture()
    {
        var copies = typeof(JsMath).Assembly.GetTypes()
            .Where(t => t.Name == nameof(JsMath) && t.Namespace != "ShotAI.Core.Json")
            .Select(t => t.FullName)
            .ToList();
        Assert.Empty(copies);
    }

    /// <summary>
    /// No .NET rounding in <c>ShotAI.Core/Capture</c>, whose pixel math must round half up as
    /// JavaScript does (AC-CAP-4). The Core-wide RS0030 ban (ARCHITECTURE 14.9) is the primary
    /// guard; this is the capture folder's own. The folder does round, through
    /// <see cref="JsMath.Round"/>, so the scan is not vacuous.
    /// </summary>
    [Fact]
    public void NoMathRoundInCapture()
    {
        var folder = Path.Combine(RepoFiles.Root, "dotnet", "src", "ShotAI.Core", "Capture");
        var files = Directory.EnumerateFiles(folder, "*.cs", SearchOption.AllDirectories).Order(StringComparer.Ordinal).ToList();
        Assert.NotEmpty(files);
        var offenders = files
            .Where(f => DotNetRoundingProblem(File.ReadAllText(f)) is not null)
            .Select(f => Path.GetRelativePath(folder, f))
            .ToList();
        Assert.Empty(offenders);
        Assert.Contains(files, f => JsRound().IsMatch(CSharpText.StripComments(File.ReadAllText(f))));
    }

    [Theory]
    [InlineData("var x = Math.Round(v);")]
    [InlineData("var x = System.Math.Round(v, MidpointRounding.AwayFromZero);")]
    [InlineData("var x = MathF.Round(v);")]
    [InlineData("var x = double.Round(v);")]
    [InlineData("var x = Math . Round (v);")]
    [InlineData("using static System.Math;")]
    public void ADotNetRoundingIsReported(string code) => Assert.NotNull(DotNetRoundingProblem(code));

    [Theory]
    [InlineData("var x = JsMath.Round(v);")]
    [InlineData("var x = ShotAI.Core.Json.JsMath.Round(v);")]
    [InlineData("// Math.Round would round half to even")]
    [InlineData("/* Math.Round(v) */ var x = Math.Floor(v);")]
    public void JsMathAndCommentsAreNotReported(string code) => Assert.Null(DotNetRoundingProblem(code));

    // Null when the code, comments blanked, has no .NET rounding call and no static import of Math.
    internal static string? DotNetRoundingProblem(string source)
    {
        var code = CSharpText.StripComments(source);
        if (DotNetRound().Match(code) is { Success: true } m) return "calls " + m.Value;
        return StaticMath().IsMatch(code) ? "imports System.Math statically" : null;
    }

    [GeneratedRegex(@"\b(Math|MathF|double|Double|float|Single|decimal|Decimal|Half)\s*\.\s*Round\s*\(")]
    private static partial Regex DotNetRound();

    [GeneratedRegex(@"\busing\s+static\s+(global::)?System\s*\.\s*MathF?\s*;")]
    private static partial Regex StaticMath();

    [GeneratedRegex(@"\bJsMath\s*\.\s*Round\s*\(")]
    private static partial Regex JsRound();
}
