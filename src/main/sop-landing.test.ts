// #108 — a generation that edits nothing must FAIL, not report success.
//
// The bug: nothing checked that any edit actually landed. A plan full of
// well-written steps whose `stepNumber`s matched no screenshot was applied,
// matched nothing, and left the user a fresh title and overview with every
// caption unchanged and NO error. Reported on macOS as "it quickly returned only
// an overview".
//
// The distinction these tests exist to pin is PLAN vs RESULT. macOS's first
// guard inspected the plan — it counted well-written entries — so a plan with
// bogus numbers sailed through it. A guard is only correct if it counts what
// reaches a real screenshot, using the same numbering applySopEdits uses.
//
// Real store against a temp directory, in the intro-authored.test.ts style: the
// numbering depends on `!aiInserted` filtering and on coerceManifest round-trips,
// neither of which a pure-function test would exercise.
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

import { applySopEdits, SopNoLandingError } from './sop-apply';
import { DEFAULT_SOP_TONE } from '../shared/sop';
import type { ProjectStep } from '../shared/project';

let dir: string;
const PROV = { model: 'claude-sonnet-5', tone: DEFAULT_SOP_TONE };

/** A shot step as CAPTURE writes it: note there is no `kind` key at all. */
const shot = (id: string, caption = 'orig'): Partial<ProjectStep> => ({
  id,
  order: 0,
  screenshot: `shots/${id}.png`,
  trigger: 'click',
  caption,
  annotations: [],
});
const text = (id: string, aiInserted = false): Partial<ProjectStep> => ({
  id,
  order: 0,
  kind: 'text',
  screenshot: '',
  trigger: 'click',
  heading: 'H',
  body: 'B',
  annotations: [],
  ...(aiInserted ? { aiInserted: true } : {}),
});

const entry = (stepNumber: number, caption = 'Click Save', body = 'Press it.') => ({
  stepNumber,
  caption,
  body,
  sectionHeading: null,
  sectionBody: null,
});

const planOf = (...steps: ReturnType<typeof entry>[]) => ({
  title: 'A Title',
  intro: { heading: 'Overview', body: 'Body' },
  steps,
});

async function write(steps: Partial<ProjectStep>[]): Promise<void> {
  await fs.writeFile(
    path.join(dir, 'project.json'),
    JSON.stringify({
      version: 1,
      id: 'p',
      title: 'Original Title',
      createdWith: 'shotAI',
      createdAt: '2026-01-01T00:00:00.000Z',
      updatedAt: '2026-01-01T00:00:00.000Z',
      captureSettings: null,
      steps,
      intro: null,
      sopBackup: null,
      archived: false,
      archivedAt: null,
    }),
  );
}
/** The manifest as it sits on disk — the only witness to "nothing was written". */
const onDisk = async (): Promise<{
  title: string;
  intro: { heading: string; body: string } | null;
  sopBackup: unknown;
  updatedAt: string;
  steps: { caption?: string }[];
}> => JSON.parse(await fs.readFile(path.join(dir, 'project.json'), 'utf8'));

beforeEach(async () => {
  h.root = await fs.mkdtemp(path.join(os.tmpdir(), 'shotai-landing-'));
  dir = path.join(h.root, 'proj1');
  await fs.mkdir(dir, { recursive: true });
});
afterEach(async () => {
  await fs.rm(h.root, { recursive: true, force: true });
});

describe('a plan that lands nothing fails, and changes nothing', () => {
  it('rejects well-written steps numbered against the wrong scheme', async () => {
    // THE test. The plan is not empty and not lazy — it carries real content. It
    // is simply numbered 1, where the only screenshot is number 2 because the
    // author's text block consumed number 1. A guard that counts good-looking
    // plan entries passes this and applies nothing.
    await write([text('t1'), shot('s1')]);

    await expect(applySopEdits(dir, planOf(entry(1)), PROV)).rejects.toBeInstanceOf(
      SopNoLandingError,
    );

    const m = await onDisk();
    expect(m.title, 'the title must not change').toBe('Original Title');
    expect(m.intro, 'the overview must not be written').toBeNull();
    expect(m.sopBackup, 'and no revert point is created').toBeNull();
    expect(m.steps[1].caption, 'the step keeps its caption').toBe('orig');
    expect(m.updatedAt, 'the project is not re-dated').toBe('2026-01-01T00:00:00.000Z');
  });

  it('accepts the SAME content numbered correctly', async () => {
    // The control. Without it, a guard that rejected everything would pass the
    // test above and look correct.
    await write([text('t1'), shot('s1')]);

    const { landed } = await applySopEdits(dir, planOf(entry(2)), PROV);

    expect(landed).toBe(1);
    const m = await onDisk();
    expect(m.title).toBe('A Title');
    expect(m.steps[1].caption).toBe('Click Save');
  });

  it('rejects entries that match a step but carry no text', async () => {
    // Landing is about CONTENT reaching a step, not a map lookup succeeding.
    await write([shot('s1')]);
    await expect(applySopEdits(dir, planOf(entry(1, '', '')), PROV)).rejects.toBeInstanceOf(
      SopNoLandingError,
    );
    expect((await onDisk()).steps[0].caption).toBe('orig');
  });

  it('rejects a plan aimed only at a text step', async () => {
    // A text step's number is a legal number that no edit can land on, since
    // edits apply to shot steps only.
    await write([text('t1'), shot('s1')]);
    await expect(applySopEdits(dir, planOf(entry(1)), PROV)).rejects.toThrow(
      /wrote for steps that do not exist/,
    );
  });
});

