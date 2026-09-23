using ShotAI.Core.Model;
using Xunit;

namespace ShotAI.Core.Tests.Store;

/// <summary>
/// The setters built on <c>mutate</c> (spec 01 2.9.3): the display scale, clamped with 1 stored
/// as absent and a no-op left unwritten (D-11), and the intro, the only path that marks it as
/// the author's (#64). The theme setter is <see cref="ProjectThemeKeyTests"/>.
/// </summary>
public sealed class ProjectSettersTests : IAsyncLifetime
{
    private readonly StoreHarness _h = new();
    private string _project = "";

    public ValueTask InitializeAsync()
    {
        _project = _h.Project("proj1");
        return ValueTask.CompletedTask;
    }

    public ValueTask DisposeAsync() => _h.DisposeAsync();

    private string? Raw(string key) => StoreHarness.OnDisk(_project)[key]?.ToJsonString();

    [Theory]
    [InlineData(0.9, "0.9")]
    [InlineData(0.87, "0.85")]
    [InlineData(9.0, "1.25")]
    [InlineData(0.1, "0.65")]
    [InlineData(0.825, "0.85")]
    public async Task StoresTheClampedDetent(double scale, string stored)
    {
        var manifest = await _h.Store.SetProjectDisplayScaleAsync(_project, scale);
        Assert.Equal(stored, Raw("displayScale"));
        Assert.Equal(double.Parse(stored, System.Globalization.CultureInfo.InvariantCulture), manifest.DisplayScale);
    }

    [Theory]
    [InlineData(1.0)]
    [InlineData(null)]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    public async Task OneIsStoredAsAbsent(double? scale)
    {
        await _h.Store.SetProjectDisplayScaleAsync(_project, 0.9);
        await _h.Store.SetProjectDisplayScaleAsync(_project, scale);
        Assert.False(StoreHarness.OnDisk(_project).ContainsKey("displayScale"));
    }

    /// <summary>D-11: already 1 (absent), so nothing is written.</summary>
    [Theory]
    [InlineData(1.0)]
    [InlineData(null)]
    [InlineData(1.01)]
    public async Task ADisplayScaleAlreadyInPlaceWritesNothing(double? scale)
    {
        var bytes = StoreHarness.Bytes(_project);
        await _h.Store.SetProjectDisplayScaleAsync(_project, scale);
        Assert.Equal(bytes, StoreHarness.Bytes(_project));
    }

    [Fact]
    public async Task SetsTheIntroAndMarksItAsTheAuthors()
    {
        var manifest = await _h.Store.SetProjectIntroAsync(_project, new SopIntro("Overview", "Why this exists"));

        Assert.Equal("""{"heading":"Overview","body":"Why this exists"}""", Raw("intro"));
        Assert.Equal("true", Raw("introEditedByUser"));
        Assert.True(manifest.IntroEditedByUser);
    }

    [Fact]
    public async Task AHeadingAloneIsAnIntro()
    {
        await _h.Store.SetProjectIntroAsync(_project, new SopIntro("Overview", ""));
        Assert.Equal("""{"heading":"Overview","body":""}""", Raw("intro"));
        Assert.Equal("true", Raw("introEditedByUser"));
    }

    [Fact]
    public async Task ClearingTheIntroDropsTheMark()
    {
        await _h.Store.SetProjectIntroAsync(_project, new SopIntro("H", "B"));
        await _h.Store.SetProjectIntroAsync(_project, null);

        var onDisk = StoreHarness.OnDisk(_project);
        Assert.True(onDisk.ContainsKey("intro"));
        Assert.Null(onDisk["intro"]);
        Assert.False(onDisk.ContainsKey("introEditedByUser"));
    }

    /// <summary><c>coerceIntro</c>: both parts empty is no intro.</summary>
    [Fact]
    public async Task AnEmptyIntroClears()
    {
        await _h.Store.SetProjectIntroAsync(_project, new SopIntro("H", "B"));
        var manifest = await _h.Store.SetProjectIntroAsync(_project, new SopIntro("", ""));

        Assert.Null(manifest.Intro);
        Assert.False(StoreHarness.OnDisk(_project).ContainsKey("introEditedByUser"));
    }

    [Fact]
    public async Task ASetterRefusesAnUnknownProject()
    {
        var outside = _h.Temp.Combine("elsewhere");
        await Assert.ThrowsAsync<ShotAI.Core.Store.ProjectNotKnownException>(() => _h.Store.SetProjectIntroAsync(outside, null));
        await Assert.ThrowsAsync<ShotAI.Core.Store.ProjectNotKnownException>(() => _h.Store.SetProjectDisplayScaleAsync(outside, 0.9));
    }
}
