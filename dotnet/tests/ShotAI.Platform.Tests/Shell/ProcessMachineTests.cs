using System.Runtime.InteropServices;
using ShotAI.Core.Shell;
using ShotAI.Platform.Shell;
using Xunit;

namespace ShotAI.Platform.Tests.Shell;

/// <summary>Spec 03 8.3: the machines of the runtime line.</summary>
public sealed class ProcessMachineTests
{
    /// <summary>
    /// The process machine is what the runtime reports; the native machine is the OS's, so an
    /// x64 build on ARM64 hardware reports ARM64.
    /// </summary>
    [Fact]
    public void CurrentMatchesRuntimeInformation()
    {
        var (process, native) = ProcessMachine.Current();
        Assert.Equal(Map(RuntimeInformation.ProcessArchitecture), process);
        Assert.Equal(Map(RuntimeInformation.OSArchitecture), native);
    }

    /// <summary>The test runners are native x64 and native ARM64, so neither is emulated.</summary>
    [Fact]
    public void TheRunnersAreNotEmulated()
    {
        var (process, native) = ProcessMachine.Current();
        Assert.False(RuntimeDiagnostics.IsEmulated(process, native));
    }

    private static Machine Map(Architecture a) => a switch
    {
        Architecture.X86 => Machine.X86,
        Architecture.X64 => Machine.X64,
        Architecture.Arm64 => Machine.Arm64,
        _ => Machine.Other,
    };
}
