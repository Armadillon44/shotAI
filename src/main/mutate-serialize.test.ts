// P6 — direct concurrent-call test for ProjectStore.mutate serialization + the
// atomic write underneath it. The writeQueue must serialize read-modify-write so
// concurrent mutations can't lose updates (last-writer-wins) or tear the file.
// Mocks only the settings/electron boundary so the REAL mutate + readManifest +
// writeManifest (writeFileAtomic) run against a temp project on disk.
import { describe, it, expect, beforeEach, afterEach, vi } from 'vitest';
import { promises as fs } from 'node:fs';
import os from 'node:os';
import path from 'node:path';

const h = vi.hoisted(() => ({ root: '' }));

vi.mock('electron', () => ({ shell: { showItemInFolder: () => undefined } }));
vi.mock('./settings', () => ({
  getProjectsDir: async () => h.root,
  getRecents: async () => [],
  addRecent: async () => undefined,
  setRecents: async () => undefined,
  persistProjectsDir: async () => undefined,
}));

// Imported AFTER the mocks above (vi.mock is hoisted) so project-store binds the stubs.
import { mutate } from './project-store';

let projectDir: string;
const INIT = 'INIT'; // non-empty so readManifest keeps it (empty title -> basename)

beforeEach(async () => {
  h.root = await fs.mkdtemp(path.join(os.tmpdir(), 'shotai-mutate-'));
  projectDir = path.join(h.root, 'proj1');
  await fs.mkdir(projectDir, { recursive: true });
  const manifest = {
    version: 1, id: 'test', title: INIT, createdWith: 'shotAI',
    createdAt: '', updatedAt: '', captureSettings: null, steps: [], sopBackup: null,
  };
  await fs.writeFile(path.join(projectDir, 'project.json'), JSON.stringify(manifest));
});
afterEach(async () => { await fs.rm(h.root, { recursive: true, force: true }); });

describe('ProjectStore.mutate serialization', () => {
  it('does not lose updates under many concurrent calls (writeQueue serializes RMW)', async () => {
    const N = 40;
    // Fire all mutations WITHOUT awaiting between them — they queue on writeQueue.
    const ps = Array.from({ length: N }, () =>
      mutate(projectDir, (m) => { m.title = (m.title ?? '') + 'x'; }),
    );
    await Promise.all(ps);

    const raw = await fs.readFile(path.join(projectDir, 'project.json'), 'utf8');
    const j = JSON.parse(raw); // throws if the atomic write ever left a torn file
    // Every appended 'x' survived -> no lost updates.
    expect(j.title).toBe(INIT + 'x'.repeat(N));
  });

  it('leaves no stray .tmp files and a valid manifest after concurrency', async () => {
    const N = 25;
    await Promise.all(
      Array.from({ length: N }, () => mutate(projectDir, (m) => { m.title += 'y'; })),
    );
    const entries = await fs.readdir(projectDir);
    expect(entries.filter((e) => e.endsWith('.tmp'))).toEqual([]);
    const j = JSON.parse(await fs.readFile(path.join(projectDir, 'project.json'), 'utf8'));
    expect(j.title).toBe(INIT + 'y'.repeat(N));
  });

  it('a throwing mutation aborts its write without corrupting or losing others', async () => {
    const ps = [
      mutate(projectDir, (m) => { m.title += 'a'; }),
      mutate(projectDir, () => { throw new Error('abort this one'); }).catch(() => 'caught'),
      mutate(projectDir, (m) => { m.title += 'b'; }),
    ];
    const results = await Promise.all(ps);
    expect(results[1]).toBe('caught');
    const j = JSON.parse(await fs.readFile(path.join(projectDir, 'project.json'), 'utf8'));
    // The two successful mutations both landed; the thrower wrote nothing.
    expect(j.title).toBe(INIT + 'ab');
  });
});
