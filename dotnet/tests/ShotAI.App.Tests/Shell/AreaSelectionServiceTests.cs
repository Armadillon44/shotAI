using System.ComponentModel;
using System.Windows;
using System.Windows.Input;
using Microsoft.Extensions.Logging.Abstractions;
using ShotAI.App.Shell;
using ShotAI.App.Tests.Support;
using ShotAI.Platform.Shell;
using Xunit;
using Rect = ShotAI.Core.Model.Rect;

namespace ShotAI.App.Tests.Shell;

/// <summary>
/// Spec 03 8.3: a selection ends exactly once (INV-SHELL-14), only from its own overlays or its
/// own cancellation (INV-SHELL-15, INV-IPC-15), and brings the requester back however it ends
/// (INV-SHELL-9, INV-IPC-16), unless a newer selection for it is open (D23). The overlays stand
/// in for monitors off every real screen, so nothing here needs real input.
/// </summary>
public sealed class AreaSelectionServiceTests
{
    private static readonly Rect Sample = new(10, 20, 300, 200);

    [Fact]
    public Task NewSelectionResolvesPreviousNull() => WithHarnessAsync(2, async h =>
    {
        var first = await h.OpenAsync();
        var firstOverlays = Handles(h);
        var second = await h.OpenAsync();
        Assert.Null(await AreaSelectionHarness.ResultAsync(first));
        Assert.All(firstOverlays, hwnd => Assert.False(User32.IsWindow(hwnd)));
        Assert.False(second.IsCompleted);
        Assert.Equal(2, h.Service.PendingOverlays.Count);
        Assert.DoesNotContain(Handles(h), firstOverlays.Contains);
        h.Service.Finish(h.Service.PendingGeneration, Sample);
        Assert.Equal(Sample, await AreaSelectionHarness.ResultAsync(second));
    });

    /// <summary>The first selection's overlay, closed by the second, reports a drag; its generation reports a rectangle: both are ignored.</summary>
    [Fact]
    public Task StaleOverlayCannotResolve() => WithHarnessAsync(1, async h =>
    {
        var first = await h.OpenAsync();
        var stale = h.Service.PendingOverlays[0];
        var staleGeneration = h.Service.PendingGeneration;
        var second = await h.OpenAsync();
        Assert.Null(await AreaSelectionHarness.ResultAsync(first));
        stale.Press(MouseButton.Left, new Point(10, 10));
        stale.Move(new Point(200, 120));
        stale.Release();
        h.Service.Finish(staleGeneration, Sample);
        await TestShell.Settle();
        Assert.False(second.IsCompleted);
        Assert.NotEqual(staleGeneration, h.Service.PendingGeneration);
        Assert.DoesNotContain(h.Lines, l => l.StartsWith("region selected:", StringComparison.Ordinal));
        h.Service.Finish(h.Service.PendingGeneration, Sample);
        Assert.Equal(Sample, await AreaSelectionHarness.ResultAsync(second));
    });

    /// <summary>Electron's "dismissed out from under us": one overlay closed, as by Alt+F4, ends the selection and closes the others.</summary>
    [Fact]
    public Task OverlayClosedResolvesNull() => WithHarnessAsync(2, async h =>
    {
        var selection = await h.OpenAsync();
        var handles = Handles(h);
        User32.Close(handles[1]);
        Assert.Null(await AreaSelectionHarness.ResultAsync(selection));
        Assert.All(handles, hwnd => Assert.False(User32.IsWindow(hwnd)));
        Assert.Empty(h.Service.PendingOverlays);
        Assert.Equal(["region: overlay opened across 2 display(s)", "region: selection cancelled"], h.Lines);
    });

    /// <summary>Spec 11 K5: a token cancelled on another thread ends the selection on the UI thread.</summary>
    [Fact]
    public Task CancellationResolvesNull() => WithHarnessAsync(2, async h =>
    {
        using var cts = new CancellationTokenSource();
        var selection = await h.OpenAsync(cts.Token);
        var handles = Handles(h);
        await Task.Run(cts.Cancel, TestContext.Current.CancellationToken);
        Assert.Null(await AreaSelectionHarness.ResultAsync(selection));
        Assert.All(handles, hwnd => Assert.False(User32.IsWindow(hwnd)));
        Assert.True(h.Requester.IsVisible);
    });

