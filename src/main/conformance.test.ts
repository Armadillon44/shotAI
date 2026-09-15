// The shared `project.json` conformance suite.
//
// Cases live in `contract/conformance/manifest/` as data, and the macOS app runs
// the SAME files against its own codec
// (Packages/ShotModel/Tests/ShotModelTests/ConformanceTests.swift).
// See contract/conformance/README.md for the format.
//
// The assertion surface is decode-then-encode, not the store: what a platform
// writes back for a given input IS the cross-platform contract. `coerceManifest`
// is exactly that decode step here — its output object is what gets serialized
// to disk — so no temp directory and no updatedAt bump is involved.
import { describe, it, expect, vi } from 'vitest';
import { readdirSync, readFileSync } from 'node:fs';
import path from 'node:path';
import { fileURLToPath } from 'node:url';

// project-store reaches electron at import time; this suite only needs its codec.
vi.mock('electron', () => ({ shell: { showItemInFolder: () => undefined } }));

import { coerceManifest } from './project-store';

interface Case {
  name: string;
  why: string;
  status: 'agreed' | 'open';
  issue?: string;
  divergence?: string;
  input: Record<string, unknown>;
  expect: Record<string, unknown>;
}

// Located from this file, not the working directory — vitest's cwd is not
// something this suite should depend on, and finding zero cases silently is
// worse than failing.
const DIR = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '../../contract/conformance/manifest');

const CASES: Case[] = readdirSync(DIR)
  .filter((f) => f.endsWith('.json'))
  .sort()
  .map((f) => JSON.parse(readFileSync(path.join(DIR, f), 'utf8')) as Case);

const ABSENT = Symbol('absent');

/** A dotted path into the re-encoded JSON. `steps.0.kind` indexes arrays. */
function at(root: unknown, dotted: string): unknown | typeof ABSENT {
  let cur: unknown = root;
  for (const part of dotted.split('.')) {
    if (cur === null || typeof cur !== 'object') return ABSENT;
    if (Array.isArray(cur)) {
      const i = Number(part);
      if (!Number.isInteger(i) || i < 0 || i >= cur.length) return ABSENT;
      cur = cur[i];
    } else {
      if (!(part in (cur as Record<string, unknown>))) return ABSENT;
      cur = (cur as Record<string, unknown>)[part];
    }
  }
  return cur;
}

const wantsAbsent = (v: unknown) =>
  typeof v === 'object' && v !== null && !Array.isArray(v) &&
  Object.keys(v).length === 1 && (v as Record<string, unknown>).$absent === true;

describe('project.json conformance (shared with the macOS app)', () => {
  it('actually loaded the suite', () => {
    expect(CASES.length, `no cases found at ${DIR}`).toBeGreaterThan(0);
  });

  for (const c of CASES) {
    it(`${c.name} [${c.status}]`, () => {
      // Round trip through the real codec, then through JSON so the comparison
      // sees exactly what would be written.
      //
      // The codec THROWING is itself a possible divergence — a null entry in
      // `steps` makes coerceManifest throw here and is merely dropped on macOS —
      // so a throw has to be reportable rather than an unconditional test
      // failure, or an `open` case covering it can never be expressed.
      let written: unknown;
      try {
        written = JSON.parse(
          JSON.stringify(coerceManifest(c.input as never, 'Imported project')),
        ) as unknown;
      } catch (err) {
        const threw = `${c.name}: the codec THREW — ${(err as Error).message}`;
        if (c.status === 'open') {
          console.warn(`[conformance] open divergence — ${threw}\n  ${c.divergence ?? ''}\n  tracked: ${c.issue ?? 'untracked'}`);
          return;
        }
        expect.fail(`${threw}\n  why this matters: ${c.why}`);
      }

      const failures: string[] = [];
      for (const key of Object.keys(c.expect).sort()) {
        const want = c.expect[key];
        const got = at(written, key);
        if (wantsAbsent(want)) {
          if (got !== ABSENT) failures.push(`${key}: expected ABSENT, got ${JSON.stringify(got)}`);
          continue;
        }
        if (got === ABSENT) {
          failures.push(`${key}: expected ${JSON.stringify(want)}, got ABSENT`);
          continue;
        }
        if (JSON.stringify(got) !== JSON.stringify(want)) {
          failures.push(`${key}: expected ${JSON.stringify(want)}, got ${JSON.stringify(got)}`);
        }
      }

      if (failures.length === 0) return;
      const detail = `${c.name}: ${failures.join('; ')}`;
      if (c.status === 'open') {
        // A KNOWN divergence: reported, never failed. Resolving one is a status
        // flip in the fixture, so the suite says what is left rather than going
        // quietly green. See contract/conformance/README.md.
        console.warn(
          `[conformance] open divergence — ${detail}\n  ${c.divergence ?? ''}\n  tracked: ${c.issue ?? 'untracked'}`,
        );
        return;
      }
      expect.fail(`${detail}\n  why this matters: ${c.why}`);
    });
  }
});
