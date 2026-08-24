// How shotAI decides which credential to authenticate with (#63), and how it builds
// the Anthropic client for each.
//
// Electron-free and fully injected — the credential readers, the logger, the fetch,
// the browser opener and the cache plugin all come in as dependencies. That is what
// lets scripts/wif-probe.mjs drive THIS factory under plain node and prove the
// production auth path, instead of a parallel imitation that can drift.
// claude-auth.ts is the thin shell that supplies the Electron versions.
//
// BYO-key stays the default and the fallback: this repo is public and external users
// have no Entra tenant, so with nothing configured the app behaves exactly as it
// always has.
import Anthropic from '@anthropic-ai/sdk';
import { oidcFederationProvider } from '@anthropic-ai/sdk/lib/credentials/oidc-federation';
import type { ICachePlugin } from '@azure/msal-node';
import { createEntraClient, SignInRequiredError, type EntraClient } from './msal';
import { createNetworkModule } from './net-module';
import type { FederationConfig } from './config-validate';

// Pin the Anthropic egress host. Without an explicit baseURL the SDK defaults to
// process.env.ANTHROPIC_BASE_URL, which would let a poisoned environment redirect
// the credential AND the captured screenshots to an attacker host.
export const ANTHROPIC_BASE_URL = 'https://api.anthropic.com';

export type AuthMode = 'federated' | 'apiKey';

export interface AuthDeps {
  /** Used for the TOKEN EXCHANGE and MSAL only. The app passes Electron's net.fetch
   *  so the Windows system proxy, WPAD/PAC, and the Windows certificate store apply
   *  to sign-in; the probe passes global fetch. Deliberately NOT the client's fetch
   *  for API traffic: routing SSE streaming through net.fetch is untested (#63 item
   *  3), and the BYO-key path already uses the SDK default, so leaving API traffic
   *  alone is the status quo rather than a new risk. */
  fetchImpl: typeof fetch;
  openBrowser: (url: string) => Promise<void>;
  /** Omit for a memory-only session (the probe signs in fresh each run). */
  cachePlugin?: ICachePlugin;
  /** null when federation is unconfigured or was rejected. */
  getFederationConfig: () => Promise<FederationConfig | null>;
  /** The BYO key, or null. Never logged, never returned to the renderer. */
  getApiKey: () => Promise<string | null>;
  log?: (line: string) => void;
}

export interface Auth {
  federation(): Promise<FederationConfig | null>;
  /** Null when federation is unavailable on this machine. */
  entra(): Promise<EntraClient | null>;
  /** True when a usable Entra account is cached. */
  isSignedIn(): Promise<boolean>;
  /** A ready client plus the mode it authenticated with. Throws
   *  SignInRequiredError naming the option that applies when neither is available. */
  makeClient(): Promise<{ client: Anthropic; mode: AuthMode }>;
}

export function createAuth(deps: AuthDeps): Auth {
  let entraClient: EntraClient | null = null;
  let builtFor: string | null = null;

  const entraFrom = (cfg: FederationConfig): EntraClient => {
    // Rebuild only when the effective client id changed (a policy correction).
    if (!entraClient || builtFor !== cfg.clientAppId) {
      entraClient = createEntraClient(cfg, {
        openBrowser: deps.openBrowser,
        networkClient: createNetworkModule(deps.fetchImpl),
        cachePlugin: deps.cachePlugin,
        log: deps.log,
      });
      builtFor = cfg.clientAppId;
    }
    return entraClient;
  };

  const federation = () => deps.getFederationConfig();

  const entra = async (): Promise<EntraClient | null> => {
    const cfg = await federation();
    return cfg ? entraFrom(cfg) : null;
  };

  const isSignedIn = async (): Promise<boolean> => {
    const c = await entra();
    return c ? !!(await c.signedInAccount()) : false;
  };

  return {
    federation,
    entra,
    isSignedIn,

    async makeClient(): Promise<{ client: Anthropic; mode: AuthMode }> {
      const cfg = await federation();
      // Federation is only usable once somebody has signed in on this machine. A
      // configured-but-signed-out machine falls back to a key when one exists, so a
      // rollout cannot strand a user who already had one.
      const signedIn = cfg ? !!(await entraFrom(cfg).signedInAccount()) : false;
      const key = signedIn ? null : await deps.getApiKey();

      if (!signedIn && !key) {
        throw new SignInRequiredError(
          cfg
            ? 'Sign in with your Microsoft account to use Claude.'
            : 'Add an Anthropic API key in Settings to use Claude.',
        );
      }

      const client = new Anthropic({
        baseURL: ANTHROPIC_BASE_URL,
        // LOAD-BEARING EXPLICIT NULLS. `apiKey` defaults from
        // process.env.ANTHROPIC_API_KEY and takes PRECEDENCE over `credentials`, so a
        // leftover env var on a developer machine would silently disable federation.
        // Even ANTHROPIC_API_KEY="" occupies its precedence slot.
        apiKey: signedIn ? null : key,
        authToken: null,
        ...(signedIn && cfg
          ? {
              // The client wraps this provider in its own TokenCache: advisory
              // refresh at exp-120s, mandatory at exp-30s, concurrent coalescing,
              // and one invalidate-and-retry on a 401. So there is no timer to
              // schedule here, which is the right answer on Windows: a setTimeout
              // does not advance across S3 suspend, and NTP correction or a VM
              // resume can move the clock either way.
              credentials: oidcFederationProvider({
                // Silent ONLY. This can fire from a BACKGROUND refresh, where an
                // interactive prompt would pop a browser with no user context.
                identityTokenProvider: () => entraFrom(cfg).acquireSilent(),
                federationRuleId: cfg.federationRuleId,
                organizationId: cfg.organizationId,
                serviceAccountId: cfg.serviceAccountId,
                workspaceId: cfg.workspaceId,
                // Both are REQUIRED fields on OIDCFederationConfig. #63's snippet
                // omits `fetch`; without it this does not compile.
                baseURL: ANTHROPIC_BASE_URL,
                fetch: deps.fetchImpl,
              }),
            }
          : {}),
      });

      return { client, mode: signedIn ? 'federated' : 'apiKey' };
    },
  };
}
