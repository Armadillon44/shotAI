using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using ShotAI.App.Home;
using ShotAI.App.Shell;
using ShotAI.App.Tests.Support;
using ShotAI.Core.Capture;
using ShotAI.Core.Home;
using ShotAI.Core.Model;
using ShotAI.Core.Store;
using Xunit;
using static ShotAI.App.Tests.Support.Manifests;

namespace ShotAI.App.Tests.Home;

/// <summary>
/// Spec 06 8.4 <c>RecordingPanelTests</c>, 2.6 and 7.6: the panel lists the opened project's
/// steps and each one that lands, counts the list, runs Pause, Resume and Stop off the UI thread,
/// shows each capture error as the error notice, and follows the engine for the shell's life.
/// </summary>
public sealed class RecordingPanelTests
{
    private const string Recorded = @"C:\Projects\A";

    /// <summary>2.5 step 2: a recording lists the opened manifest's steps, read before the start.</summary>
    [Fact]
    public Task SeededStepsAreListed() => Sta.RunAsync(async () =>
    {
        using var t = new TestShell();
        t.Projects.CanOpen(Recorded, Of("A", Shot("s1", caption: "Open the app"), Shot("s2", caption: "Click Save", extra: ",\"order\":2,\"window\":{\"title\":\"Notes\"}")));
        var seededBeforeStart = -1;
        t.Capture.OnCall = call =>
        {
            if (call == "start") seededBeforeStart = t.Recording.Steps.Count;
        };
        t.Shell.Start();

        Assert.True(await t.Shell.RecordAsync(Recorded, new CaptureTarget("auto"), createdThisSession: false));

        Assert.Equal(2, seededBeforeStart);
        Assert.Equal([new RecordingStepRow("", "Open the app", null), new RecordingStepRow("2", "Click Save", "Notes")], t.Recording.Steps);
        Assert.Equal("2 steps", t.Recording.CountText);
        // The panel's state arrives with its posted re-read.
        await TestShell.Settle();
        Assert.Equal("Capturing \u00b7 A", t.Recording.Label);
    });

    /// <summary>Each session lists its project's steps again, not the last session's.</summary>
    [Fact]
    public Task EachRecordingSeedsAgain() => Sta.RunAsync(async () =>
    {
        using var t = new TestShell();
        t.Projects.CanOpen(Recorded, Of("A", Shot("s1"), Shot("s2"), Shot("s3")));
        t.Shell.Start();
        await t.Shell.RecordAsync(Recorded, new CaptureTarget("auto"), createdThisSession: false);
        t.Capture.RaiseEnded();
        await TestShell.Settle();

        t.Projects.CanOpen(Recorded, Of("A", Shot("s1")));
        await t.Shell.RecordAsync(Recorded, new CaptureTarget("auto"), createdThisSession: false);

        Assert.Single(t.Recording.Steps);
    });

    /// <summary>06 7.6: a landed step goes at the end of the list, whatever its index, as Electron appended it.</summary>
    [Fact]
    public Task ALandedStepIsAppendedAtTheEnd() => Sta.RunAsync(async () =>
    {
        using var t = new TestShell();
        t.Recording.Seed([Shot("s1", caption: "First")]);
        t.Capture.State = FakeCaptureService.Recording(0);

        await Task.Run(() => t.Capture.RaiseStepLanded(Shot("s2", caption: "Inserted", extra: ",\"order\":1"), index: 0));
        await TestShell.Settle();

        Assert.Equal(["First", "Inserted"], t.Recording.Steps.Select(s => s.Caption));
    });

    /// <summary>Q-IPC-22, D-HOME-37, EDGE-HOME-30: the count is the list's length, with no singular, and the engine's count does not move it.</summary>
    [Fact]
    public Task TheCountIsTheListLength() => Sta.RunAsync(async () =>
    {
        using var t = new TestShell();
        Assert.Equal("0 steps", t.Recording.CountText);
        t.Capture.State = FakeCaptureService.Recording(0);
        t.Capture.RaiseStepLanded(Shot("s1"), 0);
        await TestShell.Settle();
        Assert.Equal("1 steps", t.Recording.CountText);

        t.Capture.RaiseState(FakeCaptureService.Recording(9));
        await TestShell.Settle();

        Assert.Equal("1 steps", t.Recording.CountText);
    });

    /// <summary>
    /// 11 T9: Pause and Resume, one button whose text follows the status (D-HOME-38), run off the
    /// UI thread, and the state is read again after each.
    /// </summary>
    [Fact]
    public Task PauseAndResumeRunOffTheUiThread() => Sta.RunAsync(async () =>
    {
        using var t = new TestShell();
        t.Capture.RaiseState(FakeCaptureService.Recording(1));
        await TestShell.Settle();
        Assert.Equal(HomeText.Pause, t.Recording.PauseResumeText);
        t.Capture.OnCall = call =>
        {
            if (call == "pause") t.Capture.State = FakeCaptureService.Paused(1);
            if (call == "resume") t.Capture.State = FakeCaptureService.Recording(1);
        };

        await t.Recording.PauseResumeCommand.ExecuteAsync(null);
        Assert.True(t.Recording.IsPaused);
        Assert.Equal(HomeText.Resume, t.Recording.PauseResumeText);
        Assert.Equal("Paused \u00b7 Demo", t.Recording.Label);

        await t.Recording.PauseResumeCommand.ExecuteAsync(null);
        Assert.False(t.Recording.IsPaused);

        Assert.Equal(["pause", "resume"], t.Capture.Calls);
        Assert.All(t.Capture.Threads, thread => Assert.NotSame(Thread.CurrentThread, thread));
    });

