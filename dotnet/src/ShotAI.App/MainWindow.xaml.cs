using System.Windows;
using System.Windows.Interop;
using ShotAI.Platform;

namespace ShotAI.App;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
    }

    /// <summary>
    /// Excludes the window from capture as soon as it has an HWND, before it is shown.
    /// </summary>
    /// <remarks>
    /// Fail-closed ordering, as in the Electron build: every window starts protected and
    /// is only relaxed afterwards if the remote-visibility setting says so. The reverse
    /// order would leave it briefly capturable on every launch.
    /// </remarks>
    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        CaptureExclusion.Apply(new WindowInteropHelper(this).Handle, excluded: true);
    }
}
