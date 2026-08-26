import { describe, it, expect } from 'vitest';
import {
  SCALE_MIN, SCALE_MAX, SCALE_DEFAULT, SCALE_STEPS,
  clampScale, isLegalScale, docWidths, detailWindowWidth,
  HTML_COL_BASE, STEP_CHROME, REP_FRAME_BASE, REPORT_COL_BASE, DETAIL_WINDOW_BASE,
} from './doc-scale';

describe('the scale range', () => {
  it('is 65% to 125% in 5% detents, 13 positions', () => {
    expect(SCALE_STEPS.length).toBe(13);
    expect(SCALE_STEPS[0]).toBe(SCALE_MIN);
    expect(SCALE_STEPS[SCALE_STEPS.length - 1]).toBe(SCALE_MAX);
    expect(SCALE_STEPS).toContain(SCALE_DEFAULT);
  });

  it('has no floating-point noise in any detent', () => {
    // 0.7000000000000001 in a manifest would fail an equality check on the macOS
    // side and render one project at two widths.
    for (const s of SCALE_STEPS) {
      expect(Math.round(s * 100) / 100).toBe(s);
      expect(String(s).length).toBeLessThanOrEqual(4);
    }
  });
});

describe('clampScale', () => {
  it('defaults anything unusable, so a pre-#70 project is untouched', () => {
    for (const bad of [undefined, null, 'big', NaN, Infinity, {}, []]) {
      expect(clampScale(bad)).toBe(SCALE_DEFAULT);
    }
  });

  it('clamps out-of-range values to the ends rather than defaulting them', () => {
    // A 3.0 from a future build means "as large as possible", not "normal".
    expect(clampScale(3)).toBe(SCALE_MAX);
    expect(clampScale(0.1)).toBe(SCALE_MIN);
    expect(clampScale(-5)).toBe(SCALE_MIN);
  });

  it('snaps between detents to the nearest legal position', () => {
    expect(clampScale(0.83)).toBe(0.85);
    expect(clampScale(0.82)).toBe(0.8);
    expect(clampScale(1.13)).toBe(1.15);
    expect(clampScale(1)).toBe(1);
  });

  it('resolves an exact midpoint UPWARD, by the documented integer-percent rule', () => {
    // Pinned for macOS parity. The obvious float formulation, (v - 0.65) / 0.05,
    // puts 0.825 at 3.4999999999999996 and rounds it DOWN to 0.80. Snapping in
    // integer percent instead makes the rule exactly reproducible on any platform:
    //   pct = round(v * 100); clamp to [65,125]; pct = round(pct / 5) * 5; / 100
    expect(clampScale(0.825)).toBe(0.85);
    expect(clampScale(0.775)).toBe(0.8);
    expect(clampScale(1.125)).toBe(1.15);
    // and just either side of a midpoint
    expect(clampScale(0.824)).toBe(0.8);
    expect(clampScale(0.826)).toBe(0.85);
  });

  it('matches the NORMATIVE table exactly, including the float-sensitive midpoints', () => {
    // This table IS the cross-platform contract (#70 / macOS #83), not an
    // illustration. Exact midpoints are float-sensitive and no formulation is
    // naturally tie-safe: 0.825 * 100 is 82.5 (rounds up, -> 0.85) while 1.025 * 100
    // is 102.49999999999999 (rounds down, -> 1.00). So the ALGORITHM is normative,
    // and any reimplementation must run the same steps on the same double rather
    // than apply its own idea of how a tie should break:
    //   1. not a finite number  -> 1.0
    //   2. pct = round(v * 100)
    //   3. pct = clamp(pct, 65, 125)
    //   4. pct = round(pct / 5) * 5
    //   5. return pct / 100
    const table: Array<[number, number]> = [
      [0.65, 0.65], [1, 1], [1.25, 1.25],
      [0.824, 0.8], [0.825, 0.85], [0.826, 0.85],
      [1.024, 1], [1.025, 1], [1.026, 1.05],
      [0.775, 0.8], [1.125, 1.15],
      [0.6, 0.65], [1.3, 1.25], [42, 1.25], [-42, 0.65], [0, 0.65],
    ];
    for (const [input, expected] of table) {
      expect(clampScale(input), `clampScale(${input})`).toBe(expected);
    }
  });
  it('only ever returns a legal detent, for any input', () => {
    const inputs = [0.6, 0.651, 0.9999, 1.0001, 1.249, 1.3, 42, -42, 0];
    for (const v of inputs) expect(isLegalScale(clampScale(v))).toBe(true);
  });
  it('is idempotent, so a read-write-read round trip cannot drift', () => {
    for (const s of [0.65, 0.83, 1, 1.13, 1.25, 2]) {
      expect(clampScale(clampScale(s))).toBe(clampScale(s));
    }
    for (const s of SCALE_STEPS) expect(isLegalScale(clampScale(s))).toBe(true);
  });
});

