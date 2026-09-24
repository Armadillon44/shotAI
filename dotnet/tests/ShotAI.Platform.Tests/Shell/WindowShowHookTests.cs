using ShotAI.Platform.Shell;
using ShotAI.Platform.Tests.Support;
using Xunit;

namespace ShotAI.Platform.Tests.Shell;

/// <summary>
/// Spec 03 Q-SHELL-3, the catch-all: each top-level window of the thread is reported before it
/// is visible, every time it is shown, and again as it is destroyed.
/// </summary>
public sealed class WindowShowHookTests
{
    [Fact]
    public void ReportsATopLevelWindowBeforeItIsVisible()
    {
        var reports = new List<(nint Hwnd, bool Visible)>();
        using var hook = WindowShowHook.Install(h => reports.Add((h, User32.IsWindowVisible(h))), _ => { });
        using var w = TestWindow.Popup();
        Assert.Empty(reports);
        User32.ShowWindow(w.Handle, User32.SwShowNoActivate);
        Assert.True(User32.IsWindowVisible(w.Handle));
        Assert.NotEmpty(reports);
        Assert.All(reports, r => Assert.Equal((w.Handle, false), r));
    }

    /// <summary>A window hidden and shown again is reported again, so a registration dropped in between is restored.</summary>
    [Fact]
    public void ReportsEveryShow()
    {
        var shown = 0;
        using var hook = WindowShowHook.Install(_ => shown++, _ => { });
        using var w = TestWindow.Popup();
        User32.ShowWindow(w.Handle, User32.SwShowNoActivate);
        var first = shown;
        User32.ShowWindow(w.Handle, User32.SwHide);
        Assert.Equal(first, shown);
        User32.ShowWindow(w.Handle, User32.SwShowNoActivate);
        Assert.True(first > 0 && shown > first, $"{first} then {shown}");
    }

    /// <summary>A child lives inside its parent and has no display affinity of its own.</summary>
    [Fact]
    public void IgnoresChildWindows()
    {
        var reports = new List<nint>();
        using var hook = WindowShowHook.Install(reports.Add, _ => { });
        using var parent = TestWindow.Popup();
        using var child = TestWindow.Child(parent);
        User32.ShowWindow(child.Handle, User32.SwShowNoActivate);
        Assert.Empty(reports);
        User32.ShowWindow(parent.Handle, User32.SwShowNoActivate);
        Assert.Contains(parent.Handle, reports);
        Assert.DoesNotContain(child.Handle, reports);
    }

    [Fact]
    public void ReportsDestroyedWindows()
    {
        var destroyed = new List<nint>();
        using var hook = WindowShowHook.Install(_ => { }, destroyed.Add);
        using var parent = TestWindow.Popup();
        using var child = TestWindow.Child(parent);
        parent.Destroy();
        Assert.Contains(parent.Handle, destroyed);
        Assert.Contains(child.Handle, destroyed);
    }

    [Fact]
    public void DisposeRemovesTheHook()
    {
        var shown = 0;
        var hook = WindowShowHook.Install(_ => shown++, _ => { });
        hook.Dispose();
        hook.Dispose();
        using var w = TestWindow.Popup();
        User32.ShowWindow(w.Handle, User32.SwShowNoActivate);
        Assert.Equal(0, shown);
    }

    /// <summary>The hook is the installing thread's: a window another thread shows is not reported.</summary>
    [Fact]
    public void OnlyTheInstallingThreadIsSeen()
    {
        var shown = 0;
        using var hook = WindowShowHook.Install(_ => shown++, _ => { });
        var thread = new Thread(() =>
        {
            using var w = TestWindow.Popup();
            User32.ShowWindow(w.Handle, User32.SwShowNoActivate);
        });
        thread.Start();
        Assert.True(thread.Join(TimeSpan.FromSeconds(30)));
        Assert.Equal(0, shown);
    }

    /// <summary>An exception must not leave the hook: the show goes on and the process with it.</summary>
    [Fact]
    public void AThrowingReportDoesNotStopTheShow()
    {
        using var hook = WindowShowHook.Install(_ => throw new InvalidOperationException("report"), _ => throw new InvalidOperationException("report"));
        var w = TestWindow.Popup();
        User32.ShowWindow(w.Handle, User32.SwShowNoActivate);
        Assert.True(User32.IsWindowVisible(w.Handle));
        w.Dispose();
    }

    [Fact]
    public void CallbacksAreRequired()
    {
        Assert.Throws<ArgumentNullException>("showing", () => WindowShowHook.Install(null!, _ => { }));
        Assert.Throws<ArgumentNullException>("destroyed", () => WindowShowHook.Install(_ => { }, null!));
    }
}
