// The single source of truth for shotAI's renderer-sandbox path-traversal
// boundary. Kept dependency-free (only node:path) so it is trivially unit-tested
// and importable from anywhere in main without pulling in electron.
//
// Used by the shot:// resolver, the Claude/export read paths, the per-step render
// writes, and the delete path. Any future hardening (symlink resolution, UNC
// rejection, case-folding on Windows) belongs HERE so every caller inherits it.
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
