import { describe, it, expect } from 'vitest';
import { unionRect, clickBox, captureModeFor, cropRect } from './capture-geometry';

describe('unionRect', () => {
  it('bounds both rects', () => {
    expect(unionRect({ x: 0, y: 0, width: 10, height: 10 }, { x: 5, y: 5, width: 10, height: 10 })).toEqual({
      x: 0,
      y: 0,
      width: 15,
      height: 15,
    });
  });
  it('handles a contained rect (union = the outer)', () => {
    expect(unionRect({ x: 0, y: 0, width: 100, height: 100 }, { x: 40, y: 40, width: 10, height: 10 })).toEqual({
      x: 0,
      y: 0,
      width: 100,
      height: 100,
    });
  });
});

describe('clickBox', () => {
  it('is a 1240px box centered on the point at scale 1', () => {
    expect(clickBox({ x: 800, y: 600 }, 1)).toEqual({ x: 180, y: -20, width: 1240, height: 1240 });
  });
  it('scales the half-size by the monitor scale factor', () => {
    expect(clickBox({ x: 1000, y: 1000 }, 1.5)).toEqual({ x: 70, y: 70, width: 1860, height: 1860 });
  });
  it('treats a 0/NaN scale as 1', () => {
    expect(clickBox({ x: 620, y: 620 }, 0)).toEqual({ x: 0, y: 0, width: 1240, height: 1240 });
  });
});

describe('captureModeFor', () => {
  it('fullscreen when focus is unknown', () => {
    expect(captureModeFor(null)).toBe('fullscreen');
    expect(captureModeFor(undefined)).toBe('fullscreen');
  });
  it('fullscreen on the desktop (Explorer / Program Manager)', () => {
    expect(captureModeFor({ owner: { name: 'Windows Explorer' }, title: 'Program Manager' })).toBe('fullscreen');
  });
  it('region for the taskbar / tray (Explorer, empty title)', () => {
    expect(captureModeFor({ owner: { name: 'Windows Explorer' }, title: '' })).toBe('region');
    expect(captureModeFor({ owner: { name: 'Windows Explorer' }, title: '   ' })).toBe('region');
  });
  it('region for shell hosts (Start / Search)', () => {
    expect(captureModeFor({ owner: { name: 'SearchHost' }, title: 'Search' })).toBe('region');
    expect(captureModeFor({ owner: { name: 'StartMenuExperienceHost' }, title: 'Start' })).toBe('region');
  });
  it('window for a normal app', () => {
    expect(captureModeFor({ owner: { name: 'Notepad' }, title: 'Untitled' })).toBe('window');
    expect(captureModeFor({ owner: { name: 'Windows Explorer' }, title: 'Documents' })).toBe('window');
  });
});

describe('cropRect', () => {
  const mon = { x: 0, y: 0, width: 1920, height: 1080 };
  it('passes through a fully-contained region', () => {
    expect(cropRect(mon, { x: 100, y: 100, width: 200, height: 150 })).toEqual({ x: 100, y: 100, width: 200, height: 150 });
  });
  it('clamps a region overflowing the right/bottom edges', () => {
    expect(cropRect(mon, { x: 1800, y: 1000, width: 400, height: 400 })).toEqual({ x: 1800, y: 1000, width: 120, height: 80 });
  });
  it('clamps to a secondary monitor origin (region starts left of the monitor)', () => {
    expect(cropRect({ x: 1920, y: 0, width: 1920, height: 1080 }, { x: 1900, y: 50, width: 100, height: 100 })).toEqual({
      x: 0,
      y: 50,
      width: 80,
      height: 100,
    });
  });
  it('never returns a zero-size crop', () => {
    const r = cropRect(mon, { x: 5000, y: 5000, width: 10, height: 10 });
    expect(r.width).toBeGreaterThanOrEqual(1);
    expect(r.height).toBeGreaterThanOrEqual(1);
  });
});
