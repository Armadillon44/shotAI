using System.Diagnostics;
using ShotAI.App.Tests.Support;
using Xunit;

namespace ShotAI.App.Tests.Shell;

/// <summary>
/// AC-SHELL-4, automated with two real instances: whether Windows then gives the first one the
/// foreground depends on the desktop, which the manual check covers.
/// </summary>
[Collection(AppProcessCollection.Name)]
public sealed class SecondLaunchTests
{
    /// <summary>
    /// With shotAI running and minimized, a second launch exits 0 within the bound and leaves one
    /// process, the first window is restored, and the lock line is logged once.
    /// </summary>
    [Fact]
    public async Task SurfacesTheMinimizedFirstInstanceAndExits()
    {
        using var temp = new TempDir();
        using var first = Process.Start(AppProcess.StartInfo([], temp.Root))!;
        try
        {
            var window = await AppProcess.MainWindowAsync(first);
            User32.ShowWindow(window, User32.SwMinimize);
            Assert.True(await EventuallyAsync(() => User32.IsIconic(window)), "the window did not minimize");

            var start = AppProcess.LogLength();
            var clock = Stopwatch.StartNew();
            using var second = Process.Start(AppProcess.StartInfo([], temp.Root, ("SHOTAI_LOG_LEVEL", "debug")))!;
            Assert.Equal(0, await AppProcess.WaitForExitAsync(second));
            var exited = clock.Elapsed;
            Assert.True(await EventuallyAsync(() => !User32.IsIconic(window) && User32.IsWindowVisible(window)), "the first window was not restored");
            Assert.False(first.HasExited);

            var lines = AppProcess.LogFrom(start);
            Assert.Single(lines, l => l.EndsWith("] [info]  (main)     another instance already holds the lock \u2014 exiting.", StringComparison.Ordinal));
            Assert.Contains(lines, l => l.EndsWith("] [debug] (main)     second instance: activation signal sent", StringComparison.Ordinal));
            TestContext.Current.SendDiagnosticMessage($"the second launch exited after {exited.TotalMilliseconds:F0} ms");

            User32.Close(window);
            Assert.Equal(0, await AppProcess.WaitForExitAsync(first));
        }
        finally
        {
            if (!first.HasExited) first.Kill(entireProcessTree: true);
        }
    }

    private static async Task<bool> EventuallyAsync(Func<bool> condition)
    {
        var clock = Stopwatch.StartNew();
        while (clock.Elapsed < TimeSpan.FromSeconds(30))
        {
            if (condition()) return true;
            await Task.Delay(100, TestContext.Current.CancellationToken);
        }
        return condition();
    }
}
