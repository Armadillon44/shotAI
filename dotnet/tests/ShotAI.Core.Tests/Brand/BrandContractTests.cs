using ShotAI.GenBrand;
using Xunit;

namespace ShotAI.Core.Tests.Brand;

/// <summary>
/// The checked-in C# table is the contract's current output and carries its stamp
/// (INV-INFRA-1, INV-INFRA-2, AC-INFRA-3). The CI step runs the same check through the tool.
/// </summary>
public sealed class BrandContractTests
{
    private static string Normalize(string s) => s.Replace("\r\n", "\n", StringComparison.Ordinal);

    [Fact]
    public void GeneratedFileIsCurrent() =>
        Assert.Equal(
            BrandGenerator.Generate(File.ReadAllBytes(BrandFiles.ContractPath)),
            Normalize(File.ReadAllText(BrandFiles.GeneratedPath)));

    [Fact]
    public void StampMatchesCanonicalHash() =>
        Assert.Equal(
            BrandGenerator.ContractHash(File.ReadAllBytes(BrandFiles.ContractPath)),
            BrandFiles.Stamp(File.ReadAllText(BrandFiles.GeneratedPath)));

    /// <summary>Both Windows tables name the same contract while Electron's exists.</summary>
    [Fact]
    public void StampEqualsTypeScriptStamp()
    {
        if (!File.Exists(BrandFiles.TypeScriptPath))
            Assert.Skip("src/shared/brand-colors.generated.ts is gone: the Electron tree was removed at cutover.");
        Assert.Equal(
            BrandFiles.Stamp(File.ReadAllText(BrandFiles.TypeScriptPath)),
            BrandFiles.Stamp(File.ReadAllText(BrandFiles.GeneratedPath)));
    }
}
