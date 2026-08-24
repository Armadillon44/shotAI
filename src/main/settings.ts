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
  type ThemePref,
} from '../shared/project';

/** Coerce an untrusted theme preference to a valid value (default 'system'). */
function coerceTheme(v: unknown): ThemePref {
  return v === 'light' || v === 'dark' || v === 'system' ? v : 'system';
}

/** Coerce an untrusted captureScale to the allowed range (or the default). */
function clampCaptureScale(v: unknown): number {
  return typeof v === 'number' && Number.isFinite(v)
    ? Math.min(CAPTURE_SCALE_MAX, Math.max(CAPTURE_SCALE_MIN, v))
    : CAPTURE_SCALE_DEFAULT;
}

/** Coerce an untrusted archiveAgeDays: 0 = never (auto-archive off), else clamped
 *  to 1..1825 days. Default 90 (~3 months). */
function clampArchiveAge(v: unknown): number {
  if (typeof v !== 'number' || !Number.isFinite(v)) return ARCHIVE_AGE_DEFAULT;
  if (v <= 0) return 0;
  return Math.min(1825, Math.max(1, Math.round(v)));
}

/** Trim + cap a user-supplied display name (defensive; it lands in exports). */
function coerceUserName(v: unknown): string {
  return typeof v === 'string' ? v.slice(0, 120) : '';
}

