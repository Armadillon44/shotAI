using System.Windows.Controls.Primitives;

namespace ShotAI.App.Shell;

/// <summary>
/// The popup every shotAI XAML uses where WPF needs a popup HWND (spec 03 7.4.7, ARCHITECTURE
/// 5.4, R-ARCH-19): <see cref="Popup.Opened"/> is not a routed event, so this subclass registers
/// its own HWND when it opens, through the <see cref="ShotAIWindow"/> it sits in.
/// </summary>
public class ShotAIPopup : Popup
{
    /// <inheritdoc/>
    protected override void OnOpened(EventArgs e)
    {
        PopupExclusion.Register(PlacementTarget ?? this, Child);
        base.OnOpened(e);
    }
}
