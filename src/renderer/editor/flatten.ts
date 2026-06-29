// Pure flatten: a screenshot + its annotations -> a single PNG Blob, with the
// crop applied and blur/redact regions BAKED destructively into the pixels.
//
// Security-critical: redaction must be irreversible in the output. We mosaic
// (downscale-then-upscale) or solid-fill the region on the composited canvas
// BEFORE drawing any vector overlay, so the original pixels under a redaction
// are gone from the result. Exports and the Claude pass consume only this
// flattened PNG — never the raw shots/*.png. Kept free of React/Konva so it can
// be reasoned about (and unit-tested) on its own.
import type { Annotation, BlurAnnotation, Rect } from '../../shared/project';

// Minimum redaction downsample factor (image px). Below this, averaged text can
// stay legible, so the bake clamps up to it regardless of the stored value.
export const MIN_REDACT_BLOCK = 8;

function clamp(v: number, lo: number, hi: number): number {
  return Math.max(lo, Math.min(v, hi));
}

function finite(v: number): number {
  return Number.isFinite(v) ? v : 0;
}

/**
 * The click-register marker to bake into the output, in image (uncropped) px.
 * Baking it (vs. an overlay) means the Claude vision pass and exports both see
 * exactly where the user clicked — otherwise Claude has no idea and guesses.
 */
export interface FlattenMarker {
  x: number;
  y: number;
  /** Ring color (CSS color string). */
  color: string;
}

/** Flatten to a PNG Blob. `crop` (image px) selects the region; null = whole image. */
export async function flattenToPng(
  image: HTMLImageElement,
  annotations: Annotation[],
  crop: Rect | null,
  marker: FlattenMarker | null = null,
): Promise<Blob> {
  const nw = image.naturalWidth;
  const nh = image.naturalHeight;
  const cx = crop ? clamp(Math.round(crop.x), 0, Math.max(0, nw - 1)) : 0;
  const cy = crop ? clamp(Math.round(crop.y), 0, Math.max(0, nh - 1)) : 0;
  const cw = crop ? clamp(Math.round(crop.width), 1, nw - cx) : nw;
  const ch = crop ? clamp(Math.round(crop.height), 1, nh - cy) : nh;

  const canvas = document.createElement('canvas');
  canvas.width = cw;
  canvas.height = ch;
  const ctx = canvas.getContext('2d');
  if (!ctx) throw new Error('2d canvas context unavailable');

  // 1) the (cropped) source
  ctx.drawImage(image, cx, cy, cw, ch, 0, 0, cw, ch);
  // 2) BAKE redaction destructively, before any overlay. Fail CLOSED: if a blur
  //    overlaps the exported region but can't be baked (rounds to <1px), throw —
  //    never emit a PNG with an un-obscured area the user believes is redacted.
  for (const a of annotations) {
    if (a.type !== 'blur') continue;
    const bx = finite(a.x);
    const by = finite(a.y);
    const bw = finite(a.width);
    const bh = finite(a.height);
    // overlap of the blur with the output (crop) region, in image px
    const ow = Math.min(bx + bw, cx + cw) - Math.max(bx, cx);
    const oh = Math.min(by + bh, cy + ch) - Math.max(by, cy);
    if (ow <= 0 || oh <= 0) continue; // blur lies entirely outside the export
    const baked = bakeRedaction(ctx, { ...a, x: bx, y: by, width: bw, height: bh }, cx, cy);
    if (!baked) {
      throw new Error(
        'A redaction region could not be applied (too small or off-image). Adjust or remove it, then save again.',
      );
    }
  }
  // 3) vector overlay on top (image-px coords -> cropped-canvas coords)
  ctx.save();
  ctx.translate(-cx, -cy);
  for (const a of annotations) drawVector(ctx, a);
  ctx.restore();
  // 4) click-register markers, baked on top so Claude's vision + exports see the
  //    clicked spot(s). Off-canvas (outside the crop) draws harmlessly clipped.
  //    The step's own click marker (param) plus any 'marker' annotations (e.g. a
  //    second click brought in by merging two steps) all render with one style.
  const markerRadius = clickMarkerRadius(nw, nh);
  if (marker) drawClickMarker(ctx, marker, markerRadius, cx, cy);
  for (const a of annotations) {
    if (a.type === 'marker') drawClickMarker(ctx, { x: a.x, y: a.y, color: a.color }, markerRadius, cx, cy);
  }

  return await new Promise<Blob>((resolve, reject) => {
    canvas.toBlob(
      (b) => (b ? resolve(b) : reject(new Error('canvas.toBlob returned null'))),
      'image/png',
    );
  });
}

/**
 * Destructively obscure one region of the composited canvas. Returns true if it
 * actually filled ≥1px; false if the region clamped to nothing (caller fails the
 * save rather than emit an unprotected region).
 */
