// The Home projects list — extracted from App.tsx. Self-contained: owns its sort /
// inline-rename / per-row-busy state and the row actions (open / rename / reveal /
// export / delete), driven by a small prop surface. App still owns the `projects`
// array (its effects refresh it on home-return / SOP edits); this component
// reports mutations back via onChanged() so App re-lists.
import React from 'react';
import type { ExportFormat } from '../../shared/ipc';
import type { ProjectSummary } from '../../shared/project';
import { OverflowMenu, type MenuItem } from './OverflowMenu';
import { ensureFlattened } from './sop-prepare';
import { useConfirm } from '../useConfirm';

type SortKey = 'name' | 'created' | 'modified';
const SORT_LABELS: { key: SortKey; label: string }[] = [
  { key: 'name', label: 'Name' },
  { key: 'created', label: 'Created' },
  { key: 'modified', label: 'Modified' },
];

export function ProjectList({
  projects,
  onOpen,
  onChanged,
  onError,
  onImport,
}: {
  projects: ProjectSummary[];
  /** Open a project in the detail view. */
  onOpen: (path: string) => void;
  /** Called after a mutation (rename/delete/export re-bake) so App re-lists. */
  onChanged: () => Promise<void> | void;
  /** Surface an error through App's shared notice. */
  onError: (e: unknown) => void;
  /** Import a shared project package (.zip). Renders the Import button when set. */
  onImport?: () => void;
}): React.JSX.Element {
  const [sortKey, setSortKey] = React.useState<SortKey>('modified');
  const [sortAsc, setSortAsc] = React.useState(false);
  const [renamingPath, setRenamingPath] = React.useState<string | null>(null);
  const [renameValue, setRenameValue] = React.useState('');
  const [rowBusyPath, setRowBusyPath] = React.useState<string | null>(null);
  const { confirm, confirmModal } = useConfirm();

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
      await onChanged();
    } catch (e) {
      onError(e);
    }
  };
  const doDelete = async (p: ProjectSummary) => {
    if (rowBusyPath) return;
    if (
      !(await confirm(`Delete "${p.title}"? This removes the project folder and its screenshots.`, {
        confirmLabel: 'Delete',
        danger: true,
      }))
    ) {
      return;
    }
    setRowBusyPath(p.path);
    try {
      await window.shotai.projects.delete(p.path);
      await onChanged();
    } catch (e) {
      onError(e);
    } finally {
      setRowBusyPath(null);
    }
  };
  const doExport = async (p: ProjectSummary, format: ExportFormat) => {
    if (rowBusyPath) return; // one row op at a time
    setRowBusyPath(p.path);
    try {
      // Open (for the shot:// id + steps), flatten so only redacted/marker-baked
      // renders are written, then export — same guarantee as the in-project flow.
      const { projectId, manifest } = await window.shotai.projects.open(p.path);
      await ensureFlattened(projectId, p.path, manifest.steps);
      await window.shotai.projects.export(p.path, format);
      await onChanged(); // re-baking bumped updatedAt — refresh the list's metadata
    } catch (e) {
      onError(e);
    } finally {
      setRowBusyPath(null);
    }
  };

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
    <>
      {confirmModal}
      <div className="home__listhead">
        <div className="home__listhead-left">
          <h2 className="home__h">
            Projects <span className="home__count">· {sortedProjects.length}</span>
          </h2>
          {onImport && (
            <button
              type="button"
              className="btn btn--small btn--ghost"
              onClick={onImport}
              title="Import a project package (.zip) someone shared with you"
            >
              ⤓ Import project
            </button>
          )}
        </div>
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
            Create a project above: press <b>Capture ▸</b> to record a process, or{' '}
            <b>Empty Project</b> to build one from images and text.
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
                ['docx', 'Word'],
                ['pptx', 'PowerPoint'],
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
                          p.updatedAt ? new Date(p.updatedAt).toLocaleDateString() : '—'
                        }`}
                  </span>
                </div>
                <div className="project__item-actions">
                  <button
                    type="button"
                    className="btn btn--small btn--primary"
                    disabled={anyBusy}
                    onClick={() => onOpen(p.path)}
                  >
                    Open
                  </button>
                  <OverflowMenu
                    disabled={anyBusy}
                    items={[
                      { label: 'Rename', onClick: () => startRename(p) },
                      {
                        label: 'Reveal in Explorer',
                        onClick: () => void window.shotai.projects.reveal(p.path).catch(onError),
                      },
                      { kind: 'sep' },
                      ...exportItems,
                      { kind: 'sep' },
                      { label: 'Delete', danger: true, onClick: () => void doDelete(p) },
                    ]}
                  />
                </div>
              </li>
            );
          })}
        </ul>
      )}
    </>
  );
}
