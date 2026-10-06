using ShotAI.Core.Json;
using ShotAI.Core.Settings;

namespace ShotAI.Core.SettingsUi;

/// <summary>
/// The Screenshot quality slider's steps (spec 06 2.26, 7.4): 0.5 to 1 by 0.05, as the decimal
/// values Chromium's range input gives.
/// </summary>
public static class CaptureScaleSteps
{
    /// <summary>
    /// The step nearest <paramref name="value"/>, clamped to 0.5 to 1, as the double the decimal
    /// text parses to: a WPF slider adds binary fractions, so 0.85 can arrive as
    /// 0.8500000000000001, and the quotient of two exact integers is correctly rounded, so it is
    /// the same double as <c>0.85</c> (EDGE-HOME-38). <see cref="JsMath.Round"/> rounds, since
    /// <c>Math.Round</c> is banned in Core (ARCHITECTURE 14.9).
    /// </summary>
    public static double Snap(double value) =>
        (50 + 5 * JsMath.Round((Math.Clamp(value, SettingsDefaults.CaptureScaleMin, SettingsDefaults.CaptureScaleMax) - 0.5) / 0.05)) / 100.0;
}
