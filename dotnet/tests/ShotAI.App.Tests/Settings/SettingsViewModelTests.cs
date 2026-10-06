using System.Windows;
using System.Windows.Media;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using ShotAI.App.Chrome;
using ShotAI.App.Services;
using ShotAI.App.Settings;
using ShotAI.App.Shell;
using ShotAI.App.Tests.Support;
using ShotAI.App.Threading;
using ShotAI.Core.Capture;
using ShotAI.Core.Errors;
using ShotAI.Core.Settings;
using ShotAI.Core.SettingsUi;
using ShotAI.Core.Sop;
using ShotAI.Core.Theme;
using Xunit;

namespace ShotAI.App.Tests.Settings;

/// <summary>
/// Spec 06 8.4 (INV-HOME-28, INV-HOME-29, INV-HOME-41, D-HOME-31, EDGE-HOME-39): every control
/// shows what is stored, coerced or rolled back; a failed write shows the error notice; a blank
/// name turns the credit off; the blur-saved fields also save when Settings goes; an unchanged
/// field writes nothing. The settings are a fake that coerces and fails as the real service does.
/// </summary>
public sealed class SettingsViewModelTests
{
    private static readonly IOException DiskFull = new("The disk is full.");

    /// <summary>
    /// INV-HOME-28: the control shows the stored value, not the value written. A stored age that is
    /// none of the five (5000 in the file, coerced to 1825 at load) is listed as itself; a name past
    /// 120 code units is kept as its first 120; a slider value is stored as its decimal step.
    /// </summary>
    [Fact]
    public Task ShowsCoercedValue() => Sta.RunAsync(async () =>
    {
        using var rig = new SettingsRig(s => SettingsCoercer.Normalize(s with { ArchiveAgeDays = 5000 }, @"C:\Users\test\Documents\shotAI"));
        Assert.Equal(new ArchiveAgeOption(1825, "After 1825 days"), rig.Vm.Storage.ArchiveAge);
        Assert.Equal(6, rig.Vm.Storage.ArchiveAges.Count);

        rig.Vm.About.UserName = new string('n', 130);
        await rig.Vm.About.CommitUserNameAsync();
        await TestShell.Settle();
        Assert.Equal(new string('n', 120), rig.Current.UserName);
        Assert.Equal(new string('n', 120), rig.Vm.About.UserName);

        rig.Vm.Capture.Scale = 0.55 + 0.05;
        Assert.NotEqual(0.6, rig.Vm.Capture.Scale);
        await rig.Vm.Capture.CommitScaleAsync();
        await TestShell.Settle();
        Assert.Equal(0.6, rig.Current.CaptureScale);
        Assert.Equal(0.6, rig.Vm.Capture.Scale);
        Assert.Equal("60%", rig.Vm.Capture.ScaleLabel);
    });

    // Each control: how the view changes it, and what it shows and stores of its setting.
    private static readonly Dictionary<string, (Func<SettingsViewModel, Task> Change, Func<SettingsViewModel, object> Shown, Func<AppSettings, object> Stored)> Controls = new()
    {
        ["ai switch"] = (vm => Set(() => vm.Ai.Enabled = false), vm => vm.Ai.Enabled, s => s.Sop.Enabled),
        ["tone"] = (vm => Set(() => vm.Ai.Tones[2].IsSelected = true), vm => Selected(vm.Ai.Tones), s => SopCatalog.ToWire(s.Sop.Tone)),
        ["effort"] = (vm => Set(() => vm.Ai.Efforts[2].IsSelected = true), vm => Selected(vm.Ai.Efforts), s => SopCatalog.ToWire(s.Sop.Effort)),
        ["custom instructions"] = (vm => Typed(() => vm.Ai.CustomInstructions = "Note permissions", vm.Ai.CommitCustomInstructionsAsync),
            vm => vm.Ai.CustomInstructions, s => s.Sop.CustomInstructions),
        ["quality"] = (vm => Typed(() => vm.Capture.Scale = 0.7, vm.Capture.CommitScaleAsync), vm => vm.Capture.Scale, s => s.CaptureScale),
        ["remote visibility"] = (vm => Set(() => vm.Capture.RemoteVisible = true), vm => vm.Capture.RemoteVisible, s => s.RemoteVisible),
        ["theme"] = (vm => Set(() => vm.Appearance.Themes[2].IsSelected = true), vm => Selected(vm.Appearance.Themes), s => ThemePrefWire.ToWire(s.Theme)),
        ["brand"] = (vm => Set(() => vm.Appearance.Brands[1].IsSelected = true), vm => Selected(vm.Appearance.Brands), s => s.Brand),
        ["archive age"] = (vm => Set(() => vm.Storage.ArchiveAge = vm.Storage.ArchiveAges[4]), vm => vm.Storage.ArchiveAge!.Days, s => s.ArchiveAgeDays),
        ["name"] = (vm => Typed(() => vm.About.UserName = "Dana Reyes", vm.About.CommitUserNameAsync), vm => vm.About.UserName, s => s.UserName),
        ["include name"] = (vm => Set(() => vm.About.IncludeName = true), vm => vm.About.IncludeName, s => s.IncludeNameInReports),
        ["update check"] = (vm => Set(() => vm.About.UpdateCheckEnabled = false), vm => vm.About.UpdateCheckEnabled, s => s.UpdateCheckEnabled),
    };