    /// <summary>INV-SHELL-9: back after a cancel, after the monitors could not be read, and after an overlay could not be made.</summary>
    [Fact]
    public Task RestoresMainWindowOnCancelAndError() => WithHarnessAsync(1, async h =>
    {
        var selection = await h.OpenAsync();
        Assert.False(h.Requester.IsVisible);
        h.Service.Finish(h.Service.PendingGeneration, null);
        Assert.Null(await AreaSelectionHarness.ResultAsync(selection));
        Assert.True(h.Requester.IsVisible);

        h.MonitorsFail = new Win32Exception(5);
        await Assert.ThrowsAsync<Win32Exception>(() => h.SelectAsync(TestContext.Current.CancellationToken));
        Assert.True(h.Requester.IsVisible);
        Assert.Empty(h.Service.PendingOverlays);

        // The first overlay opened, the second could not be made: the first closes again.
        h.MonitorsFail = null;
        h.Monitors = [AreaSelectionHarness.OffScreen(0), null!];
        await Assert.ThrowsAsync<ArgumentNullException>(() => h.SelectAsync(TestContext.Current.CancellationToken));
        await TestShell.Settle();
        Assert.True(h.Requester.IsVisible);
        Assert.Empty(h.Service.PendingOverlays);
        Assert.Equal<nint>([h.RequesterHandle], User32.VisibleThreadWindows());
    });

    /// <summary>EDGE-SHELL-50, D22: with no monitor the selection ends at once instead of waiting for ever.</summary>
    [Fact]
    public Task EmptyMonitorListResolvesNull() => WithHarnessAsync(0, async h =>
    {
        Assert.Null(await AreaSelectionHarness.ResultAsync(h.SelectAsync()));
        Assert.True(h.Requester.IsVisible);
        Assert.Equal(["region: overlay opened across 0 display(s)", "region: selection cancelled"], h.Lines);
    });

    /// <summary>EDGE-SHELL-47, D23: the first caller's restore runs after the second selection opened, and leaves the requester hidden.</summary>
    [Fact]
    public Task SecondSelectionKeepsRequesterHidden() => WithHarnessAsync(1, async h =>
    {
        var first = await h.OpenAsync();
        var second = await h.OpenAsync();
        Assert.Null(await AreaSelectionHarness.ResultAsync(first));
        await TestShell.Settle();
        Assert.False(h.Requester.IsVisible);
        h.Service.Finish(h.Service.PendingGeneration, Sample);
        Assert.Equal(Sample, await AreaSelectionHarness.ResultAsync(second));
        Assert.True(h.Requester.IsVisible);
    });

    /// <summary>A newer selection for another window leaves the first window hidden for nobody: it comes back.</summary>
    [Fact]
    public Task AnotherRequestersSelectionBringsTheFirstBack() => WithHarnessAsync(1, async h =>
    {
        var other = new ProbeWindow(h.Registration) { Left = -20000, Top = -19000 };
        other.Show();
        try
        {
            var first = await h.OpenAsync();
            var second = h.Service.SelectAreaAsync(other);
            Assert.Null(await AreaSelectionHarness.ResultAsync(first));
            Assert.True(await TestShell.UntilAsync(() => h.Requester.IsVisible, 10), "the first requester stayed hidden");
            Assert.False(other.IsVisible);
            h.Service.Finish(h.Service.PendingGeneration, null);
            Assert.Null(await AreaSelectionHarness.ResultAsync(second));
            Assert.True(other.IsVisible);
        }
        finally
        {
            other.Close();
        }
    });

    [Fact]
    public Task PreCancelledTokenDoesNotHide() => WithHarnessAsync(1, async h =>
    {
        var changes = 0;
        h.Requester.IsVisibleChanged += (_, _) => changes++;
        Assert.Null(await h.SelectAsync(new CancellationToken(canceled: true)));
        Assert.Equal(0, changes);
        Assert.True(h.Requester.IsVisible);
        Assert.Equal(0, h.MonitorReads);
        Assert.Empty(h.Service.PendingOverlays);
        Assert.Empty(h.Lines);
    });

