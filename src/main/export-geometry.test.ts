import { describe, it, expect } from 'vitest';
import {
  HTML_IMG_EMBED_MAX_W,
  HTML_IMG_MAX_W,
  htmlEmbedPolicy,
  htmlImageSize,
  zoomCropRect,
  docxImgMaxW,
  DOCX_CARD_INNER_W,
  DOCX_PAGE_COL_W,
  DOCX_IMG_BASE_W,
  DOCX_CELL_INSET_TWIPS,
  DOCX_CARD_BORDER_EIGHTHS,
} from './export-geometry';
import { SCALE_STEPS } from '../shared/doc-scale';

describe('zoomCropRect', () => {
  it('returns null at zoom 1 (whole image visible)', () => {
    expect(zoomCropRect(800, 600, 1, 0.5, 0.5)).toBeNull();
  });

  it('returns null for zoom < 1 (image shrinks in a smaller box, no crop)', () => {
    expect(zoomCropRect(800, 600, 0.5, 0.5, 0.5)).toBeNull();
  });

  it('returns null for a non-finite / <=0 zoom', () => {
    expect(zoomCropRect(800, 600, NaN, 0.5, 0.5)).toBeNull();
    expect(zoomCropRect(800, 600, 0, 0.5, 0.5)).toBeNull();
  });

  it('crops the centered half at zoom 2, pan centered', () => {
    // zoom 2 shows w/2 x h/2 of the image; centered pan → the middle quarter.
    expect(zoomCropRect(800, 600, 2, 0.5, 0.5)).toEqual({
      x: 200, // 800*(2-1)*0.5/2 = 200
      y: 150, // 600*(2-1)*0.5/2 = 150
      width: 400, // 800/2
      height: 300, // 600/2
    });
  });

  it('pans to the top-left corner at pan 0', () => {
    expect(zoomCropRect(800, 600, 2, 0, 0)).toEqual({ x: 0, y: 0, width: 400, height: 300 });
  });

  it('pans to the bottom-right corner at pan 1 (offset clamped in-bounds)', () => {
    // x = 800*(1)*1/2 = 400 = width - cropW; fully in bounds.
    expect(zoomCropRect(800, 600, 2, 1, 1)).toEqual({ x: 400, y: 300, width: 400, height: 300 });
  });

  it('keeps the crop rectangle inside the image bounds', () => {
    const r = zoomCropRect(801, 601, 3, 1, 1);
    expect(r).not.toBeNull();
    if (r) {
      expect(r.x).toBeGreaterThanOrEqual(0);
      expect(r.y).toBeGreaterThanOrEqual(0);
      expect(r.x + r.width).toBeLessThanOrEqual(801);
      expect(r.y + r.height).toBeLessThanOrEqual(601);
    }
  });

  it('defaults a non-finite pan to centered rather than crashing', () => {
    expect(zoomCropRect(800, 600, 2, NaN, NaN)).toEqual({
      x: 200,
      y: 150,
      width: 400,
      height: 300,
    });
  });

  it('returns null for a degenerate (sub-2px) image', () => {
    expect(zoomCropRect(1, 1, 2, 0.5, 0.5)).toBeNull();
  });
});

describe('htmlImageSize', () => {
  it('caps a wide capture at the macOS-matched 738px, preserving aspect', () => {
    // 2560x1440 → scale 738/2560; height rounds to 415.
    expect(htmlImageSize(2560, 1440)).toEqual({ w: 738, h: 415 });
  });

  it('leaves a capture already narrower than the cap at its native size', () => {
    expect(htmlImageSize(400, 300)).toEqual({ w: 400, h: 300 });
    expect(htmlImageSize(HTML_IMG_MAX_W, 450)).toEqual({ w: 738, h: 450 });
  });

  it('never rounds a very wide/short image away to zero height', () => {
    // 3000x1 → scale 0.246; height would round to 0 without the floor.
    expect(htmlImageSize(3000, 1)).toEqual({ w: 738, h: 1 });
  });

  it('returns null for an undecodable size so the caller omits the attributes', () => {
    expect(htmlImageSize(0, 0)).toBeNull();
    expect(htmlImageSize(NaN, 100)).toBeNull();
    expect(htmlImageSize(100, NaN)).toBeNull();
    expect(htmlImageSize(-800, 600)).toBeNull();
    expect(htmlImageSize(Infinity, 600)).toBeNull();
  });
});

