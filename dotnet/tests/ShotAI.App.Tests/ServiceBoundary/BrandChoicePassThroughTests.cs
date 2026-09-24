using ShotAI.App.Tests.Support;
using ShotAI.Core.Codec;
using ShotAI.Core.Report.Operations;
using Xunit;

namespace ShotAI.App.Tests.ServiceBoundary;

/// <summary>
/// Spec 11 8.2 (INV-IPC-14, D-IPC-9, V23; AC-IPC-13 and AC-IPC-14's automated halves): View,
/// Brand hands its value to the open project's session untouched. App default is null, which
/// clears the key; the default brand is its own id, which pins it, a different operation (#77);
/// with no project open nothing reaches a session; an id the catalog does not have is refused.
/// Ports <c>passes the choice through untouched, null included</c> of
/// <c>src/renderer/project/theme-wiring.test.ts</c>.
/// </summary>
public sealed class BrandChoicePassThroughTests
{
    private const string Project = @"C:\Projects\Handbook";

    [Fact]
    public Task AppDefaultSendsNull() => WithProjectAsync("lfi", async r =>
    {
        r.Test.Menu.ChooseBrandCommand.Execute(null);
        await TestShell.Settle();
        var op = Assert.IsType<SetProjectThemeOperation>(Assert.Single(r.Recorded.Applied));
        Assert.Null(op.Brand);
        Assert.Equal([Project], r.Test.Projects.Mutated);
        Assert.False(ManifestCodec.Encode(r.Test.Projects.Stored[Project]).ContainsKey("theme"));
    });

    /// <summary>With the app brand on the default, "shotAI" still pins it: the same look as App default, a different request.</summary>
    [Fact]
    public Task DefaultBrandSendsItsId() => WithProjectAsync(null, async r =>
    {
        Assert.Equal("shotAI", r.Test.Settings.Current.Brand);
        r.Test.Menu.ChooseBrandCommand.Execute("shotAI");
        await TestShell.Settle();
        var op = Assert.IsType<SetProjectThemeOperation>(Assert.Single(r.Recorded.Applied));
        Assert.Equal("shotAI", op.Brand);
        Assert.Equal("shotAI", ManifestCodec.Encode(r.Test.Projects.Stored[Project])["theme"]!.GetValue<string>());
    });

    [Fact]
    public Task NoProjectOpenDoesNothing() => Sta.RunAsync(async () =>
    {
        RecordingSessions? recorded = null;
        using var t = new TestShell(sessions: real => recorded = new RecordingSessions(real));
        var chosen = 0;
        t.Menu.ProjectThemeChosen += (_, _) => chosen++;
        t.Menu.ChooseBrandCommand.Execute(null);
        t.Menu.ChooseBrandCommand.Execute("lfi");
        await TestShell.Settle();
        Assert.Equal(0, chosen);
        Assert.Empty(recorded!.Applied);
        Assert.Empty(t.Projects.Mutated);
    });

    /// <summary>D-IPC-9: the menu only offers the catalog; anything else is refused before anything is applied or queued.</summary>
    [Fact]
    public Task UnknownIdRejectedByStore() => WithProjectAsync("lfi", async r =>
    {
        var e = Assert.Throws<ArgumentException>(() => r.Test.Menu.ChooseBrandCommand.Execute("neon"));
        Assert.Equal("brand", e.ParamName);
        Assert.Throws<ArgumentException>(() => r.Test.Menu.ChooseBrandCommand.Execute("LFI"));
        await TestShell.Settle();
        Assert.Empty(r.Recorded.Applied);
        Assert.Empty(r.Test.Projects.Mutated);
        Assert.Equal("lfi", r.Test.Project.RawProjectTheme);
    });

    /// <summary>AC-IPC-14: clicking the ticked row is applied, and the raw compare makes it no write at all.</summary>
    [Fact]
    public Task TheTickedRowWritesNothing() => WithProjectAsync("lfi", async r =>
    {
        Assert.True(r.Test.Menu.BrandItems[2].IsChecked);
        r.Test.Menu.ChooseBrandCommand.Execute("lfi");
        await TestShell.Settle();
        Assert.Equal("lfi", Assert.IsType<SetProjectThemeOperation>(Assert.Single(r.Recorded.Applied)).Brand);
        Assert.Empty(r.Test.Projects.Mutated);
    });

    /// <summary>AC-IPC-14, #107: a pin this build does not know ticks nothing, and App default removes it with a real write.</summary>
    [Fact]
    public Task AppDefaultClearsAnUnrecognisedPin() => WithProjectAsync("future-brand", async r =>
    {
        Assert.DoesNotContain(r.Test.Menu.BrandItems, i => i.IsChecked);
        r.Test.Menu.ChooseBrandCommand.Execute(null);
        await TestShell.Settle();
        Assert.Equal([Project], r.Test.Projects.Mutated);
        Assert.Null(r.Test.Projects.Stored[Project].Theme);
        Assert.True(r.Test.Menu.BrandItems[0].IsChecked);
    });

    private static Task WithProjectAsync(string? theme, Func<Rig, Task> body) => Sta.RunAsync(async () =>
    {
        RecordingSessions? recorded = null;
        using var t = new TestShell(sessions: real => recorded = new RecordingSessions(real));
        var manifest = Manifests.Of("Handbook", Manifests.Shot("s1"));
        manifest.Theme = theme;
        t.Projects.CanOpen(Project, manifest);
        await t.Shell.OpenProjectAsync(Project);
        Assert.Equal(theme, t.Shell.RawProjectTheme);
        await body(new Rig(t, recorded!));
    });

    private sealed record Rig(TestShell Test, RecordingSessions Recorded);
}
