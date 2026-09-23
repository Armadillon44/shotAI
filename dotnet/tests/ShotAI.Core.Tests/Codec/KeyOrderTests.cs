using System.Text;
using System.Text.Json.Nodes;
using ShotAI.Core.Codec;
using ShotAI.Core.Json;
using ShotAI.Core.Model;
using Xunit;

namespace ShotAI.Core.Tests.Codec;

/// <summary>
/// Root key order (spec 01 2.7): extras first, then the canonical order, on every write
/// (D-3, EDGE-MODEL-10, Q-MODEL-3).
/// </summary>
public sealed class KeyOrderTests
{
    private static readonly string[] Canonical =
    [
        "version", "id", "title", "createdWith", "createdAt", "updatedAt", "captureSettings", "steps",
        "displayScale", "theme", "intro", "introEditedByUser", "sopBackup", "archived", "archivedAt",
    ];

    private static string[] Keys(JsonObject o) => JsOrder.Keys(o).ToArray();

    private static ProjectManifest Full()
    {
        var m = new ProjectManifest
        {
            Id = "0f8c2e1a-5b7d-4c3e-9a1f-2b3c4d5e6f70",
            Title = "Full",
            CreatedAt = "2026-09-22T19:03:07.123Z",
            UpdatedAt = "2026-09-22T19:03:07.123Z",
            CaptureSettings = new JsonObject { ["mode"] = "auto" },
            DisplayScale = 0.8,
            Theme = "lfi",
            Intro = new SopIntro("Overview", "Body"),
            IntroEditedByUser = true,
            SopBackup = new SopBackup { Title = "Before", IntroEditedByUser = true, Model = "m", At = "a" },
            Archived = true,
            ArchivedAt = "2026-09-23T00:00:00.000Z",
        };
        m.Steps.Add(new ProjectStep(new JsonObject { ["id"] = "s1", ["annotations"] = new JsonArray() }));
        return m;
    }

    [Fact]
    public void AFullManifestIsWrittenInCanonicalOrder() => Assert.Equal(Canonical, Keys(ManifestCodec.Encode(Full())));

    /// <summary>
    /// A file whose keys are in any order is written back in canonical order: the order a
    /// mutation appended a key in is not kept (D-3).
    /// </summary>
    [Fact]
    public void ANamedKeyOrderFromTheFileIsNotKept()
    {
        var input = JsJson.Parse("""
            {"archivedAt":null,"archived":false,"sopBackup":null,"introEditedByUser":true,"intro":{"heading":"H","body":""},
             "theme":"lfi","displayScale":0.8,"steps":[],"captureSettings":null,"updatedAt":"","createdAt":"",
             "createdWith":"shotAI","title":"T","id":"i","version":1}
            """);
        Assert.Equal(Canonical, Keys(ManifestCodec.Encode(ManifestCodec.Decode(input, "T"))));
    }

    [Fact]
    public void ExtrasComeFirstInFileOrderAndIndexKeysLeadThem()
    {
        var input = JsJson.Parse("""{"zeta":1,"version":1,"10":"a","alpha":2,"title":"T","9":"b","beta":3}""");
        var keys = Keys(ManifestCodec.Encode(ManifestCodec.Decode(input, "T")));
        Assert.Equal(["9", "10", "zeta", "alpha", "beta", "version", "id", "title"], keys.Take(8).ToArray());
    }

    /// <summary>
    /// Electron's createProject writes <c>theme</c> after <c>archivedAt</c>; the native writer
    /// puts it at its canonical place on the first save, which Electron's own next save does
    /// too (D-3).
    /// </summary>
    [Fact]
    public void ThemeFollowsStepsOnANewProject()
    {
        var m = new ProjectManifest { Id = "new", Title = "Project 2026/09/22 14:03:07", Theme = "lfi" };
        var keys = Keys(ManifestCodec.Encode(m));
        Assert.Equal(Array.IndexOf(keys, "steps") + 1, Array.IndexOf(keys, "theme"));
        Assert.Equal("archivedAt", keys[^1]);
    }

    [Fact]
    public void TwoEncodesAreByteIdenticalAndAReadWriteIsStable()
    {
        var m = Full();
        var first = ManifestCodec.Serialize(m);
        Assert.Equal(first, ManifestCodec.Serialize(m));
        Assert.Equal(first, ManifestCodec.Serialize(ManifestCodec.Read(first, "unused")));
    }

    /// <summary>The spec 01 2.7 example, as Electron writes a new default-branded project.</summary>
    [Fact]
    public void ANewProjectIsWrittenAsTheSpecExample()
    {
        var m = new ProjectManifest
        {
            Id = "0f8c2e1a-5b7d-4c3e-9a1f-2b3c4d5e6f70",
            Title = "Project 2026/09/22 14:03:07",
            CreatedAt = "2026-09-22T19:03:07.123Z",
            UpdatedAt = "2026-09-22T19:03:07.123Z",
        };
        const string expected = """
            {
              "version": 1,
              "id": "0f8c2e1a-5b7d-4c3e-9a1f-2b3c4d5e6f70",
              "title": "Project 2026/09/22 14:03:07",
              "createdWith": "shotAI",
              "createdAt": "2026-09-22T19:03:07.123Z",
              "updatedAt": "2026-09-22T19:03:07.123Z",
              "captureSettings": null,
              "steps": [],
              "intro": null,
              "sopBackup": null,
              "archived": false,
              "archivedAt": null
            }
            """;
        var bytes = ManifestCodec.Serialize(m);
        // The literal has the checkout's line endings (CRLF on a Windows runner); the writer's are LF.
        Assert.Equal(expected.ReplaceLineEndings("\n"), Encoding.UTF8.GetString(bytes));
        Assert.NotEqual(0xEF, bytes[0]);           // no BOM
        Assert.Equal((byte)'}', bytes[^1]);         // no trailing newline
    }
}
