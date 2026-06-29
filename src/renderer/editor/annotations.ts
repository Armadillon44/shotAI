// Editor tool types, default styles, and annotation factories. Geometry is in
// IMAGE (screenshot) pixel coordinates — see shared/project.ts.
import type {
  Annotation,
  ArrowAnnotation,
  BlurAnnotation,
  MarkerAnnotation,
  RectAnnotation,
  StampAnnotation,
  TextAnnotation,
} from '../../shared/project';

export type Tool =
  | 'select'
  | 'rect'
  | 'arrow'
  | 'blur'
  | 'stamp'
  | 'marker'
  | 'text'
  | 'crop';

export const TOOLS: { tool: Tool; label: string; hint: string }[] = [
  { tool: 'select', label: 'Select', hint: 'Select / move / resize (V)' },
  { tool: 'rect', label: 'Box', hint: 'Rounded rectangle' },
  { tool: 'arrow', label: 'Arrow', hint: 'Arrow' },
  { tool: 'blur', label: 'Redact', hint: 'Blur / redact a region (baked into the export)' },
  { tool: 'stamp', label: 'Number', hint: 'Numbered step stamp' },
  { tool: 'marker', label: 'Marker', hint: 'Click-point ring — click to place' },
  { tool: 'text', label: 'Text', hint: 'Text label' },
  { tool: 'crop', label: 'Crop', hint: 'Crop the screenshot' },
];

// High-contrast accent for outlines/arrows/stamps.
export const ACCENT = '#e11d48'; // rose-600
export const DEFAULT_STROKE_WIDTH = 4;
export const DEFAULT_BLOCK_SIZE = 14;

function newId(): string {
  try {
    return crypto.randomUUID();
  } catch {
    return `a-${Date.now().toString(36)}-${Math.round(Math.random() * 1e9).toString(36)}`;
  }
}

export function createRect(
  x: number,
  y: number,
  w: number,
  h: number,
  strokeWidth = DEFAULT_STROKE_WIDTH,
  color = ACCENT,
): RectAnnotation {
  return {
    id: newId(),
    type: 'rect',
    x,
    y,
    width: w,
    height: h,
    cornerRadius: 10,
    stroke: color,
    strokeWidth,
    fill: null,
  };
}

export function createArrow(
  x1: number,
  y1: number,
  x2: number,
  y2: number,
  strokeWidth = DEFAULT_STROKE_WIDTH,
  color = ACCENT,
): ArrowAnnotation {
  return {
    id: newId(),
    type: 'arrow',
    points: [x1, y1, x2, y2],
    stroke: color,
    strokeWidth,
  };
}

export function createBlur(
  x: number,
  y: number,
  w: number,
  h: number,
  mode: BlurAnnotation['mode'] = 'pixelate',
  blockSize = DEFAULT_BLOCK_SIZE,
): BlurAnnotation {
  return {
    id: newId(),
    type: 'blur',
    x,
    y,
    width: w,
    height: h,
    mode,
    blockSize,
  };
}

/** Radius (image px) for the click-register marker, scaled to the image size. */
export function clickMarkerRadius(naturalW: number, naturalH: number): number {
  return Math.max(14, Math.min(60, Math.round(Math.min(naturalW, naturalH) * 0.02)));
}

export function createStamp(
  x: number,
  y: number,
  n: number,
  radius = 22,
  color = ACCENT,
): StampAnnotation {
  return {
    id: newId(),
    type: 'stamp',
    x,
    y,
    n,
    radius,
    fill: color,
    textColor: '#ffffff',
  };
}

/** Default line width scaled to the image so it reads boldly on large captures. */
export function defaultStrokeWidth(naturalW: number, naturalH: number): number {
  return Math.max(4, Math.min(50, Math.round(Math.min(naturalW, naturalH) * 0.008)));
}

/** Default numbered-stamp radius, scaled to the image. */
export function defaultStampRadius(naturalW: number, naturalH: number): number {
  return Math.max(16, Math.min(72, Math.round(Math.min(naturalW, naturalH) * 0.022)));
}

export function createText(
  x: number,
  y: number,
  text: string,
  fontSize = 28,
  color = ACCENT,
): TextAnnotation {
  return {
    id: newId(),
    type: 'text',
    x,
    y,
    text,
    fontSize,
    fill: color,
  };
}

/** Default text size, scaled to the image. */
export function defaultFontSize(naturalW: number, naturalH: number): number {
  return Math.max(16, Math.min(96, Math.round(Math.min(naturalW, naturalH) * 0.022)));
}

/** A movable click-register ring. Radius is derived from the image at draw time
 *  (mirrors the baked click marker), so only center + color are stored. */
export function createMarker(x: number, y: number, color = ACCENT): MarkerAnnotation {
  return { id: newId(), type: 'marker', x, y, color };
}

/** Normalize a drag (start/end points) to a top-left rect with positive size. */
export function dragRect(
  x1: number,
  y1: number,
  x2: number,
  y2: number,
): { x: number; y: number; width: number; height: number } {
  return {
    x: Math.min(x1, x2),
    y: Math.min(y1, y2),
    width: Math.abs(x2 - x1),
    height: Math.abs(y2 - y1),
  };
}

export function isAnnotation(a: unknown): a is Annotation {
  return !!a && typeof a === 'object' && typeof (a as { type?: unknown }).type === 'string';
}
