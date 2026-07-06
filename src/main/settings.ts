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
import {
  CAPTURE_SCALE_MIN,
  CAPTURE_SCALE_MAX,
  CAPTURE_SCALE_DEFAULT,
} from '../shared/project';

/** Coerce an untrusted captureScale to the allowed range (or the default). */
function clampCaptureScale(v: unknown): number {
  return typeof v === 'number' && Number.isFinite(v)
    ? Math.min(CAPTURE_SCALE_MAX, Math.max(CAPTURE_SCALE_MIN, v))
    : CAPTURE_SCALE_DEFAULT;
}

export interface Settings {
  projectsDir: string;
  recents: string[];
  /** SOP generation settings (Phase 3; NON-SECRET — the API key is in secrets.ts). */
  sop: SopSettings;
  /**
   * Keep the app window VISIBLE during capture instead of hiding it (demo /
   * screen-share mode). Default false = hide while recording (so the window
   * doesn't appear in the screenshots). Read synchronously at recording-start via
   * captureNoHideNow(); replaces the old SHOTAI_CAPTURE_NO_HIDE env var.
   */
  captureNoHide: boolean;
  /**
   * Screenshot quality: target downscale factor for captures (CAPTURE_SCALE_MIN..1,
   * default 0.85). Lower = smaller files + cheaper AI, softer text. CaptureController
   * enforces a readability floor on top of this. Read synchronously via
   * captureScaleNow().
   */
  captureScale: number;
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
      captureNoHide: typeof parsed.captureNoHide === 'boolean' ? parsed.captureNoHide : false,
      captureScale: clampCaptureScale(parsed.captureScale),
    };
  } catch {
    return {
      projectsDir: defaultProjectsDir(),
      recents: [],
      sop: DEFAULT_SOP_SETTINGS,
      captureNoHide: false,
      captureScale: CAPTURE_SCALE_DEFAULT,
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

// In-memory mirror of captureNoHide so the capture path can read it SYNCHRONOUSLY
// at recording-start — an async load there would risk a frame where the window is
// still visible and leaks into the first screenshot. Primed at startup via
// getCaptureNoHide() and updated immediately by setCaptureNoHide().
let captureNoHideCache = false;

/** Current captureNoHide value, synchronously (safe default false = hide). */
export function captureNoHideNow(): boolean {
  return captureNoHideCache;
}

export async function getCaptureNoHide(): Promise<boolean> {
  captureNoHideCache = (await load()).captureNoHide;
  return captureNoHideCache;
}

/** Persist captureNoHide and update the synchronous cache. Returns the new value. */
export function setCaptureNoHide(value: boolean): Promise<boolean> {
  captureNoHideCache = value; // reflect immediately for the next recording
  return mutate((s) => {
    s.captureNoHide = value;
    return value;
  });
}

// Same sync-cache pattern for the screenshot-quality (downscale) factor, so
// CaptureController reads the current value at capture-start without an async hop.
let captureScaleCache = CAPTURE_SCALE_DEFAULT;

/** Current captureScale, synchronously (already clamped). */
export function captureScaleNow(): number {
  return captureScaleCache;
}

export async function getCaptureScale(): Promise<number> {
  captureScaleCache = (await load()).captureScale;
  return captureScaleCache;
}

/** Persist captureScale (clamped) and update the cache. Returns the stored value. */
export function setCaptureScale(value: number): Promise<number> {
  const clamped = clampCaptureScale(value);
  captureScaleCache = clamped;
  return mutate((s) => {
    s.captureScale = clamped;
    return clamped;
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
