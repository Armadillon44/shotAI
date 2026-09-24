namespace ShotAI.Core.Capture;

/// <summary>
/// The remote-visibility shield (spec 02 2.7.3 and 7.8, <c>src/main/remote-visibility.ts</c>).
/// With remote visibility on, shotAI's windows may be captured by a screen share between grabs;
/// every grab takes the shield, which excludes them from capture for exactly the read and then
/// restores what the setting says. Screen capture cannot tell shotAI's grab from a remote
/// viewer's, so the only way to be both remotely visible and absent from the screenshots is to
/// do it in time.
/// </summary>
/// <remarks>
/// Reference counted, because grabs overlap: the menu poll grabs on a timer outside the capture
/// queue. Protection is applied once at depth 0 and restored only when the count returns to 0,
/// and a release is idempotent (INV-CAP-2). The value to restore is latched from the setting,
/// read synchronously, when the first shield is taken; a change of the setting while a shield
/// is held waits for the final release (INV-CAP-3, INV-CAP-5). The protection is changed under
/// the lock on purpose, so two overlapping takes never interleave a relax after an exclude. No
/// UI-thread code may take the lock (INV-CAP-29): the setting reaches
/// <see cref="ApplyRemoteVisibility"/> through the thread pool.
/// </remarks>
public sealed class CaptureShield
{
    private readonly object _lock = new();
    private readonly IWindowProtection _protection;
    private readonly ICaptureSettings _settings;
    private int _depth;
    private bool _restore = true;

    /// <summary>A shield over the own windows' protection and the settings' synchronous cache.</summary>
    public CaptureShield(IWindowProtection protection, ICaptureSettings settings)
    {
        ArgumentNullException.ThrowIfNull(protection);
        ArgumentNullException.ThrowIfNull(settings);
        _protection = protection;
        _settings = settings;
    }

    /// <summary>
    /// Applies the setting to every own window at start-up and after a change: visible lets
    /// them be captured. While a shield is held it only changes what the last release restores,
    /// so a toggle during a grab never exposes a window mid-capture.
    /// </summary>
    public void ApplyRemoteVisibility(bool visible)
    {
        lock (_lock)
        {
            _restore = !visible;
            if (_depth > 0) return;
            _protection.SetAllExcluded(!visible);
        }
    }

    /// <summary>
    /// Excludes every own window for one grab. Dispose the result in a <c>finally</c>, or with
    /// <c>using</c>, so a grab that throws still restores (INV-CAP-4).
    /// </summary>
    public Releaser Take()
    {
        lock (_lock)
        {
            if (_depth == 0)
            {
                _restore = !_settings.RemoteVisibleNow();
                _protection.SetAllExcluded(true);
            }
            _depth++;
        }
        return new Releaser(this);
    }

    /// <summary>
    /// Whether a window registered now must start excluded: while a shield is held, or while the
    /// setting keeps windows protected (INV-CAP-7).
    /// </summary>
    public bool ExcludedForNewWindow
    {
        get
        {
            lock (_lock) return _depth > 0 || _restore;
        }
    }

    /// <summary>The shields held, for the tests.</summary>
    internal int DepthForTest
    {
        get
        {
            lock (_lock) return _depth;
        }
    }

    private void Release()
    {
        lock (_lock)
        {
            if (--_depth == 0) _protection.SetAllExcluded(_restore);
        }
    }

    /// <summary>One held shield; the first <see cref="Dispose"/> releases it, and any later one does nothing.</summary>
    public sealed class Releaser : IDisposable
    {
        private readonly CaptureShield _owner;
        private int _released;

        internal Releaser(CaptureShield owner) => _owner = owner;

        /// <summary>Releases the shield once; a second call cannot drop a count another grab holds.</summary>
        public void Dispose()
        {
            if (Interlocked.Exchange(ref _released, 1) == 0) _owner.Release();
        }
    }
}
