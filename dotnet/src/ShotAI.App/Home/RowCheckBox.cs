using System.Windows.Automation;
using System.Windows.Automation.Peers;
using System.Windows.Automation.Provider;
using System.Windows.Controls;

namespace ShotAI.App.Home;

/// <summary>
/// A row's selection checkbox (spec 06 2.12, 2.15): a click never ticks it by itself. Its
/// command decides, a toggle or with Shift a range that only adds, and the tick shows the
/// selection's answer through its one-way binding, as Electron's shift-click prevents the
/// checkbox's own toggle. A screen reader's toggle runs the same command.
/// </summary>
public sealed class RowCheckBox : CheckBox
{
    /// <inheritdoc/>
    protected override void OnToggle()
    {
        // The command run by the click decides; IsChecked follows the selection.
    }

    /// <inheritdoc/>
    protected override AutomationPeer OnCreateAutomationPeer() => new Peer(this);

    /// <summary>Clicks the box as the mouse and Space do: the command runs, the tick does not move by itself.</summary>
    internal void PerformClick() => OnClick();

    private sealed class Peer(RowCheckBox owner) : CheckBoxAutomationPeer(owner), IToggleProvider
    {
        ToggleState IToggleProvider.ToggleState => owner.IsChecked == true ? ToggleState.On : ToggleState.Off;

        void IToggleProvider.Toggle()
        {
            if (!IsEnabled()) throw new ElementNotEnabledException();
            owner.PerformClick();
        }
    }
}
