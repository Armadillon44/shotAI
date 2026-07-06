// ProjectStore — creates, opens, and lists shotAI projects on disk.
// Each project is a discrete, self-contained folder (project.json + shots/ +
// export/) under the user-chosen projects directory.
import { promises as fs } from 'node:fs';
import path from 'node:path';
import { randomUUID } from 'node:crypto';
import { shell } from 'electron';
import {
  PROJECT_SCHEMA_VERSION,
  type CalloutKind,
  type ProjectManifest,
  type ProjectStep,
  type ProjectSummary,
  type SopBackup,
  type SopIntro,
  type StepPatch,
} from '../shared/project';
import { DEFAULT_SOP_TONE, isSopTone } from '../shared/sop';
import {
  addRecent,
  getProjectsDir,
  getRecents,
  persistProjectsDir,
  setRecents,
} from './settings';
import { confinePath } from './path-confine';
import { applyPatchAndInvalidate, writeStepRender } from './step-render';
import { writeFileAtomic } from './atomic-write';

const MANIFEST = 'project.json';

export { getProjectsDir };
// Re-export the path-confinement boundary so existing `from './project-store'`
// callers (ClaudeService, export, ipc) keep a stable import while the impl lives
// in the dependency-free path-confine module (unit-testable without electron).
export { confinePath };

/** Change the projects directory (creating it if needed) and persist it. */
export async function setProjectsDir(dir: string): Promise<void> {
  await fs.mkdir(dir, { recursive: true });
  await persistProjectsDir(dir);
}

/**
 * Allow opening a project only if its path is inside the current projects root
 * OR is a path the app itself recorded in recents (e.g. under a previous root).
 * Blocks a compromised renderer from reading arbitrary files via openProject.
 */
async function resolveKnownProject(projectPath: string): Promise<string> {
  const resolved = path.resolve(projectPath);
  const root = path.resolve(await getProjectsDir());
  const rel = path.relative(root, resolved);
  const underRoot = rel !== '' && !rel.startsWith('..') && !path.isAbsolute(rel);
  if (underRoot) return resolved;
  const recents = await getRecents();
  if (recents.some((r) => path.resolve(r) === resolved)) return resolved;
  throw new Error('Project path is not within the projects directory');
}

/** Ensure each step has the fields the editor relies on (defensive on read).
 *  Exported for sop-apply (revertSop normalizes the restored snapshot). */
export function normalizeSteps(steps: unknown): ProjectStep[] {
  if (!Array.isArray(steps)) return [];
  return steps.map((s) => {
    const step = s as ProjectStep;
    return {
      ...step,
      annotations: Array.isArray(step.annotations) ? step.annotations : [],
    };
  });
}

/** Coerce a persisted SOP intro (or null) — a preamble, not a step. */
function coerceIntro(raw: unknown): SopIntro | null {
  if (!raw || typeof raw !== 'object') return null;
  const r = raw as Record<string, unknown>;
  const heading = typeof r.heading === 'string' ? r.heading : '';
  const body = typeof r.body === 'string' ? r.body : '';
  if (!heading && !body) return null;
  return { heading, body };
}

/** Coerce a persisted SOP backup (or null) from a possibly-corrupt manifest. */
function coerceSopBackup(raw: unknown): SopBackup | null {
  if (!raw || typeof raw !== 'object') return null;
  const r = raw as Record<string, unknown>;
  if (!Array.isArray(r.steps) || typeof r.title !== 'string') return null;
  return {
    steps: normalizeSteps(r.steps),
    title: r.title,
    intro: coerceIntro(r.intro),
    model: typeof r.model === 'string' ? r.model : '',
    tone: isSopTone(r.tone) ? r.tone : DEFAULT_SOP_TONE,
    at: typeof r.at === 'string' ? r.at : '',
  };
}

/**
 * Coerce an untrusted/partial manifest object into a valid ProjectManifest,
 * defaulting any missing/corrupt field. Shared by readManifest (disk) and the
 * package importer (untrusted zip) so both validate identically.
 */
