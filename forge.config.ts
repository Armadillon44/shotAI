import type { ForgeConfig } from '@electron-forge/shared-types';
import { MakerSquirrel } from '@electron-forge/maker-squirrel';
import { MakerZIP } from '@electron-forge/maker-zip';
import { MakerDeb } from '@electron-forge/maker-deb';
import { MakerRpm } from '@electron-forge/maker-rpm';
import { VitePlugin } from '@electron-forge/plugin-vite';
import { FusesPlugin } from '@electron-forge/plugin-fuses';
import { FuseV1Options, FuseVersion } from '@electron/fuses';
import { cpSync, existsSync, mkdirSync, readFileSync } from 'node:fs';
import path from 'node:path';

/**
 * Copy the PRODUCTION node_modules closure into the packaged app.
 *
 * @electron-forge/plugin-vite packages only the Vite build output (.vite/) +
 * package.json and does NOT copy node_modules — it assumes Vite bundles every
 * dependency. But our main/preload builds intentionally EXTERNALIZE the runtime
 * deps that can't (or shouldn't) be bundled: the native capture/hotkey/element +
 * OCR modules (uiohook-napi, node-screenshots, get-windows, koffi + the optional
 * *-win32-x64* binary packages), tesseract.js (ships worker + wasm files), and
 * electron-log / @anthropic-ai/sdk / zod. Without their node_modules the packaged
 * app crashes at launch (`Cannot find module 'electron-log/main'`).
 *
 * We WALK THE ACTUAL RUNTIME CLOSURE (each package.json's dependencies +
 * optionalDependencies, breadth-first from the root's prod deps) rather than
 * trusting package-lock's dev/devOptional flags — npm marks some genuinely-needed
 * transitive deps (e.g. readable-stream, reached via get-windows → node-pre-gyp →
 * npmlog → are-we-there-yet) as devOptional, which a flag-based filter would
 * wrongly drop. Each package dir is copied recursively (so its NESTED node_modules
 * for version conflicts ride along); .node binaries are then unpacked from the
 * asar by packagerConfig.asar.unpack.
 */
function copyProductionNodeModules(buildPath: string): void {
  const projectRoot = process.cwd();
  const nm = path.join(projectRoot, 'node_modules');
  const readPkg = (dir: string): { dependencies?: Record<string, string>; optionalDependencies?: Record<string, string> } | null => {
    try {
      return JSON.parse(readFileSync(path.join(dir, 'package.json'), 'utf8'));
    } catch {
      return null;
    }
  };
  const root = readPkg(projectRoot) ?? {};
  const queue: string[] = [
    ...Object.keys(root.dependencies ?? {}),
    ...Object.keys(root.optionalDependencies ?? {}),
  ];
  const visited = new Set<string>();
  let copied = 0;
  while (queue.length) {
    const name = queue.shift() as string;
    if (visited.has(name)) continue;
    visited.add(name);
    const from = path.join(nm, name);
    if (!existsSync(from)) continue; // optional/peer not installed, or only nested (rides via parent copy)
    const to = path.join(buildPath, 'node_modules', name);
    mkdirSync(path.dirname(to), { recursive: true });
    cpSync(from, to, { recursive: true });
    copied++;
    const pkg = readPkg(from);
    for (const dep of [
      ...Object.keys(pkg?.dependencies ?? {}),
      ...Object.keys(pkg?.optionalDependencies ?? {}),
    ]) {
      if (!visited.has(dep)) queue.push(dep);
    }
  }
  // eslint-disable-next-line no-console
  console.log(`[forge] packageAfterCopy: copied ${copied} production node_modules into the package`);
}

// App version, read from package.json (process.cwd() is the project root during
// `electron-forge make`, same assumption copyProductionNodeModules relies on).
// Used to name the installer so the built artifact needs no post-build rename.
const { version: APP_VERSION } = JSON.parse(
  readFileSync(path.join(process.cwd(), 'package.json'), 'utf8'),
) as { version: string };

