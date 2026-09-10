import { describe, it, expect } from 'vitest';
import fs from 'node:fs';
import {
  DEFAULT_EXPORT_THEME,
  chipRadiusCss,
  exportTheme,
  fontStackFor,
  plainFontStackFor,
} from './export-theme';
import { BRANDS, BRAND_IDS, DEFAULT_BRAND } from './theme-palette';
import { docCss, plainCss } from '../main/export-css';

describe('an export is a brand, never an appearance', () => {
  it('takes the light palette for every brand, whatever the app is wearing', () => {
    // The rule with a reason: a dark-background SOP is unreadable printed and
    // ruinous on toner, and an export is not a screenshot of the UI. Asserted
    // rather than commented, because "the current palette" is the obvious wrong
    // thing to reach for when someone later wires this to the live theme.
    for (const id of BRAND_IDS) {
      const t = exportTheme(id);
      expect(t.palette, `${id} must export its LIGHT values`).toBe(BRANDS[id].light);
      expect(t.palette).not.toBe(BRANDS[id].dark);
    }
  });

  it('carries the brand it was asked for, and falls back safely', () => {
    expect(exportTheme('lfi').brand).toBe('lfi');
    expect(exportTheme('nonsense' as never).brand).toBe(DEFAULT_BRAND);
  });

  it('defaults to the brand every existing caller used to get', () => {
    expect(DEFAULT_EXPORT_THEME.brand).toBe(DEFAULT_BRAND);
    expect(DEFAULT_EXPORT_THEME.palette).toBe(BRANDS[DEFAULT_BRAND].light);
  });
});

describe('the document reskins with the brand', () => {
  const shot = docCss(1, exportTheme('shotAI'));
  const lfi = docCss(1, exportTheme('lfi'));

  it('replaces every colour, leaving none of the other brand behind', () => {
    // A single missed substitution is a violet hairline in a charcoal document,
    // which reads as a rendering fault rather than a missed conversion.
    const strays: string[] = [];
    for (const v of Object.values(BRANDS.shotAI.light)) {
      if (Object.values(BRANDS.lfi.light).includes(v)) continue; // shared by both
      if (lfi.includes(v)) strays.push(v);
    }
    expect(strays, `the LFI document still contains shotAI colours: ${strays.join(', ')}`).toEqual(
      [],
    );
  });

  it('actually differs, so a passing test above is not vacuous', () => {
    expect(lfi).not.toBe(shot);
    expect(lfi).toContain(BRANDS.lfi.light.accent);
    expect(shot).toContain(BRANDS.shotAI.light.accent);
  });

  it('carries the brand geometry, not only its colour', () => {
    expect(lfi).toContain(`border-radius:${BRANDS.lfi.radii.card}px`);
    expect(lfi).toContain(`border-radius:${BRANDS.lfi.radii.figure}px`);
  });

  it('draws the step badge as the brand draws a chip', () => {
    // macOS shipped an LFI document with square-ish cards and perfectly circular
    // step numbers, by treating border-radius:50% as structural geometry. It is
    // not: the badge is a chip, and a brand decides what a chip looks like.
    expect(chipRadiusCss(BRANDS.shotAI.radii)).toBe('50%');
    expect(chipRadiusCss(BRANDS.lfi.radii)).toBe(`${BRANDS.lfi.radii.chip}px`);
    expect(shot).toContain('border-radius:50%');
    expect(lfi).not.toContain('border-radius:50%');
  });

  it('names the brand face first and never embeds it', () => {
    // Embedding is the thing to keep out: as base64 the face is ~0.84MB against a
    // measured 0.8-1.5MB paste budget, so it would consume most of the budget and
    // the images would silently drop.
    expect(fontStackFor('lfi')).toMatch(/^"Archivo",/);
    expect(fontStackFor('shotAI')).not.toContain('Archivo');
    expect(lfi).toContain('font-family:"Archivo"');
    for (const css of [shot, lfi, plainCss(1, exportTheme('lfi'))]) {
      expect(css, 'a font must never be embedded in an exported document').not.toContain(
        '@font-face',
      );
    }
  });

  it('keeps Arial behind the brand face in the Word-facing export', () => {
    // Arial-first there is a COMPATIBILITY decision (#42), not a design one: that
    // document exists to be pasted into Word and Google Docs, where substitution
    // is silent and Arial is the one face present everywhere.
    expect(plainFontStackFor('shotAI')).toBe('Arial,Helvetica,sans-serif');
    expect(plainFontStackFor('lfi')).toBe('"Archivo",Arial,Helvetica,sans-serif');
    expect(plainCss(1, exportTheme('shotAI'))).toContain('font-family:Arial,Helvetica,sans-serif');
  });
});

describe('the brand actually reaches the exporters', () => {
  // The failure macOS hit in testing and reported on #77: "LFI selected,
  // documents still violet". Every builder takes `theme` with a DEFAULT, which is
  // what keeps existing callers working and is also what makes a forgotten
  // argument invisible — the export succeeds and produces the wrong brand.
  const read = (f: string): string => fs.readFileSync(f, 'utf8');

  it('export.ts passes the theme to every builder it calls', () => {
    const src = read('src/main/export.ts');
    for (const call of ['buildDocx(', 'buildPptx(', 'buildPlainHtmlDoc(', 'buildHtmlDoc(']) {
      const at = src.indexOf(call, src.indexOf('export async function exportProject'));
      expect(at, `${call} is not called from exportProject`).toBeGreaterThan(-1);
    }
    // Every invocation inside exportProject must hand the theme over.
    const bodyStart = src.indexOf('export async function exportProject');
    const body = src.slice(bodyStart);
    // Paren-balanced, because one of these calls nests two calls in an argument
    // (`htmlEmbedPolicy(format, clampScale(...))`) and a lazy regex stops inside it.
    for (const m of body.matchAll(/build(Docx|Pptx|PlainHtmlDoc|HtmlDoc)\(/g)) {
      const open = (m.index ?? 0) + m[0].length - 1;
      let depth = 0;
      let end = open;
      for (let i = open; i < body.length; i++) {
        if (body[i] === '(') depth++;
        else if (body[i] === ')') {
          depth--;
          if (depth === 0) {
            end = i;
            break;
          }
        }
      }
      expect(
        body.slice(open + 1, end),
        `build${m[1]} is called without the theme`,
      ).toContain('theme');
    }
  });

  it('the IPC layer supplies the app brand as the fallback', () => {
    // Without this the setting persists, Settings shows it selected, and every
    // exported document stays the default brand forever.
    const src = read('src/main/ipc.ts');
    expect(src, 'ipc.ts should read the app brand').toContain('getBrand()');
    const handlers = [...src.matchAll(/exportProject\(([\s\S]*?)\n {6}\}\);/g)];
    expect(handlers.length, 'expected three export handlers').toBe(3);
    for (const h of handlers) {
      expect(h[1], 'an export handler does not pass a brand').toContain('brand:');
    }
  });

  it('resolves the project brand ahead of the app brand, in exportProject', () => {
    // The precedence has to live where the MANIFEST is. A caller resolving it
    // would have to read project.json a second time, and the two reads would
    // eventually disagree — which is the whole failure mode #77 phase 1b exists
    // to prevent: the same project exporting differently on two machines.
    const src = read('src/main/export.ts');
    expect(src, 'exportProject should prefer the project key').toMatch(
      /exportTheme\(\s*coerceBrand\(manifest\.theme \?\? opts\.brand\)\s*\)/,
    );
  });
});
