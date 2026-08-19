import { describe, it, expect } from 'vitest';
import {
  isUnconfigured,
  validateFederationConfig,
  type RawFederationConfig,
} from './config-validate';

/**
 * A complete, valid delivery — the SIX values actually deployed today, where one
 * Entra registration is both the client and the audience. ClientAppId is added
 * only by the tests that cover the split-registration topology.
 */
const full = (over: RawFederationConfig = {}): RawFederationConfig => ({
  TenantId: '3f2504e0-4f89-11d3-9a0c-0305e82c3301',
  AudienceAppId: 'aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee',
  FederationRuleId: 'fdrl_01ABCdef',
  OrganizationId: '00000000-0000-0000-0000-000000000000',
  ServiceAccountId: 'svac_01XYZ789',
  ...over,
});

describe('validateFederationConfig — happy path', () => {
  it('accepts the six required values and omits absent optionals', () => {
    const r = validateFederationConfig(full());
    expect(r.ok).toBe(true);
    if (!r.ok) return;
    expect(r.config.tenantId).toBe('3f2504e0-4f89-11d3-9a0c-0305e82c3301');
    expect(r.config.federationRuleId).toBe('fdrl_01ABCdef');
    expect(r.config.serviceAccountId).toBe('svac_01XYZ789');
    expect(r.config.workspaceId).toBeUndefined();
    expect(r.config.supportUrl).toBeUndefined();
  });

  it('defaults clientAppId to the audience id when none is delivered', () => {
    // The deployed topology: one registration is both client and audience, which
    // is what macOS ships, so both platforms share one Entra object.
    const r = validateFederationConfig(full());
    expect(r.ok).toBe(true);
    if (!r.ok) return;
    expect(r.config.clientAppId).toBe(r.config.audienceAppId);
  });

  it('uses a separately delivered ClientAppId, so the split topology needs no code change', () => {
    const r = validateFederationConfig(
      full({ ClientAppId: '11111111-2222-3333-4444-555555555555' }),
    );
    expect(r.ok).toBe(true);
    if (!r.ok) return;
    expect(r.config.clientAppId).toBe('11111111-2222-3333-4444-555555555555');
    expect(r.config.clientAppId).not.toBe(r.config.audienceAppId);
  });

  it('accepts a valid workspace id and https support url', () => {
    const r = validateFederationConfig(
      full({ WorkspaceId: 'wrkspc_01AAA', SupportUrl: 'https://help.example.com/shotai' }),
    );
    expect(r.ok).toBe(true);
    if (!r.ok) return;
    expect(r.config.workspaceId).toBe('wrkspc_01AAA');
    expect(r.config.supportUrl).toBe('https://help.example.com/shotai');
  });

  it('trims whitespace and strips the braces Windows tooling adds to GUIDs', () => {
    const r = validateFederationConfig(
      full({ TenantId: '  {3f2504e0-4f89-11d3-9a0c-0305e82c3301}  ' }),
    );
    expect(r.ok).toBe(true);
    if (!r.ok) return;
    expect(r.config.tenantId).toBe('3f2504e0-4f89-11d3-9a0c-0305e82c3301');
  });
});

