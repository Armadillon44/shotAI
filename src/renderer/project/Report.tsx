// The in-app report: each step rendered as its screenshot with an overlaid
// click-register marker + caption + note. (Phase 2a — read-only view; the
// Konva editor and structural edits come in 2b/2c.)
import React from 'react';
import type { ProjectStep } from '../../shared/project';
import { shotUrl, useProjectStore } from './store';

/**
 * One step's screenshot with a marker at the click point. The marker is placed
 * as a fraction of the image's natural pixel size (captured on load), so it
 * tracks the image as it scales. Exact DPI calibration is deferred to Phase 4.
 */
function StepFigure({
  projectId,
  step,
}: {
  projectId: string;
  step: ProjectStep;
}): React.JSX.Element {
  const [dims, setDims] = React.useState<{ w: number; h: number } | null>(null);

  const marker =
    dims && step.click && dims.w > 0 && dims.h > 0
      ? {
          left: `${(step.click.image.x / dims.w) * 100}%`,
          top: `${(step.click.image.y / dims.h) * 100}%`,
        }
      : null;

  return (
    <figure className="rep__figure">
      <div className="rep__imgwrap">
        <img
          className="rep__img"
          src={shotUrl(projectId, step.screenshot)}
          alt={step.caption}
          loading="lazy"
          onLoad={(e) =>
            setDims({
              w: e.currentTarget.naturalWidth,
              h: e.currentTarget.naturalHeight,
            })
          }
        />
        {marker && step.click && (
          <span
            className={`rep__marker rep__marker--${step.click.button}`}
            style={marker}
            aria-hidden="true"
          />
        )}
      </div>
    </figure>
  );
}

export function Report(): React.JSX.Element | null {
  const projectId = useProjectStore((s) => s.projectId);
  const steps = useProjectStore((s) => s.steps);

  if (!projectId) return null;
  if (steps.length === 0) {
    return (
      <p className="project__hint">
        No steps yet. Resume capturing to add steps to this project.
      </p>
    );
  }

  return (
    <ol className="rep">
      {steps.map((s) => (
        <li key={s.id} className="rep__step">
          <div className="rep__num" aria-hidden="true">
            {s.order}
          </div>
          <div className="rep__bodywrap">
            <h3 className="rep__caption">{s.caption}</h3>
            <StepFigure projectId={projectId} step={s} />
            {s.note && <p className="rep__note">{s.note}</p>}
            {s.window && (
              <p className="rep__meta">
                {s.window.app}
                {s.window.title ? ` — ${s.window.title}` : ''}
              </p>
            )}
          </div>
        </li>
      ))}
    </ol>
  );
}
