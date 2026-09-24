using System.IO;
using System.Windows;
using Microsoft.Extensions.Logging.Abstractions;
using ShotAI.App.Chrome;
using ShotAI.App.Home;
using ShotAI.App.Tests.Support;
using ShotAI.Core.Capture;
using ShotAI.Core.Home;
using ShotAI.Core.Model;
using Xunit;
using Rect = ShotAI.Core.Model.Rect;

namespace ShotAI.App.Tests.Home;

/// <summary>
/// Spec 06 8.4's <c>CaptureModePickerTests</c> (2.4, INV-HOME-16, EDGE-HOME-31, EDGE-HOME-57,
/// R-ARCH-22): the loads and their keep-or-default rule, the picks, the area selection, the
/// labels and warnings, and the one picker outliving Home. The dropdown's view cases are
/// <c>TargetDropdownTests</c>.
/// </summary>
public sealed class CaptureModePickerTests
{
    private static readonly WindowInfo Notepad = new(0x0003_04A2, 4312, "notes.txt - Notepad", "Notepad");
    private static readonly WindowInfo Outlook = new(0x0002_0C18, 9120, "Inbox", "Outlook");
    private static readonly WindowInfo Untitled = new(0x0005_0002, 77, "", "");
    private static readonly MonitorInfo Side = new(131_073, "Display 2", 1920, 1080, IsPrimary: false);
    private static readonly MonitorInfo Main = new(65_537, "DELL U2720Q", 3840, 2160, IsPrimary: true);
    private static readonly Rect Area = new(2660, 140, 600, 450);

    private sealed class Rig
    {
        public Rig() => Picker = new CaptureModePickerViewModel(Capture, Areas, Notices);

        public FakeCaptureService Capture { get; } = new() { Targets = new CaptureTargets([Notepad, Outlook], [Side, Main]) };

        public FakeAreaSelection Areas { get; } = new();

        public NoticeCenter Notices { get; } = new(NullLogger<NoticeCenter>.Instance);

        public CaptureModePickerViewModel Picker { get; }

        public string? Error => Notices.Error?.Text;
    }

    [Fact]
    public Task EachLaunchStartsInScreenModeWithNothingLoaded() => Sta.RunAsync(() =>
    {
        var r = new Rig();
        Assert.Equal(CaptureMode.Screen, r.Picker.Mode);
        Assert.True((r.Picker.IsScreen, r.Picker.IsAuto, r.Picker.IsWindow, r.Picker.IsArea) == (true, false, false, false));
        Assert.Null(r.Picker.Targets);
        Assert.Equal(0, r.Capture.ListCount);
        Assert.Equal(new CaptureTarget("screen"), r.Picker.BuildTarget());
    });

    /// <summary>INV-HOME-16 through the picker: Window needs a window and Area an area.</summary>
    [Fact]
    public Task Readiness() => Sta.RunAsync(async () =>
    {
        var r = new Rig { Capture = { Targets = new CaptureTargets([], [Main]) } };
        Assert.True(r.Picker.IsReady);
        r.Picker.SelectModeCommand.Execute(CaptureMode.Window);
        await TestShell.Settle();
        Assert.False(r.Picker.IsReady);
        r.Picker.SelectModeCommand.Execute(CaptureMode.Area);
        Assert.False(r.Picker.IsReady);
        r.Areas.Result = Area;
        await r.Picker.SelectAreaCommand.ExecuteAsync(Probe());
        Assert.True(r.Picker.IsReady);
        r.Picker.SelectModeCommand.Execute(CaptureMode.Auto);
        Assert.True(r.Picker.IsReady);
        r.Picker.SelectModeCommand.Execute(CaptureMode.Screen);
        Assert.True(r.Picker.IsReady);
    });

    /// <summary>The first Home in Screen mode loads the targets once, and picks the primary monitor (<c>App.tsx:216-218</c>).</summary>
    [Fact]
    public Task DefaultMonitorIsThePrimary() => Sta.RunAsync(async () =>
    {
        var r = new Rig();
        r.Picker.OnHomeShown();
        await TestShell.Settle();
        Assert.Equal(Main.Id, r.Picker.PickedMonitorId);
        Assert.Same(Notepad, r.Picker.PickedWindow);
        Assert.Equal(HomeText.MonitorLabel(Main, loading: false), r.Picker.TriggerLabel);
        Assert.Equal(new CaptureTarget("screen", MonitorId: Main.Id), r.Picker.BuildTarget());
        r.Picker.OnHomeShown();
        await TestShell.Settle();
        Assert.Equal(1, r.Capture.ListCount);
    });

