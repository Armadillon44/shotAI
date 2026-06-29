import React from 'react';
import type { AppInfo, CaptureState, ExportFormat } from '../../shared/ipc';
import type {
  CaptureMode,
  CaptureTarget,
  MonitorInfo,
  ProjectStep,
  ProjectSummary,
  Rect,
  WindowInfo,
} from '../../shared/project';
import { useProjectStore } from './store';
import { ProjectDetail } from './ProjectDetail';
import { Settings } from './Settings';
import { ensureFlattened } from './sop-prepare';

type SortKey = 'name' | 'created' | 'modified';
const SORT_LABELS: { key: SortKey; label: string }[] = [
  { key: 'name', label: 'Name' },
  { key: 'created', label: 'Created' },
  { key: 'modified', label: 'Modified' },
];

type Targets = { windows: WindowInfo[]; monitors: MonitorInfo[] };

const MODE_OPTIONS: {
  mode: CaptureMode;
  label: string;
  hint: string;
  disabled?: boolean;
}[] = [
  { mode: 'auto', label: 'Auto', hint: 'Smart per-click: app window, OS element, or desktop' },
  { mode: 'window', label: 'Window', hint: 'Capture one specific window each step' },
  { mode: 'area', label: 'Area', hint: 'Drag-select a fixed region to capture' },
  { mode: 'screen', label: 'Screen', hint: 'Capture one monitor each step' },
  { mode: 'all', label: 'All screens', hint: 'Capture the whole screen each step' },
];

const MODE_DESC: Record<CaptureMode, string> = {
  auto: 'Smart capture — picks the app window, a tight region around OS elements (taskbar, Start, tray), or the full screen on the desktop.',
  window: 'Every step captures the window you pick below (re-found if it moves).',
  area: 'Every step captures a fixed rectangle you drag-select on screen.',
  screen: 'Every step captures the monitor you pick below.',
  all: 'Every step captures the entire screen.',
};

