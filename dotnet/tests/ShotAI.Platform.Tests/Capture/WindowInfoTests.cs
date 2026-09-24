using System.Windows;
using System.Windows.Interop;
using ShotAI.Core.Capture;
using ShotAI.Core.Model;
using ShotAI.Platform.Capture;
using ShotAI.Platform.Tests.Support;
using Xunit;

namespace ShotAI.Platform.Tests.Capture;

/// <summary>
/// Spec 02 7.5 and risk R4 on the real system: the Win32 answers get-windows' rules run over
/// (Core's <c>WindowDescriberTests</c> has the rules), the list filter on windows of every kind,
/// and the foreground window. The windows are top-level and visible, so the class runs alone.
/// </summary>
[Collection(ScreenPixelsCollection.Name)]
public sealed class WindowInfoTests
{
    private const uint SwpNoMove = 0x0002;
    private const uint SwpNoSize = 0x0001;
    private const uint SwpNoActivate = 0x0010;

    private static readonly TimeSpan Bound = TimeSpan.FromSeconds(10);

    private readonly Win32WindowFacts _facts = new();
    private readonly Win32WindowInfoProvider _provider = new();

    /// <summary>R4: the name captions and SOP prompts show for a click on the desktop or in a folder.</summary>
    [Fact]
    public void ExplorerFileDescriptionIsWindowsExplorer()
    {
        var explorer = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "explorer.exe");

