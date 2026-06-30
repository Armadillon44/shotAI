import type { ForgeConfig } from '@electron-forge/shared-types';
import { MakerSquirrel } from '@electron-forge/maker-squirrel';
import { MakerZIP } from '@electron-forge/maker-zip';
import { MakerDeb } from '@electron-forge/maker-deb';
import { MakerRpm } from '@electron-forge/maker-rpm';
import { VitePlugin } from '@electron-forge/plugin-vite';
import { FusesPlugin } from '@electron-forge/plugin-fuses';
import { FuseV1Options, FuseVersion } from '@electron/fuses';

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
      './vendor/tessdata',
    ],
  },
  // Our native deps (uiohook-napi, node-screenshots, get-windows) are all N-API
  // (ABI-stable) and ship/carry x64 prebuilts, which are resolved at runtime by
  // the x64 (emulated) Electron. Skip Forge's auto-rebuild — there's nothing to
  // build, and source-building here would fail (no Python/MSVC on this box).
  rebuildConfig: { onlyModules: [] },
  makers: [
    new MakerSquirrel({ setupIcon: './assets/shotAI_icon.ico' }),
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
