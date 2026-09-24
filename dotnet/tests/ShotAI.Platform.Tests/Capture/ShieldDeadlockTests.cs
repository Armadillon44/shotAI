using System.Diagnostics;
using ShotAI.Core.Capture;
using ShotAI.Platform.Capture;
using ShotAI.Platform.Tests.Support;
using Xunit;

namespace ShotAI.Platform.Tests.Capture;

/// <summary>
/// Spec 02 7.8's deadlock rule (DL1, INV-CAP-29, Q-CAP-22) with the real registry, protection
/// and shield: grabs on workers take the shield while a UI thread owns the windows, registers new
/// ones and changes the setting the way the app does, which never waits on the shield's lock.
/// </summary>
[Collection(ScreenPixelsCollection.Name)]
public sealed class ShieldDeadlockTests
{
    private static readonly TimeSpan Bound = TimeSpan.FromSeconds(2);

    /// <summary>
    /// While a worker holds a shield, and then while workers take and release it without pause
    /// (each a Win32 call per window under the lock), the UI thread registers windows and toggles
    /// remote visibility through the pool; its work finishes within 2 s each time, and once the
    /// grabs end every window has the affinity the last setting says.
    /// </summary>
    [Fact]
    public async Task RegisterAndToggleWhileShieldHeldCompletes()
    {
        var registry = new OwnWindowRegistry(new ListLogger<OwnWindowRegistry>());
        var protection = new DisplayAffinityProtection(registry, new ListLogger<DisplayAffinityProtection>());
        var settings = new FixedCaptureSettings(remoteVisible: false);
        var shield = new CaptureShield(protection, settings);
        using var ui = new UiThread();
        var windows = new List<TestWindow>();
        try
        {
            // A worker holds a shield throughout.
            var held = await Task.Run(shield.Take, TestContext.Current.CancellationToken);
            await UiWorkAsync(ui, registry, shield, settings, windows, rounds: 10);
            held.Dispose();

            // Workers take and release it as fast as they can.
            using var stop = new CancellationTokenSource();
            var churn = Enumerable.Range(0, 3).Select(_ => Task.Run(() =>
            {
                while (!stop.IsCancellationRequested)
                {
                    using (shield.Take()) Thread.SpinWait(200);
                }
            })).ToArray();
            await UiWorkAsync(ui, registry, shield, settings, windows, rounds: 10);
            await stop.CancelAsync();
            await Task.WhenAll(churn).WaitAsync(Bound, TestContext.Current.CancellationToken);

            Assert.Equal(0, shield.ExcludedForNewWindow ? 1 : 0); // the last toggle was visible, and no grab holds the shield
            var deadline = DateTime.UtcNow + Bound;
            while (windows.Any(w => User32.Affinity(w.Handle) != User32.WdaNone) && DateTime.UtcNow < deadline)
                await Task.Delay(20, TestContext.Current.CancellationToken);
            Assert.All(windows, w => Assert.Equal(User32.WdaNone, User32.Affinity(w.Handle)));
        }
        finally
        {
            ui.Invoke(() => windows.ForEach(w => w.Dispose()));
        }
    }

    // Rounds of the UI thread's work: a window registered on it, then the setting toggled as the
    // remote-visibility applier toggles it, on the pool. The round ends visible.
    private static async Task UiWorkAsync(UiThread ui, OwnWindowRegistry registry, CaptureShield shield, FixedCaptureSettings settings, List<TestWindow> windows, int rounds)
    {
        var watch = Stopwatch.StartNew();
        for (var i = 0; i < rounds; i++)
        {
            ui.Invoke(() =>
            {
                var w = TestWindow.Popup();
                windows.Add(w);
                registry.Register(w.Handle);
            });
            var visible = i % 2 == 1;
            var apply = ui.Invoke(() =>
            {
                settings.RemoteVisible = visible;
                return Task.Run(() => shield.ApplyRemoteVisibility(visible));
            });
            await apply.WaitAsync(Bound, TestContext.Current.CancellationToken);
        }
        Assert.True(watch.Elapsed < Bound, $"the UI thread's work took {watch.Elapsed}");
    }
}
