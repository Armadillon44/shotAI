// #86 — a null element in `steps` threw a TypeError, which failed coerceManifest,
// failed openProject, and because the Home listing walks every project through
// the same path, made the project VANISH from the list rather than showing an
// error.
import { describe, it, expect, vi } from 'vitest';

vi.mock('electron', () => ({ shell: { showItemInFolder: () => undefined } }));

import { normalizeSteps } from './project-store';

const good = { id: 's1', order: 0, kind: 'shot', screenshot: 'shots/a.png', trigger: 'click' };

describe('normalizeSteps degrades per element', () => {
  it('does not throw on a null element', () => {
    expect(() => normalizeSteps([good, null])).not.toThrow();
  });

  it.each([[null], [42], ['x'], [[]], [true], [undefined]])(
    'keeps the valid step beside junk %s',
    (junk) => {
      const out = normalizeSteps([good, junk]);
      expect(out).toHaveLength(1);
      expect(out[0].id).toBe('s1');
    },
  );

  it('keeps both neighbours when the junk is in the middle', () => {
    const b = { ...good, id: 's2', order: 1 };
    expect(normalizeSteps([good, null, b]).map((s) => s.id)).toEqual(['s1', 's2']);
  });

  /// An empty object IS a valid step — every field has a default — so dropping it
  /// would be a different bug. macOS keeps it too.
  it('keeps an empty object as a skeletal step', () => {
    expect(normalizeSteps([good, {}])).toHaveLength(2);
  });

  it('still returns [] for a non-array', () => {
    for (const v of [null, undefined, 42, 'x', {}]) expect(normalizeSteps(v)).toEqual([]);
  });

  it('still repairs a malformed annotations field', () => {
    expect(normalizeSteps([{ ...good, annotations: 'nope' }])[0].annotations).toEqual([]);
  });
});
