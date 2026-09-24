using System.Text.RegularExpressions;
using ShotAI.Core.Tests.Support;
using Xunit;

namespace ShotAI.Core.Tests.Packaging;

/// <summary>
/// Spec 12 8.2 (INV-PKG-16, 7.2.1), the cases WP-A12 owns: the entry point hardens the DLL
/// search before anything else, and <c>App.xaml</c> is a Page, so WPF generates no second
/// <c>Main</c>. Each rule is also run on a changed copy, to prove it can fail (EDGE-PKG-39).
/// </summary>
public sealed partial class SourceScanTests
{
    private const string ProgramFile = "dotnet/src/ShotAI.App/Program.cs";
    private const string HardeningFile = "dotnet/src/ShotAI.Platform/DllSearchHardening.cs";
    private const string AppProject = "dotnet/src/ShotAI.App/ShotAI.App.csproj";
    private const string AppXaml = "dotnet/src/ShotAI.App/App.xaml";

    [Fact]
    public void SetDefaultDllDirectoriesIsFirstInMain()
    {
        Assert.Null(FirstStatementProblem(RepoFiles.ReadText(ProgramFile)));
        Assert.Null(HardeningProblem(RepoFiles.ReadText(HardeningFile)));
    }

    [Theory]
    [InlineData("var app = new App();\n        if (!DllSearchHardening.Apply()) Environment.FailFast(\"x\");")]
    [InlineData("Console.WriteLine();\n        if (!DllSearchHardening.Apply()) Environment.FailFast(\"x\");")]
    [InlineData("DllSearchHardening.Apply();")]
    [InlineData("if (DllSearchHardening.Apply()) Environment.FailFast(\"x\");")]
    public void AMainThatDoesNotStartWithTheHardeningFails(string body) =>
        Assert.NotNull(FirstStatementProblem(ProgramWith(body)));

    [Fact]
    public void AMainWithoutStaThreadFails() =>
        Assert.NotNull(FirstStatementProblem(RepoFiles.ReadText(ProgramFile).Replace("[STAThread]", "", StringComparison.Ordinal)));

    [Fact]
    public void CommentsBeforeTheFirstStatementAreAllowed() =>
        Assert.Null(FirstStatementProblem(ProgramWith("// Must come first.\n        /* really */\n        if (!DllSearchHardening.Apply()) Environment.FailFast(\"x\");\n        var app = new App();")));

    [Theory]
    [InlineData("LOAD_LIBRARY_SEARCH_DEFAULT_DIRS", "LOAD_LIBRARY_SEARCH_SYSTEM32")]
    [InlineData("PInvoke.SetDefaultDllDirectories(", "PInvoke.SetDllDirectory(")]
    public void HardeningWithAnotherCallFails(string from, string to) =>
        Assert.NotNull(HardeningProblem(RepoFiles.ReadText(HardeningFile).Replace(from, to, StringComparison.Ordinal)));

    [Fact]
    public void AppXamlIsPage()
    {
        Assert.Null(AppXamlProblem(RepoFiles.ReadText(AppProject), RepoFiles.ReadText(AppXaml)));
        // One Main in the App's source: Program's.
        var root = Path.Combine(RepoFiles.Root, "dotnet", "src", "ShotAI.App");
        var mains = Directory.EnumerateFiles(root, "*.cs", SearchOption.AllDirectories)
            .Where(f => !IsBuildOutput(root, f))
            .Where(f => MainDeclaration().IsMatch(File.ReadAllText(f)))
            .Select(f => Path.GetRelativePath(root, f).Replace('\\', '/'))
            .ToList();
        Assert.Equal(["Program.cs"], mains);
    }

    [Theory]
    [InlineData("<ApplicationDefinition Remove=\"App.xaml\" />", "")]
    [InlineData("<Page Include=\"App.xaml\" />", "")]
    public void AnAppXamlThatIsNotAPageFails(string from, string to) =>
        Assert.NotNull(AppXamlProblem(RepoFiles.ReadText(AppProject).Replace(from, to, StringComparison.Ordinal), RepoFiles.ReadText(AppXaml)));

    [Fact]
    public void AStartupUriFails() =>
        Assert.NotNull(AppXamlProblem(RepoFiles.ReadText(AppProject), RepoFiles.ReadText(AppXaml).Replace("ShutdownMode=", "StartupUri=\"MainWindow.xaml\" ShutdownMode=", StringComparison.Ordinal)));

    // Null when Main is [STAThread] and its first statement is "if (!DllSearchHardening.Apply()) Environment.FailFast(...)".
    internal static string? FirstStatementProblem(string program)
    {
        var main = MainWithBody().Match(program);
        if (!main.Success) return "no Main";
        if (!main.Groups["attrs"].Value.Contains("[STAThread]", StringComparison.Ordinal)) return "Main is not [STAThread]";
        var body = StripComments(program[(main.Index + main.Length)..]).TrimStart();
        return HardeningFirst().IsMatch(body) ? null : "the first statement of Main is not the DllSearchHardening.Apply() check";
    }

    internal static string? HardeningProblem(string hardening) =>
        hardening.Contains("PInvoke.SetDefaultDllDirectories(LOAD_LIBRARY_FLAGS.LOAD_LIBRARY_SEARCH_DEFAULT_DIRS)", StringComparison.Ordinal)
            ? null
            : "DllSearchHardening does not call SetDefaultDllDirectories(LOAD_LIBRARY_SEARCH_DEFAULT_DIRS)";

    internal static string? AppXamlProblem(string project, string xaml)
    {
        if (!project.Contains("<ApplicationDefinition Remove=\"App.xaml\" />", StringComparison.Ordinal)) return "App.xaml is still an ApplicationDefinition";
        if (!project.Contains("<Page Include=\"App.xaml\" />", StringComparison.Ordinal)) return "App.xaml is not a Page";
        if (project.Contains("<ApplicationDefinition Include", StringComparison.Ordinal)) return "the project defines an ApplicationDefinition";
        return xaml.Contains("StartupUri", StringComparison.Ordinal) ? "App.xaml sets StartupUri" : null;
    }

    private static string ProgramWith(string body) =>
        "namespace ShotAI.App;\npublic static class Program\n{\n    [STAThread]\n    public static int Main(string[] args)\n    {\n        " + body + "\n        return 0;\n    }\n}\n";

    private static string StripComments(string code) => Comments().Replace(code, "");

    private static bool IsBuildOutput(string root, string file)
    {
        var rel = Path.GetRelativePath(root, file).Replace('\\', '/');
        return rel.StartsWith("obj/", StringComparison.Ordinal) || rel.StartsWith("bin/", StringComparison.Ordinal);
    }

    [GeneratedRegex(@"(?<attrs>(\[[^\]]*\]\s*)*)public\s+static\s+int\s+Main\s*\([^)]*\)\s*\{")]
    private static partial Regex MainWithBody();

    [GeneratedRegex(@"\bstatic\s+(async\s+)?(void|int|Task(<int>)?)\s+Main\s*\(")]
    private static partial Regex MainDeclaration();

    [GeneratedRegex(@"^if\s*\(\s*!\s*DllSearchHardening\s*\.\s*Apply\s*\(\s*\)\s*\)\s*Environment\s*\.\s*FailFast\s*\(")]
    private static partial Regex HardeningFirst();

    [GeneratedRegex(@"//[^\n]*|/\*.*?\*/", RegexOptions.Singleline)]
    private static partial Regex Comments();
}
