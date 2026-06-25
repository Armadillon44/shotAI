// The project-detail view: a header bar (back / title / step count) over the
// in-app report, plus the inline Konva editor as an overlay when a step is being
// edited. Shown when a project is open (store.projectPath set).
import React from 'react';
import type { ProjectStep } from '../../shared/project';
import { useProjectStore } from './store';
import { Report } from './Report';
import { Editor } from '../editor/Editor';

export function ProjectDetail(): React.JSX.Element {
  const projectId = useProjectStore((s) => s.projectId);
  const projectPath = useProjectStore((s) => s.projectPath);
  const title = useProjectStore((s) => s.title);
  const steps = useProjectStore((s) => s.steps);
  const error = useProjectStore((s) => s.error);
  const loading = useProjectStore((s) => s.loading);
  const close = useProjectStore((s) => s.close);
  const applyManifest = useProjectStore((s) => s.applyManifest);

  const [editing, setEditing] = React.useState<ProjectStep | null>(null);

  // Only screenshot steps open the image editor (text steps get a text editor in 2c).
  const onEditStep = (step: ProjectStep) => {
    if (step.kind === 'text') return;
    setEditing(step);
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
        <span className="detail__count">
          {steps.length} step{steps.length === 1 ? '' : 's'}
        </span>
      </div>

      {error && <p className="project__error">Error: {error}</p>}
      {loading ? (
        <p className="project__hint">Loading…</p>
      ) : (
        <Report onEditStep={onEditStep} />
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
