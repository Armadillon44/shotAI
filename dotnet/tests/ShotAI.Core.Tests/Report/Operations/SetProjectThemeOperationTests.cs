using System.Text.Json.Nodes;
using ShotAI.Core.Codec;
using ShotAI.Core.Model;
using ShotAI.Core.Report.Operations;
using ShotAI.Core.Store;
using Xunit;

namespace ShotAI.Core.Tests.Report.Operations;

/// <summary>
/// <see cref="SetProjectThemeOperation"/> (spec 05 7.5 P8, 03 INV-SHELL-17, 11 INV-IPC-14): the
/// value passes through untouched, null clears the key, a brand pins it, the default included,
/// and the compare is raw. The store applies the same operation (ProjectThemeKeyTests).
/// </summary>
public sealed class SetProjectThemeOperationTests
{
    private static ProjectManifest With(string? theme)
    {
        var json = new JsonObject
        {
            ["version"] = 1d,
            ["id"] = "p1",
            ["title"] = "Handbook",
            ["createdAt"] = "2026-07-01T00:00:00.000Z",
            ["updatedAt"] = "2026-07-01T00:00:00.000Z",
            ["steps"] = new JsonArray(),
        };
        if (theme is not null) json["theme"] = theme;
        return ManifestCodec.Decode(json, "Handbook");
    }

    [Fact]
    public void NullClearsTheKey()
    {
        var m = With("lfi");
        Assert.Equal(MutateResult.Changed, new SetProjectThemeOperation(null).Apply(m));
        Assert.Null(m.Theme);
        Assert.False(ManifestCodec.Encode(m).ContainsKey("theme"));
    }

    /// <summary>#77: the default brand is pinned as itself, never folded into "no pin".</summary>
    [Theory]
    [InlineData("shotAI")]
    [InlineData("lfi")]
    public void ABrandPinsIt(string brand)
    {
        var m = With(null);
        Assert.Equal(MutateResult.Changed, new SetProjectThemeOperation(brand).Apply(m));
        Assert.Equal(brand, m.Theme);
        Assert.Equal(brand, ManifestCodec.Encode(m)["theme"]!.GetValue<string>());
    }

    /// <summary>The raw compare: re-pinning the pin and clearing no pin change nothing, so nothing is written or re-dated.</summary>
    [Theory]
    [InlineData(null, null)]
    [InlineData("shotAI", "shotAI")]
    [InlineData("lfi", "lfi")]
    public void TheSameValueIsUnchanged(string? stored, string? chosen)
    {
        var m = With(stored);
        Assert.Equal(MutateResult.Unchanged, new SetProjectThemeOperation(chosen).Apply(m));
        Assert.Equal(stored, m.Theme);
    }

    /// <summary>
    /// The other value is a change, the default brand against no pin included (#77), and so is a
    /// stored pin that differs only by case or that this build does not know: the compare is exact.
    /// </summary>
    [Theory]
    [InlineData(null, "shotAI")]
    [InlineData("shotAI", null)]
    [InlineData("shotAI", "lfi")]
    [InlineData("lfi", "shotAI")]
    [InlineData("LFI", "lfi")]
    [InlineData("shotai", "shotAI")]
    [InlineData("future-brand", "shotAI")]
    public void AnotherValueIsAChange(string? stored, string? chosen)
    {
        var m = With(stored);
        Assert.Equal(MutateResult.Changed, new SetProjectThemeOperation(chosen).Apply(m));
        Assert.Equal(chosen, m.Theme);
    }

    /// <summary>#107, AC-IPC-14: App default on a pin this build does not know removes it, a real change.</summary>
    [Fact]
    public void ClearingAnUnrecognisedPinRemovesIt()
    {
        var m = With("future-brand");
        Assert.Equal("future-brand", m.Theme);
        Assert.Equal(MutateResult.Changed, new SetProjectThemeOperation(null).Apply(m));
        Assert.False(ManifestCodec.Encode(m).ContainsKey("theme"));
    }

    /// <summary>D-IPC-9: only null or a brand id; the id is matched exactly, before anything is applied.</summary>
    [Theory]
    [InlineData("solarpunk")]
    [InlineData("LFI")]
    [InlineData("shotai")]
    [InlineData("")]
    [InlineData("toString")]
    public void AnUnknownBrandIsRefused(string brand)
    {
        var e = Assert.Throws<ArgumentException>(() => new SetProjectThemeOperation(brand));
        Assert.Equal("brand", e.ParamName);
    }

    /// <summary>The value passes through untouched (INV-IPC-14): null stays null, the default stays its id.</summary>
    [Fact]
    public void TheBrandIsKeptAsGiven()
    {
        Assert.Null(new SetProjectThemeOperation(null).Brand);
        Assert.Equal("shotAI", new SetProjectThemeOperation("shotAI").Brand);
        Assert.Equal("lfi", new SetProjectThemeOperation("lfi").Brand);
    }

    /// <summary>05 7.4: no card changes (an empty set, not the structural null), and the write re-dates the project.</summary>
    [Fact]
    public void AffectsNoCardAndReDates()
    {
        var op = new SetProjectThemeOperation("lfi");
        Assert.NotNull(op.AffectedStepIds);
        Assert.Empty(op.AffectedStepIds);
        Assert.True(op.BumpsUpdatedAt);
        Assert.IsAssignableFrom<ReportOperation>(op);
    }

    /// <summary>S10: the one instance gives the same result on the clone and on the fresh read.</summary>
    [Fact]
    public void TheSameInstanceAppliesTheSameWay()
    {
        var op = new SetProjectThemeOperation("lfi");
        var clone = With("shotAI");
        var fresh = With("shotAI");
        Assert.Equal(op.Apply(clone), op.Apply(fresh));
        Assert.Equal(ManifestCodec.Serialize(clone), ManifestCodec.Serialize(fresh));
        Assert.Equal(MutateResult.Unchanged, op.Apply(clone));
    }

    [Fact]
    public void ArgumentsAreChecked() => Assert.Throws<ArgumentNullException>(() => new SetProjectThemeOperation(null).Apply(null!));
}
