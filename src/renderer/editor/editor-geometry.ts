// Pure editor geometry — extracted from Editor.tsx so the crop/clamp math is
// unit-testable without Konva/DOM. Both are redaction-adjacent (the crop rect and
// its on-stage transform decide what pixels are shown/baked).
import type { Rect } from '../../shared/project';

/** Clamp a rectangle (image px) to lie fully within the image bounds. */
export function clampRectToImage(r: Rect, w: number, h: number): Rect {
  const x = Math.max(0, Math.min(r.x, w));
  const y = Math.max(0, Math.min(r.y, h));
  return {
    x,
    y,
    width: Math.max(1, Math.min(r.width, w - x)),
    height: Math.max(1, Math.min(r.height, h - y)),
  };
}

export interface CropView {
  scale: number;
  x: number;
  y: number;
  w: number;
  h: number;
}

/**
 * The Konva Stage transform that fits `region` (whole image, or the applied crop)
 * to the canvas at `stageScale` (fit × editing zoom). x/y translate the region's
 * top-left to the origin; w/h are the pixel size of the transformed region.
 */
export function computeCropView(region: Rect, stageScale: number): CropView {
  return {
    scale: stageScale,
    // `|| 0` normalizes negative-zero (when region.x/y is 0) to +0 — cosmetic
    // (Konva treats them identically) but avoids a -0 leaking into logs/tests.
    x: -region.x * stageScale || 0,
    y: -region.y * stageScale || 0,
    w: Math.max(1, Math.round(region.width * stageScale)),
    h: Math.max(1, Math.round(region.height * stageScale)),
  };
}
