// The project-detail view: a header bar (back / title / step count) over the
// in-app report. Shown when a project is open (store.projectPath set).
import React from 'react';
import { useProjectStore } from './store';
import { Report } from './Report';

export function ProjectDetail(): React.JSX.Element {
  const title = useProjectStore((s) => s.title);
  const steps = useProjectStore((s) => s.steps);
  const error = useProjectStore((s) => s.error);
  const loading = useProjectStore((s) => s.loading);
  const close = useProjectStore((s) => s.close);

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
        <Report />
      )}
    </section>
  );
}
