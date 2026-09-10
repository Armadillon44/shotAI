import { describe, it, expect } from 'vitest';
import fs from 'node:fs';

// The brand axis only works if BOTH ends of it are connected, and neither end is
// something a typecheck can notice is missing:
//
//   - applyTheme takes `brand` with a DEFAULT, so a caller that forgets it still
//     compiles and still applies a theme. It just always applies the default one,
//     and the picker looks broken in a way that reads as a palette problem.
//   - Settings' onBrandChanged is an OPTIONAL prop, so App can omit it and the
//     picker persists the choice while the window it is sitting in never repaints
//     until the next launch.
//
// Both are the shape that has bitten this codebase before: a function wired at one
// end only (invalidateFederationConfig had zero callers while two merged documents
// described its behaviour). So the wiring is asserted from source. The renderer is
// not import-clean under plain node, and vitest runs with environment 'node', so
// reading the code is also the only option available.

const read = (f: string): string => fs.readFileSync(f, 'utf8');

describe('the brand reaches the document', () => {
  it('applyTheme writes both data-theme and data-brand', () => {
    // One attribute without the other selects no generated block at all, because
    // they are emitted fully qualified. The page would fall back to the bare
    // :root block and dark mode would silently stop working.
    const src = read('src/renderer/project/theme.ts');
    expect(src).toMatch(/root\.dataset\.theme\s*=/);
    expect(src).toMatch(/root\.dataset\.brand\s*=/);
  });

  it('applyTheme installs the generated token sheet', () => {
    const src = read('src/renderer/project/theme.ts');
    expect(src).toContain('installThemeStyle()');
    expect(src).toContain('themeStylesheet()');
  });

  it('the renderer entry installs the sheet before the first render', () => {
    // Otherwise the first frame paints before any token exists. Order matters, so
    // this checks position, not merely presence.
    const src = read('src/renderer/project/main.tsx');
    const install = src.indexOf('installThemeStyle()');
    const render = src.indexOf('createRoot(');
    expect(install, 'main.tsx should call installThemeStyle()').toBeGreaterThan(-1);
    expect(render, 'main.tsx should call createRoot').toBeGreaterThan(-1);
    expect(install, 'installThemeStyle() must run before createRoot').toBeLessThan(render);
  });

  it('App passes a brand to applyTheme, not just the appearance', () => {
    const src = read('src/renderer/project/App.tsx');
    const calls = [...src.matchAll(/applyTheme\(([^)]*)\)/g)].map((m) => m[1]);
    expect(calls.length, 'App should apply the theme').toBeGreaterThan(0);
    for (const args of calls) {
      expect(args, `applyTheme(${args}) drops the brand`).toMatch(/[Bb]rand/);
    }
  });

  it('App re-applies when the brand changes, not only the appearance', () => {
    // A dependency array missing the brand leaves the attribute at whatever the
    // first render set, which looks like the picker not working.
    const src = read('src/renderer/project/App.tsx');
    expect(src).toMatch(/\[themePref,\s*activeBrand\]/);
  });

  it('an open project s own brand outranks the app preference (#77 1b)', () => {
    // The precedence, and the exact expression that encodes it. A project
    // carrying a brand wears it EVERYWHERE while open, chrome included: a
    // corporate report inside a violet shell is incoherent, and the report is
    // meant to be WYSIWYG with the export. `openPath &&` is what keeps Home and
    // Settings — which belong to no project — on the app preference.
    const src = read('src/renderer/project/App.tsx');
    expect(src).toMatch(/const activeBrand = \(openPath && projectTheme\) \|\| brand;/);
    expect(src, 'the project brand has to come from the store').toMatch(
      /useProjectStore\(\(s\) => s\.projectTheme\)/,
    );
  });

  it('App subscribes to the Settings brand picker', () => {
    const src = read('src/renderer/project/App.tsx');
    expect(src, 'Settings needs onBrandChanged wired or the change lands only on disk').toContain(
      'onBrandChanged=',
    );
  });

  it('Settings offers appearance and brand as two separate controls', () => {
    // The decision this encodes: a brand is not an appearance. Every brand exists
    // in light and dark, so a single combined picker would present "LFI" and
    // "Dark" as alternatives to one another.
    const src = read('src/renderer/project/Settings.tsx');
    expect(src).toContain(`aria-label="Theme"`);
    expect(src).toContain(`aria-label="Brand"`);
  });
});
