using System.Globalization;

namespace ShotAI.Core.Links;

/// <summary>
/// WHATWG's serialization of a URL's origin (spec 11 7.3.4), which the refusal log line carries
/// in place of the URL, so no path or query is ever logged (EDGE-INFRA-51).
/// </summary>
public static class UrlOrigin
{
    /// <summary>
    /// For <c>http</c>, <c>https</c>, <c>ws</c>, <c>wss</c> and <c>ftp</c>: the scheme,
    /// <c>://</c> and the lower-case punycode host, then <c>:</c> and the port when it is not the
    /// scheme's default. For every other scheme, or a relative URI: <c>null</c>, as WHATWG
    /// serializes an opaque origin.
    /// </summary>
    public static string Of(Uri u)
    {
        ArgumentNullException.ThrowIfNull(u);
        if (!u.IsAbsoluteUri) return "null";
        int? standardPort = u.Scheme switch
        {
            "http" or "ws" => 80,
            "https" or "wss" => 443,
            "ftp" => 21,
            _ => null,
        };
        if (standardPort is not { } standard) return "null";
        // An IPv6 host keeps its brackets, as WHATWG writes it; IdnHost gives the bare address.
        var host = u.HostNameType == UriHostNameType.IPv6 ? u.Host : u.IdnHost;
        var origin = u.Scheme + "://" + host.ToLowerInvariant();
        return u.Port == standard || u.Port < 0 ? origin : origin + ":" + u.Port.ToString(CultureInfo.InvariantCulture);
    }
}
