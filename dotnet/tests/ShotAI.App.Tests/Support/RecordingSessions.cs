using ShotAI.Core.Model;
using ShotAI.Core.Store;

namespace ShotAI.App.Tests.Support;

/// <summary>
/// A session factory over another whose sessions record every operation handed to
/// <see cref="IProjectSession.Apply"/> before passing it on, so a test sees exactly what reached
/// the session (spec 11 BrandChoicePassThroughTests).
/// </summary>
internal sealed class RecordingSessions(IProjectSessionFactory inner) : IProjectSessionFactory
{
    /// <summary>Every operation applied through any session made, in order.</summary>
    public List<ProjectOperation> Applied { get; } = [];

    public IProjectSession Create(OpenedProject opened) => new RecordingSession(inner.Create(opened), Applied);

    private sealed class RecordingSession(IProjectSession inner, List<ProjectOperation> applied) : IProjectSession
    {
        public string ProjectDir => inner.ProjectDir;

        public ProjectManifest Current => inner.Current;

        public int PendingCount => inner.PendingCount;

        // The events are the inner session's, raised with it as the sender; forwarding keeps the sender this wrapper, which is what the view holds.
        private readonly Dictionary<EventHandler<ManifestChangedEventArgs>, EventHandler<ManifestChangedEventArgs>> _changed = [];
        private readonly Dictionary<EventHandler<PersistFailedEventArgs>, EventHandler<PersistFailedEventArgs>> _failed = [];

        public event EventHandler<ManifestChangedEventArgs>? Changed
        {
            add
            {
                if (value is null) return;
                EventHandler<ManifestChangedEventArgs> forward = (_, e) => value(this, e);
                _changed[value] = forward;
                inner.Changed += forward;
            }
            remove
            {
                if (value is not null && _changed.Remove(value, out var forward)) inner.Changed -= forward;
            }
        }

        public event EventHandler<PersistFailedEventArgs>? PersistFailed
        {
            add
            {
                if (value is null) return;
                EventHandler<PersistFailedEventArgs> forward = (_, e) => value(this, e);
                _failed[value] = forward;
                inner.PersistFailed += forward;
            }
            remove
            {
                if (value is not null && _failed.Remove(value, out var forward)) inner.PersistFailed -= forward;
            }
        }

        public Task<ProjectManifest> Apply(ProjectOperation op)
        {
            applied.Add(op);
            return inner.Apply(op);
        }

        public Task<ProjectManifest> ApplyDurable(Func<IProjectService, Task<ProjectManifest>> call) => inner.ApplyDurable(call);

        public Task WhenIdleAsync(CancellationToken ct = default) => inner.WhenIdleAsync(ct);

        public ValueTask DisposeAsync() => inner.DisposeAsync();
    }
}
