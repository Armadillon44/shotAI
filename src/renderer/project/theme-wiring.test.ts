import { describe, it, expect } from 'vitest';
import fs from 'node:fs';
import { pinIsUnrecognised, pinnedBrand } from '../../shared/theme-palette';

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
    expect(src).toMatch(
      /const activeBrand = \(openPath && !showSettings && projectTheme\) \|\| brand;/,
    );
    expect(src, 'the project brand has to come from the store').toMatch(
      /useProjectStore\(\(s\) => s\.projectTheme\)/,
    );
  });

  it('keeps Settings on the app preference, even from inside a pinned project', () => {
    // Rule 3 of the settled precedence: Home and Settings belong to no project.
    // Easy to miss here because Settings REPLACES the project view in the same
    // window (App renders ProjectDetail under `showDetail && !showSettings`),
    // so openPath is still set while the project is not on screen — and without
    // the term the Settings sheet wore the project's brand.
    const src = read('src/renderer/project/App.tsx');
    expect(src, 'the brand must drop back to the app preference in Settings').toMatch(
      /activeBrand = \([^)]*!showSettings[^)]*\)/,
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
    // projectPinUnrecognised joined the list in #107: without it the menu keeps
    // ticking "App default" after opening a project with an unreadable pin.
    expect(src).toMatch(/\[openPath,\s*projectTheme,\s*projectPinUnrecognised,\s*brand\]/);
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

  it('passes the choice through untouched, null included', () => {
    // null ("App default", clear the key) and the DEFAULT BRAND ("pin shotAI")
    // are different requests, and the renderer used to fold the first into the
    // second with `choice ?? DEFAULT_BRAND`. That made them the same request and
    // left no way to pin the default — which is exactly the bug that shipped:
    // with the app brand on LFI, every menu entry produced the same document.
    const src = read('src/renderer/project/App.tsx');
    expect(src).toMatch(/setProjectTheme\(projectPath, choice\)/);
    expect(src, 'the renderer must not flatten null into a brand').not.toMatch(
      /choice \?\? DEFAULT_BRAND/,
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
    // Sliced to the END of the setBrandMenuState call rather than a fixed number
    // of characters. The 600-char window this used to take broke the moment the
    // handler grew a comment (#107), which is a test failing on prose rather than
    // on behaviour.
    const body = src.slice(at);
    const handler = body.slice(0, body.indexOf('});', body.indexOf('setBrandMenuState(')));
    expect(handler, 'an unknown brand must land on the default').toContain('coerceBrand(');
    // The unrecognised-pin flag crosses the same boundary and is trusted as a
    // boolean, so it has to be compared rather than cast (#107).
    expect(handler, 'the flag must be coerced, not truthy-tested').toContain(
      "s.projectPinUnrecognised === true",
    );
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

  it('offers every brand, the default one included', () => {
    // It was once filtered out, on the reasoning that the write rule stored the
    // default as an absent key so picking it could not differ from "App
    // default". With the APP brand set to LFI that left "App default" and "LFI"
    // both rendering LFI and shotAI absent from the menu — every option
    // produced the same document. The write rule was corrected instead.
    const src = read('src/main/menu.ts');
    expect(src, 'no brand may be filtered out of the group').not.toMatch(
      /BRAND_IDS\.filter\(/,
    );
    expect(src).toMatch(/BRAND_IDS\.map\(/);
  });

  it('keeps the rebuild off the click path', () => {
    // Replacing the application menu while the native menu is still tearing
    // down after a click is a known way to lose the window on Windows, and the
    // click -> IPC -> write -> push -> rebuild round trip lands inside exactly
    // that window. Two defences, because the deferral alone is a race:
    const src = read('src/main/menu.ts');
    // 1. the click records its own choice, so the renderer's echo matches and
    //    the changed-check rebuilds nothing at all
    expect(src).toMatch(/brandState = \{ \.\.\.brandState, projectTheme: brand \}/);
    // 2. and any rebuild that does happen is deferred off the current stack
    expect(src).toContain('scheduleRebuild()');
    expect(src, 'a rebuild must not run synchronously from the state push').not.toMatch(
      /brandState = next;\s*\n\s*rebuildMenu\?\.\(\)/,
    );
  });

  it('never lets a menu rebuild fail the call that triggered it', () => {
    // The menu is cosmetic and the setting it describes is already saved; a
    // throw here would reject the renderer's IPC call and report a failure that
    // did not happen.
    const src = read('src/main/menu.ts');
    const at = src.indexOf('function scheduleRebuild');
    expect(at, 'scheduleRebuild should exist').toBeGreaterThan(-1);
    expect(src.slice(at, at + 500)).toMatch(/try \{[\s\S]*?\} catch/);
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


describe('the brand menu never ticks a state the project is not in (#107)', () => {
  // A project can carry a pin this build cannot read — a brand a NEWER build
  // wrote. pinnedBrand is null for it, correctly, because the pin cannot be
  // honoured and the document really does render in the app brand. But null also
  // means "no pin at all", and the menu was bound straight to it, so "App
  // default" showed as selected for a pinned project. Clicking that already-
  // ticked row is presented by the UI as a no-op, and it DELETED the pin and
  // re-dated the project — the loss #95 preserved the value to prevent.
  //
  // macOS reached the same design (their #119): show nothing ticked, and do not
  // offer the unrecognised state as a choice, since it is not something a user
  // can pick.
  const read = (f: string): string => fs.readFileSync(f, 'utf8');

  it('distinguishes an unreadable pin from no pin at all', () => {
    // The predicate the whole fix rests on. Asserted in every direction, because
    // one that answered true for everything, or for nothing, would satisfy a
    // one-sided test.
    expect(pinIsUnrecognised('solarpunk'), 'a brand a newer build wrote').toBe(true);
    expect(pinIsUnrecognised('lfi'), 'a brand this build knows is not unrecognised').toBe(false);
    expect(pinIsUnrecognised('shotAI'), 'nor is the default, pinned explicitly').toBe(false);
    expect(pinIsUnrecognised(undefined), 'no key at all is not a pin').toBe(false);
    expect(pinIsUnrecognised(''), 'an empty string is not a brand anyone chose').toBe(false);
    expect(pinIsUnrecognised(42), 'a non-string never reaches a manifest we wrote').toBe(false);
  });

  it('and pinnedBrand still answers null for BOTH, which is why the flag exists', () => {
    // The two functions answer different questions about the same value. If
    // pinnedBrand ever started distinguishing them, an unrecognised brand would
    // reach data-brand and drop the page to the bare :root — default brand,
    // LIGHT palette, in dark mode too.
    expect(pinnedBrand('solarpunk')).toBeNull();
    expect(pinnedBrand(undefined)).toBeNull();
  });

  it('ticks App default only when nothing is pinned', () => {
    const src = read('src/main/menu.ts');
    // The checked expression must consult BOTH. Matching only the old
    // `projectTheme === null` would pass with the fix reverted.
    expect(src).toMatch(
      /checked:\s*!brandState\.projectPinUnrecognised\s*&&\s*brandState\.projectTheme === null/,
    );
  });

  it('never offers the unrecognised state as something to pick', () => {
    // It is not a choice a user can make, only a state a project can be in. If
    // it appeared in the radio list, picking it would have no meaning.
    const src = read('src/main/menu.ts');
    const items = src.slice(src.indexOf('label: `App default'));
    expect(items).not.toMatch(/label:.*[Uu]nrecognised/);
    expect(items).not.toMatch(/label:.*[Uu]nknown/);
  });

  it('carries the flag through every layer between the manifest and the menu', () => {
    // Windows narrows the raw value away in the renderer, deliberately, so the
    // signal needs its own path. A layer that drops it silently re-opens the bug,
    // and tsc cannot catch a field that is simply never set.
    for (const f of [
      'src/renderer/project/store.ts',
      'src/renderer/project/App.tsx',
      'src/shared/ipc.ts',
      'src/preload/preload.ts',
      'src/main/main.ts',
      'src/main/menu.ts',
    ]) {
      expect(read(f), `${f} must carry projectPinUnrecognised`).toContain(
        'projectPinUnrecognised',
      );
    }
  });

  it('clears the flag when the project closes', () => {
    // A stale true would tick nothing for the NEXT project, which is the same
    // class of lie in the other direction.
    const src = read('src/renderer/project/store.ts');
    expect(src.match(/projectPinUnrecognised: false/g)?.length ?? 0).toBeGreaterThanOrEqual(3);
  });
});
