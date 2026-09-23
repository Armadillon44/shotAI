namespace ShotAI.Core.Store;

/// <summary>
/// The payload of <see cref="IProjectSession.PersistFailed"/>: the operation whose queued write
/// failed, so the report can recover a draft from it (spec 05 7.5, S4), and why.
/// </summary>
public sealed record PersistFailedEventArgs(ProjectOperation Operation, Exception Error);
