using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using ShotAI.Core.Editor;
using ShotAI.Core.Geometry;
using ShotAI.Core.Json;
using ShotAI.Core.Report;
using ShotAI.Core.Theme;

namespace ShotAI.App.Report;

/// <summary>
/// A shot's figure (spec 05 2.11, 7.10): the image fitted to the width the card offers, framed by
/// its stored zoom and pan, with the click ring over it; a placeholder while it first loads, and
/// the missing-image state when it cannot be shown (D-REP-18). The zoom and delete controls and
/// the pan gesture join with WP-C3.
/// </summary>
/// <remarks>
/// The loader, the project folder and the document scale are inherited attached properties: the
/// main window sets the loader and the report's frame the other two, so no view model holds an
/// App service (INV-ARCH-3). An image loads once the figure is within a viewport's height of the
/// report's viewport (parity with <c>loading="lazy"</c>), and again only when its key changes or
/// the figure needs more pixels than were decoded (7.11); until a new decode completes, the image
/// on screen and its size stay together (EDGE-REP-50). The template is <c>ReportFigure.Template</c>
/// in <c>StepCardTemplates.xaml</c>.
/// </remarks>
[TemplatePart(Name = WrapPart, Type = typeof(Border))]
[TemplatePart(Name = CanvasPart, Type = typeof(Canvas))]
[TemplatePart(Name = ImagePart, Type = typeof(Image))]
[TemplatePart(Name = HaloPart, Type = typeof(Ellipse))]
[TemplatePart(Name = RingPart, Type = typeof(Ellipse))]
[TemplatePart(Name = PlaceholderPart, Type = typeof(Border))]
[TemplatePart(Name = ProgressPart, Type = typeof(FrameworkElement))]
[TemplatePart(Name = MissingPart, Type = typeof(TextBlock))]
public sealed class ReportFigure : Control
{
    private const string WrapPart = "PART_Wrap";
    private const string CanvasPart = "PART_Canvas";
    private const string ImagePart = "PART_Image";
    private const string HaloPart = "PART_Halo";
    private const string RingPart = "PART_Ring";
    private const string PlaceholderPart = "PART_Placeholder";
    private const string ProgressPart = "PART_Progress";
    private const string MissingPart = "PART_Missing";

    /// <summary>The ring's diameter, its border included (<c>.rep__marker</c>).</summary>
    internal const double RingSize = 22;

    /// <summary>The halo's diameter: the ring and its 2 DIP white box shadow.</summary>
    internal const double HaloSize = 26;

    /// <summary>The loading and missing placeholder's height (7.10).</summary>
    internal const double PlaceholderHeight = 120;

