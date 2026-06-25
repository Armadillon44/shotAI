// The project-detail view: a header bar (back / title / step count) over the
// in-app report, plus the inline Konva editor as an overlay when a step is being
// edited. Shown when a project is open (store.projectPath set).
import React from 'react';
import type { ProjectStep } from '../../shared/project';
import { useProjectStore } from './store';
import { Report } from './Report';
import { Editor } from '../editor/Editor';

export function ProjectDetail({
  onResumeCapture,
}: {
  /** Resume capturing into this project (wired to App's capture flow). */
  onResumeCapture?: () => void;
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

  // Only screenshot steps open the image editor (text steps edit inline in the report).
  const onEditStep = (step: ProjectStep) => {
    if (step.kind === 'text') return;
    setEditing(step);
  };

  const onAddText = async () => {
    if (!projectPath) return;
    setImportErr(null);
    try {
      const manifest = await window.shotai.projects.addTextStep(
        projectPath,
        steps.length,
      );
      applyManifest(manifest);
      // Open the newly appended text step straight into its inline editor.
      const added = manifest.steps[manifest.steps.length - 1];
      if (added) setAutoEditId(added.id);
    } catch (err) {
      setImportErr(err instanceof Error ? err.message : String(err));
    }
  };

  const onImportFile = async (e: React.ChangeEvent<HTMLInputElement>) => {
    const file = e.target.files?.[0];
    e.target.value = ''; // allow re-picking the same file
    if (!file || !projectPath) return;
    setImporting(true);
    setImportErr(null);
    try {
      const bytes = new Uint8Array(await file.arrayBuffer());
      // Main also validates by magic bytes; this is a fast client-side reject.
      const manifest = await window.shotai.projects.importStep(projectPath, bytes);
      applyManifest(manifest);
    } catch (err) {
      setImportErr(err instanceof Error ? err.message : String(err));
    } finally {
      setImporting(false);
    }
  };

  return (
    <section className="detail">
      <div className="detail__bar">
        <button type="button" className="btn btn--small" onClick={close}>
          ← Back
        </button>
        <h2 className="detail__title" title={title}>
          {title}
        </h2>
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
          disabled={textEditing}
          onClick={() => void onAddText()}
          title={
            textEditing
              ? 'Finish editing the text step first'
              : 'Add a text-only step (heading + body) for instructions between screenshots'
          }
        >
          + Text step
        </button>
        <button
          type="button"
          className="btn btn--small"
          disabled={importing || textEditing}
          onClick={() => fileRef.current?.click()}
          title={
            textEditing
              ? 'Finish editing the text step first'
              : 'Add your own PNG/JPEG image as a new step'
          }
        >
          {importing ? 'Importing…' : 'Import image'}
        </button>
        <input
          ref={fileRef}
          type="file"
          accept="image/png,image/jpeg"
          style={{ display: 'none' }}
          onChange={onImportFile}
        />
        <span className="detail__count">
          {steps.length} step{steps.length === 1 ? '' : 's'}
        </span>
      </div>

      {importErr && <p className="project__error">Import failed: {importErr}</p>}
      {error && <p className="project__error">Error: {error}</p>}
      {loading ? (
        <p className="project__hint">Loading…</p>
      ) : (
        <Report
          onEditStep={onEditStep}
          autoEditId={autoEditId}
          onEditingChange={setTextEditing}
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
