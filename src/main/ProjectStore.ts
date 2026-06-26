// ProjectStore — creates, opens, and lists shotAI projects on disk.
// Each project is a discrete, self-contained folder (project.json + shots/ +
// export/) under the user-chosen projects directory.
import { promises as fs } from 'node:fs';
import path from 'node:path';
import { randomUUID } from 'node:crypto';
import {
  PROJECT_SCHEMA_VERSION,
  type ProjectManifest,
  type ProjectStep,
  type ProjectSummary,
  type SopBackup,
  type StepPatch,
} from '../shared/project';
import {
  DEFAULT_SOP_TONE,
  isSopTone,
  type SopEditPlan,
  type SopTone,
} from '../shared/sop';
import {
  addRecent,
  getProjectsDir,
  getRecents,
  persistProjectsDir,
  setRecents,
} from './settings';

const MANIFEST = 'project.json';
// Characters not allowed in Windows/macOS file names.
const RESERVED_CHARS = '<>:"/\\|?*';
// Windows reserved device names that can't be used as folder names.
const RESERVED_NAME = /^(con|prn|aux|nul|com[1-9]|lpt[1-9])$/i;

export { getProjectsDir };

/** Change the projects directory (creating it if needed) and persist it. */
export async function setProjectsDir(dir: string): Promise<void> {
  await fs.mkdir(dir, { recursive: true });
  await persistProjectsDir(dir);
}