    public static TheoryData<string> ControlNames() => [.. Controls.Keys];

    // Each control, with the rollback after the refresh the optimistic step posted, and before it.
    public static TheoryData<string, bool> FailureCases()
    {
        var cases = new TheoryData<string, bool>();
        foreach (var control in Controls.Keys)
        {
            cases.Add(control, false);
            cases.Add(control, true);
        }
        return cases;
    }

    /// <summary>
    /// INV-HOME-28, D-HOME-8: a write that fails is undone by the service; the control shows the
    /// stored value again and the failure is the error notice, with the OS's message. A field shows
    /// it too when the rollback came before any refresh could see the failed value.
    /// </summary>
    [Theory]
    [MemberData(nameof(FailureCases))]
    public Task RollsBackWithNoticeOnFailure(string control, bool rollsBackFirst) => Sta.RunAsync(async () =>
    {
        using var rig = new SettingsRig(s => s with { UserName = "Pat" });
        var (change, shown, stored) = Controls[control];
        var before = stored(rig.Current);
        Assert.Equal(before, shown(rig.Vm));
        rig.Settings.WriteFails = DiskFull;
        rig.Settings.RollsBackFirst = rollsBackFirst;

        await change(rig.Vm);
        await TestShell.Settle();

        Assert.Equal(1, rig.Settings.Writes);
        Assert.Equal(before, stored(rig.Current));
        Assert.Equal(before, shown(rig.Vm));
        Assert.Equal(DiskFull.Message, rig.Notices.Error?.Text);
    });

    /// <summary>The same writes, when they succeed, store the value and show it.</summary>
    [Theory]
    [MemberData(nameof(ControlNames))]
    public Task EachControlWritesItsKey(string control) => Sta.RunAsync(async () =>
    {
        using var rig = new SettingsRig(s => s with { UserName = "Pat" });
        var (change, shown, stored) = Controls[control];
        var before = stored(rig.Current);

        await change(rig.Vm);
        await TestShell.Settle();

        Assert.Equal(1, rig.Settings.Writes);
        Assert.NotEqual(before, stored(rig.Current));
        Assert.Equal(stored(rig.Current), shown(rig.Vm));
        Assert.Null(rig.Notices.Error);
    });

    /// <summary>
    /// INV-HOME-29: Include is off limits while the name as typed trims to nothing, and saving a
    /// blank name turns it off; the name itself is stored untrimmed. A name that is not blank keeps it.
    /// </summary>
    [Fact]
    public Task BlankNameDisablesAndClearsInclude() => Sta.RunAsync(async () =>
    {
        using var rig = new SettingsRig(s => s with { UserName = "Dana", IncludeNameInReports = true });
        var about = rig.Vm.About;
        Assert.True(about.CanIncludeName);

        about.UserName = " Dana Reyes ";
        await about.CommitUserNameAsync();
        await TestShell.Settle();
        Assert.True(rig.Current.IncludeNameInReports);

        about.UserName = " \t\u00a0";
        Assert.False(about.CanIncludeName);
        Assert.True(about.IncludeName);
        await about.CommitUserNameAsync();
        await TestShell.Settle();
        Assert.Equal(" \t\u00a0", rig.Current.UserName);
        Assert.False(rig.Current.IncludeNameInReports);
        Assert.False(about.IncludeName);
        Assert.False(about.CanIncludeName);
    });

