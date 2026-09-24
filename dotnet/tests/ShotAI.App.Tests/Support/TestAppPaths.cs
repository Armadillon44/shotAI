using ShotAI.Core.Paths;

namespace ShotAI.App.Tests.Support;

/// <summary>An <see cref="IAppPaths"/> under one temp folder (ARCHITECTURE 10.2: tests pass a temp-folder implementation).</summary>
internal sealed class TestAppPaths(string root) : IAppPaths
{
    public string UserDataDirectory { get; } = Path.Combine(root, "userData");

    public string SettingsFile => Path.Combine(UserDataDirectory, "settings.json");

    public string LogsDirectory => Path.Combine(UserDataDirectory, "logs");

    public string LocalDataDirectory { get; } = Path.Combine(root, "local", "LFI", "shotAI");

    public string DefaultProjectsDir { get; } = Path.Combine(root, "home", "shotAI Projects");

    public string TempDirectory { get; } = Path.Combine(root, "tmp");

    public string FontsDirectory { get; } = Path.Combine(root, "fonts");
}
