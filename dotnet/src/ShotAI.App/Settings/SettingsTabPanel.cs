using System.Windows.Automation.Peers;
using System.Windows.Controls;

namespace ShotAI.App.Settings;

/// <summary>
/// The shown tab's content (spec 06 2.24, INV-HOME-39): a Tab stop of its own, so a panel with
/// nothing to focus, such as About's lines, is reached by Tab, as Electron's
/// <c>tabIndex={0}</c> panel is. UI Automation sees it as a pane named after its tab.
/// </summary>
public sealed class SettingsTabPanel : ContentControl
{
    /// <inheritdoc/>
    protected override AutomationPeer OnCreateAutomationPeer() => new PanelPeer(this);

    private sealed class PanelPeer(SettingsTabPanel owner) : FrameworkElementAutomationPeer(owner)
    {
        protected override AutomationControlType GetAutomationControlTypeCore() => AutomationControlType.Pane;

        protected override string GetClassNameCore() => nameof(SettingsTabPanel);
    }
}
