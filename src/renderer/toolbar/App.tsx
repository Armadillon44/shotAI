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
  React.useEffect(() => {
    if (active && count > prevCount.current) setFlashKey((k) => k + 1);
    prevCount.current = count;
  }, [count, active]);

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
    <div className={`toolbar toolbar--${status}`}>
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
          only in the main window, which hides while recording. */}
      {active && (
        <div className="toolbar__hint">
          {status === 'paused'
            ? 'Paused — press Resume to keep capturing'
            : 'Click anything to capture a step · Ctrl+Shift+S'}
        </div>
      )}

      {flashKey > 0 && <span key={flashKey} className="toolbar__flash" aria-hidden="true" />}
    </div>
  );
}
