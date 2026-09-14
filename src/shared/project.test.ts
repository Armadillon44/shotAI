import { describe, it, expect } from 'vitest';
import { parseRect, parsePoint, isCalloutKind, CALLOUT_GLYPH } from './project';

/** `U+XXXX` for a failure message. */
const cp = (ch: string): string =>
  'U+' + (ch.codePointAt(0) ?? 0).toString(16).toUpperCase().padStart(4, '0');

describe('parseRect', () => {
  it('accepts a valid rect and strips extra fields', () => {
    expect(parseRect({ x: 1, y: 2, width: 3, height: 4, evil: '../x' })).toEqual({
      x: 1,
      y: 2,
      width: 3,
      height: 4,
    });
  });

  it.each([
    ['missing height', { x: 1, y: 2, width: 3 }],
    ['NaN', { x: 1, y: 2, width: 3, height: NaN }],
    ['Infinity', { x: 1, y: 2, width: 3, height: Infinity }],
    ['string field', { x: '1', y: 2, width: 3, height: 4 }],
    ['null', null],
    ['non-object', 'nope'],
  ])('rejects %s', (_label, input) => {
    expect(parseRect(input)).toBeNull();
  });
});

describe('parsePoint', () => {
  it('accepts a valid point', () => {
    expect(parsePoint({ x: 5, y: 6 })).toEqual({ x: 5, y: 6 });
  });

  it.each([
    ['missing y', { x: 5 }],
    ['string y', { x: 5, y: '6' }],
    ['undefined', undefined],
  ])('rejects %s', (_label, input) => {
    expect(parsePoint(input)).toBeNull();
  });
});

describe('isCalloutKind', () => {
  it.each(['note', 'caution', 'warning', 'section'])('accepts %s', (k) => {
    expect(isCalloutKind(k)).toBe(true);
  });

  it.each([
    ['plain text (no callout)', 'text'],
    ['empty string', ''],
    ['null', null],
    ['number', 5],
  ])('rejects %s', (_label, v) => {
    expect(isCalloutKind(v)).toBe(false);
  });

  it('has a CALLOUT_GLYPH entry for every kind (section has none)', () => {
    expect(CALLOUT_GLYPH.note).toBeTruthy();
    expect(CALLOUT_GLYPH.caution).toBeTruthy();
    expect(CALLOUT_GLYPH.warning).toBeTruthy();
    expect(CALLOUT_GLYPH.section).toBe('');
  });

  // The warning mark was U+26D4 NO ENTRY. What made it wrong is not its shape, it
  // is EMOJI PRESENTATION: the font draws it as a coloured orb beside two plain
  // typographic marks, and it keeps that colour in a grayscale print where the
  // callout tint is gone and the glyph is all that carries the kind. So assert the
  // property, not the character — a replacement picked on looks alone would
  // otherwise reintroduce it silently.
  it('uses typographic marks, never emoji', () => {
    for (const [kind, glyph] of Object.entries(CALLOUT_GLYPH)) {
      for (const ch of glyph) {
        expect(
          /\p{Emoji_Presentation}/u.test(ch),
          `${kind} glyph ${cp(ch)} defaults to emoji presentation`,
        ).toBe(false);
        expect(ch, `${kind} glyph carries a VS16 emoji selector`).not.toBe('\uFE0F');
      }
    }
  });

  // Locked to what macOS renders. The platforms share exported documents, so a
  // differing mark shows as the same project reading differently depending on
  // which app produced the file.
  it('matches the macOS glyph table exactly', () => {
    expect(CALLOUT_GLYPH.note).toBe('\u2139');
    expect(CALLOUT_GLYPH.caution).toBe('\u26a0');
    expect(CALLOUT_GLYPH.warning).toBe('\u2501');
  });
});