export const ARCHIVE_AGE_DEFAULT = 90;

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
   * Let shotAI be seen over a remote-control session or a screen share.
   *
   * shotAI sets contentProtection on every window, which is what keeps it out of
   * its own screenshots. On Windows that is WDA_EXCLUDEFROMCAPTURE, and it cannot
   * tell OUR capture from Teams' or Splashtop's: both are screen capture. So the
   * only way to have both is temporal, and this flag drives it. When true,
   * protection is OFF while the app is merely being used, and CaptureController
   * flips it ON around each grab (measured free: the toggle is synchronous, no
   * settle needed, unlike the 350ms window-hide settle).
   *
   * NOT the same as captureNoHide. This does not change what is HIDDEN during a
   * recording: the project window still hides, so it never appears in a capture
   * and a remote viewer sees only the pill while recording. Read synchronously
   * via remoteVisibleNow() because the capture path cannot afford an async hop.
   */
  remoteVisible: boolean;
  /**
   * Screenshot quality: target downscale factor for captures (CAPTURE_SCALE_MIN..1,
   * default 0.85). Lower = smaller files + cheaper AI, softer text. CaptureController
   * enforces a readability floor on top of this. Read synchronously via
   * captureScaleNow().
   */
  captureScale: number;
  /**
   * Whether the first-run coach-mark tour has been shown/dismissed (R2). Default
   * false → the tour fires once on first launch; the user can replay it from
   * Settings → About (which sets this back to false).
   */
  hasSeenTour: boolean;
  /** Display name (F8). Appended to the "Created on …" export line as "by <name>"
   *  when includeNameInReports is on. Default '' (empty). */
  userName: string;
  /** Opt-in to include userName in reports/exports (F8). Default false. */
  includeNameInReports: boolean;
  /**
   * Auto-archive projects untouched (by updatedAt) longer than this many days
   * (F2). 0 = never — auto-archive off; manual Archive is always available.
   * Default 90 (~3 months).
   */
  archiveAgeDays: number;
  /** UI color theme (F10): 'light' | 'dark' | 'system' (default 'system'). */
  theme: ThemePref;
  /**
   * Check GitHub once a day, on startup, for a newer release (#54). Default true.
   *
   * This is the app's ONLY unsolicited network call — everything else is local or an
   * explicit Claude request — so it stays switchable without a rebuild. That matters
   * if shotAI ever lands under managed (Intune) deployment, where IT usually wants
   * apps not to advertise their own updates.
   */
  updateCheckEnabled: boolean;
  /** Epoch ms of the last completed check, for the once-a-day throttle. 0 = never. */
  lastUpdateCheckAt: number;
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
      remoteVisible: typeof parsed.remoteVisible === 'boolean' ? parsed.remoteVisible : false,
      captureScale: clampCaptureScale(parsed.captureScale),
      hasSeenTour: typeof parsed.hasSeenTour === 'boolean' ? parsed.hasSeenTour : false,
      userName: coerceUserName(parsed.userName),
      includeNameInReports:
        typeof parsed.includeNameInReports === 'boolean' ? parsed.includeNameInReports : false,
      archiveAgeDays: clampArchiveAge(parsed.archiveAgeDays),
      theme: coerceTheme(parsed.theme),
      updateCheckEnabled:
        typeof parsed.updateCheckEnabled === 'boolean' ? parsed.updateCheckEnabled : true,
      lastUpdateCheckAt:
        typeof parsed.lastUpdateCheckAt === 'number' && Number.isFinite(parsed.lastUpdateCheckAt)
          ? parsed.lastUpdateCheckAt
          : 0,
    };
  } catch {
    return {
      projectsDir: defaultProjectsDir(),
      recents: [],
      sop: DEFAULT_SOP_SETTINGS,
      captureNoHide: false,
      remoteVisible: false,
      captureScale: CAPTURE_SCALE_DEFAULT,
      hasSeenTour: false,
      userName: '',
      includeNameInReports: false,
      archiveAgeDays: ARCHIVE_AGE_DEFAULT,
      theme: 'system',
      updateCheckEnabled: true,
      lastUpdateCheckAt: 0,
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

// Same sync-cache reasoning as captureNoHide, and here it is load-bearing for a
// different reason: CaptureController reads this INSIDE the shield around every
// grab. An async read there would open a window in which the pill is capturable,
// which is exactly the leak the shield exists to prevent. Default false = fully
// protected, i.e. today's behavior for anyone who never touches the setting.
let remoteVisibleCache = false;

/** Current remoteVisible value, synchronously (safe default false = protected). */
export function remoteVisibleNow(): boolean {
  return remoteVisibleCache;
}

export async function getRemoteVisible(): Promise<boolean> {
  remoteVisibleCache = (await load()).remoteVisible;
  return remoteVisibleCache;
}

/** Persist remoteVisible and update the synchronous cache. Returns the new value. */
export function setRemoteVisible(value: boolean): Promise<boolean> {
  remoteVisibleCache = value; // the next grab must see this, not the persisted value
  return mutate((s) => {
    s.remoteVisible = value;
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

export async function getHasSeenTour(): Promise<boolean> {
  return (await load()).hasSeenTour;
}

/** Persist whether the first-run tour has been seen (false replays it). */
export function setHasSeenTour(value: boolean): Promise<boolean> {
  return mutate((s) => {
    s.hasSeenTour = value;
    return value;
  });
}

export async function getUserName(): Promise<string> {
  return (await load()).userName;
}

/** Persist the display name (trimmed/capped). Returns the stored value. */
export function setUserName(value: string): Promise<string> {
  const v = coerceUserName(value);
  return mutate((s) => {
    s.userName = v;
    return v;
  });
}

export async function getUpdateCheckEnabled(): Promise<boolean> {
  return (await load()).updateCheckEnabled;
}

/** Persist the update-check opt-out (#54). Returns the new value. */
export function setUpdateCheckEnabled(value: boolean): Promise<boolean> {
  return mutate((s) => {
    s.updateCheckEnabled = value;
    return value;
  });
}

export async function getLastUpdateCheckAt(): Promise<number> {
  return (await load()).lastUpdateCheckAt;
}

/** Stamp a completed check so the once-a-day throttle can see it. */
export function setLastUpdateCheckAt(value: number): Promise<number> {
  return mutate((s) => {
    s.lastUpdateCheckAt = value;
    return value;
  });
}

export async function getIncludeNameInReports(): Promise<boolean> {
  return (await load()).includeNameInReports;
}

/** Persist the include-name-in-reports opt-in. Returns the new value. */
export function setIncludeNameInReports(value: boolean): Promise<boolean> {
  return mutate((s) => {
    s.includeNameInReports = value;
    return value;
  });
}

export async function getArchiveAgeDays(): Promise<number> {
  return (await load()).archiveAgeDays;
}

/** Persist archiveAgeDays (0 = never; else 1..1825). Returns the stored value. */
export function setArchiveAgeDays(value: number): Promise<number> {
  const clamped = clampArchiveAge(value);
  return mutate((s) => {
    s.archiveAgeDays = clamped;
    return clamped;
  });
}

/**
 * The byline to append to the "Created on …" export line, or null when the user
 * hasn't opted in or hasn't set a name. Single source of truth for every exporter
 * (F7) so the opt-in gate lives in one place.
 */
export async function getReportByline(): Promise<string | null> {
  const s = await load();
  const name = s.userName.trim();
  return s.includeNameInReports && name ? name : null;
}

export async function getTheme(): Promise<ThemePref> {
  return (await load()).theme;
}

/** Persist the theme preference (coerced). Returns the stored value. */
export function setTheme(value: unknown): Promise<ThemePref> {
  const v = coerceTheme(value);
  return mutate((s) => {
    s.theme = v;
    return v;
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
