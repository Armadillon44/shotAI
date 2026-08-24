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

describe('captionEditedByUser (#62 gap 3)', () => {
  it('flags the step when a caption arrives in a patch', () => {
    // A caption only reaches here from the report's inline editor. The SOP apply
    // path writes captions through ProjectStore.mutate and never comes this way.
    const s = step();
    applyPatchAndInvalidate(s, patch({ caption: 'Click Save' }), false);
    expect(s.captionEditedByUser).toBe(true);
    expect(s.caption).toBe('Click Save');
  });

  it('does NOT flag the step for an annotation save', () => {
    // The false positive that would matter: flagging every step on an editor save
    // would defeat the pre-AI-original rule entirely, since every step would then
    // look hand-edited.
    const s = step();
    applyPatchAndInvalidate(
      s,
      patch({ annotations: [{ type: 'blur' }], crop: null, markerBaked: true }),
      true,
    );
    expect(s.captionEditedByUser).toBeUndefined();
  });

  it('does NOT flag the step for a heading or body edit', () => {
    const s = step();
    applyPatchAndInvalidate(s, patch({ heading: 'H', body: 'B' }), false);
    expect(s.captionEditedByUser).toBeUndefined();
  });

  it('flags an empty-string caption too — clearing it is still a human edit', () => {
    const s = step();
    applyPatchAndInvalidate(s, patch({ caption: '' }), false);
    expect(s.captionEditedByUser).toBe(true);
  });

  it('is set even when a fresh PNG accompanies the patch', () => {
    // hasFreshPng returns early from the INVALIDATION logic; the flag must be
    // recorded before that return, not after it.
    const s = step();
    applyPatchAndInvalidate(s, patch({ caption: 'edited' }), true);
    expect(s.captionEditedByUser).toBe(true);
  });
});