    /// <summary>With no primary listed, the first monitor; with none at all, no monitor, and Screen is still ready.</summary>
    [Fact]
    public Task WithoutAPrimaryTheFirstMonitor() => Sta.RunAsync(async () =>
    {
        var r = new Rig { Capture = { Targets = new CaptureTargets([], [Side, Side with { Id = 9, Name = "Display 3" }]) } };
        await r.Picker.LoadTargetsAsync();
        Assert.Equal(Side.Id, r.Picker.PickedMonitorId);
        r.Capture.Targets = new CaptureTargets([], []);
        await r.Picker.LoadTargetsAsync();
        Assert.Null(r.Picker.PickedMonitorId);
        Assert.Null(r.Picker.PickedWindow);
        Assert.True(r.Picker.IsReady);
        Assert.Equal(HomeText.SelectMonitor, r.Picker.TriggerLabel);
    });

    /// <summary>Another first mode loads nothing at the first Home; a mode that lists targets loads them when chosen, once.</summary>
    [Fact]
    public Task SelectModeLoadsOnceAndClosesTheDropdown() => Sta.RunAsync(async () =>
    {
        var r = new Rig();
        r.Picker.SelectModeCommand.Execute(CaptureMode.Auto);
        r.Picker.OnHomeShown();
        await TestShell.Settle();
        Assert.Equal(0, r.Capture.ListCount);
        r.Picker.SelectModeCommand.Execute(CaptureMode.Area);
        await TestShell.Settle();
        Assert.Equal(0, r.Capture.ListCount);
        r.Picker.PickerOpen = true;
        r.Picker.SelectModeCommand.Execute(CaptureMode.Window);
        Assert.False(r.Picker.PickerOpen);
        await TestShell.Settle();
        Assert.Equal(1, r.Capture.ListCount);
        r.Picker.SelectModeCommand.Execute(CaptureMode.Screen);
        await TestShell.Settle();
        Assert.Equal(1, r.Capture.ListCount);
        // A click on the chip already chosen closes the dropdown too, as selectMode does.
        r.Picker.PickerOpen = true;
        r.Picker.SelectModeCommand.Execute(CaptureMode.Screen);
        Assert.False(r.Picker.PickerOpen);
    });

    /// <summary>Choosing a mode while a load runs starts no second one.</summary>
    [Fact]
    public Task OneLoadAtATime() => Sta.RunAsync(async () =>
    {
        var gate = new TaskCompletionSource();
        var r = new Rig { Capture = { ListGate = gate.Task } };
        r.Picker.SelectModeCommand.Execute(CaptureMode.Window);
        Assert.True(r.Picker.TargetsLoading);
        Assert.Equal(HomeText.Loading, r.Picker.TriggerLabel);
        Assert.Equal(HomeText.Loading, r.Picker.EmptyText);
        Assert.False(r.Picker.RefreshCommand.CanExecute(null));
        r.Picker.SelectModeCommand.Execute(CaptureMode.Screen);
        r.Picker.SelectModeCommand.Execute(CaptureMode.Window);
        gate.SetResult();
        Assert.True(await TestShell.UntilAsync(() => !r.Picker.TargetsLoading));
        Assert.Equal(1, r.Capture.ListCount);
        Assert.True(r.Picker.RefreshCommand.CanExecute(null));
    });

    /// <summary>
    /// 2.4's keep-or-default rule: a window still listed stays picked as first listed, its old
    /// title kept (EDGE-HOME-31); a monitor still listed stays; otherwise the first window and the
    /// primary monitor.
    /// </summary>
    [Fact]
    public Task KeepOrDefaultOnReload() => Sta.RunAsync(async () =>
    {
        var r = new Rig();
        await r.Picker.LoadTargetsAsync();
        r.Picker.SelectModeCommand.Execute(CaptureMode.Window);
        r.Picker.PickCommand.Execute(r.Picker.Items[1]);
        Assert.Same(Outlook, r.Picker.PickedWindow);
        r.Picker.SelectModeCommand.Execute(CaptureMode.Screen);
        r.Picker.PickCommand.Execute(r.Picker.Items[0]);
        Assert.Equal(Side.Id, r.Picker.PickedMonitorId);

        var renamed = Outlook with { Title = "Sent Items" };
        var third = new MonitorInfo(7, "Display 3", 1280, 1024, IsPrimary: true);
        r.Capture.Targets = new CaptureTargets([Notepad, renamed], [third, Side]);
        await r.Picker.RefreshCommand.ExecuteAsync(null);
        Assert.Same(Outlook, r.Picker.PickedWindow);
        Assert.Equal(Side.Id, r.Picker.PickedMonitorId);
        Assert.Equal(2, r.Capture.ListCount);

        r.Capture.Targets = new CaptureTargets([renamed with { Id = 1 }], [Main, third]);
        await r.Picker.RefreshCommand.ExecuteAsync(null);
        Assert.Equal(1u, r.Picker.PickedWindow!.Id);
        Assert.Equal(Main.Id, r.Picker.PickedMonitorId);
    });

