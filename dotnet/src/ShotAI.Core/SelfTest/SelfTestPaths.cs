using ShotAI.Core.Paths;

namespace ShotAI.Core.SelfTest;

/// <summary>
/// The app's paths, with the two the settings service and the store read pointed at a self-test's
/// own (spec 10 7.8, INV-INFRA-30): the user's <c>settings.json</c> and projects folder are never
/// opened.
/// </summary>
internal sealed class SelfTestPaths(IAppPaths paths, string testRoot, string settingsFile) : IAppPaths
{
    public string UserDataDirectory => paths.UserDataDirectory;

    public string SettingsFile => settingsFile;

    public string LogsDirectory => paths.LogsDirectory;

    public string LocalDataDirectory => paths.LocalDataDirectory;

    public string DefaultProjectsDir => testRoot;

    public string TempDirectory => paths.TempDirectory;

    public string FontsDirectory => paths.FontsDirectory;
}
