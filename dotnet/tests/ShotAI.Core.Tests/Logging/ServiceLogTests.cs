using Microsoft.Extensions.Logging;
using ShotAI.Core.Logging;
using ShotAI.Core.Tests.Support;
using Xunit;

namespace ShotAI.Core.Tests.Logging;

/// <summary>Spec 11 7.11 L1 and L2: the boundary trace line.</summary>
public sealed class ServiceLogTests
{
    [Fact]
    public void CallLinesNameServiceAndMember()
    {
        using var logs = new CapturingLoggerProvider();
        ServiceLog.Call(logs.CreateLogger(LogCategories.Svc), "IProjectService", "CreateProjectAsync");
        var entry = Assert.Single(logs.Entries);
        Assert.Equal(LogLevel.Debug, entry.Level);
        Assert.Equal("call: IProjectService.CreateProjectAsync", entry.Message);
        Assert.Equal("svc", entry.Category);
        Assert.Null(entry.Exception);
    }

    /// <summary>Written under <c>svc</c>, the successor of Electron's <c>ipc</c> scope.</summary>
    [Fact]
    public void WrittenUnderSvc()
    {
        using var h = new LogHarness();
        var provider = h.Provider(h.Options(minimum: LogLevel.Debug));
        ServiceLog.Call(provider.CreateLogger(LogCategories.Svc), "IAuthService", "SetApiKeyAsync");
        provider.Flush(TimeSpan.FromSeconds(10));
        Assert.Equal([LogHarness.Stamp + " [debug] (svc)      call: IAuthService.SetApiKeyAsync"], h.Lines());
    }

    /// <summary>Q-IPC-11: Debug only, so a Release build writes nothing unless <c>SHOTAI_LOG_LEVEL=debug</c>.</summary>
    [Fact]
    public void NothingAtInformation()
    {
        using var h = new LogHarness();
        var provider = h.Provider(h.Options(minimum: LogLevel.Information));
        ServiceLog.Call(provider.CreateLogger(LogCategories.Svc), "IProjectService", "CreateProjectAsync");
        provider.Flush(TimeSpan.FromSeconds(10));
        Assert.Null(h.Text());
    }
}
