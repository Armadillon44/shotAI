// Golden outputs for the native app's project.json codec (docs/native/PLAN.md WP-A3,
// docs/native/spec/01-model-store.md Q-MODEL-18 and AC-MODEL-3).
//
// For each input, this writes the bytes Electron's readManifest-then-writeManifest path
// produces for it, JSON.stringify(coerceManifest(JSON.parse(text), name), null, 2), to
// dotnet/tests/ShotAI.Core.Tests/Golden/codec/expected/<name>.json. The native
// ElectronGoldenTests compare the C# codec's output with those files byte for byte.
//
// Test-only and env-gated: `npm test` skips it and writes nothing. Regenerate with
//   SHOTAI_CODEC_GOLDENS=1 npx vitest run src/main/codec-golden.test.ts
// and commit the outputs. The inputs are synthetic: the hand-written files in
// Golden/codec/inputs/, the macOS fixture project in Golden/macos-fixture/, and the
// `input` of every shared case in contract/conformance/manifest/, read in place.
import { describe, it, expect, vi } from 'vitest';
import { mkdirSync, readdirSync, readFileSync, writeFileSync } from 'node:fs';
import path from 'node:path';
import { fileURLToPath } from 'node:url';

// project-store reaches electron at import time; this only needs its codec.
vi.mock('electron', () => ({ shell: { showItemInFolder: () => undefined } }));

import { coerceManifest } from './project-store';

const REPO = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '../..');
const GOLDEN = path.join(REPO, 'dotnet/tests/ShotAI.Core.Tests/Golden');
const OUT = path.join(GOLDEN, 'codec/expected');

interface GoldenInput {
  /** The output file name without `.json`, and the fallback title. */
  name: string;
  parsed: unknown;
}

const jsonFiles = (dir: string): string[] =>
  readdirSync(dir).filter((f) => f.endsWith('.json')).sort();

function goldenInputs(): GoldenInput[] {
  const out: GoldenInput[] = [];
  // readFileSync(..., 'utf8') is readManifest's decode: invalid UTF-8 becomes U+FFFD.
  const inputs = path.join(GOLDEN, 'codec/inputs');
  for (const f of jsonFiles(inputs)) {
    out.push({ name: f.slice(0, -'.json'.length), parsed: JSON.parse(readFileSync(path.join(inputs, f), 'utf8')) });
  }
  const fixture = path.join(GOLDEN, 'macos-fixture/b7e2c4d1-9f3a-4e8b-a2c5-6d1f8e9a0b3c/project.json');
  out.push({ name: 'macos-fixture', parsed: JSON.parse(readFileSync(fixture, 'utf8')) });
  const cases = path.join(REPO, 'contract/conformance/manifest');
  for (const f of jsonFiles(cases)) {
    const c = JSON.parse(readFileSync(path.join(cases, f), 'utf8')) as { input: unknown };
    out.push({ name: `conformance-${f.slice(0, -'.json'.length)}`, parsed: c.input });
  }
  return out;
}

describe.skipIf(!process.env.SHOTAI_CODEC_GOLDENS)('codec goldens for the native app', () => {
  it('writes what Electron would write for every input', () => {
    const inputs = goldenInputs();
    expect(inputs.length).toBeGreaterThan(0);
    mkdirSync(OUT, { recursive: true });
    for (const { name, parsed } of inputs) {
      const text = JSON.stringify(coerceManifest(parsed as never, name), null, 2);
      writeFileSync(path.join(OUT, `${name}.json`), text, 'utf8');
    }
  });
});