export function coerceManifest(
  parsed: Partial<ProjectManifest>,
  fallbackTitle: string,
): ProjectManifest {
  return {
    version:
      typeof parsed.version === 'number'
        ? parsed.version
        : PROJECT_SCHEMA_VERSION,
    id: typeof parsed.id === 'string' ? parsed.id : '',
    title:
      typeof parsed.title === 'string' && parsed.title ? parsed.title : fallbackTitle,
    createdWith: 'shotAI',
    createdAt: typeof parsed.createdAt === 'string' ? parsed.createdAt : '',
    updatedAt: typeof parsed.updatedAt === 'string' ? parsed.updatedAt : '',
    captureSettings: parsed.captureSettings ?? null,
    steps: normalizeSteps(parsed.steps),
    intro: coerceIntro(parsed.intro),
    sopBackup: coerceSopBackup(parsed.sopBackup),
  };
}

/** Read + validate a project manifest, defaulting any missing/corrupt fields. */
async function readManifest(projectPath: string): Promise<ProjectManifest> {
  const raw = await fs.readFile(path.join(projectPath, MANIFEST), 'utf8');
  const parsed = JSON.parse(raw) as Partial<ProjectManifest>;
  return coerceManifest(parsed, path.basename(projectPath));
}

async function writeManifest(
  projectPath: string,
  manifest: ProjectManifest,
): Promise<void> {
  // Atomic (tmp + rename) so a crash/power-loss mid-write can't corrupt the
  // project's manifest — it's the highest-churn user-data file (every capture/
  // edit/SOP-apply rewrites it). Same helper settings.ts/secrets.ts already use.
  await writeFileAtomic(path.join(projectPath, MANIFEST), JSON.stringify(manifest, null, 2));
}

function summarize(
  manifest: ProjectManifest,
  projectPath: string,
): ProjectSummary {
  return {
    id: manifest.id,
    title: manifest.title,
    path: projectPath,
    createdAt: manifest.createdAt,
    updatedAt: manifest.updatedAt,
    stepCount: manifest.steps.length,
  };
}

/** Default project title when the user doesn't name one:
 *  "Project yyyy/MM/dd HH:mm:ss" (local time). */
function defaultTitle(): string {
  const d = new Date();
  const p = (n: number) => String(n).padStart(2, '0');
  return (
    `Project ${d.getFullYear()}/${p(d.getMonth() + 1)}/${p(d.getDate())} ` +
    `${p(d.getHours())}:${p(d.getMinutes())}:${p(d.getSeconds())}`
  );
}

/** Create a new, empty project folder and write its v1 manifest. An empty title
 *  gets a timestamped default ("Project yyyy/MM/dd HH:mm:ss"). */
export async function createProject(title?: string): Promise<ProjectSummary> {
  const root = await getProjectsDir();
  await fs.mkdir(root, { recursive: true });

  const id = randomUUID();
  const name = (title ?? '').trim() || defaultTitle();
  // The folder is named by the project's UUID — opaque, collision-free, and
  // immutable. The human title lives only in the manifest, so two projects can
  // share a display name and renaming never moves the folder.
  const dir = path.join(root, id);
  await fs.mkdir(path.join(dir, 'shots'), { recursive: true });
  await fs.mkdir(path.join(dir, 'export'), { recursive: true });

  const now = new Date().toISOString();
  const manifest: ProjectManifest = {
    version: PROJECT_SCHEMA_VERSION,
    id,
    title: name,
    createdWith: 'shotAI',
    createdAt: now,
    updatedAt: now,
    captureSettings: null,
    steps: [],
    intro: null,
    sopBackup: null,
  };
  await writeManifest(dir, manifest); // atomic, same as every other manifest write

  await addRecent(dir);
  return summarize(manifest, dir);
}

/**
 * Materialize an imported project package into a NEW project folder (a fresh
 * UUID, so it never collides with the sender's). Files come from an untrusted
 * zip, so each is CONFINED to the new folder and WHITELISTED to `shots/` or
 * `export/.render/` — anything else (including path-traversal names) is refused.
 * The manifest is re-stamped with the new id and a fresh sopBackup=null (the
 * sender's local revert history isn't shared).
 */
