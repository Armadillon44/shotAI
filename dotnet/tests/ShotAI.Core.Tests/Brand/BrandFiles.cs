namespace ShotAI.Core.Tests.Brand;

/// <summary>
/// The brand files the test project links into its output: the contract (read in place from
/// the repository root, never copied) and the two checked-in tables. The tool's project file
/// is read from the source tree.
/// </summary>
internal static class BrandFiles
{
    /// <summary>The generator's project file (EDGE-INFRA-48).</summary>
    public static string ToolProjectPath =>
        Path.Combine(RepoRoot(), "dotnet", "tools", "ShotAI.GenBrand", "ShotAI.GenBrand.csproj");

    public static string ContractPath => Path.Combine(AppContext.BaseDirectory, "contract", "brand.json");

    public static string GeneratedPath => Path.Combine(AppContext.BaseDirectory, "brand", "BrandPalette.Generated.cs");

    /// <summary>Electron's table; absent after cutover, when the Electron tree is deleted.</summary>
    public static string TypeScriptPath => Path.Combine(AppContext.BaseDirectory, "brand", "brand-colors.generated.ts");

    /// <summary>The <c>// contract sha256: </c> value of a generated table.</summary>
    public static string Stamp(string tableText)
    {
        const string prefix = "// contract sha256: ";
        var line = tableText.Split('\n').Single(l => l.StartsWith(prefix, StringComparison.Ordinal));
        return line[prefix.Length..].TrimEnd('\r');
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
