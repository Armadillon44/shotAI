import React from 'react';

export function App(): React.JSX.Element {
  return (
    <main className="project">
      <header className="project__header">
        <div className="project__brand">
          <span className="project__logo" aria-hidden="true">
            ◎
          </span>
          <h1 className="project__title">shotAI</h1>
        </div>
        <p className="project__tagline">Local-first SOP builder</p>
      </header>
      <section className="project__body">
        <p className="project__hint">
          Project window scaffold is live. Recent projects, the step list, the
          annotated report, and Claude-generated SOPs will live here.
        </p>
      </section>
    </main>
  );
}
