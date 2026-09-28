using ShotAI.App.Chrome;
using ShotAI.App.Home;
using ShotAI.Core.Model;

namespace ShotAI.App.Tests.Support;

/// <summary>An <see cref="ICaptureTargetSelection"/> that gives <see cref="Target"/> and counts the reads.</summary>
internal sealed class FixedTargets : ICaptureTargetSelection
{
    /// <summary>The target each read gives: Screen with no monitor by default.</summary>
    public CaptureTarget Target { get; set; } = new("screen");

    /// <summary>How many times the target was read.</summary>
    public int Reads { get; private set; }

    /// <inheritdoc/>
    public CaptureTarget BuildTarget()
    {
        Reads++;
        return Target;
    }

    /// <summary>A picker over a <see cref="FakeCaptureService"/> and a <see cref="FakeAreaSelection"/>, for a view model that needs the real one.</summary>
    public static CaptureModePickerViewModel Picker(INoticeService notices, FakeCaptureService? capture = null, FakeAreaSelection? areas = null) =>
        new(capture ?? new FakeCaptureService(), areas ?? new FakeAreaSelection(), notices);
}
