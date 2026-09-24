using Microsoft.Extensions.Logging;
using ShotAI.Platform.Capture;
using ShotAI.Platform.Tests.Support;
using Xunit;

namespace ShotAI.Platform.Tests.Capture;

/// <summary>
/// The shield's Windows half (spec 02 7.8): the affinity of every registered window, set and
/// cleared, destroyed windows skipped, refusals logged, and the registry's announcements passed on.
/// </summary>
public sealed class DisplayAffinityProtectionTests
{
    private readonly ListLogger<OwnWindowRegistry> _registryLog = new();
    private readonly ListLogger<DisplayAffinityProtection> _log = new();
    private readonly OwnWindowRegistry _registry;
    private readonly DisplayAffinityProtection _protection;

    public DisplayAffinityProtectionTests()
    {
        _registry = new OwnWindowRegistry(_registryLog);
        _protection = new DisplayAffinityProtection(_registry, _log);
    }

    [Fact]
    public void SetAllExcludedWalksEveryRegisteredWindow()
    {
        using var a = TestWindow.Popup();
        using var b = TestWindow.Popup();
        using var outside = TestWindow.Popup();
        _registry.Register(a.Handle);
        _registry.Register(b.Handle);

        _protection.SetAllExcluded(false);
        Assert.Equal([User32.WdaNone, User32.WdaNone], new[] { User32.Affinity(a.Handle), User32.Affinity(b.Handle) });
        CaptureExclusion.Apply(outside.Handle, excluded: false);
        _protection.SetAllExcluded(true);
        Assert.Equal([User32.WdaExcludeFromCapture, User32.WdaExcludeFromCapture], new[] { User32.Affinity(a.Handle), User32.Affinity(b.Handle) });
        Assert.Equal(User32.WdaNone, User32.Affinity(outside.Handle));
        Assert.Empty(_log.Entries);
    }

    /// <summary>A destroyed window is skipped: no call, no warning, and the live ones still change.</summary>
    [Fact]
    public void SkipsDestroyedHwnd()
    {
        using var live = TestWindow.Popup();
        using var dead = TestWindow.Popup();
        _registry.Register(live.Handle);
        _registry.Register(dead.Handle);
        dead.Destroy();

        _protection.SetAllExcluded(false);
        _protection.SetExcluded(dead.Handle, excluded: true);

        Assert.Equal(User32.WdaNone, User32.Affinity(live.Handle));
        Assert.Empty(_log.Entries);
    }

    /// <summary>One window at a time, and only a registered one (7.8 rule 1's reconcile).</summary>
    [Fact]
    public void SetExcludedTouchesOnlyARegisteredWindow()
    {
        using var registered = TestWindow.Popup();
        using var stranger = TestWindow.Popup();
        _registry.Register(registered.Handle);

        _protection.SetExcluded(registered.Handle, excluded: false);
        _protection.SetExcluded(stranger.Handle, excluded: true);

        Assert.Equal(User32.WdaNone, User32.Affinity(registered.Handle));
        Assert.Equal(User32.WdaNone, User32.Affinity(stranger.Handle));
    }

    /// <summary>IMPROVEMENT: a refusal, here for the desktop, which is not this process's, is logged with its Win32 error.</summary>
    [Fact]
    public void ARefusalIsLoggedWithItsError()
    {
        var desktop = User32.GetDesktopWindow();
        _registry.Register(desktop);

        _protection.SetAllExcluded(false);
        _protection.SetAllExcluded(true);

        Assert.Collection(
            _log.Entries,
            e => Assert.StartsWith($"own window 0x{desktop:x}: capture exclusion could not be lifted (Win32 error ", e.Message, StringComparison.Ordinal),
            e => Assert.StartsWith($"own window 0x{desktop:x}: capture exclusion refused (Win32 error ", e.Message, StringComparison.Ordinal));
        Assert.All(_log.Entries, e => Assert.Equal(LogLevel.Warning, e.Level));
    }

    /// <summary>The shield hears the registry's announcements through the protection, and stops hearing them when it leaves.</summary>
    [Fact]
    public void WindowAddedIsTheRegistrysAnnouncement()
    {
        var heard = new List<nint>();
        void OnAdded(object? sender, nint hwnd) => heard.Add(hwnd);
        using var first = TestWindow.Popup();
        using var second = TestWindow.Popup();

        _protection.WindowAdded += OnAdded;
        _registry.Register(first.Handle);
        _protection.WindowAdded -= OnAdded;
        _registry.Register(second.Handle);

        Assert.Equal([first.Handle], heard);
    }

    [Fact]
    public void ArgumentsAreChecked()
    {
        Assert.Throws<ArgumentNullException>("registry", () => new DisplayAffinityProtection(null!, _log));
        Assert.Throws<ArgumentNullException>("log", () => new DisplayAffinityProtection(_registry, null!));
    }
}
