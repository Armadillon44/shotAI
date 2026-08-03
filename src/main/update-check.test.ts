import { describe, it, expect } from 'vitest';
import {
  CHECK_INTERVAL_MS,
  checkForUpdate,
  compareVersions,
  isNewer,
  normalizeTag,
  pickRelease,
  shouldCheck,
  startupCheckDecision,
} from './update-check';

describe('normalizeTag', () => {
  it('strips a leading v and surrounding space', () => {
    expect(normalizeTag('v1.1.5')).toBe('1.1.5');
    expect(normalizeTag(' 1.1.5 ')).toBe('1.1.5');
    expect(normalizeTag('V2.0.0')).toBe('2.0.0');
  });
});

describe('compareVersions', () => {
  it('orders by numeric segment, not string', () => {
    // The bug this guards: '1.1.10' < '1.1.9' under string comparison.
    expect(compareVersions('1.1.10', '1.1.9')).toBeGreaterThan(0);
    expect(compareVersions('1.2.0', '1.10.0')).toBeLessThan(0);
    expect(compareVersions('2.0.0', '1.9.9')).toBeGreaterThan(0);
  });

  it('treats missing segments as zero', () => {
    expect(compareVersions('1.1', '1.1.0')).toBe(0);
    expect(compareVersions('1', '1.0.0')).toBe(0);
  });

  it('ignores a prerelease suffix for ordering', () => {
    // So an rc can never out-rank the final release of the same number.
    expect(compareVersions('1.2.0-rc1', '1.2.0')).toBe(0);
    expect(compareVersions('1.2.0-rc1', '1.1.9')).toBeGreaterThan(0);
  });

  it('is symmetric', () => {
    expect(compareVersions('1.1.5', '1.1.4')).toBeGreaterThan(0);
    expect(compareVersions('1.1.4', '1.1.5')).toBeLessThan(0);
  });
});

describe('isNewer', () => {
  it('is true only for a strictly greater version', () => {
    expect(isNewer('1.1.5', '1.1.6')).toBe(true);
    expect(isNewer('1.1.5', '1.2.0')).toBe(true);
    expect(isNewer('1.1.5', '1.1.5')).toBe(false);
    expect(isNewer('1.1.5', '1.1.4')).toBe(false); // a rollback is not an update
  });

  it('handles the v prefix on either side', () => {
    expect(isNewer('1.1.5', 'v1.1.6')).toBe(true);
    expect(isNewer('v1.1.6', '1.1.6')).toBe(false);
  });
});

describe('shouldCheck', () => {
  const now = 1_000_000_000_000;

  it('always checks when it has never checked', () => {
    expect(shouldCheck(undefined, now)).toBe(true);
    expect(shouldCheck(0, now)).toBe(true);
  });

  it('waits out the interval', () => {
    expect(shouldCheck(now - 1000, now)).toBe(false);
    expect(shouldCheck(now - CHECK_INTERVAL_MS + 1, now)).toBe(false);
    expect(shouldCheck(now - CHECK_INTERVAL_MS, now)).toBe(true);
    expect(shouldCheck(now - CHECK_INTERVAL_MS * 3, now)).toBe(true);
  });

  it('checks anyway when the stored time is in the FUTURE', () => {
    // Clock moved backwards, or a settings file synced from another machine —
    // without this the check would be locked out until the clock caught up.
    expect(shouldCheck(now + CHECK_INTERVAL_MS * 10, now)).toBe(true);
  });
});

describe('pickRelease', () => {
  const good = { tag_name: 'v1.2.0', html_url: 'https://github.com/Armadillon44/shotAI/releases/tag/v1.2.0' };

  it('accepts a normal release and strips the tag prefix', () => {
    expect(pickRelease(good)).toEqual({ version: '1.2.0', url: good.html_url });
  });

  it('refuses drafts and prereleases', () => {
    expect(pickRelease({ ...good, draft: true })).toBeNull();
    expect(pickRelease({ ...good, prerelease: true })).toBeNull();
  });

  it('refuses a tag that is not a version', () => {
    expect(pickRelease({ ...good, tag_name: 'nightly' })).toBeNull();
  });

  it('refuses a url that is not on github.com', () => {
    expect(pickRelease({ ...good, html_url: 'https://evil.example/x' })).toBeNull();
  });

  it('refuses junk', () => {
    expect(pickRelease(null)).toBeNull();
    expect(pickRelease('nope')).toBeNull();
    expect(pickRelease({})).toBeNull();
    expect(pickRelease({ tag_name: 'v1.2.0' })).toBeNull(); // no url
  });
});

