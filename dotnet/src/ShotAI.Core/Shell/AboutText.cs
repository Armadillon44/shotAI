namespace ShotAI.Core.Shell;

/// <summary>
/// The About dialog's text (spec 03 2.8.4, 7.2): Electron's message and tagline, and the runtime
/// line of D11, which names the native components where Electron named Electron and Chromium.
/// </summary>
public static class AboutText
{
    /// <summary>What the runtime line says when no WebView2 runtime answers the probe.</summary>
    public const string NotInstalled = "not installed";

    /// <summary>The bold line: <c>shotAI &lt;version&gt;</c>, for example <c>shotAI 2.0.0</c>.</summary>
    /// <param name="version">The version without its <c>+</c> build metadata.</param>
    public static string Message(string version)
    {
        ArgumentNullException.ThrowIfNull(version);
        return $"{ShellStrings.AppName} {version}";
    }

    /// <summary>
    /// The detail: the tagline, a blank line, <c>.NET &lt;v&gt; &#183; WebView2 &lt;v&gt;</c> (or
    /// <see cref="NotInstalled"/>), and <c>win32/&lt;arch&gt;</c> on the last line.
    /// </summary>
    /// <param name="dotNetVersion">The runtime's version, <c>Environment.Version</c>.</param>
    /// <param name="webView2Version">The WebView2 runtime's version, or null when it is missing or the probe failed.</param>
    /// <param name="arch">The process architecture, <c>x64</c> or <c>arm64</c>.</param>
    public static string Detail(string dotNetVersion, string? webView2Version, string arch)
    {
        ArgumentNullException.ThrowIfNull(dotNetVersion);
        ArgumentNullException.ThrowIfNull(arch);
        return ShellStrings.AboutTagline + "\n\n"
            + $".NET {dotNetVersion} \u00b7 WebView2 {webView2Version ?? NotInstalled}" + "\n"
            + $"win32/{arch}";
    }
}
