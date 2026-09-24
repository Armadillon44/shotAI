using System.Collections.Concurrent;
using ShotAI.Platform.Capture;
using ShotAI.Platform.Tests.Support;
using Xunit;

namespace ShotAI.Platform.Tests.Capture;

/// <summary>
/// INV-CAP-26: capture geometry is global physical pixels, so every capture thread is
/// per-monitor DPI aware. The app declares Per-Monitor V2 in its manifest; this test project
/// declares the same, so the hook, dispatcher and worker threads are checked as they run in
/// the app.
/// </summary>
[Collection(InputHookCollection.Name)]
public sealed class DpiAwarenessTests
{
    [Fact]
    public async Task ProcessIsPerMonitorV2()
    {
        Assert.True(User32.AreDpiAwarenessContextsEqual(User32.GetThreadDpiAwarenessContext(), User32.DpiAwarenessContextPerMonitorAwareV2), "the test process is not Per-Monitor V2");
        var dispatcher = new ConcurrentQueue<int>();
        using var target = new ClickTarget();
        using var source = new Win32TriggerSource(new ListLogger<Win32TriggerSource>());
        source.Attach(_ => dispatcher.Enqueue(User32.ThreadDpiAwareness()), null);
        var (x, y) = target.Point();
        SyntheticInput.Click(x, y);
        var deadline = DateTime.UtcNow + MouseHookTests.Timeout;
        while (dispatcher.IsEmpty)
        {
            if (DateTime.UtcNow > deadline) throw new TimeoutException("no click arrived");
            await Task.Delay(10, TestContext.Current.CancellationToken);
        }
        var worker = await Task.Run(User32.ThreadDpiAwareness, TestContext.Current.CancellationToken);

        Assert.Equal(User32.DpiAwarenessPerMonitorAware, (int)source.HookThreadAwarenessForTest);
        Assert.All(dispatcher, a => Assert.Equal(User32.DpiAwarenessPerMonitorAware, a));
        Assert.Equal(User32.DpiAwarenessPerMonitorAware, worker);
    }

    /// <summary>The other half of INV-CAP-26: the app's own manifest asks for Per-Monitor V2.</summary>
    [Fact]
    public void TheAppDeclaresPerMonitorV2()
    {
        var manifest = File.ReadAllText(Path.Combine(RepoRoot(), "dotnet", "src", "ShotAI.App", "app.manifest"));
        Assert.Contains("<dpiAwareness xmlns=\"http://schemas.microsoft.com/SMI/2016/WindowsSettings\">PerMonitorV2</dpiAwareness>", manifest, StringComparison.Ordinal);
    }

    private static string RepoRoot()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        {
            if (File.Exists(Path.Combine(dir.FullName, "dotnet", "ShotAI.slnx"))) return dir.FullName;
        }
        throw new DirectoryNotFoundException("The repository root was not found above " + AppContext.BaseDirectory);
    }
}
