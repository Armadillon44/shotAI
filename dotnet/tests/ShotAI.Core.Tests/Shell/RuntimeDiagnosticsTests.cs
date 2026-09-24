using System.Globalization;
using ShotAI.Core.Shell;
using Xunit;

namespace ShotAI.Core.Tests.Shell;

/// <summary>
/// Spec 03 8.1: <c>src/main/gpu-policy.test.ts</c>'s <c>isEmulatedOnArm</c> group on machine
/// enums, and the runtime line of 7.9. The platform case (<c>:21-24</c>) and the case-insensitivity
/// case (<c>:26-28</c>) are ELECTRON-ONLY: Core runs only on Windows here, and enums have no case.
/// </summary>
public sealed class RuntimeDiagnosticsTests
{
    private const string Dot = " \u00b7 ";

    /// <summary><c>gpu-policy.test.ts:5-7</c>.</summary>
    [Fact]
    public void NativeX64IsNotEmulated() => Assert.False(RuntimeDiagnostics.IsEmulated(Machine.X64, Machine.X64));

    /// <summary><c>gpu-policy.test.ts:9-11</c>.</summary>
    [Fact]
    public void X64OnArm64IsEmulated() => Assert.True(RuntimeDiagnostics.IsEmulated(Machine.X64, Machine.Arm64));

    /// <summary><c>gpu-policy.test.ts:13-15</c>: WOW64, the native machine is x64.</summary>
    [Fact]
    public void X86OnX64IsWow64NotEmulation() => Assert.False(RuntimeDiagnostics.IsEmulated(Machine.X86, Machine.X64));

    /// <summary><c>gpu-policy.test.ts:17-19</c>.</summary>
    [Fact]
    public void NativeArm64IsNotEmulated() => Assert.False(RuntimeDiagnostics.IsEmulated(Machine.Arm64, Machine.Arm64));

    /// <summary>New: an x86 process on ARM64 is emulated too.</summary>
    [Fact]
    public void X86OnArm64IsEmulated() => Assert.True(RuntimeDiagnostics.IsEmulated(Machine.X86, Machine.Arm64));

    /// <summary>
    /// Every pair: emulated exactly when an x86-family process runs on a machine that is not
    /// x86-family.
    /// </summary>
    [Theory]
    [InlineData(Machine.X86, Machine.X86, false)]
    [InlineData(Machine.X86, Machine.X64, false)]
    [InlineData(Machine.X86, Machine.Arm64, true)]
    [InlineData(Machine.X86, Machine.Other, true)]
    [InlineData(Machine.X64, Machine.X86, false)]
    [InlineData(Machine.X64, Machine.X64, false)]
    [InlineData(Machine.X64, Machine.Arm64, true)]
    [InlineData(Machine.X64, Machine.Other, true)]
    [InlineData(Machine.Arm64, Machine.X86, false)]
    [InlineData(Machine.Arm64, Machine.X64, false)]
    [InlineData(Machine.Arm64, Machine.Arm64, false)]
    [InlineData(Machine.Arm64, Machine.Other, false)]
    [InlineData(Machine.Other, Machine.X86, false)]
    [InlineData(Machine.Other, Machine.X64, false)]
    [InlineData(Machine.Other, Machine.Arm64, false)]
    [InlineData(Machine.Other, Machine.Other, false)]
    public void EmulatedOnlyForX86FamilyOnOtherHardware(Machine process, Machine native, bool emulated) =>
        Assert.Equal(emulated, RuntimeDiagnostics.IsEmulated(process, native));

    /// <summary>Electron's <c>process.arch</c> spelling, which the banner and About use too.</summary>
    [Theory]
    [InlineData(Machine.X86, "x86")]
    [InlineData(Machine.X64, "x64")]
    [InlineData(Machine.Arm64, "arm64")]
    [InlineData(Machine.Other, "other")]
    [InlineData((Machine)42, "other")]
    public void Names(Machine machine, string name) => Assert.Equal(name, RuntimeDiagnostics.Name(machine));

    [Fact]
    public void RuntimeLineOnNativeHardware() =>
        Assert.Equal(
            "runtime: win32/x64" + Dot + "native x64" + Dot + ".NET 10.0.3" + Dot + "os 10.0.26100.0" + Dot + "render tier 2",
            RuntimeDiagnostics.RuntimeLine(Machine.X64, Machine.X64, "10.0.3", "10.0.26100.0", 2));

    [Fact]
    public void RuntimeLineMarksEmulation() =>
        Assert.Equal(
            "runtime: win32/x64" + Dot + "native arm64 (emulated)" + Dot + ".NET 10.0.3" + Dot + "os 10.0.26100.0" + Dot + "render tier 0",
            RuntimeDiagnostics.RuntimeLine(Machine.X64, Machine.Arm64, "10.0.3", "10.0.26100.0", 0));

    [Fact]
    public void RuntimeLineOnArm64() =>
        Assert.Equal(
            "runtime: win32/arm64" + Dot + "native arm64" + Dot + ".NET 10.0.3" + Dot + "os 10.0.22631.0" + Dot + "render tier 1",
            RuntimeDiagnostics.RuntimeLine(Machine.Arm64, Machine.Arm64, "10.0.3", "10.0.22631.0", 1));

    /// <summary>The separators are U+00B7 with a space each side, as 03 7.9 writes them.</summary>
    [Fact]
    public void SeparatorsAreMiddleDots()
    {
        var line = RuntimeDiagnostics.RuntimeLine(Machine.X64, Machine.X64, "v", "o", 2);
        Assert.Equal(4, line.Count(c => c == '\u00b7'));
        Assert.DoesNotContain('\u2022', line);
    }

    /// <summary>The number is invariant: a culture whose minus sign is U+2212 still writes <c>-1</c>.</summary>
    [Fact]
    public void TierIsInvariant()
    {
        var culture = CultureInfo.GetCultureInfo("sv-SE");
        Assert.SkipUnless(culture.NumberFormat.NegativeSign != "-", "this runtime's sv-SE uses the ASCII minus sign");
        var saved = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = culture;
            Assert.EndsWith("render tier -1", RuntimeDiagnostics.RuntimeLine(Machine.X64, Machine.X64, "v", "o", -1), StringComparison.Ordinal);
        }
        finally
        {
            CultureInfo.CurrentCulture = saved;
        }
    }
}
