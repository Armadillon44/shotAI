// Inline screenshot editor (Konva). Non-destructive: annotations + crop + the
// (movable) click marker are stored in the manifest; on save we also flatten to
// a PNG with redaction baked in. Geometry is in IMAGE px; the stage is scaled to
// fit × zoom so the pointer (getRelativePointerPosition) reads back in image px.
import React from 'react';
import Konva from 'konva';
import {
  Stage,
  Layer,
  Image as KImage,
  Rect as KRect,
  Arrow as KArrow,
  Circle,
  Text as KText,
  Group,
  Transformer,
} from 'react-konva';
import type {
  Annotation,
  BlurAnnotation,
  ProjectManifest,
  ProjectStep,
  Point,
  Rect,
} from '../../shared/project';
import { shotUrl } from '../project/store';
import {
  ACCENT,
  DEFAULT_BLOCK_SIZE,
  DEFAULT_STROKE_WIDTH,
  TOOLS,
  type Tool,
  clickMarkerRadius,
  createArrow,
  createBlur,
  createMarker,
  createRect,
  createStamp,
  createText,
  defaultFontSize,
  defaultStampRadius,
  defaultStrokeWidth,
  dragRect,
} from './annotations';
import { MIN_REDACT_BLOCK, flattenToPng } from './flatten';

const CLICK_ID = '__click__'; // pseudo-selection id for the click marker

const VIEW_W = 940;
const VIEW_H = 540;
const MIN_DRAG = 6; // image px — ignore accidental micro-drags

type Props = {
  projectId: string;
  projectPath: string;
  step: ProjectStep;
  onClose: () => void;
  onSaved: (manifest: ProjectManifest) => void;
};

type TextEntry = {
  imageX: number;
  imageY: number;
  id: string | null; // editing an existing text, or null = new
  value: string;
  fontSize: number;
};

/**
 * A blur/redact region rendered as a LIVE preview that matches flatten.ts: the
 * region is AVERAGE-downsampled into a tiny canvas, which Konva then upscales
 * smoothly — a soft blur (not hard pixel blocks), with detail destroyed. 'solid'
 * renders a black box. The preview canvas is recomputed when geometry/amount
 * change, so the blur-amount slider shows its real effect.
 */
function BlurRegion({
  img,
  a,
  draggable,
  onSelect,
  onDragEnd,
  onTransformEnd,
  registerRef,
}: {
  img: HTMLImageElement;
  a: BlurAnnotation;
  draggable: boolean;
  onSelect: () => void;
  onDragEnd: (e: Konva.KonvaEventObject<DragEvent>) => void;
  onTransformEnd: () => void;
  registerRef: (node: Konva.Node | null) => void;
}): React.JSX.Element {
  const preview = React.useMemo(() => {
    if (a.mode !== 'pixelate') return null;
    const w = Math.max(1, Math.round(a.width));
    const h = Math.max(1, Math.round(a.height));
    const block = Math.max(MIN_REDACT_BLOCK, Math.round(a.blockSize));
    const sw = Math.max(1, Math.round(w / block));
    const sh = Math.max(1, Math.round(h / block));
    const small = document.createElement('canvas');
    small.width = sw;
    small.height = sh;
    const sctx = small.getContext('2d');
    if (!sctx) return null;
    sctx.imageSmoothingEnabled = true;
    try {
      sctx.drawImage(img, a.x, a.y, a.width, a.height, 0, 0, sw, sh);
    } catch {
      return null;
    }
    // Rebuild at full size matching flatten.ts: opaque base + soft Gaussian.
    const out = document.createElement('canvas');
    out.width = w;
    out.height = h;
    const octx = out.getContext('2d');
    if (!octx) return null;
    octx.imageSmoothingEnabled = true;
    octx.drawImage(small, 0, 0, sw, sh, 0, 0, w, h);
    octx.filter = `blur(${Math.max(1, Math.round(block * 0.6))}px)`;
    octx.drawImage(small, 0, 0, sw, sh, 0, 0, w, h);
    octx.filter = 'none';
    return out;
  }, [img, a.x, a.y, a.width, a.height, a.blockSize, a.mode]);

  if (a.mode === 'solid' || !preview) {
    return (
      <KRect
        ref={(n) => registerRef(n)}
        x={a.x}
        y={a.y}
        width={a.width}
        height={a.height}
        fill={a.mode === 'solid' ? '#000000' : 'rgba(15,23,42,0.55)'}
        draggable={draggable}
        onClick={onSelect}
        onTap={onSelect}
        onDragEnd={onDragEnd}
        onTransformEnd={onTransformEnd}
      />
    );
  }
  return (
    <KImage
      ref={(n) => registerRef(n)}
      image={preview}
      x={a.x}
      y={a.y}
      width={a.width}
      height={a.height}
      draggable={draggable}
      onClick={onSelect}
      onTap={onSelect}
      onDragEnd={onDragEnd}
      onTransformEnd={onTransformEnd}
    />
  );
}

