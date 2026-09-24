using System.Text.Json.Nodes;
using ShotAI.Core.Json;
using Xunit;

namespace ShotAI.Core.Tests.Settings;

/// <summary>
/// Port of <c>src/main/settings.test.ts</c> (#92, spec 10 8.3): keys this build does not name
/// survive a write, and a known key holding a bad value is still repaired. The write is
/// <c>UpdateAsync(s =&gt; s with { Recents = [] })</c>, the native <c>setRecents([])</c>, which
/// goes through the same re-read and write as every change.
/// </summary>
public sealed class SettingsStoreTests : IAsyncLifetime
{
    private readonly SettingsHarness _h = new();

    public ValueTask InitializeAsync() => ValueTask.CompletedTask;

    public ValueTask DisposeAsync() => _h.DisposeAsync();

    private async Task AnyWriteAsync()
    {
        var service = _h.Load();
        await service.UpdateAsync(s => s with { Recents = [] }, TestContext.Current.CancellationToken);
    }

    /// <summary>The concrete case: a newer build's <c>brand</c> must survive an older build's write (INV-INFRA-14).</summary>
    [Fact]
    public async Task KeepsUnknownKey()
    {
        _h.Write("{\"projectsDir\":" + JsJson.Stringify(JsonValue.Create(_h.Temp.Root)) + ",\"brand\":\"lfi\",\"somethingFuture\":{\"a\":[1,2]}}");

        await AnyWriteAsync();

        var after = _h.OnDisk();
        Assert.Equal("lfi", after["brand"]!.GetValue<string>());
        Assert.Equal("""{"a":[1,2]}""", JsJson.Stringify(after["somethingFuture"], 0));
    }

    /// <summary>A wrong type for a known key is coerced, not carried (INV-INFRA-15).</summary>
    [Fact]
    public async Task RepairsKnownKey()
    {
        _h.Write("""{"projectsDir":42,"recents":"nope","unknownKey":"kept"}""");

        await AnyWriteAsync();

        var after = _h.OnDisk();
        Assert.Equal(_h.DefaultDir, after["projectsDir"]!.GetValue<string>());
        Assert.Empty(after["recents"]!.AsArray());
        Assert.Equal("kept", after["unknownKey"]!.GetValue<string>());
    }

    /// <summary>
    /// The shape guard: a JSON scalar would spread into indexed junk (<c>{...'ab'}</c> is
    /// <c>{0:'a',1:'b'}</c>), so only a plain object carries keys (EDGE-INFRA-10, EDGE-INFRA-11).
    /// </summary>
    [Theory]
    [InlineData("\"just a string\"")]
    [InlineData("42")]
    [InlineData("true")]
    [InlineData("[1,2,3]")]
    [InlineData("null")]
    public async Task NoJunkForNonObject(string body)
    {
        _h.Write(body);

        await AnyWriteAsync();

        var after = _h.OnDisk();
        Assert.Null(after["0"]);
        Assert.Equal(_h.DefaultDir, after["projectsDir"]!.GetValue<string>());
    }

    /// <summary>INV-INFRA-17: a file that is not JSON at all loads as the defaults, and loading never throws.</summary>
    [Fact]
    public void NotJsonYieldsDefaults()
    {
        _h.Write("not json {{{");

        Assert.Equal(_h.DefaultDir, _h.Load().Current.ProjectsDir);
    }

    /// <summary>INV-INFRA-17: <c>JSON.parse('null')</c> succeeds and then yields the defaults.</summary>
    [Fact]
    public void NullFileYieldsDefaults()
    {
        _h.Write("null");

        Assert.Equal(_h.DefaultDir, _h.Load().Current.ProjectsDir);
    }
}
