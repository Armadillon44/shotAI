using System.Windows;
using System.Windows.Automation;
using System.Windows.Automation.Peers;
using System.Windows.Automation.Provider;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using ShotAI.Core.Home;

namespace ShotAI.App.Chrome;

/// <summary>
/// Electron's <c>OverflowMenu</c> natively (spec 06 2.20, 7.8). A click on an item closes the
/// menu first, then runs the item (parity); a click outside closes it and does nothing else, as
/// Electron's backdrop does; the keyboard works it as Windows menus work (IMPROVEMENT D-HOME-12):
/// opening focuses the first enabled item, Up and Down move, Home and End jump, Enter and Space
/// run, and Escape and Tab close with the focus back on the trigger.
/// </summary>
public partial class OverflowMenu : UserControl
{
    /// <summary>2.20's height estimate per item, in DIP, for the flip.</summary>
    public const double ItemHeightEstimate = 34;

    /// <summary>And for the padding.</summary>
    public const double PaddingEstimate = 16;

    /// <summary>The gap between the trigger and the popover, in DIP.</summary>
    public const double Gap = 4;

    /// <summary>The entries, in order.</summary>
    public static readonly DependencyProperty ItemsProperty = DependencyProperty.Register(
        nameof(Items), typeof(IReadOnlyList<MenuItemModel>), typeof(OverflowMenu),
        new FrameworkPropertyMetadata(Array.Empty<MenuItemModel>(), (d, _) => ((OverflowMenu)d)._built = false));

    /// <summary>The trigger's content: <c>&#8943;</c> by default.</summary>
    public static readonly DependencyProperty LabelProperty = DependencyProperty.Register(
        nameof(Label), typeof(object), typeof(OverflowMenu), new FrameworkPropertyMetadata(HomeText.MoreActionsGlyph));

    /// <summary>The trigger's tooltip and accessible name: <c>More actions</c> by default.</summary>
    public static readonly DependencyProperty TitleProperty = DependencyProperty.Register(
        nameof(Title), typeof(string), typeof(OverflowMenu), new FrameworkPropertyMetadata(HomeText.MoreActions));

    /// <summary>The trigger's style key: <c>Button.Icon</c> by default (<c>btn btn--small btn--ghost btn--icon</c>).</summary>
    public static readonly DependencyProperty TriggerStyleKeyProperty = DependencyProperty.Register(
        nameof(TriggerStyleKey), typeof(string), typeof(OverflowMenu),
        new FrameworkPropertyMetadata("Button.Icon", (d, e) => ((OverflowMenu)d).Trigger.SetResourceReference(StyleProperty, e.NewValue)));

    private bool _built;

    /// <summary>A closed menu with no items.</summary>
    public OverflowMenu()
    {
        InitializeComponent();
        Trigger.Menu = this;
        Trigger.SetResourceReference(StyleProperty, TriggerStyleKey);
        Trigger.SetBinding(ContentProperty, new Binding(nameof(Label)) { Source = this });
        Trigger.SetBinding(ToolTipProperty, new Binding(nameof(Title)) { Source = this });
        Trigger.SetBinding(AutomationProperties.NameProperty, new Binding(nameof(Title)) { Source = this });
        Trigger.Click += (_, _) =>
        {
            if (IsOpen) Close(returnFocus: true);
            else Open();
        };
        Popup.Opened += (_, _) =>
        {
            FirstEnabled()?.Focus();
            Trigger.RaiseExpandCollapse(open: true);
        };
        Popup.Closed += (_, _) =>
        {
            // The focus was in the popover, which is gone: it goes back to the trigger.
            if (Keyboard.FocusedElement is null || (Keyboard.FocusedElement is DependencyObject d && IsInPopover(d))) Trigger.Focus();
            Trigger.RaiseExpandCollapse(open: false);
        };
        Pop.PreviewKeyDown += OnPopoverKey;
    }

    /// <inheritdoc cref="ItemsProperty"/>
    public IReadOnlyList<MenuItemModel> Items
    {
        get => (IReadOnlyList<MenuItemModel>)GetValue(ItemsProperty);
        set => SetValue(ItemsProperty, value);
    }

    /// <inheritdoc cref="LabelProperty"/>
    public object Label
    {
        get => GetValue(LabelProperty);
        set => SetValue(LabelProperty, value);
    }

    /// <inheritdoc cref="TitleProperty"/>
    public string Title
    {
        get => (string)GetValue(TitleProperty);
        set => SetValue(TitleProperty, value);
    }

