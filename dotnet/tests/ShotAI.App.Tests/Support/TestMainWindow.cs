using Microsoft.Extensions.Logging.Abstractions;
using ShotAI.App.Chrome;
using ShotAI.App.Home;
using ShotAI.App.Services;
using ShotAI.App.Shell;
using ShotAI.App.Threading;
using ShotAI.Platform.Capture;

namespace ShotAI.App.Tests.Support;

/// <summary>The main window as startup step 8 makes it, over its own registry and a fixed <see cref="IAppInfo"/>. On the UI thread.</summary>
internal static class TestMainWindow
{
    public static MainWindow Create(
        WindowRegistration? registration = null, MainWindowSizer? sizer = null, AppMenuViewModel? menu = null, IAppInfo? appInfo = null, ShellViewModel? shell = null)
    {
        menu ??= new AppMenuViewModel();
        return new(
            registration ?? new WindowRegistration(new OwnWindowRegistry(NullLogger<OwnWindowRegistry>.Instance)),
            menu,
            sizer ?? new MainWindowSizer(),
            appInfo ?? new FakeAppInfo(),
            shell ?? Shell(menu));
    }

    // A shell over an empty store; nothing lists until the test starts it.
    private static ShellViewModel Shell(AppMenuViewModel menu)
    {
        var notices = new NoticeCenter(NullLogger<NoticeCenter>.Instance);
        var home = new HomeViewModel(
            new ListingProjects(), notices, new WpfUiDispatcher(System.Windows.Threading.Dispatcher.CurrentDispatcher), TimeProvider.System, NullLogger<HomeViewModel>.Instance);
        return new ShellViewModel(home, menu, notices);
    }
}

/// <summary>An <see cref="IAppInfo"/> with made-up versions.</summary>
internal sealed class FakeAppInfo : IAppInfo
{
    public AppInfo Current { get; init; } = new("shotAI", "2.0.0-test", "win32", "x64", "10.0.12", "140.0.3485.54");
}