    /// <summary>Stop is awaited, runs once at a time, and the state is read again when it returns.</summary>
    [Fact]
    public Task StopIsAwaited() => Sta.RunAsync(async () =>
    {
        using var t = new TestShell();
        t.Capture.RaiseState(FakeCaptureService.Recording(1));
        await TestShell.Settle();
        var gate = new TaskCompletionSource();
        t.Capture.StopGate = gate.Task;

        var stop = t.Recording.StopCommand.ExecuteAsync(null);
        Assert.False(t.Recording.StopCommand.CanExecute(null));
        t.Capture.State = FakeCaptureService.Idle;
        gate.SetResult();
        await stop;

        Assert.Equal(["stop"], t.Capture.Calls);
        Assert.True(t.Recording.StopCommand.CanExecute(null));
        Assert.Equal("Capturing \u00b7 ", t.Recording.Label);
    });

    /// <summary>A failed Pause, Resume or Stop shows the error notice with the failure's message.</summary>
    [Fact]
    public Task AFailedActionShowsTheNotice() => Sta.RunAsync(async () =>
    {
        using var t = new TestShell();
        t.Capture.RaiseState(FakeCaptureService.Recording(1));
        await TestShell.Settle();
        t.Capture.Fails = new IOException("The device is not ready.");

        await t.Recording.PauseResumeCommand.ExecuteAsync(null);
        Assert.Equal("The device is not ready.", t.Notices.Error?.Text);
        t.Notices.ClearError();
        await t.Recording.StopCommand.ExecuteAsync(null);

        Assert.Equal("The device is not ready.", t.Notices.Error?.Text);
        Assert.False(t.Recording.IsPaused);
    });

    /// <summary>2.6, 7.10: each capture error shows the error notice, <c>Capture error: </c> and the message, raised from the engine's thread.</summary>
    [Fact]
    public Task ACaptureErrorShowsTheNotice() => Sta.RunAsync(async () =>
    {
        using var t = new TestShell();

        await Task.Run(() => t.Capture.RaiseError("Disk full"));
        await TestShell.Settle();
        Assert.Equal("Capture error: Disk full", t.Notices.Error?.Text);

        t.Capture.RaiseError("");
        await TestShell.Settle();
        Assert.Equal("Capture error: ", t.Notices.Error?.Text);
    });

    /// <summary>11 T6: every event reaches the panel on the UI thread, from the engine's.</summary>
    [Fact]
    public Task EventsFromAPoolThreadAreMarshalled() => Sta.RunAsync(async () =>
    {
        using var t = new TestShell();
        var ui = Thread.CurrentThread;
        var threads = new List<Thread>();
        t.Recording.Steps.CollectionChanged += (_, _) => threads.Add(Thread.CurrentThread);
        t.Recording.PropertyChanged += (_, _) => threads.Add(Thread.CurrentThread);
        t.Capture.State = FakeCaptureService.Recording(0);

        await Task.Run(() =>
        {
            t.Capture.RaiseStepLanded(Shot("s1"), 0);
            t.Capture.RaiseStepLanded(Shot("s2"), 1);
        });
        await TestShell.Settle();

        Assert.Equal(2, t.Recording.Steps.Count);
        Assert.NotEmpty(threads);
        Assert.All(threads, thread => Assert.Same(ui, thread));
    });

    /// <summary>11 7.7: a dispose leaves the three events, and a post queued before it changes nothing.</summary>
    [Fact]
    public Task DisposeLeavesTheEngine() => Sta.RunAsync(async () =>
    {
        using var t = new TestShell();
        var before = t.Capture.Subscribers;
        t.Capture.State = FakeCaptureService.Recording(0);
        t.Capture.RaiseStepLanded(Shot("s1"), 0);

        t.Recording.Dispose();
        await TestShell.Settle();

        Assert.Equal(before - 3, t.Capture.Subscribers);
        Assert.Empty(t.Recording.Steps);
        t.Recording.Dispose();
    });

