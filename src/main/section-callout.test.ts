// #45 — a `callout:'section'` text step must survive coerceManifest/normalizeSteps
// (round-trips losslessly, including from a macOS-authored project.json). Also
// covers the forward-compat contract: an UNKNOWN callout value is kept, not
// stripped (older builds degrade it in the UI but still round-trip).
import { describe, it, expect, vi } from 'vitest';
import type { ProjectManifest } from '../shared/project';

vi.mock('electron', () => ({ shell: { showItemInFolder: () => undefined } }));
vi.mock('./settings', () => ({
  getProjectsDir: async () => '',
  getRecents: async () => [],
  addRecent: async () => undefined,
  setRecents: async () => undefined,
  persistProjectsDir: async () => undefined,
}));

// Imported AFTER the mocks (vi.mock is hoisted) so project-store binds the stubs.
import { coerceManifest } from './project-store';

const textStep = (extra: Record<string, unknown>) => ({
  id: 's1',
  order: 1,
  kind: 'text',
  screenshot: '',
  trigger: 'hotkey',
  click: null,
  monitor: null,
  window: null,
  element: { available: false, name: null, controlType: null, bounds: null },
  caption: '',
  heading: 'Phase 2',
  body: 'Now do the next part',
  crop: null,
  annotations: [],
  ...extra,
});

describe('coerceManifest — section callout round-trip (#45)', () => {
  it('preserves callout:"section" on a text step', () => {
    const parsed = { steps: [textStep({ callout: 'section' })] } as unknown as Partial<ProjectManifest>;
    const m = coerceManifest(parsed, 'fallback');
    expect(m.steps).toHaveLength(1);
    expect(m.steps[0].kind).toBe('text');
    expect(m.steps[0].callout).toBe('section');
  });

  it('keeps an unknown callout value (forward-compat — not stripped on read)', () => {
    const parsed = { steps: [textStep({ callout: 'futurekind' })] } as unknown as Partial<ProjectManifest>;
    const m = coerceManifest(parsed, 'fallback');
    expect((m.steps[0] as { callout?: string }).callout).toBe('futurekind');
  });
});