    /// <inheritdoc cref="TriggerStyleKeyProperty"/>
    public string TriggerStyleKey
    {
        get => (string)GetValue(TriggerStyleKeyProperty);
        set => SetValue(TriggerStyleKeyProperty, value);
    }

    /// <summary>The popover is open.</summary>
    public bool IsOpen => Popup.IsOpen;

    /// <summary>The trigger, for the tests.</summary>
    internal OverflowTrigger TriggerButton => Trigger;

    /// <summary>The popup, for the tests.</summary>
    internal ShotAI.App.Shell.ShotAIPopup PopupWindow => Popup;

    /// <summary>The drawn entries, in order, for the tests.</summary>
    internal UIElementCollection Entries => List.Children;

    /// <summary>Opens the popover below the trigger, or above it when the window has no room below (2.20).</summary>
    public void Open()
    {
        if (IsOpen || !IsEnabled) return;
        if (!_built) Build();
        Pop.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        var up = DropsUp();
        Popup.Placement = up ? System.Windows.Controls.Primitives.PlacementMode.Top : System.Windows.Controls.Primitives.PlacementMode.Bottom;
        // The border, inside the popup's shadow margin, meets the trigger's right edge, Gap from it.
        Popup.HorizontalOffset = Trigger.ActualWidth - Pop.DesiredSize.Width + Pop.Margin.Right;
        Popup.VerticalOffset = up ? Pop.Margin.Bottom - Gap : Gap - Pop.Margin.Top;
        Popup.IsOpen = true;
    }

    /// <summary>Closes the popover.</summary>
    /// <param name="returnFocus">Put the focus on the trigger, as Escape and Tab do.</param>
    public void Close(bool returnFocus)
    {
        if (!IsOpen) return;
        Popup.IsOpen = false;
        if (returnFocus) Trigger.Focus();
    }

    /// <summary>
    /// 2.20's flip against the window's content, not the screen: up when the room below the
    /// trigger is less than the estimate and the room above is more than the room below.
    /// </summary>
    internal bool DropsUp()
    {
        if (Window.GetWindow(this)?.Content is not FrameworkElement content || !content.IsAncestorOf(Trigger)) return false;
        var rect = Trigger.TransformToAncestor(content).TransformBounds(new Rect(Trigger.RenderSize));
        return DropsUp(Items.Count, rect.Top, rect.Bottom, content.ActualHeight);
    }

    /// <summary>The rule itself: <c>below &lt; items * 34 + 16 &amp;&amp; top &gt; below</c>.</summary>
    internal static bool DropsUp(int items, double top, double bottom, double height)
    {
        var below = height - bottom;
        return below < items * ItemHeightEstimate + PaddingEstimate && top > below;
    }

    private void Build()
    {
        _built = true;
        List.Children.Clear();
        foreach (var item in Items)
        {
            FrameworkElement entry = item switch
            {
                MenuSeparatorItem => Separator(),
                MenuHeaderItem header => Header(header),
                MenuActionItem action => ActionButton(action),
                _ => throw new InvalidOperationException($"Not a menu item kind: {item?.GetType().Name}"),
            };
            // The popover's flex gap of 1px.
            if (List.Children.Count > 0) entry.Margin = new Thickness(entry.Margin.Left, entry.Margin.Top + 1, entry.Margin.Right, entry.Margin.Bottom);
            List.Children.Add(entry);
        }
    }

    // menu__sep: 1px hair-2, margin 0.25rem 0.2rem.
    private static Border Separator()
    {
        var line = new Border { Height = 1, Margin = new Thickness(3.2, 4, 3.2, 4), SnapsToDevicePixels = true, Focusable = false };
        line.SetResourceReference(Border.BackgroundProperty, "Brush.hair-2");
        return line;
    }

    // menu__header: role presentation, never focused; upper-cased in the binding's culture, as the view's other headers.
    private static TextBlock Header(MenuHeaderItem header)
    {
        var text = new TextBlock { Focusable = false };
        text.SetResourceReference(StyleProperty, "MenuHeader");
        text.SetBinding(TextBlock.TextProperty, new Binding { Source = header.Label, Converter = new UpperCaseConverter() });
        return text;
    }