describe('a partial miss still succeeds', () => {
  it('lands the valid entries and ignores the bogus ones', async () => {
    // `some`, not `every`. One good edge among bad ones is a degraded result,
    // not a failed one, and rejecting the whole run would throw away real work.
    await write([text('t1'), shot('s1'), shot('s2')]);

    const { landed } = await applySopEdits(
      dir,
      planOf(entry(2, 'Two', 'Body two'), entry(3, 'Three', 'Body three'), entry(99, 'Nope')),
      PROV,
    );

    expect(landed, 'two of three entries reached a screenshot').toBe(2);
    const m = await onDisk();
    expect(m.steps[1].caption).toBe('Two');
    expect(m.steps[2].caption).toBe('Three');
  });
});

describe('the numbering matches applySopEdits exactly', () => {
  it('counts author text steps but never lands on them', async () => {
    await write([text('t1'), text('t2'), shot('s1')]);
    // The shot is number 3, not number 1.
    await expect(applySopEdits(dir, planOf(entry(1)), PROV)).rejects.toBeInstanceOf(
      SopNoLandingError,
    );
    const { landed } = await applySopEdits(dir, planOf(entry(3)), PROV);
    expect(landed).toBe(1);
  });

  it('excludes a prior run\'s aiInserted steps from the numbering', async () => {
    // The base is rebuilt without aiInserted steps, so a leftover AI section from
    // a previous generation must NOT shift the numbers.
    await write([text('ai1', true), text('t1'), shot('s1')]);
    const { landed } = await applySopEdits(dir, planOf(entry(2)), PROV);
    expect(landed, 'the shot is 2 once the AI insert is excluded').toBe(1);
  });

  it('treats a step with no `kind` key as a shot', async () => {
    // This is the normal on-disk shape — capture writes no `kind` at all — so a
    // predicate written as `kind === "shot"` would land nothing on a real project.
    await write([shot('s1')]);
    const { landed } = await applySopEdits(dir, planOf(entry(1)), PROV);
    expect(landed).toBe(1);
  });

  it('follows the deduped map when a number repeats', async () => {
    // The edit map is last-wins, so a later empty entry DISCARDS an earlier good
    // one for the same number. A guard scanning plan.steps would see the good
    // entry and wrongly pass.
    await write([shot('s1')]);
    await expect(
      applySopEdits(dir, planOf(entry(1, 'Click Save'), entry(1, '', '')), PROV),
    ).rejects.toBeInstanceOf(SopNoLandingError);
  });
});

describe('an intentionally step-free plan still applies', () => {
  it('writes title and overview when the plan carries no steps at all', async () => {
    // The guard is scoped to non-empty plans so existing title/overview-only
    // callers keep working; the schema's minItems makes an empty plan unreachable
    // from a real generation.
    await write([shot('s1')]);
    const { landed } = await applySopEdits(
      dir,
      { title: 'T2', intro: { heading: 'H', body: 'B' }, steps: [] },
      PROV,
    );
    expect(landed).toBe(0);
    expect((await onDisk()).title).toBe('T2');
  });
});

describe('the schema makes an empty plan unrepresentable on the wire', () => {
  it('sends minItems:1 through the real SDK transport', async () => {
    // Asserted through zodOutputFormat, not on the zod object, because the SDK
    // is where a constraint silently stops being a constraint: it forwards
    // minItems as a real keyword but RELOCATES minimum/maximum/pattern into a
    // description string the model merely reads. A test on the zod schema would
    // pass while the wire carried prose.
    //
    // Keys, never JSON.stringify: the schema already contains the words
    // "minimum" and "maximum" inside that description, so a string search would
    // pass for the wrong reason.
    const { zodOutputFormat } = await import('@anthropic-ai/sdk/helpers/zod');
    const { SopEditSchema } = await import('./claude-service');
    const out = zodOutputFormat(SopEditSchema) as unknown as {
      schema: { properties: { steps: { minItems?: number } } };
    };
    expect(out.schema.properties.steps.minItems).toBe(1);
  });
});
