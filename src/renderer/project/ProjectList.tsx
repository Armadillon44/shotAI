// The Home projects list — owns its sort / tab / multi-select / inline-rename /
// per-row-busy state and the row + bulk actions (open / rename / reveal / export /
// delete / archive). App owns the `projects` array (all projects, both tabs) and
// re-lists via onChanged() after any mutation.
import React from 'react';
import type { ExportFormat } from '../../shared/ipc';
import type { ProjectSummary } from '../../shared/project';
import { OverflowMenu, type MenuItem } from './OverflowMenu';
import { ensureFlattened } from './sop-prepare';
import { useConfirm } from '../useConfirm';
import { groupByDate } from './date-groups';

type SortKey = 'name' | 'created' | 'modified';
const SORT_LABELS: { key: SortKey; label: string }[] = [
  { key: 'name', label: 'Name' },
  { key: 'created', label: 'Created' },
  { key: 'modified', label: 'Modified' },
];

// Formats offered in the bulk-export picker (each selected project exports to its
// own export/ folder). Mirrors the per-row menu minus the niche paste helper.
const BULK_FORMATS: [ExportFormat, string][] = [
  ['html', 'HTML'],
  ['docx', 'Word'],
  ['pptx', 'PowerPoint'],
  ['pdf', 'PDF'],
  ['markdown', 'Markdown'],
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
  /** Called after a mutation (rename/delete/export/archive) so App re-lists. */
  onChanged: () => Promise<void> | void;
  /** Surface an error through App's shared notice. */
  onError: (e: unknown) => void;
  /** Import a shared project package (.zip). Renders the Import button when set. */
  onImport?: () => void;
}): React.JSX.Element {
  const [sortKey, setSortKey] = React.useState<SortKey>('modified');
  const [sortAsc, setSortAsc] = React.useState(false);
  const [tab, setTab] = React.useState<'active' | 'archive'>('active');
  const [query, setQuery] = React.useState('');
  const [selected, setSelected] = React.useState<Set<string>>(new Set());
  const [lastClicked, setLastClicked] = React.useState<string | null>(null);
  const [exportPickerOpen, setExportPickerOpen] = React.useState(false);
  const [renamingPath, setRenamingPath] = React.useState<string | null>(null);
  const [renameValue, setRenameValue] = React.useState('');
  const [rowBusyPath, setRowBusyPath] = React.useState<string | null>(null);
  const [bulkBusy, setBulkBusy] = React.useState(false);
  const { confirm, confirmModal } = useConfirm();

  const activeCount = React.useMemo(() => projects.filter((p) => !p.archived).length, [projects]);
  const archiveCount = React.useMemo(() => projects.filter((p) => p.archived).length, [projects]);

  const clearSelection = React.useCallback(() => {
    setSelected(new Set());
    setLastClicked(null);
    setExportPickerOpen(false);
  }, []);

  // Switching tabs starts a fresh selection (paths don't carry across tabs).
  const switchTab = (t: 'active' | 'archive') => {
    if (t === tab) return;
    setTab(t);
    clearSelection();
  };

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
      const { projectId, manifest } = await window.shotai.projects.open(p.path);
      await ensureFlattened(projectId, p.path, manifest.steps);
      await window.shotai.projects.export(p.path, format);
      await onChanged();
    } catch (e) {
      onError(e);
    } finally {
      setRowBusyPath(null);
    }
  };
  const doArchive = async (p: ProjectSummary, archive: boolean) => {
    if (rowBusyPath) return;
    setRowBusyPath(p.path);
    try {
      if (archive) await window.shotai.projects.archive(p.path);
      else await window.shotai.projects.unarchive(p.path);
      await onChanged();
    } catch (e) {
      onError(e);
    } finally {
      setRowBusyPath(null);
    }
  };

  const filtered = React.useMemo(
    () => projects.filter((p) => (tab === 'archive' ? p.archived : !p.archived)),
    [projects, tab],
  );

  // Normalized search term; empty when not searching.
  const q = query.trim().toLowerCase();
  const searching = q.length > 0;
  const titleMatches = React.useCallback(
    (p: ProjectSummary) => p.title.toLowerCase().includes(q),
    [q],
  );

  // Search filter: keep projects whose title OR in-content text matches. Empty
  // query passes everything through unchanged.
  const searched = React.useMemo(
    () => (searching ? filtered.filter((p) => titleMatches(p) || p.searchText.includes(q)) : filtered),
    [filtered, searching, q, titleMatches],
  );

  const sorted = React.useMemo(() => {
    const arr = [...searched];
    arr.sort((a, b) => {
      let cmp: number;
      if (sortKey === 'name') cmp = a.title.localeCompare(b.title, undefined, { sensitivity: 'base' });
      else if (sortKey === 'created') cmp = a.createdAt.localeCompare(b.createdAt);
      else cmp = a.updatedAt.localeCompare(b.updatedAt);
      return sortAsc ? cmp : -cmp;
    });
    return arr;
  }, [searched, sortKey, sortAsc]);

  // Date grouping (F4) applies only to the date sorts; Name sort stays flat.
  // While searching, date grouping is suppressed — results are ranked by
  // relevance instead: title matches first, then content-only matches (each
  // still in the chosen sort order), with a divider between the two tiers.
  const showGroups = sortKey !== 'name' && !searching;
  const groups = React.useMemo<{ label: string; items: ProjectSummary[] }[]>(() => {
    if (searching) {
      const titleHits = sorted.filter((p) => titleMatches(p));
      const contentHits = sorted.filter((p) => !titleMatches(p)); // matched via content only
      const g: { label: string; items: ProjectSummary[] }[] = [];
      if (titleHits.length) g.push({ label: '', items: titleHits });
      if (contentHits.length)
        g.push({ label: titleHits.length ? 'Matches in content' : '', items: contentHits });
      return g;
    }
    if (!showGroups) return [{ label: '', items: sorted }];
    const g = groupByDate(
      sorted,
      (p) => Date.parse(sortKey === 'created' ? p.createdAt : p.updatedAt),
      new Date(),
    );
    // Newest bucket first for descending; oldest first for ascending.
    return (sortAsc ? [...g].reverse() : g) as { label: string; items: ProjectSummary[] }[];
  }, [sorted, searching, titleMatches, showGroups, sortKey, sortAsc]);

  // Visible order (flat, in render order) for shift-range selection — derived
  // from the groups so it tracks the search tiers, not just the raw sort.
  const visibleOrder = React.useMemo(
    () => groups.flatMap((grp) => grp.items.map((p) => p.path)),
    [groups],
  );

  const toggleSelect = (path: string) => {
    setSelected((prev) => {
      const next = new Set(prev);
      if (next.has(path)) next.delete(path);
      else next.add(path);
      return next;
    });
    setLastClicked(path);
  };
  const rangeSelect = (path: string) => {
    const from = lastClicked ? visibleOrder.indexOf(lastClicked) : -1;
    const to = visibleOrder.indexOf(path);
    if (from === -1 || to === -1) {
      toggleSelect(path);
      return;
    }
    const [lo, hi] = from < to ? [from, to] : [to, from];
    setSelected((prev) => {
      const next = new Set(prev);
      for (let i = lo; i <= hi; i++) next.add(visibleOrder[i]);
      return next;
    });
    setLastClicked(path);
  };

  const allSelected = sorted.length > 0 && sorted.every((p) => selected.has(p.path));
  const toggleSelectAll = () => {
    if (allSelected) clearSelection();
    else setSelected(new Set(visibleOrder));
  };

  // Esc clears an active selection.
  React.useEffect(() => {
    if (selected.size === 0) return;
    const onKey = (e: KeyboardEvent) => {
      if (e.key === 'Escape') clearSelection();
    };
    window.addEventListener('keydown', onKey);
    return () => window.removeEventListener('keydown', onKey);
  }, [selected.size, clearSelection]);

  const selectedProjects = () => sorted.filter((p) => selected.has(p.path));

  // Progress for the active bulk op (null when idle). `verb` is a present
  // participle ("Exporting" / "Archiving" / …) shown in the bulk bar.
  const [bulkProgress, setBulkProgress] = React.useState<{
    verb: string;
    done: number;
    total: number;
  } | null>(null);

  const runBulk = async (
    verb: string,
    fn: (p: ProjectSummary) => Promise<unknown>,
  ) => {
    const targets = selectedProjects();
    if (!targets.length) return;
    setBulkBusy(true);
    setBulkProgress({ verb, done: 0, total: targets.length });
    try {
      for (let i = 0; i < targets.length; i++) {
        try {
          await fn(targets[i]);
        } catch (e) {
          onError(e);
        }
        setBulkProgress({ verb, done: i + 1, total: targets.length });
      }
      await onChanged();
      clearSelection();
    } finally {
      setBulkBusy(false);
      setBulkProgress(null);
    }
  };

  const bulkDelete = async () => {
    const n = selected.size;
    if (
      !(await confirm(
        `Delete ${n} project${n === 1 ? '' : 's'}? This removes each project folder and its screenshots.`,
        { confirmLabel: `Delete ${n}`, danger: true },
      ))
    ) {
      return;
    }
    await runBulk('Deleting', (p) => window.shotai.projects.delete(p.path));
  };
  const bulkArchive = () =>
    runBulk(tab === 'archive' ? 'Restoring' : 'Archiving', (p) =>
      tab === 'archive'
        ? window.shotai.projects.unarchive(p.path)
        : window.shotai.projects.archive(p.path),
    );
  const bulkExport = async (format: ExportFormat) => {
    // #37: ask ONCE for a destination folder, then drop every selected project's
    // export into it (no per-file dialog). Cancelling the picker aborts the bulk.
    const destDir = await window.shotai.projects.chooseExportDir();
    if (!destDir) return;
    await runBulk('Exporting', async (p) => {
      const { projectId, manifest } = await window.shotai.projects.open(p.path);
      await ensureFlattened(projectId, p.path, manifest.steps);
      await window.shotai.projects.exportToDir(p.path, format, destDir);
    });
    // Reveal the destination folder ONCE, after every export has finished (bulk
    // suppresses the per-file reveal so it doesn't pop open mid-run).
    await window.shotai.projects.revealExportDir(destDir).catch(() => undefined);
  };

  const anyBusy = rowBusyPath !== null || bulkBusy;

  const renderRow = (p: ProjectSummary): React.JSX.Element => {
    const rowBusy = rowBusyPath === p.path;
    const isSel = selected.has(p.path);
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
      <li key={p.path} className={`project__item${isSel ? ' project__item--sel' : ''}`}>
        <input
          type="checkbox"
          className="project__check"
          aria-label={`Select ${p.title}`}
          checked={isSel}
          onClick={(e) => {
            if (e.shiftKey) {
              e.preventDefault();
              rangeSelect(p.path);
            }
          }}
          onChange={() => toggleSelect(p.path)}
        />
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
            <span className="project__item-title">
              <span className="project__item-name">{p.title}</span>
              <span
                className={`project__badge project__badge--${p.hasSop ? 'ok' : 'draft'}`}
                title={p.hasSop ? 'Claude has written this guide' : 'No SOP generated yet'}
              >
                {p.hasSop ? 'SOP ready' : 'Draft'}
              </span>
            </span>
          )}
          <span className="project__item-meta">
            {rowBusy
              ? 'Working…'
              : `${p.stepCount} step${p.stepCount === 1 ? '' : 's'} · ${
                  tab === 'archive' ? 'archived' : 'modified'
                } ${p.updatedAt ? new Date(p.updatedAt).toLocaleDateString() : '—'}`}
          </span>
        </div>
        <div className="project__item-actions">
          <button
            type="button"
            className="btn btn--small btn--primary"
            disabled={anyBusy}
            onClick={() => onOpen(p.path)}
            title={p.archived ? 'Open (restores the archived project)' : 'Open'}
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
              p.archived
                ? { label: 'Restore', onClick: () => void doArchive(p, false) }
                : { label: 'Archive', onClick: () => void doArchive(p, true) },
              { kind: 'sep' },
              ...exportItems,
              { kind: 'sep' },
              { label: 'Delete', danger: true, onClick: () => void doDelete(p) },
            ]}
          />
        </div>
      </li>
    );
  };

  const emptyLabel =
    tab === 'archive' ? 'No archived projects' : 'No projects yet';

  return (
    <>
      {confirmModal}
      <div className="home__tabs" role="tablist" aria-label="Project sections">
        <button
          type="button"
          role="tab"
          aria-selected={tab === 'active'}
          className={`home__tab${tab === 'active' ? ' home__tab--on' : ''}`}
          onClick={() => switchTab('active')}
        >
          Projects <span className="home__tabcount">{activeCount}</span>
        </button>
        <button
          type="button"
          role="tab"
          aria-selected={tab === 'archive'}
          className={`home__tab${tab === 'archive' ? ' home__tab--on' : ''}`}
          onClick={() => switchTab('archive')}
        >
          Archive <span className="home__tabcount">{archiveCount}</span>
        </button>
      </div>

      <div className="home__listhead">
        <div className="home__listhead-left">
          <h2 className="home__h">
            {tab === 'archive' ? 'Archive' : 'Projects'}{' '}
            <span className="home__count">· {sorted.length}</span>
          </h2>
          {onImport && tab === 'active' && (
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
        <div className="project__search">
          <input
            className="project__input"
            type="search"
            placeholder="Search projects…"
            aria-label="Search projects by title or content"
            value={query}
            onChange={(e) => {
              setQuery(e.target.value);
              // The visible set changes as you type; drop any selection so the
              // bulk-bar count can't go stale against rows that filtered out.
              if (selected.size) clearSelection();
            }}
            onKeyDown={(e) => {
              // Esc clears the query first (before the global selection-clear).
              if (e.key === 'Escape' && query) {
                e.stopPropagation();
                setQuery('');
              }
            }}
          />
          {query && (
            <button
              type="button"
              className="project__search-clear"
              aria-label="Clear search"
              title="Clear search"
              onClick={() => setQuery('')}
            >
              ✕
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

      {selected.size > 0 && (
        <div className="project__bulk" role="region" aria-label="Bulk actions">
          <button
            type="button"
            className="project__bulk-all"
            onClick={toggleSelectAll}
            aria-pressed={allSelected}
          >
            <span className={`project__check-box${allSelected ? ' project__check-box--on' : ''}`} aria-hidden="true" />
            {allSelected ? 'Clear all' : 'Select all'}
          </button>
          <span className="project__bulk-count" role="status" aria-live="polite">
            {bulkProgress
              ? `${bulkProgress.verb} ${bulkProgress.done} of ${bulkProgress.total}…`
              : `${selected.size} selected`}
          </span>
          <span className="project__bulk-spacer" />
          {exportPickerOpen ? (
            <>
              <span className="project__bulk-hint">Export as</span>
              {BULK_FORMATS.map(([f, label]) => (
                <button
                  key={f}
                  type="button"
                  className="btn btn--small"
                  disabled={anyBusy}
                  onClick={() => void bulkExport(f)}
                >
                  {label}
                </button>
              ))}
              <button
                type="button"
                className="btn btn--small btn--ghost"
                onClick={() => setExportPickerOpen(false)}
              >
                Cancel
              </button>
            </>
          ) : (
            <>
              <button
                type="button"
                className="btn btn--small"
                disabled={anyBusy}
                onClick={() => void bulkArchive()}
              >
                {tab === 'archive' ? '⤴ Restore' : '🗄 Archive'}
              </button>
              <button
                type="button"
                className="btn btn--small"
                disabled={anyBusy}
                onClick={() => setExportPickerOpen(true)}
              >
                ⤓ Export
              </button>
              <button
                type="button"
                className="btn btn--small btn--danger"
                disabled={anyBusy}
                onClick={() => void bulkDelete()}
              >
                🗑 Delete
              </button>
              <button
                type="button"
                className="btn btn--small btn--ghost"
                onClick={clearSelection}
              >
                Clear
              </button>
            </>
          )}
        </div>
      )}

      {sorted.length === 0 && searching ? (
        <div className="empty">
          <div className="empty__icon" aria-hidden="true">
            🔍
          </div>
          <p className="empty__line">No projects match “{query.trim()}”</p>
          <p className="empty__sub">
            Search looks at the project title and the text inside it (step captions, notes,
            and the SOP overview){tab === 'archive' ? ', in the Archive tab' : ''}.
          </p>
        </div>
      ) : sorted.length === 0 ? (
        <div className="empty">
          <div className="empty__icon" aria-hidden="true">
            {tab === 'archive' ? '🗄️' : '🗂️'}
          </div>
          <p className="empty__line">{emptyLabel}</p>
          {tab === 'active' && (
            <p className="empty__sub">
              Create a project above: press <b>Capture ▸</b> to record a process, or{' '}
              <b>Empty Project</b> to build one from images and text.
            </p>
          )}
          {tab === 'archive' && (
            <p className="empty__sub">
              Projects you haven’t touched in a while land here (or archive them yourself).
              Opening one restores it automatically.
            </p>
          )}
        </div>
      ) : (
        <ul className="project__list">
          {groups.map((group) => (
            <React.Fragment key={group.label || 'all'}>
              {group.label && (
                <li className="project__group" aria-hidden="true">
                  {group.label}
                </li>
              )}
              {group.items.map((p) => renderRow(p))}
            </React.Fragment>
          ))}
        </ul>
      )}
    </>
  );
}
