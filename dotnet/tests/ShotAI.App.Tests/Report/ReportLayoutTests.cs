using System.Windows;
using System.Windows.Controls;
using Microsoft.Extensions.Logging.Abstractions;
using ShotAI.App.Report;
using ShotAI.App.Tests.Support;
using ShotAI.Core.Geometry;
using ShotAI.Core.Model;
using ShotAI.Core.Store;
using ShotAI.Platform.Imaging;
using Xunit;
using static ShotAI.App.Tests.Support.Manifests;

namespace ShotAI.App.Tests.Report;

/// <summary>
/// Spec 05 8.2, 7.9 and INV-REP-9, INV-REP-34, in real WPF layout: a full-width figure is the
/// export's figure at every detent (AC-REP-3); the column comes from the space offered, never
/// from the content; a narrow window narrows the frame to the viewport and the figure fits it.
/// </summary>
public sealed class ReportLayoutTests
{
    /// <summary>
    /// The project view over <see cref="FakeSessions"/>. At a <c>viewWidth</c> it is laid out at
    /// that size on a canvas, so a report wider than the runner's screen (1084 DIP at 125%)
    /// needs no window that wide; with a <c>windowWidth</c> it fills a window of that width.
    /// </summary>
    private sealed class Rig : IDisposable
    {
        public Rig(double? viewWidth = null, double? windowWidth = null, double height = 900)
        {
            Project = new ProjectDetailViewModel(Projects, Sessions, new ReportViewModelFactory(), new RecordingLayout(), NullLogger<ProjectDetailViewModel>.Instance);
            View = new ProjectDetailView { DataContext = Project };
            FrameworkElement content = View;
            if (viewWidth is { } w)
            {
                View.Width = w;
                View.Height = height;
                var canvas = new Canvas();
                canvas.Children.Add(View);
                content = canvas;
            }
            Window = TestShell.Host(content, windowWidth ?? 720, windowWidth is null ? 740 : height);
            ReportFigure.SetLoader(Window, new ReportImageLoader(new ReportImageDecoder(), NullLogger<ReportImageLoader>.Instance));
            Window.Show();
        }

        public ListingProjects Projects { get; } = new();

        public FakeSessions Sessions { get; } = new();

        public ProjectDetailViewModel Project { get; }

        public ProjectDetailView View { get; }

        public Window Window { get; }

        public void Dispose()
        {
            Window.Close();
            Project.Dispose();
        }

        public async Task OpenAsync(string dir, ProjectManifest manifest)
        {
            Projects.CanOpen(dir, manifest);
            Assert.True(await Project.OpenAsync(dir));
            await TestShell.Settle();
        }

        /// <summary>The first card's figure once its image is on screen.</summary>
        public async Task<ReportFigure> FigureAsync()
        {
            ReportFigure? figure = null;
            Assert.True(await TestShell.UntilAsync(() => (figure = VisualTree.Descendants<ReportFigure>(View).FirstOrDefault())?.Shown is not null));
            return figure!;
        }

        /// <summary>The report's content column: the frame's child.</summary>
        public FrameworkElement Column => (FrameworkElement)View.ReportFrame.Child;
    }

    private static async Task<string> ProjectWith(TempDir temp, string name, byte[] png)
    {
        var dir = temp.Combine("project");
        Directory.CreateDirectory(Path.Combine(dir, "shots"));
        await File.WriteAllBytesAsync(Path.Combine(dir, "shots", name), png, TestContext.Current.CancellationToken);
        return dir;
    }

    private static Border Wrap(ReportFigure figure) => VisualTree.Named<Border>(figure, "PART_Wrap");

