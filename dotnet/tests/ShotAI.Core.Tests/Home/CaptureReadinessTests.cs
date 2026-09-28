using System.Text.Json.Nodes;
using ShotAI.Core.Capture;
using ShotAI.Core.Home;
using ShotAI.Core.Model;
using Xunit;

namespace ShotAI.Core.Tests.Home;

/// <summary>
/// Spec 06 8.4's <c>CaptureReadinessTests</c>: <c>modeReady</c>'s truth table and 2.4's
/// <c>buildTarget</c> table, the Auto fallbacks of EDGE-HOME-7 included.
/// </summary>
public sealed class CaptureReadinessTests
{
    private static readonly WindowInfo Notepad = new(0x0003_04A2, 4312, "notes.txt - Notepad", "Notepad");
    private static readonly Rect Area = new(100, 200, 300, 150);

    [Fact]
    public void EachLaunchStartsInScreenMode() => Assert.Equal(CaptureMode.Screen, CaptureReadiness.DefaultMode);

    /// <summary>INV-HOME-16: Window needs a window and Area an area; Screen and Auto are always ready.</summary>
    [Theory]
    [InlineData(CaptureMode.Screen, false, false, true)]
    [InlineData(CaptureMode.Screen, true, false, true)]
    [InlineData(CaptureMode.Screen, false, true, true)]
    [InlineData(CaptureMode.Screen, true, true, true)]
    [InlineData(CaptureMode.Auto, false, false, true)]
    [InlineData(CaptureMode.Auto, true, false, true)]
    [InlineData(CaptureMode.Auto, false, true, true)]
    [InlineData(CaptureMode.Auto, true, true, true)]
    [InlineData(CaptureMode.Window, false, false, false)]
    [InlineData(CaptureMode.Window, true, false, true)]
    [InlineData(CaptureMode.Window, false, true, false)]
    [InlineData(CaptureMode.Window, true, true, true)]
    [InlineData(CaptureMode.Area, false, false, false)]
    [InlineData(CaptureMode.Area, true, false, false)]
    [InlineData(CaptureMode.Area, false, true, true)]
    [InlineData(CaptureMode.Area, true, true, true)]
    public void Readiness(CaptureMode mode, bool hasWindow, bool hasArea, bool ready) =>
        Assert.Equal(ready, CaptureReadiness.IsReady(mode, hasWindow, hasArea));

    [Fact]
    public void AWindowTargetNamesThePickedWindow() =>
        Assert.Equal(
            new CaptureTarget("window", Window: new CaptureTargetWindow(0x0003_04A2, 4312, "notes.txt - Notepad")),
            CaptureReadiness.BuildTarget(CaptureMode.Window, Notepad, 7, Area));

    [Fact]
    public void AScreenTargetNamesThePickedMonitor() =>
        Assert.Equal(new CaptureTarget("screen", MonitorId: 65_537), CaptureReadiness.BuildTarget(CaptureMode.Screen, Notepad, 65_537, Area));

    /// <summary>With no monitor picked the engine chooses one (<c>{ mode: 'screen' }</c>).</summary>
    [Fact]
    public void AScreenTargetWithoutAMonitorLeavesItOut() =>
        Assert.Equal(new CaptureTarget("screen"), CaptureReadiness.BuildTarget(CaptureMode.Screen, Notepad, null, Area));

    [Fact]
    public void AnAreaTargetCarriesTheArea() =>
        Assert.Equal(new CaptureTarget("area", Area: Area), CaptureReadiness.BuildTarget(CaptureMode.Area, Notepad, 7, Area));

    [Fact]
    public void AutoIgnoresEveryPick() =>
        Assert.Equal(new CaptureTarget("auto"), CaptureReadiness.BuildTarget(CaptureMode.Auto, Notepad, 7, Area));

    /// <summary>EDGE-HOME-7: what Resume capturing starts from a mode that is not ready.</summary>
    [Fact]
    public void WindowAndAreaWithoutAPickFallBackToAuto()
    {
        Assert.Equal(new CaptureTarget("auto"), CaptureReadiness.BuildTarget(CaptureMode.Window, null, 7, Area));
        Assert.Equal(new CaptureTarget("auto"), CaptureReadiness.BuildTarget(CaptureMode.Area, Notepad, 7, null));
    }

    /// <summary>R-ARCH-22: a monitor or window id above <c>int.MaxValue</c> reaches the target whole.</summary>
    [Fact]
    public void IdsAboveIntMaxValueSurvive()
    {
        Assert.Equal(4_294_967_294d, CaptureReadiness.BuildTarget(CaptureMode.Screen, null, 0xFFFF_FFFE, null).MonitorId);
        var window = Notepad with { Id = 0x8000_0001 };
        Assert.Equal(2_147_483_649d, CaptureReadiness.BuildTarget(CaptureMode.Window, window, null, null).Window!.Id);
    }

    /// <summary>
    /// A kept pick carries the title it was listed with (EDGE-HOME-31); the engine re-resolves the
    /// window by id at each capture, so the title is only its fallback's.
    /// </summary>
    [Fact]
    public void AWindowTargetTakesTheTitleAsListed()
    {
        var untitled = Notepad with { Title = "" };
        Assert.Equal("", CaptureReadiness.BuildTarget(CaptureMode.Window, untitled, null, null).Window!.Title);
    }

    /// <summary>Anything else is JavaScript's <c>default</c>: ready, and Auto.</summary>
    [Fact]
    public void AnUnknownModeIsReadyAndAuto()
    {
        Assert.True(CaptureReadiness.IsReady((CaptureMode)9, false, false));
        Assert.Equal(new CaptureTarget("auto"), CaptureReadiness.BuildTarget((CaptureMode)9, Notepad, 7, Area));
    }

    /// <summary>Each target the picker builds reads back the same through 01's lenient parser, so the mode names are the manifest's.</summary>
    [Theory]
    [InlineData(CaptureMode.Screen)]
    [InlineData(CaptureMode.Auto)]
    [InlineData(CaptureMode.Window)]
    [InlineData(CaptureMode.Area)]
    public void TheModeNamesAreTheWireNames(CaptureMode mode)
    {
        var target = CaptureReadiness.BuildTarget(mode, Notepad, 7, Area);
        var json = new JsonObject { ["mode"] = target.Mode };
        if (target.MonitorId is { } id) json["monitorId"] = id;
        if (target.Window is { } w) json["window"] = new JsonObject { ["id"] = w.Id, ["pid"] = w.Pid, ["title"] = w.Title };
        if (target.Area is { } a) json["area"] = a.ToJson();
        Assert.Equal(target, CaptureTarget.TryParse(json));
    }
}
