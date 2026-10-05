using System.Globalization;
using System.Windows;
using System.Windows.Automation.Peers;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;

namespace ShotAI.App.Chrome;

/// <summary>
/// A radio group of chips made from a list (spec 06 2.24's <c>capmode__modes</c>, 7.13): each
/// item's container is a <see cref="RadioButton"/> whose text is the item's
/// <see cref="LabelPath"/> and whose <c>IsChecked</c> is bound both ways to its
/// <see cref="CheckedPath"/>, and UI Automation sees the group as a named <c>Group</c> of radio
/// buttons named by their text, as Electron's <c>role="radiogroup"</c> is. Its items panel takes
/// <see cref="RadioGroupKeys"/>, which finds the radio buttons among the panel's children.
/// </summary>
/// <remarks>
/// The chips share a group name of their own: a generated container has no logical parent, which
/// is what groups radio buttons that have no name, so checking one would leave the others checked.
/// </remarks>
public sealed class ChipGroup : ItemsControl
{
    /// <summary>The item property each chip shows: a string, so it is also the chip's accessible name.</summary>
    public static readonly DependencyProperty LabelPathProperty = DependencyProperty.Register(
        nameof(LabelPath), typeof(string), typeof(ChipGroup), new PropertyMetadata("Label"));

    /// <summary>The item property each chip's <c>IsChecked</c> is bound to, both ways.</summary>
    public static readonly DependencyProperty CheckedPathProperty = DependencyProperty.Register(
        nameof(CheckedPath), typeof(string), typeof(ChipGroup), new PropertyMetadata("IsSelected"));

    /// <summary>The space between chips, across and down (CSS's flex <c>gap</c>): each chip's right and bottom margin.</summary>
    public static readonly DependencyProperty GapProperty = DependencyProperty.Register(
        nameof(Gap), typeof(double), typeof(ChipGroup), new PropertyMetadata(0.0));

    private static int s_groups;

    private readonly string _groupName = "ChipGroup" + Interlocked.Increment(ref s_groups).ToString(CultureInfo.InvariantCulture);

    /// <inheritdoc cref="LabelPathProperty"/>
    public string LabelPath
    {
        get => (string)GetValue(LabelPathProperty);
        set => SetValue(LabelPathProperty, value);
    }

    /// <inheritdoc cref="CheckedPathProperty"/>
    public string CheckedPath
    {
        get => (string)GetValue(CheckedPathProperty);
        set => SetValue(CheckedPathProperty, value);
    }

    /// <inheritdoc cref="GapProperty"/>
    public double Gap
    {
        get => (double)GetValue(GapProperty);
        set => SetValue(GapProperty, value);
    }

    /// <inheritdoc/>
    protected override bool IsItemItsOwnContainerOverride(object item) => item is RadioButton;

    /// <inheritdoc/>
    protected override DependencyObject GetContainerForItemOverride() => new RadioButton();

    /// <inheritdoc/>
    protected override void PrepareContainerForItemOverride(DependencyObject element, object item)
    {
        base.PrepareContainerForItemOverride(element, item);
        if (element is not RadioButton chip || ReferenceEquals(chip, item)) return;
        chip.Margin = new Thickness(0, 0, Gap, Gap);
        chip.GroupName = _groupName;
        chip.SetBinding(ContentControl.ContentProperty, new Binding(LabelPath) { Source = item, Mode = BindingMode.OneWay });
        chip.SetBinding(ToggleButton.IsCheckedProperty, new Binding(CheckedPath) { Source = item, Mode = BindingMode.TwoWay });
    }

    /// <inheritdoc/>
    protected override void ClearContainerForItemOverride(DependencyObject element, object item)
    {
        if (element is RadioButton chip && !ReferenceEquals(chip, item))
        {
            BindingOperations.ClearBinding(chip, ToggleButton.IsCheckedProperty);
            BindingOperations.ClearBinding(chip, ContentControl.ContentProperty);
        }
        base.ClearContainerForItemOverride(element, item);
    }

    /// <inheritdoc/>
    protected override AutomationPeer OnCreateAutomationPeer() => new GroupPeer(this);

    // A plain element peer: its children are the chips' own radio button peers, with no data
    // item wrapped around each as an ItemsControl's peer would.
    private sealed class GroupPeer(ChipGroup owner) : FrameworkElementAutomationPeer(owner)
    {
        protected override AutomationControlType GetAutomationControlTypeCore() => AutomationControlType.Group;

        protected override string GetClassNameCore() => nameof(ChipGroup);
    }
}