export function Editor({
  projectId,
  projectPath,
  step,
  onClose,
  onSaved,
}: Props): React.JSX.Element {
  const [img, setImg] = React.useState<HTMLImageElement | null>(null);
  const [annotations, setAnnotations] = React.useState<Annotation[]>(
    step.annotations ?? [],
  );
  const [crop, setCrop] = React.useState<Rect | null>(step.crop ?? null);
  // When true, the editor shows only the cropped region (zoomed to fill) so you
  // can work on it directly. Annotations stay in full-image coords regardless.
  const [viewCropped, setViewCropped] = React.useState(false);
  // Editing zoom on top of the fit/crop scale — for precise work on large
  // captures. The canvas scrolls when the zoomed stage exceeds it.
  const [editorZoom, setEditorZoom] = React.useState(1);
  const [clickImage, setClickImage] = React.useState<Point | null>(
    step.click ? { ...step.click.image } : null,
  );
  const [tool, setTool] = React.useState<Tool>('select');
  const [strokeWidth, setStrokeWidth] = React.useState(DEFAULT_STROKE_WIDTH);
  const [blockSize, setBlockSize] = React.useState(DEFAULT_BLOCK_SIZE);
  const [redactMode, setRedactMode] = React.useState<'pixelate' | 'solid'>('pixelate');
  const [color, setColor] = React.useState(ACCENT); // default for new shapes
  const [markerColor, setMarkerColor] = React.useState(step.markerColor ?? ACCENT);
  const [selectedId, setSelectedId] = React.useState<string | null>(null);
  const [draft, setDraft] = React.useState<Rect | null>(null);
  const [arrowDraft, setArrowDraft] = React.useState<
    [number, number, number, number] | null
  >(null);
  const [textEntry, setTextEntry] = React.useState<TextEntry | null>(null);
  const [selBox, setSelBox] = React.useState<Rect | null>(null);
  const [saving, setSaving] = React.useState(false);
  const [error, setError] = React.useState<string | null>(null);

  const stageRef = React.useRef<Konva.Stage | null>(null);
  const trRef = React.useRef<Konva.Transformer | null>(null);
  const shapeRefs = React.useRef<Map<string, Konva.Node>>(new Map());
  const dragStart = React.useRef<{ x: number; y: number } | null>(null);
  const textInputRef = React.useRef<HTMLInputElement | null>(null);

  // Load the ORIGINAL screenshot (editing is on the original + annotations).
  // crossOrigin keeps the canvas untainted so flatten()'s toBlob() works (the
  // shot:// scheme is registered corsEnabled to allow the CORS-mode load).
  React.useEffect(() => {
    let cancelled = false;
    const im = new window.Image();
    im.crossOrigin = 'anonymous';
    im.onload = () => {
      if (cancelled) return;
      setImg(im);
      // Scale the default line width to the image so it reads boldly.
      setStrokeWidth(defaultStrokeWidth(im.naturalWidth, im.naturalHeight));
    };
    im.onerror = () => {
      if (!cancelled) setError('Could not load the screenshot.');
    };
    im.src = shotUrl(projectId, step.screenshot);
    return () => {
      cancelled = true;
      im.onload = null;
      im.onerror = null;
    };
  }, [projectId, step.screenshot]);

  const natW = img?.naturalWidth ?? 0;
  const natH = img?.naturalHeight ?? 0;
  const scale = natW && natH ? Math.min(VIEW_W / natW, VIEW_H / natH, 1) : 1;
  const markerR = natW && natH ? clickMarkerRadius(natW, natH) : 20;
  // Stage transform: the visible region (whole image, or — when crop is applied
  // in-line — just the crop) fit to the canvas, times the editing zoom. Pointer
  // coords stay in image space regardless of transform/scroll.
  const region =
    viewCropped && crop ? crop : { x: 0, y: 0, width: natW, height: natH };
  const baseScale =
    viewCropped && crop ? Math.min(VIEW_W / crop.width, VIEW_H / crop.height, 8) : scale;
  const stageScale = baseScale * editorZoom;
  const cropView = {
    scale: stageScale,
    x: -region.x * stageScale,
    y: -region.y * stageScale,
    w: Math.max(1, Math.round(region.width * stageScale)),
    h: Math.max(1, Math.round(region.height * stageScale)),
  };

  const selected = annotations.find((a) => a.id === selectedId) ?? null;

  // Attach the Transformer to the selected resizable shape (rect/blur only).
  React.useEffect(() => {
    const tr = trRef.current;
    if (!tr) return;
    const node = selectedId ? shapeRefs.current.get(selectedId) : undefined;
    if (
      tool === 'select' &&
      node &&
      selected &&
      (selected.type === 'rect' || selected.type === 'blur')
    ) {
      tr.nodes([node]);
    } else {
      tr.nodes([]);
    }
    tr.getLayer()?.batchDraw();
  }, [selectedId, tool, annotations, selected]);

  const update = (id: string, patch: Partial<Annotation>) =>
    setAnnotations((prev) =>
      prev.map((a) => (a.id === id ? ({ ...a, ...patch } as Annotation) : a)),
    );

  const remove = (id: string) => {
    setAnnotations((prev) => prev.filter((a) => a.id !== id));
    shapeRefs.current.delete(id);
    setSelectedId((cur) => (cur === id ? null : cur));
  };

  const pointer = (): { x: number; y: number } | null => {
    const p = stageRef.current?.getRelativePointerPosition();
    return p ? { x: p.x, y: p.y } : null;
  };

  const onStageMouseDown = (e: Konva.KonvaEventObject<MouseEvent>) => {
    const p = pointer();
    if (!p) return;
    const onEmpty = e.target === e.target.getStage();

    if (tool === 'select') {
      if (onEmpty) setSelectedId(null);
      return;
    }
    if (tool === 'stamp') {
      const n = annotations.filter((a) => a.type === 'stamp').length + 1;
      const a = createStamp(p.x, p.y, n, defaultStampRadius(natW, natH), color);
      setAnnotations((prev) => [...prev, a]);
      setSelectedId(a.id);
      setTool('select');
      return;
    }
    if (tool === 'marker') {
      // Place a click-register ring at the click point; recolor/move via Select.
      const a = createMarker(p.x, p.y, color);
      setAnnotations((prev) => [...prev, a]);
      setSelectedId(a.id);
      setTool('select');
      return;
    }
    if (tool === 'text') {
      setSelectedId(null);
      setTextEntry({
        imageX: p.x,
        imageY: p.y,
        id: null,
        value: '',
        fontSize: defaultFontSize(natW, natH),
      });
      return;
    }
    // drag-create tools: rect / arrow / blur / crop
    dragStart.current = p;
    if (tool === 'arrow') setArrowDraft([p.x, p.y, p.x, p.y]);
    else setDraft({ x: p.x, y: p.y, width: 0, height: 0 });
  };

  const onStageMouseMove = () => {
    const start = dragStart.current;
    const p = pointer();
    if (!start || !p) return;
    if (tool === 'arrow') setArrowDraft([start.x, start.y, p.x, p.y]);
    else setDraft(dragRect(start.x, start.y, p.x, p.y));
  };

  // Finalize from the last drafted geometry (NOT a fresh pointer read), so a
  // mouseup that lands outside the stage still completes the shape.
  const finishDrag = () => {
    if (!dragStart.current) return;
    dragStart.current = null;
    if (tool === 'arrow') {
      const pts = arrowDraft;
      setArrowDraft(null);
      if (pts && Math.hypot(pts[2] - pts[0], pts[3] - pts[1]) >= MIN_DRAG) {
        const a = createArrow(pts[0], pts[1], pts[2], pts[3], strokeWidth, color);
        setAnnotations((prev) => [...prev, a]);
        setSelectedId(a.id);
      }
      setTool('select');
      return;
    }
    const r = draft;
    setDraft(null);
    if (!r || r.width < MIN_DRAG || r.height < MIN_DRAG) {
      setTool('select');
      return;
    }
    if (tool === 'crop') {
      setCrop(r);
    } else if (tool === 'rect') {
      const a = createRect(r.x, r.y, r.width, r.height, strokeWidth, color);
      setAnnotations((prev) => [...prev, a]);
      setSelectedId(a.id);
    } else if (tool === 'blur') {
      const a = createBlur(r.x, r.y, r.width, r.height, redactMode, blockSize);
      setAnnotations((prev) => [...prev, a]);
      setSelectedId(a.id);
    }
    setTool('select');
  };

  React.useEffect(() => {
    window.addEventListener('mouseup', finishDrag);
    return () => window.removeEventListener('mouseup', finishDrag);
  });

  const onTransformEnd = (id: string) => {
    const node = shapeRefs.current.get(id);
    if (!node) return;
    const sx = Math.abs(node.scaleX());
    const sy = Math.abs(node.scaleY());
    node.scaleX(1);
    node.scaleY(1);
    update(id, {
      x: node.x(),
      y: node.y(),
      width: Math.max(4, node.width() * sx),
      height: Math.max(4, node.height() * sy),
    } as Partial<Annotation>);
  };

  React.useEffect(() => {
    const onKey = (e: KeyboardEvent) => {
      const t = e.target as HTMLElement | null;
      if (t && (t.tagName === 'INPUT' || t.tagName === 'TEXTAREA')) return;
      if ((e.key === 'Delete' || e.key === 'Backspace') && selectedId) {
        e.preventDefault();
        remove(selectedId);
      } else if (e.key === 'Escape') {
        setSelectedId(null);
        setTool('select');
      }
    };
    window.addEventListener('keydown', onKey);
    return () => window.removeEventListener('keydown', onKey);
  }, [selectedId]);

  // Focus the text field when a new entry opens (keyed on identity, not value,
  // so typing doesn't re-focus and reset the caret).
  const textEntryKey = textEntry
    ? `${textEntry.id ?? 'new'}:${textEntry.imageX}:${textEntry.imageY}`
    : '';
  React.useEffect(() => {
    if (textEntryKey) textInputRef.current?.focus();
  }, [textEntryKey]);

  // Selection outline for arrow/stamp/text (rect/blur already show the
  // Transformer's handles). Measured from the node so it fits any shape.
  React.useEffect(() => {
    if (selectedId === CLICK_ID) {
      setSelBox(
        clickImage
          ? {
              x: clickImage.x - markerR,
              y: clickImage.y - markerR,
              width: markerR * 2,
              height: markerR * 2,
            }
          : null,
      );
      return;
    }
    const sel = annotations.find((a) => a.id === selectedId);
    if (!selectedId || !sel || sel.type === 'rect' || sel.type === 'blur') {
      setSelBox(null);
      return;
    }
    const node = shapeRefs.current.get(selectedId);
    const layer = node?.getLayer();
    if (node && layer) {
      const r = node.getClientRect({ relativeTo: layer });
      setSelBox({ x: r.x, y: r.y, width: r.width, height: r.height });
    } else {
      setSelBox(null);
    }
  }, [selectedId, annotations, clickImage, markerR]);

  const setRef = (id: string) => (node: Konva.Node | null) => {
    if (node) shapeRefs.current.set(id, node);
    else shapeRefs.current.delete(id);
  };

  const commitText = () => {
    const entry = textEntry;
    setTextEntry(null);
    if (!entry) return;
    const value = entry.value.trim();
    if (entry.id) {
      if (value)
        update(entry.id, { text: value, fontSize: entry.fontSize } as Partial<Annotation>);
      else remove(entry.id);
    } else if (value) {
      const a = createText(entry.imageX, entry.imageY, value, entry.fontSize, color);
      setAnnotations((prev) => [...prev, a]);
      setSelectedId(a.id);
    }
  };

  const changeStroke = (v: number) => {
    setStrokeWidth(v);
    if (selected && (selected.type === 'rect' || selected.type === 'arrow')) {
      update(selected.id, { strokeWidth: v } as Partial<Annotation>);
    }
  };
  const changeBlock = (v: number) => {
    setBlockSize(v);
    if (selected?.type === 'blur') update(selected.id, { blockSize: v } as Partial<Annotation>);
  };
  const changeMode = (m: 'pixelate' | 'solid') => {
    setRedactMode(m);
    if (selected?.type === 'blur') update(selected.id, { mode: m } as Partial<Annotation>);
  };
  const changeColor = (c: string) => {
    setColor(c); // also the default for new shapes
    if (selectedId === CLICK_ID) {
      setMarkerColor(c);
    } else if (selected?.type === 'rect' || selected?.type === 'arrow') {
      update(selected.id, { stroke: c } as Partial<Annotation>);
    } else if (selected?.type === 'text' || selected?.type === 'stamp') {
      update(selected.id, { fill: c } as Partial<Annotation>);
    } else if (selected?.type === 'marker') {
      update(selected.id, { color: c } as Partial<Annotation>);
    }
  };

  const onSave = async () => {
    if (!img) return;
    setSaving(true);
    setError(null);
    try {
      // clickImage is null when the step has no click marker OR the user removed
      // it → persist click:null so the marker is actually deleted (not kept).
      const click =
        step.click && clickImage ? { ...step.click, image: clickImage } : null;
      // Bake the click ring into the render so Claude's vision + exports see it.
      const blob = await flattenToPng(
        img,
        annotations,
        crop,
        click ? { x: click.image.x, y: click.image.y, color: markerColor } : null,
      );
      const bytes = new Uint8Array(await blob.arrayBuffer());
      const manifest = await window.shotai.projects.updateStep(
        projectPath,
        step.id,
        { annotations, crop, click, markerColor, markerBaked: true },
        bytes,
      );
      onSaved(manifest);
      onClose();
    } catch (e) {
      setError(e instanceof Error ? e.message : String(e));
    } finally {
      setSaving(false);
    }
  };

  const selectable = tool === 'select';
  // Controls appear only for the SELECTED element (not merely an active tool);
  // new shapes use the scaled defaults, then you select to adjust.
  const showStrokeCtl = selected?.type === 'rect' || selected?.type === 'arrow';
  const showBlurCtl = selected?.type === 'blur';
  const strokeVal =
    selected && (selected.type === 'rect' || selected.type === 'arrow')
      ? selected.strokeWidth
      : strokeWidth;
  const blockVal = selected?.type === 'blur' ? selected.blockSize : blockSize;
  const modeVal = selected?.type === 'blur' ? selected.mode : redactMode;
  const selectedIsClick = selectedId === CLICK_ID;
  // Color applies to the click marker + box/arrow/text/stamp (not redactions).
  const showColorCtl = selectedIsClick || (!!selected && selected.type !== 'blur');
  const colorOf = (s: Annotation): string =>
    s.type === 'text' || s.type === 'stamp'
      ? s.fill
      : s.type === 'rect' || s.type === 'arrow'
        ? s.stroke
        : s.type === 'marker'
          ? s.color
          : color;
  const colorVal = selectedIsClick ? markerColor : selected ? colorOf(selected) : color;

  return (
    <div className="ed">
      <div className="ed__toolbar">
        <div className="ed__tools" role="toolbar" aria-label="Editor tools">
          {TOOLS.map((t) => (
            <button
              key={t.tool}
              type="button"
              className={`ed__tool${tool === t.tool ? ' ed__tool--on' : ''}`}
              title={t.hint}
              aria-pressed={tool === t.tool}
              onClick={() => {
                setTool(t.tool);
                if (t.tool !== 'select') setSelectedId(null);
                if (t.tool === 'crop') {
                  setViewCropped(false); // re-crop on the full image
                  setEditorZoom(1);
                }
              }}
            >
              {t.label}
            </button>
          ))}
        </div>

        <div className="ed__spacer" />

        {crop && (
          <button
            type="button"
            className="btn btn--small"
            onClick={() => {
              setViewCropped((v) => !v);
              setEditorZoom(1);
            }}
            title="Work on just the cropped region (non-destructive)"
          >
            {viewCropped ? 'Show full' : 'Apply crop'}
          </button>
        )}
        {crop && (
          <button
            type="button"
            className="btn btn--small"
            onClick={() => {
              setCrop(null);
              setViewCropped(false);
            }}
          >
            Reset crop
          </button>
        )}
        {selectedId && (
          <button
            type="button"
            className="btn btn--small"
            onClick={() => {
              if (selectedId === CLICK_ID) {
                // Remove the click marker; onSave persists click:null.
                setClickImage(null);
                setSelectedId(null);
              } else {
                remove(selectedId);
              }
            }}
          >
            {selectedId === CLICK_ID ? 'Remove marker' : 'Delete'}
          </button>
        )}
        <button type="button" className="btn btn--small" onClick={onClose} disabled={saving}>
          Cancel
        </button>
        <button
          type="button"
          className="btn btn--primary btn--small"
          onClick={() => void onSave()}
          disabled={saving || !img}
        >
          {saving ? 'Saving…' : 'Save'}
        </button>
      </div>

      {/* Properties row — reserved height so the modal doesn't reflow/grow. */}
      <div className="ed__props">
        <div className="ed__zoom" title="Zoom the canvas for precise editing">
          <button
            type="button"
            className="ed__tool"
            onClick={() => setEditorZoom((z) => Math.max(0.5, z / 1.25))}
          >
            −
          </button>
          <button
            type="button"
            className="ed__tool"
            title="Fit"
            onClick={() => setEditorZoom(1)}
          >
            {Math.round(editorZoom * 100)}%
          </button>
          <button
            type="button"
            className="ed__tool"
            onClick={() => setEditorZoom((z) => Math.min(8, z * 1.25))}
          >
            +
          </button>
        </div>
        {showColorCtl && (
          <label className="ed__opt ed__opt--color" title="Color">
            Color
            <input
              type="color"
              value={colorVal}
              onChange={(e) => changeColor(e.target.value)}
            />
          </label>
        )}
        {showStrokeCtl && (
          <label className="ed__opt" title="Line width">
            Width
            <input
              type="range"
              min={1}
              max={80}
              value={strokeVal}
              onChange={(e) => changeStroke(Number(e.target.value))}
            />
          </label>
        )}
        {showBlurCtl && (
          <>
            <label className="ed__opt" title="How redaction is baked in">
              Redact
              <select value={modeVal} onChange={(e) => changeMode(e.target.value as 'pixelate' | 'solid')}>
                <option value="pixelate">Blur</option>
                <option value="solid">Black box</option>
              </select>
            </label>
            {modeVal === 'pixelate' && (
              <label
                className="ed__opt"
                title="Blur strength — higher is stronger; the minimum keeps text unreadable"
              >
                Strength
                <input
                  type="range"
                  min={MIN_REDACT_BLOCK}
                  max={60}
                  value={blockVal}
                  onChange={(e) => changeBlock(Number(e.target.value))}
                />
              </label>
            )}
          </>
        )}
        {!showColorCtl && !showStrokeCtl && !showBlurCtl && (
          <span className="ed__props-hint">
            Select an element to change its color, width, or blur.
          </span>
        )}
      </div>

      {error && <p className="project__error">Error: {error}</p>}

      {textEntry && (
        <div className="ed__textbar">
          <span className="ed__textlabel">
            {textEntry.id ? 'Edit text' : 'Add text'}
          </span>
          <input
            ref={textInputRef}
            className="ed__textfield"
            value={textEntry.value}
            placeholder="Type the label, then Add (or Enter)…"
            onChange={(e) =>
              setTextEntry((cur) => (cur ? { ...cur, value: e.target.value } : cur))
            }
            onKeyDown={(e) => {
              if (e.key === 'Enter') commitText();
              else if (e.key === 'Escape') setTextEntry(null);
              e.stopPropagation();
            }}
          />
          <label className="ed__opt" title="Text size">
            Size
            <input
              type="range"
              min={10}
              max={160}
              value={textEntry.fontSize}
              onChange={(e) =>
                setTextEntry((cur) =>
                  cur ? { ...cur, fontSize: Number(e.target.value) } : cur,
                )
              }
            />
          </label>
          <button
            type="button"
            className="btn btn--small btn--primary"
            onClick={commitText}
          >
            Add
          </button>
          <button
            type="button"
            className="btn btn--small"
            onClick={() => setTextEntry(null)}
          >
            Cancel
          </button>
        </div>
      )}

      <div className="ed__canvas" style={{ width: VIEW_W, height: VIEW_H }}>
        {!img ? (
          <p className="project__hint">Loading screenshot…</p>
        ) : (
          <Stage
            ref={stageRef}
            width={cropView.w}
            height={cropView.h}
            scaleX={cropView.scale}
            scaleY={cropView.scale}
            x={cropView.x}
            y={cropView.y}
            onMouseDown={onStageMouseDown}
            onMouseMove={onStageMouseMove}
            style={{ cursor: selectable ? 'default' : 'crosshair' }}
          >
            <Layer listening={false}>
              <KImage image={img} width={natW} height={natH} />
            </Layer>
            <Layer>
              {annotations.map((a) => {
                // blur is its own component (no `common` spread) — handle first
                // so we don't build/invoke setRef twice for it.
                if (a.type === 'blur') {
                  return (
                    <BlurRegion
                      key={a.id}
                      img={img}
                      a={a}
                      draggable={selectable}
                      registerRef={setRef(a.id)}
                      onSelect={() => selectable && setSelectedId(a.id)}
                      onDragEnd={(e) => update(a.id, { x: e.target.x(), y: e.target.y() })}
                      onTransformEnd={() => onTransformEnd(a.id)}
                    />
                  );
                }
                const common = {
                  key: a.id,
                  ref: setRef(a.id),
                  draggable: selectable,
                  // hide the (state-based) selection outline while dragging so it
                  // doesn't trail the shape; it's recomputed on dragEnd.
                  onDragStart: () => setSelBox(null),
                  onClick: () => selectable && setSelectedId(a.id),
                  onTap: () => selectable && setSelectedId(a.id),
                };
                if (a.type === 'rect') {
                  return (
                    <KRect
                      {...common}
                      x={a.x}
                      y={a.y}
                      width={a.width}
                      height={a.height}
                      cornerRadius={a.cornerRadius}
                      stroke={a.stroke}
                      strokeWidth={a.strokeWidth}
                      fill={a.fill ?? undefined}
                      onDragEnd={(e) => update(a.id, { x: e.target.x(), y: e.target.y() })}
                      onTransformEnd={() => onTransformEnd(a.id)}
                    />
                  );
                }
                if (a.type === 'arrow') {
                  return (
                    <KArrow
                      {...common}
                      points={a.points}
                      stroke={a.stroke}
                      fill={a.stroke}
                      strokeWidth={a.strokeWidth}
                      pointerLength={Math.max(12, a.strokeWidth * 3)}
                      pointerWidth={Math.max(12, a.strokeWidth * 3)}
                      lineCap="round"
                      onDragEnd={(e) => {
                        const dx = e.target.x();
                        const dy = e.target.y();
                        e.target.position({ x: 0, y: 0 });
                        setAnnotations((prev) =>
                          prev.map((x) =>
                            x.id === a.id && x.type === 'arrow'
                              ? {
                                  ...x,
                                  points: [
                                    x.points[0] + dx,
                                    x.points[1] + dy,
                                    x.points[2] + dx,
                                    x.points[3] + dy,
                                  ],
                                }
                              : x,
                          ),
                        );
                      }}
                    />
                  );
                }
                if (a.type === 'stamp') {
                  return (
                    <Group
                      {...common}
                      x={a.x}
                      y={a.y}
                      onDragEnd={(e) => update(a.id, { x: e.target.x(), y: e.target.y() })}
                    >
                      <Circle radius={a.radius} fill={a.fill} />
                      <KText
                        text={String(a.n)}
                        fontSize={Math.round(a.radius * 1.15)}
                        fontStyle="bold"
                        fill={a.textColor}
                        width={a.radius * 2}
                        height={a.radius * 2}
                        offsetX={a.radius}
                        offsetY={a.radius}
                        align="center"
                        verticalAlign="middle"
                      />
                    </Group>
                  );
                }
                if (a.type === 'marker') {
                  // A second click-register ring (e.g. brought in by merging two
                  // steps). Same visual as the step's own click marker; movable.
                  return (
                    <Circle
                      {...common}
                      x={a.x}
                      y={a.y}
                      radius={markerR}
                      stroke={a.color}
                      strokeWidth={Math.max(2, Math.round(markerR * 0.22))}
                      fill={`${a.color}2e`}
                      onDragEnd={(e) => update(a.id, { x: e.target.x(), y: e.target.y() })}
                    />
                  );
                }
                // text
                return (
                  <KText
                    {...common}
                    x={a.x}
                    y={a.y}
                    text={a.text}
                    fontSize={a.fontSize}
                    fill={a.fill}
                    onDblClick={() =>
                      setTextEntry({
                        imageX: a.x,
                        imageY: a.y,
                        id: a.id,
                        value: a.text,
                        fontSize: a.fontSize,
                      })
                    }
                    onDragEnd={(e) => update(a.id, { x: e.target.x(), y: e.target.y() })}
                  />
                );
              })}

              {/* movable click-register marker */}
              {clickImage && (
                <Circle
                  x={clickImage.x}
                  y={clickImage.y}
                  radius={markerR}
                  stroke={markerColor}
                  strokeWidth={Math.max(2, Math.round(markerR * 0.22))}
                  fill={`${markerColor}2e`}
                  draggable={selectable}
                  onClick={() => selectable && setSelectedId(CLICK_ID)}
                  onTap={() => selectable && setSelectedId(CLICK_ID)}
                  onDragStart={() => setSelBox(null)}
                  onDragEnd={(e) => setClickImage({ x: e.target.x(), y: e.target.y() })}
                />
              )}

              {/* in-progress drag previews */}
              {draft && (tool === 'rect' || tool === 'crop') && (
                <KRect
                  x={draft.x}
                  y={draft.y}
                  width={draft.width}
                  height={draft.height}
                  cornerRadius={tool === 'rect' ? 10 : 0}
                  stroke={tool === 'crop' ? '#2563eb' : ACCENT}
                  strokeWidth={tool === 'crop' ? 3 : strokeWidth}
                  dash={tool === 'crop' ? [10, 6] : undefined}
                  listening={false}
                />
              )}
              {draft && tool === 'blur' && (
                <KRect
                  x={draft.x}
                  y={draft.y}
                  width={draft.width}
                  height={draft.height}
                  fill="rgba(15,23,42,0.55)"
                  listening={false}
                />
              )}
              {arrowDraft && (
                <KArrow
                  points={arrowDraft}
                  stroke={ACCENT}
                  fill={ACCENT}
                  strokeWidth={strokeWidth}
                  pointerLength={Math.max(12, strokeWidth * 3)}
                  pointerWidth={Math.max(12, strokeWidth * 3)}
                  listening={false}
                />
              )}

              {crop && (
                <KRect
                  x={crop.x}
                  y={crop.y}
                  width={crop.width}
                  height={crop.height}
                  stroke="#2563eb"
                  strokeWidth={3}
                  dash={[10, 6]}
                  listening={false}
                />
              )}

              {/* live text preview: shows where the text will land + its size */}
              {textEntry && (
                <KText
                  x={textEntry.imageX}
                  y={textEntry.imageY}
                  text={textEntry.value || 'Text…'}
                  fontSize={textEntry.fontSize}
                  fill={color}
                  opacity={0.65}
                  listening={false}
                />
              )}

              {/* selection outline (arrow / stamp / text) */}
              {selBox && (
                <KRect
                  x={selBox.x - 6}
                  y={selBox.y - 6}
                  width={selBox.width + 12}
                  height={selBox.height + 12}
                  stroke="#4f46e5"
                  strokeWidth={2}
                  dash={[6, 4]}
                  listening={false}
                />
              )}

              <Transformer
                ref={trRef}
                rotateEnabled={false}
                flipEnabled={false}
                ignoreStroke
                boundBoxFunc={(oldBox, newBox) =>
                  newBox.width < 8 || newBox.height < 8 ? oldBox : newBox
                }
              />
            </Layer>
          </Stage>
        )}
      </div>

      <p className="ed__hint">
        {tool === 'select'
          ? 'Click to select; drag to move; handles to resize. The ring is the click point — drag it, or select it and use Remove marker to delete it.'
          : tool === 'crop'
            ? 'Drag to set the crop region. Reset crop to clear.'
            : tool === 'stamp'
              ? 'Click to place a numbered stamp.'
              : tool === 'text'
                ? 'Click where the text should go, then type.'
                : 'Drag to draw.'}{' '}
        Redactions are baked into the exported image on save.
      </p>
    </div>
  );
}