    /// <summary>A failed load shows the error notice and keeps what was listed before.</summary>
    [Fact]
    public Task AFailedLoadShowsTheError() => Sta.RunAsync(async () =>
    {
        var r = new Rig();
        await r.Picker.LoadTargetsAsync();
        r.Capture.ListFails = new IOException("The window list could not be read.");
        await r.Picker.LoadTargetsAsync();
        Assert.Equal("The window list could not be read.", r.Error);
        Assert.False(r.Picker.TargetsLoading);
        Assert.Equal(2, r.Picker.Targets!.Windows.Count);
        Assert.Equal(Main.Id, r.Picker.PickedMonitorId);
    });

    /// <summary>A pick takes the row's window or monitor, closes the dropdown and marks the row.</summary>
    [Fact]
    public Task PickClosesAndMarksTheRow() => Sta.RunAsync(async () =>
    {
        var r = new Rig();
        await r.Picker.LoadTargetsAsync();
        r.Picker.SelectModeCommand.Execute(CaptureMode.Window);
        Assert.Equal([true, false], r.Picker.Items.Select(i => i.IsPicked));
        r.Picker.PickerOpen = true;
        r.Picker.PickCommand.Execute(r.Picker.Items[1]);
        Assert.False(r.Picker.PickerOpen);
        Assert.Equal([false, true], r.Picker.Items.Select(i => i.IsPicked));
        Assert.Equal(new CaptureTarget("window", Window: new CaptureTargetWindow(Outlook.Id, Outlook.Pid, "Inbox")), r.Picker.BuildTarget());
        r.Picker.PickCommand.Execute(null);
        Assert.Same(Outlook, r.Picker.PickedWindow);
    });

    /// <summary>The rows: a window's app before its title, or its title alone; a monitor's size after its name.</summary>
    [Fact]
    public Task RowsReadAsTheListShowsThem() => Sta.RunAsync(async () =>
    {
        var r = new Rig { Capture = { Targets = new CaptureTargets([Notepad, Untitled], [Main, Side]) } };
        await r.Picker.LoadTargetsAsync();
        r.Picker.SelectModeCommand.Execute(CaptureMode.Window);
        Assert.Equal(
            [("notes.txt - Notepad", "Notepad", true, true), ("(untitled)", "", true, false)],
            r.Picker.Items.Select(i => (i.Name, i.Detail, i.DetailFirst, i.HasDetail)));
        Assert.Equal(["Notepad notes.txt - Notepad", "(untitled)"], r.Picker.Items.Select(i => i.AccessibleName));
        Assert.Equal((HomeText.WindowsHead, HomeText.WindowListName, HomeText.NoWindows), (r.Picker.ListHead, r.Picker.ListName, r.Picker.EmptyText));
        r.Picker.SelectModeCommand.Execute(CaptureMode.Screen);
        Assert.Equal(
            [("DELL U2720Q", "3840\u00d72160 \u00b7 primary", false), ("Display 2", "1920\u00d71080", false)],
            r.Picker.Items.Select(i => (i.Name, i.Detail, i.DetailFirst)));
        Assert.Equal("DELL U2720Q 3840\u00d72160 \u00b7 primary", r.Picker.Items[0].AccessibleName);
        Assert.Equal((HomeText.MonitorsHead, HomeText.MonitorListName, HomeText.NoMonitors), (r.Picker.ListHead, r.Picker.ListName, r.Picker.EmptyText));
        r.Capture.Targets = new CaptureTargets([], []);
        await r.Picker.LoadTargetsAsync();
        Assert.Equal((false, true), (r.Picker.HasItems, r.Picker.IsListEmpty));
    });

