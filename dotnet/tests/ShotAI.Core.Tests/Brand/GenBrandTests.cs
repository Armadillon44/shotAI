using System.Globalization;
using System.Text;
using System.Text.Json.Nodes;
using System.Xml.Linq;
using ShotAI.GenBrand;
using Xunit;

namespace ShotAI.Core.Tests.Brand;

/// <summary>
/// The C# brand generator (spec 10 7.3): the rules and messages of <c>scripts/gen-brand.mjs</c>
/// (2.2) plus the IMPROVEMENT rows, and the command's write and check modes.
/// </summary>
public sealed class GenBrandTests
{
    private static readonly string EmDash = ((char)0x2014).ToString();

    private const string Why =
        "a text field the same colour as the card stops reading as an input on an elevated surface. " +
        "Windows shipped fieldBg == surface2 in dark; this rule is why that cannot come back.";

    private static byte[] ContractBytes => File.ReadAllBytes(BrandFiles.ContractPath);

    private static JsonObject Contract() => JsonNode.Parse(ContractBytes)!.AsObject();

    private static JsonObject Brand(JsonObject contract, string id) => contract["brands"]![id]!.AsObject();

    private static string Gen(JsonObject contract) => BrandGenerator.Generate(Encoding.UTF8.GetBytes(contract.ToJsonString()));

    private static string Error(JsonObject contract) => Assert.Throws<GenBrandException>(() => Gen(contract)).Message;

    private static string Error(byte[] bytes) => Assert.Throws<GenBrandException>(() => BrandGenerator.Generate(bytes)).Message;

    [Fact]
    public void GeneratesCheckedInFile()
    {
        using var repo = new ScratchRepo(ContractBytes);
        var (code, stdout, stderr) = repo.Run();
        Assert.Equal(0, code);
        Assert.Equal("gen-brand: wrote BrandPalette.Generated.cs\n", stdout);
        Assert.Equal("", stderr);
        Assert.Equal(File.ReadAllBytes(BrandFiles.GeneratedPath), File.ReadAllBytes(repo.OutputPath));
    }

    /// <summary>A CRLF checkout hashes and generates exactly as the LF file (EDGE-INFRA-1).</summary>
    [Fact]
    public void HashIgnoresCrlf()
    {
        var lf = ContractBytes;
        Assert.DoesNotContain((byte)'\r', lf);
        var crlf = Encoding.UTF8.GetBytes(Encoding.UTF8.GetString(lf).Replace("\n", "\r\n", StringComparison.Ordinal));
        Assert.Equal(BrandGenerator.ContractHash(lf), BrandGenerator.ContractHash(crlf));
        Assert.Equal(BrandGenerator.Generate(lf), BrandGenerator.Generate(crlf));
        Assert.Equal(64, BrandGenerator.ContractHash(lf).Length);
    }

    [Fact]
    public void CheckReportsStale()
    {
        using var repo = new ScratchRepo(ContractBytes);
        repo.WriteExisting(File.ReadAllText(BrandFiles.GeneratedPath).Replace("\"#6344f1\"", "\"#6344f2\"", StringComparison.Ordinal));
        var (code, stdout, stderr) = repo.Run("--check");
        Assert.Equal(1, code);
        Assert.Equal("", stdout);
        Assert.Equal(
            "gen-brand: BrandPalette.Generated.cs is STALE.\nRun `dotnet run --project dotnet/tools/ShotAI.GenBrand` and commit the result.\n",
            stderr);
    }

    [Fact]
    public void CheckTreatsAMissingTableAsStale()
    {
        using var repo = new ScratchRepo(ContractBytes);
        var (code, _, stderr) = repo.Run("--check");
        Assert.Equal(1, code);
        Assert.StartsWith("gen-brand: BrandPalette.Generated.cs is STALE.", stderr, StringComparison.Ordinal);
    }

    /// <summary>A CRLF worktree copy is not stale forever (EDGE-INFRA-2).</summary>
    [Fact]
    public void CheckIgnoresCrlfInExisting()
    {
        using var repo = new ScratchRepo(ContractBytes);
        repo.WriteExisting(File.ReadAllText(BrandFiles.GeneratedPath).Replace("\n", "\r\n", StringComparison.Ordinal));
        var (code, stdout, stderr) = repo.Run("--check");
        Assert.Equal(0, code);
        Assert.Equal("gen-brand: up to date\n", stdout);
        Assert.Equal("", stderr);
    }

