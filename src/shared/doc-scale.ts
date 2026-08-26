// Per-project document scale (#70): one slider that makes the captured steps, and
// the whole document column around them, narrower or wider.
//
// Electron-free and DOM-free on purpose: main derives export widths from it and the
// renderer derives the report layout from it, so the SAME arithmetic serves both.
// Before this, `export-css.ts` and `export-geometry.ts` each carried the column
// width and told the reader to "keep the two in sync". A scale would have turned
// that comment into two drifting derivations, so the derivation moved here instead
// and the sync is a test rather than a hope.
//
// WHAT SCALES: column widths and the screenshots inside them.
// WHAT DOES NOT: step-number badges, gaps, card padding, and every font size.
// So at 65% you get a narrower document with normal, readable text, not a document
// zoomed out to 10px type. That is a deliberate product decision, and it is why the
// image width has to be RE-DERIVED per scale rather than multiplied: the chrome
// subtracted from the column is a constant, so `imgWidth(s) != imgWidth(1) * s`.

/** Range and granularity of the slider. */
export const SCALE_MIN = 0.65;
export const SCALE_MAX = 1.25;
/** 5% detents: predictable, reproducible, and keeps derived widths near-integral. */
export const SCALE_STEP = 0.05;
export const SCALE_DEFAULT = 1;

/** Every legal slider position, smallest first. 13 of them. */
export const SCALE_STEPS: readonly number[] = (() => {
  const out: number[] = [];
  const n = Math.round((SCALE_MAX - SCALE_MIN) / SCALE_STEP);
  for (let i = 0; i <= n; i++) {
    // Round each step to avoid 0.7000000000000001 landing in a manifest.
    out.push(Math.round((SCALE_MIN + i * SCALE_STEP) * 100) / 100);
  }
  return out;
})();

/**
 * Coerce any value to a legal scale: snapped to the nearest detent and clamped.
 *
 * Applied on BOTH read and write, the same discipline `reportZoom` uses, so no
 * hand-edited manifest, no older build, and no future macOS divergence can render a
 * project at a width this app never intended. A missing or unusable value means
 * SCALE_DEFAULT, which is why every pre-#70 project is untouched.
 */
export function clampScale(value: unknown): number {
  const n = typeof value === 'number' ? value : Number.NaN;
  if (!Number.isFinite(n)) return SCALE_DEFAULT;
  // Snap in INTEGER PERCENT, not in float steps. `(v - 0.65) / 0.05` puts an exact
  // midpoint like 0.825 at 3.4999999999999996, so it rounds DOWN to 0.80 while a
  // reader (and a Swift reimplementation) would expect 0.85. Integer percent makes
  // the rule exactly reproducible on any platform, which matters because macOS reads
  // and writes this same field.
  const pct = Math.round(n * 100);
  const clamped = Math.min(125, Math.max(65, pct));
  const snapped = Math.round(clamped / 5) * 5;
  return snapped / 100;
}

/** True when the value is exactly a legal detent (used by tests and validators). */
export function isLegalScale(value: unknown): boolean {
  return typeof value === 'number' && SCALE_STEPS.includes(value);
}

// ---------------------------------------------------------------------------
// Base widths at scale 1. Changing any of these changes both the app and every
// export, which is the point of them living together.
// ---------------------------------------------------------------------------

/** `.rep` max-width. Matches the macOS ReportView content frame. */
export const REP_FRAME_BASE = 880;
/** `.rep__bodywrap` max-width, the in-app step card. */
export const REPORT_COL_BASE = 820;
/** The export document's CONTENT column: `.doc` is this + DOC_PAD*2. */
export const HTML_COL_BASE = 816;
/** `.doc` horizontal padding, per side. Fixed: chrome does not scale. */
export const HTML_DOC_PAD = 32;

/** Fixed chrome subtracted from the column to get the image width. */
export const STEP_BADGE_W = 30;
export const STEP_GAP = 16;
/** `.step__main` horizontal padding, both sides combined. */
export const STEP_CARD_PAD = 32;
/** Everything between the column edge and the image, both sides. */
export const STEP_CHROME = STEP_BADGE_W + STEP_GAP + STEP_CARD_PAD; // 78

/** Window width in detail view at scale 1, and the chrome around `.rep`. */
export const DETAIL_WINDOW_BASE = 1010;
const WINDOW_CHROME = DETAIL_WINDOW_BASE - REP_FRAME_BASE; // 130

export interface DocWidths {
  /** `.rep` frame. */
  repFrame: number;
  /** `.rep__bodywrap` step card. */
  reportCol: number;
  /** Export document content column (`HTML_COL_W`). */
  htmlCol: number;
  /** `.doc` outer width, i.e. htmlCol + padding. */
  htmlDoc: number;
  /** Display width of an exported screenshot. */
  htmlImgMax: number;
  /** Pixels actually embedded, @2x the display width. */
  htmlImgEmbedMax: number;
}

/**
 * Every derived width at a given scale.
 *
 * `htmlImgMax` is the one to be careful with. It is
 * `round(HTML_COL_BASE * scale) - STEP_CHROME`, NOT `738 * scale`, because the
 * chrome is fixed. At scale 1 the two happen to agree (816 - 78 = 738), which is
 * exactly what makes the wrong version look right in a spot check.
 */
export function docWidths(scale: number): DocWidths {
  const s = clampScale(scale);
  const htmlCol = Math.round(HTML_COL_BASE * s);
  // Never let the chrome eat the whole column: at the floor there is still an image.
  const htmlImgMax = Math.max(120, htmlCol - STEP_CHROME);
  return {
    repFrame: Math.round(REP_FRAME_BASE * s),
    reportCol: Math.round(REPORT_COL_BASE * s),
    htmlCol,
    htmlDoc: htmlCol + HTML_DOC_PAD * 2,
    htmlImgMax,
    htmlImgEmbedMax: htmlImgMax * 2,
  };
}

/**
 * Width the project window should open at in detail view, so a scaled-up column
 * actually has room. Clamped to the usable display width, since a window wider
 * than the screen is worse than a slightly narrow column: the report measures the
 * space it really gets, so a clamp degrades gracefully instead of clipping.
 *
 * Only applied on entering detail view, matching today's behavior, so a window the
 * user resized by hand is not fought over.
 */
export function detailWindowWidth(scale: number, workAreaWidth: number): number {
  const want = docWidths(scale).repFrame + WINDOW_CHROME;
  const usable = Number.isFinite(workAreaWidth) && workAreaWidth > 0
    ? Math.floor(workAreaWidth)
    : want;
  // Never shrink below the scale-1 window: a small scale should narrow the COLUMN,
  // not the window, or the home list gets cramped on the way back out.
  return Math.max(DETAIL_WINDOW_BASE, Math.min(want, usable));
}
