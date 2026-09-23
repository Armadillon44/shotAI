using ShotAI.Core.Json;
using ShotAI.Core.Model;
using ShotAI.Core.Report.Operations;
using Xunit;

namespace ShotAI.Core.Tests.Report.Operations;

/// <summary>
/// <see cref="TextStepFactory"/> (spec 05 7.4): Electron's text step, key for key and in its
/// order (<c>src/main/project-store.ts:946-962</c>), for the store and the report alike.
/// </summary>
public sealed class TextStepFactoryTests
{
    private const string Head =
        """{"id":"t1","order":0,"kind":"text","screenshot":"","trigger":"hotkey","click":null,"monitor":null,"window":null,"element":{"available":false,"name":null,"controlType":null,"bounds":null},"caption":"","heading":"","body":"",""";

    private const string Tail = "\"crop\":null,\"annotations\":[]}";

    [Fact]
    public void BuildsElectronsTextStepInItsKeyOrder() =>
        Assert.Equal(Head + Tail, JsJson.Stringify(TextStepFactory.Create("t1", null).Raw, 0));

    [Theory]
    [InlineData(CalloutKinds.Note)]
    [InlineData(CalloutKinds.Caution)]
    [InlineData(CalloutKinds.Warning)]
    [InlineData(CalloutKinds.Section)]
    public void WritesAKnownCalloutAfterTheBody(string callout) =>
        Assert.Equal(Head + "\"callout\":\"" + callout + "\"," + Tail, JsJson.Stringify(TextStepFactory.Create("t1", callout).Raw, 0));

    [Theory]
    [InlineData("danger")]
    [InlineData("NOTE")]
    [InlineData("")]
    public void RefusesAnUnknownCallout(string callout) =>
        Assert.Throws<ArgumentException>(() => TextStepFactory.Create("t1", callout));

    [Fact]
    public void RefusesAnEmptyId() => Assert.Throws<ArgumentException>(() => TextStepFactory.Create("", null));

    [Fact]
    public void EachStepIsItsOwnTree()
    {
        var a = TextStepFactory.Create("a", null);
        var b = TextStepFactory.Create("b", null);
        a.Annotations.Add(1);
        Assert.Empty(b.Annotations);
        Assert.True(a.IsText);
    }
}
