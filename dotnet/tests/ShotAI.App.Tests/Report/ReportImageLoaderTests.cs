using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using ShotAI.App.Report;
using ShotAI.App.Tests.Chrome;
using ShotAI.App.Tests.Support;
using ShotAI.Core.Editor;
using ShotAI.Core.Geometry;
using ShotAI.Core.Report;
using ShotAI.Core.Store;
using ShotAI.Platform.Imaging;
using Xunit;

namespace ShotAI.App.Tests.Report;

/// <summary>
/// The report's images (spec 05 7.10, 7.11, 8.2): the loader reads only confined image paths and
/// holds no file after a read (INV-REP-18, INV-REP-32), and the figure decodes at the size it
/// shows, again only for a new key or when it grows, near the viewport only, keeping the image
/// it shows until the next one is ready (EDGE-REP-50).
/// </summary>
public sealed class ReportImageLoaderTests
{
    private static ReportImageLoader Loader(ILogger<ReportImageLoader>? log = null) =>
        new(new ReportImageDecoder(), log ?? NullLogger<ReportImageLoader>.Instance);

    private static async Task<string> ProjectWith(TempDir temp, params (string Path, byte[] Bytes)[] files)
    {
        var dir = temp.Combine("project");
        Directory.CreateDirectory(Path.Combine(dir, "shots"));
        foreach (var (path, bytes) in files) await File.WriteAllBytesAsync(Path.Combine(dir, path), bytes, TestContext.Current.CancellationToken);
        return dir;
    }

    private static ReportFigure Figure(string projectDir, ReportImageLoader loader, string relativePath, double renderRev = 0)
    {
        var templates = ControlStylesTests.Load("Report/StepCardTemplates.xaml");
        var figure = new ReportFigure { Template = (ControlTemplate)templates["ReportFigure.Template"], ImageKey = new ReportImageKey(relativePath, renderRev) };
        ReportFigure.SetProjectDir(figure, projectDir);
        ReportFigure.SetLoader(figure, loader);
        return figure;
    }

    private static Window Host(FrameworkElement content, double width = 800, double height = 700)
    {
        var window = TestShell.Host(content, width, height);
        window.Resources.MergedDictionaries.Add(ControlStylesTests.Load("Report/StepCardTemplates.xaml"));
        return window;
    }

    [Fact]
    public Task DoesNotHoldTheFile() => Sta.RunAsync(async () =>
    {
        using var temp = new TempDir();
        var dir = await ProjectWith(temp, ("shots/a.png", TestImages.Png(40, 30)));
        var image = await Loader().LoadAsync(dir, "shots/a.png", n => (int)n.Width, TestContext.Current.CancellationToken);
        Assert.NotNull(image);
        var path = Path.Combine(dir, "shots", "a.png");
        var replacement = Path.Combine(dir, "shots", "a.png.tmp");
        await File.WriteAllBytesAsync(replacement, TestImages.Png(20, 10), TestContext.Current.CancellationToken);
        File.Move(replacement, path, overwrite: true);
        File.Delete(path);
        Assert.False(File.Exists(path));
    });

    /// <summary>INV-REP-18: a path outside the project, or not a .png, .jpg or .jpeg, is never read.</summary>
    [Theory]
    [InlineData("..\\secret.png")]
    [InlineData("../secret.png")]
    [InlineData("shots\\..\\..\\secret.png")]
    [InlineData("shots/x.exe")]
    [InlineData("OUTSIDE")]
    public Task RefusesPathsOutsideTheProject(string relative) => Sta.RunAsync(async () =>
    {
        using var temp = new TempDir();
        var dir = await ProjectWith(temp, ("shots/x.exe", TestImages.Png(4, 4)));
        await File.WriteAllBytesAsync(temp.Combine("secret.png"), TestImages.Png(4, 4), TestContext.Current.CancellationToken);
        if (relative == "OUTSIDE") relative = temp.Combine("secret.png");
        var loader = Loader();
        Assert.Null(await loader.LoadAsync(dir, relative, n => (int)n.Width, TestContext.Current.CancellationToken));
        Assert.Equal(0, loader.DecodeCount);
    });

    /// <summary>
    /// 01 D-6, EDGE-MODEL-20: a drive letter's colon is hostile, so an absolute path is refused
    /// even when it names a file inside the project, and nothing is read. Electron's
    /// <c>confinePath</c> accepted one; the native rule is the stricter one.
    /// </summary>
    [Fact]
    public Task ADriveLetterPathIsRefusedEvenInsideTheProject() => Sta.RunAsync(async () =>
    {
        using var temp = new TempDir();
        var dir = await ProjectWith(temp, ("shots/a.png", TestImages.Png(4, 4)));
        var loader = Loader();
        var absolute = Path.Combine(dir, "shots", "a.png");
        Assert.Contains(':', absolute);
        Assert.Null(await loader.LoadAsync(dir, absolute, n => (int)n.Width, TestContext.Current.CancellationToken));
        Assert.Equal(0, loader.DecodeCount);
        Assert.NotNull(await loader.LoadAsync(dir, "shots/a.png", n => (int)n.Width, TestContext.Current.CancellationToken));
    });

