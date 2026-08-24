// The impure shell around config-sources.ts (#63): supplies the real reg.exe
// runner and the build-time baked literal, then caches and logs.
//
// MAIN-ONLY. Nothing here is a secret — the Entra JWT is what authenticates — but
// the values are org-identifying, so they are never sent to the renderer and never
// logged. The renderer only learns whether federation is available at all.
import { execFile } from 'node:child_process';
import { promisify } from 'node:util';
import { claudeLog } from '../logger';
import { resolveFederation, type RegRunner, type ResolvedFederation } from './config-sources';
import type { FederationConfig } from './config-validate';

const exec = promisify(execFile);

const defaultRegRunner: RegRunner = async (args) => {
  // Absolute path and no shell, so PATH cannot be hijacked into a different
  // reg.exe. windowsHide so no console flashes on a user's screen.
  const reg = `${process.env.SystemRoot ?? 'C:\\Windows'}\\System32\\reg.exe`;
  const { stdout } = await exec(reg, args, { windowsHide: true });
  return stdout;
};

/** The values baked in at build time, or {} for any clone without the local file.
 *  __FEDERATION_BAKED__ is replaced by a literal by vite.main.config.ts; the
 *  typeof guard covers a non-Vite context such as a script importing this file. */
function bakedRecord(): Record<string, string | undefined> {
  return typeof __FEDERATION_BAKED__ === 'undefined' ? {} : __FEDERATION_BAKED__;
}

/** Full resolution result, for Settings and the Test-connection diagnostic path. */
export function resolveFederationNow(): Promise<ResolvedFederation> {
  return resolveFederation({
    baked: bakedRecord(),
    runner: defaultRegRunner,
    platform: process.platform,
  });
}

// Cached for the process lifetime: the policy read costs a process spawn and the
// answer only changes when an administrator rewrites policy. Settings drops the
// cache on open so a correction is picked up without a restart.
let cached: Promise<FederationConfig | null> | null = null;

/**
 * The validated config, or null when federation is unavailable. Unconfigured (the
 * default, and every external user) and rejected both mean "behave exactly as
 * today", which is why callers get one nullable value rather than a union.
 */
export function getFederationConfig(): Promise<FederationConfig | null> {
  if (!cached) {
    cached = resolveFederationNow().then(({ result, unconfigured }) => {
      if (result.ok) {
        claudeLog.info('federation: configured.');
        return result.config;
      }
      if (!unconfigured) {
        // A half-delivered policy is an administrator's problem and must be
        // visible. Key NAMES only: the values are not secret but they are
        // org-identifying, and this line can end up in a support attachment.
        claudeLog.warn(
          `federation: config rejected, falling back to API key (missing: ${
            result.missing.join(',') || 'none'
          }; invalid: ${result.invalid.join(',') || 'none'}).`,
        );
      }
      return null;
    });
  }
  return cached;
}

/** Drop the cache so the next read re-reads policy (called when Settings opens). */
export function invalidateFederationConfig(): void {
  cached = null;
}