    /// <summary>A name write that fails stops there: Include stays as it was, as Electron's did.</summary>
    [Fact]
    public Task AFailedNameWriteLeavesInclude() => Sta.RunAsync(async () =>
    {
        using var rig = new SettingsRig(s => s with { UserName = "Dana", IncludeNameInReports = true });
        rig.Settings.WriteFails = DiskFull;
        rig.Vm.About.UserName = "";
        await rig.Vm.About.CommitUserNameAsync();
        await TestShell.Settle();
        Assert.Equal(1, rig.Settings.Writes);
        Assert.Equal(("Dana", true), (rig.Current.UserName, rig.Current.IncludeNameInReports));
        Assert.Equal("Dana", rig.Vm.About.UserName);
    });

    /// <summary>
    /// INV-HOME-41: the switch only writes; the real applier, over the same settings, takes the
    /// windows out of the exclusion at once, and back in on the rollback of a failed write.
    /// </summary>
    [Fact]
    public Task RemoteVisibleAppliesImmediately() => Sta.RunAsync(async () =>
    {
        using var rig = new SettingsRig();
        var protection = new RecordingProtection();
        using var applier = new RemoteVisibilityApplier(
            rig.Settings, new CaptureShield(protection, new SettingsCache(rig.Settings)), NullLogger<RemoteVisibilityApplier>.Instance);
        applier.Start();

        rig.Vm.Capture.RemoteVisible = true;
        Assert.True(await TestShell.UntilAsync(() => protection.Calls.Count == 1 && applier.LoopForTest.IsCompleted));
        Assert.False(protection.Calls[0].Excluded);
        Assert.True(protection.Calls[0].Pool);

        rig.Settings.WriteFails = DiskFull;
        rig.Vm.Capture.RemoteVisible = false;
        Assert.True(await TestShell.UntilAsync(() => rig.Notices.Error is not null && applier.LoopForTest.IsCompleted));
        Assert.True(rig.Current.RemoteVisible);
        Assert.True(protection.Calls.Count > 1, "the failed toggle applied nothing");
        Assert.False(protection.Calls[^1].Excluded);
    });

    /// <summary>A Brand chip repaints the window at once: the theme manager follows the write, before it lands.</summary>
    [Fact]
    public Task BrandChoiceRepaintsImmediately() => Sta.RunAsync(async () =>
    {
        using var rig = new SettingsRig(s => s with { Brand = "shotAI", Theme = ThemePref.Light });
        var ui = new WpfUiDispatcher(System.Windows.Threading.Dispatcher.CurrentDispatcher);
        var resources = new ResourceDictionary();
        using var theme = new ThemeManager(
            rig.Settings, new FakeSystemAppearance(), new NavigationState(NullLogger<NavigationState>.Instance), ui, NullLogger<ThemeManager>.Instance, false, () => false);
        theme.ApplyInitial(resources);
        theme.Start();
        Assert.Equal(Expected("shotAI", "accent"), Colour(resources, "accent"));

        rig.Vm.Appearance.Brands.Single(b => b.Id == "lfi").IsSelected = true;
        await TestShell.Settle();
        Assert.Equal("lfi", theme.CurrentBrand);
        Assert.Equal(Expected("lfi", "accent"), Colour(resources, "accent"));
        Assert.Equal(SettingsText.BrandBlurb("lfi"), rig.Vm.Appearance.BrandBlurb);
        Assert.True(rig.Vm.Appearance.Brands.Single(b => b.Id == "lfi").IsSelected);
        Assert.False(rig.Vm.Appearance.Brands.Single(b => b.Id == "shotAI").IsSelected);
    });

