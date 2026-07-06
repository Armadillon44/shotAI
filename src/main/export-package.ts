// Shareable project package (.zip) that round-trips: export it, send it to a
// colleague, and they import + edit it in shotAI. Two modes (chosen at export):
//   - safe (default): only the redaction-baked renders travel — redactions are
//     permanent, un-redacted originals never leave the machine.
//   - full: the un-redacted originals travel too, for complete re-editing
//     (recoverable redactions — an explicit opt-in with a UI warning).
// Import treats the zip as UNTRUSTED input: size caps, image magic-byte checks,
// a folder whitelist, and per-file path confinement (zip-slip safe).
import { promises as fs } from 'node:fs';
import path from 'node:path';
import { shell } from 'electron';
import JSZip from 'jszip';
import type { ProjectManifest, ProjectSummary } from '../shared/project';
import type { PackageResult } from '../shared/ipc';
import {
  getProjectForRead,
  createProjectFromImport,
  coerceManifest,
} from './project-store';
import { confinePath } from './path-confine';
import { resolveSendableRender } from './render-gate';
import { safeFileBase, nextAvailableStem } from './export';
import { mainLog } from './logger';

const PKG_MARKER = 'shotai-package.json';
const PKG_FORMAT = 'shotai-package';
const PKG_VERSION = 1;
// Untrusted-input guards for import.
const MAX_PKG_BYTES = 600 * 1024 * 1024; // whole .zip
const MAX_FILE_BYTES = 80 * 1024 * 1024; // any single extracted image

function isPngOrJpeg(buf: Buffer): boolean {
  if (buf.length < 4) return false;
  const png = buf[0] === 0x89 && buf[1] === 0x50 && buf[2] === 0x4e && buf[3] === 0x47;
  const jpeg = buf[0] === 0xff && buf[1] === 0xd8 && buf[2] === 0xff;
  return png || jpeg;
}

/** Add a manifest-relative file to the zip at the same relative path (confined). */
async function addFileRef(zip: JSZip, dir: string, rel: string): Promise<void> {
  const abs = confinePath(dir, rel);
  if (!abs) return;
  try {
    const bytes = await fs.readFile(abs);
    zip.file(rel.replace(/\\/g, '/'), bytes);
  } catch {
    /* referenced file missing on disk — skip (best-effort) */
  }
}

/**
 * Export a shareable package. The renderer is expected to have flattened all shot
 * steps first (same as other exports), so every shot has a current redaction- and
 * marker-baked render.
 */
export async function exportPackage(
  projectPath: string,
  includeOriginals: boolean,
): Promise<PackageResult> {
  const { dir, manifest } = await getProjectForRead(projectPath);
  if (manifest.steps.length === 0) {
    throw new Error('This project has nothing to export yet — add a step first.');
  }

  const zip = new JSZip();
  // Work on a clone; never share the sender's local revert history.
  const out: ProjectManifest = JSON.parse(JSON.stringify(manifest));
  out.sopBackup = null;

  if (includeOriginals) {
    // Full fidelity: ship exactly the files the manifest references (originals +
    // baked renders), so the imported project is a faithful, fully-editable clone.
    for (const step of out.steps) {
      if (step.kind === 'text') continue;
      if (step.screenshot) await addFileRef(zip, dir, step.screenshot);
      if (step.flattened) await addFileRef(zip, dir, step.flattened);
    }
  } else {
    // Safe: collapse each shot to its SENDABLE (redaction-baked) render, which
    // becomes the new base image. No original pixels, no editable vector state
    // that references baked content. Fail-closed via resolveSendableRender.
    let n = 0;
    const shots = zip.folder('shots');
    if (!shots) throw new Error('Failed to assemble the package.');
    for (const step of out.steps) {
      if (step.kind === 'text') continue;
      n += 1;
      const { abs, ext } = resolveSendableRender(dir, step, `Step ${n}`, 'export');
      const bytes = await fs.readFile(abs);
      const name = `step-${String(n).padStart(4, '0')}${ext.startsWith('.') ? ext : `.${ext}`}`;
      shots.file(name, bytes);
      step.screenshot = `shots/${name}`;
      step.annotations = [];
      step.crop = null;
      step.click = null; // the click ring is baked into the render; no overlay
      step.flattened = undefined;
      step.renderRev = 0;
      step.markerBaked = false;
    }
  }

  zip.file('project.json', JSON.stringify(out, null, 2));
  zip.file(
    PKG_MARKER,
    JSON.stringify(
      {
        format: PKG_FORMAT,
        version: PKG_VERSION,
        app: 'shotAI',
        includeOriginals,
        exportedAt: new Date().toISOString(),
      },
      null,
      2,
    ),
  );

  const buf = await zip.generateAsync({ type: 'nodebuffer', compression: 'DEFLATE' });
  const exportDir = path.join(dir, 'export');
  await fs.mkdir(exportDir, { recursive: true });
  const base = safeFileBase(manifest.title);
  const stem = await nextAvailableStem(exportDir, `${base} (shotAI package)`, '.zip');
  const outputPath = path.join(exportDir, `${stem}.zip`);
  await fs.writeFile(outputPath, buf);
  mainLog.info(`exported package (originals=${includeOriginals}) → ${outputPath}`);
  shell.showItemInFolder(outputPath);
  return { outputPath, includeOriginals };
}