export async function createProjectFromImport(
  manifest: ProjectManifest,
  files: { rel: string; bytes: Buffer }[],
): Promise<ProjectSummary> {
  const root = await getProjectsDir();
  await fs.mkdir(root, { recursive: true });
  const id = randomUUID();
  const dir = path.join(root, id);
  await fs.mkdir(path.join(dir, 'shots'), { recursive: true });
  await fs.mkdir(path.join(dir, 'export'), { recursive: true });

  for (const f of files) {
    const rel = f.rel.replace(/\\/g, '/');
    // Whitelist the only two folders a package legitimately carries images in.
    if (!/^shots\/[^/]+$/.test(rel) && !/^export\/\.render\/[^/]+$/.test(rel)) {
      throw new Error(`Package contains an unexpected file path: ${f.rel}`);
    }
    const abs = confinePath(dir, rel); // defense-in-depth against zip-slip
    if (!abs) throw new Error(`Refusing to extract a path outside the project: ${f.rel}`);
    await fs.mkdir(path.dirname(abs), { recursive: true });
    await fs.writeFile(abs, f.bytes, { flag: 'wx' }); // wx: never overwrite in a fresh dir
  }

  const now = new Date().toISOString();
  manifest.id = id;
  manifest.createdWith = 'shotAI';
  if (!manifest.createdAt) manifest.createdAt = now;
  manifest.updatedAt = now;
  manifest.sopBackup = null;
  await writeManifest(dir, manifest);
  await addRecent(dir);
  return summarize(manifest, dir);
}

/** Open: resolve + confine the path, read the manifest, mark recently opened. */
async function loadProject(
  projectPath: string,
): Promise<{ resolved: string; manifest: ProjectManifest }> {
  const resolved = await resolveKnownProject(projectPath);
  const manifest = await readManifest(resolved);
  // Back-fill a stable id for older projects, persisted once on open.
  if (!manifest.id) {
    manifest.id = randomUUID();
    await writeManifest(resolved, manifest).catch(() => undefined);
  }
  await addRecent(resolved);
  return { resolved, manifest };
}

/** Read an existing project's manifest and mark it recently opened. */
export async function openProject(
  projectPath: string,
): Promise<ProjectManifest> {
  return (await loadProject(projectPath)).manifest;
}

// Session-scoped registry mapping an opaque id → a project's absolute folder.
// The renderer references projects (and their shot images) by this id, never by
// a filesystem path, so a compromised renderer can't point the shot:// protocol
// at arbitrary files. Ids are stable per folder within a session.
const idToDir = new Map<string, string>();
const dirToId = new Map<string, string>();

function registerProject(absDir: string): string {
  const existing = dirToId.get(absDir);
  if (existing) return existing;
  const id = randomUUID();
  idToDir.set(id, absDir);
  dirToId.set(absDir, id);
  return id;
}

/**
 * Open a project for the renderer: same as openProject, plus an opaque id the
 * renderer uses to build shot:// URLs (see resolveProjectFile).
 */
export async function openProjectWithId(
  projectPath: string,
): Promise<{ projectId: string; manifest: ProjectManifest }> {
  const { resolved, manifest } = await loadProject(projectPath);
  return { projectId: registerProject(resolved), manifest };
}

/**
 * Resolve a project-relative file path for the shot:// protocol, confined to
 * the registered project's folder. Returns the absolute path, or null if the id
 * is unknown or the path escapes the folder. Caller still checks the extension.
 */
export function resolveProjectFile(
  projectId: string,
  rel: string,
): string | null {
  const dir = idToDir.get(projectId);
  if (!dir) return null;
  return confinePath(dir, rel);
}

/** Recent projects, most-recently-touched first; prunes entries gone from disk. */
export async function listRecentProjects(): Promise<ProjectSummary[]> {
  const recents = await getRecents();
  const summaries: ProjectSummary[] = [];
  const stillValid: string[] = [];

  for (const projectPath of recents) {
    try {
      const manifest = await readManifest(projectPath);
      summaries.push(summarize(manifest, projectPath));
      stillValid.push(projectPath);
    } catch {
      // Folder moved/deleted or manifest unreadable — drop it from recents.
    }
  }

  if (stillValid.length !== recents.length) {
    await setRecents(stillValid);
  }

  // `recents` is already most-recently-touched-first; preserve that order.
  return summaries;
}

/** All projects for the home screen: every subfolder of the current projects
 *  root with a valid manifest, PLUS any still-valid recents outside that root
 *  (so changing the projects directory doesn't hide previously-created
 *  projects). Deduped by resolved path; unsorted — the UI sorts. */
