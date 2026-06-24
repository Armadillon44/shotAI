import React from 'react';
import type { AppInfo } from '../../shared/ipc';

export function App(): React.JSX.Element {
  const [info, setInfo] = React.useState<AppInfo | null>(null);
  const [error, setError] = React.useState<string | null>(null);

  React.useEffect(() => {
    window.shotai
      .getAppInfo()
      .then(setInfo)
      .catch((e: unknown) =>
        setError(e instanceof Error ? e.message : String(e)),
      );
  }, []);

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

        <div className="project__runtime">
          <h2 className="project__runtime-title">Runtime (via IPC)</h2>
          {error && <p className="project__error">IPC error: {error}</p>}
          {info ? (
            <dl className="project__runtime-grid">
              <dt>Version</dt>
              <dd>{info.version}</dd>
              <dt>Platform</dt>
              <dd>
                {info.platform}/{info.arch}
              </dd>
              <dt>Electron</dt>
              <dd>{info.electron}</dd>
              <dt>Chrome</dt>
              <dd>{info.chrome}</dd>
              <dt>Node</dt>
              <dd>{info.node}</dd>
            </dl>
          ) : (
            !error && <p className="project__hint">Loading runtime info…</p>
          )}
        </div>
      </section>
    </main>
  );
}
