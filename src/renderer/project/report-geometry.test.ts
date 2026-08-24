import { describe, it, expect } from 'vitest';
import {
  reportFit,
  wrapContentSlack,
  REPORT_BASE_W,
  REPORT_BASE_H,
  WRAP_BORDER,
} from './report-geometry';

// The real column: .rep__bodywrap is max-width 820 with a 1px border and ~1rem of
// side padding, and box-sizing is border-box app-wide, so what the figure actually
// gets is ~786. That gap is what produced the reported horizontal crop.
const REAL_COLUMN = 786;

describe('the no-crop invariant', () => {
  it('never gives the wrap a content box narrower than its image', () => {
    // Every shape that matters: a full desktop, a tall portrait, an exact fit, a
    // tiny capture, and extreme aspect ratios.
    const shapes = [
      { w: 1920, h: 1200 }, { w: 3840, h: 2160 }, { w: 1366, h: 768 },
      { w: 800, h: 1400 }, { w: 786, h: 500 }, { w: 120, h: 90 },
      { w: 5000, h: 100 }, { w: 100, h: 5000 }, { w: 1, h: 1 },
    ];
    for (const avail of [REAL_COLUMN, 820, 500, 300, 120]) {
      for (const s of shapes) {
        const slack = wrapContentSlack(reportFit(s, avail));
        expect(slack.x, `x slack for ${s.w}x${s.h} @ ${avail}`).toBeGreaterThanOrEqual(0);
        expect(slack.y, `y slack for ${s.w}x${s.h} @ ${avail}`).toBeGreaterThanOrEqual(0);
      }
    }
  });

  it('keeps the whole wrap inside the measured column', () => {
    // The original failure was the wrap being CLAMPED by max-width:100% while the
    // image kept its inline width. If the wrap fits, there is nothing to clamp.
    for (const avail of [REAL_COLUMN, 820, 640, 300]) {
      for (const s of [{ w: 1920, h: 1200 }, { w: 900, h: 300 }, { w: 4000, h: 1000 }]) {
        expect(reportFit(s, avail).wrapW).toBeLessThanOrEqual(avail);
      }
    }
  });
});

describe('reportFit', () => {
  it('fits a 1920x1200 desktop to the real column, not to the 820 constant', () => {
    const fit = reportFit({ w: 1920, h: 1200 }, REAL_COLUMN);
    // 786 - 2 border = 784 of usable width.
    expect(fit.baseW).toBe(784);
    // Aspect preserved.
    expect(fit.baseH).toBe(Math.floor(1200 * (784 / 1920)));
    // And the old behavior is gone: it used to be sized to 820 and then clipped.
    expect(fit.baseW).toBeLessThan(REPORT_BASE_W);
  });

  it('is bounded by height when the capture is tall', () => {
    const fit = reportFit({ w: 800, h: 1600 }, REAL_COLUMN);
    expect(fit.baseH).toBeLessThanOrEqual(REPORT_BASE_H - WRAP_BORDER * 2);
  });

  it('never upscales a capture smaller than the column', () => {
    const fit = reportFit({ w: 300, h: 200 }, REAL_COLUMN);
    expect(fit.baseW).toBe(300);
    expect(fit.baseH).toBe(200);
  });

  it('holds the box at the zoom-1 fit while the image grows, so panning works', () => {
    const one = reportFit({ w: 1920, h: 1200 }, REAL_COLUMN, 1);
    const three = reportFit({ w: 1920, h: 1200 }, REAL_COLUMN, 3);
    expect(three.wrapW).toBe(one.wrapW);
    expect(three.wrapH).toBe(one.wrapH);
    expect(three.baseW).toBe(one.baseW); // caller multiplies the IMG by zoom
  });

  it('returns whole pixels, so layout and paint cannot disagree by a subpixel', () => {
    for (const s of [{ w: 1920, h: 1200 }, { w: 1366, h: 768 }, { w: 1777, h: 999 }]) {
      const f = reportFit(s, REAL_COLUMN);
      expect(Number.isInteger(f.baseW)).toBe(true);
      expect(Number.isInteger(f.baseH)).toBe(true);
      expect(Number.isInteger(f.wrapW)).toBe(true);
    }
  });

  it('falls back to the constant before the first measurement, and recovers after', () => {
    const pre = reportFit({ w: 1920, h: 1200 }, null);
    expect(pre.baseW).toBe(REPORT_BASE_W - WRAP_BORDER * 2);
    const post = reportFit({ w: 1920, h: 1200 }, REAL_COLUMN);
    expect(post.baseW).toBeLessThan(pre.baseW);
  });

  it('is inert for a missing or degenerate image', () => {
    for (const d of [null, { w: 0, h: 0 }, { w: -5, h: 10 }]) {
      expect(reportFit(d, REAL_COLUMN)).toEqual({ baseW: 0, baseH: 0, wrapW: 0, wrapH: 0 });
    }
  });
});
