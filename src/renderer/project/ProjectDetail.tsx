// The project-detail view: a header bar (back / title / step count) over the
// in-app report, plus the inline Konva editor as an overlay when a step is being
// edited. Shown when a project is open (store.projectPath set).
import React from 'react';
import type { CalloutKind, ProjectStep } from '../../shared/project';
import type { ExportFormat } from '../../shared/ipc';
import { useProjectStore } from './store';
import { ensureFlattened } from './sop-prepare';
import { Report, type InsertKind } from './Report';
import { SopPanel } from './SopPanel';
import { Editor } from '../editor/Editor';
import { Notice } from '../Notice';

const EXPORT_LABEL: Record<ExportFormat, string> = {
  html: 'HTML',
  'html-plain': 'HTML (for Word)',
  pdf: 'PDF',
  markdown: 'Markdown',
};

export function ProjectDetail({
  onResumeCapture,
  onOpenSettings,
}: {
  /** Resume capturing into this project (wired to App's capture flow). */
  onResumeCapture?: () => void;
  /** Open the Settings panel without leaving the project (model/tone/key). */
  onOpenSettings?: () => void;
}): React.JSX.Element {
  const projectId = useProjectStore((s) => s.projectId);
  const projectPath = useProjectStore((s) => s.projectPath);
  const title = useProjectStore((s) => s.title);
  const steps = useProjectStore((s) => s.steps);
  const error = useProjectStore((s) => s.error);
  const loading = useProjectStore((s) => s.loading);
  const close = useProjectStore((s) => s.close);
  const applyManifest = useProjectStore((s) => s.applyManifest);

  const [editing, setEditing] = React.useState<ProjectStep | null>(null);
  const [importing, setImporting] = React.useState(false);
  const [importErr, setImportErr] = React.useState<string | null>(null);
  const [autoEditId, setAutoEditId] = React.useState<string | null>(null);
  // True while a text step is being inline-edited in the report; we disable
  // structural actions (resume/add/import) so they can't discard the draft.
  const [textEditing, setTextEditing] = React.useState(false);
  const fileRef = React.useRef<HTMLInputElement | null>(null);
  // Where the next image import lands (set just before opening the file dialog);
  // null → append. Lets one hidden <input> serve both the header button and the
  // per-gap insert affordance.
  const pendingInsertRef = React.useRef<number | null>(null);

  // Whether AI SOP generation is enabled (gates the Generate control). The SOP is
  // applied IN-LINE to the steps, so the report below always renders the result.
  const [sopEnabled, setSopEnabled] = React.useState(false);
  React.useEffect(() => {
    window.shotai.settings
      .getSop()
      .then((s) => setSopEnabled(s.enabled))
      .catch(() => undefined);
  }, []);

  // Export menu (HTML / PDF / Markdown). Export is independent of AI — it works on
  // the report whether or not Claude was run.
  const [exportMenuOpen, setExportMenuOpen] = React.useState(false);
  const [exporting, setExporting] = React.useState<ExportFormat | null>(null);
  const [exportErr, setExportErr] = React.useState<string | null>(null);
  const exportRef = React.useRef<HTMLDivElement | null>(null);
  // Aborts an in-flight flatten if the user leaves the project mid-export.
  const exportAbortRef = React.useRef<AbortController | null>(null);
  const hasShots = steps.some((s) => s.kind !== 'text');

  React.useEffect(
    () => () => exportAbortRef.current?.abort(),
    [projectId],
  );

  React.useEffect(() => {
    if (!exportMenuOpen) return;
    const onDoc = (e: MouseEvent) => {
      if (exportRef.current && !exportRef.current.contains(e.target as Node)) {
        setExportMenuOpen(false);
      }
    };
    const onKey = (e: KeyboardEvent) => {
      if (e.key === 'Escape') setExportMenuOpen(false);
    };
    document.addEventListener('mousedown', onDoc);
    document.addEventListener('keydown', onKey);
    return () => {
      document.removeEventListener('mousedown', onDoc);
      document.removeEventListener('keydown', onKey);
    };
  }, [exportMenuOpen]);

  const doExport = async (format: ExportFormat) => {
    if (!projectPath || !projectId) return;
    exportAbortRef.current?.abort();
    const controller = new AbortController();
    exportAbortRef.current = controller;
    setExportMenuOpen(false);
    setExportErr(null);
    setExporting(format);
    try {
      // Flatten first so only redacted, marker-baked renders are written/embedded
      // (export refuses raw screenshots for any step with a redaction/crop).
      const flattened = await ensureFlattened(projectId, projectPath, steps, controller.signal);
      if (flattened) applyManifest(flattened);
      await window.shotai.projects.export(projectPath, format);
    } catch (err) {
      if (err instanceof DOMException && err.name === 'AbortError') return; // left the project
      setExportErr(err instanceof Error ? err.message : String(err));
    } finally {
      if (exportAbortRef.current === controller) exportAbortRef.current = null;
      setExporting(null);
    }
  };

  // Only screenshot steps open the image editor (text steps edit inline in the report).
  const onEditStep = (step: ProjectStep) => {
    if (step.kind === 'text') return;
    setEditing(step);
  };

  const addTextAt = async (atIndex: number, callout?: CalloutKind) => {
    if (!projectPath) return;
    setImportErr(null);
    try {
      const manifest = await window.shotai.projects.addTextStep(projectPath, atIndex, callout);
      applyManifest(manifest);
      // Identify the inserted step deterministically from the RETURNED manifest:
      // the store clamps the insert index to the old length, so the new step sits
      // at clamp(atIndex, 0, newLen-1). (Reverse-engineering it by diffing a
      // possibly-stale `steps` snapshot could open the wrong/no step on a rapid
      // double-insert.) Only auto-open if it really is a fresh empty text step.
      const i = Math.max(0, Math.min(Math.round(atIndex), manifest.steps.length - 1));
      const added = manifest.steps[i];
      if (added && added.kind === 'text' && !added.heading && !added.body) {
        setAutoEditId(added.id);
      }
    } catch (err) {
      setImportErr(err instanceof Error ? err.message : String(err));
    }
  };

  const pickImageAt = (atIndex: number | null) => {
    pendingInsertRef.current = atIndex;
    fileRef.current?.click();
  };

  const onImportFile = async (e: React.ChangeEvent<HTMLInputElement>) => {
    const file = e.target.files?.[0];
    e.target.value = ''; // allow re-picking the same file
    const atIndex = pendingInsertRef.current;
    pendingInsertRef.current = null;
    if (!file || !projectPath) return;
    setImporting(true);
    setImportErr(null);
    try {
      const bytes = new Uint8Array(await file.arrayBuffer());
      // Main also validates by magic bytes; this is a fast client-side reject.
      const manifest = await window.shotai.projects.importStep(
        projectPath,
        bytes,
        atIndex ?? undefined,
      );
      applyManifest(manifest);
    } catch (err) {
      setImportErr(err instanceof Error ? err.message : String(err));
    } finally {
      setImporting(false);
    }
  };

  // Record a single screenshot inserted at `atIndex`. This flips capture state to
  // "recording" (App hides the window + shows the pill); the first click is
  // captured, inserted, and the session auto-stops, after which App reloads the
  // report. No HUD seeding needed here.
  const captureSingleAt = async (atIndex: number) => {
    if (!projectPath) return;
    setImportErr(null);
    try {
      await window.shotai.capture.captureSingle(projectPath, atIndex);
    } catch (err) {
      setImportErr(err instanceof Error ? err.message : String(err));
    }
  };

  const onInsert = (atIndex: number, kind: InsertKind) => {
    if (kind === 'text') void addTextAt(atIndex);
    else if (kind === 'image') pickImageAt(atIndex);
    else if (kind === 'shot') void captureSingleAt(atIndex);
    else void addTextAt(atIndex, kind); // 'note' | 'caution' | 'warning'
  };

  return (
    <section className="detail">
      <div className="detail__bar">
        <div className="detail__barhead">
          <button type="button" className="btn btn--small" onClick={close}>
            ← Back
          </button>
          <h2 className="detail__title" title={title}>
            {title}
          </h2>
          <span className="detail__count">
            {steps.length} step{steps.length === 1 ? '' : 's'}
          </span>
        </div>
        <div className="detail__baractions">
          {/* SOP generate/revert folds into this one command bar (its review /
              progress modals are fixed overlays, unaffected by placement). */}
          <SopPanel sopEnabled={sopEnabled} />
          {onOpenSettings && (
            <button
              type="button"
              className="btn btn--small"
              onClick={onOpenSettings}
              title="Settings — Claude model, tone, API key"
            >
              ⚙ Settings
            </button>
          )}
          {onResumeCapture && (
            <button
              type="button"
              className="btn btn--small"
              disabled={textEditing}
              onClick={onResumeCapture}
              title={
                textEditing
                  ? 'Finish editing the text step first'
                  : 'Resume capturing — click through more steps; they append to this project'
              }
            >
              ⏺ Resume capturing
            </button>
          )}
          <div className="export" ref={exportRef}>
            <button
              type="button"
              className="btn btn--small"
              disabled={!hasShots || exporting !== null || textEditing || importing}
              onClick={() => setExportMenuOpen((o) => !o)}
              title={
                !hasShots
                  ? 'Add a screenshot before exporting'
                  : textEditing
                    ? 'Finish editing the text step first'
                    : 'Export this report as HTML, PDF, or Markdown'
              }
            >
              {exporting ? `Exporting ${EXPORT_LABEL[exporting]}…` : '⬇ Export'}
            </button>
            {exportMenuOpen && (
              <div className="export__menu" role="menu">
                <button
                  type="button"
                  role="menuitem"
                  className="export__item"
                  disabled={exporting !== null}
                  onClick={() => void doExport('html')}
                >
                  HTML <span className="export__hint">single self-contained file</span>
                </button>
                <button
                  type="button"
                  role="menuitem"
                  className="export__item"
                  disabled={exporting !== null}
                  onClick={() => void doExport('html-plain')}
                >
                  HTML (for Word){' '}
                  <span className="export__hint">minimal formatting — paste into a doc</span>
                </button>
                <button
                  type="button"
                  role="menuitem"
                  className="export__item"
                  disabled={exporting !== null}
                  onClick={() => void doExport('pdf')}
                >
                  PDF <span className="export__hint">print-ready document</span>
                </button>
                <button
                  type="button"
                  role="menuitem"
                  className="export__item"
                  disabled={exporting !== null}
                  onClick={() => void doExport('markdown')}
                >
                  Markdown <span className="export__hint">.md + images/ folder</span>
                </button>
              </div>
            )}
          </div>
        </div>
        <input
          ref={fileRef}
          type="file"
          accept="image/png,image/jpeg"
          style={{ display: 'none' }}
          onChange={onImportFile}
        />
      </div>

      {(importErr || exportErr || error) && (
        <div className="notice-stack">
          {importErr && (
            <Notice kind="error" onDismiss={() => setImportErr(null)}>
              Import failed: {importErr}
            </Notice>
          )}
          {exportErr && (
            <Notice kind="error" onDismiss={() => setExportErr(null)}>
              Export failed: {exportErr}
            </Notice>
          )}
          {error && (
            <Notice kind="error" onDismiss={() => useProjectStore.setState({ error: null })}>
              Error: {error}
            </Notice>
          )}
        </div>
      )}
      {loading ? (
        <p className="project__hint">Loading…</p>
      ) : (
        <Report
          onEditStep={onEditStep}
          autoEditId={autoEditId}
          onEditingChange={setTextEditing}
          onInsert={onInsert}
        />
      )}

      {editing && projectId && projectPath && (
        <div className="ed__overlay" role="dialog" aria-label="Edit screenshot">
          <Editor
            projectId={projectId}
            projectPath={projectPath}
            step={editing}
            onClose={() => setEditing(null)}
            onSaved={(manifest) => {
              applyManifest(manifest);
              setEditing(null);
            }}
          />
        </div>
      )}
    </section>
  );
}