export async function listProjects(): Promise<ProjectSummary[]> {
  const root = path.resolve(await getProjectsDir());
  const summaries: ProjectSummary[] = [];
  const seen = new Set<string>();

  let names: string[] = [];
  try {
    const entries = await fs.readdir(root, { withFileTypes: true });
    names = entries.filter((e) => e.isDirectory()).map((e) => e.name);
  } catch {
    /* projects root missing — fall through to recents */
  }
  for (const name of names) {
    const dir = path.join(root, name);
    try {
      summaries.push(summarize(await readManifest(dir), dir));
      seen.add(path.resolve(dir));
    } catch {
      // not a project folder (no/invalid manifest) — skip
    }
  }

  // Recents recorded under a previous root (or otherwise outside the current
  // one) — include them so they stay reachable on the home screen.
  for (const recent of await getRecents()) {
    const abs = path.resolve(recent);
    if (seen.has(abs)) continue;
    try {
      summaries.push(summarize(await readManifest(abs), abs));
      seen.add(abs);
    } catch {
      // gone / unreadable — skip (listRecentProjects prunes these elsewhere)
    }
  }

  return summaries;
}

/** Rename a project (title only — the folder is left in place so the path,
 *  recents, and any open references stay valid). Serialized via the writeQueue.
 *  Returns the updated summary. */
export function renameProject(
  projectPath: string,
  title: string,
): Promise<ProjectSummary> {
  const run = writeQueue.then(async () => {
    const resolved = await resolveKnownProject(projectPath);
    const manifest = await readManifest(resolved);
    if (!manifest.id) manifest.id = randomUUID();
    manifest.title = title.trim() || defaultTitle();
    manifest.updatedAt = new Date().toISOString();
    await writeManifest(resolved, manifest);
    return summarize(manifest, resolved);
  });
  writeQueue = run.then(
    () => undefined,
    () => undefined,
  );
  return run;
}

/** Reveal a project's folder in the OS file manager (Explorer/Finder). Confined
 *  to a known project so the renderer can't reveal arbitrary paths. */
export async function revealProject(projectPath: string): Promise<void> {
  const resolved = await resolveKnownProject(projectPath);
  shell.showItemInFolder(resolved);
}

/** Delete a project: remove its folder (originals included) and drop it from
 *  recents + the session id registry. Confined to a known project. */
export async function deleteProject(projectPath: string): Promise<void> {
  const resolved = await resolveKnownProject(projectPath);
  await fs.rm(resolved, { recursive: true, force: true });
  const recents = await getRecents();
  const pruned = recents.filter((r) => path.resolve(r) !== resolved);
  if (pruned.length !== recents.length) await setRecents(pruned);
  const id = dirToId.get(resolved);
  if (id) {
    idToDir.delete(id);
    dirToId.delete(resolved);
  }
}

// Serialize manifest writes so rapid captures can't interleave read-modify-write.
let writeQueue: Promise<unknown> = Promise.resolve();

/**
 * Run a read-modify-write against a project's manifest inside the shared
 * writeQueue (so captures / edits / SOP-applies can't interleave). Confines the
 * path, reads the manifest, runs `fn` (which mutates it in place — may throw to
 * abort without writing), bumps updatedAt, writes atomically, and returns the
 * manifest. Lets feature modules (e.g. sop-apply) mutate the manifest without
 * re-implementing the queue + IO (and without reaching the private helpers).
 */
export function mutate(
  projectPath: string,
  fn: (manifest: ProjectManifest) => void | Promise<void>,
): Promise<ProjectManifest> {
  const run = writeQueue.then(async () => {
    const resolved = await resolveKnownProject(projectPath);
    const manifest = await readManifest(resolved);
    await fn(manifest);
    manifest.updatedAt = new Date().toISOString();
    await writeManifest(resolved, manifest);
    return manifest;
  });
  writeQueue = run.then(
    () => undefined,
    () => undefined,
  );
  return run;
}

/** Set (or clear, with a null/empty value) the SOP overview preamble (E8). The
 *  intro is coerced from untrusted (IPC) input — a bad shape becomes null. */
export function setProjectIntro(
  projectPath: string,
  intro: unknown,
): Promise<ProjectManifest> {
  const clean = coerceIntro(intro);
  return mutate(projectPath, (manifest) => {
    manifest.intro = clean;
  });
}

