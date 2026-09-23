using Microsoft.Extensions.Logging;

namespace ShotAI.Core.Store;

/// <summary>
/// Creates <see cref="IProjectSession"/>s and finds them by path for <see cref="IProjectSettle"/>
/// (spec 01 7.10); one instance is registered as both (7.14).
/// </summary>
/// <remarks>
/// The only Core file that reads <see cref="SynchronizationContext.Current"/> (ARCHITECTURE
/// 6.3, 14.9). A session leaves the registry only when the drain after its
/// <c>DisposeAsync</c> finishes, so a settle started right after Back still waits for the
/// writes it accepted (S9, R-ARCH-27).
/// </remarks>
public sealed class ProjectSessionFactory(IProjectService store, ILogger<ProjectSessionFactory> log) : IProjectSessionFactory, IProjectSettle
{
    private readonly IProjectService _store = store ?? throw new ArgumentNullException(nameof(store));
    private readonly ILogger<ProjectSessionFactory> _log = log ?? throw new ArgumentNullException(nameof(log));
    private readonly object _gate = new();
    private readonly List<ProjectSession> _registered = [];

    /// <summary>How many sessions are registered: open ones and disposed ones still draining.</summary>
    internal int RegisteredCount
    {
        get
        {
            lock (_gate) return _registered.Count;
        }
    }

    /// <inheritdoc/>
    public IProjectSession Create(OpenedProject opened)
    {
        ArgumentNullException.ThrowIfNull(opened);
        var context = SynchronizationContext.Current
            ?? throw new InvalidOperationException("A project session must be created on the UI thread, where a SynchronizationContext is current.");
        var session = new ProjectSession(opened, _store, context, _log, Unregister);
        lock (_gate) _registered.Add(session);
        return session;
    }

    /// <inheritdoc/>
    public Task WhenSettledAsync(string projectPath, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(projectPath);
        var key = ProjectSession.KeyOf(projectPath);
        ProjectSession[] sessions;
        lock (_gate) sessions = [.. _registered.Where(s => string.Equals(s.Key, key, StringComparison.OrdinalIgnoreCase))];
        return sessions.Length == 0 ? Task.CompletedTask : Task.WhenAll(sessions.Select(s => s.WhenIdleAsync(ct)));
    }

    private void Unregister(ProjectSession session)
    {
        lock (_gate) _registered.Remove(session);
    }
}