describe('checkForUpdate', () => {
  const ok = (body: unknown): typeof fetch =>
    (async () => ({ ok: true, status: 200, json: async () => body })) as unknown as typeof fetch;

  it('reports an available update', async () => {
    const r = await checkForUpdate({
      currentVersion: '1.1.5',
      fetchImpl: ok({ tag_name: 'v1.2.0', html_url: 'https://github.com/a/b/releases/tag/v1.2.0' }),
    });
    expect(r).toEqual({ available: true, version: '1.2.0', url: 'https://github.com/a/b/releases/tag/v1.2.0' });
  });

  it('reports up to date when the latest release IS the current version', async () => {
    const r = await checkForUpdate({
      currentVersion: '1.1.5',
      fetchImpl: ok({ tag_name: 'v1.1.5', html_url: 'https://github.com/a/b/releases/tag/v1.1.5' }),
    });
    expect(r).toEqual({ available: false });
  });

  it('never claims an update when the request fails', async () => {
    const boom = (async () => {
      throw new Error('getaddrinfo ENOTFOUND');
    }) as unknown as typeof fetch;
    const r = await checkForUpdate({ currentVersion: '1.1.5', fetchImpl: boom });
    expect(r.available).toBe(false);
    expect(r.error).toContain('ENOTFOUND');
  });

  it('surfaces a rate-limit / non-200 as an error, not an update', async () => {
    const rateLimited = (async () => ({ ok: false, status: 403 })) as unknown as typeof fetch;
    const r = await checkForUpdate({ currentVersion: '1.1.5', fetchImpl: rateLimited });
    expect(r).toEqual({ available: false, error: 'GitHub returned 403' });
  });

  it('never claims an update from a malformed body', async () => {
    const r = await checkForUpdate({ currentVersion: '1.1.5', fetchImpl: ok({ nope: 1 }) });
    expect(r.available).toBe(false);
    expect(r.error).toBeTruthy();
  });

  it('does not offer a prerelease to someone on a stable build', async () => {
    const r = await checkForUpdate({
      currentVersion: '1.1.5',
      fetchImpl: ok({ tag_name: 'v1.2.0-rc1', prerelease: true, html_url: 'https://github.com/a/b' }),
    });
    expect(r.available).toBe(false);
  });
});

describe('startupCheckDecision', () => {
  const now = 1_000_000_000_000;

  it('does not run when the setting is off', () => {
    expect(startupCheckDecision({ enabled: false, lastCheckedMs: 0, nowMs: now })).toEqual({
      run: false,
      reason: 'disabled',
    });
  });

  it('does not run twice in the same day', () => {
    expect(
      startupCheckDecision({ enabled: true, lastCheckedMs: now - 60_000, nowMs: now }),
    ).toEqual({ run: false, reason: 'throttled' });
  });

  it('runs on a first-ever launch and once the day has elapsed', () => {
    expect(startupCheckDecision({ enabled: true, lastCheckedMs: 0, nowMs: now })).toEqual({ run: true });
    expect(
      startupCheckDecision({ enabled: true, lastCheckedMs: now - CHECK_INTERVAL_MS, nowMs: now }),
    ).toEqual({ run: true });
  });

  it('prefers the disabled reason over the throttle', () => {
    // Turning the setting off should read as off, not as "come back tomorrow".
    expect(
      startupCheckDecision({ enabled: false, lastCheckedMs: now - 60_000, nowMs: now }),
    ).toEqual({ run: false, reason: 'disabled' });
  });
});