/** Append a captured step to a project's manifest (serialized, atomic-ish). */
export function addStep(projectPath: string, step: ProjectStep): Promise<void> {
  const run = writeQueue.then(async () => {
    const resolved = await resolveKnownProject(projectPath);
    const manifest = await readManifest(resolved);
    manifest.steps.push(step);
    // step.order tracks array position, not the capture filename counter (which
    // climbs past orphaned step-NNNN.png files left by deletes). Without this,
    // resuming capture after a delete renders a gap like 1, 2, 3, 6.
    renumber(manifest.steps);
    manifest.updatedAt = new Date().toISOString();
    await writeManifest(resolved, manifest);
  });
  writeQueue = run.then(
    () => undefined,
    () => undefined,
  );
  return run;
}

/**
 * Apply an editor patch to one step (serialized through the same writeQueue as
 * captures, so a live recording can't race an edit). If `flattenedPng` is given,
 * write it into the project's render-cache and point step.flattened at it. The
 * path is confined to a known project. Returns the updated manifest.
 */
export function updateStep(
  projectPath: string,
  stepId: string,
  patch: StepPatch,
  flattenedPng?: Buffer | null,
): Promise<ProjectManifest> {
  const run = writeQueue.then(async () => {
    const resolved = await resolveKnownProject(projectPath);
    const manifest = await readManifest(resolved);
    const step = manifest.steps.find((s) => s.id === stepId);
    if (!step) throw new Error(`step ${stepId} not found`);
    applyPatchAndInvalidate(step, patch, !!(flattenedPng && flattenedPng.length));
    if (flattenedPng && flattenedPng.length) {
      await writeStepRender(resolved, step, stepId, flattenedPng);
    }
    manifest.updatedAt = new Date().toISOString();
    await writeManifest(resolved, manifest);
    return manifest;
  });
  // Keep the chain alive even if one write rejects.
  writeQueue = run.then(
    () => undefined,
    () => undefined,
  );
  return run;
}

/**
 * Merge two steps into one: apply `patch` (+ optional re-baked render) to the
 * KEPT step, then delete the DROPPED step, then renumber — all in a single
 * writeQueue task so the pair can't be observed half-merged. Used by the report
 * to fold a right-click step into its menu-selection step: the selection
 * screenshot (which shows the open menu) is kept, and the right-click's click is
 * carried in as a marker annotation (baked into `flattenedPng`). The merged step
 * stays at the DROPPED step's position (the flow reads in the original order).
 */
export function mergeSteps(
  projectPath: string,
  keepId: string,
  dropId: string,
  patch: StepPatch,
  flattenedPng?: Buffer | null,
): Promise<ProjectManifest> {
  const run = writeQueue.then(async () => {
    if (keepId === dropId) throw new Error('cannot merge a step into itself');
    const resolved = await resolveKnownProject(projectPath);
    const manifest = await readManifest(resolved);
    const keep = manifest.steps.find((s) => s.id === keepId);
    if (!keep) throw new Error(`step ${keepId} not found`);
    const dropIdx = manifest.steps.findIndex((s) => s.id === dropId);
    if (dropIdx === -1) throw new Error(`step ${dropId} not found`);

    // Same fresh-render-or-invalidate rule as updateStep (S3): a merge patch that
    // changes annotations/crop without co-sending a re-baked PNG must drop the
    // stale render so an unbaked redaction can't ride a stale flattened to egress.
    applyPatchAndInvalidate(keep, patch, !!(flattenedPng && flattenedPng.length));
    if (flattenedPng && flattenedPng.length) {
      await writeStepRender(resolved, keep, keepId, flattenedPng);
    }
    manifest.steps.splice(dropIdx, 1);
    renumber(manifest.steps);
    manifest.updatedAt = new Date().toISOString();
    await writeManifest(resolved, manifest);
    return manifest;
  });
  writeQueue = run.then(
    () => undefined,
    () => undefined,
  );
  return run;
}

/** Reassign step.order to 1..N in array order. Exported for sop-apply. */
export function renumber(steps: ProjectStep[]): void {
  steps.forEach((s, i) => {
    s.order = i + 1;
  });
}

/** Remove a step from the manifest and renumber. Leaves its files on disk
 *  (originals are preserved). Serialized via the writeQueue. */
