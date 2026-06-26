// The project-detail view: a header bar (back / title / step count) over the
// in-app report, plus the inline Konva editor as an overlay when a step is being
// edited. Shown when a project is open (store.projectPath set).
import React from 'react';
import type { ProjectStep } from '../../shared/project';
import { useProjectStore } from './store';
import { Report } from './Report';
import { SopPanel } from './SopPanel';
import { Editor } from '../editor/Editor';

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

  // Only screenshot steps open the image editor (text steps edit inline in the report).
  const onEditStep = (step: ProjectStep) => {
    if (step.kind === 'text') return;
    setEditing(step);
  };

  const addTextAt = async (atIndex: number) => {
    if (!projectPath) return;
    setImportErr(null);
    try {
      const manifest = await window.shotai.projects.addTextStep(projectPath, atIndex);
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

  const onInsert = (atIndex: number, kind: 'text' | 'image' | 'shot') => {
    if (kind === 'text') void addTextAt(atIndex);
    else if (kind === 'image') pickImageAt(atIndex);
    else void captureSingleAt(atIndex);
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
          <button
            type="button"
            className="btn btn--small"
            disabled={importing || textEditing}
            onClick={() => pickImageAt(null)}
            title={
              textEditing
                ? 'Finish editing the text step first'
                : 'Add your own PNG/JPEG image as a new step'
            }
          >
            {importing ? 'Importing…' : 'Import image'}
          </button>
        </div>
        <input
          ref={fileRef}
          type="file"
          accept="image/png,image/jpeg"
          style={{ display: 'none' }}
          onChange={onImportFile}
        />
      </div>

      {importErr && <p className="project__error">Import failed: {importErr}</p>}
      {error && <p className="project__error">Error: {error}</p>}
      {loading ? (
        <p className="project__hint">Loading…</p>
      ) : (
        <>
          <SopPanel sopEnabled={sopEnabled} />
          <Report
            onEditStep={onEditStep}
            autoEditId={autoEditId}
            onEditingChange={setTextEditing}
            onInsert={onInsert}
          />
        </>
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
