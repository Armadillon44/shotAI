namespace ShotAI.App.Shell;

/// <summary>
/// Help, About shotAI (spec 03 2.8.4, 7.4.5): a small dialog owned by the main window, registered
/// like every window (INV-SHELL-1), never a Win32 message box, which could not be. Its one button
/// is the cancel button, so Enter, Esc and a click all close it.
/// </summary>
public partial class AboutWindow : ShotAIWindow
{
    /// <summary>The dialog with its two texts, <c>AboutText.Message</c> and <c>AboutText.Detail</c>.</summary>
    /// <param name="registration">The own-window registration.</param>
    /// <param name="message">The bold line.</param>
    /// <param name="detail">The tagline and the runtime lines.</param>
    public AboutWindow(WindowRegistration registration, string message, string detail)
        : base(registration)
    {
        ArgumentNullException.ThrowIfNull(message);
        ArgumentNullException.ThrowIfNull(detail);
        InitializeComponent();
        MessageText.Text = message;
        DetailText.Text = detail;
    }
}
