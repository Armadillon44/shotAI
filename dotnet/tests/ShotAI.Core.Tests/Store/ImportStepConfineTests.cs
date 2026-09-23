using ShotAI.Core.Store;
using ShotAI.Core.Tests.Support;
using Xunit;

namespace ShotAI.Core.Tests.Store;

/// <summary>
/// The imported image lands only inside the project (IMPROVEMENT [SECURITY] D-22,
/// EDGE-MODEL-49, AC-MODEL-34), with Electron's file names for huge counters. Windows
/// junctions are Platform.Tests' <c>ImportStepJunctionTests</c>.
/// </summary>
public sealed class ImportStepConfineTests : IAsyncLifetime
{
    private readonly StoreHarness _h = new();
    private string _project = "";

    public ValueTask InitializeAsync()
    {
        _project = _h.Project("proj1");
        return ValueTask.CompletedTask;
    }

    public ValueTask DisposeAsync() => _h.DisposeAsync();

    /// <summary>Electron writes through the link; natively nothing is written and the manifest is untouched.</summary>
    [Fact]
    public async Task ALinkedShotsFolderRefusesTheImportAndWritesNothingOutside()
    {
        var outside = Directory.CreateDirectory(_h.Temp.Combine("outside")).FullName;
        Links.Directory(Path.Join(_project, "shots"), outside);
        var bytes = StoreHarness.Bytes(_project);

        var e = await Assert.ThrowsAsync<ImportRejectedException>(() => _h.Store.ImportStepAsync(_project, StoreHarness.Png, null));

        Assert.Equal("Refusing to write outside the project: shots/step-0001.png", e.Message);
        Assert.Empty(Directory.GetFileSystemEntries(outside));
        Assert.Equal(bytes, StoreHarness.Bytes(_project));
    }

    /// <summary><c>String(1e21 + 1)</c> is <c>1e+21</c>, so that is the name, as in Electron.</summary>
    [Fact]
    public async Task ACounterOf1e21NamesTheFileWithAnExponent()
    {
        StoreHarness.WriteFile(_project, "shots/step-" + new string('9', 21) + ".png");

        var manifest = await _h.Store.ImportStepAsync(_project, StoreHarness.Png, null);

        Assert.Equal("shots/step-1e+21.png", manifest.Steps[^1].Screenshot);
        Assert.True(File.Exists(Path.Join(_project, "shots", "step-1e+21.png")));
    }

    /// <summary>No file name is long enough to hold such a counter, so this one is checked on the numbering alone.</summary>
    [Fact]
    public void ADigitStringBeyondTheDoubleRangeCountsAsInfinity()
    {
        var next = ProjectStore.NextShotNumber(["step-" + new string('9', 400) + ".png"], 3);
        Assert.Equal(double.PositiveInfinity, next);
        Assert.Equal("step-Infinity.png", ProjectStore.ShotFileName(next, "png"));
    }

    [Theory]
    [InlineData(4d, "png", "step-0004.png")]
    [InlineData(12345d, "jpg", "step-12345.jpg")]
    [InlineData(1e21, "png", "step-1e+21.png")]
    public void TheFileNameIsPaddedToFourDigits(double number, string ext, string expected) =>
        Assert.Equal(expected, ProjectStore.ShotFileName(number, ext));

    [Fact]
    public void TheNumberStartsAfterTheStepCount()
    {
        Assert.Equal(1, ProjectStore.NextShotNumber([], 0));
        Assert.Equal(6, ProjectStore.NextShotNumber(["step-0002.png", "STEP-0005.JPG", "step-9x.png"], 3));
        Assert.Equal(4, ProjectStore.NextShotNumber(["step-0002.png"], 3));
    }

    /// <summary><c>[0-9]</c>, not .NET's <c>\d</c>: Arabic-Indic digits are no counter (D-26).</summary>
    [Fact]
    public void OnlyAsciiDigitsCount() =>
        Assert.Equal(1, ProjectStore.NextShotNumber(["step-" + new string((char)0x0669, 3) + ".png"], 0));
}
