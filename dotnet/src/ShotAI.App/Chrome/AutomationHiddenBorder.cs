using System.Windows.Automation.Peers;
using System.Windows.Controls;

namespace ShotAI.App.Chrome;

/// <summary>
/// A border that UI Automation's control and content views leave out with all it holds: HTML's
/// <c>aria-hidden="true"</c>, for a picture made of text, such as the tour's pill mock-up
/// (spec 06 2.31), which a screen reader would otherwise read as words.
/// </summary>
public class AutomationHiddenBorder : Border
{
    /// <inheritdoc/>
    protected override AutomationPeer OnCreateAutomationPeer() => new HiddenPeer(this);

    private sealed class HiddenPeer(AutomationHiddenBorder owner) : FrameworkElementAutomationPeer(owner)
    {
        protected override List<AutomationPeer>? GetChildrenCore() => null;

        protected override bool IsControlElementCore() => false;

        protected override bool IsContentElementCore() => false;
    }
}
