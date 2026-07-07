// Project archiving (F2) — compress a project's bulk files in place so it takes
// far less disk while staying listed, then restore them on demand.
//
// Layout: a live project is a folder with project.json + shots/ + export/.
// Archiving zips the BULK dirs (shots/, export/) into archive.zip at the project
// root and removes the loose copies; project.json stays put so the project keeps
// listing (title/dates/step-count) without a full extract. Opening an archived
// project restores it first (see project-store loadProject).
//
// DATA SAFETY (fail-closed): the compressed copy is fully written AND verified
// (re-read, entry set matches) BEFORE any original file is deleted; extraction
// verifies every entry landed BEFORE the zip is removed. A failure at any step
// throws and leaves the project's files intact.
import { promises as fs } from 'node:fs';
import path from 'node:path';
import JSZip from 'jszip';
import { confinePath } from './path-confine';
import { projectsLog } from './logger';

const ARCHIVE_ZIP = 'archive.zip';
/** Top-level dirs compressed + removed on archive; project.json is never touched. */
const ARCHIVED_DIRS = ['shots', 'export'] as const;

/** Recursively collect the files under `dir`, as paths relative to `base` (POSIX
 *  separators for stable zip entry names). Missing dirs are skipped. */
async function walkFiles(
  base: string,
  dir: string,
  out: { rel: string; abs: string }[],
): Promise<void> {
  let entries: import('node:fs').Dirent[];
  try {
    entries = await fs.readdir(dir, { withFileTypes: true });
  } catch {
    return; // dir doesn't exist (e.g. no export/) — nothing to add
  }
  for (const e of entries) {
    const abs = path.join(dir, e.name);
    if (e.isDirectory()) await walkFiles(base, abs, out);
    else if (e.isFile()) out.push({ rel: path.relative(base, abs).replace(/\\/g, '/'), abs });
  }
}

/** True if the project folder is archived on disk (archive.zip present). */
export async function isArchivedOnDisk(projectDir: string): Promise<boolean> {
  try {
    await fs.access(path.join(projectDir, ARCHIVE_ZIP));
    return true;
  } catch {
    return false;
  }
}

/**
 * Compress the project's bulk dirs into archive.zip and remove the loose copies.
 * No-op if already archived. Fail-closed: verifies the zip before deleting any
 * original. Does NOT touch the manifest — the caller flips manifest.archived.
 */
export async function packArchive(projectDir: string): Promise<void> {
  const zipPath = path.join(projectDir, ARCHIVE_ZIP);
  if (await isArchivedOnDisk(projectDir)) return;

  // 1. Gather every file in the bulk dirs.
  const files: { rel: string; abs: string }[] = [];
  for (const d of ARCHIVED_DIRS) await walkFiles(projectDir, path.join(projectDir, d), files);

  // 2. Build the compressed archive in memory.
  const zip = new JSZip();
  for (const f of files) zip.file(f.rel, await fs.readFile(f.abs));
  const buf = await zip.generateAsync({
    type: 'nodebuffer',
    compression: 'DEFLATE',
    compressionOptions: { level: 6 },
  });

  // 3. Write to a tmp file, then VERIFY it re-reads with the exact same entry set.
  const tmp = `${zipPath}.tmp`;
  await fs.writeFile(tmp, buf);
  try {
    const check = await JSZip.loadAsync(await fs.readFile(tmp));
    const got = Object.values(check.files)
      .filter((e) => !e.dir)
      .map((e) => e.name)
      .sort();
    const want = files.map((f) => f.rel).sort();
    if (got.length !== want.length || got.some((n, i) => n !== want[i])) {
      throw new Error(
        `archive verification failed (${got.length} entries, expected ${want.length}) — nothing deleted`,
      );
    }
  } catch (e) {
    await fs.rm(tmp, { force: true }).catch(() => undefined);
    throw e;
  }

  // 4. Atomically place archive.zip, THEN (only now) remove the loose originals.
  await fs.rename(tmp, zipPath);
  for (const d of ARCHIVED_DIRS) {
    await fs.rm(path.join(projectDir, d), { recursive: true, force: true });
  }
  projectsLog.info(`archive: packed ${files.length} file(s) → ${zipPath}`);
}

/**
 * Restore a project's files from archive.zip and remove the zip. No-op if not
 * archived. Fail-closed: every entry is extracted (confined to the project, only
 * into the known bulk dirs) and verified present BEFORE the zip is deleted.
 * Does NOT touch the manifest — the caller flips manifest.archived=false.
 */
export async function unpackArchive(projectDir: string): Promise<void> {
  const zipPath = path.join(projectDir, ARCHIVE_ZIP);
  if (!(await isArchivedOnDisk(projectDir))) return;

  const zip = await JSZip.loadAsync(await fs.readFile(zipPath));
  const entries = Object.values(zip.files).filter((e) => !e.dir);
  const written: string[] = [];
  for (const e of entries) {
    const rel = e.name.replace(/\\/g, '/');
    // Only restore into the dirs we archive — reject anything else (a tampered zip).
    const allowed = ARCHIVED_DIRS.some((d) => rel === d || rel.startsWith(`${d}/`));
    if (!allowed) throw new Error(`archive contains an unexpected path: ${e.name}`);
    const abs = confinePath(projectDir, rel); // defense-in-depth against zip-slip
    if (!abs) throw new Error(`refusing to extract a path outside the project: ${e.name}`);
    await fs.mkdir(path.dirname(abs), { recursive: true });
    await fs.writeFile(abs, await e.async('nodebuffer'));
    written.push(abs);
  }
  // Verify every extracted file exists before removing the compressed copy.
  for (const abs of written) await fs.access(abs);
  await fs.rm(zipPath, { force: true });
  projectsLog.info(`archive: restored ${written.length} file(s) from ${zipPath}`);
}
