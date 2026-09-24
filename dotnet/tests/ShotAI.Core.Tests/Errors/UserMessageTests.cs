using ShotAI.Core.Errors;
using Xunit;

namespace ShotAI.Core.Tests.Errors;

/// <summary><see cref="UserMessage.From"/> (spec 11 7.9, ARCHITECTURE 8.2).</summary>
public sealed class UserMessageTests
{
    [Fact]
    public void ShotAIExceptionShowsMessage()
    {
        // Verbatim, including the em dash the Electron strings carry.
        const string text = "A capture failed \u2014 see the log for details.";
        Assert.Equal(text, UserMessage.From(new ShotAIException(text)));
        Assert.Equal("cannot merge a step into itself", UserMessage.From(new DerivedException("cannot merge a step into itself")));
    }

    [Fact]
    public void EmptyMessageShowsGeneric()
    {
        Assert.Equal(UserMessage.Generic, UserMessage.From(new ShotAIException("")));
        Assert.Equal(UserMessage.Generic, UserMessage.From(new ShotAIException("   ")));
    }

    [Fact]
    public void CanceledShowsNothing()
    {
        Assert.Null(UserMessage.From(new OperationCanceledException()));
        Assert.Null(UserMessage.From(new TaskCanceledException()));
        Assert.Null(UserMessage.From(new AggregateException(new TaskCanceledException())));
    }

    [Fact]
    public void SingleInnerAggregateUnwraps()
    {
        Assert.Equal("inner text", UserMessage.From(new AggregateException(new ShotAIException("inner text"))));
        Assert.Equal(
            "inner text",
            UserMessage.From(new AggregateException(new AggregateException(new ShotAIException("inner text")))));
        // Two inner exceptions are not one failure to report: the generic sentence.
        Assert.Equal(
            UserMessage.Generic,
            UserMessage.From(new AggregateException(new ShotAIException("a"), new ShotAIException("b"))));
    }

    [Fact]
    public void IOExceptionShowsOsMessage()
    {
        const string os = "The process cannot access the file because it is being used by another process.";
        Assert.Equal(os, UserMessage.From(new IOException(os)));
        Assert.Equal("missing.png", UserMessage.From(new FileNotFoundException("missing.png")));
    }

    [Fact]
    public void UnauthorizedAccessShowsMessage()
    {
        const string os = "Access to the path is denied.";
        Assert.Equal(os, UserMessage.From(new UnauthorizedAccessException(os)));
    }

    [Fact]
    public void ArgumentExceptionShowsGeneric() =>
        Assert.Equal(UserMessage.Generic, UserMessage.From(new ArgumentException("internal detail")));

    [Fact]
    public void NullReferenceShowsGeneric() =>
        Assert.Equal(UserMessage.Generic, UserMessage.From(new NullReferenceException("internal detail")));

    [Fact]
    public void GenericTextIsExact() =>
        Assert.Equal("Something went wrong. See the log for details.", UserMessage.Generic);

    /// <summary>Unexpected is exactly "the generic sentence stands in for a message that is not user text".</summary>
    [Fact]
    public void UnexpectedIsTheGenericCaseOnly()
    {
        Assert.True(UserMessage.IsUnexpected(new ArgumentException("internal detail")));
        Assert.True(UserMessage.IsUnexpected(new NullReferenceException()));
        Assert.True(UserMessage.IsUnexpected(new AggregateException(new ShotAIException("a"), new ShotAIException("b"))));
        Assert.True(UserMessage.IsUnexpected(new AggregateException(new InvalidOperationException())));

        Assert.False(UserMessage.IsUnexpected(new ShotAIException("text")));
        Assert.False(UserMessage.IsUnexpected(new ShotAIException("")));
        Assert.False(UserMessage.IsUnexpected(new IOException("os")));
        Assert.False(UserMessage.IsUnexpected(new UnauthorizedAccessException("os")));
        Assert.False(UserMessage.IsUnexpected(new OperationCanceledException()));
        Assert.False(UserMessage.IsUnexpected(new AggregateException(new TaskCanceledException())));
        Assert.False(UserMessage.IsUnexpected(new AggregateException(new ShotAIException("inner"))));
    }

    private sealed class DerivedException(string message) : ShotAIException(message);
}
