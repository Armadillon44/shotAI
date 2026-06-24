// ProjectStore — creates, opens, and lists shotAI projects on disk.
// Each project is a discrete, self-contained folder (project.json + shots/ +
// export/) under the user-chosen projects directory.
import { promises as fs } from 'node:fs';
import path from 'node:path';
import {
  PROJECT_SCHEMA_VERSION,
  type ProjectManifest,
  type ProjectSummary,
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
    steps: Array.isArray(parsed.steps) ? parsed.steps : [],
    sop: parsed.sop ?? null,
  };
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

/** Read an existing project's manifest and mark it recently opened. */
export async function openProject(
  projectPath: string,
): Promise<ProjectManifest> {
  const resolved = await resolveKnownProject(projectPath);
  const manifest = await readManifest(resolved);
  await addRecent(resolved);
  return manifest;
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
