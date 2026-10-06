using System.ComponentModel;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;
using ShotAI.App.Chrome;
using ShotAI.Core.Theme;
using ShotAI.Core.Tour;

namespace ShotAI.App.Tour;

/// <summary>
/// Draws the <see cref="TourViewModel"/>'s step (spec 06 2.31, 7.8). Each step finds its anchor
/// under the root given to <see cref="Attach"/>, brings it into view (D-HOME-18) and, once laid
/// out, measures it in the overlay's coordinates and places the bubble, the caret and the spot
/// from <see cref="TourLayout.Place"/>. It measures again after every layout pass while shown, so
/// a resize, a scroll of Home or any layout change under the anchor moves them with it
/// (D-HOME-32); an unchanged measure changes nothing. The spot moves to a new place over 0.25 s,
/// as CSS's transition did, unless Windows' animations are off.
/// </summary>
public partial class TourOverlay : UserControl
{
    /// <summary>The spot as drawn, which an animation moves.</summary>
    private static readonly DependencyProperty SpotProperty = DependencyProperty.Register(
        "Spot", typeof(Rect), typeof(TourOverlay), new PropertyMetadata(Rect.Empty, (d, _) => ((TourOverlay)d).DrawSpot()));

    /// <summary>The brand's card radius, the spot's corners, following the theme.</summary>
    private static readonly DependencyProperty CardRadiusProperty = DependencyProperty.Register(
        "CardRadius", typeof(double), typeof(TourOverlay), new PropertyMetadata(0.0, (d, _) => ((TourOverlay)d).DrawSpot()));

    // CSS's ease: cubic-bezier(0.25, 0.1, 0.25, 1).
    private static readonly KeySpline Ease = new(0.25, 0.1, 0.25, 1);
    private static readonly TimeSpan SpotTransition = TimeSpan.FromSeconds(0.25);

    // The accent ring's width outside the spot (box-shadow's 3px spread).
    private const double RingWidth = 3;

    private readonly RectangleGeometry _full = new();
    private readonly RectangleGeometry _hole = new();
    private readonly RectangleGeometry _ringLine = new();
    private readonly CombinedGeometry _dim;
    private TourViewModel? _tour;
    private DependencyObject? _anchorRoot;
    private FrameworkElement? _anchor;
    private TourPlacement? _placement;
    private Size _viewport;
    private Rect? _spotTarget;
    private IInputElement? _focusBefore;
    private bool _shown;
    private bool _focusPending;
    private bool _appearing;

    /// <summary>An overlay drawing its data context's tour.</summary>
    public TourOverlay()
    {
        InitializeComponent();
        _dim = new CombinedGeometry(GeometryCombineMode.Exclude, _full, _hole);
        Ring.Data = _ringLine;
        Dim.Data = _full;
        SetResourceReference(CardRadiusProperty, ThemeTokenKeys.RadiusValue("card"));
        for (var i = 0; i < TourSteps.All.Count; i++)
            Dots.Children.Add(new Ellipse { Width = 6, Height = 6, Margin = new Thickness(0, 0, i < TourSteps.All.Count - 1 ? 5 : 0, 0) });
        DataContextChanged += (_, e) => Follow(e.NewValue as TourViewModel);
        Root.PreviewKeyDown += OnPreviewKeyDown;
        Scrim.MouseLeftButtonDown += (_, e) =>
        {
            _tour?.FinishCommand.Execute(null);
            e.Handled = true;
        };
        SizeChanged += (_, _) => Remeasure();
    }

    /// <summary>The placement drawn, or null while the tour is not shown; for the tests.</summary>
    internal TourPlacement? Placement => _placement;

    /// <summary>Where the spot is going, or null for a step without one; for the tests.</summary>
    internal Rect? SpotTarget => _spotTarget;

    /// <summary>The spot as drawn now, partway through a move or at its end; for the tests.</summary>
    internal Rect SpotDrawn => (Rect)GetValue(SpotProperty);

    /// <summary>The bubble, for the tests.</summary>
    internal FrameworkElement BubbleElement => BubbleBox;

    /// <summary>The caret, for the tests.</summary>
    internal FrameworkElement CaretElement => Caret;

    /// <summary>The primary button, Next or Done, for the tests.</summary>
    internal Button Primary => PrimaryButton;

    /// <summary>The Back button, for the tests.</summary>
    internal Button BackAction => BackButton;

    /// <summary>The Skip button, for the tests.</summary>
    internal Button SkipAction => SkipButton;

    /// <summary>The step line, for the tests.</summary>
    internal TextBlock StepLine => StepLineText;

    /// <summary>The pill mock-up, for the tests.</summary>
    internal FrameworkElement PillMock => Pill;

    /// <summary>The click-catcher, for the tests.</summary>
    internal FrameworkElement ScrimElement => Scrim;

    /// <summary>The dim and its hole, for the tests.</summary>
    internal Path DimPath => Dim;

    /// <summary>The accent ring, for the tests.</summary>
    internal Path RingPath => Ring;

