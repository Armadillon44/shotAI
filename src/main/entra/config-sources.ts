// Where federation config comes from (#63), and how the two sources combine.
//
// Pure and fully injected — no electron, no child_process at call time, no globals
// read directly — so the whole chain (registry output -> parse -> merge ->
// validate) is unit-testable under plain node without touching the real hive.
// config.ts is the thin impure shell that supplies the real runner, the baked
// literal, caching, and logging.
import {
  FEDERATION_KEYS,
  isUnconfigured,
  mergeFederationSources,
  validateFederationConfig,
  type FederationConfigResult,
  type RawFederationConfig,
} from './config-validate';

/**
 * HKLM only, never HKCU. HKLM is the Windows integrity analog of macOS's
 * `objectIsForced`: only an administrator or MDM can write it, so a standard user
 * cannot repoint their own client at a different tenant or federation rule. Under
 * SOFTWARE\Policies rather than SOFTWARE so it reads as policy, and so ADMX
 * ingestion permits it (the blocked namespaces are System, Software\Microsoft,
 * and Software\Policies\Microsoft).
 */
export const POLICY_KEY = 'HKLM\\SOFTWARE\\Policies\\shotAI\\Federation';

/** Runs reg.exe and returns stdout, or throws. Injected so tests never spawn. */
export type RegRunner = (args: string[]) => Promise<string>;

/**
 * Parse `reg query <key>` output into the flat value map.
 *
 * Lines look like `    TenantId    REG_SZ    3f2504e0-...`. Only REG_SZ is part of
 * the contract: another type reads as absent and therefore fails closed, rather
 * than being coerced into a mangled value. A value may legitimately contain
 * spaces, hence the greedy tail. Unknown value names are ignored so an unrelated
 * value under the same key cannot inject a field.
 */
export function parseRegQuery(stdout: string): RawFederationConfig {
  const out: RawFederationConfig = {};
  const known = new Set<string>(FEDERATION_KEYS);
  for (const line of stdout.split(/\r?\n/)) {
    // reg.exe separates the columns with at least four spaces; requiring two or
    // more keeps a value that itself contains single spaces intact.
    const m = /^\s+(\S+)\s+REG_SZ\s{2,}(.*)$/.exec(line);
    if (!m) continue;
    const [, name, value] = m;
    if (known.has(name)) {
      out[name as (typeof FEDERATION_KEYS)[number]] = value.trim();
    }
  }
  return out;
}

/**
 * Read the machine policy key. An absent key, absent values, a reg.exe failure,
 * or a non-Windows host all read as "nothing delivered" and never as an error:
 * no policy is the default state for every machine, including every external
 * user's.
 */
export async function readPolicyConfig(
  runner: RegRunner,
  platform: string,
): Promise<RawFederationConfig> {
  if (platform !== 'win32') return {};
  try {
    return parseRegQuery(await runner(['query', POLICY_KEY, '/reg:64']));
  } catch {
    // reg.exe exits non-zero for "key not found", which is normal, not a fault.
    return {};
  }
}

/** Narrow an arbitrary baked record to the contract: known keys, non-blank strings. */
export function pickBaked(baked: Record<string, string | undefined>): RawFederationConfig {
  const out: RawFederationConfig = {};
  for (const k of FEDERATION_KEYS) {
    const v = baked[k];
    if (typeof v === 'string' && v.trim()) out[k] = v.trim();
  }
  return out;
}

export interface ResolvedFederation {
  raw: RawFederationConfig;
  result: FederationConfigResult;
  /** True when NOTHING was delivered by either source — the default, and the
   *  signal to say nothing about Entra anywhere in the UI. Distinct from a
   *  rejected config, which an administrator needs to see. */
  unconfigured: boolean;
}

/**
 * Resolve the effective config: baked values with policy overriding per key.
 * Returns the full validation result, so Settings and Test-connection can name
 * what an administrator got wrong (names only, never values).
 */
export async function resolveFederation(opts: {
  baked: Record<string, string | undefined>;
  runner: RegRunner;
  platform: string;
}): Promise<ResolvedFederation> {
  const raw = mergeFederationSources(
    pickBaked(opts.baked),
    await readPolicyConfig(opts.runner, opts.platform),
  );
  return { raw, result: validateFederationConfig(raw), unconfigured: isUnconfigured(raw) };
}
