namespace ShotAI.Core.Auth;

/// <summary>
/// The Request access extension of the link allowlist (spec 08 7.13, 11 2.5.1 step 5): the
/// exact origin of the administrator's SupportUrl, consulted only while federation is
/// configured (INV-AUTH-32). <see cref="Links.ExternalLinks"/> asks it only for an <c>https</c>
/// URL the base allowlist refused.
/// </summary>
public interface ISupportUrlAllowlist
{
    /// <summary>
    /// True when <paramref name="candidate"/> has the configured SupportUrl's origin (scheme, host
    /// and effective port); false with no configuration, and a malformed configured value never
    /// widens the list.
    /// </summary>
    Task<bool> IsAllowedAsync(Uri candidate, CancellationToken ct);
}
