// #95 — root keys this build does not name survive a read/write round trip.
//
// coerceManifest rebuilds the manifest field by field, which made it a FILTER:
// anything unnamed was dropped on every read and gone at the next write. Both
// apps share project.json, and the additive-schema approach they rest on assumes
// a build that does not understand a key leaves it alone. macOS holds that
// assumption; this tree did not, so a key written by a newer build (or by macOS)
// was destroyed simply by opening the project here. Not rendered as a fallback —
// gone, with no way to tell it had existed.
//
// Same harness as project-theme-key.test.ts: mock only the electron/settings
// boundary and run the REAL store against a temp directory, because the bug was
// in the read/write ROUND TRIP rather than in any single function.
import { describe, it, expect, beforeEach, afterEach, vi } from 'vitest';
import { promises as fs } from 'node:fs';
import os from 'node:os';
import path from 'node:path';

const h = vi.hoisted(() => ({ root: '', brand: 'shotAI' as string }));

vi.mock('electron', () => ({ shell: { showItemInFolder: () => undefined } }));
vi.mock('./settings', () => ({
  getProjectsDir: async () => h.root,
  getRecents: async () => [],
  addRecent: async () => undefined,
  setRecents: async () => undefined,
  persistProjectsDir: async () => undefined,
  getBrand: async () => h.brand,
}));

import { coerceManifest, openProject, setProjectIntro } from './project-store';
import { MANIFEST_KEYS } from '../shared/project';

let projectDir: string;

const BASE = {
  version: 1,
  id: 'test',
  title: 'T',
  createdWith: 'shotAI',
  createdAt: '2026-01-01T00:00:00.000Z',
  updatedAt: '2026-01-01T00:00:00.000Z',
  captureSettings: null,
  steps: [],
  sopBackup: null,
};

async function onDisk(dir: string): Promise<Record<string, unknown>> {
  return JSON.parse(await fs.readFile(path.join(dir, 'project.json'), 'utf8'));
}

async function writeRaw(dir: string, obj: unknown): Promise<void> {
  await fs.writeFile(path.join(dir, 'project.json'), JSON.stringify(obj, null, 2));
}

beforeEach(async () => {
  h.root = await fs.mkdtemp(path.join(os.tmpdir(), 'shotai-extras-'));
  h.brand = 'shotAI';
  projectDir = path.join(h.root, 'proj1');
  await fs.mkdir(projectDir, { recursive: true });
  await writeRaw(projectDir, BASE);
});
afterEach(async () => {
  await fs.rm(h.root, { recursive: true, force: true });
});

describe('an unknown root key rides along', () => {
  it('survives a read AND a real write', async () => {
    await writeRaw(projectDir, { ...BASE, somethingNew: { nested: [1, 2] } });

    // Cast, because ProjectManifest deliberately has NO index signature: adding
    // one to make this line compile would turn off excess-property checking on
    // every manifest literal in the app, which is a far bigger loss than a cast
    // in a test. Unknown keys ride along at runtime and are invisible to the type.
    const opened = (await openProject(projectDir)) as unknown as Record<string, unknown>;
    expect(opened.somethingNew).toEqual({ nested: [1, 2] });

    // The read half alone is not the fix. The key had to survive being written
    // back, which is where the rebuild actually discarded it.
    await setProjectIntro(projectDir, { heading: 'H', body: 'B' });
    expect((await onDisk(projectDir)).somethingNew).toEqual({ nested: [1, 2] });
  });

  it('keeps the value verbatim rather than coercing it', async () => {
    // "Preserve" means preserve. A build that does not know a key also does not
    // know its shape, so anything it does to the value is a guess.
    const odd = { s: 'x', n: 0, b: false, nul: null, arr: [], deep: { a: { b: 1 } } };
    await writeRaw(projectDir, { ...BASE, futureThing: odd });
    await setProjectIntro(projectDir, { heading: 'H', body: 'B' });
    expect((await onDisk(projectDir)).futureThing).toEqual(odd);
  });
});

describe('preserving cannot resurrect a value the coercion rejected', () => {
  // THE trap in this change, and the reason MANIFEST_KEYS is an exact list of key
  // NAMES rather than "whatever coerceManifest emitted". displayScale, theme and
  // introEditedByUser are emitted CONDITIONALLY — absent when default or invalid.
  // Deriving the known-set from the OUTPUT would classify each as unknown exactly
  // when it was deliberately omitted, and the extras copy would put the rejected
  // value straight back. The coercion would look like it worked and the file
  // would say otherwise.

  it('drops a junk displayScale instead of preserving it', async () => {
    await writeRaw(projectDir, { ...BASE, displayScale: 'banana' });
    const m = await coerceManifest({ ...BASE, displayScale: 'banana' } as never, 'T');
    expect(m.displayScale, 'a non-numeric scale is not a scale').toBeUndefined();
    expect(Object.keys(m)).not.toContain('displayScale');
  });

  it('drops an out-of-range displayScale rather than re-adding the raw number', async () => {
    const m = await coerceManifest({ ...BASE, displayScale: 9 } as never, 'T');
    expect(m.displayScale, 'clamped to the max, not the raw 9').toBe(1.25);
  });

  it('drops a non-string theme instead of preserving it', async () => {
    // The string-only line matches macOS's `String` decode. 42/null/{}/[] are not
    // brands anyone could have meant, so they are junk rather than a future value.
    for (const bad of [42, null, {}, []]) {
      const m = await coerceManifest({ ...BASE, theme: bad } as never, 'T');
      expect(Object.keys(m), JSON.stringify(bad)).not.toContain('theme');
    }
  });

  it('drops introEditedByUser:false instead of preserving it', async () => {
    const m = await coerceManifest({ ...BASE, introEditedByUser: false } as never, 'T');
    expect(Object.keys(m), 'omitted rather than stored false').not.toContain('introEditedByUser');
  });

  it('names every interface key, so none of the above can silently flip', () => {
    // The compiler keeps MANIFEST_KEYS complete; this keeps it HONEST about the
    // three conditional keys, which are the only ones where a miss is invisible.
    for (const k of ['displayScale', 'theme', 'introEditedByUser']) {
      expect(Object.hasOwn(MANIFEST_KEYS, k), `${k} must be a KNOWN key`).toBe(true);
    }
  });
});

