import { describe, it, expect } from 'vitest';
import {
  authorBlockGuidance,
  authorIntro,
  authorIntroBlock,
  captionForPrompt,
  mergeAiStepText,
  textStepLabel,
} from './sop-input';

describe('textStepLabel', () => {
  it('keeps the numbered label for a plain text step', () => {
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

  it('gives every kind a DISTINCT label', () => {
    const labels = ([undefined, 'note', 'caution', 'warning', 'section'] as const).map((k) =>
      textStepLabel(1, k),
    );
    expect(new Set(labels).size).toBe(5);
  });

  it('keeps the position number on callouts too, matching macOS', () => {
    // The two platforms share a project format and a prompt shape; diverging on the
    // label would make the same project produce different output on each.
    for (const k of ['note', 'caution', 'warning', 'section'] as const) {
      expect(textStepLabel(7, k)).toContain('7');
    }
  });

  it('still tells Claude to leave the content alone, whatever the kind', () => {
    for (const k of [undefined, 'note', 'caution', 'warning', 'section'] as const) {
      expect(textStepLabel(1, k)).toContain('leave this content alone');
    }
  });

  it('falls back to the plain label for a foreign/hand-edited callout value', () => {
    const label = textStepLabel(4, 'tip' as unknown as 'note');
    expect(label).toContain('Text step 4');
    expect(label).not.toContain('undefined');
  });
});

describe('authorBlockGuidance', () => {
  it('says nothing extra for a plain text step', () => {
    expect(authorBlockGuidance(undefined)).toBeNull();
    expect(authorBlockGuidance(null)).toBeNull();
  });

  it('tells Claude to let a highlighted callout inform the surrounding steps', () => {
    const g = authorBlockGuidance('warning');
    expect(g).toContain('the screenshots do not show');
    // The two failure modes worth naming: duplicating it, and contradicting it.
    expect(g).toContain('Do not restate it wholesale');
    expect(g).toContain('never contradict it');
  });

  it('treats note, caution and warning identically', () => {
    // They differ in emphasis for the READER, not in what Claude should do.
    expect(authorBlockGuidance('note')).toBe(authorBlockGuidance('caution'));
    expect(authorBlockGuidance('caution')).toBe(authorBlockGuidance('warning'));
  });

  it('tells Claude to respect an existing phase structure for a section', () => {
    const g = authorBlockGuidance('section');
    expect(g).toContain('NON-NUMBERED');
    expect(g).toContain('rather than inventing your own');
    // The concrete instruction: do not double up on a boundary the author set.
    expect(g).toContain('sectionHeading');
    expect(g).not.toBe(authorBlockGuidance('note'));
  });

  it('returns null for a foreign callout value rather than guessing', () => {
    expect(authorBlockGuidance('tip' as unknown as 'note')).toBeNull();
  });
});

describe('authorIntro', () => {
  it('returns the overview the author wrote', () => {
    expect(authorIntro({ intro: { heading: 'Goal', body: 'Why' } })).toEqual({
      heading: 'Goal',
      body: 'Why',
    });
  });

  it('returns null when there is no overview', () => {
    expect(authorIntro({ intro: null })).toBeNull();
  });

  it('sends the CURRENT overview, so a post-generation edit is not discarded', () => {
    // The regression this issue was reported for. Reading the pre-AI snapshot
    // instead looks safer but silently throws away an overview the user edited
    // after a generation — which is the whole complaint. macOS made the same call.
    expect(authorIntro({ intro: { heading: 'My edit', body: 'My intent' } })).toEqual({
      heading: 'My edit',
      body: 'My intent',
    });
  });

  it('treats a whitespace-only overview as absent', () => {
    expect(authorIntro({ intro: { heading: '  ', body: '\n\t ' } })).toBeNull();
  });

  it('trims, and keeps a heading-only or body-only overview', () => {
    expect(authorIntro({ intro: { heading: '  Goal  ', body: '' } })).toEqual({
      heading: 'Goal',
      body: '',
    });
    expect(authorIntro({ intro: { heading: '', body: ' Just prose ' } })).toEqual({
      heading: '',
      body: 'Just prose',
    });
  });
});

describe('authorIntroBlock', () => {
  it('labels the block the way macOS does', () => {
    expect(authorIntroBlock({ heading: 'Goal', body: 'Why' })).toContain(
      'The author already wrote this overview',
    );
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

  it('frames the overview as context to build on, not text to protect', () => {
    const text = authorIntroBlock({ heading: 'Goal', body: 'Why' });
    expect(text).toContain('AUTHORITATIVE CONTEXT');
    expect(text).toContain('not as text to protect');
    // Rewriting is explicitly allowed; losing the author's facts is not.
    expect(text).toContain('rewrite it freely');
    expect(text).toContain('CONTRADICT or SILENTLY DROP');
  });
});

describe('captionForPrompt', () => {
  it('uses the pre-AI original by default, so regeneration cannot compound', () => {
    expect(
      captionForPrompt({ caption: "Claude's rewrite" }, { caption: 'the original auto-caption' }),
    ).toBe('the original auto-caption');
  });

  it("uses the author's hand-edited caption when the flag is set (#62 gap 3)", () => {
    // A deliberate human correction is exactly what Claude should rewrite from,
    // rather than the machine text the human was correcting.
    expect(
      captionForPrompt(
        { caption: 'Click the Save button', captionEditedByUser: true },
        { caption: 'button' },
      ),
    ).toBe('Click the Save button');
  });

  it('falls back to the step caption when there is no snapshot yet', () => {
    expect(captionForPrompt({ caption: 'first run' }, undefined)).toBe('first run');
  });

  it('treats an explicit false the same as absent', () => {
    expect(
      captionForPrompt({ caption: 'ai text', captionEditedByUser: false }, { caption: 'original' }),
    ).toBe('original');
  });

  it('prefers the edit even when a snapshot exists — that is the whole point', () => {
    expect(
      captionForPrompt({ caption: 'human', captionEditedByUser: true }, { caption: 'original' }),
    ).toBe('human');
  });
});

describe('mergeAiStepText', () => {
  it("clears the human-edit flag once Claude's caption replaces the human's", () => {
    // Otherwise the NEXT regenerate treats Claude's own output as an author
    // correction and rewrites from it, compounding on every run.
    const r = mergeAiStepText(
      { caption: 'human text', body: '', captionEditedByUser: true },
      'Claude caption',
      'Claude body',
    );
    expect(r.caption).toBe('Claude caption');
    expect(r.captionEditedByUser).toBeUndefined();
  });

  it('KEEPS the flag when Claude returned a blank caption', () => {
    // The human text survives, so the flag still describes it accurately.
    const r = mergeAiStepText(
      { caption: 'human text', body: '', captionEditedByUser: true },
      '   ',
      'Claude body',
    );
    expect(r.caption).toBe('human text');
    expect(r.captionEditedByUser).toBe(true);
  });

  it('does not wipe existing text when the model returns blanks', () => {
    const r = mergeAiStepText({ caption: 'keep me', body: 'body too' }, '', '');
    expect(r.caption).toBe('keep me');
    expect(r.body).toBe('body too');
  });

  it('leaves an unflagged step unflagged', () => {
    const r = mergeAiStepText(
      { caption: 'a', body: '', captionEditedByUser: undefined },
      'new',
      'new body',
    );
    expect(r.captionEditedByUser).toBeUndefined();
  });
});
