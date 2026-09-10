import { describe, it, expect } from 'vitest';
import fs from 'node:fs';
import { docCss } from './export-css';
import { CARD_RADIUS_PX, IMAGE_RADIUS_PX } from '../shared/theme-palette';

// #77 phase 0, the part that keeps it done.
//
// Extracting 78 colour literals onto shared/theme-palette is worth little on its own: the
// next person to add a rule writes `color:#6b7280` because that is what the file looked
// like for a year, and the fifth copy of the palette starts growing again. Nothing in the
// type system objects to a hardcoded colour.
//
// So this asserts from SOURCE that the three export surfaces contain no colour literals at
// all. It is the same technique as admx-contract.test.ts and federation-cache-wiring.test.ts:
// the invariant is about how the code is WRITTEN, so it is checked by reading the code.
//
// Deliberately strict. If a genuine exception ever arrives, add it to ALLOWED below with a
// reason, so it is a decision on the record rather than a slow return to four palettes.

interface Surface {
  file: string;
  /** Which palette this surface renders with, for the failure message. */
  palette: string;
}

// One palette for all three, since the ramp collapse. There used to be
// DOC_LIGHT here for two of them and SLIDE_LIGHT for the deck, which is what a
// three-ramp document set looks like written down.
// One palette for all three, since the ramp collapse — and since phase 4 it
// arrives as an argument rather than an import, so an export can follow the brand
// it was asked for.
const SURFACES: Surface[] = [
  { file: 'src/main/export-css.ts', palette: 'the export theme' },
  { file: 'src/main/export-docx.ts', palette: 'the export theme' },
  { file: 'src/main/export-pptx.ts', palette: 'the export theme' },
];

/**
 * Colour-shaped strings that are NOT colours, or are colours that legitimately stay
 * literal. Every entry needs a reason.
 */
const ALLOWED: Array<{ file: string; value: string; why: string }> = [];

