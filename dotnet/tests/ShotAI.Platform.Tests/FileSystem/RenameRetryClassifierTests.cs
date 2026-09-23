using ShotAI.Core.Store;
using ShotAI.Platform.FileSystem;
using ShotAI.Platform.Tests.Support;
using Xunit;

namespace ShotAI.Platform.Tests.FileSystem;

/// <summary>
/// <see cref="WindowsRenameRetryClassifier"/> against libuv 1.52.1's mapping (spec 01 7.6), and
/// a real sharing violation that <see cref="AtomicFile"/> waits out.
/// </summary>
public sealed class RenameRetryClassifierTests : IDisposable
{
    private static readonly WindowsRenameRetryClassifier Classifier = new();
    private readonly TempDir _root = new("rename-");

    public void Dispose() => _root.Dispose();

    private static IOException Win32(int code) => new("x", unchecked((int)(0x80070000u | (uint)code)));

    [Theory]
    [InlineData(5, "EPERM")]       // ERROR_ACCESS_DENIED
    [InlineData(1314, "EPERM")]    // ERROR_PRIVILEGE_NOT_HELD
    [InlineData(32, "EBUSY")]      // ERROR_SHARING_VIOLATION
    [InlineData(33, "EBUSY")]      // ERROR_LOCK_VIOLATION
    [InlineData(231, "EBUSY")]     // ERROR_PIPE_BUSY
    [InlineData(740, "EACCES")]    // ERROR_ELEVATION_REQUIRED
    [InlineData(1920, "EACCES")]   // ERROR_CANT_ACCESS_FILE
    [InlineData(10013, "EACCES")]  // WSAEACCES
    public void MapsTheRetriedCodesAsLibuvDoes(int code, string expected) => Assert.Equal(expected, Classifier.Classify(Win32(code)));

    /// <summary>libuv 1.50 moved ERROR_NOACCESS from EACCES to EFAULT, which Electron does not retry.</summary>
    [Theory]
    [InlineData(998)]   // ERROR_NOACCESS
    [InlineData(2)]     // ERROR_FILE_NOT_FOUND
    [InlineData(3)]     // ERROR_PATH_NOT_FOUND
    [InlineData(112)]   // ERROR_DISK_FULL
    [InlineData(183)]   // ERROR_ALREADY_EXISTS
    public void EveryOtherCodeIsFinal(int code) => Assert.Null(Classifier.Classify(Win32(code)));

    [Fact]
    public void ReadsTheErrorFromWhatDotNetThrows()
    {
        Assert.Equal("EPERM", Classifier.Classify(new UnauthorizedAccessException("denied")));
        Assert.Null(Classifier.Classify(new FileNotFoundException("gone")));
        Assert.Null(Classifier.Classify(new IOException("not a Win32 error")));
        Assert.Null(Classifier.Classify(new InvalidOperationException("x")));
    }

    /// <summary>
    /// A target held open without FILE_SHARE_DELETE makes MoveFileExW fail; the classifier calls
    /// it transient, and the rename lands once the handle closes 30 ms later.
    /// </summary>
    [Fact]
    public async Task ARealSharingViolationIsRetriedUntilTheLockClears()
    {
        var target = _root.File("project.json", "old");
        var tmp = _root.File("project.json.tmp", "new");
        var codes = new List<string>();
        var atomic = new AtomicFile(TimeProvider.System, Classifier);

        await using var held = new FileStream(target, FileMode.Open, FileAccess.Read, FileShare.None);
        var failure = Record.Exception(() => File.Move(tmp, target, overwrite: true));
        Assert.NotNull(failure);
        var code = Classifier.Classify(failure);
        Assert.True(code is "EBUSY" or "EPERM", $"{failure.GetType().Name} 0x{failure.HResult:X8} classified as {code ?? "null"}");

        var rename = atomic.RenameWithRetryAsync(tmp, target, codes.Add);
        await Task.Delay(30, TestContext.Current.CancellationToken);
        await held.DisposeAsync();
        await rename;

        Assert.Equal("new", await File.ReadAllTextAsync(target, TestContext.Current.CancellationToken));
        Assert.False(File.Exists(tmp));
        Assert.Equal([code!], codes);
    }
}
