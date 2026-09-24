using System.Windows.Interop;

namespace ShotAI.App.Shell;

/// <summary>
/// The main window (spec 03 7.4.2). Closing it ends the process (<c>ShutdownMode</c> in
/// <c>App.xaml</c>, INV-SHELL-4), so it has no <c>Closing</c> handler that could cancel. Its size,
/// menu and placement join in WP-A14.
/// </summary>
public partial class MainWindow : ShotAIWindow
{
    private bool _closed;

    /// <summary>The window, registered with the own-window registry before it is first shown.</summary>
    public MainWindow(WindowRegistration registration)
        : base(registration)
    {
        InitializeComponent();
    }

    /// <summary>
    /// A second launch surfaces this window (2.2, 7.4.8): shown if hidden, restored if minimized,
    /// and brought to the front when Windows allows it. The restore is Win32's, as Electron's
    /// <c>restore()</c> is, so a window minimized from maximized comes back maximized. A signal
    /// that arrives after the window closed, while the app is exiting, is ignored.
    /// </summary>
    public void ShowFromSecondInstance()
    {
        if (_closed) return;
        Show();
        // Qualified: inside a window, Foreground is the brush.
        global::ShotAI.Platform.Shell.Foreground.TryActivate(new WindowInteropHelper(this).Handle);
    }

    /// <inheritdoc/>
    protected override void OnClosed(EventArgs e)
    {
        _closed = true;
        base.OnClosed(e);
    }
}
