using System.IO;
using ShotAI.Core.Paths;

namespace ShotAI.App;

/// <summary>
/// The app's folders (spec 10 7.4.4, ARCHITECTURE 10.2), from the known folders, read once. The
/// roaming ones are the Electron build's (INV-INFRA-14); the machine-local one is under
/// <c>LFI\shotAI</c>, never the Squirrel root <c>%LOCALAPPDATA%\shotai</c> (R-ARCH-13).
/// </summary>
public sealed class AppPaths : IAppPaths
{
    /// <exception cref="InvalidOperationException">
    /// A known folder is empty: a fatal startup error (10 7.4.4), which cannot happen on a normal
    /// profile. When it is the roaming folder, the log cannot exist either.
    /// </exception>
    public AppPaths()
        : this(Environment.GetFolderPath)
    {
    }

    /// <summary>With the known folders given, for the test of an empty one.</summary>
    internal AppPaths(Func<Environment.SpecialFolder, string> knownFolder)
    {
        ArgumentNullException.ThrowIfNull(knownFolder);
        UserDataDirectory = Path.Combine(Known(knownFolder, Environment.SpecialFolder.ApplicationData), "shotAI");
        LocalDataDirectory = Path.Combine(Known(knownFolder, Environment.SpecialFolder.LocalApplicationData), "LFI", "shotAI");
        DefaultProjectsDir = Path.Combine(Known(knownFolder, Environment.SpecialFolder.UserProfile), "shotAI Projects");
        TempDirectory = Path.GetTempPath();
        FontsDirectory = Path.Combine(AppContext.BaseDirectory, "Fonts");
    }

    /// <inheritdoc/>
    public string UserDataDirectory { get; }

    /// <inheritdoc/>
    public string SettingsFile => Path.Combine(UserDataDirectory, "settings.json");

    /// <inheritdoc/>
    public string LogsDirectory => Path.Combine(UserDataDirectory, "logs");

    /// <inheritdoc/>
    public string LocalDataDirectory { get; }

    /// <inheritdoc/>
    public string DefaultProjectsDir { get; }

    /// <inheritdoc/>
    public string TempDirectory { get; }

    /// <inheritdoc/>
    public string FontsDirectory { get; }

    private static string Known(Func<Environment.SpecialFolder, string> knownFolder, Environment.SpecialFolder folder)
    {
        var path = knownFolder(folder);
        return string.IsNullOrEmpty(path)
            ? throw new InvalidOperationException($"The known folder {folder} is empty, so shotAI has nowhere to keep its files.")
            : path;
    }
}