describe('a coerced field always wins over a preserved one', () => {
  it('still hands steps to normalizeSteps, not through as raw data', async () => {
    // Honest about what this does and does not prove. With a correct
    // MANIFEST_KEYS filter, `steps` is never in extras, so this passes whether
    // extras spread first or last — mutation-checked. It pins that adding the
    // extras path did not accidentally route steps around the coercion; the
    // ORDERING is pinned by the key-order assertion below.
    //
    // First-position is belt to that braces: if the filter ever leaks a named
    // key, last-position would overwrite the coerced value with the raw one, and
    // for `steps` that is the #86 crash reintroduced. First-position makes a
    // leak inert instead.
    const m = await coerceManifest(
      { ...BASE, steps: [null, { id: 's1', order: 0, kind: 'shot' }] } as never,
      'T',
    );
    expect(m.steps, 'the null is dropped by normalizeSteps, not preserved').toHaveLength(1);
    expect(m.steps[0].id).toBe('s1');
  });
});

describe('__proto__ is data, not a prototype', () => {
  it('round-trips as an own key without reparenting the manifest', async () => {
    // JSON.parse makes `__proto__` an OWN property; assigning it into a plain
    // object with out[k] = v or Object.assign would instead reparent the target,
    // losing the key AND corrupting the result. Object.create(null) + spread is
    // what keeps it data.
    const parsed = JSON.parse(`{"__proto__": {"polluted": true}}`);
    const m = await coerceManifest({ ...BASE, ...parsed } as never, 'T');

    expect(Object.getPrototypeOf(m), 'the manifest must not be reparented').toBe(Object.prototype);
    expect(({} as Record<string, unknown>).polluted, 'nothing global was polluted').toBeUndefined();
    expect(Object.hasOwn(m, '__proto__'), 'and the key survives as data').toBe(true);
  });
});

describe('a project with no extras is untouched', () => {
  it('adds no key and no sidecar', async () => {
    // The guarantee every existing project depends on: this change must be
    // invisible to anyone who has no unknown keys. An `extra: {}` sidecar would
    // fail here, and so would preserving a key the coercion meant to omit.
    await setProjectIntro(projectDir, { heading: 'H', body: 'B' });
    const before = Object.keys(await onDisk(projectDir)).sort();

    await setProjectIntro(projectDir, { heading: 'H2', body: 'B2' });
    const after = Object.keys(await onDisk(projectDir)).sort();

    expect(after, 'the key SET must not change').toEqual(before);
    expect(after).not.toContain('extra');
    expect(after).not.toContain('extras');
    for (const k of after) expect(Object.hasOwn(MANIFEST_KEYS, k), `${k} is unexpected`).toBe(true);
  });

  it('spreads nothing when there is nothing to spread', () => {
    // Order is asserted HERE, at the codec, rather than across two saves. Across
    // saves it does not hold, and NOT because of this change: `mutate` assigns
    // introEditedByUser onto an object that lacks it, so JS appends it last, and
    // the next read puts it back in its literal position. Verified by running the
    // across-saves assertion against unmodified main, where it fails identically.
    // That is the Windows twin of shotAI_MacOS#110 and is filed separately; do
    // not "fix" it by weakening this.
    //
    // What this change owes is narrower and is what is checked: for a manifest
    // with no unknown keys, the extras spread is empty and the output is exactly
    // what the literal produces.
    const withoutExtras = coerceManifest({ ...BASE } as never, 'T');
    const withIgnored = coerceManifest({ ...BASE, futureThing: 1 } as never, 'T');

    expect(Object.keys(withoutExtras), 'no extras, literal order').toEqual([
      'version',
      'id',
      'title',
      'createdWith',
      'createdAt',
      'updatedAt',
      'captureSettings',
      'steps',
      'intro',
      'sopBackup',
      'archived',
      'archivedAt',
    ]);
    // And an extra goes FIRST, ahead of every coerced field, so a filter leak can
    // never overwrite one. See the comment in coerceManifest.
    expect(Object.keys(withIgnored)[0]).toBe('futureThing');
  });
});
