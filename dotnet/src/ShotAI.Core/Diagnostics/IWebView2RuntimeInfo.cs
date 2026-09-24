namespace ShotAI.Core.Diagnostics;

/// <summary>
/// The installed WebView2 runtime's version (spec 11 7.2 and 7.3.5), for the About dialog's
/// runtime line. Platform's <c>ShotAI.Platform.Export.WebView2RuntimeInfo</c> implements it, so
/// the App reads the version without a WebView2 reference of its own (R-ARCH-12, INV-ARCH-5).
/// </summary>
public interface IWebView2RuntimeInfo
{
    /// <summary>
    /// The runtime's version string, or null when no runtime is installed or the probe failed.
    /// It never throws.
    /// </summary>
    string? GetVersion();
}
