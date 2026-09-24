using ShotAI.Core.Capture;
using ShotAI.Core.Model;

namespace ShotAI.Platform.Capture;

/// <summary>
/// The foreground window, the window list and window targets (spec 02 2.10, 7.5): Core's
/// get-windows rules (<see cref="WindowDescriber"/>) over the system's answers
/// (<see cref="Win32WindowFacts"/>). Callable from any thread.
/// </summary>
internal sealed class Win32WindowInfoProvider : IWindowInfoProvider
{
    private readonly WindowDescriber _describer = new(new Win32WindowFacts());

    /// <inheritdoc/>
    public ForegroundInfo? Foreground() => _describer.Foreground();

    /// <inheritdoc/>
    public IReadOnlyList<ListedWindow> ListWindows() => _describer.ListWindows();

    /// <inheritdoc/>
    public ListedWindow? Resolve(CaptureTargetWindow target) => _describer.Resolve(target);
}
