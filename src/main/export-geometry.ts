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
 * The width a step image is DISPLAYED at in either HTML export: a step card's
 * content column, `HTML_COL_W` 816 − 30 (badge) − 16 (gap) − 32 (card padding) =
 * 738px. **Keep in sync with export-css.ts.** Matches the macOS app's
 * `htmlExportImageMaxWidth`.
 *
 * Measured footnote: `.step__main` also has a 1px border each side, and
 * `*{box-sizing:border-box}` makes that eat the content box — so an image actually
 * lays out at **736px**. 738 is kept anyway (it's the macOS constant, and erring
 * 2px high can only over-supply pixels, never render an image upscaled/soft).
 *
 * Both HTML varieties also RESAMPLE the embedded pixels to this width (#56). They
 * inline images as base64 data URIs, so shipping the full render meant far more
 * pixels than are ever shown, plus base64's ~33% overhead — enough that copying a
 * long SOP out of a browser into another system (a Freshservice KB article) chokes.
 */
export const HTML_IMG_MAX_W = 738;

/**
 * The width/height ATTRIBUTES for an inlined export `<img>`, or null when the
 * decoded size is unusable (the caller then emits no attributes rather than a
 * bogus size). Caps at HTML_IMG_MAX_W preserving aspect; an image already narrower
 * keeps its native size — never upscaled.
 *
 * Attributes, not CSS, because every rich-text destination drops the `<style>`
 * block: Word and Google Docs ignore `max-width` on paste and would lay a capture
 * out at its full pixel size, and a KB editor strips it off `<img>` outright.
 * In a browser the CSS still wins for shrinking (`max-width:100%;height:auto`), so
 * the file stays responsive; the attributes only pin the intrinsic size, which
 * also avoids layout shift while the data URI decodes.
 *
 * Mirrors macOS ExportKit/HTMLExport.swift.
 */
/**
 * Pixel density the styled HTML export embeds at: 2x the display width, i.e. a
 * @2x asset. The `width`/`height` attributes still pin it to HTML_IMG_MAX_W, so
 * layout is unchanged — the extra pixels only serve high-DPI screens and a reader
 * zooming in to actually read the UI text in a screenshot.
 *
 * 1x was tried first and is too destructive: a full-desktop 2924px capture
 * downscaled to 738 leaves its dialog text unreadable no matter the codec. WebP is
 * efficient enough to afford 2x — measured on a real 13-step SOP, base64 payload:
 * PNG@738 3.12 MB · WebP@738 233 KB · **WebP@1476 524 KB** · WebP@native 1.15 MB.
 * So 2x costs ~290 KB over 1x and is still 6x smaller than the PNG it replaces.
 */
export const HTML_IMG_EMBED_MAX_W = HTML_IMG_MAX_W * 2;

/** WebP quality for the styled export. 0.85 is the knee of the curve on screenshot
 *  text — 0.8 is visibly smeary, 0.9 costs ~30% more for no visible gain. Mirrors
 *  the quality macOS picked for AVIF. */
export const HTML_IMG_WEBP_QUALITY = 0.85;

/** How an export format embeds its step images. */
export interface EmbedPolicy {
  /** Cap the embedded pixels at this width, or null to embed at full resolution. */
  embedMaxW: number | null;
  /** Container for the embedded bytes. `png` is also the fallback if webp fails. */
  codec: 'png' | 'webp';
}

/**
 * How a given export format should embed its step images (#56).
 *
 * - **`html`** — WebP at 2x the display width. It inlines pixels as base64 in the
 *   document, and that payload is what breaks pasting a long SOP into a Freshservice
 *   KB article; WebP makes it ~13x smaller than the equivalent PNG, which in turn
 *   buys back enough headroom to keep 2x detail.
 * - **`html-plain`** — PNG at 1x. This is the Word/Google-Docs paste target and
 *   **Word cannot read WebP**, so it must stay PNG; and PNG at 2x costs 11.75 MB,
 *   so it stays 1x. (macOS keeps its plain export on PNG for the same reason.)
 * - **`pdf`** — full resolution, and this is the trap: on Windows the PDF is printed
 *   from the very same `buildHtmlDoc` output, so it inherits anything done for the
 *   HTML unless excluded on purpose. `printToPDF` embeds the SOURCE bitmap, so
 *   capping at 738px would put a Letter print near 110 DPI where the full render
 *   gives ~355. macOS draws the same boundary but gets it for free, because its PDF
 *   renders natively rather than through the HTML.
 * - Anything else (`markdown`/`docx`/`pptx`) never routes through the HTML builder
 *   and keeps full resolution for the same print-quality reason.
 */
export function htmlEmbedPolicy(format: string): EmbedPolicy {
  if (format === 'html') {
    return { embedMaxW: HTML_IMG_EMBED_MAX_W, codec: 'webp' };
  }
  if (format === 'html-plain') {
    return { embedMaxW: HTML_IMG_MAX_W, codec: 'png' };
  }
  return { embedMaxW: null, codec: 'png' };
}

export function htmlImageSize(
  width: number,
  height: number,
): { w: number; h: number } | null {
  if (!Number.isFinite(width) || !Number.isFinite(height)) return null;
  if (!(width >= 1) || !(height >= 1)) return null; // 0x0 / negative → undecodable
  const scale = Math.min(1, HTML_IMG_MAX_W / width);
  return {
    w: Math.max(1, Math.round(width * scale)),
    h: Math.max(1, Math.round(height * scale)),
  };
}
