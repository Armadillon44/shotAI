using Microsoft.Extensions.Logging.Abstractions;
using ShotAI.App.Services;
using ShotAI.App.Shell;
using ShotAI.Platform.Capture;

namespace ShotAI.App.Tests.Support;

/// <summary>The main window as startup step 8 makes it, over its own registry and a fixed <see cref="IAppInfo"/>. On the UI thread.</summary>
internal static class TestMainWindow
{
    public static MainWindow Create(WindowRegistration? registration = null, MainWindowSizer? sizer = null, AppMenuViewModel? menu = null, IAppInfo? appInfo = null) =>
        new(
            registration ?? new WindowRegistration(new OwnWindowRegistry(NullLogger<OwnWindowRegistry>.Instance)),
            menu ?? new AppMenuViewModel(),
            sizer ?? new MainWindowSizer(),
            appInfo ?? new FakeAppInfo());
}

/// <summary>An <see cref="IAppInfo"/> with made-up versions.</summary>
internal sealed class FakeAppInfo : IAppInfo
{
    public AppInfo Current { get; init; } = new("shotAI", "2.0.0-test", "win32", "x64", "10.0.12", "140.0.3485.54");
}
