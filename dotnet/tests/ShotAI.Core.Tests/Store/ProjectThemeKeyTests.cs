using ShotAI.Core.Brand;
using ShotAI.Core.Model;
using Xunit;

namespace ShotAI.Core.Tests.Store;

/// <summary>
/// Ports <c>src/main/project-theme-key.test.ts</c> (#77 phase 1b, #95, AC-MODEL-9): the
/// <c>theme</c> key is the cross-platform schema, so the write rule is checked on the bytes.
/// </summary>
public sealed class ProjectThemeKeyTests : IAsyncLifetime
{
    private StoreHarness _h = new();
    private string _project = "";

    public ValueTask InitializeAsync()
    {
        _project = _h.Project("proj1");
        return ValueTask.CompletedTask;
    }

    public ValueTask DisposeAsync() => _h.DisposeAsync();

    private async Task<StoreHarness> OnBrand(string brand)
    {
        await _h.DisposeAsync();
        _h = new StoreHarness(brand);
        _project = _h.Project("proj1");
        return _h;
    }

    // The default brand writes nothing.

    [Fact]
    public async Task CreatesAProjectWithNoThemeKeyOnTheDefaultBrand()
    {
        var summary = await _h.Store.CreateProjectAsync("Untouched");
        Assert.False(StoreHarness.OnDisk(summary.Path).ContainsKey("theme"), "a default-branded project must stay byte-identical");
    }

    [Fact]
    public async Task StampsTheKeyAtCreationOnAnotherBrand()
    {
        var h = await OnBrand("lfi");
        var summary = await h.Store.CreateProjectAsync("Corporate");
        Assert.Equal("lfi", StoreHarness.OnDisk(summary.Path)["theme"]!.GetValue<string>());
    }

    [Fact]
    public async Task ClearsTheKeyOnNull()
    {
        await _h.Store.SetProjectThemeAsync(_project, "lfi");
        Assert.Equal("lfi", StoreHarness.OnDisk(_project)["theme"]!.GetValue<string>());
        await _h.Store.SetProjectThemeAsync(_project, null);
        Assert.False(StoreHarness.OnDisk(_project).ContainsKey("theme"));
    }

    /// <summary>The correction: an absent key follows the app; the default can be pinned.</summary>
    [Fact]
    public async Task PinsTheDefaultBrandWhenChosenExplicitly()
    {
        await _h.Store.SetProjectThemeAsync(_project, BrandPalette.DefaultBrand);
        Assert.Equal("shotAI", StoreHarness.OnDisk(_project)["theme"]!.GetValue<string>());
    }

    [Fact]
    public async Task KeepsAPinnedDefaultThroughARead()
    {
        await _h.Store.SetProjectThemeAsync(_project, "shotAI");
        Assert.Equal("shotAI", (await _h.Store.OpenProjectAsync(_project)).Manifest.Theme);
    }

    [Fact]
    public async Task DistinguishesAPinnedDefaultFromAnAbsentKeyEndToEnd()
    {
        await _h.Store.SetProjectThemeAsync(_project, "shotAI");
        Assert.Equal("shotAI", (await _h.Store.OpenProjectAsync(_project)).Manifest.Theme);
        await _h.Store.SetProjectThemeAsync(_project, null);
        Assert.Null((await _h.Store.OpenProjectAsync(_project)).Manifest.Theme);
    }

    // A write that changes nothing is refused.

    [Fact]
    public async Task DoesNotReDateAProjectWhenTheBrandIsAlreadyThatBrand()
    {
        await _h.Store.SetProjectThemeAsync(_project, "lfi");
        var bytes = StoreHarness.Bytes(_project);
        _h.Time.Advance(TimeSpan.FromMinutes(5));

        await _h.Store.SetProjectThemeAsync(_project, "lfi");

        Assert.Equal(bytes, StoreHarness.Bytes(_project));
    }

    [Fact]
    public async Task DoesNotReDateAnUnbrandedProjectWhenAskedToClearIt()
    {
        var before = StoreHarness.Bytes(_project);
        await _h.Store.SetProjectThemeAsync(_project, null);
        Assert.Equal(before, StoreHarness.Bytes(_project));
    }

    /// <summary>The control for the test above: pinning the default is a real write.</summary>
    [Fact]
    public async Task DoesWriteWhenAnUnbrandedProjectIsPinnedToTheDefault()
    {
        var before = StoreHarness.OnDisk(_project)["updatedAt"]!.GetValue<string>();
        await _h.Store.SetProjectThemeAsync(_project, "shotAI");
        Assert.Equal("shotAI", StoreHarness.OnDisk(_project)["theme"]!.GetValue<string>());
        Assert.NotEqual(before, StoreHarness.OnDisk(_project)["updatedAt"]!.GetValue<string>());
    }

    [Fact]
    public async Task DoesReDateWhenTheBrandActuallyChanges()
    {
        var before = StoreHarness.OnDisk(_project)["updatedAt"]!.GetValue<string>();
        await _h.Store.SetProjectThemeAsync(_project, "lfi");
        Assert.NotEqual(before, StoreHarness.OnDisk(_project)["updatedAt"]!.GetValue<string>());
    }

    // Reading a key the other platform wrote.

    [Fact]
    public async Task RoundTripsABrandMacOsSet()
    {
        _h.Project("proj1", StoreHarness.BaseJson.TrimEnd('}') + ",\"theme\":\"lfi\"}");
        Assert.Equal("lfi", (await _h.Store.OpenProjectAsync(_project)).Manifest.Theme);
    }

    /// <summary>#95: an unknown brand is kept on disk, and resolves to no brand.</summary>
    [Fact]
    public async Task KeepsABrandItDoesNotKnowAndRendersItAsNone()
    {
        _h.Project("proj1", StoreHarness.BaseJson.TrimEnd('}') + ",\"theme\":\"some-future-brand\"}");

        var opened = await _h.Store.OpenProjectAsync(_project);
        Assert.Equal("some-future-brand", opened.Manifest.Theme);
        Assert.Null(BrandPalette.PinnedBrand(opened.Manifest.Theme));

        await _h.Store.SetProjectIntroAsync(_project, new SopIntro("H", "B"));
        Assert.Equal("some-future-brand", StoreHarness.OnDisk(_project)["theme"]!.GetValue<string>());
    }

    [Theory]
    [InlineData("42")]
    [InlineData("null")]
    [InlineData("{}")]
    [InlineData("[]")]
    public async Task IsNotConfusedByANonString(string bad)
    {
        _h.Project("proj1", StoreHarness.BaseJson.TrimEnd('}') + ",\"theme\":" + bad + "}");
        Assert.Null((await _h.Store.OpenProjectAsync(_project)).Manifest.Theme);
    }

    /// <summary>Native: an unknown brand is refused before anything is queued (Q-MODEL-15).</summary>
    [Fact]
    public void AnUnknownBrandIsAProgrammingError()
    {
        var before = StoreHarness.Bytes(_project);
        Assert.Throws<ArgumentException>(() => { _ = _h.Store.SetProjectThemeAsync(_project, "solarpunk"); });
        Assert.Equal(before, StoreHarness.Bytes(_project));
    }
}
