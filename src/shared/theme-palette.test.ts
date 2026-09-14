import { describe, it, expect } from 'vitest';
import fs from 'node:fs';
import {
  APP_LIGHT,
  APP_DARK,
  RETIRED_GREYS,
  BRANDS,
  BRAND_IDS,
  COLOUR_TOKENS,
  RADIUS_TO_TOKEN,
  RADIUS_TOKENS,
  TYPE_TOKENS,
  radiusCss,
  DEFAULT_BRAND,
  ROLE_TO_TOKEN,
  coerceBrand,
  brandPalette,
  hexNoHash,
  themeStylesheet,
  CARD_RADIUS_PX,
  IMAGE_RADIUS_PX,
  type Appearance,
  type BrandRadii,
  type Palette,
} from './theme-palette';

// #77. These tests exist so that adding a second brand cannot hide a regression,
// and so the drift that ALREADY existed between four copies of this palette is
// written down rather than rediscovered by someone comparing a report against its
// own PDF.
//
// Nothing here asserts what the colours SHOULD be. It asserts they are what ships.

/** A stylesheet with its comments stripped. The prose in these files cites token
 *  names and hex values deliberately — the note in project.css explains why the
 *  colours moved and quotes the dead read it replaced — and a citation is
 *  documentation, not a declaration. */
