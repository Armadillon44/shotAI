using Microsoft.Extensions.Logging.Abstractions;
using ShotAI.App.Chrome;
using ShotAI.App.Home;
using ShotAI.App.Report;
using ShotAI.App.Services;
using ShotAI.App.Shell;
using ShotAI.App.Threading;
using ShotAI.Core.Store;
using ShotAI.Platform.Capture;
using ShotAI.Platform.Imaging;

namespace ShotAI.App.Tests.Support;

/// <summary>The main window as startup step 8 makes it, over its own registry and a fixed <see cref="IAppInfo"/>. On the UI thread.</summary>
internal static class TestMainWindow
{
    /// <summary>
    /// The window over <paramref name="menu"/> and <paramref name="shell"/>; without them, a menu
    /// that follows a navigation state that follows a shell over that menu, as startup wires them.
    /// </summary>
    public static MainWindow Create(
        WindowRegistration? registration = null, MainWindowSizer? sizer = null, AppMenuViewModel? menu = null, IAppInfo? appInfo = null, ShellViewModel? shell = null)
    {
        if (menu is null)
        {
            var navigation = new NavigationState(NullLogger<NavigationState>.Instance);
            menu = new AppMenuViewModel(
                navigation, new FakeSettingsService(), new WpfUiDispatcher(System.Windows.Threading.Dispatcher.CurrentDispatcher), NullLogger<AppMenuViewModel>.Instance);
            shell ??= Shell(menu);
            navigation.Follow(shell);
        }
        return new(
            registration ?? new WindowRegistration(new OwnWindowRegistry(NullLogger<OwnWindowRegistry>.Instance)),
            menu,
            sizer ?? new MainWindowSizer(),
            appInfo ?? new FakeAppInfo(),
            shell ?? Shell(menu),
            new ReportImageLoader(new ReportImageDecoder(), NullLogger<ReportImageLoader>.Instance));
    }

    // A shell over an empty store; nothing lists until the test starts it.
    private static ShellViewModel Shell(AppMenuViewModel menu)
    {
        var notices = new NoticeCenter(NullLogger<NoticeCenter>.Instance);
        var projects = new ListingProjects();
        var ui = new WpfUiDispatcher(System.Windows.Threading.Dispatcher.CurrentDispatcher);
        var confirm = new ConfirmService(ui);
        var home = new HomeViewModel(projects, new FakeShellReveal(), notices, confirm, ui, TimeProvider.System, NullLogger<HomeViewModel>.Instance);
        var project = new ProjectDetailViewModel(
            projects, new ProjectSessionFactory(projects, NullLogger<ProjectSessionFactory>.Instance), new ReportViewModelFactory(), new RecordingLayout(),
            NullLogger<ProjectDetailViewModel>.Instance);
        return new ShellViewModel(home, project, menu, notices, confirm);
    }
}

/// <summary>An <see cref="IAppInfo"/> with made-up versions.</summary>
internal sealed class FakeAppInfo : IAppInfo
{
    public AppInfo Current { get; init; } = new("shotAI", "2.0.0-test", "win32", "x64", "10.0.12", "140.0.3485.54");
}
