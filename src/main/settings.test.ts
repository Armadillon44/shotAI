// #92 — settings.json was rebuilt field by field on load and written back with
// JSON.stringify, so any key the running build did not name was DELETED.
//
// The realistic trigger is a rollback, not corruption: pick the LFI brand on a
// newer build, then open the app on a machine still on v1.2.0. That build does
// not name `brand`, so its first settings write removes it, and returning to the
// newer build shows no brand selection and no reason why.
//
// There was no settings.test.ts at all before this.
import { describe, it, expect, beforeEach, afterEach, vi } from 'vitest';
import { promises as fs } from 'node:fs';
import os from 'node:os';
import path from 'node:path';

const h = vi.hoisted(() => ({ dir: '' }));
vi.mock('electron', () => ({
  app: { getPath: (k: string) => (k === 'home' ? h.dir : h.dir) },
}));

import { getProjectsDir, setRecents, defaultProjectsDir } from './settings';

const file = () => path.join(h.dir, 'settings.json');
const read = async () => JSON.parse(await fs.readFile(file(), 'utf8'));

beforeEach(async () => {
  h.dir = await fs.mkdtemp(path.join(os.tmpdir(), 'settings-'));
  vi.resetModules();
});
afterEach(async () => { await fs.rm(h.dir, { recursive: true, force: true }); });

describe('settings preserve keys this build does not name', () => {
  it('keeps an unknown key across a load/save cycle', async () => {
    await fs.writeFile(file(), JSON.stringify({
      projectsDir: h.dir,
      brand: 'lfi',                 // the concrete case: a newer build's key
      somethingFuture: { a: [1, 2] },
    }));
    // Any write goes through the same load → save path.
    await setRecents([]);
    const after = await read();
    expect(after.brand).toBe('lfi');
    expect(after.somethingFuture).toEqual({ a: [1, 2] });
  });

  it('still repairs a KNOWN key holding a bad value', async () => {
    await fs.writeFile(file(), JSON.stringify({
      projectsDir: 42,              // wrong type — must be coerced, not carried
      recents: 'nope',
      unknownKey: 'kept',
    }));
    await setRecents([]);
    const after = await read();
    expect(after.projectsDir).toBe(defaultProjectsDir());
    expect(after.recents).toEqual([]);
    expect(after.unknownKey).toBe('kept');
  });

  /// The shape guard. A JSON scalar spreads into indexed junk — `{...'ab'}` is
  /// `{0:'a',1:'b'}` — so preserving must only apply to a plain object.
  it.each(['"just a string"', '42', 'true', '[1,2,3]', 'null'])(
    'does not inject junk when the file is %s',
    async (body) => {
      await fs.writeFile(file(), body);
      await setRecents([]);
      const after = await read();
      expect(after['0']).toBeUndefined();
      expect(after.projectsDir).toBe(defaultProjectsDir());
    },
  );

  it('survives a file that is not JSON at all', async () => {
    await fs.writeFile(file(), 'not json {{{');
    await expect(getProjectsDir()).resolves.toBe(defaultProjectsDir());
  });
});
