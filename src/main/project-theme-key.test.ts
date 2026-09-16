// #77 phase 1b — the `theme` key in project.json.
//
// This one is worth a REAL behavioural test rather than a source scan, because it
// is the cross-platform schema: macOS reads and writes the same field, and the
// write rule is the part that has to hold byte-for-byte. A project that never
// picks a brand must stay exactly as it is on disk, or every existing project
// churns the moment this ships and the other platform sees phantom edits.
//
// Same harness as mutate-serialize.test.ts: mock only the electron/settings
// boundary, run the REAL store against a temp directory.
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

import { createProject, setProjectTheme, setProjectIntro, openProject } from './project-store';
import { pinnedBrand } from '../shared/theme-palette';

let projectDir: string;

/** The manifest exactly as it sits on disk, not as the store re-derives it. */
async function onDisk(dir: string): Promise<Record<string, unknown>> {
  return JSON.parse(await fs.readFile(path.join(dir, 'project.json'), 'utf8'));
}

beforeEach(async () => {
  h.root = await fs.mkdtemp(path.join(os.tmpdir(), 'shotai-theme-'));
  h.brand = 'shotAI';
  projectDir = path.join(h.root, 'proj1');
  await fs.mkdir(projectDir, { recursive: true });
  await fs.writeFile(
    path.join(projectDir, 'project.json'),
    JSON.stringify({
      version: 1,
      id: 'test',
      title: 'T',
      createdWith: 'shotAI',
      createdAt: '2026-01-01T00:00:00.000Z',
      updatedAt: '2026-01-01T00:00:00.000Z',
      captureSettings: null,
      steps: [],
      sopBackup: null,
    }),
  );
});
afterEach(async () => {
  await fs.rm(h.root, { recursive: true, force: true });
});

describe('the write rule: the default brand writes nothing', () => {
  it('creates a project with NO theme key when the app is on the default brand', async () => {
    const summary = await createProject('Untouched');
    const m = await onDisk(summary.path);
    expect(Object.keys(m), 'a default-branded project must stay byte-identical').not.toContain(
      'theme',
    );
  });

  it('stamps the key at creation when the app is on another brand', async () => {
    h.brand = 'lfi';
    const summary = await createProject('Corporate');
    expect((await onDisk(summary.path)).theme).toBe('lfi');
  });

  it('clears the key on null, which is what "App default" means', async () => {
    await setProjectTheme(projectDir, 'lfi');
    expect((await onDisk(projectDir)).theme).toBe('lfi');
    await setProjectTheme(projectDir, null);
    expect(Object.keys(await onDisk(projectDir))).not.toContain('theme');
  });

  it('PINS the default brand when it is chosen explicitly', async () => {
    // The correction. Setting the default used to clear the key, on the theory
    // that an absent key already says "the default". It does not — it says
    // "follow the app" — and the two come apart the moment the app preference
    // is something else. With the app on LFI there was then no way to hold a
    // project on shotAI: the value that would have said so was refused here and
    // dropped on read.
    await setProjectTheme(projectDir, 'shotAI');
    expect((await onDisk(projectDir)).theme).toBe('shotAI');
  });

  it('keeps a pinned default through a read, rather than dropping it', async () => {
    // The other half. coerceManifest rebuilds field by field, and it used to
    // drop theme whenever it coerced to the default — so even a correctly
    // written 'shotAI' would not survive being opened.
    await setProjectTheme(projectDir, 'shotAI');
    expect((await openProject(projectDir)).theme).toBe('shotAI');
  });

  it('distinguishes a pinned default from an absent key, end to end', async () => {
    // The distinction this whole correction exists for, stated once.
    await setProjectTheme(projectDir, 'shotAI');
    expect((await openProject(projectDir)).theme).toBe('shotAI');
    await setProjectTheme(projectDir, null);
    expect((await openProject(projectDir)).theme).toBeUndefined();
  });
});

