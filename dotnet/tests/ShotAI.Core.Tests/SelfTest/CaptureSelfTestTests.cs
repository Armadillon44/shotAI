using Microsoft.Extensions.Logging;
using ShotAI.Core.Capture;
using ShotAI.Core.Model;
using ShotAI.Core.Paths;
using ShotAI.Core.SelfTest;
using ShotAI.Core.Tests.Capture;
using ShotAI.Core.Tests.Support;
using Xunit;

namespace ShotAI.Core.Tests.SelfTest;

/// <summary>
/// Spec 02 2.16, 10 7.8 and EDGE-CAP-34: the capture self-test's lines, its verdict and its
/// clean-up, driven through the real engine over the harness's seams. No Electron test exists;
/// <c>capture-selftest.ts</c> is the reference.
/// </summary>
public sealed class CaptureSelfTestTests : IAsyncDisposable
{
    private static readonly Rect NotepadFrame = new(300, 150, 800, 600);

    private readonly TempDir _temp = new();
    private readonly TestAppPaths _paths;
    private readonly StringWriter _out = new();
    private readonly StringWriter _err = new();
    private readonly CapturingLoggerProvider _logs = new();
    private readonly EngineHarness _h = new();
    private int _disposed;

    public CaptureSelfTestTests()
    {
        _paths = new TestAppPaths(_temp.Root);
        Directory.CreateDirectory(_paths.TempDirectory);
        _h.Codec.RealIhdr = true;
        _h.Windows.Current = FakeWindows.App("Notepad", "notes.txt - Notepad", NotepadFrame);
        _h.Windows.Listed.Add(new ListedWindow(7, 200, "notes.txt - Notepad", "Notepad", NotepadFrame, false, true));
    }

    public async ValueTask DisposeAsync()
    {
        await _h.DisposeAsync();
        _out.Dispose();
        _err.Dispose();
        _logs.Dispose();
        _temp.Dispose();
    }

    private string Pid => Environment.ProcessId.ToString(System.Globalization.CultureInfo.InvariantCulture);

