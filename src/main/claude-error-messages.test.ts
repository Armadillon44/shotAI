// The auth-mode error messages, and the property that they are actually PAIRED
// to the mode they describe.
//
// `friendlyError` branches on `mode === 'federated'` to pick between two wordings
// for the same HTTP status, and the two give OPPOSITE advice: one sends the
// reader to check an Entra role, the other sends them to check an API key. The
// source comment already says the key wording is "actively WRONG under federation
// and would send users hunting for a key they never had" — but nothing enforced
// it. Inverting that one condition used to pass all 652 tests, which means the
// distinction the comment calls important was unguarded.
//
// Found by running a property the macOS side proposed: invert the classifier,
// run the suite, count failures. Zero failures means the distinction is not
// really being tested, however many tests exist nearby.
//
// So every test below asserts BOTH directions of a pair. A test that only checked
// the federated wording would still pass with the branch inverted, because the
// key wording would simply never be asserted.
import { describe, it, expect, vi } from 'vitest';
import Anthropic from '@anthropic-ai/sdk';

vi.mock('electron', () => ({ shell: { showItemInFolder: () => undefined } }));
vi.mock('./settings', () => ({
  getProjectsDir: async () => '',
  getRecents: async () => [],
  addRecent: async () => undefined,
  setRecents: async () => undefined,
  persistProjectsDir: async () => undefined,
  getSopSettings: async () => ({}),
  getReportByline: async () => '',
}));

import { friendlyError } from './claude-service';

const H = (): Headers => new Headers();
const auth = (): Error => new Anthropic.AuthenticationError(401, { type: 'error' }, 'denied', H());
const perm = (): Error =>
  new Anthropic.PermissionDeniedError(403, { type: 'error' }, 'denied', H());
const missing = (): Error => new Anthropic.NotFoundError(404, { type: 'error' }, 'missing', H());

describe('a 401 names the credential the user actually has', () => {
  it('points a signed-in user at their role, and a key user at their key', () => {
    const fed = friendlyError(auth(), 'federated');
    const key = friendlyError(auth(), 'apiKey');

    expect(fed).toMatch(/shotAI role/i);
    expect(fed, 'a signed-in user has no key to go and check').not.toMatch(/API key/i);

    expect(key).toMatch(/Invalid API key/i);
    expect(key).not.toMatch(/shotAI role/i);

    expect(fed).not.toBe(key);
  });
});

describe('403 and 404 name the right credential too', () => {
  it('attributes a permission denial to the federation scope or to the key', () => {
    const fed = friendlyError(perm(), 'federated');
    const key = friendlyError(perm(), 'apiKey');

    expect(fed).toMatch(/federation rule/i);
    expect(fed).not.toMatch(/\bkey\b/i);
    expect(key).toMatch(/\bkey\b/i);
    expect(key).not.toMatch(/federation/i);
    expect(fed).not.toBe(key);
  });

  it('attributes an unavailable model to the sign-in or to the key', () => {
    const fed = friendlyError(missing(), 'federated');
    const key = friendlyError(missing(), 'apiKey');

    expect(fed).toMatch(/sign-in/i);
    expect(fed).not.toMatch(/\bkey\b/i);
    expect(key).toMatch(/\bkey\b/i);
    expect(key).not.toMatch(/sign-in/i);
    expect(fed).not.toBe(key);
  });
});

describe('ordering invariants the comments claim', () => {
  it('reports offline as offline, in BOTH modes, rather than as a credential problem', () => {
    // APIConnectionError extends APIError, so the connection branch has to come
    // first or a dropped network reads as an auth failure and sends the user to
    // re-check a credential that is fine. Verified: `new APIConnectionError(...)
    // instanceof Anthropic.APIError` is true.
    const offline = new Anthropic.APIConnectionError({ message: 'socket hang up' });
    for (const mode of ['federated', 'apiKey'] as const) {
      const msg = friendlyError(offline, mode);
      expect(msg, `${mode} mode`).toMatch(/network connection/i);
      expect(msg, 'offline must not read as a credential failure').not.toMatch(
        /key|role|sign-in/i,
      );
    }
  });

  it('falls through to the SDK wording for a status it does not classify', () => {
    // The fallthrough deliberately surfaces the SDK's own text rather than
    // inventing one. Worth pinning because it must NOT collide with the four
    // classified cases above — if a 500 ever started reading as a credential
    // problem, the user would go and re-check something that was never wrong.
    //
    // Asserted loosely on purpose: the SDK composes this string itself from the
    // status and body and ignores the message argument, so pinning exact text
    // would be pinning the SDK's formatting rather than our behaviour.
    const odd = new Anthropic.APIError(500, { type: 'error' }, 'ignored by the SDK', H());
    const msg = friendlyError(odd, 'federated');

    expect(msg).toContain('500');
    expect(msg).not.toMatch(/key|role|sign-in|network connection/i);
  });
});
