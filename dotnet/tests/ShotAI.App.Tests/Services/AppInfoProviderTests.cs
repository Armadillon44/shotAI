using System.Runtime.InteropServices;
using System.Windows.Threading;
using ShotAI.App.Services;
using ShotAI.App.Tests.Support;
using ShotAI.Core.Diagnostics;
using ShotAI.Core.Updates;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace ShotAI.App.Tests.Services;

/// <summary>Spec 11 7.3.5 (I1, R-ARCH-12): what About and later Settings read of the running app.</summary>
public sealed class AppInfoProviderTests
{
    [Fact]
    public void FieldsAreTheSpecs()
    {
        var info = new AppInfoProvider(new CountingProbe("140.0.3485.54")).Current;
        Assert.Equal("shotAI", info.Name);
        Assert.Equal(AppVersion.Current.Display, info.Version);
        Assert.DoesNotContain('+', info.Version);
        Assert.Equal("win32", info.Platform);
        Assert.Equal(RuntimeInformation.ProcessArchitecture.ToString().ToLowerInvariant(), info.Arch);
        Assert.Contains(info.Arch, new[] { "x64", "arm64" });
        Assert.Equal(Environment.Version.ToString(), info.DotNetVersion);
        Assert.Equal("140.0.3485.54", info.WebView2Version);
    }

    /// <summary>The probe runs on the first read, not at startup, and once: the snapshot is the same object after.</summary>
    [Fact]
    public void TheProbeRunsOnceOnTheFirstRead()
    {
        var probe = new CountingProbe(null);
        var provider = new AppInfoProvider(probe);
        Assert.Equal(0, probe.Calls);
        var first = provider.Current;
        Assert.Same(first, provider.Current);
        Assert.Equal(1, probe.Calls);
        Assert.Null(first.WebView2Version);
    }

    /// <summary>
    /// The container's provider over Platform's probe finds the runtime the runners have, so the
    /// core assembly and its loader reach an App output through Platform alone.
    /// </summary>
    [Fact]
    public Task TheContainersProbeFindsTheRuntime() => Sta.RunAsync(() =>
    {
        using var c = new TestContainer(Dispatcher.CurrentDispatcher);
        var info = c.Provider.GetRequiredService<IAppInfo>().Current;
        Assert.NotNull(info.WebView2Version);
        Assert.Matches(@"^\d+\.\d+\.\d+\.\d+$", info.WebView2Version);
    });

    private sealed class CountingProbe(string? version) : IWebView2RuntimeInfo
    {
        public int Calls { get; private set; }

        public string? GetVersion()
        {
            Calls++;
            return version;
        }
    }
}
