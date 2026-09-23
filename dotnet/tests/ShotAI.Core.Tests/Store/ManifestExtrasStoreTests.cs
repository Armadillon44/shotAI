using ShotAI.Core.Codec;
using ShotAI.Core.Json;
using ShotAI.Core.Model;
using Xunit;

namespace ShotAI.Core.Tests.Store;

/// <summary>
/// Ports the store cases of <c>src/main/manifest-extras.test.ts</c> (#95) through the real store
/// over a temp folder, because the bug was in the read and write round trip rather than in any
/// one function. The codec cases are <c>Codec/ManifestExtrasTests</c>.
/// </summary>
public sealed class ManifestExtrasStoreTests : IAsyncLifetime
{
    private readonly StoreHarness _h = new();
    private string _project = "";

    public ValueTask InitializeAsync()
    {
        _project = _h.Project("proj1");
        return ValueTask.CompletedTask;
    }

    public ValueTask DisposeAsync() => _h.DisposeAsync();

    private static string BaseWith(string extra) => StoreHarness.BaseJson.TrimEnd('}') + "," + extra + "}";

    private static string[] SortedKeys(string dir) =>
        StoreHarness.OnDisk(dir).Select(p => p.Key).Order(StringComparer.Ordinal).ToArray();

    [Fact]
    public async Task AnUnknownRootKeySurvivesAReadAndARealWrite()
    {
        _h.Project("proj1", BaseWith("\"somethingNew\":{\"nested\":[1,2]}"));

        var opened = await _h.Store.OpenProjectAsync(_project);
        Assert.Equal("""{"nested":[1,2]}""", JsJson.Stringify(opened.Manifest.Extras["somethingNew"], 0));

        // The read half alone is not the fix: the key has to survive being written back.
        await _h.Store.SetProjectIntroAsync(_project, new SopIntro("H", "B"));
        Assert.Equal("""{"nested":[1,2]}""", JsJson.Stringify(StoreHarness.OnDisk(_project)["somethingNew"], 0));
    }

    [Fact]
    public async Task KeepsTheValueVerbatimRatherThanCoercingIt()
    {
        const string odd = """{"s":"x","n":0,"b":false,"nul":null,"arr":[],"deep":{"a":{"b":1}}}""";
        _h.Project("proj1", BaseWith("\"futureThing\":" + odd));

        await _h.Store.SetProjectIntroAsync(_project, new SopIntro("H", "B"));

        Assert.Equal(odd, JsJson.Stringify(StoreHarness.OnDisk(_project)["futureThing"], 0));
    }

    /// <summary>The guarantee every existing project depends on: no key and no sidecar across two real saves.</summary>
    [Fact]
    public async Task AProjectWithNoExtrasGainsNoKeyAndNoSidecar()
    {
        await _h.Store.SetProjectIntroAsync(_project, new SopIntro("H", "B"));
        var before = SortedKeys(_project);

        await _h.Store.SetProjectIntroAsync(_project, new SopIntro("H2", "B2"));
        var after = SortedKeys(_project);

        Assert.Equal(before, after);
        Assert.DoesNotContain("extra", after);
        Assert.DoesNotContain("extras", after);
        foreach (var k in after) Assert.True(ManifestKeys.All.Contains(k), $"{k} is unexpected");
        Assert.Equal(["project.json"], Directory.GetFileSystemEntries(_project).Select(Path.GetFileName));
    }
}