    /// <summary>2.6: recording is danger-tinted with a red dot that pulses; paused is amber with a still dot in the draft colour and Resume.</summary>
    [Fact]
    public Task PausedTurnsThePanelAmberAndStillsTheDot() => Sta.RunAsync(async () =>
    {
        using var t = new TestShell();
        var view = new RecordingPanelView { DataContext = t.Recording };
        var window = TestShell.Host(view);
        window.Show();
        try
        {
            t.Capture.RaiseState(FakeCaptureService.Recording(1));
            await TestShell.Settle();
            var panel = VisualTree.Named<Border>(view, "Panel");
            var dot = VisualTree.Named<Ellipse>(view, "Dot");
            Assert.Same(window.FindResource("Brush.danger-tint"), panel.Background);
            Assert.Same(window.FindResource("Brush.danger-bd"), panel.BorderBrush);
            Assert.Same(window.FindResource("Brush.danger"), dot.Fill);
            Assert.Equal(SystemParameters.ClientAreaAnimation, view.Pulsing);
            Assert.Equal(HomeText.Pause, VisualTree.Named<Button>(view, "PauseResumeButton").Content);

            t.Capture.RaiseState(FakeCaptureService.Paused(1));
            await TestShell.Settle();

            Assert.Same(window.FindResource("Brush.caut-bg"), panel.Background);
            Assert.Same(window.FindResource("Brush.caut-bd"), panel.BorderBrush);
            Assert.Same(window.FindResource("Brush.draft"), dot.Fill);
            Assert.False(view.Pulsing);
            Assert.Equal(1.0, dot.Opacity);
            Assert.Equal(HomeText.Resume, VisualTree.Named<Button>(view, "PauseResumeButton").Content);
            Assert.Same(window.FindResource("Button.Primary"), VisualTree.Named<Button>(view, "StopButton").Style);
        }
        finally
        {
            window.Close();
        }
    });

    /// <summary>2.6: a row shows its order, caption and window title; a step with no window shows no title; the list shows only with a step.</summary>
    [Fact]
    public Task ARowShowsItsOrderCaptionAndWindow() => Sta.RunAsync(async () =>
    {
        using var t = new TestShell();
        var view = new RecordingPanelView { DataContext = t.Recording };
        var window = TestShell.Host(view);
        window.Show();
        try
        {
            await TestShell.Settle();
            Assert.Equal(Visibility.Collapsed, VisualTree.Named<ScrollViewer>(view, "StepsScroller").Visibility);
            Assert.Equal(HomeText.RecordingHint, VisualTree.Named<TextBlock>(view, "Hint").Text);

            t.Recording.Seed([Shot("s1", caption: "Click Save", extra: ",\"order\":1,\"window\":{\"title\":\"notes.txt - Notepad\"}"), Text("t1")]);
            await TestShell.Settle();

            Assert.Equal(Visibility.Visible, VisualTree.Named<ScrollViewer>(view, "StepsScroller").Visibility);
            var rows = VisualTree.Named<ItemsControl>(view, "Steps");
            var first = (ContentPresenter)rows.ItemContainerGenerator.ContainerFromIndex(0);
            var second = (ContentPresenter)rows.ItemContainerGenerator.ContainerFromIndex(1);
            Assert.Equal(("1", "Click Save", "notes.txt - Notepad"), Texts(first));
            Assert.Equal(("", "", ""), Texts(second));
            Assert.Equal("2 steps", VisualTree.Named<TextBlock>(view, "Count").Text);
        }
        finally
        {
            window.Close();
        }
    });

    /// <summary>
    /// The PLAN's demo, Q-SHELL-18: steps that land while the main window is hidden are listed
    /// when a second launch shows it, and the panel's Stop ends the session.
    /// </summary>
    [Fact]
    public Task AWindowShownMidSessionListsTheStepsSoFar() => Sta.RunAsync(async () =>
    {
        using var t = new TestShell();
        t.Projects.CanOpen(Recorded, Of("A", Shot("s1")));
        var view = new ShellView { DataContext = t.Shell };
        var window = TestShell.Host(view);
        window.Show();
        try
        {
            t.Shell.Start();
            await t.Shell.RecordAsync(Recorded, new CaptureTarget("auto"), createdThisSession: false);
            window.Hide();
            await Task.Run(() =>
            {
                t.Capture.RaiseStepLanded(Shot("s2", caption: "Two"), 1);
                t.Capture.RaiseStepLanded(Shot("s3", caption: "Three"), 2);
            });
            await TestShell.Settle();

            window.Show();
            await TestShell.Settle();

            Assert.Equal(Visibility.Visible, view.RecordingHost.Visibility);
            Assert.Equal(3, VisualTree.Named<ItemsControl>(view.Panel, "Steps").Items.Count);
            Assert.Equal("3 steps", VisualTree.Named<TextBlock>(view.Panel, "Count").Text);
            t.Capture.OnCall = call =>
            {
                if (call == "stop") t.Capture.RaiseEnded();
            };
            await t.Recording.StopCommand.ExecuteAsync(null);
            Assert.True(await TestShell.UntilAsync(() => t.Shell.CurrentView == ShellViewKind.Project));
        }
        finally
        {
            window.Close();
        }
    });

    private static (string Order, string Caption, string Window) Texts(ContentPresenter row) =>
        (VisualTree.Named<TextBlock>(row, "Order").Text ?? "", VisualTree.Named<TextBlock>(row, "Caption").Text ?? "", VisualTree.Named<TextBlock>(row, "Window").Text ?? "");
}
