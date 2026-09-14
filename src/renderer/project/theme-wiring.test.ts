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

describe('View -> Brand is driven by what is actually open', () => {
  // The per-project brand control moved out of the command bar into the native
  // application menu, which makes it a GLOBAL object describing a PER-PROJECT
  // setting. Every failure mode of that arrangement is silent: the menu keeps
  // rendering, it just describes the wrong thing.
  //
  // None of it is reachable from a unit test — menu.ts imports electron, and
  // vitest runs with environment 'node' — so the wiring is asserted from source,
  // the same technique as admx-contract and federation-cache-wiring.

  it('App pushes the state, and pushes it on every input that changes it', () => {
    const src = read('src/renderer/project/App.tsx');
    expect(src, 'App should push the menu state').toContain('setBrandMenu(');
    // A dependency array missing any one of these leaves the menu describing a
    // previous project, or checking the wrong item after the app brand changes.
    expect(src).toMatch(/\[openPath,\s*projectTheme,\s*brand\]/);
  });

  it('App pushes from the top level, not from the project view', () => {
    // ProjectDetail is UNMOUNTED when no project is open — which is exactly the
    // state the menu has to be told about, so it can disable itself.
    expect(read('src/renderer/project/ProjectDetail.tsx')).not.toContain('setBrandMenu');
  });

  it('App subscribes to the menu, or the items do nothing at all', () => {
    const src = read('src/renderer/project/App.tsx');
    expect(src).toContain('onMenuSetProjectTheme(');
    // Registered ONCE and reading the store imperatively. A subscription
    // re-registered per project change is absent for a frame, and a click that
    // lands in that frame is simply lost.
    expect(src, 'the listener should read the store, not close over the path').toContain(
      'useProjectStore.getState()',
    );
  });

  it('"App default" clears the key rather than writing the default brand', () => {
    // null and the default brand are different intents. The store maps the
    // default to an absent key, so passing it through is right — but only
    // because null has already been turned into it deliberately.
    expect(read('src/renderer/project/App.tsx')).toMatch(
      /setProjectTheme\(projectPath, choice \?\? DEFAULT_BRAND\)/,
    );
  });

  it('main arms the rebuilder BEFORE the first menu build', () => {
    // Order is the whole of it. setBrandMenuState can arrive as soon as the
    // renderer mounts; with no rebuilder registered the state is stored and
    // never drawn, and the menu sits at its startup values indefinitely.
    const src = read('src/main/main.ts');
    const arm = src.indexOf('armBrandMenu(');
    const install = src.indexOf('installAppMenu(');
    expect(arm, 'main should arm the brand menu').toBeGreaterThan(-1);
    expect(install, 'main should install the menu').toBeGreaterThan(-1);
    expect(arm, 'armBrandMenu must run before installAppMenu').toBeLessThan(install);
  });

  it('main coerces the pushed state instead of trusting it', () => {
    const src = read('src/main/main.ts');
    const at = src.indexOf('IpcChannels.setBrandMenu');
    expect(at, 'main should handle setBrandMenu').toBeGreaterThan(-1);
    const handler = src.slice(at, at + 600);
    expect(handler, 'an unknown brand must land on the default').toContain('coerceBrand(');
  });

  it('the menu refuses to rebuild when nothing changed', () => {
    // The push comes from an effect that also runs for unrelated re-renders, and
    // rebuilding the application menu under the cursor closes an open menu.
    const src = read('src/main/menu.ts');
    const at = src.indexOf('export function setBrandMenuState');
    expect(at).toBeGreaterThan(-1);
    const fn = src.slice(at, src.indexOf('\n}', at));
    expect(fn).toContain('brandState.projectOpen');
    expect(fn).toContain('brandState.projectTheme');
    expect(fn).toContain('brandState.appBrand');
    expect(fn, 'an unchanged push should return early').toMatch(/return;/);
  });

  it('the Brand submenu is disabled when no project is open', () => {
    // It is per project. Left enabled it opens onto radio buttons that set
    // nothing, which reads as the feature being broken.
    expect(read('src/main/menu.ts')).toMatch(/enabled:\s*brandState\.projectOpen/);
  });

  it('does not offer the default brand as a named, pinnable entry', () => {
    // The cross-platform write rule stores the default brand as an ABSENT key,
    // so picking it is indistinguishable from "App default" — the radio would
    // snap back to the entry above it every time. Offering it would be a control
    // that visibly ignores the user.
    const src = read('src/main/menu.ts');
    expect(src).toMatch(/BRAND_IDS\.filter\(\(id\) => id !== DEFAULT_BRAND\)/);
  });

  it('spells the View menu out so the standard entries survive', () => {
    // Replacing `role: 'viewMenu'` with a hand-built submenu is what lets Brand
    // join it; keeping every standard entry on its ROLE is what keeps
    // reload/zoom/fullscreen and their accelerators working.
    const src = read('src/main/menu.ts');
    const at = src.indexOf("label: 'View'");
    expect(at, 'View should be spelled out').toBeGreaterThan(-1);
    const view = src.slice(at, src.indexOf("{ role: 'windowMenu' }", at));
    for (const r of ['reload', 'forceReload', 'toggleDevTools', 'resetZoom', 'zoomIn', 'zoomOut', 'togglefullscreen']) {
      expect(view, `View lost the ${r} role`).toContain(`role: '${r}'`);
    }
    expect(src, "the role menu must not also be present").not.toContain("{ role: 'viewMenu' }");
  });
});

