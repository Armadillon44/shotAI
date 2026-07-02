import { describe, it, expect } from 'vitest';
import path from 'node:path';
import { resolveSendableRender } from './render-gate';
import type { ProjectStep } from '../shared/project';

const DIR = process.platform === 'win32' ? 'C:\\proj\\p' : '/proj/p';

// Build a shot step from just the fields the gate reads.
function step(p: {
  annotations?: { type: string }[];
  flattened?: string | null;
  crop?: unknown;
  screenshot?: string;
}): ProjectStep {
  return {
    id: 's1',
    order: 1,
    kind: 'shot',
    screenshot: 'shots/s.png',
    annotations: [],
    ...p,
  } as unknown as ProjectStep;
}

describe('resolveSendableRender (fail-closed redaction gate)', () => {
  it('REFUSES a blur with no baked render (send)', () => {
    expect(() =>
      resolveSendableRender(DIR, step({ annotations: [{ type: 'blur' }], flattened: null }), 'Step 1', 'send'),
    ).toThrow(/redaction or crop/);
  });

  it('REFUSES a crop with no baked render (export)', () => {
    expect(() =>
      resolveSendableRender(DIR, step({ crop: { x: 0, y: 0, width: 1, height: 1 }, flattened: null }), 'Step 1', 'export'),
    ).toThrow(/redaction or crop/);
  });

  it('allows a blur WITH a baked render → reads the flattened path', () => {
    const r = resolveSendableRender(
      DIR,
      step({ annotations: [{ type: 'blur' }], flattened: 'export/.render/s1.png' }),
      'Step 1',
      'send',
    );
    expect(r.abs).toBe(path.resolve(DIR, 'export/.render/s1.png'));
    expect(r.mediaType).toBe('image/png');
  });

  it('allows a plain shot (no blur/crop) → reads the raw screenshot', () => {
    const r = resolveSendableRender(DIR, step({ screenshot: 'shots/s.jpg', flattened: null }), 'Step 1', 'send');
    expect(r.abs).toBe(path.resolve(DIR, 'shots/s.jpg'));
    expect(r.mediaType).toBe('image/jpeg');
  });

  it('throws if the path escapes the project folder', () => {
    expect(() =>
      resolveSendableRender(DIR, step({ screenshot: '../../evil.png', flattened: null }), 'Step 1', 'send'),
    ).toThrow(/no readable screenshot/);
  });
});
