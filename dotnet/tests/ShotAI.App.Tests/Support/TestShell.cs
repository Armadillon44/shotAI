using System.Windows;
using System.Windows.Threading;
using Microsoft.Extensions.Logging;
using ShotAI.App.Chrome;
using ShotAI.App.Home;
using ShotAI.App.Report;
using ShotAI.App.Shell;
using ShotAI.App.Tests.Chrome;
using ShotAI.App.Threading;
using ShotAI.Core.Store;
using ShotAI.Core.Theme;

namespace ShotAI.App.Tests.Support;

/// <summary>
/// The shell, Home, the project view and the menu as the container and startup make them, over a
/// <see cref="ListingProjects"/> store, a <see cref="FakeSettingsService"/>, a
/// <see cref="FakeShellReveal"/>, a <see cref="TestClock"/>, a <see cref="FakeCaptureService"/>
/// and a <see cref="FakeAreaSelection"/>, on the calling UI thread: the navigation state follows
/// the shell and the menu follows the navigation state (06 7.7), and Home and the project view
/// share the one capture-mode picker.
/// </summary>
internal sealed class TestShell : IDisposable
{
    /// <param name="projects">The store; a new one when null.</param>
    /// <param name="clock">The clock; 2026-07-22 10:00 UTC when null.</param>
    /// <param name="settings">The settings; the defaults when null.</param>
    /// <param name="sessions">Wraps the real session factory, for a test that watches the sessions.</param>
    /// <param name="capture">The capture engine; an idle one when null.</param>
    public TestShell(
        ListingProjects? projects = null, TestClock? clock = null, FakeSettingsService? settings = null,
        Func<IProjectSessionFactory, IProjectSessionFactory>? sessions = null, FakeCaptureService? capture = null)
    {
        Capture = capture ?? new FakeCaptureService();
        Projects = projects ?? new ListingProjects();
        Clock = clock ?? new TestClock(new DateTimeOffset(2026, 7, 22, 10, 0, 0, TimeSpan.Zero));
        Settings = settings ?? new FakeSettingsService();
        var ui = new WpfUiDispatcher(Dispatcher.CurrentDispatcher);
        Notices = new NoticeCenter(new Logger<NoticeCenter>(Logs));
        Confirm = new ConfirmService(ui);
        Mode = new CaptureModePickerViewModel(Capture, Areas, Notices);
        Home = new HomeViewModel(Projects, Reveal, Notices, Confirm, Mode, ui, Clock, new Logger<HomeViewModel>(Logs));
        Navigation = new NavigationState(new Logger<NavigationState>(Logs));
        Menu = new AppMenuViewModel(Navigation, Settings, ui, new Logger<AppMenuViewModel>(Logs));
        IProjectSessionFactory real = new ProjectSessionFactory(Projects, new Logger<ProjectSessionFactory>(Logs));
        Sessions = sessions?.Invoke(real) ?? real;
        Project = new ProjectDetailViewModel(Projects, Sessions, new ReportViewModelFactory(), Layout, Mode, new Logger<ProjectDetailViewModel>(Logs));
        Shell = new ShellViewModel(Home, Project, Menu, Notices, Confirm, Capture, Projects, ui);
        Navigation.Follow(Shell);
    }

    /// <summary>The capture engine: nothing listed and idle until the test says otherwise.</summary>
    public FakeCaptureService Capture { get; }

    /// <summary>The area overlay, which answers each selection as the test sets it.</summary>
    public FakeAreaSelection Areas { get; } = new();

    /// <summary>The one capture-mode picker, Home's and the project view's.</summary>
    public CaptureModePickerViewModel Mode { get; }

    public ListingProjects Projects { get; }

    public FakeSettingsService Settings { get; }

    public NavigationState Navigation { get; }

    public TestClock Clock { get; }

    public CapturingLoggerProvider Logs { get; } = new();

    public NoticeCenter Notices { get; }

    /// <summary>The confirm dialog's service; a test answers it with its commands.</summary>
    public ConfirmService Confirm { get; }

    /// <summary>The shell reveal, recording each reveal.</summary>
    public FakeShellReveal Reveal { get; } = new();

    public HomeViewModel Home { get; }

    public AppMenuViewModel Menu { get; }

    public IProjectSessionFactory Sessions { get; }

    public RecordingLayout Layout { get; } = new();

    public ProjectDetailViewModel Project { get; }

    public ShellViewModel Shell { get; }

    public void Dispose()
    {
        Shell.Dispose();
        Project.Dispose();
        Home.Dispose();
        Menu.Dispose();
    }

    /// <summary>
    /// A window showing <paramref name="content"/> with the App's styles and a theme, as the
    /// Application's resources give them, not yet shown.
    /// </summary>
    public static Window Host(FrameworkElement content, double width = 720, double height = 740, string brand = "shotAI", Appearance appearance = Appearance.Light)
    {
        var window = new Window { Width = width, Height = height, Content = content, ShowActivated = false, WindowStartupLocation = WindowStartupLocation.Manual, Left = 0, Top = 0 };
        window.Resources.MergedDictionaries.Add(ControlStylesTests.Load("Themes/Controls.xaml"));
        window.Resources.MergedDictionaries.Add(ThemeResources.Build(ThemeTokenSet.For(brand, appearance)));
        return window;
    }

    /// <summary>Posted work runs at Normal, and layout before the idle priority comes back.</summary>
    public static async Task Settle()
    {
        await Dispatcher.Yield(DispatcherPriority.Background);
        await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
    }

    /// <summary>
    /// Settles until <paramref name="condition"/> holds, for work that finishes on the thread pool
    /// (an image decode); false when it still does not after <paramref name="seconds"/>.
    /// </summary>
    public static async Task<bool> UntilAsync(Func<bool> condition, double seconds = 20)
    {
        var clock = System.Diagnostics.Stopwatch.StartNew();
        while (clock.Elapsed < TimeSpan.FromSeconds(seconds))
        {
            await Settle();
            if (condition()) return true;
            await Task.Delay(20, Xunit.TestContext.Current.CancellationToken);
        }
        await Settle();
        return condition();
    }
}
