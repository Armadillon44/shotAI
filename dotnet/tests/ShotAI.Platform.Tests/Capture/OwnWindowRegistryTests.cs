using Microsoft.Extensions.Logging;
using ShotAI.Platform.Capture;
using ShotAI.Platform.Tests.Support;
using Xunit;

namespace ShotAI.Platform.Tests.Capture;

/// <summary>
/// Spec 02 7.8 rule 1 and 7.9: the registration surface. A registered window is excluded from
/// capture before it is in the set, and a second registration leaves an exclusion the shield may
/// since have relaxed.
/// </summary>
public sealed class OwnWindowRegistryTests
{
    [Fact]
    public void RegisterExcludesTheWindowFromCapture()
    {
        var log = new ListLogger<OwnWindowRegistry>();
        var registry = new OwnWindowRegistry(log);
        using var w = TestWindow.Popup();
        Assert.Equal(User32.WdaNone, User32.Affinity(w.Handle));
        Assert.True(registry.Register(w.Handle));
        Assert.Equal(User32.WdaExcludeFromCapture, User32.Affinity(w.Handle));
        Assert.True(registry.IsRegistered(w.Handle));
        Assert.Equal([w.Handle], registry.Snapshot());
        Assert.Empty(log.Entries);
    }

    [Fact]
    public void ASecondRegisterLeavesTheWindowAsItIs()
    {
        var registry = new OwnWindowRegistry(new ListLogger<OwnWindowRegistry>());
        using var w = TestWindow.Popup();
        Assert.True(registry.Register(w.Handle));
        Assert.True(CaptureExclusion.Apply(w.Handle, excluded: false));
        Assert.False(registry.Register(w.Handle));
        Assert.Equal(User32.WdaNone, User32.Affinity(w.Handle));
    }

    [Fact]
    public void UnregisterRemoves()
    {
        var registry = new OwnWindowRegistry(new ListLogger<OwnWindowRegistry>());
        using var w = TestWindow.Popup();
        registry.Register(w.Handle);
        Assert.True(registry.Unregister(w.Handle));
        Assert.False(registry.IsRegistered(w.Handle));
        Assert.Empty(registry.Snapshot());
        Assert.False(registry.Unregister(w.Handle));
    }

    /// <summary>A window registered after it was removed is excluded again (a new window may reuse a handle).</summary>
    [Fact]
    public void ReRegisteredAfterRemovalIsExcludedAgain()
    {
        var registry = new OwnWindowRegistry(new ListLogger<OwnWindowRegistry>());
        using var w = TestWindow.Popup();
        registry.Register(w.Handle);
        registry.Unregister(w.Handle);
        CaptureExclusion.Apply(w.Handle, excluded: false);
        Assert.True(registry.Register(w.Handle));
        Assert.Equal(User32.WdaExcludeFromCapture, User32.Affinity(w.Handle));
    }

    /// <summary>
    /// Windows refuses the exclusion for a window of another process (the desktop's): the refusal
    /// is logged with its Win32 error, and the window is still tracked so the shield keeps trying.
    /// </summary>
    [Fact]
    public void ARefusedExclusionIsLoggedAndTheWindowStillTracked()
    {
        var log = new ListLogger<OwnWindowRegistry>();
        var registry = new OwnWindowRegistry(log);
        var desktop = User32.GetDesktopWindow();
        Assert.True(registry.Register(desktop));
        Assert.True(registry.IsRegistered(desktop));
        var (level, message) = Assert.Single(log.Entries);
        Assert.Equal(LogLevel.Warning, level);
        Assert.StartsWith($"own window 0x{desktop:x}: capture exclusion refused (Win32 error ", message, StringComparison.Ordinal);
    }

    [Fact]
    public void ZeroIsRefused() =>
        Assert.Throws<ArgumentException>("hwnd", () => new OwnWindowRegistry(new ListLogger<OwnWindowRegistry>()).Register(0));

    [Fact]
    public void LogIsRequired() => Assert.Throws<ArgumentNullException>("log", () => new OwnWindowRegistry(null!));

    /// <summary>
    /// Registrations from many threads at once add each window once. The handles name no window
    /// of this process, so each exclusion is refused and logged; only the set is under test.
    /// </summary>
    [Fact]
    public async Task ConcurrentRegistrationsAddEachWindowOnce()
    {
        var registry = new OwnWindowRegistry(new ListLogger<OwnWindowRegistry>());
        var handles = Enumerable.Range(1, 64).Select(i => (nint)(0x7F00_0000 + (i * 4))).ToArray();
        var added = await Task.WhenAll(Enumerable.Range(0, 8).Select(_ => Task.Run(() => handles.Count(registry.Register), TestContext.Current.CancellationToken)));
        Assert.Equal(handles.Length, added.Sum());
        Assert.Equal(handles, registry.Snapshot().Order());
    }
}
