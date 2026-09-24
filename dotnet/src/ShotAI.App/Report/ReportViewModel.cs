using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using ShotAI.Core.Geometry;
using ShotAI.Core.Home;
using ShotAI.Core.Model;
using ShotAI.Core.Report;
using ShotAI.Core.Store;

namespace ShotAI.App.Report;

/// <summary>
/// The report of one open project (spec 05 7.8): the overview and the cards, kept in step with
/// the session's manifest by <see cref="Sync"/>. A card that survives a change keeps its view
/// model and its container, moved and never re-created (D-REP-3), so its image and focus survive.
/// The edit state and the edits join with the editing UI (WP-C1, WP-C2). UI thread only.
/// </summary>
public sealed partial class ReportViewModel : ViewModelBase
{
    private readonly ObservableCollection<StepCardViewModel> _cards = [];
    private IReadOnlyList<string> _keys = [];

    /// <summary>The report of <paramref name="session"/>'s project; empty until the first <see cref="Sync"/>.</summary>
    public ReportViewModel(IProjectSession session)
    {
        ArgumentNullException.ThrowIfNull(session);
        Session = session;
        Cards = new ReadOnlyObservableCollection<StepCardViewModel>(_cards);
    }

    /// <summary>The open project's session, which the edits go through (WP-C2).</summary>
    public IProjectSession Session { get; }

    /// <summary>The project folder the images are read from.</summary>
    public string ProjectDir => Session.ProjectDir;

    /// <summary>The cards, one per step, in step order.</summary>
    public ReadOnlyObservableCollection<StepCardViewModel> Cards { get; }

    /// <summary>The project has no steps: the empty state shows, and the overview does not (EDGE-REP-19, Q-REP-7).</summary>
    [ObservableProperty]
    private bool _isEmpty = true;

    /// <summary>The overview shows: steps exist and the intro has a heading or a body (2.8).</summary>
    [ObservableProperty]
    private bool _hasIntro;

    /// <summary>The overview's heading, or "".</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasIntroHeading))]
    private string _introHeading = "";

    /// <summary>The overview's text, or "".</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasIntroBody))]
    private string _introBody = "";

    /// <summary>The document scale the report lays out at: the committed <c>displayScale</c>, clamped.</summary>
    [ObservableProperty]
    private double _scale = DocScale.Default;

    /// <summary>The overview's heading is not empty.</summary>
    public bool HasIntroHeading => IntroHeading.Length > 0;

    /// <summary>The overview's text is not empty.</summary>
    public bool HasIntroBody => IntroBody.Length > 0;

    /// <summary>
    /// Shows <paramref name="manifest"/>: the cards are moved, inserted and removed by the keyed
    /// diff, then each is updated. After a local or persisted change with a known set of steps,
    /// only those cards and the cards whose place or number changed are updated; after any other,
    /// every card is, and one whose step and place are as shown changes nothing (7.8).
    /// </summary>
    /// <param name="manifest">The session's <c>Current</c>.</param>
    /// <param name="kind">Why it changed.</param>
    /// <param name="affected">The steps a local change touched, or null.</param>
    public void Sync(ProjectManifest manifest, ManifestChangeKind kind, IReadOnlyList<string>? affected)
    {
        ArgumentNullException.ThrowIfNull(manifest);
        var steps = manifest.Steps;
        var context = StepContext.Build(steps);
        var keys = CardListDiff.Keys(steps);
        foreach (var edit in CardListDiff.Plan(_keys, keys))
        {
            switch (edit.Kind)
            {
                case ListEditKind.Remove:
                    _cards.RemoveAt(edit.Index);
                    break;
                case ListEditKind.Move:
                    _cards.Move(edit.From, edit.Index);
                    break;
                case ListEditKind.Insert:
                    _cards.Insert(edit.Index, new StepCardViewModel(edit.Item));
                    break;
            }
        }
        _keys = keys;

        var targeted = affected is not null && kind is ManifestChangeKind.Local or ManifestChangeKind.Persisted;
        var touched = targeted ? new HashSet<string>(affected!, StringComparer.Ordinal) : null;
        for (var i = 0; i < steps.Count; i++)
        {
            var card = _cards[i];
            var at = context.For(i);
            if (touched is not null && !(steps[i].Id is { } id && touched.Contains(id)) && card.Shows(at)) continue;
            card.Update(steps[i], at);
        }

        IsEmpty = steps.Count == 0;
        var intro = manifest.Intro;
        IntroHeading = intro?.Heading ?? "";
        IntroBody = intro?.Body ?? "";
        HasIntro = !IsEmpty && (HasIntroHeading || HasIntroBody);
        Scale = DocScale.Clamp(manifest.DisplayScale ?? DocScale.Default);
    }
}

/// <summary>
/// Makes the <see cref="ReportViewModel"/> of each open project (ARCHITECTURE 4.1 C6, spec 05
/// 7.19): it takes the session, a runtime argument, so it cannot come from the container.
/// </summary>
public sealed class ReportViewModelFactory
{
    /// <summary>The report of <paramref name="session"/>'s project.</summary>
    public ReportViewModel Create(IProjectSession session) => new(session);
}