    /// <summary>
    /// EDGE-HOME-39, D-HOME-21: the fields saved on blur are saved when Settings goes, once: the
    /// first flush writes each edit, and another writes nothing; after it is disposed nothing runs.
    /// </summary>
    [Fact]
    public Task CustomInstructionsPersistOnUnload() => Sta.RunAsync(async () =>
    {
        using var rig = new SettingsRig();
        rig.Vm.Ai.CustomInstructions = "Always note the permissions needed";
        rig.Vm.About.UserName = "Dana";
        rig.Vm.Flush();
        await TestShell.Settle();
        Assert.Equal("Always note the permissions needed", rig.Current.Sop.CustomInstructions);
        Assert.Equal("Dana", rig.Current.UserName);
        Assert.Equal(2, rig.Settings.Writes);

        rig.Vm.Flush();
        await TestShell.Settle();
        Assert.Equal(2, rig.Settings.Writes);

        rig.Vm.Ai.CustomInstructions = "Changed after";
        rig.Vm.Dispose();
        rig.Vm.Flush();
        await TestShell.Settle();
        Assert.Equal(2, rig.Settings.Writes);
    });

    /// <summary>
    /// A line break in the custom instructions, CR LF in a WPF text box, counts one and is stored
    /// as LF, as Electron's textarea has it; the same text again writes nothing.
    /// </summary>
    [Fact]
    public Task CustomInstructionsKeepElectronsLineBreaks() => Sta.RunAsync(async () =>
    {
        using var rig = new SettingsRig();
        rig.Vm.Ai.CustomInstructions = "a\r\nb";
        Assert.Equal("3/2000", rig.Vm.Ai.CustomInstructionsCount);
        await rig.Vm.Ai.CommitCustomInstructionsAsync();
        await TestShell.Settle();
        Assert.Equal(("a\nb", 1), (rig.Current.Sop.CustomInstructions, rig.Settings.Writes));

        rig.Vm.Ai.CustomInstructions = "a\r\nb";
        await rig.Vm.Ai.CommitCustomInstructionsAsync();
        Assert.Equal(1, rig.Settings.Writes);
    });

    /// <summary>
    /// D-HOME-31: a field that loses the focus unchanged, a slider key-up on the stored step, and a
    /// click on the chip already chosen write nothing.
    /// </summary>
    [Fact]
    public Task UnchangedBlurDoesNotWrite() => Sta.RunAsync(async () =>
    {
        using var rig = new SettingsRig(s => s with { UserName = "Dana", Sop = s.Sop with { CustomInstructions = "Be brief" }, CaptureScale = 0.7 });
        await rig.Vm.Ai.CommitCustomInstructionsAsync();
        await rig.Vm.About.CommitUserNameAsync();
        rig.Vm.Capture.Scale = 0.65 + 0.05;
        await rig.Vm.Capture.CommitScaleAsync();
        rig.Vm.Appearance.Themes[0].IsSelected = true;
        rig.Vm.Storage.ArchiveAge = rig.Vm.Storage.ArchiveAge;
        rig.Vm.Ai.Enabled = true;
        await TestShell.Settle();
        Assert.Equal(0, rig.Settings.Writes);
    });

    /// <summary>
    /// A rollback reaches every control from the settings alone: the view model keeps no old value,
    /// and a field's edit is replaced only when its own setting changed.
    /// </summary>
    [Fact]
    public Task RollbackShowsCurrent() => Sta.RunAsync(async () =>
    {
        using var rig = new SettingsRig();
        rig.Settings.Set(s => s with { Theme = ThemePref.Dark, UserName = "Pat", ArchiveAgeDays = 45 });
        await TestShell.Settle();
        Assert.Equal("dark", Selected(rig.Vm.Appearance.Themes));
        Assert.Equal("Pat", rig.Vm.About.UserName);
        Assert.Equal(new ArchiveAgeOption(45, "After 45 days"), rig.Vm.Storage.ArchiveAge);

        rig.Vm.Ai.CustomInstructions = "typed, not saved";
        rig.Settings.Set(s => s with { Theme = ThemePref.System, UserName = "", ArchiveAgeDays = 90 }, rollback: true);
        await TestShell.Settle();
        Assert.Equal("system", Selected(rig.Vm.Appearance.Themes));
        Assert.Equal(SettingsText.ThemeBlurb(ThemePref.System), rig.Vm.Appearance.ThemeBlurb);
        Assert.Equal("", rig.Vm.About.UserName);
        Assert.Equal(new ArchiveAgeOption(90, "After 3 months"), rig.Vm.Storage.ArchiveAge);
        Assert.Same(ArchiveAgeOptions.Standard, rig.Vm.Storage.ArchiveAges);
        Assert.Equal("typed, not saved", rig.Vm.Ai.CustomInstructions);
    });

