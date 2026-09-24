namespace ShotAI.Core.Tests.Support;

/// <summary>
/// Reads a file of the repository in place, for tests that check code against a document that
/// stays in the repository (the architecture's namespace table, for example). The Electron tree,
/// which goes away at cutover, is read through <see cref="ElectronSource"/> instead.
/// </summary>
internal static class RepoFiles
{
    /// <summary>The first ancestor of the test output that holds dotnet/ShotAI.slnx.</summary>
    public static string Root { get; } = FindRoot();

    /// <summary>The text of <paramref name="relative"/> under <see cref="Root"/>.</summary>
    public static string ReadText(string relative) => File.ReadAllText(Path.Combine(Root, relative));

    private static string FindRoot()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        {
            if (File.Exists(Path.Combine(dir.FullName, "dotnet", "ShotAI.slnx"))) return dir.FullName;
        }
        throw new InvalidOperationException($"no ancestor of {AppContext.BaseDirectory} contains dotnet/ShotAI.slnx");
    }
}
