using ShotAI.Core.Model;
using ShotAI.Core.Store;
using Xunit;

namespace ShotAI.Core.Tests.Store;

/// <summary>
/// Ports <c>src/main/mutate-serialize.test.ts</c> (P6, AC-MODEL-8): the queue serializes every
/// read-modify-write, so concurrent mutations lose nothing and never tear the file.
/// </summary>
public sealed class MutateSerializeTests : IAsyncLifetime
{
    private const string Init = "INIT";

    /// <summary>The TS test's manifest, <c>JSON.stringify</c>-ed: the title non-empty, so the read keeps it, and both dates empty.</summary>
    private const string Manifest =
        """{"version":1,"id":"test","title":"INIT","createdWith":"shotAI","createdAt":"","updatedAt":"","captureSettings":null,"steps":[],"sopBackup":null}""";

    private readonly StoreHarness _h = new();
    private string _project = "";

    public ValueTask InitializeAsync()
    {
        _project = _h.Project("proj1", Manifest);
        return ValueTask.CompletedTask;
    }

    public ValueTask DisposeAsync() => _h.DisposeAsync();

    private Task<ProjectManifest> Append(string suffix) =>
        _h.Store.MutateAsync(_project, m =>
        {
            m.Title += suffix;
            return ValueTask.FromResult(MutateResult.Changed);
        });

    [Fact]
    public async Task DoesNotLoseUpdatesUnderManyConcurrentCalls()
    {
        await Task.WhenAll(Enumerable.Range(0, 40).Select(_ => Append("x")));
        Assert.Equal(Init + new string('x', 40), StoreHarness.OnDisk(_project)["title"]!.GetValue<string>());
    }

    [Fact]
    public async Task LeavesNoStrayTmpFilesAndAValidManifest()
    {
        await Task.WhenAll(Enumerable.Range(0, 25).Select(_ => Append("y")));
        Assert.Empty(Directory.GetFiles(_project, "*.tmp"));
        Assert.Equal(Init + new string('y', 25), StoreHarness.OnDisk(_project)["title"]!.GetValue<string>());
    }

    [Fact]
    public async Task AThrowingMutationAbortsItsWriteWithoutLosingOthers()
    {
        var a = Append("a");
        var thrower = _h.Store.MutateAsync(_project, _ => throw new InvalidOperationException("abort this one"));
        var b = Append("b");

        await a;
        Assert.Equal("abort this one", (await Assert.ThrowsAsync<InvalidOperationException>(() => thrower)).Message);
        await b;
        Assert.Equal(Init + "ab", StoreHarness.OnDisk(_project)["title"]!.GetValue<string>());
    }
}
