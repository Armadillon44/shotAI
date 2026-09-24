namespace ShotAI.Core.Links;

/// <summary>
/// I2 (spec 11 7.3.4, 10 7.7): the only way a URL leaves the app for the browser, behind the
/// allowlist of 11 2.5.1 (INV-IPC-3, ARCHITECTURE S15). Electron's comment on the handler holds:
/// this is the only egress to a browser, so it stays fail-closed.
/// </summary>
public interface IExternalLinks
{
    /// <summary>
    /// Hands <paramref name="url"/> to the shell if the allowlist admits it; a refusal is logged
    /// by origin only, and an unparseable URL is refused without a log line.
    /// </summary>
    /// <param name="url">The URL; null is refused, as Electron refuses a value that is not a string.</param>
    /// <param name="ct">Passed to the SupportUrl check.</param>
    /// <returns>True when the URL was handed to the shell; false when it was refused.</returns>
    /// <remarks>
    /// It throws only what the launcher throws (R-ARCH-25, parity with Electron's awaited
    /// <c>shell.openExternal</c>), so a caller such as spec 06's wraps the call and shows nothing.
    /// </remarks>
    Task<bool> OpenAsync(string url, CancellationToken ct = default);
}
