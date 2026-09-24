namespace ShotAI.Core.Shell;

/// <summary>
/// <c>SHOTAI_ENABLE_GPU</c>, the troubleshooting switch Electron's GPU policy read (spec 03 D17):
/// natively it can only force software rendering, at startup step 4.
/// </summary>
public static class RenderModePolicy
{
    /// <summary>The environment variable.</summary>
    public const string Variable = "SHOTAI_ENABLE_GPU";

    /// <summary>
    /// True for exactly <c>0</c>, as Electron compared (<c>=== '0'</c>); anything else, <c>1</c>
    /// and an unset variable included, leaves WPF's default.
    /// </summary>
    public static bool ForceSoftware(string? shotaiEnableGpu) => shotaiEnableGpu == "0";
}
