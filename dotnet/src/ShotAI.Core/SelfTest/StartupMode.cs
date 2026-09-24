namespace ShotAI.Core.SelfTest;

/// <summary>A mode of <see cref="StartupModeParser.Parse"/> (spec 10 7.8).</summary>
/// <param name="Kind">The mode.</param>
/// <param name="UpdateSelfTestVersion">
/// The version the update self-test checks as the installed one; null means the running version.
/// </param>
public sealed record StartupMode(StartupModeKind Kind, string? UpdateSelfTestVersion = null)
{
    /// <summary>The app, started normally.</summary>
    public static StartupMode Normal { get; } = new(StartupModeKind.Normal);
}
