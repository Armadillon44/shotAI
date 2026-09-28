using System.Windows.Automation.Peers;
using System.Windows.Controls;

namespace ShotAI.App.Chrome;

/// <summary>
/// A <see cref="WrapPanel"/> that UI Automation sees as a group, so its
/// <c>AutomationProperties.Name</c> reaches a screen reader, as a named <c>role="radiogroup"</c>
/// does in Electron (spec 06 7.13): WPF gives a panel no automation peer of its own, and a name
/// set on one reaches nobody.
/// </summary>
public sealed class GroupWrapPanel : WrapPanel
{
    /// <inheritdoc/>
    protected override AutomationPeer OnCreateAutomationPeer() => new GroupPeer(this);

    private sealed class GroupPeer(GroupWrapPanel owner) : FrameworkElementAutomationPeer(owner)
    {
        protected override AutomationControlType GetAutomationControlTypeCore() => AutomationControlType.Group;

        protected override string GetClassNameCore() => nameof(GroupWrapPanel);
    }
}