export function deleteStep(
  projectPath: string,
  stepId: string,
): Promise<ProjectManifest> {
  const run = writeQueue.then(async () => {
    const resolved = await resolveKnownProject(projectPath);
    const manifest = await readManifest(resolved);
    const idx = manifest.steps.findIndex((s) => s.id === stepId);
    if (idx === -1) throw new Error(`step ${stepId} not found`);
    manifest.steps.splice(idx, 1);
    renumber(manifest.steps);
    manifest.updatedAt = new Date().toISOString();
    await writeManifest(resolved, manifest);
    return manifest;
  });
  writeQueue = run.then(
    () => undefined,
    () => undefined,
  );
  return run;
}

/** Delete multiple steps by id (e.g. discarding a capture session's additions):
 *  removes them from the manifest, renumbers, and best-effort deletes each one's
 *  screenshot + flattened render from disk. Serialized via the writeQueue. */
export function deleteSteps(
  projectPath: string,
  stepIds: string[],
): Promise<ProjectManifest> {
  const run = writeQueue.then(async () => {
    const resolved = await resolveKnownProject(projectPath);
    const manifest = await readManifest(resolved);
    const idSet = new Set(stepIds);
    const removed = manifest.steps.filter((s) => idSet.has(s.id));
    manifest.steps = manifest.steps.filter((s) => !idSet.has(s.id));
    renumber(manifest.steps);
    manifest.updatedAt = new Date().toISOString();
    await writeManifest(resolved, manifest);
    for (const s of removed) {
      for (const rel of [s.screenshot, s.flattened]) {
        if (!rel) continue;
        // Confine: a manifest-sourced path must stay inside the project folder
        // before we rm it (defends against a hand-edited traversal path).
        const abs = confinePath(resolved, rel);
        if (!abs) continue;
        await fs.rm(abs, { force: true }).catch(() => undefined);
      }
    }
    return manifest;
  });
  writeQueue = run.then(
    () => undefined,
    () => undefined,
  );
  return run;
}

/** Reorder steps to match `orderedIds` (any unmentioned steps keep their
 *  relative order at the end), then renumber. Serialized via the writeQueue. */
export function reorderSteps(
  projectPath: string,
  orderedIds: string[],
): Promise<ProjectManifest> {
  const run = writeQueue.then(async () => {
    const resolved = await resolveKnownProject(projectPath);
    const manifest = await readManifest(resolved);
    const byId = new Map(manifest.steps.map((s) => [s.id, s]));
    const reordered: ProjectStep[] = [];
    for (const id of orderedIds) {
      const s = byId.get(id);
      if (s) {
        reordered.push(s);
        byId.delete(id);
      }
    }
    for (const s of manifest.steps) if (byId.has(s.id)) reordered.push(s);
    manifest.steps = reordered;
    renumber(manifest.steps);
    manifest.updatedAt = new Date().toISOString();
    await writeManifest(resolved, manifest);
    return manifest;
  });
  writeQueue = run.then(
    () => undefined,
    () => undefined,
  );
  return run;
}

/** Insert an empty text step at `atIndex` (clamped) and renumber. When `callout`
 *  is given, the text step renders as a colored note/caution/warning box. */
export function addTextStep(
  projectPath: string,
  atIndex: number,
  callout?: CalloutKind,
): Promise<ProjectManifest> {
  const run = writeQueue.then(async () => {
    const resolved = await resolveKnownProject(projectPath);
    const manifest = await readManifest(resolved);
    const step: ProjectStep = {
      id: randomUUID(),
      order: 0,
      kind: 'text',
      screenshot: '',
      trigger: 'hotkey',
      click: null,
      monitor: null,
      window: null,
      element: { available: false, name: null, controlType: null, bounds: null },
      caption: '',
      note: '',
      heading: '',
      body: '',
      ...(callout ? { callout } : {}),
      crop: null,
      annotations: [],
    };
    const i = Math.max(0, Math.min(Math.round(atIndex), manifest.steps.length));
    manifest.steps.splice(i, 0, step);
    renumber(manifest.steps);
    manifest.updatedAt = new Date().toISOString();
    await writeManifest(resolved, manifest);
    return manifest;
  });
  writeQueue = run.then(
    () => undefined,
    () => undefined,
  );
  return run;
}

