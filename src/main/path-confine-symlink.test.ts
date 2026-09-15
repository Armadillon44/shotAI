// #82 — lexical confinement works on strings, so `shots/x` passes it happily
// while `shots` is a symlink pointing anywhere on disk and the write lands
// outside the project. A symlink gets there without anything hostile: a synced
// or shared project folder is enough.
import { describe, it, expect, beforeEach, afterEach } from 'vitest';
import { promises as fs } from 'node:fs';
import os from 'node:os';
import path from 'node:path';
import { confinePath, confinePathNoSymlinks } from './path-confine';

let root: string;
let project: string;
let outside: string;

beforeEach(async () => {
  root = await fs.mkdtemp(path.join(os.tmpdir(), 'confine-'));
  project = path.join(root, 'project');
  outside = path.join(root, 'outside');
  await fs.mkdir(path.join(project, 'shots'), { recursive: true });
  await fs.mkdir(outside, { recursive: true });
});
afterEach(async () => { await fs.rm(root, { recursive: true, force: true }); });

describe('confinePathNoSymlinks', () => {
  it('allows an ordinary path, existing or not', async () => {
    await fs.writeFile(path.join(project, 'shots', 'a.png'), 'x');
    expect(await confinePathNoSymlinks(project, 'shots/a.png'))
      .toBe(path.join(project, 'shots', 'a.png'));
    // The normal case for a write: nothing exists yet.
    expect(await confinePathNoSymlinks(project, 'export/.render/new.png'))
      .toBe(path.join(project, 'export', '.render', 'new.png'));
  });

  it('refuses a symlinked DIRECTORY component — the case lexical confinement misses', async () => {
    await fs.rm(path.join(project, 'shots'), { recursive: true });
    await fs.symlink(outside, path.join(project, 'shots'), 'dir');

    // Lexical confinement sees nothing wrong, which is the whole point.
    expect(confinePath(project, 'shots/evil.png')).not.toBeNull();
    expect(await confinePathNoSymlinks(project, 'shots/evil.png')).toBeNull();
  });

  it('refuses a symlinked FINAL component', async () => {
    const target = path.join(outside, 'target.png');
    await fs.writeFile(target, 'x');
    await fs.symlink(target, path.join(project, 'shots', 'link.png'));
    expect(await confinePathNoSymlinks(project, 'shots/link.png')).toBeNull();
  });

  it('refuses a link nested deeper in the walk', async () => {
    await fs.mkdir(path.join(project, 'export'), { recursive: true });
    await fs.symlink(outside, path.join(project, 'export', '.render'), 'dir');
    expect(await confinePathNoSymlinks(project, 'export/.render/x.png')).toBeNull();
  });

  /// A link pointing back INSIDE the project is still refused. The check is on
  /// the mechanism, not the destination: a link can be repointed after the check
  /// and before the write, so "it currently lands inside" is not a safety property.
  it('refuses a symlink even when its target is inside the project', async () => {
    await fs.mkdir(path.join(project, 'real'), { recursive: true });
    await fs.symlink(path.join(project, 'real'), path.join(project, 'aliased'), 'dir');
    expect(await confinePathNoSymlinks(project, 'aliased/x.png')).toBeNull();
  });

  it('still refuses everything the lexical check refuses', async () => {
    for (const rel of ['../escape.png', '/etc/passwd', '', 'shots/../../out.png']) {
      expect(await confinePathNoSymlinks(project, rel), rel).toBeNull();
    }
  });
});
