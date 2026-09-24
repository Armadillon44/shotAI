using ShotAI.Core.Capture;
using ShotAI.Core.Model;
using Xunit;

namespace ShotAI.Core.Tests.Capture;

/// <summary>
/// get-windows 9.3.0's window information (spec 02 2.10, 7.5, EDGE-CAP-53, risk R4) over
/// scripted Win32 facts; the facts themselves are Platform's <c>WindowInfoTests</c>.
/// </summary>
public sealed class WindowDescriberTests
{
    private const string Explorer = @"C:\Windows\explorer.exe";
    private const string FrameHostPath = @"C:\Windows\System32\ApplicationFrameHost.exe";
    private const string SettingsPath = @"C:\Windows\ImmersiveControlPanel\SystemSettings.exe";

    private readonly FakeWindowFacts _facts = new();

    [Fact]
    public void TheAppIsTheFileDescription()
    {
        _facts.Process(10, Explorer, "Windows Explorer");
        _facts.Add(1, pid: 10, title: "Downloads");
        _facts.ForegroundWindow = 1;

        var w = Assert.IsType<ForegroundInfo>(Describer().Foreground());
        Assert.Equal("Windows Explorer", w.App);
        Assert.Equal("Downloads", w.Title);
        Assert.Equal(10, w.Pid);
    }

    /// <summary>An executable without a FileDescription is named by its file name, extension included.</summary>
    [Fact]
    public void FallsBackToExeName()
    {
        _facts.Process(10, @"C:\Tools\SearchHost.exe");
        _facts.Add(1, pid: 10);
        _facts.Descriptions[@"C:\Tools\Other.exe"] = "Other";

        Assert.Equal("SearchHost.exe", Describer().Describe(1)!.App);
    }

    [Fact]
    public void AnEmptyFileDescriptionFallsBackToo()
    {
        _facts.Process(10, @"C:\Tools\tool.exe", "");
        _facts.Add(1, pid: 10);

        Assert.Equal("tool.exe", Describer().Describe(1)!.App);
    }

