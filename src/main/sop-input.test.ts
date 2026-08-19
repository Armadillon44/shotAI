import { describe, it, expect } from 'vitest';
import { authorIntro, authorIntroBlock, textStepLabel } from './sop-input';
import type { SopBackup, SopIntro } from '../shared/project';

/** Minimal SopBackup — only `intro` matters to authorIntro. */
const backup = (intro: SopIntro | null): SopBackup => ({
  steps: [],
  title: 'before',
  intro,
  model: 'claude-sonnet-5',
  tone: 'professional',
  at: '2026-01-01T00:00:00.000Z',
});

describe('textStepLabel', () => {
  it('keeps the legacy numbered label for a plain text step', () => {
    expect(textStepLabel(5)).toBe(
      '--- Text step 5 (author-written — leave this content alone) ---',
    );
  });

  it('treats null/undefined callout as a plain text step', () => {
    expect(textStepLabel(2, null)).toContain('Text step 2');
    expect(textStepLabel(2, undefined)).toContain('Text step 2');
  });

  it('names each callout kind so Claude can tell them apart (#62 gap 2)', () => {
    expect(textStepLabel(3, 'note')).toContain('Note callout');
    expect(textStepLabel(3, 'caution')).toContain('Caution callout');
    expect(textStepLabel(3, 'warning')).toContain('Warning callout');
    expect(textStepLabel(3, 'section')).toContain('Section heading');
  });

  it('gives every callout kind a DISTINCT label', () => {
    const labels = (['note', 'caution', 'warning', 'section'] as const).map((k) =>
      textStepLabel(1, k),
    );
    expect(new Set(labels).size).toBe(4);
  });

  it('omits the number on callouts, which are un-numbered in the document', () => {
    for (const k of ['note', 'caution', 'warning', 'section'] as const) {
      expect(textStepLabel(7, k)).not.toContain('7');
    }
  });

  it('marks a section as a phase divider, not just author-written', () => {
    expect(textStepLabel(1, 'section')).toContain('phase divider');
    expect(textStepLabel(1, 'note')).not.toContain('phase divider');
  });

  it('still tells Claude to leave the content alone, whatever the kind', () => {
    for (const k of [undefined, 'note', 'caution', 'warning', 'section'] as const) {
      expect(textStepLabel(1, k)).toContain('leave this content alone');
    }
  });

  it('falls back to the plain label for a foreign/hand-edited callout value', () => {
    // Never render `undefined` into the prompt from an out-of-union value.
    const label = textStepLabel(4, 'tip' as unknown as 'note');
    expect(label).toContain('Text step 4');
    expect(label).not.toContain('undefined');
  });
});

describe('authorIntro', () => {
  it('returns the manifest intro before any generation has run', () => {
    expect(authorIntro({ intro: { heading: 'Goal', body: 'Why' }, sopBackup: null })).toEqual({
      heading: 'Goal',
      body: 'Why',
    });
  });

  it('returns null when the author wrote no overview', () => {
    expect(authorIntro({ intro: null, sopBackup: null })).toBeNull();
  });

  it('prefers the pre-AI snapshot so a regenerate never echoes Claude back', () => {
    // manifest.intro is Claude's from the last run; the backup holds the author's.
    expect(
      authorIntro({
        intro: { heading: "Claude's heading", body: "Claude's body" },
        sopBackup: backup({ heading: 'Mine', body: 'My intent' }),
      }),
    ).toEqual({ heading: 'Mine', body: 'My intent' });
  });

  it('returns null when the backup shows the author wrote none (the compounding guard)', () => {
    // The sharpest case: without this, Claude's own overview would be fed back
    // as if the author had written it, and compound on every regeneration.
    expect(
      authorIntro({
        intro: { heading: "Claude's heading", body: "Claude's body" },
        sopBackup: backup(null),
      }),
    ).toBeNull();
  });

  it('treats a whitespace-only overview as absent', () => {
    expect(authorIntro({ intro: { heading: '  ', body: '\n\t ' }, sopBackup: null })).toBeNull();
  });

  it('trims, and keeps a heading-only or body-only overview', () => {
    expect(authorIntro({ intro: { heading: '  Goal  ', body: '' }, sopBackup: null })).toEqual({
      heading: 'Goal',
      body: '',
    });
    expect(authorIntro({ intro: { heading: '', body: ' Just prose ' }, sopBackup: null })).toEqual({
      heading: '',
      body: 'Just prose',
    });
  });
});

describe('authorIntroBlock', () => {
  it('labels the block with the wording the system prompt refers to', () => {
    expect(authorIntroBlock({ heading: 'Goal', body: 'Why' })).toContain('Author overview');
  });

  it('emits both fields when present', () => {
    const text = authorIntroBlock({ heading: 'Goal', body: 'Why' });
    expect(text).toContain('Heading: Goal');
    expect(text).toContain('Body: Why');
  });

  it('omits an empty field rather than emitting a bare label', () => {
    expect(authorIntroBlock({ heading: 'Goal', body: '' })).not.toContain('Body:');
    expect(authorIntroBlock({ heading: '', body: 'Why' })).not.toContain('Heading:');
  });
});
