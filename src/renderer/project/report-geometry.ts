// Report screenshot fit geometry, kept pure and electron/DOM-free so the
// no-crop invariant is unit-testable. Same reasoning as export-geometry.ts.
//
// THE BUG THIS EXISTS TO PREVENT (observed: "screenshots are getting cropped
// slightly horizontally in the report view"):
//
// The fit was computed against REPORT_BASE_W (820), but 820 is the width of the
// step CARD, not of the space inside it. `* { box-sizing: border-box }` is global,
// and .rep__bodywrap spends 820 on its own 1px borders and ~1rem side padding, so
// the actual content width is nearer 786. .rep__imgwrap carries max-width:100%, so
// the WRAP was silently clamped to 786 while the IMG kept its inline width of 820,
// and the wrap's overflow:hidden quietly ate the difference.
//
// Horizontal only, which is the clue that named the cause: nothing clamps height,
// so the vertical axis was always fine.
//
// Two rules follow, and the tests assert both:
//   1. fit against the MEASURED content width, never a constant that predates the
//      card's padding.
//   2. the wrap is sized to the image PLUS its own border, so a border-box wrap
//      never has a content box smaller than the image it contains.

import { REPORT_COL_BASE, clampScale } from '../../shared/doc-scale';

/** Max image box at zoom 1, scale 1. Width matches macOS ReportPresentation.baseWidth. */
export const REPORT_BASE_W = REPORT_COL_BASE;
export const REPORT_BASE_H = 600;

/** .rep__imgwrap border width, per side. Must match project.css. */
export const WRAP_BORDER = 1;

export interface ImgDims {
  w: number;
  h: number;
}

export interface ReportFit {
  /** Displayed image size at zoom 1. */
  baseW: number;
  baseH: number;
  /** Outer size for .rep__imgwrap, border included (border-box). */
  wrapW: number;
  wrapH: number;
}

/**
 * Fit a screenshot into the report column.
 *
 * `availW` is the MEASURED content width available to the figure, or null before
 * the first measurement. Null falls back to REPORT_BASE_W, which can over-size by
 * the card's padding for one frame; that is a brief over-size on first paint
 * rather than a persistent crop, and the measured value takes over immediately.
 *
 * Scale is capped at 1: a capture smaller than the column is never blown up.
 */
export function reportFit(
  dims: ImgDims | null,
  availW: number | null,
  zoom = 1,
  scale = 1,
): ReportFit {
  if (!dims || dims.w <= 0 || dims.h <= 0) {
    return { baseW: 0, baseH: 0, wrapW: 0, wrapH: 0 };
  }
  // Subtract the wrap's own border from the budget. Without this the image is
  // sized to the full budget and the border pushes it past the clamp, which is
  // half of how the original crop happened.
  // Both caps scale with the project (#70). Width is normally decided by availW
  // anyway, since the CSS column is already scaled and this measures it; the
  // scaled cap matters when the window is wide enough that the column, not the
  // window, is the limit. HEIGHT has no measured equivalent, so without scaling it
  // a scaled-up capture would grow sideways and then hit an unscaled 600px ceiling.
  const s = clampScale(scale);
  const capW = Math.round(REPORT_BASE_W * s);
  const capH = Math.round(REPORT_BASE_H * s);
  const budgetW = Math.max(1, Math.min(capW, availW ?? capW) - WRAP_BORDER * 2);
  const budgetH = Math.max(1, capH - WRAP_BORDER * 2);

  // Named fitScale, not scale: `scale` is the PROJECT scale parameter. Conflating
  // the two is how a document scale silently becomes an image scale.
  const fitScale = Math.min(budgetW / dims.w, budgetH / dims.h, 1);
  // Floor the image box to whole pixels. A fractional width invites a
  // sub-pixel rounding difference between the wrap's layout and the image's
  // paint, which is a 1px clip of exactly the kind being fixed here.
  const baseW = Math.floor(dims.w * fitScale);
  const baseH = Math.floor(dims.h * fitScale);

  // The box stays fixed at the zoom-1 fit and the image overflows for zoom > 1 so
  // it pans in both axes instead of the box growing taller.
  const boxScale = Math.min(zoom, 1);
  return {
    baseW,
    baseH,
    wrapW: Math.round(baseW * boxScale) + WRAP_BORDER * 2,
    wrapH: Math.round(baseH * boxScale) + WRAP_BORDER * 2,
  };
}

/**
 * The invariant, expressed once so a test can assert it directly: the wrap's
 * CONTENT box (outer minus border, because box-sizing is border-box) must be at
 * least as wide as the image drawn inside it. Any negative slack is a crop.
 */
export function wrapContentSlack(fit: ReportFit): { x: number; y: number } {
  return {
    x: fit.wrapW - WRAP_BORDER * 2 - fit.baseW,
    y: fit.wrapH - WRAP_BORDER * 2 - fit.baseH,
  };
}