    /// <summary>AC-INFRA-2: one colour changes that value's line and the stamp line, nothing else.</summary>
    [Fact]
    public void OneColourChangesExactlyItsLineAndTheStamp()
    {
        var contract = Contract();
        Brand(contract, "lfi")["colors"]!["accent"]!["dark"] = "#010203";
        var before = BrandGenerator.Generate(ContractBytes).Split('\n');
        var after = Gen(contract).Split('\n');
        Assert.Equal(before.Length, after.Length);
        var changed = before.Zip(after).Where(p => p.First != p.Second).ToArray();
        Assert.Equal(2, changed.Length);
        Assert.StartsWith("// contract sha256: ", changed[0].Second, StringComparison.Ordinal);
        Assert.Equal("            Accent: \"#010203\",", changed[1].Second);
    }

    /// <summary>AC-INFRA-5: the invariant is checked at generation, for every brand.</summary>
    [Fact]
    public void InvariantViolationFails()
    {
        var contract = Contract();
        Brand(contract, "shotAI")["colors"]!["field"]!["dark"] = "#211f2e";
        var expected = "invariant violated " + EmDash + " shotAI.dark: field and surface2 are both #211f2e.\n  " + Why;
        Assert.Equal(expected, Error(contract));

        using var repo = new ScratchRepo(Encoding.UTF8.GetBytes(contract.ToJsonString()));
        var (code, stdout, stderr) = repo.Run();
        Assert.Equal(1, code);
        Assert.Equal("", stdout);
        Assert.Equal("gen-brand: " + expected + "\n", stderr);
        Assert.False(File.Exists(repo.OutputPath), "nothing is written");
    }

