using System.Reflection;
using ShotAI.Core.Updates;
using Xunit;

namespace ShotAI.Core.Tests.Updates;

/// <summary>Spec 10 7.6.1: the version the banner, the User-Agent and About show (EDGE-INFRA-29).</summary>
public sealed class AppVersionTests
{
    /// <summary>Spec 10 8.5's <c>BuildMetadataStripped</c>.</summary>
    [Theory]
    [InlineData("2.0.0-alpha.0+abc", "2.0.0-alpha.0")]
    [InlineData("2.0.0+0123456789abcdef0123456789abcdef01234567", "2.0.0")]
    [InlineData("2.0.0", "2.0.0")]
    [InlineData("2.0.0-rc.1+a+b", "2.0.0-rc.1")]
    [InlineData("+abc", "")]
    [InlineData("", "")]
    public void BuildMetadataStripped(string informational, string display) =>
        Assert.Equal(display, AppVersion.FromInformational(informational).Display);

    /// <summary>Until the App sets it, the running version is Core's own, cut the same way.</summary>
    [Fact]
    public void CurrentDefaultsToCoresVersion()
    {
        var informational = typeof(AppVersion).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()!.InformationalVersion;
        Assert.Equal(AppVersion.FromInformational(informational), AppVersion.Current);
        Assert.DoesNotContain('+', AppVersion.Current.Display);
        Assert.StartsWith("2.", AppVersion.Current.Display, StringComparison.Ordinal);
    }

    [Fact]
    public void NullsAreRefused()
    {
        Assert.Throws<ArgumentNullException>(() => AppVersion.FromInformational(null!));
        Assert.Throws<ArgumentNullException>(() => AppVersion.Current = null!);
    }
}