    /// <summary>INV-REP-18, R-ARCH-21: GIF and BMP bytes under a .png name are the missing state; the decoder refused them before WIC.</summary>
    [Fact]
    public Task RefusesBytesThatAreNotPngOrJpeg() => Sta.RunAsync(async () =>
    {
        using var temp = new TempDir();
        using var logs = new CapturingLoggerProvider();
        var dir = await ProjectWith(temp, ("shots/gif.png", TestImages.Gif(8, 8)), ("shots/bmp.png", TestImages.Bmp(8, 8)));
        var loader = Loader(new Logger<ReportImageLoader>(logs));
        Assert.Null(await loader.LoadAsync(dir, "shots/gif.png", n => (int)n.Width, TestContext.Current.CancellationToken));
        Assert.Null(await loader.LoadAsync(dir, "shots/bmp.png", n => (int)n.Width, TestContext.Current.CancellationToken));
        Assert.Equal(2, logs.Entries.Count(e => e.Level == LogLevel.Debug && e.Exception is UnsupportedImageException));
    });

    [Fact]
    public Task AMissingFileIsNotShown() => Sta.RunAsync(async () =>
    {
        using var temp = new TempDir();
        var dir = await ProjectWith(temp);
        Assert.Null(await Loader().LoadAsync(dir, "shots/none.png", n => (int)n.Width, TestContext.Current.CancellationToken));
    });

    /// <summary>EDGE-REP-40 through the loader: an orientation-6 JPEG's natural size and pixels are upright.</summary>
    [Fact]
    public Task JpegExifOrientationIsApplied() => Sta.RunAsync(async () =>
    {
        using var temp = new TempDir();
        var dir = await ProjectWith(temp, ("shots/a.jpg", TestImages.WithOrientation(TestImages.Jpeg(64, 32), 6)));
        var image = await Loader().LoadAsync(dir, "shots/a.jpg", n => (int)n.Width, TestContext.Current.CancellationToken);
        Assert.NotNull(image);
        Assert.Equal(new ImageSize(32, 64), image.NaturalSize);
        Assert.Equal((32, 64), (image.Bitmap.PixelWidth, image.Bitmap.PixelHeight));
        Assert.True(image.Bitmap.IsFrozen);
    });

