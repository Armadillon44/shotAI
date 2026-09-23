using Microsoft.Extensions.Logging;
using ShotAI.Core.Tests.Support;
using ShotAI.Core.Threading;
using Xunit;

namespace ShotAI.Core.Tests.Threading;

/// <summary><see cref="EventRaiser"/> (spec 11 7.3.1, INV-IPC-24, L5).</summary>
public sealed class EventRaiserTests
{
    private readonly CapturingLoggerProvider _logs = new();

    private ILogger Log => _logs.CreateLogger("test");

    [Fact]
    public void AllHandlersRunInOrder()
    {
        var seen = new List<string>();
        EventHandler<int>? handler = null;
        handler += (_, n) => seen.Add($"a{n}");
        handler += (_, n) => seen.Add($"b{n}");
        handler += (_, n) => seen.Add($"c{n}");

        EventRaiser.Raise(handler, this, 7, Log, "StateChanged");

        Assert.Equal(["a7", "b7", "c7"], seen);
        Assert.Empty(_logs.Entries);
    }

    [Fact]
    public void ThrowingHandlerIsLoggedAndOthersStillRun()
    {
        var seen = new List<string>();
        var boom = new InvalidOperationException("subscriber bug");
        EventHandler<int>? handler = null;
        handler += (_, _) => seen.Add("first");
        handler += (_, _) => throw boom;
        handler += (_, _) => seen.Add("third");

        EventRaiser.Raise(handler, this, 1, Log, "StateChanged");

        Assert.Equal(["first", "third"], seen);
        var entry = Assert.Single(_logs.Entries);
        Assert.Equal(LogLevel.Warning, entry.Level);
        Assert.Equal("event handler failed: StateChanged", entry.Message);
        Assert.Same(boom, entry.Exception);
    }

    [Fact]
    public void NullHandlerIsNoop()
    {
        EventRaiser.Raise<int>(null, this, 1, Log, "StateChanged");
        EventRaiser.Raise(null, this, Log, "ProjectsChanged");
        Assert.Empty(_logs.Entries);
    }

    [Fact]
    public void NonGenericOverloadIsolatesHandlersToo()
    {
        var seen = new List<string>();
        EventHandler? handler = null;
        handler += (_, e) => { Assert.Same(EventArgs.Empty, e); seen.Add("first"); };
        handler += (_, _) => throw new InvalidOperationException("subscriber bug");
        handler += (_, _) => seen.Add("third");

        EventRaiser.Raise(handler, this, Log, "ProjectsChanged");

        Assert.Equal(["first", "third"], seen);
        Assert.Equal("event handler failed: ProjectsChanged", Assert.Single(_logs.Entries).Message);
    }
}