/** Turn a project title into a safe Windows/macOS folder name. */
function toFolderName(title: string): string {
  const cleaned = Array.from(title)
    // Drop control chars (code point <= 0x1F) and filesystem-reserved chars;
    // keep spaces and hyphens so "Invoice Flow - Q1" stays readable.
    .filter(
      (ch) => (ch.codePointAt(0) ?? 0) > 0x1f && !RESERVED_CHARS.includes(ch),
    )
    .join('')
    .replace(/\s+/g, ' ')
    .trim()
    .replace(/[.\s]+$/, ''); // Windows: no trailing dot/space
  if (!cleaned) return 'Untitled Project';
  // Avoid reserved device names and leading-dot (hidden) folders.
  if (RESERVED_NAME.test(cleaned) || cleaned.startsWith('.')) {
    return `_${cleaned}`;
  }
  return cleaned;
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

/** Ensure each step has the fields the editor relies on (defensive on read). */
function normalizeSteps(steps: unknown): ProjectStep[] {
  if (!Array.isArray(steps)) return [];
  return steps.map((s) => {
    const step = s as ProjectStep;
    return {
      ...step,
      annotations: Array.isArray(step.annotations) ? step.annotations : [],
    };
  });
}

/** Coerce a persisted SOP backup (or null) from a possibly-corrupt manifest. */
function coerceSopBackup(raw: unknown): SopBackup | null {
  if (!raw || typeof raw !== 'object') return null;
  const r = raw as Record<string, unknown>;
  if (!Array.isArray(r.steps) || typeof r.title !== 'string') return null;
  return {
    steps: normalizeSteps(r.steps),
    title: r.title,
    model: typeof r.model === 'string' ? r.model : '',
    tone: isSopTone(r.tone) ? r.tone : DEFAULT_SOP_TONE,
    at: typeof r.at === 'string' ? r.at : '',
  };
}

/** Read + validate a project manifest, defaulting any missing/corrupt fields. */
async function readManifest(projectPath: string): Promise<ProjectManifest> {
  const raw = await fs.readFile(path.join(projectPath, MANIFEST), 'utf8');
  const parsed = JSON.parse(raw) as Partial<ProjectManifest>;
  return {
    version:
      typeof parsed.version === 'number'
        ? parsed.version
        : PROJECT_SCHEMA_VERSION,
    title:
      typeof parsed.title === 'string' && parsed.title
        ? parsed.title
        : path.basename(projectPath),
    createdWith: 'shotAI',
    createdAt: typeof parsed.createdAt === 'string' ? parsed.createdAt : '',
    updatedAt: typeof parsed.updatedAt === 'string' ? parsed.updatedAt : '',
    captureSettings: parsed.captureSettings ?? null,
    steps: normalizeSteps(parsed.steps),
    sopBackup: coerceSopBackup(parsed.sopBackup),
  };
}

async function writeManifest(
  projectPath: string,
  manifest: ProjectManifest,
): Promise<void> {
  await fs.writeFile(
    path.join(projectPath, MANIFEST),
    JSON.stringify(manifest, null, 2),
    'utf8',
  );
}

function summarize(
  manifest: ProjectManifest,
  projectPath: string,
): ProjectSummary {
  return {
    title: manifest.title,
    path: projectPath,
    createdAt: manifest.createdAt,
    updatedAt: manifest.updatedAt,
    stepCount: manifest.steps.length,
  };
}

/** Atomically claim a non-colliding folder under `parent` for `base`. */
async function claimDir(parent: string, base: string): Promise<string> {
  for (let n = 1; ; n++) {
    const candidate =
      n === 1 ? path.join(parent, base) : path.join(parent, `${base} (${n})`);
    try {
      await fs.mkdir(candidate, { recursive: false }); // claim it atomically
      return candidate;
    } catch (e) {
      const err = e as NodeJS.ErrnoException;
      if (err.code === 'EEXIST') continue; // taken — try the next suffix
      throw e;
    }
  }
}

/** Create a new, empty project folder and write its v1 manifest. */
export async function createProject(title: string): Promise<ProjectSummary> {
  const root = await getProjectsDir();
  await fs.mkdir(root, { recursive: true });

  const dir = await claimDir(root, toFolderName(title));
  await fs.mkdir(path.join(dir, 'shots'), { recursive: true });
  await fs.mkdir(path.join(dir, 'export'), { recursive: true });

  const now = new Date().toISOString();
  const manifest: ProjectManifest = {
    version: PROJECT_SCHEMA_VERSION,
    title: title.trim() || 'Untitled Project',
    createdWith: 'shotAI',
    createdAt: now,
    updatedAt: now,
    captureSettings: null,
    steps: [],
    sopBackup: null,
  };
  await fs.writeFile(
    path.join(dir, MANIFEST),
    JSON.stringify(manifest, null, 2),
    'utf8',
  );

  await addRecent(dir);
  return summarize(manifest, dir);
}

/** Open: resolve + confine the path, read the manifest, mark recently opened. */
async function loadProject(
  projectPath: string,
): Promise<{ resolved: string; manifest: ProjectManifest }> {
  const resolved = await resolveKnownProject(projectPath);
  const manifest = await readManifest(resolved);
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
  const abs = path.resolve(dir, rel);
  const within = path.relative(dir, abs);
  if (within === '' || within.startsWith('..') || path.isAbsolute(within)) {
    return null; // escapes the project folder
  }
  return abs;
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

// Serialize manifest writes so rapid captures can't interleave read-modify-write.
let writeQueue: Promise<unknown> = Promise.resolve();

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
    Object.assign(step, patch);
    if (flattenedPng && flattenedPng.length) {
      const renderDir = path.join(resolved, 'export', '.render');
      await fs.mkdir(renderDir, { recursive: true });
      await fs.writeFile(path.join(renderDir, `${stepId}.png`), flattenedPng);
      // posix separators for the shot:// URL the renderer builds from this.
      step.flattened = path.posix.join('export', '.render', `${stepId}.png`);
      // Bump only on a real re-render so the report cache-busts the <img> then —
      // but NOT on display-only patches (e.g. reportZoom), avoiding a reload.
      step.renderRev = (step.renderRev ?? 0) + 1;
    } else if ('annotations' in patch || 'crop' in patch) {
      // Annotations/crop changed without a fresh bake → the cached render no longer
      // reflects the step (a new/changed redaction or crop isn't applied). Invalidate
      // it so it MUST be re-flattened before any send: redaction is enforced by
      // FRESHNESS, not mere existence (Phase-3b review). The shipping editor always
      // co-sends a PNG so this branch only fires on annotations/crop-only patches.
      step.flattened = null;
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

/** Reassign step.order to 1..N in array order. */
function renumber(steps: ProjectStep[]): void {
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

/** Insert an empty text step at `atIndex` (clamped) and renumber. */
export function addTextStep(
  projectPath: string,
  atIndex: number,
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

/** Build a fresh text step. `aiInserted` marks SOP-generated intro/section steps. */
function makeTextStep(heading: string, body: string, aiInserted = false): ProjectStep {
  return {
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
    heading,
    body,
    crop: null,
    annotations: [],
    aiInserted,
  };
}

/**
 * Apply Claude's inline SOP edit plan to the project's steps: snapshot the
 * current steps+title for revert, rewrite each referenced SHOT step's
 * caption/heading/body/note, insert an optional intro + per-step section
 * headings, optionally refine the title, then renumber. Author-written text
 * steps pass through untouched; edits keyed to a non-shot step are ignored.
 * Serialized via the writeQueue. Returns the updated manifest.
 */
export function applySopEdits(
  projectPath: string,
  plan: SopEditPlan,
  provenance: { model: string; tone: SopTone },
): Promise<ProjectManifest> {
  const run = writeQueue.then(async () => {
    const resolved = await resolveKnownProject(projectPath);
    const manifest = await readManifest(resolved);

    // Preserve the FIRST snapshot (the pristine pre-AI state) across regenerations
    // so "Revert Claude's edits" always restores the true original — never a prior
    // AI pass. Cleared by revertSop, so a fresh generate re-snapshots.
    const backup: SopBackup = manifest.sopBackup ?? {
      steps: structuredClone(manifest.steps),
      title: manifest.title,
      model: provenance.model,
      tone: provenance.tone,
      at: new Date().toISOString(),
    };

    // Rebuild from the non-AI base (current steps minus a prior run's inserts),
    // matching the numbering assembleRequest showed Claude. Author text steps and
    // the user's own screenshots/edits are preserved; only prior AI inserts drop.
    const base = manifest.steps.filter((s) => !s.aiInserted);
    const editByNum = new Map(plan.steps.map((e) => [e.stepNumber, e]));
    const next: ProjectStep[] = [];
    if (plan.intro && (plan.intro.heading || plan.intro.body)) {
      next.push(makeTextStep(plan.intro.heading, plan.intro.body, true));
    }
    base.forEach((step, idx) => {
      // Author text steps pass through; edits only apply to SHOT steps (so an
      // edit mis-keyed to a text step's number is simply ignored).
      if (step.kind === 'text') {
        next.push(step);
        return;
      }
      const e = editByNum.get(idx + 1);
      if (!e) {
        next.push(step);
        return;
      }
      if (e.sectionHeading) {
        next.push(makeTextStep(e.sectionHeading, e.sectionBody ?? '', true));
      }
      next.push({
        ...step,
        caption: e.caption.trim() || step.caption,
        // Fall back to the existing value (don't wipe user-written text) if the
        // model returns blank — mirrors the caption guard.
        heading: e.heading.trim() || step.heading || '',
        body: e.body.trim() || step.body || '',
        note: e.note ?? step.note,
      });
    });

    manifest.steps = next;
    renumber(manifest.steps);
    if (plan.title && plan.title.trim()) manifest.title = plan.title.trim();
    manifest.sopBackup = backup;
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
 * Revert Claude's inline SOP edits: restore the pre-generation snapshot
 * (steps + title) and clear it. Throws if there's nothing to revert.
 * Serialized via the writeQueue. Returns the updated manifest.
 */
export function revertSop(projectPath: string): Promise<ProjectManifest> {
  const run = writeQueue.then(async () => {
    const resolved = await resolveKnownProject(projectPath);
    const manifest = await readManifest(resolved);
    if (!manifest.sopBackup) {
      throw new Error('Nothing to revert — no AI edits are recorded for this project.');
    }
    manifest.steps = normalizeSteps(manifest.sopBackup.steps);
    manifest.title = manifest.sopBackup.title;
    manifest.sopBackup = null;
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
