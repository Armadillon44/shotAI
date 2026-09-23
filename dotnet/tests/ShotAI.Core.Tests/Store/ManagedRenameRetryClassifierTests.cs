using ShotAI.Core.Store;
using Xunit;

namespace ShotAI.Core.Tests.Store;

/// <summary>The managed default of spec 01 7.6: denied is EACCES, busy is EBUSY, anything else is final.</summary>
public sealed class ManagedRenameRetryClassifierTests
{
    private static readonly ManagedRenameRetryClassifier Classifier = new();

    [Fact]
    public void ADeniedRenameIsEacces() => Assert.Equal("EACCES", Classifier.Classify(new UnauthorizedAccessException("denied")));

    [Fact]
    public void ABusyRenameIsEbusy()
    {
        Assert.Equal("EBUSY", Classifier.Classify(new IOException("x", 16)));
        Assert.Equal("EBUSY", Classifier.Classify(new IOException("Device or resource busy : '/p/project.json'")));
        Assert.Equal("EBUSY", Classifier.Classify(new IOException("Resource busy")));
    }

    [Fact]
    public void AnythingElseIsFinal()
    {
        Assert.Null(Classifier.Classify(new FileNotFoundException("gone")));
        Assert.Null(Classifier.Classify(new IOException("No space left on device", 28)));
        Assert.Null(Classifier.Classify(new InvalidOperationException("x")));
    }

    [Fact]
    public void NullIsAProgrammingError() => Assert.Throws<ArgumentNullException>(() => Classifier.Classify(null!));
}
