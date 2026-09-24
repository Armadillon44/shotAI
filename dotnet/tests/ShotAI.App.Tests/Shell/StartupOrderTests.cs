using System.Diagnostics;
using System.Security.Cryptography;
using System.Security.Principal;
using Microsoft.Extensions.Logging;
using ShotAI.App.Shell;
using ShotAI.App.Tests.Support;
using ShotAI.Core.Shell;
using ShotAI.Platform.Shell;
using Xunit;

namespace ShotAI.App.Tests.Shell;

/// <summary>
/// Spec 03 8.3 (INV-SHELL-5, EDGE-SHELL-45): the real exe, launched while this test holds the
/// user's lock and listens as the running instance would.
/// </summary>
[Collection(AppProcessCollection.Name)]
public sealed class StartupOrderTests
{
    /// <summary>
    /// The launch signals the running instance and exits 0 before it touches settings or shows a
    /// window: its run logs the lock line once, sends the signal, and has neither the runtime line
    /// (logged once the main window is shown) nor the first-render line.
    /// </summary>
    [Fact]
    public Task SecondInstanceCreatesNoWindow() => Sta.RunAsync(async () =>
    {
        string sid;
        using (var identity = WindowsIdentity.GetCurrent()) sid = identity.User!.Value;
        using var held = SingleInstanceLock.TryAcquire(SingleInstanceIdentity.MutexName(sid));
        Assert.True(held is not null, "a running shotAI holds the lock; close it to run this test");
        var signalled = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var listener = new ActivationListener(() => signalled.TrySetResult(), new Logger<ActivationListener>(new CapturingLoggerProvider()));
        listener.Start(sid);

        var settings = Hash(AppProcess.Settings);
        var start = AppProcess.LogLength();
        using var temp = new TempDir();
        using var launch = Process.Start(AppProcess.StartInfo([], temp.Root, ("SHOTAI_LOG_LEVEL", "debug")))!;
        Assert.Equal(0, await AppProcess.WaitForExitAsync(launch));
        await signalled.Task.WaitAsync(AppProcess.Bound, TestContext.Current.CancellationToken);

        var lines = AppProcess.LogFrom(start);
        var run = lines.Skip(lines.FindLastIndex(l => l.Contains("shotAI starting", StringComparison.Ordinal))).ToList();
        Assert.Single(run, l => l.EndsWith("] [info]  (main)     another instance already holds the lock \u2014 exiting.", StringComparison.Ordinal));
        Assert.Contains(run, l => l.EndsWith("] [debug] (main)     second instance: activation signal sent", StringComparison.Ordinal));
        Assert.DoesNotContain(run, l => l.Contains("runtime: ", StringComparison.Ordinal));
        Assert.DoesNotContain(run, l => l.Contains("startup: main window rendered", StringComparison.Ordinal));
        Assert.EndsWith("] [info]  (main)     exiting (code 0)", run[^1], StringComparison.Ordinal);
        Assert.Equal(settings, Hash(AppProcess.Settings));
        Assert.Empty(Directory.GetFileSystemEntries(temp.Root));
    });

    private static string? Hash(string file) => File.Exists(file) ? Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(file))) : null;
}