    /// <summary>The dots, for the tests.</summary>
    internal Panel DotsPanel => Dots;

    /// <summary>
    /// The tree whose elements carry <see cref="TourAnchor.Id"/>, and Home's scroller, whose
    /// scroll moves the anchors (Electron's capture-phase <c>scroll</c> listener).
    /// </summary>
    public void Attach(DependencyObject anchorRoot, ScrollViewer? scroller)
    {
        ArgumentNullException.ThrowIfNull(anchorRoot);
        _anchorRoot = anchorRoot;
        if (scroller is not null) scroller.ScrollChanged += (_, _) => Remeasure();
        Show();
    }

    private void Follow(TourViewModel? tour)
    {
        if (_tour is not null) _tour.PropertyChanged -= OnTourChanged;
        _tour = tour;
        if (tour is not null) tour.PropertyChanged += OnTourChanged;
        Show();
    }

    private void OnTourChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(TourViewModel.IsShown)) Show();
        else if (e.PropertyName == nameof(TourViewModel.Index) && _shown) ShowStep();
    }

    // Shows or hides the overlay as the tour is shown or not.
    private void Show()
    {
        var shown = _tour is { IsShown: true } && _anchorRoot is not null;
        if (shown == _shown) return;
        _shown = shown;
        if (!shown)
        {
            Hide();
            return;
        }
        if (!IsKeyboardFocusWithin) _focusBefore = Keyboard.FocusedElement ?? FocusManager.GetFocusedElement(FocusManager.GetFocusScope(this));
        // Each presentation starts with a new spot, which appears where it belongs.
        _spotTarget = null;
        Visibility = Visibility.Visible;
        LayoutUpdated += OnLayoutUpdated;
        ShowStep();
    }

    private void Hide()
    {
        LayoutUpdated -= OnLayoutUpdated;
        Visibility = Visibility.Collapsed;
        _anchor = null;
        _placement = null;
        _spotTarget = null;
        _focusPending = false;
        StopAppearing();
        BeginAnimation(SpotProperty, null);
        LiveRegion.SetText(StepLineText, null);
        // The focus goes back to what had it, if that is still on screen: logically in its scope
        // too, so a window without the keyboard focus gets it back when it is activated.
        var before = _focusBefore;
        _focusBefore = null;
        if (before is not UIElement { IsVisible: true } element) return;
        FocusManager.SetFocusedElement(FocusManager.GetFocusScope(element), element);
        element.Focus();
    }

    // A new step: its anchor, brought into view, is measured once the layout has settled, which
    // the next layout pass reports; the step line is announced and the primary button focused.
    private void ShowStep()
    {
        var tour = _tour!;
        for (var i = 0; i < Dots.Children.Count; i++)
            ((Shape)Dots.Children[i]).SetResourceReference(Shape.FillProperty, ThemeTokenKeys.Brush(i == tour.Index ? "accent" : "hair"));
        _anchor = FindAnchor();
        // The spot and its ring reach 9 DIP past the anchor, so that margin comes into view too.
        if (_anchor is { } anchor)
        {
            var reach = TourLayout.SpotPad + RingWidth;
            anchor.BringIntoView(new Rect(-reach, -reach, anchor.RenderSize.Width + 2 * reach, anchor.RenderSize.Height + 2 * reach));
        }
        AutomationProperties.SetName(StepLineText, tour.StepLine);
        LiveRegion.SetText(StepLineText, tour.StepLine);
        _focusPending = true;
        InvalidateArrange();
    }

    private FrameworkElement? FindAnchor() =>
        _tour?.Step.Anchor is { } id && _anchorRoot is not null ? TourAnchor.Find(_anchorRoot, id) : null;

    private void OnLayoutUpdated(object? sender, EventArgs e)
    {
        Remeasure();
        if (!_focusPending || !PrimaryButton.IsVisible) return;
        _focusPending = false;
        // Logically in the bubble's scope as well, which a window without the keyboard focus keeps.
        FocusManager.SetFocusedElement(BubbleBox, PrimaryButton);
        PrimaryButton.Focus();
    }

    // getBoundingClientRect in the overlay's coordinates, or null when the step has no anchor or
    // its anchor is not on screen; an anchor that went is looked for again.
    private LayoutRect? AnchorRect()
    {
        if (_tour?.Step.Anchor is null) return null;
        if (_anchor is not { IsVisible: true }) _anchor = FindAnchor();
        if (_anchor is not { } anchor || anchor.FindCommonVisualAncestor(this) is null) return null;
        var r = anchor.TransformToVisual(this).TransformBounds(new Rect(anchor.RenderSize));
        return new LayoutRect(r.X, r.Y, r.Width, r.Height);
    }

    private void Remeasure()
    {
        if (!_shown || !IsVisible || ActualWidth <= 0 || ActualHeight <= 0) return;
        var viewport = new Size(ActualWidth, ActualHeight);
        var placement = TourLayout.Place(AnchorRect(), viewport.Width, viewport.Height);
        if (placement == _placement && viewport == _viewport) return;
        if (viewport != _viewport)
        {
            _viewport = viewport;
            _full.Rect = new Rect(viewport);
            // max-width: calc(100vw - 24px).
            BubbleBox.MaxWidth = Math.Max(0, viewport.Width - 2 * TourLayout.EdgeMargin);
        }
        _placement = placement;
        PlaceBubble(placement);
        MoveSpot(placement.Spot);
    }

    private void PlaceBubble(TourPlacement placement)
    {
        if (placement.Centred)
        {
            BubbleBox.HorizontalAlignment = HorizontalAlignment.Center;
            BubbleBox.VerticalAlignment = VerticalAlignment.Center;
            BubbleBox.Margin = default;
            Caret.Visibility = Visibility.Collapsed;
            return;
        }
        var left = placement.BubbleLeft ?? 0;
        BubbleBox.HorizontalAlignment = HorizontalAlignment.Left;
        // CSS places the caret from the bubble's padding edge, inside its 1 DIP border: 8 DIP over
        // that edge is 7 over the bubble's own.
        var caretLeft = placement.CaretLeft + 1;
        if (placement.BubbleTop is { } top)
        {
            BubbleBox.VerticalAlignment = VerticalAlignment.Top;
            BubbleBox.Margin = new Thickness(left, top, 0, 0);
            Caret.VerticalAlignment = VerticalAlignment.Top;
            Caret.Margin = new Thickness(caretLeft, -7, 0, 0);
            Caret.BorderThickness = new Thickness(1, 1, 0, 0);
        }
        else
        {
            BubbleBox.VerticalAlignment = VerticalAlignment.Bottom;
            BubbleBox.Margin = new Thickness(left, 0, 0, placement.BubbleBottom ?? 0);
            Caret.VerticalAlignment = VerticalAlignment.Bottom;
            Caret.Margin = new Thickness(caretLeft, 0, 0, -7);
            Caret.BorderThickness = new Thickness(0, 0, 1, 1);
        }
        Caret.Visibility = Visibility.Visible;
    }

    // tour__spot: a step with no spot has the full dim; a spot that was shown moves to the new
    // place, and one that was not appears there, as a new element did in Electron. A spot that
    // appears keeps appearing until a frame has drawn it: the measure that showed it can come
    // before the scroll BringIntoView queued, which ScrollViewer runs in a later pass of the same
    // layout, and the measure after that scroll must not slide in from where the anchor was.
    private void MoveSpot(LayoutRect? spot)
    {
        if (spot is not { } s)
        {
            _spotTarget = null;
            BeginAnimation(SpotProperty, null);
            Dim.Data = _full;
            Ring.Visibility = Visibility.Collapsed;
            return;
        }
        var target = new Rect(s.Left, s.Top, s.Width, s.Height);
        if (_spotTarget == target) return;
        if (_spotTarget is null) StartAppearing();
        var moves = !_appearing && SystemParameters.ClientAreaAnimation;
        _spotTarget = target;
        Dim.Data = _dim;
        Ring.Visibility = Visibility.Visible;
        if (!moves)
        {
            BeginAnimation(SpotProperty, null);
            SetValue(SpotProperty, target);
            DrawSpot();
            return;
        }
        var move = new RectAnimationUsingKeyFrames();
        move.KeyFrames.Add(new SplineRectKeyFrame(target, KeyTime.FromTimeSpan(SpotTransition), Ease));
        BeginAnimation(SpotProperty, move, HandoffBehavior.SnapshotAndReplace);
    }

    private void StartAppearing()
    {
        if (_appearing) return;
        _appearing = true;
        CompositionTarget.Rendering += OnAppeared;
    }

    private void StopAppearing()
    {
        if (!_appearing) return;
        _appearing = false;
        CompositionTarget.Rendering -= OnAppeared;
    }

    private void OnAppeared(object? sender, EventArgs e) => StopAppearing();

    // The hole is the spot with the card's corners; the ring's 3 DIP line runs 1.5 DIP outside it,
    // so it covers the band box-shadow's 3 DIP spread drew, its outer corners 3 DIP rounder.
    private void DrawSpot()
    {
        var spot = (Rect)GetValue(SpotProperty);
        if (spot.IsEmpty) return;
        var radius = (double)GetValue(CardRadiusProperty);
        _hole.Rect = spot;
        _hole.RadiusX = _hole.RadiusY = radius;
        var line = spot;
        line.Inflate(1.5, 1.5);
        _ringLine.Rect = line;
        _ringLine.RadiusX = _ringLine.RadiusY = radius + 1.5;
    }

    // 2.31's keys, inside the tour only (EDGE-HOME-33); each is taken, so nothing behind sees it.
    private void OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (_tour is not { IsShown: true } tour) return;
        switch (e.Key)
        {
            case Key.Escape:
                tour.FinishCommand.Execute(null);
                break;
            case Key.Right:
                tour.NextCommand.Execute(null);
                break;
            case Key.Left:
                tour.BackCommand.Execute(null);
                break;
            default:
                return;
        }
        e.Handled = true;
    }
}
