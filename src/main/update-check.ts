// "Is there a newer release?" — the check-and-notify half of #54.
//
// Deliberately NOT a real auto-updater. Squirrel could apply updates silently (and
// would dodge SmartScreen, since nothing gets downloaded through a browser), but that
// needs `RELEASES` + `*-full.nupkg` published per release plus delta packages, or
// every client pulls ~170 MB an update. See the investigation on #54. This module
// only tells the user a newer version exists and hands them the release page.
//
// Kept free of electron/node-fs imports so it unit-tests under plain node: the HTTP
// call, the clock, and the current version are all injected.
import type { UpdateCheckResult } from '../shared/ipc';

/** Where releases are published. Public repo, so no auth is needed. */
export const RELEASES_API =
  'https://api.github.com/repos/Armadillon44/shotAI/releases/latest';

/** Once a day, on startup — the interval agreed on #54. */
export const CHECK_INTERVAL_MS = 24 * 60 * 60 * 1000;

/**
 * Strip a leading `v` and any surrounding whitespace from a release tag.
 * `'v1.1.5'` -> `'1.1.5'`.
 */
export function normalizeTag(tag: string): string {
  return tag.trim().replace(/^v/i, '');
}

/**
 * Compare two dotted numeric versions: negative if `a` < `b`, 0 if equal, positive
 * if `a` > `b`. Missing segments count as 0, so `1.1` === `1.1.0`.
 *
 * Any pre-release suffix (`1.2.0-rc1`) is IGNORED for ordering — this app has shipped
 * `-rc` tags before, and treating `1.2.0-rc1` as equal to `1.2.0` is the safe
 * direction: it means an rc never advertises itself as an update over the final.
 * Callers filter prereleases out before getting here anyway (see pickRelease).
 */
export function compareVersions(a: string, b: string): number {
  const nums = (v: string) =>
    normalizeTag(v)
      .split('-')[0]
      .split('.')
      .map((s) => Number.parseInt(s, 10) || 0);
  const x = nums(a);
  const y = nums(b);
  for (let i = 0; i < Math.max(x.length, y.length); i++) {
    const d = (x[i] ?? 0) - (y[i] ?? 0);
    if (d !== 0) return d < 0 ? -1 : 1;
  }
  return 0;
}

/** Whether `latest` is strictly newer than `current`. */
export function isNewer(current: string, latest: string): boolean {
  return compareVersions(latest, current) > 0;
}

/**
 * Whether enough time has passed to check again. `lastCheckedMs` of 0/undefined (never
 * checked) always allows one. A `lastCheckedMs` in the FUTURE — a clock that moved
 * backwards, or a synced settings file from another machine — also allows one, rather
 * than locking the check out until the clock catches up.
 */
export function shouldCheck(
  lastCheckedMs: number | undefined,
  nowMs: number,
  intervalMs: number = CHECK_INTERVAL_MS,
): boolean {
  if (!lastCheckedMs) return true;
  if (lastCheckedMs > nowMs) return true; // clock skew — don't get stuck
  return nowMs - lastCheckedMs >= intervalMs;
}

/** Reason a check didn't run at all (as opposed to running and finding nothing). */
export type SkipReason = 'disabled' | 'throttled';

/**
 * Decide whether to run the startup check. Split out from the check itself so the
 * policy — off by setting, or already done today — is unit-testable without a network
 * or a clock.
 */
export function startupCheckDecision(opts: {
  enabled: boolean;
  lastCheckedMs: number | undefined;
  nowMs: number;
  intervalMs?: number;
}): { run: true } | { run: false; reason: SkipReason } {
  if (!opts.enabled) return { run: false, reason: 'disabled' };
  if (!shouldCheck(opts.lastCheckedMs, opts.nowMs, opts.intervalMs)) {
    return { run: false, reason: 'throttled' };
  }
  return { run: true };
}

/** The subset of the GitHub release payload this needs. */
interface ReleasePayload {
  tag_name?: unknown;
  html_url?: unknown;
  draft?: unknown;
  prerelease?: unknown;
}

/**
 * Validate a GitHub `releases/latest` body into a tag + URL, or null if it isn't
 * usable. Drafts and prereleases are refused so an `-rc` build is never offered to
 * someone on a stable version.
 */
export function pickRelease(body: unknown): { version: string; url: string } | null {
  if (!body || typeof body !== 'object') return null;
  const r = body as ReleasePayload;
  if (r.draft === true || r.prerelease === true) return null;
  if (typeof r.tag_name !== 'string' || typeof r.html_url !== 'string') return null;
  const version = normalizeTag(r.tag_name);
  // Guard against a tag that isn't a version at all (e.g. 'nightly').
  if (!/^\d+(\.\d+)*/.test(version)) return null;
  if (!r.html_url.startsWith('https://github.com/')) return null;
  return { version, url: r.html_url };
}

/**
 * Ask GitHub whether a newer release exists.
 *
 * NEVER throws and never reports an update it isn't sure about: any network failure,
 * non-200, or malformed body comes back as `{available: false, error}`. An update
 * notice is a nudge, not something worth surfacing an error over — the caller decides
 * whether the error is worth showing at all.
 *
 * `fetchImpl` is injected so this is testable without a network.
 */
export async function checkForUpdate(opts: {
  currentVersion: string;
  fetchImpl: typeof fetch;
  /** Overridable for tests. */
  url?: string;
  /** Abort the request after this long, so a hung connection can't wedge startup. */
  timeoutMs?: number;
}): Promise<UpdateCheckResult> {
  const { currentVersion, fetchImpl, url = RELEASES_API, timeoutMs = 10_000 } = opts;
  const controller = new AbortController();
  const timer = setTimeout(() => controller.abort(), timeoutMs);
  try {
    const res = await fetchImpl(url, {
      signal: controller.signal,
      headers: {
        Accept: 'application/vnd.github+json',
        // GitHub asks for a UA; identify the app rather than impersonating a browser.
        'User-Agent': `shotAI/${currentVersion}`,
      },
    });
    if (!res.ok) {
      // 403 here is nearly always the 60/hour unauthenticated rate limit.
      return { available: false, error: `GitHub returned ${res.status}` };
    }
    const picked = pickRelease(await res.json());
    if (!picked) return { available: false, error: 'no usable release in the response' };
    if (!isNewer(currentVersion, picked.version)) return { available: false };
    return { available: true, version: picked.version, url: picked.url };
  } catch (e) {
    const msg = e instanceof Error ? e.message : String(e);
    return { available: false, error: msg === 'The operation was aborted.' ? 'timed out' : msg };
  } finally {
    clearTimeout(timer);
  }
}
