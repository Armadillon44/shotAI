import { describe, it, expect } from 'vitest';
import {
  FederationExchangeError,
  MAX_ASSERTION_BYTES,
  REQUIRED_APP_ROLE,
  exchangeAssertion,
  hasAppRole,
  type FetchLike,
} from './federation';
import type { FederationConfig } from './config-validate';

const cfg: FederationConfig = {
  tenantId: 'a651293b-0000-0000-0000-000000000000',
  clientAppId: '78259208-0000-0000-0000-000000000000',
  audienceAppId: '78259208-0000-0000-0000-000000000000',
  federationRuleId: 'fdrl_test',
  organizationId: '97bb1c8a-0000-0000-0000-000000000000',
  serviceAccountId: 'svac_test',
  workspaceId: 'wrkspc_test',
};

/** Records what was sent so the request shape can be asserted. */
function fakeFetch(
  reply: {
    ok?: boolean;
    status?: number;
    body?: unknown;
    text?: string;
    requestId?: string | null;
    throws?: Error;
  } = {},
): { impl: FetchLike; calls: Array<{ url: string; body: Record<string, unknown> }> } {
  const calls: Array<{ url: string; body: Record<string, unknown> }> = [];
  const impl: FetchLike = async (url, init) => {
    calls.push({ url, body: JSON.parse(init.body) as Record<string, unknown> });
    if (reply.throws) throw reply.throws;
    return {
      ok: reply.ok ?? true,
      status: reply.status ?? 200,
      headers: { get: () => reply.requestId ?? null },
      text: async () => reply.text ?? '',
      json: async () => reply.body ?? { access_token: 'sk-ant-oat01-abc', expires_in: 600 },
    };
  };
  return { impl, calls };
}

/** Await a rejection and return it narrowed. Using .catch() unions the error
 *  with the success type, which then fails to typecheck on .status/.message. */
async function caught(p: Promise<unknown>): Promise<FederationExchangeError> {
  let err: unknown;
  let resolved = false;
  try {
    await p;
    resolved = true;
  } catch (e) {
    err = e;
  }
  if (resolved) throw new Error('expected the exchange to reject, but it resolved');
  return err as FederationExchangeError;
}

