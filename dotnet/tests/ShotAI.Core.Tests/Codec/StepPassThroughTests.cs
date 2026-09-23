using ShotAI.Core.Codec;
using ShotAI.Core.Json;
using Xunit;

namespace ShotAI.Core.Tests.Codec;

/// <summary>
/// A step passes through the codec verbatim, every key, value and position, except that
/// <c>annotations</c> is forced to an array (spec 01 2.3, INV-MODEL-5, EDGE-MODEL-3).
/// </summary>
public sealed class StepPassThroughTests
{
    private static string StepsWritten(string stepsJson) =>
        JsJson.Stringify(
            ManifestCodec.Encode(ManifestCodec.Decode(JsJson.Parse("{\"steps\":" + stepsJson + "}"), "T"))["steps"]);

    /// <summary>Every key the schema knows, in an unusual order, plus macOS's <c>note</c> and a future field.</summary>
    [Fact]
    public void AStepWithEveryKnownKeyAndUnknownOnesRoundTripsByteIdentically()
    {
        const string step = """
            [
              {
                "note": "macOS writes a note on every step",
                "id": "3f8e2b6a-1c4d-4a7e-8b2f-5d9c0e1a3b7f",
                "order": 1,
                "kind": "text",
                "screenshot": "shots/step-0001.png",
                "trigger": "click",
                "click": {
                  "global": {
                    "x": 1650,
                    "y": 480
                  },
                  "image": {
                    "x": 1403,
                    "y": 408
                  },
                  "button": "left",
                  "radius": 20,
                  "imageScale": 0.85
                },
                "monitor": {
                  "id": 1,
                  "bounds": {
                    "x": 0,
                    "y": 0,
                    "width": 2560,
                    "height": 1440
                  },
                  "scaleFactor": 1.25
                },
                "window": {
                  "app": "example.exe",
                  "title": "Example",
                  "pid": 4242,
                  "bounds": null
                },
                "element": {
                  "available": false,
                  "name": null,
                  "controlType": null,
                  "bounds": null
                },
                "caption": "Click",
                "heading": "H",
                "body": "B",
                "callout": "futurekind",
                "captionEditedByUser": true,
                "aiInserted": true,
                "crop": {
                  "x": 1,
                  "y": 2,
                  "width": 3,
                  "height": 4
                },
                "markerColor": "#e11d48",
                "annotations": [
                  null,
                  {
                    "type": "future",
                    "id": "f1"
                  }
                ],
                "flattened": "export/.render/3f8e2b6a-1c4d-4a7e-8b2f-5d9c0e1a3b7f.png",
                "renderRev": 3,
                "markerBaked": "yes",
                "reportZoom": 1.5,
                "reportPanX": 0.5,
                "reportPanY": 0.4,
                "futureField": {
                  "nested": [
                    true,
                    false,
                    null
                  ]
                }
              }
            ]
            """;
        // The literal has the checkout's line endings (CRLF on a Windows runner); the writer's are LF.
        var expected = step.ReplaceLineEndings("\n");
        Assert.Equal(expected, StepsWritten(expected));
    }

    [Fact]
    public void AnAbsentAnnotationsKeyIsAppendedLast() =>
        Assert.Equal("""[{"id":"a","caption":"c","annotations":[]}]""", Compact("""[{"id":"a","caption":"c"}]"""));

    [Fact]
    public void AMalformedAnnotationsValueIsReplacedInPlace() =>
        Assert.Equal("""[{"id":"a","annotations":[],"caption":"c"}]""", Compact("""[{"id":"a","annotations":"nope","caption":"c"}]"""));

    /// <summary>macOS once trapped on this narrowing to an integer (shotAI_MacOS#107).</summary>
    [Fact]
    public void AHugeOrderIsKeptAsADouble() =>
        Assert.Equal("""[{"order":1e+21,"annotations":[]}]""", Compact("""[{"order":1e21}]"""));

    private static string Compact(string stepsJson) => JsJson.Stringify(JsJson.Parse(StepsWritten(stepsJson)), 0);
}