    /// <summary>A token cancelled after its selection ended posts nothing, so it cannot touch a newer selection.</summary>
    [Fact]
    public Task CancellationRegistrationDisposed() => WithHarnessAsync(1, async h =>
    {
        using var cts = new CancellationTokenSource();
        var first = await h.OpenAsync(cts.Token);
        h.Service.Finish(h.Service.PendingGeneration, Sample);
        Assert.Equal(Sample, await AreaSelectionHarness.ResultAsync(first));
        var second = await h.OpenAsync();
        var posts = h.Ui.Posts;
        await cts.CancelAsync();
        await TestShell.Settle();
        Assert.Equal(posts, h.Ui.Posts);
        Assert.False(second.IsCompleted);
        h.Service.Finish(h.Service.PendingGeneration, null);
        Assert.Null(await AreaSelectionHarness.ResultAsync(second));
    });

    /// <summary>A requester that closed during the selection is not shown again.</summary>
    [Fact]
    public Task ARequesterClosedMeanwhileStaysClosed() => WithHarnessAsync(1, async h =>
    {
        var selection = await h.OpenAsync();
        h.Requester.Close();
        h.Service.Finish(h.Service.PendingGeneration, Sample);
        Assert.Equal(Sample, await AreaSelectionHarness.ResultAsync(selection));
        Assert.False(h.Requester.IsVisible);
    });

    /// <summary>A requester closed before the call fails at once, and nothing opens.</summary>
    [Fact]
    public Task AClosedRequesterIsRefused() => WithHarnessAsync(1, async h =>
    {
        h.Requester.Close();
        await Assert.ThrowsAsync<InvalidOperationException>(() => h.SelectAsync(TestContext.Current.CancellationToken));
        Assert.Equal(0, h.MonitorReads);
        Assert.Empty(h.Service.PendingOverlays);
    });

    [Fact]
    public Task OnlyTheUiThreadSelects() => WithHarnessAsync(1, async h =>
    {
        await Assert.ThrowsAsync<InvalidOperationException>(() => Task.Run(() => h.SelectAsync(), TestContext.Current.CancellationToken));
        Assert.Equal(0, h.MonitorReads);
        Assert.True(h.Requester.IsVisible);
    });

    /// <summary>Spec 03 7.9 and 2.12: Electron's three lines, the rectangle in physical pixels, and the cancel line for every null (D24).</summary>
    [Fact]
    public Task TheLinesAreElectrons() => WithHarnessAsync(1, async h =>
    {
        var selected = await h.OpenAsync();
        h.Service.Finish(h.Service.PendingGeneration, new Rect(-2547, 3, 127, 64));
        await AreaSelectionHarness.ResultAsync(selected);
        var small = await h.OpenAsync();
        var overlay = h.Service.PendingOverlays[0];
        overlay.Press(MouseButton.Left, new Point(10, 10));
        overlay.Move(new Point(13, 50));
        overlay.Release();
        Assert.Null(await AreaSelectionHarness.ResultAsync(small));
        Assert.Equal(
            [
                "region: overlay opened across 1 display(s)",
                "region selected: 127x64 @ (-2547,3) [physical px]",
                "region: overlay opened across 1 display(s)",
                "region: selection cancelled",
            ],
            h.Lines);
    });

    [Fact]
    public Task ArgumentsAreChecked() => WithHarnessAsync(1, async h =>
    {
        await Assert.ThrowsAsync<ArgumentNullException>(() => h.Service.SelectAreaAsync(null!, TestContext.Current.CancellationToken));
        var logger = NullLogger<AreaSelectionService>.Instance;
        Assert.Throws<ArgumentNullException>(() => new AreaSelectionService(null!, h.Ui, logger));
        Assert.Throws<ArgumentNullException>(() => new AreaSelectionService(h.Registration, null!, logger));
        Assert.Throws<ArgumentNullException>(() => new AreaSelectionService(h.Registration, h.Ui, null!));
        Assert.Throws<ArgumentNullException>(() => new AreaSelectionService(h.Registration, h.Ui, logger, null!, () => (0, 0)));
        Assert.Throws<ArgumentNullException>(() => new AreaSelectionService(h.Registration, h.Ui, logger, () => [], null!));
    });

    private static List<nint> Handles(AreaSelectionHarness h) => [.. h.Service.PendingOverlays.Select(o => o.Handle)];

    private static Task WithHarnessAsync(int monitors, Func<AreaSelectionHarness, Task> body) => Sta.RunAsync(async () =>
    {
        using var h = new AreaSelectionHarness([.. Enumerable.Range(0, monitors).Select(i => AreaSelectionHarness.OffScreen(i))]);
        await body(h);
    });
}
