namespace ShotAI.Core.Paths;

/// <summary>
/// Where the app keeps its files (spec 10 7.4.4, ARCHITECTURE 10.2). No code composes these
/// paths itself; the App's <c>AppPaths</c> reads the known folders, and tests pass a temp-folder
/// implementation.
/// </summary>
public interface IAppPaths
{
    /// <summary><c>%APPDATA%\shotAI</c>, roaming: the folder the Electron build shares (INV-INFRA-14).</summary>
    string UserDataDirectory { get; }

    /// <summary><c>UserDataDirectory\settings.json</c>.</summary>
    string SettingsFile { get; }

    /// <summary><c>UserDataDirectory\logs</c>.</summary>
    string LogsDirectory { get; }

    /// <summary>
    /// <c>%LOCALAPPDATA%\LFI\shotAI</c>, machine-local, for native-only data (the MSAL cache,
    /// WebView2 data). Never <c>%LOCALAPPDATA%\shotAI</c>, the Squirrel install root that removing
    /// Electron deletes (R-ARCH-13, EDGE-PKG-22). Each consumer creates its own subfolder.
    /// </summary>
    string LocalDataDirectory { get; }

    /// <summary><c>%USERPROFILE%\shotAI Projects</c>, the default projects folder.</summary>
    string DefaultProjectsDir { get; }

    /// <summary><c>Path.GetTempPath()</c>.</summary>
    string TempDirectory { get; }

    /// <summary><c>AppContext.BaseDirectory\Fonts</c>.</summary>
    string FontsDirectory { get; }
}
