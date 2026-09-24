using Microsoft.Extensions.Logging;
using ShotAI.Core.Capture;
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

    /// <summary>A window that joins is announced once, on the registering thread; a second registration announces nothing.</summary>
    [Fact]
    public void ARegistrationIsAnnounced()
    {
        var registry = new OwnWindowRegistry(new ListLogger<OwnWindowRegistry>());
        var heard = new List<(nint Hwnd, int Thread, uint? Affinity)>();
        registry.Added += (_, hwnd) => heard.Add((hwnd, Environment.CurrentManagedThreadId, User32.Affinity(hwnd)));
        using var w = TestWindow.Popup();

        registry.Register(w.Handle);
        registry.Register(w.Handle);

        // Announced already excluded (7.8 rule 1), from the thread that registered.
        Assert.Equal([(w.Handle, Environment.CurrentManagedThreadId, User32.WdaExcludeFromCapture)], heard);
    }

    /// <summary>A throwing listener is logged and cannot fail the registration of the window.</summary>
    [Fact]
    public void AThrowingListenerCannotFailTheRegistration()
    {
        var log = new ListLogger<OwnWindowRegistry>();
        var registry = new OwnWindowRegistry(log);
        registry.Added += (_, _) => throw new InvalidOperationException("listener failed");
        using var w = TestWindow.Popup();

        Assert.True(registry.Register(w.Handle));
        Assert.True(registry.IsRegistered(w.Handle));
        Assert.Contains(log.Entries, e => e.Level == LogLevel.Warning && e.Message == "event handler failed: Added");
    }

    /// <summary>
    /// The registry's half of INV-CAP-7 with the real shield: a window registered while a grab
    /// holds the shield stays excluded, even with remote visibility on, until the release.
    /// </summary>
    [Fact]
    public async Task RegisterWhileShieldHeldStartsExcluded()
    {
        var (registry, shield) = Shielded(remoteVisible: true);
        shield.ApplyRemoteVisibility(true);
        using var w = TestWindow.Popup();
        var release = shield.Take();

        registry.Register(w.Handle);
        await Task.Delay(300, TestContext.Current.CancellationToken); // time for the reconcile, which must change nothing
        Assert.Equal(User32.WdaExcludeFromCapture, User32.Affinity(w.Handle));

        release.Dispose();
        Assert.Equal(User32.WdaNone, User32.Affinity(w.Handle));
    }

    /// <summary>
    /// INV-CAP-7, 7.8 rule 1: a new window starts excluded and is then relaxed on the pool when
    /// the setting allows; while it does not, the window stays excluded.
    /// </summary>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task RegisterSeedsFromSetting(bool remoteVisible)
    {
        var (registry, shield) = Shielded(remoteVisible);
        shield.ApplyRemoteVisibility(remoteVisible);
        using var w = TestWindow.Popup();

        registry.Register(w.Handle);
        var expected = remoteVisible ? User32.WdaNone : User32.WdaExcludeFromCapture;
        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(5);
        while (User32.Affinity(w.Handle) != expected && DateTime.UtcNow < deadline) await Task.Delay(20, TestContext.Current.CancellationToken);
        await Task.Delay(100, TestContext.Current.CancellationToken);

        Assert.Equal(expected, User32.Affinity(w.Handle));
    }

    private static (OwnWindowRegistry Registry, CaptureShield Shield) Shielded(bool remoteVisible)
    {
        var registry = new OwnWindowRegistry(new ListLogger<OwnWindowRegistry>());
        var protection = new DisplayAffinityProtection(registry, new ListLogger<DisplayAffinityProtection>());
        return (registry, new CaptureShield(protection, new FixedCaptureSettings(remoteVisible)));
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