    /// <summary>get-windows' getFileName splits on the backslash only, and gives nothing without one.</summary>
    [Fact]
    public void TheFileNameIsWhatFollowsTheLastBackslash()
    {
        Assert.Equal("b.exe", WindowDescriber.FileName(@"C:\a\b.exe"));
        Assert.Equal("b.exe", WindowDescriber.FileName(@"\\?\C:\a\b.exe"));
        Assert.Equal("a/b.exe", WindowDescriber.FileName(@"C:\a/b.exe"));
        Assert.Equal("", WindowDescriber.FileName("b.exe"));
        Assert.Equal("", WindowDescriber.FileName(@"C:\a\"));
        Assert.Equal("", WindowDescriber.FileName(""));
        Assert.Throws<ArgumentNullException>(() => WindowDescriber.FileName(null!));
    }

    /// <summary>A process that opens but whose image cannot be read names nothing, and the window still describes.</summary>
    [Fact]
    public void AFailedImageQueryNamesNothing()
    {
        _facts.Process(10, "");
        _facts.Add(1, pid: 10);

        var w = Describer().Describe(1)!;
        Assert.Equal("", w.App);
        Assert.DoesNotContain("", _facts.DescriptionReads);
    }

    [Fact]
    public void AProcessThatDoesNotOpenGivesNull()
    {
        _facts.Process(10, null);
        _facts.Add(1, pid: 10);
        _facts.ForegroundWindow = 1;

        Assert.Null(Describer().Foreground());
        Assert.Null(Describer().Describe(1));
    }

    [Fact]
    public void NoForegroundWindowGivesNull()
    {
        _facts.ForegroundWindow = 0;
        Assert.Null(Describer().Foreground());
        Assert.Empty(_facts.ImageQueries);
    }

    /// <summary>The whole result is null when either rectangle cannot be read (main.cc:184-192).</summary>
    [Theory]
    [InlineData(false, true)]
    [InlineData(true, false)]
    public void AFailedRectangleGivesNull(bool windowRect, bool clientRect)
    {
        _facts.Process(10, Explorer, "Windows Explorer");
        var window = _facts.Add(1, pid: 10);
        if (!windowRect) window.WindowRect = null;
        window.HasClientRect = clientRect;

        Assert.Null(Describer().Describe(1));
    }

    [Fact]
    public void TheForegroundCarriesEveryField()
    {
        _facts.Process(10, Explorer, "Windows Explorer");
        var window = _facts.Add(7, pid: 10, title: "  Docs  ");
        window.WindowRect = new Rect(-7, 0, 1934, 1047);
        window.Frame = new Rect(0, 0, 1920, 1040);
        window.Minimized = true;
        _facts.ForegroundWindow = 7;

        Assert.Equal(new ForegroundInfo(7, 10, "Windows Explorer", "  Docs  ", new Rect(-7, 0, 1934, 1047), new Rect(0, 0, 1920, 1040), true), Describer().Foreground());
    }

    [Fact]
    public void AWindowWithoutAFrameKeepsItsWindowRect()
    {
        _facts.Process(10, Explorer, "Windows Explorer");
        var window = _facts.Add(1, pid: 10);
        window.Frame = null;

        var w = Describer().Describe(1)!;
        Assert.Null(w.FrameBounds);
        Assert.Equal(window.WindowRect, w.WindowRect);
    }

    [Fact]
    public void WidgetsIsDropped()
    {
        _facts.Process(10, @"C:\Program Files\WindowsApps\MicrosoftWindows.Client.WebExperience\Dashboard\Widgets.exe");
        _facts.Add(1, pid: 10);
        _facts.Process(11, @"C:\Tools\widgets.exe");
        _facts.Add(2, pid: 11);
        _facts.Process(12, @"C:\Tools\Board.exe", "Widgets.exe");
        _facts.Add(3, pid: 12);

        var describer = Describer();
        Assert.Null(describer.Describe(1));
        Assert.Equal("widgets.exe", describer.Describe(2)!.App); // ordinal
        Assert.Null(describer.Describe(3)); // by the name, whatever the file
    }

    /// <summary>
    /// EDGE-CAP-53: a frame host window names the hosted app, the first descendant from another
    /// image, and keeps the frame host's process id; the walk stops there.
    /// </summary>
    [Fact]
    public void UwpAppKeepsFrameHostPid()
    {
        _facts.Process(20, FrameHostPath, "Application Frame Host");
        var host = _facts.Add(1, pid: 20, title: "Settings");
        _facts.Windows[11] = new FakeWindow { Pid = 20 };
        _facts.Windows[12] = new FakeWindow { Pid = 30 };
        _facts.Process(30, null);
        _facts.Windows[13] = new FakeWindow { Pid = 40 };
        _facts.Process(40, SettingsPath, "Settings");
        _facts.Windows[14] = new FakeWindow { Pid = 50 };
        _facts.Process(50, @"C:\Tools\Other.exe", "Other");
        host.Children.AddRange([11, 12, 13, 14]);

        var w = Describer().Describe(1)!;
        Assert.Equal("Settings", w.App);
        Assert.Equal(20, w.Pid);
        Assert.Equal("Settings", w.Title);
        Assert.DoesNotContain(50, _facts.ImageQueries);
    }

    /// <summary>The first descendant from another image stops the walk even when its name is empty, and then the frame host keeps its own.</summary>
    [Fact]
    public void AHostedChildWithNoNameKeepsTheFrameHost()
    {
        _facts.Process(20, FrameHostPath, "Application Frame Host");
        var host = _facts.Add(1, pid: 20);
        _facts.Windows[11] = new FakeWindow { Pid = 30 };
        _facts.Process(30, "");
        _facts.Windows[12] = new FakeWindow { Pid = 40 };
        _facts.Process(40, SettingsPath, "Settings");
        host.Children.AddRange([11, 12]);

        Assert.Equal("Application Frame Host", Describer().Describe(1)!.App);
        Assert.DoesNotContain(40, _facts.ImageQueries);
    }

    [Fact]
    public void AFrameHostWithOnlyItsOwnDescendantsKeepsItsName()
    {
        _facts.Process(20, FrameHostPath, "Application Frame Host");
        var host = _facts.Add(1, pid: 20);
        _facts.Windows[11] = new FakeWindow { Pid = 20 };
        _facts.Windows[12] = new FakeWindow { Pid = 21 };
        _facts.Process(21, FrameHostPath);
        host.Children.AddRange([11, 12]);

        Assert.Equal("Application Frame Host", Describer().Describe(1)!.App);
    }

    /// <summary>The frame host is recognized by its exact file name, and no other window's descendants are walked.</summary>
    [Fact]
    public void OnlyTheFrameHostIsWalked()
    {
        _facts.Process(20, @"C:\Windows\System32\applicationframehost.exe", "Application Frame Host");
        var host = _facts.Add(1, pid: 20);
        _facts.Windows[11] = new FakeWindow { Pid = 40 };
        _facts.Process(40, SettingsPath, "Settings");
        host.Children.Add(11);
        _facts.Process(10, Explorer, "Windows Explorer");
        _facts.Add(2, pid: 10).Children.Add(11);

        var describer = Describer();
        Assert.Equal("Application Frame Host", describer.Describe(1)!.App);
        Assert.Equal("Windows Explorer", describer.Describe(2)!.App);
        Assert.Equal(0, _facts.DescendantQueries);
    }

    /// <summary>A hosted app named Widgets.exe is dropped too: the check follows the walk (main.cc:171).</summary>
    [Fact]
    public void AHostedWidgetsIsDropped()
    {
        _facts.Process(20, FrameHostPath, "Application Frame Host");
        var host = _facts.Add(1, pid: 20);
        _facts.Windows[11] = new FakeWindow { Pid = 40 };
        _facts.Process(40, @"C:\Apps\Widgets.exe");
        host.Children.Add(11);

        Assert.Null(Describer().Describe(1));
    }

    /// <summary>The version resource is read once per image for the process lifetime.</summary>
    [Fact]
    public void TheAppNameIsReadOncePerPath()
    {
        _facts.Process(10, Explorer, "Windows Explorer");
        _facts.Process(11, Explorer);
        _facts.Process(12, @"C:\Tools\tool.exe");
        _facts.Add(1, pid: 10);
        _facts.Add(2, pid: 11);
        _facts.Add(3, pid: 12);

        var describer = Describer();
        foreach (var hwnd in new nint[] { 1, 2, 3, 1, 3 }) describer.Describe(hwnd);

        Assert.Equal([Explorer, @"C:\Tools\tool.exe"], _facts.DescriptionReads);
        Assert.Equal("Windows Explorer", describer.Describe(2)!.App);
    }

    /// <summary>A lone surrogate becomes U+FFFD, as get-windows' UTF-8 conversion leaves it.</summary>
    [Fact]
    public void StringsAreMadeWellFormed()
    {
        _facts.Process(10, "C:\\Tools\\a\\\ud800.exe");
        _facts.Add(1, pid: 10, title: "x\udc00y");
        _facts.Process(11, @"C:\Tools\b.exe", "Tool \ud83d\ude00 \ud83d");
        _facts.Add(2, pid: 11);

        var describer = Describer();
        var first = describer.Describe(1)!;
        Assert.Equal("\ufffd.exe", first.App);
        Assert.Equal("x\ufffdy", first.Title);
        Assert.Equal("Tool \ud83d\ude00 \ufffd", describer.Describe(2)!.App);
    }

    [Fact]
    public void TheListKeepsWhatTheFilterKeepsInZOrder()
    {
        _facts.Process(10, Explorer, "Windows Explorer");
        _facts.Process(11, null);
        _facts.Add(5, pid: 10, title: "Top");
        _facts.Add(6, pid: 10, title: "Tool").List = new(true, true, true, WindowListFilter.WsCaption, WindowListFilter.WsExToolWindow, false, false);
        _facts.Add(7, pid: 11, title: "Closed process");
        var framed = _facts.Add(8, pid: 10, title: "Bottom");
        framed.Frame = null;
        framed.Minimized = true;
        _facts.ForegroundWindow = 8;

        var list = Describer().ListWindows();

        Assert.Equal(
            [
                new ListedWindow(5, 10, "Top", "Windows Explorer", new Rect(17, 20, 286, 193), false, false),
                new ListedWindow(8, 10, "Bottom", "Windows Explorer", new Rect(10, 20, 300, 200), true, true),
            ],
            list);
    }

    /// <summary>A window the filter drops is never described, so its process is never opened.</summary>
    [Fact]
    public void AFilteredWindowIsNotDescribed()
    {
        _facts.Process(10, Explorer, "Windows Explorer");
        _facts.Add(5, pid: 12).List = default;

        Assert.Empty(Describer().ListWindows());
        Assert.Empty(_facts.ImageQueries);
    }

    /// <summary>The list id is the handle's low 32 bits (7.5).</summary>
    [Fact]
    public void TheListIdIsTheHandlesLow32Bits()
    {
        _facts.Process(10, Explorer, "Windows Explorer");
        _facts.Add(unchecked((nint)0x1_0000_0ABCL), pid: 10);

        Assert.Equal(0x0ABCu, Assert.Single(Describer().ListWindows()).Id);
    }

    /// <summary>2.10.3: by id, then by process and the stored title exactly, then by process alone.</summary>
    [Fact]
    public void ResolveByIdThenPidTitleThenPid()
    {
        ListedWindow Window(uint id, int pid, string title) => new(id, pid, title, "App", new Rect(0, 0, 10, 10), false, false);
        var a = Window(1, 10, "Report");
        var b = Window(2, 10, " Doc ");
        var c = Window(3, 10, "Doc");
        var d = Window(4, 20, "Doc");
        IReadOnlyList<ListedWindow> list = [a, b, c, d];

        Assert.Same(d, WindowDescriber.Resolve(list, new CaptureTargetWindow(4, 10, "Report")));
        Assert.Same(c, WindowDescriber.Resolve(list, new CaptureTargetWindow(99, 10, "Doc")));
        Assert.Same(a, WindowDescriber.Resolve(list, new CaptureTargetWindow(99, 10, "Gone")));
        Assert.Same(d, WindowDescriber.Resolve(list, new CaptureTargetWindow(99, 20, "Doc")));
        Assert.Null(WindowDescriber.Resolve(list, new CaptureTargetWindow(99, 30, "Doc")));
        Assert.Null(WindowDescriber.Resolve([], new CaptureTargetWindow(1, 10, "Report")));
        // The listed title is untrimmed and the stored one trimmed, so " Doc " is not "Doc".
        Assert.Same(a, WindowDescriber.Resolve([a, b], new CaptureTargetWindow(99, 10, "Doc")));
    }

    /// <summary>The ids of a stored target are JSON numbers: a fractional one matches nothing by id.</summary>
    [Fact]
    public void AFractionalIdOrPidMatchesNoWindow()
    {
        var a = new ListedWindow(1, 10, "Doc", "App", new Rect(0, 0, 10, 10), false, false);

        Assert.Null(WindowDescriber.Resolve([a], new CaptureTargetWindow(1.5, 10.5, "Doc")));
        Assert.Same(a, WindowDescriber.Resolve([a], new CaptureTargetWindow(1.5, 10, "Doc")));
    }

    [Fact]
    public void ResolveReadsAFreshList()
    {
        _facts.Process(10, Explorer, "Windows Explorer");
        _facts.Add(1, pid: 10, title: "Doc");
        var describer = Describer();
        var target = new CaptureTargetWindow(2, 10, "Doc");

        Assert.Equal(1u, describer.Resolve(target)!.Id);
        _facts.Add(2, pid: 10, title: "Doc");
        Assert.Equal(2u, describer.Resolve(target)!.Id);
        _facts.ZOrder.Clear();
        Assert.Null(describer.Resolve(target));
    }

    [Fact]
    public void ArgumentsAreChecked()
    {
        Assert.Throws<ArgumentNullException>(() => new WindowDescriber(null!));
        Assert.Throws<ArgumentNullException>(() => WindowDescriber.Resolve(null!, new CaptureTargetWindow(1, 1, "")));
        Assert.Throws<ArgumentNullException>(() => WindowDescriber.Resolve([], null!));
    }

    private WindowDescriber Describer() => new(_facts);
}
