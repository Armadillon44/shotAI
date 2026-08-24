// The RFC 7523 token exchange (#63): trade an Entra JWT for a short-lived
// Anthropic token.
//
// Production traffic does NOT come through here — @anthropic-ai/sdk's
// oidcFederationProvider owns that, and with it the token cache, the two-tier
// refresh, request coalescing, and the 401 invalidate-and-retry. This module
// exists for the paths where raw status codes and our own wording matter: the
// "Test connection" button and the wif-probe.
//
// HTTP is INJECTED (update-check.ts precedent) so the whole thing unit-tests under
// plain node, and so the app can pass Electron's net.fetch — Chromium's stack,
// which honors the Windows system proxy / WPAD / PAC and the Windows certificate
// store. Node's global fetch (undici) trusts only its own bundled CA set and fails
// opaquely behind a TLS-inspecting corporate proxy, which looks exactly like a
// federation failure.
import type { FederationConfig } from './config-validate';

export const ANTHROPIC_BASE_URL = 'https://api.anthropic.com';
export const TOKEN_PATH = '/v1/oauth/token';
const GRANT_TYPE_JWT_BEARER = 'urn:ietf:params:oauth:grant-type:jwt-bearer';

/** Anthropic rejects an assertion over 16 KiB. Checked locally so an oversized
 *  token reports its real size instead of an opaque server refusal. */
export const MAX_ASSERTION_BYTES = 16 * 1024;

/** Minimal fetch shape, so callers can pass Electron net.fetch or a fake. */
export type FetchLike = (
  url: string,
  init: {
    method: string;
    headers: Record<string, string>;
    body: string;
    signal?: AbortSignal;
  },
) => Promise<{
  ok: boolean;
  status: number;
  headers: { get(name: string): string | null };
  text(): Promise<string>;
  json(): Promise<unknown>;
}>;

export class FederationExchangeError extends Error {
  constructor(
    message: string,
    readonly status: number | null = null,
    readonly requestId: string | null = null,
  ) {
    super(message);
    this.name = 'FederationExchangeError';
  }
}

export interface MintedToken {
  /** `sk-ant-oat01-...`. NEVER written to disk, never logged. */
  token: string;
  /** Absolute ms. Derived from the server's expires_in, never assumed. */
  expiresAt: number;
  expiresInSeconds: number;
  scope: string | null;
}

/**
 * Exchange an Entra assertion for an Anthropic access token.
 *
 * Never throws anything but FederationExchangeError, and its messages are already
 * safe to show a user: the response body can echo request fields, so it is never
 * attached to a renderer-visible error.
 */
export async function exchangeAssertion(
  assertion: string,
  cfg: FederationConfig,
  opts: {
    fetchImpl: FetchLike;
    signal?: AbortSignal;
    baseUrl?: string;
    /** Status + request-id + duration only. Never the assertion or the token. */
    log?: (line: string) => void;
  },
): Promise<MintedToken> {
  const bytes = Buffer.byteLength(assertion, 'utf8');
  if (!assertion || bytes === 0) {
    throw new FederationExchangeError('No Microsoft sign-in token to exchange.');
  }
  if (bytes > MAX_ASSERTION_BYTES) {
    // Optional claims or a heavily-grouped user can approach the cap; say the
    // real number rather than letting it look like a rule problem.
    throw new FederationExchangeError(
      `Sign-in token is ${Math.ceil(bytes / 1024)} KiB; Anthropic rejects assertions over 16 KiB.`,
    );
  }

  const url = `${opts.baseUrl ?? ANTHROPIC_BASE_URL}${TOKEN_PATH}`;
  const started = Date.now();
  let resp: Awaited<ReturnType<FetchLike>>;
  try {
    resp = await opts.fetchImpl(url, {
      method: 'POST',
      headers: { 'content-type': 'application/json' },
      body: JSON.stringify({
        grant_type: GRANT_TYPE_JWT_BEARER,
        assertion,
        federation_rule_id: cfg.federationRuleId,
        organization_id: cfg.organizationId,
        service_account_id: cfg.serviceAccountId,
        // Omit rather than send undefined: a rule bound to a single workspace
        // picks its own, and sending a null would be a 400.
        ...(cfg.workspaceId ? { workspace_id: cfg.workspaceId } : {}),
      }),
      signal: opts.signal,
    });
  } catch (e) {
    // Transport, not a federation answer. Distinguish it, or users go re-check
    // their role assignment for what is a proxy or DNS problem.
    throw new FederationExchangeError(
      `Could not reach Anthropic to sign in (${e instanceof Error ? e.message : String(e)}).`,
    );
  }

  const requestId = resp.headers.get('request-id');
  const ms = Date.now() - started;

  if (!resp.ok) {
    // Read and DISCARD the body: it can echo request fields back, so it never
    // reaches a user-visible error or a log line.
    const detail = await resp.text().catch(() => '');
    opts.log?.(
      `federation exchange failed: HTTP ${resp.status} in ${ms}ms` +
        (requestId ? ` request-id=${requestId}` : '') +
        ` (${detail.length}-byte body withheld)`,
    );
    throw explain(resp.status, requestId);
  }

  let data: unknown;
  try {
    data = await resp.json();
  } catch {
    throw new FederationExchangeError(
      'Anthropic returned an unreadable sign-in response.',
      resp.status,
      requestId,
    );
  }
  const body = (data ?? {}) as { access_token?: unknown; expires_in?: unknown; scope?: unknown };
  const token = typeof body.access_token === 'string' ? body.access_token : '';
  const expiresIn = Number(body.expires_in);
  if (!token || !Number.isFinite(expiresIn) || expiresIn <= 0) {
    throw new FederationExchangeError(
      'Anthropic sign-in response was missing access_token or expires_in.',
      resp.status,
      requestId,
    );
  }

  // Trust expires_in and never a constant: the mint is min(rule lifetime,
  // 2 x remaining JWT life) with a 60s floor, so a stale assertion legitimately
  // yields a much shorter token than the rule's configured lifetime.
  opts.log?.(`federated token minted in ${ms}ms (expires_in=${expiresIn}s).`);
  return {
    token,
    expiresInSeconds: expiresIn,
    expiresAt: Date.now() + expiresIn * 1000,
    scope: typeof body.scope === 'string' ? body.scope : null,
  };
}

