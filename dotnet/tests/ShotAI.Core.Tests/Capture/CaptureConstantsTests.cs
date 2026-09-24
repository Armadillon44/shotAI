using System.Globalization;
using System.Text.RegularExpressions;
using ShotAI.Core.Capture;
using ShotAI.Core.Tests.Support;
using Xunit;

namespace ShotAI.Core.Tests.Capture;

/// <summary>
/// Spec 02 section 3: the capture numbers, read against the Electron source where Electron
/// names them (a typo in a timing or a box size shows only in a recording), and against the
/// spec for the native ones.
/// </summary>
public sealed partial class CaptureConstantsTests
{
    public static TheoryData<string, string, long> Named => new()
    {
        { "src/main/CaptureController.ts", "HIDE_SETTLE_MS", CaptureConstants.HideSettleMs },
        { "src/main/CaptureController.ts", "MIN_CAPTURE_LONG_EDGE", CaptureConstants.MinCaptureLongEdge },
        { "src/main/CaptureController.ts", "MENU_FOLLOWUP_WINDOW_MS", CaptureConstants.MenuFollowupWindowMs },
        { "src/main/CaptureController.ts", "SUBMENU_FOLLOWUP_WINDOW_MS", CaptureConstants.SubmenuFollowupWindowMs },
        { "src/main/CaptureController.ts", "MENU_PROXIMITY_X", CaptureConstants.MenuProximityX },
        { "src/main/CaptureController.ts", "MENU_PROXIMITY_Y", CaptureConstants.MenuProximityY },
        { "src/main/CaptureController.ts", "MENU_POLL_MS", CaptureConstants.MenuPollMs },
        { "src/main/CaptureController.ts", "MAX_POLL_FRAMES", CaptureConstants.MaxPollFrames },
        { "src/main/CaptureController.ts", "MAX_MENU_CHAIN", CaptureConstants.MaxMenuChain },
        { "src/main/CaptureController.ts", "DOUBLE_CLICK_MS", CaptureConstants.DoubleClickMs },
        { "src/main/CaptureController.ts", "DOUBLE_CLICK_DIST", CaptureConstants.DoubleClickDist },
        { "src/main/element-locator.ts", "QUERY_TIMEOUT_MS", CaptureConstants.ElementQueryTimeoutMs },
    };

    [Theory]
    [MemberData(nameof(Named))]
    public void NamedConstantsMatchElectron(string file, string name, long value)
    {
        var m = Regex.Match(ElectronSource.Read(file), @"^const " + name + @" = (-?\d+);", RegexOptions.Multiline);
        Assert.True(m.Success, $"{file} declares no {name}");
        Assert.Equal(long.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture), value);
    }

    /// <summary>The numbers Electron writes inline, found where it writes them.</summary>
    [Fact]
    public void InlineNumbersMatchElectron()
    {
        var controller = ElectronSource.Read("src/main/CaptureController.ts");
        Assert.Contains($"Math.min(Math.round({CaptureConstants.RegionBoxWidth} * sf), mon.width())", controller, StringComparison.Ordinal);
        Assert.Contains($"Math.min(Math.round({CaptureConstants.RegionBoxHeight} * sf), mon.height())", controller, StringComparison.Ordinal);
        Assert.Contains($"w.x() > {CaptureConstants.OffScreenSentinel} && w.y() > {CaptureConstants.OffScreenSentinel}", controller, StringComparison.Ordinal);
        Assert.Contains($"if (grabMs + downMs > {CaptureConstants.TimingLogThresholdMs})", controller, StringComparison.Ordinal);
        Assert.Contains($"padStart({CaptureConstants.FilenamePad}, '0')", controller, StringComparison.Ordinal);
        Assert.Contains($"Math.round({CaptureConstants.ClickBoxHalf} * (scaleFactor || 1))", ElectronSource.Read("src/main/capture-geometry.ts"), StringComparison.Ordinal);
        Assert.Contains($"if depth >= {CaptureConstants.ElementClimbDepth} {{", ElectronSource.Read("native/element-locator/src/lib.rs"), StringComparison.Ordinal);
    }

    /// <summary>The native numbers, which Electron does not have (spec 02 section 3, "new").</summary>
    [Fact]
    public void NativeNumbersMatchTheSpec()
    {
        Assert.Equal(1000000L, CaptureConstants.OrphanNumberClamp);
        Assert.Equal(0x5348, CaptureConstants.HotkeyId);
        Assert.Equal(2000, CaptureConstants.HookWatchdogIntervalMs);
        Assert.Equal(2000, CaptureConstants.HookThreadTimeoutMs);
        Assert.Equal(500, CaptureConstants.UiaTimeoutMs);
        Assert.Equal(600, CaptureConstants.UiaRequestDeadlineMs);
        Assert.Equal(256, CaptureConstants.DispatcherRingSize);
    }
}
