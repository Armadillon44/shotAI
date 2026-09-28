using System.ComponentModel;
using System.Windows.Threading;
using Microsoft.Extensions.Logging.Abstractions;
using ShotAI.App.Chrome;
using ShotAI.App.Home;
using ShotAI.App.Shell;
using ShotAI.App.Tests.Support;
using ShotAI.App.Threading;
using ShotAI.Core.Capture;
using ShotAI.Core.Shell;
using Xunit;
using static ShotAI.App.Tests.Support.Manifests;

namespace ShotAI.App.Tests.Threading;

/// <summary>
/// Spec 11 8.2, INV-IPC-5, AC-IPC-5, Q-IPC-20: the engine raises <c>StepLanded</c> then
/// <c>StateChanged</c> for each step from its own thread, and two UI subscribers, the recording
/// panel and the pill, see them in raise order: every step once and in order, and a state read
/// after the last raise. A state post that is still queued takes the place of a later one, so
/// with the UI thread held the panel sees <c>[S, T, S]</c>, the one state reading both steps.
/// </summary>
public sealed class EventOrderTests
{
    /// <summary>With the UI thread free between raises, each subscriber sees each event in raise order.</summary>
    [Fact]
    public Task DrainedAfterEachRaiseEveryEventIsSeenInOrder() => Sta.RunAsync(async () =>
    {
        using var s = new Subscribers();

        for (var i = 1; i <= 2; i++)
        {
            var n = i;
            await Task.Run(() => s.Capture.RaiseStepLanded(Shot($"s{n}"), n - 1));
            await TestShell.Settle();
        }

        Assert.Equal(["S1", "T1", "S2", "T2"], s.Panel);
        Assert.Equal([ShellStrings.PillActiveLabel(false, 1), ShellStrings.PillActiveLabel(false, 2)], s.Pill);
    });

    /// <summary>
    /// With the UI thread held while both steps land, the steps are applied in order and each
    /// subscriber queues one state, which reads the last count; nothing is lost.
    /// </summary>
    [Fact]
    public Task HeldWhileTheyAreRaisedTheStateIsCoalescedAndEveryStepKept() => Sta.RunAsync(async () =>
    {
        using var s = new Subscribers();
        using var held = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();
        s.Ui.Post(() =>
        {
            held.Set();
            release.Wait(Sta.Timeout);
        });

        var raising = Task.Run(() =>
        {
            held.Wait(Sta.Timeout);
            s.Capture.RaiseStepLanded(Shot("s1"), 0);
            s.Capture.RaiseStepLanded(Shot("s2"), 1);
            release.Set();
        });
        await raising;
        await TestShell.Settle();

        Assert.Equal(["S1", "T2", "S2"], s.Panel);
        Assert.Equal([ShellStrings.PillActiveLabel(false, 2)], s.Pill);

        // The queue is free again: the next step's state posts anew.
        await Task.Run(() => s.Capture.RaiseStepLanded(Shot("s3"), 2));
        await TestShell.Settle();
        Assert.Equal(["S1", "T2", "S2", "S3", "T3"], s.Panel);
    });

    /// <summary>The panel and the pill's controller over one engine, with what each saw after the start.</summary>
    private sealed class Subscribers : IDisposable
    {
        private readonly RecordingPanelViewModel _panel;
        private readonly CapturePillViewModel _pill;
        private readonly RecordingVisibilityController _controller;

        public Subscribers()
        {
            Ui = new WpfUiDispatcher(Dispatcher.CurrentDispatcher);
            Capture.State = FakeCaptureService.Recording(0);
            _panel = new RecordingPanelViewModel(Capture, Ui, new NoticeCenter(NullLogger<NoticeCenter>.Instance));
            _pill = new CapturePillViewModel(Capture, Ui, NullLogger<CapturePillViewModel>.Instance);
            _controller = new RecordingVisibilityController(Capture, Ui, _pill);
            _controller.Start();
            _panel.Steps.CollectionChanged += (_, _) => Panel.Add($"S{_panel.Steps.Count}");
            _panel.PropertyChanged += OnPanelChanged;
            _pill.PropertyChanged += (_, e) =>
            {
                if (e.PropertyName == nameof(CapturePillViewModel.View)) Pill.Add(_pill.View.Label);
            };
        }

        public FakeCaptureService Capture { get; } = new();

        public WpfUiDispatcher Ui { get; }

        /// <summary><c>S</c> and the list's length for each step listed, <c>T</c> and the count read for each state applied.</summary>
        public List<string> Panel { get; } = [];

        /// <summary>The pill's label for each state it was given.</summary>
        public List<string> Pill { get; } = [];

        public void Dispose()
        {
            _controller.Dispose();
            _panel.Dispose();
        }

        // One entry per applied state: Label is raised once for each.
        private void OnPanelChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(RecordingPanelViewModel.Label)) Panel.Add($"T{Capture.GetState().StepCount}");
        }
    }
}
