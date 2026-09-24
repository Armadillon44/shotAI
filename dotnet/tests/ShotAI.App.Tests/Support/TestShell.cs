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
/// The shell, Home and the project view as the container makes them, over a
/// <see cref="ListingProjects"/> store and a <see cref="TestClock"/>, on the calling UI thread.
/// </summary>
internal sealed class TestShell : IDisposable
{
    public TestShell(ListingProjects? projects = null, TestClock? clock = null)
    {
        Projects = projects ?? new ListingProjects();
        Clock = clock ?? new TestClock(new DateTimeOffset(2026, 7, 22, 10, 0, 0, TimeSpan.Zero));
        Notices = new NoticeCenter(new Logger<NoticeCenter>(Logs));
        Home = new HomeViewModel(Projects, Notices, new WpfUiDispatcher(Dispatcher.CurrentDispatcher), Clock, new Logger<HomeViewModel>(Logs));
        Menu = new AppMenuViewModel();
        Sessions = new ProjectSessionFactory(Projects, new Logger<ProjectSessionFactory>(Logs));
        Project = new ProjectDetailViewModel(Projects, Sessions, new ReportViewModelFactory(), Layout, new Logger<ProjectDetailViewModel>(Logs));
        Shell = new ShellViewModel(Home, Project, Menu, Notices);
    }

    public ListingProjects Projects { get; }

    public TestClock Clock { get; }

    public CapturingLoggerProvider Logs { get; } = new();

    public NoticeCenter Notices { get; }

    public HomeViewModel Home { get; }

    public AppMenuViewModel Menu { get; }

    public ProjectSessionFactory Sessions { get; }

    public RecordingLayout Layout { get; } = new();

    public ProjectDetailViewModel Project { get; }

    public ShellViewModel Shell { get; }

    public void Dispose()
    {
        Project.Dispose();
        Home.Dispose();
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