describe('validateFederationConfig — fails closed', () => {
  const requiredKeys = [
    'TenantId',
    'AudienceAppId',
    'FederationRuleId',
    'OrganizationId',
    'ServiceAccountId',
  ] as const;

  it.each(requiredKeys)('rejects the whole config when %s is missing', (key) => {
    const raw = full();
    delete raw[key];
    const r = validateFederationConfig(raw);
    expect(r.ok).toBe(false);
    if (r.ok) return;
    expect(r.missing).toContain(key);
  });

  it.each(requiredKeys)('treats a blank %s as missing, not malformed', (key) => {
    const r = validateFederationConfig(full({ [key]: '   ' }));
    expect(r.ok).toBe(false);
    if (r.ok) return;
    // A cleared policy value must read exactly like "never delivered" so an
    // administrator unsetting it reverts to BYO-key rather than erroring.
    expect(r.missing).toContain(key);
    expect(r.invalid).not.toContain(key);
  });

  it('rejects a non-GUID tenant id', () => {
    const r = validateFederationConfig(full({ TenantId: 'contoso.onmicrosoft.com' }));
    expect(r.ok).toBe(false);
    if (r.ok) return;
    expect(r.invalid).toContain('TenantId');
  });

  it('rejects tagged ids with the wrong prefix', () => {
    const r = validateFederationConfig(
      full({ FederationRuleId: 'svac_01ABC', ServiceAccountId: 'fdrl_01ABC' }),
    );
    expect(r.ok).toBe(false);
    if (r.ok) return;
    expect(r.invalid).toEqual(expect.arrayContaining(['FederationRuleId', 'ServiceAccountId']));
  });

  it('rejects a bare prefix with no body', () => {
    const r = validateFederationConfig(full({ FederationRuleId: 'fdrl_' }));
    expect(r.ok).toBe(false);
    if (r.ok) return;
    expect(r.invalid).toContain('FederationRuleId');
  });

  it('never returns a PARTIAL config when several values are bad', () => {
    const r = validateFederationConfig({ TenantId: 'nope', FederationRuleId: 'fdrl_ok' });
    expect(r.ok).toBe(false);
    if (r.ok) return;
    // Half a policy must not yield half a federation.
    expect(r.missing.length).toBeGreaterThan(0);
    expect(r.invalid).toContain('TenantId');
  });

  it('fails closed on a malformed ClientAppId even though it is optional', () => {
    // Aiming sign-in at a client that does not exist surfaces as an opaque
    // AADSTS error in the browser, with nothing pointing back at the config.
    const r = validateFederationConfig(full({ ClientAppId: 'not-a-guid' }));
    expect(r.ok).toBe(false);
    if (r.ok) return;
    expect(r.invalid).toContain('ClientAppId');
  });

  it('fails closed on a malformed WorkspaceId even though it is optional', () => {
    // It takes part in the exchange, so a typo yields the same opaque 401 as a
    // missing app role and would be misdiagnosed as an entitlement problem.
    const r = validateFederationConfig(full({ WorkspaceId: 'workspace-1' }));
    expect(r.ok).toBe(false);
    if (r.ok) return;
    expect(r.invalid).toContain('WorkspaceId');
  });
});

describe('validateFederationConfig — SupportUrl degrades, never blocks', () => {
  it('drops a malformed support url but still returns a usable config', () => {
    const r = validateFederationConfig(full({ SupportUrl: 'not a url' }));
    expect(r.ok).toBe(true);
    if (!r.ok) return;
    expect(r.config.supportUrl).toBeUndefined();
  });

  it('drops a non-https support url', () => {
    const r = validateFederationConfig(full({ SupportUrl: 'http://help.example.com' }));
    expect(r.ok).toBe(true);
    if (!r.ok) return;
    expect(r.config.supportUrl).toBeUndefined();
  });

  it('drops a dangerous scheme rather than handing it to openExternal', () => {
    const r = validateFederationConfig(full({ SupportUrl: 'javascript:alert(1)' }));
    expect(r.ok).toBe(true);
    if (!r.ok) return;
    expect(r.config.supportUrl).toBeUndefined();
  });
});

describe('isUnconfigured', () => {
  it('is true when nothing was delivered (every external user)', () => {
    expect(isUnconfigured({})).toBe(true);
  });

  it('is true when every value is present but blank', () => {
    expect(isUnconfigured({ TenantId: '', ClientAppId: '   ' })).toBe(true);
  });

  it('is false for a partial delivery, which an administrator must see', () => {
    expect(isUnconfigured({ TenantId: '3f2504e0-4f89-11d3-9a0c-0305e82c3301' })).toBe(false);
  });

  it('is false for a complete delivery', () => {
    expect(isUnconfigured(full())).toBe(false);
  });
});