    /// <summary>The report's image loader, set on the main window; inherited.</summary>
    public static readonly DependencyProperty LoaderProperty = DependencyProperty.RegisterAttached(
        "Loader", typeof(ReportImageLoader), typeof(ReportFigure),
        new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.Inherits, OnSourceChanged));

    /// <summary>The open project's folder, set on the report's frame; inherited.</summary>
    public static readonly DependencyProperty ProjectDirProperty = DependencyProperty.RegisterAttached(
        "ProjectDir", typeof(string), typeof(ReportFigure),
        new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.Inherits, OnSourceChanged));

    /// <summary>The report's document scale, set on the report's frame; inherited.</summary>
    public static readonly DependencyProperty DocScaleProperty = DependencyProperty.RegisterAttached(
        "DocScale", typeof(double), typeof(ReportFigure),
        new FrameworkPropertyMetadata(DocScale.Default, FrameworkPropertyMetadataOptions.Inherits | FrameworkPropertyMetadataOptions.AffectsMeasure));

    /// <summary>The image to show, a <see cref="ReportImageKey"/>; null shows the missing-image state.</summary>
    public static readonly DependencyProperty ImageKeyProperty = DependencyProperty.Register(
        nameof(ImageKey), typeof(ReportImageKey?), typeof(ReportFigure), new FrameworkPropertyMetadata(null, OnSourceChanged));

    /// <summary>The stored zoom; 1 fits the image.</summary>
    public static readonly DependencyProperty ZoomProperty = DependencyProperty.Register(
        nameof(Zoom), typeof(double), typeof(ReportFigure), new FrameworkPropertyMetadata(1.0, FrameworkPropertyMetadataOptions.AffectsMeasure));

    /// <summary>The stored horizontal pan, a fraction of the scroll range.</summary>
    public static readonly DependencyProperty PanXProperty = DependencyProperty.Register(
        nameof(PanX), typeof(double), typeof(ReportFigure), new FrameworkPropertyMetadata(0.5, FrameworkPropertyMetadataOptions.AffectsMeasure));

    /// <summary>The stored vertical pan.</summary>
    public static readonly DependencyProperty PanYProperty = DependencyProperty.Register(
        nameof(PanY), typeof(double), typeof(ReportFigure), new FrameworkPropertyMetadata(0.5, FrameworkPropertyMetadataOptions.AffectsMeasure));

    /// <summary>The click ring, a <see cref="ReportMarker"/>, or null for none.</summary>
    public static readonly DependencyProperty MarkerProperty = DependencyProperty.Register(
        nameof(Marker), typeof(ReportMarker?), typeof(ReportFigure), new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsMeasure));

    /// <summary>The image's text alternative: the caption, as Electron's <c>alt</c>.</summary>
    public static readonly DependencyProperty DescriptionProperty = DependencyProperty.Register(
        nameof(Description), typeof(string), typeof(ReportFigure), new FrameworkPropertyMetadata(""));

    /// <summary>The brand's figure radius, which the image's clip follows; a resource reference.</summary>
    private static readonly DependencyProperty FigureRadiusProperty = DependencyProperty.Register(
        "FigureRadius", typeof(double), typeof(ReportFigure), new FrameworkPropertyMetadata(0.0, FrameworkPropertyMetadataOptions.AffectsMeasure));

    private Border? _wrap;
    private Canvas? _canvas;
    private Image? _image;
    private Ellipse? _halo;
    private Ellipse? _ring;
    private Border? _placeholder;
    private FrameworkElement? _progress;
    private TextBlock? _missingText;
    private ScrollViewer? _scroller;
    private CancellationTokenSource? _load;
    private ReportImageKey? _loading;
    private ReportImage? _shown;
    private ReportImageKey? _shownKey;
    private bool _missing;
    private double? _availableWidth;
    private ReportMarker? _ringFor;

    /// <summary>A figure; its template comes from its style.</summary>
    public ReportFigure()
    {
        SetResourceReference(FigureRadiusProperty, ThemeTokenKeys.RadiusValue("figure"));
        Focusable = false;
        Loaded += (_, _) => Reload();
        Unloaded += (_, _) => Stop();
    }

    /// <inheritdoc cref="ImageKeyProperty"/>
    public ReportImageKey? ImageKey
    {
        get => (ReportImageKey?)GetValue(ImageKeyProperty);
        set => SetValue(ImageKeyProperty, value);
    }

    /// <inheritdoc cref="ZoomProperty"/>
    public double Zoom
    {
        get => (double)GetValue(ZoomProperty);
        set => SetValue(ZoomProperty, value);
    }

    /// <inheritdoc cref="PanXProperty"/>
    public double PanX
    {
        get => (double)GetValue(PanXProperty);
        set => SetValue(PanXProperty, value);
    }

    /// <inheritdoc cref="PanYProperty"/>
    public double PanY
    {
        get => (double)GetValue(PanYProperty);
        set => SetValue(PanYProperty, value);
    }

    /// <inheritdoc cref="MarkerProperty"/>
    public ReportMarker? Marker
    {
        get => (ReportMarker?)GetValue(MarkerProperty);
        set => SetValue(MarkerProperty, value);
    }

    /// <inheritdoc cref="DescriptionProperty"/>
    public string Description
    {
        get => (string)GetValue(DescriptionProperty);
        set => SetValue(DescriptionProperty, value);
    }

    /// <summary>The image on screen, or null while it first loads or when it is missing.</summary>
    internal ReportImage? Shown => _shown;

    /// <summary>The figure shows the missing-image state.</summary>
    internal bool IsMissing => _missing && _shown is null;

    /// <summary>The figure shows the loading placeholder.</summary>
    internal bool IsLoadingPlaceholder => !_missing && _shown is null;

    /// <summary>The fit of the image on screen at the last measure; all zeros without one.</summary>
    internal ReportFit Fit { get; private set; }

    /// <summary>The image's offset inside the wrap from the stored pan (7.10); zero at zoom 1.</summary>
    internal Point PanOffset { get; private set; }

    /// <summary>The ring's centre in the wrap's content box, or null when no ring is drawn.</summary>
    internal Point? RingCentre { get; private set; }

    /// <summary>The loader of <paramref name="element"/>.</summary>
    public static ReportImageLoader? GetLoader(DependencyObject element) => (ReportImageLoader?)Required(element).GetValue(LoaderProperty);

    /// <summary>Sets the loader of <paramref name="element"/> and everything below it.</summary>
    public static void SetLoader(DependencyObject element, ReportImageLoader? value) => Required(element).SetValue(LoaderProperty, value);

    /// <summary>The project folder of <paramref name="element"/>.</summary>
    public static string? GetProjectDir(DependencyObject element) => (string?)Required(element).GetValue(ProjectDirProperty);

    /// <summary>Sets the project folder of <paramref name="element"/> and everything below it.</summary>
    public static void SetProjectDir(DependencyObject element, string? value) => Required(element).SetValue(ProjectDirProperty, value);

    /// <summary>The document scale of <paramref name="element"/>.</summary>
    public static double GetDocScale(DependencyObject element) => (double)Required(element).GetValue(DocScaleProperty);

    /// <summary>Sets the document scale of <paramref name="element"/> and everything below it.</summary>
    public static void SetDocScale(DependencyObject element, double value) => Required(element).SetValue(DocScaleProperty, value);

    /// <summary>
    /// The width to decode an image of <paramref name="natural"/> size at (7.11): the fit's width
    /// times the zoom in device pixels, never more than the natural width.
    /// </summary>
    internal static int DecodeWidth(ImageSize natural, double? availableWidth, double zoom, double scale, double dpiScale)
    {
        var fit = ReportGeometry.Fit(natural, availableWidth, zoom, scale);
        return (int)Math.Max(1, Math.Min(natural.Width, Math.Ceiling(fit.BaseW * zoom * dpiScale)));
    }

    /// <inheritdoc/>
    public override void OnApplyTemplate()
    {
        base.OnApplyTemplate();
        _wrap = GetTemplateChild(WrapPart) as Border;
        _canvas = GetTemplateChild(CanvasPart) as Canvas;
        _image = GetTemplateChild(ImagePart) as Image;
        _halo = GetTemplateChild(HaloPart) as Ellipse;
        _ring = GetTemplateChild(RingPart) as Ellipse;
        _placeholder = GetTemplateChild(PlaceholderPart) as Border;
        _progress = GetTemplateChild(ProgressPart) as FrameworkElement;
        _missingText = GetTemplateChild(MissingPart) as TextBlock;
        _ringFor = null;
    }

    /// <inheritdoc/>
    protected override Size MeasureOverride(Size constraint)
    {
        _availableWidth = double.IsFinite(constraint.Width) ? constraint.Width : null;
        Lay();
        var desired = base.MeasureOverride(constraint);
        GrowIfNeeded();
        return new Size(_availableWidth ?? desired.Width, desired.Height);
    }

    /// <inheritdoc/>
    protected override void OnDpiChanged(DpiScale oldDpi, DpiScale newDpi)
    {
        base.OnDpiChanged(oldDpi, newDpi);
        InvalidateMeasure();
    }

    private static DependencyObject Required(DependencyObject element)
    {
        ArgumentNullException.ThrowIfNull(element);
        return element;
    }

    private static void OnSourceChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is ReportFigure figure) figure.Reload();
    }

    // The template's parts sized and placed for the state shown.
    private void Lay()
    {
        if (_wrap is null || _placeholder is null) return;
        var scale = GetDocScale(this);
        if (_shown is not { } shown)
        {
            Fit = default;
            PanOffset = default;
            RingCentre = null;
            _wrap.Visibility = Visibility.Collapsed;
            _placeholder.Visibility = Visibility.Visible;
            var cap = JsMath.Round(ReportGeometry.BaseWidth * DocScale.Clamp(scale));
            _placeholder.Width = Math.Max(1, Math.Min(_availableWidth ?? cap, cap));
            _placeholder.Height = PlaceholderHeight;
            if (_progress is not null) _progress.Visibility = _missing ? Visibility.Collapsed : Visibility.Visible;
            if (_missingText is not null)
            {
                _missingText.Visibility = _missing ? Visibility.Visible : Visibility.Collapsed;
                _missingText.Text = ReportStrings.ImageMissing(ImageKey?.RelativePath ?? "");
            }
            return;
        }

        _placeholder.Visibility = Visibility.Collapsed;
        _wrap.Visibility = Visibility.Visible;
        var zoom = Zoom;
        var fit = ReportGeometry.Fit(shown.NaturalSize, _availableWidth, zoom, scale);
        Fit = fit;
        _wrap.Width = fit.WrapW;
        _wrap.Height = fit.WrapH;
        var innerW = Math.Max(0, fit.WrapW - ReportGeometry.WrapBorder * 2);
        var innerH = Math.Max(0, fit.WrapH - ReportGeometry.WrapBorder * 2);
        var imageW = fit.BaseW * zoom;
        var imageH = fit.BaseH * zoom;
        // Electron's scrollWidth - clientWidth: the image beyond the box it is framed in.
        var rangeX = Math.Max(0, imageW - innerW);
        var rangeY = Math.Max(0, imageH - innerH);
        var offset = new Point(-rangeX * PanX, -rangeY * PanY);
        PanOffset = offset;
        if (_canvas is not null)
        {
            _canvas.Width = innerW;
            _canvas.Height = innerH;
            var radius = Math.Max(0, Math.Min(FigureRadius - ReportGeometry.WrapBorder, Math.Min(innerW, innerH) / 2));
            _canvas.Clip = new RectangleGeometry(new Rect(0, 0, innerW, innerH), radius, radius);
        }
        if (_image is not null)
        {
            _image.Source = shown.Bitmap;
            _image.Width = imageW;
            _image.Height = imageH;
            Canvas.SetLeft(_image, offset.X);
            Canvas.SetTop(_image, offset.Y);
        }
        LayRing(shown.NaturalSize, imageW, imageH, offset);
    }

    // INV-REP-16: the ring at its fraction of the image, in image space, so it scales and pans with it.
    private void LayRing(ImageSize natural, double imageW, double imageH, Point offset)
    {
        if (_ring is null || _halo is null) return;
        var marker = Marker;
        if (marker?.FractionIn(natural) is not { } fraction)
        {
            RingCentre = null;
            _ring.Visibility = Visibility.Collapsed;
            _halo.Visibility = Visibility.Collapsed;
            return;
        }
        var centre = new Point(offset.X + fraction.X * imageW, offset.Y + fraction.Y * imageH);
        RingCentre = centre;
        if (_ringFor != marker)
        {
            _ringFor = marker;
            _ring.Stroke = Frozen(marker!.Value.Style.Stroke);
            _ring.Fill = Frozen(marker.Value.Style.Fill);
        }
        Canvas.SetLeft(_ring, centre.X - RingSize / 2);
        Canvas.SetTop(_ring, centre.Y - RingSize / 2);
        Canvas.SetLeft(_halo, centre.X - HaloSize / 2);
        Canvas.SetTop(_halo, centre.Y - HaloSize / 2);
        _ring.Visibility = Visibility.Visible;
        _halo.Visibility = Visibility.Visible;
    }

    // The step's stored colour, parsed by 04's rule: not a brand token, because it must match the ring baked into the PNG.
    private static SolidColorBrush Frozen(Rgba colour)
    {
        var brush = new SolidColorBrush(Color.FromArgb(colour.A, colour.R, colour.G, colour.B));
        brush.Freeze();
        return brush;
    }

    private double FigureRadius => GetValue(FigureRadiusProperty) is double r && double.IsFinite(r) ? r : 0;

    // A new key, folder or loader: load it, keeping what is on screen until the new image is ready.
    private void Reload()
    {
        if (!IsLoaded) return;
        var key = ImageKey;
        if (key == _shownKey && _shown is not null) return;
        if (key is not null && key == _loading) return;
        CancelLoad();
        if (key is null || GetProjectDir(this) is null || GetLoader(this) is null)
        {
            _shown = null;
            _shownKey = null;
            _missing = key is null;
            InvalidateMeasure();
            return;
        }
        if (_shownKey != key)
        {
            // A new image: until it loads, the old one stays if there is one; otherwise the placeholder.
            _missing = false;
            if (_shown is null) InvalidateMeasure();
        }
        StartWhenNear();
    }

    private void StartWhenNear()
    {
        if (NearViewport())
        {
            StopWatchingScroll();
            _ = LoadAsync();
            return;
        }
        WatchScroll();
    }

    private async Task LoadAsync()
    {
        if (ImageKey is not { } key || GetProjectDir(this) is not { } dir || GetLoader(this) is not { } loader) return;
        CancelLoad();
        var cts = new CancellationTokenSource();
        _load = cts;
        _loading = key;
        var available = _availableWidth;
        var zoom = Zoom;
        var scale = GetDocScale(this);
        var dpi = VisualTreeHelper.GetDpi(this).DpiScaleX;
        ReportImage? image;
        try
        {
            image = await loader.LoadAsync(dir, key.RelativePath, natural => DecodeWidth(natural, available, zoom, scale, dpi), cts.Token);
        }
        catch (OperationCanceledException)
        {
            return;
        }
        if (!ReferenceEquals(_load, cts)) return;
        _load = null;
        _loading = null;
        // EDGE-REP-50: the image and its natural size swap together.
        _shown = image;
        _shownKey = key;
        _missing = image is null;
        InvalidateMeasure();
    }

    // 7.11: decode again, larger, when the figure needs more pixels than it has (zoom in, a larger scale or width, a DPI change).
    private void GrowIfNeeded()
    {
        if (_shown is not { } shown || _loading is not null || ImageKey != _shownKey || !IsLoaded) return;
        var needed = DecodeWidth(shown.NaturalSize, _availableWidth, Zoom, GetDocScale(this), VisualTreeHelper.GetDpi(this).DpiScaleX);
        if (needed > shown.DecodedWidth + 1) _ = LoadAsync();
    }

    // Not disposed: the loader may still be registering on its token, which a disposed source refuses.
    private void CancelLoad()
    {
        if (_load is not { } cts) return;
        _load = null;
        _loading = null;
        cts.Cancel();
    }

    private void Stop()
    {
        CancelLoad();
        StopWatchingScroll();
    }

    // Within one viewport height above or below the report's viewport (lazy loading); anywhere when not in a scroller.
    private bool NearViewport()
    {
        var scroller = FindScroller();
        if (scroller is null) return true;
        try
        {
            var top = TransformToAncestor(scroller).Transform(new Point(0, 0)).Y;
            var bottom = top + Math.Max(ActualHeight, PlaceholderHeight);
            var height = scroller.ViewportHeight;
            return height > 0 && bottom >= -height && top <= 2 * height;
        }
        catch (InvalidOperationException)
        {
            return true;
        }
    }

    private ScrollViewer? FindScroller()
    {
        for (var node = VisualTreeHelper.GetParent(this); node is not null; node = VisualTreeHelper.GetParent(node))
        {
            if (node is ScrollViewer scroller) return scroller;
        }
        return null;
    }

    private void WatchScroll()
    {
        if (_scroller is not null) return;
        _scroller = FindScroller();
        if (_scroller is not null) _scroller.ScrollChanged += OnScrollChanged;
    }

    private void StopWatchingScroll()
    {
        if (_scroller is null) return;
        _scroller.ScrollChanged -= OnScrollChanged;
        _scroller = null;
    }

    private void OnScrollChanged(object sender, ScrollChangedEventArgs e)
    {
        if (!NearViewport()) return;
        StopWatchingScroll();
        _ = LoadAsync();
    }
}
