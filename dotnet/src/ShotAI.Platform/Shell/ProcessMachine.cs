using System.Runtime.InteropServices;
using ShotAI.Core.Shell;
using Windows.Win32;
using Windows.Win32.System.SystemInformation;

namespace ShotAI.Platform.Shell;

/// <summary>The machines of the runtime line (spec 03 7.3): what this process runs as, and the hardware.</summary>
public static class ProcessMachine
{
    /// <summary>
    /// <c>IsWow64Process2</c> on the current process. A process that is not WOW64 (an x64 process
    /// under ARM64 emulation is not) reports <c>IMAGE_FILE_MACHINE_UNKNOWN</c>, and then
    /// <see cref="RuntimeInformation.ProcessArchitecture"/> says what it runs as.
    /// </summary>
    public static unsafe (Machine Process, Machine Native) Current()
    {
        IMAGE_FILE_MACHINE process, native;
        if (!PInvoke.IsWow64Process2(PInvoke.GetCurrentProcess(), &process, &native))
            return (FromArchitecture(RuntimeInformation.ProcessArchitecture), FromArchitecture(RuntimeInformation.OSArchitecture));
        var asProcess = process == IMAGE_FILE_MACHINE.IMAGE_FILE_MACHINE_UNKNOWN
            ? FromArchitecture(RuntimeInformation.ProcessArchitecture)
            : FromImage(process);
        return (asProcess, FromImage(native));
    }

    private static Machine FromImage(IMAGE_FILE_MACHINE machine) => machine switch
    {
        IMAGE_FILE_MACHINE.IMAGE_FILE_MACHINE_I386 => Machine.X86,
        IMAGE_FILE_MACHINE.IMAGE_FILE_MACHINE_AMD64 => Machine.X64,
        IMAGE_FILE_MACHINE.IMAGE_FILE_MACHINE_ARM64 => Machine.Arm64,
        _ => Machine.Other,
    };

    private static Machine FromArchitecture(Architecture architecture) => architecture switch
    {
        Architecture.X86 => Machine.X86,
        Architecture.X64 => Machine.X64,
        Architecture.Arm64 => Machine.Arm64,
        _ => Machine.Other,
    };
}