    /// <summary>A change raised on the settings queue's thread is shown on the UI thread.</summary>
    [Fact]
    public Task AChangeFromAnotherThreadIsShown() => Sta.RunAsync(async () =>
    {
        using var rig = new SettingsRig();
        var ui = Environment.CurrentManagedThreadId;
        var raised = new List<(string? Name, int Thread)>();
        rig.Vm.Capture.PropertyChanged += (_, e) => raised.Add((e.PropertyName, Environment.CurrentManagedThreadId));
        await rig.Settings.SetFromAnotherThreadAsync(s => s with { RemoteVisible = true });
        await TestShell.Settle();
        Assert.Contains((nameof(CaptureSettingsViewModel.RemoteVisible), ui), raised);
        Assert.All(raised, r => Assert.Equal(ui, r.Thread));
        Assert.True(rig.Vm.Capture.RemoteVisible);
    });

    /// <summary>2.24: each open starts on AI; a tab's selection shows its section, named after it.</summary>
    [Fact]
    public Task TabsShowTheirSection() => Sta.RunAsync(() =>
    {
        using var rig = new SettingsRig();
        var vm = rig.Vm;
        Assert.Equal((SettingsTab.Ai, true, "AI"), (vm.Tab, vm.IsAiTab, vm.ActiveTabLabel));
        Assert.Same(vm.Ai, vm.ActiveSection);
        (Action Select, SettingsSectionViewModel Section, string Label)[] tabs =
        [
            (() => vm.IsCaptureTab = true, vm.Capture, "Capture"),
            (() => vm.IsAppearanceTab = true, vm.Appearance, "Appearance"),
            (() => vm.IsStorageTab = true, vm.Storage, "Storage"),
            (() => vm.IsAboutTab = true, vm.About, "About"),
            (() => vm.IsAiTab = true, vm.Ai, "AI"),
        ];
        foreach (var (select, section, label) in tabs)
        {
            select();
            Assert.Same(section, vm.ActiveSection);
            Assert.Equal(label, vm.ActiveTabLabel);
        }
        vm.IsCaptureTab = false;
        Assert.Equal(SettingsTab.Ai, vm.Tab);
    });

    /// <summary>The AI tab's text and its SOP options, as Electron shows them without sign-in.</summary>
    [Fact]
    public Task AiTabShowsTheSopOptions() => Sta.RunAsync(async () =>
    {
        using var rig = new SettingsRig();
        var ai = rig.Vm.Ai;
        Assert.Equal(SettingsText.AiHint(federated: false), ai.Hint);
        Assert.Equal(SettingsText.AiOff(federated: false), ai.OffText);
        Assert.Equal(SopCatalog.Models.Select(m => m.Label), ai.Models.Select(m => m.Label));
        Assert.Equal(SopCatalog.Tones.Select(t => t.Label), ai.Tones.Select(t => t.Label));
        Assert.Equal(SopCatalog.Efforts.Select(e => e.Label), ai.Efforts.Select(e => e.Label));
        Assert.Equal(("claude-sonnet-5", "professional", "medium"), (Selected(ai.Models), Selected(ai.Tones), Selected(ai.Efforts)));
        Assert.Equal(SopCatalog.Models[0].Blurb, ai.ModelBlurb);
        Assert.Equal("Formal, third-person, SOP-standard phrasing.", ai.ToneBlurb);
        Assert.Equal("Balanced quality, speed, and cost (recommended).", ai.EffortBlurb);
        Assert.Equal(("0/2000", true, false), (ai.CustomInstructionsCount, ai.ShowsCustomInstructionsPlaceholder, ai.IsOff));

        ai.CustomInstructions = "\ud83d\ude00 ok";
        Assert.Equal(("5/2000", false), (ai.CustomInstructionsCount, ai.ShowsCustomInstructionsPlaceholder));
        ai.Enabled = false;
        await TestShell.Settle();
        Assert.True(ai.IsOff);
    });

