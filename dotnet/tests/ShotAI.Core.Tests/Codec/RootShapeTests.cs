using ShotAI.Core.Codec;
using ShotAI.Core.Json;
using ShotAI.Core.Store;
using Xunit;

namespace ShotAI.Core.Tests.Codec;

/// <summary>
/// A root that is not an object (spec 01 2.2, EDGE-MODEL-33, INV-MODEL-22): only JSON
/// <c>null</c> throws; anything else is a default manifest, so the folder still lists.
/// </summary>
public sealed class RootShapeTests
{
    [Fact]
    public void NullRootThrows()
    {
        Assert.Throws<ManifestCorruptException>(() => ManifestCodec.Decode(JsJson.Parse("null"), "folder"));
        Assert.Throws<ManifestCorruptException>(() => ManifestCodec.Decode(null, "folder"));
    }

    [Theory]
    [InlineData("[]")]
    [InlineData("""[{"title":"not a manifest","version":7}]""")]
    [InlineData("\"x\"")]
    [InlineData("5")]
    [InlineData("true")]
    [InlineData("false")]
    [InlineData("{}")]
    public void AnythingElseIsADefaultManifestWithTheFallbackTitle(string json)
    {
        var m = ManifestCodec.Decode(JsJson.Parse(json), "folder");
        Assert.Empty(m.Extras);
        Assert.Equal(
            """{"version":1,"id":"","title":"folder","createdWith":"shotAI","createdAt":"","updatedAt":"","captureSettings":null,"steps":[],"intro":null,"sopBackup":null,"archived":false,"archivedAt":null}""",
            JsJson.Stringify(ManifestCodec.Encode(m), 0));
    }
}
