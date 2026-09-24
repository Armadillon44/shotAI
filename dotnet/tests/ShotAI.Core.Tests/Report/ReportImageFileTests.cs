using ShotAI.Core.Report;
using ShotAI.Core.Tests.Support;
using Xunit;

namespace ShotAI.Core.Tests.Report;

/// <summary>
/// Spec 05 7.11 step 2, INV-REP-32: an image is read whole and closed before it is decoded, and
/// the read shares the file for writing and deleting, so an editor save or an archive is never
/// refused while the report reads.
/// </summary>
public sealed class ReportImageFileTests
{
    [Fact]
    public async Task ReadsTheWholeFile()
    {
        using var temp = new TempDir();
        var path = temp.Combine("a.png");
        byte[] bytes = [.. Enumerable.Range(0, 200_000).Select(i => (byte)i)];
        await File.WriteAllBytesAsync(path, bytes, TestContext.Current.CancellationToken);
        Assert.Equal(bytes, await ReportImageFile.ReadAllBytesAsync(path, TestContext.Current.CancellationToken));
        Assert.Empty(await ReportImageFile.ReadAllBytesAsync(await Empty(temp), TestContext.Current.CancellationToken));
    }

    /// <summary>Nothing holds the file after the read: it can be replaced and deleted at once.</summary>
    [Fact]
    public async Task HoldsNothingAfterTheRead()
    {
        using var temp = new TempDir();
        var path = temp.Combine("a.png");
        await File.WriteAllBytesAsync(path, [1, 2, 3], TestContext.Current.CancellationToken);
        await ReportImageFile.ReadAllBytesAsync(path, TestContext.Current.CancellationToken);
        await File.WriteAllBytesAsync(path + ".tmp", [4], TestContext.Current.CancellationToken);
        File.Move(path + ".tmp", path, overwrite: true);
        File.Delete(path);
        Assert.False(File.Exists(path));
    }

    /// <summary>
    /// The read opens a file another process holds open for writing and deleting: Windows refuses
    /// the open unless it shares both (on Linux the open always succeeds).
    /// </summary>
    [Fact]
    public async Task SharesTheFileForWritingAndDeleting()
    {
        using var temp = new TempDir();
        var path = temp.Combine("a.png");
        await File.WriteAllBytesAsync(path, [1, 2, 3], TestContext.Current.CancellationToken);
        await using var writer = new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.ReadWrite | FileShare.Delete, 4096, FileOptions.DeleteOnClose);
        Assert.Equal([1, 2, 3], await ReportImageFile.ReadAllBytesAsync(path, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task AMissingFileThrows()
    {
        using var temp = new TempDir();
        await Assert.ThrowsAsync<FileNotFoundException>(() => ReportImageFile.ReadAllBytesAsync(temp.Combine("none.png"), TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task ACanceledReadThrows()
    {
        using var temp = new TempDir();
        var path = temp.Combine("a.png");
        await File.WriteAllBytesAsync(path, [1, 2, 3], TestContext.Current.CancellationToken);
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => ReportImageFile.ReadAllBytesAsync(path, cts.Token));
    }

    [Fact]
    public async Task ArgumentsAreChecked()
    {
        await Assert.ThrowsAsync<ArgumentNullException>(() => ReportImageFile.ReadAllBytesAsync(null!, TestContext.Current.CancellationToken));
        var empty = await Assert.ThrowsAsync<ArgumentException>(() => ReportImageFile.ReadAllBytesAsync("", TestContext.Current.CancellationToken));
        Assert.Equal("absolutePath", empty.ParamName);
    }

    private static async Task<string> Empty(TempDir temp)
    {
        var path = temp.Combine("empty.png");
        await File.WriteAllBytesAsync(path, [], TestContext.Current.CancellationToken);
        return path;
    }
}
