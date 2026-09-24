namespace ShotAI.Core.Theme;

/// <summary>
/// A CSS <c>box-shadow: 0 &lt;OffsetY&gt;px &lt;Blur&gt;px rgba(R, G, B, Alpha)</c> (spec 06 2.33), which
/// the App draws as a <c>DropShadowEffect</c> pointing down (7.5).
/// </summary>
/// <param name="OffsetY">The downward offset in DIP.</param>
/// <param name="Blur">The blur radius in DIP.</param>
/// <param name="R">Red.</param>
/// <param name="G">Green.</param>
/// <param name="B">Blue.</param>
/// <param name="Alpha">The opacity, 0 to 1.</param>
public sealed record ShadowSpec(double OffsetY, double Blur, byte R, byte G, byte B, double Alpha);
