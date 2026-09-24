using System.Windows;
using System.Windows.Threading;
using Microsoft.Extensions.Logging;
using ShotAI.App.Chrome;
using ShotAI.App.Report;
using ShotAI.App.Tests.Support;
using ShotAI.App.Threading;
using ShotAI.Core.Errors;
using Xunit;

namespace ShotAI.App.Tests.Report;

/// <summary>
/// Spec 05 7.5 P8 and 03 7.4.5: View, Brand's edit in the project view. The choice shows at once
/// and repaints the project view (06 2.32, <c>ActiveBrandResolver</c>); a write the disk refuses
/// is rolled back and shown once as the rollback notice (Q-REP-1); a choice for a project that is
/// no longer open changes nothing.
/// </summary>
public sealed class ProjectThemeEditTests
{
    private const string Project = @"C:\Projects\Handbook";

    /// <summary>The demo of PLAN WP-A18: LFI repaints the project view and writes the pin; App default removes it and repaints back.</summary>
    [Fact]
    public Task TheChoiceRepaintsTheProjectView() => WithThemeAsync(null, async (t, theme) =>
    {
        Assert.Equal("shotAI", theme.CurrentBrand);
        t.Menu.ChooseBrandCommand.Execute("lfi");
        await TestShell.Settle();
        Assert.Equal("lfi", theme.CurrentBrand);
        Assert.Equal("lfi", t.Projects.Stored[Project].Theme);

        t.Menu.ChooseBrandCommand.Execute(null);
        await TestShell.Settle();
        Assert.Equal("shotAI", theme.CurrentBrand);
        Assert.Null(t.Projects.Stored[Project].Theme);
    });

    /// <summary>06 2.32: Settings over the project wears the app brand; the new pin shows when the project view does.</summary>
    [Fact]
    public Task SettingsOverTheProjectKeepsTheAppBrand() => WithThemeAsync(null, async (t, theme) =>
    {
        t.Shell.OpenSettings();
        t.Menu.ChooseBrandCommand.Execute("lfi");
        await TestShell.Settle();
        Assert.Equal("shotAI", theme.CurrentBrand);
        Assert.Equal("lfi", t.Projects.Stored[Project].Theme);
        t.Shell.CloseSettings();
        Assert.Equal("lfi", theme.CurrentBrand);
    });

    /// <summary>A pin this build does not know renders the app brand (#95), and App default then clears it with no repaint.</summary>
    [Fact]
    public Task AnUnrecognisedPinWearsTheAppBrand() => WithThemeAsync("future-brand", async (t, theme) =>
    {
        Assert.Equal("shotAI", theme.CurrentBrand);
        var changes = 0;
        theme.ThemeChanged += (_, _) => changes++;
        t.Menu.ChooseBrandCommand.Execute(null);
        await TestShell.Settle();
        Assert.Null(t.Projects.Stored[Project].Theme);
        Assert.Equal(0, changes);
    });

    /// <summary>
    /// S4: the disk refuses the write; the pin, the theme and the menu go back, and the rollback
    /// notice shows the error's own text once, logged at Warning with the operation.
    /// </summary>
    [Fact]
    public Task AFailedWriteRollsBackWithTheNotice() => WithThemeAsync(null, async (t, theme) =>
    {
        var gate = t.Projects.GateMutate();
        t.Projects.MutateFailure = new IOException("The disk is full.");
        t.Menu.ChooseBrandCommand.Execute("lfi");
        await TestShell.Settle();
        Assert.Equal(("lfi", "lfi"), (t.Project.RawProjectTheme, theme.CurrentBrand));
        Assert.True(t.Menu.BrandItems[2].IsChecked);

        gate.SetResult();
        Assert.True(await TestShell.UntilAsync(() => t.Project.Notices.In(ReportNoticeSlot.Save) is not null));
        Assert.Equal(((string?)null, "shotAI"), (t.Project.RawProjectTheme, theme.CurrentBrand));
        Assert.True(t.Menu.BrandItems[0].IsChecked);
        Assert.Null(t.Projects.Stored[Project].Theme);
        var notice = Assert.Single(t.Project.Notices.Notices);
        Assert.Equal("Your last change couldn't be saved and was undone. The disk is full.", notice.Text);
        Assert.Equal(NoticeKind.Error, notice.Kind);
        var line = Assert.Single(t.Logs.Entries, e => e.Message.StartsWith("report: a change could not be saved", StringComparison.Ordinal));
        Assert.Equal((LogLevel.Warning, "report: a change could not be saved and was undone: SetProjectThemeOperation"), (line.Level, line.Message));
        Assert.IsType<IOException>(line.Exception);
        Assert.Null(t.Notices.Error);
    });

    /// <summary>11 L7: an unexpected failure shows the generic sentence after the prefix, logged at Error.</summary>
    [Fact]
    public Task AnUnexpectedFailureShowsTheGenericSentence() => WithThemeAsync(null, async (t, _) =>
    {
        t.Projects.MutateFailure = new InvalidOperationException("a bug");
        t.Menu.ChooseBrandCommand.Execute("lfi");
        Assert.True(await TestShell.UntilAsync(() => t.Project.Notices.In(ReportNoticeSlot.Save) is not null));
        Assert.Equal("Your last change couldn't be saved and was undone. " + UserMessage.Generic, t.Project.Notices.In(ReportNoticeSlot.Save)!.Text);
        var line = Assert.Single(t.Logs.Entries, e => e.Message.StartsWith("report: a change could not be saved", StringComparison.Ordinal));
        Assert.Equal(LogLevel.Error, line.Level);
    });

    /// <summary>INV-SHELL-17: a choice made for a project that is no longer the one open reaches nothing.</summary>
    [Fact]
    public Task AChoiceForAnotherProjectIsIgnored() => WithThemeAsync(null, async (t, _) =>
    {
        t.Project.SetProjectTheme(@"C:\Projects\Onboarding", "lfi");
        t.Project.SetProjectTheme(Project.ToUpperInvariant(), "lfi");
        await TestShell.Settle();
        Assert.Empty(t.Projects.Mutated);
        Assert.Null(t.Project.RawProjectTheme);

        t.Project.BackCommand.Execute(null);
        t.Project.SetProjectTheme(Project, "lfi");
        await TestShell.Settle();
        Assert.Empty(t.Projects.Mutated);
        Assert.Throws<ArgumentNullException>(() => t.Project.SetProjectTheme(null!, "lfi"));
    });

    // The shell with Project open at theme, and a theme manager following its navigation state, as startup wires it.
    private static Task WithThemeAsync(string? theme, Func<TestShell, ThemeManager, Task> body) => Sta.RunAsync(async () =>
    {
        using var t = new TestShell();
        var manifest = Manifests.Of("Handbook", Manifests.Shot("s1"));
        manifest.Theme = theme;
        t.Projects.CanOpen(Project, manifest);
        using var manager = new ThemeManager(
            t.Settings, new FakeSystemAppearance(), t.Navigation, new WpfUiDispatcher(Dispatcher.CurrentDispatcher), new Logger<ThemeManager>(t.Logs));
        manager.ApplyInitial(new ResourceDictionary());
        manager.Start();
        await t.Shell.OpenProjectAsync(Project);
        await body(t, manager);
    });
}
