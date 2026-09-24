using Microsoft.Extensions.Logging;
using ShotAI.Core.Auth;

namespace ShotAI.Core.Links;

/// <summary>
/// <see cref="IExternalLinks"/>, exactly spec 11 2.5.1 (<c>src/main/ipc.ts:268-310</c>): the base
/// allowlist, then, for an <c>https</c> URL it refuses, the exact origin of the administrator's
/// SupportUrl (08 7.13); anything else is refused and logged by origin only (INV-IPC-3,
/// INV-INFRA-29). Registered by spec 10 (Q-IPC-14).
/// </summary>
public sealed partial class ExternalLinks : IExternalLinks
{
    private readonly ISupportUrlAllowlist _supportUrls;
    private readonly IUrlLauncher _launcher;
    private readonly ILogger<ExternalLinks> _log;

    /// <summary>A link service over the SupportUrl extension and the shell's launcher.</summary>
    public ExternalLinks(ISupportUrlAllowlist supportUrls, IUrlLauncher launcher, ILogger<ExternalLinks> log)
    {
        ArgumentNullException.ThrowIfNull(supportUrls);
        ArgumentNullException.ThrowIfNull(launcher);
        ArgumentNullException.ThrowIfNull(log);
        _supportUrls = supportUrls;
        _launcher = launcher;
        _log = log;
    }

    /// <inheritdoc/>
    public async Task<bool> OpenAsync(string url, CancellationToken ct = default)
    {
        if (url is null || !Uri.TryCreate(url, UriKind.Absolute, out var u)) return false;
        var allowed = ExternalLinkPolicy.IsBaseAllowed(u)
            || (u.Scheme == Uri.UriSchemeHttps && await _supportUrls.IsAllowedAsync(u, ct).ConfigureAwait(false));
        if (!allowed)
        {
            Refused(_log, UrlOrigin.Of(u));
            return false;
        }
        // The normalized URL, as Electron passes parsed.toString(), and nothing added to it.
        await _launcher.LaunchAsync(u.AbsoluteUri).ConfigureAwait(false);
        return true;
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "refused openExternal for non-allowlisted URL: {Origin}")]
    private static partial void Refused(ILogger logger, string origin);
}