/** Map a failure status to a message that is honest about what is knowable. */
function explain(status: number, requestId: string | null): FederationExchangeError {
  const rid = requestId ? ` (request-id ${requestId})` : '';
  if (status === 401) {
    // EVERY assertion denial is this same opaque 401 with the fixed message
    // "Authentication failed" — issuer mismatch, audience mismatch, CEL miss,
    // expired JWT, archived rule, missing workspace_id. Deliberately
    // indistinguishable so a caller cannot probe rule configuration. The reason
    // exists ONLY server-side, in Console > Settings > Workload identity >
    // History. Do not guess from out here: external probing produced two
    // confident wrong diagnoses during the macOS work while that page had the
    // true cause both times.
    return new FederationExchangeError(
      'Anthropic refused the sign-in. Your account may not be assigned the shotAI role yet. ' +
        'Administrators: the deny reason is in Claude Console > Settings > Workload identity > History' +
        `${rid}.`,
      status,
      requestId,
    );
  }
  if (status === 400) {
    // Rejected before the organization is corroborated, so NO history entry is
    // written. This is shotAI's configuration being wrong, not the user's account.
    return new FederationExchangeError(
      "shotAI's federation settings are invalid (rule, organization, or workspace id)." + rid,
      status,
      requestId,
    );
  }
  if (status === 429) {
    // Ambiguous under federation: transient throttle or a spent budget. There is
    // no documented error type for a spend cap, so do not assert a cause.
    return new FederationExchangeError(
      'Anthropic is rate limiting sign-in right now. Try again shortly.' + rid,
      status,
      requestId,
    );
  }
  if (status >= 500) {
    return new FederationExchangeError(
      `Anthropic sign-in is temporarily unavailable (HTTP ${status}).` + rid,
      status,
      requestId,
    );
  }
  return new FederationExchangeError(`Anthropic sign-in failed (HTTP ${status}).` + rid, status, requestId);
}

/**
 * Decode a JWT payload for a LOCAL, ADVISORY role check. Never a security gate —
 * the CEL condition on the federation rule is the authority. Returns null when the
 * token cannot be parsed, so the caller attempts the exchange anyway (fail open):
 * a parse bug must not lock a correctly-assigned user out.
 */
export function hasAppRole(jwt: string, role: string): boolean | null {
  try {
    const part = jwt.split('.')[1];
    if (!part) return null;
    const json = Buffer.from(part.replace(/-/g, '+').replace(/_/g, '/'), 'base64').toString('utf8');
    const payload = JSON.parse(json) as { roles?: unknown };
    // Entra OMITS `roles` entirely for an unassigned user rather than sending an
    // empty array, so absence is a usable signal and not an unknown.
    if (!Array.isArray(payload.roles)) return false;
    return payload.roles.includes(role);
  } catch {
    return null;
  }
}

/** The app role the federation rule's CEL condition requires. */
export const REQUIRED_APP_ROLE = 'shotAI.User';