    /// <summary>
    /// 2.28, 11 7.3.5: Change... opens the dialog at the current folder; the folder picked becomes
    /// the projects folder and Home is told; a cancel does nothing; without a window nothing opens.
    /// </summary>
    [Fact]
    public Task ChangeFolderPicksAtTheCurrentFolder() => Sta.RunAsync(async () =>
    {
        using var rig = new SettingsRig();
        var owner = new Window();
        var changed = 0;
        rig.Vm.ProjectsDirChanged += (_, _) => changed++;
        rig.Projects.ProjectsDir = @"C:\Users\test\Documents\shotAI";
        rig.Projects.ProjectsDirSet = dir => rig.Settings.Set(s => s with { ProjectsDir = dir });

        rig.Dialogs.Folder = null;
        await rig.Vm.Storage.ChangeProjectsDirCommand.ExecuteAsync(owner);
        Assert.Equal((owner, SettingsText.FolderDialogTitle, @"C:\Users\test\Documents\shotAI"), Assert.Single(rig.Dialogs.FolderPicks));
        Assert.Empty(rig.Projects.ProjectsDirsSet);
        Assert.Equal(0, changed);

        rig.Dialogs.Folder = @"D:\Guides";
        await rig.Vm.Storage.ChangeProjectsDirCommand.ExecuteAsync(owner);
        await TestShell.Settle();
        Assert.Equal([@"D:\Guides"], rig.Projects.ProjectsDirsSet);
        Assert.Equal(1, changed);
        Assert.Equal(@"D:\Guides", rig.Vm.Storage.ProjectsDir);
        Assert.False(rig.Vm.HasError);

        await rig.Vm.Storage.ChangeProjectsDirCommand.ExecuteAsync(null);
        Assert.Equal(2, rig.Dialogs.FolderPicks.Count);
    });

    /// <summary>
    /// 2.24, 7.12: a folder change that fails is the inline error, with 11's message: the OS's for
    /// an expected failure, the generic sentence (logged) for anything else. A settings write clears it.
    /// </summary>
    [Fact]
    public Task AFailedFolderChangeIsTheInlineError() => Sta.RunAsync(async () =>
    {
        using var rig = new SettingsRig();
        var owner = new Window();
        rig.Dialogs.Folder = @"Z:\Nowhere";
        rig.Projects.SetProjectsDirFailure = new UnauthorizedAccessException(@"Access to the path 'Z:\Nowhere' is denied.");
        await rig.Vm.Storage.ChangeProjectsDirCommand.ExecuteAsync(owner);
        Assert.True(rig.Vm.HasError);
        Assert.Equal(@"Error: Access to the path 'Z:\Nowhere' is denied.", rig.Vm.ErrorText);
        Assert.Null(rig.Notices.Error);

        rig.Vm.Capture.RemoteVisible = true;
        await TestShell.Settle();
        Assert.False(rig.Vm.HasError);
        Assert.Equal("", rig.Vm.ErrorText);

        rig.Dialogs.Throws = new InvalidOperationException("internal detail");
        await rig.Vm.Storage.ChangeProjectsDirCommand.ExecuteAsync(owner);
        Assert.Equal(SettingsText.Error(UserMessage.From(new InvalidOperationException())!), rig.Vm.ErrorText);
        Assert.Contains(rig.Logs.Entries, e => e.Level == LogLevel.Error && e.Message.StartsWith("settings: unexpected error", StringComparison.Ordinal));

        rig.Vm.Capture.RemoteVisible = false;
        await TestShell.Settle();
        Assert.False(rig.Vm.HasError);
        rig.Dialogs.Throws = new OperationCanceledException();
        await rig.Vm.Storage.ChangeProjectsDirCommand.ExecuteAsync(owner);
        Assert.False(rig.Vm.HasError);
        Assert.Single(rig.Logs.Entries, e => e.Level == LogLevel.Error);
    });

