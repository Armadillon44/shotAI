import { describe, it, expect } from 'vitest';
import fs from 'node:fs';

// The federation config is cached for the process lifetime. invalidateFederationConfig
// is what makes an administrator's policy correction visible without a restart.
//
// It shipped UNCALLED. config.ts's own comment said "Settings drops the cache on open",
// the ADMX help text and Intune/Windows/README.md both told administrators that
// reopening Settings re-reads policy, and none of it was true: the function had exactly
// one reference in the repo, its own definition. An exported-but-uncalled function
// typechecks perfectly and no unit test on config.ts would notice, because the module
// under test behaves correctly in isolation. Only the WIRING is broken, so the wiring
// is what gets asserted here. Same reasoning as admx-contract.test.ts.
const IPC = fs.readFileSync('src/main/ipc.ts', 'utf8');
const CONFIG = fs.readFileSync('src/main/entra/config.ts', 'utf8');
const ADMIN_DOC = fs.readFileSync('Intune/Windows/README.md', 'utf8');

describe('federation cache invalidation is actually wired', () => {
  it('imports the invalidator into the IPC layer', () => {
    expect(IPC).toMatch(/import\s*\{[^}]*invalidateFederationConfig[^}]*\}\s*from\s*'\.\/entra\/config'/);
  });

  it('CALLS it, not merely imports it', () => {
    // The exact failure mode: present in the import list, referenced nowhere.
    expect(IPC).toContain('invalidateFederationConfig();');
  });

  it('calls it inside the auth:status handler, which Settings hits on mount', () => {
    // Position matters. Invalidating somewhere unreachable would satisfy the check
    // above while leaving the documented behavior just as false.
    const handler = IPC.indexOf('IpcChannels.authStatus');
    const call = IPC.indexOf('invalidateFederationConfig();');
    expect(handler).toBeGreaterThan(-1);
    expect(call).toBeGreaterThan(handler);
    // Inside the handler body, not hundreds of lines later in some other channel.
    expect(call - handler).toBeLessThan(1600);
  });

  it('still exports the invalidator config.ts is expected to provide', () => {
    expect(CONFIG).toContain('export function invalidateFederationConfig');
  });

  it('keeps the administrator doc and the code telling the same story', () => {
    // If the doc promises a restart is not required, the invalidator must be called.
    // If someone decides restart-only is the real behavior, this test should fail and
    // force the doc to change with it rather than drifting apart in silence.
    const promisesNoRestart = /reopening Settings|without a restart/i.test(ADMIN_DOC);
    if (promisesNoRestart) expect(IPC).toContain('invalidateFederationConfig();');
  });
});
