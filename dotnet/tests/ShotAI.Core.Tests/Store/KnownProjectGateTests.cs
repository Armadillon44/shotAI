using ShotAI.Core.Store;
using Xunit;

namespace ShotAI.Core.Tests.Store;

/// <summary>
/// The known-project gate (spec 01 2.9.1, INV-MODEL-27, AC-MODEL-16): a folder strictly under
/// the projects folder at any depth, or exactly a recents entry; anything else throws
/// <see cref="ProjectNotKnownException"/> with Electron's text.
/// </summary>
public sealed class KnownProjectGateTests : IAsyncDisposable
{
    private readonly StoreHarness _h = new();

    public ValueTask DisposeAsync() => _h.DisposeAsync();

    private string Outside(string name) => _h.Temp.Combine("elsewhere", name);

    private async Task AssertRefusedAsync(string path)
    {
        var e = await Assert.ThrowsAsync<ProjectNotKnownException>(() => _h.Store.ResolveKnownProjectAsync(path));
        Assert.Equal("Project path is not within the projects directory", e.Message);
    }

    [Fact]
    public async Task AcceptsAFolderDirectlyUnderTheRoot()
    {
        var dir = Path.Combine(_h.Root, "proj1");
        Assert.Equal(dir, await _h.Store.ResolveKnownProjectAsync(dir));
    }

    [Fact]
    public async Task AcceptsAFolderAtAnyDepth()
    {
        var dir = Path.Combine(_h.Root, "group", "proj1");
        Assert.Equal(dir, await _h.Store.ResolveKnownProjectAsync(dir));
    }

    /// <summary>The folder need not exist: the gate is lexical, as <c>path.relative</c> is.</summary>
    [Fact]
    public async Task ResolvesARelativeSegmentThatStaysUnderTheRoot()
    {
        var dir = Path.Combine(_h.Root, "group", "..", "proj1");
        Assert.Equal(Path.Combine(_h.Root, "proj1"), await _h.Store.ResolveKnownProjectAsync(dir));
    }

    [Fact]
    public async Task ReturnsThePathWithoutATrailingSeparator()
    {
        var dir = Path.Combine(_h.Root, "proj1");
        Assert.Equal(dir, await _h.Store.ResolveKnownProjectAsync(dir + Path.DirectorySeparatorChar));
    }

    [Fact]
    public Task RefusesAPathOutsideTheRoot() => AssertRefusedAsync(Outside("proj1"));

    [Fact]
    public Task RefusesTheRootItself() => AssertRefusedAsync(_h.Root);

    [Fact]
    public Task RefusesTheRootWithATrailingSeparator() => AssertRefusedAsync(_h.Root + Path.DirectorySeparatorChar);

    [Fact]
    public Task RefusesTheRootsParent() => AssertRefusedAsync(_h.Temp.Root);

    /// <summary>A sibling whose name starts with the root's name is still outside.</summary>
    [Fact]
    public Task RefusesASiblingOfTheRoot() => AssertRefusedAsync(_h.Root + "-sibling");

    [Fact]
    public Task RefusesAnEscapeThroughDotDot() => AssertRefusedAsync(Path.Combine(_h.Root, "..", "elsewhere"));

    /// <summary><c>rel.startsWith('..')</c> also refuses a child named like <c>..foo</c>, a safe false negative.</summary>
    [Fact]
    public Task RefusesAChildNamedLikeDotDotFoo() => AssertRefusedAsync(Path.Combine(_h.Root, "..foo"));

    [Fact]
    public Task RefusesAnEmptyPath() => AssertRefusedAsync("");

    [Fact]
    public Task RefusesAPathThatCannotBeResolved() => AssertRefusedAsync(Path.Combine(_h.Root, "a" + (char)0 + "b"));

    [Fact]
    public async Task AcceptsARecentsEntryOutsideTheRoot()
    {
        var dir = Outside("proj1");
        _h.Settings.SeedRecents(dir);
        Assert.Equal(dir, await _h.Store.ResolveKnownProjectAsync(dir));
    }

    /// <summary>Both sides go through <c>path.resolve</c>, so a stored trailing separator still matches.</summary>
    [Fact]
    public async Task ComparesARecentsEntryInItsResolvedForm()
    {
        var dir = Outside("proj1");
        _h.Settings.SeedRecents(dir + Path.DirectorySeparatorChar);
        Assert.Equal(dir, await _h.Store.ResolveKnownProjectAsync(dir));
    }

    [Fact]
    public async Task RefusesAFolderInsideARecentsEntry()
    {
        _h.Settings.SeedRecents(Outside("proj1"));
        await AssertRefusedAsync(Path.Combine(Outside("proj1"), "shots"));
    }

    [Fact]
    public async Task FollowsAChangedProjectsFolder()
    {
        var dir = Outside("proj1");
        await AssertRefusedAsync(dir);
        await _h.Store.SetProjectsDirAsync(_h.Temp.Combine("elsewhere"));
        Assert.Equal(dir, await _h.Store.ResolveKnownProjectAsync(dir));
    }

    /// <summary><c>path.win32.relative</c> ignores case, and so does <see cref="Path.GetRelativePath"/> on Windows.</summary>
    [Fact]
    public async Task OnWindowsACaseVariantUnderTheRootIsAccepted()
    {
        if (!OperatingSystem.IsWindows()) Assert.Skip("Linux paths are case-sensitive");
        var variant = Path.Combine(_h.Root.ToUpperInvariant(), "proj1");
        Assert.Equal(variant, await _h.Store.ResolveKnownProjectAsync(variant));
    }

    /// <summary>The recents compare is an exact string compare on every platform, Windows included.</summary>
    [Fact]
    public async Task ACaseVariantOfARecentsEntryIsRefused()
    {
        _h.Settings.SeedRecents(Outside("proj1"));
        await AssertRefusedAsync(Outside("PROJ1"));
    }

    [Fact]
    public async Task OnLinuxACaseVariantOfTheRootIsOutside()
    {
        if (OperatingSystem.IsWindows()) Assert.Skip("Windows paths ignore case");
        await AssertRefusedAsync(Path.Combine(_h.Root.ToUpperInvariant(), "proj1"));
    }
}
