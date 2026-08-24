// Delegated Microsoft Entra sign-in (#63). MAIN-ONLY.
//
// Auth code + PKCE, system browser, MSAL's built-in loopback listener on
// 127.0.0.1. Deliberately NOT an embedded BrowserWindow: an embedded webview cannot
// satisfy device-based Conditional Access (the real browser carries the Primary
// Refresh Token, a BrowserWindow does not), has no FIDO or Windows Hello support,
// and is the pattern Microsoft documents against. Hosting sign-in in our own window
// would also put the auth-code response, and potentially the token, inside a
// renderer origin.
//
// Everything platform-shaped is injected — the browser opener, the network client,
// the cache plugin — so scripts/wif-probe.mjs drives THIS module rather than a
// parallel copy that can drift from what ships.
import {
  InteractionRequiredAuthError,
  PublicClientApplication,
  type AccountInfo,
  type Configuration,
  type ICachePlugin,
  type INetworkModule,
} from '@azure/msal-node';
import type { FederationConfig } from './config-validate';

/** Thrown when no cached account can produce a token without user interaction.
 *  The UI turns this into a "Sign in" button; it is never an error dialog. */
export class SignInRequiredError extends Error {
  /** A Conditional Access claims challenge to replay on the interactive call, so
   *  the prompt satisfies exactly the policy that failed (MFA, sign-in frequency)
   *  instead of a generic re-login. */
  claims?: string;

  constructor(message = 'Sign in with your Microsoft account to use Claude.') {
    super(message);
    this.name = 'SignInRequiredError';
  }
}

export interface EntraDeps {
  /** The app passes shell.openExternal. Must be the SYSTEM browser. */
  openBrowser: (url: string) => Promise<void>;
  /** The app passes an Electron-net-backed module so the Windows system proxy and
   *  certificate store apply. Omitting it falls back to MSAL's own client, which in
   *  v5 has no proxy configuration at all. */
  networkClient?: INetworkModule;
  /** Omit for a memory-only session (the probe does). */
  cachePlugin?: ICachePlugin;
  log?: (line: string) => void;
}

export interface EntraClient {
  /** The cached account, or null when nobody has signed in on this machine. */
  signedInAccount(): Promise<AccountInfo | null>;
  /** Silent ONLY. Throws SignInRequiredError rather than prompting. */
  acquireSilent(opts?: { forceRefresh?: boolean; claims?: string }): Promise<string>;
  /** User-initiated only: a click in Settings, or the Sign in button on an error. */
  signInInteractive(claims?: string): Promise<string>;
  signOut(): Promise<void>;
}

/** The delegated scope. Explicit, NOT `.default`: when a client requests its own
 *  `.default` the endpoint can return an id_token instead of an access token, and
 *  we want this audience's scope specifically rather than Graph's. */
export function scopeFor(cfg: FederationConfig): string {
  return `api://${cfg.audienceAppId}/user_impersonation`;
}

export function createEntraClient(cfg: FederationConfig, deps: EntraDeps): EntraClient {
  // Cache the PROMISE, not the instance: two concurrent callers must not each
  // construct a PublicClientApplication over the same cache file.
  let pcaPromise: Promise<PublicClientApplication> | null = null;

  const client = (): Promise<PublicClientApplication> => {
    if (!pcaPromise) {
      const config: Configuration = {
        auth: {
          clientId: cfg.clientAppId,
          authority: `https://login.microsoftonline.com/${cfg.tenantId}`,
          // NO redirectUri. acquireTokenInteractive THROWS if request.redirectUri is
          // set without a native broker plugin, and MSAL's loopback client owns the
          // URI it builds (http://localhost:<ephemeral port>).
        },
        ...(deps.cachePlugin ? { cache: { cachePlugin: deps.cachePlugin } } : {}),
        ...(deps.networkClient ? { system: { networkClient: deps.networkClient } } : {}),
        // Do NOT set clientCapabilities: ['CP1']. In a CAE session Entra extends the
        // access token to up to 28 hours, which exceeds the Anthropic issuer's
        // maximum JWT lifetime and would make EVERY exchange fail. A custom audience
        // is not a CAE-enabled resource anyway.
      };
      pcaPromise = Promise.resolve(new PublicClientApplication(config));
    }
    return pcaPromise;
  };

  const firstAccount = async (): Promise<AccountInfo | null> => {
    const all = await (await client()).getTokenCache().getAllAccounts();
    return all[0] ?? null;
  };

  return {
    signedInAccount: firstAccount,

    async acquireSilent(opts = {}): Promise<string> {
      const pca = await client();
      const account = await firstAccount();
      if (!account) throw new SignInRequiredError();
      try {
        const res = await pca.acquireTokenSilent({
          account,
          scopes: [scopeFor(cfg)],
          // forceRefresh redeems the refresh token for fresh CLAIMS. This is the fix
          // for the retry trap: right after IT grants the app role, the cached
          // 60-90 minute access token predates the assignment and carries no roles
          // claim, so a plain retry keeps failing and looks like a broken rule.
          forceRefresh: opts.forceRefresh,
          claims: opts.claims,
        });
        if (!res?.accessToken) throw new SignInRequiredError();
        return res.accessToken;
      } catch (e) {
        if (e instanceof InteractionRequiredAuthError) {
          const err = new SignInRequiredError();
          // Carry the CA challenge through to the interactive prompt.
          const claims = (e as { claims?: unknown }).claims;
          if (typeof claims === 'string' && claims) err.claims = claims;
          throw err;
        }
        throw e;
      }
    },

    async signInInteractive(claims?: string): Promise<string> {
      const pca = await client();
      const res = await pca.acquireTokenInteractive({
        scopes: [scopeFor(cfg)],
        claims,
        // responseMode is left at MSAL's default (query). #63 recommends form_post,
        // but MSAL's built-in loopback server is built around query mode and whether
        // it parses a POSTed body is unverified. The code rides a localhost-only
        // request, is single-use, and is PKCE-bound.
        //
        // Do NOT pass `loopbackClient` (deprecated in msal-node 5.x); use
        // preferredPort if a fixed port ever needs registering.
        openBrowser: deps.openBrowser,
        // PLAIN TEXT, not HTML. MSAL's loopback server calls res.end(template)
        // with no Content-Type header at all, so a browser renders markup
        // literally — tags and everything. Its own default is prose for the same
        // reason ("Auth code was successfully acquired. You can close this window
        // now."). There is no API to set the content type, so the template has to
        // read correctly as plain text.
        successTemplate: 'Signed in to shotAI. You can close this tab and return to the app.',
        errorTemplate: 'shotAI sign-in failed. Close this tab and try again from the app.',
      });
      if (!res?.accessToken) throw new SignInRequiredError();
      deps.log?.('entra: interactive sign-in completed.');
      return res.accessToken;
    },

    async signOut(): Promise<void> {
      const cache = (await client()).getTokenCache();
      for (const a of await cache.getAllAccounts()) await cache.removeAccount(a);
      deps.log?.('entra: signed out.');
    },
  };
}