    /// <summary>The trigger's label in Window mode: loading, nothing picked, then the app and title.</summary>
    [Fact]
    public Task WindowLabels() => Sta.RunAsync(async () =>
    {
        var gate = new TaskCompletionSource();
        var r = new Rig { Capture = { ListGate = gate.Task, Targets = new CaptureTargets([], []) } };
        r.Picker.SelectModeCommand.Execute(CaptureMode.Window);
        Assert.Equal(HomeText.Loading, r.Picker.TriggerLabel);
        gate.SetResult();
        Assert.True(await TestShell.UntilAsync(() => !r.Picker.TargetsLoading));
        Assert.Equal(HomeText.SelectWindow, r.Picker.TriggerLabel);
        r.Capture.Targets = new CaptureTargets([Notepad], []);
        await r.Picker.LoadTargetsAsync();
        Assert.Equal("Notepad \u2014 notes.txt - Notepad", r.Picker.TriggerLabel);
    });

    /// <summary>The warnings: Window without a window, Area without an area unless one is being selected, and Auto's.</summary>
    [Fact]
    public Task Warnings() => Sta.RunAsync(async () =>
    {
        var r = new Rig { Capture = { Targets = new CaptureTargets([], [Main]) } };
        Assert.Equal((false, false, false), Warned(r.Picker));
        r.Picker.SelectModeCommand.Execute(CaptureMode.Auto);
        Assert.Equal((false, false, true), Warned(r.Picker));
        r.Picker.SelectModeCommand.Execute(CaptureMode.Window);
        await TestShell.Settle();
        Assert.Equal((true, false, false), Warned(r.Picker));
        r.Picker.SelectModeCommand.Execute(CaptureMode.Area);
        Assert.Equal((false, true, false), Warned(r.Picker));
        r.Areas.Hold = true;
        var selecting = r.Picker.SelectAreaCommand.ExecuteAsync(Probe());
        Assert.Equal((false, false, false), Warned(r.Picker));
        r.Areas.Answer(null);
        await selecting;
        Assert.Equal((false, true, false), Warned(r.Picker));
        Assert.Equal((true, false, false), (r.Picker.ShowsAreaPicker, r.Picker.ShowsDropdown, r.Picker.HasArea));
    });

    /// <summary>2.4's selectArea: the button reads Selecting while the overlay is up and cannot run again; the area shows beside it.</summary>
    [Fact]
    public Task SelectingAnArea() => Sta.RunAsync(async () =>
    {
        var r = new Rig { Areas = { Hold = true } };
        r.Picker.SelectModeCommand.Execute(CaptureMode.Area);
        Assert.Equal(HomeText.SelectArea, r.Picker.AreaButtonText);
        var window = Probe();
        var selecting = r.Picker.SelectAreaCommand.ExecuteAsync(window);
        Assert.True(r.Picker.SelectingArea);
        Assert.Equal(HomeText.Selecting, r.Picker.AreaButtonText);
        Assert.False(r.Picker.SelectAreaCommand.CanExecute(window));
        r.Areas.Answer(Area);
        await selecting;
        Assert.False(r.Picker.SelectingArea);
        Assert.Equal((Area, HomeText.ReselectArea), (r.Picker.PickedArea, r.Picker.AreaButtonText));
        Assert.Equal("600 \u00d7 450px @ (2660, 140)", r.Picker.AreaText);
        Assert.Same(window, Assert.Single(r.Areas.Requesters));
        Assert.Equal(new CaptureTarget("area", Area: Area), r.Picker.BuildTarget());
    });

    /// <summary>A cancelled selection keeps the area before it (AC-SHELL-17's "the chooser keeps its previous area").</summary>
    [Fact]
    public Task AreaCancelKeepsTheOldArea() => Sta.RunAsync(async () =>
    {
        var r = new Rig { Areas = { Result = Area } };
        r.Picker.SelectModeCommand.Execute(CaptureMode.Area);
        await r.Picker.SelectAreaCommand.ExecuteAsync(Probe());
        r.Areas.Result = null;
        await r.Picker.SelectAreaCommand.ExecuteAsync(Probe());
        Assert.Equal(Area, r.Picker.PickedArea);
        Assert.Equal(2, r.Areas.Requesters.Count);
    });