    private OverflowMenuItem ActionButton(MenuActionItem action)
    {
        var button = new OverflowMenuItem { Content = action.Label, Command = new ItemCommand(this, action) };
        button.SetResourceReference(StyleProperty, action.Danger ? "MenuItem.Danger" : "MenuItem.Base");
        AutomationProperties.SetName(button, action.Label);
        return button;
    }

    private List<OverflowMenuItem> EnabledItems() => [.. List.Children.OfType<OverflowMenuItem>().Where(b => b.IsEnabled)];

    private OverflowMenuItem? FirstEnabled() => EnabledItems().FirstOrDefault();

    private bool IsInPopover(DependencyObject d)
    {
        for (var node = d; node is not null; node = System.Windows.Media.VisualTreeHelper.GetParent(node) ?? LogicalTreeHelper.GetParent(node))
        {
            if (ReferenceEquals(node, Pop)) return true;
        }
        return false;
    }

    private void OnPopoverKey(object sender, KeyEventArgs e)
    {
        switch (e.Key)
        {
            case Key.Escape:
            case Key.Tab:
                Close(returnFocus: true);
                e.Handled = true;
                return;
            case Key.Down or Key.Up or Key.Home or Key.End:
                var items = EnabledItems();
                if (items.Count == 0) break;
                var at = items.FindIndex(b => b.IsKeyboardFocused);
                var next = e.Key switch
                {
                    Key.Home => 0,
                    Key.End => items.Count - 1,
                    Key.Down => at < 0 ? 0 : (at + 1) % items.Count,
                    _ => at < 0 ? items.Count - 1 : (at - 1 + items.Count) % items.Count,
                };
                items[next].Focus();
                e.Handled = true;
                return;
        }
    }

    /// <summary>An item's command: the menu closes first, then the item runs (2.20).</summary>
    private sealed class ItemCommand(OverflowMenu menu, MenuActionItem item) : ICommand
    {
        public event EventHandler? CanExecuteChanged
        {
            add => item.Command.CanExecuteChanged += value;
            remove => item.Command.CanExecuteChanged -= value;
        }

        public bool CanExecute(object? parameter) => item.Enabled && item.Command.CanExecute(item.Parameter);

        public void Execute(object? parameter)
        {
            menu.Close(returnFocus: true);
            if (item.Command.CanExecute(item.Parameter)) item.Command.Execute(item.Parameter);
        }
    }
}

/// <summary>
/// The overflow menu's trigger: a button whose automation peer exposes ExpandCollapse, which a
/// <c>ToggleButton</c>'s Toggle pattern would not (spec 06 7.8).
/// </summary>
public sealed class OverflowTrigger : Button
{
    /// <summary>The menu this trigger opens.</summary>
    internal OverflowMenu? Menu { get; set; }

    /// <inheritdoc/>
    protected override AutomationPeer OnCreateAutomationPeer() => new Peer(this);

    internal void RaiseExpandCollapse(bool open)
    {
        if (UIElementAutomationPeer.FromElement(this) is Peer peer)
        {
            peer.RaisePropertyChangedEvent(
                ExpandCollapsePatternIdentifiers.ExpandCollapseStateProperty,
                open ? ExpandCollapseState.Collapsed : ExpandCollapseState.Expanded,
                open ? ExpandCollapseState.Expanded : ExpandCollapseState.Collapsed);
        }
    }

    private sealed class Peer(OverflowTrigger owner) : ButtonAutomationPeer(owner), IExpandCollapseProvider
    {
        public ExpandCollapseState ExpandCollapseState => owner.Menu?.IsOpen == true ? ExpandCollapseState.Expanded : ExpandCollapseState.Collapsed;

        public override object GetPattern(PatternInterface patternInterface) =>
            patternInterface == PatternInterface.ExpandCollapse ? this : base.GetPattern(patternInterface);

        public void Expand()
        {
            if (!IsEnabled()) throw new ElementNotEnabledException();
            owner.Menu?.Open();
        }

        public void Collapse() => owner.Menu?.Close(returnFocus: false);
    }
}

/// <summary>An item of an <see cref="OverflowMenu"/>, which reports itself as a menu item (spec 06 7.8).</summary>
public sealed class OverflowMenuItem : Button
{
    /// <inheritdoc/>
    protected override AutomationPeer OnCreateAutomationPeer() => new Peer(this);

    private sealed class Peer(OverflowMenuItem owner) : ButtonAutomationPeer(owner)
    {
        protected override AutomationControlType GetAutomationControlTypeCore() => AutomationControlType.MenuItem;
    }
}
