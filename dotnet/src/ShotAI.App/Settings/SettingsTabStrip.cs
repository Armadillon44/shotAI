using System.Windows.Automation.Peers;
using System.Windows.Automation.Provider;
using System.Windows.Controls;
using System.Windows.Input;

namespace ShotAI.App.Settings;

/// <summary>
/// Settings' tab bar (spec 06 2.24, 7.12, INV-HOME-39): a row of <see cref="SettingsTabButton"/>s
/// with the WAI-ARIA tabs keys, as <c>onTabKey</c> had them. Right or Down selects the next tab
/// and Left or Up the previous one, wrapping; Home the first and End the last; the focus follows
/// the selection. Only the selected tab is a Tab stop (its style's trigger). UI Automation sees a
/// <c>Tab</c> whose selection is the selected tab.
/// </summary>
public sealed class SettingsTabStrip : StackPanel
{
    /// <summary>A horizontal strip.</summary>
    public SettingsTabStrip() => Orientation = Orientation.Horizontal;

    /// <summary>The tabs, in order.</summary>
    internal IReadOnlyList<SettingsTabButton> Tabs => [.. Children.OfType<SettingsTabButton>()];

    /// <inheritdoc/>
    protected override void OnPreviewKeyDown(KeyEventArgs e)
    {
        ArgumentNullException.ThrowIfNull(e);
        base.OnPreviewKeyDown(e);
        if (e.Handled || Keyboard.Modifiers != ModifierKeys.None) return;
        var tabs = Tabs;
        if (tabs.Count == 0) return;
        var at = Math.Max(0, IndexOfSelected(tabs));
        int? next = e.Key switch
        {
            Key.Right or Key.Down => (at + 1) % tabs.Count,
            Key.Left or Key.Up => (at - 1 + tabs.Count) % tabs.Count,
            Key.Home => 0,
            Key.End => tabs.Count - 1,
            _ => null,
        };
        if (next is not { } i) return;
        e.Handled = true;
        tabs[i].IsChecked = true;
        tabs[i].Focus();
    }

    /// <inheritdoc/>
    protected override AutomationPeer OnCreateAutomationPeer() => new StripPeer(this);

    private static int IndexOfSelected(IReadOnlyList<SettingsTabButton> tabs)
    {
        for (var i = 0; i < tabs.Count; i++)
        {
            if (tabs[i].IsChecked == true) return i;
        }
        return -1;
    }

    private sealed class StripPeer(SettingsTabStrip owner) : FrameworkElementAutomationPeer(owner), ISelectionProvider
    {
        public bool CanSelectMultiple => false;

        public bool IsSelectionRequired => true;

        public override object GetPattern(PatternInterface patternInterface) =>
            patternInterface == PatternInterface.Selection ? this : base.GetPattern(patternInterface);

        public IRawElementProviderSimple[] GetSelection()
        {
            var tabs = owner.Tabs;
            var at = IndexOfSelected(tabs);
            return at >= 0 && CreatePeerForElement(tabs[at]) is { } peer ? [ProviderFromPeer(peer)] : [];
        }

        protected override AutomationControlType GetAutomationControlTypeCore() => AutomationControlType.Tab;

        protected override string GetClassNameCore() => nameof(SettingsTabStrip);
    }
}
