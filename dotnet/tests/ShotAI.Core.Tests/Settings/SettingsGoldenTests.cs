using System.Text;
using ShotAI.Core.Settings;
using Xunit;

namespace ShotAI.Core.Tests.Settings;

/// <summary>
/// The native codec writes exactly the bytes Electron's settings module writes for the same
/// file (spec 10 7.4.2, INV-INFRA-14, INV-INFRA-15). The expected files come from the Electron
/// generator <c>src/main/settings-golden.test.ts</c>, which runs the real load, mutate and save
/// chain with one <c>setLastUpdateCheckAt(1790000000000)</c>; <c>Golden/settings/README.md</c>
/// says how to regenerate them. Every input is a case where native and Electron agree; the
/// IMPROVEMENTs are <see cref="SettingsCodecTests"/>.
/// </summary>
public sealed class SettingsGoldenTests
{
    /// <summary>The generator's fixed home, so its default projects folder is this string.</summary>
    private const string DefaultDir = "/shotai-home/shotAI Projects";

    private const double Stamp = 1790000000000;

    private static string GoldenDir => Path.Combine(AppContext.BaseDirectory, "Golden", "settings");

    private static string InputsDir => Path.Combine(GoldenDir, "inputs");

    private static string ExpectedDir => Path.Combine(GoldenDir, "expected");

    /// <summary>Every golden name, the list the Electron generator writes: <c>fresh</c> and one per input.</summary>
    public static TheoryData<string> Names()
    {
        var data = new TheoryData<string>();
        foreach (var name in GoldenNames()) data.Add(name);
        return data;
    }

    private static IEnumerable<string> GoldenNames() =>
        Directory.GetFiles(InputsDir, "*.json").Select(Path.GetFileNameWithoutExtension).OfType<string>()
            .Append("fresh").Order(StringComparer.Ordinal);

    // The job's path for one write: decode what is on disk, apply the stamp, encode over it.
    private static byte[] NativeOutput(string name)
    {
        var disk = name == "fresh"
            ? SettingsCodec.Decode(default, missing: true, DefaultDir)
            : SettingsCodec.Decode(File.ReadAllBytes(Path.Combine(InputsDir, name + ".json")), missing: false, DefaultDir);
        var next = disk.Settings with { LastUpdateCheckAt = Stamp };
        return Encoding.UTF8.GetBytes(SettingsCodec.Encode(next, disk));
    }

    [Theory]
    [MemberData(nameof(Names))]
    public void NativeBytesEqualElectronBytes(string name)
    {
        var expected = File.ReadAllBytes(Path.Combine(ExpectedDir, name + ".json"));
        var actual = NativeOutput(name);

        // The text first, for a readable difference; then the bytes, which are what matter.
        Assert.Equal(Encoding.UTF8.GetString(expected), Encoding.UTF8.GetString(actual));
        Assert.Equal(expected, actual);
    }

    /// <summary>A missing or extra expected file means the generator and this list disagree.</summary>
    [Fact]
    public void EveryInputHasExactlyOneExpectedFile()
    {
        var expected = Directory.GetFiles(ExpectedDir, "*.json")
            .Select(Path.GetFileNameWithoutExtension).OfType<string>()
            .Order(StringComparer.Ordinal).ToArray();
        Assert.Equal(GoldenNames().ToArray(), expected);
        Assert.True(expected.Length >= 28, $"only {expected.Length} goldens found under {ExpectedDir}");
    }
}