    [Fact]
    public void InvariantCoversABrandThatIsNotEmitted()
    {
        var contract = Contract();
        var third = Brand(contract, "lfi").DeepClone().AsObject();
        third["colors"]!["field"]!["dark"] = third["colors"]!["surface2"]!["dark"]!.GetValue<string>();
        contract["brands"]!["third"] = third;
        Assert.StartsWith("invariant violated " + EmDash + " third.dark: field and surface2 are both ", Error(contract), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("\"#6344F1\"", "\"#6344F1\"")]
    [InlineData("\"#fff\"", "\"#fff\"")]
    [InlineData("\"#6344f1 \"", "\"#6344f1 \"")]
    [InlineData("5", "5")]
    [InlineData("null", "null")]
    public void RejectsUppercaseOrShortHex(string value, string printed)
    {
        var contract = Contract();
        Brand(contract, "shotAI")["colors"]!["accent"]!["light"] = JsonNode.Parse(value);
        Assert.Equal("shotAI.accent.light: expected a lowercase 6-digit #rrggbb, got " + printed, Error(contract));
    }

    [Fact]
    public void MissingAppearanceValuePrintsUndefined()
    {
        var contract = Contract();
        Brand(contract, "lfi")["colors"]!["ink"]!.AsObject().Remove("dark");
        Assert.Equal("lfi.ink.dark: expected a lowercase 6-digit #rrggbb, got undefined", Error(contract));
    }

    /// <summary>EDGE-INFRA-43: a .NET <c>$</c> would accept this value.</summary>
    [Fact]
    public void HexRejectsTrailingNewline()
    {
        var contract = Contract();
        Brand(contract, "shotAI")["colors"]!["accent"]!["light"] = "#6344f1\n";
        Assert.Equal("shotAI.accent.light: expected a lowercase 6-digit #rrggbb, got \"#6344f1\\n\"", Error(contract));
    }

    [Fact]
    public void MissingTokenFails()
    {
        var contract = Contract();
        Brand(contract, "lfi")["colors"]!.AsObject().Remove("dangerBd");
        Assert.Equal("lfi: no colour \"dangerBd\" in the contract", Error(contract));
    }

    [Fact]
    public void MissingBrandFails()
    {
        var contract = Contract();
        contract["brands"]!.AsObject().Remove("lfi");
        Assert.Equal("brand lfi missing from the contract", Error(contract));
    }

    /// <summary>Electron's first-error order: shotAI fully before lfi; missing, radii, font, then colours.</summary>
    [Fact]
    public void ErrorOrderMatchesElectron()
    {
        var contract = Contract();
        Brand(contract, "shotAI")["colors"]!["accent"]!["dark"] = "#BAD000";
        contract["brands"]!.AsObject().Remove("lfi");
        Assert.Equal("shotAI.accent.dark: expected a lowercase 6-digit #rrggbb, got \"#BAD000\"", Error(contract));

        Brand(contract, "shotAI")["colors"]!["accent"]!["light"] = "#BAD000";
        Assert.Equal("shotAI.accent.light: expected a lowercase 6-digit #rrggbb, got \"#BAD000\"", Error(contract));

        Brand(contract, "shotAI").Remove("font");
        Assert.Equal("shotAI: no font", Error(contract));

        Brand(contract, "shotAI").Remove("radii");
        Assert.Equal("shotAI: no radii", Error(contract));
    }

    [Fact]
    public void PlatformValueTakesWindows()
    {
        Assert.Contains("Control: 8,", BrandGenerator.Generate(ContractBytes), StringComparison.Ordinal);
        var contract = Contract();
        Brand(contract, "shotAI")["radii"]!["control"] = JsonNode.Parse("""{"macos": 1, "windows": 3}""");
        Assert.Contains("Control: 3,", Gen(contract), StringComparison.Ordinal);
    }

    /// <summary>EDGE-INFRA-41: Electron would silently write a capsule.</summary>
    [Fact]
    public void MissingWindowsValueFails()
    {
        var contract = Contract();
        Brand(contract, "shotAI")["radii"]!["control"] = JsonNode.Parse("""{"macos": 6}""");
        Assert.Equal("shotAI.radii.control: no windows value", Error(contract));
    }

    [Theory]
    [InlineData("null")]
    [InlineData("\"8\"")]
    [InlineData("true")]
    [InlineData("1e400")]
    public void NullNonChipRadiusFails(string value)
    {
        var contract = Contract();
        Brand(contract, "lfi")["radii"]!["card"] = JsonNode.Parse(value);
        Assert.Equal("lfi.radii.card: must be a number", Error(contract));

        var chipNull = Contract();
        Brand(chipNull, "lfi")["radii"]!["chip"] = null;
        Assert.Contains("Chip: null),", Gen(chipNull).Split("Lfi = new(")[1], StringComparison.Ordinal);
    }

    [Fact]
    public void ThirdBrandIgnored()
    {
        var contract = Contract();
        contract["brands"]!["third"] = Brand(contract, "lfi").DeepClone();
        static string WithoutStamp(string s) =>
            string.Join('\n', s.Split('\n').Where(l => !l.StartsWith("// contract sha256: ", StringComparison.Ordinal)));
        Assert.Equal(WithoutStamp(BrandGenerator.Generate(ContractBytes)), WithoutStamp(Gen(contract)));
    }

    /// <summary>A culture with a decimal comma changes nothing in the C# the tool writes.</summary>
    [Fact]
    public void NumbersFormatInvariant()
    {
        var contract = Contract();
        Brand(contract, "shotAI")["radii"]!["micro"] = 0.5;
        var saved = CultureInfo.CurrentCulture;
        try
        {
            var comma = (CultureInfo)CultureInfo.InvariantCulture.Clone();
            comma.NumberFormat.NumberDecimalSeparator = ",";
            comma.NumberFormat.NumberGroupSeparator = ".";
            CultureInfo.CurrentCulture = comma;
            var generated = Gen(contract);
            Assert.Contains("Panel: 12, Card: 10,", generated, StringComparison.Ordinal);
            Assert.Contains("Micro: 0.5,", generated, StringComparison.Ordinal);
            Assert.Contains("LabelStretch: 62)", generated, StringComparison.Ordinal);
        }
        finally
        {
            CultureInfo.CurrentCulture = saved;
        }
    }

    /// <summary>
    /// EDGE-INFRA-48: the tool must build when the table it writes does not, so it references
    /// no project and no package.
    /// </summary>
    [Fact]
    public void ToolHasNoProjectReference()
    {
        var items = XDocument.Load(BrandFiles.ToolProjectPath).Descendants().Select(e => e.Name.LocalName).ToArray();
        Assert.Contains("OutputType", items);
        Assert.DoesNotContain("ProjectReference", items);
        Assert.DoesNotContain("PackageReference", items);
        Assert.DoesNotContain(
            typeof(BrandGenerator).Assembly.GetReferencedAssemblies(),
            a => a.Name!.StartsWith("ShotAI", StringComparison.Ordinal));
    }

    [Fact]
    public void DeterministicOutput()
    {
        var first = BrandGenerator.Generate(ContractBytes);
        Assert.Equal(first, BrandGenerator.Generate(ContractBytes));
        Assert.DoesNotContain('\r', first);
        Assert.EndsWith("}\n", first, StringComparison.Ordinal);
        Assert.False(first.EndsWith("\n\n", StringComparison.Ordinal));
        Assert.DoesNotContain(" \n", first, StringComparison.Ordinal);
    }

    [Fact]
    public void ContractThatIsNotUtf8OrNotJsonFails()
    {
        Assert.Equal("contract/brand.json is not valid UTF-8", Error([(byte)'{', 0xFF, (byte)'}']));
        Assert.Equal("cannot parse contract/brand.json: it starts with a UTF-8 byte order mark", Error([0xEF, 0xBB, 0xBF, (byte)'{', (byte)'}']));
        Assert.StartsWith("cannot parse contract/brand.json: ", Error("{\"brands\": }"u8.ToArray()), StringComparison.Ordinal);
        Assert.Equal("spec has no platforms.windows.colors", Error("{}"u8.ToArray()));
        Assert.Equal("spec has no platforms.windows.colors", Error("[]"u8.ToArray()));
    }

    [Fact]
    public void ColoursMustBeAListOfNames()
    {
        var contract = Contract();
        contract["platforms"]!["windows"]!["colors"] = JsonNode.Parse("""{"accent": 1}""");
        Assert.Equal("spec has no platforms.windows.colors", Error(contract));

        contract["platforms"]!["windows"]!["colors"] = JsonNode.Parse("[\"accent\", 5]");
        Assert.Equal("spec has no platforms.windows.colors", Error(contract));
    }

    /// <summary>
    /// A token or a rename becomes a C# identifier, so anything but a plain name is refused
    /// instead of being written into Core as code.
    /// </summary>
    [Fact]
    public void NamesThatWouldInjectCodeAreRefused()
    {
        var contract = Contract();
        contract["platforms"]!["windows"]!["colors"]!.AsArray().Add("x = \"\"; //");
        Assert.Equal("platforms.windows.colors: \"x = \\\"\\\"; //\" is not a token name", Error(contract));

        var renamed = Contract();
        renamed["windowsRenames"]!["field"] = "FieldBg";
        Assert.Equal("windowsRenames.field: \"FieldBg\" is not a role name", Error(renamed));
    }

    [Fact]
    public void LabelAndFontFieldsAreTyped()
    {
        var contract = Contract();
        Brand(contract, "lfi").Remove("label");
        Assert.Equal("lfi: no label", Error(contract));

        contract = Contract();
        Brand(contract, "lfi")["font"]!["family"] = 5;
        Assert.Equal("lfi.font.family: must be a string or null", Error(contract));

        contract = Contract();
        Brand(contract, "lfi")["font"]!["fallbacks"] = JsonNode.Parse("[\"Arial\", 5]");
        Assert.Equal("lfi.font.fallbacks: must be an array of strings", Error(contract));

        contract = Contract();
        Brand(contract, "lfi")["font"]!["labelStretch"] = "62%";
        Assert.Equal("lfi.font.labelStretch: must be a number or null", Error(contract));
    }

    /// <summary>A string that reaches the file is an escaped C# literal, line terminators included.</summary>
    [Fact]
    public void StringsAreEscapedCSharpLiterals()
    {
        var contract = Contract();
        Brand(contract, "lfi")["label"] = "L\"F\\I" + (char)0x2028 + "\t";
        Assert.Contains("Label: \"L\\\"F\\\\I\\u2028\\u0009\",", Gen(contract), StringComparison.Ordinal);
    }

    [Fact]
    public void TheRootIsFoundFromBelowAndMustExist()
    {
        using var repo = new ScratchRepo(ContractBytes);
        var below = Directory.CreateDirectory(Path.Combine(repo.Root, "dotnet", "tools")).FullName;
        Assert.Equal(0, repo.RunIn(below).Code);
        Assert.True(File.Exists(repo.OutputPath));

        using var empty = new ScratchRepo(null);
        var (code, stdout, stderr) = empty.RunIn(empty.Root);
        Assert.Equal(1, code);
        Assert.Equal("", stdout);
        Assert.Equal("gen-brand: cannot find the repository root (contract/brand.json)\n", stderr);
    }

    /// <summary>A throwaway repository root with a contract, the slnx marker and an optional table.</summary>
    private sealed class ScratchRepo : IDisposable
    {
        public ScratchRepo(byte[]? contract)
        {
            Root = Directory.CreateTempSubdirectory("shotai-genbrand-").FullName;
            if (contract is null) return;
            Directory.CreateDirectory(Path.Combine(Root, "contract"));
            File.WriteAllBytes(Path.Combine(Root, "contract", "brand.json"), contract);
            Directory.CreateDirectory(Path.Combine(Root, "dotnet"));
            File.WriteAllText(Path.Combine(Root, "dotnet", "ShotAI.slnx"), "<Solution />");
        }

        public string Root { get; }

        public string OutputPath => Path.Combine(Root, BrandGenerator.OutputRelativePath);

        public void WriteExisting(string text)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(OutputPath)!);
            File.WriteAllText(OutputPath, text);
        }

        public (int Code, string Stdout, string Stderr) Run(params string[] args) =>
            Invoke([.. args, "--root", Root], Root);

        public (int Code, string Stdout, string Stderr) RunIn(string workingDirectory) => Invoke([], workingDirectory);

        private static (int, string, string) Invoke(string[] args, string workingDirectory)
        {
            using var stdout = new StringWriter();
            using var stderr = new StringWriter();
            var code = Program.Run(args, stdout, stderr, workingDirectory);
            return (code, stdout.ToString(), stderr.ToString());
        }

        public void Dispose() => Directory.Delete(Root, recursive: true);
    }
}