    private string[] Out => _out.ToString().Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries);

    private string[] Err => _err.ToString().Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries);

    /// <summary>Electron's lines, with the seams' names in place of the native modules', and a PASS.</summary>
    [Fact]
    public async Task PassesOnHealthySeams()
    {
        Assert.Equal(SelfTestOutcome.Pass, await RunAsync());

        Assert.Empty(Err);
        Assert.StartsWith("[capture-test] runtime win32/", Out[0], StringComparison.Ordinal);
        Assert.Contains(" \u00b7 .NET ", Out[0], StringComparison.Ordinal);
        Assert.Equal(
        [
            "[capture-test] window info     OK \u2014 active: Notepad :: notes.txt - Notepad",
            "[capture-test] screen capture  OK \u2014 1 monitor(s); PNG 33 bytes",
            "[capture-test] input hook      OK \u2014 loaded: true",
            "[capture-test] pipeline step    = shots/step-0001.png",
            "[capture-test] pipeline caption  = Capture: notes.txt - Notepad",
            "[capture-test] pipeline monitor  = 1920x1080 @1x",
            "[capture-test] manifest steps    = 1",
            "[capture-test] shot written       = true",
            "[capture-test] listTargets       = 1 windows, 1 monitors",
            "[capture-test] mode screen        = 1920x1080",
            "[capture-test] mode area          = 300x200",
            $"[capture-test] mode window        = {Ints(NotepadFrame.Width)}x{Ints(NotepadFrame.Height)}",
            Out[13],
            "[capture-test] mode menu(auto)    = 1100x800",
            "[capture-test] mode menu(screen)  = 1920x1080",
            "[capture-test] PASS",
        ], Out[1..]);
        Assert.StartsWith("[capture-test] mode menu(window)  = ", Out[13], StringComparison.Ordinal);
    }

    /// <summary>EDGE-CAP-34: a full monitor is expected at its stored size, 1632 x 918 for 1920 x 1080 at 0.85.</summary>
    [Fact]
    public async Task FullMonitorSizesAreCorrectedForTheDownscale()
    {
        _h.Settings.CaptureScale = 0.85;

        Assert.Equal(SelfTestOutcome.Pass, await RunAsync());
        Assert.Contains("[capture-test] mode screen        = 1632x918", Out);
        Assert.Contains("[capture-test] mode menu(screen)  = 1632x918", Out);
        Assert.Contains("[capture-test] mode area          = 300x200", Out);
    }

    /// <summary>Q-CAP-26: the expected size is the engine's own, the height following the width's ratio, with the 1100 px floor.</summary>
    [Theory]
    [InlineData(1920, 1080, 1632, 918)]
    [InlineData(2736, 1824, 2326, 1551)]
    [InlineData(2256, 1504, 1918, 1279)]
    [InlineData(1280, 800, 1100, 688)]
    [InlineData(1024, 768, 1024, 768)]
    [InlineData(300, 200, 300, 200)]
    public void TheExpectedSizeIsTheStoredOne(int width, int height, int storedWidth, int storedHeight) =>
        Assert.Equal((storedWidth, storedHeight), CaptureSelfTest.Stored(width, height, 0.85));

    /// <summary>Q-CAP-25: on a monitor no wider than the crop, the auto menu selection passes, where Electron's "smaller than the monitor" failed.</summary>
    [Fact]
    public async Task MenuAutoOnASmallMonitorPasses()
    {
        _h.Screen.Displays.Clear();
        _h.Screen.Displays.Add(FakeMonitorCapture.Monitor(1, 0, 0, 1024, 768, primary: true));
        _h.Windows.Current = FakeWindows.App("Notepad", "N", new Rect(100, 100, 600, 400));
        _h.Windows.Listed.Clear();

        Assert.Equal(SelfTestOutcome.Pass, await RunAsync());
        Assert.Contains("[capture-test] mode menu(auto)    = 1024x768", Out);
    }

    /// <summary>
    /// The expected auto menu crop is the owner joined with the click box, cropped to the monitor:
    /// 1100 x 800 at 100%, which the readability floor keeps whole at 0.85 (02 2.11).
    /// </summary>
    [Fact]
    public void MenuAutoExpectsTheOwnerAndTheClickBox()
    {
        var monitor = FakeMonitorCapture.Monitor(1, 0, 0, 2560, 1440, primary: true);
        Assert.Equal((1100, 800), CaptureSelfTest.MenuAutoStored(monitor, new Rect(200, 200, 900, 600), new Point(100, 100), 1));
        Assert.Equal((1100, 800), CaptureSelfTest.MenuAutoStored(monitor, new Rect(200, 200, 900, 600), new Point(100, 100), 0.85));
    }

    /// <summary>With no pickable window the window checks are skipped, which is no failure.</summary>
    [Fact]
    public async Task NoPickableWindowsSkipsTheWindowChecks()
    {
        _h.Windows.Listed.Clear();

        Assert.Equal(SelfTestOutcome.Pass, await RunAsync());
        Assert.Contains("[capture-test] mode window        = (no pickable windows \u2014 skipped)", Out);
        Assert.DoesNotContain(Out, l => l.StartsWith("[capture-test] mode menu(window)", StringComparison.Ordinal));
    }

    /// <summary>Without a monitor the modes fail on standard error, and so does the run.</summary>
    [Fact]
    public async Task NoMonitorFailsTheModes()
    {
        _h.Screen.Displays.Clear();

        Assert.Equal(SelfTestOutcome.Fail, await RunAsync());
        Assert.Contains("[capture-test] screen capture  OK \u2014 0 monitor(s); PNG 0 bytes", Out);
        Assert.Contains("[capture-test] modes: no monitor available", Err);
        Assert.Equal("[capture-test] FAIL", Out[^1]);
    }

    /// <summary>02 2.16: with shotAI in the foreground the pipeline's step is suppressed, and that is a failure.</summary>
    [Fact]
    public async Task ASuppressedStepFailsThePipeline()
    {
        _h.Windows.Current = FakeWindows.App("shotAI", "shotAI", NotepadFrame, pid: _h.Own.ProcessId);

        Assert.Equal(SelfTestOutcome.Fail, await RunAsync());
        Assert.Contains("[capture-test] pipeline step    = (none)", Out);
        Assert.Contains("[capture-test] pipeline caption  = undefined", Out);
        Assert.Contains("[capture-test] shot written       = false", Out);
    }

    /// <summary>A failing seam is reported on standard error, the other checks still run, and the run fails.</summary>
    [Fact]
    public async Task AFailingSeamIsReportedAndTheRestRun()
    {
        _h.Windows.FailForeground = true;

        Assert.Equal(SelfTestOutcome.Fail, await RunAsync());
        Assert.Contains("[capture-test] window info     FAILED: GetForegroundWindow failed", Err);
        Assert.Contains("[capture-test] input hook      OK \u2014 loaded: true", Out);
        Assert.Contains(Out, l => l.StartsWith("[capture-test] listTargets", StringComparison.Ordinal));
    }

    /// <summary>A failing seam alone fails the run: the first monitor's grab fails, the primary's does not.</summary>
    [Fact]
    public async Task AFailingSeamAloneFailsTheRun()
    {
        _h.Screen.Displays.Insert(0, FakeMonitorCapture.Monitor(2, -1920, 0, 1920, 1080));
        _h.Screen.Failing.Add(2);

        Assert.Equal(SelfTestOutcome.Fail, await RunAsync());
        Assert.Single(Err, l => l.StartsWith("[capture-test] screen capture  FAILED: ", StringComparison.Ordinal));
        Assert.Contains("[capture-test] shot written       = true", Out);
        Assert.Contains("[capture-test] mode menu(screen)  = 1920x1080", Out);
    }

    /// <summary>A failing window read alone fails the run: the engine's own read works.</summary>
    [Fact]
    public async Task AFailingWindowReadAloneFailsTheRun()
    {
        var failing = new FakeWindows { FailForeground = true };

        Assert.Equal(SelfTestOutcome.Fail, await CaptureSelfTest.RunAsync(_ => Services(windows: failing), _paths, _out, _err, Log));
        Assert.Equal(["[capture-test] window info     FAILED: GetForegroundWindow failed"], Err);
        Assert.Contains("[capture-test] shot written       = true", Out);
    }

    /// <summary>No monitor for the modes alone fails the run, before any mode runs.</summary>
    [Fact]
    public async Task NoMonitorForTheModesAloneFailsTheRun()
    {
        var screen = new MonitorsGoAway(_h.Screen);

        Assert.Equal(SelfTestOutcome.Fail, await CaptureSelfTest.RunAsync(_ => Services(screen: screen), _paths, _out, _err, Log));
        Assert.Equal(["[capture-test] modes: no monitor available"], Err);
        Assert.Contains("[capture-test] shot written       = true", Out);
        Assert.DoesNotContain(Out, l => l.StartsWith("[capture-test] listTargets", StringComparison.Ordinal));
    }

    /// <summary>Failing modes alone fail the run: listing the windows throws, and the rest of the modes do not run.</summary>
    [Fact]
    public async Task FailingModesAloneFailTheRun()
    {
        _h.Windows.FailListing = true;

        Assert.Equal(SelfTestOutcome.Fail, await RunAsync());
        Assert.Equal(["[capture-test] modes FAILED: EnumWindows failed"], Err);
        Assert.Contains("[capture-test] shot written       = true", Out);
        Assert.DoesNotContain(Out, l => l.StartsWith("[capture-test] mode ", StringComparison.Ordinal));
    }

    /// <summary>10 7.8 and 7.5.5: every line is logged at Information, but no window title or caption.</summary>
    [Fact]
    public async Task EveryLineIsLoggedWithoutTitlesOrCaptions()
    {
        await RunAsync();

        var logged = _logs.Entries.Select(e => e.Message).ToList();
        Assert.Equal(Out.Length + Err.Length, logged.Count);
        Assert.All(_logs.Entries, e => Assert.Equal(LogLevel.Information, e.Level));
        Assert.DoesNotContain(logged, l => l.Contains("notes.txt", StringComparison.Ordinal));
        Assert.Contains("[capture-test] window info     OK \u2014 active: Notepad :: (title of 19 characters)", logged);
        Assert.Contains("[capture-test] pipeline caption  = (caption of 28 characters)", logged);
    }

    /// <summary>A failure outside the checks is an ERROR line and the Error outcome, and the clean-up still runs.</summary>
    [Fact]
    public async Task AnErrorOutsideTheChecksIsError()
    {
        var outcome = await CaptureSelfTest.RunAsync(_ => throw new InvalidOperationException("no engine"), _paths, _out, _err, Log);

        Assert.Equal(SelfTestOutcome.Error, outcome);
        Assert.StartsWith("[capture-test] ERROR System.InvalidOperationException: no engine", Err[0], StringComparison.Ordinal);
        Assert.False(Directory.Exists(Path.Combine(_paths.TempDirectory, "shotai-capture-" + Pid)));
    }

    /// <summary>The run's folders and settings file are removed, and the services disposed within the bound.</summary>
    [Fact]
    public async Task RemovesItsFilesAndDisposesItsServices()
    {
        // As the isolated settings service leaves it once the store has moved its projects folder.
        await File.WriteAllTextAsync(Path.Combine(_paths.TempDirectory, "shotai-capture-" + Pid + ".settings.json"), "{}", TestContext.Current.CancellationToken);

        await RunAsync();

        Assert.False(Directory.Exists(Path.Combine(_paths.TempDirectory, "shotai-capture-" + Pid)));
        Assert.False(Directory.Exists(Path.Combine(_paths.TempDirectory, "shotai-modes-" + Pid)));
        Assert.False(File.Exists(Path.Combine(_paths.TempDirectory, "shotai-capture-" + Pid + ".settings.json")));
        Assert.Equal(1, _disposed);
        Assert.Equal(CaptureStatus.Idle, _h.Engine.GetState().Status);
    }

    /// <summary>The isolated paths point the settings file and the projects folder at the run's own.</summary>
    [Fact]
    public async Task TheServicesAreComposedOverIsolatedPaths()
    {
        IAppPaths? isolated = null;
        await CaptureSelfTest.RunAsync(p =>
        {
            isolated = p;
            return Services();
        }, _paths, _out, _err, Log);

        Assert.NotNull(isolated);
        Assert.Equal(Path.Combine(_paths.TempDirectory, "shotai-capture-" + Pid + ".settings.json"), isolated.SettingsFile);
        Assert.Equal(Path.Combine(_paths.TempDirectory, "shotai-capture-" + Pid), isolated.DefaultProjectsDir);
        Assert.Equal(_paths.TempDirectory, isolated.TempDirectory);
    }

    /// <summary>The PNG's size is read from its IHDR chunk, big-endian at bytes 16 and 20.</summary>
    [Fact]
    public void PngSizeReadsIhdr() => Assert.Equal((70_000, 3), CaptureSelfTest.PngSize(FakePng.WithIhdr(70_000, 3)));

    private ILogger Log => _logs.CreateLogger("ShotAI.App.SelfTestHost");

    private Task<SelfTestOutcome> RunAsync() => CaptureSelfTest.RunAsync(_ => Services(), _paths, _out, _err, Log);

    // The body's own seams may differ from the engine's, so a test can fail one check alone.
    private CaptureSelfTestServices Services(IScreenCapture? screen = null, IWindowInfoProvider? windows = null) =>
        new(_h.Engine, _h.Projects, screen ?? _h.Screen, windows ?? _h.Windows, _h.Codec, _h.Triggers, _h.Settings, new Owner(() => Interlocked.Increment(ref _disposed)));

    private static string Ints(double value) => ((int)value).ToString(System.Globalization.CultureInfo.InvariantCulture);

    // The screen with its monitors for the first read only: the seams check's, not the modes'.
    private sealed class MonitorsGoAway(IScreenCapture inner) : IScreenCapture
    {
        private int _reads;

        public IReadOnlyList<MonitorDescriptor> Monitors() => Interlocked.Increment(ref _reads) == 1 ? inner.Monitors() : [];

        public MonitorDescriptor? FromPoint(int x, int y) => inner.FromPoint(x, y);

        public PixelFrame Grab(MonitorDescriptor monitor) => inner.Grab(monitor);
    }

    // The harness owns the services; the test only sees that the run disposed its owner.
    private sealed class Owner(Action onDispose) : IAsyncDisposable
    {
        public ValueTask DisposeAsync()
        {
            onDispose();
            return ValueTask.CompletedTask;
        }
    }
}
