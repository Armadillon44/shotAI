// F2 — archive engine round-trip + fail-closed guarantees. Exercises the REAL
// packArchive/unpackArchive against a temp project on disk (jszip is real);
// only the electron-importing logger is stubbed.
import { describe, it, expect, beforeEach, afterEach, vi } from 'vitest';
import { promises as fs } from 'node:fs';
import os from 'node:os';
import path from 'node:path';
import JSZip from 'jszip';

vi.mock('./logger', () => ({ projectsLog: { info: () => undefined, warn: () => undefined } }));

import { packArchive, unpackArchive, isArchivedOnDisk } from './archive';

let dir: string;

async function seedProject(): Promise<void> {
  await fs.mkdir(path.join(dir, 'shots'), { recursive: true });
  await fs.mkdir(path.join(dir, 'export', '.render'), { recursive: true });
  await fs.writeFile(path.join(dir, 'project.json'), '{"id":"x"}');
  await fs.writeFile(path.join(dir, 'shots', 'step-0001.png'), Buffer.from([1, 2, 3]));
  await fs.writeFile(path.join(dir, 'shots', 'step-0002.png'), Buffer.from([4, 5, 6, 7]));
  await fs.writeFile(path.join(dir, 'export', '.render', 'r1.png'), Buffer.from([9, 9]));
}

beforeEach(async () => {
  dir = await fs.mkdtemp(path.join(os.tmpdir(), 'shotai-arch-'));
});
afterEach(async () => {
  await fs.rm(dir, { recursive: true, force: true });
});

describe('archive pack/unpack', () => {
  it('packs bulk dirs into archive.zip and removes the loose copies (manifest stays)', async () => {
    await seedProject();
    await packArchive(dir);
    expect(await isArchivedOnDisk(dir)).toBe(true);
    await expect(fs.access(path.join(dir, 'shots'))).rejects.toBeTruthy();
    await expect(fs.access(path.join(dir, 'export'))).rejects.toBeTruthy();
    // project.json is never archived — listing must still work.
    expect(await fs.readFile(path.join(dir, 'project.json'), 'utf8')).toBe('{"id":"x"}');
  });

  it('round-trips: unpack restores every file byte-identical and removes the zip', async () => {
    await seedProject();
    const before = {
      s1: await fs.readFile(path.join(dir, 'shots', 'step-0001.png')),
      s2: await fs.readFile(path.join(dir, 'shots', 'step-0002.png')),
      r1: await fs.readFile(path.join(dir, 'export', '.render', 'r1.png')),
    };
    await packArchive(dir);
    await unpackArchive(dir);
    expect(await isArchivedOnDisk(dir)).toBe(false);
    expect(await fs.readFile(path.join(dir, 'shots', 'step-0001.png'))).toEqual(before.s1);
    expect(await fs.readFile(path.join(dir, 'shots', 'step-0002.png'))).toEqual(before.s2);
    expect(await fs.readFile(path.join(dir, 'export', '.render', 'r1.png'))).toEqual(before.r1);
  });

  it('packArchive is a no-op when the project is already archived', async () => {
    await seedProject();
    await packArchive(dir);
    const first = await fs.readFile(path.join(dir, 'archive.zip'));
    await packArchive(dir);
    expect(await fs.readFile(path.join(dir, 'archive.zip'))).toEqual(first);
  });

  it('unpackArchive is a no-op when not archived', async () => {
    await seedProject();
    await unpackArchive(dir);
    expect(await isArchivedOnDisk(dir)).toBe(false);
    expect(await fs.readFile(path.join(dir, 'shots', 'step-0001.png'))).toEqual(Buffer.from([1, 2, 3]));
  });

  it('unpack refuses an entry outside the archived dirs and keeps the zip intact', async () => {
    await seedProject();
    // Craft a malicious archive.zip with a path-traversal entry.
    const zip = new JSZip();
    zip.file('../evil.txt', 'pwned');
    await fs.writeFile(
      path.join(dir, 'archive.zip'),
      await zip.generateAsync({ type: 'nodebuffer' }),
    );
    await expect(unpackArchive(dir)).rejects.toThrow();
    // The zip is NOT deleted, and nothing escaped the project folder.
    expect(await isArchivedOnDisk(dir)).toBe(true);
    await expect(fs.access(path.join(path.dirname(dir), 'evil.txt'))).rejects.toBeTruthy();
  });
});
