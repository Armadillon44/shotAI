// Golden outputs for the native app's settings.json codec (docs/native/PLAN.md WP-A10,
// docs/native/spec/10-brand-settings-infra.md 7.4.2 and 8.5).
//
// For each input, this runs Electron's own settings module over it: the input bytes become
// settings.json in a temp userData folder, one write goes through the real load-mutate-save
// chain (setLastUpdateCheckAt(1790000000000), the startup update check's stamp), and the bytes
// that lands on disk are written to
// dotnet/tests/ShotAI.Core.Tests/Golden/settings/expected/<name>.json. `fresh` has no input: it
// is the first write on a machine with no settings.json. The native SettingsGoldenTests compare
// SettingsCodec's output with those files byte for byte.
//
// Test-only and env-gated: `npm test` skips it and writes nothing. Regenerate with
//   SHOTAI_SETTINGS_GOLDENS=1 npx vitest run src/main/settings-golden.test.ts
// and commit the outputs. The inputs are synthetic, hand-written in Golden/settings/inputs/.
import { describe, it, expect, vi } from 'vitest';
import { mkdirSync, mkdtempSync, readdirSync, readFileSync, rmSync, writeFileSync } from 'node:fs';
import os from 'node:os';
import path from 'node:path';
import { fileURLToPath } from 'node:url';

// A fixed home, so the default projects folder in every golden is the same string.
const HOME = '/shotai-home';
const h = vi.hoisted(() => ({ userData: '' }));
vi.mock('electron', () => ({
  app: { getPath: (k: string) => (k === 'home' ? '/shotai-home' : h.userData) },
}));

const REPO = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '../..');
const GOLDEN = path.join(REPO, 'dotnet/tests/ShotAI.Core.Tests/Golden/settings');
const STAMP = 1790000000000;

describe.skipIf(!process.env.SHOTAI_SETTINGS_GOLDENS)('settings goldens for the native app', () => {
  it('writes what Electron writes for every input', async () => {
    // Imported here, not at the top, so a skipped run never loads the settings module and the
    // electron-log require behind it (the CI download race noted on #127).
    const { setLastUpdateCheckAt } = await import('./settings');
    expect(path.posix.join(HOME, 'shotAI Projects')).toBe('/shotai-home/shotAI Projects');
    const inputs = path.join(GOLDEN, 'inputs');
    const names = readdirSync(inputs).filter((f) => f.endsWith('.json')).sort();
    expect(names.length).toBeGreaterThan(0);
    const out = path.join(GOLDEN, 'expected');
    mkdirSync(out, { recursive: true });
    const cases: Array<{ name: string; bytes: Buffer | null }> = [
      { name: 'fresh', bytes: null },
      ...names.map((f) => ({ name: f.slice(0, -'.json'.length), bytes: readFileSync(path.join(inputs, f)) })),
    ];
    for (const { name, bytes } of cases) {
      h.userData = mkdtempSync(path.join(os.tmpdir(), 'settings-golden-'));
      try {
        const file = path.join(h.userData, 'settings.json');
        if (bytes) writeFileSync(file, bytes);
        await setLastUpdateCheckAt(STAMP);
        writeFileSync(path.join(out, `${name}.json`), readFileSync(file));
      } finally {
        rmSync(h.userData, { recursive: true, force: true });
      }
    }
  });
});
