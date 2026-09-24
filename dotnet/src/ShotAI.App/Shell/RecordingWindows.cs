using System.Windows;
using System.Windows.Interop;
using ShotAI.Core.Json;
using ShotAI.Core.Shell;
using ShotAI.Platform.Shell;

namespace ShotAI.App.Shell;

/// <summary>
/// The actions of spec 03 7.4.6 on the real windows: the main window hides and comes back
/// restored and activated, and the pill docks in physical pixels (7.7) and shows without being
/// activated. A step on a window that closed is skipped, as Electron skips a destroyed one.
/// </summary>
internal sealed class RecordingWindows : IRecordingWindows
{
    private readonly Window _main;
    private readonly CapturePillWindow _pill;
    private bool _mainClosed;
    private bool _pillClosed;

    /// <summary>The actions on <paramref name="main"/> and <paramref name="pill"/>.</summary>
    public RecordingWindows(Window main, CapturePillWindow pill)
    {
        ArgumentNullException.ThrowIfNull(main);
        ArgumentNullException.ThrowIfNull(pill);
        _main = main;
        _pill = pill;
        main.Closed += (_, _) => _mainClosed = true;
        pill.Closed += (_, _) => _pillClosed = true;
    }

    /// <inheritdoc/>
    public bool PillOffScreen() =>
        !_pillClosed && PillDocking.NeedsRedock(WindowStyles.GetWindowRect(_pill.Handle), [.. MonitorQueries.All().Select(m => m.WorkArea)]);

    /// <inheritdoc/>
    public void Apply(ShellAction action)
    {
        switch (action)
        {
            case ShellAction.HideMain when !_mainClosed:
                _main.Hide();
                break;
            case ShellAction.DockPill when !_pillClosed:
                Dock();
                break;
            case ShellAction.ShowPill when !_pillClosed:
                // ShowActivated is false; nothing else shows it, so WPF's visibility stays true (Q-SHELL-21).
                _pill.Show();
                break;
            case ShellAction.HidePill when !_pillClosed:
                _pill.CancelDiscard();
                _pill.Hide();
                break;
            case ShellAction.ShowMain when !_mainClosed:
                if (_main.WindowState == WindowState.Minimized) _main.WindowState = WindowState.Normal;
                _main.Show();
                break;
            case ShellAction.ActivateMain when !_mainClosed:
                _main.Activate();
                // Windows may flash the taskbar button instead, when other input came in between (EDGE-SHELL-30).
                Foreground.TryActivate(new WindowInteropHelper(_main).Handle);
                break;
        }
    }

    // 2.4.2 on the monitor the main window was on, which a hidden window still reports: the pill
    // moves onto that monitor first, so it takes its DPI, then docks (EDGE-SHELL-48). If the
    // hidden pill kept its old DPI, the centring uses the width it will have there, and it docks
    // once more when its DPI changes.
    private void Dock()
    {
        var pill = _pill.Handle;
        var main = _mainClosed ? 0 : new WindowInteropHelper(_main).Handle;
        var monitor = main != 0 ? MonitorQueries.ForWindow(main) : MonitorQueries.Primary();
        WindowStyles.MoveNoActivate(pill, monitor.WorkArea.X, monitor.WorkArea.Y);
        var expected = (int)JsMath.Round(ShellConstants.PillWidth * monitor.Scale);
        var width = WindowStyles.GetWindowRect(pill).Width;
        var (x, y) = PillDocking.TopCenter(monitor.WorkArea, expected, monitor.Scale);
        WindowStyles.MoveNoActivate(pill, x, y);
        if (width == expected) return;
        void Again(object? sender, DpiChangedEventArgs e)
        {
            _pill.DpiChanged -= Again;
            if (!_pillClosed) Dock();
        }
        _pill.DpiChanged += Again;
    }
}
