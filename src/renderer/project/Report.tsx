// The in-app report: each step rendered as its screenshot with an overlaid
// click-register marker + caption + note. Text steps (kind === 'text') render as
// a heading + body block. Prefers the flattened render (annotations baked +
// redaction) over the raw screenshot once a step has been edited. Each image is
// constrained to ~REPORT_BASE (800x600), with a per-step report zoom to enlarge.
import React from 'react';
import type { ProjectStep } from '../../shared/project';
import { shotUrl, useProjectStore } from './store';

// Base display box for report images (display only — export is full-res).
const REPORT_BASE_W = 800;
const REPORT_BASE_H = 600;
const ZOOM_MIN = 0.5;
const ZOOM_MAX = 4;

function StepFigure({
  projectId,
  step,
  onReframe,
}: {
  projectId: string;
  step: ProjectStep;
  onReframe: (step: ProjectStep, panX: number, panY: number) => void;
}): React.JSX.Element {
  const [dims, setDims] = React.useState<{ w: number; h: number } | null>(null);
  const wrapRef = React.useRef<HTMLDivElement | null>(null);
  const drag = React.useRef<{ x: number; y: number; sl: number; st: number } | null>(null);
  const flattened = !!step.flattened;
  // Cache-bust the flattened <img> with renderRev (bumped only when it's actually
  // re-rendered) so a re-save updates it but a display-zoom change doesn't reload.
  const src = flattened
    ? `${shotUrl(projectId, step.flattened as string)}?v=${step.renderRev ?? 0}`
    : shotUrl(projectId, step.screenshot);

  const zoom = step.reportZoom ?? 1;
  // Fixed viewport: the image fit within REPORT_BASE (<=800x600) at zoom 1; the
  // box shrinks for zoom<1 and stays fixed for zoom>1 so the image overflows and
  // pans in BOTH axes (instead of the box growing taller).
  const baseScale = dims ? Math.min(REPORT_BASE_W / dims.w, REPORT_BASE_H / dims.h, 1) : 0;
  const baseW = dims ? dims.w * baseScale : 0;
  const baseH = dims ? dims.h * baseScale : 0;
  const boxScale = Math.min(zoom, 1);
  const markerColor =
    step.markerColor ?? (step.click?.button === 'right' ? '#2563eb' : '#e11d48');
  // Marker as a fraction of the displayed image; subtract crop origin when the
  // displayed image is the cropped flatten; hidden if outside the visible region.
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

  // Restore the persisted pan (as a fraction of the scrollable range) whenever
  // the rendered size changes (load, zoom). Scrollbars are hidden; you drag.
  React.useEffect(() => {
    const el = wrapRef.current;
    if (!el || !dims) return;
    const rangeX = el.scrollWidth - el.clientWidth;
    const rangeY = el.scrollHeight - el.clientHeight;
    el.scrollLeft = rangeX * (step.reportPanX ?? 0.5);
    el.scrollTop = rangeY * (step.reportPanY ?? 0.5);
  }, [dims, zoom, step.reportPanX, step.reportPanY]);

  const onPanStart = (e: React.MouseEvent) => {
    const el = wrapRef.current;
    if (!el) return;
    if (el.scrollWidth <= el.clientWidth && el.scrollHeight <= el.clientHeight) return;
    e.preventDefault();
    drag.current = { x: e.clientX, y: e.clientY, sl: el.scrollLeft, st: el.scrollTop };
    const move = (ev: MouseEvent) => {
      const d = drag.current;
      if (!d) return;
      el.scrollLeft = d.sl - (ev.clientX - d.x);
      el.scrollTop = d.st - (ev.clientY - d.y);
    };
    const up = () => {
      window.removeEventListener('mousemove', move);
      window.removeEventListener('mouseup', up);
      drag.current = null;
      const rangeX = el.scrollWidth - el.clientWidth;
      const rangeY = el.scrollHeight - el.clientHeight;
      onReframe(
        step,
        rangeX > 0 ? el.scrollLeft / rangeX : 0.5,
        rangeY > 0 ? el.scrollTop / rangeY : 0.5,
      );
    };
    window.addEventListener('mousemove', move);
    window.addEventListener('mouseup', up);
  };

  return (
    <figure className="rep__figure">
      <div
        className={`rep__imgwrap${zoom > 1 ? ' rep__imgwrap--pan' : ''}`}
        ref={wrapRef}
        onMouseDown={onPanStart}
        style={dims ? { width: baseW * boxScale, height: baseH * boxScale } : undefined}
      >
        <div className="rep__imginner">
          <img
            className="rep__img"
            src={src}
            alt={step.caption}
            loading="lazy"
            style={
              dims
                ? { width: baseW * zoom, height: baseH * zoom }
                : { maxWidth: REPORT_BASE_W, maxHeight: REPORT_BASE_H }
            }
            onLoad={(e) =>
              setDims({
                w: e.currentTarget.naturalWidth,
                h: e.currentTarget.naturalHeight,
              })
            }
          />
          {marker && step.click && (
            <span
              className="rep__marker"
              style={{ ...marker, borderColor: markerColor, background: `${markerColor}2e` }}
              aria-hidden="true"
            />
          )}
        </div>
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
  const projectPath = useProjectStore((s) => s.projectPath);
  const steps = useProjectStore((s) => s.steps);
  const applyManifest = useProjectStore((s) => s.applyManifest);

  const setZoom = async (step: ProjectStep, next: number) => {
    if (!projectPath) return;
    const z = Math.max(ZOOM_MIN, Math.min(ZOOM_MAX, next));
    try {
      const manifest = await window.shotai.projects.updateStep(projectPath, step.id, {
        reportZoom: z,
      });
      applyManifest(manifest);
    } catch {
      /* display-only; ignore persistence errors */
    }
  };

  const reframe = async (step: ProjectStep, panX: number, panY: number) => {
    if (!projectPath) return;
    try {
      const manifest = await window.shotai.projects.updateStep(projectPath, step.id, {
        reportPanX: panX,
        reportPanY: panY,
      });
      applyManifest(manifest);
    } catch {
      /* display-only; ignore persistence errors */
    }
  };

  if (!projectId) return null;
  if (steps.length === 0) {
    return (
      <p className="project__hint">
        No steps yet. Resume capturing (or Import image) to add steps.
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
                <div className="rep__actions">
                  <div className="rep__zoom" title="Zoom this image in the report">
                    <button
                      type="button"
                      className="btn btn--small"
                      onClick={() => void setZoom(s, (s.reportZoom ?? 1) / 1.25)}
                    >
                      −
                    </button>
                    <span className="rep__zoom-val">
                      {Math.round((s.reportZoom ?? 1) * 100)}%
                    </span>
                    <button
                      type="button"
                      className="btn btn--small"
                      onClick={() => void setZoom(s, (s.reportZoom ?? 1) * 1.25)}
                    >
                      +
                    </button>
                  </div>
                  {onEditStep && (
                    <button
                      type="button"
                      className="btn btn--small"
                      onClick={() => onEditStep(s)}
                    >
                      Edit
                    </button>
                  )}
                </div>
              </div>
              <StepFigure projectId={projectId} step={s} onReframe={reframe} />
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
