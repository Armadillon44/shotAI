import { describe, it, expect } from 'vitest';
import fs from 'node:fs';
import {
  APP_LIGHT,
  DOC_LIGHT,
  SLIDE_LIGHT,
  KNOWN_DIVERGENCES,
  hexNoHash,
  paletteFor,
  DOC_EXTRAS,
  SLIDE_EXTRAS,
  INTERNAL_SPLITS,
  CARD_RADIUS_PX,
  type Palette,
} from './theme-palette';

// Phase 0 of #77. These tests exist so that adding a second brand later cannot hide a
// regression, and so the drift that ALREADY exists between the four copies of this
// palette is written down rather than discovered again by someone comparing a report
// against its own PDF.
//
// Nothing here asserts what the colours SHOULD be. It asserts they are what shipped.

/** Map the app's `:root` custom properties to their hex values. */
function rootTokens(): Record<string, string> {
  const css = fs.readFileSync('src/renderer/project/project.css', 'utf8');
  const root = /^:root \{[\s\S]*?^\}/m.exec(css)?.[0] ?? '';
  expect(root, 'could not find the :root block in project.css').not.toBe('');
  const out: Record<string, string> = {};
  for (const m of root.matchAll(/--([a-z0-9-]+):\s*(#[0-9a-fA-F]{3,8})/g)) {
    out[m[1]] = m[2].toLowerCase();
  }
  return out;
}

/** Palette role -> the app custom property it mirrors. */
const ROLE_TO_TOKEN: Record<keyof Palette, string> = {
  accent: 'accent',
  accentPress: 'accent-press',
  accentTint: 'accent-tint',
  accentInk: 'accent-ink',
  onAccent: 'on-accent',
  ink: 'ink',
  ink2: 'ink-2',
  ink3: 'ink-3',
  hair: 'hair',
  hair2: 'hair-2',
  controlBd: 'control-bd',
  surface: 'surface',
  surface2: 'surface-2',
  ground: 'ground',
  fieldBg: 'field-bg',
  ok: 'ok',
  okTint: 'ok-tint',
  okInk: 'ok-ink',
  draft: 'draft',
  draftTint: 'draft-tint',
  draftInk: 'draft-ink',
  danger: 'danger',
  dangerInk: 'danger-ink',
  dangerTint: 'danger-tint',
  dangerBd: 'danger-bd',
  noteBg: 'note-bg',
  noteBd: 'note-bd',
  noteFg: 'note-fg',
  cautBg: 'caut-bg',
  cautBd: 'caut-bd',
  cautFg: 'caut-fg',
  warnBg: 'warn-bg',
  warnBd: 'warn-bd',
  warnFg: 'warn-fg',
};

describe('APP_LIGHT mirrors the shipped stylesheet', () => {
  it('matches every :root custom property it claims to mirror', () => {
    // The module is only worth having if it cannot drift from what the app renders.
    // Parsing the real stylesheet is what makes that true rather than aspirational.
    const tokens = rootTokens();
    for (const [role, token] of Object.entries(ROLE_TO_TOKEN) as [keyof Palette, string][]) {
      expect(tokens[token], `--${token} is missing from project.css :root`).toBeDefined();
      expect(APP_LIGHT[role], `APP_LIGHT.${role} vs --${token}`).toBe(tokens[token]);
    }
  });

  it('covers every colour-valued :root token, so none is left un-tokenized', () => {
    // If someone adds a colour to :root without adding a role here, the second brand
    // will have no value for it and that surface silently keeps the default.
    const tokens = rootTokens();
    const mapped = new Set(Object.values(ROLE_TO_TOKEN));
    const unmapped = Object.keys(tokens).filter((t) => !mapped.has(t));
    expect(unmapped, `these :root colours have no Palette role: ${unmapped.join(', ')}`).toEqual([]);
  });

  it('is all lowercase 6-digit hex, so comparisons never fail on case', () => {
    for (const [role, v] of Object.entries(APP_LIGHT)) {
      expect(v, `APP_LIGHT.${role}`).toMatch(/^#[0-9a-f]{6}$/);
    }
  });
});

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

describe('the document card radius agrees across surfaces (#77 phase 0b)', () => {
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

  it('matches the --radius-card token in the app stylesheet', () => {
    // The whole point of 0b: the report is meant to be WYSIWYG with the export, and
    // these two numbers lived in different files and disagreed without anyone noticing.
    const m = /--radius-card:\s*(\d+)px/.exec(css());
    expect(m, '--radius-card is missing from project.css').not.toBeNull();
    expect(Number(m?.[1])).toBe(CARD_RADIUS_PX);
  });

  it('is applied by TOKEN to every document card, never by literal', () => {
    // A literal here is how the two surfaces drifted apart in the first place.
    for (const sel of ['.rep__bodywrap', '.rep__intro', '.rep__imgwrap']) {
      expect(ruleBlock(sel), sel + ' should use var(--radius-card)').toContain(
        'border-radius: var(--radius-card)',
      );
    }
  });

  it('leaves app chrome on its own radius', () => {
    // Dialogs and the SOP panel share the old 12px but are NOT document cards. A
    // document's shape should not be decided by a modal's.
    for (const sel of ['.confirm', '.sop__modal']) {
      expect(ruleBlock(sel), sel + ' must not follow the card radius').not.toContain(
        '--radius-card',
      );
    }
  });

  it('is the only radius the document cards use, so none can drift back', () => {
    for (const sel of ['.rep__bodywrap', '.rep__intro', '.rep__imgwrap']) {
      const b = ruleBlock(sel);
      const literals = [...b.matchAll(/border-radius:\s*(\d+)px/g)].map((x) => x[0]);
      expect(literals, sel + ' hardcodes a radius').toEqual([]);
    }
  });
});
