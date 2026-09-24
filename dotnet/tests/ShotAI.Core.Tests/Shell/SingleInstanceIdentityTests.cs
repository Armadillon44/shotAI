using ShotAI.Core.Shell;
using Xunit;

namespace ShotAI.Core.Tests.Shell;

/// <summary>Spec 03 8.3: the lock and activation names (D20). The SID is made up, never a real one.</summary>
public sealed class SingleInstanceIdentityTests
{
    private const string Sid = "S-1-5-21-1-2-3-1001";

    [Fact]
    public void NamesForASampleSid()
    {
        Assert.Equal("Local\\shotAI.SingleInstance.S-1-5-21-1-2-3-1001", SingleInstanceIdentity.MutexName(Sid));
        Assert.Equal("shotAI.Activation.S-1-5-21-1-2-3-1001", SingleInstanceIdentity.ActivationWindowName(Sid));
        Assert.Equal("shotAI.ActivateMainWindow", SingleInstanceIdentity.ActivationMessageName);
    }

    /// <summary>The session namespace, and its separator is the name's only backslash.</summary>
    [Fact]
    public void MutexIsInTheSessionNamespace()
    {
        var name = SingleInstanceIdentity.MutexName(Sid);
        Assert.StartsWith("Local\\", name, StringComparison.Ordinal);
        Assert.Equal(1, name.Count(c => c == '\\'));
        Assert.DoesNotContain('\\', SingleInstanceIdentity.ActivationWindowName(Sid));
    }

    [Fact]
    public void StableAcrossCalls()
    {
        Assert.Equal(SingleInstanceIdentity.MutexName(Sid), SingleInstanceIdentity.MutexName(Sid));
        Assert.Equal(SingleInstanceIdentity.ActivationWindowName(Sid), SingleInstanceIdentity.ActivationWindowName(Sid));
    }

    /// <summary>Another user's SID names another lock, so two users never block each other.</summary>
    [Fact]
    public void EachUserHasItsOwnNames()
    {
        Assert.NotEqual(SingleInstanceIdentity.MutexName(Sid), SingleInstanceIdentity.MutexName("S-1-5-21-1-2-3-1002"));
        Assert.NotEqual(SingleInstanceIdentity.ActivationWindowName(Sid), SingleInstanceIdentity.ActivationWindowName("S-1-5-21-1-2-3-1002"));
    }

    [Fact]
    public void EmptyAndNullAreRefused()
    {
        Assert.Throws<ArgumentNullException>("sid", () => SingleInstanceIdentity.MutexName(null!));
        Assert.Throws<ArgumentException>("sid", () => SingleInstanceIdentity.MutexName(""));
        Assert.Throws<ArgumentNullException>("sid", () => SingleInstanceIdentity.ActivationWindowName(null!));
        Assert.Throws<ArgumentException>("sid", () => SingleInstanceIdentity.ActivationWindowName(""));
    }

    /// <summary>A backslash would move the mutex into another namespace (<c>Local\a\b</c>).</summary>
    [Fact]
    public void BackslashIsRefused()
    {
        Assert.Throws<ArgumentException>("sid", () => SingleInstanceIdentity.MutexName("S-1\\x"));
        Assert.Throws<ArgumentException>("sid", () => SingleInstanceIdentity.ActivationWindowName("S-1\\x"));
    }
}
