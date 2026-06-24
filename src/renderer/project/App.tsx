import React from 'react';
import type { AppInfo, CaptureState } from '../../shared/ipc';
import type { ProjectStep, ProjectSummary } from '../../shared/project';

export function App(): React.JSX.Element {
  const [info, setInfo] = React.useState<AppInfo | null>(null);
  const [projectsDir, setProjectsDir] = React.useState<string>('');
  const [recents, setRecents] = React.useState<ProjectSummary[]>([]);
  const [title, setTitle] = React.useState<string>('');
  const [busy, setBusy] = React.useState<boolean>(false);
  const [error, setError] = React.useState<string | null>(null);
  const [capture, setCapture] = React.useState<CaptureState | null>(null);
  const [steps, setSteps] = React.useState<ProjectStep[]>([]);

  const fail = (e: unknown) =>
    setError(e instanceof Error ? e.message : String(e));

  const refresh = React.useCallback(async () => {
    setError(null);
    const [dir, recent] = await Promise.all([
      window.shotai.projects.getDir(),
      window.shotai.projects.listRecent(),
    ]);
    setProjectsDir(dir);
    setRecents(recent);
  }, []);

  React.useEffect(() => {
    window.shotai.getAppInfo().then(setInfo).catch(fail);
    window.shotai.capture.getState().then(setCapture).catch(fail);
    refresh().catch(fail);
  }, [refresh]);

  React.useEffect(() => {
    const offState = window.shotai.capture.onStateChanged(setCapture);
    const offStep = window.shotai.capture.onStepAdded((step) =>
      setSteps((prev) => [...prev, step]),
    );
    return () => {
      offState();
      offStep();
    };
  }, []);

  const recording = capture?.status === 'recording' || capture?.status === 'paused';

  const onRecord = async (projectPath: string) => {
    try {
      setSteps([]);
      const state = await window.shotai.capture.start(projectPath);
      setCapture(state);
    } catch (e) {
      fail(e);
    }
  };

  const onCreate = async (e: React.FormEvent) => {
    e.preventDefault();
    if (!title.trim() || busy) return;
    setBusy(true);
    try {
      const summary = await window.shotai.projects.create(title.trim());
      setTitle('');
      await refresh();
      await onRecord(summary.path);
    } catch (err) {
      fail(err);
    } finally {
      setBusy(false);
    }
  };

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
        {error && <p className="project__error">Error: {error}</p>}

        {recording && capture && (
          <div className={`rec rec--${capture.status}`}>
            <div className="rec__head">
              <span className="rec__dot" aria-hidden="true" />
              <span className="rec__label">
                {capture.status === 'paused' ? 'Paused' : 'Recording'} ·{' '}
                {capture.projectTitle}
              </span>
              <span className="rec__count">{capture.stepCount} steps</span>
            </div>
            <div className="rec__controls">
              {capture.status === 'recording' ? (
                <button
                  type="button"
                  className="btn"
                  onClick={() => window.shotai.capture.pause().then(setCapture)}
                >
                  Pause
                </button>
              ) : (
                <button
                  type="button"
                  className="btn"
                  onClick={() => window.shotai.capture.resume().then(setCapture)}
                >
                  Resume
                </button>
              )}
              <button
                type="button"
                className="btn btn--primary"
                onClick={() => window.shotai.capture.stop().then(setCapture)}
              >
                Stop
              </button>
            </div>
            <p className="project__hint">
              Click anywhere (or press Ctrl+Shift+S) to capture a step. Clicks on
              shotAI's own windows are ignored.
            </p>
            {steps.length > 0 && (
              <ol className="rec__steps">
                {steps.map((s) => (
                  <li key={s.id} className="rec__step">
                    <span className="rec__step-n">{s.order}</span>
                    <span className="rec__step-cap">{s.caption}</span>
                    {s.window && (
                      <span className="rec__step-win">{s.window.title}</span>
                    )}
                  </li>
                ))}
              </ol>
            )}
          </div>
        )}

        <div className="project__row">
          <div className="project__dir">
            <span className="project__label">Projects folder</span>
            <code className="project__dir-path" title={projectsDir}>
              {projectsDir || '…'}
            </code>
          </div>
          <button
            type="button"
            className="btn"
            disabled={recording}
            onClick={() =>
              window.shotai.projects
                .chooseDir()
                .then((d) => {
                  if (d) void refresh();
                })
                .catch(fail)
            }
          >
            Change…
          </button>
        </div>

        <form className="project__new" onSubmit={onCreate}>
          <input
            className="project__input"
            type="text"
            placeholder="New project name"
            value={title}
            onChange={(e) => setTitle(e.target.value)}
            disabled={busy || recording}
          />
          <button
            type="submit"
            className="btn btn--primary"
            disabled={busy || recording || !title.trim()}
          >
            {busy ? 'Creating…' : 'New project + record'}
          </button>
        </form>

        <h2 className="project__section-title">Recent projects</h2>
        {recents.length === 0 ? (
          <p className="project__hint">
            No projects yet. Create one above to start recording.
          </p>
        ) : (
          <ul className="project__list">
            {recents.map((p) => (
              <li key={p.path} className="project__item">
                <span className="project__item-title">{p.title}</span>
                <button
                  type="button"
                  className="btn btn--small"
                  disabled={recording}
                  onClick={() => onRecord(p.path)}
                >
                  Record
                </button>
                <code className="project__item-path" title={p.path}>
                  {p.path} · {p.stepCount} step{p.stepCount === 1 ? '' : 's'}
                </code>
              </li>
            ))}
          </ul>
        )}
      </section>

      {info && (
        <footer className="project__footer">
          {info.name} · {info.platform}/{info.arch} · electron {info.electron}
        </footer>
      )}
    </main>
  );
}
