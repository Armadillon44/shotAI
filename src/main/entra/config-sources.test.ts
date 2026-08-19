import { describe, it, expect } from 'vitest';
import {
  POLICY_KEY,
  parseRegQuery,
  pickBaked,
  readPolicyConfig,
  resolveFederation,
  type RegRunner,
} from './config-sources';
import { mergeFederationSources } from './config-validate';

/** Shape of real `reg query` output, including a foreign value and a wrong type. */
const REG_OUTPUT = [
  '',
  'HKEY_LOCAL_MACHINE\\SOFTWARE\\Policies\\shotAI\\Federation',
  '    TenantId    REG_SZ    3f2504e0-4f89-11d3-9a0c-0305e82c3301',
  '    AudienceAppId    REG_SZ    aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee',
  '    FederationRuleId    REG_SZ    fdrl_01ABCdef',
  '    OrganizationId    REG_SZ    11111111-1111-1111-1111-111111111111',
  '    ServiceAccountId    REG_SZ    svac_01XYZ789',
  '    Unrelated    REG_SZ    should be ignored',
  '    SomeFlag    REG_DWORD    0x1',
  '',
].join('\r\n');

const okBaked = {
  TenantId: '3f2504e0-4f89-11d3-9a0c-0305e82c3301',
  AudienceAppId: 'aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee',
  FederationRuleId: 'fdrl_baked',
  OrganizationId: '11111111-1111-1111-1111-111111111111',
  ServiceAccountId: 'svac_baked',
};

const runnerReturning = (out: string): RegRunner => async () => out;
const runnerThrowing: RegRunner = async () => {
  throw new Error('ERROR: The system was unable to find the specified registry key');
};

describe('POLICY_KEY', () => {
  it('reads the MACHINE hive, never the per-user one', () => {
    // HKCU is user-writable, so a standard user could repoint their own client at
    // another tenant or rule. HKLM is the integrity boundary.
    expect(POLICY_KEY.startsWith('HKLM')).toBe(true);
    expect(POLICY_KEY).not.toContain('HKCU');
  });

  it('lives under Policies, which ADMX ingestion permits', () => {
    expect(POLICY_KEY).toContain('SOFTWARE\\Policies\\shotAI');
  });
});

describe('parseRegQuery', () => {
  it('extracts every known REG_SZ value', () => {
    const r = parseRegQuery(REG_OUTPUT);
    expect(r.TenantId).toBe('3f2504e0-4f89-11d3-9a0c-0305e82c3301');
    expect(r.FederationRuleId).toBe('fdrl_01ABCdef');
    expect(r.ServiceAccountId).toBe('svac_01XYZ789');
  });

  it('ignores value names outside the contract', () => {
    expect(parseRegQuery(REG_OUTPUT)).not.toHaveProperty('Unrelated');
  });

  it('ignores non-REG_SZ types rather than coercing them', () => {
    // A value created with the wrong type must read as absent and fail closed,
    // not arrive as a mangled string.
    expect(parseRegQuery(REG_OUTPUT)).not.toHaveProperty('SomeFlag');
  });

  it('ignores the key header line', () => {
    expect(Object.keys(parseRegQuery(REG_OUTPUT))).not.toContain('HKEY_LOCAL_MACHINE');
  });

  it('keeps single spaces inside a value', () => {
    const r = parseRegQuery('    SupportUrl    REG_SZ    https://x.example.com/a b\r\n');
    expect(r.SupportUrl).toBe('https://x.example.com/a b');
  });

  it('handles LF-only output as well as CRLF', () => {
    const lf = REG_OUTPUT.split('\r\n').join('\n');
    expect(parseRegQuery(lf).TenantId).toBe('3f2504e0-4f89-11d3-9a0c-0305e82c3301');
  });

  it('returns nothing for empty or unrelated output', () => {
    expect(parseRegQuery('')).toEqual({});
    expect(parseRegQuery('ERROR: The system was unable to find the key')).toEqual({});
  });
});

