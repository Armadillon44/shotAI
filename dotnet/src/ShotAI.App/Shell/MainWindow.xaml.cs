using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using ShotAI.App.Report;
using ShotAI.App.Services;
using ShotAI.Core.Shell;
using ShotAI.Platform.Shell;

namespace ShotAI.App.Shell;

/// <summary>
/// The main window (spec 03 7.4.2): Electron's 720 x 740 DIP window with its minimums, placed on
/// the monitor the user launched from before it is first visible, with the application menu
/// (7.4.5) and View's zoom of its content. Closing it ends the process (<c>ShutdownMode</c> in
/// <c>App.xaml</c>, INV-SHELL-4), so it has no <c>Closing</c> handler that could cancel. It wears
/// the theme from its first frame: <c>body</c>'s ground, ink and font stack (spec 06 2.2) are
/// <c>DynamicResource</c> reads of the dictionary <c>ThemeManager.ApplyInitial</c> merges before
/// it is shown.
/// </summary>
public partial class MainWindow : ShotAIWindow
{
    private readonly MainWindowSizer _sizer;
    private readonly IAppInfo _appInfo;
    private FullScreenRestore? _restore;
    private bool _closed;

    /// <summary>The window, registered with the own-window registry before it is first shown.</summary>
    /// <param name="registration">The own-window registration of this window and its popups.</param>
    /// <param name="menu">The menu's view model, the window's data context.</param>
    /// <param name="sizer">The sizer, which places this window and later resizes it.</param>
    /// <param name="appInfo">What About shows.</param>
    /// <param name="shell">The content's view model: the header, the views and the overlay layer.</param>
    /// <param name="images">The report's image loader, which every figure in the window inherits (05 7.10).</param>
    public MainWindow(WindowRegistration registration, AppMenuViewModel menu, MainWindowSizer sizer, IAppInfo appInfo, ShellViewModel shell, ReportImageLoader images)
        : base(registration)
    {
        ArgumentNullException.ThrowIfNull(menu);
        ArgumentNullException.ThrowIfNull(sizer);
        ArgumentNullException.ThrowIfNull(appInfo);
        ArgumentNullException.ThrowIfNull(shell);
        ArgumentNullException.ThrowIfNull(images);
        _sizer = sizer;
        _appInfo = appInfo;
        ReportFigure.SetLoader(this, images);
        InitializeComponent();
        DataContext = menu;
        ShellContent.DataContext = shell;
        // 06 2.18 row 3: the window's activation re-lists Home while Home shows.
        Activated += (_, _) => shell.OnWindowActivated();
        // 06 D-HOME-11: an Escape no inner surface took (the confirm, a menu, the rename or search
        // box) reaches the window, where Home takes it: Electron's window listener, one thing per press.
        KeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape && !e.Handled && shell.OnEscape()) e.Handled = true;
        };
        CommandBindings.Add(new CommandBinding(ShellCommands.Exit, (_, _) => Application.Current?.Shutdown()));
        CommandBindings.Add(new CommandBinding(ShellCommands.ToggleFullScreen, (_, _) => ToggleFullScreen()));
        CommandBindings.Add(new CommandBinding(ShellCommands.Minimize, (_, _) => WindowState = WindowState.Minimized));
        CommandBindings.Add(new CommandBinding(ShellCommands.Close, (_, _) => Close()));
        CommandBindings.Add(new CommandBinding(ShellCommands.About, (_, _) => ShowAbout()));
        sizer.Attach(this);
    }

    /// <summary>The content: the shell's header, views and overlay layer.</summary>
    internal ShellView Shell => ShellContent;

    /// <summary>View, Toggle Full Screen is on: the window covers its monitor (Q-SHELL-11), and the detail resize leaves it alone (D6).</summary>
    public bool IsFullScreen => _restore is not null;

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

    /// <summary>
    /// View, Toggle Full Screen (7.4.2, Q-SHELL-11): the window loses its frame and covers its
    /// monitor, taskbar included, with the menu kept; the second toggle puts back the frame, the
    /// rectangle and the maximized state it had.
    /// </summary>
    internal void ToggleFullScreen()
    {
        var hwnd = new WindowInteropHelper(this).Handle;
        if (hwnd == 0) return;
        if (_restore is { } saved)
        {
            _restore = null;
            WindowStyle = saved.Style;
            ResizeMode = saved.Resize;
            WindowStyles.SetBoundsNoActivate(hwnd, saved.Bounds, topmost: false);
            WindowState = saved.State;
            return;
        }
        var state = WindowState;
        // A maximized window covers only the work area: back to its normal rectangle first, the one restored later.
        if (state == WindowState.Maximized) WindowState = WindowState.Normal;
        _restore = new FullScreenRestore(WindowStyle, ResizeMode, state, WindowStyles.GetWindowRect(hwnd));
        WindowStyle = WindowStyle.None;
        ResizeMode = ResizeMode.NoResize;
        WindowStyles.SetBoundsNoActivate(hwnd, MonitorQueries.ForWindow(hwnd).Bounds, topmost: false);
    }

    /// <summary>
    /// After <see cref="ShotAIWindow"/> registered the handle, and before the window is first
    /// visible: its first placement (7.4.2, EDGE-SHELL-35).
    /// </summary>
    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        _sizer.PlaceInitially(new WindowInteropHelper(this).Handle);
    }

    /// <inheritdoc/>
    protected override void OnClosed(EventArgs e)
    {
        _closed = true;
        base.OnClosed(e);
    }

    // Help, About shotAI (2.8.4): owned by this window, so it stays above it and closes with it.
    private void ShowAbout()
    {
        var info = _appInfo.Current;
        var about = new AboutWindow(Registration, AboutText.Message(info.Version), AboutText.Detail(info.DotNetVersion, info.WebView2Version, info.Arch))
        {
            Owner = this,
        };
        about.ShowDialog();
    }

    private sealed record FullScreenRestore(WindowStyle Style, ResizeMode Resize, WindowState State, PixelRect Bounds);
}
