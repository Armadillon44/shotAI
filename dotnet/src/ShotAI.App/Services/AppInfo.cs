namespace ShotAI.App.Services;

/// <summary>What <see cref="IAppInfo.Current"/> reports.</summary>
/// <param name="Name"><c>shotAI</c>.</param>
/// <param name="Version">The informational version without its <c>+</c> build metadata.</param>
/// <param name="Platform"><c>win32</c>, the value Electron's <c>process.platform</c> gave, which support scripts quote.</param>
/// <param name="Arch">The process architecture, lower case: <c>x64</c> or <c>arm64</c>.</param>
/// <param name="DotNetVersion">The runtime's version, <c>Environment.Version</c>.</param>
/// <param name="WebView2Version">The WebView2 runtime's version, or null when it is missing or the probe failed.</param>
public sealed record AppInfo(string Name, string Version, string Platform, string Arch, string DotNetVersion, string? WebView2Version);
