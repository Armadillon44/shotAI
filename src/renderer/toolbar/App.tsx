import React from 'react';

export function App(): React.JSX.Element {
  return (
    <div className="toolbar">
      <div className="toolbar__drag" title="Drag to move">
        <span className="toolbar__grip" aria-hidden="true" />
        <span className="toolbar__label">shotAI</span>
      </div>
      <div className="toolbar__controls">
        <button
          type="button"
          className="toolbar__btn toolbar__btn--rec"
          title="Start capture"
          aria-label="Start capture"
        >
          <span className="toolbar__dot" aria-hidden="true" />
        </button>
        <button
          type="button"
          className="toolbar__btn"
          title="Pause / resume"
          aria-label="Pause or resume"
        >
          ❚❚
        </button>
        <button
          type="button"
          className="toolbar__btn"
          title="Stop &amp; process"
          aria-label="Stop and process"
        >
          ■
        </button>
      </div>
    </div>
  );
}