const sheet = (f: string): string =>
  fs.readFileSync(f, 'utf8').replace(/\/\*[\s\S]*?\*\//g, '');

/** The app's authored stylesheet — the one that no longer declares colour. */
const projectCss = (): string => sheet('src/renderer/project/project.css');

/** Every stylesheet that lives in the PROJECT WINDOW's document, and so reads its
 *  tokens. toolbar.css and overlay.css are separate documents and out of scope. */
const APP_SHEETS = [
  'src/renderer/project/project.css',
  'src/renderer/editor/editor.css',
];

/** The declarations inside one `selector{...}` of the generated sheet. */
function generatedBlock(sheet: string, selector: string): Record<string, string> {
  const at = sheet.indexOf(selector + '{');
  expect(at, `no generated block for ${selector}`).toBeGreaterThan(-1);
  const body = sheet.slice(at + selector.length + 1, sheet.indexOf('}', at));
  const out: Record<string, string> = {};
  for (const decl of body.split(';')) {
    const [k, v] = decl.split(':');
    out[k.replace(/^--/, '')] = v;
  }
  return out;
}

describe('the app stylesheet is generated, not hand-maintained', () => {
  // THE POINT. project.css used to be the fourth hand-written copy of this palette.
  // It is now a consumer of it, and these tests are what stop a fifth from growing.

  it('declares no colour custom property of its own', () => {
    const offenders = [...projectCss().matchAll(/(--[a-z0-9-]+):\s*(#[0-9a-fA-F]{3,8})/g)].map(
      (m) => `${m[1]}: ${m[2]}`,
    );
    expect(
      offenders,
      'project.css declares colour tokens again. They belong in shared/theme-palette.ts;\n' +
        'declaring them here recreates the hand-copied palette #77 removed:\n  ' +
        offenders.join('\n  '),
    ).toEqual([]);
  });

  it('does not redeclare a token the generator owns, under any value', () => {
    // Stricter than the hex check: a token redeclared as `var(--other)` or a named
    // colour would slip past that and silently win or lose on cascade order.
    const css = projectCss();
    // Anchored at a declaration position. An unanchored `--danger\s*:` matches the
    // SELECTOR `.btn--danger:hover`, which is how this test first failed.
    const clashes = COLOUR_TOKENS.filter((t) =>
      new RegExp(`(^|[;{\\s])--${t}\\s*:`).test(css),
    );
    expect(clashes, `project.css redeclares generated token(s): ${clashes.join(', ')}`).toEqual(
      [],
    );
  });

  it('emits a fully-qualified block for every brand and appearance', () => {
    // Fully qualified so the blocks are mutually exclusive and equal-specificity.
    // A base-plus-override arrangement would put [data-brand] and [data-theme] at
    // the same specificity, where source order decides and a brand block would
    // silently defeat dark mode.
    const sheet = themeStylesheet();
    for (const id of BRAND_IDS) {
      for (const appearance of ['light', 'dark'] as Appearance[]) {
        const sel = `:root[data-brand="${id}"][data-theme="${appearance}"]`;
        const decls = generatedBlock(sheet, sel);
        for (const [role, token] of Object.entries(ROLE_TO_TOKEN) as [keyof Palette, string][]) {
          expect(decls[token], `${sel} --${token}`).toBe(BRANDS[id][appearance][role]);
        }
        expect(Object.keys(decls).length, `${sel} declares an extra token`).toBe(
          COLOUR_TOKENS.length + RADIUS_TOKENS.length + TYPE_TOKENS.length,
        );
      }
    }
  });

  it('emits a bare :root fallback, so a pre-attribute paint has colours', () => {
    // The attributes are written from a setting that loads asynchronously. Without
    // this block the first frame renders with no tokens at all.
    const decls = generatedBlock(themeStylesheet(), ':root');
    for (const [role, token] of Object.entries(ROLE_TO_TOKEN) as [keyof Palette, string][]) {
      expect(decls[token], `:root --${token}`).toBe(BRANDS[DEFAULT_BRAND].light[role]);
    }
  });

  it('resolves every var(--…) the project window reads', () => {
    // The guard that makes moving the declarations safe. A token renamed in the
    // generator, or mistyped in a rule, resolves to nothing and the element simply
    // inherits — which looks like a styling choice, not a bug.
    //
    // It found one on its first run: `.settings__slidval` read `var(--text)`, a
    // token that has never existed, and so painted its fallback #e7e9ee — a
    // near-white numeral on a white Settings sheet.
    const declared = new Set<string>(
      [...COLOUR_TOKENS, ...RADIUS_TOKENS, ...TYPE_TOKENS].map((t) => '--' + t),
    );
    for (const f of APP_SHEETS) {
      for (const m of sheet(f).matchAll(/(^|[;{\s])(--[a-z0-9-]+)\s*:/g)) {
        declared.add(m[2]);
      }
    }
    // Set from JS as an inline style rather than declared in a sheet.
    declared.add('--doc-scale');

    const missing = new Set<string>();
    for (const f of APP_SHEETS) {
      for (const m of sheet(f).matchAll(/var\(\s*(--[a-z0-9-]+)/g)) {
        if (!declared.has(m[1])) missing.add(`${f}: ${m[1]}`);
      }
    }
    expect(
      [...missing],
      'these var() reads resolve to nothing (the element silently inherits):\n  ' +
        [...missing].join('\n  '),
    ).toEqual([]);
  });
});

describe('every brand defines every role', () => {
  it('is all lowercase 6-digit hex, so comparisons never fail on case', () => {
    for (const id of BRAND_IDS) {
      for (const appearance of ['light', 'dark'] as Appearance[]) {
        for (const [role, v] of Object.entries(BRANDS[id][appearance])) {
          expect(v, `${id}.${appearance}.${role}`).toMatch(/^#[0-9a-f]{6}$/);
        }
      }
    }
  });

  it('gives every brand the same set of roles', () => {
    // A missing role would fall back to whatever the previous cascade left, which
    // reads as one stubborn element that refuses to follow the brand.
    const roles = Object.keys(ROLE_TO_TOKEN).sort();
    for (const id of BRAND_IDS) {
      for (const appearance of ['light', 'dark'] as Appearance[]) {
        expect(Object.keys(BRANDS[id][appearance]).sort(), `${id}.${appearance}`).toEqual(roles);
      }
    }
  });

  it('keeps a field lighter than the surface it sits on, in dark mode', () => {
    // macOS's rule, and it is not cosmetic: a text input that matches the elevated
    // card behind it stops reading as an input at all.
    for (const id of BRAND_IDS) {
      const p = BRANDS[id].dark;
      expect(luminance(p.fieldBg), `${id} dark field vs surface`).toBeGreaterThan(
        luminance(p.surface),
      );
    }
  });

  it('resolves an unknown or missing brand to the default', () => {
    // The value arrives from settings.json and, later, from project.json written by
    // another platform, so it is untrusted at the boundary.
    for (const bad of [undefined, null, '', 'LFI', 'shotai', 42, {}]) {
      expect(coerceBrand(bad), JSON.stringify(bad)).toBe(DEFAULT_BRAND);
    }
    expect(coerceBrand('lfi')).toBe('lfi');
    expect(coerceBrand('shotAI')).toBe('shotAI');
    expect(brandPalette('nonsense' as never, 'light')).toBe(BRANDS[DEFAULT_BRAND].light);
  });

  it('keeps APP_LIGHT and APP_DARK as aliases, not copies', () => {
    expect(APP_LIGHT).toBe(BRANDS.shotAI.light);
    expect(APP_DARK).toBe(BRANDS.shotAI.dark);
  });
});

describe('text meets WCAG AA on the surfaces it is drawn on', () => {
  // The test that would have caught ink3. It had never met AA on either
  // appearance of the default brand, in a codebase with no contrast assertion
  // anywhere, and the failure only surfaced when the export ramp collapse aimed
  // printed document text at the role.
  //
  // Every ground, not one per appearance. macOS's LFI dark value cleared 5.09 on
  // the page and measured 4.33 on a CARD, which is exactly where secondary labels
  // sit; a spot check on the page background would have passed it.
  const AA = 4.5;

  /** Text roles, against the three page/card backgrounds they appear on. */
  const ON_SURFACES: (keyof Palette)[] = ['ink', 'ink2', 'ink3'];
  /** Roles drawn only on their own tint, so the tint is the ground. */
  const ON_OWN_TINT: [keyof Palette, keyof Palette][] = [
    ['accentInk', 'accentTint'],
    ['okInk', 'okTint'],
    ['draftInk', 'draftTint'],
    ['dangerInk', 'dangerTint'],
    ['noteFg', 'noteBg'],
    ['cautFg', 'cautBg'],
    ['warnFg', 'warnBg'],
  ];

  for (const id of BRAND_IDS) {
    for (const appearance of ['light', 'dark'] as Appearance[]) {
      it(`${id} ${appearance}`, () => {
        const p = BRANDS[id][appearance];
        for (const role of ON_SURFACES) {
          for (const ground of ['ground', 'surface', 'surface2'] as (keyof Palette)[]) {
            const r = contrastRatio(p[role], p[ground]);
            expect(r, `${id} ${appearance}: ${role} ${p[role]} on ${ground} ${p[ground]}`).
              toBeGreaterThanOrEqual(AA);
          }
        }
        for (const [role, ground] of ON_OWN_TINT) {
          const r = contrastRatio(p[role], p[ground]);
          expect(r, `${id} ${appearance}: ${role} ${p[role]} on ${ground} ${p[ground]}`).
            toBeGreaterThanOrEqual(AA);
        }
      });
    }
  }
});

/** WCAG 2.x contrast ratio between two opaque colours. */
function contrastRatio(a: string, b: string): number {
  const [hi, lo] = [luminance(a), luminance(b)].sort((x, y) => y - x);
  return (hi + 0.05) / (lo + 0.05);
}

/** WCAG relative luminance, for the ordering and contrast rules. */
function luminance(hex: string): number {
  const ch = (i: number): number => {
    const c = parseInt(hex.slice(1 + i * 2, 3 + i * 2), 16) / 255;
    return c <= 0.03928 ? c / 12.92 : Math.pow((c + 0.055) / 1.055, 2.4);
  };
  return 0.2126 * ch(0) + 0.7152 * ch(1) + 0.0722 * ch(2);
}

describe('one neutral ramp, and it is the app inks', () => {
  // What replaced KNOWN_DIVERGENCES. That list recorded five app-vs-export
  // differences plus five internal ones and asserted they were exactly as
  // written; now there are none, so the useful assertion is the inverse — the
  // retired values must not come back.
  //
  // Stated as VALUES rather than as an empty list on purpose. An empty list
  // passes as long as the two palettes agree, including if someone reverts both
  // sides together. This fails on the specific greys, wherever they appear.
  const SEARCHED = [
    'src/main/export-css.ts',
    'src/main/export-docx.ts',
    'src/main/export-pptx.ts',
    'src/renderer/project/project.css',
  ];

  it('lists real, distinct 6-digit values', () => {
    expect(new Set(RETIRED_GREYS).size).toBe(RETIRED_GREYS.length);
    for (const g of RETIRED_GREYS) expect(g).toMatch(/^#[0-9a-f]{6}$/);
  });

  it('is not a value any brand still uses', () => {
    // A retired grey that a brand re-adopts would make the guard below fail for
    // a legitimate reason, and the guard is more useful than the value.
    const live = new Set(
      BRAND_IDS.flatMap((id) => [
        ...Object.values(BRANDS[id].light),
        ...Object.values(BRANDS[id].dark),
      ]),
    );
    for (const g of RETIRED_GREYS) {
      expect(live.has(g), `${g} is retired but a brand still declares it`).toBe(false);
    }
  });

  it('appears nowhere in the export path or the app stylesheet', () => {
    const found: string[] = [];
    for (const f of SEARCHED) {
      const src = sheet(f).replace(/(^|[^:])\/\/.*$/gm, '$1');
      for (const g of RETIRED_GREYS) if (src.includes(g)) found.push(`${f}: ${g}`);
    }
    expect(
      found,
      'the Tailwind/slide greys are back:\n  ' +
        found.join('\n  ') +
        '\nEvery surface renders the app ink ramp; see RETIRED_GREYS.',
    ).toEqual([]);
  });
});

describe('hexNoHash', () => {
  it('strips the hash and uppercases, which is what docx and pptxgenjs take', () => {
    expect(hexNoHash('#6344f1')).toBe('6344F1');
    expect(hexNoHash('#FFFFFF')).toBe('FFFFFF');
  });

  it('throws rather than passing a malformed colour into a document', () => {
    // docx renders an invalid colour as black, which reads as a design decision
    // rather than a bug, so this has to fail loudly at the boundary.
    for (const bad of ['6344f1', '#fff', '#12345', '#1234567', 'red', '', '#gggggg']) {
      expect(() => hexNoHash(bad), JSON.stringify(bad)).toThrow();
    }
  });

  it('round-trips every palette value on every brand and appearance', () => {
    for (const p of BRAND_IDS.flatMap((id) => [BRANDS[id].light, BRANDS[id].dark])) {
      for (const [role, v] of Object.entries(p)) {
        expect(hexNoHash(v), role).toBe(v.slice(1).toUpperCase());
      }
    }
  });
});

describe('typography is a brand token too (#77 phase 3)', () => {
  it('names the brand face first, then its own fallbacks', () => {
    const sheet = themeStylesheet();
    const lfi = generatedBlock(sheet, ':root[data-brand="lfi"][data-theme="light"]');
    const shot = generatedBlock(sheet, ':root[data-brand="shotAI"][data-theme="light"]');
    expect(lfi['font-stack'].startsWith('"Archivo",')).toBe(true);
    expect(shot['font-stack']).not.toContain('Archivo');
    // Grotesques of similar proportion rather than the OS UI faces: a reader
    // without the brand face should get the same family of shapes, not Segoe.
    expect(lfi['font-stack']).not.toContain('Segoe');
    expect(shot['font-stack']).toContain('Segoe');
  });

  it('gives a brand with no condensed face no width at all', () => {
    // `normal`, not 100%: a brand without a variable width axis should not be
    // asking the shaper for one.
    const sheet = themeStylesheet();
    expect(generatedBlock(sheet, ':root[data-brand="shotAI"][data-theme="light"]')['label-stretch'])
      .toBe('normal');
    expect(generatedBlock(sheet, ':root[data-brand="lfi"][data-theme="light"]')['label-stretch'])
      .toBe('62%');
  });

  it('declares the face with its full weight range', () => {
    // LOAD-BEARING, not decoration. Archivo's variable DEFAULT INSTANCE is
    // wght 600 — verified by reading the fvar table of the bundled file — so a
    // bare request for the family renders SemiBold. Declaring the range makes
    // the shaper drive the axis from the computed font-weight instead, and
    // `normal` is 400 as everywhere else.
    const css = projectCss();
    const face = css.slice(css.indexOf('@font-face'), css.indexOf('}', css.indexOf('@font-face')));
    expect(face).toContain("font-family: 'Archivo'");
    expect(face).toContain('font-weight: 100 900');
    expect(face).toContain('font-stretch: 62% 125%');
    expect(face).toContain("format('truetype-variations')");
  });

  it('bundles the face as a file, never as base64', () => {
    // Inlining it would also inline it into the exports through the same
    // stylesheet, and the paste budget cannot take it.
    expect(projectCss()).not.toContain('data:font');
    expect(fs.existsSync('src/renderer/fonts/Archivo.ttf')).toBe(true);
    // The SIL OFL requires the licence to travel with the font.
    expect(fs.existsSync('src/renderer/fonts/OFL.txt')).toBe(true);
  });

  it('reads the stack from the token rather than naming faces in a rule', () => {
    const css = projectCss();
    const body = css.slice(css.indexOf('\nbody {'), css.indexOf('}', css.indexOf('\nbody {')));
    expect(body, 'body should read --font-stack').toContain('font-family: var(--font-stack)');

    // No rule may name a face outright: it would stay on the default brand while
    // everything around it changed.
    //
    // ONE EXCEPTION, in editor.css. `.ed__textedit` is the inline overlay for a
    // text annotation, and it has to match what the CANVAS draws underneath it,
    // not what the brand says — Konva's Text defaults to Arial and the overlay
    // sits directly on top of it. A brand face there would put the caret in the
    // wrong place.
    //
    // ⚠ Noticed while writing this and NOT fixed here: the overlay matches
    // Konva (Arial) but flatten.ts bakes with "Segoe UI", so the live canvas and
    // the saved PNG already disagree. Out of scope for #77 — changing it moves
    // every annotation baked from now on — and reported separately.
    const EXEMPT = /^Arial, sans-serif$/;
    const offenders: string[] = [];
    for (const f of APP_SHEETS) {
      const rules = sheet(f).replace(/@font-face\s*\{[\s\S]*?\}/g, '');
      for (const m of rules.matchAll(/font-family:\s*([^;]+);/g)) {
        const v = m[1].trim();
        if (v.startsWith('var(--font-stack)') || v === 'inherit' || EXEMPT.test(v)) continue;
        offenders.push(`${f}: ${v}`);
      }
    }
    expect(offenders, `these rules name a typeface outright: ${offenders.join(' | ')}`).toEqual([]);
  });
});

describe('radius is a brand token, and the scale nests (#77 phase 2)', () => {
  // The rule that matters is a RELATIONSHIP, not a value. "The default brand is
  // 10px everywhere" was a real instruction on this issue and it produced a real
  // wrong implementation: it flattened a document card, its sibling overview and
  // the screenshot NESTED INSIDE the card onto one number, and the concentric
  // corners stopped reading. `figure < card` cannot be misread that way, and it
  // survives a brand scaling every value.

  it('gives every brand a value for every role', () => {
    const roles = Object.keys(RADIUS_TO_TOKEN).sort();
    for (const id of BRAND_IDS) {
      expect(Object.keys(BRANDS[id].radii).sort(), id).toEqual(roles);
    }
  });

  it('nests: a contained element is rounder than nothing and tighter than its container', () => {
    for (const id of BRAND_IDS) {
      const r = BRANDS[id].radii;
      expect(r.figure, `${id}: the screenshot sits INSIDE a step card`).toBeLessThan(r.card);
      expect(r.controlSm, `${id}: a menu item sits INSIDE a menu`).toBeLessThan(r.control);
      expect(r.micro, `${id}: micro is the tightest surface radius`).toBeLessThanOrEqual(
        r.controlSm,
      );
      expect(r.card, `${id}: a card is no rounder than the panel holding it`).toBeLessThanOrEqual(
        r.panel,
      );
      for (const [role, v] of Object.entries(r)) {
        if (v === null) continue;
        expect(v, `${id}.${role}`).toBeGreaterThan(0);
      }
    }
  });

  it('treats a fully-round chip as null, not as a large number', () => {
    // A sentinel like 999 would work visually and lie about intent. The default
    // brand's chips are capsules and its step badge a circle, neither of which is
    // a radius: a capsule's corner depends on the element's height.
    expect(BRANDS[DEFAULT_BRAND].radii.chip).toBeNull();
    expect(radiusCss(null)).toBe('999px');
    expect(radiusCss(8)).toBe('8px');
  });

  it('emits the radius tokens alongside the colours, per brand', () => {
    const sheet = themeStylesheet();
    for (const id of BRAND_IDS) {
      for (const appearance of ['light', 'dark'] as Appearance[]) {
        const decls = generatedBlock(
          sheet,
          `:root[data-brand="${id}"][data-theme="${appearance}"]`,
        );
        for (const [role, token] of Object.entries(RADIUS_TO_TOKEN) as [
          keyof BrandRadii,
          string,
        ][]) {
          expect(decls[token], `${id} --${token}`).toBe(radiusCss(BRANDS[id].radii[role]));
        }
      }
    }
  });

  it('keeps geometry on the brand axis only, never the appearance', () => {
    // A corner does not change when the lights go out. Both appearances of a
    // brand read the same radii by construction; this pins that they are emitted
    // that way too, so a future per-appearance override has to be deliberate.
    const sheet = themeStylesheet();
    for (const id of BRAND_IDS) {
      const light = generatedBlock(sheet, `:root[data-brand="${id}"][data-theme="light"]`);
      const dark = generatedBlock(sheet, `:root[data-brand="${id}"][data-theme="dark"]`);
      for (const token of RADIUS_TOKENS) expect(dark[token], `${id} --${token}`).toBe(light[token]);
    }
  });

  it('keeps the export constants tied to the same brand roles', () => {
    // These two are what export-css renders. They were their own numbers, which
    // is how the report and the export came to disagree in the first place.
    expect(CARD_RADIUS_PX).toBe(BRANDS[DEFAULT_BRAND].radii.card);
    expect(IMAGE_RADIUS_PX).toBe(BRANDS[DEFAULT_BRAND].radii.figure);
    expect(IMAGE_RADIUS_PX).toBeLessThan(CARD_RADIUS_PX);
  });

  it('applies the right role to the document surfaces', () => {
    // The two the report and the export must agree on, named explicitly so a
    // reshuffle of the scale cannot quietly move them.
    const css = sheet('src/renderer/project/project.css');
    function ruleBlock(sel: string): string {
      const at = css.indexOf(sel + ' {');
      expect(at, sel + ' not found').toBeGreaterThan(-1);
      return css.slice(at, css.indexOf('}', at));
    }
    for (const sel of ['.rep__bodywrap', '.rep__intro']) {
      expect(ruleBlock(sel), sel).toContain('border-radius: var(--radius-card)');
    }
    expect(ruleBlock('.rep__imgwrap')).toContain('border-radius: var(--radius-figure)');
    // Dialogs are not document cards: a modal's shape must not decide a
    // document's, which is why 'panel' exists as a separate role.
    for (const sel of ['.confirm', '.sop__modal']) {
      expect(ruleBlock(sel), sel).toContain('border-radius: var(--radius-panel)');
    }
  });
});
