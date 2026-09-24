using System.Windows.Controls.Primitives;

namespace ShotAI.App.Shell;

/// <summary>
/// The popup shotAI's XAML uses where WPF needs a popup HWND (spec 03 7.4.7, ARCHITECTURE 5.4,
/// R-ARCH-19). <see cref="PopupExclusion"/>'s show hook registers its HWND before it is shown;
/// <see cref="Popup.Opened"/> is not a routed event, so this subclass also registers it on open,
/// through the <see cref="ShotAIWindow"/> it sits in, as the second layer.
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
