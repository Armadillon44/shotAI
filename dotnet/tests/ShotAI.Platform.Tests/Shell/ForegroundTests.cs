using ShotAI.Platform.Shell;
using ShotAI.Platform.Tests.Support;
using Xunit;

namespace ShotAI.Platform.Tests.Shell;

/// <summary>
/// Spec 03 7.3 and 2.2: a minimized window is restored before the foreground is asked for.
/// Whether Windows grants the foreground depends on who has it, so only the restore is asserted.
/// </summary>
public sealed class ForegroundTests
{
    [Fact]
    public void RestoresAMinimizedWindow()
    {
        using var w = TestWindow.Overlapped();
        User32.ShowWindow(w.Handle, User32.SwMinimize);
        Assert.True(User32.IsIconic(w.Handle));
        Foreground.TryActivate(w.Handle);
        Assert.False(User32.IsIconic(w.Handle));
    }
}
