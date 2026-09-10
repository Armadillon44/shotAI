import { describe, it, expect } from 'vitest';
import fs from 'node:fs';
import {
  APP_LIGHT,
  APP_DARK,
  BRANDS,
  BRAND_IDS,
  COLOUR_TOKENS,
  DEFAULT_BRAND,
  DOC_LIGHT,
  SLIDE_LIGHT,
  KNOWN_DIVERGENCES,
  ROLE_TO_TOKEN,
  coerceBrand,
  brandPalette,
  hexNoHash,
  paletteFor,
  themeStylesheet,
  DOC_EXTRAS,
  SLIDE_EXTRAS,
  INTERNAL_SPLITS,
  CARD_RADIUS_PX,
  IMAGE_RADIUS_PX,
  type Appearance,
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
          COLOUR_TOKENS.length,
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
    const declared = new Set<string>(COLOUR_TOKENS.map((t) => '--' + t));
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

describe('the export palettes diverge only where it is admitted', () => {
  // THE POINT OF PHASE 0. Four surfaces, one palette, already drifted. This pins the
  // drift so a NEW divergence fails instead of quietly joining the pile.
  const surfaces: Array<{ id: 'doc' | 'slide'; palette: Palette }> = [
    { id: 'doc', palette: DOC_LIGHT },
    { id: 'slide', palette: SLIDE_LIGHT },
  ];

  it('has no undeclared divergence from the app palette', () => {
    const declared = new Set(KNOWN_DIVERGENCES.map((d) => `${d.surface}:${d.role}`));
    const undeclared: string[] = [];
    for (const { id, palette } of surfaces) {
      for (const role of Object.keys(APP_LIGHT) as (keyof Palette)[]) {
        if (palette[role] === APP_LIGHT[role]) continue;
        if (!declared.has(`${id}:${role}`)) {
          undeclared.push(`${id}.${role}: app ${APP_LIGHT[role]} vs ${palette[role]}`);
        }
      }
    }
    expect(
      undeclared,
      'undeclared palette divergence(s). Either make them match, or add them to ' +
        `KNOWN_DIVERGENCES with a reason:\n  ${undeclared.join('\n  ')}`,
    ).toEqual([]);
  });

  it('does not record a divergence that no longer exists', () => {
    // A stale entry is as misleading as a missing one: it documents drift that was
    // since fixed, and would let a real regression hide behind an accepted name.
    const stale = KNOWN_DIVERGENCES.filter(
      (d) => paletteFor(d.surface)[d.role] === APP_LIGHT[d.role],
    );
    expect(
      stale.map((d) => `${d.surface}.${d.role}`),
      'these divergences are recorded but the values now agree; delete the entries',
    ).toEqual([]);
  });

  it('records each divergence with the values it actually ships', () => {
    for (const d of KNOWN_DIVERGENCES) {
      expect(d.app, `${d.surface}.${d.role} app value`).toBe(APP_LIGHT[d.role]);
      expect(paletteFor(d.surface)[d.role], `${d.surface}.${d.role} surface value`).toBe(
        d.surfaceValue,
      );
      expect(d.note.length, `${d.surface}.${d.role} needs a reason`).toBeGreaterThan(10);
    }
  });

  it('confirms the divergence is entirely in the neutrals', () => {
    // The one encouraging finding: the callout ramp, which is the part a reader
    // actually reads for meaning, never drifted. If a future change breaks that, it
    // shows up here.
    const callouts: (keyof Palette)[] = [
      'noteBg',
      'noteBd',
      'noteFg',
      'cautBg',
      'cautBd',
      'cautFg',
      'warnBg',
      'warnBd',
      'warnFg',
    ];
    for (const role of callouts) {
      expect(DOC_LIGHT[role], `DOC_LIGHT.${role} must match the app`).toBe(APP_LIGHT[role]);
      expect(SLIDE_LIGHT[role], `SLIDE_LIGHT.${role} must match the app`).toBe(APP_LIGHT[role]);
    }
    for (const d of KNOWN_DIVERGENCES) {
      expect(callouts, `${d.role} diverged, but callouts are supposed to agree`).not.toContain(
        d.role,
      );
    }
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

  it('round-trips every palette value on every surface', () => {
    for (const p of [APP_LIGHT, DOC_LIGHT, SLIDE_LIGHT]) {
      for (const [role, v] of Object.entries(p)) {
        expect(hexNoHash(v), role).toBe(v.slice(1).toUpperCase());
      }
    }
  });
});

describe('paletteFor', () => {
  it('returns the surface palettes', () => {
    expect(paletteFor('app')).toBe(APP_LIGHT);
    expect(paletteFor('doc')).toBe(DOC_LIGHT);
    expect(paletteFor('slide')).toBe(SLIDE_LIGHT);
  });
});

describe('the export-only roles record what actually ships', () => {
  it('pins each extra to the value it replaced', () => {
    // These are the nine literals that could not be folded into a surface palette,
    // because the surface renders BOTH ramps. Pinned so a later change is deliberate.
    expect(DOC_EXTRAS.cardBd).toBe('#e7e4f2');
    expect(DOC_EXTRAS.sectionH).toBe('#191826');
    expect(DOC_EXTRAS.sectionB).toBe('#5a5772');
    expect(SLIDE_EXTRAS.bodyInk).toBe('#374151');
    expect(SLIDE_EXTRAS.captionInk).toBe('#6b7280');
  });

  it('exists only because the value differs from the surface palette', () => {
    // An extra whose value already equals the surface role is not an extra: it is a
    // missed substitution, and it would silently freeze that element out of a future
    // brand change. cardBd vs DOC hair is the canonical case.
    expect(DOC_EXTRAS.cardBd).not.toBe(DOC_LIGHT.hair);
    expect(DOC_EXTRAS.sectionH).not.toBe(DOC_LIGHT.ink);
    expect(DOC_EXTRAS.sectionB).not.toBe(DOC_LIGHT.ink2);
    expect(SLIDE_EXTRAS.bodyInk).not.toBe(SLIDE_LIGHT.ink2);
    expect(SLIDE_EXTRAS.captionInk).not.toBe(SLIDE_LIGHT.ink3);
  });

  it('takes each extra value from the APP or DOC ramp, never invents one', () => {
    // Every extra should be a value already in use somewhere, or it is a colour
    // someone typed by hand and phase 0 has quietly blessed.
    const known = new Set([
      ...Object.values(APP_LIGHT),
      ...Object.values(DOC_LIGHT),
      ...Object.values(SLIDE_LIGHT),
    ]);
    for (const [k, v] of Object.entries({ ...DOC_EXTRAS, ...SLIDE_EXTRAS })) {
      expect(known.has(v), `${k} = ${v} is not a value any palette uses`).toBe(true);
    }
  });
});

describe('INTERNAL_SPLITS documents the two-ramp surfaces', () => {
  it('lists both values, and they really do both ship', () => {
    const known = new Set([
      ...Object.values(APP_LIGHT),
      ...Object.values(DOC_LIGHT),
      ...Object.values(SLIDE_LIGHT),
      ...Object.values(DOC_EXTRAS),
      ...Object.values(SLIDE_EXTRAS),
    ]);
    expect(INTERNAL_SPLITS.length).toBeGreaterThan(0);
    for (const split of INTERNAL_SPLITS) {
      expect(split.values.length, `${split.surface} ${split.concept}`).toBeGreaterThan(1);
      expect(new Set(split.values).size, `${split.surface} ${split.concept} duplicates`).toBe(
        split.values.length,
      );
      for (const v of split.values) {
        expect(known.has(v), `${split.surface} ${split.concept}: ${v} ships nowhere`).toBe(true);
      }
      expect(split.note.length, `${split.surface} ${split.concept} needs a reason`).toBeGreaterThan(
        20,
      );
    }
  });

  it('covers every extra, so no split is recorded in code but not in the list', () => {
    // The extras and the splits are two views of the same problem. If a role gets an
    // extra without an entry here, the inconsistency stops being reviewable.
    const recorded = INTERNAL_SPLITS.flatMap((s) => s.values);
    for (const [k, v] of Object.entries({ ...DOC_EXTRAS, ...SLIDE_EXTRAS })) {
      expect(recorded, `${k} = ${v} has no INTERNAL_SPLITS entry`).toContain(v);
    }
  });
});

describe('the document radii agree across surfaces (#77 phase 0b)', () => {
  const css = () => fs.readFileSync('src/renderer/project/project.css', 'utf8');

  /** The declarations inside one selector block. */
  function ruleBlock(sel: string): string {
    const src = css();
    const start = src.indexOf(sel + ' {');
    expect(start, sel + ' not found in project.css').toBeGreaterThan(-1);
    const end = src.indexOf('}', start);
    expect(end, sel + ' has no closing brace').toBeGreaterThan(start);
    return src.slice(start, end);
  }

  function token(name: string): number {
    const m = new RegExp('--' + name + ':\\s*(\\d+)px').exec(css());
    expect(m, '--' + name + ' is missing from project.css').not.toBeNull();
    return Number(m?.[1]);
  }

  // TWO pairings, not one. An earlier version asserted a single shared number and
  // folded the nested image in with the cards, which encoded a "one radius
  // everywhere" premise that is wrong: an inner frame sharing its parent radius
  // pinches the gap between the two curves to nothing at the corner. Phase 2 builds a
  // radius scale, so a false premise here would have become its foundation.
  it('pairs the CARD radius with --radius-card', () => {
    expect(token('radius-card')).toBe(CARD_RADIUS_PX);
  });

  it('pairs the nested IMAGE radius with --radius-image', () => {
    expect(token('radius-image')).toBe(IMAGE_RADIUS_PX);
  });

  it('keeps the nested image radius SMALLER than the card that contains it', () => {
    // The relationship is the durable rule, not either number. If a future brand
    // scales these, this is the invariant that must survive the scaling.
    expect(IMAGE_RADIUS_PX).toBeLessThan(CARD_RADIUS_PX);
    expect(token('radius-image')).toBeLessThan(token('radius-card'));
  });

  it('applies each token to the right elements, by token and never by literal', () => {
    for (const sel of ['.rep__bodywrap', '.rep__intro']) {
      expect(ruleBlock(sel), sel + ' should use var(--radius-card)').toContain(
        'border-radius: var(--radius-card)',
      );
    }
    // The screenshot wrap is the nested frame, so it takes the image token.
    expect(ruleBlock('.rep__imgwrap')).toContain('border-radius: var(--radius-image)');
    expect(ruleBlock('.rep__imgwrap')).not.toContain('--radius-card');
  });

  it('leaves app chrome on its own radius', () => {
    // Dialogs and the SOP panel share the old 12px but are NOT document surfaces. A
    // document's shape should not be decided by a modal's.
    for (const sel of ['.confirm', '.sop__modal']) {
      const b = ruleBlock(sel);
      expect(b, sel + ' must not follow the card radius').not.toContain(
        '--radius-card',
      );
      expect(b, sel + ' must not follow the image radius').not.toContain(
        '--radius-image',
      );
    }
  });

  it('lets no document surface hardcode a radius', () => {
    for (const sel of ['.rep__bodywrap', '.rep__intro', '.rep__imgwrap']) {
      const literals = [...ruleBlock(sel).matchAll(/border-radius:\s*(\d+)px/g)].map(
        (x) => x[0],
      );
      expect(literals, sel + ' hardcodes a radius').toEqual([]);
    }
  });
});
