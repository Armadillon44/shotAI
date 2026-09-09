import { describe, it, expect } from 'vitest';
import fs from 'node:fs';
import {
  APP_LIGHT,
  DOC_LIGHT,
  SLIDE_LIGHT,
  KNOWN_DIVERGENCES,
  hexNoHash,
  paletteFor,
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
