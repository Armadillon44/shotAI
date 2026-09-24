using System.Windows;
using System.Windows.Interop;
using ShotAI.Core.Shell;
using ShotAI.Platform.Shell;

namespace ShotAI.App.Shell;

/// <summary>
/// The Discard confirmation (spec 03 2.4.7, 7.6.3, D8): a registered dialog, never a message box,
/// which no registration could exclude from capture (EDGE-SHELL-37, spec 02 D20). Discard closes
/// it confirmed; Cancel, Enter, Esc and closing it any other way do not. It is activated when it
/// opens, since the user just clicked the pill, and kept on the pill's monitor's work area.
/// </summary>
public partial class DiscardConfirmWindow : ShotAIWindow
{
    /// <summary>The dialog asking <paramref name="message"/>.</summary>
    /// <param name="registration">The own-window registration.</param>
    /// <param name="message"><see cref="ShellStrings.DiscardWholeProject"/> or <see cref="ShellStrings.DiscardSessionSteps"/>.</param>
    public DiscardConfirmWindow(WindowRegistration registration, string message)
        : base(registration)
    {
        ArgumentNullException.ThrowIfNull(message);
        InitializeComponent();
        MessageText.Text = message;
        DiscardButton.Click += (_, _) => DialogResult = true;
        Loaded += (_, _) =>
        {
            KeepOnScreen();
            Activate();
        };
    }

    /// <summary>The message asked.</summary>
    internal string Message => MessageText.Text;

    // Centred on a pill docked 8 DIP below the top of the screen, the dialog would start above it:
    // moved down, and left or right, into its monitor's work area.
    private void KeepOnScreen()
    {
        var hwnd = new WindowInteropHelper(this).Handle;
        if (hwnd == 0) return;
        var rect = WindowStyles.GetWindowRect(hwnd);
        var area = MonitorQueries.ForWindow(hwnd).WorkArea;
        var x = Math.Max(area.X, Math.Min(rect.X, area.Right - rect.Width));
        var y = Math.Max(area.Y, Math.Min(rect.Y, area.Bottom - rect.Height));
        if (x != rect.X || y != rect.Y) WindowStyles.MoveNoActivate(hwnd, x, y);
    }
}
