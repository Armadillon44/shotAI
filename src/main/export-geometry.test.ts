import { describe, it, expect } from 'vitest';
import {
  HTML_IMG_EMBED_MAX_W,
  HTML_IMG_MAX_W,
  htmlEmbedPolicy,
  htmlImageSize,
  zoomCropRect,
} from './export-geometry';

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
  it('embeds the styled HTML as JPEG at 2x the display width', () => {
    // JPEG, NOT WebP/AVIF: Freshservice's editor re-uploads each pasted image and
    // rejects both, leaving broken images ("No link in upload response"). Its
    // efficiency still affords 2x, so a reader can zoom in and read the UI text —
    // at 1x a full-desktop capture's dialog text is an illegible blur.
    expect(htmlEmbedPolicy('html')).toEqual({
      embedMaxW: HTML_IMG_EMBED_MAX_W,
      codec: 'jpeg',
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