const config: ForgeConfig = {
  packagerConfig: {
    // Packaged-app/exe icon. Extension omitted on purpose — electron-packager
    // auto-completes it per platform (.ico on Windows, .icns on macOS).
    icon: './assets/shotAI_icon',
    // Keep asar, but UNPACK native binaries (.node) — uiohook-napi,
    // node-screenshots, get-windows, and koffi (+ its @koromix/* binary) can't be
    // loaded from inside an asar archive.
    asar: { unpack: '**/*.node' },
    // Ship the native UI-element-locator dll AND the runtime app icon (PNG, used
    // for the window/taskbar icon + About dialog via appIconPath) into the app's
    // resources/. Neither is a .node, so the unpack rule doesn't cover them —
    // extraResource places each at resources/<basename>.
    // Also ship the vendored Tesseract eng model dir → resources/tessdata, so
    // auto-redaction OCR reads it locally (offline, CDN-free) via ocr.ts langPath.
    extraResource: [
      './native/element-locator/element_locator.dll',
      './assets/shotAI_icon.png',
      // Bundled .ico used post-install to fix the "Installed apps" icon (see the
      // MakerSquirrel note below + src/main/arp-icon.ts).
      './assets/shotAI_icon.ico',
      './vendor/tessdata',
    ],
  },
  // Our native deps (uiohook-napi, node-screenshots, get-windows) are all N-API
  // (ABI-stable) and ship/carry x64 prebuilts, which are resolved at runtime by
  // the x64 (emulated) Electron. Skip Forge's auto-rebuild — there's nothing to
  // build, and source-building here would fail (no Python/MSVC on this box).
  rebuildConfig: { onlyModules: [] },
  hooks: {
    // The Vite plugin omits node_modules; copy the production closure back in so
    // the externalized runtime deps resolve in the packaged app. See the helper.
    packageAfterCopy: async (_forgeConfig, buildPath) => {
      copyProductionNodeModules(buildPath);
    },
  },
  makers: [
    // setupExe: emit a clean hyphenated name (Squirrel's default is
    // "shotAI-<version> Setup.exe" — the space is awkward in URLs/downloads).
    new MakerSquirrel({
      setupIcon: './assets/shotAI_icon.ico',
      setupExe: `shotAI-${APP_VERSION}-Setup.exe`,
      // Custom install animation (icon + dancing sparkles + a looping "installing"
      // bar). Generated by scripts/make-loading-gif.cjs. Squirrel loops this while
      // it installs — it can't be driven by real % (the bar is indeterminate).
      loadingGif: './assets/shotAI-install.gif',
      // Publisher shown in "Installed apps" (else it falls back to package.json
      // author). Kept impersonal per request. NB: iconUrl is intentionally NOT set
      // — the usual iconUrl would bake a personal GitHub URL into the installed
      // package. The "Installed apps" (ARP) icon is instead fixed locally at
      // install/update time by src/main/arp-icon.ts, which writes the bundled
      // shotAI_icon.ico (extraResource above) to app.ico AND sets the ARP
      // DisplayIcon registry value to it (Squirrel leaves DisplayIcon blank when it
      // can't download an iconUrl). No URL, no personal data.
      authors: 'LFI',
    }),
    new MakerZIP({}, ['darwin']),
    new MakerRpm({}),
    new MakerDeb({}),
  ],
  plugins: [
    new VitePlugin({
      // `build` can specify multiple entry builds, which can be Main process, Preload scripts, Worker process, etc.
      // If you are familiar with Vite configuration, it will look really familiar.
      build: [
        {
          // `entry` is just an alias for `build.lib.entry` in the corresponding file of `config`.
          entry: 'src/main/main.ts',
          config: 'vite.main.config.ts',
          target: 'main',
        },
        {
          entry: 'src/preload/preload.ts',
          config: 'vite.preload.config.ts',
          target: 'preload',
        },
      ],
      renderer: [
        {
          // Single renderer build with two HTML entry points (index.html =
          // project window, toolbar.html = capture toolbar). See
          // vite.renderer.config.ts for the rollup inputs.
          name: 'main_window',
          config: 'vite.renderer.config.ts',
        },
      ],
    }),
    // Fuses are used to enable/disable various Electron functionality
    // at package time, before code signing the application
    new FusesPlugin({
      version: FuseVersion.V1,
      [FuseV1Options.RunAsNode]: false,
      [FuseV1Options.EnableCookieEncryption]: true,
      [FuseV1Options.EnableNodeOptionsEnvironmentVariable]: false,
      [FuseV1Options.EnableNodeCliInspectArguments]: false,
      [FuseV1Options.EnableEmbeddedAsarIntegrityValidation]: true,
      [FuseV1Options.OnlyLoadAppFromAsar]: true,
    }),
  ],
};

export default config;
