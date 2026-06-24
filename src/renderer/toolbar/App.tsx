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

  // Recording is started from the main window (pick/create a project there);
  // the pill controls the in-progress session.
  const onPause = () => window.shotai.capture.pause().then(setState).catch(ignore);
  const onResume = () => window.shotai.capture.resume().then(setState).catch(ignore);
  const onStop = () => window.shotai.capture.stop().then(setState).catch(ignore);

  return (
    <div className={`toolbar toolbar--${status}`}>
      <div className="toolbar__drag" title="Drag to move">
        <span className="toolbar__grip" aria-hidden="true" />
        <span className="toolbar__label">
          {status === 'recording' && (
            <span className="toolbar__rec-dot" aria-hidden="true" />
          )}
          {status === 'idle'
            ? 'shotAI'
            : `${status === 'paused' ? 'Paused' : 'Recording'} · ${count}`}
        </span>
      </div>
      <div className="toolbar__controls">
        {status === 'idle' && (
          <button
            type="button"
            className="toolbar__btn toolbar__btn--rec"
            title="Start a recording from the shotAI window"
            aria-label="Start a recording from the shotAI window"
            disabled
          >
            <span className="toolbar__dot" aria-hidden="true" />
          </button>
        )}
        {status === 'recording' && (
          <button
            type="button"
            className="toolbar__btn"
            title="Pause"
            aria-label="Pause"
            onClick={onPause}
          >
            ❚❚
          </button>
        )}
        {status === 'paused' && (
          <button
            type="button"
            className="toolbar__btn"
            title="Resume"
            aria-label="Resume"
            onClick={onResume}
          >
            ▶
          </button>
        )}
        {(status === 'recording' || status === 'paused') && (
          <button
            type="button"
            className="toolbar__btn toolbar__btn--stop"
            title="Stop &amp; finish"
            aria-label="Stop and finish"
            onClick={onStop}
          >
            ■
          </button>
        )}
      </div>
    </div>
  );
}
