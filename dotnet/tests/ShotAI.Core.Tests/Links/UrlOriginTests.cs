using ShotAI.Core.Links;
using Xunit;

namespace ShotAI.Core.Tests.Links;

/// <summary>Spec 11 7.3.4 and 8.2: WHATWG's origin serialization, which the refusal line logs (EDGE-INFRA-51).</summary>
public sealed class UrlOriginTests
{
    [Theory]
    [InlineData("https://a.b/c", "https://a.b")]
    [InlineData("https://a.b:443/c", "https://a.b")]
    [InlineData("http://a.b:80/", "http://a.b")]
    [InlineData("https://a.b:8443/", "https://a.b:8443")]
    [InlineData("http://a.b:8080/x", "http://a.b:8080")]
    [InlineData("https://A.B/", "https://a.b")]
    [InlineData("https://user:pw@a.b/", "https://a.b")]
    [InlineData("ws://a.b/", "ws://a.b")]
    [InlineData("ws://a.b:81/", "ws://a.b:81")]
    [InlineData("wss://a.b:443/", "wss://a.b")]
    [InlineData("wss://a.b:8443/", "wss://a.b:8443")]
    [InlineData("ftp://a.b/", "ftp://a.b")]
    [InlineData("ftp://a.b:2121/", "ftp://a.b:2121")]
    [InlineData("https://[::1]:8443/", "https://[::1]:8443")]
    public void WebSchemesGiveSchemeHostAndPort(string url, string origin) => Assert.Equal(origin, UrlOrigin.Of(new Uri(url)));

    /// <summary>A Unicode host gives its punycode form, as WHATWG's host does.</summary>
    [Fact]
    public void AnIdnHostGivesPunycode() => Assert.Equal("https://xn--bcher-kva.example", UrlOrigin.Of(new Uri("https://b\u00FCcher.example/")));

    /// <summary>Every other scheme has an opaque origin, which serializes as <c>null</c>.</summary>
    [Theory]
    [InlineData("mailto:x@y")]
    [InlineData("javascript:alert(1)")]
    [InlineData("file:///C:/x")]
    [InlineData("data:text/plain,x")]
    [InlineData("urn:isbn:0451450523")]
    public void UrlOriginOfNonWebSchemeIsNull(string url) => Assert.Equal("null", UrlOrigin.Of(new Uri(url)));

    [Fact]
    public void ARelativeUriIsNull() => Assert.Equal("null", UrlOrigin.Of(new Uri("/x", UriKind.Relative)));

    [Fact]
    public void ArgumentsAreChecked() => Assert.Throws<ArgumentNullException>(() => UrlOrigin.Of(null!));
}
