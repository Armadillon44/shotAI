// An INetworkModule for @azure/msal-node backed by an injected fetch (#63).
//
// WHY THIS EXISTS. msal-node v5 REMOVED `proxyUrl` and `customAgentOptions` from
// NodeSystemOptions, which #63 records as the single biggest v5 adoption risk: a
// fleet behind a corporate proxy would fail sign-in with no knob to turn. Verified
// against the installed 5.6.0 types, both options are indeed gone — but
// `NodeSystemOptions.networkClient?: INetworkModule` is present, and the interface
// is only two methods. So the mitigation is a first-class configuration option
// rather than a fork or a version pin.
//
// The app passes Electron's `net.fetch`, which uses Chromium's stack and therefore
// honors the Windows system proxy, WPAD/PAC, and the Windows certificate store.
// Node's global fetch (undici) trusts only its own bundled CA set and fails
// opaquely behind a TLS-inspecting proxy — indistinguishable from a broken
// federation rule. This gives MSAL and the token exchange one shared egress path.
import type { INetworkModule, NetworkRequestOptions, NetworkResponse } from '@azure/msal-node';

/** Minimal fetch shape both undici and Electron's net.fetch satisfy. */
export type NetFetch = (
  url: string,
  init: {
    method: string;
    headers?: Record<string, string>;
    body?: string;
    signal?: AbortSignal;
  },
) => Promise<{
  status: number;
  headers: { forEach(cb: (value: string, key: string) => void): void };
  text(): Promise<string>;
}>;

/** Electron's net.fetch requires app.whenReady(); MSAL only calls this lazily,
 *  on the first sign-in, which is well after ready. */
export function createNetworkModule(fetchImpl: NetFetch): INetworkModule {
  return {
    sendGetRequestAsync: <T>(url: string, options?: NetworkRequestOptions, timeout?: number) =>
      send<T>(fetchImpl, 'GET', url, options, timeout),
    sendPostRequestAsync: <T>(url: string, options?: NetworkRequestOptions) =>
      send<T>(fetchImpl, 'POST', url, options),
  };
}

async function send<T>(
  fetchImpl: NetFetch,
  method: 'GET' | 'POST',
  url: string,
  options?: NetworkRequestOptions,
  timeout?: number,
): Promise<NetworkResponse<T>> {
  // MSAL passes a timeout only on GET (metadata/discovery). Honor it, and always
  // clear the timer so a fast response cannot leave a pending handle behind.
  const controller = typeof timeout === 'number' && timeout > 0 ? new AbortController() : null;
  const timer = controller ? setTimeout(() => controller.abort(), timeout) : null;
  try {
    const resp = await fetchImpl(url, {
      method,
      headers: options?.headers,
      // Never send a body on GET: some proxies reject it outright.
      body: method === 'POST' ? (options?.body ?? '') : undefined,
      signal: controller?.signal,
    });

    const headers: Record<string, string> = {};
    resp.headers.forEach((value, key) => {
      headers[key.toLowerCase()] = value;
    });

    const raw = await resp.text();
    return {
      headers,
      status: resp.status,
      // CRITICAL: parse and return the body for EVERY status, and never throw on a
      // non-2xx. Entra returns its error detail as JSON on 4xx, and MSAL reads
      // body.error / body.suberror to decide whether a silent call needs
      // interaction. Throwing here would collapse InteractionRequiredAuthError
      // into a generic network failure and break the silent-then-interactive
      // ladder the whole sign-in flow depends on.
      body: parseBody<T>(raw),
    };
  } finally {
    if (timer) clearTimeout(timer);
  }
}

/** JSON when the body is JSON, the raw string otherwise. MSAL only ever reads
 *  fields off JSON responses; a non-JSON body means something upstream (a proxy
 *  login page, an HTML error) answered instead, and handing MSAL the text keeps
 *  that visible rather than turning it into a parse crash. */
function parseBody<T>(raw: string): T {
  if (!raw) return {} as T;
  try {
    return JSON.parse(raw) as T;
  } catch {
    return raw as unknown as T;
  }
}
