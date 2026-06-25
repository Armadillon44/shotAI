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
  type StepPatch,
} from '../shared/project';
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
    sop: parsed.sop ?? null,
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
    sop: null,
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
    const manifest = await readManifest(projectPath);
    manifest.steps.push(step);
    manifest.updatedAt = new Date().toISOString();
    await writeManifest(projectPath, manifest);
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
