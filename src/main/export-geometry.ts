// Pure geometry for reproducing the report's per-step zoom/pan as a static crop
// on export. Kept electron-free (no nativeImage) so it can be unit-tested; the
// actual pixel crop lives in export.ts and calls this to get the rectangle.

export interface CropRect {
  x: number;
  y: number;
  width: number;
  height: number;
}

/**
 * The visible sub-region (in natural image pixels) shown by StepFigure for a
 * given zoom/pan, or null when the whole image is visible (zoom <= 1) or the
 * inputs are degenerate. Mirrors Report.tsx's viewport math:
 *
 *   baseScale fits the image into the report box; the image is then drawn at
 *   `zoom` inside a fixed box of `min(zoom,1)` of the fit size and panned. The
 *   window in natural pixels works out to `size * boxScale/zoom` at offset
 *   `size * (zoom - boxScale) * pan / zoom` — baseScale cancels out.
 *
 * pan is a fraction 0..1 of the scrollable range (0.5 = centered), matching the
 * persisted reportPanX/reportPanY.
 */
export function zoomCropRect(
  width: number,
  height: number,
  zoom: number,
  panX: number,
  panY: number,
): CropRect | null {
  if (!(zoom > 1)) return null; // zoom <= 1 or NaN → full image, as displayed
  if (!(width >= 2) || !(height >= 2)) return null; // too small / invalid to crop
  const boxScale = Math.min(zoom, 1); // === 1 for zoom > 1; kept for parity
  const w = Math.max(1, Math.min(width, Math.round((width * boxScale) / zoom)));
  const h = Math.max(1, Math.min(height, Math.round((height * boxScale) / zoom)));
  if (w >= width && h >= height) return null; // nothing to crop
  const px = clamp01(panX);
  const py = clamp01(panY);
  const x = Math.max(0, Math.min(width - w, Math.round((width * (zoom - boxScale) * px) / zoom)));
  const y = Math.max(0, Math.min(height - h, Math.round((height * (zoom - boxScale) * py) / zoom)));
  return { x, y, width: w, height: h };
}

function clamp01(n: number): number {
  if (!Number.isFinite(n)) return 0.5; // default to centered on a bad value
  return n < 0 ? 0 : n > 1 ? 1 : n;
}

/**
 * Max width (px) for a shot image in the plain "HTML (for Word)" export. Matches
 * the macOS app's `plainExportImageMaxWidth` so both platforms paste at the same
 * size.
 */
export const PLAIN_IMG_MAX_W = 738;

/**
 * The width/height ATTRIBUTES for a plain-export `<img>`, or null when the decoded
 * size is unusable (the caller then emits no attributes rather than a bogus size).
 *
 * Attributes, not CSS: Word and Google Docs drop the stylesheet's `max-width` on
 * paste and lay the image out at its native pixel size, so a 2560px capture pastes
 * in far oversized. The attributes survive the paste. Caps at PLAIN_IMG_MAX_W
 * preserving aspect; a capture already narrower than the cap keeps its native
 * size. Mirrors macOS ExportKit/HTMLExport.swift.
 */
export function plainImageSize(
  width: number,
  height: number,
): { w: number; h: number } | null {
  if (!Number.isFinite(width) || !Number.isFinite(height)) return null;
  if (!(width >= 1) || !(height >= 1)) return null; // 0x0 / negative → undecodable
  const scale = Math.min(1, PLAIN_IMG_MAX_W / width);
  return {
    w: Math.max(1, Math.round(width * scale)),
    h: Math.max(1, Math.round(height * scale)),
  };
}
