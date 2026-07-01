import { describe, it, expect } from 'vitest';
import { zoomCropRect } from './export-geometry';

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
