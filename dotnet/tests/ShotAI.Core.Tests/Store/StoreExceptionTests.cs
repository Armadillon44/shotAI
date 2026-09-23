using ShotAI.Core.Errors;
using ShotAI.Core.Store;
using Xunit;

namespace ShotAI.Core.Tests.Store;

/// <summary>
/// The store's exceptions (spec 01 7.13, spec 11 X2): each derives from
/// <see cref="ShotAIException"/>, so <see cref="UserMessage.From"/> shows its text verbatim.
/// Each exception a later WP adds joins the first test without a change here.
/// </summary>
public sealed class StoreExceptionTests
{
    [Fact]
    public void EveryStoreExceptionIsAShotAIException()
    {
        var exceptions = typeof(ProjectStore).Assembly.GetTypes()
            .Where(t => t.Namespace == typeof(ProjectStore).Namespace && typeof(Exception).IsAssignableFrom(t))
            .ToArray();

        Assert.NotEmpty(exceptions);
        Assert.All(exceptions, t => Assert.True(typeof(ShotAIException).IsAssignableFrom(t), t.Name));
    }

    [Fact]
    public void TheGateRefusalShowsElectronsText() =>
        Assert.Equal("Project path is not within the projects directory", UserMessage.From(new ProjectNotKnownException()));

    [Fact]
    public void TheStepAndImportFailuresShowElectronsText()
    {
        Assert.Equal("step s9 not found", UserMessage.From(new StepNotFoundException("s9")));
        Assert.Equal("Unsupported file " + (char)0x2014 + " please choose a PNG or JPEG image.", UserMessage.From(new UnsupportedImageException()));
        Assert.Equal("Package contains an unexpected file path: x/y", UserMessage.From(ImportRejectedException.UnexpectedPath("x/y")));
        Assert.Equal("Refusing to extract a path outside the project: x/y", UserMessage.From(ImportRejectedException.OutsideProject("x/y")));
    }

    /// <summary>Electron's texts, the lowercase first letters included.</summary>
    [Fact]
    public void TheArchiveFailuresShowElectronsText()
    {
        Assert.Equal(
            "archive verification failed (1 entries, expected 2) " + (char)0x2014 + " nothing deleted",
            UserMessage.From(ArchiveException.VerificationFailed(1, 2)));
        Assert.Equal("archive contains an unexpected path: notes.txt", UserMessage.From(ArchiveException.UnexpectedPath("notes.txt")));
        Assert.Equal("refusing to extract a path outside the project: shots/a.png", UserMessage.From(ArchiveException.OutsideProject("shots/a.png")));
    }

    [Fact]
    public void AVerificationFailureKeepsWhatCausedIt()
    {
        var cause = new InvalidDataException("bad zip");
        Assert.Same(cause, ArchiveException.VerificationFailed(0, 2, cause).InnerException);
    }

    /// <summary>New native text, for a refusal Electron could not make (D-22).</summary>
    [Fact]
    public void TheImportedImageRefusalNamesTheFile() =>
        Assert.Equal(
            "Refusing to write outside the project: shots/step-0001.png",
            UserMessage.From(ImportRejectedException.OutsideShots("step-0001.png")));

    /// <summary>The reason is for the log only (Q-MODEL-11).</summary>
    [Fact]
    public void ACorruptManifestShowsTheUserTextNotTheReason() =>
        Assert.Equal(
            "This project can't be opened because its project.json is missing or damaged.",
            UserMessage.From(new ManifestCorruptException("project.json is missing", new FileNotFoundException())));
}
