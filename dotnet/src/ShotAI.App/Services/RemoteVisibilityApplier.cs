using Microsoft.Extensions.Logging;
using ShotAI.Core.Capture;
using ShotAI.Core.Settings;

namespace ShotAI.App.Services;

/// <summary>
/// Applies a change of remote visibility to the open windows as soon as the value the user sees
/// changes: the optimistic change and its rollback alike, so the windows always match the
/// switch (spec 11 7.3.6, INV-IPC-13, D-IPC-8, EDGE-IPC-10).
/// </summary>
/// <remarks>
/// No UI-thread code may take the shield's lock (ARCHITECTURE DL1, spec 02 7.8, INV-CAP-29), and
/// <see cref="ISettingsService.Changed"/> is often raised on the UI thread, so each apply runs on
/// the pool, never through <c>IUiDispatcher.Post</c>. The applies are serialized latest-wins: a
/// change marks the applier dirty and, when no apply is running, starts one pool loop that applies
/// <see cref="ISettingsService.Current"/> as it is then, and goes again while a further change came
/// in meanwhile, so quick toggles never end on an older value. It never applies at startup:
/// startup step 10 does, after the windows were created excluded (ARCHITECTURE 4.2).
/// </remarks>
public sealed partial class RemoteVisibilityApplier : IAppStartup, IDisposable
{
    private readonly Lock _gate = new();
    private readonly ISettingsService _settings;
    private readonly CaptureShield _shield;
    private readonly ILogger<RemoteVisibilityApplier> _log;
    private bool _dirty;
    private bool _running;
    private Task _loop = Task.CompletedTask;

    public RemoteVisibilityApplier(ISettingsService settings, CaptureShield shield, ILogger<RemoteVisibilityApplier> log)
    {
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(shield);
        ArgumentNullException.ThrowIfNull(log);
        _settings = settings;
        _shield = shield;
        _log = log;
    }

    /// <summary>The apply loop running now, or the last one, for the tests.</summary>
    internal Task LoopForTest
    {
        get
        {
            lock (_gate) return _loop;
        }
    }

    /// <inheritdoc/>
    public void Start() => _settings.Changed += OnChanged;

    /// <summary>Stops following the setting; idempotent.</summary>
    public void Dispose() => _settings.Changed -= OnChanged;

    private void OnChanged(object? sender, SettingsChangedEventArgs e)
    {
        if (e.Previous.RemoteVisible == e.Current.RemoteVisible) return;
        lock (_gate)
        {
            _dirty = true;
            if (_running) return;
            _running = true;
            _loop = Task.Run(ApplyLoop);
        }
    }

    // One apply per round, of the value current when the round starts; a failure is logged and the
    // loop goes on, so the task never faults.
    private void ApplyLoop()
    {
        while (true)
        {
            lock (_gate)
            {
                if (!_dirty)
                {
                    _running = false;
                    return;
                }
                _dirty = false;
            }
            try
            {
                _shield.ApplyRemoteVisibility(_settings.Current.RemoteVisible);
            }
            catch (Exception ex)
            {
                ApplyFailed(_log, ex);
            }
        }
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "remote visibility could not be applied to the windows:")]
    private static partial void ApplyFailed(ILogger logger, Exception exception);
}
