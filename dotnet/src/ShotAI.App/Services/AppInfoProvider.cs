using System.Runtime.InteropServices;
using ShotAI.App.Composition;
using ShotAI.Core.Diagnostics;
using ShotAI.Core.Shell;
using ShotAI.Core.Updates;

namespace ShotAI.App.Services;

/// <summary>
/// <see cref="IAppInfo"/> (spec 11 7.3.5). The WebView2 version comes from Platform's probe, so
/// the App has no WebView2 reference (R-ARCH-12, INV-ARCH-5); the probe runs on the first read,
/// not at startup.
/// </summary>
public sealed class AppInfoProvider : IAppInfo
{
    private readonly Lazy<AppInfo> _current;

    /// <summary>The provider over <paramref name="webView2"/>, the runtime probe.</summary>
    public AppInfoProvider(IWebView2RuntimeInfo webView2)
    {
        ArgumentNullException.ThrowIfNull(webView2);
        // PublicationOnly takes no lock: two first reads may both probe, and the first result wins.
        _current = new Lazy<AppInfo>(() => Compute(webView2), LazyThreadSafetyMode.PublicationOnly);
    }

    /// <inheritdoc/>
    public AppInfo Current => _current.Value;

    private static AppInfo Compute(IWebView2RuntimeInfo webView2) => new(
        ShellStrings.AppName,
        AppVersion.Current.Display,
        "win32",
        AppLogging.ArchName(RuntimeInformation.ProcessArchitecture),
        Environment.Version.ToString(),
        webView2.GetVersion());
}
