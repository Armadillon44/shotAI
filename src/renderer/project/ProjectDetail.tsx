// The project-detail view: a header bar (back / title / step count) over the
// in-app report, plus the inline Konva editor as an overlay when a step is being
// edited. Shown when a project is open (store.projectPath set).
import React from 'react';
import type { CalloutKind, CaptureTarget, ProjectStep } from '../../shared/project';
import type { ExportFormat } from '../../shared/ipc';
import { useProjectStore } from './store';
import { ensureFlattened } from './sop-prepare';
import { Report, type InsertKind } from './Report';
import { CaptureInsertModal, type CaptureInsertVariant } from './CaptureInsertModal';
import { SopPanel } from './SopPanel';
import { Editor } from '../editor/Editor';
import { Notice } from '../Notice';

const EXPORT_LABEL: Record<ExportFormat, string> = {
  html: 'HTML',
  'html-plain': 'HTML (for Word)',
  pdf: 'PDF',
  markdown: 'Markdown',
  docx: 'Word',
  pptx: 'PowerPoint',
};

export function ProjectDetail({
  onResumeCapture,
  onCaptureInsert,
  onOpenSettings,
}: {
  /** Resume capturing into this project (wired to App's capture flow). */
  onResumeCapture?: () => void;
  /** Start a recording that INSERTS its steps at a report gap (report "+ Capture").
   *  Routed through App so the recording HUD / adopt / stop-reload all fire there. */
  onCaptureInsert?: (atIndex: number, target: CaptureTarget) => void;
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
  // Single-shot reset for autoEditId: once Report has opened the freshly-added
  // step's editor it calls this, so a stale trigger can't re-open (and re-lock)
  // the editor on a later re-render/remount (B4). Stable ref so Report's effect
  // deps stay quiet.
  const clearAutoEdit = React.useCallback(() => setAutoEditId(null), []);
  // True while a text step is being inline-edited in the report; we disable
  // structural actions (resume/add/import) so they can't discard the draft.
  const [textEditing, setTextEditing] = React.useState(false);
  // The report "+ Capture / + Screenshot" mode-pick modal: which gap + which
  // variant is being configured (null = closed).
  const [captureModal, setCaptureModal] = React.useState<{
    atIndex: number;
    variant: CaptureInsertVariant;
  } | null>(null);
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
  // Shareable-package export: a small dialog picks redacted-only (default) vs.
  // include-originals (full editing, recoverable redactions).
  const [packageDialog, setPackageDialog] = React.useState(false);
  const [includeOriginals, setIncludeOriginals] = React.useState(false);
  const [packageBusy, setPackageBusy] = React.useState(false);
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

  const doExportPackage = async () => {
    if (!projectPath || !projectId) return;
    exportAbortRef.current?.abort();
    const controller = new AbortController();
    exportAbortRef.current = controller;
    setPackageDialog(false);
    setExportErr(null);
    setPackageBusy(true);
    try {
      // Flatten first so the safe (redacted-only) package has current baked renders
      // to collapse to — same fail-closed guarantee as every other export.
      const flattened = await ensureFlattened(projectId, projectPath, steps, controller.signal);
      if (flattened) applyManifest(flattened);
      await window.shotai.projects.exportPackage(projectPath, includeOriginals);
    } catch (err) {
      if (err instanceof DOMException && err.name === 'AbortError') return; // left the project
      setExportErr(err instanceof Error ? err.message : String(err));
    } finally {
      if (exportAbortRef.current === controller) exportAbortRef.current = null;
      setPackageBusy(false);
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

  // One-shot, no-click screenshot inserted at `atIndex` (report "+ Screenshot").
  // Synchronous in the main process (hide → grab → insert), so it returns the
  // updated manifest and we apply it directly — no recording state, no HUD, and
  // an open text draft is preserved (the report doesn't unmount).
  const screenshotAt = async (atIndex: number, target: CaptureTarget) => {
    if (!projectPath) return;
    setImportErr(null);
    try {
      const manifest = await window.shotai.capture.screenshot(projectPath, target, atIndex);
      applyManifest(manifest);
    } catch (err) {
      setImportErr(err instanceof Error ? err.message : String(err));
    }
  };

  const onInsert = (atIndex: number, kind: InsertKind) => {
    if (kind === 'text') void addTextAt(atIndex);
    else if (kind === 'image') pickImageAt(atIndex);
    else if (kind === 'capture' || kind === 'screenshot')
      setCaptureModal({ atIndex, variant: kind });
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
          <SopPanel sopEnabled={sopEnabled} onOpenSettings={onOpenSettings} />
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
              disabled={!hasShots || exporting !== null || packageBusy || textEditing || importing}
              onClick={() => setExportMenuOpen((o) => !o)}
              title={
                !hasShots
                  ? 'Add a screenshot before exporting'
                  : textEditing
                    ? 'Finish editing the text step first'
                    : 'Export as HTML, Word, PowerPoint, PDF, Markdown, or a shareable package'
              }
            >
              {packageBusy
                ? 'Packaging…'
                : exporting
                  ? `Exporting ${EXPORT_LABEL[exporting]}…`
                  : '⬇ Export'}
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
                  HTML <span className="export__hint">one self-contained file — best for sharing</span>
                </button>
                <button
                  type="button"
                  role="menuitem"
                  className="export__item"
                  disabled={exporting !== null}
                  onClick={() => void doExport('docx')}
                >
                  Word <span className="export__hint">.docx — edit in Microsoft Word</span>
                </button>
                <button
                  type="button"
                  role="menuitem"
                  className="export__item"
                  disabled={exporting !== null}
                  onClick={() => void doExport('pptx')}
                >
                  PowerPoint <span className="export__hint">.pptx — one slide per step</span>
                </button>
                <button
                  type="button"
                  role="menuitem"
                  className="export__item"
                  disabled={exporting !== null}
                  onClick={() => void doExport('html-plain')}
                >
                  HTML (for Word){' '}
                  <span className="export__hint">paste into Word or Google Docs to edit</span>
                </button>
                <button
                  type="button"
                  role="menuitem"
                  className="export__item"
                  disabled={exporting !== null}
                  onClick={() => void doExport('pdf')}
                >
                  PDF <span className="export__hint">print-ready — best for printing</span>
                </button>
                <button
                  type="button"
                  role="menuitem"
                  className="export__item"
                  disabled={exporting !== null}
                  onClick={() => void doExport('markdown')}
                >
                  Markdown <span className="export__hint">.md + images/ — for wikis &amp; version control</span>
                </button>
                <div className="export__sep" role="separator" />
                <button
                  type="button"
                  role="menuitem"
                  className="export__item"
                  disabled={exporting !== null}
                  onClick={() => {
                    setExportMenuOpen(false);
                    setPackageDialog(true);
                  }}
                >
                  Project package{' '}
                  <span className="export__hint">.zip — re-open &amp; edit in shotAI</span>
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

      {packageDialog && (
        <div
          className="sop__overlay"
          role="dialog"
          aria-label="Export a shareable project package"
          onClick={() => setPackageDialog(false)}
        >
          <div className="sop__modal" onClick={(e) => e.stopPropagation()}>
            <h3 className="sop__modal-title">Share this project</h3>
            <p className="sop__warn">
              Exports a <strong>.zip</strong> another shotAI user can import and
              edit. By default it ships only the redaction-baked images, so blurred
              or cropped-out content stays hidden.
            </p>
            <label className="pkg__opt">
              <input
                type="checkbox"
                checked={includeOriginals}
                onChange={(e) => setIncludeOriginals(e.target.checked)}
              />
              <span>
                <strong>Include original screenshots</strong> — lets the recipient
                fully re-edit (re-crop, adjust or remove blur). ⚠ Blurred/redacted
                content becomes <strong>recoverable</strong> from the package. Only
                for people you trust with the raw captures.
              </span>
            </label>
            <div className="sop__modal-actions">
              <button type="button" className="btn" onClick={() => setPackageDialog(false)}>
                Cancel
              </button>
              <button
                type="button"
                className="btn btn--primary"
                onClick={() => void doExportPackage()}
              >
                {includeOriginals ? 'Export with originals' : 'Export (redacted)'}
              </button>
            </div>
          </div>
        </div>
      )}

      {captureModal && (
        <CaptureInsertModal
          variant={captureModal.variant}
          onClose={() => setCaptureModal(null)}
          onConfirm={(target) => {
            const m = captureModal;
            setCaptureModal(null);
            if (!m) return;
            if (m.variant === 'screenshot') {
              void screenshotAt(m.atIndex, target);
            } else {
              // +Capture starts a full recording, which unmounts the report — bail
              // if a text draft is open (same rule as Resume capturing).
              if (textEditing) {
                setImportErr('Finish editing the text step before capturing.');
                return;
              }
              onCaptureInsert?.(m.atIndex, target);
            }
          }}
        />
      )}

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
          onAutoEditConsumed={clearAutoEdit}
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
