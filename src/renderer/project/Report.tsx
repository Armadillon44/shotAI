// The in-app report: each step rendered as its screenshot with an overlaid
// click-register marker + caption + note. Text steps (kind === 'text') render as
// a heading + body block. Prefers the flattened render (annotations baked +
// redaction) over the raw screenshot once a step has been edited. Each image is
// constrained to ~REPORT_BASE (800x600), with a per-step report zoom to enlarge.
import React from 'react';
import type { CalloutKind, ProjectStep } from '../../shared/project';
import { shotUrl, useProjectStore } from './store';
import { canMergeInto, mergeStepInto } from './merge';
import { markerColorFor } from '../editor/annotations';
import { OverflowMenu, type MenuItem } from './OverflowMenu';

/** What the hover-"+" insert menu can add between steps. */
export type InsertKind = 'text' | 'image' | 'shot' | CalloutKind;

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
  const markerColor = markerColorFor(step);
  // Marker as a fraction of the displayed image; subtract crop origin when the
  // displayed image is the cropped flatten; hidden if outside the visible region.
  const offX = flattened && step.crop ? step.crop.x : 0;
  const offY = flattened && step.crop ? step.crop.y : 0;
  // When the render is marker-baked the ring is already in the pixels — don't
  // draw the CSS overlay on top (it would double). Pre-marker + un-flattened
  // renders keep the overlay so the click is still shown.
  let marker: { left: string; top: string } | null = null;
  if (dims && step.click && !step.markerBaked && dims.w > 0 && dims.h > 0) {
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
            draggable={false}
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
  initialHeading,
  initialBody,
  onSave,
  onCancel,
}: {
  initialHeading: string;
  initialBody: string;
  onSave: (heading: string, body: string) => void;
  onCancel: () => void;
}): React.JSX.Element {
  const [heading, setHeading] = React.useState(initialHeading);
  const [body, setBody] = React.useState(initialBody);
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
        rows={3}
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

/** Single-line inline editor (number badge / caption). Commits once on Enter or
 *  blur; Escape cancels. The `done` guard prevents the Enter→blur double-commit. */
function InlineInput({
  initial,
  type = 'text',
  className,
  placeholder,
  max,
  onCommit,
  onCancel,
}: {
  initial: string;
  type?: 'text' | 'number';
  className: string;
  placeholder?: string;
  max?: number;
  onCommit: (value: string) => void;
  onCancel: () => void;
}): React.JSX.Element {
  const [value, setValue] = React.useState(initial);
  const done = React.useRef(false);
  const finish = (commit: boolean) => {
    if (done.current) return;
    done.current = true;
    if (commit) onCommit(value);
    else onCancel();
  };
  return (
    <input
      className={className}
      type={type}
      {...(type === 'number' ? { min: 1, max } : {})}
      value={value}
      autoFocus
      placeholder={placeholder}
      onChange={(e) => setValue(e.target.value)}
      onFocus={(e) => e.currentTarget.select()}
      onKeyDown={(e) => {
        if (e.key === 'Enter') finish(true);
        else if (e.key === 'Escape') finish(false);
      }}
      onBlur={() => finish(true)}
    />
  );
}

/** Multi-line inline editor (per-screenshot subtext). Commits on blur or
 *  Ctrl/Cmd+Enter; Escape cancels. Enter inserts a newline. */
function InlineTextarea({
  initial,
  className,
  placeholder,
  onCommit,
  onCancel,
}: {
  initial: string;
  className: string;
  placeholder?: string;
  onCommit: (value: string) => void;
  onCancel: () => void;
}): React.JSX.Element {
  const [value, setValue] = React.useState(initial);
  const done = React.useRef(false);
  const finish = (commit: boolean) => {
    if (done.current) return;
    done.current = true;
    if (commit) onCommit(value);
    else onCancel();
  };
  return (
    <textarea
      className={className}
      value={value}
      autoFocus
      rows={3}
      placeholder={placeholder}
      onChange={(e) => setValue(e.target.value)}
      onKeyDown={(e) => {
        if (e.key === 'Escape') finish(false);
        else if (e.key === 'Enter' && (e.ctrlKey || e.metaKey)) finish(true);
      }}
      onBlur={() => finish(true)}
    />
  );
}

export function Report({
  onEditStep,
  autoEditId,
  onEditingChange,
  onInsert,
}: {
  onEditStep?: (step: ProjectStep) => void;
  /** When set to a (text) step id, open that step's inline editor. */
  autoEditId?: string | null;
  /** Notifies the parent whether a text step is currently being inline-edited
   *  (so it can disable structural actions that would discard the draft). */
  onEditingChange?: (editing: boolean) => void;
  /** Insert a step at a manifest index (from the hover-"+" between steps). */
  onInsert?: (atIndex: number, kind: InsertKind) => void;
}): React.JSX.Element | null {
  const projectId = useProjectStore((s) => s.projectId);
  const projectPath = useProjectStore((s) => s.projectPath);
  const steps = useProjectStore((s) => s.steps);
  const intro = useProjectStore((s) => s.intro);
  const applyManifest = useProjectStore((s) => s.applyManifest);
  const [editingTextId, setEditingTextId] = React.useState<string | null>(null);
  const [editingIntro, setEditingIntro] = React.useState(false);
  const [editingCapId, setEditingCapId] = React.useState<string | null>(null);
  const [editingNumId, setEditingNumId] = React.useState<string | null>(null);
  // Per-screenshot instruction text (the body), editable inline under the image.
  const [editingBodyId, setEditingBodyId] = React.useState<string | null>(null);
  const [insertMenuAt, setInsertMenuAt] = React.useState<number | null>(null);
  const [dragIdx, setDragIdx] = React.useState<number | null>(null);
  const [dragOverIdx, setDragOverIdx] = React.useState<number | null>(null);
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

  // If the step being edited disappears (reorder/merge/SOP-gen reloads the
  // manifest with new ids), clear the stale editing id — otherwise no editor
  // renders yet every text step's Edit button stays disabled (looks like "can't
  // edit any text step").
  React.useEffect(() => {
    if (editingTextId && !steps.some((s) => s.id === editingTextId)) {
      setEditingTextId(null);
      freshTextIdRef.current = null;
    }
  }, [steps, editingTextId]);

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

  // Move the step at `from` to position `to` (both 0-based, `to` clamped),
  // renumber via reorderSteps. Backs the ↑/↓ buttons, drag-drop, and number entry.
  const reorderTo = async (from: number, to: number) => {
    const dest = Math.max(0, Math.min(to, steps.length - 1));
    if (busyRef.current || !projectPath || from === dest) return;
    const ids = steps.map((s) => s.id);
    const [moved] = ids.splice(from, 1);
    ids.splice(dest, 0, moved);
    busyRef.current = true;
    try {
      applyManifest(await window.shotai.projects.reorderSteps(projectPath, ids));
    } catch {
      /* ignore */
    } finally {
      busyRef.current = false;
    }
  };

  const move = (idx: number, dir: 1 | -1) => void reorderTo(idx, idx + dir);

  const commitNum = (idx: number, raw: string) => {
    setEditingNumId(null);
    const n = Number.parseInt(raw, 10);
    if (Number.isFinite(n)) void reorderTo(idx, n - 1); // 1-based → 0-based
  };

  const saveCaption = async (step: ProjectStep, caption: string) => {
    setEditingCapId(null);
    if (!projectPath || caption === step.caption) return;
    try {
      applyManifest(
        await window.shotai.projects.updateStep(projectPath, step.id, { caption }),
      );
    } catch {
      /* ignore */
    }
  };

  // Per-screenshot instruction text (reuses the step's body field). updateStep here
  // carries no annotations/crop, so the flattened render is untouched.
  const saveBody = async (step: ProjectStep, body: string) => {
    setEditingBodyId(null);
    if (!projectPath || body === (step.body ?? '')) return;
    try {
      applyManifest(
        await window.shotai.projects.updateStep(projectPath, step.id, { body }),
      );
    } catch {
      /* ignore */
    }
  };

  const onRowDrop = (idx: number) => {
    // Drop = "insert before the target row" (the indicator line is drawn above
    // it). When dragging downward, removing the source first shifts the target
    // left by one, so subtract one to keep the result matching the indicator.
    if (dragIdx !== null && dragIdx !== idx) {
      void reorderTo(dragIdx, dragIdx < idx ? idx - 1 : idx);
    }
    setDragIdx(null);
    setDragOverIdx(null);
  };

  const doInsert = (atIndex: number, kind: InsertKind) => {
    setInsertMenuAt(null);
    onInsert?.(atIndex, kind);
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

  // Fold the step at `idx` into the one after it: keep the NEXT step's screenshot
  // (e.g. the one showing the open menu) and carry this step's click in as a
  // second marker ring. Used to discard a redundant right-click step after its
  // menu selection was captured. Re-bakes both rings, then deletes this step.
  const [merging, setMerging] = React.useState<string | null>(null);
  const merge = async (idx: number) => {
    if (busyRef.current || !projectId || !projectPath || !canMergeInto(steps, idx)) return;
    const drop = steps[idx];
    const keep = steps[idx + 1];
    busyRef.current = true;
    setMerging(drop.id);
    try {
      applyManifest(await mergeStepInto(projectId, projectPath, keep, drop));
    } catch (e) {
      window.alert(
        `Could not merge these steps: ${e instanceof Error ? e.message : String(e)}`,
      );
    } finally {
      setMerging(null);
      busyRef.current = false;
    }
  };

  // Open a text/callout step's inline editor (also the heading/body click
  // target). Blocked while another draft is open so it can't be discarded.
  const openTextEdit = (step: ProjectStep) => {
    if (editingTextId === null) setEditingTextId(step.id);
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

  // Save (or clear, when both fields are empty) the SOP overview preamble (E8).
  const saveIntro = async (heading: string, body: string) => {
    if (!projectPath) return;
    const h = heading.trim();
    const b = body.trim();
    try {
      applyManifest(
        await window.shotai.projects.setIntro(projectPath, h || b ? { heading: h, body: b } : null),
      );
      setEditingIntro(false);
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
    // A callout is meaningful even when empty (its colored box + label), so don't
    // auto-delete it on Cancel — only a truly-blank plain text step.
    if (wasFresh && !step.heading && !step.body && !step.callout && projectPath) {
      try {
        applyManifest(await window.shotai.projects.deleteStep(projectPath, step.id));
      } catch {
        /* ignore */
      }
    }
  };

  if (!projectId) return null;

  // The hover-"+" between steps. Disabled while a text draft is open (inserting
  // would switch/discard it). Hidden entirely if the parent gives no handler.
  const canInsert = !!onInsert && editingTextId === null;
  const insertZone = (atIndex: number) =>
    onInsert ? (
      <div className="rep__insert">
        {insertMenuAt === atIndex ? (
          <div className="rep__insert-menu" role="menu">
            {/* Row 1 — content steps. */}
            <div className="rep__insert-row">
              <button
                type="button"
                className="btn btn--small"
                onClick={() => doInsert(atIndex, 'text')}
              >
                + Text
              </button>
              <button
                type="button"
                className="btn btn--small"
                onClick={() => doInsert(atIndex, 'image')}
              >
                + Image
              </button>
              <button
                type="button"
                className="btn btn--small"
                onClick={() => doInsert(atIndex, 'shot')}
              >
                + Screenshot
              </button>
              <button
                type="button"
                className="btn btn--small btn--icon rep__insert-x"
                title="Cancel"
                onClick={() => setInsertMenuAt(null)}
              >
                ✕
              </button>
            </div>
            {/* Row 2 — tinted callouts. */}
            <div className="rep__insert-row">
              <button
                type="button"
                className="btn btn--small rep__insert-callout rep__insert-callout--note"
                onClick={() => doInsert(atIndex, 'note')}
              >
                + Note
              </button>
              <button
                type="button"
                className="btn btn--small rep__insert-callout rep__insert-callout--caution"
                onClick={() => doInsert(atIndex, 'caution')}
              >
                + Caution
              </button>
              <button
                type="button"
                className="btn btn--small rep__insert-callout rep__insert-callout--warning"
                onClick={() => doInsert(atIndex, 'warning')}
              >
                + Warning
              </button>
            </div>
          </div>
        ) : (
          <button
            type="button"
            className="rep__insert-btn"
            title="Insert a step here"
            disabled={!canInsert}
            onClick={() => setInsertMenuAt(atIndex)}
          >
            +
          </button>
        )}
      </div>
    ) : null;

  if (steps.length === 0) {
    return (
      <div className="rep rep--empty">
        {insertZone(0)}
        <p className="project__hint">
          No steps yet. Resume capturing, Import an image, or Add a text step.
        </p>
      </div>
    );
  }

  // Left rail: a drag grip + the (click-to-edit) step number. Numbered for ALL
  // step kinds, including text steps, so the sequence reads 1..N.
  const rail = (s: ProjectStep, idx: number) => (
    <div className="rep__rail">
      <button
        type="button"
        className="rep__grip"
        draggable
        aria-label="Drag to reorder"
        title="Drag to reorder"
        onDragStart={(e) => {
          setDragIdx(idx);
          e.dataTransfer.effectAllowed = 'move';
          try {
            e.dataTransfer.setData('text/plain', String(idx));
          } catch {
            /* some platforms restrict setData */
          }
        }}
        onDragEnd={() => {
          setDragIdx(null);
          setDragOverIdx(null);
        }}
      >
        ⠿
      </button>
      {editingNumId === s.id ? (
        <InlineInput
          initial={String(s.order)}
          type="number"
          max={steps.length}
          className="rep__num-input"
          onCommit={(v) => commitNum(idx, v)}
          onCancel={() => setEditingNumId(null)}
        />
      ) : (
        <button
          type="button"
          className={`rep__num${s.kind === 'text' ? ' rep__num--text' : ''}`}
          title="Click to set this step's position"
          onClick={() => setEditingNumId(s.id)}
        >
          {s.order}
        </button>
      )}
    </div>
  );

  // Per-step controls: only Edit stays on the line; everything incidental
  // (move / zoom / delete) lives in a ⋯ overflow. Merge is offered ONLY by the
  // contextual banner below, not duplicated here.
  const controls = (s: ProjectStep, idx: number) => {
    const editDisabled = s.kind === 'text' && editingTextId !== null;
    const items: MenuItem[] = [
      { label: '↑ Move up', onClick: () => move(idx, -1), disabled: idx === 0 },
      {
        label: '↓ Move down',
        onClick: () => move(idx, 1),
        disabled: idx === steps.length - 1,
      },
    ];
    if (s.kind !== 'text') {
      items.push(
        { kind: 'sep' },
        { label: 'Zoom in', onClick: () => void setZoom(s, (s.reportZoom ?? 1) * 1.25) },
        { label: 'Zoom out', onClick: () => void setZoom(s, (s.reportZoom ?? 1) / 1.25) },
        { label: 'Reset zoom', onClick: () => void setZoom(s, 1) },
      );
    }
    items.push(
      { kind: 'sep' },
      { label: 'Delete step', danger: true, onClick: () => void del(s) },
    );
    return (
      <div className="rep__ctl">
        <button
          type="button"
          className="btn btn--small"
          // While a text step is open in the inline editor, switching to edit
          // another text step would discard the open draft — block it.
          disabled={editDisabled}
          title={editDisabled ? 'Finish editing the open text step first' : 'Edit'}
          onClick={() => (s.kind === 'text' ? setEditingTextId(s.id) : onEditStep?.(s))}
        >
          Edit
        </button>
        <OverflowMenu items={items} title="More step actions" />
      </div>
    );
  };

  const stepRow = (s: ProjectStep, idx: number) => {
    const cls =
      `rep__step${s.kind === 'text' ? ' rep__step--text' : ''}` +
      `${dragIdx === idx ? ' rep__step--dragging' : ''}` +
      `${dragOverIdx === idx && dragIdx !== null && dragIdx !== idx ? ' rep__step--over' : ''}`;
    return (
      <div
        className={cls}
        role="listitem"
        onDragOver={(e) => {
          if (dragIdx === null) return;
          e.preventDefault();
          e.dataTransfer.dropEffect = 'move';
          if (dragOverIdx !== idx) setDragOverIdx(idx);
        }}
        onDrop={(e) => {
          e.preventDefault();
          onRowDrop(idx);
        }}
      >
        {rail(s, idx)}
        <div className="rep__bodywrap">
          {s.kind === 'text' ? (
            editingTextId === s.id ? (
              <TextStepEditor
                initialHeading={s.heading ?? ''}
                initialBody={s.body ?? ''}
                onSave={(h, b) => void saveText(s, h, b)}
                onCancel={() => void cancelText(s)}
              />
            ) : s.callout ? (
              // Callout: the box COLOR conveys the type (note/caution/warning) —
              // no text label. The optional heading + body live inside the box.
              <>
                <div className="rep__caprow rep__caprow--callout">
                  <div className="rep__actions">{controls(s, idx)}</div>
                </div>
                <div
                  className={`rep__callout rep__callout--clickable rep__callout--${s.callout}`}
                  title="Click to edit"
                  onClick={() => openTextEdit(s)}
                >
                  {s.heading ? <strong className="rep__callout-h">{s.heading}</strong> : null}
                  {s.body ? (
                    <p className="rep__callout-b">{s.body}</p>
                  ) : !s.heading ? (
                    <p className="rep__callout-b rep__callout-empty">Empty — click to add text.</p>
                  ) : null}
                </div>
              </>
            ) : (
              <>
                <div className="rep__caprow">
                  <h3
                    className="rep__textheading rep__textheading--edit"
                    title="Click to edit this text step"
                    onClick={() => openTextEdit(s)}
                  >
                    {s.heading || 'Text'}
                  </h3>
                  <div className="rep__actions">{controls(s, idx)}</div>
                </div>
                {s.body ? (
                  <p
                    className="rep__textbody rep__textbody--edit"
                    title="Click to edit"
                    onClick={() => openTextEdit(s)}
                  >
                    {s.body}
                  </p>
                ) : !s.heading ? (
                  // Only truly-empty (no heading AND no body) shows a clear
                  // affordance; a heading-only step (AI divider) renders clean.
                  <button
                    type="button"
                    className="rep__addline"
                    onClick={() => openTextEdit(s)}
                  >
                    + Add text
                  </button>
                ) : null}
              </>
            )
          ) : (
            <>
              <div className="rep__caprow">
                {editingCapId === s.id ? (
                  <InlineInput
                    initial={s.caption}
                    className="rep__cap-input"
                    placeholder="Caption…"
                    onCommit={(v) => void saveCaption(s, v)}
                    onCancel={() => setEditingCapId(null)}
                  />
                ) : (
                  <h3
                    className="rep__caption"
                    title="Click to edit caption"
                    onClick={() => setEditingCapId(s.id)}
                  >
                    {s.caption || (
                      <span className="rep__caption-empty">Add a caption…</span>
                    )}
                  </h3>
                )}
                <div className="rep__actions">{controls(s, idx)}</div>
              </div>
              {s.click?.button === 'right' && canMergeInto(steps, idx) && (
                <div className="rep__merge-suggest">
                  <span>
                    This right-click likely opened a menu — merge it into the next step (the
                    menu selection) to keep one screenshot showing the menu.
                  </span>
                  <button
                    type="button"
                    className="btn btn--small"
                    disabled={merging !== null}
                    onClick={() => void merge(idx)}
                  >
                    {merging === s.id ? 'Merging…' : 'Merge ↓'}
                  </button>
                </div>
              )}
              <StepFigure projectId={projectId} step={s} onReframe={reframe} />
              {editingBodyId === s.id ? (
                <InlineTextarea
                  initial={s.body ?? ''}
                  className="rep__body-input"
                  placeholder="Write the instruction for this screenshot…"
                  onCommit={(v) => void saveBody(s, v)}
                  onCancel={() => setEditingBodyId(null)}
                />
              ) : s.body ? (
                <p
                  className="rep__shotbody"
                  title="Click to edit instructions"
                  onClick={() => setEditingBodyId(s.id)}
                >
                  {s.body}
                </p>
              ) : (
                <button
                  type="button"
                  className="rep__addline"
                  onClick={() => setEditingBodyId(s.id)}
                >
                  + Add instructions
                </button>
              )}
              {s.note && <p className="rep__note">{s.note}</p>}
              {s.window && (
                <p className="rep__meta">
                  {s.window.app}
                  {s.window.title ? ` — ${s.window.title}` : ''}
                </p>
              )}
            </>
          )}
        </div>
      </div>
    );
  };

  const hasIntro = !!(intro && (intro.heading || intro.body));
  return (
    <div className="rep" role="list">
      {editingIntro ? (
        <div className="rep__intro">
          <TextStepEditor
            initialHeading={intro?.heading ?? ''}
            initialBody={intro?.body ?? ''}
            onSave={(h, b) => void saveIntro(h, b)}
            onCancel={() => setEditingIntro(false)}
          />
        </div>
      ) : hasIntro ? (
        <div className="rep__intro" role="note">
          <div className="rep__intro-actions">
            <button type="button" className="btn btn--small" onClick={() => setEditingIntro(true)}>
              Edit overview
            </button>
            <button
              type="button"
              className="btn btn--small btn--danger"
              onClick={() => void saveIntro('', '')}
            >
              Remove
            </button>
          </div>
          {intro?.heading && <h2 className="rep__intro-h">{intro.heading}</h2>}
          {intro?.body && <p className="rep__intro-b">{intro.body}</p>}
        </div>
      ) : (
        <button
          type="button"
          className="rep__addintro"
          onClick={() => setEditingIntro(true)}
        >
          + Add an overview
        </button>
      )}
      {insertZone(0)}
      {steps.map((s, idx) => (
        <React.Fragment key={s.id}>
          {stepRow(s, idx)}
          {insertZone(idx + 1)}
        </React.Fragment>
      ))}
    </div>
  );
}
