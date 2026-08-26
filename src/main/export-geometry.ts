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
import { clampScale, docWidths } from '../shared/doc-scale';


// --- Word (.docx) image geometry -------------------------------------------
//
// Lives here, not in export-docx.ts, because that module reaches electron through
// ./export and so cannot be unit-tested. These constants MUST match stepCard() in
// export-docx.ts.

/** Letter minus 1in margins each side = 6.5in at 96dpi. Word's text column. */
export const DOCX_PAGE_COL_W = 624;
/** Base image width at scale 1. */
export const DOCX_IMG_BASE_W = 560;
/** stepCard() cell inset, per side, in twips. */
export const DOCX_CELL_INSET_TWIPS = 180;
/** stepCard() border size, per side, in eighths of a point. */
export const DOCX_CARD_BORDER_EIGHTHS = 4;

/** twips -> CSS px at 96dpi: /20 gives points, *96/72 gives px. */
function twipsToPx(t: number): number {
  return (t / 20) * (96 / 72);
}

/**
 * What actually fits INSIDE a Word step card.
 *
 * Clamping to the page column was wrong, and it showed up only at 125%: the image
 * was sized to the full 624 and Word clipped ~26px off its right edge, because the
 * card spends 12px of inset plus a border on each side. Derived rather than
 * re-guessed, so it tracks stepCard() instead of drifting from it.
 */
export const DOCX_CARD_INNER_W = Math.floor(
  DOCX_PAGE_COL_W -
    2 * twipsToPx(DOCX_CELL_INSET_TWIPS) -
    2 * twipsToPx((DOCX_CARD_BORDER_EIGHTHS / 8) * 20),
);

/**
 * Word image width at a given project scale, clamped to the card's inner width.
 *
 * A HARD ceiling: Word has a fixed page, so the document scale (#70) can shrink an
 * image but never grow it past the card, or Word clips it and the setting lies about
 * what it did. At scale 1 the base is already inside the ceiling, so 100% documents
 * are byte-identical to before.
 */
export function docxImgMaxW(scale = 1): number {
  return Math.min(DOCX_CARD_INNER_W, Math.round(DOCX_IMG_BASE_W * clampScale(scale)));
}

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
export const HTML_IMG_MAX_W = docWidths(1).htmlImgMax;

/**
 * The display width at a given project scale (#70). RE-DERIVED, not HTML_IMG_MAX_W
 * times the scale: the 78px of chrome subtracted from the column is fixed, so the
 * two agree only at scale 1, which is exactly what makes the wrong version pass a
 * spot check. doc-scale owns the arithmetic for both the app and the exports.
 */
export function htmlImgMaxW(scale = 1): number {
  return docWidths(scale).htmlImgMax;
}

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
 * The @2x embed width — twice the display width, so the `width`/`height` attributes
 * still lay the image out at HTML_IMG_MAX_W while it carries enough pixels to stay
 * sharp on a high-DPI screen and legible when a reader zooms in.
 *
 * Affordable only because the styled export is AVIF: at ~10 KB/image it fits under
 * the destination's payload ceiling with room to spare, where 2x in any other codec
 * does not (see htmlEmbedPolicy). Worth having — at 1x a full-desktop 2924px
 * capture's dialog text is unreadable no matter the codec.
 */
export const HTML_IMG_EMBED_MAX_W = docWidths(1).htmlImgEmbedMax;

/** The @2x embed width at a given project scale. Always exactly 2x the display
 *  width, or an exported capture silently stops being @2x and its text softens. */
export function htmlImgEmbedMaxW(scale = 1): number {
  return docWidths(scale).htmlImgEmbedMax;
}

/**
 * JPEG quality (0-100, as `nativeImage.toJPEG` takes it). Used as the FALLBACK for
 * the styled export when AVIF is unavailable, and nowhere else.
 */
export const HTML_IMG_JPEG_QUALITY = 85;

/** AVIF quality (0-100) for the styled export. 50 is libavif's default and, measured
 *  at the 2x embed width, keeps UI text clean at ~10 KB/image. */
export const HTML_IMG_AVIF_QUALITY = 50;

/**
 * libavif encoder effort, 0-10 where HIGHER IS FASTER. 7 measured as the clear knee:
 * identical output size to the slower 6 (8 KB on a sample, 168 vs 173 KB across a
 * 13-step SOP) at HALF the time (11.4 s vs 23.3 s for 13 images), and visually
 * indistinguishable from it. 8 and above are much faster still but visibly smear fine
 * UI text while producing LARGER files, which is the worst of both.
 */
export const HTML_IMG_AVIF_SPEED = 7;

