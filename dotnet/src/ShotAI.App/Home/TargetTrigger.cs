using System.Windows;
using System.Windows.Automation;
using System.Windows.Automation.Peers;
using System.Windows.Automation.Provider;
using System.Windows.Controls;

namespace ShotAI.App.Home;

/// <summary>
/// The target dropdown's trigger (spec 06 2.4, 7.6): a button whose click opens or closes the
/// popover, which reports itself as ExpandCollapse, as the overflow menu's trigger does (7.8),
/// where Electron's had <c>aria-haspopup</c> and <c>aria-expanded</c>.
/// </summary>
public sealed class TargetTrigger : Button
{
    /// <summary>The popover is open.</summary>
    public static readonly DependencyProperty IsOpenProperty = DependencyProperty.Register(
        nameof(IsOpen), typeof(bool), typeof(TargetTrigger), new FrameworkPropertyMetadata(false, (d, e) => ((TargetTrigger)d).OnIsOpenChanged((bool)e.NewValue)));

    /// <inheritdoc cref="IsOpenProperty"/>
    public bool IsOpen
    {
        get => (bool)GetValue(IsOpenProperty);
        set => SetValue(IsOpenProperty, value);
    }

    /// <inheritdoc/>
    protected override AutomationPeer OnCreateAutomationPeer() => new Peer(this);

    private void OnIsOpenChanged(bool open)
    {
        if (UIElementAutomationPeer.FromElement(this) is Peer peer)
        {
            peer.RaisePropertyChangedEvent(
                ExpandCollapsePatternIdentifiers.ExpandCollapseStateProperty,
                open ? ExpandCollapseState.Collapsed : ExpandCollapseState.Expanded,
                open ? ExpandCollapseState.Expanded : ExpandCollapseState.Collapsed);
        }
    }

    // Expand and Collapse run the trigger's toggle when the state is the other one.
    private void Toggle(bool open)
    {
        if (IsOpen != open && Command is { } command && command.CanExecute(CommandParameter)) command.Execute(CommandParameter);
    }

    private sealed class Peer(TargetTrigger owner) : ButtonAutomationPeer(owner), IExpandCollapseProvider
    {
        public ExpandCollapseState ExpandCollapseState => owner.IsOpen ? ExpandCollapseState.Expanded : ExpandCollapseState.Collapsed;

        public override object GetPattern(PatternInterface patternInterface) =>
            patternInterface == PatternInterface.ExpandCollapse ? this : base.GetPattern(patternInterface);

        public void Expand()
        {
            if (!IsEnabled()) throw new ElementNotEnabledException();
            owner.Toggle(open: true);
        }

        public void Collapse() => owner.Toggle(open: false);
    }
}
