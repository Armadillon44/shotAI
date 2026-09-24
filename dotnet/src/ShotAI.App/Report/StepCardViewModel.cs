using System.Globalization;
using System.Text.Json.Nodes;
using CommunityToolkit.Mvvm.ComponentModel;
using ShotAI.Core.Model;
using ShotAI.Core.Report;

namespace ShotAI.App.Report;

/// <summary>Which of the four card layouts a step takes (spec 05 2.10).</summary>
public enum StepCardKind
{
    /// <summary>A screenshot step: caption, figure, instructions and window line.</summary>
    Shot,

    /// <summary>A text step with no callout, or one of an unknown kind (#90).</summary>
    PlainText,

    /// <summary>A note, caution or warning box.</summary>
    Callout,

    /// <summary>A section divider: a rule and a heading, no card frame.</summary>
    Section,
}

/// <summary>
/// One report card (spec 05 7.8): what the card's XAML binds, taken from the step and its place
/// in the list. <see cref="Update"/> sets each property, the derived flags the templates switch
/// on included, and each raises only when its value changed, so a caption edit re-renders the
/// caption of one card and its row name (INV-REP-30). The editing members join with the editing
/// UI (WP-C2).
/// </summary>
public sealed partial class StepCardViewModel : ViewModelBase
{
    private JsonObject? _snapshot;
    private CardContext _context;

    /// <summary>The card of the step keyed <paramref name="key"/> (<see cref="CardListDiff.Keys"/>).</summary>
    public StepCardViewModel(string key)
    {
        ArgumentNullException.ThrowIfNull(key);
        Key = key;
    }

    /// <summary>The card's key: the step's id and occurrence, fixed for the card's life.</summary>
    public string Key { get; }

    /// <summary>The step's id; null when it is not a string.</summary>
    [ObservableProperty]
    private string? _id;

    /// <summary>The step's position.</summary>
    [ObservableProperty]
    private int _index;

    /// <summary>The display number; null for a note, caution, warning or section (INV-REP-1).</summary>
    [ObservableProperty]
    private int? _number;

    /// <summary>The layout.</summary>
    [ObservableProperty]
    private StepCardKind _kind;

    /// <summary>The callout kind, <c>note</c>, <c>caution</c> or <c>warning</c>, for a callout card; else null.</summary>
    [ObservableProperty]
    private string? _calloutKind;

    /// <summary>The caption of a shot.</summary>
    [ObservableProperty]
    private string _caption = "";

    /// <summary>A text step's or a callout's heading.</summary>
    [ObservableProperty]
    private string _heading = "";

    /// <summary>The instructions of a shot, or the text of a text step or a callout.</summary>
    [ObservableProperty]
    private string _body = "";

    /// <summary>The line naming the window the shot was taken in, or null (EDGE-REP-25).</summary>
    [ObservableProperty]
    private string? _windowLine;

    /// <summary>The image the figure shows and when it reloads (INV-REP-17); null for a text step.</summary>
    [ObservableProperty]
    private ReportImageKey? _imageKey;

    /// <summary>The stored zoom, read as the export reads it (EDGE-REP-47).</summary>
    [ObservableProperty]
    private double _zoom = 1;

    /// <summary>The stored horizontal pan.</summary>
    [ObservableProperty]
    private double _panX = 0.5;

    /// <summary>The stored vertical pan.</summary>
    [ObservableProperty]
    private double _panY = 0.5;

    /// <summary>The click ring, or null when none is drawn (INV-REP-16).</summary>
    [ObservableProperty]
    private ReportMarker? _marker;

    /// <summary>The row's accessible name (7.18): <c>Step 3, Click Save</c>, <c>Note callout</c>, or a section's heading.</summary>
    [ObservableProperty]
    private string _automationName = "";

    // What the templates switch on, derived from the properties above and set with them, so each raises only when it changes.

    /// <summary>A shot card.</summary>
    [ObservableProperty]
    private bool _isShot;

    /// <summary>A plain text card.</summary>
    [ObservableProperty]
    private bool _isPlainText;

    /// <summary>A note, caution or warning card.</summary>
    [ObservableProperty]
    private bool _isCallout;

    /// <summary>A section divider.</summary>
    [ObservableProperty]
    private bool _isSection;

    /// <summary>The badge's number, or "".</summary>
    [ObservableProperty]
    private string _numberText = "";

    /// <summary>The rail shows a numbered badge.</summary>
    [ObservableProperty]
    private bool _hasNumber;

    /// <summary>A callout's rail glyph (<c>CALLOUT_GLYPH</c>); null for any other card.</summary>
    [ObservableProperty]
    private string? _glyph;

