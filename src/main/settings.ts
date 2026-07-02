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
import { writeFileAtomic } from './atomic-write';
import {
  coerceSopSettings,
  DEFAULT_SOP_SETTINGS,
  type SopSettings,
} from '../shared/sop';

export interface Settings {
  projectsDir: string;
  recents: string[];
  /** SOP generation settings (Phase 3; NON-SECRET — the API key is in secrets.ts). */
  sop: SopSettings;
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
      sop: coerceSopSettings(parsed.sop),
    };
  } catch {
    return {
      projectsDir: defaultProjectsDir(),
      recents: [],
      sop: DEFAULT_SOP_SETTINGS,
    };
  }
}

async function save(settings: Settings): Promise<void> {
  // Atomic, Windows-lock-tolerant write (see atomicWrite.ts).
  await writeFileAtomic(settingsFile(), JSON.stringify(settings, null, 2), {
    onRetry: (code) =>
      projectsLog.warn(`settings rename ${code} — retrying (lock likely transient)`),
  });
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

export async function getSopSettings(): Promise<SopSettings> {
  return (await load()).sop;
}

/**
 * Patch SOP settings (validated/coerced onto the current values) and persist.
 * Returns the full, coerced settings so the renderer resyncs without a re-read.
 */
export function setSopSettings(patch: Partial<SopSettings>): Promise<SopSettings> {
  return mutate((s) => {
    s.sop = coerceSopSettings({ ...s.sop, ...patch }, s.sop);
    return s.sop;
  });
}
