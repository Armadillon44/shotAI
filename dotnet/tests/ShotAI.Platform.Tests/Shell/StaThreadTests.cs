using ShotAI.Platform.Shell;
using Xunit;

namespace ShotAI.Platform.Tests.Shell;

/// <summary>Spec 11 7.3.3: each shell call gets a new background STA thread, and its task carries the call's outcome.</summary>
public sealed class StaThreadTests
{
    [Fact]
    public async Task EachCallRunsOnANewBackgroundStaThread()
    {
        var seen = new List<Thread>();
        for (var i = 0; i < 2; i++)
        {
            await StaThread.RunAsync(() =>
            {
                var t = Thread.CurrentThread;
                Assert.Equal((ApartmentState.STA, true, "shotAI shell"), (t.GetApartmentState(), t.IsBackground, t.Name));
                lock (seen) seen.Add(t);
            });
        }
        Assert.Equal(2, seen.Count);
        Assert.NotSame(seen[0], seen[1]);
        Assert.DoesNotContain(Thread.CurrentThread, seen);
    }

    [Fact]
    public async Task WhatTheCallThrowsFaultsTheTask()
    {
        var failure = new IOException("The network path was not found.");
        var e = await Assert.ThrowsAsync<IOException>(() => StaThread.RunAsync(() => throw failure));
        Assert.Same(failure, e);
    }

    /// <summary>The task's continuation does not run on the STA thread, which ends when the call returns.</summary>
    [Fact]
    public async Task TheContinuationLeavesTheStaThread()
    {
        Thread? sta = null;
        await StaThread.RunAsync(() => sta = Thread.CurrentThread);
        Assert.NotNull(sta);
        Assert.NotSame(sta, Thread.CurrentThread);
    }

    [Fact]
    public async Task ArgumentsAreChecked() => await Assert.ThrowsAsync<ArgumentNullException>(() => StaThread.RunAsync(null!));
}