    /// <summary>A callout badge's tooltip, the kind as stored: <c>note callout &#8212; not a numbered step</c>.</summary>
    [ObservableProperty]
    private string? _badgeTip;

    /// <summary>The caption is not empty.</summary>
    [ObservableProperty]
    private bool _hasCaption;

    /// <summary>The heading is not empty.</summary>
    [ObservableProperty]
    private bool _hasHeading;

    /// <summary>The body is not empty.</summary>
    [ObservableProperty]
    private bool _hasBody;

    /// <summary>A plain text card shows its body a second time, below the heading, only when both are set (2.10).</summary>
    [ObservableProperty]
    private bool _hasHeadingAndBody;

    /// <summary>A plain text card with no heading shows its body as its first line (2.10).</summary>
    [ObservableProperty]
    private bool _hasBodyOnly;

    /// <summary>Neither heading nor body: a callout or section shows its empty line.</summary>
    [ObservableProperty]
    private bool _isBlank;

    /// <summary>There is a window line.</summary>
    [ObservableProperty]
    private bool _hasWindowLine;

    /// <summary>Whether the card shows a step, at <paramref name="context"/>.</summary>
    internal bool Shows(CardContext context) => _snapshot is not null && _context == context;

    /// <summary>
    /// Shows <paramref name="step"/> at <paramref name="context"/>. Nothing is set when the step's
    /// JSON equals the one shown last and so does its context; otherwise each property is set, and
    /// raises its change only when its value differs.
    /// </summary>
    /// <returns>Whether the step or its context differed from the ones shown.</returns>
    public bool Update(ProjectStep step, CardContext context)
    {
        ArgumentNullException.ThrowIfNull(step);
        if (_snapshot is not null && context == _context && JsonNode.DeepEquals(_snapshot, step.Raw)) return false;
        _snapshot = (JsonObject)step.Raw.DeepClone();
        _context = context;

        Id = step.Id;
        Index = context.Index;
        Number = context.Number;
        var callout = step.IsText ? step.KnownCallout : null;
        Kind = !step.IsText ? StepCardKind.Shot
            : callout == CalloutKinds.Section ? StepCardKind.Section
            : callout is not null ? StepCardKind.Callout
            : StepCardKind.PlainText;
        CalloutKind = Kind == StepCardKind.Callout ? callout : null;
        Caption = step.Caption;
        Heading = step.Heading ?? "";
        Body = step.Body ?? "";
        WindowLine = Kind == StepCardKind.Shot ? global::ShotAI.Core.Report.WindowLine.For(step) : null;
        ImageKey = ReportPresentation.ImageKey(step);
        var framing = ReportPresentation.Framing(step);
        Zoom = framing.Zoom;
        PanX = framing.PanX;
        PanY = framing.PanY;
        Marker = Kind == StepCardKind.Shot ? ReportPresentation.MarkerFor(step) : null;
        AutomationName = NameOf(Kind, Number, CalloutKind, Caption, Heading, Body);

        IsShot = Kind == StepCardKind.Shot;
        IsPlainText = Kind == StepCardKind.PlainText;
        IsCallout = Kind == StepCardKind.Callout;
        IsSection = Kind == StepCardKind.Section;
        NumberText = Number?.ToString(CultureInfo.InvariantCulture) ?? "";
        HasNumber = Number is not null;
        Glyph = CalloutGlyphs.For(CalloutKind) is { Length: > 0 } glyph ? glyph : null;
        BadgeTip = CalloutKind is { } kind ? ReportStrings.CalloutBadgeTip(kind) : null;
        HasCaption = Caption.Length > 0;
        HasHeading = Heading.Length > 0;
        HasBody = Body.Length > 0;
        HasHeadingAndBody = HasHeading && HasBody;
        HasBodyOnly = !HasHeading && HasBody;
        IsBlank = !HasHeading && !HasBody;
        HasWindowLine = WindowLine is not null;
        return true;
    }

    // 7.18: a numbered row by its number and the line it shows first; a callout by its kind; a section by its heading.
    private static string NameOf(StepCardKind kind, int? number, string? callout, string caption, string heading, string body) => kind switch
    {
        StepCardKind.Callout => ReportStrings.CalloutName(callout!),
        StepCardKind.Section => heading.Length > 0 ? heading : ReportStrings.SectionName,
        StepCardKind.Shot => ReportStrings.StepName(number ?? 0, caption),
        _ => ReportStrings.StepName(number ?? 0, heading.Length > 0 ? heading : body),
    };
}
