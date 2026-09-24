using System.Globalization;
using ShotAI.Core.Json;
using ShotAI.Core.Model;
using ShotAI.Core.Store;

namespace ShotAI.Core.Capture;

public sealed partial class CaptureEngine
{
    private const string ShotsFolder = "shots";

    // DEFAULT_TARGET (CaptureController.ts:47).
    private static readonly CaptureTarget DefaultTarget = new("auto");

    // Set while a screenshot is between its reservation and its end (D21).
    private bool _screenshotPending;

    /// <inheritdoc/>
    /// <remarks>
    /// The reservation is atomic (D8): a second call for the same project while the first is
    /// starting waits for it and returns its state; for another project it throws. Steps 2 to 7
    /// of 2.2.2 run before the session is installed, and a hook that cannot be installed ends
    /// the start with no session (D7).
    /// </remarks>
    public async Task<CaptureState> StartAsync(string projectPath, CaptureStartOptions options, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(projectPath);
        ArgumentNullException.ThrowIfNull(options);
        ct.ThrowIfCancellationRequested();
        TaskCompletionSource<CaptureState>? started = null;
        Task<CaptureState>? joined = null;
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_tornDown, this);
            if (_screenshotPending || _session is { Kind: SessionKind.Screenshot }) throw new CaptureException(CaptureMessages.RecordingInProgress);
            if (_session is { } live)
            {
                if (SamePath(live.ProjectPath, projectPath)) return StateOf(live);
                throw new CaptureException(CaptureMessages.OtherProjectRecording);
            }
            if (_starting is { } pending)
            {
                if (!SamePath(pending.ProjectPath, projectPath)) throw new CaptureException(CaptureMessages.OtherProjectRecording);
                joined = pending.Started;
            }
            else
            {
                started = new TaskCompletionSource<CaptureState>(TaskCreationOptions.RunContinuationsAsynchronously);
                _starting = new StartReservation(projectPath, started.Task);
            }
        }
        if (joined is not null) return await joined.ConfigureAwait(false);

        try
        {
            var state = await StartSessionAsync(projectPath, options).ConfigureAwait(false);
            started!.TrySetResult(state);
            return state;
        }
        catch (Exception e)
        {
            started!.TrySetException(e);
            // Observed here: the caller gets the exception itself, and a waiter, if any, rethrows it.
            _ = started.Task.Exception;
            throw;
        }
        finally
        {
            lock (_gate) _starting = null;
        }
    }

    /// <inheritdoc/>
    public CaptureState Pause() => SetPaused(true);

    /// <inheritdoc/>
    public CaptureState Resume() => SetPaused(false);

    /// <inheritdoc/>
    /// <remarks>
    /// The triggers are detached first, then the captures already queued run to completion
    /// against the live session, then the session ends (INV-CAP-12). During a screenshot it
    /// returns the idle state and touches nothing (D21).
    /// </remarks>
    public async Task<CaptureState> StopAsync()
    {
        var s = await EndSessionAsync().ConfigureAwait(false);
        if (s is { Screenshot: true }) return CaptureState.Idle;
        RecordingStopped(_log, s.Counter, s.Committed);
        if (s.Session is not null) Raise(RecordingChanged, new RecordingChangedEventArgs(false, false), nameof(RecordingChanged));
        var state = GetState();
        Raise(StateChanged, state, nameof(StateChanged));
        return state;
    }

    /// <inheritdoc/>
    /// <remarks>
    /// Ends the session as <see cref="StopAsync"/> does, then deletes exactly the steps this
    /// session added, or the whole project when it was made for this recording and started
    /// empty. A cleanup failure is logged and swallowed; a failed project delete reports false.
    /// </remarks>
    public async Task<DiscardResult> DiscardAsync()
    {
        var ended = await EndSessionAsync().ConfigureAwait(false);
        if (ended is { Screenshot: true }) return new DiscardResult(CaptureState.Idle, false);
        var deleted = false;
        if (ended.Session is { } s)
        {
            try
            {
                if (ended.DeletesProject)
                {
                    await _projects.DeleteProjectAsync(s.ProjectPath).ConfigureAwait(false);
                    deleted = true;
                    DiscardDeletedProject(_log, s.ProjectPath);
                }
                else if (ended.AddedStepIds.Count > 0)
                {
                    await _projects.DeleteStepsAsync(s.ProjectPath, ended.AddedStepIds).ConfigureAwait(false);
                    DiscardRemovedSteps(_log, ended.AddedStepIds.Count, s.ProjectPath);
                }
                else
                {
                    DiscardNothing(_log);
                }
            }
            catch (Exception e)
            {
                DiscardCleanupFailed(_log, e);
            }
            Raise(RecordingChanged, new RecordingChangedEventArgs(false, false), nameof(RecordingChanged));
        }
        var state = GetState();
        Raise(StateChanged, state, nameof(StateChanged));
        return new DiscardResult(state, deleted);
    }

    /// <inheritdoc/>
    /// <remarks>
    /// The target is checked before anything is hidden (EDGE-CAP-16). The step goes in at the
    /// fixed index with no <c>StepLanded</c>; the only state event is the idle one at the end,
    /// raised on success and on failure once the target has been checked (INV-CAP-31,
    /// INV-IPC-25). The capture runs outside the queue, so its failures are thrown (EDGE-CAP-58).
    /// </remarks>
    public async Task<ProjectManifest> CaptureScreenshotAsync(string projectPath, CaptureTarget? target, int insertAt, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(projectPath);
        if (target is null || target.Mode == "auto") throw new CaptureException(CaptureMessages.ExplicitTargetRequired);
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_tornDown, this);
            if (_session is not null || _starting is not null || _screenshotPending) throw new CaptureException(CaptureMessages.RecordingInProgress);
            _screenshotPending = true;
        }
        try
        {
            var opened = await _projects.OpenProjectAsync(projectPath).ConfigureAwait(false);
            var shots = PrepareShotsFolder(opened.Dir);
            ValidateScreenshotTarget(target);
            var count = opened.Manifest.Steps.Count;
            var counter = ShotNaming.Seed(count, EntryNames(shots));
            var at = Math.Clamp(insertAt, 0, count);
            CaptureSession session;
            lock (_gate)
            {
                ObjectDisposedException.ThrowIf(_tornDown, this);
                session = new CaptureSession(SessionKind.Screenshot, projectPath, opened.Dir, opened.Manifest.Title, target, count, counter, false, null, ++_generation);
                _session = session;
            }
            ScreenshotArmed(_log, target.Mode, at, opened.Manifest.Title);
            Raise(RecordingChanged, new RecordingChangedEventArgs(true, false), nameof(RecordingChanged));
            try
            {
                await _clock.DelayAsync(CaptureConstants.HideSettleMs, ct).ConfigureAwait(false);
                var job = new CaptureJob(StepTrigger.Hotkey, null, MouseButton.Left, at, null, Broadcast: false, SkipOwnWindowGuard: true, session.Generation);
                if (await CaptureStepAsync(job).ConfigureAwait(false) is null) throw new CaptureException(CaptureMessages.ScreenNotCaptured);
            }
            finally
            {
                lock (_gate)
                {
                    if (ReferenceEquals(_session, session))
                    {
                        _session = null;
                        _generation++;
                    }
                }
                Raise(RecordingChanged, new RecordingChangedEventArgs(false, false), nameof(RecordingChanged));
                RaiseState();
            }
        }
        finally
        {
            lock (_gate) _screenshotPending = false;
        }
        return (await _projects.OpenProjectAsync(projectPath).ConfigureAwait(false)).Manifest;
    }

    /// <inheritdoc/>
    /// <remarks>
    /// The windows skip shotAI's own, minimized ones and those with a blank title, keep the
    /// topmost of each process and title, and carry the title trimmed; a monitor without a
    /// name is <c>Display &lt;id&gt;</c> (2.10.2). Runs on the thread pool.
    /// </remarks>
    public Task<CaptureTargets> ListTargetsAsync(CancellationToken ct = default) => Task.Run(ListTargets, ct);

    private async Task<CaptureState> StartSessionAsync(string projectPath, CaptureStartOptions options)
    {
        var opened = await _projects.OpenProjectAsync(projectPath).ConfigureAwait(false);
        var shots = PrepareShotsFolder(opened.Dir);
        var manifest = opened.Manifest;
        var count = manifest.Steps.Count;
        var counter = ShotNaming.Seed(count, EntryNames(shots));
        int? cursor = options.InsertAt is { } insertAt ? Math.Clamp(insertAt, 0, count) : null;
        var target = options.Target ?? DefaultTarget;
        CaptureSession session;
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_tornDown, this);
            session = new CaptureSession(SessionKind.Recording, projectPath, opened.Dir, manifest.Title, target, count, counter, options.CreatedThisSession, cursor, ++_generation);
            _session = session;
            _lastGrabFailed = false;
        }
        try
        {
            var insert = cursor is { } c ? " [insert@" + c.ToString(CultureInfo.InvariantCulture) + "]" : "";
            RecordingStarted(_log, manifest.Title, target.Mode, insert, count, counter + 1, projectPath);
            if (MissingTargetPart(target) is { } missing) IncompleteTarget(_log, target.Mode, missing);
            _elements.WarmUp();
            if (options.AttachTriggers) _triggers.Attach(OnMouseDown, OnHotkey);
        }
        catch (Exception e)
        {
            lock (_gate)
            {
                if (ReferenceEquals(_session, session))
                {
                    _session = null;
                    _generation++;
                }
            }
            if (e is not TriggerException te) throw;
            HookFailed(_log, te, te.Win32Error);
            throw new CaptureException(CaptureMessages.ClickListenerFailed, te);
        }
        Raise(RecordingChanged, new RecordingChangedEventArgs(true, true), nameof(RecordingChanged));
        var state = GetState();
        Raise(StateChanged, state, nameof(StateChanged));
        return state;
    }

    // Stop and discard: detach, drain with the session still set, then end it.
    private async Task<EndedSession> EndSessionAsync()
    {
        CaptureSession? s;
        lock (_gate)
        {
            if (_screenshotPending || _session is { Kind: SessionKind.Screenshot }) return EndedSession.DuringScreenshot;
            s = _session;
            _stopping++;
        }
        try
        {
            _triggers.Detach();
            await DrainAsync().ConfigureAwait(false);
        }
        finally
        {
            lock (_gate) _stopping--;
        }
        lock (_gate)
        {
            if (s is null) return EndedSession.None;
            if (ReferenceEquals(_session, s))
            {
                _session = null;
                _generation++;
            }
            return new EndedSession(s, false, s.Counter, s.Committed, s.DiscardDeletesProject, [.. s.AddedStepIds]);
        }
    }

    private CaptureState SetPaused(bool paused)
    {
        CaptureState state;
        lock (_gate)
        {
            if (_screenshotPending || _session is { Kind: SessionKind.Screenshot }) return CaptureState.Idle;
            if (_session is { } s) s.Paused = paused;
            state = StateOf(_session);
        }
        if (paused) RecordingPaused(_log);
        else RecordingResumed(_log);
        Raise(StateChanged, state, nameof(StateChanged));
        return state;
    }

    // The dispatcher's mousedown (2.3, 7.11): the own-window gate first (D1), then the element
    // query (D2) and a plain capture. WP-B3 puts the double-click collapse and the menu arm
    // between the two.
    private void OnMouseDown(MouseDown e)
    {
        int generation;
        lock (_gate)
        {
            if (_session is not { Kind: SessionKind.Recording, Paused: false } s || _stopping > 0 || _tornDown) return;
            generation = s.Generation;
        }
        if (_own.PointHitsOwnWindow(e.X, e.Y)) return;
        var element = _elements.ElementAtAsync(e.X, e.Y);
        Enqueue(new CaptureJob(StepTrigger.Click, (e.X, e.Y), e.Button, null, element, Broadcast: true, SkipOwnWindowGuard: false, generation));
    }

    // The hotkey (2.5): a capture of the foreground window, with no point.
    private void OnHotkey()
    {
        int generation;
        lock (_gate)
        {
            if (_session is not { Kind: SessionKind.Recording, Paused: false } s || _stopping > 0 || _tornDown) return;
            generation = s.Generation;
        }
        Enqueue(new CaptureJob(StepTrigger.Hotkey, null, MouseButton.Left, null, null, Broadcast: true, SkipOwnWindowGuard: false, generation));
    }

    // shots/ is confined and never a link (D10, INV-CAP-23): checked, created, then checked
    // again, because a link can appear between the check and the create.
    private string PrepareShotsFolder(string projectDir)
    {
        var shots = PathConfine.ConfineNoLinks(projectDir, ShotsFolder, _probe) ?? throw new CaptureException(CaptureMessages.ShotsOutsideProject);
        Directory.CreateDirectory(shots);
        return PathConfine.ConfineNoLinks(projectDir, ShotsFolder, _probe) ?? throw new CaptureException(CaptureMessages.ShotsOutsideProject);
    }

    private void ValidateScreenshotTarget(CaptureTarget target)
    {
        switch (target.Mode)
        {
            case "window" when target.Window is not { } window || _windows.Resolve(window) is null:
                throw new CaptureException(CaptureMessages.WindowGone);
            case "area" when target.Area is not { } area || !_screen.Monitors().Any(m => Overlaps(area, m.Bounds)):
                throw new CaptureException(CaptureMessages.AreaOffScreen);
        }
    }

    private CaptureTargets ListTargets()
    {
        var windows = new List<WindowInfo>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var w in _windows.ListWindows())
        {
            if (w.Pid == _own.ProcessId || w.Minimized) continue;
            var title = JsString.Trim(w.Title);
            if (title.Length == 0 || !seen.Add(w.Pid.ToString(CultureInfo.InvariantCulture) + "::" + title)) continue;
            windows.Add(new WindowInfo(w.Id, w.Pid, title, w.App));
        }
        var monitors = _screen.Monitors()
            .Select(m => new MonitorInfo(m.Id, m.Name.Length > 0 ? m.Name : "Display " + m.Id.ToString(CultureInfo.InvariantCulture), (int)m.Bounds.Width, (int)m.Bounds.Height, m.IsPrimary))
            .ToList();
        TargetsListed(_log, windows.Count, monitors.Count);
        return new CaptureTargets(windows, monitors);
    }

    // readdir of shots/: every entry's name, or none when it cannot be listed, so the step count
    // alone seeds the counter (2.2.2 step 6).
    private static List<string> EntryNames(string shots)
    {
        try
        {
            return [.. Directory.EnumerateFileSystemEntries(shots).Select(p => Path.GetFileName(p))];
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return [];
        }
    }

    // D9: one project whatever the case or a trailing separator.
    private static bool SamePath(string a, string b) => string.Equals(FullPath(a), FullPath(b), StringComparison.OrdinalIgnoreCase);

    private static string FullPath(string path)
    {
        try
        {
            return Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));
        }
        catch (Exception e) when (e is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return path;
        }
    }

    // D23: a window or area target that lost its window or area on the way in.
    private static string? MissingTargetPart(CaptureTarget target) => target.Mode switch
    {
        "window" when target.Window is null => "window",
        "area" when target.Area is null => "area",
        _ => null,
    };

    // The area is on screen iff it overlaps some monitor (2.2.5 step 3).
    private static bool Overlaps(Rect a, Rect m) =>
        a.X < m.X + m.Width && a.X + a.Width > m.X && a.Y < m.Y + m.Height && a.Y + a.Height > m.Y;

    private sealed record EndedSession(CaptureSession? Session, bool Screenshot, long Counter, int Committed, bool DeletesProject, IReadOnlyList<string> AddedStepIds)
    {
        public static readonly EndedSession DuringScreenshot = new(null, true, 0, 0, false, []);

        public static readonly EndedSession None = new(null, false, 0, 0, false, []);
    }
}