export function App(): React.JSX.Element {
  const [info, setInfo] = React.useState<AppInfo | null>(null);
  const [projectsDir, setProjectsDir] = React.useState<string>('');
  const [projects, setProjects] = React.useState<ProjectSummary[]>([]);
  const [title, setTitle] = React.useState<string>('');
  const [busy, setBusy] = React.useState<boolean>(false);
  // Home project-manager state: sort, inline rename, per-card export menu/busy.
  const [sortKey, setSortKey] = React.useState<SortKey>('modified');
  const [sortAsc, setSortAsc] = React.useState(false);
  const [renamingPath, setRenamingPath] = React.useState<string | null>(null);
  const [renameValue, setRenameValue] = React.useState('');
  const [exportMenuPath, setExportMenuPath] = React.useState<string | null>(null);
  const [rowBusyPath, setRowBusyPath] = React.useState<string | null>(null);
  const [ctxMenu, setCtxMenu] = React.useState<{ path: string; x: number; y: number } | null>(null);
  const [error, setError] = React.useState<string | null>(null);
  const [capture, setCapture] = React.useState<CaptureState | null>(null);
  const [steps, setSteps] = React.useState<ProjectStep[]>([]);
  const [showSettings, setShowSettings] = React.useState(false);

  // Capture-mode selection (applied to the next recording).
  const [mode, setMode] = React.useState<CaptureMode>('auto');
  const [targets, setTargets] = React.useState<Targets | null>(null);
  const [targetsLoading, setTargetsLoading] = React.useState(false);
  const [pickedWindow, setPickedWindow] = React.useState<WindowInfo | null>(null);
  const [pickedMonitorId, setPickedMonitorId] = React.useState<number | null>(null);
  const [pickedArea, setPickedArea] = React.useState<Rect | null>(null);
  const [selectingArea, setSelectingArea] = React.useState(false);

  const fail = (e: unknown) =>
    setError(e instanceof Error ? e.message : String(e));

  const refresh = React.useCallback(async () => {
    setError(null);
    const [dir, all] = await Promise.all([
      window.shotai.projects.getDir(),
      window.shotai.projects.list(),
    ]);
    setProjectsDir(dir);
    setProjects(all);
  }, []);

  const loadTargets = React.useCallback(async () => {
    setTargetsLoading(true);
    try {
      const t = await window.shotai.capture.listTargets();
      setTargets(t);
      // Keep the prior pick if it's still around, otherwise sensible defaults.
      setPickedWindow((prev) =>
        prev && t.windows.some((w) => w.id === prev.id)
          ? prev
          : t.windows[0] ?? null,
      );
      setPickedMonitorId((prev) =>
        prev != null && t.monitors.some((m) => m.id === prev)
          ? prev
          : (t.monitors.find((m) => m.isPrimary) ?? t.monitors[0])?.id ?? null,
      );
    } catch (e) {
      fail(e);
    } finally {
      setTargetsLoading(false);
    }
  }, []);

  const selectMode = (m: CaptureMode) => {
    setMode(m);
    if ((m === 'window' || m === 'screen') && !targets) void loadTargets();
  };

  const buildTarget = (): CaptureTarget => {
    switch (mode) {
      case 'window':
        return pickedWindow
          ? {
              mode: 'window',
              window: {
                id: pickedWindow.id,
                pid: pickedWindow.pid,
                title: pickedWindow.title,
              },
            }
          : { mode: 'auto' };
      case 'screen':
        return pickedMonitorId != null
          ? { mode: 'screen', monitorId: pickedMonitorId }
          : { mode: 'screen' };
      case 'all':
        return { mode: 'all' };
      case 'area':
        return pickedArea ? { mode: 'area', area: pickedArea } : { mode: 'auto' };
      default:
        return { mode: 'auto' };
    }
  };

  const selectArea = async () => {
    setSelectingArea(true);
    try {
      const r = await window.shotai.region.selectArea();
      if (r) setPickedArea(r);
    } catch (e) {
      fail(e);
    } finally {
      setSelectingArea(false);
    }
  };

  // 'window' needs a picked window and 'area' a selected rect; others are ready.
  const modeReady =
    mode === 'window' ? !!pickedWindow : mode === 'area' ? !!pickedArea : true;

  React.useEffect(() => {
    window.shotai.getAppInfo().then(setInfo).catch(fail);
    window.shotai.capture.getState().then(setCapture).catch(fail);
    refresh().catch(fail);
  }, [refresh]);

  // Application menu: File → Settings opens the Settings view. Ignored while
  // recording — the project window is hidden then, so Settings would open
  // invisibly/overlap the recording panel. recordingRef avoids a stale closure.
  React.useEffect(() => {
    return window.shotai.onOpenSettings(() => {
      if (!recordingRef.current) setShowSettings(true);
    });
  }, []);

  // Dismiss the project context menu on Escape.
  React.useEffect(() => {
    if (!ctxMenu) return;
    const onKey = (e: KeyboardEvent) => {
      if (e.key === 'Escape') setCtxMenu(null);
    };
    window.addEventListener('keydown', onKey);
    return () => window.removeEventListener('keydown', onKey);
  }, [ctxMenu]);

  // Dismiss the per-card Export dropdown on outside-click / Escape.
  React.useEffect(() => {
    if (!exportMenuPath) return;
    const onDown = (e: MouseEvent) => {
      if (!(e.target as HTMLElement).closest('.project__export')) setExportMenuPath(null);
    };
    const onKey = (e: KeyboardEvent) => {
      if (e.key === 'Escape') setExportMenuPath(null);
    };
    document.addEventListener('mousedown', onDown);
    window.addEventListener('keydown', onKey);
    return () => {
      document.removeEventListener('mousedown', onDown);
      window.removeEventListener('keydown', onKey);
    };
  }, [exportMenuPath]);

  React.useEffect(() => {
    const offState = window.shotai.capture.onStateChanged(setCapture);
    const offStep = window.shotai.capture.onStepAdded((step) =>
      setSteps((prev) => [...prev, step]),
    );
    const offError = window.shotai.capture.onError((message) =>
      setError(`Capture error: ${message}`),
    );
    return () => {
      offState();
      offStep();
      offError();
    };
  }, []);

  const recording = capture?.status === 'recording' || capture?.status === 'paused';
  // Mirror `recording` into a ref so once-registered callbacks (menu → settings)
  // read the current value instead of a stale closure.
  const recordingRef = React.useRef(recording);
  recordingRef.current = recording;

  // Detail/editor view ownership lives in the Zustand store. Home (recents +
  // capture-mode picker) shows when nothing is open and we're not recording.
  const openPath = useProjectStore((s) => s.projectPath);
  const openProjectInDetail = useProjectStore((s) => s.open);
  const adoptOpened = useProjectStore((s) => s.applyOpened);
  const showDetail = !recording && !!openPath;
  const showHome = !recording && !openPath;

  // When a capture session that ran on the open project ends, reload its
  // manifest so the newly captured steps appear in the detail report.
  const wasRecording = React.useRef(false);
  React.useEffect(() => {
    if (wasRecording.current && !recording && openPath) {
      void openProjectInDetail(openPath);
    }
    wasRecording.current = recording;
  }, [recording, openPath, openProjectInDetail]);

  // Re-fetch recents whenever we return to the home screen, so changes made
  // inside a project (e.g. an AI-refined title, new step count) show in the list.
  React.useEffect(() => {
    if (showHome) refresh().catch(fail);
  }, [showHome, refresh]);

  // A SOP generate/revert flips sopBackup and rewrites the manifest title/steps;
  // refresh recents then (while still in the project) so the home list shows the
  // new title immediately on return — not only on a later manual reload.
  const sopBackup = useProjectStore((s) => s.sopBackup);
  React.useEffect(() => {
    refresh().catch(fail);
  }, [sopBackup, refresh]);

  const onRecord = async (projectPath: string) => {
    try {
      // One open: seeds the recording HUD's step list and gives us the id +
      // manifest to mark the project "open" in the detail store (no second,
      // fallible IPC). A failure here throws → caught → capture never starts.
      const { projectId, manifest } = await window.shotai.projects.open(projectPath);
      setSteps(manifest.steps);
      const state = await window.shotai.capture.start(projectPath, buildTarget());
      setCapture(state); // recording → detail/home both hidden, so no view flash
      // While recording the detail view stays hidden; when capture stops the
      // user lands in its report and the capture-end effect reloads the
      // freshly captured steps.
      adoptOpened(projectId, projectPath, manifest);
    } catch (e) {
      fail(e);
    }
  };

  const onCreate = async (e: React.FormEvent) => {
    e.preventDefault();
    if (busy || !modeReady) return; // empty title is allowed → default name
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

  // --- Home project-manager actions ---
  const startRename = (p: ProjectSummary) => {
    setRenamingPath(p.path);
    setRenameValue(p.title);
  };
  const commitRename = async () => {
    const path = renamingPath;
    setRenamingPath(null);
    if (!path) return;
    const next = renameValue.trim();
    const orig = projects.find((p) => p.path === path)?.title;
    // Blur fires on any outside interaction; don't surprise-rename on an empty
    // field (would become a default timestamp) or an unchanged title.
    if (!next || next === orig) return;
    try {
      await window.shotai.projects.rename(path, next);
      await refresh();
    } catch (e) {
      fail(e);
    }
  };
  const doDelete = async (p: ProjectSummary) => {
    if (rowBusyPath) return;
    if (!window.confirm(`Delete "${p.title}"? This removes the project folder and its screenshots.`)) {
      return;
    }
    setRowBusyPath(p.path);
    try {
      await window.shotai.projects.delete(p.path);
      await refresh();
    } catch (e) {
      fail(e);
    } finally {
      setRowBusyPath(null);
    }
  };
  const doExport = async (p: ProjectSummary, format: ExportFormat) => {
    if (rowBusyPath) return; // one row op at a time
    setExportMenuPath(null);
    setRowBusyPath(p.path);
    setError(null);
    try {
      // Open (for the shot:// id + steps), flatten so only redacted/marker-baked
      // renders are written, then export — same guarantee as the in-project flow.
      const { projectId, manifest } = await window.shotai.projects.open(p.path);
      await ensureFlattened(projectId, p.path, manifest.steps);
      await window.shotai.projects.export(p.path, format);
      await refresh(); // re-baking bumped updatedAt — refresh the list's metadata
    } catch (e) {
      fail(e);
    } finally {
      setRowBusyPath(null);
    }
  };

  // Sorted view of all projects for the home list.
  const sortedProjects = React.useMemo(() => {
    const arr = [...projects];
    arr.sort((a, b) => {
      let cmp: number;
      if (sortKey === 'name') cmp = a.title.localeCompare(b.title, undefined, { sensitivity: 'base' });
      else if (sortKey === 'created') cmp = a.createdAt.localeCompare(b.createdAt);
      else cmp = a.updatedAt.localeCompare(b.updatedAt);
      return sortAsc ? cmp : -cmp;
    });
    return arr;
  }, [projects, sortKey, sortAsc]);

  return (
    <main className="project">
      {/* The shotAI banner is hidden in the detail/edit view so the project's
          own sticky header pins flush to the top. */}
      {!showDetail && (
        <header className="project__header">
          <div className="project__brand">
            <span className="project__logo" aria-hidden="true">
              ◎
            </span>
            <h1 className="project__title">shotAI</h1>
          </div>
          {showHome && !showSettings && (
            <button
              type="button"
              className="btn btn--small"
              onClick={() => setShowSettings(true)}
              title="Settings"
            >
              ⚙ Settings
            </button>
          )}
        </header>
      )}

      <section
        className={`project__body${showDetail && !showSettings ? ' project__body--detail' : ''}`}
      >
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
                  onClick={() =>
                    window.shotai.capture.pause().then(setCapture).catch(fail)
                  }
                >
                  Pause
                </button>
              ) : (
                <button
                  type="button"
                  className="btn"
                  onClick={() =>
                    window.shotai.capture.resume().then(setCapture).catch(fail)
                  }
                >
                  Resume
                </button>
              )}
              <button
                type="button"
                className="btn btn--primary"
                onClick={() =>
                  window.shotai.capture.stop().then(setCapture).catch(fail)
                }
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

        {showDetail && !showSettings && (
          <ProjectDetail
            onResumeCapture={
              openPath ? () => void onRecord(openPath) : undefined
            }
            onOpenSettings={() => setShowSettings(true)}
          />
        )}

        {showSettings && !recording && <Settings onBack={() => setShowSettings(false)} />}

        {showHome && !showSettings && (
          <section className="capmode">
            <span className="project__label">Capture mode</span>
            <div
              className="capmode__modes"
              role="radiogroup"
              aria-label="Capture mode"
            >
              {MODE_OPTIONS.map((opt) => (
                <button
                  key={opt.mode}
                  type="button"
                  role="radio"
                  aria-checked={mode === opt.mode}
                  className={`capmode__chip${
                    mode === opt.mode ? ' capmode__chip--on' : ''
                  }`}
                  disabled={opt.disabled}
                  title={opt.hint}
                  onClick={() => selectMode(opt.mode)}
                >
                  {opt.label}
                  {opt.disabled && <span className="capmode__soon">next</span>}
                </button>
              ))}
            </div>
            <p className="capmode__desc">{MODE_DESC[mode]}</p>

            {(mode === 'window' || mode === 'screen') && (
              <div className="capmode__picker">
                {mode === 'window' ? (
                  <select
                    className="capmode__select"
                    aria-label="Window to capture"
                    value={pickedWindow ? String(pickedWindow.id) : ''}
                    onChange={(e) => {
                      const id = Number(e.target.value);
                      setPickedWindow(
                        targets?.windows.find((w) => w.id === id) ?? null,
                      );
                    }}
                  >
                    {!targets?.windows.length && (
                      <option value="">
                        {targetsLoading ? 'Loading…' : 'No windows found'}
                      </option>
                    )}
                    {targets?.windows.map((w) => (
                      <option key={w.id} value={w.id}>
                        {w.app ? `${w.app} — ` : ''}
                        {w.title}
                      </option>
                    ))}
                  </select>
                ) : (
                  <select
                    className="capmode__select"
                    aria-label="Monitor to capture"
                    value={pickedMonitorId != null ? String(pickedMonitorId) : ''}
                    onChange={(e) => setPickedMonitorId(Number(e.target.value))}
                  >
                    {!targets?.monitors.length && (
                      <option value="">
                        {targetsLoading ? 'Loading…' : 'No monitors found'}
                      </option>
                    )}
                    {targets?.monitors.map((m) => (
                      <option key={m.id} value={m.id}>
                        {m.name} · {m.width}×{m.height}
                        {m.isPrimary ? ' · primary' : ''}
                      </option>
                    ))}
                  </select>
                )}
                <button
                  type="button"
                  className="btn btn--small"
                  onClick={() => void loadTargets()}
                  disabled={targetsLoading}
                  title="Refresh the list"
                >
                  ↻
                </button>
              </div>
            )}
            {mode === 'area' && (
              <div className="capmode__picker">
                <button
                  type="button"
                  className="btn"
                  onClick={() => void selectArea()}
                  disabled={selectingArea}
                >
                  {selectingArea
                    ? 'Selecting…'
                    : pickedArea
                      ? 'Re-select area'
                      : 'Select area…'}
                </button>
                {pickedArea && (
                  <span className="capmode__area">
                    {pickedArea.width} × {pickedArea.height}px @ ({pickedArea.x},{' '}
                    {pickedArea.y})
                  </span>
                )}
              </div>
            )}
            {mode === 'window' && !pickedWindow && (
              <p className="capmode__warn">
                Pick a window above to enable recording.
              </p>
            )}
            {mode === 'area' && !pickedArea && !selectingArea && (
              <p className="capmode__warn">Select an area to enable recording.</p>
            )}
          </section>
        )}

        {showHome && !showSettings && (
          <>
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
            placeholder="New project name (optional — defaults to a timestamp)"
            value={title}
            onChange={(e) => setTitle(e.target.value)}
            disabled={busy || recording}
          />
          <button
            type="submit"
            className="btn btn--primary"
            disabled={busy || recording || !modeReady}
          >
            {busy ? 'Creating…' : 'New project + record'}
          </button>
        </form>

        <div className="project__list-head">
          <h2 className="project__section-title">Projects</h2>
          <div className="project__sort" role="group" aria-label="Sort projects">
            <span className="project__sort-label">Sort:</span>
            {SORT_LABELS.map((s) => (
              <button
                key={s.key}
                type="button"
                className={`project__sort-chip${sortKey === s.key ? ' project__sort-chip--on' : ''}`}
                onClick={() => setSortKey(s.key)}
              >
                {s.label}
              </button>
            ))}
            <button
              type="button"
              className="btn btn--small"
              title={sortAsc ? 'Ascending' : 'Descending'}
              onClick={() => setSortAsc((v) => !v)}
            >
              {sortAsc ? '▲' : '▼'}
            </button>
          </div>
        </div>
        {sortedProjects.length === 0 ? (
          <p className="project__hint">
            No projects yet. Create one above to start recording.
          </p>
        ) : (
          <ul className="project__list">
            {sortedProjects.map((p) => {
              const rowBusy = rowBusyPath === p.path;
              const anyBusy = rowBusyPath !== null; // block other rows during an op
              return (
                <li
                  key={p.path}
                  className="project__item"
                  onContextMenu={(e) => {
                    e.preventDefault();
                    setExportMenuPath(null);
                    // Clamp so the menu stays on-screen near the right/bottom edge.
                    setCtxMenu({
                      path: p.path,
                      x: Math.min(e.clientX, window.innerWidth - 200),
                      y: Math.min(e.clientY, window.innerHeight - 60),
                    });
                  }}
                >
                  {renamingPath === p.path ? (
                    <input
                      className="project__rename-input"
                      autoFocus
                      value={renameValue}
                      onChange={(e) => setRenameValue(e.target.value)}
                      onKeyDown={(e) => {
                        if (e.key === 'Enter') void commitRename();
                        else if (e.key === 'Escape') setRenamingPath(null);
                      }}
                      onBlur={() => void commitRename()}
                    />
                  ) : (
                    <span className="project__item-title">{p.title}</span>
                  )}
                  <div className="project__item-actions">
                    <button
                      type="button"
                      className="btn btn--small"
                      disabled={anyBusy}
                      onClick={() => void openProjectInDetail(p.path)}
                    >
                      Open
                    </button>
                    <button
                      type="button"
                      className="btn btn--small"
                      disabled={anyBusy}
                      onClick={() => startRename(p)}
                    >
                      Rename
                    </button>
                    <div className="project__export">
                      <button
                        type="button"
                        className="btn btn--small"
                        disabled={anyBusy}
                        onClick={() =>
                          setExportMenuPath((cur) => (cur === p.path ? null : p.path))
                        }
                      >
                        {rowBusy ? 'Working…' : 'Export ▾'}
                      </button>
                      {exportMenuPath === p.path && (
                        <div className="project__export-menu" role="menu">
                          {(['html', 'pdf', 'markdown'] as ExportFormat[]).map((f) => (
                            <button
                              key={f}
                              type="button"
                              className="btn btn--small"
                              onClick={() => void doExport(p, f)}
                            >
                              {f === 'markdown' ? 'Markdown' : f.toUpperCase()}
                            </button>
                          ))}
                        </div>
                      )}
                    </div>
                    <button
                      type="button"
                      className="btn btn--small btn--danger"
                      disabled={anyBusy}
                      onClick={() => void doDelete(p)}
                    >
                      Delete
                    </button>
                  </div>
                  <code className="project__item-path" title={p.path}>
                    {p.stepCount} step{p.stepCount === 1 ? '' : 's'} · modified{' '}
                    {p.updatedAt ? new Date(p.updatedAt).toLocaleString() : '—'}
                  </code>
                </li>
              );
            })}
          </ul>
        )}
          </>
        )}

        {ctxMenu && (
          <>
            <div
              className="project__ctx-backdrop"
              onClick={() => setCtxMenu(null)}
              onContextMenu={(e) => {
                e.preventDefault();
                setCtxMenu(null);
              }}
            />
            <div
              className="project__ctx"
              role="menu"
              style={{ left: ctxMenu.x, top: ctxMenu.y }}
            >
              <button
                type="button"
                className="project__ctx-item"
                onClick={() => {
                  const path = ctxMenu.path;
                  setCtxMenu(null);
                  window.shotai.projects.reveal(path).catch(fail);
                }}
              >
                Reveal in Explorer
              </button>
            </div>
          </>
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
