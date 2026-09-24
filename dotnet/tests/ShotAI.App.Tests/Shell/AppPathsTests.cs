using ShotAI.Core.Paths;
using Xunit;

namespace ShotAI.App.Tests.Shell;

/// <summary>
/// Spec 10 7.4.4 and 8.5, 12 INV-PKG-24, R-ARCH-13 (AC-INFRA-35): the roaming files stay where
/// the Electron build keeps them, and nothing is under the Squirrel root.
/// </summary>
public sealed class AppPathsTests
{
    private static readonly string AppData = Environment.GetEnvironmentVariable("APPDATA")!;
    private static readonly string LocalAppData = Environment.GetEnvironmentVariable("LOCALAPPDATA")!;

    [Fact]
    public void SettingsFileIsRoamingAppData() => Assert.Equal(Path.Combine(AppData, "shotAI", "settings.json"), new AppPaths().SettingsFile, StringComparer.OrdinalIgnoreCase);

    [Fact]
    public void LogsDirectoryIsRoamingAppData() => Assert.Equal(Path.Combine(AppData, "shotAI", "logs"), new AppPaths().LogsDirectory, StringComparer.OrdinalIgnoreCase);

    [Fact]
    public void LocalDataDirectoryIsUnderLfi() => Assert.Equal(Path.Combine(LocalAppData, "LFI", "shotAI"), new AppPaths().LocalDataDirectory, StringComparer.OrdinalIgnoreCase);

    /// <summary>EDGE-PKG-22: no member is <c>%LOCALAPPDATA%\shotai</c> or inside it, compared without case after <c>GetFullPath</c>.</summary>
    [Fact]
    public void NoPathUnderSquirrelRoot()
    {
        var squirrel = Path.TrimEndingDirectorySeparator(Path.GetFullPath(Path.Combine(LocalAppData, "shotai")));
        var paths = new AppPaths();
        var members = typeof(IAppPaths).GetProperties();
        Assert.Equal(7, members.Length);
        foreach (var p in members)
        {
            var value = Path.TrimEndingDirectorySeparator(Path.GetFullPath((string)p.GetValue(paths)!));
            Assert.False(value.Equals(squirrel, StringComparison.OrdinalIgnoreCase), $"{p.Name} is the Squirrel root");
            Assert.False(value.StartsWith(squirrel + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase), $"{p.Name} is under the Squirrel root");
        }
    }

    [Fact]
    public void TheOtherFolders()
    {
        var paths = new AppPaths();
        Assert.Equal(Path.Combine(AppData, "shotAI"), paths.UserDataDirectory, StringComparer.OrdinalIgnoreCase);
        Assert.Equal(Path.Combine(Environment.GetEnvironmentVariable("USERPROFILE")!, "shotAI Projects"), paths.DefaultProjectsDir, StringComparer.OrdinalIgnoreCase);
        Assert.Equal(Path.GetTempPath(), paths.TempDirectory);
        Assert.Equal(Path.Combine(AppContext.BaseDirectory, "Fonts"), paths.FontsDirectory);
    }

    /// <summary>10 7.4.4: an empty known folder is a fatal startup error.</summary>
    [Theory]
    [InlineData(Environment.SpecialFolder.ApplicationData)]
    [InlineData(Environment.SpecialFolder.LocalApplicationData)]
    [InlineData(Environment.SpecialFolder.UserProfile)]
    public void AnEmptyKnownFolderIsFatal(Environment.SpecialFolder empty)
    {
        var e = Assert.Throws<InvalidOperationException>(() => new AppPaths(f => f == empty ? "" : Environment.GetFolderPath(f)));
        Assert.Contains(empty.ToString(), e.Message, StringComparison.Ordinal);
    }

    /// <summary>The folders come from the known folders, whatever they are.</summary>
    [Fact]
    public void FoldersFollowTheKnownFolders()
    {
        var paths = new AppPaths(f => f switch
        {
            Environment.SpecialFolder.ApplicationData => @"R:\roam",
            Environment.SpecialFolder.LocalApplicationData => @"L:\local",
            _ => @"P:\profile",
        });
        Assert.Equal(@"R:\roam\shotAI\settings.json", paths.SettingsFile);
        Assert.Equal(@"R:\roam\shotAI\logs", paths.LogsDirectory);
        Assert.Equal(@"L:\local\LFI\shotAI", paths.LocalDataDirectory);
        Assert.Equal(@"P:\profile\shotAI Projects", paths.DefaultProjectsDir);
    }
}