describe('a write that changes nothing is refused', () => {
  // mutate() bumps updatedAt unconditionally, so a no-op save re-dates the
  // project and jumps it to the top of the Home list under "Today" purely
  // because a control was touched. The same trap the size slider guards against
  // in the renderer — but this setter is reachable from IPC, so the guard has to
  // be on this side too.
  it('does not re-date a project when the brand is already that brand', async () => {
    await setProjectTheme(projectDir, 'lfi');
    const first = (await onDisk(projectDir)).updatedAt;
    const bytes = await fs.readFile(path.join(projectDir, 'project.json'), 'utf8');

    await setProjectTheme(projectDir, 'lfi');
    expect((await onDisk(projectDir)).updatedAt).toBe(first);
    expect(await fs.readFile(path.join(projectDir, 'project.json'), 'utf8')).toBe(bytes);
  });

  it('does not re-date an UNBRANDED project when asked to clear it', async () => {
    // Compared RAW, not coerced: an absent key and an explicit default are
    // different states now, so null-vs-absent is the no-op and
    // default-vs-absent is a real write.
    const before = await fs.readFile(path.join(projectDir, 'project.json'), 'utf8');
    await setProjectTheme(projectDir, null);
    expect(await fs.readFile(path.join(projectDir, 'project.json'), 'utf8')).toBe(before);
  });

  it('DOES write when an unbranded project is pinned to the default', async () => {
    // The control for the test above. If the guard still folded the default
    // into "absent", this would be a no-op and the feature would be missing.
    const before = (await onDisk(projectDir)).updatedAt;
    await setProjectTheme(projectDir, 'shotAI');
    expect((await onDisk(projectDir)).theme).toBe('shotAI');
    expect((await onDisk(projectDir)).updatedAt).not.toBe(before);
  });

  it('DOES re-date when the brand actually changes', async () => {
    // The control for the two above: if the guard were "never write", they would
    // both pass and the feature would not work at all.
    const before = (await onDisk(projectDir)).updatedAt;
    await setProjectTheme(projectDir, 'lfi');
    expect((await onDisk(projectDir)).updatedAt).not.toBe(before);
  });
});

describe('reading a key the other platform wrote', () => {
  it('round-trips a brand macOS set', async () => {
    const raw = await onDisk(projectDir);
    await fs.writeFile(
      path.join(projectDir, 'project.json'),
      JSON.stringify({ ...raw, theme: 'lfi' }),
    );
    // coerceManifest rebuilds the manifest field by field, so an uncoerced field
    // is dropped on EVERY read — the exact trap displayScale and
    // introEditedByUser both hit. A macOS-authored brand would be discarded the
    // first time Windows opened the project.
    expect((await openProject(projectDir)).theme).toBe('lfi');
  });

  it('keeps a brand it does not know, and renders it as none (#95)', async () => {
    // Two separable things, and fusing them was the bug. The value MUST NOT
    // reach the renderer, because an unmatched [data-brand] matches none of the
    // generated blocks and falls back to the bare :root — default brand, LIGHT,
    // even in dark mode. But "must not be rendered" never required "must be
    // deleted from the file", and deleting it means an older build silently
    // destroys a newer build's pin with no trace it existed.
    const raw = await onDisk(projectDir);
    await fs.writeFile(
      path.join(projectDir, 'project.json'),
      JSON.stringify({ ...raw, theme: 'some-future-brand' }),
    );

    const m = await openProject(projectDir);
    expect(m.theme, 'the raw value survives the read').toBe('some-future-brand');
    expect(pinnedBrand(m.theme), 'but resolves to no brand, so the app preference wins').toBeNull();

    // A read that preserves is worthless if the next WRITE drops it, which is
    // where the field-by-field rebuild actually bit. Force a real save and
    // re-read from disk rather than trusting the in-memory object.
    await setProjectIntro(projectDir, { heading: 'H', body: 'B' });
    expect(
      (await onDisk(projectDir)).theme,
      'and survives a real write, not just the read',
    ).toBe('some-future-brand');
  });

  it('is not confused by a non-string', async () => {
    const raw = await onDisk(projectDir);
    for (const bad of [42, null, {}, []]) {
      await fs.writeFile(
        path.join(projectDir, 'project.json'),
        JSON.stringify({ ...raw, theme: bad }),
      );
      expect((await openProject(projectDir)).theme, JSON.stringify(bad)).toBeUndefined();
    }
  });
});
