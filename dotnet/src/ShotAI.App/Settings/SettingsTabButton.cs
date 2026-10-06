using System.Windows.Automation.Peers;
using System.Windows.Controls;

namespace ShotAI.App.Settings;

/// <summary>
/// One tab of Settings' tab bar (spec 06 2.24, 7.12): a radio button UI Automation sees as a
/// <c>TabItem</c>, selected while it is checked, as Electron's <c>role="tab"</c> with
/// <c>aria-selected</c> is.
/// </summary>
public sealed class SettingsTabButton : RadioButton
{
    /// <inheritdoc/>
    protected override AutomationPeer OnCreateAutomationPeer() => new TabPeer(this);

    private sealed class TabPeer(SettingsTabButton owner) : RadioButtonAutomationPeer(owner)
    {
        protected override AutomationControlType GetAutomationControlTypeCore() => AutomationControlType.TabItem;

        protected override string GetClassNameCore() => nameof(SettingsTabButton);
    }
}
