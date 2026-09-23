# 12 Packaging, signing, deployment, CI and release

> Spec for the native rewrite. Sources read: `forge.config.ts` (201 lines), `package.json` (84), `vite.main.config.ts` (82), `vite.preload.config.ts` (4), `vite.renderer.config.ts` (20), `vitest.config.ts` (13), `scripts/postinstall.mjs` (157), `scripts/build-element-locator.mjs` (35), `scripts/make-loading-gif.cjs` (203), `scripts/report-strut-probe.cjs` (121), `scripts/report-width-probe.cjs` (66), `scripts/ts-register.mjs` (4), `scripts/ts-resolve.mjs` (20), `.github/workflows/ci.yml` (68), `.github/workflows/dotnet.yml` (71), `README.md` (401), `Intune/Windows/README.md` (117), `docs/HARDENING-PLAN.md` (296), `docs/PLAN.md` (280), `.npmrc` (11), `src/vite-env.d.ts` (1), `assets/` (`shotAI_icon.ico` 67646 bytes, `shotAI_icon.png` 77792 bytes, `shotAI_icon_v3.svg` 1784 bytes, `shotAI-install.gif` 238861 bytes), `dotnet/` (`README.md` 53, `Directory.Build.props` 33, `Directory.Packages.props` 10, `ShotAI.slnx` 10, `global.json` 9, `src/ShotAI.App/ShotAI.App.csproj` 20, `src/ShotAI.App/app.manifest` 19, `src/ShotAI.App/MainWindow.xaml.cs` 27, `src/ShotAI.Core/ShotAI.Core.csproj` 15, `src/ShotAI.Platform/ShotAI.Platform.csproj` 20, `src/ShotAI.Platform/CaptureExclusion.cs` 23, `src/ShotAI.Platform/NativeMethods.txt` 1, `tests/ShotAI.Core.Tests/*` 4 files). Supporting reads (runtime behavior this subsystem packages or replaces): `src/main/main.ts` (595), `src/main/arp-icon.ts` (102), `src/main/gpu-policy.ts` (58), `src/main/update-check.ts` (163), `src/main/update-state.ts` (25), `src/main/settings.ts` (434, `:1-260`), `src/main/logger.ts` (49), `src/main/secrets.ts` (112), `src/main/atomic-write.ts` (51), `src/main/paths.ts` (44), `Intune/Windows/shotAI.admx` (68), `.gitattributes` (18), tests `src/main/arp-icon.test.ts` (119), `src/main/gpu-policy.test.ts` (65), `src/main/update-check.test.ts` (189, `:1-80`), `src/shared/export-theme.test.ts` (261, `:100-175`, the packaging assertions). Context: `docs/NATIVE-WINDOWS-FEASIBILITY.md` (274) and specs 01 to 10 in this folder (their sections that name spec 12). Commits read (`git show`): `c070095` (#104, #93 OFL shipped, packaged font path measured), `66d7376` (#58, AVIF and the asar unpack rule), `ac5e101` (#51, `--silent` and deployment notes), `e32bb8f` (#110, v1.3.0 release commit), `9da70df` (#34, the history root; everything earlier is squashed into it), `dcb4196` (native scaffold and `dotnet.yml`), plus the `git log` of every listed source. macOS (`/home/user/armadillon44/shotai_macos`, read-only): `docs/DISTRIBUTION.md` (201), `Intune/README.md` (88), `Scripts/dist.sh` (154), `.github/workflows/ci.yml` (79). Microsoft Learn pages verified: Intune Win32 app management, detection rules, dependencies and supersedence; Intune Windows LOB apps; MSIX "behind the scenes" file system virtualization, `desktop6:FileSystemWriteVirtualization`, `virtualization:FileSystemWriteVirtualization`, flexible virtualization and the `unvirtualizedResources` restricted capability; WebView2 distribution; Install .NET on Windows (silent switches, Microsoft Update, Arm64 paths); .NET publishing overview, `AppHostDotNetSearch`, runtime roll forward; Azure Artifact Signing (MSIX signing guide, code signing options, trust models, FAQ); Windows Installer `VersionNT` on Windows 10, 64-bit packages, Version data type. Verifier pass (second reading of every listed source end to end, plus Microsoft Learn): Artifact Signing signing integrations (SignTool dlib, timestamp authority, the `Artifact Signing Certificate Profile Signer` role, the `azure/artifact-signing-action` action), Artifact Signing quickstart (Public Trust eligibility), MSIX signing guide, MSBuild reference `AppHostDotNetSearch` (publish only), trimming options (`StartupHookSupport`), .NET 9 "CET supported by default" and the `/CETCOMPAT` linker page (x64 only), Install .NET on Windows (`BlockMU`, `RemovePreviousVersion`), Windows Installer "Conditional Statement Syntax", "Using 64-Bit Windows Installer Packages" and "Template Summary", `virtualization:ExcludedDirectories` (build 20348) and flexible virtualization, Intune "Add a Win32 app" (Program step: no environment variable expansion in the uninstall command; 32-bit PowerShell) and "Add apps" (uninstall assignment rules). Status: extracted and verified. Consolidated with ARCHITECTURE.md R-ARCH-1 to R-ARCH-26 on 2026-09-23.

**Notation used in this document.**

- Electron citations are `path:line` (repo-relative). macOS citations are `macOS:path:line` (relative to the macOS repo root). Other specs are cited by number and ID (for example `03 EDGE-SHELL-1`).
- Every behavior carries one class: **REQUIRED** (parity with Electron), **IMPROVEMENT** (a deliberate native change, with the reason), or **ELECTRON-ONLY** (disappears natively; the replacement for its intent is named).
- Spec numbers: 01 model and store, 02 capture engine, 03 windows, shell and app menu, 04 editor and redaction, 05 report and project detail, 06 home list and settings UI, 07 SOP generation, 08 Entra sign-in, secrets and policy, 09 exports and packages, 10 brand contract, settings file, logging, update check and self-tests, 11 service boundary, threading and DI, 12 this spec.
- "Verify" marks a fact taken from documentation or reasoning that has not yet been measured on a real machine; each one has an acceptance criterion or an open question.
- Strings containing U+2014 are written with the escape `\u2014` so this document itself contains no em dash.

## 1. Scope

### 1.1 Owned by this spec

| Area | What this spec decides and specifies |
|---|---|
| Installer format | The per-machine MSI (the decision against MSIX and against the Intune LOB MSI app type), its authoring, product identity, upgrade codes, version mapping, launch conditions, shortcut, "Installed apps" entry and icon, upgrade, downgrade and uninstall behavior |
| Build and publish | `dotnet publish` settings for `win-x64` and `win-arm64`, framework-dependent deployment, runtime roll forward, the published payload layout and its verification |
| Prerequisites | The .NET 10 Desktop Runtime (hard), the WebView2 Evergreen Runtime (soft, PDF only), the Windows OCR language Feature on Demand (soft, auto-redact only), how each is detected and deployed |
| Signing | Azure Artifact Signing for every first-party PE and the MSI, the signing order, timestamping, verification in CI |
| Native dependency provenance | How `shotai_avif.dll` (libavif plus libaom, spec 09 7.6) is built from pinned sources, attested and shipped; the NuGet supply chain |
| Runtime hardening that is a packaging concern | DLL search order, apphost runtime search, startup hooks, the execution level in the manifest, install directory permissions |
| User data placement and coexistence | Where every per-user file lives, which ones are shared with the Electron build during the pilot, the collision rules, the running-Electron guard |
| Versioning and releases | The version source, tag grammar, prerelease flags, "latest" handling on GitHub, release assets, the release checklist |
| CI | `ci.yml` (kept for Electron), `dotnet.yml` (extended), a new `release.yml`, the native dependency build job |
| Pilot, cutover and rollback | The state machine from Electron-only to native-only and back, Intune configuration, removal of the per-user Squirrel install, the Electron code removal PR |
| Hardening plan carry-over | Which `docs/HARDENING-PLAN.md` items survive, disappear, or are replaced, and the new native items |

### 1.2 Not owned here

| Area | Owner |
|---|---|
| Startup order, single-instance mutex, session end handling, window lifetime, the ARP icon fix's runtime half (deleted) | 03 |
| `settings.json` schema, coercion, unknown-key preservation, logging format and rotation, update check logic, self-test switches, `IAppPaths` | 10 |
| Baked federation embedding (`ShotAIFederationFile`), HKLM policy reader, ADMX content, MSAL cache location, API key file | 08 |
| AVIF shim ABI and encoder use, WebView2 PDF host, fonts used by exports | 09 |
| `project.json` codec and conformance suite | 01 |
| OCR language selection and notices | 04 |
| Hook thread, capture APIs, UI Automation | 02 |

### 1.3 Touches

01 (Linux and Windows test jobs, junction permission on the runner), 02 (ARM64 build, removal of the Rust DLL and native npm modules), 03 (installer never launches the app, ARP icon, Start menu shortcut, `app.manifest`, running-Electron guard placement in startup), 04 (OCR Feature on Demand deployment note, Windows test runner with a recognizer), 05 and 06 (Windows test projects), 07 (`Anthropic` NuGet notice), 08 (release secret for baked federation, `msalruntime` per RID, MSAL cache under `IAppPaths.LocalDataDirectory` per R-ARCH-13), 09 (`shotai_avif.dll`, fonts, WebView2 prerequisite, WebView2 user data folder under `IAppPaths.LocalDataDirectory` per R-ARCH-13, Q-EXP-20 answered in 7.10), 10 (brand check CI step, self-test smoke step, fonts and `OFL.txt`, third-party notices, prerelease flags, `IAppPaths.LocalDataDirectory` = `%LOCALAPPDATA%\LFI\shotAI\` per R-ARCH-13, Q-INFRA-8 answered in 7.1).

## 2. Reference behavior (Electron)

### 2.1 Build toolchain and bundling

| # | Behavior | Citation | Class |
|---|---|---|---|
| 2.1.1 | npm package `"name": "shotai"`, `"productName": "shotAI"`, `"version": "1.3.0"`, `"main": ".vite/build/main.js"`, `"private": true`, `"license": "MIT"`, `"author": { "name": "LFI", ... }`. `productName` is what Electron's `app.getName()` returns, which makes `userData` equal `%APPDATA%\shotAI`. | `package.json:1-7`, `:29-33` | REQUIRED intent: product name `shotAI`, publisher `LFI`, MIT. Native: `Directory.Build.props` `<Product>shotAI</Product>`, `<Company>LFI</Company>` (already present), MSI `Manufacturer="LFI"`. |
| 2.1.2 | Scripts: `start` = `electron-forge start`; `package` = `electron-forge package --arch=x64`; `make` = `electron-forge make --arch=x64`; `publish` = `electron-forge publish --arch=x64`; `lint` = `eslint --ext .ts,.tsx .`; `test` = `vitest run`; `test:watch` = `vitest`; `gen:brand` = `node scripts/gen-brand.mjs`, `gen:brand:check` = `node scripts/gen-brand.mjs --check`; `build:element-locator` = `node scripts/build-element-locator.mjs`; `postinstall` = `node scripts/postinstall.mjs`. `@electron-forge/plugin-auto-unpack-natives` and `@electron/rebuild` are dev dependencies that `forge.config.ts` never uses. | `package.json:8-20`, `:34-58` | ELECTRON-ONLY. Replaced by `dotnet build`, `dotnet test`, `dotnet publish -r <rid>`, the WiX build, and the `ShotAI.Release` and `ShotAI.GenBrand` tools (7.2, 7.15, spec 10). |
| 2.1.3 | Electron runtime pinned exactly: `"electron": "42.5.0"`. Every Chromium security fix requires an Electron upgrade, rebuild and redeploy. | `package.json:51`; `docs/NATIVE-WINDOWS-FEASIBILITY.md:26-28` | ELECTRON-ONLY. Replaced by the framework-dependent .NET runtime serviced by Microsoft Update and the Evergreen WebView2 runtime (7.5). |
| 2.1.4 | Vite main build externalizes native and file-loading dependencies: `uiohook-napi`, `node-screenshots`, `get-windows`, `koffi`, `tesseract.js`, `/^@jsquash\/avif/`, `/^electron-log/`, `/^@anthropic-ai\/sdk/`, `/^zod(\/|$)/`, `docx`, `pptxgenjs`, `jszip`, `/^@azure\/msal-node(\/|$)/`, `/^@azure\/msal-common(\/|$)/`. `@azure/msal-node-extensions` is deliberately not used because it "hard-depends on archived keytar plus ~44 MB of WAM binaries". `zod` is external because the externalized SDK helper loads its own zod and a second bundled copy "would make zodOutputFormat fail to introspect our schema (dual-instance hazard)". | `vite.main.config.ts:38-79` | ELECTRON-ONLY. Natively every dependency is a NuGet package or the one native DLL; MSAL.NET plus `Microsoft.Identity.Client.Extensions.Msal` and the WAM broker are the chosen stack (08). |
| 2.1.5 | Baked federation: `src/main/entra/federation.local.json` (gitignored) is read at build time; keys starting with `_` and non-string or blank values are dropped; the rest are trimmed and inlined as `__FEDERATION_BAKED__`. An absent file yields `{}` ("unconfigured", bring-your-own-key), so a fresh clone of the public repo builds. The `catch` swallows EVERY failure, not only absence: malformed JSON or a leading UTF-8 BOM also bakes `{}` silently, and nothing checks that the five required keys are present (08 EDGE-AUTH-37). The file is gitignored at `.gitignore` ("Deliberately never committed: this repo is PUBLIC"). | `vite.main.config.ts:4-27`, `:32-37`; `.gitignore` (`src/main/entra/federation.local.json` entry) | REQUIRED intent, owned by 08 (`ShotAIFederationFile` embedded resource). This spec owns where the file comes from in a release build (7.11.3) and that it never reaches a public artifact (INV-PKG-14). |
| 2.1.6 | Preload build config is empty; renderer build has three HTML inputs: `main_window` (`index.html`), `toolbar` (`toolbar.html`), `overlay` (`overlay.html`). The comment in `forge.config.ts:179-181` still says "two HTML entry points" (stale: the overlay was added later). | `vite.preload.config.ts:1-4`, `vite.renderer.config.ts:5-20` | ELECTRON-ONLY (no renderer). |
| 2.1.7 | `vitest` runs `src/**/*.test.ts` under `environment: 'node'`; modules under test "should stay import-clean (no electron / native deps) so they run under plain node". | `vitest.config.ts:1-13` | REQUIRED intent: `ShotAI.Core` references no Windows API and its tests run on Linux (`dotnet/README.md` rules). |
| 2.1.8 | `src/vite-env.d.ts` is one line: `/// <reference types="vite/client" />`. | `src/vite-env.d.ts:1` | ELECTRON-ONLY. |

### 2.2 Native binary provisioning (x64 on an ARM64 host)

| # | Behavior | Citation | Class |
|---|---|---|---|
| 2.2.1 | `.npmrc` sets `target_arch=x64` so `get-windows`' node-pre-gyp fetches its win32-x64 prebuild (it ships no arm64 prebuild). npm 11 warns the key is "Unknown project config". | `.npmrc:1-11` | ELECTRON-ONLY. Native builds each architecture natively (INV-PKG-6). |
| 2.2.2 | Root `postinstall` (root scripts are exempt from npm 11 `allow-scripts` gating) forces the x64 Electron binary via `ELECTRON_INSTALL_ARCH: 'x64'`. | `scripts/postinstall.mjs:1-6`, `:84-92`; `docs/PLAN.md:273-280` | ELECTRON-ONLY. |
| 2.2.3 | `get-windows`: if `lib/binding/napi-9-win32-unknown-x64/node-get-windows.node` is missing, run `npx node-pre-gyp install --target_arch=x64`. Residual S1: this download is not hash-verified. | `scripts/postinstall.mjs:94-115` | ELECTRON-ONLY. Its lesson (an unverified native binary in the main process) becomes INV-PKG-19. |
| 2.2.4 | `fetchNpmTarball(pkg, version, tgzName, destDir)`: URL `https://registry.npmjs.org/${pkg}/-/${tgzName}`; stages in `os.tmpdir()/shotai-fetch-tmp` (a mapped drive fails tar with "os error 87"); downloads the full buffer (not `npm pack`, which "has repeatedly truncated on this host"); rejects `buf.length < 1024`; verifies `sha512-` + base64(SHA-512(buffer)) against `package-lock.json` `packages["node_modules/<pkg>"].integrity`; throws on a lockfile version mismatch, a non-`sha512-` algorithm, or a digest mismatch; extracts with `tar -xzf ... --strip-components=1`. Used for `node-screenshots-win32-x64-msvc` and `@koromix/koffi-win32-x64`. | `scripts/postinstall.mjs:24-82`, `:117-155` | ELECTRON-ONLY. Intent (every fetched binary is verified against a pinned digest before it can reach the main process) is REQUIRED natively: NuGet lock files and package signature validation (7.8), pinned source hashes for libavif and libaom (7.7). |
| 2.2.5 | `build-element-locator.mjs`: `cargo build --release` in `native/element-locator` with `CARGO_TARGET_DIR` forced to `os.tmpdir()/shotai-element-locator-target` (cargo fails on mapped drives with "os error 87"), prefers `~/.cargo/bin/cargo(.exe)` because rustup installs with `--no-modify-path`, falls back to `cargo` on `PATH`; requires the `x86_64-pc-windows-gnu` host toolchain ("no MSVC"); copies `element_locator.dll` back into the tree, where it is committed as a prebuilt (`git ls-files` confirms `native/element-locator/element_locator.dll` and `vendor/tessdata/eng.traineddata.gz` are tracked). Log lines `[build-element-locator] cargo build --release` and `[build-element-locator] copied -> ${dest}`. | `scripts/build-element-locator.mjs:1-35` | ELECTRON-ONLY (UI Automation moves to C#, 02). The committed-prebuilt pattern is NOT carried over (INV-PKG-19). |

Log strings (verbatim, prefix `[postinstall] `): `ensuring x64 Electron binary...`, `fetching get-windows win32-x64 prebuild...`, `fetching node-screenshots-win32-x64-msvc@${version}...`, `node-screenshots x64 ready.`, `fetching @koromix/koffi-win32-x64@${version}...`, `koffi x64 ready.`, `native x64 binaries ready.`, `${pkg}@${version} integrity verified (sha512).`, `WARNING: no lockfile integrity for ${pkg}@${version} \u2014 proceeding unverified.`. Errors: `download failed (${res.status}) for ${url}`, `download too small (${buf.length} B) for ${url}`, `unexpected integrity algorithm for ${pkg}: ${integrity}`, `integrity mismatch for ${pkg}@${version} \u2014 refusing tampered binary.` followed by the two digests, `lockfile has ${pkg}@${entry.version}, but installed ${pkg} is @${version} \u2014 refusing to fetch a mismatched binary` (`scripts/postinstall.mjs:22`, `:36-38`, `:57-75`, `:87`, `:109`, `:126`, `:133`, `:147`, `:154`, `:157`). ELECTRON-ONLY.

### 2.3 Packaging (Electron Forge)

| # | Behavior | Citation | Class |
|---|---|---|---|
| 2.3.1 | `packagerConfig.icon: './assets/shotAI_icon'` (extension auto-completed per platform). | `forge.config.ts:81-83` | REQUIRED intent: the exe carries `assets/shotAI_icon.ico` (already `ApplicationIcon` in `ShotAI.App.csproj:14`). |
| 2.3.2 | `asar: { unpack: '**/*.node' }`: native addons are unpacked because they cannot load from inside the archive. `@jsquash/avif` is deliberately not unpacked: unpacking broke its load (`ERR_MODULE_NOT_FOUND` on the hoisted `wasm-feature-detect`) and would move about 8 MB of WASM outside the integrity-validated archive. | `forge.config.ts:84-98`; `66d7376` | ELECTRON-ONLY. Lesson carried: a file that exists is not a file that loads (EDGE-PKG-3). |
| 2.3.3 | `extraResource` (each lands at `resources/<basename>`): `./native/element-locator/element_locator.dll`, `./assets/shotAI_icon.png` (window, taskbar and About icon via `appIconPath()`), `./assets/shotAI_icon.ico` (post-install ARP icon fix), `./vendor/tessdata` (offline OCR model), `./src/renderer/fonts/Archivo.ttf` (PDF export only, #77), `./src/renderer/fonts/OFL.txt` (SIL OFL 1.1 must travel with the font, #93). | `forge.config.ts:99-122`; `src/main/paths.ts:29-44` | Mixed: `element_locator.dll` ELECTRON-ONLY (02); `shotAI_icon.png` ELECTRON-ONLY (WPF uses the `.ico` resource); `shotAI_icon.ico` REQUIRED as the exe icon and ARP icon (7.4); `tessdata` ELECTRON-ONLY (Windows OCR, 04); `Archivo.ttf` and `OFL.txt` REQUIRED, laid out as `<install>\Fonts\` (10 7.x, INV-INFRA-31). |
| 2.3.4 | `rebuildConfig: { onlyModules: [] }`: no native rebuild ("no Python/MSVC on this box"). | `forge.config.ts:124-128` | ELECTRON-ONLY. |
| 2.3.5 | `hooks.packageAfterCopy` runs `copyProductionNodeModules(buildPath)`: breadth-first walk of `dependencies` plus `optionalDependencies` from the root package, copying each package directory recursively into `<buildPath>/node_modules`, logging `[forge] packageAfterCopy: copied ${copied} production node_modules into the package`. Chosen over lockfile `dev`/`devOptional` flags because npm marked a needed transitive dependency (`readable-stream`) as `devOptional`. | `forge.config.ts:12-70`, `:129-135`; `docs/HARDENING-PLAN.md:129-136` (S11) | ELECTRON-ONLY. Its lesson (the packaged app was dead on arrival and no dev run could show it) becomes INV-PKG-23: every release build is installed and launched in CI. |
| 2.3.6 | Fuses: `RunAsNode: false`, `EnableCookieEncryption: true`, `EnableNodeOptionsEnvironmentVariable: false`, `EnableNodeCliInspectArguments: false`, `EnableEmbeddedAsarIntegrityValidation: true`, `OnlyLoadAppFromAsar: true`. | `forge.config.ts:187-197` | ELECTRON-ONLY. Intent (no code injection into the shipped app through environment variables, debugger arguments or a swapped app payload) is REQUIRED: `StartupHookSupport=false`, `AppHostDotNetSearch=Global`, signed binaries in an admin-only directory (INV-PKG-15 to INV-PKG-18). |
| 2.3.7 | Forge Vite plugin: main entry `src/main/main.ts`, preload `src/preload/preload.ts`, one renderer named `main_window`. | `forge.config.ts:160-186` | ELECTRON-ONLY. |
| 2.3.8 | `APP_VERSION` is read from `package.json` at make time so the installer is named without a post-build rename. | `forge.config.ts:72-77` | REQUIRED intent: one version source (`Directory.Build.props` `<Version>`), artifact names derived from it (INV-PKG-10, INV-PKG-34). |

### 2.4 Installer (Squirrel)

| # | Behavior | Citation | Class |
|---|---|---|---|
| 2.4.1 | Maker: `MakerSquirrel` with `setupIcon: './assets/shotAI_icon.ico'`, `setupExe: \`shotAI-${APP_VERSION}-Setup.exe\`` (Squirrel's default `shotAI-<version> Setup.exe` has a space, "awkward in URLs/downloads"), `loadingGif: './assets/shotAI-install.gif'`, `authors: 'LFI'`. `iconUrl` is intentionally NOT set because it would "bake a personal GitHub URL into the installed package". | `forge.config.ts:136-155` | ELECTRON-ONLY maker. REQUIRED intents: a hyphenated, versioned, space-free installer name (INV-PKG-34); publisher `LFI`; no personal data in installer metadata (INV-PKG-13). |
| 2.4.2 | Other makers: `MakerZIP({}, ['darwin'])`, `MakerRpm({})`, `MakerDeb({})`. | `forge.config.ts:156-158` | ELECTRON-ONLY (macOS is a separate app; Linux is not a target). |
| 2.4.3 | Double-click install is per-user into `%LocalAppData%\shotAI` (the folder is the nuspec id `shotai`; NTFS is case-insensitive), no admin, shows the looping animation, then launches the app. There is no wizard. | `README.md:263-268`; `src/main/arp-icon.ts:25-27`, `:42-47` | ELECTRON-ONLY. Native: per-machine, admin, silent via Intune (7.4, 7.5). |
| 2.4.4 | Silent install: `shotAI-<version>-Setup.exe --silent` suppresses the animation and does not auto-launch. "use `--silent` (not `/S`, `/quiet`, or `/qn` \u2014 those belong to NSIS/MSI installers; shotAI isn't one)". | `README.md:270-276`; `ac5e101` | ELECTRON-ONLY. Native uses the standard `msiexec /i <msi> /qn /norestart`. |
| 2.4.5 | Deployment notes: "Deploy in user context" because a device-context push has no profile to install into; uninstall via Settings, Apps or the per-user Squirrel uninstaller; "Not code-signed yet" so SmartScreen or Defender may need an allow rule. | `README.md:278-286` | IMPROVEMENT natively: device context, signed. |
| 2.4.6 | Install animation `assets/shotAI-install.gif` is generated by `scripts/make-loading-gif.cjs` (2.12.1). Squirrel "can't be driven by real %", so the bar is indeterminate. | `forge.config.ts:142-145` | ELECTRON-ONLY (an Intune MSI install shows no UI; a manual MSI install shows the standard Windows Installer progress). |
| 2.4.7 | Squirrel layout: `<InstallRoot>\app-<version>\shotAI.exe`, `<InstallRoot>\app.ico`, `<InstallRoot>\Update.exe`; ARP key `HKCU\Software\Microsoft\Windows\CurrentVersion\Uninstall\shotai`. | `src/main/arp-icon.ts:25-27`, `:42-47`; `src/main/arp-icon.test.ts:23-29` | ELECTRON-ONLY. The layout matters natively only for coexistence and removal (7.10, 7.13). |
| 2.4.8 | User and admin documentation that describes the Squirrel install lives outside the tree too: README points to the wiki page `Installation` ("the end-user walkthrough and the same deployment notes"), and README's licence section says the OFL "ships with the font in the installed app (`resources/OFL.txt`)". | `README.md:288-289`, `:395-401` | REQUIRED at cutover: the README deployment section, the licence section (native path `Fonts\OFL.txt`) and the wiki `Installation` page are rewritten for the MSI in the 2.0.0 release PR (7.12.2 item 7). |

### 2.5 Install lifecycle state machine (Electron)

`electron-squirrel-startup` (`started`) handles `--squirrel-*` arguments before the single-instance lock; spec 03 2.10.6 and EDGE-SHELL-1 hold the full detail. The packaging-relevant transitions:

| State | Event | Guard | Action | Next state |
|---|---|---|---|---|
| Not installed | user runs `Setup.exe` (or `--silent`) | none | Squirrel extracts to `%LocalAppData%\shotai\app-<v>`, writes the HKCU ARP key, runs `shotAI.exe --squirrel-install <v>` | Installing hook |
| Installing hook | `--squirrel-install` | `started` true | `fixArpIconOnSquirrelEvent`: copy `resources\shotAI_icon.ico` to `<root>\app.ico`, then `reg add HKCU\...\Uninstall\shotai /v DisplayIcon /t REG_SZ /d <root>\app.ico /f` if `app.ico` exists; `Update.exe --createShortcut=shotAI.exe`; `app.quit()` | Installed (then launched unless `--silent`) |
| Installed | a newer `Setup.exe` | none | extract `app-<v2>`, run `--squirrel-updated <v2>` (same ARP fix, shortcuts refreshed) | Installed |
| Installed | uninstall from Settings, Apps | none | Squirrel runs `--squirrel-uninstall` (`Update.exe --removeShortcut=shotAI.exe`), deletes the install root, removes the HKCU ARP key | Not installed (user data in `%APPDATA%\shotAI` remains; verify, AC-PKG-24) |
| any | normal launch | not a Squirrel argument | `requestSingleInstanceLock()`; second instance focuses the first | Running |

Citations: `src/main/main.ts:142-181`, `src/main/arp-icon.ts:55-102`. The ARP fix exists because Squirrel sets `DisplayIcon` only when it downloads the nuspec `iconUrl`, which shotAI does not set; on a real 1.0.0 install `DisplayIcon` was blank and `app.ico` existed (`src/main/arp-icon.ts:1-15`). An earlier fix (PR #19, overwrite `app.ico` only) did not work (`docs/HARDENING-PLAN.md:224-227`). Log lines: `arp-icon: wrote ${appIco} from bundled icon (${cmd})`, `arp-icon: bundled icon missing at ${bundledIcoPath} \u2014 leaving app.ico as-is`, `arp-icon: failed to write app.ico`, `arp-icon: set DisplayIcon -> ${appIco} (${cmd})`, `arp-icon: failed to set DisplayIcon` (`src/main/arp-icon.ts:80-98`). All ELECTRON-ONLY; the intent ("Installed apps" always shows the shotAI icon, offline too, with no URL) is REQUIRED and met by MSI `ARPPRODUCTICON` (7.4).

### 2.6 User data locations (Electron, per user)

| Data | Path | Citation | Native treatment |
|---|---|---|---|
| Settings | `%APPDATA%\shotAI\settings.json` | `src/main/settings.ts:125-127` | REQUIRED, shared in place (INV-PKG-2, INV-PKG-25) |
| Logs | `%APPDATA%\shotAI\logs\shotai.log`, rotated to `shotai.old.log` at 5 MB | `src/main/logger.ts:21-22`, `:25-31` | REQUIRED, shared in place (10) |
| API key | `%APPDATA%\shotAI\secrets.json` (`safeStorage` ciphertext) | `src/main/secrets.ts:1-24` | ELECTRON-ONLY file, never touched natively (08 EDGE-AUTH-33) |
| Entra cache | `%APPDATA%\shotAI\entra-cache.bin` | `src/main/claude-auth.ts:42-44` | ELECTRON-ONLY file, never touched natively (08) |
| OCR cache | `%APPDATA%\shotAI\tessdata` | `src/main/ocr.ts:45` | ELECTRON-ONLY |
| Chromium profile | `%APPDATA%\shotAI\` (`Local State`, `Preferences`, cache and storage folders) | Electron default `userData` | ELECTRON-ONLY, never touched natively during the pilot (Q-PKG-17) |
| Projects | `settings.projectsDir`, default `%USERPROFILE%\shotAI Projects` | `src/main/settings.ts:129-131` | REQUIRED, shared in place |
| Install | `%LocalAppData%\shotai\` (Squirrel root) | 2.4.7 | ELECTRON-ONLY; natively a no-write zone (INV-PKG-24) |

### 2.7 Architecture and GPU policy

| # | Behavior | Citation | Class |
|---|---|---|---|
| 2.7.1 | shotAI builds and ships x64 only and runs emulated on Windows on ARM ("one architecture, one set of native binaries"). `get-windows` has no arm64 prebuild, which forces this. | `README.md:343-349`; `docs/PLAN.md:237-251` | IMPROVEMENT natively: x64 and native ARM64 builds (INV-PKG-6). |
| 2.7.2 | GPU on by default; disabled only when `PROCESSOR_ARCHITEW6432` shows an x64 or ia32 process on a non-x86 host; `SHOTAI_ENABLE_GPU` is `1` force on, `0` force off, anything else auto. | `src/main/gpu-policy.ts:1-58`, `src/main/main.ts:108-129` | ELECTRON-ONLY (03 owns the deletion). The ARM64 build removes the emulation case. |
| 2.7.3 | `SHOTAI_NO_SANDBOX=1` disables the Chromium OS sandbox on hosts that cannot initialize it; shipped installs keep it on. | `src/main/main.ts:131-140` | ELECTRON-ONLY. |

### 2.8 Signing

No Electron artifact is signed. README: "Not code-signed yet" (`README.md:284-286`). HARDENING-PLAN post-1.0.0: "Code-signing (removes the SmartScreen "unknown publisher" warning) \u2014 needs a purchased cert; prerequisite for good auto-update" (`docs/HARDENING-PLAN.md:228-229`). `docs/PLAN.md:191` planned Authenticode signing in Phase 4; it never happened. IMPROVEMENT natively (7.6).

### 2.9 Versioning, releases and the update check contract

| # | Behavior | Citation | Class |
|---|---|---|---|
| 2.9.1 | The version lives in `package.json` and is bumped in a `release: vX.Y.Z` commit that also updates the README status block (for example `e32bb8f`: `README.md`, `package-lock.json`, `package.json`). | `git log -- package.json`; `e32bb8f` | REQUIRED intent: one version source, bumped in a release PR. Native source: `dotnet/Directory.Build.props` `<Version>` (currently `2.0.0-alpha.0`, `dotnet/Directory.Build.props:22-24`). |
| 2.9.2 | Tags are `vMAJOR.MINOR.PATCH`, with earlier `-rcN` tags (for example `v1.0.0-rc2`, `v1.0.0-rc3`, `v1.0.0-rc5`). Releases are built by hand and attached to GitHub Releases; there is no release workflow. | `docs/HARDENING-PLAN.md:185-193`; `src/main/update-check.ts:4-7` | IMPROVEMENT natively: `release.yml` (7.11.3). |
| 2.9.3 | The update check reads `https://api.github.com/repos/Armadillon44/shotAI/releases/latest`, once a day at startup, 10 s timeout, headers `Accept: application/vnd.github+json` and `User-Agent: shotAI/${currentVersion}`. | `src/main/update-check.ts:13-15`, `:17-18`, `:137-148` | REQUIRED, owned by 10. |
| 2.9.4 | `pickRelease` refuses `draft === true` or `prerelease === true`, a non-string tag or URL, a tag whose `normalizeTag` does not match `/^\d+(\.\d+)*/` (anchored at the start only), and a URL not starting `https://github.com/`. | `src/main/update-check.ts:106-117` | REQUIRED, owned by 10. This spec relies on it: an Electron 1.3.x client is never offered a GitHub release flagged prerelease. |
| 2.9.5 | `compareVersions` strips a leading `v`, takes `split('-')[0]`, splits on `.`, `Number.parseInt(s, 10) || 0` per segment, missing segments are 0; any prerelease suffix is ignored for ordering. `isNewer(current, latest)` is `compareVersions(latest, current) > 0`. | `src/main/update-check.ts:20-54`; `src/main/update-check.test.ts:21-57` | REQUIRED, owned by 10. Consequence for this spec: Electron 1.3.x is offered `2.0.0` the moment it is the latest non-prerelease (`2 > 1`), and a tag such as `2026-08-04` would be read as version `2026` (EDGE-PKG-20). |
| 2.9.6 | GitHub's `/releases/latest` is the whole repository's latest non-draft, non-prerelease release. Electron and native share the repository, so they share "latest". | `src/main/update-check.ts:13-15` | REQUIRED fact the release process must respect (INV-PKG-9). |

### 2.10 Managed configuration

The ADMX (with its `en-US/shotAI.adml`, uploaded after the ADMX) writes eight `REG_SZ` values under `HKLM\SOFTWARE\Policies\shotAI\Federation` (five required: `TenantId`, `AudienceAppId`, `FederationRuleId`, `OrganizationId`, `ServiceAccountId`; three optional: `ClientAppId`, `WorkspaceId`, `SupportUrl`; `ClientAppId` and `WorkspaceId` still fail closed when present and malformed, `SupportUrl` is ignored), read HKLM only, 64-bit view (`Intune/Windows/README.md:6-16`, `:37-47`, `:54-57`; `Intune/Windows/shotAI.admx:47-66`). "Policy values override the baked ones per key", and "A **blank** policy value counts as "not set" and does not clear a baked value" (`:23-24`, `:49-50`). Import routes: Settings catalog custom ADMX import, the OMA-URI fallback `./Device/Vendor/MSFT/Policy/ConfigOperations/ADMXInstall/shotAI/Policy/shotAIFederation`, or a domain Central Store (`Intune/Windows/README.md:52-68`). Verification: `reg query "HKLM\SOFTWARE\Policies\shotAI\Federation" /reg:64` (`:94-96`). Values "are normally baked into the installer at build time" and policy exists for "a rotated federation rule" (`:18-24`). They "identify your tenant and your Anthropic organization, so treat them as internal rather than published; that is why they are not committed to this repository" (`:85-90`). REQUIRED unchanged (08 owns the reader and the ADMX). This spec: the installer never writes the key (INV-PKG-3), and baked values never ship in a public artifact (INV-PKG-14).

### 2.11 CI

**`ci.yml` (Electron).** Name `CI`; on push to `main` and every pull request; concurrency `ci-${{ github.ref }}` with cancel in progress; one job `check` named `test · typecheck · lint` on `ubuntu-latest`: `actions/checkout@v4`, `actions/setup-node@v4` with `node-version: '24'` (pinned; "no .nvmrc or engines field") and `cache: npm`; `npm ci --ignore-scripts` with `ELECTRON_SKIP_BINARY_DOWNLOAD: '1'` (skips `postinstall.mjs`, "a security property worth having in CI regardless of platform"); `npm run gen:brand:check`; `npx tsc --noEmit` ("a type error explains a test failure"); `npx eslint --ext .ts,.tsx .`; `npx vitest run`. Runs on Linux because the four win32-only native modules are imported by five capture files and zero test files ("485 passed, 4 skipped" on macOS in about 11 s). "Packaging and anything that touches real capture still belong on Windows, and are deliberately NOT here." `ci.yml` has no `permissions:` block, so its `GITHUB_TOKEN` gets the repository's default permissions, and its actions are pinned by tag (`@v4`), not SHA (`.github/workflows/ci.yml:1-68`). REQUIRED to stay as is until cutover (7.11.1).

**`dotnet.yml` (native, already added in `dcb4196`).** Name `native (.NET)`; on push to `main` and pull requests, path-filtered to `dotnet/**`, `contract/**`, `.github/workflows/dotnet.yml`; concurrency `dotnet-${{ github.ref }}`; `permissions: contents: read`; env `DOTNET_NOLOGO: '1'`, `DOTNET_CLI_TELEMETRY_OPTOUT: '1'`; `defaults.run.working-directory: dotnet` because `global.json` (which opts `dotnet test` into Microsoft.Testing.Platform) is only found from there. Job `linux` (`build all · test core (Linux)`, `ubuntu-latest`): checkout, `actions/setup-dotnet@v4` `10.0.x`, `dotnet build ShotAI.slnx -c Release`, `dotnet test --project tests/ShotAI.Core.Tests/ShotAI.Core.Tests.csproj -c Release --no-build`. Job `windows` (`build + test (Windows)`, `windows-latest`): same setup, `dotnet build ShotAI.slnx -c Release`, `dotnet test --solution ShotAI.slnx -c Release --no-build`. Warning in the file: "Path-filtered jobs never report on PRs they skip, so do not make them required status checks without a paths-filter step, or those PRs will wait forever." (`.github/workflows/dotnet.yml:1-71`). The file's first line, `dotnet/README.md:8` and the `<Version>` comment in `dotnet/Directory.Build.props:22-23` all point at `docs/native/PLAN.md`. When this spec was first extracted that file did not exist (`docs/native/` held only `spec/`); it has since been written (the ordered implementation plan beside `docs/native/ARCHITECTURE.md`), so the links resolve (Q-PKG-31, resolved). `Directory.Build.props:18` already sets `ContinuousIntegrationBuild` when `CI` is `true`, which GitHub Actions sets. Extended in 7.11.2.

### 2.12 Developer scripts and probes

| # | Script | Behavior | Class |
|---|---|---|---|
| 2.12.1 | `make-loading-gif.cjs` | Headless Electron renders a 440 by 300 CSS animation with `force-device-scale-factor=1` (the previously shipped GIF was 552 by 378 because `capturePage()` returns physical pixels on a 1.25x desktop), 36 frames at 55 ms (about 2 s loop), card `#1b1926`, label `Installing shotAI…`, corners masked to radius 22 per pixel (a GIF has 1-bit alpha, CSS anti-aliasing would fringe), `gifenc` quantize `rgba4444` with `oneBitAlpha`, locates the transparent palette index instead of assuming 0 (`writeFrame`'s default of 0 "is NOT where the clear entry necessarily lands"), writes `assets/shotAI-install.gif` and, from frame `Math.round(FRAMES * 0.4)` (14), `assets/_loading-preview.png` (gitignored, "not shipped"), sanity-counts frame-0 pixels differing from the card by more than 6 per channel ("A blank frame would report ~0"). The artwork is `assets/shotAI_icon_v3.svg` inlined as a base64 data URI; each frame waits two `requestAnimationFrame`s before `capturePage()`. Output line `GIF written: ${OUT} · ${size[0]}x${size[1]} · ${FRAMES} frames · ${bytes} bytes · transparentIndex=${tIdxUsed} · cleared corner px=${clearPx} · frame0 art px=${artPx}` (size is the captured size, so a scale mismatch shows here); on failure `make-loading-gif failed:` and exit code 1. (`scripts/make-loading-gif.cjs:1-203`; `.gitignore` `_loading-preview.png` entry; `5bcfb8b`) | ELECTRON-ONLY. Lesson carried: generated assets must not depend on the author's display scale (EDGE-PKG-38). |
| 2.12.2 | `report-strut-probe.cjs`, `report-width-probe.cjs` | Measure CSS layout bugs against the real `project.css` and the real DOM chain "rather than taking the claim on trust". The strut probe measures the current CSS against two candidate fixes and prints a verdict (`=> the claim reproduces.` or `=> the claim does NOT reproduce; do not change the CSS.`); the width probe adds a control case (a block parent) and re-creates the bug by removing the fix ("The CONTROL (block parent) is not decoration: without it, a run showing no difference proves nothing, because the harness might simply be blind."). Both run with `env -u ELECTRON_RUN_AS_NODE npx electron <script>`. (`scripts/report-strut-probe.cjs:1-121`, `scripts/report-width-probe.cjs:1-66`) | ELECTRON-ONLY (05 owns the report geometry). Lesson carried: packaging tests include a negative control (EDGE-PKG-39). |
| 2.12.3 | `ts-register.mjs`, `ts-resolve.mjs` | Node ESM resolve hook retrying a failed relative import with `.ts`, used by the WIF probe with `--experimental-transform-types`. It retries only when the error code is `ERR_MODULE_NOT_FOUND` and the specifier matches `/^\.{1,2}\//` ("never mask a real resolution error or rewrite a bare package name"); every other error is rethrown. (`scripts/ts-register.mjs:1-4`, `scripts/ts-resolve.mjs:1-20`) | ELECTRON-ONLY (08 has `dotnet/tools/ShotAI.WifProbe`). |
| 2.12.4 | `docs/PLAN.md` x64-first notes | "Do NOT enable the `ProcessDynamicCodePolicy` "prohibit dynamic code" mitigation (breaks V8/JIT); run from local disk (Chromium sandbox). Keep the dev box on patched Win11 24H2/25H2 so AVX/AVX2 emulation (KB5066835) is present." ARM64EC is named as a later optimization. (`docs/PLAN.md:241`, `:268`) | Partly carried: the .NET JIT also needs dynamic code, so the native app must not be put under an Arbitrary Code Guard (dynamic code) exploit-protection policy either (EDGE-PKG-51); the x64 build under emulation inherits the AVX note. ARM64EC is not needed (native ARM64 build). |

### 2.13 Hardening plan items that concern packaging

| Item | Electron state | Citation |
|---|---|---|
| S1 postinstall integrity | sha512 against lockfile for two tarballs; `get-windows` residual unverified | `docs/HARDENING-PLAN.md:62-65` |
| S2 sandbox on | `--no-sandbox` only behind `SHOTAI_NO_SANDBOX=1` | `:51-55` |
| S7 offline OCR model | vendored `eng.traineddata.gz` (2.82 MB) via `extraResource` | `:62-65`, `:99` |
| S11 packaged app dead on arrival | `node_modules` missing from the package; fixed by the closure copy | `:129-136` |
| "Packaging-affecting phases" gate | `npm run make` or `npm run package` required | `:10` |
| Pending: `npm run make` full-installer smoke test | "Never run end-to-end" | `:102`, `:118` |
| Post-1.0.0: ARP icon, code signing, auto-update | ARP fixed later; signing and auto-update not done | `:223-231` |

### 2.14 User-visible strings in this subsystem (Electron)

| Where | String | Citation |
|---|---|---|
| Install animation label | `Installing shotAI…` | `scripts/make-loading-gif.cjs:56` |
| Installer file | `shotAI-${APP_VERSION}-Setup.exe` | `forge.config.ts:141` |
| Window title | `shotAI` | `src/main/main.ts:270` |
| "Installed apps" entry | display name `shotAI` (Squirrel, from `productName`), publisher `LFI` (from `authors`) | `package.json:3`, `forge.config.ts:146-154` |

## 3. Constants

| Name | Value | Unit | Meaning | Citation |
|---|---|---|---|---|
| Product name | `shotAI` | | exe name, `userData` folder, ARP display name | `package.json:3`; `ShotAI.App.csproj:11` |
| npm package id | `shotai` | | Squirrel install folder and HKCU ARP subkey | `package.json:2`; `arp-icon.ts:25-27` |
| Publisher | `LFI` | | Squirrel `authors`, native `Company` and MSI `Manufacturer` | `forge.config.ts:154`; `Directory.Build.props` |
| Electron app version | `1.3.0` | semver | last Electron release at spec time | `package.json:4` |
| Electron runtime | `42.5.0` | | pinned exactly | `package.json:51` |
| Squirrel installer name | `shotAI-${APP_VERSION}-Setup.exe` | | | `forge.config.ts:141` |
| Squirrel install root | `%LocalAppData%\shotai` | path | per-user install | `arp-icon.ts:42-47`; `README.md:266` |
| Squirrel ARP key | `HKCU\Software\Microsoft\Windows\CurrentVersion\Uninstall\shotai` | registry | | `arp-icon.ts:27` |
| asar unpack | `**/*.node` | glob | | `forge.config.ts:98` |
| Fuses | RunAsNode false, EnableCookieEncryption true, EnableNodeOptionsEnvironmentVariable false, EnableNodeCliInspectArguments false, EnableEmbeddedAsarIntegrityValidation true, OnlyLoadAppFromAsar true | | | `forge.config.ts:189-197` |
| Loading GIF | W 440, H 300, FRAMES 36, DELAY 55, RADIUS 22, CARD `#1b1926` | px, frames, ms | | `make-loading-gif.cjs:24-33` |
| `target_arch` | `x64` | | `.npmrc` | `.npmrc:11` |
| Tarball minimum size | 1024 | bytes | postinstall sanity check | `postinstall.mjs:59` |
| Releases API | `https://api.github.com/repos/Armadillon44/shotAI/releases/latest` | URL | update check | `update-check.ts:13-15` |
| Update interval | 86400000 | ms | once a day | `update-check.ts:17-18` |
| Update timeout | 10000 | ms | | `update-check.ts:137` |
| Log max size | 5 * 1024 * 1024 | bytes | per file, two files | `logger.ts:31` |
| CI Node | `'24'` | | | `ci.yml:39` |
| .NET SDK | `10.0.100`, `rollForward: latestFeature` | | `global.json` | `dotnet/global.json` |
| CI .NET | `10.0.x` | | `setup-dotnet` | `dotnet.yml:54`, `:67` |
| Windows TFM | `net10.0-windows10.0.19041.0` | | Platform and App | `Directory.Build.props` |
| Minimum Windows | `10.0.19041.0` (Windows 10 2004) | build | first build with `WDA_EXCLUDEFROMCAPTURE` | `Directory.Build.props`; `dotnet/README.md:23-24` |
| Native version (now) | `2.0.0-alpha.0` | semver | scaffold value | `Directory.Build.props` |
| CsWin32 | `0.3.335` | | | `Directory.Packages.props` |
| xunit.v3 | `4.0.1` | | | `Directory.Packages.props` |
| Policy key | `HKLM\SOFTWARE\Policies\shotAI\Federation` | registry | | `Intune/Windows/README.md:8-10` |
| Native install dir (new) | `%ProgramFiles%\shotAI` (`ProgramFiles64Folder\shotAI`) | path | both architectures | 7.4 |
| MSI file name (new) | `shotAI-<semver>-<arch>.msi`, arch `x64` or `arm64` | | release asset | 7.3 |
| MSI stage base (new) | alpha 0, beta 300, rc 600, final 999 | | MSI build field formula | 7.3 |
| MSI prerelease number limit (new) | 0 to 299 | | `N` in `-stage.N` | 7.3 |
| MSI patch limit (new) | 0 to 64 | | `Z` in `X.Y.Z` so that `Z*1000+999 <= 65535` | 7.3 |
| Native tag grammar (new) | `^v(0\|[1-9][0-9]*)\.(0\|[1-9][0-9]*)\.(0\|[1-9][0-9]*)(-(alpha\|beta\|rc)\.(0\|[1-9][0-9]*))?$` with major at least 2 | regex | release tags | 7.3 |
| UpgradeCode (new) | one GUID generated in the implementing PR, uppercase, committed in `Package.wxs` and pinned by a test | GUID | never changes | 7.4 |
| Shortcut name (new) | `shotAI` for a final version, `shotAI Preview` for a prerelease | | Start menu | 7.4 |
| PE machine (new) | x64 `0x8664`, ARM64 `0xAA64` | uint16 | payload arch check | 7.2.4 |
| WebView2 client key | `{F3017226-FE2A-4295-8BDF-00C3A9A7E4C5}` under `HKLM\SOFTWARE\WOW6432Node\Microsoft\EdgeUpdate\Clients\` (64-bit Windows) or `HKCU\Software\Microsoft\EdgeUpdate\Clients\`, value `pv` | registry | WebView2 detection | Microsoft Learn, WebView2 distribution |
| .NET runtime registry | `HKLM\SOFTWARE\dotnet\Setup\InstalledVersions\<arch>\sharedfx\Microsoft.WindowsDesktop.App` (value names are versions) | registry | detection, verify | Microsoft Q&A, verify (Q-PKG-26) |
| .NET install roots | `%ProgramFiles%\dotnet\` (native arch), `%ProgramFiles%\dotnet\x64\` (x64 on an Arm64 PC) | path | | Microsoft Learn, Install .NET on Windows |
| .NET silent switches | `/install /quiet /norestart`; exit 0 success, 3010 restart required | | Desktop Runtime installer | Microsoft Learn |
| OCR Feature on Demand | `Language.OCR~~~en-US~0.0.1.0` | capability | Windows OCR recognizer | 04 7.9 |
| Artifact Signing timestamp | `http://timestamp.acs.microsoft.com` | URL | RFC 3161 timestamp authority ("Microsoft Public RSA Time Stamping Authority"). The Artifact Signing "signing integrations" page and FAQ give `http://`; the MSIX signing guide shows `https://timestamp.acs.microsoft.com`. Use the `http://` form the service documents; RFC 3161 responses are themselves signed, so HTTP is not a downgrade (Q-PKG-2) | Microsoft Learn, Artifact Signing signing integrations |
| Artifact Signing certificate life | three days ("Artifact Signing certificates have a three-day validity") | | why timestamping is mandatory | Microsoft Learn, signing integrations |
| Artifact Signing signer role | `Artifact Signing Certificate Profile Signer` (older docs: `Trusted Signing Certificate Profile Signer`) | Azure RBAC role | assigned on the certificate profile only | Microsoft Learn |
| Artifact Signing dlib | `Azure.CodeSigning.Dlib.dll` from NuGet `Microsoft.ArtifactSigning.Client` or the `Microsoft.Azure.ArtifactSigningClientTools` winget package; metadata keys `Endpoint`, `CodeSigningAccountName`, `CertificateProfileName`, optional `CorrelationId` | | SignTool plug-in; the `Endpoint` region must match the account (mismatch gives 403) | Microsoft Learn |
| `.NET` Microsoft Update block keys | `HKLM\SOFTWARE\Microsoft\.NET` and `HKLM\SOFTWARE\Microsoft\.NET\10.0`, value `BlockMU` `REG_DWORD` `1` | registry | must be absent on managed PCs | Microsoft Learn, Install .NET on Windows |
| `.NET` previous-version removal | `HKLM\SOFTWARE\Microsoft\.NET[\10.0]` value `RemovePreviousVersion` `REG_SZ` `always` (default), `never`, `nextSession` | registry | when a serviced runtime deletes the old one (EDGE-PKG-49) | Microsoft Learn, Install .NET on Windows |
| MSI schema for Arm64 | Page Count Summary at least `500`; Template Summary platform `Arm64` (x64 package: `x64`, schema at least `200`) | | ICE80 enforces | Microsoft Learn, 64-bit Windows Installer packages |
| Loading GIF preview frame | `Math.round(FRAMES * 0.4)` = 14 | frame index | `_loading-preview.png` | `make-loading-gif.cjs:175` |
| msiexec return codes (Intune) | 0 success, 1707 success, 3010 soft reboot, 1641 hard reboot, 1618 retry | | Intune defaults | Intune Win32 app |
| Retention of Electron artifacts (new) | 90 | days after GA | rollback window | 7.13 |

## 4. Invariants

**INV-PKG-1. The native app ships as a per-machine MSI per architecture, deployed by Intune as a Win32 app; there is no MSIX and no Intune "line-of-business" MSI app.** Why: MSIX virtualizes new files under `AppData` to a private per-package location, which would stop sharing `settings.json` and the logs with the Electron build during the pilot and break rollback (10 EDGE-INFRA-39); the per-directory exclusion needs build 20348 or later and a restricted capability documented as "not intended for other scenarios"; the Win32 app type is the only one with dependencies (the Desktop Runtime), architecture requirement rules, supersedence and return-code handling. Citation: 7.1. Test: `PackageSourceTests.ScopeIsPerMachine` (Linux, reads `Package.wxs`), AC-PKG-1.

**INV-PKG-2. The installer never creates, modifies or deletes per-user data: `%APPDATA%\shotAI\` (settings, logs, Electron files), the projects folder, and the native local data folder `%LOCALAPPDATA%\LFI\shotAI\` (`IAppPaths.LocalDataDirectory`: MSAL cache, WebView2 data; R-ARCH-13) survive install, upgrade, repair and uninstall.** Why: the Electron and native builds share the `%APPDATA%\shotAI\` files and the projects folder (`src/main/settings.ts:125-131`); uninstall must never cost a user their projects or sign them out. Citation: 2.6, 7.10. Test: `PackageSourceTests.NoUserProfileLocations` (no `AppDataFolder`, `LocalAppDataFolder`, `PersonalFolder`, `RemoveFolderEx` or `util:RemoveFolderEx` in `Package.wxs`), AC-PKG-9.

**INV-PKG-3. [SECURITY] The installer writes nothing under `HKLM\SOFTWARE\Policies`.** Why: the policy key is the administrator's (Intune or Group Policy) channel; an installer value would be indistinguishable from policy and would outrank baked values (`Intune/Windows/README.md:12-16`, `:23-24`; 08 interface). Citation: 2.10. Test: `PackageSourceTests.NoPolicyRegistryWrites`.

**INV-PKG-4. No installer action launches `shotAI.exe`, and the MSI has no custom action that runs app code.** Why: a lifecycle launch contending with a running instance was the Squirrel ARP bug (03 EDGE-SHELL-1, `d79bc3b`); Intune installs are silent and in the system account, where a launched GUI would run as SYSTEM. Citation: `src/main/main.ts:154-159`. Test: `PackageSourceTests.NoCustomActionRunsTheApp` (no `CustomAction` whose `FileRef`, `ExeCommand` or `Target` names `shotAI.exe`; no `util:QtExecCmdLine` and no `WixShellExec`).

**INV-PKG-5. The published app is framework-dependent on `Microsoft.WindowsDesktop.App` 10.0 with the default roll forward (`Minor`, latest patch); no build is self-contained.** Why: Microsoft Update services the shared runtime on Patch Tuesday; a self-contained build freezes it until the next shotAI release (`docs/NATIVE-WINDOWS-FEASIBILITY.md:70-72`). `LatestMajor` is refused because a .NET 11 runtime could change behavior unseen. Citation: 7.2. Test: `PayloadVerifierTests.RuntimeConfigNamesDesktopRuntime10` (parses `shotAI.runtimeconfig.json`: `framework.name == "Microsoft.WindowsDesktop.App"`, `framework.version` starts `10.0.`, no `rollForward` key or `rollForward == "Minor"`, `includedFrameworks` absent).

**INV-PKG-6. Each MSI is architecture-pure: every PE file in the x64 payload has machine `0x8664` (or is IL-only AnyCPU) and every PE file in the ARM64 payload has machine `0xAA64` (or is IL-only); the MSI template platform matches (`x64` or `Arm64`).** Why: an arm64-only native DLL cannot load into an x64 process and vice versa (`docs/PLAN.md:241`); a mixed payload fails only at the first export or sign-in. Citation: 7.2.4. Test: `PayloadVerifierTests.ArchPure` (Linux, synthetic PE headers), CI step `verify-payload`.

**INV-PKG-7. [SECURITY] Every first-party PE in the payload (`shotAI.exe`, `shotAI.dll`, `ShotAI.Core.dll`, `ShotAI.Platform.dll`, `shotai_avif.dll`) and each MSI carry a valid Authenticode signature from the organization's Artifact Signing identity, and no PE in the payload is unsigned.** Why: SmartScreen and application control policies key on publisher; an unsigned DLL beside a signed exe is a planting target for publisher rules. Third-party PEs keep their own signatures; unsigned third-party PEs are signed per Q-PKG-3. ReadyToRun compilation rewrites every NuGet assembly it compiles and so drops that assembly's original Authenticode signature (EDGE-PKG-47): the set of "third-party PEs that keep their own signatures" is only the ones excluded from ReadyToRun or never compiled by it (native DLLs such as `msalruntime*.dll` and `WebView2Loader.dll`). Citation: 7.6. Test: CI step `Get-AuthenticodeSignature` over every `*.exe` and `*.dll` in the payload and the MSI: every `Status -eq 'Valid'`, first-party `SignerCertificate.Subject` matches the configured publisher; AC-PKG-5.

**INV-PKG-8. Signing order: sign payload PEs, then build the MSI from the signed payload, then sign the MSI; nothing modifies a file after it is signed.** Why: an MSI built from unsigned files embeds unsigned files; re-running `dotnet publish` after signing overwrites the signed files. Citation: 7.11.3. Test: the release workflow's job graph (`WorkflowContractTests.ReleaseSignsBeforePackaging`) and AC-PKG-5.

**INV-PKG-9. A version with a prerelease suffix is published as a GitHub prerelease that is not latest; a final version is published as latest; an Electron release after 2.0.0 is published with `--latest=false`.** Why: `/releases/latest` excludes only flagged prereleases and `pickRelease` refuses only `prerelease: true` (2.9.4), so an unflagged `v2.0.0-alpha.1` would be offered to every Electron user (10 EDGE-INFRA-32); a post-2.0.0 Electron hotfix created with GitHub's default `make_latest` would displace 2.0.0 as latest. Citation: `macOS:docs/DISTRIBUTION.md:85-102`, `src/main/update-check.ts:106-117`. Test: `ReleaseFlagsTests.*` (Linux), the post-publish verification step in `release.yml` (reads the release back through the API and fails if `prerelease` or `latest` disagree), AC-PKG-13.

**INV-PKG-10. The release tag equals `v` + `<Version>` of `dotnet/Directory.Build.props`, matches the native tag grammar with major at least 2, and is strictly greater (SemVer precedence) than every existing `v2.*` tag other than itself.** (The tag being released already exists when a tag push starts `release.yml`, so `git tag -l 'v2.*'` includes it; the check excludes exactly that tag and separately refuses when a non-draft GitHub release already exists for it.) Why: one version source (2.3.8); "Versions must only go up" (`macOS:docs/DISTRIBUTION.md:108-110`); a tag the comparator cannot read notifies nobody (`macOS:docs/DISTRIBUTION.md:104-106`) or, for Electron's looser regex, is misread (EDGE-PKG-20). Citation: 7.3. Test: `TagCheckTests.*`.

**INV-PKG-11. The MSI `ProductVersion` is `X.Y.B` with `B = Z*1000 + stageBase + N` for `X.Y.Z-stage.N` and `B = Z*1000 + 999` for a final `X.Y.Z`; the mapping is strictly monotonic in SemVer precedence over the grammar.** Why: Windows Installer compares only the first three fields and cannot express a prerelease, so without a mapping `2.0.0-beta.1` and `2.0.0` would be the same product version and the final would not upgrade the beta. Citation: 7.3. Test: `MsiVersionTests.*` including the exhaustive ordering test.

**INV-PKG-12. One `UpgradeCode` for both architectures, fixed forever; a new `ProductCode` per build; `MajorUpgrade` removes the previous version, including a different build of the SAME version (`AllowSameVersionUpgrades="yes"`); a downgrade is refused with `A newer version of shotAI is already installed.`** Why: Intune supersedence and detection rely on the MSI identity; a changed UpgradeCode would install side by side. Without `AllowSameVersionUpgrades`, Windows Installer's upgrade search ignores a product with the same ProductVersion, so the public and internal MSIs of one release (same version, different ProductCodes, INV-PKG-14) or a rebuilt MSI would install side by side into the same folder with two ARP entries (EDGE-PKG-46). Citation: 7.4. Test: `PackageSourceTests.UpgradeCodePinned`, `.MajorUpgradeWithDowngradeMessage`, `.AllowsSameVersionUpgrade`; AC-PKG-7, AC-PKG-8, AC-PKG-32.

**INV-PKG-13. [SECURITY] No personal data in installer metadata, release assets or the repo: no personal URL, name or email in the MSI (ARP fields), no tenant or organization identifier anywhere in the public repo.** Why: `iconUrl` was left unset for exactly this reason (`forge.config.ts:146-153`); the repo is public. Citation: 2.4.1, 2.10. Test: `PackageSourceTests.NoPersonalOrTenantData` (scans `Package.wxs`, workflow files and `Directory.Build.props` for `@` email patterns other than `noreply@`, GUIDs other than the pinned UpgradeCode and documented Microsoft GUIDs, and `onmicrosoft.com`).

**INV-PKG-14. [SECURITY] A public GitHub release asset is always a bring-your-own-key build with no baked federation; a build with baked federation is produced only as a restricted workflow artifact for Intune.** Why: the values "identify your tenant and your Anthropic organization, so treat them as internal rather than published" (`Intune/Windows/README.md:85-90`); a public MSI would publish them. Citation: 7.11.3. Test: `verify-payload --public` asserts the `shotAI.dll` manifest resources contain no `ShotAI.Federation.Baked.json`; `WorkflowContractTests.FederationSecretOnlyInInternalJob`.

**INV-PKG-15. [SECURITY] The install directory inherits the `Program Files` ACL (only administrators and SYSTEM can write), and the app never writes to its install directory.** Why: an app directory writable by a standard user lets that user plant a DLL that every later launch loads. Citation: 7.9. Test: `PackageSourceTests.NoPermissionElements` (no `Permission`, `PermissionEx` or `util:PermissionEx` in `Package.wxs`); AC-PKG-6 (`icacls` of the installed folder shows no write for `Users` or `Authenticated Users`).

**INV-PKG-16. [SECURITY] The process restricts native library search before loading any native library: `SetDefaultDllDirectories(LOAD_LIBRARY_SEARCH_DEFAULT_DIRS)` is the first statement of `Main`, and every assembly with P/Invoke declares `[assembly: DefaultDllImportSearchPaths(DllImportSearchPath.System32 | DllImportSearchPath.AssemblyDirectory)]`.** Why: the current directory and `PATH` must never supply a DLL (a document opened from a share would otherwise become a planting vector). Citation: 7.9. Test: `DllSearchTests.CurrentDirectoryNotSearched` (Platform.Tests: a DLL named like a delay-loaded dependency placed in the working directory is not loaded), `SourceScanTests.DllImportSearchPathsDeclared` (Linux, scans `AssemblyInfo` of Platform and App).

**INV-PKG-17. [SECURITY] The published apphost searches only the global .NET install locations: `<AppHostDotNetSearch>Global</AppHostDotNetSearch>` in `ShotAI.App.csproj`.** Why: the default search honors `DOTNET_ROOT`, so a user-level environment variable could substitute a private runtime under a signed exe; this is the .NET analog of the disabled `RunAsNode` and `NODE_OPTIONS` fuses (2.3.6). Citation: Microsoft Learn `AppHostDotNetSearch` (.NET 9 and later; valid values `AppLocal`, `AppRelative`, `EnvironmentVariable`, `Global`). The property "only impacts the executable produced on publish, not build", so a `dotnet build` or `dotnet run` apphost still honors `DOTNET_ROOT`; only the published payload is hardened, and only the published payload may be tested for it. Test: AC-PKG-18 (with `DOTNET_ROOT` pointing at an empty folder, the installed app still starts).

**INV-PKG-18. [SECURITY] Startup hooks are disabled: `<StartupHookSupport>false</StartupHookSupport>` in `ShotAI.App.csproj`, so `DOTNET_STARTUP_HOOKS` cannot inject an assembly.** Why: same intent as the `EnableNodeOptionsEnvironmentVariable: false` fuse. Citation: 2.3.6. Test: `PayloadVerifierTests.StartupHooksDisabled` (`runtimeconfig.json` `configProperties["System.StartupHookProvider.IsSupported"] == false`); AC-PKG-18. The MSBuild property is documented (Microsoft Learn, "Trimming options": `<StartupHookSupport>`, "Running code before `Main` with `DOTNET_STARTUP_HOOKS` isn't supported"); the runtimeconfig key name comes from the dotnet/runtime feature-switch list, not from Learn, so the test asserts it and AC-PKG-18 proves the behavior (Q-PKG-19).

**INV-PKG-19. [SECURITY] `shotai_avif.dll` is built in CI from libavif and libaom sources pinned by full commit SHA and SHA-256 of the source archive in `dotnet/native/avif/pins.json`; no native binary is committed to the repo; each build is attested.** Why: `element_locator.dll` was a committed prebuilt nobody could verify, and `get-windows` stayed an unverified binary (S1 residual, `scripts/postinstall.mjs:96-99`). This is the port's only third-party native code in-process (`docs/NATIVE-WINDOWS-FEASIBILITY.md:151-152`). Citation: 7.7. Test: `AvifPinsTests.*`; CI fails on a source hash mismatch; `git ls-files '*.dll' '*.exe'` under `dotnet/` is empty (`RepoHygieneTests.NoCommittedBinaries`).

**INV-PKG-20. [SECURITY] NuGet restore is locked and sourced from nuget.org only: `RestorePackagesWithLockFile=true`, CI restores with `--locked-mode`, and `dotnet/nuget.config` clears inherited sources and maps `*` to nuget.org.** Why: the analog of S1's lockfile integrity (`scripts/postinstall.mjs:24-41`); package `.targets` run at build time, so a substituted package is code execution in the signing job. Citation: 7.8. Test: `RepoHygieneTests.LockFilesPresent` (one `packages.lock.json` per project), `.NuGetConfigMapsOnlyNuGetOrg`.

**INV-PKG-21. `contract/` is read in place and the generated C# brand table is checked for staleness in the Linux CI job before the build.** Why: spec 10 INV-INFRA-1 and INV-INFRA-2; the same step exists on both other platforms (`ci.yml:52-57`, `macOS:.github/workflows/ci.yml:56-57`). Citation: 7.11.2. Test: `WorkflowContractTests.LinuxJobRunsBrandCheckBeforeBuild`.

**INV-PKG-22. `dotnet build ShotAI.slnx` stays buildable on Linux; the WiX installer project is not in `ShotAI.slnx` and is built only on Windows.** Why: the Linux job builds the whole solution (`dotnet.yml:6-7`, `:56`) and WiX needs Windows Installer APIs. Citation: 7.4. Test: the Linux job itself; `RepoHygieneTests.InstallerNotInSolution`.

**INV-PKG-23. Every CI run on Windows and every release installs the built MSI on the runner, launches the installed `shotAI.exe --selftest` (exit 0, `[selftest] PASS`), uninstalls, and checks the result.** Why: S11 (a package nobody launched was dead on arrival, `docs/HARDENING-PLAN.md:129-136`) and #93 (a packaged path nobody exercised, `c070095`). Citation: 7.11.2. Test: the `package` job; AC-PKG-3.

**INV-PKG-24. The native app never reads, writes or deletes Electron-owned files (`secrets.json`, `entra-cache.bin`, `tessdata\`, the Chromium profile entries in `%APPDATA%\shotAI`) or anything under the Squirrel root `%LocalAppData%\shotai\`, and keeps none of its own data under that root: native-only local data lives under `IAppPaths.LocalDataDirectory` = `%LOCALAPPDATA%\LFI\shotAI\` (MSAL cache `entra\msal-cache.bin`, WebView2 data `WebView2\`), while `settings.json`, the logs and the projects folder stay where the Electron build keeps them (R-ARCH-13).** Why: rollback needs Electron's files intact (08 EDGE-AUTH-33); Squirrel's uninstall deletes its whole root, which is the same folder as `%LOCALAPPDATA%\shotAI` on a case-insensitive file system (EDGE-PKG-22, R-ARCH-13). Citation: 2.6, 7.10; ARCHITECTURE 10.1, 10.2. Test: `Shell.AppPathsTests.NoPathUnderSquirrelRoot` and `.LocalDataDirectoryIsUnderLfi` (App.Tests, owned with 10: every `IAppPaths` member is outside `%LOCALAPPDATA%\shotai`, compared `OrdinalIgnoreCase` after `Path.GetFullPath`, and `LocalDataDirectory` equals `%LOCALAPPDATA%\LFI\shotAI`); AC-PKG-24; ARCHITECTURE AC-ARCH-7.

**INV-PKG-25. The shared `settings.json` stays a JSON object that Electron 1.3.x parses and preserves: native writes are atomic, preserve unknown keys, and never change the type or meaning of a key Electron owns; the supported rollback target is Electron 1.3.0 or later.** Why: Electron's `load()` falls back to defaults on any parse failure, and its next write would then reset `projectsDir` and recents; Electron before 1.3.0 deletes unknown keys (#92, `src/main/settings.ts:137-150`). Citation: 7.10.2. Test: owned by 10 (`SettingsServiceTests` unknown-key round trip); here AC-PKG-22.

**INV-PKG-26. The native app does not run while an Electron shotAI (an `shotAI.exe` whose image path is under `%LocalAppData%\shotai\app-`) is running in the same Windows session: it shows the legacy-running notice and exits.** Why: the two builds cannot see each other's single-instance lock or write queue and would edit the same `project.json` concurrently (`src/main/main.ts:161-167`). IMPROVEMENT (no Electron analog; coexistence did not exist). In a self-test mode (10 7.8: `--selftest`, `--capture-selftest`, `--update-selftest`, or the `SHOTAI_SELFTEST` / `SHOTAI_CAPTURE_TEST` switches) the guard shows NO message box (it would block an unattended script forever); it writes one line to standard error and exits with the self-test Error code 2 instead of 0, so a script never reads a guard hit as PASS (EDGE-PKG-50). Citation: 7.10.3. Test: `LegacyInstanceGuardTests.*` (App.Tests with a fake process source); AC-PKG-20.

**INV-PKG-27. The Electron 1.3.x release assets on GitHub and the Electron Intune app object are kept for at least 90 days after 2.0.0 GA.** Why: they are the rollback path (7.13). Test: release checklist item; AC-PKG-27.

**INV-PKG-28. [SECURITY] `release.yml` runs with `permissions: {}` at the top and per-job minimum permissions (`contents: read` for build, `id-token: write` only for the signing job and the attesting `native-avif` job, `attestations: write` only for `native-avif`, `contents: write` only for the draft-release job), signs only inside the protected `release` environment, creates the release as a draft, and pins third-party actions by full commit SHA.** Why: the signing identity is the most valuable secret the project will hold. Citation: 7.11.3. Test: `WorkflowContractTests.ReleasePermissionsMinimal`, `.ActionsPinnedBySha`.

**INV-PKG-29. `THIRD-PARTY-NOTICES.txt` is installed next to `shotAI.exe` and attached to each release; it names every shipped NuGet package and native library with its licence, including the SIL OFL 1.1 for Archivo (pointing at `Fonts\OFL.txt`), BSD-2-Clause for libavif and libaom, and the Alliance for Open Media Patent License 1.0 for libaom.** Why: OFL condition 2 (10 INV-INFRA-31); libaom's licence and patent grant must accompany binaries. Citation: 7.7.4. Test: `ThirdPartyNoticesTests.CoversShippedPackages` (every `PackageVersion` not marked build-only appears), `.CoversNativeLibraries`.

**INV-PKG-30. The MSI creates exactly one per-machine Start menu shortcut to `[INSTALLFOLDER]shotAI.exe` named per the shortcut rule, sets `ARPPRODUCTICON` from `assets/shotAI_icon.ico`, sets DisplayName `shotAI` and Manufacturer `LFI`, and creates no desktop shortcut unless `DESKTOPSHORTCUT=1` is passed.** Why: parity with Squirrel's Start menu entry and the ARP icon intent (2.5); a per-machine desktop shortcut lands on every user's desktop (Q-PKG-9). Citation: 7.4. Test: `PackageSourceTests.ShortcutAndArp`; AC-PKG-2 (03 AC-SHELL-30).

**INV-PKG-31. The MSI neither bundles nor chains the .NET Desktop Runtime, the WebView2 Runtime or the OCR language: the Desktop Runtime is an Intune dependency (hard: the app cannot start without it), WebView2 and OCR are soft (only PDF export or auto-redact degrade, with their own notices from 09 and 04).** Why: prerequisites are serviced by Microsoft and deployed once per device; a chained runtime installer inside an MSI is not supported by Windows Installer and would freeze a runtime version. Citation: 7.5. Test: `PackageSourceTests.NoChainedInstallers`; AC-PKG-10, AC-PKG-11.

**INV-PKG-32. [SECURITY] Every signature is RFC 3161 timestamped.** Why: Artifact Signing certificates live about three days; an untimestamped signature becomes invalid days after release. Citation: Microsoft Learn (Artifact Signing). Test: CI step asserts `TimeStamperCertificate` is not null for every signed file; AC-PKG-5.

**INV-PKG-33. The app manifest requests `asInvoker` and `uiAccess="false"`; the app never needs elevation after install.** Why: all state is per user; the installer is the only elevated component. Citation: 7.9. Test: `ManifestTests.AsInvoker` (Linux, parses `app.manifest`).

**INV-PKG-34. Release assets are named `shotAI-<semver>-x64.msi` and `shotAI-<semver>-arm64.msi` (no spaces), and every release carries `SHA256SUMS.txt` with one `<sha256>  <file>` line per asset.** Why: 2.4.1 (the space in Squirrel's default name) and the macOS rule that the notice shows the asset name (`macOS:docs/DISTRIBUTION.md:112-113`). Test: `ReleaseVersionTests.AssetNames`; `release.yml` verification step.

## 5. Edge cases and hard-won fixes

**EDGE-PKG-1. The packaged app was dead on arrival** because the Vite plugin shipped no `node_modules`: `Cannot find module 'electron-log/main'`, and after the first fix `Cannot find module 'readable-stream'`. Dev runs could not show it. Required natively: the `package` CI job installs and launches every MSI (INV-PKG-23). From S11, `docs/HARDENING-PLAN.md:129-136`; `forge.config.ts:12-70`.

**EDGE-PKG-2. Dependency metadata lied**: npm marked a needed transitive package `devOptional`, so a flag-based filter dropped it. Required natively: the payload is verified against an explicit expected list (`verify-payload`, 7.2.4), not inferred from project metadata. From S11 second iteration. `forge.config.ts:24-31`.

**EDGE-PKG-3. "A path that points at a file which EXISTS is not a path that LOADS."** Twice: the asar check and the `@jsquash/avif` unpack (which broke the ESM import). Required natively: loadability tests on the installed payload: `NativeLibrary.TryLoad` of `shotai_avif.dll` and one encode (`--selftest` includes an AVIF probe from 09, or a dedicated `--avif-selftest`, Q-PKG-28), and the PDF self-test that checks the embedded Archivo subset (09). From `c070095`, `66d7376`. `forge.config.ts:84-98`.

**EDGE-PKG-4. The OFL licence was in the source tree but not in the installed app.** Required: `Fonts\OFL.txt` beside every Archivo file and a notices file (INV-PKG-29, 10 INV-INFRA-31). From #93, `c070095`. `forge.config.ts:117-121`.

**EDGE-PKG-5. "Installed apps" showed a generic icon** because Squirrel sets `DisplayIcon` only after downloading `iconUrl`; the first fix (overwrite `app.ico`) did not work. Required: MSI `ARPPRODUCTICON` from the bundled `.ico`, which Windows Installer writes into the ARP entry itself. From PR #19 and the later `arp-icon.ts`. `src/main/arp-icon.ts:1-15`; `docs/HARDENING-PLAN.md:224-227`.

**EDGE-PKG-6. `iconUrl` would bake a personal GitHub URL into the installed package.** Required: no URL fields other than the project repo (`ARPURLINFOABOUT` optional, repo URL only); INV-PKG-13. `forge.config.ts:146-153`.

**EDGE-PKG-7. A Squirrel lifecycle launch lost the single-instance race and skipped the ARP fix.** Required: no installer action launches the app (INV-PKG-4). From `d79bc3b`. `src/main/main.ts:154-159`; 03 EDGE-SHELL-1.

**EDGE-PKG-8. Squirrel's default installer name `shotAI-<version> Setup.exe` has a space.** Required: INV-PKG-34 names. `forge.config.ts:137-141`.

**EDGE-PKG-9. Admins tried `/S`, `/quiet` and `/qn` on a Squirrel installer.** Required: native is a real MSI; the documented command is `msiexec /i "shotAI-<semver>-<arch>.msi" /qn /norestart` and uninstall `msiexec /x {ProductCode} /qn /norestart`. From `ac5e101`. `README.md:274-276`.

**EDGE-PKG-10. A device-context push could not install the per-user Squirrel app.** Required: per-machine MSI installed in System context. `README.md:280-281`.

**EDGE-PKG-11. The unsigned installer met SmartScreen and Defender prompts.** Required: signing (INV-PKG-7). Note that Intune-installed files carry no Mark of the Web, so SmartScreen does not prompt for them; a manually downloaded signed MSI can still show a SmartScreen warning until the publisher identity has reputation (Microsoft Learn, Artifact Signing). `README.md:284-286`.

**EDGE-PKG-12. npm 11 `allow-scripts` silently skipped Electron's binary download.** Native analog: NuGet packages run no install scripts, but their `build\*.targets` execute during every build. Required: locked restore from a single mapped source (INV-PKG-20). `docs/PLAN.md:273-280`.

**EDGE-PKG-13. Mapped and virtualized drives broke `tar` and `cargo` ("os error 87").** Required: CI builds on the runner's local disk; the release scripts never assume the repo is on a mapped drive; the libavif build uses a runner-local build directory. `scripts/postinstall.mjs:49-51`, `scripts/build-element-locator.mjs:6-8`.

**EDGE-PKG-14. `get-windows` had no arm64 prebuild, which forced x64 everywhere, and its prebuild download was unverified.** Required natively: no such module (02); ARM64 is a first-class build. `docs/PLAN.md:245-251`; `scripts/postinstall.mjs:94-99`.

**EDGE-PKG-15. x64 under ARM64 emulation could not create a GPU context, so Electron forced software rendering on the dev machine.** Required: ship the ARM64 MSI to ARM64 devices (Intune requirement rule); the x64 build is still expected to run under emulation (WPF, no GPU policy needed). `src/main/gpu-policy.ts:1-17`.

**EDGE-PKG-16. A pilot user on `2.0.0-alpha.N` is never told that `2.0.0` shipped**, because Electron's comparator ignores the suffix. Required: 10's IMPROVEMENT (a running prerelease is older than its final, 10 7.6.2); for managed pilots, Intune delivers 2.0.0 anyway. 10 EDGE-INFRA-31.

**EDGE-PKG-17. An unflagged native prerelease would be offered to every Electron user.** Required: INV-PKG-9 plus the post-publish API check. 10 EDGE-INFRA-32; `macOS:docs/DISTRIBUTION.md:85-88`.

**EDGE-PKG-18. An Electron hotfix published after 2.0.0 would become GitHub's latest by default**, so native users would stop seeing native updates and Electron users would be told nothing new. Required: `gh release create v1.3.N --latest=false` for every Electron release after 2.0.0 (7.12). New.

**EDGE-PKG-19. Publishing a lower version as latest makes every installed copy think it is current.** Required: INV-PKG-10 monotonic check in `release.yml` against existing tags. `macOS:docs/DISTRIBUTION.md:108-110`.

**EDGE-PKG-20. Electron's tag regex is anchored only at the start.** A tag such as `2026-08-04` passes `/^\d+(\.\d+)*/`, `split('-')[0]` gives `2026`, and every Electron client would be told "2026" is newer. Required: the release workflow refuses any tag outside the native grammar; no non-version releases (for example `nightly`) are ever published as non-prerelease in this repo. `src/main/update-check.ts:37-42`, `:113-114`. New.

**EDGE-PKG-21. MSIX would virtualize new files under `AppData`.** Documented behavior (Windows 10 1903 and later): new files and folders under `AppData\Roaming` go to a per-user, per-package private location; existing files are modified in place. `settings.json` is written by atomic temp-and-rename (`src/main/atomic-write.ts:37-51`), so every native write would create a new, virtualized file and the two builds would diverge silently; on uninstall the virtualized copy is deleted. Required: MSI (INV-PKG-1). 10 EDGE-INFRA-39.

**EDGE-PKG-22. `%LOCALAPPDATA%\shotAI` is the Squirrel install root.** Squirrel installs to `%LocalAppData%\shotai`, and Squirrel's uninstall removes that whole folder. The first drafts of specs 08 (MSAL cache `%LOCALAPPDATA%\shotAI\entra\`) and 09 (WebView2 user data `%LOCALAPPDATA%\shotAI\WebView2`) placed native data there, so removing Electron at cutover would have silently deleted the native sign-in cache and WebView2 profile, and a Squirrel repair could too. Required (R-ARCH-13, adopting Q-PKG-4): all native-only local data lives under `IAppPaths.LocalDataDirectory` = `%LOCALAPPDATA%\LFI\shotAI\`, which is not the Squirrel root: the MSAL cache at `%LOCALAPPDATA%\LFI\shotAI\entra\msal-cache.bin` (08) and the WebView2 user data at `%LOCALAPPDATA%\LFI\shotAI\WebView2` (09); no code composes the path itself (ARCHITECTURE 10.2); INV-PKG-24; proved manually by AC-PKG-24 and ARCHITECTURE AC-ARCH-7. New.

**EDGE-PKG-23. The two builds can run at the same time** (different locks) and write the same `project.json`, and both replace `settings.json` by temp file and rename (Electron's temp name is `${file}.${process.pid}.tmp`, `src/main/atomic-write.ts:37-51`), so the last writer silently wins on settings too. Required: INV-PKG-26 on the native side; pilot guidance for the reverse order (an Electron launch cannot be prevented). New.

**EDGE-PKG-24. Both builds append to `%APPDATA%\shotAI\logs\shotai.log` during the pilot**, and electron-log rotates by renaming the file. Required: the native file sink opens the log for each flush with `FileShare.ReadWrite | FileShare.Delete` (or keeps a handle that allows delete and rename) and treats a rename underneath it as a new file; lines may interleave, which is acceptable. 10 owns the sink; this is the coexistence constraint. New.

**EDGE-PKG-25. Electron before 1.3.0 deletes unknown `settings.json` keys.** A rollback to 1.2.x would erase native-only keys (and `brand`). Required: rollback target is 1.3.x (INV-PKG-25); the retained Electron artifact is 1.3.x. From #92, `2b14b79`. `src/main/settings.ts:137-150`.

**EDGE-PKG-26. Windows Installer compares only three version fields and has no prerelease notion.** Required: the 7.3 mapping (INV-PKG-11). New.

**EDGE-PKG-27. `VersionNT` is 603 on Windows 10**, so it cannot express "Windows 10 2004 or later" (the companion `WindowsBuild` property is likewise frozen at the Windows 8.1 value 9600, UNVERIFIED on a real Windows 10 install, so it is not used either). Required: the launch condition reads the build number from the registry (7.4.4, Q-PKG-6). Microsoft Learn (KB 3202260). New.

**EDGE-PKG-28. WebView2's default user data folder sits next to the exe**, which is read-only under `Program Files`, so environment creation fails in a per-machine install. Required: explicit user data folder (09 EDGE-EXP-40) `%LOCALAPPDATA%\LFI\shotAI\WebView2`, that is `Path.Combine(IAppPaths.LocalDataDirectory, "WebView2")` (R-ARCH-13; never the Squirrel root, EDGE-PKG-22). 09.

**EDGE-PKG-29. An upgrade while shotAI is running finds `shotAI.exe` in use.** With `/qn`, Windows Installer's Restart Manager asks applications to close; a refusal leaves files for replacement at reboot (3010). Required: the app handles session end (`WM_QUERYENDSESSION`, `WM_ENDSESSION`, surfaced by WPF as `SessionEnding`) by calling `ICaptureService.Teardown()` and letting WPF continue to `App.OnExit`, whose `ShutdownFlush.Run(TimeSpan.FromSeconds(5))` drains the project and settings queues before `provider.Dispose()`; nothing vetoes (03 session end, ARCHITECTURE 4.5, R-ARCH-10), and Intune "Device restart behavior" is "Determine behavior based on return codes". New; verify (AC-PKG-8).

**EDGE-PKG-30. Taskbar pins and desktop shortcuts point at the Squirrel path.** After the Electron uninstall they break. Required: cutover communication tells users to re-pin from the Start menu; the native MSI creates no desktop shortcut by default. New.

**EDGE-PKG-31. During the pilot the Start menu and "Installed apps" show two shotAI entries.** Required: the native prerelease shortcut is named `shotAI Preview` (a final is `shotAI`); ARP entries differ by version (1.3.x per user, 2.0.0-series per machine) and by scope. At GA the final native shortcut is also named `shotAI`, so between the 2.0.0 install and the Electron removal assignment (7.13.3) a user sees two identical `shotAI` Start menu entries; the removal must run in the same Intune cycle as the 2.0.0 deployment wherever possible. New.

**EDGE-PKG-32. Intune does not expand environment variables in the uninstall command**, and Squirrel's uninstaller lives under `%LocalAppData%`. Microsoft Learn, "Add a Win32 app", Program step: "Environment variable expansion within the **Uninstall command** is not supported. If you require the use of environment variables, use a custom wrapper script within your Win32 package". The same page notes that `powershell.exe` named in either command runs the 32-bit PowerShell; that is harmless here (`%LOCALAPPDATA%` is not redirected for 32-bit processes). Required: the Electron removal uses a wrapper script shipped INSIDE the Win32 package content (7.13.3). New.

**EDGE-PKG-33. Squirrel's uninstall is expected to leave `%APPDATA%\shotAI` in place** (it removes the install root and shortcuts), which is what makes a native-after-Electron cutover lossless. Verify on a 1.3.0 install before the pilot (AC-PKG-24). New.

**EDGE-PKG-34. With warnings as errors, NuGet audit warnings (NU1901 to NU1904) become build errors** the moment an advisory is published, failing unrelated PRs. Required: keep audit on and failing in the release workflow; in PR CI, decide per Q-PKG-14. `dotnet/Directory.Build.props` (`TreatWarningsAsErrors`). New.

**EDGE-PKG-35. Path-filtered jobs never report on PRs they skip**, so they cannot be required checks. Required: keep the warning; at cutover, remove the filters and make the native jobs required. `.github/workflows/dotnet.yml:11-12`.

**EDGE-PKG-36. `dotnet test` misses the Microsoft.Testing.Platform opt-in unless run from `dotnet/`.** Required: every native workflow step runs with `working-directory: dotnet` (including release steps). `.github/workflows/dotnet.yml:39-44`.

**EDGE-PKG-37. A default Windows clone (`core.autocrlf=true`) rewrites the contract to CRLF.** Windows runners are such clones. Required: `.gitattributes` pins `contract/**` and every generated table (C# included) to LF, and the generator canonicalizes (10 EDGE-INFRA-1). `.gitattributes:4-15`.

**EDGE-PKG-38. The install GIF's size depended on the author's display scale.** Required: any generated packaging asset (for example an MSI banner, if one is ever added) is produced deterministically or committed as source art; the `.ico` stays a committed asset. `scripts/make-loading-gif.cjs:16-24`.

**EDGE-PKG-39. A test that cannot fail proves nothing.** The probes carry a control, and the #93 packaging test was strengthened after "matching two literals SEPARATELY" passed against a stale name. Required: packaging tests read one side from the other (for example the font name from the WiX source and from `AppPaths`) and each new packaging test is mutation-checked once when written. `scripts/report-width-probe.cjs:13-15`; `src/shared/export-theme.test.ts:127-152`; `c070095`.

**EDGE-PKG-40. Electron users who follow the 2.0.0 update notice land on a release page whose MSI needs administrator rights and the .NET Desktop Runtime.** Required: the 2.0.0 release notes open with the managed-device instruction (7.12.3); unmanaged users may stay on 1.3.x. New.

**EDGE-PKG-41. .NET's informational version can carry `+<sha>`.** Required: `IncludeSourceRevisionInInformationalVersion=false` so About, the User-Agent and logs show exactly the tag version (10 EDGE-INFRA-29 strips it defensively). New.

**EDGE-PKG-42. If the Desktop Runtime is missing, the apphost shows a .NET "install the runtime" dialog (exact wording UNVERIFIED; it names the missing framework and offers a download link) and exits.** Required: Intune dependency ordering (the runtime installs first); the MSI does not block installation on the runtime (so a dependency retry cannot wedge the app install). Microsoft Q&A. New.

**EDGE-PKG-43. On an Arm64 PC the x64 runtime lives under `%ProgramFiles%\dotnet\x64\`.** An x64 shotAI on an Arm64 PC needs the x64 Desktop Runtime there; the ARM64 build needs the Arm64 runtime in `%ProgramFiles%\dotnet\`. Required: the Intune dependency matches the MSI architecture. Microsoft Learn. New.

**EDGE-PKG-44. MSAL's broker brings native `msalruntime` binaries per RID.** Required: RID-specific publish ships the matching one; `verify-payload` checks its PE machine. 08 NuGet note.

**EDGE-PKG-45. A per-machine uninstall runs as SYSTEM and cannot reliably reach every user's profile.** Required: the MSI does not try to clean per-user folders (INV-PKG-2); 09 Q-EXP-20 ("delete it on uninstall") is answered "no". New.

**EDGE-PKG-46. Two MSIs of the same version install side by side.** Windows Installer's major-upgrade search ignores a related product whose version equals the new one unless the upgrade row allows it, and the public and internal builds of one release share `UpgradeCode` and ProductVersion but not ProductCode. IT installing the public MSI and later the internal one (or a rebuilt MSI) would get two ARP entries over one folder, and uninstalling either would break the other. Required: `MajorUpgrade AllowSameVersionUpgrades="yes"` (INV-PKG-12); the WiX ICE61 warning this raises is suppressed for that one ICE with a comment. New.

**EDGE-PKG-47. ReadyToRun drops third-party signatures.** `PublishReadyToRun` compiles the NuGet assemblies in the publish closure into new PE files, which carry no Authenticode signature (their publishers' signatures covered the IL-only originals). With Q-PKG-3's default every such file would then be re-signed under LFI's identity. Required: either exclude third-party assemblies from ReadyToRun with `PublishReadyToRunExclude` items (they keep their original signatures; only first-party assemblies are precompiled), or accept that LFI signs the recompiled files and list them in the release log. Recommended: exclude (Q-PKG-10). UNVERIFIED which assemblies the SDK compiles; the first `package` job run lists every `NotSigned` PE, which decides it. New.

**EDGE-PKG-48. `SetDefaultDllDirectories` can hide WPF's own native DLLs.** WPF's native components (`wpfgfx_cor3.dll`, `PresentationNative_cor3.dll`, `D3DCompiler_47_cor3.dll`, `vcruntime140_cor3.dll`) live in the shared framework folder `%ProgramFiles%\dotnet\shared\Microsoft.WindowsDesktop.App\10.x\`, which is neither the application directory nor `System32`. The runtime loads P/Invoke targets by absolute path, but any plain `LoadLibrary("<name>")` inside native WPF code after `LOAD_LIBRARY_SEARCH_DEFAULT_DIRS` is set would not find a sibling in that folder, and the failure could be a silent fallback to software rendering rather than a crash. UNVERIFIED either way. Required: AC-PKG-31 (render tier and D3D compiler load checked on real hardware with the hardening on); if it fails, `AddDllDirectory` the WindowsDesktop framework directory (the folder of `typeof(System.Windows.Window).Assembly.Location`) right after `SetDefaultDllDirectories` (Q-PKG-30). New.

**EDGE-PKG-49. A Microsoft Update runtime patch can remove the runtime shotAI is running on.** "The installer executables always install new content before removing the previous installation. Applications that are running might be interrupted or crash when older runtimes are removed" (Microsoft Learn, Install .NET on Windows). Required: every edit is queued at once on the store queue (01) or the settings queue (10) and every file is written atomically, so a crash leaves each file either old or new and loses at most the writes still queued (ARCHITECTURE 7.3, 7.10, 7.11); IT may set `RemovePreviousVersion` to `nextSession` under `HKLM\SOFTWARE\Microsoft\.NET\10.0` (section 3) if pilot users report it. Documented in the Intune notes, not enforced by the MSI (INV-PKG-3 forbids only the Policies key, but the MSI still writes nothing outside `SOFTWARE\LFI\shotAI`). New.

**EDGE-PKG-50. A modal notice in a self-test hangs unattended runs.** `LegacyInstanceGuard` runs before the self-test modes (10 7.8), and a `MessageBox` on a headless runner or in an IT script waits forever, then exits 0, which the script reads as PASS. Required: INV-PKG-26 self-test branch (stderr line `legacy shotAI 1.x is running (pid <pid>, <path>); self-test refused.`, exit code 2, no dialog). New.

**EDGE-PKG-51. Exploit-protection "Arbitrary code guard" breaks a JIT.** `docs/PLAN.md:241` warned not to enable `ProcessDynamicCodePolicy` for Electron (V8). The CLR JIT (and ReadyToRun rejit) allocates executable memory the same way. Required: the Intune notes tell IT not to target `shotAI.exe` with an ACG (dynamic code) exploit-protection rule; CET shadow stack, which the .NET 9+ apphost enables, is compatible. New.

**EDGE-PKG-52. A test project that silently does not run.** The macOS CI exists because "41 auth tests silently did not run" behind a broken package manifest (`macOS:.github/workflows/ci.yml:3-8`). `dotnet test --solution` runs only projects listed in `ShotAI.slnx`, so a Windows test project that is never added reports nothing. Microsoft.Testing.Platform already fails a project that discovers zero tests, but not a project that is absent. Required: `RepoHygieneTests.EveryTestProjectInSolution` (every `tests/*/*.csproj` appears in `ShotAI.slnx`). New.

**EDGE-PKG-53. The pushed tag is its own "existing tag".** A tag push creates the tag before `release.yml` runs, so a "strictly newer than every existing `v2.*` tag" check that reads `git tag -l 'v2.*'` would always fail. Required: INV-PKG-10 wording (exclude the tag itself; refuse if a published release already exists for it). New.

**EDGE-PKG-54. A PowerShell `Test-Path` or `-eq` on its own line never fails a step.** It only prints `True` or `False`. Required: every smoke assertion in 7.11.4 is `if (-not (<condition>)) { throw '<message>' }`. New.

**EDGE-PKG-55. Architecture support is asymmetric.** Windows Installer refuses a package whose Template Summary platform does not match the machine ("If the current platform does not match one of the platforms specified in the Template Summary property then the installer does not process the package"), so the ARM64 MSI never installs on an x64 PC. The x64 MSI on ARM64 relies on x64 emulation, which Windows 11 on Arm provides and Windows 10 on Arm does not (Windows 10 on Arm emulates only x86; UNVERIFIED against a Learn page in this pass). Required: Intune requirement rules route ARM64 devices to the ARM64 MSI; Q-PKG-20's "allow" default is limited to Windows 11 on Arm. New.

**EDGE-PKG-56. The CI runner's .NET is not where production's is.** `actions/setup-dotnet` sets `DOTNET_ROOT` and may install outside the global default location, while the published apphost searches only global locations (INV-PKG-17). Required: the install smoke (7.11.4) installs the official .NET 10 Desktop Runtime installer for the runner architecture with `/install /quiet /norestart` before launching the installed app, the same way Intune does, instead of relying on the SDK that `setup-dotnet` placed. New.

**EDGE-PKG-57. Locked restore and runtime identifiers.** `packages.lock.json` records RID-specific graphs only for the RIDs in `RuntimeIdentifiers`. `dotnet publish -r win-arm64` restores every referenced project for that RID, and a project that does not list it (Platform, Core) fails locked-mode restore (NuGet `NU1004`, UNVERIFIED for projects without RID-specific assets). Required: `RuntimeIdentifiers` `win-x64;win-arm64` is set for Platform and App (and harmless for Core), and the `package` job runs with `RestoreLockedMode` like the release. New.

**EDGE-PKG-58. The "Latest" flag of a draft is decided when it is published.** GitHub drafts cannot be latest; the person publishing the draft in the web UI chooses "Set as the latest release" and "Set as a pre-release" at that moment, so `--latest` or `--latest=false` given to `gh release create --draft` may not survive (UNVERIFIED). Required: the 7.12.2 checklist names both checkboxes, and `verify-published` checks `GET /repos/{owner}/{repo}/releases/latest` returns the expected tag (a final) or not this tag (a prerelease), which does not depend on `gh` JSON field names. New.

**EDGE-PKG-59. An Intune uninstall assignment is ignored while an install assignment targets the same group.** "If a group is assigned to both install an app and uninstall an app, the app will remain and not be removed" (Microsoft Learn, "Add apps to Microsoft Intune"). Required: 7.13.3 removes the Electron app's Required (install) assignment for a group before adding its Uninstall assignment. New.

**EDGE-PKG-60. The macOS release preflight checks all seven federation keys; Windows has five required.** `macOS:Scripts/dist.sh:79-87` fails when any of seven plist keys (including the client id and the workspace id) is missing. On Windows `ClientAppId` and `WorkspaceId` are optional but fail closed when present and malformed (`Intune/Windows/README.md:40-47`). Required: the internal-variant preflight (7.11.3) runs 08's validator over the secret, so a present but malformed optional value fails the release build instead of shipping a fleet-wide fail-closed configuration. New.

## 6. macOS port notes

| Topic | macOS implementation | Divergence and lesson for Windows |
|---|---|---|
| One-command release | `Scripts/dist.sh [dmg\|pkg\|all]`: preflight identities and notary profile, clean Release build with signing disabled, explicit sign, notarize, staple, package, verify (`macOS:Scripts/dist.sh:1-154`) | Windows moves the same pipeline into `release.yml` so it does not depend on one person's keychain (IMPROVEMENT). Keep the shape: preflight, clean build, sign inside-out, package, verify. |
| Signing identity | Developer ID Application for the app, Developer ID Installer for the `.pkg`; prefer the organization account so "the app becomes a company asset" (`macOS:docs/DISTRIBUTION.md:14-35`, `:138-146`) | Same principle: the Artifact Signing identity validation is for the organization, not a person (Q-PKG-2). |
| Inside-out signing | sign every file in `Frameworks` and `PlugIns`, then the app, with `--timestamp` (`macOS:Scripts/dist.sh:106-113`) | INV-PKG-8 and INV-PKG-32 are the same rules. |
| Timestamps | "Timestamp required \u2014 signing uses `--timestamp` (needs network at sign time)" (`macOS:docs/DISTRIBUTION.md:197-198`) | INV-PKG-32; Artifact Signing is online by design. |
| Zero third-party dependencies | "there's no embedded framework/dylib to sign" (`macOS:docs/DISTRIBUTION.md:129-130`) | Windows has NuGet packages and one native DLL, hence INV-PKG-7, INV-PKG-19, INV-PKG-29. |
| SSO preflight | the build fails if `Federation.plist` is missing or incomplete, unless `SHOTAI_ALLOW_NO_SSO=1` builds the bring-your-own-key artifact on purpose (`macOS:Scripts/dist.sh:70-100`) | Adopted as the internal-variant preflight (7.11.3): the internal job fails without the secret, with any of the five required keys missing, or when 08's validator rejects the config; the public job is always bring-your-own-key (INV-PKG-14). macOS requires all seven of its plist keys (`macOS:Scripts/dist.sh:79-87`, including the client id and workspace id); Windows requires five because `ClientAppId` and `WorkspaceId` are optional there (EDGE-PKG-60). macOS's final Gatekeeper checks are advisory (`spctl ... \|\| true`, `macOS:Scripts/dist.sh:151-153`); Windows verification in 7.6 is blocking. |
| MDM package | signed `.pkg` for Intune, plus a PPPC profile pre-approving TCC permissions (`macOS:Intune/README.md:6-43`) | Windows needs no permission profile: hooks, capture and UI Automation need no consent. The Intune artifact is the MSI wrapped as `.intunewin` (Q-PKG-18). |
| Prerelease discipline | "A release candidate MUST be published with `prerelease: true`"; `gh release create v1.2.0-rc1 --prerelease`, `gh release create v1.2.0 --latest`; verify with `gh release view --json tagName,isPrerelease,isLatest` and `UpdateSelfTest` (`macOS:docs/DISTRIBUTION.md:79-102`) | Adopted, automated (INV-PKG-9, 7.12). |
| Tag and version rules | tags must parse as semver; versions only go up; attach the installer (`macOS:docs/DISTRIBUTION.md:104-113`) | INV-PKG-10, INV-PKG-34. |
| Fleet opt-out of the update check | managed preference `updateCheckDisabled` honored only from a forced domain (`macOS:Intune/README.md:52-88`) | Windows has no equivalent policy value today (10 Q-INFRA-4); Q-PKG-15 keeps it out of 2.0.0. |
| CI | per-package `swift test` matrix with `fail-fast: false` ("the first failure hides the state of the other six"), a brand contract job, an unsigned `xcodebuild` (`macOS:.github/workflows/ci.yml:1-79`); CI was added after a broken manifest meant "41 auth tests silently did not run (#99)" (`:3-8`) | Adopted: independent jobs that each report; brand check; unsigned builds in PR CI, signing only in release; every test project must be in the solution (EDGE-PKG-52). |
| Signing in CI | disabled on purpose: "a runner holds no such certificate" (`macOS:.github/workflows/ci.yml:65-69`) | Windows signs in CI because Artifact Signing is a cloud service reached with OIDC; PR CI stays unsigned. |
| Notarization | required for Gatekeeper outside MDM; optional for MDM installs (`macOS:docs/DISTRIBUTION.md:173-175`) | No Windows analog; SmartScreen reputation accrues to the publisher identity. |

## 7. Native design (C#)

### 7.1 Decision: per-machine MSI, deployed as an Intune Win32 app

| Criterion | Per-machine MSI (Win32 app) | MSIX | Intune LOB MSI app |
|---|---|---|---|
| Shares `%APPDATA%\shotAI\settings.json` and logs with Electron | Yes, real paths | No by default: new `AppData` files are virtualized per package (EDGE-PKG-21); opt-out needs `unvirtualizedResources` (restricted, "not intended for other scenarios") and either the all-or-nothing `desktop6:FileSystemWriteVirtualization` (1903+) or per-folder `virtualization:ExcludedDirectories` (build 20348+, not available on Windows 10 client builds 19041 to 19045) | Yes |
| Uninstall keeps user data | Yes | Virtualized data is deleted on uninstall | Yes |
| Signing | Recommended | Mandatory, publisher in the manifest must equal the certificate subject (Artifact Signing FAQ: error `0x8007000b` "indicates that the publisher in the manifest file doesn't match the certificate subject") | Recommended |
| Desktop Runtime prerequisite | Intune dependency | Only via App Installer `win32dependencies:ExternalDependency` (element name UNVERIFIED in this pass), not via Intune dependencies | Not supported (no dependencies) |
| Architecture targeting | Requirement rules (x64, ARM64) | Bundle | No requirement rules |
| Detection | MSI product code and version (automatic) | Package identity | MSI product code |
| Upgrade path | MajorUpgrade plus supersedence (Uninstall previous version off) | Package update | Only in-place |
| Autopilot guidance | Microsoft recommends Win32 apps exclusively | | Mixing LOB and Win32 during Autopilot can fail on the Trusted Installer |
| HKLM policy read | Unaffected | Reads of HKLM are unaffected | Unaffected |

Decision: **MSI, authored with the WiX Toolset MSBuild SDK, wrapped as an Intune Win32 app.** IMPROVEMENT over Squirrel (device context, signed, standard detection). This closes 10 Q-INFRA-8 and 03's installer items. Licensing of the WiX Toolset version used is an open decision (Q-PKG-1).

Consequences:

| Topic | Consequence |
|---|---|
| Intune detection | MSI rule: product code plus "MSI product version check: Yes" (greater than or equal to the packaged version). Alternative file rule `%ProgramFiles%\shotAI\shotAI.exe` version at least `X.Y.B.0`. |
| Upgrade | New Win32 app per version that supersedes the previous one with "Uninstall previous version" off; the MSI MajorUpgrade removes the old files. |
| Uninstall | `msiexec /x {ProductCode} /qn /norestart` (Intune fills the product code). Removes `%ProgramFiles%\shotAI`, the Start menu shortcut and the ARP entry; keeps all per-user data. |
| Rollback between native versions | A downgrade is refused by the MSI; Intune uninstalls the newer version first, then installs the older (7.13). |

### 7.2 Build and publish

#### 7.2.1 MSBuild properties

`dotnet/Directory.Build.props` (additions; REQUIRED unless marked):

```xml
<PropertyGroup>
  <!-- Single source of the release version (INV-PKG-10). Bumped in a release PR. -->
  <Version>2.0.0-alpha.0</Version>
  <IncludeSourceRevisionInInformationalVersion>false</IncludeSourceRevisionInInformationalVersion>
  <!-- FileVersion is set by the release tool from the MSI mapping (7.3); default for dev builds: -->
  <FileVersion Condition="'$(FileVersion)' == ''">0.0.0.0</FileVersion>
  <RestorePackagesWithLockFile>true</RestorePackagesWithLockFile>
  <RestoreLockedMode Condition="'$(CI)' == 'true'">true</RestoreLockedMode>
  <NuGetAudit>true</NuGetAudit>
  <NuGetAuditMode>all</NuGetAuditMode>
  <!-- Q-PKG-14, ARCHITECTURE 3.1 V5: low and moderate advisories stay warnings outside the release workflow,
       whose test job passes -p:ShotAIStrictAudit=true so they fail there too. -->
  <WarningsNotAsErrors Condition="'$(ShotAIStrictAudit)' != 'true'">$(WarningsNotAsErrors);NU1901;NU1902</WarningsNotAsErrors>
</PropertyGroup>
```

`dotnet/src/ShotAI.App/ShotAI.App.csproj` (additions):

```xml
<PropertyGroup>
  <RuntimeIdentifiers>win-x64;win-arm64</RuntimeIdentifiers>
  <SelfContained>false</SelfContained>
  <PublishSelfContained>false</PublishSelfContained>
  <UseAppHost>true</UseAppHost>
  <AppHostDotNetSearch>Global</AppHostDotNetSearch>      <!-- INV-PKG-17 -->
  <StartupHookSupport>false</StartupHookSupport>          <!-- INV-PKG-18, verify Q-PKG-19 -->
  <PublishReadyToRun>true</PublishReadyToRun>             <!-- IMPROVEMENT, Q-PKG-10 -->
  <SatelliteResourceLanguages>en</SatelliteResourceLanguages>
  <GenerateDocumentationFile>false</GenerateDocumentationFile>
  <DebugType>portable</DebugType>
</PropertyGroup>
```

Not used: `PublishSingleFile` (native DLLs would still sit beside it, extraction slows start, and antivirus scanning of a large single file is slower); `PublishTrimmed` (WPF is not trim-compatible: Microsoft Learn, "trimming support for WPF is currently disabled in the .NET SDK"); `SelfContained` (INV-PKG-5). `RollForward` is not set, so the default `Minor` applies. `CETCompat` is not set: the .NET 9+ apphost is CET-compatible by default (Microsoft Learn, "CET supported by default"), which also constrains every native DLL loaded in-process (no `SetThreadContext` or `RtlRestoreContext` to addresses off the shadow stack), hence the `/CETCOMPAT` link of `shotai_avif.dll` on x64 (7.7.2).

Also REQUIRED (verifier additions):

- `dotnet/src/ShotAI.Platform/ShotAI.Platform.csproj` gets the same `<RuntimeIdentifiers>win-x64;win-arm64</RuntimeIdentifiers>` so locked-mode restore of a RID-specific publish succeeds (EDGE-PKG-57).
- `AppHostDotNetSearch` affects only the PUBLISHED apphost (Microsoft Learn, MSBuild reference). Developer builds under `bin\` keep the default search; nothing in CI may treat a `bin\` exe as evidence for INV-PKG-17.
- `App.xaml` is built as a `Page`, not an `ApplicationDefinition`, so the SDK does not generate a second `Main` next to `Program.Main` (03 7.4.1 owns the startup body; the scaffold today still has `StartupUri="MainWindow.xaml"` in `dotnet/src/ShotAI.App/App.xaml:4` and no `Program` class).
- `.gitignore` gains `dotnet/artifacts/` (the publish, MSI and native output roots of 7.2.2, 7.4.1 and 7.7.3) and `dotnet/federation.local.json` (08 7.4); neither is ignored today (`.gitignore` ignores only `dotnet/**/bin/`, `dotnet/**/obj/`, `dotnet/**/TestResults/`).

#### 7.2.2 Publish commands

From `dotnet/`, per architecture `<rid>` in `win-x64`, `win-arm64`:

```
dotnet publish src/ShotAI.App/ShotAI.App.csproj -c Release -r <rid> --self-contained false \
  -p:FileVersion=<X.Y.B.0> -p:ContinuousIntegrationBuild=true -o artifacts/publish/<rid>
```

The internal variant adds `-p:ShotAIFederationFile=<path>` (08 7.4); the public variant passes `-p:ShotAIFederationFile=` (an explicitly EMPTY global property). 08's `ShotAI.App.csproj` fragment picks up `dotnet/federation.local.json` or `src/main/entra/federation.local.json` automatically when the property is empty and a file exists; a global property set on the command line cannot be reassigned by the project ("Global properties cannot" be reset in a project, Microsoft Learn, MSBuild properties), so the empty value disables that fallback even on a maintainer's machine that has the file (INV-PKG-14). `verify-payload --public` remains the backstop. Both architectures publish on an x64 Windows runner (ReadyToRun cross-compiles for arm64); the ARM64 payload is then tested on an ARM64 runner (7.11.2).

#### 7.2.3 Payload layout (`%ProgramFiles%\shotAI\`)

| Path | Source | Notes |
|---|---|---|
| `shotAI.exe` | apphost, `ApplicationIcon` `assets/shotAI_icon.ico`, manifest 7.9.3 | signed |
| `shotAI.dll`, `ShotAI.Core.dll`, `ShotAI.Platform.dll` | first party | signed |
| `shotAI.runtimeconfig.json`, `shotAI.deps.json` | SDK | INV-PKG-5, INV-PKG-18 checks |
| NuGet assemblies (MVVM toolkit, DI, logging, Open XML SDK, Anthropic, MSAL, WebView2 managed) | NuGet | keep their signatures only if excluded from ReadyToRun (EDGE-PKG-47); unsigned ones per Q-PKG-3 |
| `shotai_avif.dll` | 7.7 | arch-specific, signed |
| `msalruntime*.dll`, `WebView2Loader.dll` | NuGet `runtimes\<rid>\native` flattened by RID publish | Microsoft-signed; arch checked |
| `Fonts\Archivo.ttf`, `Fonts\OFL.txt`, `Fonts\static\*.ttf` plus `Fonts\static\OFL.txt` | the variable `Archivo.ttf` and its `OFL.txt` from `src/renderer/fonts/` until the cutover cleanup PR moves them to `dotnet/assets/fonts/`; the upstream static instances from `dotnet/assets/fonts/static/` with `SOURCES.md` (06 Q-HOME-2 default, ARCHITECTURE 2.5, 3.3) | 10 INV-INFRA-31, INV-INFRA-32; the folder is `IAppPaths.FontsDirectory` (`AppContext.BaseDirectory\Fonts`, ARCHITECTURE 10.2) |
| `THIRD-PARTY-NOTICES.txt`, `LICENSE.txt` (MIT) | generated and repo `LICENSE` | INV-PKG-29 |
| No `*.pdb` | | symbols ship as a release asset (Q-PKG-11) |

#### 7.2.4 Payload verification (`ShotAI.Release verify-payload`)

`PayloadVerifier.Verify(string dir, TargetArch arch, bool publicBuild) : IReadOnlyList<string>` returns problems (empty means pass). Rules, each a message:

1. Required files present: `shotAI.exe`, `shotAI.dll`, `shotAI.runtimeconfig.json`, `shotAI.deps.json`, `ShotAI.Core.dll`, `ShotAI.Platform.dll`, `shotai_avif.dll`, `Fonts\Archivo.ttf`, `Fonts\OFL.txt`, `THIRD-PARTY-NOTICES.txt`, `LICENSE.txt`, at least one `msalruntime*.dll`, `WebView2Loader.dll`. Message `missing: <relative path>`.
2. Every `Archivo*.ttf` anywhere has an `OFL.txt` in the same folder. `font without OFL.txt: <folder>`.
3. Forbidden: any `*.node`, `element_locator.dll`, `tessdata\`, `*.pdb`, `federation.local.json`, `*.local.json`, `app.asar`, `resources\`. `forbidden: <path>`.
4. For every `*.exe` and `*.dll`: parse the PE header (DOS `e_lfanew` at 0x3C, `PE\0\0`, `IMAGE_FILE_HEADER.Machine`); IL-only assemblies (`IMAGE_COR20_HEADER` present with `COMIMAGE_FLAGS_ILONLY` and machine `0x014C`) are allowed in both payloads; otherwise machine must be `0x8664` for x64 and `0xAA64` for arm64. ReadyToRun images carry the target machine and must match. `wrong machine 0x<hex>: <path>`.
5. `shotAI.runtimeconfig.json`: 7.2.1 checks (INV-PKG-5, INV-PKG-18).
6. `publicBuild`: `shotAI.dll` has no manifest resource named `ShotAI.Federation.Baked.json` (read with `System.Reflection.Metadata`, platform-neutral). `baked federation in a public build`.

Platform-neutral (Linux-testable with synthetic files). The Authenticode half of INV-PKG-7 is a PowerShell step on Windows (7.11).

### 7.3 Version scheme

**Grammar.** `Version` in `Directory.Build.props` is `X.Y.Z` or `X.Y.Z-stage.N`, `stage` in `alpha`, `beta`, `rc`; `X`, `Y`, `Z`, `N` are non-negative decimal integers without leading zeros; `X <= 255`, `Y <= 255`, `Z <= 64`, `N <= 299`; native requires `X >= 2`. Tag = `v` + `Version`.

**Precedence.** SemVer 2.0: compare `(X, Y, Z)` numerically; a version with a prerelease is lower than the same `X.Y.Z` without; between prereleases of the same `X.Y.Z`, `alpha < beta < rc`, then `N` numerically.

**MSI version.** `stageBase(alpha) = 0`, `stageBase(beta) = 300`, `stageBase(rc) = 600`.

```
B = Z * 1000 + (IsPrerelease ? stageBase(stage) + N : 999)
MsiProductVersion = $"{X}.{Y}.{B}"
FileVersion       = $"{X}.{Y}.{B}.0"
```

| Version | MSI ProductVersion |
|---|---|
| `2.0.0-alpha.0` | `2.0.0` |
| `2.0.0-alpha.1` | `2.0.1` |
| `2.0.0-alpha.12` | `2.0.12` |
| `2.0.0-beta.1` | `2.0.301` |
| `2.0.0-rc.2` | `2.0.602` |
| `2.0.0` | `2.0.999` |
| `2.0.1-beta.1` | `2.0.1301` |
| `2.0.1` | `2.0.1999` |
| `2.1.0-alpha.0` | `2.1.0` |
| `2.1.0` | `2.1.999` |
| `2.64.64` | `2.64.64999` |

Monotonicity proof sketch: within one `X.Y`, `B` orders first by `Z` (the stage term is at most 999 < 1000), then by stage (bases 300 apart with `N <= 299`), then `N`, then final (999) above every prerelease of the same `Z`. Across `X.Y`, Windows Installer compares the first two fields first. IMPROVEMENT (no Electron analog).

**Display.** About, logs and the User-Agent show `Version` (10); "Installed apps" shows the MSI ProductVersion (Windows Installer writes `DisplayVersion` from it). The ARP DisplayName stays `shotAI`.

**Shortcut name.** `ShortcutName = IsPrerelease ? "shotAI Preview" : "shotAI"` (EDGE-PKG-31).

**Asset names.** `shotAI-{Version}-x64.msi`, `shotAI-{Version}-arm64.msi`, `shotAI-{Version}-symbols.zip`, `SHA256SUMS.txt`, `THIRD-PARTY-NOTICES.txt`.

**GitHub flags.** Prerelease: `--prerelease --latest=false`. Final: `--latest`. Electron release after 2.0.0 GA: `--latest=false` (INV-PKG-9).

### 7.4 MSI authoring

#### 7.4.1 Project

`dotnet/installer/ShotAI.Installer.wixproj` using the `WixToolset.Sdk` MSBuild SDK and `WixToolset.UI.wixext` only if a UI is kept (default: minimal UI, `WixUI_Minimal` is not required because Intune installs are silent; a manual double-click shows the standard Windows Installer progress dialog). Not part of `ShotAI.slnx` (INV-PKG-22). Built with:

```
dotnet build installer/ShotAI.Installer.wixproj -c Release -p:Platform=<x64|arm64> \
  -p:PayloadDir=artifacts/publish/<rid> -p:ProductVersion=<X.Y.B> -p:SemVer=<Version> \
  -p:ShortcutName="<name>" -o artifacts/msi/<rid>
```

WiX package versions go in `Directory.Packages.props` (SDK version in `global.json` `msbuild-sdks` or the project `Sdk` attribute, per the WiX version chosen; Q-PKG-1).

MSBuild properties do not reach the WiX preprocessor on their own. The `.wixproj` maps each one explicitly: `<DefineConstants>ProductVersion=$(ProductVersion);SemVer=$(SemVer);ShortcutName=$(ShortcutName);PayloadDir=$(PayloadDir);RepoRoot=$(RepoRoot)</DefineConstants>`, and `Package.wxs` references them as `$(var.Name)` (the shorter `$(Name)` form used below is accepted by WiX v4 and later, UNVERIFIED; use `$(var.Name)` if the build rejects it). `$(RepoRoot)` comes from `dotnet/Directory.Build.props:10` and ends with a separator. A value containing `;` (none here) would need escaping. `ShortcutName` contains a space for prereleases (`shotAI Preview`) and must be quoted on the command line as shown.

#### 7.4.2 `Package.wxs` (normative content, WiX v5 or later syntax; the `Files` harvesting element does not exist in WiX v4, UNVERIFIED which minor version introduced it)

```xml
<Wix xmlns="http://wixtoolset.org/schemas/v4/wxs">
  <Package Name="shotAI" Manufacturer="LFI" Version="$(ProductVersion)"
           UpgradeCode="{PINNED-UPPERCASE-GUID}" Scope="perMachine"
           InstallerVersion="500" Compressed="yes" Language="1033">
    <SummaryInformation Description="shotAI $(SemVer)" Manufacturer="LFI" />
    <MajorUpgrade Schedule="afterInstallValidate" AllowSameVersionUpgrades="yes"
                  DowngradeErrorMessage="A newer version of shotAI is already installed." />
    <MediaTemplate EmbedCab="yes" />

    <Property Id="SHOTAI_OSBUILD">
      <RegistrySearch Root="HKLM" Key="SOFTWARE\Microsoft\Windows NT\CurrentVersion"
                      Name="CurrentBuildNumber" Type="raw" Bitness="always64" />
    </Property>
    <Launch Condition="Installed OR (SHOTAI_OSBUILD AND SHOTAI_OSBUILD &gt;= 19041)"
            Message="shotAI requires Windows 10 version 2004 (build 19041) or later." />

    <Icon Id="shotAI.ico" SourceFile="$(RepoRoot)assets\shotAI_icon.ico" />
    <Property Id="ARPPRODUCTICON" Value="shotAI.ico" />
    <Property Id="ARPNOMODIFY" Value="1" />
    <Property Id="ARPURLINFOABOUT" Value="https://github.com/Armadillon44/shotAI" />
    <Property Id="DESKTOPSHORTCUT" Value="0" Secure="yes" />

    <StandardDirectory Id="ProgramFiles64Folder">
      <Directory Id="INSTALLFOLDER" Name="shotAI" />
    </StandardDirectory>

    <ComponentGroup Id="Payload" Directory="INSTALLFOLDER">
      <Files Include="$(PayloadDir)\**" />
    </ComponentGroup>

    <StandardDirectory Id="ProgramMenuFolder">
      <Component Id="StartMenuShortcut">
        <Shortcut Id="StartMenu" Name="$(ShortcutName)" Target="[INSTALLFOLDER]shotAI.exe"
                  WorkingDirectory="INSTALLFOLDER" Icon="shotAI.ico" />
        <RegistryValue Root="HKLM" Key="SOFTWARE\LFI\shotAI" Name="StartMenuShortcut"
                       Type="integer" Value="1" KeyPath="yes" />
      </Component>
    </StandardDirectory>
    <StandardDirectory Id="DesktopFolder">
      <Component Id="DesktopShortcut" Condition="DESKTOPSHORTCUT = 1">
        <Shortcut Id="Desktop" Name="$(ShortcutName)" Target="[INSTALLFOLDER]shotAI.exe"
                  WorkingDirectory="INSTALLFOLDER" Icon="shotAI.ico" />
        <RegistryValue Root="HKLM" Key="SOFTWARE\LFI\shotAI" Name="DesktopShortcut"
                       Type="integer" Value="1" KeyPath="yes" />
      </Component>
    </StandardDirectory>

    <Feature Id="Main">
      <ComponentGroupRef Id="Payload" />
      <ComponentRef Id="StartMenuShortcut" />
      <ComponentRef Id="DesktopShortcut" />
    </Feature>
  </Package>
</Wix>
```

Rules pinned by `PackageSourceTests` (Linux, text and XML parse of `Package.wxs`):

| Rule | Class |
|---|---|
| `Scope="perMachine"`, `ProgramFiles64Folder`, directory name `shotAI` | IMPROVEMENT (per-user Squirrel before) |
| `UpgradeCode` equals the pinned constant in the test | REQUIRED stability (INV-PKG-12) |
| `MajorUpgrade` present with the exact downgrade message, `Schedule="afterInstallValidate"` and `AllowSameVersionUpgrades="yes"` | IMPROVEMENT (EDGE-PKG-46). `afterInstallValidate` removes the old product completely before the new files are copied, so Windows Installer's file-version replacement rules never keep an old file (dev builds have `FileVersion` `0.0.0.0`) |
| No `CustomAction` that runs `shotAI.exe`; no `util:QtExecCmdLine`, no `WixShellExec` | REQUIRED (INV-PKG-4) |
| No `RegistryValue` or `RegistryKey` under `SOFTWARE\Policies` | REQUIRED (INV-PKG-3) |
| No `Permission`, `PermissionEx`, `util:PermissionEx` | REQUIRED (INV-PKG-15) |
| No `AppDataFolder`, `LocalAppDataFolder`, `PersonalFolder`, `util:RemoveFolderEx` | REQUIRED (INV-PKG-2) |
| No `ExePackage`, no `Chain` (this is not a bundle) | REQUIRED (INV-PKG-31) |
| `ARPPRODUCTICON` references `assets\shotAI_icon.ico` | REQUIRED intent (EDGE-PKG-5) |
| The only HKLM registry writes are the two keypath values under `SOFTWARE\LFI\shotAI` | IMPROVEMENT |

The two `HKLM\SOFTWARE\LFI\shotAI` values exist only because a per-machine shortcut component needs a keypath; the app never reads them.

#### 7.4.3 Architecture

Built twice: `-p:Platform=x64` (template summary `x64`) and `-p:Platform=arm64` (template summary `Arm64`, which requires a database schema of 500 or higher, satisfied by `InstallerVersion="500"`). Same `UpgradeCode` for both, so an ARM64 MSI upgrades an x64 install on the same ARM64 device (a device moved from the x64 to the ARM64 assignment converges). The ARM64 MSI never installs on an x64 PC: Windows Installer does not process a package whose Template Summary platform does not match (EDGE-PKG-55). Whether the x64 MSI should refuse to install on ARM64 Windows is Q-PKG-20 (default: allow on Windows 11 on Arm; Intune requirement rules keep ARM64 devices on the ARM64 MSI).

#### 7.4.4 Launch condition

`VersionNT` is 603 on Windows 10 (EDGE-PKG-27), so the condition reads `CurrentBuildNumber` (a `REG_SZ` such as `19045`; a `raw` search returns a `REG_SZ` unprefixed). Windows Installer "Conditional Statement Syntax" (Microsoft Learn): "Comparison of an integer with a string or property value that cannot be converted to an integer is always msiEvaluateConditionFalse, except for the comparison operator "<>"", so a property that CAN be converted is compared as an integer, and a missing property ("Nonexistent property values are treated as empty strings") makes `>=` false, which blocks the install (fail closed); the `SHOTAI_OSBUILD AND` term only makes that explicit. Still measured on a Windows 10 1909 VM and a 2004 VM (AC-PKG-12, Q-PKG-6). `Installed OR` keeps uninstall and repair possible after an OS change. Message (user-visible, exact): `shotAI requires Windows 10 version 2004 (build 19041) or later.`

### 7.5 Prerequisites and Intune configuration

#### 7.5.1 Prerequisites

| Prerequisite | Needed by | Kind | Deployment | Detection |
|---|---|---|---|---|
| .NET 10 Desktop Runtime, matching architecture | the app to start | hard | Intune Win32 app per architecture: `windowsdesktop-runtime-10.0.<patch>-win-<x64\|arm64>.exe /install /quiet /norestart`; return codes 0 and 3010 success | script: exits 0 and writes `found` when a folder `%ProgramFiles%\dotnet\shared\Microsoft.WindowsDesktop.App\10.*` exists (Arm64 runtime on Arm64, x64 runtime on x64; for x64 on Arm64 the root is `%ProgramFiles%\dotnet\x64\`); or registry `HKLM\SOFTWARE\dotnet\Setup\InstalledVersions\<arch>\sharedfx\Microsoft.WindowsDesktop.App` has a value name starting `10.` (verify, Q-PKG-26) |
| Servicing of the runtime | security patches | | Microsoft Update (not Windows Update): Windows Update for Business must allow "updates for other Microsoft products"; the value `BlockMU` (`REG_DWORD` `1`) must not be set under `HKLM\SOFTWARE\Microsoft\.NET` or `HKLM\SOFTWARE\Microsoft\.NET\10.0`. A serviced runtime removes the previous patch after installing the new one, which can interrupt a running shotAI (EDGE-PKG-49; `RemovePreviousVersion` `nextSession` defers it) | Microsoft Learn, Install .NET on Windows |
| WebView2 Evergreen Runtime | PDF export only (09) | soft | normally present (Windows 11, Microsoft 365 Apps installs it); otherwise Intune Win32 app `MicrosoftEdgeWebView2RuntimeInstaller<X64\|ARM64>.exe /silent /install` (standalone) or the bootstrapper `MicrosoftEdgeWebview2Setup.exe /silent /install`, run elevated for a per-machine install | registry value `pv` greater than `0.0.0.0` under the client key (section 3) |
| Windows OCR recognizer | auto-redact only (04) | soft | present on en-US installs; otherwise the capability `Language.OCR~~~en-US~0.0.1.0` via Intune (script `Add-WindowsCapability -Online -Name 'Language.OCR~~~en-US~0.0.1.0'`) or Features on Demand policy | `Get-WindowsCapability -Online -Name 'Language.OCR*'` state `Installed` |

#### 7.5.2 Intune Win32 app for shotAI (one per architecture)

| Field | Value |
|---|---|
| Name | `shotAI (x64)`, `shotAI (ARM64)` |
| Publisher | `LFI` |
| App version | `Version` (semver) |
| Content | `shotAI-<Version>-<arch>.msi` wrapped with the Microsoft Win32 Content Prep Tool |
| Install command | `msiexec /i "shotAI-<Version>-<arch>.msi" /qn /norestart` |
| Uninstall command | `msiexec /x {<ProductCode>} /qn /norestart` (filled by Intune from the MSI) |
| Allow available uninstall | No (a Win32 app with dependencies shows no Company Portal uninstall anyway) |
| Install behavior | System |
| Device restart behavior | Determine behavior based on return codes |
| Return codes | 0 success, 1707 success, 3010 soft reboot, 1641 hard reboot, 1618 retry |
| Requirements | OS architecture x64 (or ARM64); minimum OS Windows 10 2004 |
| Detection | MSI product code, product version check Yes, greater than or equal to `X.Y.B` |
| Dependencies | `.NET 10 Desktop Runtime (<arch>)`, Automatically install: Yes. WebView2 optional as a dependency only where the image lacks it. |
| Supersedence | supersedes the previous `shotAI (<arch>)` version, Uninstall previous version: No. "Superseding apps don't get automatic targeting": each new version needs its own assignment. At most 10 nodes per supersedence relationship, so old versions are pruned from the chain (Microsoft Learn, Win32 app supersedence) |
| Assignment | pilot group Required during the pilot; all users or devices at GA |

The ADMX and the federation policy profile are unchanged (`Intune/Windows/README.md`). Nothing in the Intune app configures policy (INV-PKG-3). Dependencies apply only to installation: uninstalling shotAI leaves the Desktop Runtime in place (Microsoft Q&A; intended, other apps may share it). The Intune notes also carry EDGE-PKG-49 (runtime servicing) and EDGE-PKG-51 (no ACG rule for `shotAI.exe`).

### 7.6 Signing (Azure Artifact Signing)

| Item | Specification |
|---|---|
| Service | Azure Artifact Signing (formerly Trusted Signing), Public Trust certificate profile, organization identity validation for LFI. Eligibility per the Artifact Signing quickstart: Public Trust certificates "are available to organizations in the United States, Canada, the European Union, the United Kingdom, Australia, New Zealand, Japan, South Korea, Singapore, Switzerland, Norway, and Israel"; the three-year verifiable history requirement appears in Microsoft Q&A answers, not on the quickstart page (UNVERIFIED as current; Q-PKG-2). Identity validation can be completed only in the Azure portal and needs the Identity Verifier role |
| Authentication | GitHub OIDC federated credential on an Entra app registration (no client secret), `azure/login` with `client-id`, `tenant-id`, `subscription-id` from the protected `release` environment; the identity holds the `Artifact Signing Certificate Profile Signer` role (older name `Trusted Signing Certificate Profile Signer`) on the certificate profile scope only |
| Tool | `azure/artifact-signing-action` (linked from the Artifact Signing "signing integrations" page; the MSIX signing guide still names `azure/trusted-signing-action`; pin the SHA of the repository actually used), or SignTool with the Artifact Signing dlib (`/dlib <path>\Azure.CodeSigning.Dlib.dll /dmdf metadata.json`, dlib from NuGet `Microsoft.ArtifactSigning.Client` or winget `Microsoft.Azure.ArtifactSigningClientTools`; the x64 SignTool needs the x64 dlib); "Standard SignTool syntax does **not** work with Artifact Signing without this package" |
| Digest and timestamp | `/fd SHA256 /tr http://timestamp.acs.microsoft.com /td SHA256`, with `/td` after `/tr` (SignTool: "If the `/td` switch is declared before the `/tr` switch, the timestamp that is returned is from the SHA1 algorithm") (INV-PKG-32, section 3) |
| Files, pass 1 | `shotAI.exe`, `shotAI.dll`, `ShotAI.Core.dll`, `ShotAI.Platform.dll`, `shotai_avif.dll` in each payload, plus every PE whose `Get-AuthenticodeSignature` status is `NotSigned` (Q-PKG-3) |
| Files, pass 2 | each MSI |
| Never | re-sign a file that already has a valid signature from another publisher; sign in PR CI |
| Verification | `Get-AuthenticodeSignature` for every PE in the payload and each MSI: `Status -eq 'Valid'`, `TimeStamperCertificate -ne $null`; first-party subject equals the configured publisher; plus `signtool verify /pa /all` on the MSI |

SmartScreen: signing does not grant instant reputation ("typically several weeks and hundreds of clean installs", MSIX signing guide); the guide ties reputation to the verified identity while the Artifact Signing FAQ says the prompt stops "once the file hash has sufficient download history", so each release may start cold (UNVERIFIED which applies). Intune installs are not affected. A Private Trust profile (validated only against the Azure tenant) is the fallback for internal-only signing if Public Trust validation fails, at the cost of trust only where IT deploys the root (Q-PKG-2). IMPROVEMENT (Electron unsigned).

### 7.7 Native dependency provenance (`shotai_avif.dll`)

#### 7.7.1 Sources

`dotnet/native/avif/pins.json`:

```json
{
  "libavif": { "repo": "https://github.com/AOMediaCodec/libavif", "tag": "<vX.Y.Z>", "commit": "<40 hex>", "archiveSha256": "<64 hex>" },
  "libaom":  { "repo": "https://aomedia.googlesource.com/aom", "tag": "<vX.Y.Z>", "commit": "<40 hex>", "archiveSha256": "<64 hex>" }
}
```

Versions are chosen in the implementing PR (libavif 1.x per 09 7.6). The shim source `dotnet/native/avif/shotai_avif.c` and `CMakeLists.txt` are committed; binaries never are (INV-PKG-19).

#### 7.7.2 Build (workflow job `native-avif`, `windows-latest`)

1. Download each archive by commit, verify `archiveSha256` (fail on mismatch), extract to the runner temp directory (EDGE-PKG-13).
2. libaom, encoder only: CMake with `-A x64` or `-A ARM64`, `-DCMAKE_MSVC_RUNTIME_LIBRARY=MultiThreaded` (static CRT, no VC++ redistributable dependency), `-DCONFIG_AV1_DECODER=0 -DENABLE_DOCS=0 -DENABLE_EXAMPLES=0 -DENABLE_TESTS=0 -DENABLE_TOOLS=0`, assembly per Q-PKG-13 (NASM for x64).
3. libavif static: `-DAVIF_CODEC_AOM=SYSTEM` against step 2, `-DBUILD_SHARED_LIBS=OFF`, no decoder apps, static CRT.
4. Shim DLL: link both statically; linker flags `/DYNAMICBASE /HIGHENTROPYVA /NXCOMPAT /guard:cf`, plus `/CETCOMPAT` on x64 only ("This switch is currently only applicable to the x64 architecture", Microsoft Learn `/CETCOMPAT`; the process is CET-enabled by the .NET apphost, 7.2.1); exports exactly `shotai_avif_encode`, `shotai_avif_free`, `shotai_avif_version` (09 7.6); `dumpbin /exports` in CI must list exactly these three, and `dumpbin /dependents` must list only system DLLs (`KERNEL32.dll` and API sets).
5. Record `shotai_avif_version()` output, compiler version, pins and the DLL SHA-256 in `avif-provenance.json`; attest the DLL with `actions/attest-build-provenance`.
6. Cache the result keyed by `hashFiles('dotnet/native/avif/**')` plus the runner image version; the release workflow always rebuilds (no cache) and signs.

#### 7.7.3 Consumption

The App project copies `artifacts/native/<rid>/shotai_avif.dll` into the publish output when present (`Condition="Exists(...)"`), so Linux builds and developer builds without it still build; 09's `LibavifEncoder` then falls back to PNG or JPEG with its warn line. The `package` job and the release fail if it is missing (`verify-payload` rule 1).

#### 7.7.4 Notices

`THIRD-PARTY-NOTICES.txt` is generated by `ShotAI.Release notices` from `Directory.Packages.props` (skipping build-only packages: `PrivateAssets="all"` in consuming projects, test packages), the NuGet cache licence metadata, `pins.json` (libavif `LICENSE`, libaom `LICENSE` and `PATENTS`), and a fixed Archivo section pointing at `Fonts\OFL.txt`. `--check` fails when the committed file is stale (same pattern as the brand generator).

### 7.8 Supply chain (NuGet)

| Control | Setting | Class |
|---|---|---|
| Lock files | `RestorePackagesWithLockFile=true`; `packages.lock.json` committed per project; CI `RestoreLockedMode=true` | IMPROVEMENT (S1 analog) |
| Sources | `dotnet/nuget.config` with `<clear/>`, one source `https://api.nuget.org/v3/index.json`, `<packageSourceMapping><packageSource key="nuget.org"><package pattern="*" /></packageSource></packageSourceMapping>` | IMPROVEMENT |
| Signature validation | `signatureValidationMode` `require` with trusted signer `nuget.org` repository certificate (verify every package is repository-signed; Q-PKG-29) | IMPROVEMENT |
| Audit | `NuGetAudit=true`, `NuGetAuditMode=all` (Q-PKG-14 for severity in PR CI) | IMPROVEMENT |
| Central versions | `Directory.Packages.props` only (existing rule) | REQUIRED |
| Dependabot | `.github/dependabot.yml` for `nuget` in `/dotnet` and `github-actions` weekly | IMPROVEMENT |

### 7.9 Runtime hardening owned by packaging

#### 7.9.1 DLL search order (INV-PKG-16)

`ShotAI.App.Program.Main` (the WPF app uses an explicit `Main`; 03 owns the rest of startup):

```csharp
[STAThread]
public static int Main(string[] args)
{
    // Must precede any native load, including WPF's own (PresentationNative, wpfgfx).
    // Platform wraps PInvoke.SetDefaultDllDirectories(LOAD_LIBRARY_FLAGS.LOAD_LIBRARY_SEARCH_DEFAULT_DIRS)
    // because CsWin32's generated PInvoke class is internal to ShotAI.Platform.
    if (!ShotAI.Platform.DllSearchHardening.Apply())
        Environment.FailFast("SetDefaultDllDirectories failed");   // cannot happen on 19041+
    ...
}
```

CsWin32 `NativeMethods.txt` gains `SetDefaultDllDirectories` (and `AddDllDirectory` if EDGE-PKG-48 needs it). CsWin32 generates `internal` types by default, so `PInvoke` in `ShotAI.Platform` is not visible from `ShotAI.App`: the call is wrapped in a public Platform method, `ShotAI.Platform.DllSearchHardening.Apply()` (returns `bool`), and `Main` calls that; App does not get its own `NativeMethods.txt` for this one API. Calling into `ShotAI.Platform.dll` loads a managed assembly by absolute path from the app directory, which is not a search-order load, so the ordering claim still holds. `LOAD_LIBRARY_SEARCH_DEFAULT_DIRS` means the application directory, `System32` and directories added with `AddDllDirectory`; the current directory and `PATH` are excluded. The shared framework folders (Microsoft.NETCore.App and Microsoft.WindowsDesktop.App) are NOT in that set; see EDGE-PKG-48 and Q-PKG-30. Every assembly with P/Invoke or `LibraryImport` (Platform, App) carries `[assembly: DefaultDllImportSearchPaths(DllImportSearchPath.System32 | DllImportSearchPath.AssemblyDirectory)]`. 09's `NativeLibrary.TryLoad("shotai_avif", ..., DllImportSearchPath.AssemblyDirectory, ...)` is consistent. IMPROVEMENT.

#### 7.9.2 Runtime and injection

`AppHostDotNetSearch=Global` (INV-PKG-17), `StartupHookSupport=false` (INV-PKG-18). The .NET apphost enables CET shadow stack by default (.NET 9 and later; Microsoft Learn, publishing overview and "CET supported by default"); `CETCompat` must never be set to `false`. No Arbitrary Code Guard policy may target the process (EDGE-PKG-51). IMPROVEMENT.

#### 7.9.3 `app.manifest` addition

```xml
<trustInfo xmlns="urn:schemas-microsoft-com:asm.v3">
  <security><requestedPrivileges>
    <requestedExecutionLevel level="asInvoker" uiAccess="false" />
  </requestedPrivileges></security>
</trustInfo>
```

The existing `supportedOS` (Windows 10 and 11 GUID), `dpiAware true/pm`, `dpiAwareness PerMonitorV2` and `longPathAware true` stay (`dotnet/src/ShotAI.App/app.manifest:4-17`). REQUIRED (INV-PKG-33).

#### 7.9.4 Install directory

Inherited `Program Files` ACL; the app writes only to `IAppPaths` locations (10 7.4.4, ARCHITECTURE 10.2: `UserDataDirectory`, `SettingsFile`, `LogsDirectory`, `LocalDataDirectory`, the projects folder, `TempDirectory`) and never to `AppContext.BaseDirectory` (INV-PKG-15). The installed fonts are read-only and reached only through `IAppPaths.FontsDirectory` (`AppContext.BaseDirectory\Fonts`); `AppPaths.BrandFontPath()` is `Path.Combine(IAppPaths.FontsDirectory, "Archivo.ttf")` (03 2.10.10, 09 `IBrandFontSource`).

### 7.10 User data and coexistence

#### 7.10.1 Paths

| Data | Native path | Shared with Electron | Owner | Class |
|---|---|---|---|---|
| Settings | `%APPDATA%\shotAI\settings.json` | yes, same file | 10 | REQUIRED |
| Logs | `%APPDATA%\shotAI\logs\shotai.log`, `shotai.old.log` | yes, same files (EDGE-PKG-24) | 10 | REQUIRED |
| API key | `%APPDATA%\shotAI\secrets.dpapi.json` | no (Electron `secrets.json` untouched) | 08 | IMPROVEMENT |
| Corrupt settings backup | `%APPDATA%\shotAI\settings.json.bad` | no | 10 | IMPROVEMENT (10 Q-INFRA-12) |
| Native local data root | `%LOCALAPPDATA%\LFI\shotAI\` (`IAppPaths.LocalDataDirectory`) | no | 10 (`IAppPaths`) | IMPROVEMENT (R-ARCH-13, ARCHITECTURE D-ARCH-2) |
| MSAL cache | `%LOCALAPPDATA%\LFI\shotAI\entra\msal-cache.bin` plus the MSAL extension's lock file (`IAppPaths.LocalDataDirectory` + `entra`) | no | 08 | IMPROVEMENT (R-ARCH-13) |
| WebView2 user data | `%LOCALAPPDATA%\LFI\shotAI\WebView2` (`IAppPaths.LocalDataDirectory` + `WebView2`) | no | 09 | IMPROVEMENT (R-ARCH-13) |
| Projects | `settings.projectsDir`, default `%USERPROFILE%\shotAI Projects` | yes | 01, 10 | REQUIRED |
| Install | `%ProgramFiles%\shotAI` | no | 12 | IMPROVEMENT |
| Electron-only | `secrets.json`, `entra-cache.bin`, `tessdata\`, Chromium entries in `%APPDATA%\shotAI`, `%LocalAppData%\shotai\` | never touched (INV-PKG-24) | | ELECTRON-ONLY |

Rule (R-ARCH-13): new native-only local data goes under `IAppPaths.LocalDataDirectory` (`%LOCALAPPDATA%\LFI\shotAI\`), never under `%LOCALAPPDATA%\shotAI\` (the Squirrel root, EDGE-PKG-22); `settings.json`, the logs and the projects folder stay where the Electron build keeps them. No code composes any of these paths itself; each comes from `IAppPaths` (ARCHITECTURE 10.2). This table matches ARCHITECTURE 10.1.

#### 7.10.2 `settings.json` migration

There is no migration step: the native build reads and writes the same file (10). Constraints for the pilot and rollback (INV-PKG-25): the file is always a JSON object written atomically; unknown keys (including Electron-only keys such as `hasSeenTour` if the native schema ever drops one) are preserved; keys Electron owns keep their type and meaning; native-only keys are additive. Settings already chosen in Electron (projects folder, recents, theme, brand, SOP settings, update check opt-out, name) therefore carry over on first native launch, and the native first-run tour does not show again if `hasSeenTour` is `true` (06). Credentials do not carry over (08: API key re-entered or Microsoft sign-in repeated).

#### 7.10.3 `LegacyInstanceGuard` (App)

Runs in 03's startup order immediately after the native single-instance mutex is acquired and before any settings or project I/O.

```csharp
// File dotnet/src/ShotAI.Platform/Processes/IProcessSnapshot.cs (App references Platform, never the reverse)
namespace ShotAI.Platform.Processes;

public interface IProcessSnapshot
{
    IReadOnlyList<(int Pid, int SessionId, string? ImagePath)> ProcessesNamed(string name);
    int CurrentSessionId { get; }
    int CurrentPid { get; }
}

public sealed class ProcessSnapshot : IProcessSnapshot   // Process.GetProcessesByName + QueryFullProcessImageName
{ /* disposes every Process object it enumerates */ }

// File dotnet/src/ShotAI.App/Startup/LegacyInstanceGuard.cs
namespace ShotAI.App.Startup;

public sealed class LegacyInstanceGuard(IProcessSnapshot processes, string localAppData, ILogger log)
{
    // Path.Combine(localAppData, "shotai", "app-"), full path, compared OrdinalIgnoreCase.
    public (int Pid, string Path)? FindRunningElectron();
}
```

The original draft declared `IProcessSnapshot` in `ShotAI.App.Startup` with a Platform implementation, which is a circular project reference (Platform references only Core); it now lives in Platform. The guard runs at 03 step 2a, before the DI container is built (03 EDGE-SHELL-45: early exits happen "before any service, window, timer or background task exists"), so `OnStartup` constructs it directly with `new ProcessSnapshot()`, `Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData)` and the bootstrap logger of 10; it is not resolved from the container and needs no `IAppPaths`. `ProcessSnapshot` opens each process with `PROCESS_QUERY_LIMITED_INFORMATION` (CsWin32 `OpenProcess`, `QueryFullProcessImageName`, `CloseHandle`; `Process.MainModule` is not used because it throws for inaccessible processes) and disposes every `System.Diagnostics.Process` it received. Electron runs several `shotAI.exe` processes (main, renderers, GPU) from the same `app-<version>` folder, so any one hit is enough; the Squirrel stub `<root>\shotAI.exe` (outside `app-`) exits within seconds and is deliberately not matched.

`FindRunningElectron`: for each process named `shotAI` with `SessionId == CurrentSessionId` and `Pid != CurrentPid`, take `ImagePath` (`QueryFullProcessImageName`, access denied yields `null`, skipped); if `Path.GetFullPath(ImagePath)` starts with `Path.Combine(localAppData, "shotai", "app-")` (`OrdinalIgnoreCase`), return it. On a hit: log info `legacy shotAI 1.x is running (pid <pid>, <path>); exiting.`, show a `MessageBox` (owner none, icon Information) with caption `shotAI` and text `The previous version of shotAI is still running. Close it first, so the two versions don't edit the same projects at the same time.` and button OK, then exit with code 0. In a self-test mode the notice is replaced by the stderr line `legacy shotAI 1.x is running (pid <pid>, <path>); self-test refused.` and exit code 2 (EDGE-PKG-50). Nothing is written. IMPROVEMENT (INV-PKG-26; Q-PKG-8 for warn-only). The guard (and its `MessageBox.Show` allowlist entry, ARCHITECTURE 14.9) stays through the S4 cutover cleanup PR and is removed at stage S5, 90 days after GA, once IT reports no Electron installs remain (7.13, Q-PKG-22, ARCHITECTURE 13.4).

### 7.11 CI workflows

#### 7.11.1 `ci.yml` (Electron)

Unchanged until the cutover cleanup PR, which deletes it with the Electron tree. REQUIRED.

#### 7.11.2 `dotnet.yml` (extended)

```yaml
name: native (.NET)
on:
  push:
    branches: [main]
    paths: ['dotnet/**', 'contract/**', 'assets/**', 'src/renderer/fonts/**', '.gitattributes', '.github/workflows/dotnet.yml']
  pull_request:
    paths: ['dotnet/**', 'contract/**', 'assets/**', 'src/renderer/fonts/**', '.gitattributes', '.github/workflows/dotnet.yml']
concurrency: { group: dotnet-${{ github.ref }}, cancel-in-progress: true }
permissions: { contents: read }
env: { DOTNET_NOLOGO: '1', DOTNET_CLI_TELEMETRY_OPTOUT: '1' }
defaults: { run: { working-directory: dotnet } }

jobs:
  linux:
    name: brand check · build all · test core (Linux)
    runs-on: ubuntu-latest
    steps:
      - uses: actions/checkout@v4
      - uses: actions/setup-dotnet@v4
        with: { dotnet-version: '10.0.x' }
      - name: Brand table is current            # INV-PKG-21, spec 10
        run: dotnet run --project tools/ShotAI.GenBrand -c Release -- --check
      - name: Third-party notices are current   # INV-PKG-29
        run: dotnet run --project tools/ShotAI.Release -c Release -- notices --check
      - name: Build
        run: dotnet build ShotAI.slnx -c Release
      - name: Test (core)
        run: dotnet test --project tests/ShotAI.Core.Tests/ShotAI.Core.Tests.csproj -c Release --no-build

  native-avif:
    name: build shotai_avif (${{ matrix.arch }})
    runs-on: windows-latest
    strategy: { fail-fast: false, matrix: { arch: [x64, arm64] } }
    steps: [ checkout, cache keyed per 7.7.2, build per 7.7.2, upload artifact native-avif-${{ matrix.arch }} ]

  windows:
    name: build + test (Windows ${{ matrix.arch }})
    needs: native-avif
    strategy:
      fail-fast: false
      matrix:
        include:
          - { arch: x64,   runner: windows-latest }
          - { arch: arm64, runner: windows-11-arm }   # Q-PKG-12
    runs-on: ${{ matrix.runner }}
    steps:
      - checkout, setup-dotnet 10.0.x, download native-avif-${{ matrix.arch }} to artifacts/native/win-${{ matrix.arch }}
      - run: dotnet build ShotAI.slnx -c Release
      - run: dotnet test --solution ShotAI.slnx -c Release --no-build

  package:
    name: publish · MSI · install smoke (${{ matrix.arch }})
    needs: windows
    strategy: { fail-fast: false, matrix: same include as windows }
    runs-on: ${{ matrix.runner }}
    steps:
      - publish per 7.2.2 (public variant, unsigned)
      - run: dotnet run --project tools/ShotAI.Release -- verify-payload artifacts/publish/win-${{ matrix.arch }} --arch ${{ matrix.arch }} --public
      - build the MSI per 7.4.1
      - smoke (PowerShell, 7.11.4)
      - upload artifact msi-${{ matrix.arch }} (retention 7 days)
```

Unchanged rules: working directory `dotnet`; do not make these required checks while path filters exist (EDGE-PKG-35). `assets/**` is added because the icon is packaged; `.gitattributes` and `src/renderer/fonts/**` (the fonts the payload ships until cutover, 7.2.3) are added for the same reason. PR CI never signs. The `native-avif` job does not need the repository to contain `dotnet/native/avif/` yet: until 09's shim lands the job is skipped with `if: hashFiles('dotnet/native/avif/pins.json') != ''`, and `verify-payload` rule 1 stays off for `shotai_avif.dll` only while that file is absent (the release workflow never relaxes it). The smoke step installs the Desktop Runtime the production way (EDGE-PKG-56). `RestoreLockedMode` is on in every job because GitHub Actions sets `CI=true` (7.2.1).

#### 7.11.3 `release.yml` (new)

Trigger: `push: tags: ['v2.*']` and `workflow_dispatch` with input `tag`. Top-level `permissions: {}`. Jobs:

| Job | Runs on | Permissions | Environment | Steps |
|---|---|---|---|---|
| `check` | ubuntu-latest | `contents: read` | | checkout with tags (`fetch-depth: 0`); `ShotAI.Release check-tag <tag> --props Directory.Build.props --existing-tags "$(git tag -l 'v2.*' \| grep -vxF "<tag>")"` (the tag being released is excluded, EDGE-PKG-53); fail if `gh release view <tag> --json isDraft` finds a non-draft release (a "release not found" error is the expected, passing case); for `workflow_dispatch`, check out the tag's commit, never the branch head; print `msi-version`, `prerelease`, `shortcut-name` as job outputs |
| `test` | ubuntu-latest and windows-latest (x64), windows-11-arm | `contents: read` | | same as `dotnet.yml` linux and windows jobs, locked restore, audit as error (`-p:ShotAIStrictAudit=true`, 7.2.1, Q-PKG-14) |
| `native-avif` | windows-latest | `contents: read`, `id-token: write`, `attestations: write` | | 7.7.2 without cache; attest |
| `build` | windows-latest | `contents: read` | | publish both RIDs, public variant; `verify-payload --public`; upload unsigned payloads |
| `build-internal` | windows-latest | `contents: read` | `release` | only if secret `SHOTAI_FEDERATION_JSON` exists; write it to `%RUNNER_TEMP%\federation.json`; preflight (`ShotAI.Release federation-check <file>`, which references `ShotAI.Core` and runs 08's `BakedFederationParser` plus `FederationConfigValidator.Validate`): require the five keys `TenantId`, `AudienceAppId`, `FederationRuleId`, `OrganizationId`, `ServiceAccountId` non-blank, else fail with `internal build: federation secret is incomplete, missing: <keys>`; fail with `internal build: federation secret is invalid: <validator messages>` when the validator rejects it (a present but malformed `ClientAppId` or `WorkspaceId` included, EDGE-PKG-60); never print a value; publish with `-p:ShotAIFederationFile=%RUNNER_TEMP%\federation.json`; `verify-payload` (not `--public`); delete the file in an `always()` step |
| `sign` | windows-latest | `contents: read`, `id-token: write` | `release` (required reviewers) | `azure/login` OIDC; sign pass 1 on both payloads (and the internal payloads if present); verify; build MSIs with the WiX project; sign pass 2; verify (7.6); install smoke on x64 (7.11.4); compute `SHA256SUMS.txt`; zip PDBs into `shotAI-<v>-symbols.zip` |
| `smoke-arm64` | windows-11-arm | `contents: read` | | download the signed ARM64 MSI, smoke (7.11.4) |
| `draft-release` | ubuntu-latest | `contents: write` | | `gh release create <tag> --draft --title "shotAI <v>" --notes-file dotnet/installer/release-notes/<v>.md <flags from 7.3> <public assets>`; the internal MSIs are uploaded only as a workflow artifact `internal-msi` with 30-day retention, never to the release (INV-PKG-14) |
| `verify-published` | ubuntu-latest | `contents: read` | | runs on `release: types: [published]` (and `workflow_dispatch`): `gh release view <tag> --json tagName,isPrerelease,isDraft,assets` plus `gh api repos/{owner}/{repo}/releases/latest --jq .tag_name`; fail unless `isDraft=false`, `isPrerelease == prerelease`, the latest tag equals `<tag>` for a final and differs from `<tag>` for a prerelease, and every asset of 7.3 is present. (Whether `gh release view --json` accepts `isLatest`, as `macOS:docs/DISTRIBUTION.md:98` uses, is UNVERIFIED, so the API call is the normative check, EDGE-PKG-58.) |

Third-party actions pinned by full commit SHA with the version in a comment (INV-PKG-28). `release-notes/<v>.md` is committed in the release PR; for `2.0.0` it begins with the 7.12.3 text.

#### 7.11.4 Install smoke (PowerShell, both architectures)

```
$ErrorActionPreference = 'Stop'
function Assert($ok, $msg) { if (-not $ok) { throw "smoke: $msg" } }      # EDGE-PKG-54
$msi = <path>; $log = "$env:RUNNER_TEMP\install.log"
# Production-faithful runtime (EDGE-PKG-56): the official Desktop Runtime installer for the runner arch.
$rt = Start-Process <windowsdesktop-runtime-10.0.x-win-<arch>.exe> -ArgumentList '/install /quiet /norestart' -Wait -PassThru
Assert ($rt.ExitCode -in 0, 3010) "runtime install exit $($rt.ExitCode)"
$settings = "$env:APPDATA\shotAI\settings.json"
$before = if (Test-Path $settings) { (Get-FileHash $settings).Hash } else { $null }
$p = Start-Process msiexec -ArgumentList "/i `"$msi`" /qn /norestart /l*v `"$log`"" -Wait -PassThru
Assert ($p.ExitCode -eq 0) "install exit $($p.ExitCode)"
Assert (Test-Path "$env:ProgramFiles\shotAI\shotAI.exe") 'shotAI.exe missing'
$arp = Get-ChildItem 'HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall' | Get-ItemProperty | Where-Object DisplayName -eq 'shotAI'
Assert (@($arp).Count -eq 1) 'expected exactly one shotAI ARP entry'
Assert ($arp.Publisher -eq 'LFI' -and $arp.DisplayVersion -eq '<X.Y.B>') 'ARP publisher or version'
Assert (Test-Path "$env:ProgramData\Microsoft\Windows\Start Menu\Programs\<ShortcutName>.lnk") 'Start menu shortcut'
Assert (-not (Test-Path "$env:PUBLIC\Desktop\<ShortcutName>.lnk")) 'unexpected desktop shortcut'
$r = Start-Process "$env:ProgramFiles\shotAI\shotAI.exe" -ArgumentList '--selftest' -Wait -PassThru -RedirectStandardOutput "$env:RUNNER_TEMP\st.txt"
Assert ($r.ExitCode -eq 0) "selftest exit $($r.ExitCode)"
Assert ((Get-Content "$env:RUNNER_TEMP\st.txt")[-1] -eq '[selftest] PASS') 'selftest last line'
$after = if (Test-Path $settings) { (Get-FileHash $settings).Hash } else { $null }
Assert ($before -eq $after) 'settings.json changed'                        # 10 AC-INFRA-27
$u = Start-Process msiexec -ArgumentList "/x `"$msi`" /qn /norestart" -Wait -PassThru
Assert ($u.ExitCode -eq 0) "uninstall exit $($u.ExitCode)"
Assert (-not (Test-Path "$env:ProgramFiles\shotAI")) 'install folder left behind'
```

The runtime installer is downloaded from the official .NET release metadata with its published SHA-512 checked before it runs (the same rule as INV-PKG-19 for any fetched binary).

The release smoke also runs the upgrade check: install the previous `v2.*` release MSI of the same architecture (downloaded with `gh release download`), then the new one; exactly one `shotAI` ARP entry remains with the new DisplayVersion; then attempt the old MSI again and expect exit code 1603 with the downgrade message in the log.

### 7.12 Release process

#### 7.12.1 Release state machine (one native version)

| State | Event | Guard | Action | Next state |
|---|---|---|---|---|
| Idea | release PR merged | `Version` bumped, release notes file present | maintainer pushes tag `v<Version>` | Tagged |
| Tagged | `release.yml` starts | tag passes `check-tag` | build, sign, smoke | Building |
| Building | any job fails | | no release; fix forward with a new version (tags are never reused) | Failed |
| Building | all jobs pass | | draft release with assets and flags | Draft |
| Draft | human review | assets, notes and flags correct | publish the draft in the GitHub UI | Published |
| Published | `verify-published` | flags and assets match | none | Verified |
| Published | `verify-published` fails | | fix flags immediately (`gh release edit <tag> --prerelease` or `--latest`) | Published |
| Verified | Intune upload | IT imports the internal or public MSI | new Win32 app superseding the previous | Deployed |

#### 7.12.2 Checklist (manual items)

1. `Version` in `Directory.Build.props` is the intended semver; release notes file exists.
2. For a prerelease: when publishing the draft in the web UI, "Set as a pre-release" is checked and "Set as the latest release" is not (EDGE-PKG-58).
3. For a final: "Set as the latest release" is checked and "Set as a pre-release" is not.
4. After publishing: run `verify-published`; for the first native prerelease also confirm from an Electron 1.3.x machine that "Check for updates" says up to date.
5. Any Electron release after 2.0.0: `gh release create v1.3.N --latest=false` (EDGE-PKG-18).
6. Never delete or edit the assets of `v1.3.*` releases (INV-PKG-27).
7. For `2.0.0`: the README deployment and licence sections and the wiki `Installation` page describe the MSI (2.4.8); the README's Privacy section names the MSAL cache location `%LOCALAPPDATA%\LFI\shotAI\entra\` (R-ARCH-13); the links to `docs/native/PLAN.md` still resolve (Q-PKG-31, resolved: the file exists).
8. The Intune notes shipped to IT include the prerequisites of 7.5.1, EDGE-PKG-49 and EDGE-PKG-51.

#### 7.12.3 2.0.0 release notes opening (exact text)

`shotAI 2.0.0 is the native Windows version of shotAI. On a company-managed PC, you don't need to do anything: IT installs it for you. It installs for all users and needs an administrator and the .NET 10 Desktop Runtime. Your projects and settings carry over; you will need to sign in or enter your API key again.`

### 7.13 Pilot, cutover and rollback

#### 7.13.1 Phases

| Phase | Native | Electron | Update check behavior |
|---|---|---|---|
| S0 Electron only | none | 1.3.x per user | Electron sees latest `v1.3.x` |
| S1 Pilot | `2.0.0-alpha.N`, `2.0.0-beta.N` to the pilot group via Intune (Required) | still installed for everyone, including pilot users | prereleases invisible to Electron (INV-PKG-9); native pilot builds see latest `v1.3.x`, not newer |
| S2 Release candidate | `2.0.0-rc.N` to a wider group | still installed | same |
| S3 GA | `2.0.0` to all (Required), Electron removed (7.13.3) | removed by Intune | Electron (where still present, for example unmanaged PCs) is offered `2.0.0` |
| S4 Cleanup | main contains only native | Electron tree, `ci.yml`, npm files deleted in one PR; `dotnet.yml` path filters removed and its jobs made required; `LegacyInstanceGuard` kept until S5 | |
| S5 Legacy guard removal | after the 90-day retention window | | |

#### 7.13.2 State machine

| State | Event | Guard | Action | Next state |
|---|---|---|---|---|
| S0 | first native prerelease published | 7.12 verified | Intune: create `shotAI (x64)` and `shotAI (ARM64)` Win32 apps plus the runtime dependency; assign Required to the pilot group | S1 |
| S1 | pilot issue that blocks work | | remove the user from the pilot group and assign the native app as Uninstall; the user continues on Electron (never removed in S1) | S1 (user in S0) |
| S1 | new prerelease | | new superseding Win32 app | S1 |
| S1 | exit criteria met (AC-PKG-29) | | publish `2.0.0-rc.1` to a wider group | S2 |
| S2 | exit criteria met | | publish `2.0.0` (latest) | S3 |
| S3 | native deployed to a user | detection succeeds | Electron removal assignment runs for that user (7.13.3) | S3 |
| S3 | blocking regression, fleet-wide | within 90 days | rollback R2 (7.13.4) | S0 or S1 |
| S3 | stable for the agreed period | | cleanup PR | S4 |
| S4 | 90 days after GA | no Electron installs reported by IT | remove `LegacyInstanceGuard` and the Electron retention; Electron Intune app deleted | S5 |

#### 7.13.3 Removing the per-user Squirrel install

Recommended mechanism (Q-PKG-7): keep the existing Electron Intune app (if Electron was deployed through Intune) and assign it as **Uninstall** to the same group in User context, with an uninstall command that runs a wrapper script shipped in its package (Intune does not expand environment variables, EDGE-PKG-32):

```powershell
# uninstall-shotai-electron.ps1 (user context)
$update = Join-Path $env:LOCALAPPDATA 'shotai\Update.exe'
if (Test-Path $update) {
  Start-Process $update -ArgumentList '--uninstall','-s' -Wait
}
exit 0
```

Its detection rule: file `%LOCALAPPDATA%\shotai\Update.exe` exists. The script must be INSIDE the app's `.intunewin` content, so the existing Electron Intune app needs a content update (a re-wrapped package with the script next to the Setup.exe) before its uninstall command can call it; the command is `powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\uninstall-shotai-electron.ps1` (32-bit PowerShell, harmless here, EDGE-PKG-32). The group's Required (install) assignment of the Electron app is removed before the Uninstall assignment is added, because an app assigned both ways "will remain and not be removed" (EDGE-PKG-59). Where Electron was installed by hand, the same script runs as an Intune platform script in user context. `Update.exe --uninstall -s` is Squirrel.Windows' silent uninstall switch pair, UNVERIFIED against the 1.3.0 build and covered by AC-PKG-24. Squirrel removes its root, its shortcuts and the HKCU ARP key; `%APPDATA%\shotAI` stays (EDGE-PKG-33, verify with AC-PKG-24). The native app never uninstalls software itself (IMPROVEMENT over self-removal: IT owns software lifecycle).

#### 7.13.4 Rollback

| Rollback | Mechanism | Data safety |
|---|---|---|
| R1 during pilot (one user) | unassign native, Uninstall assignment for the native app; Electron is still there | settings and projects shared; Electron's own `secrets.json` and `entra-cache.bin` untouched (INV-PKG-24) |
| R2 after GA (fleet) | Uninstall assignment for the native apps; reassign the retained Electron 1.3.x Intune app (User context, Required); optionally edit the `2.0.0` GitHub release to prerelease so `/releases/latest` returns `v1.3.x` and Electron stops offering 2.0.0 (Q-PKG-21) | as R1; requires the rollback target to be 1.3.x (INV-PKG-25); `project.json` written natively stays readable by 1.3.x through the shared conformance suite (01) |
| Native to older native | Uninstall the newer MSI, install the older (MajorUpgrade refuses downgrades) | settings and projects unaffected |

### 7.14 Hardening plan carry-over

| Item | Native status | Replacement or note |
|---|---|---|
| S1 postinstall integrity | CARRIES (intent) | NuGet lock plus source mapping plus signature validation (7.8); libavif pins (7.7) |
| S2 sandbox on | DISAPPEARS | no renderer; the only web engine is the WebView2 PDF host with JavaScript off (09) |
| S3 redaction invalidation backstop | CARRIES | 01, 04 |
| S4 and S8 navigation confinement | DISAPPEARS, except the WebView2 PDF host navigation lock (09) | |
| S5 and S6 path confinement | CARRIES | 01 |
| S7 offline OCR model | DISAPPEARS | Windows OCR; the recognizer is an OS capability (7.5.1) |
| S9 overlay IPC sender guard | DISAPPEARS | no IPC |
| S10 egress pin | CARRIES | 07 |
| S11 packaged app launch | CARRIES as a test | INV-PKG-23 |
| Fuses | DISAPPEAR | INV-PKG-15 to INV-PKG-18 |
| C1 GPU policy | DISAPPEARS | ARM64 build (EDGE-PKG-15) |
| Code signing (deferred) | NEW | INV-PKG-7 |
| Auto-update (deferred) | NOT ADOPTED in 2.0 | Intune delivers updates; the notice-only update check stays (10) |
| NEW N1 DLL search order | NEW | INV-PKG-16 |
| NEW N2 apphost runtime search | NEW | INV-PKG-17 |
| NEW N3 startup hooks off | NEW | INV-PKG-18 |
| NEW N4 native library provenance | NEW | INV-PKG-19 |
| NEW N5 release pipeline least privilege | NEW | INV-PKG-28 |
| NEW N6 install directory ACL | NEW | INV-PKG-15 |
| NEW N7 public artifacts carry no tenant data | NEW | INV-PKG-14 |
| NEW N8 concurrent Electron guard | NEW | INV-PKG-26 |
| NEW N9 CET kept on, no ACG policy | NEW | 7.9.2, EDGE-PKG-51 |
| NEW N10 every binary CI downloads is hash-checked (runtime installer, NASM, libavif and libaom archives) | NEW | 7.7.2, 7.11.4 |

### 7.15 `ShotAI.Release` tool

Project `dotnet/tools/ShotAI.Release/ShotAI.Release.csproj`: `net10.0`, `OutputType Exe`, no Windows API (runs on Linux and Windows), added to `ShotAI.slnx` under `/tools/`; referenced by `ShotAI.Core.Tests` (same pattern as 10's `ShotAI.GenBrand`).

```csharp
namespace ShotAI.Release;

public enum Stage { Alpha, Beta, Rc }
public enum TargetArch { X64, Arm64 }

public sealed record ReleaseVersion(int Major, int Minor, int Patch, Stage? Stage, int? Number)
{
    public static bool TryParse(string text, out ReleaseVersion? version, out string? error);
    public bool IsPrerelease => Stage is not null;
    public string SemVer { get; }                 // "2.0.0-beta.1"
    public string Tag => "v" + SemVer;
    public MsiVersion Msi { get; }                // 7.3 formula
    public string FileVersion => $"{Major}.{Minor}.{Msi.Build}.0";
    public string ShortcutName => IsPrerelease ? "shotAI Preview" : "shotAI";
    public IReadOnlyList<string> AssetNames { get; }
    public IReadOnlyList<string> GhReleaseFlags { get; }  // ["--prerelease","--latest=false"] or ["--latest"]
    public static int ComparePrecedence(ReleaseVersion a, ReleaseVersion b);
}

public readonly record struct MsiVersion(int Major, int Minor, int Build) : IComparable<MsiVersion>
{
    public override string ToString() => $"{Major}.{Minor}.{Build}";
}

public static class TagCheck
{
    // Errors (exact): "tag <t> is not a native release tag", "tag <t> does not match Directory.Build.props version <v>",
    // "tag <t> is not newer than existing tag <e>", "major version must be at least 2".
    // existingTags must already exclude <t> itself (EDGE-PKG-53); Check also ignores an entry equal to <t> defensively.
    public static IReadOnlyList<string> Check(string tag, string propsVersion, IEnumerable<string> existingTags);
}

public static class PropsReader { public static string ReadVersion(string directoryBuildPropsPath); }
public static class PeHeader { public static (ushort Machine, bool IlOnly) Read(Stream pe); }
public static class PayloadVerifier { public static IReadOnlyList<string> Verify(string dir, TargetArch arch, bool publicBuild); }
public static class NoticesGenerator { public static string Generate(string dotnetRoot, string repoRoot); }
public static class FederationCheck { public static IReadOnlyList<string> Check(byte[] federationJson); }   // 7.11.3 preflight; wraps Core's BakedFederationParser and FederationConfigValidator (08); messages name keys, never values
```

Commands: `version [--props <path>]` prints `semver=`, `msi=`, `file=`, `prerelease=`, `shortcut=` lines (for `$GITHUB_OUTPUT`); `check-tag <tag> --props <path> --existing-tags <list>`; `verify-payload <dir> --arch x64|arm64 [--public]`; `notices [--check]`; `federation-check <file>`. `ShotAI.Release` references `ShotAI.Core` (platform-neutral) for the federation validator. Exit 0 on success, 1 on any error (each error printed as one line prefixed `release: `), 2 on bad arguments.

Threading, cancellation, disposal: synchronous console tool, files opened with `using`, no network.

### 7.16 Divergences

| ID | Divergence | Class | Justification |
|---|---|---|---|
| D-PKG-1 | per-machine MSI instead of per-user Squirrel | IMPROVEMENT | device-context Intune, standard detection, admin-only install directory |
| D-PKG-2 | x64 and ARM64 instead of x64 only | IMPROVEMENT | native speed and GPU on Windows on ARM |
| D-PKG-3 | framework-dependent runtime instead of a bundled Chromium and Node | IMPROVEMENT | Microsoft services patches |
| D-PKG-4 | signed binaries and MSI | IMPROVEMENT | SmartScreen, application control |
| D-PKG-5 | release workflow instead of hand builds | IMPROVEMENT | reproducible, flags cannot be forgotten |
| D-PKG-6 | public release is bring-your-own-key; baked build internal only | IMPROVEMENT | public repo must not publish tenant identifiers |
| D-PKG-7 | libavif built from pinned source instead of WASM from npm | IMPROVEMENT | provenance, multi-core speed (09) |
| D-PKG-8 | no install animation, no auto-launch after install | ELECTRON-ONLY removal | Intune installs are silent |
| D-PKG-9 | no desktop shortcut by default | IMPROVEMENT | per-machine desktop shortcuts appear for every user (Q-PKG-9) |
| D-PKG-10 | ARP icon by `ARPPRODUCTICON` instead of a registry write at first run | IMPROVEMENT | deterministic, no runtime registry writes |
| D-PKG-11 | `LegacyInstanceGuard` | IMPROVEMENT | coexistence did not exist before |
| D-PKG-12 | GPU policy, sandbox switches, fuses, asar, extraResource, postinstall, rebuild config, Vite configs, loading GIF generator, Rust DLL build | ELECTRON-ONLY | replacements named in section 2 |

## 8. Tests

### 8.1 Electron tests listed for this subsystem

None are listed, and none exist: Electron's packaging was never under automated test (`ci.yml:15-16`: "Packaging and anything that touches real capture still belong on Windows, and are deliberately NOT here"). Related Electron tests owned by other specs, for completeness:

| Electron test | Purpose | Owner and native target |
|---|---|---|
| `src/main/arp-icon.test.ts` | Squirrel ARP icon fix | ELECTRON-ONLY (03 8.2); intent tested here by `PackageSourceTests.ShortcutAndArp` and AC-PKG-2 |
| `src/main/gpu-policy.test.ts` | GPU auto-disable under emulation | ELECTRON-ONLY (03) |
| `src/main/update-check.test.ts` | tag parsing, prerelease refusal | 10 (`UpdateCheckTests`); this spec relies on `PickRelease` refusing prereleases |
| `src/shared/export-theme.test.ts:127-172` | `extraResource` ships `Archivo.ttf` and `OFL.txt` and main looks for the same basename | 09 and 10 (`FontPackagingTests`); here `PayloadVerifierTests.FontHasOfl` on the payload |

### 8.2 New tests

**ShotAI.Core.Tests (Linux and Windows), referencing `tools/ShotAI.Release`:**

| Class | Cases |
|---|---|
| `Release.ReleaseVersionTests` | `ParsesFinal` (`2.0.0`); `ParsesPrerelease` (`2.0.0-alpha.0`, `-beta.12`, `-rc.1`); `RejectsLeadingZeros` (`2.01.0`, `2.0.0-rc.01`); `RejectsUnknownStage` (`-preview.1`, `-rc1`, `-RC.1`); `RejectsBuildMetadata` (`2.0.0+abc`); `RejectsOutOfRange` (`Z=65`, `N=300`, `X=256`); `ShortcutName`; `AssetNames` (exact list of 7.3); `GhFlags` (prerelease `--prerelease --latest=false`, final `--latest`) |
| `Release.MsiVersionTests` | every row of the 7.3 table; `MonotonicExhaustive` (all versions with `X=2`, `Y in 0..2`, `Z in 0..3`, every stage, `N in {0,1,2,298,299}` plus finals: sorting by `ComparePrecedence` equals sorting by `MsiVersion`, strictly); `FitsWindowsInstallerLimits` (`X,Y <= 255`, `B <= 65535`) |
| `Release.TagCheckTests` | tag equals props; mismatch message exact; `v1.3.1` refused (`major version must be at least 2`); `2026-08-04` and `nightly` refused (EDGE-PKG-20); not newer than an existing tag refused; equal to an existing tag refused; the tag itself present in `existingTags` is ignored (EDGE-PKG-53) |
| `Release.PropsReaderTests` | reads `<Version>` from the real `dotnet/Directory.Build.props`; the value parses with `ReleaseVersion.TryParse` (the repo is always in a releasable state) |
| `Release.PeHeaderTests` | synthetic x64, ARM64, IL-only AnyCPU, ReadyToRun x64 headers; truncated file fails cleanly |
| `Release.PayloadVerifierTests` | `MissingFileReported`; `FontHasOfl`; `ForbiddenFilesReported` (each forbidden pattern); `ArchPure` (an x64 DLL in an arm64 payload is reported); `RuntimeConfigNamesDesktopRuntime10`; `StartupHooksDisabled`; `PublicBuildHasNoBakedFederation` (a synthetic assembly with the resource is reported); negative control: a complete synthetic payload passes |
| `Release.NoticesTests` | `CoversShippedPackages`; `CoversNativeLibraries` (libavif, libaom `LICENSE` and `PATENTS`, Archivo OFL pointer); `CheckReportsStale` |
| `Release.AvifPinsTests` | `pins.json` parses; `commit` is 40 lowercase hex; `archiveSha256` is 64 lowercase hex; tags non-empty |
| `Packaging.PackageSourceTests` | parses `dotnet/installer/Package.wxs`: `ScopeIsPerMachine`, `UpgradeCodePinned`, `MajorUpgradeWithDowngradeMessage`, `NoCustomActionRunsTheApp`, `NoPolicyRegistryWrites`, `NoPermissionElements`, `NoUserProfileLocations`, `NoChainedInstallers`, `ShortcutAndArp`, `LaunchConditionMessageExact`, `NoPersonalOrTenantData`, `AllowsSameVersionUpgrade` (EDGE-PKG-46), `DefinesMapEveryPreprocessorVariable` (every `$(var.X)` or `$(X)` used in `Package.wxs` is in the `.wixproj` `DefineConstants`); each asserted once against a mutated copy of the file to prove it can fail (EDGE-PKG-39) |
| `Packaging.WorkflowContractTests` | text checks of `.github/workflows/dotnet.yml` and `release.yml`: `LinuxJobRunsBrandCheckBeforeBuild`, `NativeJobsRunFromDotnetDirectory`, `ReleaseTriggersOnlyOnV2Tags`, `ReleasePermissionsMinimal`, `ReleaseSignsBeforePackaging` (job `needs` graph), `FederationSecretOnlyInInternalJob`, `InternalMsiNeverUploadedToRelease`, `ActionsPinnedBySha` (release.yml), `DraftReleaseOnly`, `CheckTagExcludesItself` (EDGE-PKG-53), `PublicPublishEmptiesFederationFile` (every public-variant publish passes `-p:ShotAIFederationFile=`), `SmokeAssertionsThrow` (no bare `Test-Path` or `-eq` line in the smoke script, EDGE-PKG-54), `InternalPreflightUsesValidator` |
| `Packaging.RepoHygieneTests` | `NoCommittedBinaries` (no tracked `*.dll`, `*.exe`, `*.msi` under `dotnet/`); `LockFilesPresent`; `NuGetConfigMapsOnlyNuGetOrg`; `InstallerNotInSolution`; `GitattributesPinsGeneratedTables`; `EveryTestProjectInSolution` (EDGE-PKG-52); `GitignoreCoversNativeArtifacts` (`dotnet/artifacts/`, `dotnet/federation.local.json`); `PlatformAndAppDeclareRuntimeIdentifiers` (EDGE-PKG-57) |
| `Packaging.ManifestTests` | `AsInvoker`; existing PMv2, `longPathAware` and `supportedOS` entries still present |
| `Release.FederationCheckTests` | complete synthetic config passes; each of the five required keys missing gives the exact `missing:` message; a malformed `WorkspaceId` or `ClientAppId` is rejected; a leading UTF-8 BOM is accepted; no message contains any input value (all fixtures use obviously fake GUIDs) |
| `Packaging.SourceScanTests` | `DllImportSearchPathsDeclared` (Platform and App); `SetDefaultDllDirectoriesIsFirstInMain` (the first statement of `Program.Main` is the `DllSearchHardening.Apply()` call, and `DllSearchHardening` calls `SetDefaultDllDirectories` with `LOAD_LIBRARY_SEARCH_DEFAULT_DIRS`); `AppXamlIsPage` (no second generated `Main`); `AppCsprojHardening` (`AppHostDotNetSearch Global`, `StartupHookSupport false`, `SelfContained false`, `RuntimeIdentifiers win-x64;win-arm64`) |

**ShotAI.Platform.Tests (Windows):**

| Class | Cases |
|---|---|
| `Startup.DllSearchTests` | `CurrentDirectoryNotSearched`: after `SetDefaultDllDirectories`, `LoadLibraryEx("shotai_test_probe.dll")` with the DLL only in the current directory fails with `ERROR_MOD_NOT_FOUND` |
| `Startup.ProcessSnapshotTests` | returns the current process with its session and image path; access denied yields a null path |

**ShotAI.App.Tests (Windows):**

| Class | Cases |
|---|---|
| `Startup.LegacyInstanceGuardTests` | hit in the same session under `%LOCALAPPDATA%\shotai\app-1.3.0\shotAI.exe` (case variants `ShotAI`, `SHOTAI`); miss for another session; miss for the native install path; miss for the Squirrel stub `%LOCALAPPDATA%\shotai\shotAI.exe`; miss for a null path; miss for the current pid; message text exact; nothing written to settings on a hit; in a self-test mode no dialog is shown, the exact stderr line is written and the exit code is 2 (EDGE-PKG-50) |
| `Shell.AppPathsTests` (shared with 10, which lists the full class) | `NoPathUnderSquirrelRoot` and `LocalDataDirectoryIsUnderLfi` (INV-PKG-24, R-ARCH-13) |

**CI-only checks (PowerShell, Windows):** Authenticode verification (7.6), install smoke and upgrade/downgrade (7.11.4), `dumpbin` export and dependency checks for `shotai_avif.dll` (7.7.2).

## 9. Acceptance criteria

**AC-PKG-1.** `PackageSourceTests`, `ReleaseVersionTests`, `MsiVersionTests`, `TagCheckTests`, `PayloadVerifierTests`, `WorkflowContractTests`, `RepoHygieneTests`, `ManifestTests`, `SourceScanTests`, `NoticesTests` and `AvifPinsTests` pass on the Linux job.

**AC-PKG-2.** Manual: after installing the MSI offline on a clean Windows 11 x64 VM, Settings, Apps, Installed apps shows `shotAI` with the shotAI icon and publisher `LFI`; the Start menu has one `shotAI` (or `shotAI Preview` for a prerelease) shortcut that launches `C:\Program Files\shotAI\shotAI.exe`; there is no desktop shortcut. (03 AC-SHELL-30.)

**AC-PKG-3.** The `package` job passes on x64 and ARM64: install exit 0, `--selftest` exit 0 with last line `[selftest] PASS`, uninstall exit 0, install folder gone, `settings.json` hash unchanged.

**AC-PKG-4.** `verify-payload --public` passes for both payloads, and fails (non-zero, one `release: ` line each) when, separately: `Fonts\OFL.txt` is deleted, an x64 `shotai_avif.dll` is copied into the arm64 payload, a `.pdb` is added, or the federation resource is embedded.

**AC-PKG-5.** On a release build, `Get-AuthenticodeSignature` reports `Valid` with a non-null `TimeStamperCertificate` for every `*.exe` and `*.dll` under `C:\Program Files\shotAI` and for both MSIs; the five first-party files show the organization's subject.

**AC-PKG-6.** `icacls "C:\Program Files\shotAI"` shows no write, modify or full-control entry for `BUILTIN\Users`, `NT AUTHORITY\Authenticated Users` or `Everyone`.

**AC-PKG-7.** Upgrade: installing `2.0.0-beta.1` then `2.0.0` leaves one ARP entry with DisplayVersion `2.0.999` and one Start menu shortcut named `shotAI` (the `shotAI Preview` shortcut is gone).

**AC-PKG-8.** Downgrade: installing `2.0.0-beta.1` over `2.0.0` fails with exit code 1603 and the log contains `A newer version of shotAI is already installed.`; upgrading while shotAI is running either closes it (Restart Manager) or returns 3010, and in both cases the next launch runs the new version with no lost project edits.

**AC-PKG-9.** After uninstall, `%APPDATA%\shotAI\settings.json`, `%APPDATA%\shotAI\logs\`, the projects folder and the native local data folder `%LOCALAPPDATA%\LFI\shotAI\` (R-ARCH-13) are byte-identical to before (hash comparison).

**AC-PKG-10.** Manual: on a VM without the .NET 10 Desktop Runtime, an Intune Required assignment installs the runtime dependency first, then shotAI; shotAI launches. Launching `shotAI.exe` on a VM without the runtime (manual MSI install) shows the .NET runtime-missing dialog (record its exact text here on first run, EDGE-PKG-42) and does not crash.

**AC-PKG-11.** Manual: with the WebView2 runtime uninstalled, the app launches, all exports except PDF succeed, and PDF export shows 09's notice; on a VM without the OCR capability, auto-redact shows 04's notice.

**AC-PKG-12.** Manual: on a Windows 10 1909 (build 18363) VM the MSI refuses with `shotAI requires Windows 10 version 2004 (build 19041) or later.`; on 2004 (19041) it installs.

**AC-PKG-13.** For the first native prerelease: the GitHub release shows Pre-release and not Latest; `gh release view <tag> --json isPrerelease` returns `true` and `gh api repos/{owner}/{repo}/releases/latest --jq .tag_name` returns a tag other than `<tag>`, exactly as the `verify-published` job checks (the earlier `gh release view --json isPrerelease,isLatest` form is waived because `isLatest` support is UNVERIFIED, EDGE-PKG-58, `docs/native/PLAN.md` 1.5); an Electron 1.3.0 client's manual update check reports up to date; `GET /repos/Armadillon44/shotAI/releases/latest` still returns a `v1.3.x` tag.

**AC-PKG-14.** For 2.0.0: the release is Latest; an Electron 1.3.0 client shows the update notice for `2.0.0` with the release URL.

**AC-PKG-15.** `release.yml` refuses (job `check` fails) a tag `v2.0.0` when `Directory.Build.props` says `2.0.0-rc.1`, a tag `v2.0.0-rc1`, a tag lower than an existing `v2.*` tag, and a re-run for a tag that already has a published release; it ACCEPTS a correct new tag even though `git tag -l` lists that tag (EDGE-PKG-53).

**AC-PKG-16.** The public release assets contain no baked federation (`verify-payload --public` on the extracted MSI payload, via `msiexec /a` administrative extraction), and the internal MSI appears only as a workflow artifact.

**AC-PKG-17.** `shotai_avif.dll` in each payload: `dumpbin /exports` lists exactly `shotai_avif_encode`, `shotai_avif_free`, `shotai_avif_version`; `dumpbin /dependents` lists only system DLLs; an attestation exists for its SHA-256 (`gh attestation verify`); an HTML export of a 13-step project on ARM64 embeds AVIF images (09 behavior).

**AC-PKG-18.** With `DOTNET_ROOT` and `DOTNET_ROOT_X64`/`DOTNET_ROOT_ARM64` set to an empty folder and `DOTNET_STARTUP_HOOKS` set to a path of a probe assembly, the installed shotAI starts normally and the probe's marker file is not created.

**AC-PKG-19.** Process Monitor on first launch from a working directory containing a DLL named `wpfgfx_cor3.dll` shows no load from that directory.

**AC-PKG-20.** Manual: with Electron 1.3.0 running, launching native shows the exact legacy notice and exits within 2 s, and `settings.json` is unchanged; with Electron closed, native starts normally.

**AC-PKG-21.** Manual: during the pilot, a setting changed in native (theme) is visible in Electron after restarting it, and a setting changed in Electron (projects folder) is visible in native after restarting it.

**AC-PKG-22.** Manual: after native writes `settings.json` with native-only keys, Electron 1.3.0 starts, keeps its projects folder and recents, and after an Electron settings change the native-only keys are still present.

**AC-PKG-23.** Manual: a project edited in native opens in Electron 1.3.0 with the same steps, captions and redactions, and vice versa (01's conformance guarantees, checked end to end).

**AC-PKG-24.** Manual, before the pilot: on a PC with Electron 1.3.0 and native installed and signed in, running the 7.13.3 script removes `%LOCALAPPDATA%\shotai`, the Electron shortcuts and the HKCU ARP key; `%APPDATA%\shotAI\settings.json`, logs, projects and `%LOCALAPPDATA%\LFI\shotAI\` (the MSAL cache `entra\msal-cache.bin` and the `WebView2\` folder, R-ARCH-13) are intact; native still shows the user as signed in and a PDF export still works (ARCHITECTURE AC-ARCH-7).

**AC-PKG-25.** Intune detection: after install the Win32 app reports Installed (MSI product code and version rule); after a manual uninstall it reports Not installed and reinstalls within the Intune evaluation cycle.

**AC-PKG-26.** The ARM64 MSI on a Windows 11 ARM64 device runs natively (Task Manager architecture column `ARM64`) and the x64 MSI on an x64 device runs as x64.

**AC-PKG-27.** 90 days after GA the `v1.3.*` release assets are still downloadable and the Electron Intune app object still exists.

**AC-PKG-28.** `dotnet build ShotAI.slnx -c Release` succeeds on Linux with the tools projects added and the installer project absent from the solution.

**AC-PKG-29.** Pilot exit criteria (manual, recorded in the pilot issue): at least two weeks of daily use by the pilot group on both architectures; 02 AC-CAP-6 (same flow recorded in both builds matches) passes; no open blocker; AC-PKG-20 to AC-PKG-24 pass.

**AC-PKG-30.** `THIRD-PARTY-NOTICES.txt` is present in `C:\Program Files\shotAI` and attached to the release, and `notices --check` passes.

**AC-PKG-31.** On real x64 and ARM64 hardware with a GPU, the installed app (with `DllSearchHardening.Apply()` in effect) reports `RenderCapability.Tier >> 16 == 2`, and Process Monitor shows `D3DCompiler_47_cor3.dll` and `wpfgfx_cor3.dll` loaded from the WindowsDesktop framework folder; the same measurement with the call removed gives the same tier (EDGE-PKG-48, Q-PKG-30).

**AC-PKG-32.** Installing the public MSI and then the internal MSI of the same version (and the reverse) leaves exactly one `shotAI` ARP entry and one install folder (EDGE-PKG-46).

**AC-PKG-33.** `PayloadVerifier` on the first `package` job run lists every `NotSigned` PE; with the chosen ReadyToRun exclusions (EDGE-PKG-47), every NuGet-supplied assembly that was signed in its package is still `Valid` with its original publisher in the release payload.

**AC-PKG-34.** With Electron 1.3.0 running, `shotAI.exe --selftest` exits 2 within 2 s with the EDGE-PKG-50 stderr line and shows no dialog.

## 10. Interfaces with other subsystems

| Spec | Consumes from it | Provides to it |
|---|---|---|
| 01 Model and store | the conformance suite (rollback safety of `project.json`); Windows junction permission requirement for Platform tests | Linux and Windows test jobs |
| 02 Capture | nothing at build time | ARM64 and x64 builds; Windows ARM64 runner for capture tests; removal of the Rust and npm native modules |
| 03 Shell | the startup order slot for `LegacyInstanceGuard` (after the mutex, before settings); session end flush (EDGE-PKG-29); `app.manifest` ownership of DPI entries | installer never launches the app (INV-PKG-4); Start menu shortcut and ARP icon (AC-SHELL-30 via AC-PKG-2); the `asInvoker` manifest block; `SetDefaultDllDirectories` as the first statement of `Main` |
| 04 Editor and OCR | OCR language notice | OCR Feature on Demand deployment note (7.5.1); Windows runner with a recognizer |
| 05, 06 UI | test projects | Windows test jobs; fonts in the payload |
| 07 SOP | `Anthropic` package licence | notices entry |
| 08 Auth | `ShotAIFederationFile` property, the five required keys, `BakedFederationParser` and `FederationConfigValidator` (reused by `ShotAI.Release federation-check`), `msalruntime` per RID, MSAL cache path `%LOCALAPPDATA%\LFI\shotAI\entra\msal-cache.bin` from `IAppPaths.LocalDataDirectory` (R-ARCH-13) | release secret wiring and preflight (7.11.3); public build is bring-your-own-key (INV-PKG-14); installer never writes the policy key (INV-PKG-3); the Squirrel-root constraint behind R-ARCH-13 (EDGE-PKG-22, Q-PKG-4 resolved) |
| 09 Exports | `shotai_avif.dll` ABI, WebView2 prerequisite, WebView2 user data folder `%LOCALAPPDATA%\LFI\shotAI\WebView2` from `IAppPaths.LocalDataDirectory` (R-ARCH-13), fonts | the pinned AVIF build (7.7), payload layout (7.2.3), WebView2 deployment (7.5.1), answer to Q-EXP-20 (no deletion on uninstall), the Squirrel-root constraint behind R-ARCH-13 (EDGE-PKG-22, EDGE-PKG-28) |
| 10 Infra | `ShotAI.GenBrand --check`, `--selftest` exit codes, `IAppPaths` (including `LocalDataDirectory` = `%LOCALAPPDATA%\LFI\shotAI\` and `FontsDirectory`, R-ARCH-13, ARCHITECTURE 10.2), settings unknown-key preservation, prerelease-aware `IsNewer`, log sink sharing, the bootstrap logger used by `LegacyInstanceGuard` | the self-test behavior of the guard (EDGE-PKG-50: stderr line, exit code 2, no dialog), CI steps (7.11.2), release flags (INV-PKG-9), answer to Q-INFRA-8 (MSI), notices file, `IncludeSourceRevisionInInformationalVersion=false` |
| 11 Services | DI composition root | nothing at startup (`ProcessSnapshot` and `DllSearchHardening` are public Platform helpers with no Core counterpart, ARCHITECTURE INV-ARCH-4): `ProcessSnapshot` (Platform) and `LegacyInstanceGuard` (App) are constructed directly in 03 step 2a, before the container exists (7.10.3); `IProcessSnapshot` may also be registered later for diagnostics |

## 11. Open questions and risks

**Q-PKG-1. WiX Toolset licensing.** Recent WiX Toolset releases reportedly add an Open Source Maintenance Fee for organizations that use it commercially; the exact terms, and which version introduced them, are not verified here. Recommended default: read the current licence and fee terms before the installer PR; if the fee applies and is not approved, pin the last WiX release without it (MS-RL) or budget the fee; Advanced Installer is the commercial fallback. Record the decision in the installer PR. Constraint added by verification: 7.4.2 uses the `Files` harvesting element, which is not in WiX v4, so pinning a pre-fee release is only possible if that release has `Files`; otherwise the payload must be listed with explicit `File` elements generated by `ShotAI.Release` (the licence terms of each WiX version are NOT verified here and remain an open decision).

**Q-PKG-2. Artifact Signing eligibility and ownership.** Public Trust needs organization identity validation (LFI must show a three-year tax history in an eligible country) and an Azure subscription (trial subscriptions are reported to fail with 403). Recommended default: IT owns the Azure subscription and the identity validation under LFI; the GitHub `release` environment gets only the OIDC client id, tenant id, subscription id, endpoint, account and profile names. If not eligible, an OV certificate in Azure Key Vault signed through the same job. Timestamp URL: the Artifact Signing documentation gives `http://timestamp.acs.microsoft.com` (section 3; the MSIX guide's `https://` form is the outlier); confirm `TimeStamperCertificate` on the first signed build. Eligibility: the quickstart lists the USA among Public Trust organization countries; the three-year history rule is stated only in Microsoft Q&A (UNVERIFIED as current). A Private Trust profile is the internal-only fallback if Public Trust validation is refused.

**Q-PKG-3. Sign unsigned third-party PEs?** Recommended default: yes, sign any payload PE that is `NotSigned` (never re-sign a validly signed one), so publisher-based application control rules cover the whole install; list them in the release log.

**Q-PKG-4. Native local data collides with the Squirrel root.** Resolved by R-ARCH-13: native local data lives under `IAppPaths.LocalDataDirectory` = `%LOCALAPPDATA%\LFI\shotAI\` (MSAL cache `entra\msal-cache.bin` for 08, WebView2 data `WebView2\` for 09; 10 7.4.4 adds the member), and the pilot keeps its coexistence (EDGE-PKG-22, INV-PKG-24, AC-PKG-24, ARCHITECTURE AC-ARCH-7). Original text, kept for the record: recommended default: move native local data to `%LOCALAPPDATA%\LFI\shotAI\` (`entra\msal-cache.bin` for 08, `WebView2\` for 09), and add `IAppPaths.LocalDataDirectory` (10) returning it; update 08 and 09 in their next revision. Alternative: keep `%LOCALAPPDATA%\shotAI\` and require Electron removal before first native launch, which rules out the pilot's coexistence.

**Q-PKG-5. Internal build distribution.** Recommended default: internal MSIs (baked federation) exist only as a 30-day workflow artifact downloaded by IT for Intune; alternatively, deploy only the public bring-your-own-key MSI and configure every managed device through the existing ADMX policy, which removes the secret from CI entirely. Decide with IT before S1.

**Q-PKG-6. Launch condition mechanics.** Documented, pending measurement: "Conditional Statement Syntax" implies that a property value that converts to an integer is compared as an integer (7.4.4), so `SHOTAI_OSBUILD >= 19041` is numeric. Recommended default: keep it and still test on 1909 and 2004 VMs (AC-PKG-12).

**Q-PKG-7. Removing Electron: supersedence versus an Uninstall assignment.** Supersedence with "Uninstall previous version" crosses install contexts (native System, Electron User) and its behavior there is unverified. Recommended default: a separate Uninstall assignment of the Electron app in User context with the 7.13.3 wrapper; test supersedence in a lab only if IT prefers it.

**Q-PKG-8. Legacy guard: block or warn?** Recommended default: block (INV-PKG-26), because concurrent writes can lose edits; revisit if pilot users need both open to compare output (then offer "Open anyway" with a second confirmation).

**Q-PKG-9. Desktop shortcut.** Squirrel created one per user. Recommended default: none (`DESKTOPSHORTCUT=0`); IT can pass `DESKTOPSHORTCUT=1` in the install command. `DESKTOPSHORTCUT` is not remembered across a major upgrade (the new product evaluates the component condition afresh), so IT must pass it in EVERY version's install command or the upgrade removes the shortcut; alternatively persist it (WiX "remember property" pattern under `HKLM\SOFTWARE\LFI\shotAI`), decided in the installer PR.

**Q-PKG-10. ReadyToRun.** Recommended default: on for first-party assemblies only, with every third-party assembly listed in `PublishReadyToRunExclude` so it keeps its publisher's signature (EDGE-PKG-47); measure cold start with and without on both architectures during the pilot and keep it only if it saves at least 100 ms.

**Q-PKG-11. Symbols.** Recommended default: PDBs not installed; published as `shotAI-<v>-symbols.zip` on the release (they contain source paths from the runner, no secrets). Alternative: a private symbol store.

**Q-PKG-12. ARM64 runner.** `windows-11-arm` hosted runners are expected to be available to public repositories; availability and label are unverified. Recommended default: use them; if unavailable, run the ARM64 tests and smoke on a self-hosted ARM64 runner or manually per release (AC-PKG-26).

**Q-PKG-13. libaom build details.** NASM for x64 assembly and the ARM64 MSVC build path need confirming. Recommended default: NASM installed from a pinned, hash-checked download; for ARM64 build natively on `windows-11-arm` if cross-compilation fails; if assembly cannot be built, `-DAOM_TARGET_CPU=generic` (slower, still correct).

**Q-PKG-14. NuGet audit severity in PR CI.** Adopted as ARCHITECTURE 3.1 V5 (high and critical fail every build, low and moderate fail the release workflow only). Recommended default: audit errors fail the release workflow always; in PR CI set `<WarningsNotAsErrors>NU1901;NU1902</WarningsNotAsErrors>` (low and moderate) and keep high and critical as errors.

**Q-PKG-15. Fleet-wide update-check opt-out.** Intune delivers updates, so the notice is noise on managed PCs (macOS has a managed key). Recommended default: not in 2.0.0 (10 Q-INFRA-4); if IT asks, add `HKLM\SOFTWARE\Policies\shotAI\UpdateCheckDisabled` (DWORD) with an ADMX revision, never under `Federation`.

**Q-PKG-16. Import the Electron API key.** Recommended default: no (08 Q-AUTH-2); the 2.0.0 notes say keys are re-entered.

**Q-PKG-17. Clean the Chromium profile out of `%APPDATA%\shotAI` after cutover.** Electron leaves caches there. Recommended default: not in 2.0; after S5, a native one-time cleanup of known Chromium entries (`Cache`, `Code Cache`, `GPUCache`, `DawnCache`, `Local State`, `Preferences`, `Network`, `Session Storage`, `Local Storage`, `blob_storage`, `Shared Dictionary`, `tessdata`) may be added, never touching `settings.json`, logs, `secrets.json` or `entra-cache.bin` without a separate decision.

**Q-PKG-18. Produce `.intunewin` in the release.** Recommended default: no; IT wraps the MSI with the Content Prep Tool. Revisit if IT wants a ready package (the tool runs on the Windows release runner).

**Q-PKG-19. `StartupHookSupport` property.** The MSBuild property is verified (Microsoft Learn, "Trimming options"); the runtimeconfig switch name `System.StartupHookProvider.IsSupported` is from the dotnet/runtime feature-switch list and not from Learn. Recommended default: set it and let `PayloadVerifierTests.StartupHooksDisabled` plus AC-PKG-18 prove it; if the switch does not appear in `runtimeconfig.json` for a non-trimmed app, add it explicitly with `<RuntimeHostConfigurationOption Include="System.StartupHookProvider.IsSupported" Value="false" />`.

**Q-PKG-20. x64 MSI on ARM64 Windows.** Recommended default: allow on Windows 11 on Arm (it runs emulated); Intune requirement rules route ARM64 devices to the ARM64 MSI, and the shared UpgradeCode converts a device that received the wrong one. Windows 10 on Arm has no x64 emulation (EDGE-PKG-55), so there the x64 build cannot run at all.

**Q-PKG-21. Stop Electron's nag after a fleet rollback.** Recommended default: do not flip 2.0.0 to prerelease unless the rollback is expected to last more than a week; users can switch the check off in Settings, About.

**Q-PKG-22. Retention window.** Recommended default: 90 days after GA for Electron artifacts and the Intune app object; extend if any rollback happened.

**Q-PKG-23. Unmanaged users following the 2.0.0 notice.** They need admin rights and the runtime. Recommended default: the 7.12.3 text; no per-user MSI variant in 2.0 (a dual-purpose MSI would complicate detection).

**Q-PKG-24. Pin actions by SHA in `dotnet.yml` and `ci.yml` too.** Recommended default: `release.yml` only now (it holds signing rights); Dependabot keeps the others current.

**Q-PKG-25. Restart Manager during upgrade.** Recommended default: leave Windows Installer's default (close applications with Restart Manager); verify with AC-PKG-8 that shotAI's session-end handling (03) flushes before exit.

**Q-PKG-26. .NET runtime detection for the Intune dependency.** The `InstalledVersions` registry layout is taken from a Microsoft Q&A answer, not a reference page. Recommended default: detect by folder (`%ProgramFiles%\dotnet\shared\Microsoft.WindowsDesktop.App\10.*`), which is the location the apphost resolves from with `AppHostDotNetSearch=Global`.

**Q-PKG-27. Risk: the pilot lasts long enough for Electron changes.** Every Electron change during the pilot must be mirrored natively. Recommended default: freeze Electron to fixes only (`docs/NATIVE-WINDOWS-FEASIBILITY.md:155-156`), each fix released with `--latest=false` after 2.0.0 or normally before it.

**Q-PKG-28. AVIF loadability in the self-test.** Recommended default: extend 10's `--selftest` with one AVIF encode of a 16 by 16 image through `LibavifEncoder` (09), failing the self-test if the DLL is present but the encode fails; absent DLL is a failure only in packaged runs (`verify-payload` already requires it).

**Q-PKG-29. NuGet repository signature validation.** `signatureValidationMode=require` fails for any package that is not repository-signed; all nuget.org packages are repository-signed today, but the setting also rejects local test packages. Recommended default: enable it in `dotnet/nuget.config`; if a legitimate package fails, pin its author certificate as a trusted signer rather than disabling validation.

**Q-PKG-30. WPF native DLLs under restricted DLL search.** EDGE-PKG-48: whether `LOAD_LIBRARY_SEARCH_DEFAULT_DIRS` affects any WPF native load from the WindowsDesktop framework folder is not verified. Recommended default: keep INV-PKG-16 and measure AC-PKG-31 in the first Windows PR that adds `Program.Main`; if the render tier drops or a load fails, add `AddDllDirectory(<WindowsDesktop framework folder>)` immediately after `SetDefaultDllDirectories` rather than dropping the hardening.

**Q-PKG-31. `docs/native/PLAN.md` does not exist.** Resolved: `docs/native/PLAN.md` exists (the ordered implementation plan, ARCHITECTURE header and 15.6), so the links below resolve and nothing further is needed. Original text, kept for the record: `dotnet/README.md:8`, `dotnet/Directory.Build.props:22-23` and `.github/workflows/dotnet.yml:1` link it. Recommended default: the ordered implementation plan is written there (it is the natural home of the PR sequence that this spec's 7.11 to 7.13 assume); until then the links are dead and a reader lands on nothing.

**Q-PKG-32. Squirrel stub and AppUserModelID.** Electron's Squirrel shortcuts carry an AppUserModelID set by Squirrel (value UNVERIFIED); the native WPF app sets none unless 03 does. A taskbar pin made on the Electron build therefore does not become a pin of the native app (EDGE-PKG-30). Recommended default: no AUMID compatibility attempt; the cutover message tells users to re-pin (03 owns any AUMID decision).