describe('readPolicyConfig', () => {
  it('never touches the registry off Windows', async () => {
    let called = false;
    const spy: RegRunner = async () => {
      called = true;
      return REG_OUTPUT;
    };
    expect(await readPolicyConfig(spy, 'darwin')).toEqual({});
    expect(called).toBe(false);
  });

  it('treats a missing key as nothing delivered, not an error', async () => {
    await expect(readPolicyConfig(runnerThrowing, 'win32')).resolves.toEqual({});
  });

  it('queries the policy key explicitly for the 64-bit view', async () => {
    let args: string[] = [];
    const spy: RegRunner = async (a) => {
      args = a;
      return REG_OUTPUT;
    };
    await readPolicyConfig(spy, 'win32');
    // Without /reg:64 a 32-bit build is silently redirected to Wow6432Node and
    // told the policy is absent.
    expect(args).toEqual(['query', POLICY_KEY, '/reg:64']);
  });
});

describe('pickBaked', () => {
  it('keeps known keys and drops documentation / unknown ones', () => {
    const r = pickBaked({ ...okBaked, _README: 'notes', Nonsense: 'x' } as Record<string, string>);
    expect(r.TenantId).toBe(okBaked.TenantId);
    expect(r).not.toHaveProperty('_README');
    expect(r).not.toHaveProperty('Nonsense');
  });

  it('drops blank values and trims the rest', () => {
    const r = pickBaked({ TenantId: '  abc  ', AudienceAppId: '   ' });
    expect(r.TenantId).toBe('abc');
    expect(r).not.toHaveProperty('AudienceAppId');
  });
});

describe('mergeFederationSources', () => {
  it('lets policy override a baked value', () => {
    const r = mergeFederationSources({ FederationRuleId: 'fdrl_baked' }, { FederationRuleId: 'fdrl_policy' });
    expect(r.FederationRuleId).toBe('fdrl_policy');
  });

  it('keeps baked values policy does not mention', () => {
    const r = mergeFederationSources(okBaked, { FederationRuleId: 'fdrl_policy' });
    expect(r.TenantId).toBe(okBaked.TenantId);
  });

  it('does NOT let a blank policy value clear a baked one', () => {
    // Blank means "not set" everywhere else here; a half-cleared policy key must
    // not silently disable federation for the whole fleet.
    const r = mergeFederationSources({ FederationRuleId: 'fdrl_baked' }, { FederationRuleId: '   ' });
    expect(r.FederationRuleId).toBe('fdrl_baked');
  });
});

describe('resolveFederation', () => {
  it('validates the baked config when no policy exists', async () => {
    const r = await resolveFederation({
      baked: okBaked,
      runner: runnerThrowing,
      platform: 'win32',
    });
    expect(r.unconfigured).toBe(false);
    expect(r.result.ok).toBe(true);
    if (!r.result.ok) return;
    expect(r.result.config.federationRuleId).toBe('fdrl_baked');
  });

  it('lets policy correct a rotated rule without a rebuild', async () => {
    const r = await resolveFederation({
      baked: okBaked,
      runner: runnerReturning(REG_OUTPUT),
      platform: 'win32',
    });
    expect(r.result.ok).toBe(true);
    if (!r.result.ok) return;
    // This is the whole point of chaining the two sources.
    expect(r.result.config.federationRuleId).toBe('fdrl_01ABCdef');
  });

  it('reports unconfigured when neither source delivered anything', async () => {
    const r = await resolveFederation({ baked: {}, runner: runnerThrowing, platform: 'win32' });
    expect(r.unconfigured).toBe(true);
    expect(r.result.ok).toBe(false);
  });

  it('reports a half-delivered config as rejected, NOT unconfigured', async () => {
    // An administrator has to be able to tell "not deployed" from "deployed wrong".
    const r = await resolveFederation({
      baked: { TenantId: okBaked.TenantId },
      runner: runnerThrowing,
      platform: 'win32',
    });
    expect(r.unconfigured).toBe(false);
    expect(r.result.ok).toBe(false);
    if (r.result.ok) return;
    expect(r.result.missing.length).toBeGreaterThan(0);
  });

  it('fails closed when policy overwrites a good baked value with a malformed one', async () => {
    const bad = '    FederationRuleId    REG_SZ    not-a-rule-id\r\n';
    const r = await resolveFederation({
      baked: okBaked,
      runner: runnerReturning(bad),
      platform: 'win32',
    });
    expect(r.result.ok).toBe(false);
    if (r.result.ok) return;
    expect(r.result.invalid).toContain('FederationRuleId');
  });
});