/** How an export format embeds its step images. */
export interface EmbedPolicy {
  /** Cap the embedded pixels at this width, or null to embed at full resolution. */
  embedMaxW: number | null;
  /**
   * Container for the embedded bytes. `avif` degrades to `jpeg` and then to the
   * original bytes if the WASM encoder isn't available.
   */
  codec: 'png' | 'jpeg' | 'avif';
}

/**
 * How a given export format should embed its step images (#56).
 *
 * - **`html`** — AVIF at 2x the display width. It inlines pixels as base64, and that
 *   payload is what breaks pasting a long SOP into a Freshservice KB article.
 *
 *   **The constraint is a TOTAL PAYLOAD ceiling in the destination**, not a format
 *   allowlist. Freshservice's editor re-uploads every pasted image and falls over
 *   when handed too much data at once. Measured base64 for one real 13-step SOP:
 *   PNG@1x 3.12 MB, JPEG@2x 1.02 MB, WebP@2x 524 KB and JPEG@1x 414 KB **all fail to
 *   paste**; AVIF@2x is 168 KB and pastes (macOS's ~164 KB AVIF was the known-good
 *   reference). An earlier read of this — that Froala's `imageAllowedTypes` was
 *   rejecting the container — was wrong: JPEG is on that list and still failed.
 *
 *   So AVIF is not a preference, it's the only codec that fits while keeping 2x, and
 *   2x is what makes a full-desktop capture's UI text legible at all. It needs a WASM
 *   encoder (see avif-encode.ts) and degrades to JPEG then to the original bytes.
 *   Losing alpha is safe either way — step renders are fully opaque (verified: no
 *   pixel below alpha 255).
 * - **`html-plain`** — PNG at 1x. This is the Word/Google-Docs paste target; PNG is
 *   the safest thing to hand Word, and PNG at 2x would cost 11.75 MB.
 * - **`pdf`** — full resolution, and this is the trap: on Windows the PDF is printed
 *   from the very same `buildHtmlDoc` output, so it inherits anything done for the
 *   HTML unless excluded on purpose. `printToPDF` embeds the SOURCE bitmap, so
 *   capping at 738px would put a Letter print near 110 DPI where the full render
 *   gives ~355. macOS draws the same boundary but gets it for free, because its PDF
 *   renders natively rather than through the HTML.
 * - Anything else (`markdown`/`docx`/`pptx`) never routes through the HTML builder
 *   and keeps full resolution for the same print-quality reason.
 */
/**
 * `docScale` (#70) scales the EMBED target with the display width, so an exported
 * capture stays exactly @2x at any project scale. Leaving it fixed would make a
 * 65% document embed 2x the OLD width, i.e. ~3x its new display size, inflating a
 * payload that already has a hard ceiling in the destination editor.
 *
 * The pdf branch stays full-resolution regardless: see the trap above.
 */
export function htmlEmbedPolicy(format: string, docScale = 1): EmbedPolicy {
  if (format === 'html') {
    // AVIF at 2x. The constraint here is a TOTAL PAYLOAD ceiling in the destination:
    // Freshservice's editor re-uploads every pasted image and cannot cope with too
    // much data at once. Measured base64 for one real 13-step SOP —
    //   PNG  @1x  3.12 MB   fails to paste
    //   JPEG @2x  1.02 MB   fails
    //   WebP @2x   524 KB   fails
    //   JPEG @1x   414 KB   fails
    //   AVIF @2x   168 KB   <- this, and the macOS app's ~164 KB AVIF works
    // So AVIF is not a preference, it is the only codec that fits under the ceiling
    // while keeping 2x, which is what makes a full-desktop capture's UI text
    // readable at all (at 1x it is an illegible blur in every codec).
    return { embedMaxW: htmlImgEmbedMaxW(docScale), codec: 'avif' };
  }
  if (format === 'html-plain') {
    // 1x for the Word paste target, at the SCALED display width.
    return { embedMaxW: htmlImgMaxW(docScale), codec: 'png' };
  }
  return { embedMaxW: null, codec: 'png' };
}

export function htmlImageSize(
  width: number,
  height: number,
  docScale = 1,
): { w: number; h: number } | null {
  if (!Number.isFinite(width) || !Number.isFinite(height)) return null;
  if (!(width >= 1) || !(height >= 1)) return null; // 0x0 / negative → undecodable
  // Named `fit`, not `scale`: `docScale` is the project scale, and conflating the
  // two would make a document setting silently rescale the image twice.
  const fit = Math.min(1, htmlImgMaxW(docScale) / width);
  return {
    w: Math.max(1, Math.round(width * fit)),
    h: Math.max(1, Math.round(height * fit)),
  };
}
