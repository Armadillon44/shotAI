using System.Globalization;

namespace ShotAI.Core.Shell;

/// <summary>
/// The runtime line of startup step 12 (spec 03 7.2, 7.9): the diagnostic remnant of Electron's
/// <c>gpu-policy.ts</c>. WPF has no GPU process to switch off, so emulation only shows in the
/// line (03 D17).
/// </summary>
public static class RuntimeDiagnostics
{
    /// <summary>
    /// True when an x86-family process runs on a machine that is not x86-family (x64 under ARM64
    /// emulation). WOW64 (x86 on x64) is not emulation. The rule of <c>isEmulatedOnArm</c>
    /// (<c>gpu-policy.ts:32-43</c>).
    /// </summary>
    public static bool IsEmulated(Machine process, Machine native) =>
        (process is Machine.X64 or Machine.X86) && native is not (Machine.X64 or Machine.X86);

    /// <summary>Electron's <c>process.arch</c> spelling: <c>x86</c>, <c>x64</c>, <c>arm64</c>, <c>other</c>.</summary>
    public static string Name(Machine machine) => machine switch
    {
        Machine.X86 => "x86",
        Machine.X64 => "x64",
        Machine.Arm64 => "arm64",
        _ => "other",
    };

    /// <summary>
    /// <c>runtime: win32/&lt;arch&gt; &#183; native &lt;arch&gt;[ (emulated)] &#183; .NET &lt;v&gt; &#183; os &lt;v&gt; &#183; render tier &lt;n&gt;</c>.
    /// </summary>
    /// <param name="process">The machine the process runs as.</param>
    /// <param name="native">The machine of the hardware.</param>
    /// <param name="dotnet"><c>Environment.Version</c>.</param>
    /// <param name="os"><c>Environment.OSVersion.Version</c>, for example <c>10.0.26100.0</c>.</param>
    /// <param name="renderTier"><c>RenderCapability.Tier &gt;&gt; 16</c>: 0, 1 or 2.</param>
    public static string RuntimeLine(Machine process, Machine native, string dotnet, string os, int renderTier) =>
        string.Create(
            CultureInfo.InvariantCulture,
            $"runtime: win32/{Name(process)} \u00b7 native {Name(native)}{(IsEmulated(process, native) ? " (emulated)" : "")} \u00b7 .NET {dotnet} \u00b7 os {os} \u00b7 render tier {renderTier}");
}
