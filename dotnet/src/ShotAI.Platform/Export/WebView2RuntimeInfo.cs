using Microsoft.Web.WebView2.Core;
using ShotAI.Core.Diagnostics;

namespace ShotAI.Platform.Export;

/// <summary>
/// The installed WebView2 runtime's version (spec 11 7.3.5, R-ARCH-12), read by the loader
/// without starting a browser process. It sits beside the PDF host, the one other WebView2 user
/// (INV-ARCH-5).
/// </summary>
internal sealed class WebView2RuntimeInfo : IWebView2RuntimeInfo
{
    /// <inheritdoc/>
    public string? GetVersion()
    {
        try
        {
            return CoreWebView2Environment.GetAvailableBrowserVersionString();
        }
        catch (WebView2RuntimeNotFoundException)
        {
            return null;
        }
        catch (Exception)
        {
            // A missing loader or a broken install: About says "not installed", nothing throws.
            return null;
        }
    }
}
