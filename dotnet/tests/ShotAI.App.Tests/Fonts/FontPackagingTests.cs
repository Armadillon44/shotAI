using System.Security.Cryptography;
using System.Text.RegularExpressions;
using ShotAI.App.Tests.Support;
using Xunit;

namespace ShotAI.App.Tests.Fonts;

/// <summary>
/// Spec 10 INV-INFRA-31 and INV-INFRA-32, 7.9: every copy of an Archivo file has the licence in its
/// folder, the variable face is Electron's byte for byte, and the static faces are the upstream
/// files <c>SOURCES.md</c> names. Checked on the repository, on the App's build output and on
/// this test's output, which carries the App's layout; 12 adds the installer payload.
/// </summary>
public sealed partial class FontPackagingTests
{
    /// <summary>spec 10 2.10: the variable file Electron and macOS ship.</summary>
    private const string ArchivoSha256 = "0e094a7d3c7c4c25cf1310c4b30014f1dae9332220b1c2c88f4fa996f0b05053";

    private static string ElectronFonts => Path.Combine(RepoFiles.Root, "src", "renderer", "fonts");

    private static string StaticSources => Path.Combine(RepoFiles.Root, "dotnet", "assets", "fonts", "static");

    public static TheoryData<string> Outputs() => [AppOutput(), Path.Combine(AppContext.BaseDirectory, "Fonts")];

    [Theory]
    [MemberData(nameof(Outputs))]
    public void OflBesideEveryFont(string fonts)
    {
        var licence = File.ReadAllBytes(Path.Combine(ElectronFonts, "OFL.txt"));
        var files = Directory.GetFiles(fonts, "*.ttf", SearchOption.AllDirectories);
        Assert.Equal(8, files.Length);
        foreach (var folder in files.Select(Path.GetDirectoryName).Distinct())
        {
            var ofl = Path.Combine(folder!, "OFL.txt");
            Assert.True(File.Exists(ofl), $"{folder} has fonts and no OFL.txt");
            Assert.Equal(licence, File.ReadAllBytes(ofl));
        }
    }

    [Fact]
    public void OflBesideTheRepositorysStaticFonts() =>
        Assert.Equal(File.ReadAllBytes(Path.Combine(ElectronFonts, "OFL.txt")), File.ReadAllBytes(Path.Combine(StaticSources, "OFL.txt")));

    /// <summary>The licence copies are the upstream text, LF on every checkout (<c>.gitattributes</c>).</summary>
    [Fact]
    public void OflIsTheUpstreamText() =>
        Assert.Equal(Hash(File.ReadAllBytes(Path.Combine(StaticSources, "OFL.txt"))), Sources()["OFL.txt"]);

    [Theory]
    [MemberData(nameof(Outputs))]
    public void ArchivoMatchesElectronHash(string fonts)
    {
        var shipped = File.ReadAllBytes(Path.Combine(fonts, "Archivo.ttf"));
        Assert.Equal(ArchivoSha256, Hash(shipped));
        Assert.Equal(File.ReadAllBytes(Path.Combine(ElectronFonts, "Archivo.ttf")), shipped);
    }

    /// <summary>The static files are the ones <c>SOURCES.md</c> records, every one of them and nothing else.</summary>
    [Fact]
    public void StaticFontsMatchTheirSources()
    {
        var recorded = Sources().Where(r => r.Key.EndsWith(".ttf", StringComparison.Ordinal)).ToDictionary(r => r.Key, r => r.Value);
        Assert.Equal(7, recorded.Count);
        var present = Directory.GetFiles(StaticSources, "*.ttf").ToDictionary(f => Path.GetFileName(f), f => Hash(File.ReadAllBytes(f)));
        Assert.Equal(recorded.OrderBy(r => r.Key, StringComparer.Ordinal), present.OrderBy(r => r.Key, StringComparer.Ordinal));
    }

    [Theory]
    [MemberData(nameof(Outputs))]
    public void TheOutputCarriesTheStaticFonts(string fonts)
    {
        foreach (var source in Directory.GetFiles(StaticSources, "*.ttf"))
            Assert.Equal(File.ReadAllBytes(source), File.ReadAllBytes(Path.Combine(fonts, "static", Path.GetFileName(source))));
    }

    // The file and sha256 columns of SOURCES.md's table.
    private static Dictionary<string, string> Sources() =>
        SourceRow().Matches(File.ReadAllText(Path.Combine(StaticSources, "SOURCES.md")))
            .ToDictionary(m => m.Groups["file"].Value, m => m.Groups["hash"].Value, StringComparer.Ordinal);

    private static string Hash(byte[] bytes) => Convert.ToHexStringLower(SHA256.HashData(bytes));

    // The App's own output, which the solution build puts beside this project's.
    private static string AppOutput()
    {
        var here = new DirectoryInfo(Path.TrimEndingDirectorySeparator(AppContext.BaseDirectory));
        return Path.Combine(RepoFiles.Root, "dotnet", "src", "ShotAI.App", "bin", here.Parent!.Name, here.Name, "Fonts");
    }

    // A Windows checkout has CRLF line ends here.
    [GeneratedRegex(@"^\| `(?<file>[^`]+)` \|.*\| `(?<hash>[0-9a-f]{64})` \|\r?$", RegexOptions.Multiline)]
    private static partial Regex SourceRow();
}
