# 06 Home list, settings, onboarding tour, notices and app chrome

> Spec for the native rewrite. Sources read: `src/renderer/project/App.tsx` (869 lines), `src/renderer/project/ProjectList.tsx` (629), `src/renderer/project/date-groups.ts` (64), `src/renderer/project/OverflowMenu.tsx` (106), `src/renderer/project/Settings.tsx` (990), `src/renderer/project/Tour.tsx` (191), `src/renderer/project/theme.ts` (79), `src/renderer/project/main.tsx` (21), `src/renderer/Notice.tsx` (39), `src/renderer/notice.css` (95), `src/renderer/useConfirm.tsx` (91), `src/renderer/project/project.css` (3216; the rules at `:1-1126` (tokens, chrome, home, shared controls, menu), `:2077-2369` (settings), `:2544-2588` (overlay and confirm), `:2862-3216` (tour, tabs, bulk bar, date groups)). Tests: `src/renderer/project/date-groups.test.ts` (63), `src/renderer/project/app-chrome-tokens.test.ts` (167), `src/renderer/project/theme-wiring.test.ts` (341). Supporting reads: `src/shared/theme-palette.ts` (530), `src/shared/brand-colors.generated.ts` (184), `src/shared/theme-palette.test.ts` (516, the project.css sections `:30-130`, `:270-300`, `:490-516`), `src/shared/sop.ts` (153), `src/shared/project.ts` (`:18-22`, `:530-560`), `src/shared/ipc.ts` (624, `AppInfo`, `AuthStatus`, `ApiKeyStatus`, `TestConnectionResult`, `UpdateCheckResult`, the `updates` bridge), `src/main/settings.ts` (434), `src/main/ipc.ts` (981, `:250-420`, `:560-800`), `src/main/update-state.ts` (25), `src/main/update-check.ts` (163, `:1-80`), `src/main/main.ts` (`:515-560`), `src/main/export.ts` (`:133-145`, `:918-925`), `src/main/project-store.ts` (`:45-48`, `:384-435`), `src/renderer/project/store.ts` (213, `:100-200`), `src/renderer/project/sop-prepare.ts` (`:29-41`), `src/main/menu.ts` (`:205-230`). Commits read (git show): `42ec117` (#36/#38 auto-refresh), `037858d` (#54/#60 update notice, pull plus push), `da727b8` (tour copy, onClose twice), `f9f8b10` (#37/#39 bulk export destinations and progress), `53045c6` and `f24b3dc` (remote visibility, removal of the keep-visible toggle), `f1b24a8` and `d79bc3b` (#63 sign-in UI, three-leg test), `88b333e`, `1446d6e`, `533cc55`, `d11ee4a`, `285403d`, `b244010`, `701d4aa`, `772e381`, `e19bcc0`, `38908cd` (#77, #107 brand and theme), `62b4b7d` (#70 grow-only resize, re-dating on no-op). History before `9da70df` is squashed into that commit. macOS (`/home/user/armadillon44/shotai_macos`, read-only): `shotAI/HomeView.swift` (1057), `shotAI/SettingsView.swift` (504), `shotAI/Tour.swift` (265), `shotAI/AppModel.swift` (1469, `:40-135`, `:610-670`, `:895-1090`), `shotAI/Theme.swift` (146), `shotAI/Style.swift` (48), `shotAI/Radii.swift` (34), `shotAI/UpdateModel.swift` (222), `Packages/ShotModel/Sources/ShotModel/DateGroups.swift` (61). Context: `docs/NATIVE-WINDOWS-FEASIBILITY.md`, `dotnet/README.md`, `docs/native/spec/01-model-store.md`, `02-capture.md`, `03-windows-shell.md`, `04-editor-redaction.md`, `05-report.md` (partial at time of reading). Verification (adversarial pass): every constant, string, colour and citation re-checked against source; the spec numbering, the cross-spec interface names (08, 10, 11), the WPF font-stretch mapping, the dispatcher rule and the popup capture exclusion were corrected; missing behaviours were appended as INV-HOME-42 to 44, EDGE-HOME-48 to 58 and D-HOME-28 to 34. Status: extracted and verified. Consolidated with ARCHITECTURE.md R-ARCH-1 to R-ARCH-26 on 2026-09-23.

**Notation used in this document.**

- Several Electron strings contain U+2014 (EM DASH). This document never prints that character. Wherever it occurs in a quoted string it is written as the escape `\u2014`, and the C# literal must contain that exact character (C# accepts the same `\u2014` escape inside a string literal, so copying the quoted text into C# source is exact). Other non-ASCII characters are printed as themselves. The ones that matter: `…` U+2026, `·` U+00B7, `×` U+00D7 (monitor sizes, notice dismiss), `✕` U+2715 (search clear, tour pill), `→` U+2192, `←` U+2190, `▸` U+25B8, `▾` U+25BE, `▲` U+25B2, `▼` U+25BC, `⚙` U+2699, `⚠` U+26A0, `↻` U+21BB, `↺` U+21BA, `⤓` U+2913, `⤴` U+2934, `✓` U+2713, `●` U+25CF, `✨` U+2728, `“` U+201C, `”` U+201D, `’` U+2019, and the emoji `🗄` U+1F5C4, `🗑` U+1F5D1, `🔍` U+1F50D, `🗄️` (U+1F5C4 U+FE0F), `🗂️` (U+1F5C2 U+FE0F).
- JSX text that spans several source lines is given here already collapsed by the JSX whitespace rule (a line break plus indentation between two pieces of text becomes one space; whitespace that contains a line break next to an element is dropped; `{' '}` is one space). `<b>x</b>` inside a string is written `**x**` and means bold.
- `JsMath.Round`, `JsString.Trim`, `IsoTime.TryParseJsDate` and `IsoTime.ToIsoString` are the Core helpers defined by spec 01 (7, `ShotAI.Core.Json`). `round(x)` below means `JsMath.Round`, never C# `Math.Round`.
- A CSS px is one DIP (1/96 inch), the WPF device-independent unit. `1rem` is 16 DIP (Chromium default; the app never changes the root font size). So `0.82rem` is 13.12 DIP.
- Spec numbers (the files in `docs/native/spec/`): 01 model and store, 02 capture engine, 03 windows, shell and app menu, 04 editor and redaction, 05 report and project detail, 06 this spec, 07 Claude SOP generation (the Claude client and the SOP panel), 08 Entra sign-in, federation, secrets and managed policy (API key storage, `AuthStatus`, `IAuthService`, the three-leg connection test, the SupportUrl origin rule), 09 exports and the shareable package, 10 brand contract, settings, logging, update check and self-tests (the brand palette `BrandPalette`, `contract/brand.json` generation, `PinnedBrand`, `settings.json`, `IUpdateService`, `IExternalLinks`; spec 05 calls it "the brand spec"), 11 service boundary and threading (`IUiDispatcher`, the channel map, the banned-API list), 12 packaging, deployment and CI. Verification corrected an earlier draft that put auth under 07 and the brand under 08; this numbering is binding (ARCHITECTURE R-ARCH-14: brand, palette, narrowing, settings, logging and links are 10; auth is 08), and a reference to "08 Brand and theme" anywhere means 10. Where 11 names a member differently from this spec, 11 (and the owning spec it cites) wins, and where ARCHITECTURE.md 15.3 resolves a cross-spec conflict (`R-ARCH-n`), that resolution wins over both.
- Classifications: **REQUIRED** (parity), **IMPROVEMENT** (a deliberate native change, with justification), **ELECTRON-ONLY** (disappears natively; the replacement for its intent is named).

---

## 1. Scope

### 1.1 Owned by this subsystem

| Area | Electron location |
|---|---|
| The main window's view switching (Home, project, Settings, recording panel), its header and body chrome, and scroll memory | `src/renderer/project/App.tsx:252-305`, `:515-869` |
| The Home create hero: name field, `Capture ▸`, `Empty Project`, the mission line | `App.tsx:654-692` |
| The capture-mode picker (Screen, Auto, Window, Area), the target dropdown, area selection, readiness rules, and `buildTarget` (also used by "Resume capturing" in 05) | `App.tsx:25-35`, `:81-184`, `:694-850` |
| The new-recording flow, the empty-project flow and the Home import flow | `App.tsx:431-513` |
| The in-window recording panel (shown only if the main window is visible during a session) | `App.tsx:568-628` |
| The project list: tabs with counts, list head, search box, sort chips and direction, date grouping, rows, badges, meta line, row overflow menu, inline rename, row operations and per-row busy state, multi-select with shift-range, the bulk bar with its progress and export destinations, empty states | `src/renderer/project/ProjectList.tsx:1-629`, `src/renderer/project/date-groups.ts:1-64` |
| Home auto-refresh (triggers, interval, suppression) | `App.tsx:37-39`, `:95-98`, `:186-189`, `:301-357` |
| Settings: every tab, control, default, disabled state, validation reaction and string; which settings key each control writes | `src/renderer/project/Settings.tsx:1-990` |
| The first-run coach-mark tour: steps, spotlight, bubble placement, keyboard, once flag, replay | `src/renderer/project/Tour.tsx:1-191`, `App.tsx:73-76`, `:191-212`, `:866` |
| Notices (error, info, success), the notice stack, the update notice, and the in-window confirm and alert dialogs | `src/renderer/Notice.tsx`, `src/renderer/notice.css`, `src/renderer/useConfirm.tsx`, `App.tsx:53-72`, `:544-566` |
| The shared overflow/dropdown menu control | `src/renderer/project/OverflowMenu.tsx` |
| Applying the theme: appearance resolution (light, dark, system), following the OS live, active-brand precedence, and installing the tokens before first paint | `src/renderer/project/theme.ts`, `src/renderer/project/main.tsx`, `App.tsx:359-385` |
| The app-chrome design tokens as consumed by the UI: the static type, weight and shadow tokens, the generated colour, radius and type tokens mapped to WPF resource dictionaries, derived `color-mix` values, and the no-hardcoded-colour rule | `project.css:23-82`, `theme-palette.ts:329-441`, `app-chrome-tokens.test.ts` |
| Keyboard, focus and accessibility behavior of all of the above | throughout |

### 1.2 Not owned (consumed or delegated)

| Concern | Owner |
|---|---|
| `ProjectSummary`, `listProjects`, the search text, the search and ranking function (`ProjectSearch`), create, rename, delete, archive, unarchive, auto-unarchive on open, recents, `setProjectsDir`, the default title | 01 |
| Capture targets, `CaptureState`, `StartAsync`, pause, resume, stop, the capture-error event, the screenshot-quality and remote-visibility semantics | 02 |
| Area selection overlay, the capture pill, the app menu (File then Import Project, File then Settings, View then Brand), window sizing (`SetDetailView`), single instance, Explorer reveal, the top overlay layer | 03 |
| `ensureFlattened` (egress preparation before any export) | 04 |
| The project detail view and report; the open project's `IProjectSession` (contract owned by 01, ARCHITECTURE 7.4; created by 05 through `IProjectSessionFactory.Create` on the UI thread, R-ARCH-5); the open-project store (`projectTheme`, `projectPinUnrecognised`, `committedScale`, `sopBackup`) | 05 |
| The Claude client, the SOP panel | 07 |
| API key storage, Entra sign-in, auth status (and the policy-cache invalidation it performs), the three-leg connection test, `FederationPolicy.DefaultSupportUrl` | 08 |
| The brand palette values, `contract/brand.json`, `isBrandId`, `coerceBrand`, `pinnedBrand`, `pinIsUnrecognised` (natively `BrandPalette.IsBrandId`, `CoerceBrand`, `PinnedBrand`, `PinIsUnrecognised` with string brand ids, R-ARCH-14), the bundled Archivo face and its licence | 10 |
| Export formats, the Save dialog, export to a directory, export to the project's own folder, reveal of an export directory, package import | 09 |
| `settings.json` (location `%APPDATA%\shotAI\settings.json`, shared with the Electron build, ARCHITECTURE 10.1; schema, coercion, serialized atomic writes), logging, the startup update check and its pending stash, the registration of `IExternalLinks` (its allowlist algorithm is 11 7.3.4) | 10 |
| Dispatcher and threading rules, DI composition, cross-service events | 11 |
| Test projects in CI, installer, fonts packaging | 12 |

Specs this one touches: 01, 02, 03, 04, 05, 07, 08, 09, 10, 11, 12.

---

## 2. Reference behavior (Electron)

### 2.1 Views and navigation

One window, one React root (`App`). The visible view is derived, not stored (`App.tsx:252-264`):

```
recording  = capture.status === 'recording' || capture.status === 'paused'
openPath   = projectStore.projectPath                   (05)
showDetail = !recording && !!openPath
showHome   = !recording && !openPath
atHome     = showHome && !showSettings
```

`showHome` does NOT include `!showSettings`: while Settings is open from Home, `showHome` stays true, so every effect keyed on it (the `showHome` refresh, the window-focus refresh and the 20 s tick of 2.18) keeps running behind Settings, and returning from Settings to Home neither refreshes nor restarts the interval (EDGE-HOME-48). Only `atHome` (scroll memory, 2.19) and the render gates use `!showSettings`. REQUIRED to understand; the native rule is D-HOME-28.

What renders (`App.tsx:519-866`):

| Element | Condition |
|---|---|
| Header (logo, `shotAI`, `⚙ Settings` button) | `!showDetail` |
| `⚙ Settings` button inside the header | `showHome && !showSettings` |
| Notice stack | `error \|\| update` (any view, including recording) |
| Recording panel | `recording && capture` |
| Project detail (05) | `showDetail && !showSettings` |
| Settings | `showSettings && !recording` |
| Create hero and project list | `showHome && !showSettings` |
| Tour | `showHome && !showSettings && tourOpen` |

The body element carries class `project__body--detail` (top padding 0) when `showDetail && !showSettings` (`:542`). REQUIRED.

State machine (`showSettings` is a boolean; "project" means `openPath` set):

| State | Event | Guard | Action | Next state |
|---|---|---|---|---|
| Home | Open (row Open, create Empty Project, import) | open succeeds | 05 open | Project |
| Home | Open | open fails | nothing visible (EDGE-HOME-24) | Home |
| Home | `⚙ Settings` click, or File then Settings (`Ctrl+,`) | not recording | `showSettings = true` | Settings (from Home) |
| Home | `Capture ▸` (create and record) | ready, not busy | create, start capture, adopt project | Recording |
| Project | Back (05), close | | 05 close | Home |
| Project | SOP panel "open settings" link (05, `onOpenSettings`) or File then Settings | not recording | `showSettings = true` | Settings (from Project) |
| Project | Resume capturing or "+ Capture" (05) | | `onRecord(openPath, false, insert?)` | Recording |
| Settings (from Home) | `← Back` | | `showSettings = false` | Home |
| Settings (from Project) | `← Back` | | `showSettings = false` | Project |
| Settings (from any) | `↺ Show intro tour` | | `showSettings = false; tourOpen = true` | Home, or Project if a project is open (EDGE-HOME-20) |
| Any | capture state becomes recording or paused | | hero, list, detail and Settings unmount; recording panel shows | Recording |
| Recording | capture state becomes idle | a project is open | reload it in the detail view (`:293-299`) | Project |

`showSettings` is not reset by starting a recording; the Settings render is gated on `!recording`, so if Settings was showing when a recording started it reappears when the recording ends. REQUIRED (it cannot normally happen: recording starts only from Home or Project).

Menu events (`App.tsx:220-235`): `onOpenSettings` and `onImportProject` are registered once and read `recordingRef` so they are ignored while recording. REQUIRED (03 INV-SHELL-18 makes the shell not raise them while recording; this view also ignores them).

### 2.2 Header and body chrome

`App.tsx:519-543`, `project.css:111-166`.

| Part | Behavior | Style |
|---|---|---|
| `main.project` | column flex, full height | |
| `header.project__header` | `!showDetail` | padding `0.5rem 2rem`; bottom border 1px `--hair`; background `--surface`; flex, centered, space-between, gap 1rem |
| Logo | `assets/shotAI_icon.png`, `alt=""`, `aria-hidden="true"` | 30 x 30 DIP, radius `--radius-control-sm`, contain |
| Title | `<h1>` text `shotAI` | 0.95rem (15.2 DIP), weight 650, letter-spacing -0.01em |
| Settings button | text `⚙ Settings`, `title="Settings"`, class `btn btn--small`, `data-tour="settings"` | |
| `section.project__body` | the one scroll container for all views | flex 1; padding `1.75rem 2rem`; `overflow-y: auto`; `position: relative` (anchors the notice stack) |
| `.project__body--detail` | detail view only | `padding-top: 0` |
| `body` | | `font-family: var(--font-stack)`; `color: var(--ink)`; `background: var(--ground)` |

REQUIRED.

### 2.3 Create hero

`App.tsx:654-692`, `project.css:442-471`. Container `div.home__create` with `data-tour="hero"`, margin-bottom 1.5rem.

| Element | Content | Behavior |
|---|---|---|
| Heading `h2.home__h` | `Start a project` | `--fs-section`, `--fw-section`, letter-spacing -0.01em, margin-bottom 0.7rem |
| Mission `p.home__mission` | `Record a process, mark it up, and let Claude turn it into a step-by-step guide \u2014 a standard operating procedure \u2014 you can export and share.` | max 60ch, `--ink-2`, `--fs-body`, line-height 1.5, margin `-0.3rem 0 1rem` |
| Form `form.home__createrow` | flex row, gap 0.6rem | submit (Enter in the name field) runs `onCreate` |
| Name input | `type="text"`, placeholder `Name (optional \u2014 defaults to a timestamp)`, class `project__input` | `disabled` when `busy \|\| recording`; value `title` |
| Capture button | `type="submit"`, class `btn btn--primary`, `data-tour="capture"`, title `Start recording \u2014 every click captures a step`; text `Creating…` while `busy`, else `Capture ▸` | `disabled` when `busy \|\| recording \|\| !modeReady` |
| Empty Project button | class `btn btn--ghost`, title `Create an empty project and open it \u2014 add images, screenshots, or text without capturing`; text `Empty Project` | `disabled` when `busy \|\| recording` |

`title` state lives in `App`, so it survives navigation within the session; it is cleared after a successful create (both flows). REQUIRED.

### 2.4 Capture-mode picker

`App.tsx:25-35`, `:81-184`, `:694-850`, `project.css:473-508`, `:558-805`.

**Modes** (`MODE_OPTIONS`, render order):

| Mode | Label | Tooltip (`title`) |
|---|---|---|
| `screen` | `Screen` | `Capture one full monitor each step` |
| `auto` | `Auto` | `Best-effort smart capture \u2014 may include extra/unintended context` |
| `window` | `Window` | `Capture one specific window each step` |
| `area` | `Area` | `Drag-select a fixed region to capture` |

Default `mode = 'screen'` (`:82`). The row is `div.home__mode` with `role="radiogroup"`, `aria-label="Capture mode"`, `data-tour="mode"`; first a label `span.home__mode-label` text `Mode` (uppercase via CSS, `--fs-label`, letter-spacing 0.08em, `--ink-3`, `font-stretch: var(--label-stretch)`); then one `button.capmode__chip` per mode with `role="radio"`, `aria-checked`, the tooltip, class `capmode__chip--on` when selected. When `mode === 'auto'` a `span.home__mode-warn` follows: text `⚠ Auto is best-effort`, title `Auto guesses per click and may capture extra or unintended context. Pick Screen, Window, or Area for predictable results.`, colour `--caut-fg`, `--fs-meta`, cursor help. There is no arrow-key navigation between the chips; each is a Tab stop. REQUIRED (strings); IMPROVEMENT for keyboard (7.9).

Below the row, always: `p.home__mode-hint`: `What shotAI grabs for each step: a full monitor (**Screen**), one **Window**, a fixed **Area** you drag out, or **Auto**-detect per click.` (`--fs-meta`, `--ink-3`, bold parts `--ink-2` weight 600, max 62ch).

**State** (`:82-90`): `mode`, `targets: {windows, monitors} | null` (null until first load), `targetsLoading`, `pickedWindow: WindowInfo | null`, `pickedMonitorId: number | null`, `pickedArea: Rect | null`, `selectingArea`, `pickerOpen`. All of it lives in `App`, not in the unmounted hero, so the chosen mode, the loaded targets and the picks survive a trip into a project or Settings and back; they reset only on relaunch (EDGE-HOME-57). `pickerOpen` also survives: a dropdown left open when the user navigates away is open again on return. REQUIRED.

**Loading targets** (`loadTargets`, `:100-121`): `targetsLoading = true`; `t = capture.listTargets()` (02); `targets = t`; keep `pickedWindow` if `t.windows` contains one with the same `id`, else `t.windows[0] ?? null`; keep `pickedMonitorId` if some monitor has that id, else the id of the first `isPrimary` monitor, else of `t.monitors[0]`, else null; on error show the error notice; finally `targetsLoading = false`. Note the kept `pickedWindow` is the OLD object (its title is not refreshed). REQUIRED.

Load triggers: on mount and whenever `mode === 'screen' && !targets` (`:216-218`, so the primary monitor is preselected for the default mode); `selectMode(m)` for `window` or `screen` when `!targets` (`:123-127`); the `↻ Refresh` button in the dropdown. Targets are never reloaded automatically after the first success. REQUIRED.

**selectMode(m)** (`:123-127`): `mode = m`; `pickerOpen = false`; lazy-load as above.

**Readiness** (`:183-184`): `modeReady = mode === 'window' ? pickedWindow != null : mode === 'area' ? pickedArea != null : true`. Screen is ready even with no monitor (the target then omits `monitorId`, 02 picks). REQUIRED.

**Target dropdown** (shown for `window` and `screen`, `:728-816`): `div.home__dd` (inline-block, min-width 280, max-width 460, margin-top 0.6rem).

- Trigger `button.home__dd-trigger`, `aria-haspopup="listbox"`, `aria-expanded={pickerOpen}`, toggles `pickerOpen`. Content: current label (ellipsis) and caret `▾` (`aria-hidden`, `--ink-3`). Style (`project.css:568-599`): full width of `.home__dd`, padding `0.45rem 0.7rem`, 1px `--control-bd`, radius `--radius-control`, `--surface`, `--ink`, `--fs-body`, left-aligned; hover border `--accent`.
- Label (`pickerLabel`, `:130-144`):
  - window mode: picked window ? `` `${app ? app + ' \u2014 ' : ''}${title || '(untitled)'}` `` : `targetsLoading ? 'Loading…' : 'Select a window…'`
  - screen mode: picked monitor found in `targets` ? `` `${name} · ${width}×${height}${isPrimary ? ' · primary' : ''}` `` : `targetsLoading ? 'Loading…' : 'Select a monitor…'`
- Open state: a full-window transparent backdrop `div.menu__backdrop` (fixed, inset 0, z 50) whose click closes; the popover `div.home__dd-pop` (absolute below the trigger, `top: calc(100% + 4px)`, full trigger width, z 51, border `--hair`, radius `--radius-panel`, background `--surface`, `--menu-shadow`), `role="listbox"`, `aria-label` `Window to capture` or `Monitor to capture`.
- Popover head `div.home__dd-head` (padding `0.4rem 0.7rem`, bottom 1px `--hair-2`, `--surface-2`, `--fs-meta`, weight 600, `--ink-2`, `project.css:614-625`): text `Windows` or `Monitors`, and a button `↻ Refresh` (`btn btn--small btn--ghost`, title `Refresh the list`, disabled while `targetsLoading`) that calls `loadTargets` and does not close the popover.
- List `div.home__dd-list` (max-height 220 DIP, scrolls). Window items: `button.home__picker-item`, `role="option"`, `aria-selected` when `pickedWindow.id === w.id`, class `--on` when selected; content: `span.home__picker-app` with `w.app` (only if non-empty), then `span.home__picker-name` with `w.title || '(untitled)'`. Monitor items: name, then `span.home__picker-app` with `` `${width}×${height}` `` plus `' · primary'` for the primary. Click selects and closes.
- Empty list: `p.home__picker-empty`: `targetsLoading ? 'Loading…' : 'No windows found'` (or `'No monitors found'`).
- There is no Escape handling and no arrow-key handling for this popover (EDGE-HOME-31).

REQUIRED (behavior and strings).

**Area** (`:817-838`, `:170-180`): `div.capmode__picker` with a `btn` whose text is `Selecting…` while selecting, else `Re-select area` when an area exists, else `Select area…`; disabled while selecting. Click: `selectingArea = true`; `r = region.selectArea()` (03 overlay); if `r` non-null, `pickedArea = r` (a cancel keeps the previous area); errors to the notice; finally `selectingArea = false`. When an area exists, `span.capmode__area` shows `` `${w} × ${h}px @ (${x}, ${y})` `` (`--accent-ink`, tabular numerals). REQUIRED.

**Warnings** (`:839-850`, `p.capmode__warn`, `--caut-fg`, 0.82rem):
- `mode === 'window' && !pickedWindow`: `Pick a window above to start recording \u2014 that's why Capture ▸ is greyed out.`
- `mode === 'area' && !pickedArea && !selectingArea`: `Select an area above to start recording \u2014 that's why Capture ▸ is greyed out.`

REQUIRED.

**buildTarget()** (`:146-168`), the target for the NEXT recording (Home create and 05's Resume capturing):

| mode | Result |
|---|---|
| `window` | picked ? `{ mode: 'window', window: { id, pid, title } }` : `{ mode: 'auto' }` |
| `screen` | `pickedMonitorId != null` ? `{ mode: 'screen', monitorId }` : `{ mode: 'screen' }` |
| `area` | picked ? `{ mode: 'area', area }` : `{ mode: 'auto' }` |
| `auto` | `{ mode: 'auto' }` |

REQUIRED. (The `auto` fallbacks are unreachable from Home because of `modeReady`, but reachable from 05's Resume capturing when Window mode has no window, EDGE-HOME-7.)

### 2.5 New-recording, empty-project and import flows

**onRecord(projectPath, createdThisSession = false, insert?)** (`:433-459`):

1. `{projectId, manifest} = projects.open(projectPath)` (01; auto-unarchives).
2. `steps = manifest.steps` (seeds the recording panel's list).
3. `state = capture.start(projectPath, insert ? insert.target : buildTarget(), { createdThisSession, insertAt: insert?.atIndex })` (02).
4. `capture = state` (the view switches to Recording without flashing the detail view).
5. `adoptOpened(projectId, projectPath, manifest)` (05; marks the project open so the report shows when capture stops).
6. Any throw: error notice; capture never starts. (If step 3 throws, the project stays open in the store but nothing else happens.)

**onCreate** (form submit, `:462-476`): `preventDefault`; return if `busy || !modeReady` (an empty title is allowed); `busy = true`; `summary = projects.create(JsString.Trim(title))` (01 gives the default `Project YYYY/MM/DD HH:mm:ss` name for an empty string); `title = ''`; `await refresh()`; `await onRecord(summary.path, true)`; errors to the notice; finally `busy = false`. `createdThisSession = true` is what lets Discard from the pill delete the whole new project (02). REQUIRED.

**onCreateEmpty** (`:500-513`): guard `busy`; `busy = true`; create with the trimmed title; `title = ''`; `refresh()`; 05 `open(summary.path)`; errors to the notice; finally `busy = false`. It does NOT check `modeReady`. REQUIRED.

**onImportPackage** (`:480-496`), from the `⤓ Import project` button and from File then Import Project (`Ctrl+O`, 03): guard `busy`; `busy = true`; clear the error; `summary = projects.importPackage()` (09: open dialog titled `Import a shotAI project package`, filter `shotAI package` `*.zip`, validate, materialize); null (cancel) returns quietly; else `refresh()` then 05 `open(summary.path)`; errors to the notice; finally `busy = false`. The menu listener calls the latest handler through a ref (`importPackageRef`, `:77-79`, `:495-496`). REQUIRED.

`busy` is shared by the three flows: while any runs, the name field, Capture and Empty Project are disabled and the other flows return immediately. It does not disable the list. REQUIRED.

### 2.6 Recording panel

`App.tsx:568-628`, `project.css:806-897`. Rendered only while `recording && capture`. In practice the main window is hidden during a recording (02, 03), so this panel is seen only when the window is shown again during a session (for example a second launch, 03 EDGE-SHELL-31).

| Part | Content |
|---|---|
| Container | `div.rec rec--recording` or `rec--paused`; border `--danger-bd` and background `--danger-tint`; paused: `--caut-bd` and `--caut-bg`; radius `--radius-card`; padding `1rem 1.25rem` |
| Dot | 12 DIP circle, `--danger`, pulsing (opacity 1 to 0.35 at 50%, 1.4s ease-in-out infinite); paused: `--draft`, no animation |
| Label | `` `${status === 'paused' ? 'Paused' : 'Capturing'} · ${projectTitle}` `` weight 650 |
| Count | `` `${stepCount} steps` `` (no singular form: `1 steps`, EDGE-HOME-30), right-aligned, `--ink-2`, 0.85rem, tabular |
| Buttons | recording: `Pause` (`btn`) calls `capture.pause()`; paused: `Resume` (`btn`) calls `capture.resume()`; always `Stop` (`btn btn--primary`) calls `capture.stop()`; each result replaces `capture`; errors to the notice |
| Hint | `Click anywhere (or press Ctrl+Shift+S) to capture a step. Clicks on shotAI's own windows are ignored.` |
| Steps | when `steps.length > 0`: `ol.rec__steps` (max-height 14rem, scrolls); each `li`: order number (`s.order`, right-aligned, `--ink-3`), caption, and the window title when `s.window` is set (`--ink-3`, 0.78rem, max 16rem, ellipsis) |

`steps` is seeded by `onRecord` and appended by the `onStepAdded` event (`:239-241`); `capture.onError` sets the error notice to `` `Capture error: ${message}` `` (`:242-244`); `onStateChanged` replaces `capture` (`:238`). REQUIRED.

### 2.7 Project list: tabs and counts

`ProjectList.tsx:59-72`, `:425-444`, `project.css:3081-3129`.

- Container `div.home__tabs`, `role="tablist"`, `aria-label="Project sections"`; bottom border 1px `--hair`; gap 1.25rem.
- Two `button.home__tab` with `role="tab"`, `aria-selected`: `Projects` followed by `span.home__tabcount` with `activeCount`, and `Archive` followed by `archiveCount`.
- `activeCount = projects.filter(p => !p.archived).length`, `archiveCount = projects.filter(p => p.archived).length`, over the FULL list (never narrowed by search). REQUIRED.
- Selected tab: text `--ink`, a 2.5 DIP `--accent` underline at the bottom (radius 3px, the only literal radius here, allowed as a hairline indicator); count chip `--accent-ink` on `--accent-tint`. Unselected: `--ink-3`, chip `--ink-3` on `--hair-2`. Tab font `--fs-title` at `--fw-section`. Count chip 0.7rem weight 700, radius `--radius-chip`, padding `0.05rem 0.45rem`, margin-left 0.4rem.
- `switchTab(t)`: no-op when `t === tab`; else `tab = t` and clear the selection (and its shift anchor). REQUIRED.
- Default tab `active`. No arrow-key navigation between the two tabs (each is a Tab stop). REQUIRED for behavior; IMPROVEMENT for keyboard (7.9).

### 2.8 List head: title, import, search, sort

`ProjectList.tsx:446-517`, `project.css:233-273`, `:509-531`, `:900-934`. `div.home__listhead` wraps (flex, baseline, space-between, gap 1rem).

| Part | Content and behavior |
|---|---|
| Heading | `h2.home__h`: `Projects` or `Archive`, then `span.home__count` with `` `· ${sorted.length}` `` (the count AFTER search filtering; `--ink-3`, weight 400, `--fs-meta`) |
| Import | only on the `active` tab: `button` `btn btn--small btn--ghost`, text `⤓ Import project`, title `Import a project package (.zip) someone shared with you`, runs 2.5 `onImportPackage` (not disabled by `busy`, but the handler returns while busy) |
| Search box | `div.project__search` (flex 1 1 200px, max-width 340): `input.project__input` `type="search"`, placeholder `Search projects…`, `aria-label="Search projects by title or content"`; the native WebKit cancel affordance is hidden; a clear button appears when `query` is non-empty |
| Search input change | `query = value`; if the selection is non-empty, clear it (so the bulk count cannot go stale against rows that filtered out) |
| Search Escape | when `query` is non-empty: stop propagation (so the global selection-clear does not also run) and set `query = ''`; with an empty query Escape propagates |
| Clear button | `button.project__search-clear`, text `✕`, `aria-label` and `title` `Clear search`; sets `query = ''`; does not clear the selection and does not refocus the input |
| Sort group | `div.project__sort`, `role="group"`, `aria-label="Sort projects"`: label `Sort:`, then chips `Name`, `Created`, `Modified` (`button.project__sort-chip`, class `--on` for the active key: `--accent` background, `--on-accent` text), then a direction button `btn btn--small` showing `▲` when ascending, `▼` when descending, `title` `Ascending` or `Descending` |

Defaults: `sortKey = 'modified'`, `sortAsc = false` (`:47-48`). Clicking a sort chip sets the key and keeps the direction. The chips expose no pressed state to assistive tech (EDGE-HOME-32). REQUIRED.

All list state (`tab`, `sortKey`, `sortAsc`, `query`, `selected`, `lastClicked`, `renamingPath`, `rowBusyPath`, `bulkBusy`, `bulkProgress`) lives in `ProjectList`, which is unmounted whenever Home is not showing (`App.tsx:854`). So returning from a project or from Settings resets the tab to Projects, the sort to Modified descending, clears the search, the selection and any rename. REQUIRED (EDGE-HOME-3).

### 2.9 Search and ranking

`ProjectList.tsx:140-202`. Spec 01 (2.9.6) owns the pure function; restated here because the UI consumes every part:

```
filtered     = projects.filter(p => tab === 'archive' ? p.archived : !p.archived)
q            = query.trim().toLowerCase()            // JsString.Trim, then the 01 lowercase rule
searching    = q.length > 0
titleHit(p)  = p.title.toLowerCase().includes(q)
searched     = searching ? filtered.filter(p => titleHit(p) || p.searchText.includes(q)) : filtered
sorted       = stable sort of searched (2.10)
groups:
  searching          -> [ {label: '', items: sorted.filter(titleHit)}                  if non-empty,
                          {label: titleHits non-empty ? 'Matches in content' : '',
                           items: sorted.filter(p => !titleHit(p))}                     if non-empty ]
  sortKey === 'name' -> [ {label: '', items: sorted} ]
  otherwise          -> date groups (2.11), bucket order reversed when ascending
visibleOrder = groups.flatMap(g => g.items.map(p => p.path))
```

`searchText` is already lowercase and excludes the title (01). A label of `''` renders no header row. REQUIRED.

### 2.10 Sorting

`ProjectList.tsx:160-170`. JavaScript `Array.prototype.sort` is stable.

| Key | Comparator (ascending) |
|---|---|
| `name` | `a.title.localeCompare(b.title, undefined, { sensitivity: 'base' })` (case- and accent-insensitive, default locale) |
| `created` | `a.createdAt.localeCompare(b.createdAt)` (string comparison of the ISO strings) |
| `modified` | `a.updatedAt.localeCompare(b.updatedAt)` |

Descending negates the comparator (`-cmp`), so ties keep the input order (the order `listProjects` returned) in BOTH directions. REQUIRED.

### 2.11 Date grouping

`date-groups.ts:1-64`, used at `ProjectList.tsx:176-195`. Only when `sortKey` is `created` or `modified` and not searching. Timestamp: `Date.parse(sortKey === 'created' ? p.createdAt : p.updatedAt)`; `now = new Date()` at render time.

Buckets, in canonical newest-to-oldest order: `This Week`, `Last Week`, `This Month`, `Last Month`, `Older`.

Boundaries (all in the LOCAL time zone, by calendar arithmetic, never fixed millisecond offsets):

```
startOfWeek(d)  = local midnight of d's calendar date, minus d.getDay() days   // weeks start Sunday
thisWeek        = startOfWeek(now)
lastWeek        = local midnight of (thisWeek's date - 7 days)
thisMonth       = local midnight of day 1 of now's month
lastMonth       = local midnight of day 1 of the month before now's month (year rolls back in January)

bucketFor(ts):
  if ts is not finite          -> 'Older'
  if ts >= thisWeek            -> 'This Week'       (includes future timestamps)
  if ts >= lastWeek            -> 'Last Week'
  if ts >= thisMonth           -> 'This Month'
  if ts >= lastMonth           -> 'Last Month'
  else                         -> 'Older'
```

The checks are ordered, so the week buckets win over the month buckets: a date in the current week reads `This Week` even if it is also this month, and `Last Week` can contain days of the previous month; `This Month` can be empty (for example when the month started within the last two weeks) and then `Last Month` starts right after `Last Week`. JavaScript `new Date(y, m, d)` normalizes out-of-range days and months and resolves a local midnight that does not exist (DST gap) forward to the first valid instant, and an ambiguous one to the earlier instant.

`groupByDate(items, getTs, now)` keeps each item's input order within its bucket and emits only non-empty buckets, in canonical order. The caller reverses the bucket list when ascending (`Older` first). Group headers are `li.project__group` with `aria-hidden="true"`, rendered uppercase (`text-transform`), `--fs-label`, weight 700, letter-spacing 0.05em, `--ink-3`, `font-stretch: var(--label-stretch)`, padding `0.9rem 0.15rem 0.35rem`, followed by a 1px `--hair` rule filling the rest of the line. REQUIRED.

Worked reference (`date-groups.test.ts:4-9`): now = Wed 2026-07-22 10:00 local. This Week is on or after Sun 2026-07-19 00:00; Last Week Sun 07-12 to Sat 07-18; This Month 07-01 to 07-11; Last Month 06-01 to 06-30; Older before 06-01.

### 2.12 Rows

`ProjectList.tsx:323-417`, `project.css:317-435`.

`ul.project__list` (column, gap 0.5rem). Each project is `li.project__item` (grid `auto 1fr auto`, gap 0.9rem, padding `0.75rem 0.9rem`, border 1px `--hair`, radius `--radius-panel`, background `--surface`, `--shadow-sm`; hover: border `color-mix(in srgb, var(--accent) 40%, var(--hair))` and `translateY(-1px)`, 0.16s ease; selected (`--sel`): border `--accent`, background `color-mix(in srgb, var(--accent-tint) 70%, var(--surface))`).

| Column | Content |
|---|---|
| Checkbox | `input.project__check` `type="checkbox"`, `aria-label` `` `Select ${p.title}` ``, checked when selected, accent colour `--accent`, 1.05rem square. Plain click toggles (2.15); shift-click prevents the default toggle and range-selects |
| Title | when not renaming: `span.project__item-name` (ellipsis) then the badge; when renaming: the rename input (2.13) |
| Badge | `span.project__badge`: `SOP ready` (class `--ok`: `--ok-ink` on `--ok-tint`, title `Claude has written this guide`) when `p.hasSop`, else `Draft` (class `--draft`: `--draft-ink` on `--draft-tint`, title `No SOP generated yet`); 0.66rem, weight 700, radius `--radius-chip`, preceded by a 0.34rem dot in the current colour |
| Meta | `span.project__item-meta` (`--ink-2`, `--fs-meta`): `Working…` while this row is busy, else `` `${n} step${n === 1 ? '' : 's'} · ${tab === 'archive' ? 'archived' : 'modified'} ${p.updatedAt ? new Date(p.updatedAt).toLocaleDateString() : '\u2014'}` `` |
| Open | `button` `btn btn--small btn--primary`, text `Open`, title `Open (restores the archived project)` when `p.archived`, else `Open`; `disabled` when `anyBusy`; calls 05 `open(p.path)` |
| Overflow | `OverflowMenu` (2.20) with default trigger `⋯`, title `More actions`, `disabled` when `anyBusy` |

`hasSop` is `intro !== null || some step.aiInserted === true` (01). The meta date uses `updatedAt` on both tabs, labelled `archived` on the Archive tab (EDGE-HOME-16). `toLocaleDateString()` uses the renderer's locale (the OS UI language) and the numeric short date (en-US `9/22/2026`); an unparseable non-empty string renders `Invalid Date` (EDGE-HOME-17). `anyBusy = rowBusyPath !== null || bulkBusy` (`:321`). REQUIRED.

Row overflow items, in order (`:397-413`):

| # | Label | Action |
|---|---|---|
| 1 | `Rename` | start rename (2.13) |
| 2 | `Reveal in Explorer` | `projects.reveal(p.path)` (03 reveal, 01 gate), error to the notice |
| 3 | `Restore` if `p.archived`, else `Archive` | `doArchive(p, !p.archived)` |
| sep | | |
| 4-9 | `Export → HTML`, `Export → Word`, `Export → PowerPoint`, `Export → HTML (for Word)`, `Export → PDF`, `Export → Markdown` (formats `html`, `docx`, `pptx`, `html-plain`, `pdf`, `markdown`) | `doExport(p, format)`; each `disabled: anyBusy` |
| sep | | |
| 10 | `Delete` (danger) | `doDelete(p)` |

REQUIRED.

### 2.13 Inline rename

`ProjectList.tsx:74-91`, `:356-367`, `project.css:936-943`. Only one row renames at a time (`renamingPath`).

| State | Event | Guard | Action | Next |
|---|---|---|---|---|
| Idle | `Rename` menu item | | `renamingPath = p.path`; `renameValue = p.title` | Editing |
| Editing | typing | | `renameValue = value` | Editing |
| Editing | Enter | | `commitRename()` | Idle |
| Editing | Escape | | `renamingPath = null` (discard) | Idle |
| Editing | input loses focus | | `commitRename()` | Idle |
| Editing | `Rename` on another row | | switches `renamingPath` (the first input blurs first, which commits it) | Editing |
| Editing | Home unmounts (navigation) | | state discarded | Idle |

`commitRename()`: `path = renamingPath`; `renamingPath = null`; if `path` null return; `next = JsString.Trim(renameValue)`; `orig` = the title of that path in the current `projects`; if `next === ''` or `next === orig` return (no write, so no re-dating); else `projects.rename(path, next)` (01; bumps `updatedAt`) then `refresh()`; errors to the notice. The input (`project__rename-input`, weight 600, 1px `--accent` border, radius `--radius-control-sm`, full width) is `autoFocus` and replaces the title and badge. The auto-refresh tick is suppressed while it has focus (it is an `INPUT`, 2.18). REQUIRED. The at-most-once guarantee is not explicit in Electron (EDGE-HOME-10).

### 2.14 Row operations and per-row busy

`ProjectList.tsx:92-138`. `rowBusyPath` holds the one row being worked on.

| Operation | Guard | Steps |
|---|---|---|
| `doDelete(p)` | `rowBusyPath` null | confirm `` `Delete "${p.title}"? This removes the project folder and its screenshots.` `` with confirm label `Delete`, danger (straight ASCII double quotes around the title); cancel returns; `rowBusyPath = p.path`; `projects.delete(p.path)`; `refresh()`; errors to the notice; finally `rowBusyPath = null` |
| `doExport(p, format)` | `rowBusyPath` null | `rowBusyPath = p.path`; `{projectId, manifest} = projects.open(p.path)` (this AUTO-UNARCHIVES an archived project, EDGE-HOME-14); `ensureFlattened(projectId, p.path, manifest.steps)` (04); `projects.export(p.path, format)` (09: Save dialog defaulting to the project's `export/` folder; cancel writes nothing and returns `canceled`; on success the written file is revealed in Explorer, because `exportProject` defaults `reveal` to true, `src/main/export.ts:814`, `:863`, `:908`); `refresh()`; errors to the notice; finally clear |
| `doArchive(p, archive)` | `rowBusyPath` null | busy; `archive ? projects.archive : projects.unarchive`; `refresh()`; errors to the notice; finally clear |
| Reveal | none | fire and forget; error to the notice |
| Open | trigger disabled while `anyBusy` | 05 open |

While a row is busy its meta reads `Working…` and every row's Open and overflow trigger, and every bulk action button except `Clear`, is disabled. Export progress events (09) are not shown on Home. Rename is NOT a busy operation: it never sets `rowBusyPath`, never shows `Working…`, and its store call can overlap a running row or bulk operation (it is reachable only through the overflow trigger, which is disabled while `anyBusy`, but a rename input that was already open keeps working). The row menu's `Rename`, `Reveal in Explorer` and `Archive`/`Restore` items carry no `disabled` flag of their own; only the six export items do (moot while the trigger is disabled). REQUIRED.

### 2.15 Multi-select and shift-range

`ProjectList.tsx:51-52`, `:62-65`, `:204-245`, `:342-354`.

State: `selected: Set<path>`, `lastClicked: path | null` (the range anchor).

| Event | Action |
|---|---|
| Plain checkbox click (`onChange`) | toggle `path` in `selected`; `lastClicked = path` |
| Shift+click on a checkbox (`onClick` with `shiftKey`) | `preventDefault` (the checkbox does not toggle); `from = indexOf(visibleOrder, lastClicked)` (or -1), `to = indexOf(visibleOrder, path)`; if either is -1, plain toggle; else ADD every path in `visibleOrder[min..max]` inclusive (never removes); `lastClicked = path` |
| `Select all` (bulk bar) when not all selected | `selected = new Set(visibleOrder)` (anchor unchanged) |
| `Clear all` (same button when all selected) | clear selection and anchor |
| `Clear` button (bulk bar) | clear selection and anchor |
| Escape anywhere (window listener, active only while `selected.size > 0`) | clear selection and anchor |
| Tab switch | clear |
| Search input change with a non-empty selection | clear |
| Bulk operation completes | clear (only after `onChanged` resolved, 2.16) |

`allSelected = sorted.length > 0 && every p in sorted is in selected` (`:229`). `selectedProjects() = sorted.filter(p => selected.has(p.path))` (visible, in sort order, not tier or bucket order). An auto-refresh does NOT prune `selected`: a path that disappeared stays in the set and is counted by `N selected` and by the bulk-delete confirmation, but no operation touches it (EDGE-HOME-8). REQUIRED.

### 2.16 Bulk bar

`ProjectList.tsx:247-319`, `:519-581`, `project.css:3131-3194`. Shown when `selected.size > 0`: `div.project__bulk`, `role="region"`, `aria-label="Bulk actions"`; background `--accent-tint`, border `color-mix(in srgb, var(--accent) 30%, transparent)`, radius `--radius-control`, padding `0.55rem 0.7rem`, wraps.

| Part | Content |
|---|---|
| Select-all toggle | `button.project__bulk-all`, `aria-pressed={allSelected}`: a 1rem box (radius `--radius-micro`, 1.5px `--accent` border; filled `--accent` with a `✓` in `--on-accent` when all selected) then `Clear all` or `Select all` |
| Count | `span.project__bulk-count`, `role="status"`, `aria-live="polite"`: during an operation `` `${verb} ${done} of ${total}…` `` else `` `${selected.size} selected` `` |
| Spacer | pushes the actions right |
| Archive or Restore | `btn btn--small`: `⤴ Restore` on the Archive tab, `🗄 Archive` otherwise; disabled `anyBusy`; `bulkArchive` |
| Export | an `OverflowMenu` with trigger class `btn btn--small`, label `⤓ Export ▾`, title `Export the selected projects`, disabled `anyBusy`; items: header `Each project's own folder`; `HTML`, `Word`, `PowerPoint`, `PDF`, `Markdown` (formats `html`, `docx`, `pptx`, `pdf`, `markdown`; no `html-plain`); separator; header `One shared folder…`; the same five |
| Delete | `btn btn--small btn--danger`, `🗑 Delete`, disabled `anyBusy`; `bulkDelete` |
| Clear | `btn btn--small btn--ghost`, `Clear`, never disabled |

**runBulk(verb, fn)** (`:255-278`):

```
targets = selectedProjects()                      // snapshot
if targets is empty: return
bulkBusy = true; bulkProgress = {verb, done: 0, total: targets.length}
try:
  for i in 0..targets.length-1:
    try: await fn(targets[i])
    catch e: onError(e)                           // replaces the notice; the loop continues
    bulkProgress = {verb, done: i + 1, total}
  await onChanged()                               // one refresh
  clearSelection()
finally:
  bulkBusy = false; bulkProgress = null
```

Sequential, one project at a time. Only the LAST failure stays visible (each `onError` replaces the one error notice). If the user presses `Clear` mid-run the operation continues but the bar (and its progress) disappears (EDGE-HOME-12). The final `onChanged()` is App's `refresh`, which throws on a listing failure; that throw skips `clearSelection()` (the selection stays), escapes `runBulk` as an unhandled rejection (the callers use `void`), shows NO notice, and in `bulkExportToFolder` also skips the final reveal (EDGE-HOME-50). If no selected project is visible, `runBulk` returns at once, but `bulkExportToFolder` has already shown the folder dialog and still reveals the folder, and `bulkDelete` has already shown `Delete N projects?` (EDGE-HOME-51). REQUIRED.

| Bulk action | Verb | Per project | Before and after |
|---|---|---|---|
| `bulkDelete` | `Deleting` | `projects.delete` | first confirm `` `Delete ${n} project${n === 1 ? '' : 's'}? This removes each project folder and its screenshots.` `` with confirm label `` `Delete ${n}` ``, danger, `n = selected.size` |
| `bulkArchive` | `Restoring` on the Archive tab, else `Archiving` | `unarchive` or `archive` | none |
| `bulkExportEach(format)` (#37) | `Exporting` | `open` (auto-unarchives), `ensureFlattened`, `exportToOwnFolder(path, format)` (09: into the project's own `export/`, no dialog, no reveal) | none |
| `bulkExportToFolder(format)` (#37) | `Exporting` | same prep, then `exportToDir(path, format, destDir)` (09: collision-safe names, no per-file reveal) | first `destDir = chooseExportDir()` (09 folder dialog titled `Choose a folder for the exports`, may create folders); null aborts before any work; after the run `revealExportDir(destDir)` once, errors swallowed, even if every export failed |

REQUIRED.

### 2.17 Empty states

`ProjectList.tsx:583-612`, `project.css:532-556`. `div.empty` (centered, padding `2.5rem 1rem`, `--ink-2`), icon (`aria-hidden`, 2rem, opacity 0.75), line (`--fs-title`, `--fw-title`, `--ink`), sub (`--fs-meta`, max 44ch).

| Condition | Icon | Line | Sub |
|---|---|---|---|
| `sorted` empty and searching | `🔍` | `No projects match “${query.trim()}”` | `Search looks at the project title and the text inside it (step captions, notes, and the SOP overview).` with `, in the Archive tab` inserted before the final period on the Archive tab |
| `sorted` empty, Active tab | `🗂️` | `No projects yet` | `Create a project above: press **Capture ▸** to record a process, or **Empty Project** to build one from images and text.` |
| `sorted` empty, Archive tab | `🗄️` | `No archived projects` | `Projects you haven’t touched in a while land here (or archive them yourself). Opening one restores it automatically.` |

REQUIRED.

### 2.18 Auto-refresh

`refresh()` (`App.tsx:95-98`) = clear the error notice, then `projects = await projects.list()` (01, unsorted). There is no in-flight guard: overlapping refreshes resolve in any order and the last to resolve wins. Triggers:

| # | Trigger | Condition | Citation |
|---|---|---|---|
| 1 | App mount | always (together with `capture.getState()`) | `App.tsx:186-189` |
| 2 | `showHome` becomes true (also on mount; with rows 1 and 6 the mount runs three refreshes, six in a development StrictMode build) | `showHome` | `:303-305` |
| 3 | Window `focus` event (window activation) | `showHome` | `:312-327` |
| 4 | Interval every `HOME_REFRESH_MS = 20_000` ms | `showHome`, and `document.activeElement` is not an `INPUT`, `TEXTAREA` or contentEditable element | `:37-39`, `:315-322` |
| 5 | `projects.onChanged` push (startup auto-archive moved projects, 10) | always | `:352-357` |
| 6 | `sopBackup` of the open project changes (07 generate or revert renamed the project); the effect also runs once on mount | always, including inside a project | `:329-338` |
| 7 | After every Home mutation (`onChanged`), create, import | | 2.5, 2.13-2.16 |
| 8 | Settings changed the projects folder | | `Settings.tsx:288-298`, `App.tsx:647` |

The interval is created when `showHome` becomes true and cleared when it becomes false, so it restarts from zero on every return from a project or a recording; because `showHome` ignores Settings, it keeps running while Settings is open from Home and does not restart when Settings closes (EDGE-HOME-48). Every trigger except row 8 ends in `.catch(fail)`, so a failed listing, background or not, replaces the error notice with the raw message; row 8 (`onProjectsDirChanged={() => void refresh()}`, `App.tsx:647`) has no catch and a failure there is an unhandled rejection with no notice (EDGE-HOME-49). Suppression (#36): the tick is skipped while a text field has focus so the list "never reorders out from under the cursor". Because a checkbox is also an `INPUT`, the tick is also skipped while the focus sits on a row checkbox (typically right after selecting); this is incidental (EDGE-HOME-9). The focus trigger is not suppressed. Because `refresh()` clears the error notice first, any trigger (including the tick and window activation) dismisses a visible error (EDGE-HOME-21). REQUIRED (triggers and interval), with the changes in 7.6.

### 2.19 Scroll behavior

`App.tsx:266-289`. `.project__body` is one scroll container shared by every view.

- While `atHome`, every scroll event stores `homeScroll = scrollTop` (stored as it scrolls, because by the time a view-change effect runs the new, shorter content has already clamped `scrollTop`, usually to 0).
- When `atHome` changes: entering Home sets `scrollTop = homeScroll`; leaving Home (to a project or to Settings) sets `scrollTop = 0`.
- Changes between two non-Home views (Project to Settings and back) do nothing, so Settings opened from a scrolled project starts at the project's offset, clamped (EDGE-HOME-19).

REQUIRED for Home-restore and project-at-top; IMPROVEMENT for Settings (7.7).

### 2.20 Overflow menu control

`OverflowMenu.tsx:1-106`, `project.css:976-1081`. One component for every dropdown (row menu, bulk export, report menus in 05). A custom popover, not a native menu, "because native popups are unreliable on the software-render VM" (ELECTRON-ONLY reason; 7.8).

- Props: `items`, `label = '⋯'`, `title = 'More actions'`, `disabled = false`, `triggerClassName = 'btn btn--small btn--ghost btn--icon'`.
- Item kinds: separator (`menu__sep`, 1px `--hair-2`, margin `0.25rem 0.2rem`), header (`menu__header`, `role="presentation"`, 0.72rem, weight 600, uppercase, letter-spacing 0.03em, `--ink-3`, `font-stretch: var(--label-stretch)`, not selectable), action (`button.menu__item`, `role="menuitem"`, optional `danger` (`--danger-ink`, hover `--danger-tint`) and `disabled` (opacity 0.5)).
- Trigger: `aria-haspopup="menu"`, `aria-expanded`, `title`, `disabled`.
- Opening measures the trigger: `estHeight = items.length * 34 + 16`; `below = innerHeight - rect.bottom`; `dropUp = below < estHeight && rect.top > below`. The popover is `right: 0` aligned to the trigger, `top: calc(100% + 4px)` or, when `dropUp`, `bottom: calc(100% + 4px)`; min-width 172 DIP; padding 0.3rem; border `--hair`; radius `--radius-control`; `--menu-shadow`; items gap 1px.
- Closes on: backdrop click (full-window transparent layer, z 50), an item click (closes first, then runs the action), Escape (window listener while open).
- No arrow-key navigation and no focus move into the menu when it opens.

REQUIRED (items, order, strings, close rules, flip intent); IMPROVEMENT for keyboard (7.8).

### 2.21 Notices

`Notice.tsx`, `notice.css`, `App.tsx:544-566`.

- `Notice({kind = 'error', children, onDismiss})`: `div.notice notice--${kind}`, `role="status"`; text span (`flex: 1 1 auto`); dismiss button `×` with `aria-label` and `title` `Dismiss` (`notice__dismiss`: transparent, no border, inherits the white text, 1.15rem, line-height 1, padding `0 0.15rem`, opacity 0.8, 1 on hover, `notice.css:71-84`). The notice is a flex row, `align-items: flex-start`, gap 0.6rem.
- Kinds and fills (translucent by alpha; blur is not relied on because the GPU is disabled on the dev VM): error `rgba(185, 28, 28, 0.6)`, info `rgba(37, 99, 235, 0.6)`, success `rgba(22, 163, 74, 0.6)`. Text `#fff` with `text-shadow: 0 1px 2px rgba(0, 0, 0, 0.45)`; `box-shadow: 0 6px 20px rgba(2, 6, 23, 0.25)`; radius 8px; padding `0.5rem 0.5rem 0.5rem 0.8rem`; 0.85rem, line-height 1.35; entry animation `notice-in` 140ms ease-out from opacity 0 and `translateY(-6px)`. `backdrop-filter: blur(8px)` is declared (`notice.css:33-34`) but is a no-op with the GPU disabled; natively no blur (REQUIRED look is the flat translucent fill). These colours are NOT tokens: `notice.css` is outside the app-chrome guard, so notices look the same under every brand and appearance.
- `div.notice-stack`: absolute, `top: 0.6rem`, horizontally centred, z 60, column, gap 0.4rem, `width: max-content`, `max-width: min(92%, 680px)`; `pointer-events: none` on the stack and `auto` on each notice, so clicks pass through the gaps. It floats over content and never reflows it.
- The stack is inside the scroll container, so it scrolls with the content (EDGE-HOME-22).
- App holds exactly one error string (a new error replaces the old) and one update notice. Order in the stack: error, then update.
- The App error notice is set by: any failed Home operation, `` `Capture error: ${message}` ``, a failed target load, a failed area selection, a failed brand-menu project theme write (03, 05). Messages are the raw `Error.message` (`e instanceof Error ? e.message : String(e)`, `:92-93`).
- Dismissed by `×`, replaced by a newer error, or cleared by any `refresh()` (2.18) and by `onImportPackage` start.

REQUIRED (look, placement, stacking, dismissal, message source), with the changes in 7.10.

### 2.22 Update notice

`App.tsx:53-72`, `:551-564`, `src/main/update-state.ts`, `src/main/main.ts:519-557` (10 owns the check).

- Main checks GitHub once a day on startup (10) and, only when a newer release exists, stashes the result and pushes `updates.onAvailable`. "Up to date" and failures are silent on Home (#54).
- On mount App both subscribes to the push and PULLS `updates.pending()`; whichever arrives first sets `update`; the other is a no-op (`webContents.send` does not buffer, and the check measurably finished 40 s before the renderer's first IPC in dev, `037858d`). A push with `available: false` clears `update` in the renderer, but main never sends one: the startup check pushes only after `setPendingUpdate(result)` for an available result (`src/main/main.ts:551-555`), so in practice nothing clears the notice except `×`.
- Rendered as an info notice: `` `shotAI ${version} is available.` `` then one space, then a link-styled button `Open the download page` (`button.notice__link`: inherits colour and font, weight 600, underlined, underline removed on hover) that calls `openExternal(url)` when `url` is set (10 allowlist: `https:` and host exactly `github.com`, INV-HOME-31).
- `×` dismisses for this session only; the next launch offers it again (subject to the daily throttle).
- The manual `Check now` in Settings (2.29) updates main's stash but does NOT push, so it does not raise the Home notice in the running session (the `037858d` message claims it would; the renderer only pulls on mount, EDGE-HOME-23).

REQUIRED.

### 2.23 Confirm and alert dialogs

`useConfirm.tsx:1-91`, `project.css:2544-2587`. A promise-based in-page modal that REPLACES `window.confirm` and `window.alert`, because the native dialogs steal the BrowserWindow's keyboard focus on this Electron build (text fields stop accepting typing until the window is re-focused, B4).

- `confirm(message, {confirmLabel?, danger?}) : Promise<boolean>`, `alert(message) : Promise<void>` (label `OK`, no Cancel).
- Markup: overlay `div.sop__overlay` (fixed, inset 0, z 50, centred, `rgba(17, 19, 24, 0.55)`, padding 1.5rem), `role="dialog"`, `aria-modal="true"`, `aria-label="Confirm"`; card `div.confirm` (max-width 420, `--surface`, radius `--radius-panel`, padding `1.25rem 1.5rem`, shadow `0 12px 40px rgba(0, 0, 0, 0.25)`); message `p.confirm__msg` (1rem, line-height 1.5, `--ink`, margin-bottom 1.1rem); actions right-aligned, gap 0.6rem: `Cancel` (`btn`, only for confirm), then the confirm button (`btn btn--danger` when `danger`, else `btn btn--primary`; text `confirmLabel ?? 'OK'`) with `autoFocus`.
- Escape inside the dialog (`onKeyDown` on the overlay) resolves `false`. It fires only while keyboard focus is inside the overlay: after a click on the dim area (a non-focusable `div`) focus falls to the document body, Escape no longer reaches the handler, and the window-level selection-clear of 2.15 receives it instead (EDGE-HOME-52). Clicking the dim overlay does nothing. Enter activates the focused button, which is initially the confirm button (so Enter confirms a Delete, EDGE-HOME-27). There is no focus trap.
- Portalled to `document.body`; a second `confirm` while one is open replaces it and the first promise never settles.

REQUIRED (strings, focus default, Escape), IMPROVEMENT for focus trapping (7.11); the Electron reason for the DOM modal is ELECTRON-ONLY.

### 2.24 Settings: shell, tabs, loading and errors

`Settings.tsx:36-179`, `:388-435`, `project.css:2077-2136`. Settings REPLACES the Home or project content in the same window (2.1).

- `section.settings` (max-width 640 DIP). Bar `div.settings__bar`: `← Back` (`btn btn--small`, calls `onBack`) and `h2.settings__title` `Settings` (`--fs-section`, `--fw-section`).
- Error: `p.project__error` with text `` `Error: ${error}` `` (`--danger-ink` on `--danger-tint`, radius `--radius-control`, padding `0.6rem 0.8rem`, 0.9rem). This is an inline block, not a floating notice; it does reflow the panel. `fail(e)` sets it; most handlers clear it first.
- Loading: `refresh()` loads 13 values with one `Promise.all` (`:149-179`): `settings.getSop`, `claude.keyStatus`, `getAppInfo`, `projects.getDir`, `settings.getRemoteVisible`, `getCaptureScale`, `getUserName`, `getIncludeNameInReports`, `getArchiveAgeDays`, `getTheme`, `getBrand`, `getUpdateCheckEnabled`, `auth.status`. Until `sop` is loaded the panel shows only `p.project__hint` `Loading…`. If any one read rejects, the error shows and the panel stays at `Loading…` (EDGE-HOME-25). Settings re-reads everything on every mount. [SECURITY] The `auth.status` read is also the policy refresh: its handler calls `invalidateFederationConfig()` before computing the status (`src/main/ipc.ts:788-815`), so an administrator's change to `HKLM\SOFTWARE\Policies\shotAI\Federation` takes effect when Settings is reopened, without a restart (INV-HOME-42; 08 INV-AUTH-9).
- Tabs (`SETTINGS_TABS`, order is the tab order and the arrow cycle): `ai` `AI`, `capture` `Capture`, `appearance` `Appearance`, `storage` `Storage`, `about` `About`. Default `ai` on every mount.
- Tab bar `div.settings__tabs`, `role="tablist"`, `aria-label="Settings sections"`; each `button.settings__tab`, `role="tab"`, `id="settings-tab-${id}"`, `aria-selected`, `aria-controls="settings-panel-${id}"` ONLY on the active tab (only the active panel exists), roving `tabIndex` (0 on the active tab, -1 on the others). Style: underline tabs (`--ink-2`, active `--accent` text and 2px `--accent` bottom border sitting on the 1px `--hair` container line).
- Keyboard on a tab (`onTabKey`, `:133-147`), WAI-ARIA tabs pattern with automatic activation: `ArrowRight` or `ArrowDown` selects the next tab, wrapping; `ArrowLeft` or `ArrowUp` the previous, wrapping; `Home` the first; `End` the last; the key's default is prevented and focus moves to the newly selected tab.
- Panel `div.settings__panel`, `role="tabpanel"`, `id="settings-panel-${tab}"`, `aria-labelledby="settings-tab-${tab}"`, `tabIndex={0}` (so a static panel such as About is reachable by Tab). Column flex, gap 1rem.
- Shared building blocks: `settings__group` (card: padding `1rem 1.1rem`, 1px `--hair`, radius `--radius-panel`, `--surface`); `settings__h` (`--fs-title`, `--fw-title`, `--ink`); `settings__hint` (0.82rem, `--ink-2`, line-height 1.4, margin-top 0.35rem); `settings__toggle` (a clickable `label` card: text on the left, switch on the right; padding `0.9rem 1.1rem`, `--surface-2`, radius `--radius-panel`; the whole card toggles the switch); `settings__switch` (a checkbox drawn as a 38 x 22 DIP track, radius `--radius-chip`, `--control-bd`, checked `--accent`; an 18 DIP `--surface` knob at left 2, checked left 18, `0 1px 2px rgba(0, 0, 0, 0.25)`; 0.15s transitions); radio chips reuse `capmode__chip` inside `div.capmode__modes` with `role="radiogroup"` and `role="radio"` plus `aria-checked`.

REQUIRED.

### 2.25 Settings: AI tab

`Settings.tsx:381-386`, `:436-739`. `fed = !!authStatus?.federationAvailable`, `signedIn = !!authStatus?.signedIn`. Three presentations (#63): unconfigured (`fed` false: every external user; the tab has NO mention of Entra anywhere), configured, configured with the key fallback opened.

**Master switch** (always shown): `settings__toggle` with strong text `AI SOP generation` and hint `Use Claude to write a step-by-step guide from your capture \u2014 this needs ${fed ? 'you to sign in below' : 'an Anthropic API key (below)'}. When off, no Claude features appear and nothing ever leaves your machine.`; the switch is `sop.enabled`; change calls `patch({enabled})`.

When `!sop.enabled`, only `p.settings__off` (dashed `--hair` border, `--ink-3`): `Claude SOP generation is off. Turn it on to choose a model and tone and ${fed ? 'sign in with your work account' : 'connect your Anthropic API key'}.` Everything below is hidden.

When `sop.enabled`:

**Microsoft sign-in group** (only when `fed`), heading `Microsoft sign-in`:

| Condition | Content |
|---|---|
| `signedIn` | hint `shotAI uses your work account for AI features. No API key needed. Configured by your organization.` then hint `Signed in as **${authStatus.account}**` |
| not signed in | hint `Sign in with your work account to use AI features. No API key needed \u2014 access is granted by your organization.` |

Action row `settings__keyactions` (right-aligned): the test chip (below) if a result exists; `Sign out` (`btn btn--small`, only when signed in, disabled `busy || signingIn`); `Test connection` / `Testing…` (only when signed in, disabled `testing`); primary button (disabled `signingIn || busy`) with text `Waiting for your browser…` while signing in, else `Sign in again` when signed in, else `Sign in with Microsoft`. When a test failed: `p.project__error` (margin-top 0.5rem) with the message; and when `authStatus.supportUrl` is set (always, when `fed` is true: main returns the configured SupportUrl or else 08's default support URL, `DEFAULT_SUPPORT_URL` in `src/main/entra/config-validate.ts:82`, the project's public GitHub issues page, `src/main/ipc.ts:814`), hint: `Your sign-in worked. If Claude access has not been granted to your account yet, [request access], then use **Sign in with Microsoft** again \u2014 a retry only picks up a new grant after a fresh sign-in.` where `request access` is a link (`settings__link`: `--accent`, weight 600, underlined, hover `--accent-press`) that calls `openExternal(supportUrl)`.

`signIn()` (`:181-197`): clear error and test result; `signingIn = true`; `auth.signIn()` (08; ALWAYS interactive, because a silent retry after IT grants the role re-serves a cached token without the roles claim); `refresh()`; errors to the inline error; finally `signingIn = false`. `signOut()` (`:199-208`): clear error and test result; `auth.signOut()` (forgets the Entra account, keeps any stored API key); `refresh()`.

**Key fallback disclosure** (only when `fed`): `btn btn--small`, `aria-expanded={showKeyFallback}`, text `▾ Use my own Anthropic API key instead` when open, `▸ Use my own Anthropic API key instead` when closed; toggles `showKeyFallback` (default false, not persisted).

**Anthropic API key group** (when `!fed || showKeyFallback`), heading `Anthropic API key`:

- Hint: `shotAI uses **your own** Anthropic API key to write SOPs (billed per use). Your organization may provide one \u2014 paste it below. Otherwise, create your own at [console.anthropic.com/settings/keys].` The link opens `https://console.anthropic.com/settings/keys` via `openExternal`.
- Status hint (when `keyStatus` loaded), the concatenation of, in this order:
  - `source === 'stored'`: `A key is saved (encrypted) on this machine ✓`
  - `source === 'env'`: `Using the ANTHROPIC_API_KEY environment variable.`
  - `source === 'none' && !hasStoredCiphertext`: `No key set yet.`
  - `hasStoredCiphertext && source !== 'stored'`: ` A previously saved key couldn’t be read on this machine \u2014 clear it and enter a new one.` (leading space)
  - `!encryptionAvailable`: ` Secure storage is unavailable on this system \u2014 a key can’t be saved here; set ANTHROPIC_API_KEY instead.` (leading space)
- Key input: `input.project__input settings__keyfield`, `type="password"`, placeholder `sk-ant-…`, `autoComplete="off"`, `spellCheck={false}`, value `keyInput` (never pre-filled: the key is never read back), disabled when `busy || !keyStatus.encryptionAvailable`.
- Action row: the test chip; `Clear` (`btn btn--small`, shown when `source === 'stored' || hasStoredCiphertext`, disabled `busy`); `Test connection` / `Testing…` (disabled `testing || !keyStatus.hasKey`); `Save` (`btn btn--small btn--primary`, disabled `busy || !keyInput.trim()`).
- When a test failed: `p.project__error` with the message.

`saveKey()` (`:320-334`): return if the trimmed input is empty; `busy = true`; clear error and test; `claude.setApiKey(trimmed)` (08, DPAPI); `keyInput = ''`; re-read ONLY the key status (a full refresh would clobber an in-flight blur-persisted custom-instructions edit); errors inline; finally `busy = false`. `clearKey()` (`:336-348`): same shape with `claude.clearApiKey()`. Enter in the key field does nothing (no form).

**Connection test** (`:350-379`): `testing = true`; clear test and error; `r = claude.testConnection()` (08); `legLabel = r.leg === 'signIn' ? 'Microsoft sign-in' : r.leg === 'exchange' ? 'Claude access' : r.leg === 'api' ? 'Claude API' : null`; result `ok = r.ok`, `msg = r.ok ? 'Connected' + (r.model ? ' (' + r.model + ')' : '') + '.' : (legLabel ? legLabel + ': ' : '') + (r.error ?? 'Test failed.')`; a throw goes to the inline error; finally `testing = false`. The chip (`span.settings__chip`, `title = msg`) reads `● Connected` (`--note-fg` on `--note-bg`, border `--note-bd`) or `● Error` (`--danger-ink` on `--danger-tint`, border `--danger-bd`). The result is cleared by any `patch`, `saveKey`, `clearKey`, `signIn`, `signOut` (any settings change invalidates a prior test). With `fed` true and the key fallback open, the chip and the failed-test `p.project__error` render in BOTH groups at once (both read the one `testResult`), and the Microsoft group's `request access` hint appears for a failure of any leg, including a key test (EDGE-HOME-54).

**Model** group, heading `Model`: radiogroup `aria-label="Model"` with one chip per `SOP_MODELS` entry; today one: `Sonnet 5 \u2014 latest (recommended)` (id `claude-sonnet-5`); click `patch({model})`; hint = the selected entry's blurb `Anthropic’s latest Sonnet: capable, fast, and cost-effective.` (`src/shared/sop.ts:19-25`).

**Tone** group, heading `Tone`, `aria-label="Tone"`: `Professional` (`Formal, third-person, SOP-standard phrasing.`), `Friendly` (`Warm, second-person, approachable.`), `Concise` (`Minimal words, action-first.`), `Detailed` (`Thorough; explains the "why" and adds context.`) (`sop.ts:64-69`). The hint shows the selected blurb.

**Effort** group, heading `Effort`, `aria-label="Effort"`: `Low` (`Fastest and cheapest; least deliberation.`), `Medium` (`Balanced quality, speed, and cost (recommended).`), `High` (`Most thorough; slower and pricier.`) (`sop.ts:43-47`).

**Custom instructions** group, heading `Custom instructions (optional)`: `textarea.settings__textarea`, 3 rows, `maxLength = SOP_CUSTOM_INSTRUCTIONS_MAX` (2000), placeholder `e.g. Reference our ticketing system, avoid jargon, always note required permissions…`, vertical resize; typing updates local state only; BLUR persists `patch({customInstructions: value})` on EVERY blur, changed or not, so merely tabbing through the field rewrites `settings.json` and clears a shown connection-test result (EDGE-HOME-55); hint `` `${length}/2000` `` (UTF-16 code units, live).

`patch(p)` (`:310-318`): clear error and test; `sop = await settings.setSop(p)` (10 coerces onto the stored value and returns the full coerced object); errors inline. The chip selection changes only after the write returns (not optimistic).

REQUIRED.

### 2.26 Settings: Capture tab

`Settings.tsx:741-792`.

**Screenshot quality** group, heading `Screenshot quality`, hint `Downscales captured screenshots to cut file size and AI cost. Lower = smaller and cheaper but softer text; a readability floor keeps small captures legible to Claude. Applies to new captures.`; a range input (`min 0.5`, `max 1`, `step 0.05`, `aria-label="Screenshot quality"`, accent `--accent`) and a value label `` `${round(captureScale * 100)}%` `` (weight 600, tabular, `--ink`). Dragging updates the local value live; `pointerup` and every `keyup` on the slider persist (`settings.setCaptureScale(value)`, 10 clamps to [0.5, 1], non-finite becomes 0.85) and adopt the returned value; errors inline.

**Remote visibility** toggle: strong `Show shotAI in remote sessions & screen shares`, hint `By default shotAI is hidden from all screen capture, which is what keeps it out of its own screenshots, but also makes it invisible in Teams, Splashtop, GoToAssist and similar. Turn this on to see and use the app over a remote connection. Your screenshots stay clean: shotAI still hides itself while recording, and the capture pill is excluded from each shot as it is taken.`; switch bound to `remoteVisible`; change persists (`settings.setRemoteVisible`, which also re-applies display affinity to the open windows immediately, 02, 03) and adopts the result; errors inline. The switch reflects the change only after the write returns.

The removed "Keep shotAI visible during capture" toggle (`f24b3dc`) must not come back; a leftover `captureNoHide` key in `settings.json` is ignored (10). REQUIRED.

### 2.27 Settings: Appearance tab

`Settings.tsx:18-34`, `:794-846`. One group.

- Heading `Theme`; hint `Choose the app’s color theme. “System” follows your Windows light/dark setting.`; radiogroup `aria-label="Theme"`: `System` (blurb `Match your Windows light/dark setting.`), `Light` (`Always use the light theme.`), `Dark` (`Always use the dark theme.`); hint = selected blurb.
- Heading `Brand`; hints `Which identity the app wears. Independent of light/dark \u2014 each brand has both.` and `Exports follow the brand too, always in its light colors \u2014 a dark document is unreadable printed.`; radiogroup `aria-label="Brand"`: one chip per `BRAND_IDS` entry in catalog order with label `BRANDS[id].label` (`shotAI`, `LFI`); blurb `shotAI’s own identity \u2014 violet.` for the default brand and `LaCrosse Footwear corporate \u2014 charcoal and rust.` for every other brand (EDGE-HOME-26); hint = selected blurb.

`chooseTheme(v)` and `chooseBrand(v)` are OPTIMISTIC: set local state and call `onThemeChanged(v)` or `onBrandChanged(v)` (App re-applies at once), then persist (`settings.setTheme` or `setBrand`, both coerced by 10), then adopt the stored value and notify App again; errors inline (with NO rollback of the optimistic value, EDGE-HOME-28). Appearance and brand are two separate controls because every brand exists in light and dark (#77). REQUIRED.

### 2.28 Settings: Storage tab

`Settings.tsx:848-890`.

- **Projects folder**: heading `Projects folder`; hint `Where shotAI stores each project \u2014 screenshots, manifest, and exports.`; row: `code.settings__dir` with the path (or `…` while empty; single line, ellipsis, full path in `title`), and `Change…` (`btn btn--small`). `chooseDir()`: `d = projects.chooseDir()` (main: folder dialog titled `Choose shotAI projects folder`, starting at the current folder, may create folders; on OK `setProjectsDir` creates the folder recursively and persists, 01); if `d` non-null, show it and call `onProjectsDirChanged` (Home re-lists). Recents from the previous root are kept and keep appearing on Home (01 lists recents too; macOS clears them, EDGE-HOME-29).
- **Auto-archive old projects**: heading `Auto-archive old projects`; hint `Projects you haven’t opened or edited in this long are compressed and moved to the Archive tab automatically. They stay listed \u2014 opening one restores it. You can also archive projects manually anytime.`; a `select` (`aria-label="Auto-archive age"`) with options `Never` (0), `After 1 month` (30), `After 3 months` (90), `After 6 months` (180), `After 1 year` (365); change persists `setArchiveAgeDays(Number(value))` and adopts the stored value; errors inline. The sweep itself runs at startup (01, 10).

REQUIRED.

### 2.29 Settings: About tab

`Settings.tsx:892-983`.

- **Your name**: heading `Your name`; hint `Optionally credited on exported guides. When included, the footer reads “Created on <date> by <your name>”.`; `input.project__input` text, placeholder `e.g. Dana Reyes`, `maxLength = 120`; typing is local; BLUR persists `setUserName(value)` (10: non-string becomes `''`, then first 120 UTF-16 units, NOT trimmed); adopts the stored value; then, if the stored value trims to empty and `includeName` is on, persists `setIncludeNameInReports(false)` (an empty name cannot be included).
- Toggle (margin-top 0.75rem): strong `Include my name in reports & exports`; hint `Adds “by <your name>” to the “Created on …” line of every export. Set a name above to enable this.`; switch `includeName`, DISABLED while `!userName.trim()` (evaluated on the live, unsaved field value); change persists and adopts.
- **About**: heading `About`; hint `` `${name} ${version} · ${platform}/${arch} · Electron ${electron}` `` (for example `shotAI 1.3.0 · win32/x64 · Electron 42.5.0`), `…` until loaded. ELECTRON-ONLY for the Electron part (7.12).
- **Updates**: heading `Updates`; toggle strong `Check for updates`, hint `Asks GitHub once a day, when shotAI starts, whether a newer version has been released, and tells you if one has. This is the only time shotAI contacts the internet on its own. It never installs anything by itself.`; switch `updateCheck`. `toggleUpdateCheck(v)` is optimistic: set local `v`, clear the message, persist, adopt; on failure revert to `!v` silently (no error shown). Row (margin-top 0.75rem): `btn btn--small` `↻ Check now` / `Checking…` (disabled while checking) and, when set, a hint span with the message.
- `checkNow()` (`:104-122`): `updateBusy = true`; clear message; `r = updates.check()` (10: bypasses the daily throttle, stamps `lastUpdateCheckAt`, updates the pending stash); if `r.available && r.url`: message `` `shotAI ${r.version} is available.` `` AND open `r.url` in the browser at once; else if `r.error`: `` `Couldn't check: ${r.error}` ``; else `You're up to date.`; a throw sets the message to the raw error text; finally `updateBusy = false`. The manual check runs even when the toggle is off.
- **Getting started** (rendered when `onReplayTour` is passed, which App always does): heading `Getting started`; hint `New to shotAI, or want a refresher? Replay the quick intro tour on the home screen.`; `↺ Show intro tour` (`btn btn--small`) calls `onReplayTour` (2.31).

REQUIRED.

### 2.30 Settings keys each control writes

All in `%APPDATA%\shotAI\settings.json` (10 owns the file, the coercion and the serialized atomic write; `src/main/settings.ts:25-194`). "Adopt" means the UI shows the value the writer returns.

| Control | Key | Type and default | Coercion on write (10) | When written | UI timing (Electron) |
|---|---|---|---|---|---|
| AI SOP generation switch | `sop.enabled` | bool, `true` | boolean else keep | on change | after write |
| Model chip | `sop.model` | `'claude-sonnet-5'` | must be in `SOP_MODELS` else keep | on click | after write |
| Tone chip | `sop.tone` | `'professional'` | in `SOP_TONES` else keep | on click | after write |
| Effort chip | `sop.effort` | `'medium'` | in `SOP_EFFORTS` else keep | on click | after write |
| Custom instructions | `sop.customInstructions` | `''` | string, first 2000 code units | on blur | local while typing |
| API key field, Save, Clear | none (08's API key store: Electron `secrets.json`; natively DPAPI in `%APPDATA%\shotAI\secrets.dpapi.json`, reached only through `IAuthService.SetApiKeyAsync` and `ClearApiKeyAsync`, R-ARCH-2, R-ARCH-14) | | trimmed | Save or Clear | after write |
| Screenshot quality | `captureScale` | `0.85` | finite number clamped to [0.5, 1], else 0.85 | pointer up, key up | local while dragging |
| Remote visibility | `remoteVisible` | `false` | `value === true` | on change | after write |
| Theme | `theme` | `'system'` | `light`, `dark`, `system`, else `system` | on click | optimistic |
| Brand | `brand` | `'shotAI'` | `isBrandId` else `shotAI` | on click | optimistic |
| Projects folder | `projectsDir` | `%USERPROFILE%\shotAI Projects` | folder created first | dialog OK | after write |
| Auto-archive | `archiveAgeDays` | `90` | non-number or non-finite: 90; `<= 0`: 0; else `round` clamped to [1, 1825] | on change | after write |
| Your name | `userName` | `''` | string, first 120 code units | on blur | local while typing |
| Include my name | `includeNameInReports` | `false` | `value === true` | on change, and forced false after a blank name is saved | after write |
| Check for updates | `updateCheckEnabled` | `true` | `value === true` | on change | optimistic, silent revert |
| Check now | `lastUpdateCheckAt` | `0` (epoch ms) | | after the manual check | |
| Tour close | `hasSeenTour` | `false` | `value === true` | tour Skip, Done, Esc, click-outside | fire and forget |

`recents` is written by 01, never by this UI. Unknown keys in `settings.json` survive every write (10, #92).

### 2.31 Onboarding tour

`Tour.tsx:1-191`, `App.tsx:73-76`, `:191-212`, `:866`, `project.css:2862-3075`.

**Once flag.** On mount App reads `settings.getHasSeenTour()`; if false, `tourOpen = true`; a read failure is swallowed (no tour). `closeTour()` sets `tourOpen = false` and writes `setHasSeenTour(true)` (fire and forget). The tour renders only when `showHome && !showSettings && tourOpen` (its anchors live on Home). `replayTour()` (Settings `↺ Show intro tour`) sets `showSettings = false` and `tourOpen = true`; it does NOT write `hasSeenTour = false` (the `settings.ts:82-85` comment claiming it does is wrong; closing writes `true` again, harmlessly). REQUIRED.

**Steps** (`STEPS`, `:17-49`; five, in order):

| # | Anchor (`data-tour`) | Headline | Body |
|---|---|---|---|
| 1 | `hero` (the create hero) | `Welcome to shotAI` | `Record a process, mark it up, and let Claude turn it into a step-by-step guide \u2014 an SOP \u2014 you can export and share. It all starts here.` |
| 2 | `capture` (the Capture button) | `Capture your process` | `Click “Capture ▸” to start recording. shotAI hides while you work \u2014 every click captures a screenshot and becomes a numbered step. Building from images or text instead? Use “Empty Project”.` |
| 3 | `mode` (the mode radiogroup) | `Choose what gets captured` | `“Screen” grabs a full monitor each step \u2014 the most predictable choice, and the default. Pick “Window” or “Area” to narrow it down. “Auto” guesses per click and can grab extra context.` |
| 4 | none (centred), with the pill mock-up | `Recording? Just click` | `Once recording, a small bar stays on top. Switch to any app and click anything to capture a step \u2014 or press Ctrl+Shift+S. Pause to stop capturing, Stop to finish, the red ✕ to discard.` |
| 5 | `settings` (the header Settings button) | `Let Claude write the guide` | `Open ⚙ Settings → AI and choose “Sign in with Microsoft” to use your work account. No API key needed. If sign-in isn’t offered there, your organization hasn’t set it up, so add your own Anthropic API key instead. Then hit “✨ Generate SOP with Claude”.` |

Step 5 leads with sign-in for EVERY user (not branched on federation); the "if sign-in isn't offered" clause is load-bearing (`da727b8`). REQUIRED.

**Pill mock-up** (step 4, `aria-hidden`): a dark capsule (`#1f2330` background, `#f4f5f7` text, radius 999px, padding `7px 10px`, gap 8px): a 9 DIP `#34d399` dot; label `Capturing · 3` (0.78rem, 600) with a second line `Click anything · Ctrl+Shift+S` (0.64rem, 500, `#aeb4c7`); chips `Pause` (`#cfd4e4` text, `#3a4159` border, radius 6px, padding `2px 6px`, 0.66rem, pushed right), `Stop` (`--accent` background and border, `--on-accent` text), `✕` (`#fca5a5` text, `#5a3040` border). These literals are deliberate: it mimics the real pill, which is theme-agnostic by decision (03). REQUIRED.

**Rendering** (portalled to `document.body`): `div.tour` (fixed, inset 0, z 1000), `role="dialog"`, `aria-modal="true"`, `aria-label="Getting started"`, containing:

1. `tour__scrim`: transparent full-window layer; click = finish (skip).
2. With an anchor rect: `tour__spot` (fixed, radius `--radius-card`, `pointer-events: none`, `box-shadow: 0 0 0 3px var(--accent), 0 0 0 9999px rgba(17, 19, 27, 0.55)`, transitions `top, left, width, height` 0.25s ease, none under `prefers-reduced-motion: reduce`). Without: `tour__fulldim` (fixed, inset 0, `rgba(17, 19, 27, 0.55)`, no pointer events).
3. `tour__bubble` (fixed, width 330, `max-width: calc(100vw - 24px)`, `--surface`, 1px `--hair`, radius `--radius-panel`, `0 18px 44px rgba(20, 22, 31, 0.3)`, padding `16px 18px 14px`); clicks inside do not reach the scrim. Content: caret (when anchored); `Step ${i + 1} of 5` (`tour__step`: 0.7rem, 700, uppercase, letter-spacing 0.06em, `--accent`, label stretch); headline `h3` (1.1rem, `--ink`); the pill mock (step 4); body (0.9rem, line-height 1.5, `--ink-2`); nav row: five dots (`aria-hidden`; 6 DIP circles, `--accent` for the current step, else `--hair`), `Skip` (`tour__btn--skip`: no border, `--ink-3`), `Back` (only when `i > 0`), and `Next`, or `Done` on the last step (`tour__btn--pri`: `--accent`, `--on-accent`). Buttons: 0.82rem, 600, radius `--radius-control`, padding `6px 12px`, 1px `--hair`, hover `--ground` (primary hover `--accent-press`).

**Measuring the anchor** (`:71-88`): on each step change (layout effect) and on every window `resize` and every `scroll` (capture phase, so the body's scroll counts): `rect = document.querySelector('[data-tour="<anchor>"]')?.getBoundingClientRect() ?? null`. A missing anchor renders the step centred with the full dim.

**Placement** (`:101-133`), viewport `W = innerWidth`, `H = innerHeight`, `BUBBLE_W = 330`, `GAP = 14`:

```
if rect:
  below     = rect.bottom + 220 < H
  left      = max(12, min(rect.left + rect.width / 2 - BUBBLE_W / 2, W - BUBBLE_W - 12))
  bubble    = below ? { top: rect.bottom + GAP, left } : { bottom: H - rect.top + GAP, left }
  caretLeft = max(18, min(rect.left + rect.width / 2 - left, BUBBLE_W - 18))   // caret's LEFT edge
  caret     = below ? 'top' : 'bottom'
  spot      = { top: rect.top - 6, left: rect.left - 6, width: rect.width + 12, height: rect.height + 12 }
else:
  bubble    = centred (top 50%, left 50%, translate -50% -50%); no caret; full dim
```

The caret is a 14 x 14 square rotated 45 degrees with `--surface` fill and 1px `--hair` border on its two outward sides, at `top: -8px` (caret `top`) or `bottom: -8px`, left edge at `caretLeft`. The 220 is an estimate of the bubble's height; `bottom` placement avoids measuring it. REQUIRED.

**Keyboard** (`:90-99`, window listener while the tour is mounted): `Escape` finish; `ArrowRight` next (finishes on the last step); `ArrowLeft` back (not below step 1). No Enter binding. Initial focus is not moved into the bubble; Tab can reach the page behind (EDGE-HOME-33).

**State machine**:

| State | Event | Guard | Action | Next |
|---|---|---|---|---|
| Closed | App mount | `hasSeenTour` read false | `tourOpen = true` | Open(0) |
| Closed | Replay from Settings | | leave Settings; `tourOpen = true` | Open(0) (renders when Home shows) |
| Open(i) | Next or ArrowRight | `i < 4` | `i + 1` | Open(i+1) |
| Open(4) | Done, Next or ArrowRight | | finish | Closed, `hasSeenTour = true` |
| Open(i) | Back or ArrowLeft | | `max(0, i - 1)` | Open |
| Open(i) | Skip, Escape, click outside the bubble | | finish | Closed, `hasSeenTour = true` |
| Open(i) | Home stops showing (Settings via `Ctrl+,`, project opened) | | the component unmounts; `tourOpen` stays true | Open(0) again when Home shows (step resets) |

`finish` is called outside any state updater: the earlier form called it inside `setI`'s updater, which React StrictMode double-invokes, so `onClose` ran twice (two `set-has-seen-tour` writes 2 ms apart, `da727b8`). REQUIRED natively as "close exactly once" (INV-HOME-20).

### 2.32 Theme: appearance, brand precedence, application

`theme.ts:1-79`, `main.tsx:1-21`, `App.tsx:359-385`, `theme-palette.ts:412-441`.

- **Two independent axes** (#77): appearance (`ThemePref`: `light`, `dark`, `system`) and brand (`BrandId`: `shotAI`, `lfi`). Never one combined value.
- `resolveAppearance(pref) = pref === 'dark' || (pref === 'system' && matchMedia('(prefers-color-scheme: dark)').matches) ? 'dark' : 'light'`. On Windows Chromium's `prefers-color-scheme` follows the per-user "app mode" (Settings, Personalization, Colors, "Choose your default app mode"; registry `HKCU\Software\Microsoft\Windows\CurrentVersion\Themes\Personalize` value `AppsUseLightTheme`, `0` = dark).
- `applyTheme(pref, brand = DEFAULT_BRAND)`: install the token sheet if missing; set BOTH `<html data-theme=<appearance>>` and `<html data-brand=<brand>>` (a document missing either attribute falls back to the bare `:root` block, the default brand's light palette).
- `watchSystemTheme(pref, onChange)`: only while `pref === 'system'`, re-apply when the OS setting flips; unsubscribes when the preference or brand changes.
- `installThemeStyle()` runs in `main.tsx` BEFORE `createRoot` (the preference loads asynchronously; without it the first frame has no colours), and inside `applyTheme` too. Idempotent by element id `shotai-theme-tokens`.
- App loads `theme` and `brand` once on mount (`:361-364`), failures swallowed (defaults `system`, `shotAI`); Settings pushes changes through `onThemeChanged` and `onBrandChanged`.
- **Active brand** (#77 phase 1b, `e19bcc0`): `activeBrand = (openPath && !showSettings && projectTheme) || brand`. A project that pins a recognised brand wears it everywhere while its view is on screen, chrome included; Home and Settings belong to no project and always use the app brand, even when Settings was opened from inside a pinned project. `projectTheme` is `pinnedBrand(manifest.theme)` (05, 10): an unrecognised pin (written by a newer build) is null here and renders the app brand; only the View then Brand menu is told it is unrecognised (03, #107).
- The effect re-runs on `[themePref, activeBrand]`, so a brand or appearance change re-cascades every `var()` read with no re-render and no lost view state.
- App pushes `{projectOpen: !!openPath, projectTheme: openPath ? projectTheme : null, projectPinUnrecognised: openPath ? projectPinUnrecognised : false, appBrand: brand}` to the menu (03) on `[openPath, projectTheme, projectPinUnrecognised, brand]`, errors swallowed; and handles `onMenuSetProjectTheme(choice)` once, reading the store imperatively, passing `choice` (null clears, any brand pins, the default included) untouched to `setProjectTheme`, then applying the returned manifest; errors to the notice (`App.tsx:387-429`, 03 and 05 own the menu and the write).

REQUIRED.

### 2.33 Design tokens

Two layers. Static tokens authored in `project.css:23-82`; generated tokens emitted by `themeStylesheet()` (`theme-palette.ts:426-441`) from `BRANDS` (`src/shared/brand-colors.generated.ts`, generated from `contract/brand.json`, owned by 08).

**Static tokens** (same for every brand):

| Token | Light | Dark | DIP / meaning |
|---|---|---|---|
| `--fs-display` | `1.75rem` | same | 28, app or brand display |
| `--fs-section` | `1.2rem` | same | 19.2, section headings |
| `--fs-title` | `0.94rem` | same | 15.04, item and step titles |
| `--fs-body` | `0.875rem` | same | 14, body |
| `--fs-meta` | `0.8rem` | same | 12.8, metadata |
| `--fs-label` | `0.69rem` | same | 11.04, uppercase micro-labels |
| `--fw-display` | `750` | same | weight |
| `--fw-section` | `700` | same | weight |
| `--fw-title` | `600` | same | weight |
| `--shadow-sm` | `0 2px 8px rgba(20, 22, 31, 0.06)` | `0 2px 8px rgba(0, 0, 0, 0.35)` | row card elevation |
| `--shadow` | `0 10px 30px rgba(20, 22, 31, 0.1)` | `0 12px 30px rgba(0, 0, 0, 0.5)` | raised surfaces |
| `--menu-shadow` | `0 8px 24px rgba(0, 0, 0, 0.16)` | `0 8px 24px rgba(0, 0, 0, 0.55)` | menus, dropdowns |
| `color-scheme` (UA hint) | `light dark` | `dark` | native control rendering |

The shadows are shared by both brands (a recorded divergence from macOS, which tints its shadow with the brand; at 6-10% alpha the difference is not perceptible, `project.css:68-76`).

**Generated colour tokens** (36 roles; `--<token>`; source rows `brand-colors.generated.ts:22-59` shotAI light, `:60-97` shotAI dark, `:107-144` LFI light, `:145-182` LFI dark):

| Role | CSS token | shotAI light | shotAI dark | LFI light | LFI dark |
|---|---|---|---|---|---|
| accent | `--accent` | `#6344f1` | `#9a8bf7` | `#b46b3e` | `#d58b5c` |
| accentPress | `--accent-press` | `#5233d4` | `#b0a4fa` | `#9a5a33` | `#e3a579` |
| accentTint | `--accent-tint` | `#efeafe` | `#241f3a` | `#f6ede5` | `#3a2e25` |
| accentInk | `--accent-ink` | `#4a34c9` | `#c8bdfb` | `#8f5430` | `#e3a579` |
| onAccent | `--on-accent` | `#ffffff` | `#171528` | `#ffffff` | `#211f1c` |
| ink | `--ink` | `#191826` | `#ece9f7` | `#47443e` | `#f8f4ec` |
| ink2 | `--ink-2` | `#5a5772` | `#a8a4c0` | `#6f695f` | `#cfc7b8` |
| ink3 | `--ink-3` | `#6f6c88` | `#8e8aa8` | `#756c5c` | `#b5aa99` |
| hair | `--hair` | `#e7e4f2` | `#302c42` | `#d8d2c6` | `#4a463f` |
| hair2 | `--hair-2` | `#efedf7` | `#282539` | `#e7e2d7` | `#3f3c36` |
| controlBd | `--control-bd` | `#cbc7db` | `#3c3852` | `#c9c1b3` | `#686258` |
| focusRing | `--focus-ring` | `#c7d2fe` | `#c7d2fe` | `#e8cdb4` | `#e8cdb4` |
| accentSoft | `--accent-soft` | `#a5b4fc` | `#a5b4fc` | `#dcb896` | `#dcb896` |
| surface | `--surface` | `#ffffff` | `#1b1926` | `#ffffff` | `#3a3833` |
| surface2 | `--surface-2` | `#faf9ff` | `#211f2e` | `#faf8f3` | `#43403a` |
| ground | `--ground` | `#f5f4fb` | `#121019` | `#f5f2eb` | `#2f2d29` |
| fieldBg | `--field-bg` | `#ffffff` | `#2e2b40` | `#ffffff` | `#4a4740` |
| ok | `--ok` | `#0e9f6e` | `#34d399` | `#3e7d5a` | `#6fb089` |
| okTint | `--ok-tint` | `#e7f7ef` | `#12271e` | `#e9f1eb` | `#23302a` |
| okInk | `--ok-ink` | `#07724f` | `#6ee7b7` | `#2b5b40` | `#9bceb1` |
| draft | `--draft` | `#c77d16` | `#e0a355` | `#c79a72` | `#d5ae89` |
| draftTint | `--draft-tint` | `#fbf1e0` | `#2a2113` | `#f7efe6` | `#332a21` |
| draftInk | `--draft-ink` | `#8a5610` | `#f0c98a` | `#8a5f35` | `#e0c09e` |
| danger | `--danger` | `#dc2626` | `#f87171` | `#9d3f32` | `#c96253` |
| dangerInk | `--danger-ink` | `#b91c1c` | `#fca5a5` | `#7f3227` | `#e0897b` |
| dangerTint | `--danger-tint` | `#fef2f2` | `#2a1414` | `#f7eae7` | `#33211e` |
| dangerBd | `--danger-bd` | `#f0c2c2` | `#5a2a2a` | `#ddbcb7` | `#5a332c` |
| noteBg | `--note-bg` | `#ecfdf5` | `#10281f` | `#e9f1eb` | `#23302a` |
| noteBd | `--note-bd` | `#6ee7b7` | `#2f6f52` | `#3e7d5a` | `#6fb089` |
| noteFg | `--note-fg` | `#065f46` | `#8ee7bf` | `#2b5b40` | `#9bceb1` |
| cautBg | `--caut-bg` | `#fffbeb` | `#2a2113` | `#f7efe6` | `#332a21` |
| cautBd | `--caut-bd` | `#fcd34d` | `#7a5c1e` | `#c79a72` | `#d5ae89` |
| cautFg | `--caut-fg` | `#92400e` | `#f0c98a` | `#8a5f35` | `#e0c09e` |
| warnBg | `--warn-bg` | `#fef2f2` | `#2a1414` | `#f7eae7` | `#33211e` |
| warnBd | `--warn-bd` | `#fca5a5` | `#7a3a3a` | `#c97f72` | `#c96253` |
| warnFg | `--warn-fg` | `#991b1b` | `#f6b0b0` | `#7f3227` | `#e0897b` |

The table is informational: the values are GENERATED from `contract/brand.json` and the native app must read them from 10's generated Core table (`BrandPalette`), never from this document (INV-HOME-26).

**Generated radius tokens** (by brand only; `radiusCss(null) = '999px'`, a capsule; `brand-colors.generated.ts:16`, `:101`):

| Token | Role | shotAI | LFI |
|---|---|---|---|
| `--radius-panel` | dialogs, popovers, panels, the Home row card | `12px` | `8px` |
| `--radius-card` | report step card, overview, tour spotlight | `10px` | `8px` |
| `--radius-figure` | media inside a card | `8px` | `6px` |
| `--radius-control` | inputs, buttons, menus, bordered boxes | `8px` | `5px` |
| `--radius-control-sm` | a control inside a control (menu item, inline input) | `6px` | `4px` |
| `--radius-micro` | checkboxes, tiny readouts, the focus ring | `4px` | `3px` |
| `--radius-chip` | chips, badges, status pill, sort chip, toggle track | `999px` (capsule; null) | `8px` |

**Generated type tokens** (`theme-palette.ts:403-408`, `brand-colors.generated.ts:17-21`, `:102-106`):

| Token | shotAI | LFI |
|---|---|---|
| `--font-stack` | `-apple-system,"Segoe UI",Roboto,Helvetica,Arial,sans-serif` (on Windows: Segoe UI) | `"Archivo","Helvetica Neue",Helvetica,Arial,"Liberation Sans",sans-serif` |
| `--label-stretch` | `normal` | `62%` (Archivo Condensed, for uppercase micro-labels) |

The app bundles `Archivo.ttf` as ONE variable font (`wght` 100-900, `wdth` 62-125) declared with the full ranges, because the face's default instance is weight 600 and without the declared range every `normal` text would render SemiBold (`project.css:1-21`, #77 phase 3).

**Sheet shape** (`theme-palette.ts:426-441`): a leading bare `:root{...}` block with the default brand's LIGHT values (the pre-JS fallback), then one fully qualified block per brand and appearance, `:root[data-brand="<id>"][data-theme="<light|dark>"]{...}`, each declaring all 36 colour, 7 radius and 2 type tokens. Fully qualified so the blocks are mutually exclusive and equally specific (a base-plus-override arrangement would let source order decide and a brand block would silently defeat dark mode). `project.css` declares no colour custom property and redeclares no generated token (`theme-palette.test.ts:64-91`).

**Derived colours** (`color-mix(in srgb, ...)`, per channel on 8-bit sRGB values, no linearization: `mix(a, p, b) = a * p + b * (1 - p)`; with `transparent` the colour is `a` at alpha `p`):

| Use | Expression | shotAI light | shotAI dark | LFI light | LFI dark |
|---|---|---|---|---|---|
| Row hover border | `color-mix(in srgb, var(--accent) 40%, var(--hair))` | `#b2a4f2` | `#5a528a` | `#caa990` | `#82624b` |
| Selected row background | `color-mix(in srgb, var(--accent-tint) 70%, var(--surface))` | `#f4f0fe` | `#211d34` | `#f9f2ed` | `#3a3129` |
| Bulk bar border | `color-mix(in srgb, var(--accent) 30%, transparent)` | accent at alpha 0.3 | same | same | same |

(The four computed values are rounded to the nearest integer per channel; a native test allows a difference of 1 per channel.)

**Fixed colours that are not tokens** in this subsystem: the notice fills and text (2.21), the overlay scrims `rgba(17, 19, 24, 0.55)` (confirm) and `rgba(17, 19, 27, 0.55)` (tour), the neutral shadows, the tour pill mock-up literals (2.31).

### 2.34 Shared control styles

`project.css:84-99`, `:220-303`, `:437-440`, `:965-992`. Measurements the native styles reproduce (DIP):

| Control | Style |
|---|---|
| Keyboard focus ring (all focusable) | `:focus-visible`: 2 px solid `--accent` outline, offset 2, radius `--radius-micro` |
| Text input `project__input` | padding `0.55rem 0.75rem`, 1px `--control-bd`, radius `--radius-control`, 0.95rem (15.2); focused: 2 px `--focus-ring` outline and `--accent` border |
| Button `btn` | padding `0.55rem 0.95rem`, 1px `--control-bd`, radius `--radius-control`, `--surface`, `--ink`, 0.9rem (14.4), no wrap; hover `--surface-2`; disabled opacity 0.55 |
| `btn--small` | padding `0.3rem 0.7rem`, 0.8rem (12.8) |
| `btn--primary` | `--accent` border and fill, `--on-accent` text; hover `--accent-press` |
| `btn--danger` | `--danger-ink` text, `--danger-bd` border; hover `--danger-tint` fill, `--danger` border |
| `btn--ghost` | transparent border and fill, `--ink-2`; hover `--accent-tint` fill, `--ink` |
| `btn--icon` | padding `0.3rem 0.5rem`, line-height 1 |
| Chip `capmode__chip` | padding `0.4rem 0.85rem`, 1px `--control-bd`, radius `--radius-chip`, `--surface`, `--ink`, 0.88rem; hover `--surface-2`; on: `--accent` border, `--accent-tint` fill, `--accent-ink`, 600 |
| Sort chip | padding `0.2rem 0.6rem`, 1px `--control-bd`, radius `--radius-chip`, `--surface`, `--ink-2`, 0.82rem; on: `--accent` fill and border, `--on-accent` |
| Menu item | padding `0.42rem 0.6rem`, radius `--radius-control-sm`, `--ink`, `--fs-body`; hover `--accent-tint` |
| Picker item | padding `0.45rem 0.7rem`, bottom 1px `--hair-2`; hover `--accent-tint`; on: `--accent-tint`, 600, and a 3 px `--accent` inset bar on the left |

`project__input`, `settings__textarea` and the `select` set no `background` or `color` of their own, so Chromium paints them with the UA `Field` and `FieldText` system colours under the page's `color-scheme` (white in light; the UA dark field grey in dark). The generated `--field-bg` token (2.33) is declared but read by no stylesheet (a repo-wide search finds it only in `src/shared/theme-palette.ts:353`). Natively WPF has no UA field colours; D-HOME-30 decides (EDGE-HOME-53).

REQUIRED (look), with the WPF translation in 7.5.

### 2.35 The no-hardcoded-colour rule

`app-chrome-tokens.test.ts:1-167`, commits `533cc55`, `d11ee4a`, `e19bcc0`. Applies to the project window's own stylesheets (`project.css`, `editor.css`); `toolbar.css` and `overlay.css` (the pill and the area overlay, separate documents that never receive `data-theme`) are out of scope by decision on both platforms, and `notice.css` is not scanned.

1. No hex colour literal (`#[0-9a-fA-F]{3,8}\b`) anywhere after stripping comments, except inside rules whose selector starts with `.rep__marker` (click markers mirror annotation colours already flattened into PNGs, 04 and 05) or `.tour__pill` (the pill mock-up). Exceptions are by SELECTOR, not by value, and each must carry a reason longer than 40 characters and must still exist in the sheets (a stale exception is a hole).
2. No `border-radius` literal except `var(--radius-*)`, `50%`, `0`, `2px`, `3px`, or inside the two exempt selectors (geometry is part of the brand: LFI draws tighter corners and rectangular chips).
3. No CHROMATIC `rgb()`/`rgba()`: a value whose channel spread `max(r, g, b) - min(r, g, b)` exceeds 40 is banned outside the exceptions; neutrals (spread at most 40; the widest in use is `rgba(15, 23, 42)`, spread 27) are allowed for shared shadows and scrims. The live case it caught: focus glows in `rgba(79, 70, 229, .18)` beside a `var(--accent)` border, which went rust under LFI while the glow stayed indigo; fix `color-mix(in srgb, var(--token) N%, transparent)`.
4. No `var(--x, #literal)` fallback (dead when the token resolves; and the inverse shipped: `var(--text, #e7e9ee)` where `--text` never existed painted a near-white numeral on white).
5. (From `theme-palette.test.ts`) every `var(--…)` the window reads resolves to a declared token.

REQUIRED natively as the XAML equivalent (INV-HOME-24, INV-HOME-25).

### 2.36 Keyboard and focus summary (Electron)

| Where | Key | Effect | Citation |
|---|---|---|---|
| Create name field | Enter | submit `onCreate` (no-op if busy or not ready) | `App.tsx:665` |
| Search field | Escape (query non-empty) | clear query, stop propagation | `ProjectList.tsx:476-482` |
| Anywhere on Home, selection non-empty | Escape | clear selection | `:236-243` |
| Rename field | Enter / Escape / blur | commit / cancel / commit | `:362-366` |
| Overflow menu open | Escape | close (the selection-clear listener ALSO runs) | `OverflowMenu.tsx:38-46` |
| Confirm dialog | Escape / Enter | cancel / activate focused button (confirm initially) | `useConfirm.tsx:63-65`, `:79` |
| Settings tab | Arrow keys, Home, End | roving selection with focus follow | `Settings.tsx:133-147` |
| Tour | Escape / ArrowRight / ArrowLeft | finish / next / back (global) | `Tour.tsx:90-99` |
| Menu accelerators | `Ctrl+O`, `Ctrl+,` | Import Project, Settings (03) | `src/main/menu.ts:211-217` |
| Recording | `Ctrl+Shift+S` | capture a step (02) | |

Focus order follows DOM order: header Settings button; hero (name, Capture, Empty Project, mode chips, dropdown trigger or area button); tabs; list head (heading has no stop; Import; search; clear; sort chips; direction); bulk bar (when shown); rows (checkbox, Open, overflow; rename input when editing). Initial focus on launch is not set (document body).

### 2.37 Accessibility summary (Electron)

Accessible names and roles present today: logo hidden; Settings button name `⚙ Settings`; mode `radiogroup` "Capture mode" with `radio`s and `aria-checked`; dropdown trigger `aria-haspopup="listbox"` and `aria-expanded`, `listbox` named "Window to capture" or "Monitor to capture" with `option`s and `aria-selected`; tabs `tablist` "Project sections"; search "Search projects by title or content"; clear "Clear search"; sort `group` "Sort projects" (chips have no pressed state); checkbox "Select <title>"; bulk `region` "Bulk actions", count `status` live polite, select-all `aria-pressed`; group headers and empty-state icons `aria-hidden`; overflow trigger "More actions" `aria-haspopup="menu"`; items `menuitem`; notices `role="status"` (not assertive; errors are announced politely), dismiss "Dismiss"; confirm `dialog` "Confirm" modal; tour `dialog` "Getting started" modal; Settings tabs with `aria-controls` and a focusable `tabpanel`; switches are native checkboxes wrapped in a `label` whose text is the name; Settings radiogroups "Model", "Tone", "Effort", "Theme", "Brand"; slider "Screenshot quality"; select "Auto-archive age". The name and custom-instructions fields have no programmatic label (EDGE-HOME-32).

---

## 3. Constants

| Name | Value | Unit | Meaning | Citation |
|---|---|---|---|---|
| `HOME_REFRESH_MS` | `20_000` | ms | Home auto-refresh interval while Home shows (#36) | `App.tsx:39` |
| Default capture mode | `'screen'` | | initial `mode` each launch | `App.tsx:82` |
| `MODE_OPTIONS` order | `screen`, `auto`, `window`, `area` | | chip order | `App.tsx:30-35` |
| Default sort | `'modified'`, descending | | list sort on every Home entry | `ProjectList.tsx:47-48` |
| Default tab | `'active'` | | on every Home entry | `ProjectList.tsx:49` |
| `SORT_LABELS` | `Name`, `Created`, `Modified` | | chip order | `ProjectList.tsx:14-18` |
| `BULK_FORMATS` | `html` HTML, `docx` Word, `pptx` PowerPoint, `pdf` PDF, `markdown` Markdown | | bulk export items (no `html-plain`) | `ProjectList.tsx:22-28` |
| Row export formats | `html`, `docx`, `pptx`, `html-plain`, `pdf`, `markdown` | | row menu order | `ProjectList.tsx:327-334` |
| `DATE_BUCKET_ORDER` | `This Week`, `Last Week`, `This Month`, `Last Month`, `Older` | | canonical newest-first order and labels | `date-groups.ts:7-13` |
| Week start | Sunday (`getDay() === 0`) | | local midnight | `date-groups.ts:16-20` |
| Last-week offset | `-7` | days | calendar days from this week's start | `date-groups.ts:33` |
| Overflow menu height estimate | `items.length * 34 + 16` | DIP | drop-up decision | `OverflowMenu.tsx:51` |
| Overflow menu gap | `4` | DIP | `calc(100% + 4px)` from the trigger | `project.css:1010`, `:1018` |
| Overflow menu min width | `172` | DIP | | `project.css:1012` |
| Target dropdown width | min `280`, max `460` | DIP | | `project.css:564-565` |
| Target list max height | `220` | DIP | then scrolls | `project.css:630` |
| Tour `BUBBLE_W` | `330` | DIP | bubble width | `Tour.tsx:51`, `project.css:2901` |
| Tour `GAP` | `14` | DIP | bubble to anchor | `Tour.tsx:52` |
| Tour below-threshold | `220` | DIP | `rect.bottom + 220 < innerHeight` | `Tour.tsx:108` |
| Tour edge margin | `12` | DIP | horizontal clamp | `Tour.tsx:110-111` |
| Tour caret clamp | `18` | DIP | min and `BUBBLE_W - 18` max | `Tour.tsx:116` |
| Tour spotlight pad | `6` | DIP | each side (so +12 in size) | `Tour.tsx:128-131` |
| Tour caret size | `14 x 14`, offset `-8` | DIP | rotated 45 degrees | `project.css:2910-2929` |
| Tour dim | `rgba(17, 19, 27, 0.55)` | colour | spotlight shadow and full dim | `project.css:2883`, `:2895` |
| Tour spotlight ring | `3` | DIP | `--accent` ring | `project.css:2882` |
| Tour spotlight transition | `0.25` | s | ease; off under reduced motion | `project.css:2884-2888`, `:3071-3075` |
| Tour steps | `5` | | | `Tour.tsx:17-49` |
| Tour z-order | `1000` | | above everything | `project.css:2866` |
| Notice fills | error `rgba(185, 28, 28, 0.6)`, info `rgba(37, 99, 235, 0.6)`, success `rgba(22, 163, 74, 0.6)` | colour | brand-agnostic | `notice.css:39-47` |
| Notice text | `#fff`, shadow `0 1px 2px rgba(0, 0, 0, 0.45)` | | | `notice.css:28-31` |
| Notice shadow | `0 6px 20px rgba(2, 6, 23, 0.25)` | | | `notice.css:32` |
| Notice radius | `8` | DIP | not a token | `notice.css:25` |
| Notice entry animation | `140` | ms | ease-out, from opacity 0 and `translateY(-6px)` | `notice.css:35`, `:86-95` |
| Notice stack | top `0.6rem`, gap `0.4rem`, max-width `min(92%, 680px)`, z `60` | | | `notice.css:4-17` |
| Confirm overlay | `rgba(17, 19, 24, 0.55)`, z `50`, padding `1.5rem` | | | `project.css:2544-2553` |
| Confirm card | max-width `420`, padding `1.25rem 1.5rem`, shadow `0 12px 40px rgba(0, 0, 0, 0.25)` | DIP | | `project.css:2569-2576` |
| Settings max width | `640` | DIP | | `project.css:2078` |
| `SETTINGS_TABS` | `ai` AI, `capture` Capture, `appearance` Appearance, `storage` Storage, `about` About | | order and arrow cycle | `Settings.tsx:38-44` |
| `THEME_OPTIONS` | `system`, `light`, `dark` | | order | `Settings.tsx:18-22` |
| `CAPTURE_SCALE_MIN` / `MAX` / `DEFAULT` | `0.5` / `1` / `0.85` | factor | screenshot quality | `src/shared/project.ts:18-20` |
| Capture scale slider step | `0.05` | factor | | `Settings.tsx:756` |
| `SOP_CUSTOM_INSTRUCTIONS_MAX` | `2000` | UTF-16 units | textarea `maxLength` and write cap | `src/shared/sop.ts:78` |
| User name max | `120` | UTF-16 units | input `maxLength` and write cap | `Settings.tsx:905`, `settings.ts:46-48` |
| `ARCHIVE_AGE_DEFAULT` | `90` | days | | `settings.ts:50` |
| Archive-age write clamp | `0` = never, else `[1, 1825]` after `round` | days | | `settings.ts:39-43` |
| Archive-age options | `0`, `30`, `90`, `180`, `365` | days | select values | `Settings.tsx:882-886` |
| Default theme / brand | `'system'` / `'shotAI'` | | | `settings.ts:189-190`, `theme-palette.ts:247` |
| `updateCheckEnabled` default | `true` | | | `settings.ts:171-172` |
| `CHECK_INTERVAL_MS` (10) | `24 * 60 * 60 * 1000` | ms | once a day on startup | `src/main/update-check.ts:18` |
| Anthropic console URL | `https://console.anthropic.com/settings/keys` | | key link | `Settings.tsx:576-584` |
| `openExternal` allowlist (10) | `https:` and host `anthropic.com`, `*.anthropic.com`, exactly `github.com`, or the exact origin of the configured support URL | | the only browser egress | `src/main/ipc.ts:271-310` |
| Theme sheet id | `shotai-theme-tokens` | | ELECTRON-ONLY | `theme.ts:29` |
| Neutral rgba spread | `40` | channel units | above it a colour is chromatic | `app-chrome-tokens.test.ts:130` |
| Allowed literal radii | `50%`, `0`, `2px`, `3px` | | `RADIUS_LITERAL_OK` | `app-chrome-tokens.test.ts:58` |
| Exception reason minimum | `> 40` | characters | each literal exception's reason | `app-chrome-tokens.test.ts:83` |
| Logo size | `30 x 30` | DIP | header | `project.css:139-145` |
| Switch track / knob | `38 x 22` / `18`, knob left `2` then `18` | DIP | settings switch | `project.css:2164-2196` |
| Row checkbox | `1.05rem` (16.8) | DIP | | `project.css:354-360` |
| Tab underline | `2.5` DIP high, radius `3px` | | Home tabs | `project.css:3105-3114` |

---

## 4. Invariants

**INV-HOME-1. Date buckets are computed in the local time zone by calendar arithmetic, checked in the fixed order This Week, Last Week, This Month, Last Month, Older, with a non-finite timestamp in Older and a future one in This Week.** Why: F4; the week buckets must win over the month buckets and boundaries must survive DST and month lengths. Citation: `date-groups.ts:15-41`. Test: `DateGroupsTests` (all `bucketFor` cases of 8.1) plus `DateGroupsDstTests`.

**INV-HOME-2. `Group` emits only non-empty buckets, in canonical order, each keeping its items' input order; Home reverses the bucket order when ascending.** Citation: `date-groups.ts:43-64`, `ProjectList.tsx:193-194`. Test: `DateGroupsTests.GroupEmitsNonEmptyInCanonicalOrder`, `.GroupOfNothingIsEmpty`, `HomeListPipelineTests.AscendingReversesBuckets`.

**INV-HOME-3. The list is date-grouped only when the sort key is Created or Modified and no search is active; Name sort is one flat group; a search groups by tier.** Citation: `ProjectList.tsx:172-195`. Test: `HomeListPipelineTests.NameSortIsFlat`, `.SearchSuppressesDateGroups`.

**INV-HOME-4. While searching, title matches come first, then content-only matches, each in sort order; the content tier is labelled `Matches in content` only when a title tier exists; the query is trimmed and lowercased and a whitespace-only query is not a search.** Citation: `ProjectList.tsx:145-186`; 01 2.9.6. Test: `HomeListPipelineTests.SearchTiers` (and 01 `ProjectSearchTests`).

**INV-HOME-5. Sorting is stable: equal keys keep the order `ListProjectsAsync` returned, in both directions.** Why: JavaScript's sort is stable and descending is a negated comparator; `List<T>.Sort` is not stable. Citation: `ProjectList.tsx:160-170`. Test: `HomeListPipelineTests.TiesKeepListOrderBothDirections`.

**INV-HOME-6. The tab counts are computed over the whole list and never change with the search; the heading count is the number of rows shown.** Citation: `ProjectList.tsx:59-60`, `:450`. Test: `HomeListPipelineTests.TabCountsIgnoreSearch`.

**INV-HOME-7. Bulk operations only ever touch projects that are selected AND currently visible; the selection is cleared on tab switch, on a search edit while something is selected, by Escape, by Clear, and after a bulk operation finishes.** Why: paths do not carry across tabs; the bulk count must not go stale against filtered rows. Citation: `ProjectList.tsx:67-72`, `:235-245`, `:470-475`. Test: `HomeSelectionTests`, `HomeViewModelTests.SearchEditClearsSelection`, `.TabSwitchClearsSelection`.

**INV-HOME-8. Shift-click adds the inclusive range between the anchor and the clicked row in VISIBLE order (tiers and date buckets included), never removes, and falls back to a plain toggle when either end is not visible.** Citation: `ProjectList.tsx:197-227`. Test: `HomeSelectionTests.RangeUsesVisibleOrder`, `.RangeIsAdditive`, `.RangeFallsBackToToggle`.

**INV-HOME-9. A bulk operation snapshots its targets, runs them one at a time in sort order, reports each failure and continues, refreshes once at the end, then clears the selection; progress reads `<Verb> <done> of <total>…`.** Citation: `ProjectList.tsx:255-278`. Test: `BulkRunnerTests.SequentialContinuesPastFailure`, `.ProgressCountsEveryItem`, `HomeViewModelTests.BulkRefreshesOnceThenClears`.

**INV-HOME-10. At most one Home operation runs at a time: while a row operation or a bulk operation runs, every row's Open and overflow trigger and every bulk action except Clear are disabled, and the busy row reads `Working…`.** Citation: `ProjectList.tsx:92-138`, `:321`, `:380`. Test: `HomeViewModelTests.AnyBusyDisablesActions`.

**INV-HOME-11. [SECURITY] Every export started from Home (row, bulk to own folders, bulk to one folder) runs 04's `EnsureFlattenedAsync` on that project's steps and completes it before calling the exporter; a flatten failure fails that project's export.** Why: only redacted, marker-baked renders may leave the machine. Citation: `ProjectList.tsx:112-125`, `:298-316`; 04 AC-EDIT-25. Test: `HomeExportFlowTests.FlattenPrecedesEveryExport` (fake flattener and exporter record call order), `.FlattenFailureSkipsExport`.

**INV-HOME-12. A bulk export to one shared folder asks for the folder once, before any project is touched; cancelling does nothing at all; the folder is revealed once, after every export has finished.** Why: #37 (no N dialogs, no folder popping open mid-run). Citation: `ProjectList.tsx:309-319`, `f9f8b10`. Test: `HomeExportFlowTests.SharedFolderAskedOnceRevealedOnce`, `.CancelledFolderDoesNothing`.

**INV-HOME-13. Deleting from Home always goes through an explicit confirmation with the exact 2.14 or 2.16 text and a danger-styled confirm button; a declined confirmation writes nothing.** Citation: `ProjectList.tsx:92-101`, `:280-290`. Test: `HomeViewModelTests.DeleteNeedsConfirm`, `.BulkDeleteNeedsConfirm`.

**INV-HOME-14. Rename commits only a trimmed, non-empty title that differs from the current one, at most once per rename session; Escape never commits.** Why: an unchanged write still bumps `updatedAt` and throws the project to the top of the list (the #70 lesson, `62b4b7d`). Citation: `ProjectList.tsx:78-91`. Test: `RenameSessionTests`.

**INV-HOME-15. The Home list refreshes on entering Home, on window activation while Home shows, every 20 s while Home shows, on the store's projects-changed signal, after every Home mutation and after a projects-folder change; the periodic tick is skipped while a text field has keyboard focus (and, natively, while renaming, while anything is selected, and while an operation runs, 7.6).** Why: #36 (external changes from cloud sync, another machine, the macOS app). Citation: `App.tsx:301-357`. Test: `AutoRefreshPolicyTests`, `HomeViewModelTests.RefreshTriggers`.

**INV-HOME-16. `Capture ▸` is enabled only when not busy, not recording, and the mode is ready (Window needs a picked window, Area needs a selected area, Screen and Auto are always ready).** Citation: `App.tsx:183-184`, `:677`. Test: `CaptureModePickerTests.Readiness`.

**INV-HOME-17. A project created by `Capture ▸` is started with `CreatedThisSession = true`; Empty Project, import and Resume capturing never set it; an empty name is allowed and becomes the store's default title.** Why: Discard on a brand-new project deletes the whole project (02, R5). Citation: `App.tsx:433-476`. Test: `HomeViewModelTests.CaptureCreatesAndStartsWithCreatedThisSession`.

**INV-HOME-18. While a capture session exists (recording or paused) the hero, the list, the project view and Settings are not shown and the Settings and Import requests are ignored.** Citation: `App.tsx:220-235`, `:252-264`, `:644`; 03 INV-SHELL-18. Test: `ShellViewModelTests.RecordingHidesViews`, `.MenuRequestsIgnoredWhileRecording`.

**INV-HOME-19. Returning to Home restores the Home scroll offset recorded while Home was scrolled; opening a project or Settings starts at the top.** Why: one shared scroll container used to open projects part-way down. Citation: `App.tsx:266-289`. Test: `ShellScrollTests` (Windows).

**INV-HOME-20. The tour opens automatically only when `hasSeenTour` is false, only on Home, and closing it by any path writes `hasSeenTour = true` exactly once per presentation; replay never writes `false`.** Why: `da727b8` (double `onClose`). Citation: `App.tsx:191-212`, `Tour.tsx:59-68`. Test: `TourViewModelTests.ClosesExactlyOnce`, `.FirstRunOnly`, `.ReplayDoesNotResetFlag`.

**INV-HOME-21. Tour bubble placement, caret and spotlight follow the 2.31 formulas exactly, with a centred bubble and full dim when the step has no anchor or the anchor is missing.** Citation: `Tour.tsx:101-133`. Test: `TourLayoutTests`.

**INV-HOME-22. The theme is always applied as a pair (appearance, brand); the active brand is the open project's recognised pin only while that project's view is on screen, otherwise the app brand; an unrecognised pin renders the app brand.** Why: #77 phase 1b, `e19bcc0` (Settings belongs to no project), #107. Citation: `App.tsx:365-385`, `theme.ts:57-70`. Test: `ActiveBrandResolverTests`, `ThemeManagerTests.AppliesBrandAndAppearanceTogether`.

**INV-HOME-23. Appearance `system` follows the OS app-mode setting live; `light` and `dark` ignore it; the resolved theme is in place before the first frame of the main window.** Citation: `theme.ts:46-79`, `main.tsx:6-10`. Test: `AppearanceResolverTests`, `ThemeManagerTests.FollowsSystemOnlyWhenSystem`, AC-HOME-21.

**INV-HOME-24. No XAML in `ShotAI.App` names a colour or a corner radius of its own, except the named, reasoned exceptions (the tour pill mock-up, the report click markers owned by 04 and 05, and the fixed notice and scrim colours kept in one named dictionary); a chromatic ARGB literal (channel spread above 40) is never allowed outside them; a stale exception fails.** Why: #77 phase 1 and 2, `e19bcc0` (the rgba blind spot). Citation: `app-chrome-tokens.test.ts:1-167`. Test: `XamlChromeGuardTests` (8.2).

**INV-HOME-25. Every theme key referenced from XAML exists in every (brand, appearance) dictionary, and every reference to a theme key is a `DynamicResource`, so a brand or appearance switch repaints the whole window without a restart and without losing view state.** Why: the macOS lesson (137 call sites read statics that registered no dependency; `.id(brand)` would have destroyed state), and the `var(--text)` dead read. Citation: `theme.ts:14-17`, `theme-palette.test.ts:122-130`, `macOS:shotAI/Theme.swift:16-36`. Test: `XamlResourceKeyGuardTests`, `ThemeManagerTests.SwitchKeepsState`.

**INV-HOME-26. Colour, radius and font values come only from 10's generated Core brand table (`BrandPalette`); the App defines none by hand.** Why: four hand copies had drifted (#77 phase 0). Citation: `theme-palette.ts:211-241`. Test: `ThemeTokenSetTests.EveryRoleFromBrandTable` (compares against the generated table, not literals).

**INV-HOME-27. Appearance and brand are two separate controls in Settings.** Why: every brand exists in light and dark (#77). Citation: `Settings.tsx:24-34`, `:794-846`. Test: `SettingsViewTests.AppearanceAndBrandSeparate` (the UIA names `Theme` and `Brand` are two radio groups).

**INV-HOME-28. Every Settings write goes through 10's coercion; the control then shows the stored value; natively every write is applied to the view model first and, on failure, rolled back with an error notice.** Why: the fixed write rule. Mechanism: 10's `ISettingsService.UpdateAsync` applies the change to `Current` at once, queues the atomic write, and on failure itself restores the last persisted snapshot (re-applying still-pending changes), raises `Changed` with `IsRollback = true` and faults the task (11 7.3.6); the view model shows `Current` on every `Changed` and shows the error, so it never keeps a private "previous value". Citation: `Settings.tsx:95-318`, `settings.ts:25-48`. Test: `SettingsViewModelTests.ShowsCoercedValue`, `.RollsBackWithNoticeOnFailure`.

**INV-HOME-29. `Include my name` is disabled while the name field trims to empty, and saving a blank name turns `includeNameInReports` off.** Citation: `Settings.tsx:229-242`, `:921`. Test: `SettingsViewModelTests.BlankNameDisablesAndClearsInclude`.

**INV-HOME-30. [SECURITY] The API key field is write-only: it is never pre-filled, never read back from storage, never logged, is a masked password field, and is cleared after a successful save.** Citation: `Settings.tsx:47-53`, `:320-334`, `:604-613`; `src/main/ipc.ts:773-779` (channel logged, never the value). Test: `SettingsViewModelTests.KeyFieldWriteOnly`, code review of log calls, AC-HOME-27.

**INV-HOME-31. [SECURITY] Links from this subsystem (update download page, Anthropic console, support URL) open only through `IExternalLinks.OpenAsync` (`ShotAI.Core.Links`; allowlist algorithm 11 7.3.4, registered by 10): `https` only; host `anthropic.com` or a subdomain, host exactly `github.com`, or the exact origin of the administrator-configured support URL; anything else is refused (`false`) and logged by origin only. `OpenAsync` throws only if the shell launcher throws; every caller in this subsystem wraps the call, logs a throw at Warning and shows nothing (R-ARCH-25).** Citation: `src/main/ipc.ts:271-310`, `App.tsx:70`. Test: 11's `Links.ExternalLinkPolicyTests`; here `UpdateNoticeTests.OpensThroughLauncher` and `.LauncherThrowShowsNothing`.

**INV-HOME-32. [SECURITY] The signed-in account (UPN) is shown only in Settings, AI, Microsoft sign-in, and never in a notice, a log line or an export.** Why: it is personal data (`src/shared/ipc.ts:69-71`). Test: code review; `SettingsViewModelTests.AccountOnlyInSettings`.

**INV-HOME-33. When federation is not configured the AI tab contains no Entra control or wording at all.** Why: #63 (a greyed-out SSO control in a public repo is a permanent support question). Citation: `Settings.tsx:381-386`. Test: `SettingsViewTests.UnconfiguredShowsNoEntra`.

**INV-HOME-34. Any Settings change (SOP patch, save or clear key, sign in or out) clears the last connection-test result; the result text follows the 2.25 formula.** Citation: `Settings.tsx:310-379`. Test: `SettingsTextTests.ConnectionMessage`, `SettingsViewModelTests.ChangeClearsTest`.

**INV-HOME-35. The update notice appears at most once per launch, only when a newer release exists, is not lost when the check finishes before the window exists, and a dismiss lasts for the session.** Why: #54, `037858d`. Citation: `App.tsx:53-72`, `update-state.ts`. Test: `UpdateNoticeTests.PendingBeforeUiIsShown`, `.PushAfterUiIsShown`, `.NotShownTwice`, `.DismissForSession`.

**INV-HOME-36. `Check now` reports `shotAI <v> is available.` and opens the release page, or `Couldn't check: <error>`, or `You're up to date.`, and runs even when automatic checks are off.** On a per-machine install the available case follows INV-HOME-45 instead. Citation: `Settings.tsx:104-122`. Test: `SettingsTextTests.UpdateCheckMessage`, `SettingsViewModelTests.CheckNowIgnoresToggle`.

**INV-HOME-37. Notices float over the content without moving it; there is at most one error notice (a newer error replaces it) and one update notice; the error text is the exception's message.** Citation: `notice.css:1-17`, `App.tsx:92-93`, `:544-566`. Test: `NoticeCenterTests`.

**INV-HOME-38. A confirmation is modal within the window, starts with the confirm button focused, cancels on Escape, resolves to exactly one boolean, and never leaves an earlier request unsettled.** Citation: `useConfirm.tsx:37-91`. Test: `ConfirmServiceTests`.

**INV-HOME-39. The Settings tab bar implements the roving pattern: arrows cycle with wrap, Home and End jump, focus follows selection, only the active tab is a Tab stop, and the panel is focusable.** Citation: `Settings.tsx:131-147`, `:403-435`. Test: `SettingsViewTests.TabKeyboard` (Windows).

**INV-HOME-40. The persisted screenshot quality is a multiple of 0.05 within [0.5, 1] rounded to two decimals, and the label is `round(scale * 100)%`.** Why: Chromium's range input serializes exact decimal steps; a WPF `Slider` does not. Citation: `Settings.tsx:750-768`, `settings.ts:31-35`. Test: `CaptureScaleStepsTests`, `SettingsTextTests` (`CaptureScaleLabel`).

**INV-HOME-41. [SECURITY] The remote-visibility switch defaults off and, when changed, is applied to the open windows immediately through 02 and 03's protection service, not only on the next launch.** Why: `53045c6` (a toggle that bites only on the next launch reads as broken; startup is fail-closed). Natively the view model only writes the setting; 11's `RemoteVisibilityApplier` applies every change, rollbacks included (ARCHITECTURE 9.2 S7). Citation: `src/main/ipc.ts:654-664` (the `setRemoteVisible` handler calls `applyRemoteVisibility`). Test: `SettingsViewModelTests.RemoteVisibleAppliesImmediately` (the real `RemoteVisibilityApplier` over a fake settings service and a fake window-protection seam), 11's `Settings.RemoteVisibilityApplierTests`.

**INV-HOME-42. [SECURITY] Opening Settings re-reads the auth status through 08's `IAuthService.GetStatusAsync`, which invalidates the cached federation policy first, so an administrator's policy change (enabling or removing federation, a new SupportUrl) is reflected by reopening Settings without restarting the app; natively Settings also re-reads on 08's `AuthStatusChanged`.** Why: `config.ts` once cached the policy for the process lifetime while the ADMX help text promised a reopen-Settings refresh (`src/main/ipc.ts:790-801`). Citation: `Settings.tsx:149-179`, `src/main/ipc.ts:788-815`; 08 INV-AUTH-9. Test: `SettingsViewModelTests.OpenReadsFreshAuthStatus` (a fake `IAuthService` counts `GetStatusAsync` per open), `.AuthStatusChangedRefreshes`.

**INV-HOME-43. [SECURITY] Every popup or popover this subsystem opens (row and bulk overflow menus, the target dropdown, the auto-archive drop-down, tooltips) carries the same capture exclusion as the main window: it is drawn in 03's in-window overlay layer, so the excluded main window covers it (the default of R-ARCH-19: the target dropdown, the confirm dialog, the notices, the tour), or, only where WPF needs a popup HWND, it is a 03 `ShotAIPopup` (the `OverflowMenu`, named by R-ARCH-19) or a popup of a WPF control (tooltips, context menus, the auto-archive `ComboBox`'s drop-down), and 03's `PopupExclusion` show hook registers each of them before it is shown (corrected in WP-A13, 03 Q-SHELL-3: the hook reaches a `ComboBox`'s templated drop-down, so no implicit style replaces the template's `Popup`).** Why: a WPF `Popup` is its own top-level HWND that `SetWindowDisplayAffinity` on the main window does not cover, so with remote visibility off a menu would show in a Teams share or a capture while the window behind it is excluded (03 EDGE-SHELL-40). Citation: 03 7.4.7, INV-SHELL-1; ARCHITECTURE 5.4. Test: 03 `AllWindowsRegisteredTests`; here `Chrome.OverflowMenuTests.PopupIsShotAIPopup`, `Settings.SettingsViewTests.ComboBoxDropDownIsExcluded` (the auto-archive drop-down excluded before it is visible, with 03's show probe), `Home.CaptureModePickerTests.DropdownIsOverlayElement`.

**INV-HOME-44. The Home, Settings, tour and notice code never waits synchronously on a service and reaches the UI thread from service events only through 11's `IUiDispatcher.Post`; it calls no `Dispatcher.Invoke`, `BeginInvoke` or `InvokeAsync` directly.** Why: 11 T6, T9 and INV-IPC-6 (a blocked UI thread deadlocks a cross-thread display-affinity or UIA call into our own window); the App's banned-symbol list enforces it. Citation: 11 7.6, 7.12. Test: 11's analyzers (`BannedSymbols.txt`, `VSTHRD002`) and `Threading.NoSyncWaitTests`.

**INV-HOME-45. On a per-machine install (12's `IInstallInfo.Scope` is `PerMachine`), the update notice reads `shotAI <version> is available. On this PC, IT or an administrator installs updates.` with no `Open the download page` button, and `Check now` shows that same text for an available update and opens no release page; a per-user or unpackaged build keeps INV-HOME-35 and INV-HOME-36 unchanged.** Why: a per-machine copy is updated by IT (Intune) or an administrator, and the MSI on the release page installs per-user by default, which 12 INV-PKG-37 refuses beside a per-machine copy, so the button would lead a standard user to an installer that says no (12 EDGE-PKG-65). IMPROVEMENT, new 2026-09-23 (D-HOME-35); resolves 10 Q-INFRA-5. Citation: 12 7.10.4, 7.4.5. Test: `UpdateNoticeTests.PerMachineHasNoDownloadAction`, `.PerUserKeepsDownloadAction`; `SettingsTextTests.UpdateCheckMessage` (per-machine cases); `SettingsViewModelTests.CheckNowPerMachineOpensNothing`; AC-HOME-41.

---

## 5. Edge cases and hard-won fixes

**EDGE-HOME-1. The week starts on Sunday whatever the user's locale.** `startOfWeek` subtracts `getDay()` (0 = Sunday); a user whose region starts weeks on Monday still sees Sunday boundaries. Required: parity; do NOT use `CultureInfo.DateTimeFormat.FirstDayOfWeek`. Source: `date-groups.ts:15-20`; macOS matches (`macOS:Packages/ShotModel/Sources/ShotModel/DateGroups.swift:17-22`).

**EDGE-HOME-2. Calendar arithmetic across month and year boundaries and DST.** `new Date(y, m, d - 7)` rolls into the previous month, `new Date(y, m - 1, 1)` in January is December of the previous year, and a local midnight that falls in a DST gap resolves forward to the first valid instant (an ambiguous one to the earlier instant). Required: compute boundary DATES with `DateOnly` arithmetic (`AddDays(-7)`, `AddMonths(-1)`), then convert each to an instant with the ECMAScript "compatible" disambiguation (7.2.1); never `TimeZoneInfo.ConvertTimeToUtc` on an invalid local time (it throws). Source: `date-groups.ts:22-41` ("DST/month-safe").

**EDGE-HOME-3. The list controls reset whenever Home is re-entered.** Tab, sort, direction, search, selection and rename live in the unmounted `ProjectList`, so coming back from a project or from Settings shows Projects, Modified descending, no search. The Home scroll offset is still restored (2.19), onto the reset list. Required: parity (Q-HOME-3 records the alternative). Source: `App.tsx:854-862`.

**EDGE-HOME-4. Refreshes overlap.** Mount runs three refreshes (the mount effect, the `showHome` effect and the `sopBackup` effect, which also fires on mount; StrictMode doubles them in development), the focus event and the tick can coincide, and there is no in-flight guard; the last to resolve wins, so an older listing can overwrite a newer one. Required natively: IMPROVEMENT, coalesce (one in flight, at most one queued) and apply a result only if it is the newest requested (generation counter); macOS coalesces the same way (`macOS:shotAI/AppModel.swift:97-111`). Source: `App.tsx:95-98`, `:186-189`, `:303-305`.

**EDGE-HOME-5. Search corner cases.** A whitespace-only query is not a search (no tiers, date groups stay). Typing into the search box clears a non-empty selection; the `✕` clear button does not (the visible set only grows). The empty-state line quotes the TRIMMED query inside `“ ”`. Required: parity. Source: `ProjectList.tsx:145-158`, `:470-494`, `:588`.

**EDGE-HOME-6. Escape precedence.** Escape in the search box with text clears the query and stops propagation; an open overflow menu closes on Escape AND the window-level selection-clear also runs; Escape in the confirm dialog cancels AND clears the selection; Escape in the rename box discards the rename AND clears the selection (the rename handler does not stop propagation, `ProjectList.tsx:362-365`); Escape while the tour is open finishes the tour AND clears the selection (both are window listeners). Required natively: IMPROVEMENT, Escape acts on the innermost open surface only (confirm, then tour, then an open popup, then rename, then search text, then the selection), marking the key handled. Justification: one key press should undo one thing; the Electron double effect is an artefact of independent window listeners. Source: `ProjectList.tsx:236-243`, `:362-365`, `:476-482`, `OverflowMenu.tsx:38-46`, `useConfirm.tsx:63-65`, `Tour.tsx:90-99`.

**EDGE-HOME-7. `buildTarget` falls back to Auto.** Window mode with no picked window, or Area with no area, yields `{mode: 'auto'}`. Home cannot reach it (Capture is disabled), but 05's Resume capturing calls `buildTarget()` directly and can start an Auto session from Window mode. Required: parity (05 EDGE-REP-37). Source: `App.tsx:146-168`, `:632-634`.

**EDGE-HOME-8. The selection is not pruned on refresh.** A selected project that disappears (deleted elsewhere, moved by auto-archive) stays in `selected`: `N selected` and the bulk-delete confirmation count it, but no operation touches it. Required natively: IMPROVEMENT, prune the selection to paths still present after every refresh (the confirmation then states the true count). Source: `ProjectList.tsx:245`, `:280-290`.

**EDGE-HOME-9. The periodic refresh is paused by a focused checkbox, not by the selection.** The typing test is `tagName === 'INPUT'`, which a checkbox satisfies, so the tick stops while a row checkbox has focus and resumes as soon as focus moves to a button, selection or not. Required natively: IMPROVEMENT, pause the tick while a text box has keyboard focus, a rename is open, the selection is non-empty, or an operation runs (the macOS rule plus the Electron typing rule), so the list never reorders under a pending bulk action. Source: `App.tsx:315-320`; `macOS:shotAI/HomeView.swift:159-171`.

**EDGE-HOME-10. Rename must commit at most once and Escape must never commit.** Enter commits and unmounts the input; Escape clears `renamingPath` and unmounts it. Whether the unmount then fires `blur` (and so `commitRename` from a stale closure) is browser-dependent and not guarded in source. Required natively: a rename session token; the first of Enter or focus loss commits and ends the session; Escape ends it without committing; any later focus loss is ignored. Source: `ProjectList.tsx:78-91`, `:356-367`.

**EDGE-HOME-11. The renamed row can vanish.** If the project being renamed disappears in a refresh (deleted in another window, auto-archived into the other tab), the input goes with it. Required natively: end the rename session without committing and without error; macOS reconciles the same way (`macOS:shotAI/HomeView.swift:162-168`). Source: `ProjectList.tsx:53-54`.

**EDGE-HOME-12. `Clear` during a bulk operation hides its progress.** `Clear` is never disabled; pressing it mid-run empties the selection, which hides the whole bar including `Exporting 2 of 5…`, while the run continues on its snapshot. Required natively: IMPROVEMENT, the bar stays visible while `bulkBusy` (showing progress), `Clear` is disabled while busy; macOS gives progress precedence over the bar (`macOS:shotAI/HomeView.swift:77-89`). Source: `ProjectList.tsx:519-581`.

**EDGE-HOME-13. The bulk-delete count.** The confirmation's `n` is `selected.size`, which can differ from the number actually deleted (EDGE-HOME-8). Required natively: with pruning, `n` is the number of selected visible projects, which is exactly what is deleted. Source: `ProjectList.tsx:280-291`.

**EDGE-HOME-14. Exporting from the Archive tab restores the project.** Row export and both bulk exports call `projects.open`, which auto-unarchives an archived project, so an export from the Archive tab moves it back to Projects on the next refresh. macOS deliberately offers bulk export on the Active tab only. Required: parity by default (Q-HOME-5). Source: `ProjectList.tsx:112-125`, `:298-302`, `src/main/project-store.ts:388-392`; `macOS:shotAI/HomeView.swift:494-497`.

**EDGE-HOME-15. The shared folder is revealed even when every export failed.** `revealExportDir` runs after the run unconditionally and opens the folder only if it still is a directory (errors swallowed). macOS reveals only when something was written. Required: parity. Source: `ProjectList.tsx:317-318`, `src/main/export.ts:918-925`.

**EDGE-HOME-16. Archived rows show the last-modified date labelled `archived`.** The meta line prints `archived <updatedAt>`; archiving does not bump `updatedAt` and `ProjectSummary` carries no `archivedAt`. Required: parity (both platforms). Source: `ProjectList.tsx:382-384`; `macOS:shotAI/HomeView.swift:642`.

**EDGE-HOME-17. Meta date formatting.** An empty `updatedAt` prints `\u2014`; a non-empty unparseable one prints `Invalid Date` (JavaScript). Required natively: `\u2014` for empty; IMPROVEMENT, `\u2014` also for unparseable (never the words `Invalid Date`). Source: `ProjectList.tsx:384`.

**EDGE-HOME-18. A failed capture start leaves the new project behind.** `onCreate` creates the project and then `onRecord`; if `capture.start` throws, the empty project stays in the list and in the store as open, the error shows, and nothing is rolled back. Required: parity (the user can delete it; 02 owns start failures). Source: `App.tsx:440-458`, `:466-470`.

**EDGE-HOME-19. Settings opened from inside a project keeps the project's scroll offset.** Only transitions into and out of Home move `scrollTop`. Required natively: IMPROVEMENT, Settings always opens at the top (it is its own view with its own scroller). Source: `App.tsx:282-289`.

**EDGE-HOME-20. Replaying the tour from Settings opened inside a project.** `replayTour` closes Settings and sets `tourOpen`, but the tour renders only on Home, so it appears later, when the user leaves the project. Required natively: IMPROVEMENT, replay closes Settings AND the open project (05 close, which auto-saves nothing pending because edits are already queued), then shows the tour on Home; macOS does exactly this (`macOS:shotAI/AppModel.swift:640-643`). Source: `App.tsx:208-212`.

**EDGE-HOME-21. Every refresh dismisses the error notice.** `refresh()` starts with `setError(null)`, so the 20 s tick or simply switching back to the window clears an error the user may not have read. Required natively: IMPROVEMENT, background refreshes (tick, activation, projects-changed) do not touch the notice; an error is cleared only by dismiss, by a newer error, or by the start of the next user-initiated operation. Source: `App.tsx:95-98`.

**EDGE-HOME-22. The notice stack scrolls away.** It is absolutely positioned inside the scroll container, so a notice raised while the list is scrolled down renders at the top of the content, out of view. Required natively: IMPROVEMENT, pin the stack to the top of the visible content area (an overlay outside the scroller). Source: `notice.css:1-17`, `project.css:154-160`.

**EDGE-HOME-23. A manual `Check now` never raises the Home notice.** Main updates its stash but does not push, and App pulls only on mount; the commit message's claim that a manual find "also raises the home notice" is not true in the running session. `Check now` instead opens the release page immediately. Required: parity (Q-HOME-8). Source: `src/main/ipc.ts:754-767`, `App.tsx:57-72`, `037858d`.

**EDGE-HOME-24. A failed open from Home is silent.** 05's store catches the error, clears `projectPath` and stores the message where only the (unmounted) detail view shows it; a "gone" error (`ENOENT`, `no such file`, `not found`) is deliberately not shown. Required natively: IMPROVEMENT, 05 raises `OpenFailed(message)` for a non-gone failure and Home shows it as an error notice with 01 Q-MODEL-11's wording `This project can't be opened because its project.json is missing or damaged.` Source: `src/renderer/project/store.ts:128-151`; 05 EDGE-REP-39.

**EDGE-HOME-25. Settings loads all or nothing.** One rejected read (for example `auth.status` failing) leaves the whole panel at `Loading…` with `Error: <message>`. Required natively: IMPROVEMENT, settings values come from the in-memory settings service (10, loaded at startup) and cannot fail; key and auth status load independently and a failure shows only in their own group. Source: `Settings.tsx:149-179`, `:399-401`.

**EDGE-HOME-26. The brand blurb is binary.** Every non-default brand gets the LFI blurb `LaCrosse Footwear corporate \u2014 charcoal and rust.`. Required: parity for the two shipping brands; natively the blurb is looked up per brand id with the same two strings (Q-HOME-11 for a third brand). Source: `Settings.tsx:27-34`.

**EDGE-HOME-27. Enter confirms a destructive dialog.** The confirm button has `autoFocus`, including for `Delete`, so Enter deletes. Required: parity (the danger styling and explicit wording are the protection); Q-HOME-6 records the alternative. Source: `useConfirm.tsx:75-82`.

**EDGE-HOME-28. Inconsistent optimism in Settings.** Theme and brand update the UI before the write and do not roll back on failure; the update toggle updates first and silently reverts on failure; SOP chips, switches, the slider value and the archive select update only after the write returns. Required natively: IMPROVEMENT, one rule for all (the fixed write rule): apply to the view model at once, persist through 10's queue, on failure restore the previous value AND show an error notice. Source: `Settings.tsx:95-103`, `:210-318`.

**EDGE-HOME-29. Changing the projects folder keeps the old recents.** Home keeps listing recent projects from the previous root (01 merges recents into the list). macOS clears recents on a folder change. Required: parity (01 owns recents; Q-HOME-9). Source: `Settings.tsx:288-298`, `src/main/project-store.ts:45-48`; `macOS:shotAI/AppModel.swift:648-666`.

**EDGE-HOME-30. `1 steps` in the recording panel.** The count has no singular form (unlike the row meta line). Required: parity. Source: `App.tsx:576`.

**EDGE-HOME-31. The target dropdown is sticky.** Targets load once and never refresh by themselves; a kept window pick keeps its OLD title and app; the popover has no Escape or arrow-key handling. Required: parity for the data (Refresh is manual); IMPROVEMENT for keyboard (Escape closes, arrows move, Enter picks). Source: `App.tsx:100-121`, `:728-816`.

**EDGE-HOME-32. Accessibility gaps.** Sort chips expose no pressed or selected state; the create name field and the custom-instructions textarea have only placeholders, no label; date-group headers are hidden from assistive tech; the confirm and tour dialogs do not trap focus. Required natively: IMPROVEMENT, every one fixed (7.13). Source: `ProjectList.tsx:496-516`, `:617-621`, `App.tsx:666-673`, `Settings.tsx:723-731`.

**EDGE-HOME-33. The tour's keyboard and lifetime.** Arrow keys are global while the tour is open (they also move a caret in a focused text field under the scrim); focus is not moved into the bubble; opening Settings with `Ctrl+,` unmounts the tour and it restarts at step 1 on return. Required natively: IMPROVEMENT, the tour takes keyboard focus (its Next button) and handles keys only inside its own focus scope; parity for the restart-at-step-1 rule. Source: `Tour.tsx:90-99`, `App.tsx:866`.

**EDGE-HOME-34. The tour closed twice.** `finish()` inside a StrictMode-double-invoked updater wrote `hasSeenTour` twice. Required: exactly-once close (INV-HOME-20). ELECTRON-ONLY cause. Source: `da727b8`, `Tour.tsx:59-68`.

**EDGE-HOME-35. The update result can arrive before the UI exists.** `webContents.send` does not buffer; the check was measured finishing 40 s before the renderer's first IPC in dev. Required natively: Home reads 10's pending stash when it loads AND subscribes to the event; whichever comes first shows the notice once (03 EDGE-SHELL-19). Source: `037858d`, `App.tsx:57-72`, `src/main/update-state.ts:1-25`.

**EDGE-HOME-36. A refused link fails silently.** `openExternal` returns `false` and logs the origin for a non-allowlisted URL; the v1.1.6 update download link failed exactly this way before `github.com` was allowlisted, and the support URL needed exact-origin matching (#63). Required: parity (refuse and log), plus a test that every URL this subsystem opens passes the allowlist (AC-HOME-28). Source: `src/main/ipc.ts:271-310`, `037858d`, `f1b24a8`.

**EDGE-HOME-37. An archive age that is not an option.** `settings.json` can hold any value in [1, 1825] (hand edit, an older build); the controlled `select` then matches no option and shows blank. Required natively: IMPROVEMENT, show an extra option `After N days` for the stored value so the control never lies. Source: `Settings.tsx:876-887`, `settings.ts:39-43`.

**EDGE-HOME-38. Slider steps are decimal in Chromium, binary in WPF.** A WPF `Slider` with `TickFrequency = 0.05` produces values such as `0.8500000000000001`; Chromium serializes `0.85`. Required: snap `v = (50 + 5 * JsMath.Round((v - 0.5) / 0.05)) / 100.0` before persisting (INV-HOME-40): the division of two exact integers is correctly rounded, so it yields the same double as parsing the decimal text (`0.85`), and it avoids `Math.Round`, which is banned in Core (ARCHITECTURE 14.9, Q-ARCH-5). Source: `Settings.tsx:750-768`.

**EDGE-HOME-39. Blur-persisted fields and navigation.** Custom instructions and the user name persist on blur only. Clicking `← Back` or another tab blurs first, so the edit is saved; a navigation that does not move focus (a menu accelerator) could drop it. Required natively: IMPROVEMENT, persist on `LostKeyboardFocus` AND when the Settings view unloads, whichever comes first, once. Source: `Settings.tsx:723-731`, `:900-908`.

**EDGE-HOME-40. Import while Settings is showing.** File then Import Project (`Ctrl+O`) works from Settings; after a successful import the project opens but `showSettings` is still true, so Settings stays on screen over it. Required natively: IMPROVEMENT, a successful import (or Empty Project, or any open) closes Settings. Source: `App.tsx:224-235`, `:480-496`, `:630`.

**EDGE-HOME-41. The key status sentence is a concatenation.** For example `source === 'none'` with unreadable ciphertext and no encryption renders two sentences each starting with a space. Required: parity, byte for byte (7.4 `SettingsText.KeyStatus`). Source: `Settings.tsx:588-603`.

**EDGE-HOME-42. First-paint colours.** The token sheet installs before the first render, but the preference loads asynchronously, so a dark-mode or LFI user sees the bare `:root` (default brand, light) for the first frames. Required natively: IMPROVEMENT, 10 loads `settings.json` synchronously at startup and the theme is applied before `MainWindow.Show()`. Source: `main.tsx:6-10`, `App.tsx:361-364`, `theme-palette.ts:422-425`.

**EDGE-HOME-43. A tour anchor can be off screen.** With the Home list scrolled down (restored scroll), step 1's hero is above the viewport; the spotlight and bubble are positioned at negative coordinates. Required natively: IMPROVEMENT, bring the anchor into view (`FrameworkElement.BringIntoView`) before measuring each step. Source: `Tour.tsx:71-88`, `:107-117`.

**EDGE-HOME-44. Home order is sensitive to no-op writes.** The list sorts by `updatedAt`, and any write that bumps it (01) moves a project to the top of `This Week`; the #70 review found a slider focus re-dating projects. Required: this subsystem writes a project only when something changed (rename guard, INV-HOME-14; archive and unarchive do not bump). Source: `62b4b7d`, `ProjectList.tsx:84`.

**EDGE-HOME-45. High contrast.** Chromium applies Windows high-contrast (forced colours) to the page automatically; the app has no rule of its own. WPF does not: custom brushes ignore high contrast. Required natively: IMPROVEMENT, when `SystemParameters.HighContrast` is true, the theme manager maps the tokens to system colours (Q-HOME-12). Source: absence of any `forced-colors` rule in `project.css`.

**EDGE-HOME-46. Emoji glyphs.** Several labels start with emoji (`🗄 Archive`, `🗑 Delete`, the empty-state icons). WPF renders colour emoji as monochrome outlines (no colour-font support). Required: keep the strings; accept monochrome glyphs (Q-HOME-13). Source: `ProjectList.tsx:542`, `:571`, `:586`, `:597`.

**EDGE-HOME-47. Import is offered in two places with different reach.** The Home button exists only on the Active tab; File then Import Project works on any tab, in a project and in Settings (not while recording). Required: parity. Source: `ProjectList.tsx:452-461`, `App.tsx:228-230`.

**EDGE-HOME-48. Settings opened from Home is still "Home" to the refresh logic.** `showHome` ignores `showSettings`, so while Settings is open from Home the window-focus refresh and the 20 s tick keep re-listing (and each refresh clears the App error notice) behind Settings, and closing Settings neither refreshes nor restarts the interval, although the list (a freshly mounted `ProjectList`) is shown again. Required natively: IMPROVEMENT (D-HOME-28), Settings counts as leaving Home: the tick and the activation refresh stop while Settings shows, and returning to Home runs `OnEnter` (reset, refresh, timer from zero). Justification: nothing is visible to refresh, and the return refresh covers anything that changed. Source: `App.tsx:264`, `:303-327`.

**EDGE-HOME-49. A failed listing shows an error notice from every trigger.** Every refresh trigger except the projects-folder change ends in `.catch(fail)`, so a listing failure (for example the projects folder on an unplugged drive) raises the error notice again on every tick, activation and projects-changed signal; the folder-change refresh (`() => void refresh()`) has no catch and fails silently as an unhandled rejection. Required natively: IMPROVEMENT (D-HOME-29), a failed background refresh is logged at warning and shows nothing; a failed user-initiated refresh (after a mutation, create, import, or the folder change) shows the error notice. Source: `App.tsx:188`, `:304`, `:314`, `:319`, `:337`, `:355`, `:647`.

**EDGE-HOME-50. A bulk run whose final refresh fails.** `runBulk` awaits `onChanged()` inside the outer `try` without a `catch`: a throw skips `clearSelection()`, is not reported (unhandled rejection) and, for the shared-folder export, skips the final reveal. Required natively: IMPROVEMENT (D-HOME-33), the final refresh is user-initiated (its failure shows the error notice), and the selection is cleared and the shared folder revealed regardless. Source: `ProjectList.tsx:263-277`, `:313-318`.

**EDGE-HOME-51. A selection with nothing visible.** When every selected path has filtered out or vanished (EDGE-HOME-8), `bulkExportToFolder` still shows the folder dialog and then reveals the chosen folder with nothing written, and `bulkDelete` still asks `Delete N projects?` and then deletes nothing. Required natively: pruning (D-HOME-5) and the search-edit clear make the state unreachable in normal use; additionally every bulk command returns before any dialog when `Selection.SelectedVisible(view.Sorted)` is empty (IMPROVEMENT). Source: `ProjectList.tsx:245`, `:259-260`, `:280-291`, `:310-318`.

**EDGE-HOME-52. The confirm dialog's Escape depends on focus.** The Escape handler is the overlay's `onKeyDown`, which only sees keys while focus is inside the dialog; a click on the dim area moves focus to the document body, after which Escape does not cancel (and clears the Home selection instead). Required natively: the `ConfirmHost` owns a focus scope and handles Escape in `PreviewKeyDown` of its root regardless of which element inside has focus; a click on the scrim returns focus to the confirm button (IMPROVEMENT, part of D-HOME-13). Source: `useConfirm.tsx:63-66`.

**EDGE-HOME-53. Text fields use the browser's field colours, not a token.** No rule sets `background` or `color` on `project__input`, the textarea or the `select`, and `--field-bg` is read nowhere, so fields are white in light mode and the Chromium UA dark field grey in dark mode for every brand. Required natively: IMPROVEMENT (D-HOME-30), `TextBox`, `PasswordBox` and `ComboBox` use `Brush.field-bg` for the background and `Brush.ink` for the text (the light value of `--field-bg` is `#ffffff` for both brands, so light mode is unchanged). Source: `project.css:220-231`, `:2345-2359`; `src/shared/theme-palette.ts:353`.

**EDGE-HOME-54. One connection-test result, two places.** With federation configured and the key fallback open, `● Connected` or `● Error` and the failure text render in the Microsoft sign-in group AND in the API key group, and the `request access` hint shows for a failed key test too. Required: parity (the result is shared; the leg label in the message says which leg failed). Source: `Settings.tsx:485-552`, `:615-654`.

**EDGE-HOME-55. No-op settings writes.** The custom-instructions and name fields persist on every blur even when unchanged, and the slider persists on every `keyup` (including the Tab key-up that lands focus on it), so tabbing through Settings rewrites `settings.json` several times; the custom-instructions blur also clears a shown connection-test result. Required natively: IMPROVEMENT (D-HOME-31), a control persists only when its value differs from `ISettingsService.Current`, and the test result is cleared only by a real write (or sign-in, sign-out, key save or clear). Source: `Settings.tsx:730`, `:760-765`, `:907`, `:310-318`.

**EDGE-HOME-56. The tour spotlight goes stale after a layout shift.** The anchor is measured on step change, window `resize` and `scroll` only; a layout change without either (the monitor list loading into the dropdown, the Auto warning or the capture warning appearing, a notice) leaves the spotlight and bubble at the old rectangle. Required natively: IMPROVEMENT (D-HOME-32), the overlay also re-measures on the anchor's `LayoutUpdated` (coalesced to one measure per layout pass). Source: `Tour.tsx:71-88`.

**EDGE-HOME-57. The capture-mode choice outlives Home.** Mode, loaded targets, picks and even an open dropdown are App state, so they survive opening a project or Settings and are what 05's Resume capturing uses (EDGE-HOME-7); only a relaunch resets them to Screen with the primary monitor. Required: parity; natively `CaptureModePickerViewModel` lives as long as the shell (a singleton), never recreated by `OnEnter`, and its dropdown closes when Home is left (IMPROVEMENT, a popup must not float over another view). Source: `App.tsx:81-90`.

**EDGE-HOME-58. Week start when today's midnight does not exist.** `startOfWeek` builds today's local midnight first and then moves the date back with `setDate`, which keeps the wall-clock time of that first result; if today's midnight falls in a DST gap (zones whose transition is at 00:00), the first result is 01:00 and the week then starts at Sunday 01:00, not 00:00, so an item stamped Sunday 00:00 to 00:59 falls into Last Week. Required natively: IMPROVEMENT (D-HOME-34), `LocalMidnight(weekStart)` of 7.2.1 (the week always starts at the resolved Sunday midnight). Source: `date-groups.ts:16-20`.

---

## 6. macOS port notes

| Topic | macOS implementation | Divergence from Windows and why | Lesson for the Windows port |
|---|---|---|---|
| Home layout | One `HomeView` (`macOS:shotAI/HomeView.swift:12-157`): banner bar, hero card (only on the Active tab), tab chips, list head, bulk bar or bulk progress, list; 760 DIP max column | Hero hidden on the Archive tab; tabs are chips `Projects N` / `Archive N`; banner has a wordmark and tagline `Turn any process into a step-by-step guide` | Keep Windows' layout (hero on both tabs, underline tabs, header). The column cap is a good idea but not parity (Q-HOME-3 does not change it). |
| Refresh | `.task` on appear, `scenePhase == .active`, and a 20 s `Timer.publish` held in `@State` so re-renders do not reset it (`:54-67`, `:143-171`); `autoRefresh` coalesces with an in-flight flag and never runs while a project is open (`macOS:shotAI/AppModel.swift:97-111`); `refresh` publishes only when the list changed (`:82-95`) | Poll paused while renaming or while anything is selected, not while typing in search | Adopt coalescing, change detection and the pause-while-selected rule (7.6). A timer that is recreated on every render never fires: use one `DispatcherTimer` owned by the view model. |
| Selection | Always-visible checkbox per card, shift-click range over `visibleOrder` (`:532-553`), `Select all` disabled when all visible are selected, `Clear` is `.cancelAction` (Escape) | Bulk actions snapshot and clear the selection BEFORE running (archive, restore, delete); export clears only after the destination chooser is confirmed (`:511-518`) | Snapshot is shared; keep Windows' "clear after completion" for parity, but keep the bar visible for progress (EDGE-HOME-12). |
| Bulk export | One menu `Export` with HTML, PDF, Markdown, HTML for Word; a chooser asks per-project folders or one folder; progress `Exporting N of M…` with a linear bar (`:469-518`; `AppModel.swift:1030-1085`); failures counted into one message `N of M exports failed.` | Offered on the Active tab only, because exporting an archived project restores it (`:494-497`); reveal only if something was written | Windows keeps its two-group menu (parity). The single aggregated failure message is better than Electron's last-error-wins; recorded as Q-HOME-7. |
| Per-row busy | `busyPaths: Set<String>`; only the busy row is disabled (`:48-50`, `:567-575`) | Windows disables every row while one works | Keep Windows' single-operation rule (parity; it also serialises dialogs). |
| Search | `localizedStandardContains` over `searchText` only; title hits not tiered; `⌘F` focuses the field; a search change commits an open rename (`:111-115`, `:804-813`) | Windows tiers title matches first (01 2.9.6) | Keep Windows' tiers. Add `Ctrl+F` to focus search (IMPROVEMENT, 7.9). Commit an open rename before the list changes under it. |
| Date groups | `DateGroups.bucket` with `Calendar.current`, Sunday start computed from `weekday - 1` (`macOS:Packages/ShotModel/Sources/ShotModel/DateGroups.swift:9-53`) | Same algorithm; unparseable dates become `.distantPast` (Older) | Port the same pure function; take the zone as a parameter so tests are deterministic. |
| Settings | A separate native Settings window with General, AI, Capture, Appearance, Permissions tabs (`macOS:shotAI/SettingsView.swift:10-26`); `Form` bindings persist on every change (`onChange(of: model.preferences)`) | Different tab set and wording; API key under an `Advanced` disclosure; archive options include 60 days; per-change persistence (no blur) | Windows keeps its in-window Settings, tab order and strings (parity). Borrow persist-on-change semantics only where Electron persists on blur and navigation could drop an edit (EDGE-HOME-39). |
| Account rows | States unavailable, misconfigured (names missing fields, never values), signed out, signed in with or without entitlement (`:182-238`) | Richer than Windows | Out of scope for parity; 08 (auth, R-ARCH-14) may adopt the misconfigured row. |
| Updates | `UpdateModel` with persisted state, skip version, dismissed-for-launch badge, status line with relative dates, MDM kill switch (`macOS:shotAI/UpdateModel.swift:17-214`); the badge lives in the Home banner, Home only | Windows shows a floating info notice in every view; no skip | Keep Windows' notice (parity); the pull-on-load plus event design is the same idea as `loadPersistedState`. |
| Tour | Anchors publish frames through a `PreferenceKey` in a named coordinate space so the spotlight resolves through the ScrollView (`macOS:shotAI/Tour.swift:10-43`); the bubble height is MEASURED (`:245-250`) and the placement keeps it on screen (`:235-243`); steps 4 and 5 centred; step 5 text branches on federation; Esc via `.cancelAction`; Home controls disabled while the tour is up | Windows anchors step 5 to the Settings button and does not branch the copy | Keep Windows' five steps and copy (parity). Resolve anchor bounds relative to the overlay root (7.8), and measure the bubble instead of guessing 220 only if it does not change placement for the shipped layout (Q-HOME-10). |
| Theme | `PaletteTokens` struct in the SwiftUI environment; colours are dynamic `NSColor` providers for appearance; brand selects the struct (`macOS:shotAI/Theme.swift:1-146`); `@Environment(\.palette)` in every body registers the dependency; 137 call sites moved | Reading a `static` registered no dependency and a brand flip repainted nothing, and did so only partially (views re-rendering for other reasons picked up the new colour) | WPF has the same trap: `StaticResource` resolves once. Every token reference must be `DynamicResource` and the dictionary is swapped as a whole (INV-HOME-25). Do not rebuild the window to repaint (it destroys state, the `.id(brand)` warning). |
| Radii | `chipShape` returns `Capsule()` for the default brand (null radius) and a rounded rectangle for LFI (`macOS:shotAI/Radii.swift:12-26`) | Same model | WPF `CornerRadius` cannot express "capsule": compute it from the element's height (7.5). |
| Type | `wordmark`, `heroTitle`, `sectionTitle`, `cardTitle`, `eyebrow` (condensed) on the palette (`macOS:shotAI/Style.swift:1-20`) | Different scale | Use Windows' `--fs-*` tokens (parity). |
| Card elevation | `cardShadow` is brand-tinted and alpha-carrying (`macOS:shotAI/Style.swift:22-48`) | Windows shares neutral shadows across brands (recorded divergence) | Keep Windows' neutral shadows. |

---

## 7. Native design (C#)

### 7.1 Placement

| Project | Namespace | Types |
|---|---|---|
| ShotAI.Core | `ShotAI.Core.Home` | `HomeTab`, `HomeSortKey`, `DateBucket`, `DateGroups`, `DateGroup<T>`, `HomeListQuery`, `HomeListGroup`, `HomeListView`, `HomeListPipeline`, `HomeSelection`, `RenameSession`, `RenameCommit`, `BulkProgress`, `BulkRunner`, `BulkOutcome`, `AutoRefreshPolicy`, `CaptureReadiness`, `HomeText` |
| ShotAI.Core | `ShotAI.Core.Tour` | `TourAnchorId`, `TourStep`, `TourSteps`, `LayoutRect`, `TourPlacement`, `TourLayout` |
| ShotAI.Core | `ShotAI.Core.SettingsUi` | `SettingsText` (every Settings string and formatter), `ArchiveAgeOption`, `ArchiveAgeOptions`, `CaptureScaleSteps` |
| ShotAI.Core | `ShotAI.Core.Theme` | `Appearance`, `AppearanceResolver`, `ActiveBrandResolver`, `Rgb`, `ColorMix`, `ChromeTokens`, `ThemeTokenSet`, `ThemeTokenKeys`, `ISystemAppearance` (the Platform seam, declared in Core per INV-ARCH-1 and ARCHITECTURE 2.3; the brand palette itself is 10's `BrandPalette`, generated) |
| ShotAI.Platform | `ShotAI.Platform.Theme` | `SystemAppearanceMonitor` (`internal sealed`, registered only as `ISystemAppearance` by `AddShotAIPlatform`, INV-ARCH-4) |
| ShotAI.App | `ShotAI.App.Shell` | `ShellViewModel`, `ShellView`, `NavigationState` (implements 03's `IShellNavigationState`), `ScrollMemory` |
| ShotAI.App | `ShotAI.App.Home` | `HomeView`, `HomeViewModel`, `ProjectRowViewModel`, `GroupHeaderItem`, `CreateHeroViewModel`, `ICaptureTargetSelection` (an App interface, accepted by R-ARCH-26), `CaptureModePickerViewModel` (implements `ICaptureTargetSelection`, consumed by 05 for Resume capturing), `TargetDropdownView` (the overlay-layer popover of 7.6), `BulkBarViewModel`, `RecordingPanelViewModel`, `HomeExportFlow` |
| ShotAI.App | `ShotAI.App.Settings` | `SettingsView`, `SettingsViewModel`, `AiSettingsViewModel`, `CaptureSettingsViewModel`, `AppearanceSettingsViewModel`, `StorageSettingsViewModel`, `AboutSettingsViewModel` |
| ShotAI.App | `ShotAI.App.Tour` | `TourOverlay`, `TourViewModel`, `TourAnchor` (attached property) |
| ShotAI.App | `ShotAI.App.Chrome` | `INoticeService`, `NoticeCenter`, `NoticeHost`, `IConfirmService`, `ConfirmService`, `ConfirmHost`, `OverflowMenu` (control), `MenuItemModel`, `ThemeManager`, `ThemeResources`, `CapsuleCornerConverter`, `UpperCaseConverter`, `Themes/Controls.xaml` (styles), `Themes/FixedColors.xaml` (the named non-token colours), `BundledFonts` (internal: the `Fonts\` and `Fonts\static\` folders and the width families, added in WP-A14) |

As built in WP-A16: `ShotAI.Core.Home` also has `ListSync`, `ListEdit<T>` and `ListEditKind` (the keyed diff of 7.6), and `ShotAI.Core.Geometry` has `FlexWrap`, `FlexItem` and `FlexSlot` (the wrapping list head of 2.8); `ShotAI.App.Chrome` also has `NoticeKind`, `NoticeViewModel`, `LiveRegion` (the announcement of 7.10 and 7.13) and `FlexWrapPanel`, and `ShotAI.App.Shell` has `ShellViewKind`.

Core stays free of Windows APIs: everything above in Core is pure, takes `TimeZoneInfo`, `CultureInfo`, `CompareInfo` and `DateTimeOffset now` as parameters, references no App or Platform type (INV-ARCH-1, INV-ARCH-2; for example `SettingsText.AppInfoLine` takes the `AppInfo` fields, not 11's App record), calls no `Math.Round` (ARCHITECTURE 14.9), and is tested on Linux. Every view model above derives from the App's `ViewModelBase : ObservableObject` (ARCHITECTURE 5.1), depends only on catalog interfaces, this subsystem's chrome services, the registered `TimeProvider` (added to INV-ARCH-3 in WP-A16, for `HomeViewModel`), `ILogger<T>`, value types and other view models (INV-ARCH-3), and is transient except `CaptureModePickerViewModel` (a singleton, EDGE-HOME-57, ARCHITECTURE 4.3). This subsystem stores no file of its own; nothing here lives under `%LOCALAPPDATA%\LFI\shotAI` (R-ARCH-13), and `settings.json`, the logs and the projects folder stay where the Electron build keeps them (ARCHITECTURE 10.1).

### 7.2 Core: list pipeline

```csharp
namespace ShotAI.Core.Home;

public enum HomeTab { Active, Archive }
public enum HomeSortKey { Name, Created, Modified }
public enum DateBucket { ThisWeek, LastWeek, ThisMonth, LastMonth, Older }   // canonical order

public sealed record DateGroup<T>(DateBucket Bucket, IReadOnlyList<T> Items);

public static class DateGroups
{
    public static IReadOnlyList<DateBucket> CanonicalOrder { get; } =
        [DateBucket.ThisWeek, DateBucket.LastWeek, DateBucket.ThisMonth, DateBucket.LastMonth, DateBucket.Older];

    public static string Label(DateBucket b) => b switch
    {
        DateBucket.ThisWeek => "This Week", DateBucket.LastWeek => "Last Week",
        DateBucket.ThisMonth => "This Month", DateBucket.LastMonth => "Last Month", _ => "Older",
    };

    /// null instant = not finite = Older.
    public static DateBucket BucketFor(DateTimeOffset? instant, DateTimeOffset now, TimeZoneInfo zone);

    public static IReadOnlyList<DateGroup<T>> Group<T>(IReadOnlyList<T> items,
        Func<T, DateTimeOffset?> instant, DateTimeOffset now, TimeZoneInfo zone);
}
```

**7.2.1 Boundaries.** `today = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(now, zone).DateTime)`; `weekStart = today.AddDays(-(int)today.DayOfWeek)` (`DayOfWeek.Sunday == 0`); `lastWeekStart = weekStart.AddDays(-7)`; `monthStart = new DateOnly(today.Year, today.Month, 1)`; `lastMonthStart = monthStart.AddMonths(-1)`. Each boundary instant is `LocalMidnight(date, zone)`, the ECMAScript "compatible" resolution of a local wall time `L = date.ToDateTime(TimeOnly.MinValue, DateTimeKind.Unspecified)`:

```
if zone.IsInvalidTime(L):          // DST gap: interpret L with the offset in effect BEFORE the transition,
                                   // which yields the later instant (lands after the gap by L - gapStart)
    offsetBefore = zone.GetUtcOffset(DateTime.SpecifyKind(L.AddHours(-24) - zone.BaseUtcOffset, DateTimeKind.Utc))
    instant      = new DateTimeOffset(DateTime.SpecifyKind(L - offsetBefore, DateTimeKind.Utc))
elif zone.IsAmbiguousTime(L):      // repeated hour: the EARLIER instant, i.e. the larger offset
    instant      = new DateTimeOffset(L, zone.GetAmbiguousTimeOffsets(L).Max())
else:
    instant      = new DateTimeOffset(L, zone.GetUtcOffset(L))
```

The gap branch assumes no second transition within the 24 hours before `L`, which holds for every zone in the IANA and Windows databases. `TimeZoneInfo.ConvertTimeToUtc(L, zone)` must not be used: it throws for an invalid time. Comparisons are on `DateTimeOffset.UtcTicks` (`ts >= boundary`). REQUIRED.

As built in WP-A16: `DateGroups.LocalMidnight(date, zone)` is 01's `IsoTime.FromLocalTime`, the resolution `TryParseJsDate` already applies to a date-time with no offset. In a gap it reads the offset before the transition 3 hours before `L` instead of 24, which gives the same instant `L - offsetBefore`, since no zone has two transitions within 3 hours. Each boundary is the resolved midnight of its own date, so a skipped midnight today does not move the start of the week (D-HOME-34, `DateGroupsDstTests`).

**7.2.2 Timestamps.** `Instant(p) = IsoTime.TryParseJsDate(sortKey == Created ? p.CreatedAt : p.UpdatedAt, zone, out var t) ? t : null` (01; ECMAScript ISO subset; date-only forms are UTC; date-time without offset is local). IMPROVEMENT D-17 of 01 applies (legacy V8 formats are not accepted and fall into Older).

```csharp
public sealed record HomeListQuery(HomeTab Tab, string Query, HomeSortKey SortKey, bool Ascending);
public sealed record HomeListGroup(string Label, IReadOnlyList<ProjectSummary> Items);   // Label "" = no header
public sealed record HomeListView(
    IReadOnlyList<HomeListGroup> Groups,
    IReadOnlyList<string> VisibleOrder,          // paths, render order
    IReadOnlyList<ProjectSummary> Sorted,        // searched + sorted, for allSelected / selectedVisible
    int ActiveCount, int ArchiveCount, bool Searching, string TrimmedQuery);

public static class HomeListPipeline
{
    public static HomeListView Build(IReadOnlyList<ProjectSummary> all, HomeListQuery q,
        DateTimeOffset now, TimeZoneInfo zone, CompareInfo collation);
}
```

`Build` implements 2.9 to 2.11 exactly: tab filter; 01's `ProjectSearch` for the trimmed lowercase query, title and content matching and the two tiers (if 01's final signature differs, 2.9.6 of 01 is the contract); the stable sort; tiers or date groups or one flat group; `VisibleOrder`; counts over `all`.

**Sort comparers** (REQUIRED equivalence):

| Key | C# |
|---|---|
| Name | `Math.Sign(collation.Compare(a.Title, b.Title, CompareOptions.IgnoreCase \| CompareOptions.IgnoreNonSpace \| CompareOptions.IgnoreKanaType \| CompareOptions.IgnoreWidth))` with `collation = CultureInfo.CurrentCulture.CompareInfo` (ICU on Windows 10 1903+, which is what Chromium's `localeCompare` uses; `sensitivity: 'base'` is ICU primary strength). The app must NOT set `InvariantGlobalization`. |
| Created / Modified | `Math.Sign(string.CompareOrdinal(a.CreatedAt, b.CreatedAt))` (resp. `UpdatedAt`). Identical to `localeCompare` for every value the stores write (`YYYY-MM-DDTHH:mm:ss.sssZ`, or `''`), because the first difference is always digit against digit or a prefix. |

Stability: `searched.OrderBy(p => p, Comparer<ProjectSummary>.Create((a, b) => asc ? cmp(a, b) : -cmp(a, b)))` (LINQ `OrderBy` is a stable sort; never `List<T>.Sort` or `Array.Sort`, which are not). `Math.Sign` before negating avoids `-int.MinValue`. REQUIRED.

**Date grouping in `Build`**: `DateGroups.Group(sorted, Instant, now, zone)`; if `q.Ascending`, reverse the list of groups (not the items). Labels come from `DateGroups.Label`; the view upper-cases them for display with `ToUpper(CultureInfo.CurrentCulture)` (CSS `text-transform: uppercase`).

As built in WP-A16: the name sort gives one flat group labelled `""`, even when it is empty, as Electron's does, and a group labelled `""` has no header. The name comparer is 01's `ProjectSearch.ByTitle(CompareInfo)` (added in WP-A16), given `CultureInfo.CurrentCulture.CompareInfo` by the view model (Q-HOME-4). `UpperCaseConverter` upper-cases with the binding's culture, the element's `Language` (en-US unless set), not `CurrentCulture`; the labels are English, so the two agree.

### 7.3 Core: selection, rename, bulk, refresh policy, readiness

```csharp
public sealed class HomeSelection
{
    public IReadOnlySet<string> Selected { get; }           // StringComparer.Ordinal
    public string? Anchor { get; }
    public int Count { get; }
    public void Toggle(string path);                        // flip, Anchor = path
    public void ShiftClick(string path, IReadOnlyList<string> visibleOrder); // 2.15 range or fallback toggle
    public void SelectAll(IReadOnlyList<string> visibleOrder);              // Anchor unchanged
    public void Clear();                                    // also Anchor = null
    public bool AllSelected(IReadOnlyList<ProjectSummary> sorted);          // sorted.Count > 0 && all in
    public IReadOnlyList<ProjectSummary> SelectedVisible(IReadOnlyList<ProjectSummary> sorted);
    public void Prune(IReadOnlySet<string> presentPaths);   // IMPROVEMENT, EDGE-HOME-8; Anchor cleared if pruned
    public event EventHandler? Changed;
}

public sealed record RenameCommit(string Path, string Title);
public sealed class RenameSession
{
    public string? Path { get; }  public string Value { get; set; }  public bool IsOpen { get; }
    public void Begin(string path, string currentTitle);    // ends any open session by committing it first (blur rule)
    public RenameCommit? Commit(Func<string, string?> currentTitleOf);   // at most once per Begin; null = nothing to write
    public void Cancel();                                   // Escape; never commits
    public void Abandon();                                  // row vanished (EDGE-HOME-11)
}
```

`Commit`: if not open return null; close; `next = JsString.Trim(Value)`; `orig = currentTitleOf(Path)`; return `next == "" || next == orig ? null : new RenameCommit(Path, next)` (ordinal equality, as JavaScript `===`).

```csharp
public sealed record BulkProgress(string Verb, int Done, int Total);
public sealed record BulkOutcome(int Total, int Failed);

public static class BulkRunner
{
    /// 2.16 runBulk. op failures go to onError and do not stop the loop; cancellation stops before the next item.
    public static Task<BulkOutcome> RunAsync(IReadOnlyList<ProjectSummary> targets, string verb,
        Func<ProjectSummary, CancellationToken, Task> op, IProgress<BulkProgress> progress,
        Action<Exception> onError, CancellationToken ct);
}

public static class AutoRefreshPolicy
{
    public static readonly TimeSpan Interval = TimeSpan.FromMilliseconds(20_000);
    public static readonly TimeSpan RegroupInterval = TimeSpan.FromMinutes(1);   // added in WP-A16, D-HOME-25
    public static bool ShouldTick(bool homeVisible, bool textInputFocused, bool renaming,
                                  bool selectionNonEmpty, bool anyBusy)
        => homeVisible && !textInputFocused && !renaming && !selectionNonEmpty && !anyBusy;
}

public static class CaptureReadiness
{
    public static bool IsReady(CaptureMode mode, bool hasWindow, bool hasArea)
        => mode == CaptureMode.Window ? hasWindow : mode == CaptureMode.Area ? hasArea : true;
    public static CaptureTarget BuildTarget(CaptureMode mode, WindowInfo? window, uint? monitorId, Rect? area); // 2.4 table
}
```

`CaptureMode`, `CaptureTarget`, `WindowInfo`, `MonitorInfo`, `Rect` are 01 and 02's Core types. A monitor id is a `uint` everywhere (R-ARCH-22): `MonitorInfo.Id` is `(uint)HMONITOR` (02 Q-CAP-11), the picker's `PickedMonitorId` is `uint?`, the manifest's `captureSettings.monitorId` JSON number converts at 01's codec edge, and an id is never compared across launches (the picker state is not persisted, EDGE-HOME-57).

`BulkRunner` is Core, so its awaits use `ConfigureAwait(false)` (ARCHITECTURE T2): `onError` may run on a pool thread. The caller passes a callback that marshals (`e => ui.Post(() => notices.ShowError(e))`, T6), and `progress` is a `Progress<BulkProgress>` the view model creates on the UI thread (T8); the task's continuation in the App resumes on the UI thread (T4).

`HomeText` (every Home string and formatter, so tests pin them on Linux):

| Member | Returns |
|---|---|
| `StepsMeta(int n, HomeTab tab, string updatedAt, CultureInfo c, TimeZoneInfo z)` | `` `${n} step${n == 1 ? "" : "s"} · ${tab == Archive ? "archived" : "modified"} ${date}` `` with `date = updatedAt == "" \|\| !TryParseJsDate ? "\u2014" : ToLocal(t).ToString("d", c)` (IMPROVEMENT for unparseable, EDGE-HOME-17; Q-HOME-4 for the culture) |
| `Working` | `Working…` |
| `BulkCount(int n)` | `` `${n} selected` `` |
| `BulkProgress(BulkProgress p)` | `` `${p.Verb} ${p.Done} of ${p.Total}…` `` |
| `DeleteOne(string title)` | `` `Delete "${title}"? This removes the project folder and its screenshots.` `` |
| `DeleteMany(int n)`, `DeleteManyLabel(int n)` | `` `Delete ${n} project${n == 1 ? "" : "s"}? This removes each project folder and its screenshots.` ``, `` `Delete ${n}` `` |
| `WindowLabel(WindowInfo? w, bool loading)` | 2.4 |
| `MonitorLabel(MonitorInfo? m, bool loading)`, `MonitorItemDetail(m)` | 2.4 |
| `AreaLabel(Rect a)` | `` `${a.Width} × ${a.Height}px @ (${a.X}, ${a.Y})` `` with JavaScript number-to-string formatting (`JsNumber.ToString`, 01) |
| `NoMatches(string trimmedQuery)`, `NoMatchesSub(HomeTab)` | 2.17 |
| `RecordingLabel(CaptureState s)`, `RecordingCount(int n)` | `` `${Paused ? "Paused" : "Capturing"} · ${title}` ``, `` `${n} steps` `` |
| `CaptureError(string m)` | `` `Capture error: ${m}` `` |
| constants | every other literal of 2.3 to 2.17 |

As built in WP-A19a: `HomeSelection`, `RenameCommit`, `BulkProgress`, `BulkOutcome` and `BulkRunner` are as specified. `HomeSelection.Changed` is raised only for a call that changed the selection or the anchor, and `SelectAll` makes the selection exactly the rows shown, as `new Set(visibleOrder)` does. `BulkRunner` throws `OperationCanceledException` once its token is cancelled, before the next project; an operation's own cancellation, with the run's token not cancelled, is a failure like any other. `RenameSession.Begin` also takes the list's title lookup and returns the commit of the rename it ended, `RenameCommit? Begin(string path, string currentTitle, Func<string, string?> currentTitleOf)`, which the caller writes: the blur rule compares with the title the list shows at that moment, as `Commit` does. `HomeText` has the row menu's items (`Rename`, `RevealInExplorer`, `ArchiveItem`, `RestoreItem`, `Delete`), the overflow trigger's (`MoreActionsGlyph`, `MoreActions`), the bulk bar's parts (`BulkName`, `SelectAll`, `ClearAll`, `SelectAllTick`, `BulkArchive`, `BulkRestore`, `BulkDelete`, `BulkClear`) and verbs (`Deleting`, `Archiving`, `Restoring`), the confirm's (`ConfirmName`, `ConfirmCancel`, `ConfirmOk`) and `SelectRow(title)`; `HomeTextTests` finds each in the Electron source.

As built in WP-B9a: `CaptureMode` is Core's `ShotAI.Core.Capture.CaptureMode` (`Screen`, `Auto`, `Window`, `Area`, the chips' order), and `CaptureReadiness` (`ShotAI.Core.Home`) also has `DefaultMode`, `Screen`, the mode each launch starts in. `IsReady` and `BuildTarget` are as specified; the Window target carries the window's id, pid and title as they were listed. `HomeText` has the hero's strings (`StartProject`, `Mission`, `NamePlaceholder`, `NameBoxName`, `CaptureButton`, `CaptureButtonTitle`, `Creating`, `EmptyProject`, `EmptyProjectTitle`), the mode row's (`ModeGroupName`, `ModeLabel`, the four chips and their tooltips, `ModeChip(mode)` and `ModeHint(mode)`, `AutoWarning`, `AutoWarningTitle`, and the hint's parts around the bold mode names, `ModeHintBefore` to `ModeHintAfterAuto`), the dropdown's (`DropdownCaret`, `Loading`, `SelectWindow`, `SelectMonitor`, `Untitled`, `WindowsHead`, `MonitorsHead`, `Refresh`, `RefreshTitle`, `WindowListName`, `MonitorListName`, `NoWindows`, `NoMonitors`, `PrimarySuffix` and `WindowItemName(window)`), the Area's (`SelectArea`, `ReselectArea`, `Selecting` and `AreaButton(selecting, hasArea)`), the two warnings (`WindowWarning`, `AreaWarning`), and the recording panel's buttons and hint for WP-B9b (`Pause`, `Resume`, `Stop`, `RecordingHint`); `HomeTextTests` finds each in the Electron source.

### 7.4 Core: Settings text, tour and theme logic

```csharp
namespace ShotAI.Core.SettingsUi;
// ApiKeyStatus, TestConnectionResult, ConnectionLeg: 08's canonical value types in ShotAI.Core.Auth (08 7.12, R-ARCH-2);
// UpdateCheckResult: 10's, in ShotAI.Core.Updates (11 7.3.6); ThemePref: 10's, in ShotAI.Core.Settings.
public static class SettingsText
{
    public static string AiHint(bool federated);          // 2.25
    public static string AiOff(bool federated);
    public static string KeyStatus(ApiKeyStatus s);        // exact 2.25 concatenation, leading spaces kept
    public static string ConnectionMessage(TestConnectionResult r);   // 2.25 formula
    public static string? LegLabel(ConnectionLeg? leg);    // "Microsoft sign-in" | "Claude access" | "Claude API" | null
    public const string PerMachineUpdateNote = "On this PC, IT or an administrator installs updates.";   // INV-HOME-45
    public static string UpdateAvailable(string version, bool perMachine) =>          // 2.22; INV-HOME-45
        perMachine ? $"shotAI {version} is available. {PerMachineUpdateNote}" : $"shotAI {version} is available.";
    public static string? UpdateCheckMessage(UpdateCheckResult? r, Exception? thrown, bool perMachine);
                                                           // 2.29; the available case is UpdateAvailable(r.Version, perMachine)
    public static string AppInfoLine(string name, string version, string platform, string arch, string dotNetVersion);
                                                           // 7.12; the App passes the fields of 11's IAppInfo.Current
                                                           // (AppInfo is an App record, which Core cannot reference, INV-ARCH-2)
    public static string CaptureScaleLabel(double scale) => $"{JsMath.Round(scale * 100)}%";
    public static string ThemeBlurb(ThemePref p);  public static string BrandBlurb(string brandId);
    // plus every literal of 2.24 to 2.29
}
public sealed record ArchiveAgeOption(int Days, string Label);
public static class ArchiveAgeOptions
{
    public static IReadOnlyList<ArchiveAgeOption> Standard { get; } =
        [new(0, "Never"), new(30, "After 1 month"), new(90, "After 3 months"), new(180, "After 6 months"), new(365, "After 1 year")];
    public static IReadOnlyList<ArchiveAgeOption> For(int stored);   // Standard plus ($"After {stored} days") when not standard (EDGE-HOME-37)
}
public static class CaptureScaleSteps
{
    // EDGE-HOME-38: an exact-integer quotient gives the decimal step's double; Math.Round is banned in Core (ARCHITECTURE 14.9)
    public static double Snap(double v) => (50 + 5 * JsMath.Round((Math.Clamp(v, 0.5, 1) - 0.5) / 0.05)) / 100.0;
}

namespace ShotAI.Core.Tour;
public enum TourAnchorId { Hero, Capture, Mode, Settings }
public sealed record TourStep(TourAnchorId? Anchor, string Headline, string Body, bool ShowPill);
public static class TourSteps { public static IReadOnlyList<TourStep> All { get; } /* 2.31, verbatim */ }
public readonly record struct LayoutRect(double Left, double Top, double Width, double Height)
{ public double Right => Left + Width; public double Bottom => Top + Height; }
public enum CaretSide { None, Top, Bottom }
public sealed record TourPlacement(double? BubbleTop, double? BubbleBottom, double? BubbleLeft, bool Centred,
                                   CaretSide Caret, double CaretLeft, LayoutRect? Spot);
public static class TourLayout
{
    public const double BubbleWidth = 330, Gap = 14, BelowThreshold = 220, EdgeMargin = 12, CaretClamp = 18, SpotPad = 6;
    public static TourPlacement Place(LayoutRect? anchor, double viewportWidth, double viewportHeight);   // 2.31 exactly
}

namespace ShotAI.Core.Theme;
public enum Appearance { Light, Dark }
public static class AppearanceResolver
{ public static Appearance Resolve(ThemePref pref, bool systemDark) => pref == ThemePref.Dark || (pref == ThemePref.System && systemDark) ? Appearance.Dark : Appearance.Light; }
public static class ActiveBrandResolver
{
    /// (openPath && !showSettings && projectTheme) || appBrand
    public static string Resolve(bool projectViewVisible, string? projectPinnedBrand, string appBrand)
        => projectViewVisible && projectPinnedBrand is not null ? projectPinnedBrand : appBrand;
}
public readonly record struct Rgb(byte R, byte G, byte B)
{ public static Rgb FromHex(string hex); }        // #rrggbb, either case (added in WP-A14)
public static class ColorMix
{
    public static Rgb Srgb(Rgb a, double p, Rgb b);   // per channel JsMath.Round(a * p + b * (1 - p))
}
public static class ChromeTokens   // project.css :23-82, as DIP and weights
{
    public const double Rem = 16;                  // DIP per rem, 7.5 (added in WP-A14)
    public const double FsDisplay = 28, FsSection = 19.2, FsTitle = 15.04, FsBody = 14, FsMeta = 12.8, FsLabel = 11.04;
    public const int FwDisplay = 750, FwSection = 700, FwTitle = 600;
    public static ShadowSpec ShadowSm(Appearance a);  public static ShadowSpec Shadow(Appearance a);  public static ShadowSpec MenuShadow(Appearance a);
}
public sealed record ShadowSpec(double OffsetY, double Blur, byte R, byte G, byte B, double Alpha);
public sealed class ThemeTokenSet   // one (brand, appearance): colours, derived colours, radii, font stack, label stretch
{
    public static ThemeTokenSet For(string? brandId, Appearance appearance);  // reads 10's generated table only (BrandPalette.Get)
    public const double BulkBorderAlpha = 0.3;                  // the bulk bar's accent at 30% (added in WP-A14)
    public string BrandId { get; }  public Appearance Appearance { get; }   // the coerced pair (added in WP-A14)
    public IReadOnlyDictionary<string, Rgb> Colours { get; }    // key = CSS token name without "--", 36 entries
    public Rgb ItemHoverBorder { get; }  public Rgb ItemSelectedBackground { get; }   // 2.33 derived
    public IReadOnlyDictionary<string, double?> Radii { get; }  // 7 entries keyed without "radius-" (panel, control-sm), null = capsule
    public string? BundledFontFamily { get; }                   // "Archivo" for LFI, null for shotAI (added in WP-A14)
    public IReadOnlyList<string> FontStack { get; }  public int? LabelStretchPercent { get; }
}
public static class ThemeTokenKeys   // 7.5's keys (added in WP-A14 as built)
{
    public static IReadOnlyList<string> All { get; }            // the 105 keys of 7.5, in a fixed order
    public static IReadOnlyList<string> RadiusRoles { get; }    // panel, card, figure, control, control-sm, micro, chip
    public static string Brush(string token);  public static string Color(string token);
    public static string Radius(string role);  public static string RadiusValue(string role);
    public const string ItemHoverBorder = "Brush.item-hover-border", ItemSelectedBackground = "Brush.item-selected-bg",
        BulkBorder = "Brush.bulk-border", FocusVisible = "Brush.focus-visible",
        FontStack = "Font.stack", LabelStack = "Font.label-stack", LabelStretch = "Font.label-stretch",
        FsDisplay = "Fs.display" /* ... Fs.label, Fw.display ... Fw.title */, ShadowSm = "Shadow.sm", Shadow = "Shadow.default", MenuShadow = "Shadow.menu";
}
public interface ISystemAppearance { bool IsDark { get; } event EventHandler? Changed; }   // 7.14 (added in WP-A14)
```

`FontStack` names each family once (added in WP-A14): shotAI's `sans-serif` becomes a second `Segoe UI`, which is dropped, so its stack is `Segoe UI, Roboto, Helvetica, Arial`.

`ActiveBrandResolver` receives `projectPinnedBrand = BrandPalette.PinnedBrand(manifest.Theme)` (10, R-ARCH-14) from 05 (null for no pin AND for an unrecognised pin); the unrecognised flag goes only to 03's menu. An unknown `brandId` passed to `ThemeTokenSet.For` is coerced with 10's `BrandPalette.CoerceBrand` (never throws).

### 7.5 WPF resources and styles

**ThemeManager** (`ShotAI.App.Chrome`, UI thread, singleton):

```csharp
public sealed class ThemeManager : IAppStartup, IDisposable
{
    public ThemeManager(ISettingsService settings, ISystemAppearance system, NavigationState nav, IUiDispatcher ui, ILogger<ThemeManager> log);
    public Appearance CurrentAppearance { get; }  public string CurrentBrand { get; }
    public void ApplyInitial(Application app);   // ARCHITECTURE 4.2 step 8, before MainWindow.Show(): builds and merges the dictionary
    public void Start();                         // step 9, after RemoteVisibilityApplier and RecordingVisibilityController
                                                 // (ARCHITECTURE 4.3 order): subscribes only, no IO (11 7.10 rule 3)
    public void Dispose();                       // unsubscribes; never needs the UI thread to be free (ARCHITECTURE C5)
    // re-applies on: ISettingsService.Changed where Theme or Brand differ (rollbacks included; the handler
    // posts through IUiDispatcher.Post and reads settings.Current, 11 T6/T7); system appearance changed while
    // Theme == System; nav.ProjectViewVisible or nav.ProjectPinnedBrand changed (active brand)
    public event EventHandler? ThemeChanged;
}
```

As built in WP-A14: `ApplyInitial` delegates to an internal `ApplyInitial(ResourceDictionary)`, which the tests use with a window's resources because a process holds one `Application`; the composition root calls it through `App.ShowThemed(theme, Resources, main)`, which applies and then shows. A navigation change re-applies synchronously, because `NavigationState` is the UI thread's own, so a project view's first frame already wears its pinned brand; settings and system changes are posted. `Start` reads no setting of Windows (the monitor takes its baseline from reads, 7.14). Each apply logs debug `theme: <brand> <light|dark>`; a failed re-apply logs warning `theme apply failed (non-fatal):` with the exception and changes nothing. An internal constructor takes the high-contrast switch and reading (Q-HOME-12).

`Apply(appearance, brand)`: if unchanged, return. Build a new `ResourceDictionary` with `ThemeResources.Build(ThemeTokenSet.For(brand, appearance), appearance)`, replace the ONE merged dictionary slot owned by the manager in `Application.Current.Resources.MergedDictionaries` (replace in place, keep the index, so styles that reference keys by `DynamicResource` update), raise `ThemeChanged`. The swap happens on the UI thread; the dictionary contents are frozen (`Freezable.Freeze`) so they are cheap and thread-safe. REQUIRED (behaviour of 2.32).

**Resource keys** (`ThemeTokenKeys`, used by every XAML file through `{DynamicResource ...}`):

| Kind | Key pattern | Example | Type |
|---|---|---|---|
| Colour token as brush | `Brush.<token>` | `Brush.accent`, `Brush.ink-2`, `Brush.control-bd`, `Brush.caut-fg` | `SolidColorBrush` (frozen) |
| Colour token as colour | `Color.<token>` | `Color.accent` | `Color` |
| Derived | `Brush.item-hover-border`, `Brush.item-selected-bg`, `Brush.bulk-border` (accent at alpha 0.3) | | `SolidColorBrush` |
| Radius | `Radius.<role>` and `RadiusValue.<role>` | `Radius.panel` (`CornerRadius`), `RadiusValue.chip` (`double`, `double.PositiveInfinity` for capsule) | |
| Type | `Font.stack`, `Font.label-stretch` | | `FontFamily`, `FontStretch` |
| Sizes | `Fs.display`, `Fs.section`, `Fs.title`, `Fs.body`, `Fs.meta`, `Fs.label`; `Fw.display`, `Fw.section`, `Fw.title` | | `double`, `FontWeight` |
| Shadows | `Shadow.sm`, `Shadow.default`, `Shadow.menu` | | `DropShadowEffect` (frozen) |
| Focus | `Brush.focus-visible` (= accent), `Brush.focus-ring` | | |

Keys are the CSS token names so a reader can map `var(--ink-2)` to `{DynamicResource Brush.ink-2}` mechanically. 36 colour tokens times 2 key kinds, 3 derived, the focus ring, 7 radii times 2, 3 type, 9 sizes, 3 shadows: 105 keys (`ThemeTokenKeys.All`).

Added in WP-A14: `Font.label-stack` (`FontFamily`), the uppercase micro-labels' family. The upstream `wdth` 62 files name their own family, `Archivo ExtraCondensed` (name IDs 1 and 16, read from the files), so a stretch request inside `Archivo` may not reach them; for a brand with a label stretch the label stack names the bundled face's family of that width first and the face's own family second (`./#Archivo ExtraCondensed, ./#Archivo, Helvetica Neue, ...`), which resolves whichever way WPF groups the files; otherwise it equals `Font.stack`. A capsule has no `CornerRadius` (`CornerRadius` refuses infinity, and a large one draws an ellipse, below), so `Radius.chip` of a capsule brand holds 999 and is never read; a chip's corner comes from `RadiusValue.chip` through `CapsuleCornerConverter` (`XamlChromeGuardTests`).

**Translations** (IMPROVEMENT where noted, justified by WPF having no CSS):

| CSS | WPF |
|---|---|
| `rem` | 16 DIP per rem (constant) |
| `font-weight: 650` / `750` | `FontWeight.FromOpenTypeWeight(650)` / `750`; the face snaps to the nearest bundled weight |
| `font-stretch: var(--label-stretch)` | `FontStretch` from `Font.label-stretch`: `Normal`, or `FontStretches.ExtraCondensed` for 62% (usWidthClass 2, 62.5% of normal, the nearest WPF stretch; `UltraCondensed` is 50%) resolving to the bundled wdth-62 face (Q-HOME-2) |
| `text-transform: uppercase` | `UpperCaseConverter` on the bound text, upper-casing with the binding's culture, which is the element's `Language` (en-US unless set). Corrected in WP-A14 from `CultureInfo.CurrentCulture`: Chromium upper-cases by the document's language, `<html lang="en">` in all three Electron documents, so a Turkish Windows still shows `I` for `i`; the Windows culture would give `\u0130` |
| `border-radius: var(--radius-chip)` when null | `CapsuleCornerConverter`, a multi-value converter over the element's `ActualHeight` and `RadiusValue.chip`, which the `Border` carries in its `Tag` (a `DynamicResource` cannot feed a binding): half the height for a capsule, else the brand's radius clamped to half the height. Settled in WP-A14 by `ControlStylesTests.ALargeCornerRadiusIsAnEllipseNotACapsule`: WPF clamps a corner to half of each side on its own, so a large constant draws an ellipse where CSS scales every corner by one factor and draws a capsule |
| `box-shadow: 0 Ypx Bpx rgba(...)` | `DropShadowEffect { ShadowDepth = Y, Direction = 270, BlurRadius = B, Color, Opacity = alpha }`; on the list rows IMPROVEMENT: apply only on the row's background `Border` (not the content, so text is not rasterised through the effect); if profiling shows cost, drop the row shadow (Q-HOME-14) |
| `:hover` | `IsMouseOver` triggers in the control templates |
| `:focus-visible` outline 2px accent offset 2 | `FocusVisualStyle` (the style `FocusVisual`, which every control style sets) with a `Rectangle` 2 DIP `Brush.focus-visible` stroke, margin -4, `RadiusX/Y` = `RadiusValue.micro`; WPF shows it only for keyboard focus, matching `:focus-visible`. Corrected in WP-A14 from margin -2: a `Rectangle` draws its stroke inside its bounds, so the 2 DIP offset plus the 2 DIP stroke is -4 |
| input `:focus` ring | `IsKeyboardFocusWithin` trigger: 2 DIP outer border `Brush.focus-ring` (margin -2 in the template, so it sits outside the field as an outline does) and inner border `Brush.accent` |
| `transition` | `Storyboard` animations with the same durations; all disabled when `SystemParameters.ClientAreaAnimation` is false (the Windows "Animation effects" setting, the analogue of `prefers-reduced-motion`). The `Switch` style's 0.15 s track and knob transitions join with the Settings view (WP-B10), its first user |
| `color-mix` | precomputed by `ThemeTokenSet` (2.33) |
| emoji in labels | text as-is (EDGE-HOME-46) |

**Fonts.** `Font.stack`: the brand's `family` (a bundled face) followed by the fallbacks with quotes removed, `-apple-system` dropped, and `sans-serif` replaced by `Segoe UI` (WPF `FontFamily` accepts a comma-separated fallback list). shotAI therefore resolves to Segoe UI, as Chromium does on Windows. The Archivo face ships as static instances (Q-HOME-2) packaged by 12, licence from 10. Corrected in WP-A14: the bundled face is a loose file, not a resource, referenced as `./#Archivo` from the base URI of `<app folder>\Fonts\static\` (`BundledFonts.StaticFolderUri`), because every copy of a font file needs its `OFL.txt` in the same folder (10 INV-INFRA-31), which an embedded copy has not, and because the pack URI's `application` authority names the entry assembly, which is the test host in the tests.

**Styles** (`Themes/Controls.xaml`): one keyed style per Electron class family: `Button.Base` (`btn`), `Button.Small`, `Button.Primary`, `Button.Danger`, `Button.Ghost`, `Button.Icon`, `Chip` (`capmode__chip`, a `RadioButton` template), `SortChip` (`RadioButton`), `TextInput` (`project__input`), `Switch` (a `CheckBox` template drawing 2.24's track and knob), `SettingsGroup` (a `Border`), `SettingsToggleCard` (a `CheckBox` template whose whole card toggles), `Badge.Ok`, `Badge.Draft`, `MenuItem.Base`, `MenuItem.Danger`, `MenuHeader`, `PickerItem`, `TabUnderline` (Settings tabs), `HomeTab`. Each reproduces 2.34 measurements with `DynamicResource` tokens only. As built in WP-A14: `FocusVisual` (the ring above), `Badge.Base` (under both badges) and `HomeTabCount` (a `Border` whose colours follow its tab's `IsChecked`, 2.7) join them; `Button.Icon` is Electron's `btn btn--small btn--ghost btn--icon`, the one combination the overflow trigger uses, since WPF gives an element one style; the `TextBlock` of a button's content takes `line-height: 1` as `LineHeight` 12.8 with `BlockLineHeight`; `menu__header`'s 0.03em letter spacing has no WPF property; a `PickerItem` keeps its bottom hair line on the last item too (the list view drops it in WP-A16). Where CSS's `:hover:not(:disabled)` outranks a modifier class (for `capmode__chip--on`), the hover trigger comes after the checked trigger, as the cascade applies them. `TextInput` is D-HOME-30 for a `TextBox`; the `PasswordBox` and `ComboBox` field styles join with the Settings view (WP-B10). `Controls.xaml` merges `FixedColors.xaml` itself, so its `StaticResource` reads of a fixed colour resolve wherever it is loaded, and `App.xaml` merges `Controls.xaml`.

**Fixed colours.** `Themes/FixedColors.xaml` holds, with a comment giving the reason for each, the notice fills (`#99B91C1C`, `#992563EB`, `#9916A34A`; 0.6 alpha = 0x99), notice text `#FFFFFFFF`, the confirm scrim `#8C111318` and tour dim `#8C11131B` (0.55 = 0x8C), and the tour pill mock-up literals. This file and the report marker styles (05) are the only exceptions the guard accepts (INV-HOME-24). As built in WP-A14: frozen brushes keyed `FixedColors.NoticeError`, `FixedColors.NoticeInfo`, `FixedColors.NoticeSuccess`, `FixedColors.NoticeText`, `FixedColors.ConfirmScrim` and `FixedColors.TourDim`, read with `StaticResource`, and the colour `FixedColors.KnobShadow` (`#FF000000`, the settings switch knob's neutral `0 1px 2px rgba(0, 0, 0, 0.25)`, 2.24, drawn at the effect's 0.25 opacity); the tour pill literals join with the tour (WP-B10).

### 7.6 Home

**Views.** `ShellView` is a `Grid`: row 0 the header (`HeaderView`, hidden when `CurrentView == Project`), row 1 a content grid holding `HomeView`, the project host (05) and `SettingsView`, one visible at a time, and above them the overlay layer (03 provides it; this subsystem places `NoticeHost`, `ConfirmHost`, `TourOverlay` and the target dropdown's `TargetDropdownView` in it, R-ARCH-19). Each view has its own `ScrollViewer`, which replaces the single shared Electron scroller (7.7).

**HomeViewModel** (`ViewModelBase : ObservableObject`, CommunityToolkit.Mvvm, ARCHITECTURE 5.1; UI thread):

```csharp
public sealed partial class HomeViewModel : ViewModelBase
{
    public HomeViewModel(IProjectService projects /*01, 11 7.3.2*/, ICaptureService capture /*02*/,
        ProjectDetailViewModel detail /*05: OpenAsync, Adopt, OpenFailed*/, IStepFlattener flattener /*04*/,
        IExportService exports /*09*/, IShellReveal reveal /*11 7.3*/, INoticeService notices, IConfirmService confirm,
        ISettingsService settings, IUiDispatcher ui, TimeProvider time, ILogger<HomeViewModel> log);
    // The first draft's `ProjectStore store` and `IProjectOpener` do not exist as named: 11 exposes the store as
    // IProjectService (R-ARCH-4), and 05 exposes open and adopt on ProjectDetailViewModel (05 7, 11 P11).
    // IStepFlattener is ShotAI.Core.Rendering (R-ARCH-7); IExportService is ShotAI.App.Export with 09 7.13's
    // seven members (R-ARCH-9); IShellReveal is ShotAI.Core.Shell (11 7.3.3).

    public CreateHeroViewModel Hero { get; }
    public CaptureModePickerViewModel Mode { get; }            // also ICaptureTargetSelection for 05
    [ObservableProperty] HomeTab tab;  [ObservableProperty] HomeSortKey sortKey;  [ObservableProperty] bool sortAscending;
    [ObservableProperty] string query = "";
    public ObservableCollection<object> Items { get; }          // GroupHeaderItem | ProjectRowViewModel, render order
    public int ActiveCount { get; }  public int ArchiveCount { get; }  public int ShownCount { get; }
    public HomeSelection Selection { get; }  public BulkBarViewModel Bulk { get; }
    public string? RowBusyPath { get; }  public bool AnyBusy { get; }
    public void OnEnter();       // reset tab/sort/query/selection/rename (EDGE-HOME-3), start timer, RefreshAsync(user: false)
    public void OnLeave();       // stop timer, end rename by committing it (Q-HOME-3), close popups
    public void OnWindowActivated();
    public Task RefreshAsync(bool userInitiated);
    [RelayCommand] Task OpenAsync(ProjectRowViewModel row);
    [RelayCommand] Task RevealAsync(ProjectRowViewModel row);
    [RelayCommand] Task ArchiveOrRestoreAsync(ProjectRowViewModel row);
    [RelayCommand] Task ExportAsync((ProjectRowViewModel Row, ExportFormat Format) a);
    [RelayCommand] Task DeleteAsync(ProjectRowViewModel row);
    [RelayCommand] void StartRename(ProjectRowViewModel row);
    [RelayCommand] Task ImportAsync();
}
```

**Refresh.** `RefreshAsync`: increment `generation`; if a refresh is in flight, mark "one queued" and return (coalescing; IMPROVEMENT, EDGE-HOME-4); else `list = await projects.ListProjectsAsync(ct)` (runs off the UI thread in 01), back on the UI thread: if `generation` moved on during the call, start one more pass; else, if the list differs from the last one (`SequenceEqual` on `ProjectSummary` records, which are value-equal), rebuild `Items` via `HomeListPipeline.Build` and a keyed diff (rows are reused by path so a focused rename box, hover and focus survive; rows no longer present are removed, `Selection.Prune`, `RenameSession.Abandon` if its row went). A background refresh never clears the error notice (EDGE-HOME-21); a failed background refresh is logged at warning and shows nothing; a failed user-initiated refresh (after a mutation) shows the error notice.

`Items` is rebuilt (not re-listed) when `Tab`, `SortKey`, `SortAscending` or `Query` change, and once a minute while Home is visible so the date buckets roll over at midnight and on Sunday (IMPROVEMENT: Electron recomputes `new Date()` on every render, which in practice includes the 20 s tick).

**Timer.** One `DispatcherTimer` (priority `Background`, `Interval = AutoRefreshPolicy.Interval`) created by the view model, started in `OnEnter`, stopped in `OnLeave` (so it restarts from zero on every return, parity for returns from a project; opening Settings from Home is a leave and closing it an enter, D-HOME-28, EDGE-HOME-48). `DispatcherTimer` is not on 11's banned list (only `Dispatcher.Invoke*`, `BeginInvoke*` and `InvokeAsync*` are). `Tick`: `if (AutoRefreshPolicy.ShouldTick(visible, Keyboard.FocusedElement is TextBoxBase or PasswordBox, Rename.IsOpen, Selection.Count > 0, AnyBusy)) RefreshAsync(false)`. `OnWindowActivated` (from `MainWindow.Activated`, only while the Home view itself is visible, not behind Settings) refreshes unconditionally (parity: focus is not suppressed). The 01 projects-changed signal (`IProjectService.ProjectsChanged`, raised on a pool thread, among others by the startup `AutoArchiveStaleAsync` when at least one project moved, R-ARCH-24; handled through `ui.Post`) and 07's SOP apply or revert of the open project (`sopBackup` change, 05 raises it) call `RefreshAsync(false)` in any view (parity 2.18 rows 5 and 6).

**Row operations** follow 2.14 with these native rules:
- One operation at a time (`RowBusyPath`, `Bulk.IsBusy`, `AnyBusy`), parity.
- Rename, archive, restore and delete follow the fixed write rule where 01 supports it: the row updates immediately (title changes, the row moves tab, the row disappears), the store call runs through 01's serialized queue, and on failure the list is restored from a fresh listing and an error notice shows the store's message. Delete still requires the confirmation first. The row shows `Working…` only while the store call runs. IMPROVEMENT (fixed decision); the Electron UI waited for the disk.
- Export: `HomeExportFlow.ExportOneAsync(row, format)`: `opened = await projects.OpenProjectAsync(path, ct)` (returns `OpenedProject(Dir, Manifest)`, 11 EDGE-IPC-26; auto-unarchive, parity EDGE-HOME-14); `await flattener.EnsureFlattenedAsync(path, opened.Dir, opened.Manifest.Steps, ct)` (04, INV-HOME-11); `await exports.ExportWithSaveDialogAsync(path, format, owner, progress: null, ct)` (09 7.13 signature, Q-EXP-18, R-ARCH-9; `ExportResult.Canceled` is not an error; on success 09 reveals the written file, parity 2.14); refresh. Not optimistic (it is not a model edit). With no open session for a Home project, the pre-egress settle of ARCHITECTURE 7.7 step 2 (`IProjectSettle.WhenSettledAsync`, which 09 calls, R-ARCH-6) completes at once.
- Reveal: `await reveal.RevealProjectAsync(path)` (11 7.3.3 `IShellReveal`: the known-project gate, then `RevealInExplorerAsync`; 11 section 10 requests this name), errors to the notice.
- Open: 05 `ProjectDetailViewModel.OpenAsync(path)`; its `OpenFailed(message)` (05 EDGE-REP-39) shows the error notice with 01 Q-MODEL-11 wording (IMPROVEMENT, EDGE-HOME-24).

**Bulk** (`BulkBarViewModel`): commands `ToggleAll`, `ArchiveOrRestore`, `ExportEach(format)`, `ExportToFolder(format)`, `Delete`, `Clear`; `IsVisible = Selection.Count > 0 || IsBusy` (IMPROVEMENT, EDGE-HOME-12); `Clear` disabled while busy (IMPROVEMENT); count text `HomeText.BulkProgress` while busy else `HomeText.BulkCount`. Each runs `BulkRunner.RunAsync(Selection.SelectedVisible(view.Sorted), verb, op, progress, e => ui.Post(() => notices.ShowError(e)), ct)` (the error callback marshals because Core's continuations run on the pool, 7.3) then one `RefreshAsync(true)` then `Selection.Clear()`. `ExportToFolder` first calls 09's `ChooseExportDirectoryAsync(owner)` (null aborts), and after the run 09's `RevealExportDirectoryAsync(dir)` (errors logged, not shown), parity EDGE-HOME-15. Failures: parity keeps "each failure replaces the error notice" (Q-HOME-7 proposes one aggregated message).

**CreateHeroViewModel**: `Title`, `IsBusy`, commands `CaptureCommand` (`CanExecute = !IsBusy && !recording && Mode.IsReady`), `EmptyProjectCommand` (`!IsBusy && !recording`), `ImportCommand` (shared with File then Import Project). `CaptureAsync`: 2.5 `onCreate` then `onRecord(path, createdThisSession: true)` with `capture.StartAsync(path, new CaptureStartOptions(Target: Mode.BuildTarget(), CreatedThisSession: true))` (02), then 05's `Adopt(dir, manifest)` (05 7, the `applyOpened` successor). `Enter` in the name `TextBox` executes `CaptureCommand` through a `KeyBinding` on that box only (not `IsDefault`, which would fire from other fields). A successful create, empty project or import closes Settings if open (IMPROVEMENT, EDGE-HOME-40).

**CaptureModePickerViewModel**: the 2.4 state; `Modes` as four `RadioButton`s in one group (arrow keys move between them natively; IMPROVEMENT); `LoadTargetsAsync` via `capture.ListTargetsAsync()` (02) with 2.4's keep-or-default rule; `SelectAreaAsync` via 03's `IAreaSelectionService.SelectAreaAsync(mainWindow, ct)` (null keeps the old area); the dropdown is a `ToggleButton` trigger plus a popover (`TargetDropdownView`) drawn in 03's in-window overlay layer, not a `Popup` (R-ARCH-19: a popover goes in the overlay layer unless WPF needs a popup HWND, and this one does not; it draws inside the already excluded main window, INV-HOME-43). The popover is a full-window transparent backdrop (Electron's `menu__backdrop`) whose click closes it and is consumed (parity), and whose mouse wheel is re-raised on the Home `ScrollViewer` (parity: the DOM scrolls under the fixed backdrop), plus the popover `Border` placed from the trigger's bounds transformed into overlay coordinates (`trigger.TransformToVisual(overlayRoot)`): top = trigger bottom + 4 DIP, left and width = the trigger's; it is re-placed on the Home `ScrollViewer`'s `ScrollChanged` and the window's `SizeChanged`, so it follows the trigger as Electron's absolutely positioned popover does. It contains the head (`Windows`/`Monitors` and `↻ Refresh`) and a `ListBox` (`AutomationProperties.Name` "Window to capture" or "Monitor to capture"); opening focuses the list; Escape closes and returns focus to the trigger, arrows move, Enter or click picks (IMPROVEMENT, EDGE-HOME-31); the trigger exposes ExpandCollapse through the same custom peer as `OverflowMenu` (7.8). `PickedMonitorId` is `uint?` (R-ARCH-22, 7.3). `BuildTarget()` delegates to `CaptureReadiness.BuildTarget`. It implements `ICaptureTargetSelection { CaptureTarget BuildTarget(); }` (an App interface in `ShotAI.App.Home`, accepted by R-ARCH-26; 05's `ProjectDetailViewModel` calls `BuildTarget()` at click time and raises `ResumeCaptureRequested(target)`, and the shell's capture coordinator starts the session with the target from the event args and never calls `BuildTarget()` itself, 05 7.14 and section 10) for 05's Resume capturing (EDGE-HOME-7). Lifetime: one instance per shell (singleton), never recreated by `HomeViewModel.OnEnter`, so the mode and picks survive navigation (parity, EDGE-HOME-57); `OnLeave` closes its popup.

**RecordingPanelViewModel**: shown when the capture status is not Idle and the main window is visible; subscribes to 02's `StateChanged`, `StepLanded` (append, seeded from the opened manifest), `CaptureFailed` (error notice `HomeText.CaptureError`); buttons call `Pause` and `Resume` through `Task.Run` (they may take the engine lock, 11 T9) and `await capture.StopAsync()`; each result replaces the shown state; errors go to the notice. Events arrive on engine threads and are marshalled with `IUiDispatcher.Post` (11 T6); the state is re-read with `capture.GetState()` inside the posted action (11 T7).

As built in WP-A16 (the list; the rest of 7.6 joins with its work packages):
- The constructor is `HomeViewModel(IProjectService projects, INoticeService notices, IUiDispatcher ui, TimeProvider time, ILogger<HomeViewModel> log)`. The other dependencies join with their parts: 05's `ProjectDetailViewModel` with the open (WP-A17), `IConfirmService` and `IShellReveal` with the row operations (WP-A19a), `ICaptureService`, `ISettingsService` and the hero (WP-B9a), 04's flattener and 09's exports with Home export (WP-D16).
- `OpenCommand` raises `OpenRequested(path)` and `ImportCommand` raises `ImportRequested`; the shell connects them to the project view (WP-A17) and to the import flow (WP-D15), so `OpenFailedShowsNotice` moves to WP-A17. `ClearSearchCommand` runs while the box has any text, spaces included, from the clear button and from Escape in the box.
- Refresh: a request counter takes the place of `generation`. Concurrent calls share the one running pass, which reads again when a request arrived during its read; a user-initiated request that joins a background pass makes it user-initiated, so its failure shows the notice. `OnLeave` cancels the pass's token and the listing it was reading is dropped. A background failure is logged at Warning, `home: background refresh failed:` with the exception (7.16).
- The keyed diff is Core's `ListSync`: removals last first, then a move or an insert per position, by reference, after rows are reused by path and span headers by label. An unchanged listing edits nothing (AC-HOME-35's automated half), and a changed project updates its row in place.
- Two `DispatcherTimer`s at `Background` priority, the 20 s tick and the 1 minute regroup (`AutoRefreshPolicy.RegroupInterval`, D-HOME-25), both started by `OnEnter` and stopped by `OnLeave`. Until WP-A19a the tick passes `false` for renaming, selection and busy.
- `ShownCount`, `EmptyState` (`None`, `NoMatches`, `NoProjects`, `NoArchived`, 2.17) and the flags the view binds (the tab and chip radio states, `ImportVisible`, `ShowPlaceholder`, `HasQuery`, the empty-state lines) are view-model properties; the texts are `HomeText`'s.
- `HomeView`: the tabs are `RadioButton`s named `Projects <n>` and `Archive <n>` (`HomeText.TabName`, the text Electron's buttons read as); the list head is a `FlexWrapPanel`, 2.8's flex row with `gap: 1rem` and `justify-content: space-between`, where the search box grows from its 200 DIP basis to 340 DIP; the rows are an `ItemsControl` with no virtualization, and each card keeps `Shadow.sm` (Q-HOME-14, AC-HOME-35 by hand). The shared styles are read with `DynamicResource`, so the view also loads without an `Application` (the tests' windows).
- The search box's placeholder is a `TextBlock` in `Brush.ink-3` over the box, shown while it is empty; WPF draws no `letter-spacing`, so the header's `0.04em` is not drawn (accepted).

As built in WP-A19a (the row operations; export from a row and the bar joins in WP-D16):
- The constructor is `HomeViewModel(IProjectService projects, IShellReveal reveal, INoticeService notices, IConfirmService confirm, IUiDispatcher ui, TimeProvider time, ILogger<HomeViewModel> log)`.
- List-level optimism: an edit the store has not written yet (a new title, the archive flag flipped, the row hidden for a delete) is laid over every listing until the store is done, so a refresh meanwhile shows it too. When the store is done, its summary, or nothing for a delete, takes the row's place in the listing and a user refresh follows, whose failure shows the notice. When it fails, the edit goes, so the row is back at once, the notice shows the store's message, and the list is read again as a background refresh, so a failed listing does not replace that message. Each rename that writes, archive, restore, delete and bulk run takes the error notice down when it starts (7.10).
- `RowBusyPath` is set for an archive, restore or delete, from the start of the store call to the end of its refresh; the row reads `Working…` wherever it is shown, and `NotBusy` (the rows' Open and overflow triggers) and the bulk actions but Clear follow `AnyBusy`. A rename is not busy. Delete asks first, with the row's title as shown.
- The selection is pruned, and a rename abandoned, on every rebuild that changes the rows shown, each refresh included (D-HOME-5, EDGE-HOME-11): an archive or delete moves its row out of the selection at once. Typing in the search box clears the selection; the clear button and Escape in the box do not (EDGE-HOME-5). A tab switch clears it, as `OnEnter` and the end of a bulk run do.
- A bulk run takes the selected rows shown, runs each project's operation on the UI thread through `IUiDispatcher.InvokeAsync`, so each is shown and rolled back as a row operation is, counts through a `Progress<BulkProgress>` made on the UI thread whose late reports a run token drops, posts each failure to the notice, then runs one user refresh and clears the selection in a `finally` (D-HOME-33). The bulk delete's question counts the selected rows shown (EDGE-HOME-13), and with none it asks nothing.
- Escape: an inner surface takes it first (the confirm, an open menu, the rename box, the search box with text); what reaches the main window goes to `ShellViewModel.OnEscape`, and while Home shows `HomeViewModel.OnEscape` ends a rename, else clears the selection, marking the key handled (D-HOME-11).
- `ProjectRowViewModel(string path, HomeViewModel? home)` has `IsSelected`, `IsRenaming`, `IsBusy`, `SelectName` and `MenuItems` (Rename, Reveal in Explorer, Archive or Restore, a separator, Delete in the danger colour; the export items join between two separators in WP-D16); `Meta` reads `Working…` while the row is busy. `BulkBarViewModel(HomeViewModel)` holds the bar's texts and commands.
- `HomeView`: the checkbox is a `RowCheckBox`, a `CheckBox` whose click never ticks it by itself: its command toggles, or with Shift held adds the range (7.9), and the tick follows `IsSelected`; a screen reader's Toggle runs the same click. The rename box (`RenameInput`) opens focused, the caret after the title; Enter commits and Escape cancels, each handled, with the focus back on the row's menu trigger; a focus loss commits, unless the box's own context menu took the focus. A selected card wears `Brush.accent` and `Brush.item-selected-bg`, and the hover border wins over it, as `:hover` outranks the selected class. The bulk bar is a `FlexWrapPanel` of two groups: the toggle (`BulkToggle`) and the live count, and the archive, delete (`Button.SmallDanger`) and Clear buttons. Hiding Home closes a row's open menu.

As built in WP-B9a (the hero, the picker, the target dropdown and the recording flows; the recording panel joins in WP-B9b):
- The constructor is `HomeViewModel(IProjectService projects, IShellReveal reveal, INoticeService notices, IConfirmService confirm, CaptureModePickerViewModel mode, IUiDispatcher ui, TimeProvider time, ILogger<HomeViewModel> log)`. `ICaptureService` is the picker's and the shell's, not Home's, and `ISettingsService` joins with the part that reads it. Home makes the hero with the picker, and `Mode` is the hero's. `OnEnter` tells the picker Home shows, which on the first entry loads the targets when the mode is Screen, so the primary monitor is picked (2.4's first load); `OnLeave` tells it Home is gone, which closes the dropdown.
- `CreateHeroViewModel(CaptureModePickerViewModel mode)` holds `Title`, and `IsBusy` and `IsRecording`, which the shell sets. `CaptureText` reads `Creating…` while busy, `CanEditName` is `!IsBusy && !IsRecording` (the name box and Empty Project), and `ShowNamePlaceholder` shows the placeholder over an empty box. `CaptureCommand` runs while `CanEditName && Mode.IsReady` and `EmptyProjectCommand` while `CanEditName`; each raises an event for the shell, `CaptureRequested` or `EmptyProjectRequested`. The import stays Home's `ImportCommand` (WP-D15).
- Corrected in WP-B9a: the flows are the shell's, not a `CaptureAsync` of the hero's. 2.5's `onRecord` ends in 05's `Adopt`, which must reach the project view the shell shows, and view models are transient (INV-IPC-22): a hero given its own `ProjectDetailViewModel` would adopt into a view nobody shows. `ShellViewModel`, which holds the one project view and follows the capture state, is the capture coordinator this section and 05 7.14 name (7.7).
- `CaptureModePickerViewModel(ICaptureService capture, IAreaSelectionService areas, INoticeService notices)` is registered once, and as `ICaptureTargetSelection` (the same instance). Its state is 2.4's: `Mode`, `Targets`, `TargetsLoading`, `PickedWindow`, `PickedMonitorId` (`uint?`), `PickedArea`, `SelectingArea` and `PickerOpen`, with the chip flags `IsScreen`, `IsAuto`, `IsWindow` and `IsArea`, whose setters select their mode, and what the views read: `IsReady`, `PickedMonitor`, `ShowsDropdown`, `ShowsAreaPicker`, the three warning flags, `TriggerLabel`, `ListHead`, `ListName`, `EmptyText`, `Items`, `HasArea`, `AreaText` and `AreaButtonText`. `Items` holds one `TargetItem` per listed window or monitor: its name, its detail (a window's app, before the name; a monitor's size, after it), whether it is the pick, and an accessible name that reads them in that order. `LoadTargetsAsync` applies 2.4's keep-or-default rule, the kept window being the old object (EDGE-HOME-31), and a failure shows the error notice. One load runs at a time: `SelectMode` loads for Window or Screen only when nothing is loaded and no load runs, and Refresh is disabled while one runs. `SelectAreaAsync(Window? requester)` takes the window the Area button is in, the one the overlay hides (03's `IAreaSelectionService`); a cancel keeps the old area, and a failure shows the notice.
- `HomeView`: the hero is a `StackPanel` above the tabs, 1.5rem over them. The name box binds `Title` as typed, and Enter in it is `CaptureCommand` through a `KeyBinding` on the box (7.9); its placeholder is a `TextBlock` over it and its `AutomationProperties.HelpText` (7.13). The mission is 60ch wide and the mode hint 62ch, with `ch` taken as 0.5625 em (472.5 and 446.4 DIP). The chips are `RadioButton`s (`Chip`) in one group, in a `WrapPanel` named `Capture mode`, each with its tooltip and a command that selects its mode, so the arrows move between them (7.9); the hint is a `TextBlock` of `Run`s with the mode names in SemiBold `Brush.ink-2`. The Area button passes its window as the command parameter.
- The target dropdown's trigger is a `TargetTrigger`, a `Button` whose `IsOpen` follows `PickerOpen`, where this section said `ToggleButton`: a `ToggleButton`'s click flips a checked state of its own beside the view model's. Its peer reports ExpandCollapse (7.8), and Expand and Collapse run its command when the state is the other one. `TargetDropdownView` is the overlay layer's first child, so the confirm and the notices draw over it; its data context is the picker, and the shell attaches it to Home's trigger and scroller once both exist. It places the popover from `trigger.TransformToVisual(this)` on the trigger's `SizeChanged`, the scroller's `ScrollChanged` and its own `SizeChanged`, which follows the window's. The backdrop closes on a left button down, which it handles, and passes the wheel on to Home's scroller; the popover's rounded corners clip the head and the rows. Opening selects and focuses the picked row, or the first, or Refresh when the list is empty, after the first layout pass. Up, Down, Home and End are the `ListBox`'s own; Enter picks the selected row, a click (the row's mouse-up) picks its row, and Escape closes; each gives the focus back to the trigger, as a close with the focus inside does. Tab cycles inside the popover.

### 7.7 Navigation and scroll

`ShellViewModel` owns `CurrentView` (`Home`, `Project`, `Settings`, `Recording`) and `SettingsReturnsTo` (`Home` or `Project`), computed from 02's capture status, 05's open project and its own `SettingsOpen` exactly as 2.1. It implements the 2.1 state machine, handles 03's `OpenSettingsRequested` and `ImportProjectRequested` (both ignored while recording, INV-HOME-18), and exposes `NavigationState` (`ProjectOpen`, `OpenProjectPath`, `RawProjectTheme`, `ProjectViewVisible = CurrentView == Project`, `ProjectPinnedBrand`, `Changed`) for 03's menu and the theme manager. Added in WP-A14, ahead of the shell, because `ThemeManager` takes it: `NavigationState` with `ProjectViewVisible`, `ProjectPinnedBrand` (`BrandPalette.PinnedBrand` of the raw theme, so an unrecognised pin is null), `Changed` (raised only when one of them changed) and `SetProjectView(bool projectViewVisible, string? rawProjectTheme)`, the shell's setter; the rest joins in WP-A15. As built in WP-A15: `NavigationState` implements 03's `IShellNavigationState` (`ProjectOpen`, `OpenProjectPath`, `RawProjectTheme`, `Changed`), registered once for both its readers; one setter, `Set(bool projectViewVisible, string? openProjectPath, string? rawProjectTheme)`, replaces `SetProjectView` and refuses a project view or a theme with no project open; `Changed` is raised once per call that changes any fact, the raw theme included. `ShellViewModel` moves to WP-A16 with `ShellView`, whose hosts give `CurrentView` something to switch between; with the one placeholder view it would never change, and a view model may not take `NavigationState` (INV-ARCH-3), so `NavigationState` will follow the shell's view model rather than the reverse.

Scroll: `HomeView` is created once and kept (hidden, not destroyed) while another view shows, so its `ScrollViewer` keeps its offset by construction; `OnEnter` resets the list controls (EDGE-HOME-3), rebuilds `Items`, and then restores the saved offset after layout (a one-shot `LayoutUpdated` handler on the Home `ScrollViewer` that calls `ScrollToVerticalOffset(saved)` and unsubscribes; clamped by WPF; the App helper `UiDeferral` is the sanctioned alternative for a deferral at a named priority, R-ARCH-18, ARCHITECTURE 6.3. Not a raw `Dispatcher.BeginInvoke`, which the App's banned-symbol list forbids outside the allowlisted `WpfUiDispatcher.cs`, `StaRenderThread.cs` and `UiDeferral.cs`, ARCHITECTURE 14.9, and not `IUiDispatcher.Post`, whose `Normal` priority runs before layout), where `saved` was recorded from `ScrollChanged` while Home was visible (parity with 2.19's "record as it scrolls"). `SettingsView` is created fresh on every open (so it starts at the top and at the AI tab, and re-reads status, parity), and 05 scrolls its report to the top on open. IMPROVEMENT only for Settings-from-project (EDGE-HOME-19).

As built in WP-A16: `ShellViewModel(HomeViewModel home, AppMenuViewModel menu, INoticeService notices)` derives `CurrentView` from `SettingsOpen` and the open project in one step per transition, and raises `NavigationChanged` once, after every fact of the transition is set. `NavigationState.Follow(shell)`, which the composition root calls once, sets the navigation facts from it, so Settings over a project reports the project view hidden (8.3). The inputs land with their views: `ShowProject` and `CloseProject` with the project view (WP-A17), `OpenSettings` and `CloseSettings` with Settings (WP-B10), which also wires 03's menu requests; the header's `⚙ Settings` button runs the menu's `OpenSettingsCommand` until then. The Recording view and INV-HOME-18 join with the capture state (WP-B9a). `Start` enters Home once, after the main window is shown (startup step 8); from then on leaving Home calls `OnLeave` and entering it `OnEnter`, for Settings too (D-HOME-28). `ShellView` is the header row, the three hosts in one cell (Home made once and kept) and the overlay layer over both rows, whose notice host sits in the content row through a shared row size. `ScrollMemory` is as specified; its `Enter` also invalidates the scroller's arrange, so a layout pass, and the restore with it, always follows. Corrected in WP-A16: the header shows whenever no project is open and hides while one is, Settings over it included (`App.tsx:519`, `!showDetail`); 7.6's "hidden when `CurrentView == Project`" missed the Settings-over-a-project case.

As built in WP-A18: the open project's pin follows its session. `ShellViewModel` watches the project view's `RawProjectTheme` and, while the project the view has open is the one the shell shows, takes a new value and raises `NavigationChanged` (one transition); during a Back or another open the paths differ, and `ShowProject` or `CloseProject` sets the pin. So a View, Brand choice, its rollback and a durable result that carries another pin all reach `NavigationState` at once, and through it the theme and the menu. `NavigationState(ILogger<NavigationState>)` raises `Changed` through 11's `EventRaiser`: each subscriber runs in its own try/catch, and a failure is logged at Warning as `event handler failed: Changed`, so a menu or theme that fails to follow never fails the edit (8.3).

As built in WP-B9a: `ShellViewModel(HomeViewModel home, ProjectDetailViewModel project, AppMenuViewModel menu, INoticeService notices, IConfirmService confirm, ICaptureService capture, IProjectService projects, IUiDispatcher ui)` follows 02's capture state: it subscribes to `StateChanged`, then reads `GetState()`, so a session already under way shows at once, and each change posts a handler that reads the state again (11 T7). While a session exists (recording or paused) `CurrentView` is `Recording`, which outranks Settings, the project and Home (INV-HOME-18); `OpenSettings` is ignored; the header shows, as Electron's `showDetail` is false while recording, without its Settings button; and the hero's `IsRecording` is set. The container disposes the shell, which leaves the engine's event then. The `Recording` view's host holds the recording panel from WP-B9b; until then it is empty, and the main window is hidden during a session anyway (03 7.4.6).

The shell is also the capture coordinator (7.6, 05 7.14). `CaptureFromHomeAsync` (2.5's `onCreate`: nothing while busy, during a session or while the mode is not ready) and `CreateEmptyProjectAsync` (`onCreateEmpty`: nothing while busy or during a session) share one create: busy throughout (the hero's `IsBusy` follows), the error notice cleared, `IProjectService.CreateProjectAsync(JsString.Trim(title))`, the name box cleared, a user refresh of Home, whose failure shows its notice and stops neither flow (D-HOME-36), then the record or the open; a failure shows the error notice. Resume capturing (05 2.3) records into the open project with the target its event carries, and does nothing during a session. Each record is one `RecordAsync(path, target, createdThisSession, insertAt)`: `OpenProjectAsync(path)` (restoring an archived project), `StartAsync(path, new CaptureStartOptions(target ?? Home.Mode.BuildTarget(), createdThisSession, insertAt))`, the state read again rather than taken from the start's result, 05's `AdoptAsync(dir, manifest)` unless that project is open already, and `ShowProject`, which closes Settings (EDGE-HOME-40). So Home's target is read at the start, after the open, as `onRecord` read `buildTarget()`, and Resume's is the one read at the click. A failure of the open or the start shows the notice and starts nothing, and a project created before it stays (EDGE-HOME-18). Resume leaves the open session as it is, since it holds every edit. When a session ends with a project shown, the shell calls 05's `ReloadAsync`, which reads the project again into its session (EDGE-REP-43); when the project is gone, as after a Discard of a new one, Home shows with nothing said, and another failure shows Home with the notice. A session the engine ended before the start's continuation ran leaves no Recording view up: its events were handled first, the state read again is idle, and the project, shown, is read again. The header's visibility is raised when the recording state changes, so a repeated derivation raises nothing.

### 7.8 Overflow menu and tour overlay

**OverflowMenu** (`ShotAI.App.Chrome`, a `UserControl`): a trigger `Button` (default content `⋯`, `ToolTip` "More actions", style `Button.Icon`) and 03's `ShotAIPopup` (INV-HOME-43; `StaysOpen = false`, `Placement = Custom` with a `CustomPopupPlacementCallback` that aligns the popup's right edge to the trigger's right edge, 4 DIP below, and flips above when `below < estHeight && top > below` using 2.20's `items * 34 + 16` estimate against the WINDOW's client area, not the screen; parity). Items: `MenuItemModel` records (`Separator`, `Header(string)`, `Action(string label, ICommand, bool danger, bool enabled)`) rendered with the `MenuItem.Base` and `MenuItem.Danger` styles and headers with `MenuHeader` (corrected in WP-A14 from `Button.MenuItem`: 7.5's catalog names the styles). Opening focuses the first enabled item; Up and Down move, Home and End jump, Enter and Space activate, Escape and Tab close and return focus to the trigger (IMPROVEMENT: Electron has no keyboard menu). A click on an item closes the popup first, then executes (parity). Automation: the trigger gets a custom `AutomationPeer` (a `ButtonAutomationPeer` subclass implementing `IExpandCollapseProvider`, raising the `ExpandCollapseState` property change on open and close), because a `ToggleButton`'s peer exposes the Toggle pattern, not ExpandCollapse; popup items report `AutomationControlType.MenuItem` through their own peer. Electron's reason for a custom popover (native popups unreliable on the software-render VM) is ELECTRON-ONLY; the WPF popup is a native window and fine, provided it is a `ShotAIPopup`. The `OverflowMenu` is the one Home popover that R-ARCH-19 names as a sanctioned `ShotAIPopup` (ARCHITECTURE 5.4); every other new popover of this subsystem is an overlay-layer element (the target dropdown, 7.6). 05's report menus are overlay-layer elements of their own (05 7.13) and do not reuse this control.

As built in WP-A19a: `MenuItemModel` is an abstract record whose `Separator`, `Header(label)` and `Action(label, command, parameter, danger, enabled)` make a `MenuSeparatorItem`, a `MenuHeaderItem` and a `MenuActionItem`; an action runs its command with its parameter. The popup takes `Placement` `Bottom` or `Top` with offsets from the measured popover, in DIPs, instead of a custom placement callback: the popover's right edge meets the trigger's, 4 DIP away, inside a margin that leaves the menu shadow room. The items are built in code on the first open after `Items` changes; Up and Down wrap; when the popover closes with the focus in it, the focus goes to the trigger. The trigger is an `OverflowTrigger` and each item an `OverflowMenuItem`, each with the peer 7.8 names; `Label`, `Title` and `TriggerStyleKey` (`Button.Icon` by default) set the trigger.

**TourOverlay**: a full-size layer in the overlay host containing (1) a transparent hit-test `Border` whose click finishes; (2) a `Path` with `Fill = FixedColors.TourDim` and `Data = CombinedGeometry(Exclude, RectangleGeometry(full), RectangleGeometry(spot, RadiusValue.card, RadiusValue.card))`, plus a 3 DIP `Brush.accent` rounded `Border` at `spot`; or, with no spot, a full `Rectangle` in the dim colour; (3) the bubble `Border` placed from `TourLayout.Place` in overlay coordinates (for `BubbleBottom`, `Canvas.Bottom`), with the caret as a rotated 14 DIP `Border`. Anchors are elements carrying the attached property `TourAnchor.Id` (`Hero`, `Capture`, `Mode`, `Settings`); for each step the overlay calls `element.BringIntoView()` (IMPROVEMENT, EDGE-HOME-43), waits for layout, then measures `element.TransformToVisual(overlayRoot).TransformBounds(new Rect(element.RenderSize))`; it re-measures on the window's `SizeChanged` and on `ScrollChanged` of the Home scroller (parity with `resize` and capture-phase `scroll`), and on the anchor's `LayoutUpdated`, coalesced to one measure per pass (IMPROVEMENT, EDGE-HOME-56). Spot geometry animates 0.25 s unless animations are off. Focus: on open and on each step the overlay focuses its primary button; `KeyboardNavigation.TabNavigation = Cycle` inside the bubble; `Escape`, `Right` and `Left` are handled in the overlay's `PreviewKeyDown` only (IMPROVEMENT, EDGE-HOME-33). `AutomationProperties.Name = "Getting started"`, and the step line is a live region (`AutomationProperties.LiveSetting = Polite`; WPF announces a live region only when the code raises `AutomationEvents.LiveRegionChanged` on its peer after the text changes, so the overlay does that on each step).

**TourViewModel**: `IsOpen`, `Index`, `Step`, `Next`, `Back`, `Finish`. `Finish` is idempotent per presentation (a `presentationId` compare-and-set) and calls `settings.UpdateAsync(s => s with { HasSeenTour = true })` once, fire and forget through an explicit `_ =` with a continuation that logs a failure at warning (VSTHRD110, 11 7.12; parity with the swallowed Electron write) (INV-HOME-20). First run: after the main window's first `ContentRendered`, if `!settings.Current.HasSeenTour`, set the tour pending (`IsOpen = true`); it is SHOWN only while `CurrentView == Home`, so a launch that lands elsewhere first (a second-instance activation that opens a project, Settings via `Ctrl+,`) still shows it on the first visit to Home (parity with Electron's `tourOpen` flag, which is set on mount and rendered only on Home). Replay: `ShellViewModel.ReplayTour()` closes Settings and the open project (05 close) and opens the tour on Home (IMPROVEMENT, EDGE-HOME-20). The tour is shown only while `CurrentView == Home`; leaving Home hides it and resets `Index` to 0 on return (parity).

### 7.9 Keyboard (native)

| Where | Key | Effect | Class |
|---|---|---|---|
| Create name box | Enter | Capture (if enabled) | REQUIRED |
| Home | Ctrl+F | focus the search box | IMPROVEMENT (macOS has Cmd+F) |
| Search box | Escape with text | clear text, handled | REQUIRED |
| Home | Escape | innermost first: open popup, rename, search text, selection (EDGE-HOME-6) | IMPROVEMENT (order) |
| Rename box | Enter / Escape / focus loss | commit / cancel / commit, once | REQUIRED |
| Mode chips, Settings radio groups | Arrow keys | move selection within the group | IMPROVEMENT |
| Home tabs | Left / Right | switch tab (they become a `TabControl`-like `RadioButton` pair with roving focus) | IMPROVEMENT |
| Row checkbox | Space; Shift+Space | toggle; range-select (`ShiftClick` when `Keyboard.Modifiers` has Shift) | REQUIRED (Chromium's click with shift) |
| Overflow and target popups | Up, Down, Home, End, Enter, Space, Escape, Tab | 7.8 (overflow), 7.6 (target dropdown) | IMPROVEMENT |
| Confirm | Enter / Escape / Tab | activate focused button (confirm initially) / cancel / cycle inside | REQUIRED, IMPROVEMENT (cycle) |
| Settings tabs | Arrows, Home, End | 2.24 roving pattern | REQUIRED |
| Tour | Escape / Right / Left | finish / next / back, inside the tour's focus scope | REQUIRED (keys), IMPROVEMENT (scope) |
| App | Ctrl+O, Ctrl+, | 03 menu accelerators | REQUIRED (03) |

### 7.10 Notices

```csharp
public interface INoticeService
{
    void ShowError(string message);               // replaces the current error (parity)
    void ShowError(Exception e);                  // text = 11's UserMessage.From(e): null (cancellation) shows nothing;
                                                  // ShotAIException, IOException, UnauthorizedAccessException show Message;
                                                  // anything else shows UserMessage.Generic and is logged at Error
    void ClearError();                            // start of a user-initiated operation
    void ShowUpdate(string version, Uri? downloadUrl);   // idempotent per launch
    void DismissUpdate();
}
```

`NoticeCenter` (UI thread) holds `Error` and `Update` view models; `NoticeHost` is an `ItemsControl` in the overlay layer, pinned 0.6 rem below the top of the content area (IMPROVEMENT, EDGE-HOME-22), centred, max width `min(92% of the content width, 680)`, with the 2.21 look from `FixedColors.xaml`, `IsHitTestVisible` only on the notices. Each notice: `TextBlock` (wrap), an optional link `Hyperlink`-styled `Button` (`Open the download page`), and a `×` button with `AutomationProperties.Name = "Dismiss"`. Error notices use `AutomationProperties.LiveSetting = Assertive`, info `Polite` (IMPROVEMENT: Electron announces errors politely); WPF does not announce a live region by itself, so `NoticeCenter` raises `AutomationEvents.LiveRegionChanged` on the notice's peer each time it shows or replaces a notice. Entry animation 140 ms. The update notice: the shell FIRST subscribes to 10's `IUpdateService.UpdateAvailable` (handler posts through `IUiDispatcher.Post`) and THEN reads `IUpdateService.Pending` (11 T7 subscribe-then-read, U1 and E4); whichever yields an available result first shows it, once per launch (INV-HOME-35); the link opens through `IExternalLinks.OpenAsync(string url, CancellationToken ct = default)` (`ShotAI.Core.Links`, 11 7.3.4, registered by 10): it returns `false` when the URL is refused and throws only if the shell launcher throws, so the notice's handler wraps the call in a `try`, logs a throw at Warning and shows nothing (R-ARCH-25; an update nudge is never worth an error, `App.tsx:70`; INV-HOME-31). The Settings links (`console.anthropic.com`, `request access`, the `Check now` release page) use the same wrapper. Rollback failures from optimistic writes (fixed rule) use `ShowError` with the store's or settings service's message.

The update notice depends on the install scope (INV-HOME-45): `NoticeCenter` takes 12's `IInstallInfo` (`ShotAI.Core.Install`, 12 7.10.4, registered as the instance built at startup). When `Scope` is `PerMachine`, the update notice's text is `SettingsText.UpdateAvailable(version, perMachine: true)` and it has no link button, whatever `downloadUrl` is; for `PerUser` and `Unpackaged` it is `SettingsText.UpdateAvailable(version, perMachine: false)` with the `Open the download page` button (parity). The `×` button, the once-per-launch rule and the dismissal are the same in both.

Error wording: the Electron UI shows raw `Error.message`. Natively the services throw exceptions whose `Message` is the same user-facing text Electron's main process produced (01, 07, 08, 09 own those strings); unexpected exceptions show `Something went wrong. See the log for details.` and log the details (03 Q-SHELL-16 wording, confirmed here; IMPROVEMENT, because a .NET `Message` for an unexpected exception is not user text).

As built in WP-A16:
- `INoticeService` has `ShowError(string)`, `ShowError(Exception)` and `ClearError()`. `ShowUpdate`, `DismissUpdate`, the update slot and `IInstallInfo` join with the update check (WP-E1), and `NoticeCenterTests`' "update shown once per launch" moves there.
- `NoticeCenter` is a singleton service, not a view model: it holds `Notices` (read-only, top first), `Error` and `DismissCommand`. A newer error replaces the text of the notice shown, which stays. `ShowError(Exception)` shows `UserMessage.From`'s text and, when 11's `UserMessage.IsUnexpected` holds (added in WP-A16), logs Error `notice: unexpected error, shown as the generic message:` with the exception (7.16).
- `NoticeHost` renders `Notice.Template` from `Controls.xaml`. The notice's 8 DIP corners and drop shadow are `notice.css`'s own, so `XamlChromeGuard` exempts the `Notice.` keys.
- The announcement is the view's, not `NoticeCenter`'s, which has no element. The notice text carries `LiveRegion.Text`, an attached property bound to the element's own `Text`: each change raises `LiveRegionChanged` on the element's peer, and a new notice's first text is announced once it is loaded. `Binding.TargetUpdated`, tried first, fired before the text arrived.
- The host takes no clicks beside a notice: it has no background, so the view below gets them (`NoticeCenterTests.ClicksPassBesideTheNotice`).

### 7.11 Confirm

```csharp
public interface IConfirmService
{
    Task<bool> ConfirmAsync(string message, string confirmLabel = "OK", bool danger = false, CancellationToken ct = default);
    Task AlertAsync(string message, CancellationToken ct = default);
}
```

`ConfirmHost` in the overlay layer: scrim `FixedColors.ConfirmScrim`, card per 2.23, `Cancel` then the confirm button (`Button.Danger` or `Button.Primary`), `AutomationProperties.Name = "Confirm"`, `FocusManager.IsFocusScope = true`, `KeyboardNavigation.TabNavigation = Cycle`, initial focus on the confirm button (parity), `PreviewKeyDown`: Escape resolves false (handled), Enter invokes the focused button. The page behind is not hit-testable while open. A second request while one is open first resolves the open one with `false`, then shows (IMPROVEMENT: Electron leaves the first promise pending forever). Cancellation resolves false. `AlertAsync` has only `OK`. The Electron reason (native `confirm` steals keyboard focus, B4) is ELECTRON-ONLY; an in-window overlay is kept because it follows the brand and never leaves the window.

As built in WP-A19a: `ConfirmService` (a singleton on the UI thread) holds the question shown, a `ConfirmRequest` (`Message`, `ConfirmLabel`, `Danger`, `HasCancel`), and answers it once through its `ConfirmCommand` and `CancelCommand`; a cancellation is posted to the UI thread, and a token already cancelled answers false without showing anything. `ConfirmHost` is in the overlay layer over the header and the views, under the notices (Electron's z 50 and 60); the confirm button takes the focus after the first layout pass, the scrim takes every click, and when the last question closes the focus goes back to the element that had it. The host is collapsed while no question is shown, and until its binding has a service to read, so it takes no click then; `ConfirmServiceTests` checks what a click reaches with `InputHitTest`, as the mouse does, because a bare visual hit test also finds a collapsed element. `ShellViewModel` takes `IConfirmService` for the host. The card's shadow colour is `FixedColors.ConfirmShadow`.

### 7.12 Settings

**SettingsViewModel** reads `ISettingsService.Current` (10: an immutable `AppSettings` snapshot loaded synchronously at startup) and writes with `ISettingsService.UpdateAsync(Func<AppSettings, AppSettings>, ct)` (10 applies the 2.30 coercions to `Current` at once, raises `Changed`, serializes the atomic write, preserves unknown keys, returns the stored snapshot; on a write failure 10 itself restores the last persisted snapshot, raises `Changed` with `IsRollback = true` and faults the task, 11 7.3.6). Every control is optimistic: call `UpdateAsync` (skipped when the new value equals `Current`, D-HOME-31); the view model subscribes to `Changed` (through `IUiDispatcher.Post`) and always displays `Current`, so coercion and rollback both reach the control without a private copy of the old value; on the faulted task it calls `ShowError` (INV-HOME-28; IMPROVEMENT over the mixed Electron timings, EDGE-HOME-28). The Settings inline `Error: ...` block is kept for failures of the non-settings calls on this page (key save or clear, sign in or out, status loads), parity.

Per control (strings from `SettingsText`, keys from 2.30):

| Control | WPF | Notes |
|---|---|---|
| Tabs | five `RadioButton`s styled `TabUnderline` with roving focus (arrows, Home, End; selection follows focus) and one content presenter; UIA: `Tab` and `TabItem` control types via custom `AutomationPeer`s | INV-HOME-39; default AI each open |
| AI switch | `CheckBox` `SettingsToggleCard` | hides the rest when off |
| Microsoft sign-in group | shown when `auth.Status.FederationAvailable && sop.Enabled` | 08's `IAuthService` and value types are canonical (08 7.12, R-ARCH-2; `AuthStatus.Mode` is the enum `AuthStatusMode`, never a string): `GetStatusAsync(ct)` on every Settings open and on `AuthStatusChanged` (INV-HOME-42), `SignInAsync(ownerWindow, ct)` (`ownerWindow` = `new WindowInteropHelper(Application.Current.MainWindow).Handle`, read on the UI thread, 08 7.12, 11 7.3; WAM, interactive every time; `SignInOutcome.Canceled` shows nothing, 08 EDGE-AUTH-11), `SignOutAsync(ct)` |
| Key fallback toggle | an `Expander` restyled as the `btn btn--small` disclosure (its peer implements ExpandCollapse; a `ToggleButton` would expose only Toggle), header text `▸`/`▾` plus `Use my own Anthropic API key instead` | not persisted |
| Key field | `PasswordBox` (no binding of `Password`; read once on Save and then `Clear()`) | INV-HOME-30; disabled per 2.25; `IAuthService.GetApiKeyStatusAsync`, `SetApiKeyAsync`, `ClearApiKeyAsync` (08 7.12, R-ARCH-2); `IApiKeyStore` and 07's `VerifyFederationAsync` are internal to Core and never reach a view model (INV-ARCH-3) |
| Test connection | 08's `IAuthService.TestConnectionAsync(ct)`, the one public entry point (R-ARCH-3; there is no `IClaudeService.TestConnectionAsync`); expected failures come back in the result, never thrown, 11 X7 | message from `SettingsText.ConnectionMessage` |
| Model, Tone, Effort | `RadioButton` groups `Chip` with `AutomationProperties.Name` "Model", "Tone", "Effort" | |
| Custom instructions | `TextBox` (multi-line, 3 lines min, `MaxLength = 2000`, `AutomationProperties.Name = "Custom instructions (optional)"`) | persist on `LostKeyboardFocus` and on view `Unloaded`, once (EDGE-HOME-39) |
| Screenshot quality | `Slider` (`Minimum 0.5`, `Maximum 1`, `SmallChange 0.05`, `LargeChange 0.05`, `TickFrequency 0.05`, `IsSnapToTickEnabled`, `IsMoveToPointEnabled`, `AutomationProperties.Name = "Screenshot quality"`) | persist `CaptureScaleSteps.Snap(value)` on `Thumb.DragCompleted`, on `PreviewMouseUp`, and on `KeyUp` (parity with pointerup and keyup); label `SettingsText.CaptureScaleLabel` |
| Remote visibility | `SettingsToggleCard` | 11's `RemoteVisibilityApplier` (App, an `IAppStartup`) sees `ISettingsService.Changed` with a different `RemoteVisible` and applies it to the open windows through 02's `CaptureShield.ApplyRemoteVisibility` at once, including on a rollback (11 G4, D-IPC-8; INV-HOME-41; ARCHITECTURE 9.2 S7); the shield call never runs on the UI thread while it could take the shield's lock (ARCHITECTURE DL1: through `Task.Run`); this view model does not call the shield itself |
| Theme, Brand | two `RadioButton` groups named "Theme" and "Brand" | the theme manager re-applies immediately (optimistic) |
| Projects folder | path `TextBlock` (`TextTrimming = CharacterEllipsis`, full path in `ToolTip`) and `Change…` | `current = await projects.GetProjectsDirAsync()`; `dir = dialogs.PickFolder(mainWindow, "Choose shotAI projects folder", current)` (11's `IFileDialogs`, over `Microsoft.Win32.OpenFolderDialog`, .NET 8+); null returns; else `await projects.SetProjectsDirAsync(dir)` (01; creates the folder) then a user-initiated Home refresh (11 7.3.5) |
| Auto-archive | `ComboBox` (`AutomationProperties.Name = "Auto-archive age"`), items `ArchiveAgeOptions.For(stored)`; its drop-down is the theme's own, registered by 03's show hook before it is shown (INV-HOME-43; corrected in WP-A13, which closed 03 7.4.7's gap) | EDGE-HOME-37 |
| Your name | `TextBox` `MaxLength = 120`, `AutomationProperties.Name = "Your name"` | persist on focus loss and unload; then the include rule (INV-HOME-29) |
| Include my name | `SettingsToggleCard`, `IsEnabled = !string.IsNullOrWhiteSpace(liveName)` using JS trim semantics (`JsString.Trim(liveName) != ""`) | |
| About line | `SettingsText.AppInfoLine` | 7.12 below |
| Check for updates | `SettingsToggleCard` | optimistic with rollback and notice (IMPROVEMENT over the silent revert) |
| Check now | `Button` | 10's `IUpdateService.CheckNowAsync()`; message `SettingsText.UpdateCheckMessage(r, thrown, perMachine: install.Scope == InstallScope.PerMachine)` per INV-HOME-36; opens the URL through `IExternalLinks.OpenAsync`, wrapped as in 7.10 (R-ARCH-25), except on a per-machine install, which opens nothing (INV-HOME-45) |
| Show intro tour | `Button` | `ShellViewModel.ReplayTour()` |

The view model that owns `Check now` takes 12's `IInstallInfo` (the `install` above; 12 7.10.4).

**About line** (ELECTRON-ONLY replaced): `AboutSettingsViewModel` reads 11's `IAppInfo.Current` (`AppInfoProvider`, `ShotAI.App.Services`, 11 7.3.5) and passes its fields to `SettingsText.AppInfoLine`: `` $"{Name} {Version} · {Platform}/{Arch} · .NET {DotNetVersion}" ``, which reads `shotAI <version> · win32/x64 · .NET 10.0.x`; `Version` is the assembly informational version without the `+commit` suffix, `Arch` `x64` or `arm64` (`RuntimeInformation.ProcessArchitecture`, lower-case, matching Node's `process.arch`). `win32` is kept because support scripts and users already quote it. `IAppInfo.Current.WebView2Version` (from Platform's `IWebView2RuntimeInfo`; the App has no WebView2 reference, R-ARCH-12, INV-ARCH-5) is not shown in this line.

### 7.13 Accessibility (native)

Every interactive element gets an explicit `AutomationProperties.Name` equal to its Electron accessible name (2.37); in addition (IMPROVEMENT, EDGE-HOME-32): the create name box is named "Project name" with the placeholder as `AutomationProperties.HelpText`; sort chips are `RadioButton`s (selection state exposed) in a group named "Sort projects", and the direction button is a `ToggleButton` named "Sort direction" whose `HelpText` is `Ascending` or `Descending`; date-group headers are exposed as text with `AutomationProperties.HeadingLevel = Level3` and are not focusable; row checkboxes keep "Select <title>"; the bulk count is a live region (`Polite`, with `LiveRegionChanged` raised by the view on each text change, as for notices); the confirm dialog and tour trap focus; every icon-only button has a name. Focus order follows 2.36. Minimum hit target for icon buttons 24 x 24 DIP.

### 7.14 Threading, cancellation, disposal, persistence, errors

| Concern | Rule |
|---|---|
| Thread | All view models and the theme manager on the UI thread. Store, export, flatten, capture and settings calls are async and do their IO off the UI thread (01, 04, 09, 10); results are awaited back on the UI thread (default `SynchronizationContext`). Engine events (02), `IProjectService.ProjectsChanged`, `ISettingsService.Changed`, `IUpdateService.UpdateAvailable`, `IAuthService.AuthStatusChanged` and `SystemAppearanceMonitor` events are marshalled with 11's `IUiDispatcher.Post` only (T6, R-ARCH-11; `Dispatcher.Invoke`, `BeginInvoke` and `InvokeAsync` are banned in App code outside the allowlisted `WpfUiDispatcher.cs`, `StaRenderThread.cs` and `UiDeferral.cs`, ARCHITECTURE 14.9, R-ARCH-18); each handler checks its own disposal first and re-reads the snapshot (T7). Snapshot readers called synchronously on the UI thread are only those of ARCHITECTURE T9 (`ICaptureService.GetState`, `ISettingsService.Current`, `IUpdateService.Pending`, `IAppInfo.Current`, `IShellNavigationState`); `ICaptureService.Pause` and `Resume` go through `Task.Run`. |
| Cancellation | Home owns a `CancellationTokenSource` per operation: navigation away cancels a pending refresh (its result is dropped) but NOT a running row or bulk operation (parity: Electron kept running after unmount); app shutdown cancels everything (03 teardown). `BulkRunner` checks the token before each item. |
| Disposal | `HomeViewModel` stops its timer and unsubscribes from 01 (`ProjectsChanged`), 02, 05 and 10 events in `Dispose`; `SettingsViewModel` (created per open) unsubscribes from `ISettingsService.Changed` and 08's `AuthStatusChanged` when the view unloads, after its unload flush of blur-persisted fields (EDGE-HOME-39); every posted handler checks its owner's disposal first (11 T6); app shutdown tokens come from 11's `IAppLifetime.Stopping`; `SystemAppearanceMonitor` unsubscribes `SystemEvents.UserPreferenceChanged` (a static event that otherwise leaks). |
| Persistence | Only through 01 (project data) and 10 (`%APPDATA%\shotAI\settings.json`, the file the Electron build shares, ARCHITECTURE 10.1); this subsystem stores no files, so it adds nothing under `%LOCALAPPDATA%\LFI\shotAI` (R-ARCH-13). `hasSeenTour` through 10. Home list operations and every Settings control follow ARCHITECTURE 7.5 (list-level optimism over a queued `IProjectService` call; `ISettingsService.UpdateAsync` on the settings queue); the API key set and clear are awaited, not optimistic (INV-AUTH-29). |
| Errors | 7.10. No exception from a background refresh, a theme apply, a menu-state push or a tour flag write reaches the user as a failure of something they did (parity with the swallowed Electron calls); each is logged at warning. |

**SystemAppearanceMonitor** (`ShotAI.Platform.Theme`, `internal sealed`, implements Core's `ShotAI.Core.Theme.ISystemAppearance` and is registered only as that interface, INV-ARCH-4; ARCHITECTURE 2.3): `bool IsDark` reads `HKCU\Software\Microsoft\Windows\CurrentVersion\Themes\Personalize` DWORD `AppsUseLightTheme` with `Microsoft.Win32.Registry` (missing key or value = light, `0` = dark); `event EventHandler Changed` raised from `Microsoft.Win32.SystemEvents.UserPreferenceChanged` when `Category == UserPreferenceCategory.General` (the category Windows uses for the `ImmersiveColorSet` broadcast) AND the re-read value differs. No P/Invoke needed. `ThemeManager` subscribes only while `Theme == System` (parity with `watchSystemTheme`). Also `bool IsHighContrast => SystemParameters.HighContrast` is read by the App (Q-HOME-12). As built in WP-A14: `SystemEvents` is in the desktop runtime, which Platform does not reference, so Platform takes it from the `Microsoft.Win32.SystemEvents` package (ARCHITECTURE 3.2) and stays off WPF; the monitor is on the static event only while `Changed` has a handler, and `Dispose` takes it off; the value a change is compared with is the last one read or reported, so subscribing reads nothing and a start does no IO (11 7.10 rule 3); `Changed` is raised on the thread `SystemEvents` raises on, and the manager posts. The App reads `SystemParameters.HighContrast` inside `ThemeManager`, not through the seam.

### 7.15 Divergences from Electron

| # | Divergence | Class | Justification |
|---|---|---|---|
| D-HOME-1 | Refreshes coalesce and apply only the newest result | IMPROVEMENT | EDGE-HOME-4: an older listing could overwrite a newer one |
| D-HOME-2 | Tick paused while renaming, selecting or busy, not by a focused checkbox | IMPROVEMENT | EDGE-HOME-9, macOS rule |
| D-HOME-3 | Background refresh does not clear the error notice | IMPROVEMENT | EDGE-HOME-21 |
| D-HOME-4 | Notice stack pinned to the visible area | IMPROVEMENT | EDGE-HOME-22 |
| D-HOME-5 | Selection pruned after refresh | IMPROVEMENT | EDGE-HOME-8, EDGE-HOME-13 |
| D-HOME-6 | Bulk bar stays visible and `Clear` disabled while a bulk op runs | IMPROVEMENT | EDGE-HOME-12 |
| D-HOME-7 | Home rename, archive, restore, delete are optimistic with rollback and notice | IMPROVEMENT | fixed write rule |
| D-HOME-8 | Every Settings write optimistic with rollback and notice | IMPROVEMENT | fixed write rule; EDGE-HOME-28 |
| D-HOME-9 | Settings values from the in-memory service; per-group status loads | IMPROVEMENT | EDGE-HOME-25 |
| D-HOME-10 | Theme applied before the first frame | IMPROVEMENT | EDGE-HOME-42 |
| D-HOME-11 | Escape acts on the innermost surface only | IMPROVEMENT | EDGE-HOME-6 |
| D-HOME-12 | Keyboard navigation in menus, target dropdown, radio groups, Home tabs; Ctrl+F | IMPROVEMENT | accessibility; macOS Cmd+F |
| D-HOME-13 | Confirm and tour trap focus; a superseded confirm resolves false | IMPROVEMENT | EDGE-HOME-32, 2.23 |
| D-HOME-14 | Failed open shows an error on Home | IMPROVEMENT | EDGE-HOME-24, 05 EDGE-REP-39 |
| D-HOME-15 | Unparseable meta date shows `\u2014` | IMPROVEMENT | EDGE-HOME-17 |
| D-HOME-16 | Settings from a project opens at the top | IMPROVEMENT | EDGE-HOME-19 |
| D-HOME-17 | Replay tour closes the project and shows the tour at once | IMPROVEMENT | EDGE-HOME-20, macOS |
| D-HOME-18 | Tour brings each anchor into view | IMPROVEMENT | EDGE-HOME-43 |
| D-HOME-19 | Successful open or import closes Settings | IMPROVEMENT | EDGE-HOME-40 |
| D-HOME-20 | Off-list archive age shown as `After N days` | IMPROVEMENT | EDGE-HOME-37 |
| D-HOME-21 | Blur-persisted fields also persist on unload | IMPROVEMENT | EDGE-HOME-39 |
| D-HOME-22 | About line names .NET instead of Electron | ELECTRON-ONLY replaced | no Electron |
| D-HOME-23 | Custom popovers, DOM modal, token `<style>` sheet, `data-theme` attributes, StrictMode, `webContents.send` | ELECTRON-ONLY | replaced by overlay-layer elements (the target dropdown, `ConfirmHost`, notices, tour) and, only where WPF needs a popup HWND, `ShotAIPopup` (`OverflowMenu`, the `ComboBox` drop-down) per R-ARCH-19; `ThemeManager` dictionaries, the dispatcher, 10's stash plus event |
| D-HOME-24 | Error notices announced assertively | IMPROVEMENT | errors are urgent |
| D-HOME-25 | Date buckets rebuilt once a minute | IMPROVEMENT | midnight and Sunday rollover without a list change |
| D-HOME-26 | Unexpected exceptions show a generic sentence, details in the log | IMPROVEMENT | .NET messages are not user text |
| D-HOME-27 | High-contrast mapping | IMPROVEMENT | EDGE-HOME-45 (Q-HOME-12) |
| D-HOME-28 | Settings counts as leaving Home for the tick and the activation refresh; returning refreshes | IMPROVEMENT | EDGE-HOME-48 |
| D-HOME-29 | Background refresh failures are logged, not shown; user-initiated refresh failures show the notice | IMPROVEMENT | EDGE-HOME-49 |
| D-HOME-30 | Text fields use `Brush.field-bg` and `Brush.ink` | IMPROVEMENT | EDGE-HOME-53; WPF has no UA field colours to inherit |
| D-HOME-31 | A Settings control writes only when its value changed; the test result clears only on a real write | IMPROVEMENT | EDGE-HOME-55 |
| D-HOME-32 | The tour re-measures its anchor on layout changes | IMPROVEMENT | EDGE-HOME-56 |
| D-HOME-33 | A bulk run's final refresh failure is reported; selection cleared and folder revealed regardless; bulk commands with no visible target show no dialog | IMPROVEMENT | EDGE-HOME-50, EDGE-HOME-51 |
| D-HOME-34 | The week starts at the resolved Sunday midnight even when today's midnight is in a DST gap | IMPROVEMENT | EDGE-HOME-58 |
| D-HOME-35 | On a per-machine install the update notice and `Check now` say who installs updates and offer no download page | IMPROVEMENT | INV-HOME-45; 12 EDGE-PKG-65 |
| D-HOME-36 | A failed list refresh after a create shows its notice and stops neither the recording nor the open that follows (added in WP-B9a) | IMPROVEMENT | Electron's rejected `refresh()` skipped `onRecord` or the open and left an empty project; the list read and the new project are unrelated |

---

### 7.16 Native log lines (added in WP-A16)

| Level | Text |
|---|---|
| warn | `home: background refresh failed:` with the exception (D-HOME-29) |
| error | `notice: unexpected error, shown as the generic message:` with the exception (D-HOME-26) |

## 8. Tests

Test projects: `tests/ShotAI.Core.Tests` (Linux and Windows, exists), `tests/ShotAI.App.Tests` (Windows only, `net10.0-windows10.0.19041.0`, `UseWPF`, added by the plan; view-model and control tests run on a dedicated STA thread with its own `Dispatcher`, Q-HOME-15), `tests/ShotAI.Platform.Tests` (Windows only, added by the plan).

### 8.1 `src/renderer/project/date-groups.test.ts` (63 lines)

Purpose: pins the five-bucket boundaries and the grouping contract. Reference now: `new Date(2026, 6, 22, 10, 0, 0)` (Wed 2026-07-22 10:00 local); sample instants at local noon. Ports to **ShotAI.Core.Tests** (Linux), class `ShotAI.Core.Tests.Home.DateGroupsTests`, with the zone passed explicitly (a custom zone with a fixed offset, so the run does not depend on the machine's zone).

| Electron case | Assertions | C# test |
|---|---|---|
| `bucketFor` / places the current week under This Week (incl. the Sunday start) | 07-22, 07-20, 07-19 are This Week | `CurrentWeekIncludingSunday` |
| places the prior week under Last Week | 07-18, 07-12 are Last Week | `PriorWeekIsLastWeek` |
| places earlier-this-month dates under This Month | 07-11, 07-01 | `EarlierThisMonth` |
| places the previous calendar month under Last Month | 06-30, 06-01 | `PreviousCalendarMonth` |
| places anything older under Older, and handles bad input | 05-31, 2025-01-01, `NaN` (C#: `null` instant) | `OlderAndBadInput` |
| buckets are mutually exclusive across all five spans | 07-22, 07-14, 07-05, 06-15, 03-01 map to the five labels in canonical order | `MutuallyExclusiveInCanonicalOrder` |
| `groupByDate` / emits only non-empty buckets in canonical order, preserving item order | items a (07-22), b (07-20), c (06-15): groups This Week [a, b], Last Month [c] | `GroupEmitsNonEmptyInCanonicalOrder` |
| returns an empty array for no items | `[]` | `GroupOfNothingIsEmpty` |

### 8.2 `src/renderer/project/app-chrome-tokens.test.ts` (167 lines)

Purpose: the app chrome follows the brand only if it reads tokens; bans colour and radius literals in the project window's stylesheets outside two reasoned, selector-scoped exceptions, bans chromatic `rgb()`/`rgba()`, and bans literal `var()` fallbacks. The CSS it scans is ELECTRON-ONLY; its INTENT ports as a source scan of the WPF app (stricter than Electron in one respect, IMPROVEMENT: the Electron guard does not ban CSS named colours or `hsl()`, the native one bans named colours other than `Transparent`), which reads text files only and so runs on Linux: **ShotAI.Core.Tests**, class `ShotAI.Core.Tests.SourceGuards.XamlChromeGuardTests` (locates the repo root by walking up to `dotnet/ShotAI.slnx`; scans `dotnet/src/ShotAI.App/**/*.xaml` and `**/*.cs`; strips XML comments and C# comments first, since prose may quote hex on purpose).

| Electron case | Native case | Rule |
|---|---|---|
| `${f} reads every colour from a token` (project.css, editor.css) | `XamlNamesNoColourOfItsOwn` (one data row per file) | no `#[0-9A-Fa-f]{3,8}\b`, no `sc#` value and no named colour other than `Transparent` in any attribute value or element text (identifier attributes such as `x:Key`, `x:Name` and a setter's `Property` aside), except inside `Themes/FixedColors.xaml` and inside elements whose `x:Key` starts with an exempt prefix (`TourPill`, `RepMarker`). As built in WP-A14 the file is the one exemption: each key prefix joins with the element it exempts (`TourPill` with the tour, WP-B10; `RepMarker` with 05's markers), since a listed exemption with nothing to exempt fails |
| (same, extended) | `CodeNamesNoColourOfItsOwn` | no `Color.FromRgb(`, `Color.FromArgb(`, `Colors.<Name>`, `Brushes.<Name>` (except `Transparent`), or `ColorConverter.ConvertFromString("#` in App `.cs` files except `Chrome/ThemeResources.cs` (which builds from `ThemeTokenSet`) |
| records a reason for every exception, and each is still real | `ExceptionsHaveReasonsAndStillExist` | every exemption (file or key prefix) has a reason string longer than 40 characters in the test, and each exempt file or key still exists in the scanned sources |
| lets no rule hardcode a corner radius | `NoLiteralCornerRadius` | `CornerRadius`, `RadiusX`, `RadiusY` values (attributes, property elements and setters) must be `{DynamicResource Radius.*}`, `{DynamicResource RadiusValue.*}`, a binding through `CapsuleCornerConverter`, `0`, `2` or `3` (the hairline allowance; each part of a four-part value), or inside the exemptions; a circle is an `Ellipse`, never `50%`. Added in WP-A14: `Radius.chip` is never read and `RadiusValue.chip` is never a radius value, because a capsule is the converter's (7.5) |
| names no COLOURED rgb()/rgba() either | `FixedColoursAreNeutralUnlessReasoned` | every colour in `FixedColors.xaml` whose channel spread exceeds 40 must be one of the reasoned chromatic entries (notice fills, tour pill); scrims and shadows must be neutral. As built in WP-A14 the reasoned keys are the three notice fills; the tour pill's join with the tour (WP-B10) |
| leaves no dead var() fallback that silently outranks the token | `NoLiteralFallbackOnThemeBindings` | no `FallbackValue` or `TargetNullValue` holding a colour literal, and no `{StaticResource Brush.*}` (a static read of a theme key is the WPF form of a value that silently outranks the live token) |

As built in WP-A14 each rule is a function of the sources (`SourceGuards/XamlChromeGuard`), so the tests also apply AC-HOME-3's five mutations, and more spellings of each, to in-memory copies of the real files and check each is reported; `TheScanSeesTheAppSources` fails an empty scan.

Related (owned by 10, listed so nothing is lost): `theme-palette.test.ts` "declares no colour custom property of its own", "does not redeclare a token the generator owns", "emits a fully-qualified block for every brand and appearance", "emits a bare :root fallback", "resolves every var(--…) the project window reads". Natively the first four become `ThemeTokenSetTests` below and the last becomes `XamlResourceKeyGuardTests`.

### 8.3 `src/renderer/project/theme-wiring.test.ts` (341 lines)

Purpose: asserts from source the seams no typecheck notices: both theme attributes written, the sheet installed before the first render, the brand passed and re-applied, the project brand's precedence (not in Settings), Settings wired to App, two separate controls; the View then Brand menu's state push, subscription, pass-through of `null`, rebuild rules; the unrecognised-pin flag end to end (#107). Source scans of TypeScript are ELECTRON-ONLY as a technique; each case's intent maps to a behavioral native test.

| Electron case | Native | Project and class |
|---|---|---|
| applyTheme writes both data-theme and data-brand | the merged dictionary is always the (brand, appearance) pair; a brush from each axis checked | App.Tests `Chrome.ThemeManagerTests.AppliesBrandAndAppearanceTogether` |
| applyTheme installs the generated token sheet | `ApplyInitial` merges exactly one theme dictionary built from `ThemeTokenSet` | App.Tests `ThemeManagerTests.ApplyInitialMergesOneDictionary` |
| the renderer entry installs the sheet before the first render | theme applied before `MainWindow.Show()` | App.Tests `Shell.ShellStartupTests.ThemeAppliedBeforeMainWindowShown` (composition-root hook records order) |
| App passes a brand to applyTheme, not just the appearance | brand change re-applies | App.Tests `ThemeManagerTests.BrandChangeReapplies` |
| App re-applies when the brand changes, not only the appearance | appearance change and brand change each re-apply; no re-apply when neither changed | App.Tests `ThemeManagerTests.ReappliesOnEitherAxisOnly` |
| an open project s own brand outranks the app preference (#77 1b) | `Resolve(visible: true, pinned: "lfi", app: "shotAI") == "lfi"` | Core.Tests `Theme.ActiveBrandResolverTests.VisiblePinnedProjectWins` |
| keeps Settings on the app preference, even from inside a pinned project | `Resolve(visible: false, pinned: "lfi", app: "shotAI") == "shotAI"`; shell reports `ProjectViewVisible == false` while Settings shows over an open project | Core.Tests `ActiveBrandResolverTests.SettingsUsesAppBrand`; App.Tests `Shell.NavigationStateTests.ProjectViewNotVisibleInSettings` |
| App subscribes to the Settings brand picker | choosing a brand in Settings repaints at once (before the write returns) | App.Tests `Settings.SettingsViewModelTests.BrandChoiceRepaintsImmediately` |
| Settings offers appearance and brand as two separate controls | UIA shows two radio groups named `Theme` and `Brand` | App.Tests `Settings.SettingsViewTests.AppearanceAndBrandSeparate` |
| App pushes the state, and pushes it on every input that changes it | `NavigationState.Changed` fires on project open, close, raw theme change (an unrecognised pin included, which is how the flag's change arrives), Settings open and close over a project. Corrected in WP-A18: the app brand is not a navigation fact; the menu reads it from `ISettingsService` and follows its `Changed` (03 7.4.5), so its row is `AppMenuViewModelTests.BrandItemsFollowTheAppBrand` | App.Tests `NavigationStateTests.RaisesChangedOnEveryInput`; 03 `AppMenuViewModelTests.BrandItemsFollowTheAppBrand` |
| App pushes from the top level, not from the project view | the shell (not the project view) owns `NavigationState`; with no project open it reports `ProjectOpen == false` | App.Tests `NavigationStateTests.ReportsNoProjectWhenClosed` |
| App subscribes to the menu, or the items do nothing at all | 03: the menu command reads `OpenProjectPath` at execution | 03 `AppMenuViewModelTests.BrandChoiceCallsStoreForOpenProject` |
| passes the choice through untouched, null included | 03 plus 01: `null` clears, the default brand pins | 03 `AppMenuViewModelTests.BrandChoiceCallsStoreForOpenProject`; 01 `SetProjectThemeAsync` tests |
| main arms the rebuilder BEFORE the first menu build | ELECTRON-ONLY: a WPF menu binds to its view model and is never rebuilt, so there is nothing to arm | none |
| main coerces the pushed state instead of trusting it | ELECTRON-ONLY (no IPC boundary; typed state); the remaining risk, an unknown brand id, is covered by `ThemeTokenSetTests.UnknownBrandCoercesToDefault` and 03 `BrandMenuModelTests.UnknownAppBrandLabelsDefault` | Core.Tests |
| the menu refuses to rebuild when nothing changed | 03 | 03 `AppMenuViewModelTests.BrandItemsNotifyOnlyOnChange` |
| the Brand submenu is disabled when no project is open | 03 | 03 `BrandMenuModelTests.NoProjectDisablesSubmenu` |
| offers every brand, the default one included | 03 | 03 `BrandMenuModelTests.EveryBrandListedInCatalogOrder` |
| keeps the rebuild off the click path | ELECTRON-ONLY (no menu rebuild); the click-echo concern is 03 `AppMenuViewModelTests.CheckedIsNotToggledByWpf` | 03 |
| never lets a menu rebuild fail the call that triggered it | ELECTRON-ONLY as stated; native intent: a throwing `NavigationState.Changed` subscriber does not fail the project edit (as built in WP-A18: `NavigationState` raises through `EventRaiser`, and the test also checks the later subscriber, the menu's tick, the write and the log line) | App.Tests `NavigationStateTests.SubscriberExceptionDoesNotFailEdit` |
| spells the View menu out so the standard entries survive | 03 | 03 `AppMenuViewModelTests.ItemsAndGestures` |
| distinguishes an unreadable pin from no pin at all | 10: `PinIsUnrecognised("solarpunk") == true`, `("lfi")`, `("shotAI")`, `(null)`, `("")`, `(42)` false | 10 `BrandNarrowingTests.DecisionTable` (the rows named here are a request to 10; the case name `PinIsUnrecognisedEveryDirection` of the first draft exists in no spec) |
| and pinnedBrand still answers null for BOTH, which is why the flag exists | 10: `PinnedBrand("solarpunk") == null`, `PinnedBrand(null) == null` | 10 `BrandNarrowingTests.DecisionTable` (requested rows) |
| ticks App default only when nothing is pinned | 03 | 03 `BrandMenuModelTests.NoPinTicksAppDefault`, `.UnrecognisedPinTicksNothing` |
| never offers the unrecognised state as something to pick | 03 | 03 `BrandMenuModelTests.EveryBrandListedInCatalogOrder` (the item list is the catalog plus App default only) |
| carries the flag through every layer between the manifest and the menu | ELECTRON-ONLY (the six layers are one process); native intent: `NavigationState.RawProjectTheme` carries the raw manifest value so 03 can compute the flag | App.Tests `NavigationStateTests.CarriesRawThemeForUnrecognisedPin` |
| clears the flag when the project closes | on close and on a failed open, `RawProjectTheme == null` and `ProjectPinnedBrand == null` | App.Tests `NavigationStateTests.CloseAndFailedOpenClearPinState` |

### 8.4 New tests the native code needs

**ShotAI.Core.Tests (Linux):**

| Class | Cases |
|---|---|
| `Home.DateGroupsDstTests` | midnight inside a spring-forward gap (custom zone with a 00:00 to 01:00 gap) resolves to the transition instant; ambiguous midnight resolves to the earlier instant; January `lastMonth` is December of the previous year; a week that started in the previous month; a future instant is This Week; an instant exactly at each boundary is in the newer bucket; one tick before is in the older; `TodayMidnightInGapWeekStartsAtSundayMidnight` (a custom zone whose gap is 00:00 to 01:00 on a Wednesday; an item at the preceding Sunday 00:30 is This Week, D-HOME-34) |
| `Home.HomeListPipelineTests` | `TabFilter`; `TabCountsIgnoreSearch`; `HeadingCountIsShown`; `SearchTiers` (title first, content second, label only with both tiers, label empty when only content); `WhitespaceQueryIsNotSearch`; `SearchSuppressesDateGroups`; `NameSortIsFlat`; `NameSortCaseAndAccentInsensitive` (`é` vs `e`, `A` vs `a` compare equal and keep input order); `CreatedAndModifiedOrdinalOnIso`; `TiesKeepListOrderBothDirections`; `AscendingReversesBuckets` (items inside unchanged); `VisibleOrderMatchesRender` (tiers and buckets flattened) |
| `Home.HomeSelectionTests` | `ToggleSetsAnchor`; `RangeUsesVisibleOrder`; `RangeIsAdditive` (never removes a selected row inside the range); `RangeFallsBackToToggle` (no anchor, anchor filtered out); `SelectAllKeepsAnchor`; `ClearDropsAnchor`; `AllSelectedFalseWhenEmpty`; `SelectedVisibleInSortOrder`; `PruneDropsMissingAndAnchor` |
| `Home.RenameSessionTests` | `CommitTrimmed`; `EmptyOrUnchangedWritesNothing`; `CommitAtMostOnce` (Enter then focus loss yields one commit); `EscapeNeverCommits` (Cancel then focus loss yields none); `BeginOnAnotherRowCommitsFirst`; `AbandonWritesNothing`; `TrimUsesJsSemantics` (U+FEFF trimmed) |
| `Home.BulkRunnerTests` | `SequentialInOrder`; `SequentialContinuesPastFailure` (error callback per failure; the marshaling of that callback is the App's job, covered by `HomeViewModelTests.BulkErrorShownOnUiThread`); `ProgressCountsEveryItem` (0 of 3, 1 of 3, 2 of 3, 3 of 3); `EmptyTargetsDoNothing`; `CancellationStopsBeforeNextItem` |
| `Home.AutoRefreshPolicyTests` | interval is 20 000 ms; every combination of the five inputs (32 rows; only all-clear ticks) |
| `Home.CaptureReadinessTests` | readiness truth table; `BuildTarget` for each mode with and without picks (the 2.4 table, including the Auto fallbacks) |
| `Home.HomeTextTests` | every literal of 2.3 to 2.17 equals the spec text (with `\u2014` as U+2014); `StepsMeta` singular and plural, both tabs, empty date `\u2014`, unparseable `\u2014`, en-US culture `9/22/2026` for `2026-09-22T12:00:00.000Z` in a UTC zone; `DeleteOne` keeps straight quotes; `DeleteMany(1)` and `(2)`; `WindowLabel` with and without app and title; `MonitorLabel` primary and not; `AreaLabel`; `RecordingCount(1) == "1 steps"` |
| `Tour.TourStepsTests` | five steps, anchors `Hero, Capture, Mode, null, Settings`, pill only on step 4, headlines and bodies verbatim |
| `Tour.TourLayoutTests` | below placement (`top = bottom + 14`); above placement (`bottom = H - top + 14`) when `rect.bottom + 220 >= H`; left clamp at 12 and at `W - 342`; caret clamp at 18 and 312; spot inflation by 6; centred when no anchor; exact threshold (`rect.bottom + 220 == H` places above) |
| `SettingsUi.SettingsTextTests` | `AiHint` and `AiOff` for both federation states; `KeyStatus` for all 3 x 2 x 2 combinations of (source, hasStoredCiphertext, encryptionAvailable) byte for byte; `ConnectionMessage` for ok with and without model, each leg, no leg, missing error (`Test failed.`); `UpdateCheckMessage` available, error, up to date, thrown, each with `perMachine` false and true (only the available case differs, INV-HOME-45); `UpdateAvailable` both forms byte for byte; `CaptureScaleLabel` (0.85 is `85%`, 0.5 is `50%`, 0.55 is `55%`); `AppInfoLine`; theme and brand blurbs; every other literal of 2.24 to 2.29 |
| `SettingsUi.ArchiveAgeOptionsTests` | the five standard options; `For(45)` appends `After 45 days`; `For(90)` does not |
| `SettingsUi.CaptureScaleStepsTests` | `Snap(0.8500000000000001) == 0.85`; `Snap(0.4) == 0.5`; `Snap(1.2) == 1`; every step 0.5 to 1 round-trips and equals `double.Parse` of its two-decimal text (`CultureInfo.InvariantCulture`) |
| `Theme.AppearanceResolverTests` | the 3 x 2 truth table |
| `Theme.RgbTests` (added in WP-A14) | `#rrggbb` in either case; the table's lower-case form back; every other spelling refused |
| `Theme.ThemeTokenKeysTests` (added in WP-A14) | 105 keys, each once; the colour and radius keys are the CSS tokens; the named keys are 7.5's |
| `Theme.ChromeTokensTests` (added in WP-A14) | the sizes, weights and both appearances' shadows equal `project.css`'s, read from the file (R-HOME-1) |
| `Theme.ActiveBrandResolverTests` | `VisiblePinnedProjectWins`; `SettingsUsesAppBrand`; `NoPinUsesAppBrand`; `UnrecognisedPinUsesAppBrand` (pinned null); added in WP-A14: `DefaultBrandPinStillWins` |
| `Theme.ColorMixTests` | the four derived values of 2.33 within 1 per channel; `p = 1` returns `a`, `p = 0` returns `b`; added in WP-A14: `EachChannelMixesOnItsOwn`, `HalvesRoundUp` (JavaScript's rounding), `AShareOutsideZeroToOneThrows` |
| `Theme.ThemeTokenSetTests` | `EveryRoleFromBrandTable` (36 colours, 7 radii, font stack, stretch, for 4 combinations, compared with 10's generated table, not with literals); `ChipNullIsCapsule`; `UnknownBrandCoercesToDefault`; `ShotAIFontStackResolvesToSegoe` (the WPF family string); added in WP-A14: `GeometryAndTypeIgnoreTheAppearance`, `LfiFontStackNamesArchivoFirst`, `DerivedColoursMixTheTable` |
| `SourceGuards.XamlChromeGuardTests` | 8.2 |
| `SourceGuards.XamlResourceKeyGuardTests` | every `{DynamicResource X}` in App XAML has `X` in `ThemeTokenKeys.All` or in a local dictionary; no `{StaticResource}` of a `ThemeTokenKeys` key; every `ThemeTokenKeys.All` key is referenced at least once or listed as intentionally unused. As built in WP-A14 the list (`NotReadYet`) gives each group of unread keys its reason, and a listed key that is read fails, so the work package that first reads one removes it |

**ShotAI.App.Tests (Windows):**

| Class | Cases |
|---|---|
| `Shell.ShellViewModelTests` | the 2.1 state machine rows; `RecordingHidesViews`; `MenuRequestsIgnoredWhileRecording`; `SettingsReturnsToProject`; `OpenOrImportClosesSettings`; `ReplayTourClosesSettingsAndProject` |
| `Shell.ShellScrollTests` | Home offset restored after Project and after Settings; Project and Settings start at 0 |
| `Shell.ShellStartupTests` | `ThemeAppliedBeforeMainWindowShown` (at the window's `SourceInitialized`, before it is visible, the theme is merged and its ground is the pair's); added in WP-A14: `ShellStartupProcessTests.TheThemeIsAppliedBeforeTheWindowIsShown` (the real exe's debug log has the theme line before the runtime line, which follows `Show()`) |
| `Shell.NavigationStateTests` | 8.3 rows |
| `Home.HomeViewModelTests` | `OnEnterResetsListControls`; `RefreshTriggers` (enter, activation, tick, projects-changed, sop change, after mutations); `RefreshCoalesces` and `OlderResultIgnored`; `BackgroundRefreshKeepsError`; `SearchEditClearsSelection`; `ClearButtonKeepsSelection`; `TabSwitchClearsSelection`; `EscapeOrder`; `AnyBusyDisablesActions`; `DeleteNeedsConfirm`; `BulkDeleteNeedsConfirm`; `RenameOptimisticRollsBackWithNotice`; `ArchiveOptimisticRollsBack`; `CaptureCreatesAndStartsWithCreatedThisSession`; `EmptyProjectOpensWithoutCapture`; `ImportCancelIsQuiet`; `OpenFailedShowsNotice`; `BulkBarStaysVisibleWhileBusy`; `BulkRefreshesOnceThenClears`; `SettingsFromHomeStopsTickAndRefreshesOnReturn` (D-HOME-28); `BackgroundRefreshFailureIsSilent` and `UserRefreshFailureShowsNotice` (D-HOME-29); `BulkFinalRefreshFailureStillClears` (notice shown, selection cleared, folder revealed, D-HOME-33); `BulkWithNoVisibleTargetShowsNoDialog`; `RenameDoesNotSetRowBusy`; `RowExportRevealsWrittenFile` (fake exporter records the reveal); `BulkErrorShownOnUiThread` (a failing op completes on a pool thread; `INoticeService.ShowError` is still called on the UI thread, 7.3) |
| `Home.HomeExportFlowTests` | `FlattenPrecedesEveryExport` (row, each, folder); `FlattenFailureSkipsExport`; `SharedFolderAskedOnceRevealedOnce`; `CancelledFolderDoesNothing`; `SaveDialogCancelIsNotError`; `ArchivedExportRestores` (parity) |
| `Home.CaptureModePickerTests` | `Readiness`; default monitor is the primary; keep-or-default on reload; area cancel keeps the old area; labels; popup keyboard (Escape, arrows, Enter); `DropdownIsOverlayElement` (the open target dropdown is an element of 03's overlay layer and creates no `Popup` or HWND, R-ARCH-19, INV-HOME-43); `BackdropClickClosesAndIsConsumed`; `MonitorIdIsUint` (a `MonitorInfo.Id` above `int.MaxValue` survives pick, reload and `BuildTarget`, R-ARCH-22); `SurvivesNavigation` (mode, targets and picks unchanged after Home, Project, Home); `PopupClosesOnLeave` |
| `Home.RecordingPanelTests` | seeded steps, appended on `StepLanded`, count text, pause and resume buttons, capture error notice |
| `Settings.SettingsViewModelTests` | `ShowsCoercedValue` (archive 5000 shows 1825); `RollsBackWithNoticeOnFailure` (every control); `BlankNameDisablesAndClearsInclude`; `KeyFieldWriteOnly` (the `PasswordBox` is empty on open and after save, and the key never reaches a log sink: a capturing `ILogger` sees no key text); `ChangeClearsTest`; `CheckNowIgnoresToggle`; `CheckNowPerMachineOpensNothing` (a fake `IInstallInfo` with `PerMachine` and an available result: the message is the per-machine text and a fake `IExternalLinks` records no call, INV-HOME-45); `RemoteVisibleAppliesImmediately`; `BrandChoiceRepaintsImmediately`; `CustomInstructionsPersistOnUnload`; `AccountOnlyInSettings`; `OpenReadsFreshAuthStatus` and `AuthStatusChangedRefreshes` (INV-HOME-42); `SignInCanceledShowsNothing`; `UnchangedBlurDoesNotWrite` and `TestResultSurvivesUnchangedBlur` (D-HOME-31); `RollbackShowsCurrent` (a fake `ISettingsService` raising `Changed` with `IsRollback = true` puts the control back) |
| `Settings.SettingsViewTests` | `TabKeyboard` (arrows wrap, Home, End, focus follows, one Tab stop); `AppearanceAndBrandSeparate`; `UnconfiguredShowsNoEntra` (no element whose name or text contains `Microsoft`, `Sign in` or `Entra` on the AI tab); `FederatedShowsSignInAndFallback`; accessible names of 2.37 and 7.13; `ComboBoxDropDownIsExcluded` (INV-HOME-43); `FieldsUseFieldBg` (D-HOME-30); `KeyFallbackIsExpandCollapse` |
| `Tour.TourViewModelTests` | `FirstRunOnly`; `ClosesExactlyOnce` (Skip, Done, Escape, click outside, and a double Done, all one write); `ReplayDoesNotResetFlag`; `LeavingHomeResetsStep`; keyboard |
| `Tour.TourOverlayTests` | spot and bubble positions equal `TourLayout.Place` for a known layout; anchor brought into view; focus inside the bubble; `RemeasuresOnAnchorLayoutChange` (D-HOME-32); `StepChangeRaisesLiveRegion` |
| `Chrome.NoticeCenterTests` | one error at a time; newest replaces; update shown once per launch; pinned position independent of scroll; live settings; `RaisesLiveRegionChanged` |
| `Chrome.UpdateNoticeTests` | `PendingBeforeUiIsShown`; `PushAfterUiIsShown`; `NotShownTwice`; `DismissForSession`; `OpensThroughLauncher`; `LauncherThrowShowsNothing` (a fake `IExternalLinks` that throws: the notice stays, no error notice, one Warning log line, R-ARCH-25); `SubscribesBeforeReadingPending` (11 T7); `PerMachineHasNoDownloadAction` (a fake `IInstallInfo` with `PerMachine`: the per-machine text, no link button in the visual tree, even with a download URL); `PerUserKeepsDownloadAction` (`PerUser` and `Unpackaged`: parity text and button) (INV-HOME-45) |
| `Chrome.ConfirmServiceTests` | Escape resolves false; initial focus on confirm; Enter activates focused; Tab cycles inside; a second request resolves the first false; cancellation resolves false; alert has only OK; `EscapeWorksAfterScrimClick` (EDGE-HOME-52) |
| `Chrome.ThemeManagerTests` | 8.3 rows; `StartSubscribesOnly` (`IAppStartup.Start` does no IO and applies nothing, ARCHITECTURE 4.2 step 9); `FollowsSystemOnlyWhenSystem`; `SwitchKeepsState` (a focused text box keeps focus and caret after a brand switch); `HighContrastMapsToSystemColours` (Q-HOME-12); added in WP-A14: `TheSlotKeepsItsIndex`, `AChangeFromAnotherThreadIsMarshalled`, `ARollbackReapplies`, `TheProjectViewWearsItsPinnedBrand` (the active brand, synchronous), `HighContrastWaitsForTheSwitch`, `DisposeUnsubscribes`, `AFailedApplyIsLoggedNotThrown`, `EachApplyLogsItsPair`, `TheContainerMakesOneManagerThatStarts` |
| `Chrome.ThemeResourcesTests` | `BuildsEveryKey` (every `ThemeTokenKeys.All` key present, values equal `ThemeTokenSet`); resources frozen; added in WP-A14: `FontStacks`, `LabelStretchIsTheNearestWidthClass`, `ChipKeys`, `HighContrastColours`, `CapsuleCorners`, `CapsuleConverterTakesHeightAndRadius`, `UpperCaseFollowsTheBindingsCulture` |
| `Chrome.ControlStylesTests` (added in WP-A14) | every listed style exists; every style applies its template under the 4 pairs; the styles read the theme (a primary button, a checked chip and its capsule corner, a field, a checked home tab's count); `ALargeCornerRadiusIsAnEllipseNotACapsule` (7.5's converter, settled); `ControlsMergeTheFixedColours`; `FixedColoursAreTheSpecValues` |
| `Chrome.OverflowMenuTests` | item order and kinds; drop-up rule against the window client area; click closes then runs; keyboard; `PopupIsShotAIPopup` (INV-HOME-43); `TriggerExposesExpandCollapse` (UIA pattern check) |

As built in WP-A16:
- Core: `DateGroupsTests` (the 8 ported cases, plus `TodayIsTheZonesDate` and an undefined bucket), `DateGroupsDstTests`, `HomeListPipelineTests`, `AutoRefreshPolicyTests` (the regroup interval too), `HomeTextTests` (the list, notice, tab and sort strings, the one-literal strings checked against `ProjectList.tsx`, `App.tsx` and `Notice.tsx`), and new `ListSyncTests` and `Geometry.FlexWrapTests`.
- App: `HomeViewModelTests` (`OnEnterResetsListControls`, `RefreshTriggers`, `RefreshCoalesces`, `OlderResultIgnored`, `BackgroundRefreshKeepsError`, `BackgroundRefreshFailureIsSilent`, `UserRefreshFailureShowsNotice`, `SettingsFromHomeStopsTickAndRefreshesOnReturn`, and the list, empty-state, regroup and command cases), `HomeViewTests`, `NoticeCenterTests` (all but the update case), `FlexWrapPanelTests`, `ShellViewModelTests` (the reachable 2.1 rows, `SettingsReturnsToProject`, `OpenOrImportClosesSettings`), `ShellScrollTests` (the Home offset after a project and after Settings, and `ScrollMemory` itself), `NavigationStateTests.FollowsTheShell` and `.ProjectViewNotVisibleInSettings`, and `CrashLoggingTests.GenericNoticeAfterStartupWhileVisible`.
- Moved: `OpenFailedShowsNotice` to WP-A17; the selection, rename, busy and bulk cases of `HomeViewModelTests` to WP-A19a; `RecordingHidesViews` and `MenuRequestsIgnoredWhileRecording` to WP-B9a; `ReplayTourClosesSettingsAndProject` to the tour (WP-B10); "Project and Settings start at 0" to WP-A17 and WP-B10; "update shown once per launch" to WP-E1.
- The App tests read a `TextBlock`'s text through its automation peer, as a screen reader does: WPF starts tracking inline content in `TextBlock.Text` only at the first measure, so inlines declared in XAML read as empty there, while the peer's name (`GetPlainText`) reads the content.

As built in WP-A19a:
- Core: `HomeSelectionTests` (8.4's nine cases, with `PathsCompareOrdinally`, `ChangedOnlyOnChange` and the argument checks), `RenameSessionTests` (8.4's seven, with `ACaseChangeIsANewTitle`, `ComparedWithTheTitleAtTheCommit` and `ANewSessionIsClosed`), `BulkRunnerTests` (8.4's five, with `ASynchronousThrowIsAFailure` and `AnOperationsOwnCancellationIsAFailure`), and `HomeTextTests`' row menu, bulk bar, delete question and confirm cases with `RowOperationLiteralsMatchTheElectronSource`.
- App: `HomeViewModelTests` (`RenameOptimisticRollsBackWithNotice`, `ArchiveOptimisticRollsBack`, `EscapeOrder`, `AnyBusyDisablesActions`, `DeleteNeedsConfirm`, `BulkDeleteNeedsConfirm`, `SearchEditClearsSelection`, `ClearButtonKeepsSelection`, `TabSwitchClearsSelection`, `RenameDoesNotSetRowBusy`, `BulkBarStaysVisibleWhileBusy`, `BulkRefreshesOnceThenClears`, `BulkFinalRefreshFailureStillClears`, `BulkWithNoVisibleTargetShowsNoDialog`, `BulkErrorShownOnUiThread`, with the rename, archive, delete, reveal, prune and tick cases), `HomeViewTests` (the checkbox, the rename box, the busy row, the bar and the row menu), `ConfirmServiceTests` (8.4's cases, with `ThePageBehindIsNotHit` and `DangerUsesTheDangerStyle`), `OverflowMenuTests` (8.4's cases) and `MainWindowTests.EscapeReachesHome`. These tests read logical focus (`IsFocused`), because a window the tests do not activate never has keyboard focus.
- Moved: `RowExportRevealsWrittenFile` to Home export (WP-D16).

As built in WP-B9a:
- Core: `CaptureReadinessTests` (the truth table, each mode's target with and without picks, the Auto fallbacks, the wire names and the launch's mode) and `HomeTextTests`' hero, chip, dropdown, label, area and recording panel cases with `HeroPickerAndPanelLiteralsMatchTheElectronSource`, which complete the class; 05's `ReportStringsTests` has Resume capturing's.
- App: `CaptureModePickerTests` (`Readiness`, `DefaultMonitorIsThePrimary`, `KeepOrDefaultOnReload`, `AreaCancelKeepsTheOldArea`, the labels as `WindowLabels` and `RowsReadAsTheListShowsThem`, `MonitorIdIsUint`, and `SurvivesNavigation`, which is also `PopupClosesOnLeave`, with `EachLaunchStartsInScreenModeWithNothingLoaded`, `WithoutAPrimaryTheFirstMonitor`, `SelectModeLoadsOnceAndClosesTheDropdown`, `OneLoadAtATime`, `AFailedLoadShowsTheError`, `PickClosesAndMarksTheRow`, `Warnings`, `SelectingAnArea`, `AFailedSelectionShowsTheError` and `ChipsFollowTheMode`). The popover's cases run on the real shell view as `TargetDropdownTests`: `DropdownIsOverlayElement`, `BackdropClickClosesAndIsConsumed` and the popup keyboard as `EnterPicksAndEscapeCloses` (the arrows are the `ListBox`'s own), with `ThePopoverHangsUnderTheTrigger`, `ARowClickPicksAndRefreshStaysOpen`, `AnEmptyListReadsItsLine` and `TheTriggerIsExpandCollapse`. `HomeViewModelTests` has `CaptureCreatesAndStartsWithCreatedThisSession` and `EmptyProjectOpensWithoutCapture`, with `AnEmptyNameTakesTheStoresDefault`, `CaptureNeedsAReadyMode`, `BusyUntilTheRecordingStarts`, `AFailedStartKeepsTheProjectAndShowsTheError`, `AFailedCreateShowsTheErrorAndTheNextClearsIt`, `AFailedRefreshAfterTheCreateGoesOn` (D-HOME-36), `TheTargetIsThePickersAtTheStart` and `TheHeroBindsItsState`. `ShellViewModelTests` has `RecordingHidesViews` and `MenuRequestsIgnoredWhileRecording`, with `RecordingOutranksSettingsAndTheProject`, `ASessionUnderWayIsShownAtOnce`, `DisposeLeavesTheEngine`, `ResumeRecordsIntoTheOpenProject`, `ResumeNeedsAnOpenProjectAndNoSession`, `ASessionOverBeforeTheStartReturnedShowsTheProject`, `ADiscardedNewProjectGoesHomeSilently`, `AnUnreadableRecordedProjectGoesHomeWithTheError` and `TheRecordingHostShowsOnlyDuringASession`.
- Moved: `RecordingPanelTests` to WP-B9b.

**ShotAI.Platform.Tests (Windows):**

| Class | Cases |
|---|---|
| `Theme.SystemAppearanceMonitorTests` | value `0` is dark, `1` and missing are light (through an injected registry reader); `Changed` raised only when the value changes |

---

## 9. Acceptance criteria

**AC-HOME-1.** `DateGroupsTests` (all 8 ported cases) and `DateGroupsDstTests` pass on Linux and Windows.

**AC-HOME-2.** `HomeListPipelineTests`, `HomeSelectionTests`, `RenameSessionTests`, `BulkRunnerTests`, `AutoRefreshPolicyTests`, `CaptureReadinessTests` and `HomeTextTests` pass on Linux.

**AC-HOME-3.** `XamlChromeGuardTests` and `XamlResourceKeyGuardTests` pass on Linux, and each fails when a mutation is applied: (a) add `Background="#6344F1"` to any App XAML outside the exemptions; (b) add `CornerRadius="8"`; (c) add `{StaticResource Brush.accent}`; (d) delete the tour pill style while keeping its exemption; (e) reference `{DynamicResource Brush.text}`.

**AC-HOME-4.** `ThemeTokenSetTests` passes and every value equals 10's generated table for shotAI and LFI in light and dark.

**AC-HOME-5.** Manual: with 12 projects (3 archived) the tabs read `Projects 9` and `Archive 3`; typing a query that matches 2 titles and 1 caption shows the heading `Projects · 3`, two rows, a `MATCHES IN CONTENT` header and one row; the tab counts stay 9 and 3.

**AC-HOME-6.** Manual: sorted by Modified descending with projects edited today, 9 days ago and 40 days ago, the headers read `THIS WEEK`, then (depending on the calendar) `LAST WEEK` or `THIS MONTH`, and `LAST MONTH` or `OLDER`; toggling `▼` to `▲` reverses the header order and the rows inside each header.

**AC-HOME-7.** Manual: click the checkbox of row 2, shift-click row 5: rows 2 to 5 are selected and the bar reads `4 selected`; Escape clears it; typing in the search box with a selection clears it.

**AC-HOME-8.** Manual: select 3 projects, `⤓ Export ▾`, `One shared folder…`, `HTML`: one folder dialog; the bar reads `Exporting 1 of 3…` to `Exporting 3 of 3…`; the chosen folder opens once at the end; three HTML exports are in it; each project's steps were flattened first (log shows the flatten before each export).

**AC-HOME-9.** Manual: cancelling the folder dialog in AC-HOME-8 writes nothing, shows nothing and keeps the selection.

**AC-HOME-10.** Manual: `Delete` on a row shows `Delete "<title>"? This removes the project folder and its screenshots.` with `Cancel` and a red `Delete`; Escape cancels; confirming removes the row immediately and the folder is gone.

**AC-HOME-11.** Manual: rename a project to the same name with surrounding spaces and press Enter: no write happens (the row does not move to the top of `THIS WEEK`); rename to a new name: the row updates at once; Escape during a rename leaves the old name.

**AC-HOME-12.** Manual: with Home open, create a project folder from another machine or the macOS app in the projects folder: it appears within 20 s without interaction, and immediately on switching back to shotAI from another app; while the search box has focus, or a rename is open, or a row is selected, the periodic refresh does not change the list.

**AC-HOME-13.** Manual: Window mode with no windows open shows `Pick a window above to start recording \u2014 that's why Capture ▸ is greyed out.` and `Capture ▸` is disabled; Area mode shows the area warning until an area is selected, then `W × Hpx @ (X, Y)`.

**AC-HOME-14.** Manual: type a name, press Enter in Screen mode: the window hides, the pill shows, and Discard from the pill deletes the whole new project; `Empty Project` opens the empty project without recording.

**AC-HOME-15.** Manual: scroll the Home list down, open a project: the report shows its title and step 1 at the top of the viewport (INV-HOME-19); go back: the list is at the same offset (Projects tab, Modified descending); open Settings from inside a scrolled project: Settings is at the top.

**AC-HOME-16.** Manual: on a fresh profile (no `settings.json`) the tour opens on first launch with `Step 1 of 5`, spotlighting the hero; Next, Next, Next shows the centred pill step; Next spotlights `⚙ Settings`; Done closes it; `settings.json` then holds `"hasSeenTour": true`; relaunch: no tour; Settings, About, `↺ Show intro tour` shows it again on Home.

**AC-HOME-17.** `TourLayoutTests` pass; manual: with the window 700 DIP tall, step 5's bubble sits below the Settings button with the caret under the button's centre.

**AC-HOME-18.** Manual: Settings, Appearance, Theme `Dark` repaints the whole window at once, the focused control keeps focus; Brand `LFI` repaints to charcoal and rust with Archivo text and condensed uppercase labels; relaunch keeps both.

**AC-HOME-19.** Manual: open a project pinned to LFI while the app brand is shotAI: the project view is LFI, Home is shotAI; open Settings from inside that project: Settings is shotAI; back: LFI again.

**AC-HOME-20.** Manual: Theme `System`; switch Windows "Choose your default app mode" between Light and Dark: shotAI follows within a second; with Theme `Light` it does not.

**AC-HOME-21.** Manual: set Windows to dark app mode and Theme `System`, launch: the first frame of the main window is already dark (no light flash); same for Brand LFI.

**AC-HOME-22.** Manual: every Settings control writes the key in 2.30 (inspect `settings.json` after each change) and shows the coerced value (edit `archiveAgeDays` to `5000` by hand, relaunch: the select shows `After 1825 days`); unknown keys in `settings.json` survive.

**AC-HOME-23.** Manual: make `settings.json` read-only, change Theme: the UI shows the change, then reverts it and shows an error notice.

**AC-HOME-24.** Manual: clear the name in About with `Include my name` on, click elsewhere: the toggle turns off and is disabled.

**AC-HOME-25.** Manual (unconfigured build, no federation policy): the AI tab shows the API-key group and no Microsoft or Entra wording anywhere.

**AC-HOME-26.** Manual (federation policy present): the AI tab shows `Microsoft sign-in`, `Sign in with Microsoft` (WAM prompt), then `Signed in as <UPN>`, `Sign out`, `Test connection`; `▸ Use my own Anthropic API key instead` reveals the key group.

**AC-HOME-27.** Manual: save an API key: the field empties, the status reads `A key is saved (encrypted) on this machine ✓`; reopening Settings shows an empty field; `shotai.log` contains no part of the key.

**AC-HOME-28.** `UpdateNoticeTests.OpensThroughLauncher` and `.LauncherThrowShowsNothing` pass and every URL this subsystem opens (`https://console.anthropic.com/settings/keys`, a GitHub release page, the configured support URL) is accepted by 11's `Links.ExternalLinkPolicyTests` (the allowlist 10 registers); a `http://` or `https://example.com` link is refused (`OpenAsync` returns `false`), and a launcher exception shows nothing to the user (R-ARCH-25).

**AC-HOME-29.** Manual: run a per-user or unpackaged build whose version is lower than the latest GitHub release (AC-HOME-41 covers a per-machine install): `shotAI <v> is available. Open the download page` appears once; `×` hides it until the next launch; `Open the download page` opens the release in the browser.

**AC-HOME-30.** Manual: Settings, About, `↻ Check now` on a current build shows `You're up to date.`; offline shows `Couldn't check: <reason>`; the button reads `Checking…` while running.

**AC-HOME-31.** Manual: trigger an error while the list is scrolled down (for example rename a project whose folder was just made read-only): the error notice is visible at the top of the viewport and stays until dismissed, through at least one 20 s refresh.

**AC-HOME-32.** Keyboard-only manual pass: every control in Home, Settings, the confirm dialog, the menus and the tour is reachable and operable without a mouse (Tab, arrows, Enter, Space, Escape), focus is always visible (2 DIP accent ring), and Escape undoes exactly one thing.

**AC-HOME-33.** Accessibility manual pass with Narrator and Accessibility Insights for Windows (FastPass): no missing names; the names of 2.37 and 7.13 are announced; the bulk count and error notices are announced.

**AC-HOME-34.** `SettingsViewModelTests.RemoteVisibleAppliesImmediately` and 11's `Settings.RemoteVisibilityApplierTests` pass (the view model writes the setting; 11's `RemoteVisibilityApplier` applies it, ARCHITECTURE 9.2 S7); manual: turn the remote-visibility switch on during a Teams screen share: the shotAI window becomes visible to the viewer without a restart; off hides it again.

**AC-HOME-35.** Manual: with 500 projects the Home list scrolls without visible stutter and a refresh with no changes does not rebuild rows (no flicker, the focused element keeps focus).

**AC-HOME-36.** Manual [SECURITY]: with shotAI running and Settings closed, add (or remove) the federation policy values under `HKLM\SOFTWARE\Policies\shotAI\Federation`, then open Settings, AI: the Microsoft sign-in group appears (or disappears) without restarting the app (INV-HOME-42).

**AC-HOME-37.** Manual [SECURITY]: with remote visibility off, open a row's `⋯` menu, the bulk `⤓ Export ▾` menu, the target dropdown and the auto-archive drop-down during a Teams screen share: none of them is visible to the viewer, exactly like the window behind them (INV-HOME-43); `AllWindowsRegisteredTests` passes with the two menus and the drop-down open (each a `ShotAIPopup` HWND), and `CaptureModePickerTests.DropdownIsOverlayElement` passes (the target dropdown is drawn in the overlay layer and has no HWND of its own, R-ARCH-19).

**AC-HOME-38.** Manual: open Settings from Home, then create a project folder in the projects folder from outside the app (Explorer copy or the macOS app) and wait over 20 s; close Settings: the new project is listed immediately on return, without waiting for a tick (D-HOME-28); `HomeViewModelTests.SettingsFromHomeStopsTickAndRefreshesOnReturn` passes.

**AC-HOME-39.** Manual (archiving from Home, 2.12, 2.14, 2.16, 7.6): `Archive` in a row's `⋯` menu moves the row to the Archive tab at once (before the store call finishes, 7.6), and the project folder then holds `project.json` and `archive.zip` with no `shots\` or `export\` folder (`src/main/archive.ts:20-22`); select two rows and press `🗄 Archive`: the bar counts `Archiving 1 of 2…` to `Archiving 2 of 2…` and both move likewise; on the Archive tab, `Restore` in a row's menu moves it back to Projects, `archive.zip` is gone, and opening it shows every image; `Open` on an archived row (tooltip `Open (restores the archived project)`) opens the report with every image, and on return the row is under Projects; with write access to a project folder denied, `Archive` on that row moves it, then puts it back under Projects and shows the error notice with the store's message (7.6 rollback), and the folder's files are intact.

**AC-HOME-40.** Manual (bulk export to each project's own folder, 2.16 `bulkExportEach`, 09 2.16): on the Projects tab select 3 projects, `⤓ Export ▾`, under `Each project's own folder`, `Word`: no dialog opens, no folder is revealed, the bar counts `Exporting 1 of 3…` to `Exporting 3 of 3…`, each project's `export\` gains `<title>.docx` (the 09 safe file base), a second run gives `<title> (1).docx` beside it, and the log shows each project's flatten before its export; then on the Archive tab select 1 project and run the same export: its `export\` gains `<title>.docx` and it is listed under Projects afterwards (EDGE-HOME-14). A selection never spans both tabs, because a tab switch clears it (2.15).

**AC-HOME-41.** Manual (INV-HOME-45): install a build versioned below the latest GitHub release per-machine (`ALLUSERS=1`, 12 AC-PKG-2) and launch it as a standard user: Home shows `shotAI <v> is available. On this PC, IT or an administrator installs updates.` once, with no `Open the download page` button, and Settings, About, `↻ Check now` shows the same text and opens no browser. Install the same build per-user on another VM: AC-HOME-29 and AC-HOME-30 hold unchanged.

---

## 10. Interfaces with other subsystems

| Spec | This subsystem consumes | This subsystem provides |
|---|---|---|
| 01 Model and store | `ProjectSummary`, `IProjectService` (11 7.3.2: `ListProjectsAsync`, `CreateProjectAsync`, `OpenProjectAsync` returning `OpenedProject(Dir, Manifest)`, `RenameProjectAsync`, `DeleteProjectAsync`, `ArchiveProjectAsync`, `UnarchiveProjectAsync`, `SetProjectsDirAsync`, `GetProjectsDirAsync`, the `ProjectsChanged` event after auto-archive), `ProjectSearch` (2.9.6), `JsString.Trim`, `JsMath.Round`, `JsNumber`, `IsoTime.TryParseJsDate`, the Q-MODEL-11 open-failure wording | tab, sort, direction, query; the rename, archive and delete requests |
| 02 Capture | monitor ids as `uint` (R-ARCH-22); `ICaptureService.GetState`, `StartAsync(path, CaptureStartOptions)`, `Pause`, `Resume`, `StopAsync`, `ListTargetsAsync`, events `StateChanged`, `StepLanded`, `CaptureFailed`; `CaptureTargets`, `WindowInfo`, `MonitorInfo`, `CaptureTarget`, `CaptureMode` | the target of the next recording (`CaptureReadiness.BuildTarget`), `CreatedThisSession = true` for Home-created projects; the screenshot-quality and remote-visibility settings writes |
| 03 Shell | `OpenSettingsRequested`, `ImportProjectRequested` (not raised while recording), `IAreaSelectionService.SelectAreaAsync`, `IMainWindowLayout.SetDetailView(false, 1)` on Home, the top overlay layer, `ShotAIPopup` and `PopupExclusion` (7.4.7, INV-HOME-43), the brand menu | `NavigationState` (implements `IShellNavigationState`: `ProjectOpen`, `OpenProjectPath`, `RawProjectTheme`, `Changed`), the `Something went wrong. See the log for details.` wording for Q-SHELL-16 |
| 04 Editor and redaction | `IStepFlattener.EnsureFlattenedAsync` (`ShotAI.Core.Rendering`, R-ARCH-7; the direct-store overload, since Home has no open session) before every Home export | |
| 05 Report | `ProjectDetailViewModel.OpenAsync(path)`, `Adopt(dir, manifest)`, `Back`, `OpenFailed(message)`, the `ResumeCaptureRequested(target)` (the target read through `ICaptureTargetSelection.BuildTarget()` at click time, R-ARCH-26; the capture coordinator takes it from the event args) and `CaptureInsertRequested(atIndex, target)` events, the open project's `projectTheme` (pinned), raw theme, `sopBackup` change signal, report scroll-to-top on open | `ICaptureTargetSelection.BuildTarget()` for Resume capturing (`ShotAI.App.Home`, R-ARCH-26); `INoticeService`, `IConfirmService` (the confirm service 07's SOP panel also takes, R-ARCH-16), the theme resources and control styles (05 draws its own overlay-layer menus, 05 7.13, and does not reuse `OverflowMenu`); `ShellViewModel.OpenSettings()` for the SOP panel link |
| 07 SOP generation | `SOP_MODELS`, `SOP_TONES`, `SOP_EFFORTS`, `SOP_CUSTOM_INSTRUCTIONS_MAX` (Core), the SOP apply or revert that changes `sopBackup` | the AI settings writes through 10 |
| 08 Auth, secrets and policy | `IAuthService` (08 7.12; 08's interface and value types are canonical, R-ARCH-2; `TestConnectionAsync` is the one public connection test, R-ARCH-3): `GetStatusAsync` (invalidates the policy cache, INV-HOME-42), `SignInAsync(ownerWindow, ct)` returning `SignInOutcome`, `SignOutAsync`, `GetApiKeyStatusAsync`, `SetApiKeyAsync`, `ClearApiKeyAsync`, `TestConnectionAsync`, `AuthStatusChanged`; `AuthStatus`, `ApiKeyStatus`, `TestConnectionResult`, `ConnectionLeg`; `FederationPolicy.DefaultSupportUrl`; the SupportUrl origin rule behind 10's link policy | the rendering of those values (strings in 2.25) |
| 09 Exports | `IExportService` (`ShotAI.App.Export`, 09 7.13's seven members, R-ARCH-9): `ExportWithSaveDialogAsync(path, format, owner, progress, ct)` (Q-EXP-18), `ExportToOwnFolderAsync(path, format, ct)`, `ExportToDirectoryAsync(path, format, dir, ct)`, `ChooseExportDirectoryAsync(owner)`, `RevealExportDirectoryAsync(dir)`, `ImportPackageAsync(owner, ct)` (dialog, validate, materialize) | the Home and bulk export flows |
| 10 Brand, settings, logging, updates | the generated `BrandPalette` table (colours, radii, fonts, labels), `BrandIds` order, `DefaultBrand`, `IsBrandId`, `CoerceBrand`, `PinnedBrand`, `ThemePref`, the bundled Archivo faces and OFL text; `ISettingsService.Current`, `UpdateAsync` (coercions of 2.30, rollback with `IsRollback`), `Changed`; `IUpdateService.Pending`, `UpdateAvailable`, `CheckNowAsync`; the registration of `IExternalLinks` (`OpenAsync(string url, CancellationToken ct = default)`, algorithm 11 7.3.4; `false` when refused, throws only if the launcher throws, R-ARCH-25); `ILogger<T>` | `ThemeTokenSet`, `ThemeManager`, `ThemeTokenKeys`, the WPF resource dictionaries, the `Appearance` enum 10's `BrandPalette.For` takes; every Settings write; `hasSeenTour`; `lastUpdateCheckAt` via `CheckNowAsync` |
| 11 Service boundary | `IUiDispatcher` (the only marshalling path, T6, R-ARCH-11), the banned-symbol list (with the `UiDeferral.cs` allowance, R-ARCH-18), `UserMessage.From`, `IFileDialogs.PickFolder`, `IAppInfo.Current` (fields passed to `SettingsText.AppInfoLine`), `IShellReveal.RevealProjectAsync`, `IExternalLinks` (`ShotAI.Core.Links`), `RemoteVisibilityApplier`, `IAppStartup`, `ViewModelBase`, DI registrations | view models registered transient per view (except `CaptureModePickerViewModel`, a singleton, EDGE-HOME-57), `NoticeCenter`, `ConfirmService`, `ThemeManager` as singletons (`ThemeManager` also as the third `IAppStartup`, ARCHITECTURE 4.3), `ISystemAppearance` registered by `AddShotAIPlatform` |
| 12 Packaging and CI | the Windows test projects in CI, the Archivo static font files in the package, `IInstallInfo.Scope` (12 7.10.4) for the per-machine update notice (INV-HOME-45) | `ShotAI.App.Tests`, `ShotAI.Platform.Tests` definitions (8); the update notice that does not send a per-machine user to the release page (12 EDGE-PKG-65) |

---

## 11. Open questions and risks

**Q-HOME-1. Where do the XAML source guards run?** They only read text, so they can run in `ShotAI.Core.Tests` on Linux (a Core test project reading App files). Recommended default: yes, in `ShotAI.Core.Tests/SourceGuards`, so a colour literal fails the Linux CI job; the key-set comparison against `ThemeResources` (which needs WPF types) runs in `ShotAI.App.Tests`. Adopted by ARCHITECTURE 5.2 and 12.5 (`SourceGuards/XamlResourceKeyGuardTests`, `XamlChromeGuardTests` on Linux). Built in WP-A14 as recommended: both guards run in `ShotAI.Core.Tests` on Linux and Windows; the key-set comparison is App `Chrome/ThemeResourcesTests`.

**Q-HOME-2. Archivo in WPF.** Electron bundles one variable font and relies on the declared axis ranges; WPF's text stack does not drive OpenType variation axes from `FontWeight` and `FontStretch`, and would render the default instance (weight 600) everywhere, the exact failure `project.css:1-13` warns about. Recommended default: ship the upstream static instances (Omnibus-Type/Archivo, OFL, no Reserved Font Name) for Regular 400, Medium 500, SemiBold 600, Bold 700, ExtraBold 800, plus the wdth-62 (ExtraCondensed, usWidthClass 2) SemiBold and Bold for the 62% labels (UNVERIFIED: which upstream static files carry wdth 62, and whether WPF groups static files into one family by the typographic family name; the rendering test decides), grouped under one family name so `FontWeight` and `FontStretch` select them; 10 owns the licence, 12 the packaging; verify with a rendering test that `FontWeight.Normal` text is not SemiBold. Decided in WP-A14: the upstream static instances, unmodified, from Omnibus-Type/Archivo commit `555fa4a` (10 Q-INFRA-19): Regular 400, Medium 500, SemiBold 600, Bold 700, ExtraBold 800, and the `wdth` 62 SemiBold and Bold, `ArchivoExtraCondensed-SemiBold.ttf` and `-Bold.ttf` (usWidthClass 2; the Bold declares weight 680). Measured on both Windows runners by `ArchivoRenderingTests`: each weight resolves its own file, `FontWeight.Normal` is Regular, and the `wdth` 62 files are not grouped under `Archivo` but form the family `Archivo ExtraCondensed` (their name IDs 1 and 16), reachable by that name; so the labels read `Font.label-stack` (7.5), which names that family before `Archivo`, and a label sets at under 80% of the normal width. WPF does list the variable file's 9 named weight instances, all at normal width (10 Q-INFRA-9); the static set stays, because the condensed labels need it and one source of faces does not depend on how a Windows build enumerates named instances.

**Q-HOME-3. Keep the Home list state across navigation?** Electron resets tab, sort, search and selection on every return (EDGE-HOME-3), which is surprising when returning to a search. Recommended default: parity for 2.0.0; revisit after the pilot. Either way, an open rename is committed (not dropped) when leaving Home. Decided in WP-A16: parity, `OnEnter` resets tab, sort and search on every entry (the rename commit joins with the rename in WP-A19a; as built there, `OnLeave` commits an open rename).

**Q-HOME-4. Culture for the meta date.** Chromium formats with the app locale (the Windows display language); `CultureInfo.CurrentCulture` follows the user's regional format, which can differ (display en-US, region en-GB). Recommended default: IMPROVEMENT, `CurrentCulture` (the user chose that format for dates), documented in the release notes. Decided in WP-A16: `CurrentCulture` for the meta date and for the name collation; the release note is the pilot's.

**Q-HOME-5. Export from the Archive tab.** It silently restores the project (EDGE-HOME-14); macOS hides bulk export there. Recommended default: parity (offer it, restoring is the documented behaviour of opening an archived project); revisit if pilot users are surprised.

**Q-HOME-6. Default focus in destructive confirms.** Enter confirms `Delete` (EDGE-HOME-27). Recommended default: parity; the alternative (focus Cancel for `danger`) is a one-line change if accessibility review asks for it. Decided in WP-A19a: parity, the confirm button has the initial focus, `Delete` included (`ConfirmServiceTests.InitialFocusOnConfirm`).

**Q-HOME-7. Bulk failure reporting.** Electron shows only the last failure (each replaces the notice); macOS shows `N of M ... failed.` Recommended default: parity for 2.0.0; an aggregated message is a candidate IMPROVEMENT if the pilot hits multi-failure runs.

**Q-HOME-8. Should a manual `Check now` raise the Home notice?** It opens the browser instead (EDGE-HOME-23). Recommended default: parity.

**Q-HOME-9. Recents after a projects-folder change.** Electron keeps listing the old root's recent projects; macOS clears recents. Recommended default: parity (01 owns recents); record for 01.

**Q-HOME-10. Tour bubble height.** Electron guesses 220 DIP for the below or above decision; macOS measures. Recommended default: parity (the 220 rule), because measuring can change which side a bubble appears on for the shipped layout; measure only to keep a bubble inside the window as a last resort.

**Q-HOME-11. Brand blurbs for a future third brand.** Electron gives every non-default brand the LFI blurb. Recommended default: keep the two strings keyed by brand id; if 10 adds a brand, add a `blurb` to `contract/brand.json` (a cross-repo change) rather than hardcoding it here.

**Q-HOME-12. High contrast.** Chromium adapts automatically; WPF custom brushes do not (EDGE-HOME-45). Recommended default: when `SystemParameters.HighContrast` is true, `ThemeManager` builds the dictionary from `SystemColors` (ink and ink-2 and ink-3 to `WindowTextColor`, surfaces and ground to `WindowColor`, accent and focus to `HighlightColor`, on-accent to `HighlightTextColor`, hair and control borders to `WindowTextColor`, status and callout inks to `WindowTextColor`), and re-applies on `SystemParameters.StaticPropertyChanged`; needs a design sign-off, not a blocker for the pilot. Built in WP-A14 behind `ThemeManager.HighContrastMappingEnabled`, which is false until the sign-off (WP-E6): `ThemeResources.BuildHighContrast` maps the accent family and the focus ring to `HighlightColor`, on-accent to `HighlightTextColor`, every surface and tint to `WindowColor`, and every ink, hairline, border and status colour to `WindowTextColor`, geometry and type staying the brand's; with the switch on, the manager re-applies when `SystemParameters.HighContrast` changes.

**Q-HOME-13. Emoji in labels.** WPF draws them as monochrome outlines (EDGE-HOME-46). Recommended default: accept; the alternative (Segoe Fluent Icons glyphs) changes shipped strings and needs product sign-off. Decided in WP-A16: accept, for the empty-state icons and the header's `⚙`.

**Q-HOME-14. Row shadows.** A `DropShadowEffect` per row can be costly with hundreds of rows. Recommended default: keep it (parity look) and measure against AC-HOME-35; if frame time exceeds 16 ms, replace it with a 1 DIP offset border in the shadow colour. Decided in WP-A16: kept; AC-HOME-35 is measured in the manual script, and a failure there replaces it.

**Q-HOME-15. WPF test harness.** xunit.v3 has no built-in STA fact. Recommended default: a small in-repo helper that runs each App test on a dedicated STA thread with a `Dispatcher` and pumps until the test's task completes; no extra package. Adopted by ARCHITECTURE 2.1 and 12.1 (`ShotAI.App.Tests` with the in-repo STA harness). As built in WP-A16: the harness runs one body at a time, because WPF's resource package is not safe to read from two UI threads at once (`PackagePart.GetStream` failed in `CleanUpRequestedStreamsList` when parallel test classes loaded the shell view).

**Q-HOME-16. Unexpected-exception wording.** Recommended default: `Something went wrong. See the log for details.` (confirms 03 Q-SHELL-16), used only for exceptions not thrown deliberately with user text. Adopted by ARCHITECTURE 8.2 (`UserMessage.From`: `ShotAIException`, `IOException` and `UnauthorizedAccessException` show their `Message`, anything else shows this sentence).

**Q-HOME-17. Update notice outside Home.** Windows shows it over any view, macOS only on Home. Recommended default: parity (any view except while recording, when the window is hidden anyway).

**Risk R-HOME-1.** The spec restates 01's search and ranking and 10's palette values for completeness; if either changes upstream, this document goes stale. Mitigation: the native tests compare against the Core implementations and the generated table, never against the literals printed here.

**Risk R-HOME-2.** Several Electron behaviours are incidental (EDGE-HOME-9, -21, -22) and are changed here as IMPROVEMENTs; a parity reviewer comparing the two apps side by side will see those differences. Mitigation: D-HOME-1 to D-HOME-34 are the complete list to check against.
