// Persistent app settings, stored as JSON in Electron's userData dir.
// Keeps the projects directory and the recent-projects list.
//
// Mutations are serialized (so concurrent updates can't clobber each other) and
// written atomically (tmp file + rename) so an interrupted write can't corrupt
// settings.json and silently reset the user's projects dir / recents.
import { app } from 'electron';
import { promises as fs } from 'node:fs';
import path from 'node:path';
import { projectsLog } from './logger';

export interface Settings {
  projectsDir: string;
  recents: string[];
}

const MAX_RECENTS = 20;

function settingsFile(): string {
  return path.join(app.getPath('userData'), 'settings.json');
}

export function defaultProjectsDir(): string {
  return path.join(app.getPath('home'), 'shotAI Projects');
}

async function load(): Promise<Settings> {
  try {
    const raw = await fs.readFile(settingsFile(), 'utf8');
    const parsed = JSON.parse(raw) as Partial<Settings>;
    return {
      projectsDir:
        typeof parsed.projectsDir === 'string'
          ? parsed.projectsDir
          : defaultProjectsDir(),
      recents: Array.isArray(parsed.recents)
        ? parsed.recents.filter((p): p is string => typeof p === 'string')
        : [],
    };
  } catch {
    return { projectsDir: defaultProjectsDir(), recents: [] };
  }
}

// Windows transiently fails rename-over-an-existing-file with EPERM/EACCES/EBUSY
// when antivirus, the search indexer, or another reader briefly holds the
// destination open. The lock virtually always clears within a few hundred ms, so
// retry with backoff before giving up (the same approach as write-file-atomic).
const RENAME_RETRY_DELAYS_MS = [10, 25, 50, 100, 200, 350, 600];

async function renameWithRetry(from: string, to: string): Promise<void> {
  for (let attempt = 0; ; attempt++) {
    try {
      await fs.rename(from, to);
      return;
    } catch (e) {
      const code = (e as NodeJS.ErrnoException).code;
      const retriable = code === 'EPERM' || code === 'EACCES' || code === 'EBUSY';
      if (!retriable || attempt >= RENAME_RETRY_DELAYS_MS.length) throw e;
      if (attempt === 0) {
        projectsLog.warn(`settings rename ${code} — retrying (lock likely transient)`);
      }
      await new Promise((r) => setTimeout(r, RENAME_RETRY_DELAYS_MS[attempt]));
    }
  }
}

async function save(settings: Settings): Promise<void> {
  const file = settingsFile();
  await fs.mkdir(path.dirname(file), { recursive: true });
  const tmp = `${file}.${process.pid}.tmp`;
  await fs.writeFile(tmp, JSON.stringify(settings, null, 2), 'utf8');
  try {
    await renameWithRetry(tmp, file); // atomic replace (retried on Windows locks)
  } catch (e) {
    await fs.rm(tmp, { force: true }).catch(() => undefined); // don't leak the tmp
    throw e;
  }
}

// Serialize read-modify-write so concurrent mutators can't lose updates.
let queue: Promise<unknown> = Promise.resolve();
function mutate<T>(fn: (settings: Settings) => T | Promise<T>): Promise<T> {
  const run = queue.then(async () => {
    const settings = await load();
    const result = await fn(settings);
    await save(settings);
    return result;
  });
  // Keep the chain alive even if one mutation rejects.
  queue = run.then(
    () => undefined,
    () => undefined,
  );
  return run;
}

export async function getProjectsDir(): Promise<string> {
  return (await load()).projectsDir;
}

export function persistProjectsDir(dir: string): Promise<void> {
  return mutate((s) => {
    s.projectsDir = dir;
  });
}

export async function getRecents(): Promise<string[]> {
  return (await load()).recents;
}

/**
 * Move a project path to the front of the recents list (dedup, capped).
 * Best-effort: the MRU list is convenience bookkeeping, so a settings-write
 * failure here must never block opening a project or starting a recording.
 */
export async function addRecent(projectPath: string): Promise<void> {
  try {
    await mutate((s) => {
      s.recents = [
        projectPath,
        ...s.recents.filter((p) => p !== projectPath),
      ].slice(0, MAX_RECENTS);
    });
  } catch (e) {
    projectsLog.warn('addRecent failed (non-fatal):', e);
  }
}

export function setRecents(recents: string[]): Promise<void> {
  return mutate((s) => {
    s.recents = recents;
  });
}
