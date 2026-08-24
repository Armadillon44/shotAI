import { describe, it, expect } from 'vitest';
import { createNetworkModule, type NetFetch } from './net-module';

interface Recorded {
  url: string;
  method: string;
  headers?: Record<string, string>;
  body?: string;
  hasSignal: boolean;
}

function fake(reply: {
  status?: number;
  text?: string;
  headers?: Record<string, string>;
  hang?: boolean;
}): { impl: NetFetch; calls: Recorded[] } {
  const calls: Recorded[] = [];
  const impl: NetFetch = async (url, init) => {
    calls.push({
      url,
      method: init.method,
      headers: init.headers,
      body: init.body,
      hasSignal: !!init.signal,
    });
    if (reply.hang) {
      // Resolve only when aborted, so the timeout path can be observed.
      await new Promise<void>((_res, rej) => {
        init.signal?.addEventListener('abort', () => rej(new Error('aborted')));
      });
    }
    const entries = Object.entries(reply.headers ?? { 'Content-Type': 'application/json' });
    return {
      status: reply.status ?? 200,
      headers: { forEach: (cb) => entries.forEach(([k, v]) => cb(v, k)) },
      text: async () => reply.text ?? '{"ok":true}',
    };
  };
  return { impl, calls };
}

describe('createNetworkModule', () => {
  it('implements both methods MSAL requires', () => {
    const m = createNetworkModule(fake({}).impl);
    expect(typeof m.sendGetRequestAsync).toBe('function');
    expect(typeof m.sendPostRequestAsync).toBe('function');
  });

  it('returns status, parsed body, and lowercased headers', async () => {
    const { impl } = fake({
      status: 200,
      text: '{"access_token":"a","expires_in":3600}',
      headers: { 'Content-Type': 'application/json', 'X-Ms-Request-Id': 'abc' },
    });
    const r = await createNetworkModule(impl).sendPostRequestAsync<{ access_token: string }>(
      'https://login.microsoftonline.com/t/oauth2/v2.0/token',
      { headers: { 'content-type': 'application/x-www-form-urlencoded' }, body: 'grant_type=x' },
    );
    expect(r.status).toBe(200);
    expect(r.body.access_token).toBe('a');
    // Lowercased so callers can look one up without guessing the casing.
    expect(r.headers['x-ms-request-id']).toBe('abc');
  });

  it('passes MSAL headers and body through unchanged on POST', async () => {
    const { impl, calls } = fake({});
    await createNetworkModule(impl).sendPostRequestAsync('https://x/token', {
      headers: { 'content-type': 'application/x-www-form-urlencoded' },
      body: 'grant_type=authorization_code&code=abc',
    });
    expect(calls[0].method).toBe('POST');
    expect(calls[0].headers).toEqual({ 'content-type': 'application/x-www-form-urlencoded' });
    expect(calls[0].body).toBe('grant_type=authorization_code&code=abc');
  });

  it('never sends a body on GET', async () => {
    const { impl, calls } = fake({});
    await createNetworkModule(impl).sendGetRequestAsync('https://x/.well-known/openid-configuration');
    // Some proxies reject a GET carrying a body outright.
    expect(calls[0].body).toBeUndefined();
  });

  it('does NOT throw on 4xx, and still returns the parsed error body', async () => {
    // This is the load-bearing behavior: Entra returns its detail as JSON on 4xx
    // and MSAL reads body.error / body.suberror to decide whether a silent call
    // needs interaction. Throwing would collapse InteractionRequiredAuthError into
    // a generic network failure and break the silent-then-interactive ladder.
    const { impl } = fake({
      status: 400,
      text: '{"error":"invalid_grant","suberror":"consent_required"}',
    });
    const r = await createNetworkModule(impl).sendPostRequestAsync<{
      error: string;
      suberror: string;
    }>('https://x/token', { body: 'a=1' });
    expect(r.status).toBe(400);
    expect(r.body.error).toBe('invalid_grant');
    expect(r.body.suberror).toBe('consent_required');
  });

  it('does not throw on 5xx either', async () => {
    const { impl } = fake({ status: 503, text: '{"error":"temporarily_unavailable"}' });
    const r = await createNetworkModule(impl).sendGetRequestAsync<{ error: string }>('https://x');
    expect(r.status).toBe(503);
    expect(r.body.error).toBe('temporarily_unavailable');
  });

  it('returns the raw text when the body is not JSON', async () => {
    // A proxy login page or an HTML error answered instead of the endpoint. Keep it
    // visible rather than turning it into a parse crash.
    const { impl } = fake({ status: 200, text: '<html>corporate proxy sign-in</html>' });
    const r = await createNetworkModule(impl).sendGetRequestAsync<string>('https://x');
    expect(r.body).toContain('corporate proxy');
  });

  it('returns an empty object for an empty body', async () => {
    const { impl } = fake({ status: 204, text: '' });
    const r = await createNetworkModule(impl).sendGetRequestAsync('https://x');
    expect(r.body).toEqual({});
  });

  it('only attaches an abort signal when a timeout was given', async () => {
    const { impl, calls } = fake({});
    const m = createNetworkModule(impl);
    await m.sendGetRequestAsync('https://x');
    expect(calls[0].hasSignal).toBe(false);
    await m.sendGetRequestAsync('https://x', undefined, 5000);
    expect(calls[1].hasSignal).toBe(true);
    // MSAL passes a timeout on GET only, so POST never carries one.
    await m.sendPostRequestAsync('https://x', { body: '' });
    expect(calls[2].hasSignal).toBe(false);
  });

  it('aborts a GET that exceeds its timeout', async () => {
    const { impl } = fake({ hang: true });
    await expect(
      createNetworkModule(impl).sendGetRequestAsync('https://x', undefined, 10),
    ).rejects.toThrow(/aborted/);
  });

  it('ignores a zero or negative timeout rather than aborting immediately', async () => {
    const { impl, calls } = fake({});
    await createNetworkModule(impl).sendGetRequestAsync('https://x', undefined, 0);
    expect(calls[0].hasSignal).toBe(false);
  });
});
