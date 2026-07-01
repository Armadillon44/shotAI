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
import { Notice, type NoticeData } from '../Notice';
import './editor.css';

const CLICK_ID = '__click__'; // pseudo-selection id for the click marker
const CROP_ID = '__crop__'; // pseudo-selection id for the (resizable) crop box

const VIEW_W = 940;
const VIEW_H = 540;
const MIN_DRAG = 6; // image px — ignore accidental micro-drags

// A glyph per tool + the tool-rail grouping by job (draw / mark / crop), so the
// rail is scannable rather than a row of identical text chips.
const TOOL_ICON: Record<Tool, string> = {
  select: '⬚',
  rect: '▢',
  arrow: '↗',
  blur: '░',
  stamp: '①',
  marker: '◎',
  text: 'T',
  crop: '⛶',
};
const TOOL_GROUPS: { label: string; tools: Tool[] }[] = [
  { label: 'Draw', tools: ['select', 'rect', 'arrow', 'blur'] },
  { label: 'Mark', tools: ['stamp', 'marker', 'text'] },
  { label: 'Crop', tools: ['crop'] },
];
const TOOL_BY_ID = Object.fromEntries(TOOLS.map((t) => [t.tool, t])) as Record<
  Tool,
  (typeof TOOLS)[number]
>;

/** Clamp a rectangle (image px) to lie fully within the image bounds. */
function clampRectToImage(r: Rect, w: number, h: number): Rect {
  const x = Math.max(0, Math.min(r.x, w));
  const y = Math.max(0, Math.min(r.y, h));
  return {
    x,
    y,
    width: Math.max(1, Math.min(r.width, w - x)),
    height: Math.max(1, Math.min(r.height, h - y)),
  };
}

