using Xunit;

namespace ShotAI.Core.Tests.Support;

/// <summary>
/// Reads a file of the Electron tree in place, for the parity tests that compare native
/// constants and strings with their TypeScript source. After cutover, when that tree is gone,
/// the test is skipped.
/// </summary>
internal static class ElectronSource
{
    /// <summary>The text of <paramref name="relative"/> under the repository root, or a skip when it no longer exists.</summary>
    public static string Read(string relative)
    {
        var path = Path.Combine(RepoRoot(), relative);
        if (!File.Exists(path)) Assert.Skip($"{relative} is gone: the Electron tree was removed at cutover.");
        return File.ReadAllText(path);
    }

    /// <summary>The first ancestor of the test output that holds dotnet/ShotAI.slnx.</summary>
    private static string RepoRoot()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        {
            if (File.Exists(Path.Combine(dir.FullName, "dotnet", "ShotAI.slnx"))) return dir.FullName;
        }
        throw new InvalidOperationException($"no ancestor of {AppContext.BaseDirectory} contains dotnet/ShotAI.slnx");
    }
}
