using ShotAI.Core.Errors;
using ShotAI.Core.Store;
using ShotAI.Core.Tests.Store;
using Xunit;

namespace ShotAI.Core.Tests.Validation;

/// <summary>
/// <see cref="ImportLimits"/> (spec 11 7.3.2, V9, EDGE-IPC-20, AC-IPC-10): an empty and an
/// oversized image are distinct refusals, and both come before the magic-byte check.
/// </summary>
public sealed class ImportLimitsTests
{
    [Fact]
    public void ZeroBytesIsNoImageData() =>
        Assert.Equal("No image data received", Assert.Throws<ShotAIException>(() => ImportLimits.Check(0)).Message);

    [Fact]
    public void ExactlyMaxIsAccepted()
    {
        Assert.Equal(62914560, ImportLimits.MaxBytes);
        ImportLimits.Check(62914560);
        ImportLimits.Check(1);
    }

    [Fact]
    public void OneOverMaxIsTooLarge() =>
        Assert.Equal("Image too large (max 60 MB)", Assert.Throws<ShotAIException>(() => ImportLimits.Check(62914561)).Message);

    /// <summary>An empty buffer is "no data", and a PNG one byte over the cap is "too large", never "unsupported".</summary>
    [Fact]
    public async Task ChecksRunBeforeMagicBytes()
    {
        await using var h = new StoreHarness();
        var project = h.Project("proj1");

        var empty = await Assert.ThrowsAsync<ShotAIException>(() => h.Store.ImportStepAsync(project, ReadOnlyMemory<byte>.Empty, null));
        Assert.Equal(ImportLimits.EmptyMessage, empty.Message);

        var huge = new byte[ImportLimits.MaxBytes + 1];
        StoreHarness.Png.CopyTo(huge, 0);
        var tooLarge = await Assert.ThrowsAsync<ShotAIException>(() => h.Store.ImportStepAsync(project, huge, null));
        Assert.Equal(ImportLimits.TooLargeMessage, tooLarge.Message);
        Assert.False(Directory.Exists(Path.Join(project, "shots")));
    }
}
