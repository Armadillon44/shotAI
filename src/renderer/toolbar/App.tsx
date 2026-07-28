import React from 'react';
import type { CaptureState } from '../../shared/ipc';

export function App(): React.JSX.Element {
  const [state, setState] = React.useState<CaptureState | null>(null);
  const ignore = () => undefined;

  React.useEffect(() => {
    window.shotai.capture.getState().then(setState).catch(ignore);
    const off = window.shotai.capture.onStateChanged(setState);
    return off;
  }, []);

  const status = state?.status ?? 'idle';
  const count = state?.stepCount ?? 0;
  const active = status === 'recording' || status === 'paused';

  // Per-capture confirmation flash (R4). The only proof a click registered is
  // this pill (the main window is hidden), so pulse a green ring whenever the
  // step count climbs. Bumping a key remounts the overlay to replay the one-shot
  // animation, which also fires reliably for rapid successive captures.
  const prevCount = React.useRef(count);
  const [flashKey, setFlashKey] = React.useState(0);

  // In-session capture errors. The main window owns the error banner, but it is
  // HIDDEN while recording — so the pill is the only surface a mid-session
  // failure can reach. Without this, a long recording can fail silently (macOS
  // parity: CapturePill's error badge).
  const [error, setError] = React.useState<string | null>(null);
  React.useEffect(() => {
    // A thrown Error carrying no message would otherwise paint a red row with a
    // glyph and no text. Substitute something readable — the point is that the
    // user learns a capture failed at all, message or not.
    const off = window.shotai.capture.onError((msg) =>
      setError(msg && msg.trim() ? msg : 'A capture failed — see the log for details.'),
    );
    return off;
  }, []);

  React.useEffect(() => {
    if (active && count > prevCount.current) {
      setFlashKey((k) => k + 1);
      setError(null); // a step landed — capture recovered, so drop the error
    }
    prevCount.current = count;
  }, [count, active]);

  // Never let an error linger on the idle pill, and start each session clean.
  React.useEffect(() => {
    if (!active) setError(null);
  }, [active]);

  // Only while a session exists — see above.
  const showError = active && error !== null;

  // Capture is started from the main window (pick/create a project there);
  // the pill controls the in-progress session.
  const onPause = () => window.shotai.capture.pause().then(setState).catch(ignore);
  const onResume = () => window.shotai.capture.resume().then(setState).catch(ignore);
  const onStop = () => window.shotai.capture.stop().then(setState).catch(ignore);
  const onDiscard = () => {
    // Native confirm here is fine: discarding ends the capture session, so the
    // post-dialog keyboard-focus loss (B4) has no text field to affect, and the
    // toolbar window doesn't load the in-app modal's styles.
    // Warn honestly when Discard will delete the WHOLE project, not just this
    // session's steps (R5).
    const message = state?.willDeleteProjectOnDiscard
      ? 'Discard this capture? This is a new project, so the entire project will be deleted.'
      : 'Discard this capture? Steps recorded in this session will be deleted.';
    if (!window.confirm(message)) {
      return;
    }
    window.shotai.capture
      .discard()
      .then((r) => setState(r.state))
      .catch(ignore);
  };

  return (
    <div className={`toolbar toolbar--${status}${showError ? ' toolbar--err' : ''}`}>
      <div className="toolbar__row">
        <div className="toolbar__drag" title="Drag to move">
          <span className="toolbar__grip" aria-hidden="true" />
          <span className="toolbar__label">
            {active && <span className="toolbar__rec-dot" aria-hidden="true" />}
            {status === 'idle'
              ? 'shotAI'
              : `${status === 'paused' ? 'Paused' : 'Capturing'} · ${count}`}
          </span>
        </div>
        {/* Idle shows no controls (capture starts from the main window); the pill
            only carries controls while a session is active. */}
        {active && (
          <div className="toolbar__controls">
            {status === 'recording' ? (
              <button
                type="button"
                className="toolbar__btn toolbar__btn--label"
                title="Pause"
                onClick={onPause}
              >
                ❚❚ Pause
              </button>
            ) : (
              <button
                type="button"
                className="toolbar__btn toolbar__btn--label"
                title="Resume"
                onClick={onResume}
              >
                ▶ Resume
              </button>
            )}
            <button
              type="button"
              className="toolbar__btn toolbar__btn--label toolbar__btn--stop"
              title="Stop &amp; finish"
              onClick={onStop}
            >
              ■ Stop
            </button>
            <span className="toolbar__divider" aria-hidden="true" />
            <button
              type="button"
              className="toolbar__btn toolbar__btn--discard"
              title="Discard this capture"
              aria-label="Discard this capture"
              onClick={onDiscard}
            >
              ✕
            </button>
          </div>
        )}
      </div>

      {/* Persistent instruction (R3): the core interaction is otherwise taught
          only in the main window, which hides while recording. An unacknowledged
          capture error takes this row instead — a failure outranks guidance, and
          row 1 has no room for the message beside the controls. */}
      {active &&
        (showError ? (
          <div className="toolbar__err" role="alert">
            {/* Both the glyph and the message carry the full text as a tooltip —
                the row truncates to fit 380px, so hover is how the whole message
                is read. */}
            <span
              className="toolbar__err-glyph"
              title={error ?? undefined}
              aria-hidden="true"
            >
              ⚠
            </span>
            <span className="toolbar__err-msg" title={error ?? undefined}>
              {error}
            </span>
            <button
              type="button"
              className="toolbar__err-x"
              title="Dismiss this error"
              aria-label="Dismiss this capture error"
              onClick={() => setError(null)}
            >
              ✕
            </button>
          </div>
        ) : (
          <div className="toolbar__hint">
            {status === 'paused'
              ? 'Paused — press Resume to keep capturing'
              : 'Click anything to capture a step · Ctrl+Shift+S'}
          </div>
        ))}

      {flashKey > 0 && <span key={flashKey} className="toolbar__flash" aria-hidden="true" />}
    </div>
  );
}