/** `#rrggbb` / `#rgb` in CSS, and bare quoted `'RRGGBB'` as docx and pptxgenjs take. */
function colourLiterals(src: string): string[] {
  const found: string[] = [];
  // Strip comments first: the explanatory prose in these files cites hex values on
  // purpose (the divergence table, the measured-payload notes), and citing a colour in a
  // comment is documentation, not a second copy of the palette.
  const code = src.replace(/\/\*[\s\S]*?\*\//g, '').replace(/(^|[^:])\/\/.*$/gm, '$1');
  for (const m of code.matchAll(/#[0-9a-fA-F]{3,8}\b/g)) found.push(m[0]);
  for (const m of code.matchAll(/'([0-9A-Fa-f]{6})'/g)) found.push(m[0]);
  return found;
}

describe('the export surfaces hold no colour of their own', () => {
  for (const s of SURFACES) {
    it(`${s.file} reads every colour from ${s.palette}`, () => {
      const src = fs.readFileSync(s.file, 'utf8');
      const allowed = new Set(ALLOWED.filter((a) => a.file === s.file).map((a) => a.value));
      const offenders = colourLiterals(src).filter((v) => !allowed.has(v));
      expect(
        offenders,
        `${s.file} has ${offenders.length} hardcoded colour(s): ${[...new Set(offenders)].join(', ')}.\n` +
          `Use a role from ${s.palette} in src/shared/theme-palette.ts instead. If the value is\n` +
          `genuinely not a themeable colour, add it to ALLOWED in this test with a reason.`,
      ).toEqual([]);
    });

    it(`${s.file} takes its colours from the threaded theme`, () => {
      // A file with no literals AND no palette read is a file that stopped drawing
      // colours, which would mean a rule was deleted rather than converted.
      //
      // It must read the THEME rather than a module-level palette: a constant
      // captured at import time cannot follow the brand, and the failure is
      // silent — every document renders, in the default brand, forever.
      const src = fs.readFileSync(s.file, 'utf8');
      expect(src, `${s.file} should import from shared/export-theme`).toMatch(
        /from '\.\.\/shared\/export-theme'/,
      );
      expect(src, `${s.file} should read the theme's palette`).toMatch(
        /=\s*theme\.palette/,
      );
      expect(
        /\b(APP_LIGHT|APP_DARK|BRANDS)\b/.test(src.replace(/\/\*[\s\S]*?\*\//g, '')),
        `${s.file} reads a palette directly; it would ignore the brand it was given`,
      ).toBe(false);
    });
  }

  it('records a reason for every allowed exception', () => {
    for (const a of ALLOWED) {
      expect(a.why.length, `${a.file} ${a.value} needs a reason`).toBeGreaterThan(10);
    }
  });

  it('does not allow an exception that is no longer present', () => {
    // A stale allowance is a hole: it would silently permit that literal to come back.
    for (const a of ALLOWED) {
      const src = fs.readFileSync(a.file, 'utf8');
      expect(src, `${a.file} no longer contains ${a.value}; remove the ALLOWED entry`).toContain(
        a.value,
      );
    }
  });
});

describe('the Office exporters convert through hexNoHash', () => {
  // docx and pptxgenjs want `RRGGBB`. Interpolating a palette value directly would pass
  // `#RRGGBB`, and both libraries treat an unparseable colour as black rather than
  // failing, so the document renders wrong and nothing reports it. hexNoHash throws
  // instead, which is why it must be the only path.
  for (const f of ['src/main/export-docx.ts', 'src/main/export-pptx.ts']) {
    it(`${f} never passes a #-prefixed value to the library`, () => {
      const src = fs.readFileSync(f, 'utf8');
      expect(src, `${f} should import hexNoHash`).toContain('hexNoHash');
      // Every palette read in these files must be wrapped. Catch the bare form:
      // `color: DOC_LIGHT.ink` or `fill: SLIDE_LIGHT.surface` with no conversion.
      const bare = [
        ...src.matchAll(/(?<!hexNoHash\()\bC\.[a-zA-Z0-9]+/g),
      ]
        .map((m) => m[0])
        .filter((hit) => {
          // Allow a read that is immediately handed to hexNoHash on the same line.
          const i = src.indexOf(hit);
          const line = src.slice(src.lastIndexOf('\n', i) + 1, src.indexOf('\n', i));
          return !line.includes('hexNoHash');
        });
      expect(
        bare,
        `${f} reads the palette without hexNoHash: ${bare.join(', ')}. ` +
          'docx/pptxgenjs render an unparseable colour as black.',
      ).toEqual([]);
    });
  }
});

describe('the export geometry comes from the theme (#77 phases 0b, 2)', () => {
  it('export-css.ts holds no radius literal at all', () => {
    // Including 50%: the step badge is the brand's CHIP, not structural geometry.
    // macOS shipped an LFI document with square-ish cards and perfectly circular
    // numbers by assuming otherwise.
    const src = fs.readFileSync('src/main/export-css.ts', 'utf8');
    const literals = [...src.matchAll(/border-radius:\s*(\d+px|\d+%)/g)].map((m) => m[0]);
    expect(
      literals,
      `export-css.ts hardcodes a radius: ${literals.join(', ')}. Take it from the ` +
        'theme, so the report and the export cannot drift apart and a brand can ' +
        'change its geometry in one place.',
    ).toEqual([]);
  });
});

describe('the RENDERED export gives each element the right radius', () => {
  // Source-text checks cannot see this. Folding the image radius back into the card
  // leaves IMAGE_RADIUS_PX in the import line, so a "does the file mention it" test
  // still passes while the document renders wrongly. A mutation run proved exactly
  // that, so the assertion moved to the output.
  const css = docCss(1);

  /** The border-radius a selector renders, in px. */
  function radiusOf(sel: string): number {
    const i = css.indexOf(sel + '{');
    expect(i, sel + ' has no rule in docCss').toBeGreaterThan(-1);
    const body = css.slice(i, css.indexOf('}', i));
    const m = /border-radius:(\d+)px/.exec(body);
    expect(m, sel + ' has no border-radius').not.toBeNull();
    return Number(m?.[1]);
  }

  it('renders the CARD radius on the step card and the overview card', () => {
    expect(radiusOf('.step__main')).toBe(CARD_RADIUS_PX);
    expect(radiusOf('.doc__intro')).toBe(CARD_RADIUS_PX);
  });

  it('renders the smaller IMAGE radius on the nested screenshot', () => {
    expect(radiusOf('.step__img')).toBe(IMAGE_RADIUS_PX);
    expect(radiusOf('.step__img')).toBeLessThan(radiusOf('.step__main'));
  });

  it('keeps the step-number circle a circle', () => {
    // 50% is not a card radius and must not be swept into the token scheme.
    const i = css.indexOf('.step__num{');
    const body = css.slice(i, css.indexOf('}', i));
    expect(body).toContain('border-radius:50%');
  });
});
