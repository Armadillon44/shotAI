namespace ShotAI.Core.Auth;

/// <summary>
/// <see cref="ISupportUrlAllowlist"/> for a build with no federation configuration yet: like an
/// unconfigured machine (spec 08 INV-AUTH-8), it has no SupportUrl, so it admits nothing and the
/// base allowlist is the whole list. WP-D4's <c>SupportUrlAllowlist</c>, which reads the
/// policy's SupportUrl, replaces it.
/// </summary>
internal sealed class NoFederationSupportUrlAllowlist : ISupportUrlAllowlist
{
    /// <inheritdoc/>
    public Task<bool> IsAllowedAsync(Uri candidate, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(candidate);
        return Task.FromResult(false);
    }
}
