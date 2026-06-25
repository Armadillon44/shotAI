// The in-app report: each step rendered as its screenshot with an overlaid
// click-register marker + caption + note. Text steps (kind === 'text') render
// as a heading + body block. Prefers the flattened render (annotations baked +
// redaction) over the raw screenshot once a step has been edited.
import React from 'react';
import type { ProjectStep } from '../../shared/project';
import { shotUrl, useProjectStore } from './store';

/**
 * One step's screenshot with a marker at the click point. The marker is placed
 * as a fraction of the image's natural pixel size (captured on load), so it
 * tracks the image as it scales. Exact DPI calibration is deferred to Phase 4.
 * The marker is hidden once the step is flattened (the click point may have been
 * cropped/annotated; the baked image is authoritative).
 */
function StepFigure({
  projectId,
  step,
  version,
}: {
  projectId: string;
  step: ProjectStep;
  version: string;
}): React.JSX.Element {
  const [dims, setDims] = React.useState<{ w: number; h: number } | null>(null);
  const flattened = !!step.flattened;
  // The flattened render is rewritten at the same path on each save, so bust the
  // <img> cache with the manifest version; originals never change.
  const src = flattened
    ? `${shotUrl(projectId, step.flattened as string)}?v=${encodeURIComponent(version)}`
    : shotUrl(projectId, step.screenshot);

  // Place the click marker as a fraction of the DISPLAYED image. The displayed
  // image is the original (full) or the flattened (cropped) render, so subtract
  // the crop origin when flattened+cropped; the marker is hidden if it falls
  // outside the visible region.
  const offX = flattened && step.crop ? step.crop.x : 0;
  const offY = flattened && step.crop ? step.crop.y : 0;
  let marker: { left: string; top: string } | null = null;
  if (dims && step.click && dims.w > 0 && dims.h > 0) {
    const fx = (step.click.image.x - offX) / dims.w;
    const fy = (step.click.image.y - offY) / dims.h;
    if (fx >= 0 && fx <= 1 && fy >= 0 && fy <= 1) {
      marker = { left: `${fx * 100}%`, top: `${fy * 100}%` };
    }
  }

  return (
    <figure className="rep__figure">
      <div className="rep__imgwrap">
        <img
          className="rep__img"
          src={src}
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

export function Report({
  onEditStep,
}: {
  onEditStep?: (step: ProjectStep) => void;
}): React.JSX.Element | null {
  const projectId = useProjectStore((s) => s.projectId);
  const steps = useProjectStore((s) => s.steps);
  const version = useProjectStore((s) => s.updatedAt);

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
      {steps.map((s) =>
        s.kind === 'text' ? (
          <li key={s.id} className="rep__step rep__step--text">
            <div className="rep__num rep__num--text" aria-hidden="true">
              ¶
            </div>
            <div className="rep__bodywrap">
              {s.heading && <h3 className="rep__textheading">{s.heading}</h3>}
              {s.body && <p className="rep__textbody">{s.body}</p>}
              {!s.heading && !s.body && (
                <p className="project__hint">Empty text step.</p>
              )}
              {onEditStep && (
                <button
                  type="button"
                  className="btn btn--small rep__edit"
                  onClick={() => onEditStep(s)}
                >
                  Edit
                </button>
              )}
            </div>
          </li>
        ) : (
          <li key={s.id} className="rep__step">
            <div className="rep__num" aria-hidden="true">
              {s.order}
            </div>
            <div className="rep__bodywrap">
              <div className="rep__caprow">
                <h3 className="rep__caption">{s.caption}</h3>
                {onEditStep && (
                  <button
                    type="button"
                    className="btn btn--small rep__edit"
                    onClick={() => onEditStep(s)}
                  >
                    Edit
                  </button>
                )}
              </div>
              <StepFigure projectId={projectId} step={s} version={version} />
              {s.note && <p className="rep__note">{s.note}</p>}
              {s.window && (
                <p className="rep__meta">
                  {s.window.app}
                  {s.window.title ? ` — ${s.window.title}` : ''}
                </p>
              )}
            </div>
          </li>
        ),
      )}
    </ol>
  );
}
