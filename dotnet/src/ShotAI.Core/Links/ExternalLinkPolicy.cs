namespace ShotAI.Core.Links;

/// <summary>
/// The base allowlist of spec 11 2.5.1 step 4 (<c>src/main/ipc.ts:280-290</c>), pure:
/// <c>https</c> to <c>anthropic.com</c>, any subdomain of it, or exactly <c>github.com</c>
/// (INV-IPC-3, EDGE-IPC-8).
/// </summary>
public static class ExternalLinkPolicy
{
    /// <summary>
    /// True when <paramref name="u"/> is an absolute <c>https</c> URI whose host, lower-cased in
    /// its punycode form as WHATWG's hostname is, is <c>anthropic.com</c>, ends with
    /// <c>.anthropic.com</c>, or is <c>github.com</c>.
    /// </summary>
    /// <remarks>
    /// <c>github.com</c> is matched exactly, so the list never widens to its user-content hosts
    /// such as <c>raw.github.com</c> (#54). A trailing-dot host is refused, as in Electron; user
    /// info and a non-default port on an allowed host are allowed, as in Electron (Q-INFRA-21).
    /// </remarks>
    public static bool IsBaseAllowed(Uri u)
    {
        ArgumentNullException.ThrowIfNull(u);
        if (!u.IsAbsoluteUri || u.Scheme != Uri.UriSchemeHttps) return false;
        var host = u.IdnHost.ToLowerInvariant();
        return host == "anthropic.com" || host.EndsWith(".anthropic.com", StringComparison.Ordinal) || host == "github.com";
    }
}