    /// <summary>Back asks the shell to close Settings; dispose leaves the settings' event, and a refresh already posted does nothing.</summary>
    [Fact]
    public Task BackAndDispose() => Sta.RunAsync(async () =>
    {
        var rig = new SettingsRig();
        var back = 0;
        rig.Vm.BackRequested += (_, _) => back++;
        rig.Vm.BackCommand.Execute(null);
        Assert.Equal(1, back);

        Assert.Equal(1, rig.Settings.Subscribers);
        rig.Settings.Set(s => s with { RemoteVisible = true });
        rig.Dispose();
        rig.Dispose();
        Assert.Equal(0, rig.Settings.Subscribers);
        var raised = 0;
        rig.Vm.Capture.PropertyChanged += (_, _) => raised++;
        await TestShell.Settle();
        Assert.Equal(0, raised);
    });

    [Fact]
    public Task ArgumentsAreChecked() => Sta.RunAsync(() =>
    {
        var settings = new FakeSettingsService();
        var projects = new ListingProjects();
        var dialogs = new FakeFileDialogs();
        IAppInfo info = new FakeAppInfo();
        var notices = new NoticeCenter(NullLogger<NoticeCenter>.Instance);
        var ui = new WpfUiDispatcher(System.Windows.Threading.Dispatcher.CurrentDispatcher);
        var log = NullLogger<SettingsViewModel>.Instance;
        Assert.Throws<ArgumentNullException>(() => new SettingsViewModel(null!, projects, dialogs, info, notices, ui, log));
        Assert.Throws<ArgumentNullException>(() => new SettingsViewModel(settings, null!, dialogs, info, notices, ui, log));
        Assert.Throws<ArgumentNullException>(() => new SettingsViewModel(settings, projects, null!, info, notices, ui, log));
        Assert.Throws<ArgumentNullException>(() => new SettingsViewModel(settings, projects, dialogs, null!, notices, ui, log));
        Assert.Throws<ArgumentNullException>(() => new SettingsViewModel(settings, projects, dialogs, info, null!, ui, log));
        Assert.Throws<ArgumentNullException>(() => new SettingsViewModel(settings, projects, dialogs, info, notices, null!, log));
        Assert.Throws<ArgumentNullException>(() => new SettingsViewModel(settings, projects, dialogs, info, notices, ui, null!));
        Assert.Throws<ArgumentNullException>(() => new SettingsViewModelFactory(null!, projects, dialogs, info, notices, ui, log));
        Assert.Throws<ArgumentNullException>(() => new SettingsChoice(null!, "x"));
        Assert.Throws<ArgumentNullException>(() => new SettingsChoice("x", null!));
        Assert.Equal(0, settings.Subscribers);
    });

    // A chip set by its binding, or a switch, a list or a slider: the write it starts has run its
    // optimistic step when this returns, and its outcome lands as the test settles.
    private static Task Set(Action set)
    {
        set();
        return Task.CompletedTask;
    }

    // A field typed into, then its blur.
    private static Task Typed(Action type, Func<Task> blur)
    {
        type();
        return blur();
    }

    // The selected chip's value, or empty when none is.
    private static string Selected(IEnumerable<SettingsChoice> choices) => choices.SingleOrDefault(c => c.IsSelected)?.Id ?? "";

    private static Color Expected(string brand, string token)
    {
        var c = ThemeTokenSet.For(brand, Appearance.Light).Colours[token];
        return Color.FromRgb(c.R, c.G, c.B);
    }

    private static Color Colour(ResourceDictionary resources, string token) => ((SolidColorBrush)resources[ThemeTokenKeys.Brush(token)]).Color;

    // The shield's synchronous cache over the fake service, as SettingsService serves it.
    private sealed class SettingsCache(ISettingsService settings) : ICaptureSettings
    {
        public double CaptureScaleNow() => settings.Current.CaptureScale;

        public bool RemoteVisibleNow() => settings.Current.RemoteVisible;
    }
}
