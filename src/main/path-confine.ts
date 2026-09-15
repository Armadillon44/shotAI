// The single source of truth for shotAI's renderer-sandbox path-traversal
// boundary. Kept dependency-free (only node:path) so it is trivially unit-tested
// and importable from anywhere in main without pulling in electron.
//
// Used by the shot:// resolver, the Claude/export read paths, the per-step render
// writes, and the delete path. Any future hardening (UNC rejection, case-folding
// on Windows) belongs HERE so every caller inherits it.
//
// `confinePath` is lexical and stays dependency-free. `confinePathNoSymlinks`
// adds the filesystem check and therefore needs node:fs/promises — still no
// electron, so both remain importable from anywhere in main and unit-testable.
import { promises as fs } from 'node:fs';
import path from 'node:path';

/**
 * Confine a project-relative path to `dir`: resolve it and return null if it
 * escapes the folder, equals the folder root, or is absolute. Otherwise return
 * the resolved absolute path.
 */
export function confinePath(dir: string, rel: string): string | null {
  const abs = path.resolve(dir, rel);
  const within = path.relative(dir, abs);
  if (within === '' || within.startsWith('..') || path.isAbsolute(within)) {
    return null; // escapes the project folder
  }
  return abs;
}

/**
 * Confine to `dir` AND refuse any symlinked component along the way.
 *
 * Lexical confinement alone is not enough for a path that will be WRITTEN or
 * DELETED: `path.resolve` works on strings, so `shots/evil` passes it happily
 * while `shots` is a symlink pointing anywhere on disk, and the write lands
 * outside the project. A symlink gets there without anything hostile — a synced
 * or shared project folder is enough.
 *
 * Walks one component at a time because `lstat` never follows the final
 * component: checking only the full path would resolve every parent link and
 * report on the target, which is the thing being defended against. The FIRST
 * link wins and the walk stops.
 *
 * A component that does not exist yet is fine, and is the normal case for a
 * write — the caller is about to `mkdir -p` it. Any other stat failure is
 * refused, because "cannot tell" is not "safe".
 *
 * READS deliberately keep the lexical `confinePath`. The cost is paid where it
 * matters, which mirrors macOS (`ShotModel/PathConfine.swift`), whose comment
 * records the symlinked-`shots/` residual that prompted it there.
 */
export async function confinePathNoSymlinks(dir: string, rel: string): Promise<string | null> {
  const abs = confinePath(dir, rel);
  if (abs === null) return null;

  const base = path.resolve(dir);
  const tail = path.relative(base, abs);
  let current = base;
  for (const component of tail.split(path.sep)) {
    if (component === '') continue;
    current = path.join(current, component);
    try {
      // Windows junctions are covered. Node reports a directory junction as a
      // symbolic link here, which is what we want since a junction redirects
      // identically — CONFIRMED on a real Windows box (2026-09-15):
      //   mklink /J %TEMP%\junc %TEMP%
      //   lstatSync(...).isSymbolicLink()  ->  true
      // Worth keeping written down: CI runs Linux and the maintainer works on
      // macOS, so nothing in the automated path exercises a junction.
      if ((await fs.lstat(current)).isSymbolicLink()) return null;
    } catch (e) {
      if ((e as NodeJS.ErrnoException).code === 'ENOENT') break; // nothing deeper exists either
      return null;
    }
  }
  return abs;
}