    /// <summary>A failed selection shows the error notice and keeps the area; no requester selects nothing.</summary>
    [Fact]
    public Task AFailedSelectionShowsTheError() => Sta.RunAsync(async () =>
    {
        var r = new Rig { Areas = { Result = Area } };
        await r.Picker.SelectAreaCommand.ExecuteAsync(Probe());
        r.Areas.Fails = new IOException("The overlay could not open.");
        await r.Picker.SelectAreaCommand.ExecuteAsync(Probe());
        Assert.Equal("The overlay could not open.", r.Error);
        Assert.Equal((Area, false), (r.Picker.PickedArea, r.Picker.SelectingArea));
        await r.Picker.SelectAreaCommand.ExecuteAsync(null);
        Assert.Equal(2, r.Areas.Requesters.Count);
    });

    /// <summary>R-ARCH-22: a monitor id above <c>int.MaxValue</c> survives a pick, a reload and the target.</summary>
    [Fact]
    public Task MonitorIdIsUint() => Sta.RunAsync(async () =>
    {
        var high = new MonitorInfo(0xFFFF_FFF0, "Display 9", 2560, 1440, IsPrimary: false);
        var r = new Rig { Capture = { Targets = new CaptureTargets([], [Main, high]) } };
        await r.Picker.LoadTargetsAsync();
        r.Picker.PickCommand.Execute(r.Picker.Items[1]);
        await r.Picker.LoadTargetsAsync();
        Assert.Equal(0xFFFF_FFF0u, r.Picker.PickedMonitorId);
        Assert.Same(high, r.Picker.PickedMonitor);
        Assert.Equal(4_294_967_280d, r.Picker.BuildTarget().MonitorId);
    });

    /// <summary>
    /// EDGE-HOME-57: the one picker's mode, targets and picks are unchanged by a trip into a
    /// project and back, and nothing reloads them; its dropdown closes when Home is left.
    /// </summary>
    [Fact]
    public Task SurvivesNavigation() => Sta.RunAsync(async () =>
    {
        using var t = new TestShell();
        t.Capture.Targets = new CaptureTargets([Notepad, Outlook], [Main]);
        t.Projects.CanOpen(@"C:\Projects\A", Manifests.Of("A"));
        t.Shell.Start();
        await TestShell.Settle();
        t.Mode.SelectModeCommand.Execute(CaptureMode.Window);
        t.Mode.PickCommand.Execute(t.Mode.Items[1]);
        t.Mode.PickerOpen = true;
        var targets = t.Mode.Targets;

        await t.Shell.OpenProjectAsync(@"C:\Projects\A");
        Assert.False(t.Mode.PickerOpen);
        t.Project.BackCommand.Execute(null);
        await TestShell.Settle();

        Assert.Same(t.Mode, t.Home.Mode);
        Assert.Equal((CaptureMode.Window, Outlook), (t.Mode.Mode, t.Mode.PickedWindow));
        Assert.Same(targets, t.Mode.Targets);
        Assert.Equal(1, t.Capture.ListCount);
    });

    /// <summary>The Mode chips' view: each radio chip is the mode, and Auto's warning shows only for it.</summary>
    [Fact]
    public Task ChipsFollowTheMode() => Sta.RunAsync(async () =>
    {
        using var t = new TestShell();
        var view = new HomeView { DataContext = t.Home };
        var window = TestShell.Host(view);
        window.Show();
        try
        {
            await TestShell.Settle();
            Assert.True(view.ScreenChip.IsChecked);
            Assert.Equal(Visibility.Collapsed, view.AutoWarning.Visibility);
            view.AutoChip.IsChecked = true;
            await TestShell.Settle();
            Assert.Equal(CaptureMode.Auto, t.Mode.Mode);
            Assert.Equal(Visibility.Visible, view.AutoWarning.Visibility);
            Assert.Equal(Visibility.Collapsed, view.TargetTrigger.Visibility);
            t.Mode.SelectModeCommand.Execute(CaptureMode.Window);
            await TestShell.Settle();
            Assert.True(view.WindowChip.IsChecked);
            Assert.False(view.AutoChip.IsChecked);
            Assert.Equal(Visibility.Visible, view.TargetTrigger.Visibility);
            Assert.Equal(Visibility.Visible, view.WindowWarning.Visibility);
            Assert.False(view.CaptureButton.IsEnabled);
        }
        finally
        {
            window.Close();
        }
    });

    private static (bool Window, bool Area, bool Auto) Warned(CaptureModePickerViewModel picker) =>
        (picker.ShowsWindowWarning, picker.ShowsAreaWarning, picker.ShowsAutoWarning);

    // The requester an area selection hides; the fake never shows or hides it.
    private static Window Probe() => new() { ShowActivated = false };
}
