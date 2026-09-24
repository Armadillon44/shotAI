using System.Runtime.ExceptionServices;
using ShotAI.Core.Shell;
using ShotAI.Platform.Shell;
using ShotAI.Platform.Tests.Support;
using Xunit;

namespace ShotAI.Platform.Tests.Shell;

/// <summary>
/// Spec 03 8.3 (INV-SHELL-5, D20): the lock and the second launch's signal. Each test uses a
/// mutex or window name of its own, never the app's, so a shotAI running on the machine or a
/// parallel test cannot interfere.
/// </summary>
public sealed class SingleInstanceTests
{
    /// <summary>Another process holds the lock, so this one cannot take it; once released, it can.</summary>
    [Fact]
    public async Task SecondAcquireFails()
    {
        var name = NewMutexName();
        using var child = await ChildMutexHolder.StartAsync(name);
        Assert.Null(SingleInstanceLock.TryAcquire(name));
        await child.ReleaseAsync();
        AcquireAndRelease(name);
    }

    /// <summary>
    /// Released on dispose: another thread can take it afterwards. (The thread that took a mutex
    /// could take it again while still owning it, so the second take is on another thread.)
    /// </summary>
    [Fact]
    public void ReleasedOnDispose()
    {
        var name = NewMutexName();
        OnThread(() => AcquireAndRelease(name));
        OnThread(() => AcquireAndRelease(name));
    }

    /// <summary>Held until disposed: while one thread holds it, another cannot take it (EDGE-SHELL-36's reason to keep it).</summary>
    [Fact]
    public void HeldUntilDisposed()
    {
        var name = NewMutexName();
        using var taken = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();
        var holder = new Thread(() =>
        {
            using var held = SingleInstanceLock.TryAcquire(name);
            taken.Set();
            release.Wait(TimeSpan.FromSeconds(30));
        });
        holder.Start();
        try
        {
            Assert.True(taken.Wait(TimeSpan.FromSeconds(30), TestContext.Current.CancellationToken));
            OnThread(() => Assert.Null(SingleInstanceLock.TryAcquire(name)));
        }
        finally
        {
            release.Set();
            holder.Join(TimeSpan.FromSeconds(30));
        }
        OnThread(() => AcquireAndRelease(name));
    }

    /// <summary>A holder that ended without releasing (a crash) leaves the lock abandoned, which counts as taking it.</summary>
    [Fact]
    public async Task AbandonedCountsAsAcquired()
    {
        var name = NewMutexName();
        using var child = await ChildMutexHolder.StartAsync(name);
        await child.AbandonAsync();
        AcquireAndRelease(name);
    }

    [Fact]
    public void DisposeTwiceIsHarmless()
    {
        var held = SingleInstanceLock.TryAcquire(NewMutexName());
        Assert.NotNull(held);
        held.Dispose();
        held.Dispose();
    }

    [Fact]
    public void EmptyNameIsRefused()
    {
        Assert.Throws<ArgumentNullException>("mutexName", () => SingleInstanceLock.TryAcquire(null!));
        Assert.Throws<ArgumentException>("mutexName", () => SingleInstanceLock.TryAcquire(""));
    }

    /// <summary>
    /// The running instance's message-only window, found by name, receives the registered
    /// activation message.
    /// </summary>
    [Fact]
    public void SecondLaunchSignalsFirst()
    {
        var name = NewWindowName();
        using var first = TestWindow.MessageOnly(name);
        Assert.True(ExistingInstance.Activate(name, SingleInstanceIdentity.ActivationMessageName));
        var id = User32.RegisterWindowMessage(SingleInstanceIdentity.ActivationMessageName);
        Assert.True(User32.PeekMessage(out var message, first.Handle, id, id, User32.PmRemove));
        Assert.Equal(first.Handle, message.Hwnd);
        Assert.Equal(id, message.Message);
    }

    [Fact]
    public void NoRunningWindowReturnsFalse() =>
        Assert.False(ExistingInstance.Activate(NewWindowName(), SingleInstanceIdentity.ActivationMessageName));

    /// <summary>Only message-only windows are searched, so an ordinary window with the name is not the running instance.</summary>
    [Fact]
    public void AnOrdinaryWindowWithTheNameIsNotFound()
    {
        var name = NewWindowName();
        using var ordinary = TestWindow.Popup(name: name);
        Assert.False(ExistingInstance.Activate(name, SingleInstanceIdentity.ActivationMessageName));
    }

    [Fact]
    public void ActivateRefusesEmptyNames()
    {
        Assert.Throws<ArgumentException>("windowName", () => ExistingInstance.Activate("", SingleInstanceIdentity.ActivationMessageName));
        Assert.Throws<ArgumentException>("messageName", () => ExistingInstance.Activate(NewWindowName(), ""));
    }

    /// <summary>The id every process of the session gets for the name.</summary>
    [Fact]
    public void RegisteredMessageIsTheSessionsId()
    {
        Assert.Equal(User32.RegisterWindowMessage(SingleInstanceIdentity.ActivationMessageName), WindowMessages.Register(SingleInstanceIdentity.ActivationMessageName));
        Assert.Throws<ArgumentException>("name", () => WindowMessages.Register(""));
    }

    private static string NewMutexName() => "Local\\shotAI.test." + Guid.NewGuid().ToString("N");

    private static string NewWindowName() => "shotAI.Activation.test-" + Guid.NewGuid().ToString("N");

    // Takes and releases on the calling thread, with no await between.
    private static void AcquireAndRelease(string name)
    {
        using var held = SingleInstanceLock.TryAcquire(name);
        Assert.NotNull(held);
    }

    private static void OnThread(Action body)
    {
        Exception? error = null;
        var thread = new Thread(() =>
        {
            try
            {
                body();
            }
            catch (Exception e)
            {
                error = e;
            }
        });
        thread.Start();
        Assert.True(thread.Join(TimeSpan.FromSeconds(30)), "the thread did not finish");
        if (error is not null) ExceptionDispatchInfo.Throw(error);
    }
}
