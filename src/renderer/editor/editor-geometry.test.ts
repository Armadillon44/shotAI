import { describe, it, expect } from 'vitest';
import { clampRectToImage, computeCropView } from './editor-geometry';

describe('clampRectToImage', () => {
  it('passes through a fully-contained rect', () => {
    expect(clampRectToImage({ x: 10, y: 10, width: 50, height: 50 }, 100, 100)).toEqual({ x: 10, y: 10, width: 50, height: 50 });
  });
  it('clamps width/height at the right/bottom edges', () => {
    expect(clampRectToImage({ x: 80, y: 80, width: 50, height: 50 }, 100, 100)).toEqual({ x: 80, y: 80, width: 20, height: 20 });
  });
  it('clamps a negative origin to 0 (keeps width up to the far edge)', () => {
    expect(clampRectToImage({ x: -10, y: -10, width: 50, height: 50 }, 100, 100)).toEqual({ x: 0, y: 0, width: 50, height: 50 });
  });
  it('never returns a zero-size rect for a fully-outside input', () => {
    expect(clampRectToImage({ x: 200, y: 200, width: 50, height: 50 }, 100, 100)).toEqual({ x: 100, y: 100, width: 1, height: 1 });
  });
});

describe('computeCropView', () => {
  it('identity at scale 1 on the whole image', () => {
    expect(computeCropView({ x: 0, y: 0, width: 1920, height: 1080 }, 1)).toEqual({ scale: 1, x: 0, y: 0, w: 1920, h: 1080 });
  });
  it('scales down uniformly', () => {
    expect(computeCropView({ x: 0, y: 0, width: 800, height: 600 }, 0.5)).toEqual({ scale: 0.5, x: 0, y: 0, w: 400, h: 300 });
  });
  it('translates a cropped region to the origin and scales up', () => {
    expect(computeCropView({ x: 100, y: 50, width: 400, height: 300 }, 2)).toEqual({ scale: 2, x: -200, y: -100, w: 800, h: 600 });
  });
  it('never produces a zero-size stage', () => {
    expect(computeCropView({ x: 5, y: 5, width: 0.2, height: 0.2 }, 1)).toEqual({ scale: 1, x: -5, y: -5, w: 1, h: 1 });
  });
});