function bakeRedaction(
  ctx: CanvasRenderingContext2D,
  a: BlurAnnotation,
  cropX: number,
  cropY: number,
): boolean {
  // Region in canvas (cropped) coordinates, clamped to the canvas.
  const x = Math.round(a.x - cropX);
  const y = Math.round(a.y - cropY);
  const x0 = clamp(x, 0, ctx.canvas.width);
  const y0 = clamp(y, 0, ctx.canvas.height);
  const x1 = clamp(x + Math.round(a.width), 0, ctx.canvas.width);
  const y1 = clamp(y + Math.round(a.height), 0, ctx.canvas.height);
  const w = x1 - x0;
  const h = y1 - y0;
  if (w <= 0 || h <= 0) return false;

  if (a.mode === 'solid') {
    ctx.fillStyle = '#000000';
    ctx.fillRect(x0, y0, w, h);
    return true;
  }

  // Soft blur: AVERAGE-downsample the region to a few samples (destructive —
  // text is averaged away irreversibly), then rebuild it as a SOFT blur:
  //   1) draw the downsample back up as an opaque base (no original can show
  //      through, even at edges),
  //   2) draw it again with a Gaussian on top (clipped to the rect) to dissolve
  //      the upscale banding into a smooth blur.
  // `block` is the downsample factor (≈ blur feature size), floored at
  // MIN_REDACT_BLOCK so a hand-edited manifest can't blur text back into legibility.
  const block = Math.max(MIN_REDACT_BLOCK, Math.round(a.blockSize || 12));
  const sw = Math.max(1, Math.round(w / block));
  const sh = Math.max(1, Math.round(h / block));
  const tmp = document.createElement('canvas');
  tmp.width = sw;
  tmp.height = sh;
  const tctx = tmp.getContext('2d');
  if (!tctx) {
    ctx.fillStyle = '#000000'; // fail closed: solid-fill rather than leak pixels
    ctx.fillRect(x0, y0, w, h);
    return true;
  }
  tctx.imageSmoothingEnabled = true; // averaging downsample
  tctx.drawImage(ctx.canvas, x0, y0, w, h, 0, 0, sw, sh);
  ctx.save();
  ctx.imageSmoothingEnabled = true;
  ctx.drawImage(tmp, 0, 0, sw, sh, x0, y0, w, h); // opaque destroyed base
  ctx.beginPath();
  ctx.rect(x0, y0, w, h);
  ctx.clip(); // keep the Gaussian's bleed off the un-redacted pixels
  ctx.filter = `blur(${Math.max(1, Math.round(block * 0.6))}px)`;
  ctx.drawImage(tmp, 0, 0, sw, sh, x0, y0, w, h);
  ctx.restore(); // also resets filter + smoothing + clip
  return true;
}

/** Marker ring radius from image size — mirror of clickMarkerRadius() in annotations.ts. */
function clickMarkerRadius(naturalW: number, naturalH: number): number {
  return Math.max(14, Math.min(60, Math.round(Math.min(naturalW, naturalH) * 0.02)));
}

/** Draw the click ring (translucent fill + solid stroke) at the click point. */
function drawClickMarker(
  ctx: CanvasRenderingContext2D,
  marker: FlattenMarker,
  radius: number,
  cropX: number,
  cropY: number,
): void {
  const x = finite(marker.x) - cropX;
  const y = finite(marker.y) - cropY;
  ctx.save();
  ctx.beginPath();
  ctx.arc(x, y, radius, 0, Math.PI * 2);
  ctx.globalAlpha = 0.18;
  ctx.fillStyle = marker.color;
  ctx.fill();
  ctx.globalAlpha = 1;
  ctx.lineWidth = Math.max(2, Math.round(radius * 0.22));
  ctx.strokeStyle = marker.color;
  ctx.stroke();
  ctx.restore();
}

function roundRectPath(
  ctx: CanvasRenderingContext2D,
  x: number,
  y: number,
  w: number,
  h: number,
  r: number,
): void {
  const radius = Math.max(0, Math.min(r, Math.abs(w) / 2, Math.abs(h) / 2));
  ctx.beginPath();
  ctx.roundRect(x, y, w, h, radius);
}

function drawVector(ctx: CanvasRenderingContext2D, a: Annotation): void {
  switch (a.type) {
    case 'rect': {
      roundRectPath(ctx, a.x, a.y, a.width, a.height, a.cornerRadius);
      if (a.fill) {
        ctx.fillStyle = a.fill;
        ctx.fill();
      }
      ctx.strokeStyle = a.stroke;
      ctx.lineWidth = a.strokeWidth;
      ctx.stroke();
      break;
    }
    case 'arrow': {
      const [x1, y1, x2, y2] = a.points;
      ctx.strokeStyle = a.stroke;
      ctx.fillStyle = a.stroke;
      ctx.lineWidth = a.strokeWidth;
      ctx.lineCap = 'round';
      ctx.beginPath();
      ctx.moveTo(x1, y1);
      ctx.lineTo(x2, y2);
      ctx.stroke();
      // arrowhead
      const angle = Math.atan2(y2 - y1, x2 - x1);
      const head = Math.max(12, a.strokeWidth * 3);
      ctx.beginPath();
      ctx.moveTo(x2, y2);
      ctx.lineTo(
        x2 - head * Math.cos(angle - Math.PI / 6),
        y2 - head * Math.sin(angle - Math.PI / 6),
      );
      ctx.lineTo(
        x2 - head * Math.cos(angle + Math.PI / 6),
        y2 - head * Math.sin(angle + Math.PI / 6),
      );
      ctx.closePath();
      ctx.fill();
      break;
    }
    case 'stamp': {
      ctx.beginPath();
      ctx.arc(a.x, a.y, a.radius, 0, Math.PI * 2);
      ctx.fillStyle = a.fill;
      ctx.fill();
      ctx.fillStyle = a.textColor;
      ctx.font = `bold ${Math.round(a.radius * 1.15)}px "Segoe UI", sans-serif`;
      ctx.textAlign = 'center';
      ctx.textBaseline = 'middle';
      ctx.fillText(String(a.n), a.x, a.y);
      break;
    }
    case 'text': {
      ctx.fillStyle = a.fill;
      ctx.font = `${a.fontSize}px "Segoe UI", sans-serif`;
      ctx.textAlign = 'left';
      ctx.textBaseline = 'top';
      ctx.fillText(a.text, a.x, a.y);
      break;
    }
    case 'blur':
      break; // already baked in step 2
    case 'marker':
      break; // drawn in step 4 (click-marker style), after the vector pass
  }
}
