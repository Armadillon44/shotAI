using System.Windows;
using System.Windows.Automation.Peers;
using System.Windows.Controls;

namespace ShotAI.App.Report;

/// <summary>
/// The report's cards (spec 05 7.18): an <see cref="ItemsControl"/> whose automation peer is a
/// List of ListItems, which a plain <see cref="ItemsControl"/> does not promise, named as
/// <c>role="list"</c> and <c>role="listitem"</c> were. Each item's name is its card's
/// <see cref="StepCardViewModel.AutomationName"/>. No virtualization (Q-REP-11).
/// </summary>
public sealed class ReportList : ItemsControl
{
    /// <inheritdoc/>
    protected override AutomationPeer OnCreateAutomationPeer() => new ReportListAutomationPeer(this);
}

/// <summary>The report list's peer: a List.</summary>
public sealed class ReportListAutomationPeer(ReportList owner) : ItemsControlAutomationPeer(owner)
{
    /// <inheritdoc/>
    protected override AutomationControlType GetAutomationControlTypeCore() => AutomationControlType.List;

    /// <inheritdoc/>
    protected override string GetClassNameCore() => nameof(ReportList);

    /// <inheritdoc/>
    protected override ItemAutomationPeer CreateItemAutomationPeer(object item) => new ReportListItemAutomationPeer(item, this);
}

/// <summary>A report row's peer: a ListItem named by its card.</summary>
public sealed class ReportListItemAutomationPeer(object item, ItemsControlAutomationPeer list) : ItemAutomationPeer(item, list)
{
    /// <inheritdoc/>
    protected override AutomationControlType GetAutomationControlTypeCore() => AutomationControlType.ListItem;

    /// <inheritdoc/>
    protected override string GetClassNameCore() => "ReportListItem";

    /// <inheritdoc/>
    protected override string GetNameCore() => Item is StepCardViewModel card ? card.AutomationName : base.GetNameCore();
}