const b64url = (o: unknown) =>
  Buffer.from(JSON.stringify(o), 'utf8')
    .toString('base64')
    .replace(/\+/g, '-')
    .replace(/\//g, '_')
    .replace(/=+$/, '');
const jwt = (payload: unknown) => `${b64url({ alg: 'RS256' })}.${b64url(payload)}.sig`;

describe('exchangeAssertion — success', () => {
  it('returns the minted token with an expiry derived from expires_in', async () => {
    const { impl } = fakeFetch({ body: { access_token: 'sk-ant-oat01-xyz', expires_in: 900, scope: 'workspace:inference' } });
    const before = Date.now();
    const r = await exchangeAssertion('assertion', cfg, { fetchImpl: impl });
    expect(r.token).toBe('sk-ant-oat01-xyz');
    expect(r.expiresInSeconds).toBe(900);
    expect(r.scope).toBe('workspace:inference');
    // Derived, never a constant: the mint is capped at 2x remaining JWT life, so a
    // stale assertion legitimately yields a shorter token than the rule's setting.
    expect(r.expiresAt).toBeGreaterThanOrEqual(before + 900_000);
  });

  it('sends the RFC 7523 grant with all four required ids', async () => {
    const { impl, calls } = fakeFetch();
    await exchangeAssertion('the-assertion', cfg, { fetchImpl: impl });
    expect(calls[0].url).toBe('https://api.anthropic.com/v1/oauth/token');
    expect(calls[0].body).toMatchObject({
      grant_type: 'urn:ietf:params:oauth:grant-type:jwt-bearer',
      assertion: 'the-assertion',
      federation_rule_id: 'fdrl_test',
      organization_id: '97bb1c8a-0000-0000-0000-000000000000',
      service_account_id: 'svac_test',
      workspace_id: 'wrkspc_test',
    });
  });

  it('OMITS workspace_id when the config has none, rather than sending null', async () => {
    const { impl, calls } = fakeFetch();
    const noWs: FederationConfig = { ...cfg };
    delete noWs.workspaceId;
    await exchangeAssertion('a', noWs, { fetchImpl: impl });
    expect('workspace_id' in calls[0].body).toBe(false);
  });

  it('logs status and duration but never the assertion or the token', async () => {
    const lines: string[] = [];
    const { impl } = fakeFetch({ body: { access_token: 'sk-ant-oat01-secret', expires_in: 600 } });
    await exchangeAssertion('SUPER-SECRET-ASSERTION', cfg, { fetchImpl: impl, log: (l) => lines.push(l) });
    const all = lines.join('\n');
    expect(all).toContain('expires_in=600');
    expect(all).not.toContain('SUPER-SECRET-ASSERTION');
    expect(all).not.toContain('sk-ant-oat01-secret');
  });
});

describe('exchangeAssertion — local guards', () => {
  it('refuses an empty assertion without making a request', async () => {
    const { impl, calls } = fakeFetch();
    await expect(exchangeAssertion('', cfg, { fetchImpl: impl })).rejects.toBeInstanceOf(
      FederationExchangeError,
    );
    expect(calls).toHaveLength(0);
  });

  it('refuses an oversized assertion locally and reports its real size', async () => {
    const { impl, calls } = fakeFetch();
    const big = 'x'.repeat(MAX_ASSERTION_BYTES + 1);
    await expect(exchangeAssertion(big, cfg, { fetchImpl: impl })).rejects.toThrow(/16 KiB/);
    // Locally, so an oversized token does not look like a rule problem.
    expect(calls).toHaveLength(0);
  });
});

describe('exchangeAssertion — failures', () => {
  it('reports a transport failure as unreachable, NOT as a refusal', async () => {
    const { impl } = fakeFetch({ throws: new Error('ECONNREFUSED') });
    await expect(exchangeAssertion('a', cfg, { fetchImpl: impl })).rejects.toThrow(
      /Could not reach Anthropic/,
    );
  });

  it('points a 401 at the Console History page and carries the request-id', async () => {
    const { impl } = fakeFetch({ ok: false, status: 401, requestId: 'req_123' });
    const err = await caught(exchangeAssertion('a', cfg, { fetchImpl: impl }));
    expect(err.status).toBe(401);
    expect(err.requestId).toBe('req_123');
    // The 401 is deliberately opaque server-side; the only real diagnosis is the
    // History page, and the request-id is what makes the entry findable.
    expect(err.message).toContain('Workload identity');
    expect(err.message).toContain('req_123');
  });

  it('blames shotAI config for a 400, not the user account', async () => {
    const { impl } = fakeFetch({ ok: false, status: 400 });
    const err = await caught(exchangeAssertion('a', cfg, { fetchImpl: impl }));
    expect(err.message).toMatch(/federation settings are invalid/);
    // A 400 is rejected before the org is corroborated, so no History entry
    // exists; sending an admin there would waste their time.
    expect(err.message).not.toContain('Workload identity');
  });

  it('does not assert a cause for 429', async () => {
    const { impl } = fakeFetch({ ok: false, status: 429 });
    const err = await caught(exchangeAssertion('a', cfg, { fetchImpl: impl }));
    // Transient throttle and a spent spend-cap are indistinguishable here.
    expect(err.message).toMatch(/rate limiting/);
  });

  it('treats 5xx as temporary', async () => {
    const { impl } = fakeFetch({ ok: false, status: 503 });
    await expect(exchangeAssertion('a', cfg, { fetchImpl: impl })).rejects.toThrow(/temporarily unavailable/);
  });

  it('NEVER leaks the response body into the error message', async () => {
    const { impl } = fakeFetch({
      ok: false,
      status: 400,
      text: 'organization_id=97bb1c8a leaked-echo-of-request',
    });
    const err = await caught(exchangeAssertion('a', cfg, { fetchImpl: impl }));
    // The body can echo request fields back, so it must never reach a user.
    expect(err.message).not.toContain('leaked-echo-of-request');
    expect(err.message).not.toContain('97bb1c8a');
  });

  it('rejects a 200 that is missing access_token or expires_in', async () => {
    for (const body of [{}, { access_token: 'tok' }, { access_token: 'tok', expires_in: 0 }, { access_token: 'tok', expires_in: 'soon' }]) {
      const { impl } = fakeFetch({ body });
      await expect(exchangeAssertion('a', cfg, { fetchImpl: impl })).rejects.toThrow(
        /missing access_token or expires_in/,
      );
    }
  });
});

describe('hasAppRole', () => {
  it('finds the required role', () => {
    expect(hasAppRole(jwt({ roles: ['shotAI.User'] }), REQUIRED_APP_ROLE)).toBe(true);
  });

  it('returns false when roles is absent, which is how Entra encodes unassigned', () => {
    // Entra OMITS the claim entirely rather than sending an empty array, so
    // absence is a real signal and not an unknown.
    expect(hasAppRole(jwt({ sub: 'abc' }), REQUIRED_APP_ROLE)).toBe(false);
  });

  it('returns false when roles exists but lacks the role', () => {
    expect(hasAppRole(jwt({ roles: ['Other.Role'] }), REQUIRED_APP_ROLE)).toBe(false);
  });

  it('handles base64url payloads containing - and _', () => {
    // A padded/standard-alphabet decode would corrupt these.
    const payload = { roles: ['shotAI.User'], note: 'a?b>c~d' + 'ÿ'.repeat(3) };
    expect(hasAppRole(jwt(payload), REQUIRED_APP_ROLE)).toBe(true);
  });

  it('returns null (fail OPEN) for an unparseable token', () => {
    // A parse bug must not lock out a correctly-assigned user; the CEL rule is
    // the authority, so the caller attempts the exchange anyway.
    expect(hasAppRole('not-a-jwt', REQUIRED_APP_ROLE)).toBeNull();
    expect(hasAppRole('', REQUIRED_APP_ROLE)).toBeNull();
    expect(hasAppRole('a.b.c', REQUIRED_APP_ROLE)).toBeNull();
  });
});