/**
 * Import a project package from an absolute .zip path (the caller picked it). The
 * zip is UNTRUSTED: enforce size caps, validate the marker + manifest, whitelist
 * image entries by folder + magic bytes, and let createProjectFromImport confine
 * every extracted path to the new project folder.
 */
export async function importPackage(zipPath: string): Promise<ProjectSummary> {
  const stat = await fs.stat(zipPath);
  if (stat.size > MAX_PKG_BYTES) {
    throw new Error('This package is too large to import.');
  }
  const bytes = await fs.readFile(zipPath);
  let zip: JSZip;
  try {
    zip = await JSZip.loadAsync(bytes);
  } catch {
    throw new Error('This file is not a valid .zip package.');
  }

  const markerEntry = zip.file(PKG_MARKER);
  if (!markerEntry) {
    throw new Error('This file is not a shotAI project package (missing marker).');
  }
  let marker: { format?: unknown; version?: unknown };
  try {
    marker = JSON.parse(await markerEntry.async('string'));
  } catch {
    throw new Error('The package marker is corrupt.');
  }
  if (marker.format !== PKG_FORMAT) {
    throw new Error('Unrecognized package format.');
  }
  if (typeof marker.version === 'number' && marker.version > PKG_VERSION) {
    throw new Error('This package was created by a newer version of shotAI. Update to import it.');
  }

  const manifestEntry = zip.file('project.json');
  if (!manifestEntry) {
    throw new Error('The package is missing its project.json.');
  }
  let parsed: unknown;
  try {
    parsed = JSON.parse(await manifestEntry.async('string'));
  } catch {
    throw new Error('The package project.json is corrupt.');
  }
  const manifest = coerceManifest(parsed as Partial<ProjectManifest>, 'Imported project');

  // Collect image entries, whitelisting by folder and validating magic bytes.
  const files: { rel: string; bytes: Buffer }[] = [];
  let total = 0;
  for (const entry of Object.values(zip.files)) {
    if (entry.dir) continue;
    const rel = entry.name.replace(/\\/g, '/');
    if (rel === PKG_MARKER || rel === 'project.json') continue;
    // Only images under shots/ or export/.render/ (a single path segment deep).
    if (!/^shots\/[^/]+$/.test(rel) && !/^export\/\.render\/[^/]+$/.test(rel)) {
      continue; // ignore anything unexpected (never extract it)
    }
    const buf = await entry.async('nodebuffer');
    if (buf.length > MAX_FILE_BYTES) {
      throw new Error(`A file in the package is too large: ${rel}`);
    }
    total += buf.length;
    if (total > MAX_PKG_BYTES) {
      throw new Error('The package contents exceed the size limit.');
    }
    if (!isPngOrJpeg(buf)) {
      throw new Error(`The package contains a non-image file where an image was expected: ${rel}`);
    }
    files.push({ rel, bytes: buf });
  }

  return createProjectFromImport(manifest, files);
}
