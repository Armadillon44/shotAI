using Microsoft.Extensions.Logging;

namespace ShotAI.Core.Logging;

/// <summary>
/// The boundary trace of spec 11 7.11 L2: one Debug line when a catalog method that replaces an
/// Electron invoke or send channel is entered.
/// </summary>
/// <remarks>
/// Source-generated, so the line costs nothing when Debug is off (Q-IPC-11: kept at Debug). The
/// logger is one of category <see cref="LogCategories.Svc"/>. Snapshot readers (<c>GetState</c>,
/// <c>Current</c>, <c>Pending</c>) do not call it, and no argument is ever logged (INV-IPC-18).
/// </remarks>
public static partial class ServiceLog
{
    /// <summary>Writes <c>call: {service}.{member}</c>, for example <c>call: IProjectService.CreateProjectAsync</c>.</summary>
    /// <param name="logger">A logger of category <see cref="LogCategories.Svc"/>.</param>
    /// <param name="service">The interface name, such as <c>IProjectService</c>.</param>
    /// <param name="member">The member name, such as <c>CreateProjectAsync</c>.</param>
    [LoggerMessage(Level = LogLevel.Debug, Message = "call: {Service}.{Member}")]
    public static partial void Call(ILogger logger, string service, string member);
}
