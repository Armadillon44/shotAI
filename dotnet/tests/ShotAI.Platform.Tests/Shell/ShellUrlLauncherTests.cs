using System.ComponentModel;
using System.Diagnostics;
using ShotAI.Platform.Shell;
using Xunit;

namespace ShotAI.Platform.Tests.Shell;

/// <summary>
/// Spec 11 7.3.4 and 10 7.7 (INV-IPC-3, D-IPC-6): the launcher hands an https URI to the shell's
/// open on its own STA thread, as the URI and nothing else, and refuses anything that is not an
/// absolute https URI. The start is recorded, so no browser opens.
/// </summary>
public sealed class ShellUrlLauncherTests
{
    private readonly List<(ProcessStartInfo Info, ApartmentState Apartment, bool Background, Thread Thread)> _starts = [];

    private ShellUrlLauncher Launcher(Exception? failure = null) => new(info =>
    {
        var t = Thread.CurrentThread;
        lock (_starts) _starts.Add((info, t.GetApartmentState(), t.IsBackground, t));
        if (failure is not null) throw failure;
    });

    /// <summary>The URI goes to the shell's default open, with no verb and no arguments, on a background STA thread that is not the caller's.</summary>
    [Fact]
    public async Task LaunchesThroughTheShellOnAnStaThread()
    {
        await Launcher().LaunchAsync("https://github.com/Armadillon44/shotAI/releases/tag/v1.3.0");
        var start = Assert.Single(_starts);
        Assert.Equal(("https://github.com/Armadillon44/shotAI/releases/tag/v1.3.0", true, "", ""), (start.Info.FileName, start.Info.UseShellExecute, start.Info.Verb, start.Info.Arguments));
        Assert.Empty(start.Info.ArgumentList);
        Assert.Equal(ApartmentState.STA, start.Apartment);
        Assert.True(start.Background);
        Assert.Equal(StaThread.Name, start.Thread.Name);
        Assert.NotSame(Thread.CurrentThread, start.Thread);
    }

    /// <summary>The shell gets the URI as <see cref="Uri.AbsoluteUri"/> writes it.</summary>
    [Fact]
    public async Task TheUriIsNormalized()
    {
        await Launcher().LaunchAsync("https://GITHUB.com/a b");
        Assert.Equal("https://github.com/a%20b", Assert.Single(_starts).Info.FileName);
    }

    /// <summary>Anything but an absolute https URI is refused before any thread starts: a folder, a file or another scheme's handler never opens here.</summary>
    [Theory]
    [InlineData("http://github.com/")]
    [InlineData("file:///C:/Windows/System32/calc.exe")]
    [InlineData(@"C:\Windows\System32\calc.exe")]
    [InlineData("javascript:alert(1)")]
    [InlineData("mailto:a@b.c")]
    [InlineData("ms-settings:privacy")]
    [InlineData("not a url")]
    [InlineData("")]
    public async Task OnlyHttpsIsLaunched(string uri)
    {
        var e = await Assert.ThrowsAsync<ArgumentException>(() => Launcher().LaunchAsync(uri));
        Assert.Equal("absoluteUri", e.ParamName);
        Assert.Empty(_starts);
    }

    /// <summary>What the shell throws faults the task (R-ARCH-25: the link service's open throws it on).</summary>
    [Fact]
    public async Task AShellFailureFaultsTheTask()
    {
        var failure = new Win32Exception(1155);
        var e = await Assert.ThrowsAsync<Win32Exception>(() => Launcher(failure).LaunchAsync("https://anthropic.com/"));
        Assert.Same(failure, e);
    }

    [Fact]
    public async Task ArgumentsAreChecked()
    {
        Assert.Throws<ArgumentNullException>(() => new ShellUrlLauncher(null!));
        await Assert.ThrowsAsync<ArgumentNullException>(() => Launcher().LaunchAsync(null!));
    }
}
