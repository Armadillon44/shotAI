import { describe, it, expect } from 'vitest';
import fs from 'node:fs';
import { validateFederationConfig, type RawFederationConfig } from './config-validate';

// federation.local.json is gitignored and per-developer, so this suite SKIPS on
// every clone that has none (including CI and every external contributor). When a
// developer does have one, a typo in it must fail HERE — at `npm test`, naming the
// offending key — rather than as the opaque 401 that every assertion denial
// returns, which is indistinguishable from a missing app role and cost the macOS
// effort two confident wrong diagnoses.
//
// Nothing in here prints a value. They are not credentials, but they identify one
// organization's tenant and Anthropic org, and this output can land in a CI log.
const LOCAL = 'src/main/entra/federation.local.json';
const present = fs.existsSync(LOCAL);

describe.skipIf(!present)('federation.local.json', () => {
  const read = (): RawFederationConfig =>
    JSON.parse(fs.readFileSync(LOCAL, 'utf8')) as RawFederationConfig;

  it('is valid JSON', () => {
    expect(() => read()).not.toThrow();
  });

  it('passes validation', () => {
    const r = validateFederationConfig(read());
    // Report key NAMES only, never values.
    const why = r.ok ? '' : `missing: [${r.missing.join(', ')}] invalid: [${r.invalid.join(', ')}]`;
    expect(r.ok, why).toBe(true);
  });

  it('carries a bare audience app-ID GUID, with no api:// prefix', () => {
    // The macOS work records this as a real trap: the federation rule's expected
    // audience must be the bare GUID. An api:// value fails the GUID check, so
    // this is already covered — asserted explicitly so the reason is on record.
    const raw = read();
    expect(String(raw.AudienceAppId ?? '')).not.toContain('api://');
  });

  it('resolves a client app id (falling back to the audience registration)', () => {
    const r = validateFederationConfig(read());
    expect(r.ok).toBe(true);
    if (!r.ok) return;
    expect(r.config.clientAppId).toBeTruthy();
    if (!raw_has(read(), 'ClientAppId')) {
      // The deployed topology: one registration is both client and audience.
      expect(r.config.clientAppId).toBe(r.config.audienceAppId);
    }
  });
});

function raw_has(raw: RawFederationConfig, key: keyof RawFederationConfig): boolean {
  const v = raw[key];
  return typeof v === 'string' && v.trim().length > 0;
}
