using ShotAI.Core.Shell;
using Xunit;

namespace ShotAI.Core.Tests.Shell;

/// <summary>
/// Spec 03 8.1: <c>src/main/gpu-policy.test.ts</c>'s <c>decideGpu</c> group, where it ports. The
/// decision itself is ELECTRON-ONLY (WPF has no GPU process to disable); natively only <c>0</c>
/// forces software rendering. The emulation cases, the macOS and Linux case and the reason strings
/// are ELECTRON-ONLY.
/// </summary>
public sealed class RenderModePolicyTests
{
    /// <summary><c>gpu-policy.test.ts:50-54</c>: <c>=0</c> forces software.</summary>
    [Fact]
    public void ZeroForcesSoftware() => Assert.True(RenderModePolicy.ForceSoftware("0"));

    /// <summary><c>gpu-policy.test.ts:44-48</c>: <c>=1</c> has no effect natively.</summary>
    [Fact]
    public void OneDoesNotForce() => Assert.False(RenderModePolicy.ForceSoftware("1"));

    /// <summary><c>gpu-policy.test.ts:32-36</c>: unset is the default.</summary>
    [Fact]
    public void UnsetDoesNotForce() => Assert.False(RenderModePolicy.ForceSoftware(null));

    /// <summary><c>gpu-policy.test.ts:56-59</c>, its first assertion: <c>yes</c> falls through.</summary>
    [Fact]
    public void UnrecognizedDoesNotForce() => Assert.False(RenderModePolicy.ForceSoftware("yes"));

    [Fact]
    public void EmptyStringDoesNotForce() => Assert.False(RenderModePolicy.ForceSoftware(""));

    /// <summary>Electron compared <c>=== '0'</c> exactly, with no trimming.</summary>
    [Theory]
    [InlineData(" 0")]
    [InlineData("0 ")]
    [InlineData("\t0")]
    public void ZeroWithWhitespaceDoesNotForce(string value) => Assert.False(RenderModePolicy.ForceSoftware(value));

    /// <summary>Nor is any other spelling of zero or false the switch (U+0660 is ARABIC-INDIC DIGIT ZERO).</summary>
    [Theory]
    [InlineData("00")]
    [InlineData("-0")]
    [InlineData("false")]
    [InlineData("\u0660")]
    public void OtherSpellingsOfZeroDoNotForce(string value) => Assert.False(RenderModePolicy.ForceSoftware(value));

    [Fact]
    public void TheVariableIsShotaiEnableGpu() => Assert.Equal("SHOTAI_ENABLE_GPU", RenderModePolicy.Variable);
}
