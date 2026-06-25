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

function TextStepEditor({
  step,
  onSave,
  onCancel,
}: {
  step: ProjectStep;
  onSave: (heading: string, body: string) => void;
  onCancel: () => void;
}): React.JSX.Element {
  const [heading, setHeading] = React.useState(step.heading ?? '');
  const [body, setBody] = React.useState(step.body ?? '');
  return (
    <div className="rep__textedit">
      <input
        className="rep__textedit-h"
        placeholder="Heading (optional)"
        value={heading}
        autoFocus
        onChange={(e) => setHeading(e.target.value)}
      />
      <textarea
        className="rep__textedit-b"
        placeholder="Text…"
        rows={4}
        value={body}
        onChange={(e) => setBody(e.target.value)}
      />
      <div className="rep__textedit-actions">
        <button
          type="button"
          className="btn btn--small btn--primary"
          onClick={() => onSave(heading, body)}
        >
          Save
        </button>
        <button type="button" className="btn btn--small" onClick={onCancel}>
          Cancel
        </button>
      </div>
    </div>
  );
}

export function Report({
  onEditStep,
  autoEditId,
  onEditingChange,
}: {
  onEditStep?: (step: ProjectStep) => void;
  /** When set to a (text) step id, open that step's inline editor. */
  autoEditId?: string | null;
  /** Notifies the parent whether a text step is currently being inline-edited
   *  (so it can disable structural actions that would discard the draft). */
  onEditingChange?: (editing: boolean) => void;
}): React.JSX.Element | null {
  const projectId = useProjectStore((s) => s.projectId);
  const projectPath = useProjectStore((s) => s.projectPath);
  const steps = useProjectStore((s) => s.steps);
  const applyManifest = useProjectStore((s) => s.applyManifest);
  const [editingTextId, setEditingTextId] = React.useState<string | null>(null);
  // Blocks a second structural mutation (reorder/delete) landing inside the IPC
  // round-trip of the first, which would otherwise act on a stale step order.
  const busyRef = React.useRef(false);
  // The id of a just-added text step still in its initial editing session — used
  // so cancelling it (before any save) removes it rather than leaving an empty
  // step. Cleared on the first save or cancel of that step.
  const freshTextIdRef = React.useRef<string | null>(null);

  // A freshly added text step opens straight into its editor.
  React.useEffect(() => {
    if (autoEditId) {
      setEditingTextId(autoEditId);
      freshTextIdRef.current = autoEditId;
    }
  }, [autoEditId]);

  // Keep the parent in sync so it can guard against discarding an open draft.
  React.useEffect(() => {
    onEditingChange?.(editingTextId !== null);
  }, [editingTextId, onEditingChange]);

  const setZoom = async (step: ProjectStep, next: number) => {
    if (!projectPath) return;
    const z = Math.max(ZOOM_MIN, Math.min(ZOOM_MAX, next));
    try {
      applyManifest(
        await window.shotai.projects.updateStep(projectPath, step.id, { reportZoom: z }),
      );
    } catch {
      /* display-only; ignore */
    }
  };

  const reframe = async (step: ProjectStep, panX: number, panY: number) => {
    if (!projectPath) return;
    try {
      applyManifest(
        await window.shotai.projects.updateStep(projectPath, step.id, {
          reportPanX: panX,
          reportPanY: panY,
        }),
      );
    } catch {
      /* display-only; ignore */
    }
  };

  const move = async (idx: number, dir: 1 | -1) => {
    const j = idx + dir;
    if (busyRef.current || !projectPath || j < 0 || j >= steps.length) return;
    const ids = steps.map((s) => s.id);
    [ids[idx], ids[j]] = [ids[j], ids[idx]];
    busyRef.current = true;
    try {
      applyManifest(await window.shotai.projects.reorderSteps(projectPath, ids));
    } catch {
      /* ignore */
    } finally {
      busyRef.current = false;
    }
  };

  const del = async (step: ProjectStep) => {
    if (busyRef.current || !projectPath) return;
    if (!window.confirm('Delete this step? (its image file stays on disk)')) return;
    busyRef.current = true;
    try {
      applyManifest(await window.shotai.projects.deleteStep(projectPath, step.id));
    } catch {
      /* ignore */
    } finally {
      busyRef.current = false;
    }
  };

  const saveText = async (step: ProjectStep, heading: string, body: string) => {
    if (!projectPath) return;
    try {
      applyManifest(
        await window.shotai.projects.updateStep(projectPath, step.id, { heading, body }),
      );
      if (freshTextIdRef.current === step.id) freshTextIdRef.current = null;
      setEditingTextId(null);
    } catch {
      /* ignore */
    }
  };

  // Cancel closes the inline editor. A freshly added text step (the one that
  // auto-opened) that was never saved is removed, so "+ Text step" then Cancel
  // doesn't leave an empty step behind.
  const cancelText = async (step: ProjectStep) => {
    const wasFresh = freshTextIdRef.current === step.id;
    freshTextIdRef.current = null;
    setEditingTextId(null);
    if (wasFresh && !step.heading && !step.body && projectPath) {
      try {
        applyManifest(await window.shotai.projects.deleteStep(projectPath, step.id));
      } catch {
        /* ignore */
      }
    }
  };

  if (!projectId) return null;
  if (steps.length === 0) {
    return (
      <p className="project__hint">
        No steps yet. Resume capturing, Import an image, or Add a text step.
      </p>
    );
  }

  const controls = (s: ProjectStep, idx: number) => (
    <div className="rep__ctl">
      <button
        type="button"
        className="btn btn--small"
        disabled={idx === 0}
        title="Move up"
        onClick={() => void move(idx, -1)}
      >
        ↑
      </button>
      <button
        type="button"
        className="btn btn--small"
        disabled={idx === steps.length - 1}
        title="Move down"
        onClick={() => void move(idx, 1)}
      >
        ↓
      </button>
      <button
        type="button"
        className="btn btn--small"
        // While a text step is open in the inline editor, switching to edit
        // another text step would discard the open draft — block it.
        disabled={s.kind === 'text' && editingTextId !== null}
        title={
          s.kind === 'text' && editingTextId !== null
            ? 'Finish editing the open text step first'
            : 'Edit'
        }
        onClick={() => (s.kind === 'text' ? setEditingTextId(s.id) : onEditStep?.(s))}
      >
        Edit
      </button>
      <button type="button" className="btn btn--small" onClick={() => void del(s)}>
        Delete
      </button>
    </div>
  );

  return (
    <ol className="rep">
      {steps.map((s, idx) =>
        s.kind === 'text' ? (
          <li key={s.id} className="rep__step rep__step--text">
            <div className="rep__num rep__num--text" aria-hidden="true">
              ¶
            </div>
            <div className="rep__bodywrap">
              {editingTextId === s.id ? (
                <TextStepEditor
                  step={s}
                  onSave={(h, b) => void saveText(s, h, b)}
                  onCancel={() => void cancelText(s)}
                />
              ) : (
                <>
                  <div className="rep__caprow">
                    <h3 className="rep__textheading">{s.heading || 'Text'}</h3>
                    <div className="rep__actions">{controls(s, idx)}</div>
                  </div>
                  {s.body ? (
                    <p className="rep__textbody">{s.body}</p>
                  ) : (
                    <p className="project__hint">Empty text step — click Edit.</p>
                  )}
                </>
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
                  {controls(s, idx)}
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
