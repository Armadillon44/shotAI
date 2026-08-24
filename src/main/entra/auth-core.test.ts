import { describe, it, expect } from 'vitest';
import { createAuth, type AuthDeps } from './auth-core';
import type { FederationConfig } from './config-validate';

const cfg: FederationConfig = {
  tenantId: 'a651293b-0000-0000-0000-000000000000',
  clientAppId: '78259208-0000-0000-0000-000000000000',
  audienceAppId: '78259208-0000-0000-0000-000000000000',
  federationRuleId: 'fdrl_test',
  organizationId: '97bb1c8a-0000-0000-0000-000000000000',
  serviceAccountId: 'svac_test',
};

/** Never reached in these tests — assert that, rather than allowing a real call. */
const failingFetch = (async () => {
  throw new Error('no test should reach the network');
}) as unknown as typeof fetch;

const deps = (over: Partial<AuthDeps> = {}): AuthDeps => ({
  fetchImpl: failingFetch,
  openBrowser: async () => {
    throw new Error('no test should open a browser');
  },
  getFederationConfig: async () => null,
  getApiKey: async () => null,
  ...over,
});

describe('createAuth — mode selection', () => {
  it('reports federation unavailable when nothing is configured', async () => {
    const auth = createAuth(deps());
    expect(await auth.federation()).toBeNull();
    expect(await auth.entra()).toBeNull();
    expect(await auth.isSignedIn()).toBe(false);
  });

  it('builds an API-key client when a key exists and federation is unconfigured', async () => {
    const auth = createAuth(deps({ getApiKey: async () => 'sk-ant-test' }));
    const { mode } = await auth.makeClient();
    expect(mode).toBe('apiKey');
  });

  it('refuses with a KEY-shaped message when nothing is configured and no key exists', async () => {
    const auth = createAuth(deps());
    // Naming the wrong remedy is the bug this wording exists to avoid: an external
    // user with no tenant must not be told to sign in with Microsoft.
    await expect(auth.makeClient()).rejects.toThrow(/API key/i);
  });

  it('refuses with a SIGN-IN message when federation IS configured but nobody signed in', async () => {
    const auth = createAuth(deps({ getFederationConfig: async () => cfg }));
    await expect(auth.makeClient()).rejects.toThrow(/Microsoft/i);
  });

  it('falls back to a stored key on a federation-configured machine, so a rollout strands nobody', async () => {
    const auth = createAuth(
      deps({ getFederationConfig: async () => cfg, getApiKey: async () => 'sk-ant-test' }),
    );
    const { mode } = await auth.makeClient();
    expect(mode).toBe('apiKey');
  });
});

describe('verifyFederation — leg isolation', () => {
  it('reports the signIn leg when federation is not configured at all', async () => {
    const r = await createAuth(deps()).verifyFederation();
    expect(r.ok).toBe(false);
    if (r.ok) return;
    expect(r.leg).toBe('signIn');
    expect(String((r.error as Error).message)).toMatch(/not set up for Microsoft sign-in/i);
  });

  it('reports the signIn leg when nobody has signed in, without touching the network', async () => {
    // MSAL is constructed lazily and has no cached account here, so acquireSilent
    // throws SignInRequiredError before any exchange is attempted. If the exchange
    // were reached, failingFetch would surface as a DIFFERENT leg.
    const r = await createAuth(deps({ getFederationConfig: async () => cfg })).verifyFederation();
    expect(r.ok).toBe(false);
    if (r.ok) return;
    expect(r.leg).toBe('signIn');
  });

  it('never reports the exchange leg before the signIn leg has passed', async () => {
    // Ordering matters for diagnosis: an exchange failure means "the rule refused a
    // real token", which is a completely different remedy from "sign in first".
    const r = await createAuth(deps({ getFederationConfig: async () => cfg })).verifyFederation();
    expect(r.ok).toBe(false);
    if (r.ok) return;
    expect(r.leg).not.toBe('exchange');
  });
});