        Assert.Equal("Windows Explorer", _facts.FileDescription(explorer));
    }

    /// <summary>A file without a version resource has no FileDescription, so the app is named by its file name (Core).</summary>
    [Fact]
    public void FallsBackToExeName()
    {
        using var temp = new TempDir();
        var path = temp.File("NoVersionInfo.exe", "MZ");

        Assert.Equal("", _facts.FileDescription(path));
        Assert.Equal("", _facts.FileDescription(temp.Combine("missing.exe")));
        Assert.Equal("NoVersionInfo.exe", WindowDescriber.FileName(path));
    }

    /// <summary>The image of this process, in Win32 form; a process that does not open gives null.</summary>
    [Fact]
    public void AProcessImageIsItsWin32Path()
    {
        Assert.Equal(Environment.ProcessPath, _facts.ImagePath(Environment.ProcessId), ignoreCase: true);
        Assert.Null(_facts.ImagePath(0));
    }

    [Fact]
    public void ForegroundOfOwnWindowHasOwnPid()
    {
        using var ui = new UiThread("window info foreground");
        var (window, hwnd) = ui.Invoke(() =>
        {
            var w = new Window { Title = "shotAI foreground test", Left = 200, Top = 200, Width = 400, Height = 300, Topmost = true };
            w.Show();
            w.Activate();
            return (w, new WindowInteropHelper(w).Handle);
        });
        try
        {
            if (User32.GetForegroundWindow() != hwnd)
            {
                var (left, top, right, bottom) = Dwm.FrameBounds(hwnd) ?? throw new InvalidOperationException("The window has no frame bounds.");
                SyntheticInput.Click((left + right) / 2, (top + bottom) / 2);
            }
            WaitFor(() => User32.GetForegroundWindow() == hwnd);

            var foreground = Assert.IsType<ForegroundInfo>(_provider.Foreground());
            Assert.Equal(hwnd, foreground.Hwnd);
            Assert.Equal(Environment.ProcessId, foreground.Pid);
            Assert.Equal("shotAI foreground test", foreground.Title);
            Assert.Equal(ExpectedOwnApp(), foreground.App);
            Assert.False(foreground.Minimized);
            Assert.NotNull(foreground.WindowRect);
            Assert.Equal(_facts.FrameBounds(hwnd), foreground.FrameBounds);
        }
        finally
        {
            ui.Invoke(window.Close);
        }
    }

    [Fact]
    public void ListFiltersToolWindowsAndCloaked()
    {
        using var normal = TestWindow.Visible("window info normal");
        using var tool = TestWindow.Visible("window info tool", exStyle: User32.WsExToolWindow);
        using var cloaked = TestWindow.Visible("window info cloaked");
        Dwm.CloakWindow(cloaked.Handle);

        var list = _provider.ListWindows();

        var listed = Assert.Single(list, w => w.Id == Id(normal));
        Assert.Equal(Environment.ProcessId, listed.Pid);
        Assert.Equal("window info normal", listed.Title);
        Assert.Equal(ExpectedOwnApp(), listed.App);
        Assert.DoesNotContain(list, w => w.Id == Id(tool));
        Assert.DoesNotContain(list, w => w.Id == Id(cloaked));
        Assert.True(_facts.ListFacts(cloaked.Handle).Cloaked);
        Assert.False(_facts.ListFacts(normal.Handle).Cloaked);
        Assert.Equal(WindowListFilter.WsExToolWindow, _facts.ListFacts(tool.Handle).ExStyle & WindowListFilter.WsExToolWindow);
    }

    /// <summary>
    /// A disabled window is dropped (as one behind a modal dialog would be), as is one with
    /// neither a caption nor the popup style; a popup is kept, and an owned window only as an
    /// app window.
    /// </summary>
    [Fact]
    public void ListDropsDisabledAndCaptionlessWindows()
    {
        using var disabled = TestWindow.Visible("window info disabled");
        User32.EnableWindow(disabled.Handle, false);
        using var captionless = TestWindow.Visible("window info captionless", style: User32.WsPopup);
        User32.SetWindowLong(captionless.Handle, User32.GwlStyle, User32.WsVisible);
        using var popup = TestWindow.Visible("window info popup", style: User32.WsPopup);
        using var owner = TestWindow.Visible("window info owner");
        using var owned = TestWindow.Visible("window info owned", owner: owner.Handle);
        using var ownedApp = TestWindow.Visible("window info owned app", exStyle: User32.WsExAppWindow, owner: owner.Handle);

        var ids = _provider.ListWindows().Select(w => w.Id).ToHashSet();

        Assert.DoesNotContain(Id(disabled), ids);
        Assert.DoesNotContain(Id(captionless), ids);
        Assert.Contains(Id(popup), ids);
        Assert.Contains(Id(owner), ids);
        Assert.DoesNotContain(Id(owned), ids);
        Assert.Contains(Id(ownedApp), ids);
        Assert.False(_facts.ListFacts(disabled.Handle).Enabled);
        Assert.True(_facts.ListFacts(owned.Handle).Owned);
        Assert.Equal(0u, _facts.ListFacts(captionless.Handle).Style & (WindowListFilter.WsCaption | WindowListFilter.WsPopup));
    }

    [Fact]
    public void TheListIsTopFirst()
    {
        using var a = TestWindow.Visible("window info a");
        using var b = TestWindow.Visible("window info b");

        User32.SetWindowPos(a.Handle, 0, 0, 0, 0, 0, SwpNoMove | SwpNoSize | SwpNoActivate);
        Assert.True(IndexOf(a) < IndexOf(b));
        User32.SetWindowPos(b.Handle, 0, 0, 0, 0, 0, SwpNoMove | SwpNoSize | SwpNoActivate);
        Assert.True(IndexOf(b) < IndexOf(a));
    }

    /// <summary>A minimized window stays listed, marked minimized; the chooser leaves it out (Core, 2.10.2).</summary>
    [Fact]
    public void AMinimizedWindowIsListedMinimized()
    {
        using var window = TestWindow.Visible("window info minimized");
        User32.ShowWindow(window.Handle, User32.SwMinimize);

        Assert.True(Assert.Single(_provider.ListWindows(), w => w.Id == Id(window)).Minimized);
    }

    /// <summary>A listed window carries its visible frame, which excludes the invisible resize border.</summary>
    [Fact]
    public void TheListCarriesTheFrameBounds()
    {
        using var window = TestWindow.Visible("window info frame");

        var listed = Assert.Single(_provider.ListWindows(), w => w.Id == Id(window));
        Assert.Equal(_facts.FrameBounds(window.Handle), listed.FrameBounds);
        Assert.True(listed.FrameBounds.Width < _facts.WindowRect(window.Handle)!.Value.Width);
    }

    /// <summary>2.10.3 on real windows: by id, then by process and exact title, then by process.</summary>
    [Fact]
    public void ResolveByIdThenPidTitleThenPid()
    {
        using var alpha = TestWindow.Visible("window info alpha 7f3a");
        using var beta = TestWindow.Visible("window info beta 7f3a");
        var pid = Environment.ProcessId;

        Assert.Equal(Id(beta), _provider.Resolve(new CaptureTargetWindow(Id(beta), -1, "other"))!.Id);
        Assert.Equal(Id(beta), _provider.Resolve(new CaptureTargetWindow(0, pid, "window info beta 7f3a"))!.Id);
        Assert.Equal(pid, _provider.Resolve(new CaptureTargetWindow(0, pid, "window info gone 7f3a"))!.Pid);
        Assert.Null(_provider.Resolve(new CaptureTargetWindow(0, -1, "window info beta 7f3a")));
    }

    /// <summary>
    /// ARCHITECTURE DL2: the title of a window of this process is read without a message, so a
    /// capture thread never waits for the UI thread, even while it is blocked.
    /// </summary>
    [Fact]
    public async Task AnOwnWindowsTitleIsReadWhileItsThreadIsBlocked()
    {
        using var ui = new UiThread("window info blocked");
        var (window, hwnd) = ui.Invoke(() =>
        {
            var w = new Window { Title = "shotAI blocked title", Left = 40, Top = 40, Width = 300, Height = 200, ShowActivated = false };
            w.Show();
            return (w, new WindowInteropHelper(w).Handle);
        });
        try
        {
            using (ui.Block())
            {
                Assert.Equal("shotAI blocked title", await Task.Run(() => _facts.Title(hwnd)).WaitAsync(Bound, TestContext.Current.CancellationToken));
                var listed = await Task.Run(_provider.ListWindows).WaitAsync(Bound, TestContext.Current.CancellationToken);
                Assert.Equal("shotAI blocked title", Assert.Single(listed, w => w.Id == unchecked((uint)hwnd)).Title);
            }
        }
        finally
        {
            ui.Invoke(window.Close);
        }
    }

    private static uint Id(TestWindow window) => unchecked((uint)window.Handle);

    private int IndexOf(TestWindow window) => _provider.ListWindows().ToList().FindIndex(w => w.Id == Id(window));

    // The test host's own name, by the rule of 2.10.1.
    private string ExpectedOwnApp()
    {
        var path = Environment.ProcessPath!;
        var description = _facts.FileDescription(path);
        return description.Length > 0 ? description : Path.GetFileName(path);
    }

    private static void WaitFor(Func<bool> condition)
    {
        var deadline = DateTime.UtcNow + Bound;
        while (!condition())
        {
            if (DateTime.UtcNow > deadline) throw new TimeoutException("The window did not come to the foreground.");
            Thread.Sleep(20);
        }
    }
}