describe('docWidths', () => {
  it('reproduces today exactly at scale 1, so nothing moves for existing projects', () => {
    const w = docWidths(1);
    expect(w.repFrame).toBe(REP_FRAME_BASE); // 880
    expect(w.reportCol).toBe(REPORT_COL_BASE); // 820
    expect(w.htmlCol).toBe(HTML_COL_BASE); // 816
    expect(w.htmlImgMax).toBe(738); // the shipped constant
    expect(w.htmlImgEmbedMax).toBe(1476); // @2x
  });

  it('RE-DERIVES the image width instead of multiplying it', () => {
    // The trap: 738 * s looks right at s = 1 and is wrong everywhere else, because
    // the 78px of chrome subtracted from the column does not scale.
    for (const s of SCALE_STEPS) {
      const w = docWidths(s);
      expect(w.htmlImgMax).toBe(Math.max(120, Math.round(HTML_COL_BASE * s) - STEP_CHROME));
      if (s !== 1) expect(w.htmlImgMax).not.toBe(Math.round(738 * s));
    }
  });

  it('keeps the image inside its card at every scale', () => {
    // The invariant that matters: column minus chrome must still hold the image.
    for (const s of SCALE_STEPS) {
      const w = docWidths(s);
      expect(w.htmlCol - STEP_CHROME).toBeGreaterThanOrEqual(w.htmlImgMax - 0.5);
      expect(w.htmlImgMax).toBeGreaterThan(0);
    }
  });

  it('is monotonic: a bigger scale is never a smaller document', () => {
    for (let i = 1; i < SCALE_STEPS.length; i++) {
      const a = docWidths(SCALE_STEPS[i - 1]);
      const b = docWidths(SCALE_STEPS[i]);
      expect(b.repFrame).toBeGreaterThan(a.repFrame);
      expect(b.htmlCol).toBeGreaterThan(a.htmlCol);
      expect(b.htmlImgMax).toBeGreaterThan(a.htmlImgMax);
    }
  });

  it('returns whole pixels everywhere', () => {
    for (const s of SCALE_STEPS) {
      for (const v of Object.values(docWidths(s))) expect(Number.isInteger(v)).toBe(true);
    }
  });

  it('keeps the embed at exactly @2x the display width', () => {
    // If these drift apart the exported image is silently no longer @2x and text
    // in a capture goes soft without anything failing.
    for (const s of SCALE_STEPS) {
      const w = docWidths(s);
      expect(w.htmlImgEmbedMax).toBe(w.htmlImgMax * 2);
    }
  });

  it('sanitizes its own input, so a bad manifest cannot produce a bad layout', () => {
    expect(docWidths(99)).toEqual(docWidths(SCALE_MAX));
    expect(docWidths(Number.NaN)).toEqual(docWidths(SCALE_DEFAULT));
  });
});

describe('detailWindowWidth', () => {
  it('grows the window so a scaled-up column has room', () => {
    expect(detailWindowWidth(1, 1920)).toBe(DETAIL_WINDOW_BASE);
    expect(detailWindowWidth(1.25, 1920)).toBe(docWidths(1.25).repFrame + 130);
    expect(detailWindowWidth(1.25, 1920)).toBeGreaterThan(DETAIL_WINDOW_BASE);
  });

  it('never exceeds the usable display width', () => {
    // A window wider than the screen is worse than a narrow column: the report
    // measures the space it really gets, so clamping degrades gracefully.
    expect(detailWindowWidth(1.25, 1100)).toBeLessThanOrEqual(1100);
    expect(detailWindowWidth(1.25, 1024)).toBeLessThanOrEqual(1024);
  });

  it('never shrinks below the scale-1 window when scaling DOWN', () => {
    // Small scale narrows the COLUMN, not the window; shrinking the window would
    // cramp the home list on the way back out.
    for (const s of [0.65, 0.8, 0.95]) {
      expect(detailWindowWidth(s, 1920)).toBe(DETAIL_WINDOW_BASE);
    }
  });

  it('survives a nonsense work area rather than returning NaN', () => {
    for (const bad of [0, -1, Number.NaN, Infinity]) {
      const w = detailWindowWidth(1.25, bad as number);
      expect(Number.isInteger(w)).toBe(true);
      expect(w).toBeGreaterThanOrEqual(DETAIL_WINDOW_BASE);
    }
  });
});
