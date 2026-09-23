using ShotAI.Core.Json;

namespace ShotAI.Core.Geometry;

/// <summary>
/// The per-project document scale (<c>src/shared/doc-scale.ts</c>). Spec 05 owns this class;
/// spec 01's codec needs only <see cref="Clamp"/>.
/// </summary>
public static class DocScale
{
    /// <summary>
    /// <c>clampScale</c> (spec 01 2.5.1): a finite value snapped to the nearest 5% detent from
    /// 0.65 to 1.25 in integer percent; a non-finite value is 1.
    /// </summary>
    /// <remarks>
    /// Rounds with <see cref="JsMath.Round"/>, so 0.825 is 0.85 as in JavaScript, where
    /// half-to-even rounding would give 0.8.
    /// </remarks>
    public static double Clamp(double value)
    {
        if (!double.IsFinite(value)) return 1;
        var pct = JsMath.Round(value * 100);
        var clamped = Math.Min(125, Math.Max(65, pct));
        var snapped = JsMath.Round(clamped / 5) * 5;
        return snapped / 100;
    }
}
