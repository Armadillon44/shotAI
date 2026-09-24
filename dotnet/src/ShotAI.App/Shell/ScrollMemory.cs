using System.Windows.Controls;

namespace ShotAI.App.Shell;

/// <summary>
/// A view's scroll offset across views (spec 06 2.19, 7.7, INV-HOME-19): recorded as the view
/// scrolls while it is shown, never when it changes, because by then shorter content may have
/// clamped it; and restored after the first layout pass on the return, where WPF clamps it to
/// the content as it is then.
/// </summary>
internal sealed class ScrollMemory
{
    private readonly ScrollViewer _viewer;
    private bool _recording;

    /// <summary>The memory of <paramref name="viewer"/>'s vertical offset, from 0.</summary>
    public ScrollMemory(ScrollViewer viewer)
    {
        ArgumentNullException.ThrowIfNull(viewer);
        _viewer = viewer;
        viewer.ScrollChanged += (_, e) =>
        {
            if (_recording) Saved = e.VerticalOffset;
        };
    }

    /// <summary>The offset to restore on the return.</summary>
    public double Saved { get; private set; }

    /// <summary>The view left the screen: nothing it does from now on moves the saved offset.</summary>
    public void Leave() => _recording = false;

    /// <summary>The view is back: once its content is laid out the saved offset is restored, then recorded again as it scrolls.</summary>
    public void Enter()
    {
        _recording = false;
        _viewer.LayoutUpdated -= OnLayoutUpdated;
        _viewer.LayoutUpdated += OnLayoutUpdated;
        // A view shown again is laid out anyway; this makes sure a pass follows every entry.
        _viewer.InvalidateArrange();
    }

    private void OnLayoutUpdated(object? sender, EventArgs e)
    {
        _viewer.LayoutUpdated -= OnLayoutUpdated;
        _viewer.ScrollToVerticalOffset(Saved);
        _recording = true;
    }
}
