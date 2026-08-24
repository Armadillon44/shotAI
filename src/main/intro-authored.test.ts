// #64 — the authored-overview flag, end to end through the REAL store on disk.
//
// The bug this guards: an overview Claude wrote on a previous run and one the
// author wrote look identical on disk, so without a flag the author's own words
// get replaced every generation (observed live: "Claude still re-wrote my
// overview; heading and all"), and protecting the text unconditionally would
// instead feed Claude's prior output back as the author's intent forever.
//
// Deliberately NOT a pure-function test. The flag has to survive coerceManifest,
// which rebuilds the manifest field by field — an uncoerced field is dropped on
// every read, so the feature would pass a pure test and still do nothing in the
// app after one reload. That seam only shows up against a real file.
import { describe, it, expect, beforeEach, afterEach, vi } from 'vitest';
import { promises as fs } from 'node:fs';
import os from 'node:os';
import path from 'node:path';

const h = vi.hoisted(() => ({ root: '' }));

vi.mock('electron', () => ({ shell: { showItemInFolder: () => undefined } }));
vi.mock('./settings', () => ({
  getProjectsDir: async () => h.root,
  getRecents: async () => [],
  addRecent: async () => undefined,
  setRecents: async () => undefined,
  persistProjectsDir: async () => undefined,
}));

import { mutate, setProjectIntro } from './project-store';
import { applySopEdits, revertSop } from './sop-apply';
import { DEFAULT_SOP_TONE } from '../shared/sop';

let dir: string;
const PROV = { model: 'claude-sonnet-5', tone: DEFAULT_SOP_TONE };
const plan = (intro: { heading: string; body: string } | null) => ({
  title: null,
  intro,
  steps: [],
});
/** Re-read from disk THROUGH coerceManifest — the drop trap. */
const reread = () => mutate(dir, () => undefined);

beforeEach(async () => {
  h.root = await fs.mkdtemp(path.join(os.tmpdir(), 'shotai-intro64-'));
  dir = path.join(h.root, 'proj1');
  await fs.mkdir(dir, { recursive: true });
  await fs.writeFile(
    path.join(dir, 'project.json'),
    JSON.stringify({
      version: 1, id: 'test', title: 'T', createdWith: 'shotAI',
      createdAt: '', updatedAt: '', captureSettings: null,
      steps: [], intro: null, sopBackup: null,
    }),
  );
});
afterEach(async () => { await fs.rm(h.root, { recursive: true, force: true }); });

describe('the authored-overview flag', () => {
  it('is set by the human edit path and SURVIVES a disk round-trip', async () => {
    await setProjectIntro(dir, { heading: 'Onboarding a vendor', body: 'Finance only.' });
    // Straight from the file: it was actually persisted, not just returned.
    const raw = JSON.parse(await fs.readFile(path.join(dir, 'project.json'), 'utf8'));
    expect(raw.introEditedByUser).toBe(true);
    // And through coerceManifest, which is where a new field silently vanishes.
    expect((await reread()).introEditedByUser).toBe(true);
  });

  it('is cleared when the author clears the overview — nothing left to protect', async () => {
    await setProjectIntro(dir, { heading: 'H', body: 'B' });
    await setProjectIntro(dir, null);
    const m = await reread();
    expect(m.intro).toBeNull();
    expect(m.introEditedByUser).toBeUndefined();
  });

  it('is NOT set when CLAUDE writes the overview — the whole point of #64', async () => {
    await applySopEdits(dir, plan({ heading: 'Claude heading', body: 'Claude body' }), PROV);
    const m = await reread();
    expect(m.intro?.heading).toBe('Claude heading');
    // If this ever flips true, Claude's own text becomes "the author's" and every
    // later run protects a machine-written overview.
    expect(m.introEditedByUser).toBeUndefined();
  });
});

describe('applying a plan over an AUTHORED overview (gentle massage)', () => {
  beforeEach(async () => {
    await setProjectIntro(dir, { heading: 'Onboarding a vendor', body: 'Finance staff only.' });
  });

  it('pins the author heading verbatim and takes Claude reworded body', async () => {
    await applySopEdits(
      dir,
      plan({ heading: 'How To Onboard Vendors', body: 'This guide is for finance staff.' }),
      PROV,
    );
    const m = await reread();
    expect(m.intro?.heading).toBe('Onboarding a vendor');
    expect(m.intro?.body).toBe('This guide is for finance staff.');
  });

  it('keeps the flag set afterwards, so a REGENERATE protects it again', async () => {
    await applySopEdits(dir, plan({ heading: 'Rewritten', body: 'new body' }), PROV);
    expect((await reread()).introEditedByUser).toBe(true);
    // Second pass: the heading must still be the author's, not the first pass output.
    await applySopEdits(dir, plan({ heading: 'Rewritten again', body: 'newer body' }), PROV);
    expect((await reread()).intro?.heading).toBe('Onboarding a vendor');
  });

  it('never DELETES the overview when the model returns no intro', async () => {
    await applySopEdits(dir, plan(null), PROV);
    const m = await reread();
    expect(m.intro?.heading).toBe('Onboarding a vendor');
    expect(m.intro?.body).toBe('Finance staff only.');
  });

  it('accepts a Claude heading only when the author never wrote one', async () => {
    await setProjectIntro(dir, { heading: '', body: 'Body only, no heading.' });
    await applySopEdits(dir, plan({ heading: 'Claude supplied', body: 'reworded' }), PROV);
    const m = await reread();
    expect(m.intro?.heading).toBe('Claude supplied');
  });
});

describe('applying a plan over an UNFLAGGED overview (unchanged behavior)', () => {
  it('replaces it freely, and clears it when the model returns none', async () => {
    // Written by a prior AI pass, not by the author: no flag.
    await applySopEdits(dir, plan({ heading: 'AI one', body: 'first' }), PROV);
    await applySopEdits(dir, plan({ heading: 'AI two', body: 'second' }), PROV);
    expect((await reread()).intro?.heading).toBe('AI two');
    await applySopEdits(dir, plan(null), PROV);
    expect((await reread()).intro).toBeNull();
  });
});

describe('revert', () => {
  it('restores the flag, not just the text', async () => {
    await setProjectIntro(dir, { heading: 'Author H', body: 'Author B' });
    await applySopEdits(dir, plan({ heading: 'X', body: 'Y' }), PROV);
    await revertSop(dir);
    const m = await reread();
    expect(m.intro?.heading).toBe('Author H');
    expect(m.intro?.body).toBe('Author B');
    // Without this the reverted overview is the author's but unprotected, so the
    // very next generate replaces it again.
    expect(m.introEditedByUser).toBe(true);
  });

  it('leaves a Claude-written overview unflagged after revert', async () => {
    await applySopEdits(dir, plan({ heading: 'AI', body: 'ai body' }), PROV);
    await revertSop(dir);
    expect((await reread()).introEditedByUser).toBeUndefined();
  });
});
