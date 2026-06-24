import React from 'react';
import type { AppInfo } from '../../shared/ipc';
import type { ProjectSummary } from '../../shared/project';

export function App(): React.JSX.Element {
  const [info, setInfo] = React.useState<AppInfo | null>(null);
  const [projectsDir, setProjectsDir] = React.useState<string>('');
  const [recents, setRecents] = React.useState<ProjectSummary[]>([]);
  const [title, setTitle] = React.useState<string>('');
  const [busy, setBusy] = React.useState<boolean>(false);
  const [error, setError] = React.useState<string | null>(null);

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
    refresh().catch(fail);
  }, [refresh]);

  const onChangeDir = async () => {
    try {
      const dir = await window.shotai.projects.chooseDir();
      if (dir) await refresh();
    } catch (e) {
      fail(e);
    }
  };

  const onCreate = async (e: React.FormEvent) => {
    e.preventDefault();
    if (!title.trim() || busy) return;
    setBusy(true);
    try {
      await window.shotai.projects.create(title.trim());
      setTitle('');
      await refresh();
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

        <div className="project__row">
          <div className="project__dir">
            <span className="project__label">Projects folder</span>
            <code className="project__dir-path" title={projectsDir}>
              {projectsDir || '…'}
            </code>
          </div>
          <button type="button" className="btn" onClick={onChangeDir}>
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
            disabled={busy}
          />
          <button
            type="submit"
            className="btn btn--primary"
            disabled={busy || !title.trim()}
          >
            {busy ? 'Creating…' : 'New project'}
          </button>
        </form>

        <h2 className="project__section-title">Recent projects</h2>
        {recents.length === 0 ? (
          <p className="project__hint">
            No projects yet. Create one above — each becomes a folder under your
            projects directory.
          </p>
        ) : (
          <ul className="project__list">
            {recents.map((p) => (
              <li key={p.path} className="project__item">
                <span className="project__item-title">{p.title}</span>
                <span className="project__item-meta">
                  {p.stepCount} step{p.stepCount === 1 ? '' : 's'}
                </span>
                <code className="project__item-path" title={p.path}>
                  {p.path}
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