/**
 * Main-side read for the Claude/export pipelines: resolve + confine the project
 * path and read its manifest. Returns the absolute folder + manifest so callers
 * can read per-step image bytes (via path.join(dir, step.flattened|screenshot)).
 */
export async function getProjectForRead(
  projectPath: string,
): Promise<{ dir: string; manifest: ProjectManifest }> {
  const resolved = await resolveKnownProject(projectPath);
  const manifest = await readManifest(resolved);
  return { dir: resolved, manifest };
}

/** Detect a supported image by its magic bytes (don't trust the extension). */
function detectImage(bytes: Buffer): 'png' | 'jpg' | null {
  if (
    bytes.length >= 8 &&
    bytes[0] === 0x89 &&
    bytes[1] === 0x50 &&
    bytes[2] === 0x4e &&
    bytes[3] === 0x47
  ) {
    return 'png';
  }
  if (bytes.length >= 3 && bytes[0] === 0xff && bytes[1] === 0xd8 && bytes[2] === 0xff) {
    return 'jpg';
  }
  return null;
}

/**
 * Insert an already-built step at `atIndex` (clamped; null/undefined → append),
 * then renumber. Serialized via the writeQueue. Used by the single-shot capture
 * path to drop a recorded screenshot at a chosen position.
 */
export function insertStepAt(
  projectPath: string,
  step: ProjectStep,
  atIndex?: number | null,
): Promise<void> {
  const run = writeQueue.then(async () => {
    const resolved = await resolveKnownProject(projectPath);
    const manifest = await readManifest(resolved);
    const i =
      atIndex == null
        ? manifest.steps.length
        : Math.max(0, Math.min(Math.round(atIndex), manifest.steps.length));
    manifest.steps.splice(i, 0, step);
    renumber(manifest.steps);
    manifest.updatedAt = new Date().toISOString();
    await writeManifest(resolved, manifest);
  });
  writeQueue = run.then(
    () => undefined,
    () => undefined,
  );
  return run;
}

/**
 * Import a user-supplied image as a new screenshot step. Validates the bytes are
 * actually a PNG/JPEG (magic bytes, not the extension) and writes them into
 * shots/ with a non-colliding name. Inserts at `atIndex` (clamped; null → append)
 * and renumbers. Serialized via the writeQueue.
 */
export function importStep(
  projectPath: string,
  bytes: Buffer,
  atIndex?: number | null,
): Promise<ProjectManifest> {
  const run = writeQueue.then(async () => {
    const kind = detectImage(bytes);
    if (!kind) {
      throw new Error('Unsupported file — please choose a PNG or JPEG image.');
    }
    const resolved = await resolveKnownProject(projectPath);
    const manifest = await readManifest(resolved);
    const shotsDir = path.join(resolved, 'shots');
    await fs.mkdir(shotsDir, { recursive: true });

    // Next free step-NNNN past existing files + manifest, so we never overwrite.
    let maxFile = manifest.steps.length;
    try {
      for (const f of await fs.readdir(shotsDir)) {
        const m = /^step-(\d+)\./i.exec(f);
        if (m) maxFile = Math.max(maxFile, Number(m[1]));
      }
    } catch {
      /* shots/ unreadable — fall back to manifest length */
    }
    const filename = `step-${String(maxFile + 1).padStart(4, '0')}.${kind}`;
    await fs.writeFile(path.join(shotsDir, filename), bytes, { flag: 'wx' });

    const step: ProjectStep = {
      id: randomUUID(),
      order: 0, // renumber() assigns the real position below
      screenshot: `shots/${filename}`,
      trigger: 'hotkey',
      click: null,
      monitor: null,
      window: null,
      element: { available: false, name: null, controlType: null, bounds: null },
      caption: 'Imported screenshot',
      note: '',
      crop: null,
      annotations: [],
    };
    const i =
      atIndex == null
        ? manifest.steps.length
        : Math.max(0, Math.min(Math.round(atIndex), manifest.steps.length));
    manifest.steps.splice(i, 0, step);
    renumber(manifest.steps);
    manifest.updatedAt = new Date().toISOString();
    await writeManifest(resolved, manifest);
    return manifest;
  });
  writeQueue = run.then(
    () => undefined,
    () => undefined,
  );
  return run;
}
