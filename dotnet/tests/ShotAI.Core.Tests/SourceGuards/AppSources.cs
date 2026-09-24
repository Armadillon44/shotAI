using ShotAI.Core.Tests.Support;

namespace ShotAI.Core.Tests.SourceGuards;

/// <summary>A source file of the App, by its path under <c>dotnet/src/ShotAI.App/</c> with forward slashes.</summary>
internal sealed record SourceFile(string Path, string Text);

/// <summary>
/// The App's XAML and C# as text (spec 06 Q-HOME-1): the guards read files only, so they run on
/// Linux, and a colour literal fails the Linux job. Build output is not source.
/// </summary>
internal static class AppSources
{
    public static string Folder { get; } = Path.Combine(RepoFiles.Root, "dotnet", "src", "ShotAI.App");

    public static IReadOnlyList<SourceFile> Xaml() => Read("*.xaml");

    public static IReadOnlyList<SourceFile> Code() => Read("*.cs");

    private static SourceFile[] Read(string pattern) =>
        Directory.EnumerateFiles(Folder, pattern, SearchOption.AllDirectories)
            .Select(p => Path.GetRelativePath(Folder, p).Replace('\\', '/'))
            .Where(p => !p.StartsWith("bin/", StringComparison.Ordinal) && !p.StartsWith("obj/", StringComparison.Ordinal))
            .Order(StringComparer.Ordinal)
            .Select(p => new SourceFile(p, File.ReadAllText(Path.Combine(Folder, p))))
            .ToArray();
}
