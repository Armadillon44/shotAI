using System.Collections.Specialized;
using ShotAI.App.Report;
using ShotAI.App.Tests.Support;
using ShotAI.Core.Model;
using ShotAI.Core.Store;
using Xunit;
using static ShotAI.App.Tests.Support.Manifests;

namespace ShotAI.App.Tests.Report;

/// <summary>
/// Spec 05 7.8 and 8.2: the report keeps one card per step, keyed by id and occurrence; a card
/// that survives a change is moved, never re-created (D-REP-3); a caption edit re-renders one
/// card (INV-REP-30, AC-REP-12); the overview shows only with steps (EDGE-REP-19, Q-REP-7).
/// </summary>
public sealed class ReportViewModelTests
{
    private static ReportViewModel Report(ProjectManifest manifest)
    {
        var report = new ReportViewModel(new FakeSession(new OpenedProject(@"C:\Projects\A", manifest), null, 0));
        report.Sync(manifest, ManifestChangeKind.External, null);
        return report;
    }

    private static ProjectStep[] Shots(int count, Func<int, string>? caption = null) =>
        [.. Enumerable.Range(1, count).Select(i => Shot($"s{i}", caption: caption?.Invoke(i) ?? $"Click {i}"))];

    /// <summary>Every card's property changes, as (card id, property) pairs, in order.</summary>
    private static List<(string? Id, string? Property)> Record(ReportViewModel report)
    {
        var seen = new List<(string?, string?)>();
        foreach (var card in report.Cards) card.PropertyChanged += (s, e) => seen.Add((((StepCardViewModel)s!).Id, e.PropertyName));
        return seen;
    }

    /// <summary>
    /// INV-REP-30, AC-REP-12: a caption edit on a 100-step project raises on one card only, for
    /// its caption and the row's accessible name (7.18), after a targeted sync and after a full one.
    /// </summary>
    [Theory]
    [InlineData(ManifestChangeKind.Local, true)]
    [InlineData(ManifestChangeKind.Persisted, true)]
    [InlineData(ManifestChangeKind.External, false)]
    [InlineData(ManifestChangeKind.Durable, false)]
    public Task CaptionEditTouchesOneCard(ManifestChangeKind kind, bool targeted) => Sta.RunAsync(() =>
    {
        var report = Report(Of("Handbook", Shots(100)));
        var seen = Record(report);
        var cards = report.Cards.ToList();
        var edited = Of("Handbook", Shots(100, i => i == 42 ? "Click Save" : $"Click {i}"));
        report.Sync(edited, kind, targeted ? ["s42"] : null);
        Assert.Equal([("s42", "Caption"), ("s42", "AutomationName")], seen);
        Assert.Equal("Click Save", report.Cards[41].Caption);
        Assert.Equal("Step 42, Click Save", report.Cards[41].AutomationName);
        Assert.Equal(cards, report.Cards);
    });

    /// <summary>D-REP-3: a move keeps every card's view model and raises one Move, so the container, its image and its focus survive.</summary>
    [Fact]
    public Task CardsSurviveAReorder() => Sta.RunAsync(() =>
    {
        var report = Report(Of("Handbook", Shot("a"), Shot("b"), Shot("c")));
        var (a, b, c) = (report.Cards[0], report.Cards[1], report.Cards[2]);
        var changes = new List<NotifyCollectionChangedAction>();
        ((INotifyCollectionChanged)report.Cards).CollectionChanged += (_, e) => changes.Add(e.Action);

        report.Sync(Of("Handbook", Shot("c"), Shot("a"), Shot("b")), ManifestChangeKind.Local, ["c"]);
        Assert.Equal([c, a, b], report.Cards);
        Assert.Equal([NotifyCollectionChangedAction.Move], changes);
        Assert.Equal([1, 2, 3], report.Cards.Select(x => x.Number));
        Assert.Equal([0, 1, 2], report.Cards.Select(x => x.Index));
    });

    /// <summary>A new step is a new card at its place; a removed one's card goes; the others stay.</summary>
    [Fact]
    public Task InsertAndRemoveKeepTheOthers() => Sta.RunAsync(() =>
    {
        var report = Report(Of("Handbook", Shot("a"), Shot("b")));
        var (a, b) = (report.Cards[0], report.Cards[1]);
        report.Sync(Of("Handbook", Shot("a"), Text("t", heading: "New"), Shot("b")), ManifestChangeKind.Local, ["t"]);
        Assert.Equal(3, report.Cards.Count);
        Assert.Same(a, report.Cards[0]);
        Assert.Same(b, report.Cards[2]);
        Assert.Equal(("t", "New", 2), (report.Cards[1].Id, report.Cards[1].Heading, report.Cards[1].Number));
        Assert.Equal(3, b.Number);

        report.Sync(Of("Handbook", Text("t", heading: "New"), Shot("b")), ManifestChangeKind.Local, ["a"]);
        Assert.Equal(["t", "b"], report.Cards.Select(x => x.Id));
        Assert.Same(b, report.Cards[1]);
        Assert.Equal(2, b.Number);
    });