    /// <summary>
    /// AC-REP-3, INV-REP-9: a 3840 by 2160 capture in a view wider than the report frame: the
    /// wrap is exactly <see cref="DocWidths.HtmlImageMax"/> wide at all 13 detents, and the frame
    /// and column are the export's.
    /// </summary>
    [Fact]
    public Task FigureWidthEqualsExportAtEveryDetent() => Sta.RunAsync(async () =>
    {
        using var temp = new TempDir();
        var dir = await ProjectWith(temp, "wide.png", TestImages.Png(3840, 2160));
        using var r = new Rig(viewWidth: 1400);
        await r.OpenAsync(dir, Of("Wide", DocScale.Min, Shot("s1", "shots/wide.png")));
        var figure = await r.FigureAsync();
        var session = r.Sessions.Created[0];
        Assert.Equal(13, DocScale.Detents.Count);
        foreach (var s in DocScale.Detents)
        {
            session.Raise(Of("Wide", s, Shot("s1", "shots/wide.png")), ManifestChangeKind.Persisted, []);
            await TestShell.Settle();
            var widths = DocScale.Widths(s);
            Assert.Equal(s, r.Project.Report!.Scale);
            Assert.Equal(widths.RepFrame, r.View.ReportFrame.FrameWidth);
            Assert.Equal(widths.HtmlColumn, r.Column.ActualWidth, 6);
            Assert.Equal(widths.HtmlImageMax, figure.ActualWidth, 6);
            Assert.Equal(widths.HtmlImageMax, Wrap(figure).ActualWidth, 6);
            Assert.Equal(widths.HtmlImageMax, figure.Fit.WrapW);
        }
    });

    /// <summary>INV-REP-34: an empty project and a one-text-step project both lay out at the full report frame.</summary>
    [Theory]
    [InlineData(0, 1.0)]
    [InlineData(1, 1.0)]
    [InlineData(0, 1.25)]
    [InlineData(1, 1.25)]
    [InlineData(1, 0.65)]
    public Task EmptyAndSmallProjectsUseTheFullColumn(int steps, double scale) => Sta.RunAsync(async () =>
    {
        using var r = new Rig(viewWidth: 1400);
        ProjectStep[] all = [Text("t1", heading: "A")];
        await r.OpenAsync(@"C:\Projects\Small", Of("Small", scale, all[..steps]));
        var widths = DocScale.Widths(scale);
        Assert.Equal(widths.RepFrame, r.View.ReportFrame.FrameWidth);
        Assert.Equal(widths.HtmlColumn, r.Column.ActualWidth, 6);
        Assert.Equal(steps == 0 ? Visibility.Visible : Visibility.Collapsed, VisualTree.Named<TextBlock>(r.View, "EmptyHint").Visibility);
    });

    /// <summary>7.9: in a 680 DIP window the frame is the viewport, the figure fits the card, and the wrap never crops the image.</summary>
    [Fact]
    public Task NarrowWindowShrinksTheColumn() => Sta.RunAsync(async () =>
    {
        using var temp = new TempDir();
        var dir = await ProjectWith(temp, "wide.png", TestImages.Png(1920, 1200));
        using var r = new Rig(windowWidth: 680, height: 700);
        await r.OpenAsync(dir, Of("Wide", Shot("s1", "shots/wide.png")));
        var figure = await r.FigureAsync();
        await TestShell.Settle();
        var viewport = r.View.ScrollViewer.ViewportWidth;
        Assert.True(viewport < DocScale.Widths(1).RepFrame);
        Assert.Equal(viewport, r.View.ReportFrame.FrameWidth, 6);
        Assert.Equal(viewport - ReportLayout.FramePaddingX * 2, r.Column.ActualWidth, 1);
        var offered = ReportLayout.FigureWidth(1, viewport);
        Assert.True(Wrap(figure).ActualWidth <= offered + 0.5, $"wrap {Wrap(figure).ActualWidth}, offered {offered}");
        Assert.True(Wrap(figure).ActualWidth >= offered - 1.5, $"wrap {Wrap(figure).ActualWidth}, offered {offered}");
        var (x, y) = ReportGeometry.WrapContentSlack(figure.Fit);
        Assert.True(x >= 0 && y >= 0, $"slack {x}, {y}");
    });

    /// <summary>EDGE-REP-42: every open starts the report at the top.</summary>
    [Fact]
    public Task EachOpenStartsAtTheTop() => Sta.RunAsync(async () =>
    {
        using var r = new Rig(windowWidth: 720, height: 500);
        ProjectStep[] many = [.. Enumerable.Range(1, 30).Select(i => Text($"t{i}", heading: $"Step {i}", body: "Some text"))];
        await r.OpenAsync(@"C:\Projects\A", Of("A", many));
        r.View.ScrollViewer.ScrollToVerticalOffset(600);
        await TestShell.Settle();
        Assert.Equal(600, r.View.ScrollViewer.VerticalOffset, 3);
        await r.OpenAsync(@"C:\Projects\B", Of("B", many));
        Assert.Equal(0, r.View.ScrollViewer.VerticalOffset, 3);
    });
}