    [Fact]
    public Task ACanceledLoadThrows() => Sta.RunAsync(async () =>
    {
        using var temp = new TempDir();
        var dir = await ProjectWith(temp, ("shots/a.png", TestImages.Png(4, 4)));
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => Loader().LoadAsync(dir, "shots/a.png", n => (int)n.Width, cts.Token));
    });

    /// <summary>7.11: the decode is the fit's width times the zoom in device pixels; zooming in decodes again, larger; zooming out does not.</summary>
    [Fact]
    public Task DecodeSizeTracksZoom() => Sta.RunAsync(async () =>
    {
        using var temp = new TempDir();
        var dir = await ProjectWith(temp, ("shots/a.png", TestImages.Png(2000, 1000)));
        var loader = Loader();
        var figure = Figure(dir, loader, "shots/a.png");
        var window = Host(new Border { Width = 800, Child = figure });
        window.Show();
        try
        {
            Assert.True(await TestShell.UntilAsync(() => figure.Shown is not null));
            var dpi = VisualTreeHelper.GetDpi(figure).DpiScaleX;
            var natural = new ImageSize(2000, 1000);
            Assert.Equal(ReportFigure.DecodeWidth(natural, 800, 1, 1, dpi), figure.Shown!.DecodedWidth);
            Assert.Equal(1, loader.DecodeCount);

            figure.Zoom = 2;
            Assert.True(await TestShell.UntilAsync(() => loader.DecodeCount == 2 && figure.Shown!.DecodedWidth > ReportFigure.DecodeWidth(natural, 800, 1, 1, dpi)));
            Assert.Equal(ReportFigure.DecodeWidth(natural, 800, 2, 1, dpi), figure.Shown!.DecodedWidth);

            figure.Zoom = 1;
            await TestShell.Settle();
            await Task.Delay(200, TestContext.Current.CancellationToken);
            await TestShell.Settle();
            Assert.Equal(2, loader.DecodeCount);
        }
        finally
        {
            window.Close();
        }
    });

    /// <summary>INV-REP-17: zoom and pan never reload an image already decoded at its full size; a new render revision does.</summary>
    [Fact]
    public Task ReloadsOnRenderRevOnly() => Sta.RunAsync(async () =>
    {
        using var temp = new TempDir();
        var dir = await ProjectWith(temp, ("shots/a.png", TestImages.Png(300, 200)));
        var loader = Loader();
        var figure = Figure(dir, loader, "shots/a.png", renderRev: 1);
        var window = Host(new Border { Width = 800, Child = figure });
        window.Show();
        try
        {
            Assert.True(await TestShell.UntilAsync(() => figure.Shown is not null));
            Assert.Equal(300, figure.Shown!.DecodedWidth);
            figure.Zoom = 3;
            figure.PanX = 0;
            figure.PanY = 1;
            await TestShell.Settle();
            await Task.Delay(200, TestContext.Current.CancellationToken);
            await TestShell.Settle();
            Assert.Equal(1, loader.DecodeCount);

            figure.ImageKey = new ReportImageKey("shots/a.png", 2);
            Assert.True(await TestShell.UntilAsync(() => loader.DecodeCount == 2));
        }
        finally
        {
            window.Close();
        }
    });

    /// <summary>EDGE-REP-50: on a new key the image shown and its size stay until the new image is decoded, then swap together.</summary>
    [Fact]
    public Task ANewKeyKeepsTheOldImageUntilTheNewOneLoads() => Sta.RunAsync(async () =>
    {
        using var temp = new TempDir();
        var dir = await ProjectWith(temp, ("shots/a.png", TestImages.Png(400, 200)), ("shots/b.png", TestImages.Png(300, 300)));
        var figure = Figure(dir, Loader(), "shots/a.png");
        var window = Host(new Border { Width = 800, Child = figure });
        window.Show();
        try
        {
            Assert.True(await TestShell.UntilAsync(() => figure.Shown is not null));
            var first = figure.Shown;
            figure.ImageKey = new ReportImageKey("shots/b.png", 0);
            Assert.Same(first, figure.Shown);
            Assert.False(figure.IsLoadingPlaceholder);
            Assert.True(await TestShell.UntilAsync(() => !ReferenceEquals(first, figure.Shown)));
            Assert.Equal(new ImageSize(300, 300), figure.Shown!.NaturalSize);
            Assert.Equal(new ReportFit(300, 300, 302, 302), figure.Fit);
        }
        finally
        {
            window.Close();
        }
    });

    /// <summary>D-REP-18: a figure whose image cannot be shown says so, with the path.</summary>
    [Fact]
    public Task AMissingImageShowsItsPath() => Sta.RunAsync(async () =>
    {
        using var temp = new TempDir();
        var dir = await ProjectWith(temp);
        var figure = Figure(dir, Loader(), "shots/none.png");
        var window = Host(new Border { Width = 800, Child = figure });
        window.Show();
        try
        {
            Assert.True(await TestShell.UntilAsync(() => figure.IsMissing));
            var text = Assert.Single(VisualTree.Descendants<TextBlock>(figure), t => t.Name == "PART_Missing");
            Assert.Equal("Image missing: shots/none.png", text.Text);
            Assert.Equal(Visibility.Visible, text.Visibility);
            // The host stretches the figure; a card's stack gives it the height it asks for.
            Assert.Equal(ReportFigure.PlaceholderHeight, figure.DesiredSize.Height, 3);
            Assert.Equal(ReportFigure.PlaceholderHeight, VisualTree.Named<Border>(figure, "PART_Placeholder").ActualHeight, 3);
        }
        finally
        {
            window.Close();
        }
    });

    /// <summary>7.11: a figure far below the viewport loads only once it is scrolled within a viewport's height.</summary>
    [Fact]
    public Task LoadsNearTheViewportOnly() => Sta.RunAsync(async () =>
    {
        using var temp = new TempDir();
        var dir = await ProjectWith(temp, ("shots/a.png", TestImages.Png(40, 30)));
        var loader = Loader();
        var figure = Figure(dir, loader, "shots/a.png");
        var column = new StackPanel { Width = 800 };
        column.Children.Add(new Border { Height = 3000 });
        column.Children.Add(figure);
        var scroller = new ScrollViewer { Content = column, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        var window = Host(scroller, height: 500);
        window.Show();
        try
        {
            await TestShell.Settle();
            await Task.Delay(300, TestContext.Current.CancellationToken);
            await TestShell.Settle();
            Assert.Equal(0, loader.DecodeCount);
            Assert.True(figure.IsLoadingPlaceholder);

            scroller.ScrollToEnd();
            Assert.True(await TestShell.UntilAsync(() => figure.Shown is not null));
            Assert.Equal(1, loader.DecodeCount);
        }
        finally
        {
            window.Close();
        }
    });

    /// <summary>INV-REP-16: the ring sits on the click in image space, so it pans and scales with the image; outside the image it is not drawn.</summary>
    [Fact]
    public Task TheRingSitsOnTheClick() => Sta.RunAsync(async () =>
    {
        using var temp = new TempDir();
        var dir = await ProjectWith(temp, ("shots/a.png", TestImages.Png(400, 200)));
        var style = new MarkerStyle(new Rgba(0x25, 0x63, 0xEB, 0xFF), new Rgba(0x25, 0x63, 0xEB, 0x2E));
        var figure = Figure(dir, Loader(), "shots/a.png");
        figure.Marker = new ReportMarker(100, 50, style);
        var window = Host(new Border { Width = 800, Child = figure });
        window.Show();
        try
        {
            Assert.True(await TestShell.UntilAsync(() => figure.Shown is not null));
            Assert.Equal(new ReportFit(400, 200, 402, 202), figure.Fit);
            Assert.Equal(new Point(100, 50), figure.RingCentre);
            var ring = Assert.Single(VisualTree.Descendants<System.Windows.Shapes.Ellipse>(figure), e => e.Name == "PART_Ring");
            Assert.Equal(100 - ReportFigure.RingSize / 2, Canvas.GetLeft(ring), 6);
            Assert.Equal(Color.FromArgb(0xFF, 0x25, 0x63, 0xEB), ((SolidColorBrush)ring.Stroke).Color);
            Assert.Equal(Color.FromArgb(0x2E, 0x25, 0x63, 0xEB), ((SolidColorBrush)ring.Fill).Color);

            // Zoom 2, centred: the image is 800 by 400 in a 400 by 200 box, offset by half its range.
            figure.Zoom = 2;
            await TestShell.Settle();
            Assert.Equal(new Point(-200, -100), figure.PanOffset);
            Assert.Equal(new Point(0, 0), figure.RingCentre);

            figure.Marker = new ReportMarker(401, 50, style);
            await TestShell.Settle();
            Assert.Null(figure.RingCentre);
            Assert.Equal(Visibility.Collapsed, ring.Visibility);

            figure.Marker = null;
            await TestShell.Settle();
            Assert.Null(figure.RingCentre);
        }
        finally
        {
            window.Close();
        }
    });

    /// <summary>7.10: the stored pan is a fraction of the image beyond its box; at zoom 1 there is none.</summary>
    [Theory]
    [InlineData(1, 0.5, 0.5, 0, 0)]
    [InlineData(2, 0, 0, 0, 0)]
    [InlineData(2, 1, 1, -400, -200)]
    [InlineData(2, 0.5, 0.25, -200, -50)]
    [InlineData(3, 0.5, 0.5, -400, -200)]
    public Task PanOffsetFollowsTheStoredPan(double zoom, double panX, double panY, double x, double y) => Sta.RunAsync(async () =>
    {
        using var temp = new TempDir();
        var dir = await ProjectWith(temp, ("shots/a.png", TestImages.Png(400, 200)));
        var figure = Figure(dir, Loader(), "shots/a.png");
        figure.Zoom = zoom;
        figure.PanX = panX;
        figure.PanY = panY;
        var window = Host(new Border { Width = 800, Child = figure });
        window.Show();
        try
        {
            Assert.True(await TestShell.UntilAsync(() => figure.Shown is not null));
            Assert.Equal(new Point(x, y), figure.PanOffset);
            var box = Assert.Single(VisualTree.Descendants<Border>(figure), b => b.Name == "PART_Wrap");
            Assert.Equal((402.0, 202.0), (box.ActualWidth, box.ActualHeight));
        }
        finally
        {
            window.Close();
        }
    });

    [Fact]
    public async Task ArgumentsAreChecked()
    {
        var loader = Loader();
        var ct = TestContext.Current.CancellationToken;
        Assert.Throws<ArgumentNullException>(() => new ReportImageLoader(null!, NullLogger<ReportImageLoader>.Instance));
        Assert.Throws<ArgumentNullException>(() => new ReportImageLoader(new ReportImageDecoder(), null!));
        await Assert.ThrowsAsync<ArgumentNullException>(() => loader.LoadAsync(null!, "a.png", n => 1, ct));
        await Assert.ThrowsAsync<ArgumentNullException>(() => loader.LoadAsync("C:\\p", null!, n => 1, ct));
        await Assert.ThrowsAsync<ArgumentNullException>(() => loader.LoadAsync("C:\\p", "a.png", null!, ct));
        Assert.InRange(ReportImageLoader.Concurrency, 1, 4);
    }
}
