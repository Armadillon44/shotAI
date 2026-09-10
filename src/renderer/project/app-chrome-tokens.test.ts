import { describe, it, expect } from 'vitest';
import fs from 'node:fs';

// The app chrome has to FOLLOW the brand, and the only way it can is by reading a
// token. project.css and editor.css carried 42 hardcoded colours between them —
// indigo focus rings, Tailwind greys, a violet caret — every one of which would
// have stayed violet under a charcoal-and-rust brand and read as a rendering bug
// rather than a missed conversion.
//
// So: no colour literal, anywhere in the project window's stylesheets, except the
// two places where a literal is the correct answer. Each exception is named with
// its reason here AND commented at the rule, so the next person does not "finish
// the job" and break something.
//
// toolbar.css and overlay.css are deliberately absent. Those are separate
// documents that never receive [data-theme], and the capture surfaces staying
// theme-agnostic is a settled decision on both platforms.

const SHEETS = ['src/renderer/project/project.css', 'src/renderer/editor/editor.css'];

/**
 * Rules allowed to name a colour, by selector prefix.
 *
 * Not a value allowlist: a value list would also permit the same colour to
 * reappear somewhere it has no business being.
 */
const LITERAL_OK: { prefix: string; why: string }[] = [
  {
    prefix: '.rep__marker',
    why: 'The click markers mirror annotation colours FLATTENED INTO THE PNG at save time. Retinting the live overlay would make it disagree with every screenshot already on disk.',
  },
  {
    prefix: '.tour__pill',
    why: 'A mock-up of the recording pill, shown in the tour. The real pill is a separate document that is out of scope by decision, so a themed mock would stop resembling what it teaches.',
  },
];

/** Strip comments: prose in these files quotes hex on purpose. */
const body = (f: string): string =>
  fs.readFileSync(f, 'utf8').replace(/\/\*[\s\S]*?\*\//g, (m) => m.replace(/[^\n]/g, ' '));

/** The selector whose block `index` falls inside, or '' at top level. */
function enclosingSelector(css: string, index: number): string {
  const open = css.lastIndexOf('{', index);
  if (open < 0) return '';
  const prevClose = css.lastIndexOf('}', open);
  const prevOpen = css.lastIndexOf('{', open - 1);
  // A nested block (@media) would put another '{' between; good enough here
  // because these sheets nest only inside @media, whose selector we then skip to.
  const start = Math.max(prevClose, prevOpen) + 1;
  return css.slice(start, open).trim().replace(/\s+/g, ' ');
}

/**
 * Radius values a rule may state outright, because they are geometry rather than
 * a surface style and a brand has no opinion on them.
 */
const RADIUS_LITERAL_OK = /^(50%|0|[23]px)$/;

describe('the app stylesheets name no colour of their own', () => {
  for (const f of SHEETS) {
    it(`${f} reads every colour from a token`, () => {
      const css = body(f);
      const offenders: string[] = [];
      for (const m of css.matchAll(/#[0-9a-fA-F]{3,8}\b/g)) {
        const sel = enclosingSelector(css, m.index ?? 0);
        if (LITERAL_OK.some((a) => sel.startsWith(a.prefix))) continue;
        const line = css.slice(0, m.index).split('\n').length;
        offenders.push(`${f}:${line} ${sel || '(top level)'} -> ${m[0]}`);
      }
      expect(
        offenders,
        'these colours will not follow the brand:\n  ' +
          offenders.join('\n  ') +
          '\nUse a var(--token); the roles are in src/shared/theme-palette.ts.',
      ).toEqual([]);
    });
  }

  it('records a reason for every exception, and each is still real', () => {
    const all = SHEETS.map(body).join('\n');
    for (const a of LITERAL_OK) {
      expect(a.why.length, `${a.prefix} needs a reason`).toBeGreaterThan(40);
      // A stale exception is a hole: it would silently permit literals in a rule
      // that no longer exists, under a name nobody checks.
      expect(all, `${a.prefix} no longer exists; remove the exception`).toContain(a.prefix);
    }
  });

  it('lets no rule hardcode a corner radius', () => {
    // A literal here is a corner that stays shotAI-shaped under another brand:
    // LFI draws every corner tighter and its chips as rounded rectangles rather
    // than capsules, so geometry is part of the brand, not a constant.
    //
    // Allowed outright: 50% (a circle), 0 (an explicit reset) and 2-3px (a
    // hairline indicator whose radius is half its own height). Allowed by
    // selector: the same two rules that keep their colours.
    const offenders: string[] = [];
    for (const f of SHEETS) {
      const css = body(f);
      for (const m of css.matchAll(/border-radius:\s*([^;]+);/g)) {
        const v = m[1].trim();
        if (v.startsWith('var(--radius-')) continue;
        if (RADIUS_LITERAL_OK.test(v)) continue;
        const sel = enclosingSelector(css, m.index ?? 0);
        if (LITERAL_OK.some((x) => sel.startsWith(x.prefix))) continue;
        const line = css.slice(0, m.index).split('\n').length;
        offenders.push(`${f}:${line} ${sel || '(top level)'} -> ${v}`);
      }
    }
    expect(
      offenders,
      'these corners will not follow the brand:\n  ' +
        offenders.join('\n  ') +
        '\nUse a var(--radius-…) role; the scale is BrandRadii in shared/theme-palette.ts.',
    ).toEqual([]);
  });

  it('leaves no dead var() fallback that silently outranks the token', () => {
    // `var(--accent, #4f8cff)` reads as a safety net and is nothing of the kind:
    // --accent always resolves, so the literal is dead. Worse is the inverse —
    // `var(--text, #e7e9ee)`, where the token never existed and the fallback was
    // what shipped. Either way a colour is written down twice.
    const offenders: string[] = [];
    for (const f of SHEETS) {
      const css = body(f);
      for (const m of css.matchAll(/var\(\s*--[a-z0-9-]+\s*,\s*(#[0-9a-fA-F]{3,8})/g)) {
        offenders.push(`${f}: ${m[0]})`);
      }
    }
    expect(offenders, 'var() fallbacks holding a literal:\n  ' + offenders.join('\n  ')).toEqual(
      [],
    );
  });
});