describe('htmlEmbedPolicy', () => {
  // The scope boundary from #56, and the trap it guards: on Windows the PDF is
  // printed from the SAME buildHtmlDoc output as the .html export, so it inherits
  // anything done there unless excluded on purpose. printToPDF embeds the source
  // bitmap, so capping at 738px would put a Letter print near 110 DPI where the full
  // render gives ~355. macOS asserts the same boundary
  // (testPdfAndMarkdownKeepFullResolution).
  it('embeds the styled HTML as AVIF at 2x, the only codec under the payload ceiling', () => {
    // Freshservice's editor re-uploads every pasted image and cannot cope with too
    // much data at once. Measured base64 for one real 13-step SOP: PNG@1x 3.12 MB,
    // JPEG@2x 1.02 MB, WebP@2x 524 KB, JPEG@1x 414 KB all FAIL to paste; AVIF@2x is
    // 168 KB and the macOS app's ~164 KB AVIF works. AVIF is the only codec that
    // fits while keeping 2x, which is what makes UI text readable at all.
    expect(htmlEmbedPolicy('html')).toEqual({
      embedMaxW: HTML_IMG_EMBED_MAX_W,
      codec: 'avif',
    });
    expect(HTML_IMG_EMBED_MAX_W).toBe(HTML_IMG_MAX_W * 2);
  });

  it('keeps the Word-paste variety on PNG at 1x, PNG is the safest thing to hand Word', () => {
    expect(htmlEmbedPolicy('html-plain')).toEqual({
      embedMaxW: HTML_IMG_MAX_W,
      codec: 'png',
    });
  });

  it('leaves the PDF at full resolution, in PNG, for print', () => {
    expect(htmlEmbedPolicy('pdf')).toEqual({ embedMaxW: null, codec: 'png' });
  });

  it('leaves the formats that never route through buildHtmlDoc alone', () => {
    for (const f of ['markdown', 'docx', 'pptx']) {
      expect(htmlEmbedPolicy(f), `${f} must not resample or transcode`).toEqual({
        embedMaxW: null,
        codec: 'png',
      });
    }
  });

  it('defaults an unknown format to full-resolution PNG (never transcodes blindly)', () => {
    expect(htmlEmbedPolicy('')).toEqual({ embedMaxW: null, codec: 'png' });
    expect(htmlEmbedPolicy('someFutureFormat')).toEqual({ embedMaxW: null, codec: 'png' });
  });
});

describe('Word image ceiling (#70) — the 125% clipping bug', () => {
  it('never exceeds what fits INSIDE a step card', () => {
    // The bug, reported from a real 125% export: images were clipped on the right.
    // The ceiling was the PAGE column (624), but stepCard() spends 12px of cell inset
    // plus a border on each side, so ~26px of the image had nowhere to go. Word does
    // not complain, it just cuts the picture off.
    for (const s of SCALE_STEPS) {
      expect(docxImgMaxW(s), `scale ${s}`).toBeLessThanOrEqual(DOCX_CARD_INNER_W);
    }
    expect(DOCX_CARD_INNER_W).toBeLessThan(DOCX_PAGE_COL_W);
  });

  it('derives the ceiling from the card, so it cannot drift from stepCard()', () => {
    // 624 - 2*12 (insets) - 2*0.67 (borders) = 598.67 -> 598.
    const px = (t: number) => (t / 20) * (96 / 72);
    expect(DOCX_CARD_INNER_W).toBe(
      Math.floor(
        DOCX_PAGE_COL_W -
          2 * px(DOCX_CELL_INSET_TWIPS) -
          2 * px((DOCX_CARD_BORDER_EIGHTHS / 8) * 20),
      ),
    );
  });

  it('leaves a 100% document byte-identical', () => {
    // The base width is already inside the ceiling, so nothing changes at 100% and
    // this fix cannot alter an existing export.
    expect(docxImgMaxW(1)).toBe(DOCX_IMG_BASE_W);
    expect(docxImgMaxW()).toBe(DOCX_IMG_BASE_W);
  });

  it('shrinks below 100% and clamps above it', () => {
    expect(docxImgMaxW(0.65)).toBe(Math.round(DOCX_IMG_BASE_W * 0.65));
    expect(docxImgMaxW(0.65)).toBeLessThan(DOCX_IMG_BASE_W);
    // Above 100% Word cannot actually grow, so the ceiling holds. That is the honest
    // behavior for a fixed page, not a missing feature.
    expect(docxImgMaxW(1.25)).toBe(DOCX_CARD_INNER_W);
    expect(docxImgMaxW(1.2)).toBe(DOCX_CARD_INNER_W);
  });

  it('sanitizes a bad scale', () => {
    for (const bad of [Number.NaN, 99, -1]) {
      const w = docxImgMaxW(bad as number);
      expect(Number.isInteger(w)).toBe(true);
      expect(w).toBeGreaterThan(0);
      expect(w).toBeLessThanOrEqual(DOCX_CARD_INNER_W);
    }
  });
});
