using System.Text.Json;
using ShotAI.Core.Report;
using ShotAI.Core.Store;
using Xunit;

namespace ShotAI.Core.Tests.Report;

/// <summary>
/// Spec 05 7.3 and EDGE-REP-39: a project that is gone opens nothing and says nothing; any other
/// failure raises <c>OpenFailed</c>.
/// </summary>
public sealed class OpenFailureTests
{
    [Theory]
    [InlineData("ENOENT: no such file or directory, open 'C:\\p\\project.json'")]
    [InlineData("enoent")]
    [InlineData("No Such File")]
    [InlineData("Project NOT FOUND")]
    [InlineData("the folder was not found")]
    public void AMessageLikeElectronsIsGone(string message) => Assert.True(OpenFailure.IsGone(new IOException(message)));

    [Fact]
    public void TheFileSystemsOwnExceptionsAreGone()
    {
        Assert.True(OpenFailure.IsGone(new FileNotFoundException("x")));
        Assert.True(OpenFailure.IsGone(new DirectoryNotFoundException("x")));
    }

    /// <summary>The store's missing <c>project.json</c>: its message is Q-MODEL-11's, the wrapped exception is the file system's.</summary>
    [Fact]
    public void AMissingManifestIsGone()
    {
        Assert.True(OpenFailure.IsGone(new ManifestCorruptException("project.json is missing", new FileNotFoundException("x"))));
        Assert.True(OpenFailure.IsGone(new ManifestCorruptException("project.json is missing", new DirectoryNotFoundException("x"))));
        Assert.True(OpenFailure.IsGone(new InvalidOperationException("outer", new InvalidOperationException("middle", new FileNotFoundException("x")))));
    }

    [Fact]
    public void ADamagedManifestIsShown()
    {
        Assert.False(OpenFailure.IsGone(new ManifestCorruptException("not JSON", new JsonException("Unexpected token"))));
        Assert.False(OpenFailure.IsGone(new ManifestCorruptException("the JSON null")));
    }

    /// <summary>Only the outer message is matched, as Electron matched the one message it had.</summary>
    [Fact]
    public void AWrappedMessageIsNotMatched() =>
        Assert.False(OpenFailure.IsGone(new ManifestCorruptException("not JSON", new JsonException("Property not found"))));

    [Fact]
    public void OtherFailuresAreShown()
    {
        Assert.False(OpenFailure.IsGone(new UnauthorizedAccessException("Access to the path is denied.")));
        Assert.False(OpenFailure.IsGone(new ProjectNotKnownException()));
        Assert.False(OpenFailure.IsGone(new IOException("The process cannot access the file because it is being used by another process.")));
        Assert.False(OpenFailure.IsGone(new InvalidOperationException("found")));
    }

    [Fact]
    public void ArgumentsAreChecked() => Assert.Throws<ArgumentNullException>(() => OpenFailure.IsGone(null!));
}
