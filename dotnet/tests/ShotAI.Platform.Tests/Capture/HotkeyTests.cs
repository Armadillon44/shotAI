using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;
using ShotAI.Core.Capture;
using ShotAI.Platform.Capture;
using ShotAI.Platform.Tests.Support;
using Xunit;

namespace ShotAI.Platform.Tests.Capture;

/// <summary>
/// The capture hotkey (spec 02 7.4, 8.4; D14): Ctrl+Shift+S, registered on the hook thread with
/// <c>MOD_NOREPEAT</c>, released on detach, and reported rather than thrown when another app
/// holds it. The tests that register the chord themselves are synchronous, because a hotkey
/// belongs to the thread that registered it.
/// </summary>
[Collection(InputHookCollection.Name)]
public sealed class HotkeyTests : IDisposable
{
    // Any id: the chord, not the id, is what collides.
    private const int TestHotkeyId = 0x7101;

    private readonly ListLogger<Win32TriggerSource> _log = new();
    private readonly Win32TriggerSource _source;
    private readonly ConcurrentQueue<string> _seen = new();

    public HotkeyTests() => _source = new Win32TriggerSource(_log);

    public void Dispose() => _source.Dispose();

    private TriggerAttachResult Attach(bool withHotkey = true) =>
        _source.Attach(m => _seen.Enqueue("click"), withHotkey ? () => _seen.Enqueue("hotkey") : null);

    private static bool TestCanRegisterTheChord()
    {
        var registered = User32.RegisterHotKey(0, TestHotkeyId, User32.ModControl | User32.ModShift, User32.VkS);
        if (registered) User32.UnregisterHotKey(0, TestHotkeyId);
        return registered;
    }

    /// <summary>The chord is registered, and pressing it reaches the hotkey callback on the dispatcher.</summary>
    [Fact]
    public async Task RegistersCtrlShiftS()
    {
        Assert.True(Attach().HotkeyRegistered);
        Assert.False(TestCanRegisterTheChord());

        SyntheticInput.Chord(User32.VkControl, User32.VkShift, (ushort)User32.VkS);
        var deadline = DateTime.UtcNow + MouseHookTests.Timeout;
        while (!_seen.Contains("hotkey"))
        {
            if (DateTime.UtcNow > deadline) throw new TimeoutException("the hotkey never arrived");
            await Task.Delay(10, TestContext.Current.CancellationToken);
        }
        Assert.DoesNotContain(_log.Entries, e => e.Level >= LogLevel.Warning);
    }

    /// <summary>D14: a chord another app holds is reported as not registered and logged at warning; the attach still succeeds.</summary>
    [Fact]
    public void SecondRegistrationReportsFalse()
    {
        Assert.True(User32.RegisterHotKey(0, TestHotkeyId, User32.ModControl | User32.ModShift, User32.VkS), "the test could not take the chord");
        try
        {
            Assert.False(Attach().HotkeyRegistered);
            Assert.NotNull(_source.HookThreadForTest);
            Assert.Contains(_log.Entries, e => e.Level == LogLevel.Warning
                && e.Message == "hotkey Ctrl+Shift+S could not be registered (in use by another app); recording continues mouse-only");
        }
        finally
        {
            _source.Detach();
            User32.UnregisterHotKey(0, TestHotkeyId);
        }
    }

    /// <summary>DL7: the hook thread releases the chord when it stops, so anyone may take it after a detach.</summary>
    [Fact]
    public void UnregisteredOnDetach()
    {
        Assert.True(Attach().HotkeyRegistered);
        Assert.False(TestCanRegisterTheChord());
        _source.Detach();

        Assert.True(TestCanRegisterTheChord());
    }

    /// <summary>With no hotkey callback nothing is registered, and nothing is logged about the chord.</summary>
    [Fact]
    public void NoHotkeyWithoutACallback()
    {
        Assert.False(Attach(withHotkey: false).HotkeyRegistered);

        Assert.True(TestCanRegisterTheChord());
        Assert.DoesNotContain(_log.Entries, e => e.Level >= LogLevel.Warning);
    }
}
