namespace ShotAI.Core.Shell;

/// <summary>
/// View, Actual Size, Zoom In and Zoom Out (spec 03 2.8.1, 7.2): Chromium's zoom levels, where a
/// level's factor is 1.2 to the level and each step is half a level. The level is the main
/// window's for the run; nothing persists it (Q-SHELL-12).
/// </summary>
/// <remarks>
/// Chromium clamps the factor it draws to 0.25 to 5.0 (2.8.1); whether Electron's stored level
/// also stops at the ends is unverified. Here the levels stop at the last half step inside that
/// range, -7.5 to 8.5, so every press moves one whole step and the same number of presses back
/// always returns to factor 1.
/// </remarks>
public static class UiZoom
{
    /// <summary>A level's factor base, Chromium's <c>kTextSizeMultiplierRatio</c>.</summary>
    public const double Base = 1.2;

    /// <summary>The step of Zoom In and Zoom Out (Electron's roles add and subtract 0.5).</summary>
    public const double Step = 0.5;

    /// <summary>Actual Size: level 0, factor 1.</summary>
    public const double ActualSize = 0;

    /// <summary>The smallest factor Chromium draws.</summary>
    public const double MinFactor = 0.25;

    /// <summary>The largest factor Chromium draws.</summary>
    public const double MaxFactor = 5.0;

    /// <summary>The lowest level, the last half step whose factor is at least <see cref="MinFactor"/>.</summary>
    public const double MinLevel = -7.5;

    /// <summary>The highest level, the last half step whose factor is at most <see cref="MaxFactor"/>.</summary>
    public const double MaxLevel = 8.5;

    /// <summary>The factor of <paramref name="level"/>: <c>1.2 ^ level</c>, within 0.25 to 5.0.</summary>
    public static double Factor(double level) => Math.Clamp(Math.Pow(Base, level), MinFactor, MaxFactor);

    /// <summary>One step up, no higher than <see cref="MaxLevel"/>.</summary>
    public static double ZoomIn(double level) => Math.Min(MaxLevel, level + Step);

    /// <summary>One step down, no lower than <see cref="MinLevel"/>.</summary>
    public static double ZoomOut(double level) => Math.Max(MinLevel, level - Step);
}
