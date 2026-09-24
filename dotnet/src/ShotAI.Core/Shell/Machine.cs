namespace ShotAI.Core.Shell;

/// <summary>
/// A processor architecture as <c>IsWow64Process2</c> reports it, for the runtime line of startup
/// step 12 (spec 03 7.2). Platform's <c>ProcessMachine.Current</c> reads the pair.
/// </summary>
public enum Machine
{
    /// <summary>32-bit x86.</summary>
    X86,

    /// <summary>x64 (AMD64).</summary>
    X64,

    /// <summary>ARM64.</summary>
    Arm64,

    /// <summary>Anything else.</summary>
    Other,
}