    /// <summary>EDGE-REP-36: duplicate ids are separate cards, numbered by position.</summary>
    [Fact]
    public Task DuplicateIdsAreSeparateCards() => Sta.RunAsync(() =>
    {
        var report = Report(Of("Handbook", Shot("a", caption: "first"), Shot("a", caption: "second"), Shot("b")));
        Assert.Equal([1, 2, 3], report.Cards.Select(x => x.Number));
        Assert.Equal(["first", "second", ""], report.Cards.Select(x => x.Caption));
        Assert.Equal(3, report.Cards.Select(x => x.Key).Distinct().Count());
    });

    /// <summary>
    /// 7.8: a callout conversion renumbers the later cards although only the converted step is
    /// named as changed; the earlier cards raise nothing.
    /// </summary>
    [Fact]
    public Task CalloutConversionRenumbersLaterCards() => Sta.RunAsync(() =>
    {
        var report = Report(Of("Handbook", Shot("a"), Text("t", body: "x"), Shot("b"), Shot("c")));
        Assert.Equal([1, 2, 3, 4], report.Cards.Select(x => x.Number));
        var seen = Record(report);
        report.Sync(Of("Handbook", Shot("a"), Text("t", body: "x", callout: "note"), Shot("b"), Shot("c")), ManifestChangeKind.Local, ["t"]);
        Assert.Equal([1, null, 2, 3], report.Cards.Select(x => x.Number));
        Assert.Equal(StepCardKind.Callout, report.Cards[1].Kind);
        Assert.DoesNotContain(seen, e => e.Id == "a");
        Assert.Contains(("b", "Number"), seen);
        Assert.Contains(("c", "Number"), seen);
    });

    /// <summary>
    /// 7.8: a targeted sync updates only the steps named and the cards whose place or number
    /// changed, trusting the session's affected list; a full sync updates any card that differs.
    /// </summary>
    [Fact]
    public Task ATargetedSyncTrustsTheAffectedSteps() => Sta.RunAsync(() =>
    {
        var report = Report(Of("Handbook", Shot("a", caption: "one"), Shot("b", caption: "two")));
        var changed = Of("Handbook", Shot("a", caption: "ONE"), Shot("b", caption: "TWO"));
        report.Sync(changed, ManifestChangeKind.Local, ["b"]);
        Assert.Equal(["one", "TWO"], report.Cards.Select(x => x.Caption));
        report.Sync(changed, ManifestChangeKind.External, null);
        Assert.Equal(["ONE", "TWO"], report.Cards.Select(x => x.Caption));
    });

    /// <summary>EDGE-REP-19, Q-REP-7: the overview shows only with steps and a heading or a body.</summary>
    [Theory]
    [InlineData(0, "Intro", "Text", false)]
    [InlineData(1, "Intro", "Text", true)]
    [InlineData(1, "Intro", "", true)]
    [InlineData(1, "", "Text", true)]
    [InlineData(1, "", "", false)]
    public Task OverviewShowsOnlyWithSteps(int steps, string heading, string body, bool shows) => Sta.RunAsync(() =>
    {
        var manifest = Of("Handbook", Shots(steps));
        manifest.Intro = new SopIntro(heading, body);
        var report = Report(manifest);
        Assert.Equal(steps == 0, report.IsEmpty);
        Assert.Equal(shows, report.HasIntro);
        Assert.Equal((heading, body), (report.IntroHeading, report.IntroBody));
        Assert.Equal((heading.Length > 0, body.Length > 0), (report.HasIntroHeading, report.HasIntroBody));
    });

    /// <summary>No intro at all is an empty overview.</summary>
    [Fact]
    public Task NoIntroIsNoOverview() => Sta.RunAsync(() =>
    {
        var report = Report(Of("Handbook", Shots(2)));
        Assert.Equal((false, "", ""), (report.HasIntro, report.IntroHeading, report.IntroBody));
        Assert.False(report.IsEmpty);
    });

    /// <summary>INV-REP-11: the report lays out at the committed scale, clamped and snapped.</summary>
    [Theory]
    [InlineData(null, 1.0)]
    [InlineData(0.83, 0.85)]
    [InlineData(0.1, 0.65)]
    [InlineData(5.0, 1.25)]
    [InlineData(1.1, 1.1)]
    public Task ScaleIsTheCommittedScale(double? stored, double scale) => Sta.RunAsync(() =>
    {
        var report = Report(Of("Handbook", stored, Shots(1)));
        Assert.Equal(scale, report.Scale);
    });

    [Fact]
    public Task ExposesTheSession() => Sta.RunAsync(() =>
    {
        var session = new FakeSession(new OpenedProject(@"C:\Projects\A", Of("Handbook")), null, 0);
        var report = new ReportViewModel(session);
        Assert.Same(session, report.Session);
        Assert.Equal(@"C:\Projects\A", report.ProjectDir);
        Assert.Empty(report.Cards);
        Assert.True(report.IsEmpty);
        Assert.Same(session, new ReportViewModelFactory().Create(session).Session);
    });

    [Fact]
    public Task ArgumentsAreChecked() => Sta.RunAsync(() =>
    {
        Assert.Throws<ArgumentNullException>(() => new ReportViewModel(null!));
        Assert.Throws<ArgumentNullException>(() => Report(Of("Handbook")).Sync(null!, ManifestChangeKind.External, null));
    });
}
