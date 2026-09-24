using System.Runtime.InteropServices;
using Microsoft.Extensions.Logging;
using ShotAI.Core.Capture;
using Windows.Win32;
using Windows.Win32.Foundation;

namespace ShotAI.Platform.Capture;

/// <summary>
/// The shield's Windows half (spec 02 7.8): <c>SetWindowDisplayAffinity</c> on every registered
/// own window, <c>WDA_EXCLUDEFROMCAPTURE</c> or <c>WDA_NONE</c>, through <see cref="CaptureExclusion"/>.
/// </summary>
/// <remarks>
/// The shield calls it under its own lock, from whatever thread holds the shield; the registry's
/// lock is taken only to copy the set, never across a Win32 call. A destroyed window is skipped;
/// a refusal is logged at warning with its Win32 error (IMPROVEMENT: Electron ignores the result).
/// </remarks>
internal sealed partial class DisplayAffinityProtection : IWindowProtection
{
    private readonly OwnWindowRegistry _registry;
    private readonly ILogger<DisplayAffinityProtection> _log;

    public DisplayAffinityProtection(OwnWindowRegistry registry, ILogger<DisplayAffinityProtection> log)
    {
        ArgumentNullException.ThrowIfNull(registry);
        ArgumentNullException.ThrowIfNull(log);
        _registry = registry;
        _log = log;
    }

    /// <inheritdoc/>
    public event EventHandler<nint>? WindowAdded
    {
        add => _registry.Added += value;
        remove => _registry.Added -= value;
    }

    /// <inheritdoc/>
    public void SetAllExcluded(bool excluded)
    {
        foreach (var hwnd in _registry.Snapshot()) Apply(hwnd, excluded);
    }

    /// <inheritdoc/>
    public void SetExcluded(nint hwnd, bool excluded)
    {
        if (_registry.IsRegistered(hwnd)) Apply(hwnd, excluded);
    }

    private void Apply(nint hwnd, bool excluded)
    {
        if (!PInvoke.IsWindow((HWND)hwnd)) return;
        if (CaptureExclusion.Apply(hwnd, excluded)) return;
        var error = Marshal.GetLastPInvokeError();
        // Destroyed between the check and the call: skipped like any destroyed window.
        if (!PInvoke.IsWindow((HWND)hwnd)) return;
        if (excluded) ExclusionRefused(_log, hwnd, error);
        else RelaxRefused(_log, hwnd, error);
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "own window 0x{Hwnd:x}: capture exclusion refused (Win32 error {Error})")]
    private static partial void ExclusionRefused(ILogger logger, long hwnd, int error);

    [LoggerMessage(Level = LogLevel.Warning, Message = "own window 0x{Hwnd:x}: capture exclusion could not be lifted (Win32 error {Error})")]
    private static partial void RelaxRefused(ILogger logger, long hwnd, int error);
}
