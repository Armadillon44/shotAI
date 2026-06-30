import { describe, it, expect } from 'vitest';
import { applyPatchAndInvalidate } from './step-render';
import type { ProjectStep, StepPatch } from '../shared/project';

// A shot step that already has a baked render (flattened + markerBaked) — the
// state in which a stale render could leak if invalidation is missed.
function step(): ProjectStep {
  return {
    id: 's1',
    order: 1,
    kind: 'shot',
    screenshot: 'shots/s.png',
    annotations: [],
    flattened: 'export/.render/s1.png',
    markerBaked: true,
  } as unknown as ProjectStep;
}
const patch = (p: object) => p as unknown as StepPatch;

describe('applyPatchAndInvalidate (S3 redaction-freshness backstop)', () => {
  it('drops a stale render when a blur is added WITHOUT a fresh PNG', () => {
    const s = step();
    applyPatchAndInvalidate(s, patch({ annotations: [{ type: 'blur' }] }), false);
    expect(s.flattened).toBeNull();
    expect(s.markerBaked).toBe(false);
  });

  it('drops a stale render when the crop changes WITHOUT a fresh PNG', () => {
    const s = step();
    applyPatchAndInvalidate(s, patch({ crop: { x: 0, y: 0, width: 1, height: 1 } }), false);
    expect(s.flattened).toBeNull();
  });

  it('KEEPS the render when a fresh PNG is co-written (caller sets the new one)', () => {
    const s = step();
    applyPatchAndInvalidate(s, patch({ annotations: [{ type: 'blur' }] }), true);
    expect(s.flattened).toBe('export/.render/s1.png');
  });

  it('does NOT invalidate on a display-only patch (e.g. reportZoom)', () => {
    const s = step();
    applyPatchAndInvalidate(s, patch({ reportZoom: 1.5 }), false);
    expect(s.flattened).toBe('export/.render/s1.png');
    expect(s.markerBaked).toBe(true);
  });
});
