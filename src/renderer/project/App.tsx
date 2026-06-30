import React from 'react';
import logoUrl from '../../../assets/shotAI_icon.png';
import type { CaptureState, ExportFormat } from '../../shared/ipc';
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
import { OverflowMenu, type MenuItem } from './OverflowMenu';
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
  { mode: 'screen', label: 'Screen', hint: 'Capture one full monitor each step' },
  { mode: 'auto', label: 'Auto', hint: 'Best-effort smart capture — may include extra/unintended context' },
  { mode: 'window', label: 'Window', hint: 'Capture one specific window each step' },
  { mode: 'area', label: 'Area', hint: 'Drag-select a fixed region to capture' },
];

export function App(): React.JSX.Element {
  const [projects, setProjects] = React.useState<ProjectSummary[]>([]);
  const [title, setTitle] = React.useState<string>('');
  const [busy, setBusy] = React.useState<boolean>(false);
  // Home project-manager state: sort, inline rename, per-card export menu/busy.
  const [sortKey, setSortKey] = React.useState<SortKey>('modified');
  const [sortAsc, setSortAsc] = React.useState(false);
  const [renamingPath, setRenamingPath] = React.useState<string | null>(null);
  const [renameValue, setRenameValue] = React.useState('');
  const [rowBusyPath, setRowBusyPath] = React.useState<string | null>(null);
  const [error, setError] = React.useState<string | null>(null);
  const [capture, setCapture] = React.useState<CaptureState | null>(null);
  const [steps, setSteps] = React.useState<ProjectStep[]>([]);
  const [showSettings, setShowSettings] = React.useState(false);

  // Capture-mode selection (applied to the next recording).
  const [mode, setMode] = React.useState<CaptureMode>('screen');
  const [targets, setTargets] = React.useState<Targets | null>(null);
  const [targetsLoading, setTargetsLoading] = React.useState(false);
  const [pickedWindow, setPickedWindow] = React.useState<WindowInfo | null>(null);
  const [pickedMonitorId, setPickedMonitorId] = React.useState<number | null>(null);
  const [pickedArea, setPickedArea] = React.useState<Rect | null>(null);
  const [selectingArea, setSelectingArea] = React.useState(false);
  // Whether the window/monitor target dropdown is open.
  const [pickerOpen, setPickerOpen] = React.useState(false);

  const fail = (e: unknown) =>
    setError(e instanceof Error ? e.message : String(e));

  const refresh = React.useCallback(async () => {
    setError(null);
    setProjects(await window.shotai.projects.list());
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
    setPickerOpen(false); // close the target dropdown when switching modes
    if ((m === 'window' || m === 'screen') && !targets) void loadTargets();
  };

  // The label shown on the target dropdown's trigger (current window/monitor).
  const pickerLabel =
    mode === 'window'
      ? pickedWindow
        ? `${pickedWindow.app ? `${pickedWindow.app} — ` : ''}${pickedWindow.title || '(untitled)'}`
        : targetsLoading
          ? 'Loading…'
          : 'Select a window…'
      : (() => {
          const m = targets?.monitors.find((mm) => mm.id === pickedMonitorId);
          return m
            ? `${m.name} · ${m.width}×${m.height}${m.isPrimary ? ' · primary' : ''}`
            : targetsLoading
              ? 'Loading…'
              : 'Select a monitor…';
        })();

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
    window.shotai.capture.getState().then(setCapture).catch(fail);
    refresh().catch(fail);
  }, [refresh]);

  // Screen is the default mode → load monitors so the picker shows the primary
  // preselected (selectMode only lazy-loads on a click).
  React.useEffect(() => {
    if (mode === 'screen' && !targets) void loadTargets();
  }, [mode, targets, loadTargets]);

  // Application menu: File → Settings opens the Settings view. Ignored while
  // recording — the project window is hidden then, so Settings would open
  // invisibly/overlap the recording panel. recordingRef avoids a stale closure.
  React.useEffect(() => {
    return window.shotai.onOpenSettings(() => {
      if (!recordingRef.current) setShowSettings(true);
    });
  }, []);

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

  // `createdThisSession` marks a freshly-created project so a Discard from the
  // pill deletes the whole project (vs. only this session's steps).
  const onRecord = async (projectPath: string, createdThisSession = false) => {
    try {
      // One open: seeds the recording HUD's step list and gives us the id +
      // manifest to mark the project "open" in the detail store (no second,
      // fallible IPC). A failure here throws → caught → capture never starts.
      const { projectId, manifest } = await window.shotai.projects.open(projectPath);
      setSteps(manifest.steps);
      const state = await window.shotai.capture.start(projectPath, buildTarget(), {
        createdThisSession,
      });
      setCapture(state); // recording → detail/home both hidden, so no view flash
      // While recording the detail view stays hidden; when capture stops the
      // user lands in its report and the capture-end effect reloads the
      // freshly captured steps.
      adoptOpened(projectId, projectPath, manifest);
    } catch (e) {
      fail(e);
    }
  };

  // Create + immediately start capturing (the primary flow).
  const onCreate = async (e: React.FormEvent) => {
    e.preventDefault();
    if (busy || !modeReady) return; // empty title is allowed → default name
    setBusy(true);
    try {
      const summary = await window.shotai.projects.create(title.trim());
      setTitle('');
      await refresh();
      await onRecord(summary.path, true); // new project → discard deletes it
    } catch (err) {
      fail(err);
    } finally {
      setBusy(false);
    }
  };

  // Create an EMPTY project and open it (no capture) — for building a greenfield
  // project from imported images / text without recording first.
  const onCreateEmpty = async () => {
    if (busy) return;
    setBusy(true);
    try {
      const summary = await window.shotai.projects.create(title.trim());
      setTitle('');
      await refresh();
      await openProjectInDetail(summary.path);
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
            <img className="project__logo-img" src={logoUrl} alt="" aria-hidden="true" />
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
                {capture.status === 'paused' ? 'Paused' : 'Capturing'} ·{' '}
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

        {showSettings && !recording && (
          <Settings
            onBack={() => setShowSettings(false)}
            onProjectsDirChanged={() => void refresh()}
          />
        )}

        {showHome && !showSettings && (
          <div className="home__create">
            {/* Lead with the create action — the primary intent. */}
            <h2 className="home__h">Start a project</h2>
            <form className="home__createrow" onSubmit={onCreate}>
              <input
                className="project__input"
                type="text"
                placeholder="Name (optional — defaults to a timestamp)"
                value={title}
                onChange={(e) => setTitle(e.target.value)}
                disabled={busy || recording}
              />
              <button
                type="submit"
                className="btn btn--primary"
                disabled={busy || recording || !modeReady}
              >
                {busy ? 'Creating…' : 'Capture ▸'}
              </button>
              <button
                type="button"
                className="btn btn--ghost"
                disabled={busy || recording}
                onClick={() => void onCreateEmpty()}
                title="Create an empty project and open it — add images, screenshots, or text without capturing"
              >
                Empty
              </button>
            </form>

            {/* Capture mode — a compact inline control attached to the create action. */}
            <div className="home__mode" role="radiogroup" aria-label="Capture mode">
              <span className="home__mode-label">Mode</span>
              {MODE_OPTIONS.map((opt) => (
                <button
                  key={opt.mode}
                  type="button"
                  role="radio"
                  aria-checked={mode === opt.mode}
                  className={`capmode__chip${mode === opt.mode ? ' capmode__chip--on' : ''}`}
                  disabled={opt.disabled}
                  title={opt.hint}
                  onClick={() => selectMode(opt.mode)}
                >
                  {opt.label}
                </button>
              ))}
              {mode === 'auto' && (
                <span
                  className="home__mode-warn"
                  title="Auto guesses per click and may capture extra or unintended context. Pick Screen, Window, or Area for predictable results."
                >
                  ⚠ Auto is best-effort
                </span>
              )}
            </div>

            {(mode === 'window' || mode === 'screen') && (
              <div className="home__dd">
                <button
                  type="button"
                  className="home__dd-trigger"
                  aria-haspopup="listbox"
                  aria-expanded={pickerOpen}
                  onClick={() => setPickerOpen((o) => !o)}
                >
                  <span className="home__dd-current">{pickerLabel}</span>
                  <span className="home__dd-caret" aria-hidden="true">
                    ▾
                  </span>
                </button>
                {pickerOpen && (
                  <>
                    <div className="menu__backdrop" onClick={() => setPickerOpen(false)} />
                    <div
                      className="home__dd-pop"
                      role="listbox"
                      aria-label={mode === 'window' ? 'Window to capture' : 'Monitor to capture'}
                    >
                      <div className="home__dd-head">
                        <span>{mode === 'window' ? 'Windows' : 'Monitors'}</span>
                        <button
                          type="button"
                          className="btn btn--small btn--ghost"
                          onClick={() => void loadTargets()}
                          disabled={targetsLoading}
                          title="Refresh the list"
                        >
                          ↻ Refresh
                        </button>
                      </div>
                      <div className="home__dd-list">
                        {mode === 'window' ? (
                          targets?.windows.length ? (
                            targets.windows.map((w) => (
                              <button
                                key={w.id}
                                type="button"
                                role="option"
                                aria-selected={pickedWindow?.id === w.id}
                                className={`home__picker-item${pickedWindow?.id === w.id ? ' home__picker-item--on' : ''}`}
                                onClick={() => {
                                  setPickedWindow(w);
                                  setPickerOpen(false);
                                }}
                              >
                                {w.app && <span className="home__picker-app">{w.app}</span>}
                                <span className="home__picker-name">{w.title || '(untitled)'}</span>
                              </button>
                            ))
                          ) : (
                            <p className="home__picker-empty">
                              {targetsLoading ? 'Loading…' : 'No windows found'}
                            </p>
                          )
                        ) : targets?.monitors.length ? (
                          targets.monitors.map((m) => (
                            <button
                              key={m.id}
                              type="button"
                              role="option"
                              aria-selected={pickedMonitorId === m.id}
                              className={`home__picker-item${pickedMonitorId === m.id ? ' home__picker-item--on' : ''}`}
                              onClick={() => {
                                setPickedMonitorId(m.id);
                                setPickerOpen(false);
                              }}
                            >
                              <span className="home__picker-name">{m.name}</span>
                              <span className="home__picker-app">
                                {m.width}×{m.height}
                                {m.isPrimary ? ' · primary' : ''}
                              </span>
                            </button>
                          ))
                        ) : (
                          <p className="home__picker-empty">
                            {targetsLoading ? 'Loading…' : 'No monitors found'}
                          </p>
                        )}
                      </div>
                    </div>
                  </>
                )}
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
                Pick a window above to enable capture.
              </p>
            )}
            {mode === 'area' && !pickedArea && !selectingArea && (
              <p className="capmode__warn">Select an area to enable capture.</p>
            )}
          </div>
        )}

        {showHome && !showSettings && (
          <>
            <div className="home__listhead">
              <h2 className="home__h">
                Projects <span className="home__count">· {sortedProjects.length}</span>
              </h2>
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
              <div className="empty">
                <div className="empty__icon" aria-hidden="true">
                  🗂️
                </div>
                <p className="empty__line">No projects yet</p>
                <p className="empty__sub">
                  Start one above — capture a process, or build an empty project
                  from images and text.
                </p>
              </div>
            ) : (
              <ul className="project__list">
                {sortedProjects.map((p) => {
                  const rowBusy = rowBusyPath === p.path;
                  const anyBusy = rowBusyPath !== null; // block other rows during an op
                  const exportItems: MenuItem[] = (
                    [
                      ['html', 'HTML'],
                      ['html-plain', 'HTML (for Word)'],
                      ['pdf', 'PDF'],
                      ['markdown', 'Markdown'],
                    ] as [ExportFormat, string][]
                  ).map(([f, label]) => ({
                    label: `Export → ${label}`,
                    onClick: () => void doExport(p, f),
                    disabled: anyBusy,
                  }));
                  return (
                    <li key={p.path} className="project__item">
                      <div className="project__item-main">
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
                        <span className="project__item-meta">
                          {rowBusy
                            ? 'Working…'
                            : `${p.stepCount} step${p.stepCount === 1 ? '' : 's'} · modified ${
                                p.updatedAt
                                  ? new Date(p.updatedAt).toLocaleDateString()
                                  : '—'
                              }`}
                        </span>
                      </div>
                      <div className="project__item-actions">
                        <button
                          type="button"
                          className="btn btn--small btn--primary"
                          disabled={anyBusy}
                          onClick={() => void openProjectInDetail(p.path)}
                        >
                          Open
                        </button>
                        <OverflowMenu
                          disabled={anyBusy}
                          items={[
                            { label: 'Rename', onClick: () => startRename(p) },
                            {
                              label: 'Reveal in Explorer',
                              onClick: () =>
                                void window.shotai.projects.reveal(p.path).catch(fail),
                            },
                            { kind: 'sep' },
                            ...exportItems,
                            { kind: 'sep' },
                            {
                              label: 'Delete',
                              danger: true,
                              onClick: () => void doDelete(p),
                            },
                          ]}
                        />
                      </div>
                    </li>
                  );
                })}
              </ul>
            )}
          </>
        )}

      </section>
    </main>
  );
}
