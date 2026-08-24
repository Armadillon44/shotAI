import { describe, it, expect } from 'vitest';
import fs from 'node:fs';
import { FEDERATION_KEYS } from './config-validate';

// The ADMX is the only way an administrator can deliver federation config on Windows,
// and it is a separate file from the code that reads it. Adding a key to
// FEDERATION_KEYS without adding it to the ADMX produces a value no policy can set;
// adding one to the ADMX without the code produces a policy value the app silently
// ignores. Neither shows up in a typecheck, so it is asserted here.
const ADMX = 'Intune/Windows/shotAI.admx';
const ADML = 'Intune/Windows/en-US/shotAI.adml';

const admx = fs.readFileSync(ADMX, 'utf8');
const adml = fs.readFileSync(ADML, 'utf8');
const matchAll = (s: string, re: RegExp) => [...s.matchAll(re)].map((m) => m[1]);

describe('ADMX policy contract', () => {
  it('declares exactly the value names the app reads', () => {
    const valueNames = matchAll(admx, /valueName="([^"]+)"/g).sort();
    expect(valueNames).toEqual([...FEDERATION_KEYS].sort());
  });

  it('writes to the MACHINE hive at the key config-sources reads', () => {
    // HKCU is user-writable; a standard user could otherwise repoint their own
    // client at another tenant. class="Machine" is what makes it HKLM.
    expect(admx).toContain('class="Machine"');
    expect(admx).toContain('key="SOFTWARE\\Policies\\shotAI\\Federation"');
  });

  it('stays outside the namespaces Intune ADMX ingestion refuses', () => {
    // Ingestion blocks System, Software\Microsoft and Software\Policies\Microsoft.
    const key = /key="([^"]+)"/.exec(admx)?.[1] ?? '';
    expect(key.toLowerCase()).not.toMatch(/^system/);
    expect(key.toLowerCase()).not.toMatch(/^software\\microsoft/);
    expect(key.toLowerCase()).not.toMatch(/^software\\policies\\microsoft/);
  });

  it('marks the five values the validator requires as required, and no others', () => {
    // Marking an optional value required would force administrators to invent one;
    // marking a required value optional lets a policy save that fails closed at
    // runtime with nothing pointing at the cause.
    const required = matchAll(
      admx,
      /<text id="([^"]+)"[^>]*required="true"/g,
    ).sort();
    expect(required).toEqual(
      ['AudienceAppId', 'FederationRuleId', 'OrganizationId', 'ServiceAccountId', 'TenantId'],
    );
  });

  it('resolves every ADML reference it makes', () => {
    const strings = new Set(matchAll(adml, /<string id="([^"]+)"/g));
    const presentations = new Set(matchAll(adml, /<presentation id="([^"]+)"/g));
    for (const [, kind, name] of admx.matchAll(/\$\((string|presentation)\.([A-Za-z0-9_]+)\)/g)) {
      const pool = kind === 'string' ? strings : presentations;
      expect(pool.has(name), `${kind}.${name} missing from the ADML`).toBe(true);
    }
  });

  it('gives every element a textBox, so nothing is unreachable in the UI', () => {
    const ids = matchAll(admx, /<text id="([^"]+)"/g).sort();
    const refs = matchAll(adml, /refId="([^"]+)"/g).sort();
    expect(refs).toEqual(ids);
  });
});
