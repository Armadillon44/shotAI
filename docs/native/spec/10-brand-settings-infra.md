# 10 Brand contract, settings, logging, update check and self-tests

> Spec for the native rewrite. Sources read: `src/shared/theme-palette.ts` (530 lines), `src/shared/brand-colors.generated.ts` (184), `contract/brand.json` (622), `scripts/gen-brand.mjs` (136), `src/main/settings.ts` (434), `src/main/logger.ts` (49), `src/main/update-check.ts` (163), `src/main/update-state.ts` (25), `src/main/selftest.ts` (76), `src/renderer/fonts/OFL.txt` (93). Tests: `src/shared/theme-palette.test.ts` (516), `src/main/settings.test.ts` (75), `src/main/update-check.test.ts` (189), plus `src/shared/brand-narrowing.test.ts` (47), which pins the prototype-member fix. Supporting reads: `src/main/capture-selftest.ts` (246), `src/main/atomic-write.ts` (51), `src/shared/sop.ts` (153), `src/shared/project.ts` (`:18-20`, `:530`), `src/main/main.ts` (595; `:29`, `:150-170`, `:403-415`, `:500-502`, `:517-578`), `src/main/ipc.ts` (981; `:78-80`, `:255-310`, `:640-767`), `src/shared/ipc.ts` (624; `:143-156`, `:250-253`, `:495-521`), `src/preload/preload.ts` (`:150-172`), `src/renderer/project/App.tsx` (869; `:52-72`, `:544-566`), `src/renderer/project/Settings.tsx` (990; `:85-122`, `:149-179`, `:926-963`), `src/main/paths.ts` (44), `forge.config.ts` (201; `:105-122`), `src/main/export.ts` (925; `:614-641`), `src/shared/export-theme.ts` (109), `src/renderer/project/project.css` (`:14-21`), `.gitattributes` (18), `.github/workflows/ci.yml` (68), `.github/workflows/dotnet.yml` (71), `README.md` (`:386-401`), `package.json` (`:1-20`), electron-log 5.4.4 in `node_modules/electron-log/src` (`node/transports/file/index.js` 165, `node/transports/file/File.js` 158, `core/transforms/format.js` 157, `core/scope.js` 31, `node/ErrorHandler.js`). Commits read (`git show`): `d1d980b` (#84 contract), `3d00c18` (#96, #88 CRLF), `bb0decc` (#97, #89 prototype members), `77adda3` (#106, #95 unknown brand values), `38908cd` (#115, #107 unrecognised pin), `772e381` (#77 default brand pinnable), `2b14b79` (#100, #92 unknown settings keys), `9da70df` (#34 log rotation; also the history root: earlier history of `logger.ts`, `selftest.ts` and the first `settings.ts` is squashed into it), `037858d` (#60, #54 update check and pull-on-mount), `53045c6` and `f24b3dc` (remote visibility, removal of `captureNoHide`), `88b333e` (#77 brand setting), `4def683` (ink-3 AA), `1446d6e` (generated stylesheet), `285403d` (Archivo), `c070095` (#104, #93 OFL shipped). macOS (`/home/user/armadillon44/shotai_macos` at `f445bca`, read-only): `Packages/ShotModel/Sources/ShotModel/BrandPalette.swift` (234), `BrandPalette+Generated.swift` (95), `BrandPref.swift` (24), `Settings.swift` (110), `Log.swift` (174), `Scripts/gen-brand.swift` (172), `Packages/UpdateKit/Sources/UpdateKit/{SemanticVersion.swift (161), UpdateFeed.swift (254), UpdateCheckState.swift (159), UpdateChecker.swift (230)}`, `Packages/UpdateKit/Sources/UpdateSelfTest/main.swift` (70), `Packages/UpdateKit/Package.swift` (35), `Packages/UpdateKit/Tests/UpdateKitTests/UpdateKitTests.swift` (835, test names), `shotAI/BrandFont.swift` (123), `shotAI/AppPreferences.swift` (125), `shotAI/UpdateModel.swift` (222, skimmed), `Packages/ShotModel/Tests/ShotModelTests/{BrandPaletteTests,LogRetentionTests}.swift` (test names), `.github/workflows/ci.yml` (`:56-57`). Context: `docs/NATIVE-WINDOWS-FEASIBILITY.md` (274), `dotnet/README.md` (53), `dotnet/Directory.Build.props`, `dotnet/Directory.Packages.props`, the scaffold projects, and `docs/native/spec/01-model-store.md` (JsJson, AtomicFile, SerialWriteQueue, IProjectStoreSettings), `02-capture.md` (ICaptureSettings, capture self-test), `03-windows-shell.md` (startup order, crash logging, log lines), `06-home-settings-ui.md` (Settings UI, update notice), `07-sop-generation.md` (SopSettingsCoercer), `08-auth-secrets-policy.md` (ISharedHttp, ISupportUrlAllowlist). Verification pass re-read every source above end to end, plus `node_modules/electron-log/src/node/transforms/object.js`, `core/transforms/transform.js`, the `fvar`, `name` and `head` tables of `src/renderer/fonts/Archivo.ttf`, `src/main/remote-visibility.ts`, `src/main/RegionService.ts:90`, `docs/HARDENING-PLAN.md:33`, `dotnet/ShotAI.slnx`, `dotnet/tests/ShotAI.Core.Tests/ShotAI.Core.Tests.csproj`, `dotnet/global.json`, the contracts this spec must match in `03-windows-shell.md` 7.4.1, `11-service-boundary.md` 7.3.4, 7.3.6 and 7.11, `12-packaging-deploy-ci.md` INV-PKG-1, and Microsoft Learn (`CancellationTokenSource(TimeSpan, TimeProvider)`, .NET regex anchors `$` and `\z`). Consolidation pass: `docs/native/ARCHITECTURE.md` (sections 3, 4.2, 4.3, 6, 7.8, 8.4, 10 and the R-ARCH table of 15.3) and `docs/native/PLAN.md` 1.5. Status: extracted and verified. Consolidated with ARCHITECTURE.md R-ARCH-1 to R-ARCH-26 on 2026-09-23.

**Notation used in this document.**

- `path:line` cites this repo; `macOS:path:line` cites the macOS repo.
- Quoted strings are exact. This document contains no dash punctuation, so where a quoted string contains U+2014 it is written `\u2014` inside a code span, exactly as specs 01 and 03 do.
- Every behavior is classified **REQUIRED** (parity), **IMPROVEMENT** (a deliberate native change, justified) or **ELECTRON-ONLY** (disappears natively; the replacement of its intent is named).
- "JS number" means an IEEE 754 double; `JsMath.Round`, `JsString.Trim`, `JsNumber.ToJsString` and `JsJson` are spec 01's helpers (`01-model-store.md` 7.2).

---

## 1. Scope

**Owned by this spec.**

| Area | What |
|---|---|
| Brand contract | `contract/brand.json` as the single source of brand colours, radii and fonts; its sha256 stamp; the three generators (TypeScript, Swift, and the new C# one); `--check` mode and CI wiring |
| Brand palette API | The generated C# table and the hand-written helpers around it: brand ids, `DEFAULT_BRAND`, `isBrandId`, `coerceBrand`, `pinnedBrand`, `pinIsUnrecognised`, `brandPalette`, role names and CSS token names, `RETIRED_GREYS`, `CARD_RADIUS_PX`, `IMAGE_RADIUS_PX`, `hexNoHash`, the CSS font stack formula, and the palette invariants (AA contrast, dark field, radius nesting) |
| Settings | `%APPDATA%\shotAI\settings.json`: location, complete schema, per-key coercion, unknown-key preservation, the serialized atomic write, the synchronous caches (`captureScaleNow`, `remoteVisibleNow`), the report byline |
| App paths | The `IAppPaths` contract (ARCHITECTURE 10.2): roaming user data, `settings.json` and logs under `%APPDATA%\shotAI` (shared with Electron), native-only local data under `%LOCALAPPDATA%\LFI\shotAI` (R-ARCH-13), the default projects folder, temp and fonts |
| Logging | The log files, format, levels, rotation bounds, categories, the startup banner, and what must never be logged |
| Update check | Endpoint, once-a-day throttle and its persisted timestamp, version comparison, draft and prerelease refusal, timeout, error mapping, the pending stash (pull-on-mount), the manual check, the opt-out |
| External links | The `openExternal` allowlist implementation (base rules; the SupportUrl extension is 08's `ISupportUrlAllowlist`) |
| Self-tests | `SHOTAI_SELFTEST` and `SHOTAI_CAPTURE_TEST` and their native command-line switches, exit codes and console output |
| Fonts and licensing | The bundled Archivo face, where it ships, and the SIL OFL 1.1 obligations |

**Not owned here.**

| Area | Owner |
|---|---|
| The WPF theme resources, `ThemeTokenSet`, `ThemeManager`, appearance resolution, the Settings and Home views, the update notice's look and the Settings strings | 06 |
| `project.json` `theme` key round trip (this spec supplies the narrowing functions) | 01 |
| The capture engine's use of `captureScale` and `remoteVisible`; the capture self-test body | 02 |
| Startup order, single instance, crash handlers, the runtime log line, `AppPaths.BrandFontPath()` | 03 |
| `SopSettings` and its coercer (`SopSettingsCoercer`), which this spec calls | 07 |
| Secrets, federation policy, `ISharedHttp`, `ISupportUrlAllowlist` | 08 |
| Export CSS, Word and PowerPoint colour use, `ExportTheme`, the PDF `@font-face` | 09 |
| Dispatcher and DI rules | 11 |
| Installer, font files in the package, CI job layout, third-party notices file | 12 |

**Touches:** 01, 02, 03, 05 (brand pin in the open project), 06, 07, 08, 09, 11, 12.

---

## 2. Reference behavior (Electron)

### 2.1 The brand contract: `contract/brand.json`

`contract/` is read in place, never copied, and is byte-identical with the macOS repo (`dotnet/README.md` Rules; `README.md:388-389`). `.gitattributes:4-14` pins `contract/**` and `src/shared/brand-colors.generated.ts` to `text eol=lf` because the file is hashed byte for byte. REQUIRED.

Top-level structure (`contract/brand.json:1-622`):

| Key | Type | Content | Consumed by |
|---|---|---|---|
| `version` | number | `1` | nobody (informational) |
| `tokenOrder` | string[36] | the canonical colour token names in contract vocabulary (`field`, not `fieldBg`) | nobody at generation (documentation of the vocabulary) |
| `alphaOrder` | string[2] | `cardShadow`, `cardShadowHover` | nobody at generation |
| `windowsRenames` | object | `{ "field": "fieldBg" }`: contract token to Windows role name | Windows generators (`gen-brand.mjs:41`, `:92`) |
| `invariants` | array | one entry: `{ "rule": "distinct", "mode": "dark", "tokens": ["field", "surface2"], "why": "a text field the same colour as the card stops reading as an input on an elevated surface. Windows shipped fieldBg == surface2 in dark; this rule is why that cannot come back." }` | every generator, at generation time |
| `platforms.$comment` | string[] | "A generator ERRORS if a token it needs is absent" | humans |
| `platforms.macos.members` | array of `{name, kind}` | 35 members in `BrandPalette.swift` initializer order: 15 pairs (`accent` to `field`), 2 `alpha` (`cardShadow`, `cardShadowHover`), 18 pairs (`ok` to `warnFg`). No `focusRing`, `accentSoft`, `dangerBd` | macOS generator |
| `platforms.windows.colors` | string[36] | the tokens the Windows table emits, in emission order (`:211-248`) | Windows generators |
| `platforms.windows.alpha` | [] | Windows emits no alpha tokens (its shadows are authored in CSS) | Windows generators |
| `brands.<id>.label` | string | `shotAI`, `LFI` | generators |
| `brands.<id>.colors.<token>` | `{light, dark}` | lowercase `#rrggbb` strings | generators |
| `brands.<id>.alpha.<token>` | `{dark, light, rgb}` | numbers and a hex | macOS only |
| `brands.<id>.radii` | object | `panel`, `card`, `figure`, `control`, `controlSm`, `micro`, `chip`; a value is a number, `null` (chip only: capsule) or `{ "macos": n, "windows": m }` for a value the platforms have not agreed on | generators via `platformValue` |
| `brands.<id>.font` | object | `family` (string or null), `postScriptName` (string or null), `fallbacks` (string[], CSS form, quoted where CSS needs quotes), `labelStretch` (number or null) | generators |

Radii and fonts, as the Windows generator resolves them (`src/shared/brand-colors.generated.ts:16-21`, `:101-106`):

| Brand | panel | card | figure | control | controlSm | micro | chip | family | postScriptName | fallbacks | labelStretch |
|---|---|---|---|---|---|---|---|---|---|---|---|
| `shotAI` | 12 | 10 | 8 | 8 (contract `{ "macos": 6, "windows": 8 }`) | 6 | 4 | `null` | `null` | `null` | `-apple-system`, `"Segoe UI"`, `Roboto`, `Helvetica`, `Arial`, `sans-serif` | `null` |
| `lfi` | 8 | 8 | 6 | 5 | 4 | 3 | 8 | `Archivo` | `Archivo-SemiBold` | `"Helvetica Neue"`, `Helvetica`, `Arial`, `"Liberation Sans"`, `sans-serif` | 62 |

The colour values (36 per appearance per brand, 144 total) are only in the contract; this spec does not duplicate them. Values that tests and history pin: shotAI dark `field` `#2e2b40` vs `surface2` `#211f2e` (the bug the invariant exists for, `d1d980b`); shotAI `ink3` light `#6f6c88`, dark `#8e8aa8` (`4def683`). REQUIRED.

The contract's sha256 over canonical LF bytes is `c945d17c1c348c8007d16cdd6dc47be7040de47f7071932dfc5d413e6baa64ff` (stamped at `src/shared/brand-colors.generated.ts:2` and `macOS:Packages/ShotModel/Sources/ShotModel/BrandPalette+Generated.swift:2`). The CRLF-converted file hashes to `5a7597bf…` instead (`3d00c18`).

### 2.2 The Windows generator: `scripts/gen-brand.mjs`

Invocation (`scripts/gen-brand.mjs:1-4`, `package.json:16-17`): `npm run gen:brand` writes, `npm run gen:brand:check` fails if stale. Node builtins only (`:12`). Paths are relative to the script's own directory: spec `contract/brand.json`, output `src/shared/brand-colors.generated.ts` (`:18-20`).

Algorithm, in order (REQUIRED for the C# port, which must produce the same data):

| Step | Behavior | Citation |
|---|---|---|
| 1 | Read the spec bytes. | `:28` |
| 2 | Canonicalize: decode UTF-8, replace every `\r\n` with `\n`, re-encode. Hash the canonical bytes with SHA-256, lowercase hex. | `:34-39` |
| 3 | `JSON.parse` the (non-canonical) text. Take `brands`, `windowsRenames` (default `{}`), `platforms`. `tokens = platforms?.windows?.colors`, and when that is `null` or `undefined` die `spec has no platforms.windows.colors`. | `:40-42` |
| 4 | Invariants, BEFORE any emission or hex validation: for each entry of `spec.invariants ?? []` with `rule === 'distinct'` and `tokens?.length === 2` (others skipped silently), for EVERY brand in `brands` (`Object.entries` order; not only the two emitted), `x = b.colors?.[t0]?.[mode]`, `y = b.colors?.[t1]?.[mode]`; if both are truthy and `x === y` (strict compare, so two identical strings), die with the message below. A missing colour skips the check silently for that brand. | `:58-66` |
| 5 | For `bname` in the fixed list `['shotAI', 'lfi']` (a third contract brand is ignored): brand missing: die `brand <bname> missing from the contract`. Then, BEFORE any colour of that brand is validated, `b.radii ?? die('<bname>: no radii')` and `b.font ?? die('<bname>: no font')` (lines `:95-96` run before the template literal at `:97-114` that calls `mode()`). So, per brand, the first error reported is in this order: brand missing, no radii, no font, then colours (all `light` tokens, then all `dark` tokens), and shotAI is fully checked before lfi. | `:86-87`, `:95-96` |
| 6 | Colours, per appearance `light` then `dark` (the object emits `label`, `radii`, `font`, then `light`, then `dark`): for each token `t` in `tokens` order: colour object missing (`b.colors?.[t]` nullish): die `<bname>: no colour "<t>" in the contract`; value must be a string matching `/^#[0-9a-f]{6}$/` (lowercase only) else die `<bname>.<t>.<mode>: expected a lowercase 6-digit #rrggbb, got <JSON.stringify(value)>` (a missing appearance value prints `got undefined`, a number prints the number, a string prints it double-quoted); emit key `windowsRenames[t] ?? t`. | `:88-94`, `:47-52` |
| 7 | Radii (already known present): keys in fixed order `panel, card, figure, control, controlSm, micro, chip`; `platformValue(v)` = `v.windows` when `v` is truthy, an object and not an array, else `v`; emitted with `lit`: `null` or `undefined` become `null`, a string is single-quoted, anything else is `String(v)`. | `:99-101`, `:45`, `:69` |
| 8 | Font (already known present): emits `family` (`lit`), `fallbacks` (`(f.fallbacks ?? []).map(q)`), `labelStretch` (`lit`); `postScriptName` is NOT emitted on Windows. `label` is emitted with `q(b.label)`, so a missing label becomes the string `'undefined'` (no die). | `:98`, `:102-106` |
| 9 | Strings are single-quoted with `\` and `'` escaped (`q`). | `:68` |
| 10 | Check mode: read the existing output (missing counts as stale); compare with `\r\n` normalized to `\n` on both sides; if different die `brand-colors.generated.ts is STALE.` + newline + ``Run `npm run gen:brand` and commit the result.``; else print `gen-brand: up to date`. | `:118-132` |
| 11 | Write mode: write the file (LF, as generated, ending with `};` + `\n`) and print `gen-brand: wrote brand-colors.generated.ts`. | `:116`, `:133-136` |
| 12 | Error paths that are NOT a `die`: invalid JSON in the contract throws an uncaught `SyntaxError` (Node prints its own stack, no `gen-brand:` prefix, exit code 1); a missing `brands` object throws a `TypeError` in the invariant loop (`Object.entries(undefined)`); a non-array `platforms.windows.colors` throws a `TypeError` at `.map`. Check mode still runs every validation of steps 3 to 9 before comparing. | `:40`, `:60`, `:89` |

`die(m)` writes `gen-brand: <m>` + newline to stderr and exits 1 (`:23-26`). The invariant message is `` invariant violated \u2014 <bname>.<mode>: <t0> and <t1> are both <x>. `` + newline + two spaces + `why` (or empty) (`:63`).

The generated file's header (`src/shared/brand-colors.generated.ts:1-10`) names the generator, the contract, `DO NOT EDIT`, the `contract sha256:` line, and states that only the table is generated; the types and helpers stay in `theme-palette.ts`.

CI (`.github/workflows/ci.yml:52-57`) runs `npm run gen:brand:check` before typecheck, lint and tests, on every push to `main` and every PR. The macOS repo runs `swift Scripts/gen-brand.swift --check` (`macOS:.github/workflows/ci.yml:56-57`). Nothing automatically checks that the two repos' contracts are identical; the stamps make it visible by eye (`README.md:388-389`).

State machine of a contract change (REQUIRED as a workflow, now with three generators):

| State | Event | Guard | Action | Next |
|---|---|---|---|---|
| Contract edited | run a generator | invariants hold and every needed token present | write the platform table with the new stamp | Table regenerated |
| Contract edited | run a generator | invariant violated or token missing or bad hex | print `gen-brand: ...`, exit 1, write nothing | Contract edited |
| Contract edited | CI `--check` | table not regenerated | fail the job with the STALE message | Contract edited |
| Table regenerated | commit | | the stamp line changes in the diff | Landed on one repo |
| Landed on one repo | same edit lands in the other repo | | both stamps equal | Consistent |

### 2.3 Palette vocabulary (`src/shared/theme-palette.ts`)

`Palette` has 36 roles (`:39-87`). `ROLE_TO_TOKEN` (`:336-373`) maps each role to the CSS custom property the app stylesheet uses, and fixes the canonical ROLE order used by `themeStylesheet` and the tests. The generated table emits roles in `platforms.windows.colors` order instead. Both orders matter to the port (7.2).

| # (role order) | Role | CSS token | Contract token | Emission # | Group |
|---|---|---|---|---|---|
| 1 | `accent` | `accent` | `accent` | 1 | brand |
| 2 | `accentPress` | `accent-press` | `accentPress` | 2 | brand |
| 3 | `accentTint` | `accent-tint` | `accentTint` | 3 | brand |
| 4 | `accentInk` | `accent-ink` | `accentInk` | 4 | brand |
| 5 | `onAccent` | `on-accent` | `onAccent` | 5 | brand |
| 6 | `ink` | `ink` | `ink` | 6 | text |
| 7 | `ink2` | `ink-2` | `ink2` | 7 | text |
| 8 | `ink3` | `ink-3` | `ink3` | 8 | text |
| 9 | `hair` | `hair` | `hair` | 9 | lines |
| 10 | `hair2` | `hair-2` | `hair2` | 10 | lines |
| 11 | `controlBd` | `control-bd` | `controlBd` | 11 | lines |
| 12 | `focusRing` | `focus-ring` | `focusRing` | 34 | lines |
| 13 | `accentSoft` | `accent-soft` | `accentSoft` | 35 | lines |
| 14 | `surface` | `surface` | `surface` | 12 | surfaces |
| 15 | `surface2` | `surface-2` | `surface2` | 13 | surfaces |
| 16 | `ground` | `ground` | `ground` | 14 | surfaces |
| 17 | `fieldBg` | `field-bg` | `field` (renamed) | 15 | surfaces |
| 18 | `ok` | `ok` | `ok` | 16 | status |
| 19 | `okTint` | `ok-tint` | `okTint` | 17 | status |
| 20 | `okInk` | `ok-ink` | `okInk` | 18 | status |
| 21 | `draft` | `draft` | `draft` | 19 | status |
| 22 | `draftTint` | `draft-tint` | `draftTint` | 20 | status |
| 23 | `draftInk` | `draft-ink` | `draftInk` | 21 | status |
| 24 | `danger` | `danger` | `danger` | 22 | status |
| 25 | `dangerInk` | `danger-ink` | `dangerInk` | 24 | status |
| 26 | `dangerTint` | `danger-tint` | `dangerTint` | 23 | status |
| 27 | `dangerBd` | `danger-bd` | `dangerBd` | 36 | status |
| 28 | `noteBg` | `note-bg` | `noteBg` | 25 | callout |
| 29 | `noteBd` | `note-bd` | `noteBd` | 26 | callout |
| 30 | `noteFg` | `note-fg` | `noteFg` | 27 | callout |
| 31 | `cautBg` | `caut-bg` | `cautBg` | 28 | callout |
| 32 | `cautBd` | `caut-bd` | `cautBd` | 29 | callout |
| 33 | `cautFg` | `caut-fg` | `cautFg` | 30 | callout |
| 34 | `warnBg` | `warn-bg` | `warnBg` | 31 | callout |
| 35 | `warnBd` | `warn-bd` | `warnBd` | 32 | callout |
| 36 | `warnFg` | `warn-fg` | `warnFg` | 33 | callout |

`RADIUS_TO_TOKEN` (`:382-390`): `panel` `radius-panel`, `card` `radius-card`, `figure` `radius-figure`, `control` `radius-control`, `controlSm` `radius-control-sm`, `micro` `radius-micro`, `chip` `radius-chip`. `TYPE_TOKENS` (`:379`): `font-stack`, `label-stretch`. REQUIRED as data (06 keys its WPF resources by these names).

Other definitions:

| Item | Definition | Citation | Class |
|---|---|---|---|
| `BrandId` | `'shotAI' \| 'lfi'`; the persisted form, shared with macOS `BrandPref`; never rename | `:89-98` | REQUIRED |
| `Appearance` | `'light' \| 'dark'`, resolved from `ThemePref` (06) | `:116` | REQUIRED |
| `Brand` | `{ label, light, dark, radii, font }`; geometry and type depend on the brand only, never on the appearance | `:200-209` | REQUIRED |
| `BRAND_IDS` | `Object.keys(BRANDS)` = `['shotAI', 'lfi']`, the order Settings offers them | `:244` | REQUIRED |
| `DEFAULT_BRAND` | `'shotAI'` | `:247` | REQUIRED |
| `APP_LIGHT`, `APP_DARK` | aliases (same object) of `BRANDS.shotAI.light` and `.dark` | `:324`, `:327` | REQUIRED |
| `radiusCss(v)` | `v === null ? '999px' : `${v}px`` | `:166-168` | ELECTRON-ONLY (CSS); WPF capsule corners are 06's converter |
| font stack | `[...(family ? ['"' + family + '"'] : []), ...fallbacks].join(',')` | `:404`; same formula `src/shared/export-theme.ts:56-59` | REQUIRED (exports use it, 09) |
| `--label-stretch` | `labelStretch === null ? 'normal' : `${labelStretch}%`` | `:407` | ELECTRON-ONLY as CSS; the value (`null` or 62) is REQUIRED data |
| `themeStylesheet()` | a bare `:root{...}` block with the default brand's LIGHT tokens (pre-JS fallback) then one fully qualified `:root[data-brand="<id>"][data-theme="<appearance>"]{...}` block per brand and appearance; declarations colours (ROLE order), radii, then type | `:396-441` | ELECTRON-ONLY; replaced by 06's WPF resource dictionaries, applied before the first window shows |
| `RETIRED_GREYS` | `#1f2937`, `#374151`, `#6b7280`, `#e5e7eb`, `#cbd5e1`, `#14161f`, `#525a6e`, `#8b91a3` (the Tailwind and slide greys the exports carried before the ramp collapse) | `:477-486` | REQUIRED (guard) |
| `CARD_RADIUS_PX` | `BRANDS[DEFAULT_BRAND].radii.card` (10) | `:501` | REQUIRED |
| `IMAGE_RADIUS_PX` | `BRANDS[DEFAULT_BRAND].radii.figure` (8) | `:516` | REQUIRED |
| `hexNoHash(v)` | `/^#([0-9a-fA-F]{6})$/`; returns the 6 digits UPPERCASED; otherwise throws `Error` with message `` hexNoHash: expected #rrggbb, got <JSON.stringify(v)> `` | `:526-530` | REQUIRED (Open XML takes `RRGGBB`) |
| `COLOUR_TOKENS` | `Object.values(ROLE_TO_TOKEN)`: the 36 CSS token names in ROLE order | `:376` | REQUIRED as data (`PaletteRoles.All` tokens) |
| `RADIUS_TOKENS` | `Object.values(RADIUS_TO_TOKEN)`: the 7 radius token names | `:393` | REQUIRED as data (`PaletteRoles.Radii`) |
| `brandPalette(b, a)` typing | declared `(brand: BrandId, appearance: Appearance)` but coerces anyway, so a caller that casts garbage (`'nonsense' as never`) still gets the default brand's palette | `:314-316` | REQUIRED (the C# `For` takes `string?`) |

Note on `labelStretch` 62: the source comment calls 62% "Archivo's narrowest and its named Condensed instance" (`theme-palette.ts:189-190`). The bundled file's `fvar` table has `wdth` minimum 62, but its 9 named instances are all at `wdth` 100 (`Thin` 100 to `Black` 900, read in this verification pass); there is no named Condensed instance in the shipped file. 62 is therefore the axis minimum, and any WPF path that relies on named instances gets no condensed width (Q-INFRA-9).

`theme-palette.ts:229-235` has two elided code spans in a comment ("from  \u2014 the same file", "then run ."); documentation only, no behavior.

### 2.4 Brand narrowing

Four functions answer different questions about an untrusted value (`src/shared/theme-palette.ts:249-316`):

| Function | Returns | Rule | Use at |
|---|---|---|---|
| `isBrandId(v)` | bool | `typeof v === 'string' && Object.hasOwn(BRANDS, v)` (own keys only, case-sensitive) | the predicate everything else delegates to (#89, `bb0decc`) |
| `coerceBrand(v)` | `BrandId` | `isBrandId(v) ? v : DEFAULT_BRAND` | the app's own `settings.brand`; values crossing IPC from our own UI; `brandPalette`; exports asked for a brand |
| `pinnedBrand(v)` | `BrandId \| null` | `isBrandId(v) ? v : null` | EVERY read of `project.json` `theme` (#95, `77adda3`), so `pinnedBrand(theme) ?? appBrand` falls back to the app brand |
| `pinIsUnrecognised(v)` | bool | `typeof v === 'string' && v !== '' && !isBrandId(v)` | the View, Brand menu (#107, `38908cd`): nothing is ticked for such a project |
| `brandPalette(b, a)` | `Palette` | `BRANDS[coerceBrand(b)][a]` | never throws, never undefined |

Decision table (REQUIRED; every row is a test in 8):

| Input | `isBrandId` | `coerceBrand` | `pinnedBrand` | `pinIsUnrecognised` |
|---|---|---|---|---|
| `'shotAI'` | true | `'shotAI'` | `'shotAI'` | false |
| `'lfi'` | true | `'lfi'` | `'lfi'` | false |
| `'LFI'`, `'shotai'` (case) | false | `'shotAI'` | null | true |
| `'solarpunk'` (a future brand) | false | `'shotAI'` | null | true |
| `''` | false | `'shotAI'` | null | false |
| `'toString'`, `'constructor'`, `'valueOf'`, `'hasOwnProperty'`, `'isPrototypeOf'`, `'propertyIsEnumerable'`, `'toLocaleString'`, `'__proto__'`, `'__defineGetter__'` | false | `'shotAI'` | null | true |
| `undefined`, `null`, `42`, `true`, `{}`, `[]` | false | `'shotAI'` | null | false |

Why `pinnedBrand` and not `coerceBrand` at a manifest read: an unknown pin mapped to `DEFAULT_BRAND` would override the user's app brand (an LFI user would see a shotAI-violet document), and an unmatched `data-brand` would drop the page to the bare `:root` light palette even in dark mode (`77adda3`). Why `pinIsUnrecognised`: `pinnedBrand` returns null for both "no pin" and "pin I cannot read", and the menu ticked "App default" for the second, turning "clear the pin" into a click that looks like a no-op (`38908cd`). Absent and explicitly-default are different states (a project may pin `shotAI`; `772e381`). REQUIRED.

### 2.5 Palette invariants asserted by tests

| Rule | Formula | Citation |
|---|---|---|
| Every value is lowercase 6-digit hex | `/^#[0-9a-f]{6}$/` for every role, brand, appearance | `theme-palette.test.ts:157-165` |
| Every brand has the same role set | `sort(keys(BRANDS[id][a])) == sort(keys(ROLE_TO_TOKEN))` | `:167-176` |
| Dark field lighter than surface | `L(dark.fieldBg) > L(dark.surface)` per brand | `:178-187` |
| Text meets AA | for `role` in `ink, ink2, ink3` and `ground` in `ground, surface, surface2`: `CR(role, ground) >= 4.5`; and for pairs `(accentInk, accentTint), (okInk, okTint), (draftInk, draftTint), (dangerInk, dangerTint), (noteFg, noteBg), (cautFg, cautBg), (warnFg, warnBg)`: `CR >= 4.5`; for every brand and appearance | `:206-249` |
| Relative luminance | `c = byte / 255; lin(c) = c <= 0.03928 ? c / 12.92 : ((c + 0.055) / 1.055)^2.4; L = 0.2126 lin(R) + 0.7152 lin(G) + 0.0722 lin(B)` | `:257-264` |
| Contrast ratio | `CR(a, b) = (max(La, Lb) + 0.05) / (min(La, Lb) + 0.05)` | `:251-255` |
| Retired greys | distinct, lowercase 6-digit, used by no brand value, absent from `export-css.ts`, `export-docx.ts`, `export-pptx.ts`, `project.css` (comments stripped: block comments with `/\/\*[\s\S]*?\*\//g`, then line comments with `/(^\|[^:])\/\/.*$/gm` replaced by `$1`, which keeps `://` in URLs; the search is a plain substring `includes`, lowercase only) | `:38-39`, `:275-313` |
| Radius nesting | per brand: `figure < card`; `controlSm < control`; `micro <= controlSm`; `card <= panel`; every non-null role `> 0` | `:432-448` |
| Chip | default brand `chip === null`; every brand has all 7 radius roles | `:425-430`, `:450-457` |
| Export radius constants | `CARD_RADIUS_PX == default.card`, `IMAGE_RADIUS_PX == default.figure`, `IMAGE_RADIUS_PX < CARD_RADIUS_PX` | `:489-495` |
| Fonts | LFI stack starts `"Archivo",` and contains no `Segoe`; shotAI stack contains `Segoe` and no `Archivo`; label stretch `normal` for shotAI, `62%` for LFI | `:340-360` |

All REQUIRED (they are properties of the shared data plus the port's own table).

### 2.6 Settings: `settings.json`

#### 2.6.1 Location and file format

- Path: `path.join(app.getPath('userData'), 'settings.json')` (`src/main/settings.ts:125-127`). `userData` is `%APPDATA%\` + `productName` = `%APPDATA%\shotAI` (`package.json:3`). REQUIRED: the native app reads and writes the same file.
- Encoding UTF-8, no BOM. Written as `JSON.stringify(settings, null, 2)`: two-space indent, `": "` separator, LF, no trailing newline (`:197-203`). REQUIRED.
- Written only by a mutation; reading never writes. A fresh install has no file until the first mutation (often the startup update check stamping `lastUpdateCheckAt`, 2.8.4).
- Atomic: `writeFileAtomic` (`src/main/atomic-write.ts:37-51`): `mkdir -p` the directory, write `<file>.<pid>.tmp`, rename over the target with retries on `EPERM`, `EACCES`, `EBUSY` at delays `[10, 25, 50, 100, 200, 350, 600]` ms (7 retries, 8 attempts), delete the tmp on final failure. A non-retriable rename error throws at once (tmp deleted). A failure of the tmp `writeFile` itself (disk full) is outside the `try` and leaves the partial tmp behind (`atomic-write.ts:44-50`; 01 EDGE-MODEL-22 fixes this natively). On the first retry only (`attempt === 0`, `atomic-write.ts:26`) settings logs warn `` settings rename <code> \u2014 retrying (lock likely transient) `` under `projects` (`settings.ts:199-202`). REQUIRED (spec 01 `AtomicFile`).

#### 2.6.2 Load algorithm (`load()`, `:133-195`)

1. `raw = readFile(file, 'utf8')`; `parsed = JSON.parse(raw)`.
2. `carried = parsed && typeof parsed === 'object' && !Array.isArray(parsed) ? parsed : {}` (#92, `2b14b79`).
3. Result = `{ ...carried, <known keys coerced in literal order> }`. The spread keeps every carried key in its JS own-key position; a known key already present is overwritten IN PLACE (JS own-property semantics), and known keys absent from the file are appended in the literal order of the table below. "Own-key position" is not always file position: `JSON.parse` already moves array-index keys (`"0"`, `"42"`) ahead of every other key in ascending numeric order, so such an unknown key is written first (01 2.7 "Key order"). A `"__proto__"` key in the file is an ordinary own data property after `JSON.parse` and the spread, so it round-trips as data.
4. Any exception in 1 to 3 (missing file, unreadable file, invalid JSON, and `JSON.parse('null')` followed by `parsed.projectsDir` throwing `TypeError`) returns the full defaults object with NO carried keys. Nothing is logged.

Consequences: a corrupt file is silently replaced by defaults on the next write (its content is lost); a scalar or array file (`"x"`, `42`, `true`, `[1,2,3]`) loads as defaults but still "succeeds" (no exception), so `recents: []` etc. are written and no junk keys (`0`, `1`, ...) appear (`settings.test.ts:58-69`).

Every read accessor calls `load()` afresh (no in-memory state except the two caches in 2.6.5), so a hand edit made while the app runs is seen by the next read and merged by the next mutation.

#### 2.6.3 Schema (every known key, in literal and write order)

| # | Key | Type | Default | Load coercion (untrusted file value) | Setter coercion | Citation |
|---|---|---|---|---|---|---|
| 1 | `projectsDir` | string | `path.join(home, 'shotAI Projects')` = `%USERPROFILE%\shotAI Projects` | any string kept verbatim (including `''` and relative paths); else default | none (`persistProjectsDir(dir)`; 01's `setProjectsDir` creates the folder first) | `:129-131`, `:154-157`, `:226-230` |
| 2 | `recents` | string[] | `[]` | array: keep only string elements, in order, no cap, no dedupe; else `[]` | `setRecents(list)` verbatim; `addRecent(p)`: `[p, ...recents.filter(x => x !== p)].slice(0, 20)` | `:158-160`, `:241-258` |
| 3 | `sop` | object | `{ enabled: true, model: 'claude-sonnet-5', tone: 'professional', effort: 'medium', customInstructions: '' }` | `coerceSopSettings(parsed.sop)`: non-object treated as `{}`; `enabled` boolean else base; `model` in `SOP_MODELS` else base; `tone` in `SOP_TONES` else base; `effort` in `SOP_EFFORTS` else base; `customInstructions` string sliced to 2000 UTF-16 units else base. Output has exactly these 5 keys in this order; unknown keys inside `sop` are dropped | `setSopSettings(patch) = coerceSopSettings({...current, ...patch}, current)` (07) | `src/shared/sop.ts:95-123`, `settings.ts:161`, `:429-434` |
| 4 | `remoteVisible` | boolean | `false` | boolean else `false` | IPC: `value === true` | `:162`, `ipc.ts:654-664` |
| 5 | `captureScale` | number | `0.85` | `clampCaptureScale`: finite number: `min(1, max(0.5, v))`; else `0.85` | same clamp; IPC passes `NaN` for a non-number, which becomes `0.85` | `:31-35`, `:163`, `:301-308`, `ipc.ts:669-676` |
| 6 | `hasSeenTour` | boolean | `false` | boolean else `false` | IPC: `value === true` | `:164`, `:315-320` |
| 7 | `userName` | string | `''` | `coerceUserName`: string: `v.slice(0, 120)` (UTF-16 units; NOT trimmed despite the comment); else `''` | same; IPC passes `''` for a non-string | `:45-48`, `:165`, `:327-333` |
| 8 | `includeNameInReports` | boolean | `false` | boolean else `false` | IPC: `value === true` | `:166-167` |
| 9 | `archiveAgeDays` | number | `90` | `clampArchiveAge`: non-number or non-finite: `90`; `v <= 0`: `0` (never); else `min(1825, max(1, Math.round(v)))` | same; IPC passes `NaN` for a non-number, which becomes `90` | `:37-43`, `:50`, `:168`, `:376-382` |
| 10 | `theme` | string | `'system'` | exactly `'light'`, `'dark'` or `'system'`, else `'system'` | same | `:26-28`, `:169`, `:400-406` |
| 11 | `brand` | string | `'shotAI'` | `coerceBrand` (2.4) | same | `:170`, `:413-419` |
| 12 | `updateCheckEnabled` | boolean | `true` | boolean else `true` | IPC: `value === true` | `:171-172`, `:340-345`, `ipc.ts:743-746` |
| 13 | `lastUpdateCheckAt` | number (epoch ms) | `0` (never) | finite number kept verbatim (fractional and negative too); else `0` | `setLastUpdateCheckAt(Date.now())`, internal only | `:173-176`, `:352-357` |

Worked coercions (each is a test row in 8): `captureScale: 2` loads `1`; `0.1` loads `0.5`; `"0.7"` loads `0.85`; `archiveAgeDays: 5000` loads `1825`; `0.3` loads `1` (rounds to 0, then `max(1, 0)`); `2.5` loads `3` (JS `Math.round` is half-up); `-7` loads `0`; `"30"` loads `90`; `1e400` in the file parses to `Infinity` and loads `90`; `theme: "Dark"` loads `system`; `recents: ["a", 1, null, "b"]` loads `["a", "b"]`; `userName` of 130 characters loads the first 120.

Example of the file after the first mutation on a fresh machine (the `lastUpdateCheckAt` value is illustrative; `<user>` is a placeholder):

```json
{
  "projectsDir": "C:\\Users\\<user>\\shotAI Projects",
  "recents": [],
  "sop": {
    "enabled": true,
    "model": "claude-sonnet-5",
    "tone": "professional",
    "effort": "medium",
    "customInstructions": ""
  },
  "remoteVisible": false,
  "captureScale": 0.85,
  "hasSeenTour": false,
  "userName": "",
  "includeNameInReports": false,
  "archiveAgeDays": 90,
  "theme": "system",
  "brand": "shotAI",
  "updateCheckEnabled": true,
  "lastUpdateCheckAt": 1790000000000
}
```

Retired keys: `captureNoHide` was removed with its toggle (`f24b3dc`); since #100 a leftover value rides along as an unknown key and is never interpreted. No secret is ever in this file: the API key lives in `secrets.json` (08; `src/main/secrets.ts:1-9`).

#### 2.6.4 Mutation queue (`mutate`, `:205-220`)

```
queue = resolved
mutate(fn):
  run = queue.then(async () => { s = await load(); r = await fn(s); await save(s); return r })
  queue = run.then(noop, noop)     // one failure never breaks the chain
  return run
```

One chain for all settings mutations, separate from the project store's chain. Each mutation re-reads the file, so concurrent mutations cannot lose each other's updates and a hand edit made while running is merged. A mutation always writes, even when nothing changed. `addRecent` swallows its failure and logs warn `addRecent failed (non-fatal):` with the error under `projects` (`:241-252`), because recents are convenience bookkeeping that must never block opening a project or recording. Every other setter rejects to its caller. REQUIRED (the chain semantics; see 7.4 for the in-memory design).

#### 2.6.5 Synchronous caches

| Cache | Initial | Primed by | Updated by | Read by | Why synchronous | Citation |
|---|---|---|---|---|---|---|
| `remoteVisibleCache` | `false` (fully protected) | `getRemoteVisible()`, called after `createWindows()` at startup and by Settings | `setRemoteVisible(v)`, BEFORE the write, not rolled back if the write fails | `remoteVisibleNow()`: the shield restore in `remote-visibility.ts:72` and the overlay's `setContentProtection(!remoteVisibleNow())` (`RegionService.ts:90`) | read INSIDE the shield around every grab; an async hop would open a window in which the pill is capturable | `settings.ts:260-284`, `main.ts:500-502` |
| `captureScaleCache` | `0.85` | `getCaptureScale()`, called with `void` at startup before the capture controller is built | `setCaptureScale(v)`, clamped, BEFORE the write | `captureScaleNow()` in `downscalePng` (`CaptureController.ts:102`) | read at capture time without an async hop | `settings.ts:286-308`, `main.ts:415` |

Windows are constructed protected and relaxed only after the setting loads (fail-closed ordering; `main.ts:495-502`, `53045c6`; the startup `getRemoteVisible().then(applyRemoteVisibility)` swallows any error with `.catch(() => undefined)`, so a failure leaves the windows protected). A corrupt settings file yields `remoteVisible = false`. REQUIRED.

The `settings:set-remote-visible` handler sets the cache synchronously, awaits the write, and only THEN calls `applyRemoteVisibility(next)` on the open windows (`ipc.ts:653-665`). If the write rejects, the cache is already relaxed (the next grab's shield restore reads it) but the open windows were never re-applied, so the cache, the windows and the file can disagree three ways until restart (EDGE-INFRA-19; 11 EDGE-IPC-10).

#### 2.6.6 Derived value: report byline

`getReportByline()` = `name = s.userName.trim(); return s.includeNameInReports && name ? name : null` (`:384-393`). The single gate every exporter uses (09). REQUIRED.

#### 2.6.7 Accessors over IPC

Every setting has a get and a set channel (`src/shared/ipc.ts:495-503`, `src/main/ipc.ts:640-746`); each handler logs `ipc: <channel>` at debug through `devLog` (`ipc.ts:78-80`) and never the value. ELECTRON-ONLY (no IPC natively; the setter coercions of 2.6.3 that exist only because types are erased at the IPC boundary, such as `value === true`, become static types).

### 2.7 Logging (`src/main/logger.ts`, electron-log 5.4.4)

| Aspect | Behavior | Citation | Class |
|---|---|---|---|
| Init | `initLogging()` once (guarded), called at module load of `main.ts`, before anything else | `logger.ts:11-16`, `main.ts:29` | REQUIRED (first thing at startup; 03 step 1) |
| Levels | dev (`!app.isPackaged`): console and file `debug`; packaged: `info` | `:18-20` | REQUIRED as intent (7.5) |
| File | `userData/logs/shotai.log` = `%APPDATA%\shotAI\logs\shotai.log` | `:21-22` | REQUIRED |
| File line format | electron-log default `[{y}-{m}-{d} {h}:{i}:{s}.{ms}] [{level}]{scope} {text}` in LOCAL time; `{level}]` is replaced by `(level + ']')` padded right to 6 characters; `{scope}` is `(' (' + label + ')')` padded right to `maxLabelLength + 3` characters, where `maxLabelLength` is the longest scope label created so far (8, `projects`: all six scopes are created when `logger.ts` is imported, before `initLogging()` runs, so even the banner sees 8), and 11 spaces when unscoped; each line ends with `os.EOL` (`\r\n` on Windows); each write is a synchronous open, append, close (`writeFileSync` with flag `a`, mode `0o666`) | `node_modules/electron-log/src/node/transports/file/index.js:35`, `:43`, `core/transforms/format.js:68-98`, `core/scope.js:21-22`, `File.js:122-133` | REQUIRED (7.5.2) |
| Message text | the scoped call's arguments go through `util.formatWithOptions({depth: 5}, ...)` after the header is concatenated onto the first string argument (`concatFirstStringElements`), so the first message string acts as a `util.format` FORMAT STRING: a literal `%s`, `%d`, `%i`, `%f`, `%j`, `%o`, `%O`, `%c` consumes a following argument and `%%` prints `%`; further arguments are joined with one space; an `Error` argument is serialized as its `stack` (`Error: <message>` plus `\n    at ...` frames, LF inside the line) | `format.js:49-59`, `node/transforms/object.js:60-79`, `:109-112` | ELECTRON-ONLY quirk; natively message text is written verbatim (7.5.2) |
| Write failure | a failed append never throws; the registry reports `electron-log.transports.file: Can't write to <file>` to the console transport only, so the line is lost silently from the file | `file/index.js:27-31`, `File.js:134-140` | REQUIRED intent (logging never throws) |
| Console format | `[{h}:{i}:{s}.{ms}] [{level}]{scope} {text}` | `logger.ts:23` | ELECTRON-ONLY; native Debug builds write to the debugger output |
| Rotation | before each line: if `size > maxSize`, rename `shotai.log` to `shotai.old.log` (replacing it) and start fresh; if the rename throws, log `Could not rotate log` to the console and crop the file to its last `min(round(maxSize / 4), 256 * 1024)` bytes, rewritten as `[log cropped]` + `os.EOL` + tail + `os.EOL` (the tail is decoded as UTF-8 from an arbitrary byte offset, so it can start mid line and mid character). `size` is NOT re-read per line: it is one `statSync` at first use plus the bytes this process wrote, reset after a rotation, so another process's appends are invisible to the check. `maxSize = 5 * 1024 * 1024` pinned explicitly (#34, `9da70df`; library default `1024 ** 2`) | `logger.ts:25-31`, `file/index.js:39`, `:45-55`, `:66-79`, `File.js:28-30`, `:50-75`, `:146-158` | REQUIRED (bound); the in-process size counter is ELECTRON-ONLY (7.5.4 measures the file) |
| Bound | at most 2 files (`shotai.log` + `shotai.old.log`), each at most `maxSize` + one line, about 10 MB total | `logger.ts:25-31` | REQUIRED |
| Uncaught | `log.errorHandler.startCatching({ showDialog: false })` subscribes `process.on('uncaughtException')` and `process.on('unhandledRejection')` and logs, at `error`, unscoped, `Unhandled` + the error or `Unhandled rejection` + the error (a non-`Error` rejection reason becomes `new Error(JSON.stringify(reason))`), without a dialog; `main.ts:561-578` adds `renderer gone`, `child process gone` and `uncaught exception in main:` lines (so an uncaught exception in main is logged twice) | `logger.ts:33-34`, `node_modules/electron-log/src/node/ErrorHandler.js:80-112`, `core/Logger.js:65` | REQUIRED intent; native handlers are 03 7.4.9 |
| Banner | unscoped info `` shotAI starting \u2014 <platform>/<arch> · electron <v> · packaged=<bool> `` then `logs: <absolute log path>` | `:36-39` | REQUIRED (native text in 7.5.4) |
| Scopes | `main`, `capture`, `ipc`, `projects`, `claude`, `ocr` | `:42-47` | REQUIRED as categories (native list 7.5.3; `ipc` is ELECTRON-ONLY) |

What is logged today and what is not: file paths (projects, exports, archives, the log itself) are logged; secrets never are: the API key (`src/main/secrets.ts:6`), the Entra token cache (`src/main/entra/cache-plugin.ts:6`), the federation access token (`src/main/entra/federation.ts:55`), the BYO key (`src/main/entra/auth-core.ts:41`), credentials in general (`src/main/claude-service.ts:6`); the IPC layer logs channel names only (`ipc.ts:78-80`). Refused external links log the ORIGIN only (`ipc.ts:305`). 7.5.5 turns this into a rule.

### 2.8 Update check (#54)

#### 2.8.1 Design intent

Check and notify only, deliberately not an auto-updater (`src/main/update-check.ts:1-8`): it tells the user a newer version exists and hands them the release page. It is the app's ONLY unsolicited network call (`src/main/settings.ts:110-118`); the Settings hint says so to the user (2.8.8). ELECTRON-ONLY context: Squirrel auto-update was rejected for its `RELEASES` and `nupkg` publishing cost.

#### 2.8.2 Pure functions (`update-check.ts`)

| Function | Definition | Citation |
|---|---|---|
| `RELEASES_API` | `'https://api.github.com/repos/Armadillon44/shotAI/releases/latest'` (public repo, no auth) | `:13-15` |
| `CHECK_INTERVAL_MS` | `24 * 60 * 60 * 1000` = 86400000 | `:17-18` |
| `normalizeTag(tag)` | `tag.trim().replace(/^v/i, '')` (JS `trim`, then at most one leading `v` or `V`) | `:20-26` |
| `compareVersions(a, b)` | `nums(v) = normalizeTag(v).split('-')[0].split('.').map(s => Number.parseInt(s, 10) \|\| 0)`; for `i` in `0 .. max(len) - 1`: `d = (x[i] ?? 0) - (y[i] ?? 0)`; if `d != 0` return `d < 0 ? -1 : 1`; return 0. Missing segments are 0; any `-suffix` is ignored for ordering, so `1.2.0-rc1 == 1.2.0` | `:28-50` |
| `isNewer(current, latest)` | `compareVersions(latest, current) > 0` | `:52-55` |
| `shouldCheck(last, now, interval = CHECK_INTERVAL_MS)` | `!last` (0, undefined, NaN): true; `last > now` (clock moved back, synced file): true; else `now - last >= interval` | `:57-71` |
| `startupCheckDecision({enabled, lastCheckedMs, nowMs, intervalMs})` | `!enabled`: `{run: false, reason: 'disabled'}` (checked FIRST); `!shouldCheck(...)`: `{run: false, reason: 'throttled'}`; else `{run: true}` | `:73-92` |
| `pickRelease(body)` | `!body \|\| typeof body !== 'object'`: null; `draft === true \|\| prerelease === true`: null; `tag_name` or `html_url` not a string: null; `version = normalizeTag(tag_name)`; `!/^\d+(\.\d+)*/.test(version)` (anchored at the start only, ASCII digits): null; `!html_url.startsWith('https://github.com/')`: null; else `{version, url: html_url}`. An array body passes the first guard (`typeof [] === 'object'`) and is then refused by the string checks | `:94-117` |

`Number.parseInt(s, 10) || 0` semantics that the port must reproduce: skip leading JS whitespace; optional `+` or `-`; take the longest run of ASCII digits; no digits: NaN, which `|| 0` turns into 0; `-0` becomes 0. So `"0+abc"` is 0, `" 7"` is 7, `"-1"` is -1, `"1e3"` is 1, `"rc1"` is 0.

#### 2.8.3 `checkForUpdate({currentVersion, fetchImpl, url = RELEASES_API, timeoutMs = 10_000})`

NEVER throws and never reports an update it is not sure about (`:119-128`, body `:129-163`). Result type `UpdateCheckResult { available: boolean; version?: string; url?: string; error?: string }` (`src/shared/ipc.ts:143-156`): `available: false` means both "up to date" (no `error`) and "could not tell" (`error` set).

| Step | Outcome | Result | Citation |
|---|---|---|---|
| Start a 10 000 ms abort timer (`AbortController` plus `setTimeout`) | | | `:137-139` |
| `GET url` with headers `Accept: application/vnd.github+json`, `User-Agent: shotAI/<currentVersion>` | network error | `{available: false, error: <message>}` (`e.message` for an `Error`, else `String(e)`) | `:141-148`, `:157-159` |
| | aborted by the timer | `{available: false, error: 'timed out'}` (mapped only when the message is exactly `The operation was aborted.`) | `:159` |
| `!res.ok` (not 2xx) | e.g. 403 (the 60 per hour unauthenticated rate limit, shared behind NAT) | `{available: false, error: 'GitHub returned <status>'}` | `:149-152` |
| `res.json()` throws | body not JSON | `{available: false, error: <SyntaxError message>}` | `:153`, `:157-159` |
| `pickRelease(body) == null` | draft, prerelease, not a version, not on github.com, junk | `{available: false, error: 'no usable release in the response'}` | `:153-154` |
| `!isNewer(current, picked.version)` | same or older (a rollback is not an update) | `{available: false}` | `:155` |
| else | | `{available: true, version, url}` | `:156` |
| finally | clear the timer (the timer covers the body read too) | | `:160-162` |

Redirects are followed (fetch default). Node's `fetch` uses Node's own CA bundle and no system proxy, so on a proxied corporate network the check fails and is only logged.

#### 2.8.4 Startup flow (`main.ts:517-557`)

Runs fire-and-forget after the windows are created, the IPC handlers registered and auto-archive started.

| State | Event | Guard | Action | Next |
|---|---|---|---|---|
| Idle | app ready | | `decision = startupCheckDecision({enabled: getUpdateCheckEnabled(), lastCheckedMs: getLastUpdateCheckAt(), nowMs: Date.now()})` | Decided |
| Decided | | `!decision.run` | debug `update check skipped (<reason>)` (`disabled` or `throttled`) | Done |
| Decided | | `decision.run` | `result = checkForUpdate({currentVersion: app.getVersion(), fetchImpl: fetch})` | Checked |
| Checked | | | `setLastUpdateCheckAt(Date.now())`: stamp the attempt EITHER WAY, so a persistent failure retries tomorrow, not every launch | Stamped |
| Stamped | | `result.error` | info `update check could not complete: <error>` (never surfaced) | Done |
| Stamped | | `!result.available` | debug `update check: up to date (<app.getVersion()>)`: the RUNNING version, since an up-to-date result carries no version (nothing shown) | Done |
| Stamped | | available | info `update available: <version>`; `setPendingUpdate(result)` FIRST; then, if the project window exists, `webContents.send('update:available', result)` | Done |
| any | exception (for example the stamp write fails) | | warn `startup update check failed (non-fatal):` with the error; no notice | Done |

The timestamp is `Date.now()` after the request completes, not the start time. REQUIRED.

#### 2.8.5 The pending stash (`update-state.ts`)

`setPendingUpdate(r)`: `pending = r?.available ? r : null` ("up to date" and errors are not news); `getPendingUpdate()` returns it, safe before the check completes (`:14-25`). Why: `webContents.send` does not buffer, and in dev the check finished 40 s before the renderer's first IPC, so push-only silently lost the notice (`037858d`). The renderer both subscribes to `update:available` and pulls `update:pending` on mount; whichever arrives first sets the notice and the other is a no-op (`App.tsx:52-72`). REQUIRED.

#### 2.8.6 Manual check (`update:check`, `ipc.ts:754-767`)

Ignores the throttle AND the toggle; runs `checkForUpdate`; `await setLastUpdateCheckAt(Date.now())` so the next startup does not re-check; then `setPendingUpdate(result)` (a manual find is stashed; a manual "up to date" OR a manual error clears a stale stash). It does NOT push `update:available`, so it does not raise the Home notice in the running session (06 EDGE-HOME-23), although the handler's own comment says "a manual find should also raise the home notice" (`ipc.ts:763-765`); the code, not the comment, is the reference. If the stamp write rejects, the handler rejects before `setPendingUpdate` runs: the stash is unchanged and Settings shows the raw rejection text (the `catch` in `Settings.tsx:117-118` prints `e.message`, Electron's `Error invoking remote method 'update:check': ...` wrapper) instead of the check's result (EDGE-INFRA-45). REQUIRED (except the stamp-failure path, IMPROVEMENT in 7.6.4).

#### 2.8.7 The notice (owned by 06; strings quoted for completeness)

Info notice `` shotAI <version> is available. `` followed by one space and a link-styled button `Open the download page` that calls `openExternal(url)` when `url` is set; `×` dismisses for the session (`App.tsx:551-564`). A push with `available: false` clears it (`App.tsx:62-64`). An error of `pending()` is swallowed ("an update nudge is never worth an error", `:70`).

#### 2.8.8 Settings (owned by 06; semantics owned here)

Group heading `Updates`; toggle `Check for updates` with hint `Asks GitHub once a day, when shotAI starts, whether a newer version has been released, and tells you if one has. This is the only time shotAI contacts the internet on its own. It never installs anything by itself.`; button `↻ Check now` (`Checking…` while busy); messages `` shotAI <version> is available. `` (only when `r.available && r.url`; the release page then opens at once through `openExternal`, not awaited), `` Couldn't check: <error> ``, `You're up to date.`, or the raw exception message when the IPC call itself rejects (`Settings.tsx:89-122`, `:934-963`). A failed toggle write puts the switch back (`setUpdateCheck(!value)`, `Settings.tsx:95-103`). The toggle governs the STARTUP check only.

#### 2.8.9 `openExternal` allowlist (`ipc.ts:268-310`, handler `:271-310`)

The only egress to a browser. Decision:

| Step | Rule | Result |
|---|---|---|
| 1 | not a string | `false` |
| 2 | `new URL(url)` throws | `false` |
| 3 | `host = parsed.hostname.toLowerCase()`; `allowed = protocol === 'https:' && (host === 'anthropic.com' \|\| host.endsWith('.anthropic.com') \|\| host === 'github.com')` (`github.com` exactly, so never `raw.`, `objects.`, `codeload.github.com`) | |
| 4 | not allowed and `https:`: `cfg = await getFederationConfig()`; only when `cfg?.supportUrl` is set (federation configured; the `DEFAULT_SUPPORT_URL` fallback shown in Settings is NOT consulted here): `allowed = new URL(supportUrl).origin === parsed.origin` (a malformed configured URL throws inside a `try` and never widens) | |
| 5 | not allowed | warn `refused openExternal for non-allowlisted URL: <parsed.origin>` under `ipc`; `false`. `parsed.origin` is the WHATWG origin: `scheme://host[:port]` (port only when not the default) for `http`, `https`, `ws`, `wss`, `ftp`, and the literal string `null` for every other scheme (`file:`, `javascript:`, `mailto:`), so those log `...URL: null` |
| 6 | allowed | `await shell.openExternal(parsed.toString())`; `true`. A rejection of `shell.openExternal` propagates, so the invoke rejects instead of returning a boolean |

Before `037858d` the allowlist lacked `github.com`, so the v1.1.6 update download link was silently refused and logged (`ipc.ts:288-293`). REQUIRED.

Because step 3 tests only `protocol` and `hostname`, Electron ALLOWS user info and any port on an allowed host (`https://user@github.com/`, `https://github.com:8443/`); WHATWG parsing lowercases and punycodes the hostname and keeps a trailing dot (`github.com.` is refused).

### 2.9 Self-tests

| Mode | Trigger | Where | Behavior | Citation |
|---|---|---|---|---|
| Store self-test | `process.env.SHOTAI_SELFTEST === '1'` | after `app.whenReady`, the navigation lockdown, before any window | `runSelfTest()` then `app.quit()` (exit code 0 whatever the result) | `main.ts:403-408`, `selftest.ts:1-76` |
| Capture self-test | `process.env.SHOTAI_CAPTURE_TEST === '1'` (checked second) | same | `runCaptureTest()` then `app.quit()` | `main.ts:409-414`, `capture-selftest.ts:234-246` (body owned by 02) |

Both run after the single-instance lock (`main.ts:154`), so they exit early if the app is already running. Output goes to `console.log`/`console.error` (stdout/stderr), not the log file.

`runSelfTest()` (`selftest.ts:19-76`):

1. `origDir = getProjectsDir()`, `origRecents = getRecents()`; `testRoot = path.join(app.getPath('temp'), 'shotai-selftest-' + process.pid)`.
2. `setProjectsDir(testRoot)` (creates it and persists it to the REAL `settings.json`); print `[selftest] projectsDir  = <dir>`.
3. `created = createProject('Self Test Project')`; read its `project.json`; `shots = isDir(created/shots)`; `exp = isDir(created/export)`; `created2 = createProject('Self Test Project')`; `special = createProject('Flow: A/B - C*')`; `recents = listRecentProjects()`.
4. Print, in order (console.log joins arguments with one space): `[selftest] created       = <path>`; `[selftest] manifest      = v<version> "<title>" steps=<n>`; `[selftest] folder name   = <JSON string of the folder name>`; `[selftest] shots/export  = <bool> <bool>`; `[selftest] unique folder = <bool>`; `[selftest] special title = <JSON title> folder <JSON folder>`; `[selftest] recents       = <n>`.
5. PASS iff `manifest.version === 1 && manifest.createdWith === 'shotAI' && manifest.title === 'Self Test Project' && UUID_RE.test(basename(created)) && shots && exp && created.path !== created2.path && special.title === 'Flow: A/B - C*' && UUID_RE.test(basename(special)) && recents.length >= 3`, with `UUID_RE = /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i`. Print `[selftest] PASS` or `[selftest] FAIL`.
6. On exception: `console.error('[selftest] ERROR', e)` and no PASS/FAIL line.
7. Finally: `persistProjectsDir(origDir)` (restore without creating), `setRecents(origRecents)`, `rm -rf testRoot` (errors ignored).

Line 2 prints the value READ BACK through `ps.getProjectsDir()` after the set, not the `testRoot` variable (`selftest.ts:29`). The manifest line uses `util.format` specifiers (`v%d "%s" steps=%d`), so a non-numeric `version` would print `NaN`. If the `finally` block itself rejects (the settings restore write fails), `runSelfTest()` rejects, `app.quit()` at `main.ts:406` is never reached, and the rejection goes to electron-log's unhandled-rejection handler; whether the process then keeps running is UNVERIFIED.

Because steps 2 and 7 write the user's real settings, a crash between them leaves `projectsDir` pointing at a deleted temp folder (`docs/HARDENING-PLAN.md:33` warns it "IS a live run that mutates real projectsDir/recents"). The folder assertions were rewritten when folders became UUID-named (same note).

Other environment switches in Electron (not self-tests): `SHOTAI_ENABLE_GPU` (03), `SHOTAI_NO_SANDBOX` (ELECTRON-ONLY), `ANTHROPIC_API_KEY` fallback (08).

### 2.10 Archivo and the SIL Open Font License

| Fact | Value | Citation |
|---|---|---|
| File | `src/renderer/fonts/Archivo.ttf`, one variable TTF, 658 596 bytes, sha256 `0e094a7d3c7c4c25cf1310c4b30014f1dae9332220b1c2c88f4fa996f0b05053`, byte-identical to the file macOS ships | `285403d` |
| Axes | `wght` 100 to 900, default 600; `wdth` 62 to 125, default 100 (read from its `fvar` table, re-read in verification). The default instance is SemiBold, so a bare request for "Archivo" renders SemiBold. Name table: family `Archivo`, full name `Archivo SemiBold`, PostScript name `Archivo-SemiBold`; `head.fontRevision` 2.001 | `285403d`, `theme-palette.test.ts:362-374` |
| Named instances | 9, all at `wdth` 100: `Thin` 100, `ExtraLight` 200, `Light` 300, `Regular` 400, `Medium` 500, `SemiBold` 600, `Bold` 700, `ExtraBold` 800, `Black` 900. No condensed named instance | `fvar` table (verification read) |
| App use | `@font-face { font-family: 'Archivo'; src: url('../fonts/Archivo.ttf') format('truetype-variations'); font-weight: 100 900; font-stretch: 62% 125%; font-style: normal; font-display: swap; }`; the declared ranges are load-bearing | `project.css:14-21` |
| Export use | the HTML export NAMES the face and never embeds it (as base64 about 0.84 MB against a measured 0.8 to 1.5 MB Freshservice paste budget); only the PDF print copy gets a `file:` `@font-face` (09) | `paths.ts:11-28`, `export.ts:614-641` |
| Why Archivo | the LFI guide's Acumin Variable Concept is an Adobe Fonts family that cannot be embedded in a distributed app; Archivo is the same Franklin lineage with a real width axis | `285403d` |
| Licence file | `src/renderer/fonts/OFL.txt`: line 1 `Copyright 2020 The Archivo Project Authors (https://github.com/Omnibus-Type/Archivo)`, then `This Font Software is licensed under the SIL Open Font License, Version 1.1.` and the full OFL 1.1 text. No Reserved Font Name is declared (line 1 has no "with Reserved Font Name" clause) | `OFL.txt:1-93` |
| Shipping | `forge.config.ts:112-121` places both `Archivo.ttf` and `OFL.txt` in `resources/` of the installed app (#93, `c070095`, measured by a real `npm run make`) | |
| README | the MIT licence does not cover Archivo; OFL ships with the font; "if you repackage this app, keep them together" | `README.md:391-401` |

OFL 1.1 conditions that bind the native package (`OFL.txt:47-78`): (1) the font may not be sold by itself; (2) bundling and redistribution are allowed provided each copy contains the copyright notice and the licence, as a stand-alone text file or in easily viewable metadata; (3) no Modified Version may use a Reserved Font Name (none declared); (4) the authors' names may not promote a Modified Version; (5) the font, modified or not, must stay under the OFL; documents created with it are not affected. The licence terminates if a condition is not met (`:80-82`).

### 2.11 Log lines owned by this spec

| Level | Scope | Text | Citation |
|---|---|---|---|
| info | none | `` shotAI starting \u2014 <platform>/<arch> · electron <v> · packaged=<bool> `` | `logger.ts:36-38` |
| info | none | `logs: <path>` | `:39` |
| warn | projects | `` settings rename <code> \u2014 retrying (lock likely transient) `` | `settings.ts:201` |
| warn | projects | `addRecent failed (non-fatal):` + error | `:250` |
| debug | main | `update check skipped (<disabled\|throttled>)` | `main.ts:529` |
| info | main | `update check could not complete: <error>` | `:540` |
| debug | main | `update check: up to date (<running app version>)` | `:544` |
| error | none | `Unhandled <error>`, `Unhandled rejection <error>` (electron-log's handler; 03 owns the native crash lines) | `ErrorHandler.js:103-112` |
| info | main | `update available: <version>` | `:547` |
| warn | main | `startup update check failed (non-fatal):` + error | `:556` |
| warn | ipc | `refused openExternal for non-allowlisted URL: <origin>` | `ipc.ts:305` |
| debug | ipc | `ipc: settings:*`, `ipc: update:pending`, `ipc: update:check`, `ipc: shell:open-external` | `ipc.ts:640-767` (ELECTRON-ONLY) |
| warn | projects | `settings.json unreadable, using defaults: <exception type>` (native, IMPROVEMENT) | 7.4.3 |
| warn | projects | `settings.json is not a settings object, using defaults` (native, IMPROVEMENT) | 7.4.3 |
| warn | projects | `settings.json is not a settings object, writing the saved settings over it` (native, added in WP-A10) | 7.4.3 |
| warn | projects | `settings: projectsDir is not an absolute path, using the default` (native, Q-INFRA-3) | 7.4.3 |
| debug | projects | `settings.json.bad could not be written (non-fatal):` + error (native, added in WP-A10) | 7.4.3 |
| debug | projects | `settings: a pending change no longer applies; its queued write decides` + error (native, added in WP-A10) | 7.4.3 |
| warn | main | `log: <n> line(s) dropped`: the lines lost since the last report, because the queue was full, a batch could not be written or a line could not be built (native, IMPROVEMENT, added in WP-A11) | 7.5.4 |

---

## 3. Constants

| Name | Value | Unit | Meaning | Citation |
|---|---|---|---|---|
| Contract path | `contract/brand.json` | | the brand contract, read in place | `gen-brand.mjs:19` |
| Contract sha256 | `c945d17c1c348c8007d16cdd6dc47be7040de47f7071932dfc5d413e6baa64ff` | hex | canonical LF hash stamped in every generated table | `brand-colors.generated.ts:2` |
| Stamp line | `// contract sha256: <hex>` | | exact header line, all three generators | `gen-brand.mjs:72` |
| `windowsRenames` | `{ "field": "fieldBg" }` | | contract token to Windows role | `brand.json:45-47` |
| Windows radius of shotAI `control` | `8` (macOS `6`) | px | a value the platforms have not agreed on | `brand.json:417-420` |
| Hex rule (contract) | `^#[0-9a-f]{6}$` | regex | lowercase only | `gen-brand.mjs:48` |
| Hex rule (`hexNoHash`) | `^#([0-9a-fA-F]{6})$` | regex | either case in, UPPERCASE out | `theme-palette.ts:527` |
| Emitted brands | `shotAI`, `lfi` | ordered | the fixed generator list and `BRAND_IDS` order | `gen-brand.mjs:86`, `theme-palette.ts:244` |
| `DEFAULT_BRAND` | `shotAI` | | fallback brand | `theme-palette.ts:247` |
| Brand labels | `shotAI`, `LFI` | | shown in Settings | `brand.json:254`, `:440` |
| Radius keys | `panel, card, figure, control, controlSm, micro, chip` | ordered | emitted order | `gen-brand.mjs:99` |
| `CARD_RADIUS_PX` | 10 | px | default brand card radius | `theme-palette.ts:501` |
| `IMAGE_RADIUS_PX` | 8 | px | default brand figure radius | `:516` |
| Capsule CSS | `999px` | | `radiusCss(null)` | `:167` |
| LFI `labelStretch` | 62 | percent | Archivo's narrowest width (the `wdth` axis minimum; the shipped file has no named Condensed instance, 2.3) | `brand.json:618` |
| `RETIRED_GREYS` | `#1f2937 #374151 #6b7280 #e5e7eb #cbd5e1 #14161f #525a6e #8b91a3` | | must never reappear | `theme-palette.ts:477-486` |
| AA threshold | 4.5 | ratio | WCAG 2.x AA, normal text | `theme-palette.test.ts:215` |
| Luminance constants | `0.03928`, `12.92`, `0.055`, `1.055`, `2.4`, `0.2126`, `0.7152`, `0.0722` | | WCAG relative luminance | `:258-264` |
| Settings file | `%APPDATA%\shotAI\settings.json` | path | app settings | `settings.ts:125-127` |
| Tmp name | `<file>.<pid>.tmp` | | atomic write sibling | `atomic-write.ts:43` |
| `RENAME_RETRY_DELAYS_MS` | `[10, 25, 50, 100, 200, 350, 600]` | ms | rename retry schedule | `atomic-write.ts:11` |
| Retriable rename codes | `EPERM`, `EACCES`, `EBUSY` | | transient Windows locks | `atomic-write.ts:24` |
| JSON indent | 2 | spaces | `JSON.stringify(s, null, 2)` | `settings.ts:199` |
| Default projects folder name | `shotAI Projects` | | under `%USERPROFILE%` | `settings.ts:130` |
| `MAX_RECENTS` | 20 | entries | recents cap in `addRecent` | `settings.ts:123` |
| `CAPTURE_SCALE_MIN` / `MAX` / `DEFAULT` | 0.5 / 1 / 0.85 | factor | screenshot downscale | `src/shared/project.ts:18-20` |
| `ARCHIVE_AGE_DEFAULT` | 90 | days | auto-archive age | `settings.ts:50` |
| Archive age range | 0 (never), else 1 to 1825 | days | clamp | `:39-43` |
| `userName` cap | 120 | UTF-16 units | display name | `:47` |
| `SOP_CUSTOM_INSTRUCTIONS_MAX` | 2000 | UTF-16 units | custom instructions cap | `sop.ts:78` |
| Default `sop` | `enabled true, model 'claude-sonnet-5', tone 'professional', effort 'medium', customInstructions ''` | | SOP defaults | `sop.ts:95-101` |
| `theme` values | `light`, `dark`, `system` (default) | | appearance preference | `settings.ts:26-28`, `project.ts:530` |
| Log dir and files | `%APPDATA%\shotAI\logs\shotai.log`, `shotai.old.log` | | active and archived log | `logger.ts:21-22`, `file/index.js:49` |
| Log `maxSize` | `5 * 1024 * 1024` = 5242880 | bytes | rotate when the file exceeds it | `logger.ts:31` |
| Crop size on failed rotation | `min(round(maxSize / 4), 256 * 1024)` = 262144 | bytes | tail kept | `file/index.js:52-53` |
| Crop marker | `[log cropped]` | | first line after a crop | `File.js:54` |
| Level widths | `(level + ']')` padded to 6 | chars | alignment | `format.js:98` |
| Scope width | `max label length + 3` = 11 | chars | alignment | `format.js:80-82` |
| Log levels | debug (dev), info (packaged) | | minimum level | `logger.ts:18-20` |
| Scope labels | `main`, `capture`, `ipc`, `projects`, `claude`, `ocr` | | categories | `logger.ts:42-47` |
| Line ending | `\r\n` | | `os.EOL` on Windows | `File.js:123` |
| `RELEASES_API` | `https://api.github.com/repos/Armadillon44/shotAI/releases/latest` | URL | update endpoint | `update-check.ts:14-15` |
| `CHECK_INTERVAL_MS` | 86400000 | ms | once a day | `:18` |
| Update timeout | 10000 | ms | abort a hung request | `:137` |
| `Accept` | `application/vnd.github+json` | header | GitHub media type | `:144` |
| `User-Agent` | `shotAI/<currentVersion>` | header | identifies the app | `:146` |
| Release URL prefix | `https://github.com/` | | the only accepted `html_url` prefix | `:115` |
| Version regex | `^\d+(\.\d+)*` (ASCII digits, start-anchored only) | regex | a tag that is a version | `:114` |
| Update error texts | `GitHub returned <status>`, `no usable release in the response`, `timed out` | | `UpdateCheckResult.error` | `:151`, `:154`, `:159` |
| IPC channels | `update:available`, `update:pending`, `update:check`, `settings:get-update-check`, `settings:set-update-check` | | ELECTRON-ONLY | `src/shared/ipc.ts:250-253` |
| Allowlisted hosts | `anthropic.com`, `*.anthropic.com`, `github.com` (exact) | | `openExternal` | `ipc.ts:285-287` |
| Self-test env vars | `SHOTAI_SELFTEST=1`, `SHOTAI_CAPTURE_TEST=1` | | self-test triggers (exact `'1'`) | `main.ts:403`, `:409` |
| Self-test root | `%TEMP%\shotai-selftest-<pid>` | path | temp projects dir | `selftest.ts:22-25` |
| `UUID_RE` | `^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$` case-insensitive | regex | project folder name | `selftest.ts:10` |
| Self-test titles | `Self Test Project` (twice), `Flow: A/B - C*` | | fixtures | `selftest.ts:31`, `:37-38` |
| Archivo file | 658596 bytes, sha256 `0e094a7d3c7c4c25cf1310c4b30014f1dae9332220b1c2c88f4fa996f0b05053` | | brand face | `285403d` |
| Archivo axes | `wght` 100 to 900 (default 600), `wdth` 62 to 125 (default 100) | | variable axes | `285403d` |
| OFL | SIL Open Font License 1.1, no Reserved Font Name | | licence of the face | `OFL.txt:1-9` |

---

## 4. Invariants

**INV-INFRA-1. The brand contract is read in place, never copied, never converted, and every generated table stamps `sha256(canonical LF bytes)` of it.** Why: the stamp is the only visible proof that both repos hold the same contract; a CRLF worktree changed the digest and the printed remedy would have committed the wrong one (#84, #88, #96). Citation: `gen-brand.mjs:28-39`, `.gitattributes:4-14`, `macOS:Scripts/gen-brand.swift:85-91`. Test: `BrandContractTests.StampMatchesCanonicalHash` (the C# stamp equals the recomputed hash and equals the TypeScript stamp while it exists); `GenBrandTests.HashIgnoresCrlf` (CRLF input yields `c945d17c…`).

**INV-INFRA-2. A generated palette table is never edited by hand, and CI fails when it is stale.** Why: every drift found in the 2026-09-14 cross-check was one value edited on one side (#84). Citation: `gen-brand.mjs:118-132`, `ci.yml:52-57`. Test: `BrandContractTests.GeneratedFileIsCurrent` (Linux) and the `--check` CI step; `GenBrandTests.CheckReportsStale`.

**INV-INFRA-3. A contract that violates a declared invariant cannot be generated; in dark mode `field` and `surface2` differ for every brand.** Why: Windows shipped `fieldBg == surface2` in dark and a text field on a card stopped reading as an input (`d1d980b`). Citation: `gen-brand.mjs:58-66`, `brand.json:48-58`. Test: `GenBrandTests.InvariantViolationFails` (message text exact), `BrandPaletteTests.DarkFieldLighterThanSurface`.

**INV-INFRA-4. Contract colours are lowercase 6-digit `#rrggbb`; the Windows generator rejects anything else.** Why: comparisons never fail on case, and the invariant compare is a raw string compare. Citation: `gen-brand.mjs:47-52`, `theme-palette.test.ts:157-165`. Test: `GenBrandTests.RejectsUppercaseOrShortHex`, `BrandPaletteTests.AllValuesLowercaseHex`.

**INV-INFRA-5. The Windows table takes the `windows` value of a platform-split value, and emits exactly the tokens of `platforms.windows.colors` with `windowsRenames` applied.** Why: shotAI's control radius is 8 on Windows and 6 on macOS and no single mapping fits (`d1d980b`). Citation: `gen-brand.mjs:42`, `:45`, `:92`. Test: `GenBrandTests.PlatformValueTakesWindows`, `BrandParityWithElectronTests.ValuesEqualTypeScriptTable`.

**INV-INFRA-6. Every brand defines every one of the 36 roles in both appearances, and every radius role; geometry and type depend on the brand only.** Why: a missing role falls back to whatever the cascade left, "one stubborn element that refuses to follow the brand". Citation: `theme-palette.test.ts:167-176`, `:425-430`, `:477-487`. Test: `BrandPaletteTests.SameRolesEverywhere`, `.EveryRadiusRole`, `.OneRadiiPerBrand`.

**INV-INFRA-7. Text roles meet WCAG AA (4.5:1) on every ground they are drawn on, for every brand and appearance.** Why: `ink3` never met AA and only surfaced when printed documents aimed text at it; macOS's LFI dark value passed on the page and failed on a card (`4def683`). Citation: `theme-palette.test.ts:206-264`. Test: `BrandPaletteTests.TextMeetsAa` (Theory over brand x appearance).

**INV-INFRA-8. Radii nest: `figure < card`, `controlSm < control`, `micro <= controlSm`, `card <= panel`, every non-null radius `> 0`, and the default brand's chip is `null` (capsule).** Why: "the default brand is 10px everywhere" produced a wrong implementation; a relationship cannot be flattened by accident (#77 phase 2). Citation: `theme-palette.test.ts:432-457`. Test: `BrandPaletteTests.RadiiNest`, `.DefaultChipIsCapsule`.

**INV-INFRA-9. [SECURITY] Brand narrowing accepts only the table's own keys, ordinal and case-sensitive; no inherited or reflective member ever counts as a brand.** Why: `in` accepted every `Object.prototype` member, `coerceBrand('toString')` returned `'toString'`, the palette lookup returned undefined and the font stack threw, while a legitimate unknown was discarded (#89, #97). Citation: `theme-palette.ts:249-274`. Test: `BrandNarrowingTests.RejectsPrototypeKeys` (all nine keys), `.StillRejectsFutureBrand`, `.RejectsNonStrings`.

**INV-INFRA-10. File-sourced brand values are narrowed with `PinnedBrand` (unknown means absent, fall back to the app brand); only the app's own setting and our own UI input use `CoerceBrand`.** Why: coercing a manifest pin to the default overrides the user's brand and, in Electron, dropped the page to the light palette in dark mode (#95, #106). Citation: `theme-palette.ts:276-291`. Test: `BrandNarrowingTests.DecisionTable`; 01's theme conformance cases.

**INV-INFRA-11. `PinIsUnrecognised(v)` is true exactly for a non-empty string that is not a brand id.** Why: the menu must tick nothing for such a project, or "clear the pin" looks like a no-op (#107, #115). Citation: `theme-palette.ts:293-311`. Test: `BrandNarrowingTests.DecisionTable` (column 4).

**INV-INFRA-12. The retired greys appear in no brand value and in no export template or theme resource.** Why: three neutral ramps once coexisted inside single documents; the guard fails on the specific values so a partial revert is caught (#77). Citation: `theme-palette.ts:469-486`, `theme-palette.test.ts:266-314`. Test: `BrandPaletteTests.RetiredGreysDistinctAndUnused`, `ExportSourceGuardTests.NoRetiredGreys` (source scan of 09's templates and 06's XAML).

**INV-INFRA-13. `HexNoHash` returns uppercase `RRGGBB` for a `#rrggbb` input of either case and throws for anything else.** Why: `docx` renders an invalid colour as black, a bug that looks like a design decision. Citation: `theme-palette.ts:518-530`. Test: `BrandPaletteTests.HexNoHash*`.

**INV-INFRA-14. Unknown root keys in `settings.json` survive every write, in their original position; a non-object file carries no keys.** Why: a rollback to an older build deleted `brand` on its first write (#92, #100); a scalar spreads into indexed junk. Citation: `settings.ts:137-153`. Test: `SettingsStoreTests.KeepsUnknownKey`, `.NoJunkForNonObject` (5 shapes).

**INV-INFRA-15. Every known key is coerced on every load and every write, so a bad value for a key this build owns is repaired.** Citation: `settings.ts:148-177`. Test: `SettingsStoreTests.RepairsKnownKey`, `SettingsCoercionTests` (the 2.6.3 table).

**INV-INFRA-16. Settings writes are serialized in call order and atomic; a failed write never corrupts the file and never blocks later writes.** Why: concurrent mutators must not lose updates; an interrupted write must not reset the projects folder and recents. Citation: `settings.ts:1-6`, `:205-220`, `atomic-write.ts`. Test: `SettingsServiceTests.ConcurrentUpdatesNoLostWrite`, `.FailureDoesNotBreakQueue`, 01's `AtomicFileTests`.

**INV-INFRA-17. Loading settings never throws: a missing, unreadable, non-JSON or `null` file yields the defaults.** Citation: `settings.ts:178-194`. Test: `SettingsStoreTests.NotJsonYieldsDefaults`, `.NullFileYieldsDefaults`.

**INV-INFRA-18. [SECURITY] `RemoteVisibleNow()` and `CaptureScaleNow()` are synchronous, lock-free and never block; `RemoteVisibleNow()` is `false` until settings have loaded and whenever the file is unreadable.** Why: the shield reads it around every grab; an async read would open a window in which the pill is capturable; `false` is fully protected (`53045c6`). Citation: `settings.ts:260-308`. Test: `SettingsServiceTests.CachesReadCurrentSynchronously`, `.RemoteVisibleDefaultsProtected`.

**INV-INFRA-19. [SECURITY] `settings.json` never contains a secret (API key, token, assertion) and is never the source of federation policy.** Citation: `secrets.ts:1-9`; 08 INV-AUTH-1 (policy source), INV-AUTH-19 (tokens and keys never in `settings.json`). Test: `SettingsSchemaTests.KnownKeysAreNonSecret` (the key list is exactly the 13 of 2.6.3) and code review.

**INV-INFRA-20. On-disk logs are bounded: at most `shotai.log` and `shotai.old.log`, each at most 5 MiB plus one write batch.** Why: #34. Citation: `logger.ts:25-31`. Test: `RotatingFileSinkTests.RotatesPastMax`, `.NeverMoreThanTwoFiles`.

**INV-INFRA-21. [SECURITY] No log line ever contains a credential or personal content (the never-log list of 7.5.5).** Why: logs are sent to support; secrets are documented as never logged. Citation: `secrets.ts:6`, `entra/cache-plugin.ts:6`, `entra/federation.ts:55`, `claude-service.ts:6`; 08 INV-AUTH-6, INV-AUTH-18, INV-AUTH-19, INV-AUTH-31. Test: `LogPrivacyTests.CanaryNeverLogged` (a canary API key, token and user name run through the settings, auth-status and SOP-request paths with a capturing sink; no line contains any canary).

**INV-INFRA-22. Logging initializes before anything else and an exception on any thread reaches the log.** Why: a window vanishing was indistinguishable from a clean quit (`772e381`). Citation: `logger.ts:33-34`, `main.ts:29`, `:561-578`. Test: 03's `CrashLoggingTests`; `FileLoggerProviderTests.FlushWritesSynchronously`.

**INV-INFRA-23. [SECURITY] The startup update check is the only network request the app makes without a user action, and it does not run when `updateCheckEnabled` is false; the manual check runs regardless of the toggle.** Why: the Settings hint promises it to the user; managed deployments want it switchable (#54). Citation: `settings.ts:110-118`, `update-check.ts:73-92`, `main.ts:517-530`, `ipc.ts:754-767`. Test: `UpdateServiceTests.DisabledMakesNoRequest`, `.ManualIgnoresToggle`; AC-INFRA-24 (network trace).

**INV-INFRA-24. The update check never throws to its caller and never reports an update it is not sure of: drafts, prereleases, non-version tags and URLs not starting `https://github.com/` are refused.** Why: an rc must never be offered to someone on a stable build; a malformed body must not become a notice. Citation: `update-check.ts:102-117`, `:119-163`. Test: `UpdateCheckTests.PickRelease*`, `.CheckForUpdate*`.

**INV-INFRA-25. The automatic check runs at most once per `CHECK_INTERVAL_MS`, measured from `lastUpdateCheckAt`, which is stamped after every completed attempt including failures; a stored time in the future allows a check.** Why: 60 requests per hour per IP is shared behind a corporate NAT; a clock moved backwards must not lock the check out (#54). Citation: `update-check.ts:57-71`, `main.ts:536-538`. Test: `UpdateCheckTests.ShouldCheck*`, `UpdateServiceTests.StampsEvenOnError`.

**INV-INFRA-26. An available update is stored in `Pending` before `UpdateAvailable` is raised, and only an available result is stored.** Why: the push raced the UI and was silently lost (`037858d`). Citation: `update-state.ts`, `main.ts:548-554`. Test: `UpdateServiceTests.PendingSetBeforeEvent`, `.OnlyAvailableIsPending`.

**INV-INFRA-27. Versions compare numerically per segment with missing segments as 0 (`1.1.10 > 1.1.9`, `1.1 == 1.1.0`).** Citation: `update-check.ts:28-50`. Test: `UpdateCheckTests.CompareVersions*`.

**INV-INFRA-28. A hung update request cannot delay or wedge startup: it runs off the UI thread and is cancelled after 10 s with the error `timed out`.** Citation: `update-check.ts:134-139`, `:159`. Test: `UpdateCheckTests.TimesOut` (fake handler that never completes, `FakeTimeProvider`).

**INV-INFRA-29. [SECURITY] A URL is opened in the browser only if it is `https` and its host is `anthropic.com`, a subdomain of it, exactly `github.com`, or its origin equals the administrator's SupportUrl origin; anything else is refused and logged by origin only.** Citation: `ipc.ts:268-310`; 06 INV-HOME-31. Test: `ExternalLinkPolicyTests` (table in 8).

**INV-INFRA-30. A self-test never opens a window and never leaves the user's settings changed.** Why: the Electron self-test wrote the real `settings.json` and relied on `finally` to restore it. Citation: `selftest.ts:19-76`. Test: `StoreSelfTestTests.DoesNotTouchUserSettings`; AC-INFRA-27.

**INV-INFRA-31. Every installed copy of an Archivo font file is accompanied by `OFL.txt` in the same folder, byte-identical to `src/renderer/fonts/OFL.txt`.** Why: OFL 1.1 condition 2; the source tree is not what a user is handed (#93). Citation: `OFL.txt:56-61`, `forge.config.ts:112-121`. Test: `FontPackagingTests.OflBesideEveryFont` (App.Tests, on the build output and on the installer payload in 12).

**INV-INFRA-32. The bundled variable `Archivo.ttf` is byte-identical to the file Electron and macOS ship, and exports never embed a font as base64.** Why: one face across platforms; the paste budget (#77 phase 3). Citation: `285403d`, `paths.ts:11-28`. Test: `FontPackagingTests.ArchivoMatchesElectronHash`; 09's export tests ban `data:font` and `@font-face` in the kept HTML.

**INV-INFRA-33. Brand text is never rendered at the variable default instance by accident: `FontWeight.Normal` in the brand face renders weight 400.** Why: Archivo's default instance is SemiBold (`285403d`). Citation: `project.css:14-21`, `theme-palette.test.ts:362-374`. Test: `ArchivoRenderingTests.NormalIsNotSemiBold` (App.Tests; 06 Q-HOME-2).

---

## 5. Edge cases and hard-won fixes

**EDGE-INFRA-1. A default Windows clone (`core.autocrlf=true`) rewrites the contract to CRLF.** The digest became `5a7597bf…`, the check read as stale, and its remedy (regenerate and commit) would have stamped the wrong hash. Required: hash and compare over LF-canonical bytes, and pin `eol=lf` in `.gitattributes` for the contract and every generated table, the C# one included. From #88, `3d00c18`. `gen-brand.mjs:29-34`, `:125-129`.

**EDGE-INFRA-2. The staleness check must compare line-ending-normalized text**, or a CRLF worktree copy reads as stale forever. Required in the C# `--check`. From `3d00c18`. `gen-brand.mjs:125-129`.

**EDGE-INFRA-3. Dark `fieldBg` was exactly `surface2`.** Fixed by value and prevented by rule (the generation-time invariant). Required: the C# generator enforces every `distinct` invariant for every brand in the contract, before emission. From `d1d980b`. `gen-brand.mjs:54-66`.

**EDGE-INFRA-4. `ink3` failed AA on both appearances, and the Windows dark value was a different colour from macOS's.** Measured, not inherited. Required: the AA test on every ground, not one per appearance. From `4def683`. `theme-palette.ts:100-113`.

**EDGE-INFRA-5. `in` walked the prototype chain.** `coerceBrand('toString')` kept garbage while `solarpunk` was discarded. Required: own-key lookup; the C# port uses an ordinal dictionary, and the nine prototype keys stay in the tests as a regression guard even though C# has no prototype chain. From #89, `bb0decc`. `theme-palette.ts:252-265`.

**EDGE-INFRA-6. Preserving an unknown `theme` pin naively made `??` stop firing.** Required: narrow to ABSENT (`PinnedBrand`) at every read, never to the default. From #95, `77adda3`. `theme-palette.ts:276-288`.

**EDGE-INFRA-7. The brand menu ticked "App default" for an unreadable pin.** Required: `PinIsUnrecognised` as its own signal; nothing ticked. From #107, `38908cd`.

**EDGE-INFRA-8. "Absent" and "explicitly the default brand" are different states.** A project may pin `shotAI`; `null` clears. Required: never fold the two (the raw comparison of 01's `SetProjectTheme` rule stays raw; natively it runs inside `SetProjectThemeOperation`, applied through the open project's `IProjectSession.Apply`, ARCHITECTURE 7.4 and 7.5). From `772e381`.

**EDGE-INFRA-9. A rollback build deleted keys it did not know.** Required: unknown root keys preserved on every write. From #92, `2b14b79`. `settings.ts:137-147`.

**EDGE-INFRA-10. A JSON scalar or array settings file.** `{...'ab'}` is `{0:'a',1:'b'}`. Required: carry keys only from a JSON object; a non-object file loads as defaults and writes no index keys. From `2b14b79`. `settings.ts:144-151`.

**EDGE-INFRA-11. A settings file containing `null`.** `JSON.parse('null')` succeeds and `parsed.projectsDir` throws, so the catch returns defaults. Required: defaults, no exception, no carried keys. `settings.ts:150-155`, `settings.test.ts:60`.

**EDGE-INFRA-12. A settings file saved with a UTF-8 BOM** (for example by an older Notepad during a hand edit, which 06 AC-HOME-22 prescribes). Node decodes the BOM as U+FEFF and `JSON.parse` fails, so Electron silently resets every setting at the next write. Required natively: IMPROVEMENT, strip one leading U+FEFF before parsing; write without a BOM. New.

**EDGE-INFRA-13. An unrecognised VALUE of a known enum key (`brand: "solarpunk"` from a newer build) is coerced and overwritten at the next write**, which is #92's rollback loss one level down. Required natively: IMPROVEMENT, keep the raw string on disk while the user has not changed that setting (7.4.2 step 4, Q-INFRA-1). New, by analogy with #95.

**EDGE-INFRA-14. Unknown keys inside `sop` are dropped** because `coerceSopSettings` rebuilds the object. Required natively: IMPROVEMENT, preserve them the same way as root keys (Q-INFRA-2). New.

**EDGE-INFRA-15. `projectsDir` accepts any string, including `""` and relative paths**, which resolve against the process's current directory. The UI always writes an absolute path; only a hand edit produces this. Required natively: IMPROVEMENT, a value that is not a fully qualified path loads as the default (Q-INFRA-3). New.

**EDGE-INFRA-16. `userName` is capped but not trimmed**, and the 120-unit slice can split a surrogate pair. Required: parity (the byline trims at use; 01's JSON writer emits a lone surrogate as `\udXXX`). `settings.ts:45-48`.

**EDGE-INFRA-17. A non-number reaching `setCaptureScale` or `setArchiveAgeDays` resets the value to its default** (the IPC passes `NaN`). ELECTRON-ONLY (typed natively); the clamp formulas are REQUIRED. `ipc.ts:669-676`.

**EDGE-INFRA-18. `archiveAgeDays` rounds half up and small positives become 1.** `2.5` is 3 (`Math.round`), `0.3` is 1. Required: `JsMath.Round`, never `Math.Round`. `settings.ts:39-43`.

**EDGE-INFRA-19. Electron's caches change before the write and are not rolled back on failure**, so a failed `setRemoteVisible(true)` leaves the shield relaxed while the file says protected. Required natively: IMPROVEMENT, the caches read the in-memory model, which rolls back with a notice on write failure (the fixed write rule). `settings.ts:278-283`.

**EDGE-INFRA-20. A leftover `captureNoHide` key.** Removing the toggle un-stuck users who had it on; since #100 the key rides along unread. Required: never interpret it. From `f24b3dc`.

**EDGE-INFRA-21. The log file relied on the library's 1 MB default.** Required: the bound is explicit (5 MiB) and asserted. From #34, `9da70df`.

**EDGE-INFRA-22. Rotation fails when another process holds the file** (antivirus, an editor). electron-log then crops to the last 256 KiB. Required: the same fallback natively; the native sink never holds the file open between batches, so a second instance or a support engineer's viewer does not block rotation. `file/index.js:45-55`.

**EDGE-INFRA-23. The update push was lost when the check beat the UI.** Required: store first, then raise; the UI reads `Pending` when it loads (03 EDGE-SHELL-19, 06 EDGE-HOME-35). From `037858d`.

**EDGE-INFRA-24. The download link was refused by the allowlist (v1.1.6).** Required: `github.com` exactly on the list, and a test that every URL the app itself builds or receives from the update check passes it. From `037858d`. `ipc.ts:288-293`.

**EDGE-INFRA-25. GitHub's unauthenticated limit is 60 requests per hour per IP, shared behind an office NAT;** 403 is nearly always that. Required: once a day, stamped even on failure; the error text `GitHub returned 403`. `update-check.ts:149-151`, `037858d`.

**EDGE-INFRA-26. A `lastUpdateCheckAt` in the future** (clock moved back, a settings file synced from another machine). Required: check anyway. `update-check.ts:57-71`.

**EDGE-INFRA-27. The timeout mapping may never fire in Electron.** The code maps only the exact message `The operation was aborted.`; Node's `fetch` abort message is believed to be `This operation was aborted` (unverified), in which case users see that raw text. Required natively: the native timeout (its own `CancellationTokenSource`) always maps to `timed out`, which is the evident intent. `update-check.ts:157-159`.

**EDGE-INFRA-28. A non-JSON 200 body surfaces the raw parser message** (`Couldn't check: Unexpected token ...`). Required natively: IMPROVEMENT, report `no usable release in the response`. `update-check.ts:153`.

**EDGE-INFRA-29. .NET's informational version carries build metadata** (`2.0.0-alpha.0+<sha>` when SourceLink or `IncludeSourceRevisionInInformationalVersion` is on). `compareVersions` survives it by `parseInt` luck; `int.Parse` would not. Required: strip `+...` when deriving the app version (7.6.1) and implement `parseInt` semantics exactly (2.8.2). New.

**EDGE-INFRA-30. `\d` in .NET matches every Unicode decimal digit.** Required: `[0-9]` in every ported regex (version, UUID, hex). New (same rule as 01's `ProjectTitles`).

**EDGE-INFRA-31. A pilot user on `2.0.0-alpha.N` is never told that `2.0.0` shipped**, because the suffix is ignored for ordering. Required natively: IMPROVEMENT, a running prerelease is older than the final release of the same number (7.6.2, Q-INFRA-6). New; the scaffold versions native builds `2.0.0-alpha.0` (`dotnet/Directory.Build.props`).

**EDGE-INFRA-32. A native prerelease published without the GitHub "prerelease" flag would be offered to every Electron user** (`/releases/latest` excludes only flagged prereleases, and `pickRelease` only refuses `prerelease: true`). Required: the release checklist (12) marks every `-alpha`, `-beta`, `-rc` release as a prerelease; `macOS:Packages/UpdateKit/Sources/UpdateKit/UpdateFeed.swift:130-133` records the same load-bearing rule. New.

**EDGE-INFRA-33. A manual check that errors clears a still-valid pending update, and a manual find never raises the Home notice.** Required: parity (06 EDGE-HOME-23, Q-HOME-8). `ipc.ts:760-766`.

**EDGE-INFRA-34. A failed stamp write suppresses the notice** (the exception skips the rest of the flow). Required natively: IMPROVEMENT, a stamp failure is logged and the result is still delivered. `main.ts:536-557`.

**EDGE-INFRA-35. The self-test mutated the real `settings.json` and its folder assertions broke when folders became UUID-named.** Required natively: IMPROVEMENT, run against an isolated settings file; keep the UUID assertions. `docs/HARDENING-PLAN.md:33`, `selftest.ts:57-64`.

**EDGE-INFRA-36. The self-test always exits 0** (`app.quit()`), so a script cannot tell PASS from FAIL. Required natively: IMPROVEMENT, exit codes 0, 1, 2 (7.8). `main.ts:403-414`.

**EDGE-INFRA-37. A bare request for "Archivo" renders SemiBold** because the variable default instance is `wght` 600; WPF does not drive variation axes from `FontWeight`. Required: static instances for WPF (06 Q-HOME-2), the variable file for the PDF print page (09). From `285403d`.

**EDGE-INFRA-38. The OFL was in the tree but not in the installed app.** Required: the installer places `OFL.txt` beside the fonts, verified on the built package, not on the source tree. From #93, `c070095`.

**EDGE-INFRA-39. MSIX virtualizes writes to `%APPDATA%`.** A packaged app's writes to `AppData\Roaming` go to a per-package private copy, so the native app would stop sharing `settings.json` and the logs with the Electron build and the real path, breaking rollback and support instructions. Required: MSI. Decided by 12 (INV-PKG-1, EDGE-PKG-21: an MSI, no MSIX; dual-purpose since 2026-09-23, 12 7.4.5); Q-INFRA-8 is closed. AC-INFRA-31 stays as the regression gate. New.

**EDGE-INFRA-40. Two builds running at once** (the Electron rollback build and the native app hold different single-instance locks) share `settings.json` and `shotai.log`. The native app refuses to START while an Electron 1.x instance runs (12 `LegacyInstanceGuard`, 03 7.4.1 step 2a), but Electron can still be started while the native app runs, and nothing stops it. Atomic replace keeps the file whole; the last writer wins per known key; unknown keys survive both ways; the log sink opens per batch with `FileShare.ReadWrite | FileShare.Delete`. Electron's rotation check counts only its own bytes (2.7), so it may rotate late or rename the file under the native sink; the native sink re-opens by path per batch and tolerates that. Required: tolerate, do not coordinate. New. Found in WP-A11: a .NET append stream keeps its own position and writes at the end it found when it opened, not with `O_APPEND` as Node's `writeFileSync` does, so a line the Electron build appends in the instant between a native batch's open and its write can be overwritten; tolerated like the rest.

**EDGE-INFRA-41. A platform-split radius without a `windows` value silently becomes `null`** (capsule) in `gen-brand.mjs` (`platformValue` returns `undefined`, `lit` prints `null`), and a `null` non-chip radius is emitted too. Required natively: IMPROVEMENT, the C# generator fails loudly for either. `gen-brand.mjs:45`, `:69`.

**EDGE-INFRA-42. A token that does not exist resolves to nothing, silently.** `.settings__slidval` read `var(--text)` and painted a near-white numeral on a white sheet (`1446d6e`). ELECTRON-ONLY cause; the native replacement is typed token keys (06 `ThemeTokenKeys`) generated from `RoleToToken`, so a misspelt key does not compile.

**EDGE-INFRA-43. .NET `$` matches before a trailing `\n`; JavaScript `$` (no `m` flag) does not.** `Regex.IsMatch("#6344f1\n", "^#([0-9a-fA-F]{6})$")` is true in .NET and the JS regex is false, so a literal port of `hexNoHash`, the contract hex rule or `UUID_RE` accepts a value Electron rejects. Required: end every ported fully anchored regex with `\z` instead of `$` (`^#([0-9a-fA-F]{6})\z`, `^#[0-9a-f]{6}\z`, the UUID pattern), plus `RegexOptions.CultureInvariant` and `[0-9]` (EDGE-INFRA-30). Microsoft Learn "Anchors in Regular Expressions" documents both anchors. New (verification).

**EDGE-INFRA-44. Static field initializers in different parts of a `partial` class run in an unspecified order.** The C# specification leaves the order of the parts unspecified, so a hand-written `static IReadOnlyList<BrandDefinition> All { get; } = [ShotAI, Lfi];` in `BrandPalette.cs` can run before the generated `ShotAI` and `Lfi` fields in `BrandPalette.Generated.cs` are assigned, and capture nulls. Required: every hand-written member that reads the generated fields is either expression-bodied (`=> ShotAI.Light`), or computed in a static constructor (which runs after every part's field initializers), or a `Lazy`; the generated file itself also emits `All` in `BRAND_IDS` order (7.2). Test: `BrandPaletteTests.AllIsFullyInitialized` (no null entry, `ReferenceEquals(All[0], ShotAI)`). New (verification).

**EDGE-INFRA-45. A manual check whose stamp write fails loses the result.** `update:check` awaits `setLastUpdateCheckAt` before `setPendingUpdate`, so a rejected write rejects the handler: the stash is not updated and Settings shows Electron's raw `Error invoking remote method ...` text instead of `shotAI <v> is available.` or `You're up to date.` (`ipc.ts:754-767`, `Settings.tsx:117-118`). Required natively: IMPROVEMENT, the stamp failure is logged and `CheckNowAsync` still sets `Pending` and returns the check's result (7.6.4). New (verification).

**EDGE-INFRA-46. A write job that cannot read the file writes defaults.** Electron's `mutate` re-runs `load()` inside every mutation, and `load()` turns ANY read failure (a sharing violation from antivirus or a sync client, a deleted file, a half-written hand edit) into the full defaults; the mutation then writes those defaults plus its one change, silently resetting the projects folder, recents, SOP settings and every toggle (`settings.ts:178-194`, `:207-213`). The native design re-reads inside each write too (7.4.3), so a literal port inherits the loss. Required natively: IMPROVEMENT (Q-INFRA-20): inside a write job, `Unreadable` is retried with the rename schedule and then fails the write (rollback and notice, the file untouched); `Missing`, `Corrupt` and `NotAnObject` use the in-memory `lastPersisted` snapshot and the last good raw object as the merge base, never the defaults, and `Corrupt` is backed up first (Q-INFRA-12). New (verification).

**EDGE-INFRA-47. A member named `ShotAI` shadows the root namespace inside `BrandPalette`.** Inside `ShotAI.Core.Brand.BrandPalette`, a qualified name such as `ShotAI.Core.Theme.Appearance` binds `ShotAI` to the generated field `BrandPalette.ShotAI` (a `BrandDefinition`) and fails to compile. Required: both parts of `BrandPalette` use `using` directives (or `global::ShotAI...`) for every type from another namespace; the generator emits no qualified `ShotAI.` names. New (verification).

**EDGE-INFRA-48. The C# generator must build when the generated file does not.** If `ShotAI.GenBrand` referenced `ShotAI.Core` (for example for `JsNumber`), a broken or stale `BrandPalette.Generated.cs` would break the tool's own build, and the only fix (regenerating) would be impossible. Required: the tool references no project of the solution; it formats numbers itself (7.3). New (verification).

**EDGE-INFRA-49. electron-log treats the first message string as a `util.format` format string.** A message containing `%s`, `%d`, `%o` and similar consumes the next argument, and `%%` prints `%` (2.7). ELECTRON-ONLY; natively the message text is written verbatim and structured values go through MEL's template, so a path containing `%d` logs as itself. New (verification).

**EDGE-INFRA-50. A joined in-flight update check must not inherit the first caller's cancellation.** With the in-flight join (7.6.4), a Settings `Check now` that joins the startup request would be cancelled if the startup token fired, and vice versa if the request used the view's token. Required: the shared request runs under the app lifetime token only (`IAppLifetime.Stopping`); each caller awaits it with `WaitAsync(ct)` using its own token. New (verification).

**EDGE-INFRA-51. The refusal log prints `null` for non-web schemes.** WHATWG `URL.origin` is the string `null` for `file:`, `javascript:`, `mailto:` and every scheme without a tuple origin, so Electron logs `refused openExternal for non-allowlisted URL: null` (2.8.9). Required: parity through 11's `UrlOrigin.Of`. New (verification).

---

## 6. macOS port notes

| Topic | macOS | Divergence from Windows and why | Lesson for the C# port |
|---|---|---|---|
| Palette type | `BrandPalette` struct of `Pair(light, dark)` `UInt32` values plus two `Alpha` shadow tokens, in ShotModel so ExportKit can use it without an app dependency (`macOS:Packages/ShotModel/Sources/ShotModel/BrandPalette.swift:3-15`, `:58-137`) | 33 colour pairs: no `focusRing`, `accentSoft`, `dangerBd`; carries shadows the Windows table does not | Put the palette in Core (UI-free) exactly as macOS put it in ShotModel; keep Windows' 36-role set, because WPF styles are ported from Windows CSS |
| Radii | 5 roles, `chip` nil means capsule (`BrandPalette.swift:30-56`) | Windows has 7 roles (`controlSm`, `micro`) because its stylesheets drew six sizes; shotAI `control` is 6 on macOS | Keep 7 roles and the Windows value 8 |
| Font | `fontFamily`, `fontPostScriptName`, `fontFallbacks` (`:59-76`); `postScriptName` exists because the variable face's PostScript name is `Archivo-SemiBold` | Windows ignores `postScriptName` | The C# table carries `PostScriptName` too (harmless, and useful if Q-HOME-2 ends up using the variable face through DirectWrite) |
| Brand id | `enum BrandPref: String { case shotAI, lfi }` (`macOS:Packages/ShotModel/Sources/ShotModel/BrandPref.swift:13-15`); labels and blurbs `shotAI's own violet identity.`, `LaCrosse Footwear corporate \u2014 charcoal and rust.` (`macOS:shotAI/AppPreferences.swift:111-124`) | Windows has no blurbs | Same raw values; an enum would make an unknown value unrepresentable, which is why the C# API keeps strings at file boundaries |
| Generator | `Scripts/gen-brand.swift`, run from the repo root (cwd), same stamp, same CRLF canonicalization, same invariant loop, same `--check` (`macOS:Scripts/gen-brand.swift:16-19`, `:57-91`, `:158-168`); CI `swift Scripts/gen-brand.swift --check` (`macOS:.github/workflows/ci.yml:56-57`) | accepts uppercase hex (`:44-50`), takes `platformValue.macos` (`:39-42`), emits alpha members | A third generator is exactly the proven pattern; C# must use the Windows rules (lowercase only, `windows` value) |
| Settings storage | `UserDefaults`, not a JSON file (`macOS:Packages/ShotModel/Sources/ShotModel/Settings.swift:40-74`); `AppPreferences` is one JSON blob with a tolerant per-key decode (`AppPreferences.swift:96-107`) | not shared with Windows; synthesized encoding drops unknown keys on re-encode; `addRecent` caps at 10, not 20 (`Settings.swift:27-31`); archive age is an `Int`, no rounding (`:20-22`) | The per-key tolerant decode matches Windows' coercion; the Windows cap 20 and the unknown-key preservation are REQUIRED here |
| Update opt-out | `checkForUpdates` (`AppPreferences.swift:68-71`), plus an MDM managed `updateCheckDisabled` that beats the user preference and even the manual check (`macOS:Packages/UpdateKit/Sources/UpdateKit/UpdateCheckState.swift:116-143`) | Windows has no policy switch | Q-INFRA-4 |
| Version compare | `SemanticVersion`: exactly three numeric components, `v` optional, `+build` ignored, prerelease sorts BELOW the final, natural order for `rc2 < rc10`, fail-closed parse (`macOS:Packages/UpdateKit/Sources/UpdateKit/SemanticVersion.swift:5-23`, `:54-97`) | Windows ignores the suffix and accepts any number of segments | Adopt only the "running prerelease is older than its final" rule (EDGE-INFRA-31); keep Windows parsing for parity |
| Feed | pinned host `api.github.com`, redirect refused off host, `X-GitHub-Api-Version: 2022-11-28`, ephemeral session, 15 s timeout, UA `shotAI/<v> (macOS; +https://github.com/Armadillon44/shotAI_MacOS)`, notes capped at 20 000 characters, release page must be `https`, host `github.com`, no user, password or port (`UpdateFeed.swift:73-80`, `:109-126`, `:134-175`, `:203-209`) | Windows follows redirects, 10 s, simpler UA | Adopt the final-host check and the API-version header (7.6.3); the `https://github.com/` prefix already excludes userinfo and ports |
| Throttle state | persisted `lastSuccessfulCheck`, `nextAttemptNotBefore`, `rateLimitedUntil`, remembered `latest`, `skippedTag`; backoff 24 h success, 1 h transient, 6 h structural, 24 h ceiling; sanitize with 1 h slack (`UpdateCheckState.swift:9-55`, `macOS:Packages/UpdateKit/Sources/UpdateKit/UpdateChecker.swift:29-40`, `:206-229`) | Windows persists one timestamp and retries tomorrow on any failure | Parity for 2.0.0 (Q-INFRA-18); the macOS "badge survives a relaunch" behavior is not adopted |
| Concurrency | the checker joins an in-flight check instead of issuing a second request (a joining caller receives the FIRST caller's outcome even when its reason differs, `automatic` versus `manual`), and re-reads state before every write so a skip made mid-check is not clobbered (`UpdateChecker.swift:44-58`, `:102-114`) | Windows can issue two requests (startup plus Check now) | Adopt the in-flight join (7.6.4), with the cancellation rule of EDGE-INFRA-50 |
| Busy launch | the launch check is skipped while recording or exporting (`macOS:shotAI/UpdateModel.swift:71-74`) | Windows checks once at startup, before any recording | Not needed |
| Logging | `os.Logger` subsystem `com.armadillon44.shotai` with categories `app, ui, store, capture, editor, ocr, sop, export, permissions, updates`; interpolations private by default; the privacy rule lists what must stay private: titles, paths, captions, window and element text, OCR text, redaction content, the API key (`macOS:Packages/ShotModel/Sources/ShotModel/Log.swift:14-39`); an explicit "Export shotAI Logs" writes a file, pruned to 10 files and 50 MB, always keeping the newest (`:76-78`, `:94-161`) | Windows writes a plain file with paths in it | Adopt macOS's never-log list for secrets and user content; keep Windows' decision that paths are loggable (support needs them) |
| Banner | `shotAI starting \u2014 v<version> (<build>) · <arch> · <os>` (`Log.swift:45-65`) | different fields | 7.5.4 |
| Self-tests | separate executable targets `CaptureSelfTest`, `PdfSelfTest`, `UpdateSelfTest`; the update one checks the tag is not an unflagged prerelease and that the throttle blocks a second call, and exits 0 or 1 (`macOS:Packages/UpdateKit/Sources/UpdateSelfTest/main.swift:1-9`, `:37-39`, `:54-70`). Its header says it "costs exactly one request", but the code makes two live requests (`feed.fetchLatest()` at `:28` and the first `checker.check(reason: .automatic, ...)` at `:56`, which starts from an empty in-memory store); only the third call is throttled | Windows uses environment variables inside the app and always exits 0 | Adopt exit codes and an `--update-selftest` switch (7.8); in-app switches, not separate executables, so they exercise the shipped binary |
| Brand face | registered at runtime with `CTFontManagerRegisterFontsForURL(.process)`, anchored on `Archivo-SemiBold` with explicit `wght` and `wdth` axis values (`macOS:shotAI/BrandFont.swift:44-97`) | CoreText drives axes; WPF cannot | WPF needs static instances (EDGE-INFRA-37) |

---

## 7. Native design (C#)

### 7.1 Placement

| Project | Namespace | Types |
|---|---|---|
| ShotAI.Core | `ShotAI.Core.Brand` | `Palette`, `BrandRadii`, `BrandFont`, `BrandDefinition`, `BrandPalette` (partial: hand-written helpers + generated table in `BrandPalette.Generated.cs`), `PaletteRoles`, `ContrastMath` |
| ShotAI.Core | `ShotAI.Core.Settings` | `AppSettings`, `ThemePref`, `ThemePrefWire`, `SettingsDefaults`, `SettingsCoercer`, `SettingsCodec`, `ISettingsService`, `SettingsService`, `SettingsChangedEventArgs`; `SettingsService` implements `ICaptureSettings` (declared in `ShotAI.Core.Capture` per 02 7.2, added by the settings work package WP-A10 so 02's `CaptureShield` can consume it) and `IProjectStoreSettings` (declared in `ShotAI.Core.Store` by 01 7.8) |
| ShotAI.Core | `ShotAI.Core.Logging` | `LogCategories`, `FileLogOptions`, `FileLoggerProvider`, `FileLogLineFormatter`, `RotatingFileSink` |
| ShotAI.Core | `ShotAI.Core.Updates` | `UpdateCheckResult`, `UpdateSkipReason`, `UpdateDecision`, `UpdateCheck` (pure functions), `ReleaseFeed`, `IUpdateService`, `UpdateService`, `AppVersion` |
| ShotAI.Core | `ShotAI.Core.Links` | `ExternalLinkPolicy` (pure base rules), `UrlOrigin`, `IExternalLinks`, `ExternalLinks`, `IUrlLauncher` (the contract and algorithm are 11 7.3.4; this spec registers them and owns the log line and the tests) |
| ShotAI.Core | `ShotAI.Core.SelfTest` | `StartupMode`, `StartupModeParser`, `SelfTestOutcome`, `StoreSelfTest`, `UpdateSelfTest` |
| ShotAI.Core | `ShotAI.Core.Paths` | `IAppPaths` (the ARCHITECTURE 10.2 contract, including `LocalDataDirectory`, R-ARCH-13) |
| ShotAI.Platform | `ShotAI.Platform.Shell` | `ShellUrlLauncher` (implements `IUrlLauncher`), `ConsoleAttach` |
| ShotAI.App | `ShotAI.App` | `AppPaths` (implements `IAppPaths`), composition in `App.OnStartup` (03 7.4.1), `SelfTestHost` |
| tools | `ShotAI.GenBrand` (new console project `dotnet/tools/ShotAI.GenBrand`, `net10.0`) | `Program`, `BrandGenerator` |

Core stays free of Windows APIs (INV-ARCH-1): file paths come from `IAppPaths`; the URL launch is behind `IUrlLauncher`; the console attach is Platform. Registrations follow ARCHITECTURE 4.1 C7: each type is registered by the extension method of the project it lives in (`AddShotAICore`, `AddShotAIPlatform`, `AddShotAIApp`), as listed in 7.11.

### 7.2 Brand types and the generated table (Core)

```csharp
namespace ShotAI.Core.Brand;

// Parameter order = platforms.windows.colors order (the generator's emission order).
public sealed record Palette(
    string Accent, string AccentPress, string AccentTint, string AccentInk, string OnAccent,
    string Ink, string Ink2, string Ink3, string Hair, string Hair2, string ControlBd,
    string Surface, string Surface2, string Ground, string FieldBg,
    string Ok, string OkTint, string OkInk, string Draft, string DraftTint, string DraftInk,
    string Danger, string DangerTint, string DangerInk,
    string NoteBg, string NoteBd, string NoteFg, string CautBg, string CautBd, string CautFg,
    string WarnBg, string WarnBd, string WarnFg,
    string FocusRing, string AccentSoft, string DangerBd);        // each "#rrggbb", lowercase

public sealed record BrandRadii(double Panel, double Card, double Figure, double Control,
                                double ControlSm, double Micro, double? Chip);   // Chip null = capsule

public sealed record BrandFont(string? Family, string? PostScriptName,
                               IReadOnlyList<string> Fallbacks, double? LabelStretch);

public sealed record BrandDefinition(string Id, string Label, BrandRadii Radii, BrandFont Font,
                                     Palette Light, Palette Dark);

public static partial class BrandPalette
{
    // generated part (BrandPalette.Generated.cs): ShotAI, Lfi, All (EDGE-INFRA-44)
    public const string DefaultBrand = "shotAI";
    // All: IReadOnlyList<BrandDefinition> [ShotAI, Lfi], BRAND_IDS order, emitted by the generator
    public static IReadOnlyList<string> BrandIds { get; }                // ["shotAI", "lfi"]
    public static bool IsBrandId(string? v);                             // ordinal FrozenDictionary lookup
    public static bool IsBrandId(JsonNode? v);                           // only a JSON string can be a brand
    public static string CoerceBrand(string? v);                         // IsBrandId(v) ? v : DefaultBrand
    public static string CoerceBrand(JsonNode? v);
    public static string? PinnedBrand(string? v);                        // IsBrandId(v) ? v : null
    public static string? PinnedBrand(JsonNode? v);
    public static bool PinIsUnrecognised(JsonNode? v);                   // string, non-empty, not a brand
    public static bool PinIsUnrecognised(string? v);
    public static BrandDefinition Get(string? brand);                    // by CoerceBrand
    public static Palette For(string? brand, Appearance appearance);     // 06's ShotAI.Core.Theme.Appearance
    public static Palette AppLight { get; }                              // ReferenceEquals(ShotAI.Light)
    public static Palette AppDark { get; }
    public static double CardRadiusPx { get; }                           // ShotAI.Radii.Card
    public static double ImageRadiusPx { get; }                          // ShotAI.Radii.Figure
    public static IReadOnlyList<string> RetiredGreys { get; }            // the 8 values of 2.3
    public static string CssFontStack(string? brand);                    // 2.3 formula, "," joiner
    public static string HexNoHash(string value);                        // throws ArgumentException
}

public static class PaletteRoles
{
    // ROLE order of ROLE_TO_TOKEN (2.3), each (role name, CSS token, accessor)
    public static IReadOnlyList<(string Role, string Token, Func<Palette, string> Get)> All { get; }
    public static IReadOnlyList<(string Role, string Token)> Radii { get; }   // RADIUS_TO_TOKEN
    public static IReadOnlyList<string> TypeTokens { get; }                   // "font-stack", "label-stretch"
}

public static class ContrastMath   // 2.5 formulas, doubles, for the tests and 06's derived colours
{
    public static double Luminance(string hex);
    public static double Ratio(string a, string b);
}
```

- `BrandPalette` is the one brand API of the solution (R-ARCH-14): brand ids are plain `string`s, there is no `BrandId` type and no `Brands` class; every consumer (01's manifest `theme` key, 03's brand menu, 05's pin, 06's theme, 09's exports) calls `BrandPalette.IsBrandId`, `CoerceBrand`, `PinnedBrand` and `PinIsUnrecognised`. 09's earlier names `Brands.PinnedBrand`, `Brands.Coerce` and `BrandId` are superseded by these (R-ARCH-14). REQUIRED.
- `IsBrandId` looks up a `FrozenDictionary<string, BrandDefinition>` built with `StringComparer.Ordinal`; no reflection, no `Enum.TryParse`, no case folding (INV-INFRA-9). REQUIRED.
- Initialization (EDGE-INFRA-44): the generated part emits `ShotAI`, `Lfi` AND `public static IReadOnlyList<BrandDefinition> All { get; } = [ShotAI, Lfi];` in one file, where textual order guarantees the fields are assigned first; the hand-written part builds the lookup dictionary, `BrandIds`, `RetiredGreys` and anything else derived from `All` in a static constructor or as expression-bodied members (`AppLight => ShotAI.Light`, `CardRadiusPx => ShotAI.Radii.Card`). REQUIRED.
- Name lookup (EDGE-INFRA-47): inside `BrandPalette` the identifier `ShotAI` is the field, so both parts import other namespaces with `using ShotAI.Core.Theme;` and never write a qualified `ShotAI.` name. REQUIRED.
- `Appearance` (added in WP-A4): `ShotAI.Core.Theme` is 06's (06 7.4), but `For` takes its `Appearance` and WP-A4 lands before WP-A14, so WP-A4 declares `public enum Appearance { Light, Dark }` in `ShotAI.Core.Theme` and WP-A14 adds the rest of that namespace.
- `JsonNode` overloads exist because every untrusted input (settings, `project.json`) arrives as a `JsonNode` from 01's `JsJson.Parse`; a node that is not a JSON string is never a brand, never a pin (`PinIsUnrecognised` false), exactly as `typeof v === 'string'`. REQUIRED.
- `HexNoHash`: `Regex(@"^#([0-9a-fA-F]{6})\z", RegexOptions.CultureInvariant)` (`\z`, not `$`: EDGE-INFRA-43); returns the group `ToUpperInvariant()`; else `throw new ArgumentException("hexNoHash: expected #rrggbb, got " + JsonQuote(value))`, where `JsonQuote` is 01's JSON string quoting (so `null` prints `null`). REQUIRED.
- `CssFontStack(b)`: `f = Get(b).Font`; `string.Join(",", (f.Family is null ? [] : ["\"" + f.Family + "\""]).Concat(f.Fallbacks))`. For LFI: `"Archivo","Helvetica Neue",Helvetica,Arial,"Liberation Sans",sans-serif`. REQUIRED (09 builds export CSS with it). The WPF font-family conversion (quotes removed, `-apple-system` dropped, `sans-serif` to `Segoe UI`) is 06's.
- `themeStylesheet`, `radiusCss`, the `--label-stretch` CSS string: ELECTRON-ONLY (06 builds WPF resources from `PaletteRoles` and `BrandRadii`).
- Appearance independence (INV-INFRA-6) holds by construction: a `BrandDefinition` has one `Radii` and one `Font`.

**The generated file** `dotnet/src/ShotAI.Core/Brand/BrandPalette.Generated.cs`, exactly this shape (LF line endings, no BOM, no trailing whitespace, ends with one `\n`):

```csharp
// <auto-generated/>
// GENERATED by dotnet/tools/ShotAI.GenBrand from contract/brand.json. DO NOT EDIT.
// contract sha256: c945d17c1c348c8007d16cdd6dc47be7040de47f7071932dfc5d413e6baa64ff
//
// Change a colour in contract/brand.json and regenerate. The same file drives
// src/shared/brand-colors.generated.ts here and BrandPalette+Generated.swift in
// Armadillon44/shotAI_MacOS, so the platforms cannot disagree about a value
// none of them owns. Everything about how these are consumed lives in
// BrandPalette.cs, by hand. Only the table is generated.
namespace ShotAI.Core.Brand;

public static partial class BrandPalette
{
    public static readonly BrandDefinition ShotAI = new(
        Id: "shotAI",
        Label: "shotAI",
        Radii: new BrandRadii(Panel: 12, Card: 10, Figure: 8, Control: 8, ControlSm: 6, Micro: 4, Chip: null),
        Font: new BrandFont(
            Family: null,
            PostScriptName: null,
            Fallbacks: ["-apple-system", "\"Segoe UI\"", "Roboto", "Helvetica", "Arial", "sans-serif"],
            LabelStretch: null),
        Light: new Palette(
            Accent: "#6344f1",
            ...                       // one line per token, platforms.windows.colors order
            DangerBd: "#f0c2c2"),
        Dark: new Palette(
            Accent: "#9a8bf7",
            ...
            DangerBd: "#5a2a2a"));

    public static readonly BrandDefinition Lfi = new(
        ...);

    public static IReadOnlyList<BrandDefinition> All { get; } = [ShotAI, Lfi];
}
```

The `// <auto-generated/>` marker keeps analyzers (warnings are errors) off the file. Field names: contract token `t`, role `r = windowsRenames[t] ?? t`, C# name `char.ToUpperInvariant(r[0]) + r[1..]`; brand field names `ShotAI` and `Lfi` (fixed map). The generated part also emits `All` after the two fields (EDGE-INFRA-44). Numbers print with the tool's own formatter, NOT 01's `JsNumber` (the tool references no solution project, EDGE-INFRA-48): a finite double that is an integer prints `((long)v).ToString(CultureInfo.InvariantCulture)`, any other finite double prints `v.ToString("R", CultureInfo.InvariantCulture)`, and a non-finite or non-number value is a `GenBrandException`. Strings are C# regular string literals with `\` and `"` escaped (and any character below U+0020 as `\uXXXX`). REQUIRED.

### 7.3 The C# generator (`dotnet/tools/ShotAI.GenBrand`)

Decision: a small console tool plus a checked-in generated file, not an MSBuild step. Why: (1) all three platforms check in their table, so the stamp change is visible in every diff (INV-INFRA-1); (2) an MSBuild-time generator would hide drift and would need the tool built before Core; (3) a checked-in file is readable without building. IMPROVEMENT over nothing; REQUIRED pattern parity with `gen-brand.mjs`.

```csharp
// tools/ShotAI.GenBrand/BrandGenerator.cs (public, so ShotAI.Core.Tests can call it)
public static class BrandGenerator
{
    public const string OutputRelativePath = "dotnet/src/ShotAI.Core/Brand/BrandPalette.Generated.cs";
    public static string ContractHash(ReadOnlySpan<byte> contractBytes);   // sha256 of bytes with CR LF -> LF, lowercase hex
    public static string Generate(ReadOnlySpan<byte> contractBytes);       // throws GenBrandException(message)
}
public sealed class GenBrandException(string message) : Exception(message);
```

`Program.Main(args)`:

1. `check = args.Contains("--check")`; `root = --root <dir>` if given, else walk up from `Environment.CurrentDirectory` to the first directory containing both `contract/brand.json` and `dotnet/ShotAI.slnx`; none found: die `cannot find the repository root (contract/brand.json)`.
2. `bytes = File.ReadAllBytes(root/contract/brand.json)`.
3. `text = BrandGenerator.Generate(bytes)`; a `GenBrandException` prints `gen-brand: <message>` to stderr, exit 1.
4. Check mode: existing file (missing counts as stale); compare `Normalize(existing) == Normalize(text)` where `Normalize` replaces `\r\n` with `\n`; different: stderr `gen-brand: BrandPalette.Generated.cs is STALE.` + `\n` + ``Run `dotnet run --project dotnet/tools/ShotAI.GenBrand` and commit the result.``, exit 1; same: stdout `gen-brand: up to date`, exit 0.
5. Write mode: write UTF-8 without BOM; stdout `gen-brand: wrote BrandPalette.Generated.cs`, exit 0.

`Generate` rules (REQUIRED parity with 2.2 unless marked):

| Rule | Behavior |
|---|---|
| Hash | `ContractHash`: replace every byte pair `0x0D 0x0A` with `0x0A`, then SHA-256 (`System.Security.Cryptography.SHA256.HashData`), `Convert.ToHexStringLower` (.NET 9 and later). Byte-level replacement equals Node's decode, replace, re-encode for valid UTF-8; IMPROVEMENT: bytes that are not valid UTF-8 (where Node would substitute U+FFFD and hash the substitute) are a `GenBrandException("contract/brand.json is not valid UTF-8")` |
| Parse | `JsonDocument.Parse` over the raw bytes (comments and trailing commas disallowed). IMPROVEMENT: a parse failure is `GenBrandException("cannot parse contract/brand.json: <message>")` where Node throws an uncaught `SyntaxError` (2.2 step 12); a leading UTF-8 BOM is rejected with the same message (Node's `JSON.parse` fails on U+FEFF, so this is parity in outcome) |
| Tokens | `platforms.windows.colors` array of strings; missing or `null`: `spec has no platforms.windows.colors` (parity); IMPROVEMENT: present but not an array of strings gives the same message where Node throws a `TypeError` |
| Invariants | for each `invariants[i]` with `rule == "distinct"` and `tokens.Length == 2`, for each property of `brands` (document order): if both `colors[t0][mode]` and `colors[t1][mode]` are non-empty strings and ordinal-equal (JS uses truthiness plus `===`; the two differ only for a non-string colour value, which the hex rule rejects for the emitted brands and which a third brand may carry unnoticed in both implementations): `` invariant violated \u2014 <brand>.<mode>: <t0> and <t1> are both <x>. `` + `\n  ` + `why` (empty when absent). Run before emission |
| Brands | fixed `["shotAI", "lfi"]`, shotAI fully checked first; missing: `brand <b> missing from the contract` |
| Order | per brand, exactly Electron's first-error order (2.2 step 5): brand missing, `<b>: no radii`, `<b>: no font`, then the per-key radius checks (IMPROVEMENT rules below), then colours `light` then `dark` |
| Colours | per brand, `light` object then `dark` object, tokens in order; missing: `<b>: no colour "<t>" in the contract`; value not matching `^#[0-9a-f]{6}$` (ordinal, ASCII): `<b>.<t>.<mode>: expected a lowercase 6-digit #rrggbb, got <JSON of the value>`, where a MISSING appearance value prints `undefined` (as `JSON.stringify(undefined)` does in the template) and a JSON `null` prints `null`. The regex is `^#[0-9a-f]{6}\z` (EDGE-INFRA-43) |
| Radii | missing: `<b>: no radii`; for each of the 7 keys: an object takes its `windows` member; IMPROVEMENT: an object without `windows` fails `<b>.radii.<k>: no windows value`; a `null` or missing value is allowed only for `chip`, else fails `<b>.radii.<k>: must be a number` (EDGE-INFRA-41) |
| Font | missing: `<b>: no font`; emits `family`, `postScriptName` (IMPROVEMENT: carried, unused by Windows today), `fallbacks` (default empty), `labelStretch` |
| Alpha | `platforms.windows.alpha` is `[]`; nothing emitted (parity) |

Added in WP-A4. Where Electron crashes, emits garbage or would write a C# file that does not compile, the generator fails with a message instead (IMPROVEMENT):

| Input | Message |
|---|---|
| A root that is not an object, or `platforms.windows.colors` not an array of strings | `spec has no platforms.windows.colors` |
| `brands` missing or not an object (Node throws a `TypeError`) | `spec has no brands` |
| A token, or a `windowsRenames` value, that is not a plain name `^[a-z][A-Za-z0-9]*$`: each becomes a C# identifier, so this is also what keeps the contract from injecting code | `platforms.windows.colors: <JSON> is not a token name`, `windowsRenames.<t>: <JSON> is not a role name` |
| A missing or non-string `label` (Node writes the string `'undefined'`), checked after `font` | `<b>: no label` |
| `font.family` or `font.postScriptName` neither a string nor null | `<b>.font.<k>: must be a string or null` |
| `font.fallbacks` present and not an array of strings | `<b>.font.fallbacks: must be an array of strings` |
| `font.labelStretch` neither a finite number nor null | `<b>.font.labelStretch: must be a number or null` |
| A radius that is a string, a boolean or not finite (`1e400`) | `<b>.radii.<k>: must be a number` |

Also settled in WP-A4:

- String literals escape U+0085, U+2028 and U+2029 as well as the control characters, because C# treats them as line terminators.
- An integer-valued number below 1e15 in magnitude prints as an integer.
- `--root <dir>` is resolved against the working directory.
- Every message ends with `\n` on every OS, as Node's does.
- An unreadable contract gives `cannot read <path>: <message>`.
- `Program.Run(args, stdout, stderr, workingDirectory)` is public, so `GenBrandTests` run the command in-process.
- Referencing the tool executable from the Microsoft.Testing.Platform test executable works on Linux and Windows, so the fallback library `ShotAI.GenBrand.Core` was not needed.

Workflow for a contract change, now three generators: edit `contract/brand.json`; run `npm run gen:brand`, `dotnet run --project dotnet/tools/ShotAI.GenBrand` (from the repo root, or from `dotnet/` with `--project tools/ShotAI.GenBrand`); commit both tables with the contract; land the identical contract and `swift Scripts/gen-brand.swift` output in the macOS repo. After cutover the TypeScript generator is deleted with the Electron tree.

Tool project (`dotnet/tools/ShotAI.GenBrand/ShotAI.GenBrand.csproj`): `OutputType` `Exe`, `net10.0`, `IsPackable` false, NO `ProjectReference` (EDGE-INFRA-48) and no package reference; it is BCL only, like `gen-brand.mjs` is Node builtins only.

CI (12 edits `dotnet.yml`): in the Linux job, before `Build`: `dotnet run --project tools/ShotAI.GenBrand -c Release -- --check` (working directory `dotnet`). The workflow already triggers on `contract/**`. Added in WP-A4: `.gitattributes` joins the path filter, and the Windows job runs the same check on a checkout made with `core.autocrlf true` (its first step pins that setting), which is AC-INFRA-1's CRLF clone. `.gitattributes` gains `dotnet/src/ShotAI.Core/Brand/BrandPalette.Generated.cs text eol=lf`. The tool project is added to `ShotAI.slnx` under `/tools/`. `ShotAI.Core.Tests` references the tool project (so `BrandContractTests.GeneratedFileIsCurrent` runs the same `Generate` on Linux) and links `dotnet/src/ShotAI.Core/Brand/BrandPalette.Generated.cs` and, while it exists, `src/shared/brand-colors.generated.ts` into its output with `CopyToOutputDirectory` (the same pattern the csproj already uses for `contract/**`).

### 7.4 Settings (Core, `ShotAI.Core.Settings`)

#### 7.4.1 Types

```csharp
public enum ThemePref { System, Light, Dark }                 // wire: "system", "light", "dark"
public static class ThemePrefWire { public static string ToWire(ThemePref t); public static bool TryParse(string? s, out ThemePref t); }

public sealed record AppSettings(
    string ProjectsDir,
    IReadOnlyList<string> Recents,          // Equals/GetHashCode overridden: ordinal SequenceEqual
    SopSettings Sop,                        // 07's record
    bool RemoteVisible,
    double CaptureScale,
    bool HasSeenTour,
    string UserName,
    bool IncludeNameInReports,
    int ArchiveAgeDays,                     // 0 or 1..1825 after coercion
    ThemePref Theme,
    string Brand,                           // always a known brand id
    bool UpdateCheckEnabled,
    double LastUpdateCheckAt)               // epoch ms; any finite double survives
{
    public string? ReportByline { get; }    // JsString.Trim(UserName); IncludeNameInReports && name != "" ? name : null
}

public static class SettingsDefaults
{
    public const int MaxRecents = 20;
    public const double CaptureScaleMin = 0.5, CaptureScaleMax = 1, CaptureScaleDefault = 0.85;
    public const int ArchiveAgeDefault = 90, ArchiveAgeMax = 1825;
    public const int UserNameMax = 120;
    public static AppSettings Create(string defaultProjectsDir);
    public static readonly IReadOnlyList<string> KnownKeys;   // the 13 keys of 2.6.3, literal order
}

public static class SettingsCoercer
{
    public static double CaptureScale(JsonNode? v);   // number & finite ? Min(1, Max(0.5, d)) : 0.85
    public static double CaptureScale(double v);      // same clamp; NaN or infinity -> 0.85
    public static int ArchiveAge(JsonNode? v);        // not finite -> 90; <= 0 -> 0; else Min(1825, Max(1, JsMath.Round(d)))
    public static int ArchiveAge(double v);
    public static string UserName(string? v);         // null -> ""; else first 120 UTF-16 units (no trim)
    public static ThemePref Theme(JsonNode? v);       // exact "light" | "dark" | "system", else System
    public static AppSettings Normalize(AppSettings s, string defaultProjectsDir); // re-applies every clamp; Brand via CoerceBrand
}
```

`JsonNode` "number and finite" means: `node is JsonValue` whose value kind is a JSON number (01's `JsJson.Parse` stores every number as a `double`, `Infinity` on overflow) and `double.IsFinite(d)`. A JSON string `"0.7"` is not a number. REQUIRED.

#### 7.4.2 Codec (`SettingsCodec`)

```csharp
public static class SettingsCodec
{
    public sealed record Decoded(AppSettings Settings, JsonObject? Raw, SettingsLoadStatus Status);
    public enum SettingsLoadStatus { Ok, Missing, Unreadable, Corrupt, NotAnObject }
    public static Decoded Decode(ReadOnlySpan<byte> bytes, bool missing, string defaultProjectsDir);
    public static string Encode(AppSettings next, Decoded disk);             // JsJson.Stringify, indent 2
}
```

`Decode`:

1. `missing`: defaults, `Missing`.
2. `node = JsJson.Parse(bytes)` (01 7.2.1, the BYTES overload: it skips one leading UTF-8 BOM, which is the IMPROVEMENT of EDGE-INFRA-12, and decodes invalid UTF-8 with U+FFFD exactly as Node's `readFile(file, 'utf8')` does); a `JsJsonException`: defaults, `Corrupt`.
3. (No separate BOM step; the reader does it. Never read the file with `File.ReadAllText`, whose BOM sniffing would also accept UTF-16 and UTF-32 BOMs that Node does not.)
4. `node` is not a `JsonObject` (scalar, array, `null`): defaults, `NotAnObject`, `Raw = null` (EDGE-INFRA-10, EDGE-INFRA-11).
5. Otherwise coerce every known key per 2.6.3 (plus the Q-INFRA-3 `projectsDir` rule), `Raw` = the parsed object, `Ok`.

`Encode(next, disk)`:

1. `obj = disk.Raw?.DeepClone() as JsonObject ?? new JsonObject()`.
2. For each known key in literal order: `obj[key] = value` (01's in-place replace keeps an existing key's position; a new key is appended), so the byte layout equals Electron's `{...carried, known...}` (INV-INFRA-14).
3. `sop`: IMPROVEMENT (Q-INFRA-2): start from `disk.Raw?["sop"]` when it is a `JsonObject` (deep clone), else a new object; set `enabled, model, tone, effort, customInstructions` in that order with 07's wire strings. An existing key keeps its position inside `sop` as at the root, so a hand edit that reordered the five keys keeps its order where Electron rewrites them in this order, a byte difference for that file only (added in WP-A10).
4. Enum raw preservation, IMPROVEMENT (Q-INFRA-1): for `brand`, `theme`, `sop.model`, `sop.tone`, `sop.effort`: if the disk raw value is a JSON string that the coercer did not recognise AND `next`'s value equals the value `Decode` produced for it (the user did not change that setting), write the raw string back instead of the coerced one. Any other type (number, object) is repaired as in Electron.
5. `recents` as a JSON array of strings; numbers as JS numbers; booleans; `JsJson.Stringify(obj)` (indent 2, LF, no trailing newline).

UTF-8 without BOM for the bytes.

#### 7.4.3 Service

```csharp
// The member set is 11 7.3.6's contract (11 wins on names); SettingsFilePath is this spec's addition.
public interface ISettingsService
{
    AppSettings Current { get; }                        // immutable snapshot, Volatile read
    event EventHandler<SettingsChangedEventArgs>? Changed;
    Task<AppSettings> UpdateAsync(Func<AppSettings, AppSettings> change, CancellationToken ct = default);
    string SettingsFilePath { get; }
    Task FlushAsync(TimeSpan timeout);                  // app exit (11 7.10 ShutdownFlush)
}
public sealed record SettingsChangedEventArgs(AppSettings Previous, AppSettings Current, bool IsRollback);

// Implements 01's IProjectStoreSettings and 02's ICaptureSettings on the same instance (11 7.3.6).
// App registers the instance it loaded at startup as SettingsService; AddShotAICore forwards
// ISettingsService, IProjectStoreSettings and ICaptureSettings to it (corrected in WP-A10).
// IDisposable as well as IAsyncDisposable: ServiceProvider.Dispose() throws for a singleton that
// implements only IAsyncDisposable (11 7.10 rule 2).
public sealed class SettingsService : ISettingsService, IProjectStoreSettings, ICaptureSettings, IDisposable, IAsyncDisposable
{
    public static SettingsService Load(IAppPaths paths, AtomicFile atomic, TimeProvider time,
                                       ILogger<SettingsService> log);   // synchronous initial read (ARCHITECTURE 4.2 step 5b)
}
```

Behavior:

- **Initial load** (synchronous, before any window): `File.ReadAllBytes(path)` inside `try`, then `Decode`; `FileNotFoundException` and `DirectoryNotFoundException` mean missing; any other I/O error means `Unreadable` and logs warn `settings.json unreadable, using defaults: <exception type>` (IMPROVEMENT: Electron is silent). `Corrupt` or `NotAnObject` logs warn `settings.json is not a settings object, using defaults` and, for `Corrupt`, copies the file to `settings.json.bad` (overwrite, best effort) before any write can replace it (IMPROVEMENT, Q-INFRA-12). Never logs file content. A `projectsDir` string that is not fully qualified logs warn `settings: projectsDir is not an absolute path, using the default` (Q-INFRA-3), here and when a write's re-read finds one. `lastPersisted = Current = decoded.Settings`.
- **UpdateAsync(change)** (the fixed write rule, REQUIRED): (1) under a lock, append `change` to `pending` and set `Current = Fold()` (a `Volatile.Write` of the new immutable snapshot) where `Fold() = pending.Aggregate(lastPersisted, (s, f) => SettingsCoercer.Normalize(f(s), defaultDir))`; then, after releasing the lock, raise `Changed(IsRollback: false)` through 11's `EventRaiser.Raise` (ARCHITECTURE T5: events are raised outside locks, a throwing handler is logged and the rest still run) (optimistic; the UI and the caches see the new value at once); (2) enqueue a job on this service's own `SerialWriteQueue` (01 7.7; one queue for settings, separate from the store's, as in Electron); (3) the job re-reads the file with `Decode` (parity with `load()` in `mutate`, so a hand edit made while running is merged). If the re-read is not `Ok` the job follows EDGE-INFRA-46 (IMPROVEMENT): `Unreadable` retries the read on the rename schedule (`AtomicFile.RenameRetryDelays`) and then fails the job without writing; `Missing`, `Corrupt` or `NotAnObject` uses `lastPersisted` and the last `Ok` raw object as `disk` (a `Corrupt` file is first copied to `settings.json.bad`). The job then computes `next = Normalize(change(disk.Settings))`, writes `Encode(next, disk)` with `atomic.WriteAsync(path, bytes, onRetry: code => log.LogWarning("settings rename {Code} \u2014 retrying (lock likely transient)", code))`; (4) on success: under the lock `lastPersisted = next`, remove `change` from `pending`, `Current = Fold()`; after the lock, raise `Changed` if different; return `next`; (5) on failure, and also when the queue completes the job as cancelled because `ct` fired before it started: under the lock remove `change` from `pending` and set `Current = Fold()` (exactly this change rolls back; later queued changes stay); after the lock, raise `Changed(IsRollback: true)`; rethrow (the caller shows the notice, 06 INV-HOME-28; `AddRecentAsync` swallows it, so a recents rollback shows no notice, parity with `addRecent`). A job always writes, even when nothing changed (parity).
- **IProjectStoreSettings** (01): `GetProjectsDirAsync` returns `Current.ProjectsDir`; `SetProjectsDirAsync(dir)` is `UpdateAsync(s => s with { ProjectsDir = dir })`; `GetRecentsAsync`; `SetRecentsAsync(list)`; `AddRecentAsync(p)` is `UpdateAsync(s => s with { Recents = [p, ..s.Recents.Where(x => !string.Equals(x, p, StringComparison.Ordinal))].Take(20).ToArray() })` inside `try`, logging warn `addRecent failed (non-fatal):` with the exception and never throwing; `GetBrandAsync` returns `Current.Brand`.
- **ICaptureSettings** (02): `CaptureScaleNow() => Volatile.Read(ref _current).CaptureScale`; `RemoteVisibleNow() => Volatile.Read(ref _current).RemoteVisible`. No lock, no I/O, callable from the hook and capture threads (INV-INFRA-18).
- **Mechanics** (added in WP-A10):
  - `Changed` is raised only when `Current` changes, at each of the three steps; a change that leaves the settings equal raises nothing, and its job still writes (parity).
  - A `change` must be pure. It runs on `Current` for the optimistic step, again whenever the pending changes are folded over a new base, and once on what its job read. One that throws on `Current` faults the task with that same exception and queues and raises nothing; one that throws on a later base is left out of the fold (logged at Debug, 2.11), and its own job then fails and rolls back.
  - A job that finds the file corrupt or not an object logs warn `settings.json is not a settings object, writing the saved settings over it`. A corrupt file is backed up once per corruption, so the backup made at load is not overwritten by the first write's re-read; a failed backup logs at Debug.
  - The last good raw object is the one the last successful write wrote, so an unknown key that a hand edit added and a write merged survives a later missing file.
  - After `Dispose` or `DisposeAsync`, `UpdateAsync` faults with `ObjectDisposedException` before applying anything.
  - A job reads the file through an internal seam that only the tests replace; the read at load is `File.ReadAllBytes`.
- **Change delivery**: `Changed` is raised synchronously on the thread that called `UpdateAsync` for the optimistic step, and on a thread-pool thread (the settings queue consumer, ARCHITECTURE 6.1) for the reconcile or rollback step, always outside the service's lock (T5). UI subscribers marshal with `IUiDispatcher.Post` only (ARCHITECTURE T6, R-ARCH-11), subscribe before reading `Current` and re-read it on each event (T7). 11's `RemoteVisibilityApplier` (App) subscribes and, when `Previous.RemoteVisible != Current.RemoteVisible`, calls `CaptureShield.ApplyRemoteVisibility(Current.RemoteVisible)` through `Task.Run`, never inline on the UI thread and never through `IUiDispatcher.Post` (ARCHITECTURE 6.4 DL1, 02 7.8), including on a rollback (the intent of `ipc.ts:659-663`, improved by 11 D-IPC-8); it never applies at startup (ARCHITECTURE 4.2 step 10 does).
- **Exit** (ARCHITECTURE 4.5, R-ARCH-10): 11's `ShutdownFlush.Run(TimeSpan.FromSeconds(5))` in `App.OnExit` awaits `Task.WhenAll(projects.FlushAsync(t), settings.FlushAsync(t))` with one 5 s budget shared by both queues, so a write issued just before quit reaches disk (IMPROVEMENT; Electron can lose a pending chain at exit). A settings job never needs the UI thread (DL4). `provider.Dispose()` then calls the synchronous, idempotent `Dispose`, which completes the queue without waiting for the consumer.

Divergences:

| Behavior | Electron | Native | Class |
|---|---|---|---|
| Read path | re-reads disk on every get | reads the in-memory snapshot; disk is re-read inside each write | IMPROVEMENT (fixed decision: in-memory first); a hand edit made while running appears at the next write or restart |
| Optimism and rollback | caches change before the write, never roll back; other keys only after the write | every key applies at once and rolls back on failure | IMPROVEMENT (fixed decision; EDGE-INFRA-19) |
| Corrupt file | silently replaced | logged, backed up to `settings.json.bad`, then replaced | IMPROVEMENT |
| File unreadable, missing or corrupt INSIDE a write | defaults plus the one change are written (EDGE-INFRA-46) | unreadable: the write fails and rolls back; otherwise the in-memory snapshot is the base | IMPROVEMENT (Q-INFRA-20) |
| BOM | reset to defaults | tolerated | IMPROVEMENT |
| Unknown enum strings, nested `sop` keys | overwritten, dropped | preserved | IMPROVEMENT (Q-INFRA-1, Q-INFRA-2) |
| Invalid `projectsDir` | kept | default | IMPROVEMENT (Q-INFRA-3) |
| IPC coercions (`=== true`, `NaN`) | at the boundary | static types | ELECTRON-ONLY |

#### 7.4.4 File location and paths

```csharp
namespace ShotAI.Core.Paths;

// ARCHITECTURE 10.2 (R-ARCH-13): this spec's contract plus LocalDataDirectory.
public interface IAppPaths
{
    string UserDataDirectory { get; }    // %APPDATA%\shotAI
    string SettingsFile { get; }         // UserDataDirectory\settings.json
    string LogsDirectory { get; }        // UserDataDirectory\logs
    string LocalDataDirectory { get; }   // %LOCALAPPDATA%\LFI\shotAI  (MSAL cache, WebView2 data; never the Squirrel root)
    string DefaultProjectsDir { get; }   // %USERPROFILE%\shotAI Projects
    string TempDirectory { get; }        // Path.GetTempPath()
    string FontsDirectory { get; }       // AppContext.BaseDirectory\Fonts
}
```

`AppPaths` (App): `Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData)` (FOLDERID_RoamingAppData, the same folder Electron's `appData` resolves to) + `shotAI`; `Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData)` (FOLDERID_LocalAppData) + `LFI\shotAI`; `Environment.GetFolderPath(Environment.SpecialFolder.UserProfile)` + `shotAI Projects`. An empty result from `GetFolderPath` is a fatal startup error logged and shown by 03 (it cannot happen on a normal profile). No code composes these paths itself; Core receives them through `IAppPaths`, and tests pass a temp-folder implementation (ARCHITECTURE 10.2). REQUIRED.

`AppPaths` also exposes 03's `BrandFontPath()` (03 2.10.10): `Path.Combine(FontsDirectory, "Archivo.ttf")`, returning `""` when the file is missing so an export degrades to the fallback stack (parity with `src/main/paths.ts:29-36`). It is not an `IAppPaths` member: `AppPaths` implements 09's `IBrandFontSource` (`string BrandFontPath()`) as well as `IAppPaths`, and 09 consumes it through that interface only. The production paths are pinned by `Shell.AppPathsTests` (8.5): `SettingsFileIsRoamingAppData`, `LogsDirectoryIsRoamingAppData`, `LocalDataDirectoryIsUnderLfi` (`LocalDataDirectory` equals `%LOCALAPPDATA%\LFI\shotAI`, the test 08's `MsalCachePersistenceTests.CacheLivesUnderLocalDataDirectory` defers to) and `NoPathUnderSquirrelRoot` (AC-INFRA-35).

Where each kind of data lives (ARCHITECTURE 10.1):

| Data | Directory | Why |
|---|---|---|
| `settings.json`, `settings.json.bad`, `logs\shotai.log`, `logs\shotai.old.log` | `UserDataDirectory` (`%APPDATA%\shotAI`, roaming) | REQUIRED: the same files the Electron build reads and writes, so the pilot and a rollback share them (ARCHITECTURE 10.5, INV-INFRA-14, Q-INFRA-7) |
| Projects | `settings.projectsDir`, default `DefaultProjectsDir` | REQUIRED: unchanged from Electron (01) |
| New native-only machine-local (non-roaming) data: 08's MSAL cache (`entra\msal-cache.bin`), 09's WebView2 user data folder (`WebView2\`), and anything added later | `LocalDataDirectory` (`%LOCALAPPDATA%\LFI\shotAI`) | IMPROVEMENT (D-ARCH-2, R-ARCH-13, 12 Q-PKG-4 default adopted): the obvious `%LOCALAPPDATA%\shotAI` is the Squirrel install root `%LocalAppData%\shotai\` under a case-insensitive file system, so removing Electron at cutover would delete it (EDGE-PKG-22) |

`AppPaths` never returns a path under `%LOCALAPPDATA%\shotAI`, and no native code ever writes there. `LocalDataDirectory` is not created by `AppPaths`; each consumer creates its own subfolder on first use (`Directory.CreateDirectory`). The roaming folder is shared with the Electron build and its Chromium data, which the native app never deletes (12 decides any cleanup, INV-PKG-24). REQUIRED.

### 7.5 Logging (Core provider, App composition)

#### 7.5.1 Composition

`Microsoft.Extensions.Logging` (`LoggerFactory.Create`) with one provider, `FileLoggerProvider` (Core, depends only on `Microsoft.Extensions.Logging.Abstractions`, which the foundation work package WP-A1 adds together with `ShotAI.Core.Errors` and `ShotAI.Core.Threading`, ARCHITECTURE 3.5), plus `Microsoft.Extensions.Logging.Debug` in Debug builds only (the replacement for electron-log's console transport; ELECTRON-ONLY console). No third-party logging package. Filters: `SetMinimumLevel(min)`; categories starting `Microsoft.` or `System.` at `Warning`.

`min` = `Debug` in a Debug build, `Information` in Release (the intent of `app.isPackaged`). IMPROVEMENT: the environment variable `SHOTAI_LOG_LEVEL` = `debug` (case-insensitive) lowers a Release build to `Debug` for field troubleshooting; any other value is ignored (Q-INFRA-11).

Added in WP-A11: the rule is Core's `FileLogOptions.MinimumLevelFor(debugBuild, Environment.GetEnvironmentVariable(FileLogOptions.LevelVariable))`, so it is tested on Linux. The App gives the result to `SetMinimumLevel` and to `FileLogOptions.MinimumLevel`, which the provider applies as well, so a disabled level costs one comparison in the provider too.

#### 7.5.2 Line format (REQUIRED, byte-compatible with electron-log's file transport)

```
line  = "[" + t.ToString("yyyy-MM-dd HH:mm:ss.fff", InvariantCulture) + "] ["
        + (levelName + "]").PadRight(6)
        + scopeText
        + " " + message
        + (exception is null ? "" : " " + exception)          // Exception.ToString(): type, message, stack
        + "\r\n"
scopeText = label.Length == 0 ? new string(' ', 11) : (" (" + label + ")").PadRight(11)
t = time.GetLocalNow()                                          // local time, as electron-log
```

Examples: `[2026-09-23 09:15:02.114] [info]  (main)     update available: 1.3.1`, `[2026-09-23 09:15:02.117] [debug] (main)     update check: up to date (2.0.0)` and `[2026-09-23 09:15:02.120] [warn]  (projects) addRecent failed (non-fatal): System.IO.IOException: ...`. The message text is written verbatim: no `util.format` specifier processing (EDGE-INFRA-49). The width 11 is fixed (Electron's longest label `projects` plus 3), not computed at run time. Line ending is `\r\n` on every OS, so Linux tests see Windows bytes. Encoding UTF-8 without BOM. Multi-line messages are written as-is. A line whose message or exception text cannot be built (a formatter or a `ToString` that throws) is dropped and counted in the report of 7.5.4, so a logging call never throws (added in WP-A11). Scopes (`BeginScope`) are not written: the format has no place for them.

| MEL level | Written as |
|---|---|
| `Critical` | `error` |
| `Error` | `error` |
| `Warning` | `warn` |
| `Information` | `info` |
| `Debug` | `debug` |
| `Trace` | `silly` (electron-log's level below `debug`; its `verbose` sits ABOVE `debug`, so `Trace` must not map to it) |

Log text is not a machine contract (03 said so); the format is REQUIRED so support can read old and new logs the same way.

#### 7.5.3 Categories

Services log through `ILogger<T>`. The provider maps the category (the type's full name) to a label by the longest matching prefix; an explicit category string equal to a label (`loggerFactory.CreateLogger("claude")`) maps to itself.

| Prefix | Label |
|---|---|
| `ShotAI.Core.Store`, `ShotAI.Core.Settings` | `projects` |
| `ShotAI.Core.Capture`, `ShotAI.Platform.Capture` | `capture` |
| `ShotAI.Core.Sop`, `ShotAI.Core.Auth`, `ShotAI.Platform.Auth` | `claude` |
| `ShotAI.Platform.Ocr`, `ShotAI.Core.Redaction` | `ocr` |
| `ShotAI.Core.Threading`, `ShotAI.Core.Links`, and the explicit category `svc` (11 7.11 L1: boundary `call:` lines; the refusal line of 2.8.9 was `ipc` in Electron) | `svc` |
| everything else (`ShotAI.App`, `ShotAI.Core.Updates`, exports) | `main` |
| banner lines | `` (empty label: 11 spaces) |

Every prefix in the table is a namespace of ARCHITECTURE 2.4; there is no `ShotAI.Core.Archive` or `ShotAI.Platform.Uia` namespace. `ArchiveEngine` lives in `ShotAI.Core.Store` (01 7.1, `docs/native/spec/01-model-store.md:973`) and so logs under `projects`; `UiaElementLocator` lives in `ShotAI.Platform.Capture` (02 7.1, `docs/native/spec/02-capture.md:951`) and so logs under `capture`. Specs 04 and 08 refer to categories `ocr` and `claude`; they are these labels. The mapping table is Core data (`LogCategories`) with a test; a new namespace defaults to `main`. A prefix matches a whole namespace only: the category is the namespace itself or continues with a `.`, so 06's `ShotAI.Core.SettingsUi` is not under `ShotAI.Core.Settings`. Every comparison is ordinal, and the empty category is `main` (corrected in WP-A11: a plain string prefix would have labeled 06's Settings text `projects`). `ipc` is ELECTRON-ONLY; its successor is `svc` (11 L1). No label is longer than `projects`, so the fixed width 11 still holds; a future label longer than 8 characters must not be added without widening every line, which a `LogCategoriesTests` case asserts.

#### 7.5.4 Sink, rotation and flush (`RotatingFileSink`)

- Path `IAppPaths.LogsDirectory\shotai.log`; archive `shotai.old.log`; `Directory.CreateDirectory` before each batch (corrected in WP-A11: not only on the first write, so a logs folder removed while the app runs comes back).
- `Log` calls format the line on the calling thread and `TryWrite` it into `Channel.CreateBounded<string>(new BoundedChannelOptions(10_000) { FullMode = BoundedChannelFullMode.DropWrite, SingleReader = false, AllowSynchronousContinuations = false }, itemDropped)`. Never blocks, never throws, allocation is the formatted string only; safe to call from the hook thread (02). A dropped line increments a counter; the next batch starts with `[<timestamp>] [warn]  (main)     log: <n> line(s) dropped`, stamped when that batch is written. Corrected in WP-A11: `DropWrite` makes `TryWrite` return `true` for the line it drops, so the counter is the channel's `itemDropped` callback (a count of failed `TryWrite` calls would never count); `SingleReader` is `false`, because `Flush` reads too, one reader at a time under the writer's lock; the count also takes the lines a batch could not write (below) and lines that could not be built (7.5.2).
- One writer loop (corrected in WP-A11: an async loop on the thread pool that awaits `WaitToReadAsync`, so it holds no thread while idle; `TaskCreationOptions.LongRunning` would apply to its first segment only): wait for lines, take queued lines up to 256 KiB into one buffer (at least one, so a longer line is a batch of its own), then: open `new FileStream(path, FileMode.Append, FileAccess.Write, FileShare.ReadWrite | FileShare.Delete, bufferSize: 0)`; if `stream.Length > MaxBytes` (5 242 880): close, `File.Move(path, oldPath, overwrite: true)`; on failure crop: keep the whole lines of the last `min(MaxBytes / 4, 262144)` bytes, that is, read from one byte before the cut and keep what follows the first `\n` in it, so a cut that lands on a line start keeps that line (IMPROVEMENT: no half line, no half UTF-8 sequence), rewrite the file as `[log cropped]\r\n` + tail; reopen; write the buffer; close. Open-per-batch mirrors electron-log's open, append, close per line and never holds the file between batches (EDGE-INFRA-22, EDGE-INFRA-40). An `IOException` on open retries once after 50 ms, and any other failure to open fails at once; then the batch is dropped, and its lines and the report it carried are counted for the next report (corrected in WP-A11: `Debug.WriteLine` only would have lost them without a trace).
- `Flush(TimeSpan timeout)`: writes on the calling thread, under the writer's lock, the lines queued when it was called and a pending report, and returns false when the lock was not free within `timeout`. It does not chase lines logged while it runs, so it always returns (added in WP-A11). Called by 03's `AppDomain.CurrentDomain.UnhandledException` handler (the process is ending) and by `App.OnExit`. `Dispose` completes the channel, so later lines are ignored, and flushes with the 2 s cap of 7.10; it is idempotent.
- Bound: two files, each at most `MaxBytes` plus one batch, which is 256 KiB or a single longer line (INV-INFRA-20).

Banner (first lines, logged by `App.OnStartup` step 1 through the empty label, that is a logger created with `loggerFactory.CreateLogger(LogCategories.Banner)` where `LogCategories.Banner = "ShotAI.Banner"` is a reserved category string that `LogCategories` maps to the empty label, so a filter rule can name it; it must pass the `Information` minimum level): `` shotAI starting \u2014 win32/<arch> · <version> · packaged=<true|false> `` where `arch` is `RuntimeInformation.ProcessArchitecture` lowercased (`x64`, `arm64`), `version` is `AppVersion.Current.Display`, `packaged` is `true` for a Release build; then `logs: <absolute path of shotai.log>`. 03's runtime line follows later. REQUIRED intent; exact text IMPROVEMENT (Electron printed the Electron version).

#### 7.5.5 Never logged (REQUIRED [SECURITY])

| Never | Examples |
|---|---|
| Credentials | the Anthropic API key, any `Authorization` or `x-api-key` header value, OAuth access, refresh and ID tokens, the federation assertion and `sk-ant-oat01-` token, MSAL cache bytes, DPAPI blobs, `secrets.json` content |
| Organization-identifying policy values | the federation ids (08 INV-AUTH-6); names of missing or invalid values may be logged |
| Personal data | the signed-in UPN or e-mail (08 INV-AUTH-31, 06 INV-HOME-32), `userName` |
| User content | screenshot pixels or base64, OCR text, captions, instructions, notes, SOP request and response bodies, custom instructions, project titles |
| File content | `settings.json` or `project.json` text (a parse failure logs the status, not the content) |
| Full URLs of refused links | only the origin (`ipc.ts:305`) |

Allowed: file and folder paths, ids (step and project UUIDs), counts, sizes, dimensions, durations, HTTP statuses, request ids, model ids, version strings, exception types and messages from the BCL. MSAL's own log is bridged by 08 with PII off.

### 7.6 Update check (Core)

#### 7.6.1 App version

```csharp
public sealed record AppVersion(string Display)
{
    public static AppVersion FromInformational(string informationalVersion);  // cut at the first '+'
    public static AppVersion Current { get; set; }   // set once by App from the entry assembly's
                                                     // AssemblyInformationalVersionAttribute
}
```

`Display` is what the User-Agent, the log and About show (`2.0.0-alpha.0`, later `2.0.0`). `Directory.Build.props` `<Version>` is the source (12 sets `IncludeSourceRevisionInInformationalVersion` as it likes; the cut makes it irrelevant, EDGE-INFRA-29). REQUIRED.

#### 7.6.2 Pure functions (`UpdateCheck`)

```csharp
public static class UpdateCheck
{
    public const string ReleasesApi = "https://api.github.com/repos/Armadillon44/shotAI/releases/latest";
    public static readonly TimeSpan CheckInterval = TimeSpan.FromMilliseconds(86_400_000);
    public static readonly TimeSpan Timeout = TimeSpan.FromMilliseconds(10_000);
    public static string NormalizeTag(string tag);                    // JsString.Trim, then remove one leading 'v' or 'V'
    public static double JsParseInt(string s);                        // 2.8.2 semantics; NaN when no digits
    public static int CompareVersions(string a, string b);            // parity formula; segments as doubles via (JsParseInt(s) is NaN or 0 ? 0 : value)
    public static bool IsNewer(string current, string latest);        // see below
    public static bool ShouldCheck(double lastCheckedMs, double nowMs, double intervalMs = 86_400_000);
    public static UpdateDecision StartupDecision(bool enabled, double lastCheckedMs, double nowMs);
    public static (string Version, string Url)? PickRelease(JsonNode? body);
}
public enum UpdateSkipReason { Disabled, Throttled }                  // log text "disabled", "throttled"
public sealed record UpdateDecision(bool Run, UpdateSkipReason? Reason);
public sealed record UpdateCheckResult(bool Available, string? Version = null, string? Url = null, string? Error = null);
```

- `ShouldCheck`: `lastCheckedMs == 0 || double.IsNaN(lastCheckedMs)` true; `lastCheckedMs > nowMs` true; else `nowMs - lastCheckedMs >= intervalMs`. REQUIRED.
- `StartupDecision`: disabled checked before throttled (REQUIRED).
- `PickRelease(body)`: `body` not a `JsonObject`: null; `draft` or `prerelease` is JSON `true`: null; `tag_name` or `html_url` not a JSON string: null; `v = NormalizeTag(tag)`; `!Regex.IsMatch(v, "^[0-9]+(\\.[0-9]+)*", CultureInvariant)`: null; `!html_url.StartsWith("https://github.com/", StringComparison.Ordinal)`: null; else `(v, html_url)`. REQUIRED.
- `IsNewer(current, latest)`: `c = CompareVersions(latest, current)`; `c > 0` true; `c == 0 && HasSuffix(current) && !HasSuffix(latest)` true (IMPROVEMENT, EDGE-INFRA-31: a running `2.0.0-alpha.3` is offered `2.0.0`); else false. `HasSuffix(v) = NormalizeTag(v).Contains('-')`. `CompareVersions` itself keeps parity (its tests port unchanged).

#### 7.6.3 Feed request (`ReleaseFeed`)

```csharp
public sealed class ReleaseFeed(HttpMessageHandler sharedHandler, TimeProvider time)
{
    public Task<UpdateCheckResult> CheckForUpdateAsync(string currentVersion, Uri? url = null,
                                                       TimeSpan? timeout = null, CancellationToken ct = default);
}
```

- `HttpClient`: `new HttpClient(sharedHandler, disposeHandler: false) { Timeout = Timeout.InfiniteTimeSpan }` over 08's `ISharedHttp.Handler` (one `SocketsHttpHandler` with the Windows system proxy, `DefaultProxyCredentials`, the Windows certificate store). `ReleaseFeed` adds no `DelegatingHandler` to it: the shared handler carries no Anthropic-specific or update-specific handler, because it also serves MSAL and the Anthropic clients (R-ARCH-15). "System proxy" means the Windows proxy settings, not proxy environment variables, which the composition root removes at startup step 5 (ARCHITECTURE 4.2, I-5, Q-ARCH-3 default). IMPROVEMENT: the check now works behind corporate proxies and TLS inspection (Electron's Node `fetch` did not). `SocketsHttpHandler` keeps its default `UseCookies = true` and one `CookieContainer` for every client of the shared handler; cookies are domain-scoped, so nothing set by `api.anthropic.com` or `login.microsoftonline.com` is ever sent to `api.github.com`, and a cookie GitHub sets would only be returned to GitHub within the same process. The request carries no `Authorization` header and, on the first request of a process, no `Cookie` header (unit tests assert both).
- Request: `GET`, headers `Accept: application/vnd.github+json`, `User-Agent: shotAI/<currentVersion>` (added with `TryAddWithoutValidation`), and `X-GitHub-Api-Version: 2022-11-28` (IMPROVEMENT, as macOS; GitHub's recommended pinning; harmless).
- Timeout: `using var timeoutCts = new CancellationTokenSource(timeout ?? UpdateCheck.Timeout, time);` (the `CancellationTokenSource(TimeSpan, TimeProvider)` constructor, .NET 8 and later, so `FakeTimeProvider` drives it in tests) and `using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct, timeoutCts.Token);`; `linked.Token` is passed to `SendAsync` AND to the body read, so the timer covers headers and body as in Electron. An `OperationCanceledException` (including `TaskCanceledException`) when `timeoutCts.IsCancellationRequested && !ct.IsCancellationRequested`: `Error = "timed out"` (EDGE-INFRA-27). Dispose the request and the response (`using`).
- Response: after `SendAsync`, IMPROVEMENT: if `response.RequestMessage?.RequestUri?.Host` (the handler's redirect support rewrites the request's URI to the final location) is not `api.github.com` (ordinal, ignore case) the result is `Error = "no usable release in the response"` (redirects are still followed, but only on-host answers count; macOS pins the same host). Not 2xx: `Error = "GitHub returned " + (int)status`. Body: read with the linked token, `JsJson.Parse`; a parse failure: `Error = "no usable release in the response"` (IMPROVEMENT, EDGE-INFRA-28). `PickRelease` null: the same error. `!IsNewer`: `new(false)`. Else `new(true, version, url)`.
- Any other exception: `Error = ex.Message` (the `HttpRequestException` message, for example `No such host is known. (api.github.com:443)`). The exact network error wording is platform text and not REQUIRED; the three fixed strings are.
- Never throws (INV-INFRA-24) except `OperationCanceledException` when the CALLER's `ct` is cancelled (app exit).

#### 7.6.4 Service (`UpdateService`)

```csharp
public interface IUpdateService
{
    UpdateCheckResult? Pending { get; }                     // Volatile; only Available results
    event EventHandler<UpdateCheckResult>? UpdateAvailable; // raised on a pool thread, once per launch at most
    Task RunStartupCheckAsync(CancellationToken ct = default); // 03 startup step 13; never throws
    Task<UpdateCheckResult> CheckNowAsync(CancellationToken ct = default);   // Settings "Check now"
}
public sealed class UpdateService(ISettingsService settings, ReleaseFeed feed, AppVersion version,
                                  IAppLifetime lifetime, TimeProvider time, ILogger<UpdateService> log) : IUpdateService;
// 11 7.3.6 declares RunStartupCheckAsync(CancellationToken ct = default) and "never throws"; 11 wins on the signature.
```

Startup state machine (REQUIRED unless marked):

| State | Event | Guard | Action | Next |
|---|---|---|---|---|
| Idle | `RunStartupCheckAsync` | | `d = UpdateCheck.StartupDecision(settings.Current.UpdateCheckEnabled, settings.Current.LastUpdateCheckAt, time.GetUtcNow().ToUnixTimeMilliseconds())` | Decided |
| Decided | | `!d.Run` | debug `update check skipped (disabled)` or `(throttled)` | Done |
| Decided | | `d.Run` | `r = await RunSharedAsync(ct)` (below) | Checked |
| Checked | | | `await settings.UpdateAsync(s => s with { LastUpdateCheckAt = now2 })` where `now2 = time.GetUtcNow().ToUnixTimeMilliseconds()` after the request; a failure logs warn `update check: could not record the check time:` with the exception and CONTINUES (IMPROVEMENT, EDGE-INFRA-34) | Stamped |
| Stamped | | `r.Error != null` | info `update check could not complete: <error>` | Done |
| Stamped | | `!r.Available` | debug `update check: up to date (<version.Display>)`: the RUNNING version, as Electron logs `app.getVersion()` | Done |
| Stamped | | available | info `update available: <version>`; `Pending = r`; THEN raise `UpdateAvailable(r)` | Done |
| any | unexpected exception, except `OperationCanceledException` from `ct` at exit (swallowed silently) | | warn `startup update check failed (non-fatal):` with the exception; never rethrown | Done |

`CheckNowAsync` (REQUIRED): ignores `UpdateCheckEnabled` and the throttle; `r = await RunSharedAsync(ct)`; stamp `LastUpdateCheckAt` (IMPROVEMENT, EDGE-INFRA-45: a stamp failure is logged as above and does not change `r` or skip the next step); `Pending = r.Available ? r : null` (a manual "up to date" or error clears it, EDGE-INFRA-33); does NOT raise `UpdateAvailable`; returns `r`. 06 builds the message (`shotAI <v> is available.`, `Couldn't check: <error>`, `You're up to date.`) and opens `r.Url` through `IExternalLinks`.

`RunSharedAsync(ct)` (IMPROVEMENT, from macOS): under a lock, if a request is in flight take the same `Task<UpdateCheckResult>`; else start `feed.CheckForUpdateAsync(version.Display, ct: lifetime.Stopping)` (the APP lifetime token, never a caller's) and clear the field when it completes; then `return await shared.WaitAsync(ct)`, so a caller's cancellation abandons only its own wait (EDGE-INFRA-50). A joining caller gets the first caller's result, as on macOS. A startup check and a Check now click never cost two requests.

Threading: `RunStartupCheckAsync` is started fire and forget on the thread pool under `IAppLifetime.Stopping` at ARCHITECTURE 4.2 step 13, after the main window is shown; nothing waits on it, and a failure is logged, never shown. `UpdateAvailable` is raised through `EventRaiser.Raise` outside any lock (T5). The UI SUBSCRIBES to `UpdateAvailable` first and THEN reads `Pending` (11 INV-IPC-7, T7; 06 7.10 `NoticeCenter`), and the handler marshals with `IUiDispatcher.Post` only (T6, R-ARCH-11; a payload event, never coalesced, ARCHITECTURE 6.3) (EDGE-SHELL-19, 06 EDGE-HOME-35); because `Pending` is set before the event is raised, either order of arrival shows the notice exactly once.

### 7.7 External links (`IExternalLinks`)

The contract and the algorithm are 11 7.3.4's (11 wins on names and signatures); this spec owns the registration, the refusal log line of 2.8.9 and the tests. Restated here so it can be implemented from one place:

```csharp
namespace ShotAI.Core.Links;

public interface IExternalLinks { Task<bool> OpenAsync(string url, CancellationToken ct = default); }  // 11: string, not string?
public interface IUrlLauncher { Task LaunchAsync(string absoluteUri); }                              // Platform: ShellUrlLauncher
public static class ExternalLinkPolicy
{
    // 2.8.9 step 3, pure: u.Scheme == Uri.UriSchemeHttps && (h == "anthropic.com"
    //   || h.EndsWith(".anthropic.com", StringComparison.Ordinal) || h == "github.com"), h = u.IdnHost.ToLowerInvariant()
    public static bool IsBaseAllowed(Uri u);
}
public static class UrlOrigin
{
    // WHATWG origin serialization: http, https, ws, wss, ftp: scheme + "://" + lower-case IdnHost
    // (+ ":" + port when not the scheme default); every other scheme: "null" (EDGE-INFRA-51).
    public static string Of(Uri u);
}
public sealed class ExternalLinks(ISupportUrlAllowlist supportUrls, IUrlLauncher launcher,
                                  ILogger<ExternalLinks> log) : IExternalLinks;
```

`OpenAsync(url, ct)`, exactly 11 7.3.4: (1) `url` null: `false`; (2) `!Uri.TryCreate(url, UriKind.Absolute, out var u)`: `false`, no log; (3) `IsBaseAllowed(u)`: go to 6; (4) `u.Scheme == "https"` and `await supportUrls.IsAllowedAsync(u, ct)` (08 7.13: exact origin of a configured SupportUrl, consulted only while federation is configured, a malformed configured value never widens): go to 6; (5) log Warning `refused openExternal for non-allowlisted URL: {UrlOrigin.Of(u)}` (category `svc`, 7.5.3) and return `false`; (6) `await launcher.LaunchAsync(u.AbsoluteUri)`; return `true`. `EndsWith` MUST be ordinal: the culture-sensitive overload is both an analyzer error (CA1310, warnings are errors) and wrong under some cultures.

`ShellUrlLauncher.LaunchAsync(uri)` (Platform): `Process.Start(new ProcessStartInfo(uri) { UseShellExecute = true })` on 11's `StaThread.RunAsync` (ShellExecute wants an STA); only ever called with an `https` URI that passed the policy; no arguments are ever appended; it is the only code in the solution that hands a URL to the shell (11 INV-IPC-3). A launch exception propagates, so `OpenAsync` throws, which is parity with Electron's awaited `shell.openExternal` (2.8.9 step 6). This is 11's contract and is binding (R-ARCH-25, answering Q-INFRA-22): `OpenAsync` returns `false` when refused and throws only if the launcher throws; 06's callers wrap the call and show nothing. REQUIRED [SECURITY].

Divergences: `IdnHost` makes a Unicode host compare in its punycode form, as WHATWG does (REQUIRED intent); a trailing-dot host (`github.com.`) is refused, as in Electron; user info and a non-default port on an allowed host are ALLOWED, as in Electron (parity with 11; refusing user info as macOS does is proposed in Q-INFRA-21, not adopted). Known parse differences accepted by 11: .NET `Uri` rejects a few inputs WHATWG accepts (`https:anthropic.com` without slashes); every URL shotAI itself produces parses the same way.

### 7.8 Self-test switches

```csharp
public enum StartupModeKind { Normal, StoreSelfTest, CaptureSelfTest, UpdateSelfTest }
public sealed record StartupMode(StartupModeKind Kind, string? UpdateSelfTestVersion = null);
public static class StartupModeParser
{
    public static StartupMode Parse(IReadOnlyList<string> args, Func<string, string?> env);
}
public enum SelfTestOutcome { Pass = 0, Fail = 1, Error = 2 }   // = process exit code
```

`Parse` (first match wins; REQUIRED order store before capture as in `main.ts:403-414`):

| Kind | Command-line switch (native, primary) | Environment (kept for existing scripts and docs) |
|---|---|---|
| StoreSelfTest | `--selftest` | `SHOTAI_SELFTEST` exactly `1` |
| CaptureSelfTest | `--capture-selftest` | `SHOTAI_CAPTURE_TEST` exactly `1` |
| UpdateSelfTest (IMPROVEMENT) | `--update-selftest` or `--update-selftest=<version>` | none |

Switches are matched ordinally and case-insensitively; unknown arguments are ignored. 03 runs the mode at its startup step 3 (after logging, 12's `PersonalCopyGuard` of ARCHITECTURE step 1b, which in a self-test mode only logs, the single-instance lock and 12's `LegacyInstanceGuard` of step 2a, so, as in Electron, a self-test exits early when shotAI is already running, and it also refuses while an Electron 1.x instance runs; before settings are loaded for the real app, before any window): the self-test builds what it needs, runs, writes its lines, and calls `Shutdown((int)outcome)`.

Console: the app is a `WinExe`. `ConsoleAttach.Ensure()` (Platform): if `GetStdHandle(STD_OUTPUT_HANDLE)` is a valid handle (output redirected to a file or pipe) use it; else `AttachConsole(ATTACH_PARENT_PROCESS)`; if that fails there is no console and output goes to the log only. After attaching: `Console.SetOut(new StreamWriter(Console.OpenStandardOutput(), new UTF8Encoding(false)) { AutoFlush = true })`, same for `Error`. Every self-test line is ALSO logged at info under `main`. Documented invocation for scripts: `Start-Process .\shotAI.exe -ArgumentList '--selftest' -Wait -PassThru -RedirectStandardOutput out.txt` and read `.ExitCode` (a GUI-subsystem exe does not block the shell otherwise).

**Store self-test** (`StoreSelfTest.RunAsync(ProjectStoreFactory, IAppPaths, TextWriter out, TextWriter err, ILogger)`, Core, runs on Linux too):

1. `testRoot = Path.Combine(paths.TempDirectory, "shotai-selftest-" + Environment.ProcessId)`; `settingsFile = testRoot + ".settings.json"` (a sibling, not inside the projects root).
2. IMPROVEMENT (INV-INFRA-30, EDGE-INFRA-35): build an isolated `SettingsService` over `settingsFile` and a `ProjectStore` over it; the user's `settings.json` is never opened.
3. `store.SetProjectsDirAsync(testRoot)`; print `[selftest] projectsDir  = <dir>` where `<dir>` is read back with `GetProjectsDirAsync()`, as Electron does.
4. Steps and printed lines exactly as 2.9 (the `manifest` line prints `v<version> "<title>" steps=<n>`; JSON strings use 01's `JsJson` quoting; booleans print `true`/`false`).
5. PASS condition exactly as 2.9 with `UUID_RE` as `^[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}\z` (ASCII, `\z` per EDGE-INFRA-43). Print `[selftest] PASS` (outcome Pass) or `[selftest] FAIL` (Fail).
6. Exception: `err.WriteLine("[selftest] ERROR " + ex)`; outcome Error.
7. Finally: dispose the isolated store and settings service (flushing their queues), then delete `testRoot` recursively (01's `ReparseSafeDelete`) and `settingsFile`, errors ignored; a failure in this block never changes the outcome and never prevents the exit (the Electron hang risk of 2.9 does not exist natively).

**Capture self-test**: 02 owns the body (02 2.16, with the macOS-corrected size assertions); this spec owns the switch, the console, and the exit code (`[capture-test] PASS` maps to 0, `FAIL` to 1, an unhandled exception to 2). It uses the same isolation as the store self-test.

**Update self-test** (IMPROVEMENT, from `macOS:Packages/UpdateKit/Sources/UpdateSelfTest/main.swift`; lets IT verify the proxy and TLS path on their network): `installed = switch value ?? AppVersion.Current.Display`; print `[update-test] endpoint: <ReleasesApi>` and `[update-test] installed version under test: <installed>`; `r = feed.CheckForUpdateAsync(installed)` (one request; no settings read or written, no stamp); print `[update-test] result: available=<bool> version=<v or -> url=<u or -> error=<e or ->`; PASS iff `r.Error is null`; print `[update-test] PASS` or `[update-test] FAIL`. Not run in CI (live network).

### 7.9 Fonts and licensing

- Repository: copy `src/renderer/fonts/Archivo.ttf` and `src/renderer/fonts/OFL.txt` into `dotnet/assets/fonts/` now (byte-identical; a test asserts equality with the Electron copies while they exist, INV-INFRA-32), because the Electron tree is deleted at cutover.
- WPF: the static instances of Q-HOME-2 (from the upstream Omnibus-Type/Archivo release, unmodified; 06 lists the weights and widths) live beside them in `dotnet/assets/fonts/static/` with a `SOURCES.md` recording the upstream release tag and each file's sha256. They are OFL files of the same project; the same `OFL.txt` covers them. No Reserved Font Name is declared, so instancing the variable file ourselves would also be permitted, but upstream statics are preferred (nothing to verify about our own instancing).
- Output layout (12 packages it): `<install>\Fonts\Archivo.ttf` (variable, for the WebView2 PDF print page, 03's `AppPaths.BrandFontPath()` and 09's print `@font-face`), `<install>\Fonts\static\*.ttf` (WPF, embedded as resources or loose files per 06), and `<install>\Fonts\OFL.txt` (and a copy in `Fonts\static\` if the statics ship as loose files) (INV-INFRA-31).
- Obligations (REQUIRED, from `OFL.txt:47-78`): the licence and copyright travel with every copy; the font is never offered for sale on its own; the README and the third-party notices file (12) state that the MIT licence does not cover Archivo and that `Fonts\OFL.txt` is its licence; the Archivo authors' names are not used to promote shotAI.
- Exports never embed the face as base64; only the PDF print copy references the file (09; INV-INFRA-32).

### 7.10 Threading, cancellation and disposal

| Component | Thread | Cancellation | Disposal |
|---|---|---|---|
| `SettingsService.Load` | UI thread at startup, synchronous (tiny file), ARCHITECTURE 4.2 step 5b, before the container is built | none | |
| `UpdateAsync` optimistic step | caller's thread (usually UI); the write job on the settings queue consumer (MTA pool, ARCHITECTURE 6.1), which never touches a UI object | `ct` honored before the job starts (01 queue semantics); a started write completes (ARCHITECTURE 6.5 K1, K6) | `FlushAsync` inside 11's `ShutdownFlush.Run(5 s)` on exit (one budget with the store's flush, R-ARCH-10), then `Dispose` (synchronous, idempotent, ARCHITECTURE 4.1 C5) |
| Caches | any thread, lock-free | | |
| Logging `Log` | any thread, non-blocking | | provider `Dispose` drains with a 2 s cap |
| Log writer | one async loop on the thread pool (7.5.4) | ends once disposal has completed the channel and it is empty | |
| Update startup check | thread pool, started at ARCHITECTURE 4.2 step 13 | `IAppLifetime.Stopping` | the in-flight request is cancelled at exit; the cancellation is swallowed silently |
| `CheckNowAsync` | awaited by Settings (the continuation may resume on the UI thread; nothing blocks it) | the view's token cancels only the wait; the shared request runs under `IAppLifetime.Stopping` (EDGE-INFRA-50) | |
| `ExternalLinks.OpenAsync` | caller; `ISupportUrlAllowlist` may await | `ct` | |
| Self-tests | started from 03's `OnStartup` step 3 on the UI thread and AWAITED there (`OnStartup` continues asynchronously on the running dispatcher, then calls `Shutdown((int)outcome)`); the self-test bodies use `ConfigureAwait(false)` and run their I/O on the pool. Never `GetAwaiter().GetResult()` or `.Wait()`: 11 INV-IPC-6 bans both in `ShotAI.App` outside `ShutdownFlush.cs` | none | isolated store and settings disposed in the self-test's `finally` |

### 7.11 APIs and packages

| Kind | Items |
|---|---|
| NuGet (Core) | `Microsoft.Extensions.Logging.Abstractions` and `Microsoft.Extensions.DependencyInjection.Abstractions` (for `AddShotAICore`), both added by the foundation work package WP-A1 (ARCHITECTURE 3.2, 3.5); `System.Collections.Frozen` is in the BCL |
| NuGet (App) | `Microsoft.Extensions.Logging` (for `LoggerFactory`), `Microsoft.Extensions.Logging.Debug` (Debug configuration only), `Microsoft.Extensions.DependencyInjection` (ARCHITECTURE 3.2) |
| NuGet (tests) | `Microsoft.Extensions.TimeProvider.Testing` (Core.Tests; added by WP-A1, ARCHITECTURE 3.2) |
| BCL | `System.Text.Json.Nodes` (through 01's `JsJson`), `System.Security.Cryptography.SHA256`, `System.Threading.Channels`, `System.Net.Http`, `System.Diagnostics.Process`, `System.Runtime.InteropServices.RuntimeInformation` |
| CsWin32 (`NativeMethods.txt`) | `AttachConsole`, `GetStdHandle`, `ATTACH_PARENT_PROCESS`, `STD_OUTPUT_HANDLE`, `STD_ERROR_HANDLE` |
| Versions | only in `dotnet/Directory.Packages.props`, exact, lock files committed (ARCHITECTURE 3.1 V1 to V3) |

Composition (ARCHITECTURE 4.1 C7 and 4.3: each registration lives in the extension method of the project that holds the type):

```csharp
// App: AddShotAIApp
services.AddSingleton<IAppPaths, AppPaths>();
services.AddSingleton(settingsLoadedAtStep5b);                                   // concrete SettingsService, ARCHITECTURE 4.2 step 5b
                                                                                 // (01 7.14 resolves the concrete type)
// Core: AddShotAICore (forwarders to the instance above; TimeProvider is registered by Core, ARCHITECTURE 4.3)
services.AddSingleton<ISettingsService>(sp => sp.GetRequiredService<SettingsService>());
services.AddSingleton<IProjectStoreSettings>(sp => sp.GetRequiredService<SettingsService>());
services.AddSingleton<ICaptureSettings>(sp => sp.GetRequiredService<SettingsService>());
services.AddSingleton(sp => new ReleaseFeed(sp.GetRequiredService<ISharedHttp>().Handler, sp.GetRequiredService<TimeProvider>()));
services.AddSingleton(_ => AppVersion.Current);                                  // set by the App before the container is built
services.AddSingleton<IUpdateService, UpdateService>();
services.AddSingleton<IExternalLinks, ExternalLinks>();
// Platform: AddShotAIPlatform
services.AddSingleton<IUrlLauncher, ShellUrlLauncher>();                        // internal sealed, registered only as its Core interface (INV-ARCH-4)
```

An instance registration is not disposed by the container, but each forwarding FACTORY registration hands the same object to the container's disposal list, so `SettingsService.Dispose` is called up to three times at exit and MUST be idempotent. The self-test modes (7.8) run before this container exists (ARCHITECTURE 4.2 step 3) and build their own isolated instances. View models depend only on `ISettingsService`, `IUpdateService` and `IExternalLinks`, never on the concrete `SettingsService` (INV-ARCH-3).

### 7.12 Divergence summary

| Item | Class | Justification |
|---|---|---|
| C# generator and checked-in table | REQUIRED pattern | third platform on the proven contract |
| Generator fails on a missing `windows` radius or a null non-chip radius | IMPROVEMENT | a silent capsule is a visual bug |
| `PostScriptName` carried in the C# table | IMPROVEMENT | free, useful for fonts |
| Settings in memory, optimistic, rollback | IMPROVEMENT | fixed decision |
| BOM tolerance, corrupt backup, load warnings | IMPROVEMENT | hand edits are prescribed (06 AC-HOME-22); silent data loss today |
| Preserve unknown enum strings and nested `sop` keys | IMPROVEMENT | #92's principle one level down |
| Invalid `projectsDir` loads as default | IMPROVEMENT | the working directory of an installed app (either install scope, 12 7.4.5) is not a projects folder |
| Drain settings on exit | IMPROVEMENT | a last write is not lost |
| Native-only local data under `%LOCALAPPDATA%\LFI\shotAI` (`IAppPaths.LocalDataDirectory`) | IMPROVEMENT (D-ARCH-2) | the Squirrel root `%LOCALAPPDATA%\shotAI` would be deleted with Electron (R-ARCH-13) |
| Log sink: batched, non-blocking, crop on line boundary | IMPROVEMENT | UI and hook threads never wait on disk |
| `SHOTAI_LOG_LEVEL` | IMPROVEMENT | field troubleshooting |
| Update check over the system proxy | IMPROVEMENT | corporate networks (feasibility "Networking") |
| `X-GitHub-Api-Version`, final-host check, in-flight join | IMPROVEMENT | macOS precedent, defense in depth, one request |
| `timed out` always; parse failure message | IMPROVEMENT | intent of Electron's code |
| A running prerelease is older than its final | IMPROVEMENT | the 2.0.0 pilot |
| A stamp failure does not suppress the notice | IMPROVEMENT | a disk hiccup must not hide news |
| Links with user info refused | proposed only (Q-INFRA-21); parity with Electron and 11 until decided | macOS precedent |
| Self-tests isolated, exit codes, command-line switches, update self-test | IMPROVEMENT | scripts and IT verification |
| `themeStylesheet`, `radiusCss`, console log transport, IPC channels and coercions | ELECTRON-ONLY | replaced by WPF resources (06), Debug provider, direct calls |

---

## 8. Tests

### 8.1 `src/shared/theme-palette.test.ts` (516 lines)

Purpose: pin the one palette definition, its generation into the stylesheet, and the design rules (contrast, nesting, retired greys, fonts).

| Group (Electron) | Cases | Port | Target |
|---|---|---|---|
| the app stylesheet is generated, not hand-maintained | declares no colour custom property of its own; does not redeclare a generated token; emits a fully-qualified block per brand and appearance; emits a bare `:root` fallback; resolves every `var(--…)` | ELECTRON-ONLY (CSS). Intent moves to 06: no hex literal in XAML outside `Themes/FixedColors.xaml`, every `DynamicResource` key exists (06's resource tests); the resources apply before the first window shows (06 `ThemeManager.ApplyInitial`) | 06 App.Tests |
| every brand defines every role | lowercase 6-digit hex; same role set; dark field lighter than surface; unknown or missing brand resolves to default (`undefined, null, '', 'LFI', 'shotai', 42, {}`; `lfi`, `shotAI` kept; `brandPalette('nonsense')` is the default light palette); `APP_LIGHT`/`APP_DARK` are aliases | Core.Tests (Linux). `42` and `{}` become `JsonNode` inputs; aliases asserted with `ReferenceEquals` | `Brand.BrandPaletteTests` |
| text meets WCAG AA on the surfaces it is drawn on | 4 generated cases (brand x appearance), each 9 surface checks plus 7 tint pairs | Core.Tests, `[Theory]` over brand x appearance | `Brand.BrandPaletteTests.TextMeetsAa` |
| one neutral ramp, and it is the app inks | retired greys distinct and 6-digit; not used by any brand; absent from the export path and app stylesheet | first two Core.Tests; the third becomes a source scan of 09's export template files and 06's XAML theme files (comments stripped), Core.Tests (the files are plain text) | `Brand.BrandPaletteTests`, `Brand.ExportSourceGuardTests` |
| hexNoHash | strips and uppercases (`#6344f1` to `6344F1`, `#FFFFFF` to `FFFFFF`); throws for `6344f1`, `#fff`, `#12345`, `#1234567`, `red`, `''`, `#gggggg`; round-trips every palette value | Core.Tests | `Brand.BrandPaletteTests.HexNoHash*` |
| typography is a brand token too | brand face first then fallbacks (LFI starts `"Archivo",`, no `Segoe`; shotAI contains `Segoe`, no `Archivo`); no condensed width (`normal`) vs `62%`; `@font-face` declares the full weight range; bundles the face as a file, never base64, OFL present; body reads `--font-stack` and no rule names a face | first: Core.Tests on `CssFontStack`; second: Core.Tests on `LabelStretch` (`null` vs 62); third: ELECTRON-ONLY, replaced by `ArchivoRenderingTests.NormalIsNotSemiBold` (App.Tests); fourth: `FontPackagingTests` (App.Tests) plus 09's no-`data:font` test; fifth: ELECTRON-ONLY, replaced by 06 (every text style uses the theme font resource, with 04's editor overlay exception) | `Brand.BrandPaletteTests`, App.Tests `Fonts.*` |
| radius is a brand token, and the scale nests | every role present; nesting; chip `null` (and `radiusCss`); radius tokens emitted per brand; geometry on the brand axis only; export constants tied to roles; the right role on document surfaces | Core.Tests for roles, nesting, chip `null`, export constants; `radiusCss` and token emission ELECTRON-ONLY; brand-axis-only holds by construction (a structural test that `BrandDefinition` has one `Radii`); document-surface roles move to 05 and 06 | `Brand.BrandPaletteTests` |

### 8.2 `src/shared/brand-narrowing.test.ts` (47 lines)

Accepts exactly the real brands; rejects each of 9 prototype keys (`isBrandId` false, `coerceBrand` default); each still yields a usable palette and font stack; `solarpunk` rejected; non-strings (`42, null, undefined, {}, [], true`) rejected. Port: Core.Tests, `Brand.BrandNarrowingTests`, plus the full 2.4 decision table including `PinnedBrand` and `PinIsUnrecognised` (6 rows x 4 functions).

### 8.3 `src/main/settings.test.ts` (75 lines)

Purpose: #92, unknown keys survive and known keys are still repaired. Cases: keeps an unknown key across a load and save (`brand: 'lfi'`, `somethingFuture: {a: [1, 2]}`); repairs a known key with a bad value (`projectsDir: 42`, `recents: 'nope'`, `unknownKey: 'kept'`); no junk for `"just a string"`, `42`, `true`, `[1,2,3]`, `null` (5 cases); survives a file that is not JSON. Port: Core.Tests (temp directory, `FakeTimeProvider`, fake rename classifier), `Settings.SettingsStoreTests`, driving `SettingsService.UpdateAsync(s => s with { Recents = [] })` as the "any write" trigger.

### 8.4 `src/main/update-check.test.ts` (189 lines)

| Group | Cases | Port |
|---|---|---|
| normalizeTag | `v1.1.5`, ` 1.1.5 `, `V2.0.0` | Core.Tests |
| compareVersions | numeric not string (`1.1.10 > 1.1.9`, `1.2.0 < 1.10.0`, `2.0.0 > 1.9.9`); missing segments zero; prerelease suffix ignored (`1.2.0-rc1 == 1.2.0`, `> 1.1.9`); symmetric | Core.Tests, unchanged |
| isNewer | strictly greater only (including "a rollback is not an update"); `v` prefix either side | Core.Tests, unchanged (the IMPROVEMENT adds cases, 8.5) |
| shouldCheck | never checked (`undefined` becomes `0` or NaN in C#); waits out the interval (`-1000`, `-interval+1`, `-interval`, `-3 intervals`); future timestamp | Core.Tests |
| pickRelease | normal release; drafts and prereleases refused; `nightly` refused; non-github URL refused; junk (`null`, `'nope'`, `{}`, no url) | Core.Tests over `JsonNode` fixtures |
| checkForUpdate | available; up to date; network failure (`ENOTFOUND` message contained); 403 exact `{available: false, error: 'GitHub returned 403'}`; malformed body gives an error; prerelease not offered | Core.Tests with a fake `HttpMessageHandler` |
| startupCheckDecision | disabled; throttled within a day; first launch and elapsed day run; disabled wins over throttled | Core.Tests |

Target: `Updates.UpdateCheckTests`.

### 8.5 New tests the native code needs

| Class (project) | Tests |
|---|---|
| `Brand.GenBrandTests` (Core.Tests, references the tool) | `GeneratesCheckedInFile`; `HashIgnoresCrlf` (CRLF input still gives `c945d17c…`); `CheckReportsStale` (exact message); `CheckIgnoresCrlfInExisting`; `InvariantViolationFails` (contract with dark field == surface2 gives the exact `invariant violated \u2014 shotAI.dark: field and surface2 are both #211f2e.` + why); `RejectsUppercaseOrShortHex` (exact message with JSON quoting); `MissingTokenFails`; `MissingBrandFails`; `PlatformValueTakesWindows`; `MissingWindowsValueFails`; `NullNonChipRadiusFails`; `ThirdBrandIgnored`; `DeterministicOutput` (two runs byte-equal, LF only, trailing newline); added in WP-A4: `CheckTreatsAMissingTableAsStale`, `OneColourChangesExactlyItsLineAndTheStamp` (AC-INFRA-2), `InvariantCoversABrandThatIsNotEmitted`, and one test per row of the 7.3 table added in WP-A4 (`ContractThatIsNotUtf8OrNotJsonFails`, `ColoursMustBeAListOfNames`, `NamesThatWouldInjectCodeAreRefused`, `LabelAndFontFieldsAreTyped`, `StringsAreEscapedCSharpLiterals`, `TheRootIsFoundFromBelowAndMustExist`) |
| `Brand.BrandContractTests` (Core.Tests) | `GeneratedFileIsCurrent`; `StampMatchesCanonicalHash`; `StampEqualsTypeScriptStamp` (skipped when the TS file is absent after cutover) |
| `Brand.BrandParityWithElectronTests` (Core.Tests) | `ValuesEqualTypeScriptTable`: parse `brand-colors.generated.ts` with a strict regex and compare every colour, radius, font field and label with the C# table (skipped after cutover) |
| `Brand.BrandPaletteTests` extra | `RoleToTokenOrderMatchesElectron` (36 rows of 2.3 in order); `RadiusTokens`; `TypeTokens` (added in WP-A4: `font-stack`, `label-stretch`); `CssFontStackLfi` exact string; `CardAndImageRadius` (10, 8) |
| `Settings.SettingsCoercionTests` (Core.Tests) | one `[Theory]` row per 2.6.3 worked example, per key, plus: `2.5` gives 3, `0.3` gives 1, `1e400` gives 90 (archive) and 0.85 (scale), `"0.7"` gives 0.85, `theme: "Dark"` gives System, `recents` filtering, 130-char name capped at 120 UTF-16 units, surrogate split kept |
| `Settings.SettingsCodecTests` (Core.Tests) | `FreshFileBytes` (the 2.6.3 example exactly, no trailing newline, LF); `KeyOrderKeepsFilePositions` (known key in the middle stays there, missing known keys appended in literal order, array-index-like unknown keys first per JS own-key order); `BomTolerated`; `NullFileIsDefaults`; `UnrecognisedBrandPreservedWhenUnchanged`; `UnrecognisedBrandReplacedWhenUserChangesIt`; `NonStringBrandRepaired`; `SopUnknownKeysPreserved`; `SopKnownKeysRepaired`; `RelativeProjectsDirIsDefault` |
| `Settings.SettingsServiceTests` (Core.Tests) | `OptimisticThenPersisted`; `RollbackOnlyTheFailedChange` (three queued, middle fails); `ConcurrentUpdatesNoLostWrite` (100 parallel `AddRecent`); `FailureDoesNotBreakQueue`; `WriteMergesExternalEdit` (edit the file between two writes; unknown and other known keys survive); `AddRecentDedupesCapsAndNeverThrows`; `CachesReadCurrentSynchronously`; `RemoteVisibleDefaultsProtected` (missing and corrupt file); `CorruptFileBackedUpOnce`; `ReportByline` (trim, gate); `DrainWritesPending`; `RenameRetryLogsOnce` (exact warn text) |
| `Settings.SettingsSchemaTests` (Core.Tests) | `KnownKeysAreExactly13` (INV-INFRA-19) |
| `Logging.FileLogLineFormatterTests` (Core.Tests) | exact line for each level and label, empty label (11 spaces), exception suffix, `\r\n` on Linux, invariant culture under `tr-TR` |
| `Logging.RotatingFileSinkTests` (Core.Tests) | `RotatesPastMax`; `NeverMoreThanTwoFiles`; `CropWhenRenameFails` (tail on a line boundary, marker line; corrected in WP-A11: a folder named `shotai.old.log` fails the rename on every OS, and the locked archive is `CropWhenTheArchiveIsLocked`, Windows only, because Linux renames over an open file); `DropsWhenFullAndReports`; `FlushWritesSynchronously`; `SecondWriterDoesNotBlock` (another handle with `FileShare.ReadWrite` appends between batches) |
| `Logging.LogCategoriesTests` (Core.Tests) | prefix mapping table: `[Theory]` rows use only namespaces from ARCHITECTURE 2.4 (for example `ShotAI.Core.Store.ArchiveEngine` gives `projects`, `ShotAI.Platform.Capture.UiaElementLocator` gives `capture`, `ShotAI.Core.Updates.UpdateService` gives `main`); `ShotAI.Banner` gives the empty label; `LabelsAtMost8Chars` (7.5.3); every table prefix is a namespace listed in ARCHITECTURE 2.4 |
| `Logging.LogPrivacyTests` (Core.Tests) | `CanaryNeverLogged` (INV-INFRA-21) |
| `Updates.UpdateCheckTests` extra | `JsParseIntSemantics` (`"0+abc"` 0, `" 7"` 7, `"-1"` -1, `"1e3"` 1, `"rc1"` NaN); `UnicodeDigitsRejected` (`"١.٢.٠"` is not a version); `RunningPrereleaseOlderThanFinal` (`IsNewer("2.0.0-alpha.3", "2.0.0")` true; `IsNewer("2.0.0", "2.0.0-rc1")` false); `BuildMetadataStripped` (`AppVersion.FromInformational("2.0.0-alpha.0+abc")`); `TimesOut` (`timed out`); `CallerCancellationPropagates`; `NonJsonBody`; `OffHostRedirectRefused`; `HeadersExact` (Accept, User-Agent, API version, no Authorization, no Cookie) |
| `Updates.UpdateServiceTests` (Core.Tests) | `DisabledMakesNoRequest`; `ThrottledMakesNoRequest`; `StampsEvenOnError`; `StampFailureStillDelivers`; `PendingSetBeforeEvent`; `OnlyAvailableIsPending`; `ManualIgnoresToggleAndThrottle`; `ManualClearsPendingOnUpToDateAndError`; `ManualDoesNotRaiseEvent`; `ConcurrentChecksShareOneRequest`; log lines exact |
| `Links.ExternalLinkPolicyTests` (Core.Tests) | allowed: `https://anthropic.com/x`, `https://console.anthropic.com/settings/keys`, `https://github.com/Armadillon44/shotAI/releases/tag/v1.3.0`, `HTTPS://GitHub.com/a`; refused: `http://github.com/`, `https://raw.githubusercontent.com/`, `https://raw.github.com/`, `https://evilanthropic.com/`, `https://anthropic.com.evil.test/`, `https://github.com./`, `file:///C:/x` (logs `...URL: null`), `javascript:alert(1)` (logs `...URL: null`), not a URL, null; allowed by parity: `https://user@github.com/`, `https://github.com:8443/x` (Q-INFRA-21); `EndsWith` is ordinal under `tr-TR`; SupportUrl extension through a fake `ISupportUrlAllowlist`; refusal logs the origin only; every URL the app builds (`console.anthropic.com/settings/keys`, a `PickRelease` URL) passes (EDGE-INFRA-24) |
| `SelfTest.StartupModeParserTests` (Core.Tests) | switch and environment table, order, case, unknown args |
| `SelfTest.StoreSelfTestTests` (Core.Tests) | `PassesOnHealthyStore` (lines exact); `DoesNotTouchUserSettings` (a sentinel `settings.json` unchanged byte for byte); `FailOnBrokenStore` (fake store) exits Fail; `ErrorPath` |
| `Fonts.FontPackagingTests` (App.Tests, Windows) | `OflBesideEveryFont`; `ArchivoMatchesElectronHash`; `OflMatchesElectron` |
| `Fonts.ArchivoRenderingTests` (App.Tests) | `NormalIsNotSemiBold` (render `FontWeight.Normal` and `SemiBold` text in the brand family; glyph stem widths differ; 06 Q-HOME-2) |
| `Shell.AppPathsTests` (App.Tests; `AppPaths` is App code) | `SettingsFileIsRoamingAppData` (equals `%APPDATA%\shotAI\settings.json`, not a package-virtualized path when run from the installed package, EDGE-INFRA-39); `LogsDirectoryIsRoamingAppData` (`%APPDATA%\shotAI\logs`); `LocalDataDirectoryIsUnderLfi` (equals `%LOCALAPPDATA%\LFI\shotAI`, R-ARCH-13); `NoPathUnderSquirrelRoot` (compared `OrdinalIgnoreCase` after `Path.GetFullPath`, no `IAppPaths` member is `%LOCALAPPDATA%\shotAI` or inside it, EDGE-PKG-22) |
| `SelfTest.SelfTestProcessTests` (App.Tests) | launches the built `shotAI.exe --selftest` with redirected output; exit code 0 and `[selftest] PASS`; `--update-selftest` is excluded (network) |
| `Brand.BrandPaletteTests` verification additions (Core.Tests) | `AllIsFullyInitialized` (EDGE-INFRA-44); `HexNoHashRejectsTrailingNewline` (`"#6344f1\n"` throws, EDGE-INFRA-43); `ForCoercesUnknownBrand` (`For("nonsense", Light)` is `ReferenceEquals` the default light palette) |
| `Brand.GenBrandTests` verification additions | `ErrorOrderMatchesElectron` (a contract missing both `radii` and a colour reports `shotAI: no radii`); `MissingAppearanceValuePrintsUndefined` (`shotAI.accent.dark: expected a lowercase 6-digit #rrggbb, got undefined`); `HexRejectsTrailingNewline`; `ToolHasNoProjectReference` (reads `ShotAI.GenBrand.csproj`, EDGE-INFRA-48); `NumbersFormatInvariant` (under a decimal-comma culture, `12` and `0.5` print as `12` and `0.5`. Corrected in WP-A4: the culture is a clone of the invariant culture with `,` as the decimal separator instead of `de-DE`, because a named culture depends on the runner's ICU data) |
| `Settings.SettingsServiceTests` consolidation additions | `ChangedRaisedOutsideLock` (a `Changed` handler that calls `Current` and `UpdateAsync` from inside the handler does not deadlock, T5); `ThrowingChangedHandlerDoesNotBreakUpdate` (logged through `EventRaiser`, the write still happens) |
| `Settings.SettingsServiceTests` verification additions | `UnreadableReReadFailsWithoutWriting` (a job whose re-read throws a sharing violation leaves the file byte-identical and rolls back, EDGE-INFRA-46); `MissingReReadUsesInMemoryBase` (delete the file between two updates; the second write keeps the first's values, not defaults); `CancelledBeforeStartRollsBack` (`IsRollback` true); `ChangedCarriesIsRollback`; `DisposeIsIdempotent` |
| Added in WP-A10 | `Settings.SettingsGoldenTests` (Core.Tests): the codec writes the bytes Electron's own settings module writes for each of 28 goldens (`Golden/settings/`, generated by the env-gated `src/main/settings-golden.test.ts`, which runs the real load, mutate and save chain with one `setLastUpdateCheckAt`); `Settings.SettingsParityWithElectronTests` (the limits and the two parity log lines read from `settings.ts` and `project.ts`); `SettingsServiceTests` also covers a transient read retried, a corrupt or non-object re-read written over from memory, a deleted folder, a relative `projectsDir` found by a write, a change that throws on `Current` or only on the disk, a job that writes when nothing changed, a synchronous `Dispose` alone refusing later changes, the load warnings, the first write's exact bytes and the `IProjectStoreSettings` members; `SettingsCoercionTests` also covers every boolean, the stamp, `projectsDir` and `Normalize`; `SettingsCodecTests` also covers the raw strings of `theme` and the three `sop` enums, a `sop` that is not an object, and that `Encode` never changes the decoded object |
| `Updates.UpdateServiceTests` verification additions | `UpToDateLogsRunningVersion`; `ManualStampFailureStillSetsPending` (EDGE-INFRA-45); `JoinedCheckSurvivesCallerCancellation` (cancel the Settings token while the startup request is in flight: the startup check still completes and stamps, EDGE-INFRA-50); `StartupCancellationIsSilent` |
| `Links.ExternalLinkPolicyTests` verification additions | `UrlOriginOfNonWebSchemeIsNull`; `LauncherExceptionPropagates` (parity, 11's contract per R-ARCH-25); `SupportUrlConsultedOnlyForHttps` |
| `Logging.FileLogLineFormatterTests` verification additions | `TraceWritesSilly`; `PercentSequencesVerbatim` (`50% %d %s` written as-is, EDGE-INFRA-49); `BannerCategoryHasEmptyLabel` |
| Added in WP-A11 | `Logging.FileLogLineFormatterTests` also covers every level's bracket, every label's padding, the time as the local clock time, `tr-TR` and a culture with other separators and digits, and that `None` is never a line; `Logging.RotatingFileSinkTests` also covers the batch bound and a line longer than a batch, the crop's 256 KiB cap and a cut on a line start, the drop report's stamp and a count that survives a failed batch, the one retry 50 ms later, a flush that returns while lines keep coming or gives up at its timeout, writes that never wait for a stuck writer, a batch or a clock that throws, a file renamed or held under the sink, a logs path that is a folder, a removed logs folder, UTF-8 without a BOM, disposal, a public constructor that starts the writer, and `TwelveMegabytesLeaveTwoFilesUnderTheBound` (AC-INFRA-15); `Logging.FileLogOptionsTests` (the numbers of 7.5.4, the rule of 7.5.1 with `SHOTAI_LOG_LEVEL`); `Logging.FileLoggerProviderTests` (`FlushWritesSynchronously` of INV-INFRA-22, the label per category, the minimum level, local time, a line that cannot be built, scopes, disposal, the public constructor); `Logging.ServiceLogTests` (11 L2); `LogCategoriesTests` also covers the labels as categories, the ordinal comparison and the labels read from `logger.ts`; `LogPrivacyTests.CanaryNeverLogged` runs the settings and store paths now, and the auth-status and SOP-request paths join it in their work packages (AC-INFRA-16, WP-E6) |

---

## 9. Acceptance criteria

**AC-INFRA-1.** `dotnet run --project tools/ShotAI.GenBrand -- --check` (from `dotnet/`) prints `gen-brand: up to date` and exits 0 on a clean checkout, on Linux and on a Windows clone with `core.autocrlf=true`.

**AC-INFRA-2.** Changing one colour in `contract/brand.json` without regenerating makes the native CI job fail at the brand check with the STALE message; regenerating changes exactly that value's line and the stamp line in `BrandPalette.Generated.cs`.

**AC-INFRA-3.** The stamp in `BrandPalette.Generated.cs` equals the stamp in `src/shared/brand-colors.generated.ts` and in the macOS table (`c945d17c…` today). Checked in WP-A4: all three are `c945d17c…`, and the macOS contract at `f445bca` is byte-identical to this repo's; `BrandContractTests.StampEqualsTypeScriptStamp` holds the TypeScript half.

**AC-INFRA-4.** `BrandParityWithElectronTests.ValuesEqualTypeScriptTable` passes: every value of the C# table equals the TypeScript table.

**AC-INFRA-5.** Setting shotAI's dark `field` to its `surface2` value makes the generator exit 1 with `gen-brand: invariant violated \u2014 shotAI.dark: field and surface2 are both #211f2e.` followed by the `why` text.

**AC-INFRA-6.** `BrandPaletteTests` and `BrandNarrowingTests` pass on Linux, including AA contrast for all four brand and appearance combinations and all nine prototype keys.

**AC-INFRA-7.** A native first launch on a machine that already has an Electron `settings.json` shows the same projects folder, recents, SOP settings, brand, theme, capture quality, archive age, name and toggles.

**AC-INFRA-8.** Manual: add `"somethingFuture": {"a": [1, 2]}` and `"captureNoHide": true` to `settings.json`, change any setting in the native app, and both keys are still present in their original positions; the file is 2-space indented with no BOM and no trailing newline.

**AC-INFRA-9.** Manual: set `"archiveAgeDays": 5000`, `"captureScale": 2`, `"theme": "Dark"`, `"recents": ["C:\\a", 1]`, restart: Settings shows 1825 days, 100 percent quality, System theme; after the next change the file holds `1825`, `1`, `"system"`, `["C:\\a"]`.

**AC-INFRA-10.** Manual: replace `settings.json` with `not json {{{`, launch: the app starts with defaults, the log has `settings.json is not a settings object, using defaults`, and `settings.json.bad` holds the original text after the first change.

**AC-INFRA-11.** Manual: save `settings.json` as "UTF-8 with BOM" in Notepad after editing `userName`; the native app shows the edited name.

**AC-INFRA-12.** `SettingsServiceTests.RollbackOnlyTheFailedChange` passes, and manually: with `settings.json` made read-only, toggling "Check for updates" shows an error notice and the switch returns to its previous state.

**AC-INFRA-13.** `SettingsServiceTests.CachesReadCurrentSynchronously` passes, and toggling remote visibility in Settings changes the display affinity of open windows without a restart (02 and 03 procedures).

**AC-INFRA-14.** The log file is `%APPDATA%\shotAI\logs\shotai.log`; its first line matches `^\[\d{4}-\d{2}-\d{2} \d{2}:\d{2}:\d{2}\.\d{3}\] \[info\] {13}shotAI starting \\u2014 win32/(x64|arm64) · .+ · packaged=(true|false)$` with the real U+2014 in place of the escape, and the second line is `logs: <that path>`.

**AC-INFRA-15.** `RotatingFileSinkTests` pass on Linux and both Windows legs, among them `TwelveMegabytesLeaveTwoFilesUnderTheBound`, which writes 12 MB of Debug lines through the provider and leaves exactly `shotai.log` and `shotai.old.log`, each under 5.5 MB. Corrected in WP-A11: the 12 MB check was manual, but the app has no way to write 12 MB of lines on demand, and the test makes the same write through the same code.

**AC-INFRA-16.** `LogPrivacyTests.CanaryNeverLogged` passes; a code review of every `Log*` call finds no value from the never-log list.

**AC-INFRA-17.** `UpdateCheckTests` (all 25 ported cases plus the new ones) and `UpdateServiceTests` pass on Linux.

**AC-INFRA-18.** Manual, on a per-user or unpackaged build whose version is set below the latest release (a per-machine install shows no download action, 06 INV-HOME-45): first launch shows `shotAI <latest> is available.` with `Open the download page` on Home even when the window was slow to appear; `settings.json` then holds a `lastUpdateCheckAt` within a second of the check; a second launch the same day makes no request (verified with a network trace) and shows no notice.

**AC-INFRA-19.** Manual: turn "Check for updates" off, restart: no request is made at startup (network trace); the log has `update check skipped (disabled)` at debug level only when debug logging is on; `↻ Check now` still checks and reports.

**AC-INFRA-20.** Manual: offline, `↻ Check now` shows `Couldn't check: <message>`; with a local proxy that never answers, it shows `Couldn't check: timed out` after about 10 s and the UI stays responsive throughout.

**AC-INFRA-21.** Manual: behind an authenticating corporate proxy (system proxy settings only, no environment variables), `↻ Check now` succeeds.

**AC-INFRA-22.** Manual: a pilot build versioned `2.0.0-alpha.3` checking a feed whose latest is `2.0.0` shows `shotAI 2.0.0 is available.`; a build versioned `2.0.0` checking a latest `2.0.0` shows `You're up to date.`

**AC-INFRA-23.** `ExternalLinkPolicyTests` pass; manually, on a per-user or unpackaged build, `Open the download page` opens the GitHub release page in the default browser, and a crafted `http://github.com/` link is refused with `refused openExternal for non-allowlisted URL: http://github.com` in the log.

**AC-INFRA-24.** A network trace of a native launch with default settings and no user action shows requests only to `api.github.com` (the update check) and none when the toggle is off.

**AC-INFRA-25.** `Start-Process .\shotAI.exe -ArgumentList '--selftest' -Wait -PassThru -RedirectStandardOutput out.txt` exits 0 and `out.txt` ends with `[selftest] PASS`; the same with `$env:SHOTAI_SELFTEST = '1'` and no switch.

**AC-INFRA-26.** `--capture-selftest` on a Windows desktop prints `[capture-test] ...` lines and exits 0 on PASS, 1 on FAIL (02's assertions).

**AC-INFRA-27.** Running `--selftest` leaves `%APPDATA%\shotAI\settings.json` byte-identical (compare hashes before and after) and leaves no `shotai-selftest-*` folder or settings file in `%TEMP%`.

**AC-INFRA-28.** `--update-selftest` on a corporate network prints the endpoint, the result, and `[update-test] PASS`, with exit code 0.

**AC-INFRA-29.** The installed app contains `Fonts\Archivo.ttf` (sha256 `0e094a7d…`) and `Fonts\OFL.txt` (byte-identical to `src/renderer/fonts/OFL.txt`), plus `OFL.txt` beside any other font files; the README and third-party notices name the OFL for Archivo.

**AC-INFRA-30.** Manual: with the LFI brand, body text renders at regular weight and uppercase micro-labels render condensed (compare against the Electron build side by side).

**AC-INFRA-31.** Manual: from the installed package (the MSI, in each install scope, 12 INV-PKG-1 and 7.4.5), changing a setting modifies the real `%APPDATA%\shotAI\settings.json` (check its timestamp from a non-packaged process).

**AC-INFRA-32.** Manual: while the app runs, open `settings.json` in a program that holds it with no sharing (for example `[System.IO.File]::Open($p, 'Open', 'Read', 'None')` in PowerShell) and toggle a setting: the app shows an error notice, the switch returns, and after releasing the handle the file still holds every previous value (EDGE-INFRA-46); nothing is reset to defaults.

**AC-INFRA-33.** `BrandPaletteTests.AllIsFullyInitialized` and `HexNoHashRejectsTrailingNewline` pass, and `dotnet build` of `tools/ShotAI.GenBrand` succeeds with `BrandPalette.Generated.cs` deleted.

**AC-INFRA-34.** With a Settings `Check now` clicked while the startup check is still in flight (a slow proxy), the log shows ONE request (one `update available`, `update check could not complete` or up-to-date line from the startup path) and Settings shows the same result.

**AC-INFRA-35.** `Shell.AppPathsTests.LocalDataDirectoryIsUnderLfi` and `NoPathUnderSquirrelRoot` pass: `IAppPaths.LocalDataDirectory` is `%LOCALAPPDATA%\LFI\shotAI` and never `%LOCALAPPDATA%\shotAI` (the Squirrel root), while `SettingsFile` and `LogsDirectory` stay under `%APPDATA%\shotAI` (R-ARCH-13, ARCHITECTURE 10.2). The manual proof that removing Electron leaves this folder intact is AC-ARCH-7.

---

## 10. Interfaces with other subsystems

| Spec | This subsystem consumes | This subsystem provides |
|---|---|---|
| 01 Model and store | `JsJson.Parse`/`Stringify`, `JsNumber.ToJsString`, `JsMath.Round`, `JsString.Trim`, `AtomicFile`, `IRenameRetryClassifier`, `SerialWriteQueue`, `ProjectStore` and `ReparseSafeDelete` (self-test) | `IProjectStoreSettings` (implemented by `SettingsService`); `BrandPalette.PinnedBrand`, `PinIsUnrecognised`, `IsBrandId`, `CoerceBrand` for the manifest `theme` key; `Current.ArchiveAgeDays` for startup auto-archive |
| 02 Capture | the capture self-test body (`[capture-test]` lines, PASS or FAIL) | `ICaptureSettings.CaptureScaleNow()`, `RemoteVisibleNow()`; `ISettingsService.Changed` for `RemoteVisible`; the `--capture-selftest` switch and console |
| 03 Shell | startup order (ARCHITECTURE 4.2: logging step 1, self-test step 3, settings step 5b, update check step 13), crash handlers calling `FileLoggerProvider.Flush`, the UI-thread marshal of `UpdateAvailable` (`IUiDispatcher.Post`), `AppPaths.BrandFontPath()` | `StartupModeParser`, `SettingsService.Load`, the banner, `IUpdateService.RunStartupCheckAsync`, `ISettingsService.Current.Brand` for the brand menu, `AppVersion.Current` (the version behind 11's `IAppInfo.Current.Version`, which About reads, R-ARCH-12) |
| 04 Editor | | log category `ocr` |
| 05 Report | the open project's pin (raw, from `IProjectSession.Current.Theme`; a pin change goes through `session.Apply(SetProjectThemeOperation)`, ARCHITECTURE 7.4 and 7.5) | `PinnedBrand`, `PinIsUnrecognised`, `BrandPalette.For` (R-ARCH-14) |
| 06 Home and Settings UI | `SettingsViewModel` writes, `NoticeCenter`, Settings strings, `ThemeTokenSet`, `ThemeManager`, `Appearance` | `ISettingsService.Current`, `UpdateAsync`, `Changed`; `IUpdateService.Pending`, `UpdateAvailable`, `CheckNowAsync`; `IExternalLinks.OpenAsync`; the generated `BrandPalette` (brand, palette, narrowing, settings, logging and links are spec 10, auth is 08, R-ARCH-14); `PaletteRoles`, `ContrastMath`; `ThemePref`; font files |
| 07 SOP | `SopSettings`, `SopSettingsCoercer.Coerce`, `ToJson`, wire strings | the persisted `sop` object with change notification; log category `claude` |
| 08 Auth, secrets, policy | `ISharedHttp.Handler` (with no update-specific handler added, R-ARCH-15), `ISupportUrlAllowlist.IsAllowedAsync` | the file sink and the `claude` category; the base allowlist and launcher; the never-log rule; `IAppPaths.LocalDataDirectory` for the MSAL cache at `entra\msal-cache.bin` (R-ARCH-13) |
| 09 Exports | | `BrandPalette.Get`/`For` (LIGHT palette for documents), `CoerceBrand`, `PinnedBrand`, `IsBrandId` with string brand ids (replacing 09's `Brands.*` and `BrandId`, R-ARCH-14), `CssFontStack`, `HexNoHash`, `CardRadiusPx`, `ImageRadiusPx`, `RetiredGreys` (guard), `Current.ReportByline`, the variable `Fonts\Archivo.ttf` path via 03; `IAppPaths.LocalDataDirectory` for the WebView2 user data folder `WebView2\` (R-ARCH-13) |
| 11 Service boundary | `IUiDispatcher.Post` as the only marshal (T6), `EventRaiser` (T5), `IAppLifetime.Stopping`, `ShutdownFlush.Run` (R-ARCH-10), `UserMessage`, the DI rules (ARCHITECTURE 4.1), `RemoteVisibilityApplier` (calling the shield through `Task.Run`, DL1) | the singletons of 7.11; the `ISettingsService` and `IUpdateService` members of 11 7.3.6 (11 wins on names) |
| 12 Packaging and CI | the installer format decision (Q-INFRA-8), the install scope behind Q-INFRA-5 (12 7.4.5, 7.10.4), third-party notices, release checklist | the generator CI step, `.gitattributes` line, fonts and `OFL.txt` layout, the self-test smoke step for the Windows CI job, the "prerelease flag on every `-` version" checklist item |

---

## 11. Open questions and risks

**Q-INFRA-1. Preserve unrecognised enum strings on disk?** A value a newer build wrote (`brand`, `theme`, `sop.model`, `sop.tone`, `sop.effort`) is coerced in memory; Electron also overwrites it at the next write of ANY setting. Recommended default: IMPROVEMENT as specified in 7.4.2 step 4 (the raw string survives until the user changes that setting), which is #92 and #95's principle applied to settings.

**Q-INFRA-2. Preserve unknown keys inside `sop`?** Recommended default: yes (7.4.2 step 3); only additive keys from a newer build are affected.

**Q-INFRA-3. `projectsDir` that is empty or not fully qualified.** Recommended default: load as the default folder (`Path.IsPathFullyQualified` false), log warn `settings: projectsDir is not an absolute path, using the default`; the raw value is overwritten at the next write. Needs 01's agreement.

**Q-INFRA-4. A policy to disable the update check fleet-wide.** macOS honors an MDM key; Windows has only the user toggle, and the ADMX is fixed. Recommended default: no new policy at 2.0.0; if IT asks during the pilot, add an `UpdateCheckDisabled` value under a new `HKLM\SOFTWARE\Policies\shotAI` subkey with an ADMX revision (08 and 12), never under `Federation`.

**Q-INFRA-5. The notice under per-machine deployment.** Resolved 2026-09-23 with 12's dual-purpose MSI (12 7.4.5): the notice follows the install scope (12 7.10.4, 06 INV-HOME-45). A per-user or unpackaged build keeps the parity notice with `Open the download page`, which a standard user can now act on because the MSI installs per-user with no administrator rights; a per-machine install shows `shotAI <version> is available. On this PC, IT or an administrator installs updates.` with no download action, and `Check now` opens no release page there (12 EDGE-PKG-65). This spec's check logic does not change: it reports the same result in both scopes, and only 06's presentation differs. A user can still turn the startup check off (`updateCheckEnabled`); a fleet-wide switch stays Q-INFRA-4. Original text, kept for the record: A per-machine MSI needs admin rights, so "Open the download page" sends a standard user to an installer they cannot run. Recommended default: keep the notice (parity) for the pilot; decide with IT whether the Intune package sets `updateCheckEnabled: false` for managed devices or whether the release page explains that IT deploys updates.

**Q-INFRA-6. Prerelease ordering.** Recommended default: adopt 7.6.2 (`2.0.0-alpha.N` is older than `2.0.0`). Risk: none for stable users, who never run a prerelease.

**Q-INFRA-7. Same log file for Electron and native?** Recommended default: yes, same path and names, so support instructions and rollback keep working; both formats are identical by design.

**Q-INFRA-8. MSIX file-system virtualization.** CLOSED: 12 decided an MSI and no MSIX (INV-PKG-1, EDGE-PKG-21, citing this spec's EDGE-INFRA-39; ARCHITECTURE I-2); since 2026-09-23 the MSI is dual-purpose, per-machine through Intune and per-user by hand (12 7.4.5), and neither scope virtualizes `AppData`. AC-INFRA-31 stays as the regression gate.

**Q-INFRA-9. WPF and the variable Archivo file.** Recommended default: 06's Q-HOME-2 (upstream static instances for WPF), verified by `ArchivoRenderingTests`; the variable file stays for the PDF path. Verify on Windows whether WPF exposes any named instances of the variable font before committing to the static set. Known from the file itself (2.10): its 9 named instances are weights only at `wdth` 100, so even if WPF exposes them as faces, no condensed (62) face exists without static condensed instances.

**Q-INFRA-10. Spec 06 attributes the brand palette to "08 Brand and theme".** Resolved by R-ARCH-14: brand, palette, narrowing, settings, logging and links are spec 10, auth is 08; the brand functions are `BrandPalette.PinnedBrand`, `CoerceBrand`, `IsBrandId` and `PinIsUnrecognised` over string brand ids, for 06, 07 and 09 alike. (06's verification pass had already corrected its own text; 03 made the same correction.)

**Q-INFRA-11. `SHOTAI_LOG_LEVEL`.** Recommended default: support `debug` only, as specified; no settings key, no UI.

**Q-INFRA-12. Back up a corrupt `settings.json`?** Recommended default: yes, one `settings.json.bad` (overwritten each time), never logged; it may contain the user's name and folder paths, which is acceptable in their own profile.

**Q-INFRA-13. The Electron timeout message.** Unverified whether Node 24's abort message matches `The operation was aborted.`. Recommended default: irrelevant natively (7.6.3 maps its own timeout); note it in the Electron issue tracker if anyone touches the Electron code before cutover.

**Q-INFRA-14. Redirect handling.** Recommended default: follow redirects and accept only a final host of `api.github.com` (7.6.3), not a full redirect pin.

**Q-INFRA-15. In-flight join.** Recommended default: adopt (7.6.4).

**Q-INFRA-16. Keep the environment-variable triggers?** Recommended default: yes, both (`SHOTAI_SELFTEST=1`, `SHOTAI_CAPTURE_TEST=1`), because `docs/HARDENING-PLAN.md` and existing scripts use them; the switches are primary in new docs.

**Q-INFRA-17. Generator location.** Recommended default: `dotnet/tools/ShotAI.GenBrand` plus the checked-in table, as specified; revisit an incremental source generator only if the checked-in file becomes a merge-conflict problem. Decided in WP-A4: default adopted.

**Q-INFRA-18. Remember the pending update across launches (macOS behavior)?** Electron shows the notice only on the launch that ran the check. Recommended default: parity for 2.0.0; revisit after the pilot.

**Q-INFRA-19. Where the static Archivo instances come from.** Recommended default: the upstream Omnibus-Type/Archivo release matching the variable file's version (`head.fontRevision` 2.001, read in verification), unmodified, with `SOURCES.md` recording hashes; if no matching release exists, instance the variable file with fontTools in a documented, reproducible script and keep the family name (permitted: no Reserved Font Name), stating in `SOURCES.md` that these are Modified Versions under the OFL.

**Q-INFRA-20. A write job whose re-read of `settings.json` is not `Ok`.** Electron writes defaults plus the change (EDGE-INFRA-46). Recommended default: as specified in 7.4.3 (unreadable: retry then fail and roll back; missing or corrupt: merge onto the in-memory snapshot and the last good raw object). Alternative: always merge onto the in-memory snapshot and never re-read, which loses the "hand edit merged by the next write" parity of 2.6.4. Needs 06's agreement on the notice text for a failed settings write.

**Q-INFRA-21. Refuse user info in links?** macOS refuses `https://user@github.com/` and custom ports for the release page (`macOS:Packages/UpdateKit/Sources/UpdateKit/UpdateFeed.swift:203-209`); Electron and 11 7.3.4 allow both on an allowed host. Recommended default: parity for 2.0.0 (11 owns the algorithm); the release URL itself is already constrained by `PickRelease`'s `https://github.com/` prefix, which excludes user info and ports.

**Q-INFRA-22. Does `IExternalLinks.OpenAsync` throw?** Resolved by R-ARCH-25: 11's contract (it returns `false` when refused and throws only if the launcher throws, parity with Electron's awaited `shell.openExternal`); 06's callers wrap the call and show nothing (an update nudge is never worth an error, `App.tsx:70`). 06 INV-HOME-31 now states 11's contract and says every caller wraps the call.

**Risks.** (1) The byte layout of `settings.json` depends on 01's `JsJson` matching `JSON.stringify` exactly; its tests are the gate. (2) Three generators must agree; `BrandParityWithElectronTests` covers Windows and TypeScript, and the macOS side stays a manual stamp comparison. (3) The update check is the one feature whose behavior depends on GitHub's live API; the self-test switch is the field check. (4) A pilot user switching between Electron and native builds exercises EDGE-INFRA-40 continuously; no coordination exists by design.
