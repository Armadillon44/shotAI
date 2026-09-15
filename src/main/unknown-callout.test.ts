// #90 — an unrecognised `callout` is KEPT on read for forward-compat
// (section-callout.test.ts asserts that deliberately), so every consumer that
// tested it for TRUTHINESS treated it as a real callout. Two consequences:
// the step became un-numbered — shifting every LATER step's number, and
// disagreeing with macOS where the same step is a plain numbered one — and the
// Office exports threw on CALLOUT[unknown].fg.
//
// The fix narrows at each point of USE rather than stripping on read, so the
// value survives a round trip AND behaves like a plain step everywhere.
import { describe, it, expect } from 'vitest';
import { isCalloutKind } from '../shared/project';

describe('isCalloutKind is exact', () => {
  it.each(['note', 'caution', 'warning', 'section'])('accepts %s', (k) => {
    expect(isCalloutKind(k)).toBe(true);
  });

  it.each(['danger', 'tip', 'futurekind', 'NOTE', 'Note', '', 'toString', 42, null, undefined, {}])(
    'rejects %s',
    (v) => expect(isCalloutKind(v)).toBe(false),
  );
});

// The numbering rule both platforms must agree on, expressed directly: a step is
// un-numbered if and only if its callout is a KIND THIS BUILD KNOWS.
describe('numbering treats an unknown callout as a plain step', () => {
  const numbering = (steps: { kind: string; callout?: unknown }[]) => {
    let n = 0;
    return steps.map((s) =>
      s.kind === 'text' && isCalloutKind(s.callout) ? null : ++n,
    );
  };

  it('numbers a step whose callout is unrecognised', () => {
    expect(numbering([
      { kind: 'shot' },
      { kind: 'text', callout: 'futurekind' },
      { kind: 'shot' },
    ])).toEqual([1, 2, 3]);
  });

  it('still un-numbers a real callout', () => {
    expect(numbering([
      { kind: 'shot' },
      { kind: 'text', callout: 'warning' },
      { kind: 'shot' },
    ])).toEqual([1, null, 2]);
  });

  /// The regression this is really about: under truthiness the unknown callout
  /// was un-numbered, so the trailing shot was 2 here and 3 on macOS. Every
  /// number after the unknown callout disagreed.
  it('keeps the LATER steps aligned with macOS', () => {
    const withTruthiness = (steps: { kind: string; callout?: unknown }[]) => {
      let n = 0;
      return steps.map((s) => (s.kind === 'text' && s.callout ? null : ++n));
    };
    const steps = [{ kind: 'shot' }, { kind: 'text', callout: 'futurekind' }, { kind: 'shot' }];
    expect(withTruthiness(steps)).toEqual([1, null, 2]); // the bug
    expect(numbering(steps)).toEqual([1, 2, 3]); // matches macOS
  });
});
