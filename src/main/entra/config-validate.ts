// Validation for the managed federation config (#63) — the six non-secret values
// (plus two optional) that map this organization's Entra tenant to one Anthropic
// org. None of them are secrets: the JWT is what authenticates. They are not in
// this repo because they are per-organization, and they are delivered by an
// administrator (HKLM on Windows, a forced preference on macOS).
//
// Deliberately electron-free and I/O-free so the fail-closed rule below is
// unit-testable under plain node, same pattern as export-geometry.ts and
// update-check.ts. The platform-specific read lives in config.ts.
//
// THE INVARIANT: fail closed to BYO-key on any missing or malformed required
// value, so a half-delivered policy can never produce a half-configured
// federation that fails opaquely at exchange time.

/** The delivered configuration, once every required value has validated. */
export interface FederationConfig {
  /** Entra tenant (directory) id. */
  tenantId: string;
  /** The DESKTOP CLIENT registration — the app signing the user in. */
  clientAppId: string;
  /** The API/audience registration — the token's `aud`, and what the Anthropic
   *  federation rule matches by id. Distinct from clientAppId on purpose. */
  audienceAppId: string;
  federationRuleId: string;
  organizationId: string;
  serviceAccountId: string;
  /** Optional: omit to let a single-workspace rule pick its sole workspace.
   *  Required by Anthropic only when the rule spans more than one. */
  workspaceId?: string;
  /** Optional: where "Request access" points for an unassigned user. Left unset
   *  so no organization URL is baked into a public repo; the caller supplies the
   *  fallback. NOTE: whatever host lands here must also be permitted by the
   *  openExternal allowlist in ipc.ts, or the link is silently refused — the
   *  exact failure the v1.1.6 update-check download link hit. */
  supportUrl?: string;
}

/** Why a config was rejected. Names only, never values: this can reach a log. */
export type FederationConfigResult =
  | { ok: true; config: FederationConfig }
  | { ok: false; missing: string[]; invalid: string[] };

/** The registry value / preference-key names, exactly as an administrator writes
 *  them. Shared verbatim with the macOS side so one ADMX and one configuration
 *  profile describe the same contract. */
export const FEDERATION_KEYS = [
  'TenantId',
  'ClientAppId',
  'AudienceAppId',
  'FederationRuleId',
  'OrganizationId',
  'ServiceAccountId',
  'WorkspaceId',
  'SupportUrl',
] as const;

export type FederationKey = (typeof FEDERATION_KEYS)[number];

/** Raw values as read from the platform store; absent keys may be null/undefined. */
export type RawFederationConfig = Partial<Record<FederationKey, string | null | undefined>>;

const GUID = /^[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}$/;

/** Anthropic tagged ids: a fixed prefix then an alphanumeric body. */
const TAGGED: Record<'fdrl' | 'svac' | 'wrkspc', RegExp> = {
  fdrl: /^fdrl_[0-9A-Za-z]+$/,
  svac: /^svac_[0-9A-Za-z]+$/,
  wrkspc: /^wrkspc_[0-9A-Za-z]+$/,
};

/**
 * Normalize one delivered value: trim, and strip the surrounding braces Windows
 * tooling frequently puts around a GUID ({...}). Empty becomes absent — a value
 * present but blank is an unset policy, not a malformed one, and must read the
 * same as "never delivered" so a cleared ADMX setting reverts to BYO-key.
 */
function norm(v: string | null | undefined): string | null {
  if (typeof v !== 'string') return null;
  const t = v.trim().replace(/^\{(.*)\}$/, '$1').trim();
  return t.length ? t : null;
}

/** True when `v` is a well-formed https URL. */
function isHttpsUrl(v: string): boolean {
  try {
    return new URL(v).protocol === 'https:';
  } catch {
    return false;
  }
}

/**
 * Validate raw delivered values into a FederationConfig, or explain why not.
 *
 * Required: tenant, client app, audience app, rule, organization, service
 * account. A missing OR malformed one fails the whole config closed.
 *
 * `WorkspaceId` is optional, but present-and-malformed also fails closed: it
 * takes part in the exchange, so a typo there produces the same opaque 401 as a
 * missing role and would be diagnosed as an entitlement problem for hours.
 *
 * `SupportUrl` is the one exception. It is presentational, affects no exchange,
 * and a bad value must not cost the whole org its sign-in — it degrades to
 * absent and the caller falls back to the repo's issues page.
 */
export function validateFederationConfig(raw: RawFederationConfig): FederationConfigResult {
  const missing: string[] = [];
  const invalid: string[] = [];

  const required = (key: FederationKey, re: RegExp): string => {
    const v = norm(raw[key]);
    if (v === null) {
      missing.push(key);
      return '';
    }
    if (!re.test(v)) {
      invalid.push(key);
      return '';
    }
    return v;
  };

  const tenantId = required('TenantId', GUID);
  const clientAppId = required('ClientAppId', GUID);
  const audienceAppId = required('AudienceAppId', GUID);
  const federationRuleId = required('FederationRuleId', TAGGED.fdrl);
  const organizationId = required('OrganizationId', GUID);
  const serviceAccountId = required('ServiceAccountId', TAGGED.svac);

  // Optional-but-load-bearing: absent is fine, malformed is not.
  const rawWorkspace = norm(raw.WorkspaceId);
  let workspaceId: string | undefined;
  if (rawWorkspace !== null) {
    if (TAGGED.wrkspc.test(rawWorkspace)) workspaceId = rawWorkspace;
    else invalid.push('WorkspaceId');
  }

  if (missing.length || invalid.length) return { ok: false, missing, invalid };

  // Presentational only — never fails the config.
  const rawSupport = norm(raw.SupportUrl);
  const supportUrl = rawSupport && isHttpsUrl(rawSupport) ? rawSupport : undefined;

  return {
    ok: true,
    config: {
      tenantId,
      clientAppId,
      audienceAppId,
      federationRuleId,
      organizationId,
      serviceAccountId,
      ...(workspaceId ? { workspaceId } : {}),
      ...(supportUrl ? { supportUrl } : {}),
    },
  };
}

/**
 * True when NOTHING was delivered — the default for every external user and the
 * signal to behave exactly as today with no mention of Entra anywhere in the UI.
 * Distinct from a rejected config, which an administrator needs to see.
 */
export function isUnconfigured(raw: RawFederationConfig): boolean {
  return FEDERATION_KEYS.every((k) => norm(raw[k]) === null);
}