type Props = {
  projectId: string;
  projectPath: string;
  step: ProjectStep;
  onClose: () => void;
  onSaved: (manifest: ProjectManifest) => void;
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
  // Click-marker ring radius (image px); null = use the image-derived default.
  const [clickRadius, setClickRadius] = React.useState<number | null>(
    step.click?.radius ?? null,
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
  // id of the text annotation being edited IN PLACE (overlay textarea on the
  // canvas). null = not editing. The text lives in `annotations` and is updated
  // live as you type; empty texts are dropped on finish/save.
  const [editingTextId, setEditingTextId] = React.useState<string | null>(null);
  const [selBox, setSelBox] = React.useState<Rect | null>(null);
  const [saving, setSaving] = React.useState(false);
  const [scanning, setScanning] = React.useState(false);
  const [notice, setNotice] = React.useState<NoticeData | null>(null);
  // The canvas viewport size — measured from the (flex-grown) container so the
  // stage fills as much of the window as feasible. Seeded with the old fixed
  // defaults until the ResizeObserver reports the real size.
  const [viewport, setViewport] = React.useState({ w: VIEW_W, h: VIEW_H });
  const canvasRef = React.useRef<HTMLDivElement | null>(null);

  const stageRef = React.useRef<Konva.Stage | null>(null);
  const trRef = React.useRef<Konva.Transformer | null>(null);
  const shapeRefs = React.useRef<Map<string, Konva.Node>>(new Map());
  const dragStart = React.useRef<{ x: number; y: number } | null>(null);
  const textEditRef = React.useRef<HTMLInputElement | null>(null);
  const canvasWrapRef = React.useRef<HTMLDivElement | null>(null);
  // True briefly right after inline text editing opens: the creating click's
  // mouse-up steals focus back to the canvas, which would blur the just-focused
  // overlay input and finish/remove the empty text. Absorb that one blur.
  const textEditOpeningRef = React.useRef(false);

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
      if (!cancelled) setNotice({ kind: 'error', text: 'Could not load the screenshot.' });
    };
    im.src = shotUrl(projectId, step.screenshot);
    return () => {
      cancelled = true;
      im.onload = null;
      im.onerror = null;
    };
  }, [projectId, step.screenshot]);

  // Measure the canvas container so the stage fills the available window space.
  React.useEffect(() => {
    const el = canvasRef.current;
    if (!el) return;
    const measure = () => {
      const w = Math.floor(el.clientWidth);
      const h = Math.floor(el.clientHeight);
      if (w > 0 && h > 0) setViewport({ w, h });
    };
    measure();
    const ro = new ResizeObserver(measure);
    ro.observe(el);
    return () => ro.disconnect();
  }, []);

  const natW = img?.naturalWidth ?? 0;
  const natH = img?.naturalHeight ?? 0;
  const scale = natW && natH ? Math.min(viewport.w / natW, viewport.h / natH, 1) : 1;
  const markerR = natW && natH ? clickMarkerRadius(natW, natH) : 20;
  // Stage transform: the visible region (whole image, or — when crop is applied
  // in-line — just the crop) fit to the canvas, times the editing zoom. Pointer
  // coords stay in image space regardless of transform/scroll.
  const region =
    viewCropped && crop ? crop : { x: 0, y: 0, width: natW, height: natH };
  const baseScale =
    viewCropped && crop
      ? Math.min(viewport.w / crop.width, viewport.h / crop.height, 8)
      : scale;
  const stageScale = baseScale * editorZoom;
  const cropView = {
    scale: stageScale,
    x: -region.x * stageScale,
    y: -region.y * stageScale,
    w: Math.max(1, Math.round(region.width * stageScale)),
    h: Math.max(1, Math.round(region.height * stageScale)),
  };

  const selected = annotations.find((a) => a.id === selectedId) ?? null;

  // Attach the Transformer to the selected resizable shape. Resizable: rect,
  // blur, arrow, stamp, marker annotations + the click marker + the crop box.
  React.useEffect(() => {
    const tr = trRef.current;
    if (!tr) return;
    const node = selectedId ? shapeRefs.current.get(selectedId) : undefined;
    const annResizable =
      !!selected &&
      (selected.type === 'rect' ||
        selected.type === 'blur' ||
        selected.type === 'arrow' ||
        selected.type === 'stamp' ||
        selected.type === 'marker');
    const pseudoResizable = selectedId === CLICK_ID || selectedId === CROP_ID;
    // Lock ratio only for circular things (stamp/marker/click ring) so they stay
    // round; rect / blur / crop / arrow resize freely on every handle.
    const circular =
      selectedId === CLICK_ID || selected?.type === 'stamp' || selected?.type === 'marker';
    tr.keepRatio(circular);
    if (tool === 'select' && node && (annResizable || pseudoResizable)) {
      tr.nodes([node]);
    } else {
      tr.nodes([]);
    }
    tr.getLayer()?.batchDraw();
  }, [selectedId, tool, annotations, selected, crop, clickImage, clickRadius]);

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
      setTool('select'); // drop to Select so the new stamp can be recolored/resized
      return;
    }
    if (tool === 'marker') {
      // Place a click-register ring at the click point; recolor/resize via Select.
      const a = createMarker(p.x, p.y, color);
      setAnnotations((prev) => [...prev, a]);
      setSelectedId(a.id);
      setTool('select'); // drop to Select so the new marker can be recolored/resized
      return;
    }
    if (tool === 'text') {
      if (editingTextId) finishTextEdit(); // close any in-progress edit first
      // Create the text immediately and edit it in place — you type directly on
      // the canvas (overlay textarea), no separate field.
      const a = createText(p.x, p.y, '', defaultFontSize(natW, natH), color);
      setAnnotations((prev) => [...prev, a]);
      setSelectedId(a.id);
      setEditingTextId(a.id);
      setTool('select'); // so you can move/resize/recolor it after typing
      return;
    }
    // drag-create tools: rect / arrow / blur / crop
    dragStart.current = p;
    if (tool === 'arrow') setArrowDraft([p.x, p.y, p.x, p.y]);
    else setDraft({ x: p.x, y: p.y, width: 0, height: 0 });
  };

  // Map a window mouse event to IMAGE px via the live stage transform — lets a
  // drag continue (and clamp) even when the cursor roams outside the stage.
  const eventImagePoint = (e: MouseEvent): { x: number; y: number } | null => {
    const stage = stageRef.current;
    if (!stage) return null;
    const rect = stage.container().getBoundingClientRect();
    const sx = stage.scaleX() || 1;
    const sy = stage.scaleY() || 1;
    return {
      x: (e.clientX - rect.left - stage.x()) / sx,
      y: (e.clientY - rect.top - stage.y()) / sy,
    };
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
        setTool('select'); // so it can be recolored/resized right away
      }
      return;
    }
    const r = draft;
    setDraft(null);
    // A misclick (sub-MIN_DRAG) leaves you in the current drawing tool.
    if (!r || r.width < MIN_DRAG || r.height < MIN_DRAG) return;
    if (tool === 'crop') {
      // Clamp to the image so overshooting the edges while dragging is fine.
      setCrop(clampRectToImage(r, natW, natH));
      setTool('select'); // drop to Select; the crop box stays adjustable
    } else if (tool === 'rect') {
      const a = createRect(r.x, r.y, r.width, r.height, strokeWidth, color);
      setAnnotations((prev) => [...prev, a]);
      setSelectedId(a.id);
      setTool('select');
    } else if (tool === 'blur') {
      const a = createBlur(r.x, r.y, r.width, r.height, redactMode, blockSize);
      setAnnotations((prev) => [...prev, a]);
      setSelectedId(a.id);
      setTool('select');
    }
  };

  // Track the drag on the WINDOW (not just the stage) so the cursor can roam
  // past the image edge while drawing — no more fiddly stop-exactly-at-the-edge.
  React.useEffect(() => {
    const onMove = (e: MouseEvent) => {
      const start = dragStart.current;
      if (!start) return;
      const p = eventImagePoint(e);
      if (!p) return;
      if (tool === 'arrow') setArrowDraft([start.x, start.y, p.x, p.y]);
      else setDraft(dragRect(start.x, start.y, p.x, p.y));
    };
    window.addEventListener('mousemove', onMove);
    window.addEventListener('mouseup', finishDrag);
    return () => {
      window.removeEventListener('mousemove', onMove);
      window.removeEventListener('mouseup', finishDrag);
    };
  });

  const onTransformEnd = (id: string) => {
    const node = shapeRefs.current.get(id);
    if (!node) return;
    const sx = Math.abs(node.scaleX());
    const sy = Math.abs(node.scaleY());
    node.scaleX(1);
    node.scaleY(1);

    // Crop box: resize/move the (uncommitted) crop region, clamped to the image.
    if (id === CROP_ID) {
      setCrop(
        clampRectToImage(
          {
            x: node.x(),
            y: node.y(),
            width: Math.max(8, node.width() * sx),
            height: Math.max(8, node.height() * sy),
          },
          natW,
          natH,
        ),
      );
      return;
    }
    // Click marker: a circle — scale uniformly into a new radius.
    if (id === CLICK_ID) {
      const s = Math.max(sx, sy);
      setClickRadius((r) => Math.max(6, Math.round((r ?? markerR) * s)));
      setClickImage({ x: node.x(), y: node.y() });
      return;
    }
    const ann = annotations.find((a) => a.id === id);
    if (!ann) return;
    if (ann.type === 'rect' || ann.type === 'blur') {
      update(id, {
        x: node.x(),
        y: node.y(),
        width: Math.max(4, node.width() * sx),
        height: Math.max(4, node.height() * sy),
      } as Partial<Annotation>);
    } else if (ann.type === 'stamp' || ann.type === 'marker') {
      // Circles — uniform radius from the larger scale.
      const s = Math.max(sx, sy);
      const baseR = ann.type === 'stamp' ? ann.radius : (ann.radius ?? markerR);
      update(id, {
        x: node.x(),
        y: node.y(),
        radius: Math.max(6, Math.round(baseR * s)),
      } as Partial<Annotation>);
    } else if (ann.type === 'arrow') {
      // Scale both endpoints about the node's (possibly Transformer-shifted) origin.
      const ox = node.x();
      const oy = node.y();
      const p = ann.points;
      update(id, {
        points: [ox + p[0] * sx, oy + p[1] * sy, ox + p[2] * sx, oy + p[3] * sy],
      } as Partial<Annotation>);
      node.position({ x: 0, y: 0 });
    }
  };

  React.useEffect(() => {
    const onKey = (e: KeyboardEvent) => {
      const t = e.target as HTMLElement | null;
      if (t && (t.tagName === 'INPUT' || t.tagName === 'TEXTAREA')) return;
      if ((e.key === 'Delete' || e.key === 'Backspace') && selectedId) {
        e.preventDefault();
        if (selectedId === CLICK_ID) {
          setClickImage(null);
          setSelectedId(null);
        } else if (selectedId === CROP_ID) {
          setCrop(null);
          setSelectedId(null);
        } else {
          remove(selectedId);
        }
      } else if (e.key === 'Escape') {
        setSelectedId(null);
        setTool('select');
      }
    };
    window.addEventListener('keydown', onKey);
    return () => window.removeEventListener('keydown', onKey);
  }, [selectedId]);

  // Focus (and select) the overlay textarea when inline text editing opens.
  React.useEffect(() => {
    if (!editingTextId) return;
    textEditOpeningRef.current = true;
    const t = textEditRef.current;
    if (t) {
      t.focus();
      t.select();
    }
    // Backstop: clear the guard even if the spurious blur never comes.
    const clear = setTimeout(() => {
      textEditOpeningRef.current = false;
    }, 300);
    return () => clearTimeout(clear);
  }, [editingTextId]);

  // Selection outline for arrow/stamp/text (rect/blur already show the
  // Transformer's handles). Measured from the node so it fits any shape.
  React.useEffect(() => {
    // Only free text shows a dashed outline; every other selectable element now
    // uses the Transformer's handles, so a separate outline would double up.
    const sel = annotations.find((a) => a.id === selectedId);
    if (!selectedId || !sel || sel.type !== 'text') {
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
  }, [selectedId, annotations]);

  const setRef = (id: string) => (node: Konva.Node | null) => {
    if (node) shapeRefs.current.set(id, node);
    else shapeRefs.current.delete(id);
  };

  // Close inline text editing; drop the text if it was left empty (e.g. placed
  // then clicked away without typing).
  const finishTextEdit = () => {
    const id = editingTextId;
    if (!id) return;
    setEditingTextId(null);
    const a = annotations.find((x) => x.id === id);
    if (a && a.type === 'text' && !a.text.trim()) remove(id);
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

  // Auto-redaction pre-scan: OCR the screenshot (in main), then drop a solid
  // redaction box over each detected SSN / credit-card / API-key region. They're
  // ordinary blur annotations — the user reviews/adjusts/deletes them and Saves,
  // which bakes them via the existing fail-closed flatten path. Best-effort.
  const autoRedact = async () => {
    if (!img || scanning) return;
    setScanning(true);
    setNotice(null);
    try {
      const rects = await window.shotai.projects.redactScan(projectPath, step.id);
      if (!rects.length) {
        setNotice({
          kind: 'info',
          text: 'No sensitive data detected (best-effort — redact manually if needed).',
        });
        return;
      }
      const added = rects.map((r) => createBlur(r.x, r.y, r.width, r.height, 'solid'));
      setAnnotations((prev) => [...prev, ...added]);
      setSelectedId(added[added.length - 1].id);
    } catch (e) {
      setNotice({ kind: 'error', text: e instanceof Error ? e.message : String(e) });
    } finally {
      setScanning(false);
    }
  };

  const onSave = async () => {
    if (!img) return;
    setSaving(true);
    setNotice(null);
    // Text edits are live in `annotations`; just close any open editor and drop
    // empty text annotations so a placed-but-never-typed text isn't baked.
    if (editingTextId) setEditingTextId(null);
    const anns = annotations.filter((a) => !(a.type === 'text' && !a.text.trim()));
    if (anns.length !== annotations.length) setAnnotations(anns);
    try {
      // clickImage is null when the step has no click marker OR the user removed
      // it → persist click:null so the marker is actually deleted (not kept).
      const click =
        step.click && clickImage
          ? {
              ...step.click,
              image: clickImage,
              ...(clickRadius != null ? { radius: clickRadius } : {}),
            }
          : null;
      // Bake the click ring into the render so Claude's vision + exports see it.
      const blob = await flattenToPng(
        img,
        anns,
        crop,
        click
          ? {
              x: click.image.x,
              y: click.image.y,
              color: markerColor,
              radius: clickRadius ?? undefined,
            }
          : null,
      );
      const bytes = new Uint8Array(await blob.arrayBuffer());
      const manifest = await window.shotai.projects.updateStep(
        projectPath,
        step.id,
        { annotations: anns, crop, click, markerColor, markerBaked: true },
        bytes,
      );
      onSaved(manifest);
      onClose();
    } catch (e) {
      setNotice({ kind: 'error', text: e instanceof Error ? e.message : String(e) });
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
      {/* Left tool rail — grouped by job, icon + label, zoom pinned at the base. */}
      <div className="ed__rail" role="toolbar" aria-label="Editor tools">
        {TOOL_GROUPS.map((g) => (
          <div className="ed__railgroup" key={g.label}>
            <span className="ed__raillabel">{g.label}</span>
            {g.tools.map((tl) => {
              const info = TOOL_BY_ID[tl];
              return (
                <button
                  key={tl}
                  type="button"
                  className={`ed__tool${tool === tl ? ' ed__tool--on' : ''}`}
                  title={info.hint}
                  aria-pressed={tool === tl}
                  onClick={() => {
                    if (editingTextId) finishTextEdit(); // close any in-progress edit
                    setTool(tl);
                    if (tl !== 'select') setSelectedId(null);
                    if (tl === 'crop') {
                      setViewCropped(false); // re-crop on the full image
                      setEditorZoom(1);
                    }
                  }}
                >
                  <span className="ed__tool-ico" aria-hidden="true">
                    {TOOL_ICON[tl]}
                  </span>
                  <span className="ed__tool-lbl">{info.label}</span>
                </button>
              );
            })}
          </div>
        ))}
        <div className="ed__railspace" />
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
      </div>

      <div className="ed__main">
        {/* A stable top bar: contextual buttons on the left, Cancel + Save pinned
            to the right so Save never reflows as the cluster changes. */}
        <div className="ed__topbar">
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
          <button
            type="button"
            className="btn btn--small"
            onClick={() => void autoRedact()}
            disabled={scanning || saving || !img}
            title="Scan this screenshot for SSNs, credit cards, and API keys, and add redaction boxes to review"
          >
            {scanning ? 'Scanning…' : 'Auto-redact'}
          </button>
          <div className="ed__spacer" />
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

        {/* Properties bar — ALWAYS present with a reserved height, so toggling a
            selection never reflows/resizes the canvas. Empty shows a quiet hint;
            Delete is the lone destructive control, anchored here with the selection.
            Adding/editing text reuses THIS bar (no extra banner) — see E2. */}
        <div className="ed__props">
          {selectedId ? (
            <>
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
            {selected?.type === 'text' && (
              <label className="ed__opt" title="Text size">
                Size
                <input
                  type="range"
                  min={10}
                  max={160}
                  value={selected.fontSize}
                  onChange={(e) =>
                    update(selected.id, { fontSize: Number(e.target.value) } as Partial<Annotation>)
                  }
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
                <div className="ed__opt" title="How redaction is baked in">
                  Redact
                  <div className="ed__seg" role="radiogroup" aria-label="Redaction style">
                    <button
                      type="button"
                      role="radio"
                      aria-checked={modeVal === 'pixelate'}
                      className={`ed__seg-btn${modeVal === 'pixelate' ? ' ed__seg-btn--on' : ''}`}
                      onClick={() => changeMode('pixelate')}
                    >
                      Blur
                    </button>
                    <button
                      type="button"
                      role="radio"
                      aria-checked={modeVal === 'solid'}
                      className={`ed__seg-btn${modeVal === 'solid' ? ' ed__seg-btn--on' : ''}`}
                      onClick={() => changeMode('solid')}
                    >
                      Black box
                    </button>
                  </div>
                </div>
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
            {selected?.type === 'text' && (
              <button
                type="button"
                className="btn btn--small"
                title="Edit this text (or double-click it)"
                onClick={() => setEditingTextId(selected.id)}
              >
                Edit text
              </button>
            )}
            <div className="ed__spacer" />
            <button
              type="button"
              className="btn btn--small btn--danger"
              onClick={() => {
                if (selectedId === CLICK_ID) {
                  // Remove the click marker; onSave persists click:null.
                  setClickImage(null);
                  setSelectedId(null);
                } else if (selectedId === CROP_ID) {
                  setCrop(null);
                  setSelectedId(null);
                } else {
                  remove(selectedId);
                }
              }}
            >
              {selectedId === CLICK_ID
                ? 'Remove marker'
                : selectedId === CROP_ID
                  ? 'Remove crop'
                  : 'Delete element'}
            </button>
            </>
          ) : (
            <span className="ed__props-hint">
              Select an element to change its color or size, edit its text, or delete it.
            </span>
          )}
        </div>

      <div className="ed__canvaswrap" ref={canvasWrapRef}>
        {notice && (
          <div className="notice-stack">
            <Notice kind={notice.kind} onDismiss={() => setNotice(null)}>
              {notice.text}
            </Notice>
          </div>
        )}
        {/* Inline on-canvas text editor: an input overlaid at the text's screen
            position (E7) so you type directly where the text lands. */}
        {editingTextId &&
          img &&
          (() => {
            const a = annotations.find((x) => x.id === editingTextId);
            const stage = stageRef.current;
            const wrap = canvasWrapRef.current;
            if (!a || a.type !== 'text' || !stage || !wrap) return null;
            const sBox = stage.container().getBoundingClientRect();
            const wBox = wrap.getBoundingClientRect();
            const left = sBox.left - wBox.left + (a.x - region.x) * cropView.scale;
            const top = sBox.top - wBox.top + (a.y - region.y) * cropView.scale;
            return (
              <input
                ref={textEditRef}
                type="text"
                className="ed__textedit"
                value={a.text}
                size={Math.max(a.text.length, 4)}
                placeholder="Type…"
                spellCheck={false}
                style={{
                  left,
                  top,
                  fontSize: `${a.fontSize * cropView.scale}px`,
                  color: a.fill,
                }}
                onChange={(e) =>
                  update(a.id, { text: e.target.value } as Partial<Annotation>)
                }
                onKeyDown={(e) => {
                  if (e.key === 'Enter' || e.key === 'Escape') {
                    e.preventDefault();
                    finishTextEdit();
                  }
                  e.stopPropagation();
                }}
                onBlur={() => {
                  // Absorb the one spurious blur right after opening (focus steal
                  // from the creating click's mouse-up); re-focus and keep editing.
                  if (textEditOpeningRef.current) {
                    textEditOpeningRef.current = false;
                    requestAnimationFrame(() => textEditRef.current?.focus());
                    return;
                  }
                  finishTextEdit();
                }}
              />
            );
          })()}
      <div className="ed__canvas" ref={canvasRef}>
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
                      onTransformEnd={() => onTransformEnd(a.id)}
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
                      onTransformEnd={() => onTransformEnd(a.id)}
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
                      radius={a.radius ?? markerR}
                      stroke={a.color}
                      strokeWidth={Math.max(2, Math.round((a.radius ?? markerR) * 0.22))}
                      fill={`${a.color}2e`}
                      onDragEnd={(e) => update(a.id, { x: e.target.x(), y: e.target.y() })}
                      onTransformEnd={() => onTransformEnd(a.id)}
                    />
                  );
                }
                // text — hidden while its inline overlay editor is open
                return (
                  <KText
                    {...common}
                    x={a.x}
                    y={a.y}
                    text={a.text}
                    fontSize={a.fontSize}
                    fill={a.fill}
                    visible={editingTextId !== a.id}
                    onDblClick={() => selectable && setEditingTextId(a.id)}
                    onDragEnd={(e) => update(a.id, { x: e.target.x(), y: e.target.y() })}
                  />
                );
              })}

              {/* movable + resizable click-register marker */}
              {clickImage && (
                <Circle
                  ref={setRef(CLICK_ID)}
                  x={clickImage.x}
                  y={clickImage.y}
                  radius={clickRadius ?? markerR}
                  stroke={markerColor}
                  strokeWidth={Math.max(2, Math.round((clickRadius ?? markerR) * 0.22))}
                  fill={`${markerColor}2e`}
                  draggable={selectable}
                  onClick={() => selectable && setSelectedId(CLICK_ID)}
                  onTap={() => selectable && setSelectedId(CLICK_ID)}
                  onDragStart={() => setSelBox(null)}
                  onDragEnd={(e) => setClickImage({ x: e.target.x(), y: e.target.y() })}
                  onTransformEnd={() => onTransformEnd(CLICK_ID)}
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
                  ref={setRef(CROP_ID)}
                  x={crop.x}
                  y={crop.y}
                  width={crop.width}
                  height={crop.height}
                  stroke="#2563eb"
                  strokeWidth={3}
                  dash={[10, 6]}
                  // A faint fill makes the whole crop region grab-able to move it;
                  // the Transformer handles resize. Inert while viewing the crop.
                  fill={!viewCropped && selectable ? 'rgba(37,99,235,0.06)' : undefined}
                  listening={!viewCropped && selectable}
                  draggable={!viewCropped && selectable}
                  onClick={() => selectable && setSelectedId(CROP_ID)}
                  onTap={() => selectable && setSelectedId(CROP_ID)}
                  onDragStart={() => setSelBox(null)}
                  onDragEnd={(e) =>
                    setCrop(
                      clampRectToImage(
                        { x: e.target.x(), y: e.target.y(), width: crop.width, height: crop.height },
                        natW,
                        natH,
                      ),
                    )
                  }
                  onTransformEnd={() => onTransformEnd(CROP_ID)}
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
      </div>

      <p className="ed__hint">
        {tool === 'select'
          ? 'Click to select; drag to move; handles to resize. The ring is the click point — drag it, or select it and use Remove marker to delete it.'
          : tool === 'crop'
            ? 'Drag to set the crop region. Reset crop to clear.'
            : tool === 'stamp'
              ? 'Click to place a numbered stamp.'
              : tool === 'text'
                ? 'Click where the text should go and type — it previews live; press Enter, switch tools, or Save to place it.'
                : 'Drag to draw.'}{' '}
        Redactions are baked into the exported image on save.
      </p>
      </div>
    </div>
  );
}
