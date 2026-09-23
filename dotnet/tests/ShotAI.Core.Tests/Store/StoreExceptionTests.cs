using ShotAI.Core.Errors;
using ShotAI.Core.Store;
using Xunit;

namespace ShotAI.Core.Tests.Store;

/// <summary>
/// The store's exceptions (spec 01 7.13, spec 11 X2): each derives from
/// <see cref="ShotAIException"/>, so <see cref="UserMessage.From"/> shows its text verbatim.
/// Each exception WP-A7 and WP-A8 add joins the first test without a change here.
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

    /// <summary>The reason is for the log only (Q-MODEL-11).</summary>
    [Fact]
    public void ACorruptManifestShowsTheUserTextNotTheReason() =>
        Assert.Equal(
            "This project can't be opened because its project.json is missing or damaged.",
            UserMessage.From(new ManifestCorruptException("project.json is missing", new FileNotFoundException())));
}
