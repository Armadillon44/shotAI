# 11 Service boundary (replacing IPC)

> Spec for the native rewrite. Sources read: `src/shared/ipc.ts` (624 lines), `src/main/ipc.ts` (981), `src/preload/preload.ts` (240), `src/renderer/shotai-api.d.ts` (10). Also read for the handlers and pushes that live outside `ipc.ts`: `src/main/main.ts` (595; `view:set-detail`, `view:set-brand-menu`, `projects:changed`, `update:available`, window and session hardening), `src/main/RegionService.ts` (151; `region:complete`, `region:cancel`), `src/main/menu.ts` (271; the three `menu:*` pushes), `src/main/update-state.ts` (25), `src/main/CaptureController.ts` (1485; `broadcast`, `getState`, `pause`, `resume`, `stop`, `discard`), `src/main/settings.ts` (434; setter coercions), `src/main/secrets.ts` (112; `setApiKey` errors), and every renderer call site of `window.shotai` (listed per channel in 2.4). Electron tests assigned: none. Related wiring tests that assert on IPC source text: `src/main/entra/federation-cache-wiring.test.ts` (50), `src/renderer/project/theme-wiring.test.ts` (341), `src/shared/export-theme.test.ts` (261). macOS: `macOS:shotAI/AppModel.swift` (1469). Commits read: `38908cd`, `772e381`, `701d4aa`, `1ecebd0`, `66d7376`, `f24b3dc`, `53045c6`, `9da70df`, `037858d`, `d79bc3b`, `b25a40e`. Verification (adversarial pass): every channel, coercion, string, constant and citation re-checked against `src/shared/ipc.ts`, `src/main/ipc.ts`, `src/preload/preload.ts`, `src/renderer/shotai-api.d.ts`, `src/main/main.ts`, `src/main/RegionService.ts`, `src/main/menu.ts`, `src/main/update-state.ts`, `src/main/CaptureController.ts`, `src/main/settings.ts`, `src/main/secrets.ts`, `src/main/project-store.ts`, `src/main/export.ts` and the renderer call sites; the cross-spec names re-checked against 01, 02, 03, 04, 06, 07, 08 and 09; WPF `DispatcherPriority` values checked on Microsoft Learn. Corrections: the one-shot screenshot's final state emit, the update notice's dismiss state, the boundary logging exceptions, the WPF priority order (Q-IPC-20), the WebView2 reference from `AppInfoProvider`, the banned-symbol syntax, the async-only `ICaptureService` disposal, the 04 and 08 member names. Appended: INV-IPC-25 and 26, EDGE-IPC-42 to 47, D-IPC-17, Q-IPC-21 to 23. Status: extracted and verified. Consolidated with ARCHITECTURE.md R-ARCH-1 to R-ARCH-26 on 2026-09-23.

**Notation used in this document.**

- `R->M` is renderer to main (`ipcRenderer.invoke` answered by `ipcMain.handle`, or `ipcRenderer.send` received by `ipcMain.on`). `M->R` is main to renderer (`webContents.send` received by `ipcRenderer.on`).
- Electron citations are repo-relative `path:line`. A bare `:line` repeats the file of the previous citation in the same cell.
- Other specs are cited by number: 01 model and store, 02 capture, 03 windows and shell, 04 editor and redaction, 05 report, 06 home and settings UI, 07 SOP generation, 08 auth, secrets and policy, 09 export, 10 brand, settings, logging, update check and external links (brand and palette functions are 10's `BrandPalette`, R-ARCH-14), 12 packaging and CI.
- ARCHITECTURE.md (`docs/native/ARCHITECTURE.md`) is cited as ARCHITECTURE with its section or ID; its cross-spec resolutions `R-ARCH-n` (15.3) are binding on this spec.
- `\u2014` inside a quoted string stands for the em dash character that the Electron string contains. This document never writes the character itself.
- Classes: **REQUIRED** (parity), **IMPROVEMENT** (a deliberate native change, justified), **ELECTRON-ONLY** (disappears natively; the replacement of its intent is named).

---

## 1. Scope

**This subsystem owns** the seam between the UI and everything else. In Electron that seam is a process boundary: 87 named channels (74 `ipcMain.handle` invoke channels, 3 `ipcMain.on` fire-and-forget channels, 10 `webContents.send` push events), the preload bridge that exposes them as `window.shotai`, the argument validation `src/main/ipc.ts` performs because the renderer is untrusted, and the hardening that makes the renderer untrusted-but-contained (sandbox, context isolation, CSP, navigation confinement, the `shot://` scheme).

Natively there is one process. This spec replaces the seam with:

1. A **service catalog**: the C# interfaces the view models call, which project each lives in, which spec implements it, and its lifetime (7.3).
2. A **channel map**: every one of the 87 channels mapped to exactly one native member (7.4), so that nothing the Electron UI could do is silently lost.
3. A **validation disposition**: for every check in `ipc.ts`, `main.ts` and `RegionService.ts`, whether it stays (because it guards external input: files, JSON, policy, network, OCR) or goes (because it existed only because types are erased at the IPC boundary and the renderer was untrusted), and where a kept check now lives (7.5).
4. The **threading contract**: which thread each service runs on, how events and progress reach the UI thread, the one dispatcher abstraction (`IUiDispatcher`), the rule against blocking the UI thread, cancellation, disposal and the exit flush (7.6 to 7.10).
5. The **error contract**: how an exception becomes user text, replacing Electron's `Error invoking remote method ...` wrapping (7.9).
6. The **composition root**: the DI registrations and lifetimes of every service (7.10).
7. The boundary **logging** rules (7.11) and the **analyzers** that enforce the contract at build time (7.12).

**It does not own** the behavior behind any channel. Each channel's semantics belong to the spec named in its Owner column (2.4); this spec quotes them only as far as the boundary changes them. In particular: the store and `ProjectSession` (01), the capture engine (02), windows, overlay, pill and menu (03), flatten and OCR (04), the report's optimistic operations (05), Home and Settings (06), SOP generation (07), auth and the API key (08), export and packages (09), the brand contract and `BrandPalette`, `settings.json`, `IAppPaths`, the update check, logging sinks and the external-link opener's registration (10), packaging and CI (12).

**It touches** every other spec: each one either provides a service in the catalog or consumes one. Section 10 lists the exact types.

---

## 2. Reference behavior (Electron)

### 2.1 Process model and the contract file

| # | Behavior | Citation | Class |
|---|---|---|---|
| 1 | Three kinds of process: main (Node, full privileges), one renderer per window (Chromium, sandboxed), and the preload script that runs in each renderer with access to `ipcRenderer` only. The windows are the project window, the toolbar pill and one overlay per display during area selection. | `src/main/main.ts:263-306`, `:311-352`, `src/main/RegionService.ts:55-79` | ELECTRON-ONLY (one process natively) |
| 2 | Every window is created with `contextIsolation: true`, `nodeIntegration: false`, `sandbox: true` and the same preload. | `src/main/main.ts:272-276`, `:326-330`, `src/main/RegionService.ts:73-78` | ELECTRON-ONLY |
| 3 | The preload exposes exactly one global, `window.shotai`, typed `ShotaiApi`, through `contextBridge.exposeInMainWorld('shotai', api)`. The renderer never sees `ipcRenderer` or Node. | `src/preload/preload.ts:1-4`, `:240`, `src/renderer/shotai-api.d.ts:1-10` | ELECTRON-ONLY |
| 4 | The contract is compiler-enforced: the preload declares `const api: ShotaiApi`, so a method in the shared interface missing from the bridge (or the reverse) fails `tsc`. | `src/preload/preload.ts:19`, commit `b25a40e` | REQUIRED intent: natively the interfaces themselves are the contract, checked by the compiler |
| 5 | Channel names are a single `as const` object `IpcChannels` in the shared contract file, imported by main, preload and renderer. | `src/shared/ipc.ts:174-278` | ELECTRON-ONLY (no string names natively) |
| 6 | The payload types that cross the boundary (`AppInfo`, `CaptureState`, `AuthStatus`, `ApiKeyStatus`, `TestConnectionResult`, `SopEstimate`, `SopProgress`, `ExportFormat`, `ExportProgress`, `UpdateCheckResult`, `ExportResult`, `PackageResult`) are declared in the same file. | `src/shared/ipc.ts:21-172` | REQUIRED shapes (they become C# records, 7.3); ELECTRON-ONLY location |
| 7 | Handlers are registered once, after `app.whenReady()`, by `registerIpcHandlers(capture, region)`; two more (`view:set-detail`, `view:set-brand-menu`) are registered next to it in `main.ts`; the region channels are registered by the `RegionService` constructor. | `src/main/ipc.ts:250-254`, `src/main/main.ts:452-486`, `src/main/RegionService.ts:30-38` | ELECTRON-ONLY |

### 2.2 Transport semantics

| # | Behavior | Citation | Class |
|---|---|---|---|
| 1 | **Invoke.** `ipcRenderer.invoke(channel, ...args)` returns a promise resolved with the handler's return value (awaited if it is a promise). Arguments and results are copied with the structured clone algorithm: plain objects, arrays, strings, numbers, booleans, `null`, `undefined`, `Uint8Array` and `ArrayBuffer` survive; functions, class identity and prototypes do not. | `src/preload/preload.ts:20-237` | ELECTRON-ONLY |
| 2 | **Errors.** A handler that throws (or returns a rejected promise) rejects the renderer's promise with a new `Error` whose `message` is `Error invoking remote method '<channel>': <String(error)>`, for example `Error invoking remote method 'claude:generate-sop': Error: AI SOP generation is turned off.`. The renderer displays `e.message` unchanged, so this prefix is user-visible in every error banner. The wrapping is done by the Electron runtime, not by repo code; the format is as recorded by 07 EDGE-SOP-23. | `src/renderer/project/App.tsx:92-93`, `src/renderer/project/SopPanel.tsx:94`, `src/renderer/project/Settings.tsx:129`; 07 EDGE-SOP-23 | ELECTRON-ONLY (native shows the bare message, 7.9) |
| 3 | **Fire-and-forget.** `ipcRenderer.send(channel, ...args)` has no reply. Used for three channels: `claude:cancel`, `region:complete`, `region:cancel`. | `src/preload/preload.ts:187`, `:235-236` | ELECTRON-ONLY transport; the verbs are REQUIRED |
| 4 | **Push.** `webContents.send(channel, payload)` delivers to one window's renderer. **It does not buffer**: a push to a window whose renderer has not yet subscribed is lost. | `src/main/update-state.ts:1-11`, `src/main/main.ts:545-554`, commit `037858d` | ELECTRON-ONLY transport; the loss mode has a native analogue (EDGE-IPC-1) |
| 5 | **Three push targets.** (a) Broadcast to every window: the capture events, through `createCaptureController`'s `broadcast`, which loops `BrowserWindow.getAllWindows()` skipping destroyed ones. (b) The requesting window only: `projects:export-progress` and `claude:sop-progress`, each guarded by `sender.isDestroyed()`. (c) The project window only: `projects:changed`, `update:available` and the three `menu:*` events, each guarded by a null or destroyed check. | `src/main/CaptureController.ts:1470-1476`, `src/main/ipc.ts:565-571`, `:888-891`, `src/main/main.ts:509-511`, `:552-554`, `src/main/menu.ts:182`, `:213`, `:218` | REQUIRED targeting intent (7.7) |
| 6 | **Subscriptions.** Every `on*` bridge method wraps the callback in a listener, registers it with `ipcRenderer.on`, and returns an unsubscribe function that calls `removeListener` with the same listener. | `src/preload/preload.ts:22-36`, `:62-66`, `:120-124`, `:167-171`, `:188-193`, `:215-231` | REQUIRED shape (C# `event` plus `-=`, 7.7) |
| 7 | **Bridge normalizations.** `updateStep` and `mergeSteps` send `flattenedPng ?? null` (so `undefined` never crosses); `capture.start` passes its `opts` object through verbatim "so a future opts field can't silently drop at the bridge"; every other method passes its arguments positionally. | `src/preload/preload.ts:69-81`, `:88-102`, `:196-204` | ELECTRON-ONLY |
| 8 | **Types are erased.** Nothing in the transport enforces the TypeScript types; `ipc.ts` therefore treats every argument as `unknown` and validates or coerces it (2.3). | `src/main/ipc.ts:82-88` | see 7.5 |

### 2.3 Shared validation helpers in `src/main/ipc.ts`

These run in main on every call that uses them. Exact rules:

| Helper | Rule | Throws | Citation |
|---|---|---|---|
| `asString(value, name)` | returns `value` when `typeof value === 'string'`, else throws | `` `${name} must be a string` `` with `name` one of `projectPath`, `stepId`, `keepId`, `dropId`, `dir`, `apiKey` | `src/main/ipc.ts:82-88` |
| `isNum(v)` | `typeof v === 'number' && Number.isFinite(v)` (so `NaN`, `Infinity` fail) | never | `:97` |
| `parseStartOpts(value)` | `v = (value && typeof value === 'object') ? value : {}`; `createdThisSession = (v.createdThisSession === true)`; when `isNum(v.insertAt)`, `insertAt = Math.max(0, Math.round(v.insertAt))`, else the key is absent | never | `:104-113` |
| `parseCaptureTarget(value)` | `value == null` returns `undefined` (the controller then uses Auto). Not an object: throw. `v.mode` must be a string in `CAPTURE_MODES = {'auto','window','area','screen'}`, else throw. The result is `{ mode }` plus only the fields of that mode: `screen` keeps `monitorId` when `isNum`; `window` keeps `window = { id, pid, title }` only when `v.window` is an object with `isNum(id)`, `isNum(pid)` and a string `title`; `area` keeps `area = { x, y, width, height }` only when all four are `isNum`. A malformed mode field is dropped silently (the target stays valid with only `mode`). | `'target must be an object'`, `'target.mode is invalid'` | `:90-95`, `:115-137` |
| `parseClick(value)` | not an object: `null`. `global = parsePoint(c.global)`, `image = parsePoint(c.image)` (both `src/shared/project.ts`); either null: `null`. `button` = `c.button` when it is a string in `{'left','right','middle','other'}`, else `'left'`. `radius` kept when a finite number `> 0`. `imageScale` kept when a finite number `> 0` ("Preserve the capture-time downscale factor (T2) across editor saves"). Result key order: `global`, `image`, `button`, then `radius`, then `imageScale` when present. | never | `:139-174` |
| `parseExportFormat(value)` | must be a string in `EXPORT_FORMATS = ['html','html-plain','pdf','markdown','docx','pptx']` | `` `format must be one of: ${EXPORT_FORMATS.join(', ')}` ``, which is `format must be one of: html, html-plain, pdf, markdown, docx, pptx` | `:176-184` |
| `parseStepPatch(value)` | not an object: throw. Output keys, each only when the rule holds, in this order: `caption`, `heading`, `body` when strings; `kind` when a string in `{'shot','text'}`; `callout` when the key is present (`'callout' in v`): the value if `isCalloutKind(v.callout)`, else `undefined` (clears the key); `crop` when present: `null` if `v.crop === null`, else `parseRect(v.crop)` (which may itself return `null`); `click` when present: `null` if `v.click === null`, else `parseClick(v.click)`; `markerColor` when a string matching `/^#[0-9a-fA-F]{3,8}$/`; `markerBaked` when a boolean; `reportZoom = Math.max(1, Math.min(6, v))` when `isNum`; `reportPanX`, `reportPanY` = `Math.max(0, Math.min(1, v))` when `isNum`; `annotations` when an array: the elements that are truthy objects with a string `type` and a string `id`. Unknown keys are dropped. | `'patch must be an object'` | `:186-227`; 04 2.18 owns the field semantics |
| `parseSopPatch(value)` | not an object: throw. Keeps `enabled` when boolean; `model` when `isSopModel`; `tone` when `isSopTone`; `effort` when `isSopEffort`; `customInstructions` when a string, sliced to `SOP_CUSTOM_INSTRUCTIONS_MAX` (2000) UTF-16 code units ("defense in depth"; the store also coerces). Unknown or invalid values are dropped silently. | `'settings patch must be an object'` | `:229-248`, `src/shared/sop.ts:29`, `:51-52`, `:73-78` |

### 2.4 Channel inventory

Every channel, grouped by namespace. Columns: **Bridge** is the `window.shotai` member (preload); **Args and main-side coercion** is exactly what main does before delegating; **Returns** is the resolved value; **Errors** lists the messages main or the delegate throws that the UI can show (delegate messages are quoted where they are short and owned elsewhere); **Owner** is the spec that owns the behavior; **Call sites** are the renderer lines that call it (tests excluded).

#### 2.4.1 App, shell and view (4 invoke)

| # | Channel | Dir | Bridge | Args and main-side coercion | Returns | Errors | Owner | Call sites | Citation |
|---|---|---|---|---|---|---|---|---|---|
| I1 | `app:get-info` | R->M invoke | `getAppInfo()` | none | `AppInfo { name: 'shotAI', version: app.getVersion(), platform: process.platform, arch: process.arch, electron, chrome, node }` (`name` is a literal, not `app.getName()`) | none | 06 (About line) | `src/renderer/project/Settings.tsx:154` | `src/main/ipc.ts:255-266` |
| I2 | `shell:open-external` | R->M invoke | `openExternal(url)` | the allowlist algorithm of 2.5.1 | `boolean`: `true` after `shell.openExternal(parsed.toString())` resolved, `false` when refused | rejects only if `shell.openExternal` rejects | 10 (allowlist and opener), 08 (SupportUrl origin) | `src/renderer/project/App.tsx:558` (update notice), `src/renderer/project/Settings.tsx:111` (Check now result), `:540`, `:579` (console and Request access links) | `src/main/ipc.ts:268-310` |
| I3 | `view:set-detail` | R->M invoke | `setDetailView(open, scale?)` | `open === true`; `scale` when `typeof scale === 'number'` (NaN passes this test), else `1`; then `setDetailView(open, scale)` (03 2.3.2), which returns early when the window is gone, when `open` and the window is maximized, and when the width would not change. The only invoke handler that logs nothing on entry. | `undefined` | none | 03 | `src/renderer/project/App.tsx:349` | `src/main/main.ts:454-458`, `:239-261` |
| I4 | `view:set-brand-menu` | R->M invoke | `setBrandMenu(state)` | `s = (state ?? {})`; logs debug through `mainLog` (not `ipcLog`) `` `ipc: view:set-brand-menu open=${s.projectOpen === true} project=${String(s.projectTheme)} app=${String(s.appBrand)}` ``; then `setBrandMenuState({ projectOpen: s.projectOpen === true, projectTheme: pinnedBrand(s.projectTheme), projectPinUnrecognised: s.projectPinUnrecognised === true, appBrand: coerceBrand(s.appBrand) })` | `undefined` | none (the deferred rebuild catches its own errors); the renderer also `.catch(() => undefined)`s the call ("The menu is cosmetic") | 03 | `src/renderer/project/App.tsx:396-406` | `src/main/main.ts:459-485`, `src/main/menu.ts:85-129` |

#### 2.4.2 Projects: folder, list and lifecycle (11 invoke)

| # | Channel | Dir | Bridge | Args and main-side coercion | Returns | Errors | Owner | Call sites | Citation |
|---|---|---|---|---|---|---|---|---|---|
| P1 | `projects:get-dir` | R->M invoke | `projects.getDir()` | none | `string` (the projects root) | none | 01, 10 | `src/renderer/project/Settings.tsx:155` | `src/main/ipc.ts:312-315` |
| P2 | `projects:choose-dir` | R->M invoke | `projects.chooseDir()` | `current = await getProjectsDir()`; parent = `BrowserWindow.fromWebContents(event.sender)`; `dialog.showOpenDialog(parent?, { title: 'Choose shotAI projects folder', defaultPath: current, properties: ['openDirectory', 'createDirectory'] })`; canceled or no path: `null`; else `await setProjectsDir(dir)` (`mkdir -p` then persist; recents are NOT cleared) and return `dir` | `string \| null` | `mkdir` and settings write errors propagate | 01, 06 | `src/renderer/project/Settings.tsx:290` | `src/main/ipc.ts:317-338`, `src/main/project-store.ts:45-48` |
| P3 | `projects:list-recent` | R->M invoke | `projects.listRecent()` | none | `ProjectSummary[]` from recents, pruning unreadable entries | none | 01 | none in the renderer (`src/main/selftest.ts:40` calls the store function directly) | `src/main/ipc.ts:340-343`, `src/main/project-store.ts:452-464` |
| P4 | `projects:list` | R->M invoke | `projects.list()` | none | `ProjectSummary[]` (root folders then recents, deduped; Home sorts) | none | 01 | `src/renderer/project/App.tsx:97` | `src/main/ipc.ts:345-348` |
| P5 | `projects:create` | R->M invoke | `projects.create(title)` | `typeof title === 'string' ? title : undefined`; the store trims and falls back to the timestamped default name | `ProjectSummary` | store IO errors | 01 | `src/renderer/project/App.tsx:467`, `:504` | `src/main/ipc.ts:350-357`, `src/main/project-store.ts:297-303` |
| P6 | `projects:rename` | R->M invoke | `projects.rename(path, title)` | `asString(projectPath)`; `typeof title === 'string' ? title : ''`; the store sets `title.trim() \|\| defaultTitle()` (an empty rename resets to the timestamped default name) | `ProjectSummary` | `projectPath must be a string`; `Project path is not within the projects directory` | 01, 06 | `src/renderer/project/ProjectList.tsx:86` | `src/main/ipc.ts:359-368`, `src/main/project-store.ts:520-537` |
| P7 | `projects:delete` | R->M invoke | `projects.delete(path)` | `asString(projectPath)` | `undefined` | the gate message | 01, 06 | `src/renderer/project/ProjectList.tsx:104`, `:290` | `src/main/ipc.ts:370-376` |
| P8 | `projects:reveal` | R->M invoke | `projects.reveal(path)` | `asString(projectPath)`; gate; `shell.showItemInFolder(resolved)` | `undefined` | the gate message | 01, 03 | `src/renderer/project/ProjectList.tsx:403` | `src/main/ipc.ts:378-384`, `src/main/project-store.ts:542-545` |
| P9 | `projects:archive` | R->M invoke | `projects.archive(path)` | `asString(projectPath)` | `ProjectSummary` | gate; 01's archive messages | 01, 06 | `src/renderer/project/ProjectList.tsx:130`, `:296` | `src/main/ipc.ts:386-392` |
| P10 | `projects:unarchive` | R->M invoke | `projects.unarchive(path)` | `asString(projectPath)` | `ProjectSummary` | gate; 01's restore messages | 01, 06 | `src/renderer/project/ProjectList.tsx:131`, `:295` | `src/main/ipc.ts:394-400` |
| P11 | `projects:open` | R->M invoke | `projects.open(path)` | `asString(projectPath)`; `openProjectWithId` (open, then register an opaque session id for `shot://`) | `{ projectId: string, manifest: ProjectManifest }` | gate; missing folder or `project.json`; parse errors | 01 | `src/renderer/project/App.tsx:444`, `src/renderer/project/ProjectList.tsx:116`, `:300`, `src/renderer/project/store.ts:111` | `src/main/ipc.ts:402-408`, `src/main/project-store.ts:430-435` |

#### 2.4.3 Projects: steps, document fields and SOP revert (11 invoke)

| # | Channel | Dir | Bridge | Args and main-side coercion | Returns | Errors | Owner | Call sites | Citation |
|---|---|---|---|---|---|---|---|---|---|
| S1 | `projects:update-step` | R->M invoke | `projects.updateStep(path, stepId, patch, png?)` | `asString(projectPath)`, `asString(stepId)`, `parseStepPatch(patch)`; `png`: a `Uint8Array` becomes `Buffer.from(png)`, an `ArrayBuffer` becomes `Buffer.from(new Uint8Array(png))`, anything else becomes `null` (no render written, no error) | `ProjectManifest` | `patch must be an object`; `` `step ${stepId} not found` ``; gate; 04's render-write errors | 01, 04, 05 | `src/renderer/editor/Editor.tsx:600` (editor save with PNG), `src/renderer/project/Report.tsx:478`, `:489` (zoom, pan), `:542`, `:556` (caption, instructions), `:636`, `:651` (text step, callout), `src/renderer/project/sop-prepare.ts:58` (re-bake before egress) | `src/main/ipc.ts:410-433` |
| S2 | `projects:import-step` | R->M invoke | `projects.importStep(path, bytes, atIndex?)` | `bytes` converted as S1's `png`; `null` or length 0: throw; `length > 60 * 1024 * 1024`: throw; `asString(projectPath)`; `atIndex` when `isNum`, else `null` (append) | `ProjectManifest` | `No image data received`; `Image too large (max 60 MB)`; `Unsupported file \u2014 please choose a PNG or JPEG image.` (01, magic bytes) | 01, 05 | `src/renderer/project/ProjectDetail.tsx:400` | `src/main/ipc.ts:435-458`, `src/main/project-store.ts:1051` |
| S3 | `projects:delete-step` | R->M invoke | `projects.deleteStep(path, stepId)` | `asString` both | `ProjectManifest` | `` `step ${stepId} not found` ``; gate | 01, 05 | `src/renderer/project/Report.tsx:591`, `:684` | `src/main/ipc.ts:460-469`, `src/main/project-store.ts:854` |
| S4 | `projects:reorder-steps` | R->M invoke | `projects.reorderSteps(path, orderedIds)` | `orderedIds` must be an array whose every element is a string, else throw; `asString(projectPath)` | `ProjectManifest` | `orderedIds must be an array of strings`; gate | 01, 05 | `src/renderer/project/Report.tsx:509` | `src/main/ipc.ts:471-483` |
| S5 | `projects:merge-steps` | R->M invoke | `projects.mergeSteps(path, keepId, dropId, patch, png?)` | `asString` for the three strings; `parseStepPatch(patch)`; `png` as S1 | `ProjectManifest` | `cannot merge a step into itself`; `` `step ${keepId} not found` ``; `` `step ${dropId} not found` ``; gate | 01, 05 | `src/renderer/project/merge.ts:94` | `src/main/ipc.ts:485-510`, `src/main/project-store.ts:809-815` |
| S6 | `projects:add-text-step` | R->M invoke | `projects.addTextStep(path, atIndex, callout?)` | `callout` when `isCalloutKind`, else `undefined`; `atIndex` when `isNum`, else `Number.MAX_SAFE_INTEGER` (append); `asString(projectPath)` | `ProjectManifest` | gate | 01, 05 | `src/renderer/project/ProjectDetail.tsx:367` | `src/main/ipc.ts:512-523` |
| S7 | `projects:redact-scan` | R->M invoke | `projects.redactScan(path, stepId)` | `id = asString(stepId)`; `{ dir, manifest } = await getProjectForRead(asString(projectPath))`; first step with `s.id === id`; no step or empty `screenshot`: `[]`; `abs = confinePath(dir, step.screenshot)` (a hand-edited manifest could carry a traversal path); `null`: `[]`; else `scanForSensitiveRects(abs)` (OCR of the ORIGINAL capture; best-effort, `[]` on failure) | `Rect[]` in image pixels | argument errors; the gate message (the gate runs before the step lookup, so an unknown project throws rather than returning `[]`) | 04 | `src/renderer/editor/Editor.tsx:547` | `src/main/ipc.ts:525-543` |
| S8 | `projects:set-intro` | R->M invoke | `projects.setIntro(path, intro)` | `asString(projectPath)`; `intro` passed untouched to `setProjectIntro`, which coerces it (`coerceIntro`) | `ProjectManifest` | gate | 01, 05 | `src/renderer/project/Report.tsx:665` | `src/main/ipc.ts:846-853` |
| S9 | `projects:set-display-scale` | R->M invoke | `projects.setDisplayScale(path, scale)` | `asString(projectPath)`; `scale` untouched; the store clamps (`clampScale`) "so an untrusted value cannot widen a document past what the app supports" | `ProjectManifest` | gate | 01, 05 | `src/renderer/project/ProjectDetail.tsx:81` | `src/main/ipc.ts:865-876` |
| S10 | `projects:set-theme` | R->M invoke | `projects.setProjectTheme(path, brand)` | `asString(projectPath)`; `brand` untouched; the store coerces (unknown string becomes the default brand, `null` clears) and refuses a write whose RAW value equals the stored one, so it cannot re-date the project | `ProjectManifest` | gate | 01 (brand semantics per 10's `BrandPalette`, R-ARCH-14) | `src/renderer/project/App.tsx:423` (from the View, Brand menu push) | `src/main/ipc.ts:854-863`, `src/shared/ipc.ts:400-407` |
| S11 | `projects:revert-sop` | R->M invoke | `projects.revertSop(path)` | `asString(projectPath)` | `ProjectManifest` | `Nothing to revert \u2014 no AI edits are recorded for this project.`; gate | 07 | `src/renderer/project/SopPanel.tsx:183` | `src/main/ipc.ts:839-845`, `src/main/sop-apply.ts:255` |

#### 2.4.4 Export and packages (7 invoke)

| # | Channel | Dir | Bridge | Args and main-side coercion | Returns | Errors | Owner | Call sites | Citation |
|---|---|---|---|---|---|---|---|---|---|
| X1 | `projects:export` | R->M invoke | `projects.export(path, format)` | `asString(projectPath)`, `parseExportFormat(format)`; `exportProject(path, format, { saveAs: true, brand: await getBrand(), onProgress: p => { if (!sender.isDestroyed()) sender.send('projects:export-progress', p) } })`; the brand is read at call time, never cached | `ExportResult { format, outputPath, canceled? }` (`{ canceled: true, outputPath: '' }` when the Save dialog is dismissed) | the format message; 09's messages | 09 | `src/renderer/project/ProjectDetail.tsx:323`, `src/renderer/project/ProjectList.tsx:118` | `src/main/ipc.ts:545-574` |
| X2 | `projects:export-to-dir` | R->M invoke | `projects.exportToDir(path, format, dir)` | as X1 plus `asString(dir)`; options `{ targetDir: dir, reveal: false, brand }`; no progress | `ExportResult` | as X1, `dir must be a string` | 09 | `src/renderer/project/ProjectList.tsx:315` | `src/main/ipc.ts:576-588` |
| X3 | `projects:export-to-own-folder` | R->M invoke | `projects.exportToOwnFolder(path, format)` | options `{ reveal: false, brand }`; no progress | `ExportResult` | as X1 | 09 | `src/renderer/project/ProjectList.tsx:307` | `src/main/ipc.ts:590-600` |
| X4 | `projects:choose-export-dir` | R->M invoke | `projects.chooseExportDir()` | none; the dialog's parent is `BrowserWindow.getFocusedWindow()` (NOT the sender); `{ title: 'Choose a folder for the exports', properties: ['openDirectory', 'createDirectory'] }` | `string \| null` | none | 09 | `src/renderer/project/ProjectList.tsx:311` | `src/main/ipc.ts:602-605`, `src/main/export.ts:133-145` |
| X5 | `projects:reveal-export-dir` | R->M invoke | `projects.revealExportDir(dir)` | `asString(dir)`; `fs.stat(dir)`; when a directory, `shell.openPath(dir)`; every error swallowed | `undefined` | `dir must be a string` only | 09 | `src/renderer/project/ProjectList.tsx:318` | `src/main/ipc.ts:607-610`, `src/main/export.ts:918-925` |
| X6 | `projects:export-package` | R->M invoke | `projects.exportPackage(path, includeOriginals)` | `asString(projectPath)`; `includeOriginals === true` | `PackageResult { outputPath, includeOriginals }` | 09's messages | 09 | `src/renderer/project/ProjectDetail.tsx:347` | `src/main/ipc.ts:612-618` |
| X7 | `projects:import-package` | R->M invoke | `projects.importPackage()` | none; parent = the sender's window; `{ title: 'Import a shotAI project package', properties: ['openFile'], filters: [{ name: 'shotAI package', extensions: ['zip'] }] }`; canceled or no path: `null`; else `importPackage(path)` | `ProjectSummary \| null` | 09's package messages (for example `This file is not a valid .zip package.`) | 09 | `src/renderer/project/App.tsx:485` | `src/main/ipc.ts:620-636` |

#### 2.4.5 Settings (20 invoke)

Every getter reads `settings.json` through `load()`; every setter goes through the settings module's serialized `mutate` and returns the stored value. Coercions are in `src/main/settings.ts`; 10 owns them.

| # | Channel | Bridge | Args and main-side coercion | Returns | Call sites | Citation |
|---|---|---|---|---|---|---|
| G1 | `settings:get-sop` | `settings.getSop()` | none | `SopSettings` (never the key) | `src/renderer/project/Settings.tsx:152`, `src/renderer/project/ProjectDetail.tsx:253` | `src/main/ipc.ts:639-642` |
| G2 | `settings:set-sop` | `settings.setSop(patch)` | `parseSopPatch` (2.3); the store then coerces `{ ...s.sop, ...patch }` against the current values | full coerced `SopSettings`; throws `settings patch must be an object` | `src/renderer/project/Settings.tsx:314` | `src/main/ipc.ts:643-649`, `src/main/settings.ts:429-434` |
| G3 | `settings:get-remote-visible` | `settings.getRemoteVisible()` | none; also refreshes the synchronous cache | `boolean` | `src/renderer/project/Settings.tsx:156` | `src/main/ipc.ts:650-653`, `src/main/settings.ts:272-275` |
| G4 | `settings:set-remote-visible` | `settings.setRemoteVisible(v)` | `value === true`; `setRemoteVisible` sets the synchronous cache FIRST ("the next grab must see this"), then persists; after the write resolves, `applyRemoteVisibility(next)` applies it to the open windows ("so the toggle works without a restart") | `boolean` | `src/renderer/project/Settings.tsx:213` | `src/main/ipc.ts:654-665`, `src/main/settings.ts:277-284`, commit `53045c6` |
| G5 | `settings:get-capture-scale` | `settings.getCaptureScale()` | none; refreshes the cache | `number` | `src/renderer/project/Settings.tsx:157` | `src/main/ipc.ts:666-669` |
| G6 | `settings:set-capture-scale` | `settings.setCaptureScale(v)` | `typeof value === 'number' ? value : NaN`; `clampCaptureScale`: finite: `min(1, max(0.5, v))`, else `0.85` | `number` | `src/renderer/project/Settings.tsx:223` | `src/main/ipc.ts:670-677`, `src/main/settings.ts:31-35`, `:301-308` |
| G7 | `settings:get-has-seen-tour` | `settings.getHasSeenTour()` | none | `boolean` | `src/renderer/project/App.tsx:194` | `src/main/ipc.ts:678-681` |
| G8 | `settings:set-has-seen-tour` | `settings.setHasSeenTour(v)` | `value === true` | `boolean` | `src/renderer/project/App.tsx:205` | `src/main/ipc.ts:682-688` |
| G9 | `settings:get-user-name` | `settings.getUserName()` | none | `string` | `src/renderer/project/Settings.tsx:158` | `src/main/ipc.ts:689-692` |
| G10 | `settings:set-user-name` | `settings.setUserName(v)` | `typeof value === 'string' ? value : ''`; `coerceUserName`: `v.slice(0, 120)` (no trim, despite the comment) | `string` | `src/renderer/project/Settings.tsx:233` | `src/main/ipc.ts:693-699`, `src/main/settings.ts:45-48` |
| G11 | `settings:get-include-name` | `settings.getIncludeNameInReports()` | none | `boolean` | `src/renderer/project/Settings.tsx:159` | `src/main/ipc.ts:700-703` |
| G12 | `settings:set-include-name` | `settings.setIncludeNameInReports(v)` | `value === true` | `boolean` | `src/renderer/project/Settings.tsx:237`, `:247` | `src/main/ipc.ts:704-710` |
| G13 | `settings:get-archive-age` | `settings.getArchiveAgeDays()` | none | `number` | `src/renderer/project/Settings.tsx:160` | `src/main/ipc.ts:711-714` |
| G14 | `settings:set-archive-age` | `settings.setArchiveAgeDays(v)` | `typeof value === 'number' ? value : NaN`; `clampArchiveAge`: non-finite: `90`; `v <= 0`: `0` (never); else `min(1825, max(1, round(v)))` | `number` | `src/renderer/project/Settings.tsx:256` | `src/main/ipc.ts:715-722`, `src/main/settings.ts:37-43` |
| G15 | `settings:get-theme` | `settings.getTheme()` | none | `ThemePref` | `src/renderer/project/App.tsx:362`, `src/renderer/project/Settings.tsx:161` | `src/main/ipc.ts:723-726` |
| G16 | `settings:set-theme` | `settings.setTheme(v)` | untouched; `coerceTheme`: `'light'`, `'dark'`, `'system'` kept, else `'system'` | `ThemePref` | `src/renderer/project/Settings.tsx:280` | `src/main/ipc.ts:727-730`, `src/main/settings.ts:25-28` |
| G17 | `settings:get-brand` | `settings.getBrand()` | none | `BrandId` | `src/renderer/project/App.tsx:363`, `src/renderer/project/Settings.tsx:162` | `src/main/ipc.ts:731-734` |
| G18 | `settings:set-brand` | `settings.setBrand(v)` | untouched; `coerceBrand` (unknown becomes the default brand) | `BrandId` | `src/renderer/project/Settings.tsx:267` | `src/main/ipc.ts:735-738`, `src/main/settings.ts:413-419` |
| G19 | `settings:get-update-check` | `settings.getUpdateCheckEnabled()` | none | `boolean` | `src/renderer/project/Settings.tsx:163` | `src/main/ipc.ts:739-742` |
| G20 | `settings:set-update-check` | `settings.setUpdateCheckEnabled(v)` | `value === true` | `boolean` | `src/renderer/project/Settings.tsx:99` | `src/main/ipc.ts:743-746` |

All twenty are R->M invoke, owned by 10 (storage and coercion) and 06 (the controls). None throws except G2's shape error and the settings write's IO errors.

#### 2.4.6 Update check (2 invoke)

| # | Channel | Dir | Bridge | Behavior | Returns | Owner | Call sites | Citation |
|---|---|---|---|---|---|---|---|---|
| U1 | `update:pending` | R->M invoke | `updates.pending()` | `getPendingUpdate()`: the result of this launch's startup check if it found an update, or the last manual check's if that found one; else `null`. "The renderer MUST call this on mount." | `UpdateCheckResult \| null` | 10 | `src/renderer/project/App.tsx:65` | `src/main/ipc.ts:747-753`, `src/main/update-state.ts:14-25`, `src/shared/ipc.ts:510-515` |
| U2 | `update:check` | R->M invoke | `updates.check()` | `result = await checkForUpdate({ currentVersion: app.getVersion(), fetchImpl: fetch })` ignoring the once-a-day throttle; `await setLastUpdateCheckAt(Date.now())`; `setPendingUpdate(result)` (stores it only when `available`, so an up-to-date result clears a stale notice); return `result`. The timestamp is written even when `result.error` is set. | `UpdateCheckResult` (a check failure is `{ available: false, error }`, never a throw; but a failed `setLastUpdateCheckAt` settings write DOES reject the call, and then `setPendingUpdate` is not reached, EDGE-IPC-43) | 10 | `src/renderer/project/Settings.tsx:108` | `src/main/ipc.ts:754-768`, `src/shared/ipc.ts:505-509` |

#### 2.4.7 Claude, API key and Entra (9 invoke, 1 send)

| # | Channel | Dir | Bridge | Args and main-side coercion | Returns | Errors | Owner | Call sites | Citation |
|---|---|---|---|---|---|---|---|---|---|
| C1 | `claude:key-status` | R->M invoke | `claude.keyStatus()` | none | `ApiKeyStatus { hasKey, source: 'stored' \| 'env' \| 'none', encryptionAvailable, hasStoredCiphertext }` (never the key) | none | 08 | `src/renderer/project/Settings.tsx:153`, `:303`, `src/renderer/project/SopPanel.tsx:73` | `src/main/ipc.ts:769-772` |
| C2 | `claude:set-key` | R->M invoke | `claude.setApiKey(key)` | `asString(key, 'apiKey')`; the log line carries the channel only ("never the key value"); `setApiKey` trims, rejects empty, requires `safeStorage` | `undefined` | `apiKey must be a string`; `API key is empty.`; `Secure storage is unavailable on this system, so the API key cannot be saved. Set the ANTHROPIC_API_KEY environment variable instead.` | 08 | `src/renderer/project/Settings.tsx:326` | `src/main/ipc.ts:773-780`, `src/main/secrets.ts:94-106` |
| C3 | `claude:clear-key` | R->M invoke | `claude.clearApiKey()` | none | `undefined` | secrets write errors | 08 | `src/renderer/project/Settings.tsx:341` | `src/main/ipc.ts:781-784` |
| C4 | `auth:status` | R->M invoke | `auth.status()` | 2.5.4 (invalidates the federation cache first) | `AuthStatus` (seven fields, no token, claim or expiry) | none expected | 08 | `src/renderer/project/Settings.tsx:164`, `src/renderer/project/SopPanel.tsx:74` | `src/main/ipc.ts:786-816`, `src/shared/ipc.ts:53-79` |
| C5 | `auth:sign-in` | R->M invoke | `auth.signIn()` | `entra = await appAuth().entra()`; null: throw; else `await entra.signInInteractive()` and DISCARD the returned token ("it must not cross the IPC boundary") | `undefined` | `shotAI is not set up for Microsoft sign-in on this machine.`; MSAL and browser errors (08) | 08 | `src/renderer/project/Settings.tsx:190` | `src/main/ipc.ts:817-826` |
| C6 | `auth:sign-out` | R->M invoke | `auth.signOut()` | `(await appAuth().entra())?.signOut()`: a no-op when federation is not configured; a stored API key is deliberately NOT touched | `undefined` | cache write errors | 08 | `src/renderer/project/Settings.tsx:203` | `src/main/ipc.ts:827-834` |
| C7 | `claude:test-connection` | R->M invoke | `claude.testConnection()` | none | `TestConnectionResult { ok, mode?, model?, error?, leg? }`; expected failures are returned, not thrown | none expected | 08 (legs 1, 2), 07 (leg 3) | `src/renderer/project/Settings.tsx:355` | `src/main/ipc.ts:835-838`, `src/shared/ipc.ts:93-110` |
| C8 | `claude:estimate` | R->M invoke | `claude.estimate(path)` | `asString(projectPath)` | `SopEstimate { inputTokens, model, estCostUsd }` | `AI SOP generation is turned off.`; 07's mapped errors | 07 | `src/renderer/project/SopPanel.tsx:134` | `src/main/ipc.ts:877-883` |
| C9 | `claude:generate-sop` | R->M invoke | `claude.generateSop(path)` | `asString(projectPath)`; progress callback `p => { if (!sender.isDestroyed()) sender.send('claude:sop-progress', p) }` | `ProjectManifest` (after the inline apply) | 07's messages | 07 | `src/renderer/project/SopPanel.tsx:165` | `src/main/ipc.ts:884-893` |
| C10 | `claude:cancel` | R->M send | `claude.cancel()` | none; `cancelClaude()` aborts whichever estimate or generation is in flight (global, not per project); "mirrors region:cancel" | none | none | 07 | `src/renderer/project/SopPanel.tsx:154` | `src/main/ipc.ts:894-899` |

#### 2.4.8 Capture and area selection (10 invoke, 2 send)

| # | Channel | Dir | Bridge | Args and main-side coercion | Returns | Errors | Owner | Call sites | Citation |
|---|---|---|---|---|---|---|---|---|---|
| K1 | `capture:start` | R->M invoke | `capture.start(path, target?, opts?)` | `asString(projectPath)`; `target: parseCaptureTarget(target)`; spread of `parseStartOpts(opts)` | `CaptureState` | `target must be an object`; `target.mode is invalid`; `A recording is already in progress for another project` (a second start for the SAME project returns the current state); open errors | 02 | `src/renderer/project/App.tsx:446` | `src/main/ipc.ts:901-910`, `src/main/CaptureController.ts:666-686` |
| K2 | `capture:single` | R->M invoke | `capture.captureSingle(path, atIndex)` | `asString(projectPath)`; `atIndex` when `isNum`, else `Number.MAX_SAFE_INTEGER` | `CaptureState` | `A recording is already in progress` | 02 | none (legacy click-based path, 02 Q-CAP-1) | `src/main/ipc.ts:911-920`, `src/main/CaptureController.ts:751-753` |
| K3 | `capture:screenshot` | R->M invoke | `capture.screenshot(path, target, atIndex)` | `t = parseCaptureTarget(target)`; `!t \|\| t.mode === 'auto'`: throw ("'auto' classifies off the clicked window, which doesn't exist here"); `asString(projectPath)`; `atIndex` as K2 | `ProjectManifest` | `A screenshot needs an explicit target (screen, window, or area).`; `A recording is already in progress`; `That window is no longer open \u2014 reopen it and try the screenshot again.`; `That screen area is off-screen now \u2014 drag the area again and retry.`; `Could not capture the screen \u2014 make sure the target is visible, then try again.` | 02 | `src/renderer/project/ProjectDetail.tsx:421` | `src/main/ipc.ts:921-937`, `src/main/CaptureController.ts:806-889` |
| K4 | `capture:list-targets` | R->M invoke | `capture.listTargets()` | none | `{ windows: WindowInfo[], monitors: MonitorInfo[] }` | enumeration errors | 02 | `src/renderer/project/App.tsx:103`, `src/renderer/project/useCaptureTarget.ts:82` | `src/main/ipc.ts:938-941` |
| K5 | `region:select-area` | R->M invoke | `region.selectArea()` | `win = BrowserWindow.fromWebContents(event.sender)`; `win?.hide()`; `try { return await region.selectArea() } finally { if (win && !win.isDestroyed()) { win.show(); win.focus() } }` | `Rect \| null` in global physical pixels | none | 03 | `src/renderer/project/App.tsx:173`, `src/renderer/project/useCaptureTarget.ts:121` | `src/main/ipc.ts:943-960` |
| K6 | `capture:pause` | R->M invoke | `capture.pause()` | none; sets `paused` when a session exists, disarms the menu arm, logs `recording paused`, emits state (even when idle) | `CaptureState` | none | 02 | `src/renderer/project/App.tsx:584`, `src/renderer/toolbar/App.tsx:58` | `src/main/ipc.ts:961-964`, `src/main/CaptureController.ts:929-935` |
| K7 | `capture:resume` | R->M invoke | `capture.resume()` | mirror of K6 (`recording resumed`) | `CaptureState` | none | 02 | `src/renderer/project/App.tsx:594`, `src/renderer/toolbar/App.tsx:59` | `src/main/ipc.ts:965-968`, `src/main/CaptureController.ts:937-943` |
| K8 | `capture:stop` | R->M invoke | `capture.stop()` | detaches triggers, awaits in-flight captures, ends the session, restores windows, emits state | `CaptureState` (idle) | none | 02 | `src/renderer/project/App.tsx:604`, `src/renderer/toolbar/App.tsx:60` | `src/main/ipc.ts:969-972`, `src/main/CaptureController.ts:945-956` |
| K9 | `capture:discard` | R->M invoke | `capture.discard()` | 02 2.2.7 (whole project or session steps; cleanup errors logged, not thrown) | `{ state: CaptureState, projectDeleted: boolean }` | none | 02 | `src/renderer/toolbar/App.tsx:73` | `src/main/ipc.ts:973-976`, `src/main/CaptureController.ts:962-990` |
| K10 | `capture:get-state` | R->M invoke | `capture.getState()` | none | `CaptureState { status, projectPath, projectTitle, stepCount, willDeleteProjectOnDiscard }`; idle is `{ 'idle', null, null, 0, false }` | none | 02 | `src/renderer/project/App.tsx:187`, `src/renderer/toolbar/App.tsx:9` | `src/main/ipc.ts:977-980`, `src/main/CaptureController.ts:629-646` |
| K11 | `region:complete` | R->M send (overlay) | `region.complete(rect)` | `finish(event, parseRect(rect))`: ignored unless a selection is pending AND the sender's window is one of the pending selection's overlays; a rect of width and height `>= 4` (CSS px) is converted to global physical px (2.5.6), else treated as cancel | none | none | 03 | `src/renderer/overlay/App.tsx:52` | `src/main/RegionService.ts:32-34`, `:114-140` |
| K12 | `region:cancel` | R->M send (overlay) | `region.cancel()` | `finish(event, null)` with the same sender check | none | none | 03 | `src/renderer/overlay/App.tsx:30` (Escape), `:38` (non-left button), `:54` (click without a drag) | `src/main/RegionService.ts:35-37` |

#### 2.4.9 Push events (10)

| # | Channel | Dir and target | Bridge | Payload | Emitted when | Owner | Subscribers | Citation |
|---|---|---|---|---|---|---|---|---|
| E1 | `projects:changed` | M->R, project window | `projects.onChanged(cb)` | none | once, after the startup auto-archive moved at least one project (`archived > 0`) and the project window exists; an auto-archive failure logs warn `startup auto-archive failed (non-fatal):` and pushes nothing | 01, 06 | `src/renderer/project/App.tsx:355` (re-list) | `src/main/main.ts:504-515` |
| E2 | `projects:export-progress` | M->R, the sender of X1 | `projects.onExportProgress(cb)` | `ExportProgress { done, total }` | per encoded image of a single export (only formats that embed images) | 09 | `src/renderer/project/ProjectDetail.tsx:266` | `src/main/ipc.ts:562-571`, `src/shared/ipc.ts:131-141` |
| E3 | `claude:sop-progress` | M->R, the sender of C9 | `claude.onSopProgress(cb)` | `SopProgress { stage: 'preparing' \| 'thinking' \| 'writing' \| 'done', chars? }` | during generation | 07 | `src/renderer/project/SopPanel.tsx:163` | `src/main/ipc.ts:884-891`, `src/shared/ipc.ts:121-126` |
| E4 | `update:available` | M->R, project window | `updates.onAvailable(cb)` | `UpdateCheckResult` (always `available: true`) | at most once per launch, by the startup check, after `setPendingUpdate` | 10 | `src/renderer/project/App.tsx:62` | `src/main/main.ts:517-557` |
| E5 | `capture:state-changed` | M->R, every window | `capture.onStateChanged(cb)` | `CaptureState` | after start, pause, resume, stop, discard, `capture:single` arming, after each broadcast step, and once (idle) in the `finally` of every `capture:screenshot` that got past target validation | 02 | `src/renderer/project/App.tsx:238`, `src/renderer/toolbar/App.tsx:10` | `src/main/CaptureController.ts:658-660`, `:891-895` |
| E6 | `capture:step-added` | M->R, every window | `capture.onStepAdded(cb)` | `ProjectStep` | after each captured step lands, BEFORE the matching `capture:state-changed`; for the no-click one-shot (`broadcast: false`) both the step-added event and the per-step state emit are suppressed, and the screenshot's own `finally` emits one idle `capture:state-changed` instead (EDGE-IPC-33) | 02 | `src/renderer/project/App.tsx:239` | `src/main/CaptureController.ts:1455-1463` |
| E7 | `capture:error` | M->R, every window | `capture.onError(cb)` | `string` (the error's message, possibly empty; `String(e)` for a non-`Error` throw) | a queued capture job threw (logged error `capture failed:` first); the project window shows `` `Capture error: ${message}` `` (`src/renderer/project/App.tsx:242-244`), the pill substitutes `A capture failed \u2014 see the log for details.` for an empty or blank message (`src/renderer/toolbar/App.tsx:34-36`) | 02 | `src/renderer/project/App.tsx:242`, `src/renderer/toolbar/App.tsx:34` | `src/main/CaptureController.ts:991-999` |
| E8 | `menu:open-settings` | M->R, project window | `onOpenSettings(cb)` | none | File, Settings (`CmdOrCtrl+,`) | 03, 06 | `src/renderer/project/App.tsx:225` | `src/main/menu.ts:216-219` |
| E9 | `menu:import-project` | M->R, project window | `onImportProject(cb)` | none | File, Import Project… (`CmdOrCtrl+O`) | 03, 06 | `src/renderer/project/App.tsx:228` | `src/main/menu.ts:210-214` |
| E10 | `menu:set-project-theme` | M->R, project window | `onMenuSetProjectTheme(cb)` | `BrandId \| null` (`null` is "App default") | a View, Brand item was clicked; main records the choice in its own menu state first | 03 | `src/renderer/project/App.tsx:420-427` (calls S10 with the choice untouched) | `src/main/menu.ts:172-183` |

**Totals.** Invoke: 4 + 11 + 11 + 7 + 20 + 2 + 9 + 10 = **74** (72 in `src/main/ipc.ts`, 2 in `src/main/main.ts`). Send: C10, K11, K12 = **3**. Push: **10**. All: **87**, which equals the number of values in `IpcChannels` (`src/shared/ipc.ts:175-278`).

### 2.5 Handler behaviors with logic of their own

#### 2.5.1 `shell:open-external` allowlist (`src/main/ipc.ts:268-310`)

"The app otherwise denies all window-open + confines navigation, so this is the ONLY egress to a browser \u2014 keep it fail-closed."

| Step | Rule |
|---|---|
| 1 | `typeof url !== 'string'`: return `false` (no log). |
| 2 | `parsed = new URL(url)`; a parse failure returns `false` (no log). |
| 3 | `host = parsed.hostname.toLowerCase()`. |
| 4 | `allowed = parsed.protocol === 'https:' && (host === 'anthropic.com' \|\| host.endsWith('.anthropic.com') \|\| host === 'github.com')`. `github.com` is matched EXACTLY "so this can't be widened into user-content subdomains like raw./objects./codeload.github.com" (`037858d`, #54). |
| 5 | If not allowed and the protocol is `https:`: `cfg = await getFederationConfig()` (cached; NOT invalidated here); when `cfg?.supportUrl`, `allowed = new URL(cfg.supportUrl).origin === parsed.origin`; a malformed configured URL never widens the allowlist (#63). |
| 6 | Not allowed: log warn `` `refused openExternal for non-allowlisted URL: ${parsed.origin}` `` (origin only, never the path or query) and return `false`. |
| 7 | Allowed: `await shell.openExternal(parsed.toString())` (the normalized URL), return `true`. |

#### 2.5.2 Dialog-owning handlers

| Channel | Parent window | Title | Properties and filters | Cancel result |
|---|---|---|---|---|
| `projects:choose-dir` | the sender's window, else none | `Choose shotAI projects folder` | `openDirectory`, `createDirectory`; `defaultPath` = current root | `null` |
| `projects:choose-export-dir` | the focused window, else none | `Choose a folder for the exports` | `openDirectory`, `createDirectory` | `null` |
| `projects:import-package` | the sender's window, else none | `Import a shotAI project package` | `openFile`; filter `shotAI package` with extension `zip` | `null` |
| `projects:export` (inside 09) | 09 | 09 (`Export`, or `Export Markdown (saved as a folder with its images)`) | 09 | `{ canceled: true }` |

Citations: `src/main/ipc.ts:317-338`, `:602-605`, `:620-636`, `src/main/export.ts:133-145`.

#### 2.5.3 `region:select-area` hide and restore (`src/main/ipc.ts:943-960`)

The requesting window is hidden before the overlays open "so it's not in the way of, or part of, the area the user is selecting", and restored in `finally` with `show()` then `focus()` only if it still exists. The restore runs on success, on cancel and on any exception.

#### 2.5.4 `auth:status` computation (`src/main/ipc.ts:788-816`)

| Step | Rule |
|---|---|
| 1 | `invalidateFederationConfig()` FIRST, so an administrator's policy correction is picked up without a restart ("THIS CALL IS THE WHOLE POINT of the invalidator"; it shipped uncalled until `1ecebd0`). A failing registry read degrades to the baked values, not to "federation off". |
| 2 | `cfg = await auth.federation()`; `entra = cfg ? await auth.entra() : null`; `account = entra ? await entra.signedInAccount() : null`; `key = await getApiKeyStatus()`; `signedIn = !!account`. |
| 3 | `mode = signedIn ? 'federated' : key.hasKey ? 'apiKey' : 'none'`. |
| 4 | `federationAvailable = !!cfg`; `account = account?.username ?? null`; `encryptionAvailable`, `hasStoredCiphertext` from `key`; `supportUrl = cfg ? (cfg.supportUrl ?? DEFAULT_SUPPORT_URL) : null` (`src/main/entra/config-validate.ts:82`). |

The renderer's whole auth vocabulary is this object plus the three verbs. "Deliberately absent: expiresAt, scope, request ids, any token prefix, any JWT claim" (`src/shared/ipc.ts:53-60`). The UPN "IS personal data: keep it out of logs, diagnostics and exports" (`:69-71`).

#### 2.5.5 `view:set-brand-menu` coercion asymmetry (`src/main/main.ts:459-486`)

"The two fields narrow DIFFERENTLY and it is not an oversight (#95)." `appBrand` is the app's own setting, so an unreadable value falls back to the default brand (`coerceBrand`). `projectTheme` may name a brand only a newer build knows; for that the honest tick is "App default" (what the document actually renders), so it narrows to `null` (`pinnedBrand`). `projectPinUnrecognised` is "trusted as a plain boolean rather than re-derived here, because main does not have the manifest", compared with `=== true` (#107, `38908cd`). The menu refuses a rebuild when all four fields are unchanged, and defers any rebuild by `REBUILD_DEFER_MS = 120` ms inside a `try` that logs `brand menu rebuild failed (non-fatal):` (`src/main/menu.ts:85-129`). A click records its own choice in the menu state before sending E10, so the renderer's echo matches and rebuilds nothing on the click path (`772e381`).

#### 2.5.6 `region:complete` sender check and conversion (`src/main/RegionService.ts:114-150`)

| Step | Rule |
|---|---|
| 0 | `selectArea()` while a selection is pending first tears the pending one down with `null` ("cancel any in-flight selection"), then opens one overlay per display and logs debug `` `region: overlay opened across ${n} display(s)` ``. |
| 1 | No pending selection: ignore. |
| 2 | The sender's `BrowserWindow` must be one of `pending.windows`, else ignore ("ignore any other renderer firing the channel (the same preload exposes region.* to every window)"). |
| 3 | `cssRect && cssRect.width >= 4 && cssRect.height >= 4` (`MIN_DRAG`) and the sender not destroyed: `phys = screen.dipToScreenRect(sender, { x: b.x + round(x), y: b.y + round(y), width: round(w), height: round(h) })` with `b = sender.getBounds()`; log info `` `region selected: ${w}x${h} @ (${x},${y}) [physical px]` ``. The `>= 4` test is on the UNROUNDED CSS width and height. Else (no rect, or too small): log debug `region: selection cancelled` and resolve `null`. A large enough rect from a sender that is already destroyed resolves `null` with NO log line. |
| 4 | Teardown: close every overlay not yet destroyed, resolve the pending promise. An overlay closed out from under the selection (for example at app quit) also resolves `null`, so `selectArea()` never hangs. |

03 owns the math and the overlay (03 7.4.4); the sender check becomes a generation check there (INV-IPC-15).

### 2.6 Push delivery state machine (update notice)

`webContents.send` does not buffer, and the startup check "frequently" completes before the renderer subscribes (measured: main found the update 40 s before the renderer's first IPC in dev, `037858d`). Main therefore both stashes and pushes; the renderer both pulls and subscribes. States of the notice in the renderer (`src/renderer/project/App.tsx:53-72`, `src/main/main.ts:517-557`, `src/main/update-state.ts`):

| State | Event | Guard | Action | Next |
|---|---|---|---|---|
| NoNotice | renderer mounts | | subscribe to E4 FIRST, then call U1 | Waiting |
| Waiting | U1 resolves with `r` | `r?.available` | show `shotAI <version> is available.` with `Open the download page` (the link calls I2 only when `update.url` is set) | Shown |
| Waiting | U1 resolves `null`, or rejects | | none (a rejection is swallowed: "an update nudge is never worth an error") | Waiting |
| Waiting | E4 arrives with `r` | | `setUpdate(r.available ? { version, url } : null)`; `r.available` is always true from main | Shown |
| Shown | U1 or E4 arrives again | | set to an equal value (a re-render, no visible change) | Shown |
| Shown | user dismisses | | `setUpdate(null)` | Waiting |

There is no separate Dismissed state: dismissing sets the same `null` the component starts with, so a U1 reply or an E4 push that arrived after a dismiss would show the notice again. In practice neither can happen (U1 is called once on mount and main pushes at most once, at the time it stashes), which is why "Dismissing hides it for this session" holds (`src/renderer/project/App.tsx:53-55`, `:552`). Natively the notice view model keeps an explicit `Dismissed` flag for the process lifetime (EDGE-IPC-45).

Main side (`src/main/main.ts:521-557`): startup check skipped by the throttle or setting: debug `update check skipped (<reason>)`, nothing stashed, pushed or stamped. Otherwise the check runs and `lastUpdateCheckAt` is stamped "either way, so a persistent failure retries tomorrow"; a result with `error`: info `update check could not complete: <error>`; up to date: debug `update check: up to date (<version>)`; an update: info `update available: <version>`, then `setPendingUpdate(result)` BEFORE `send`; any throw: warn `startup update check failed (non-fatal):`. Manual check (U2): `setPendingUpdate(result)`, no push.

### 2.7 Hardening around the boundary

All of this exists because a renderer process displays content and was treated as possibly compromised. None of it has a native counterpart except where noted.

| # | Measure | Citation | Class and native replacement of its intent |
|---|---|---|---|
| 1 | `sandbox: true`, `contextIsolation: true`, `nodeIntegration: false` on every window | `src/main/main.ts:272-276`, `:326-330` | ELECTRON-ONLY: no web content is hosted; WPF renders native controls |
| 2 | OS sandbox kept on; `SHOTAI_NO_SANDBOX=1` opt-out for VMs that cannot start it ("the one OS-level containment for the renderer, which decodes attacker-influenceable image pixels") | `src/main/main.ts:131-140` | ELECTRON-ONLY; the image-decoding exposure it contained is a real residual risk natively, mitigated by the magic-byte check and explicit WIC decoders (Q-IPC-16, R-ARCH-21) |
| 3 | CSP `default-src 'self'; img-src 'self' shot: data: blob:; style-src 'self' 'unsafe-inline'; font-src 'self' data:; script-src 'self'; connect-src 'self'` on packaged builds | `src/main/main.ts:88-106` | ELECTRON-ONLY |
| 4 | `setWindowOpenHandler(() => ({ action: 'deny' }))` and `will-navigate` / `will-redirect` confined to `shot:`, `file:` and the dev server, logging `` `blocked navigation to ${url}` `` | `src/main/main.ts:381-401` | ELECTRON-ONLY; the one remaining web engine (WebView2 for PDF) carries the equivalent hardening (09 INV-EXP-20, INV-IPC-20) |
| 5 | `shot://<projectId>/<rel>` scheme serving project images through an opaque per-session id and `resolveProjectFile`, `.png`, `.jpg`, `.jpeg` only, 403 `Unsupported type`, 404 `Not found`, 500 `Error` | `src/main/main.ts:31-86`, `src/main/project-store.ts:430-435` | ELECTRON-ONLY; replaced by `ProjectStore.ResolveImage` (confine plus extension allowlist) and in-process loading (01 D-19) |
| 6 | The known-project gate (`resolveKnownProject`) "Blocks a compromised renderer from reading arbitrary files via openProject" | `src/main/project-store.ts:50-63` | the rationale is ELECTRON-ONLY; the gate is kept as defense in depth (01 INV-MODEL-27) |
| 7 | Argument validation in `ipc.ts` (2.3, 2.4) | `src/main/ipc.ts:82-248` | split in 7.5: type checks disappear, value-domain rules stay |
| 8 | "Verbs, not values" for auth: no channel returns a token, assertion or claim; the API key has no getter channel at all | `src/shared/ipc.ts:236-241`, `src/main/ipc.ts:786-787`, commit `b25a40e` | REQUIRED intent [SECURITY]: the UI assemblies depend only on `IAuthService` and its value types (INV-IPC-1, INV-IPC-2) |

### 2.8 Logging on the boundary

| # | Behavior | Citation | Class |
|---|---|---|---|
| 1 | Every handler registered in `src/main/ipc.ts` (72 invoke plus `claude:cancel`) logs one debug line `ipc: <channel>` through `devLog`, which is `ipcLog.debug` (category `ipc`), with no arguments. Exceptions: `view:set-detail` logs nothing; `view:set-brand-menu` logs its own line through `mainLog` (row 4); `region:complete` and `region:cancel` log no entry line, only `RegionService`'s outcome lines (2.5.6). | `src/main/ipc.ts:78-80`, every handler; `src/main/main.ts:456-458`; `src/main/RegionService.ts:32-37` | REQUIRED intent (a trace of what the UI asked for), new format 7.11 |
| 2 | `claude:set-key` logs "the channel only \u2014 never the key value". | `src/main/ipc.ts:776-777` | REQUIRED [SECURITY] |
| 3 | The auth handlers' "log lines carry channel names only". | `src/main/ipc.ts:786-787` | REQUIRED [SECURITY] |
| 4 | `view:set-brand-menu` logs its coerced inputs (added because the line's absence made a crash diagnosis "inference", `772e381`). | `src/main/main.ts:471-476` | REQUIRED intent (7.11) |
| 5 | `refused openExternal for non-allowlisted URL: <origin>` at warn. | `src/main/ipc.ts:305` | REQUIRED |

### 2.9 User-visible strings owned by the boundary

Strings thrown or shown by `ipc.ts`, `main.ts` and `RegionService.ts` themselves (delegate strings are owned by their specs and quoted in 2.4 for reference only):

| String | Where | Reachable by a user? | Class |
|---|---|---|---|
| `` `${name} must be a string` `` (`projectPath`, `stepId`, `keepId`, `dropId`, `dir`, `apiKey`) | `src/main/ipc.ts:85` | no (only a malformed renderer call) | ELECTRON-ONLY |
| `target must be an object`, `target.mode is invalid` | `:117`, `:120` | no | ELECTRON-ONLY |
| `format must be one of: html, html-plain, pdf, markdown, docx, pptx` | `:181` | no | ELECTRON-ONLY |
| `patch must be an object` | `:195` | no | ELECTRON-ONLY |
| `settings patch must be an object` | `:236` | no | ELECTRON-ONLY |
| `orderedIds must be an array of strings` | `:476` | no | ELECTRON-ONLY |
| `No image data received` | `:450` | yes (an empty file picked for Insert Image) | REQUIRED |
| `Image too large (max 60 MB)` | `:451` | yes | REQUIRED |
| `shotAI is not set up for Microsoft sign-in on this machine.` | `:821` | rarely (policy removed while Settings is open) | REQUIRED |
| `A screenshot needs an explicit target (screen, window, or area).` | `:929` | no from the shipped UI (the insert modal never offers Auto), yes from a future caller | REQUIRED (02 enforces) |
| `Choose shotAI projects folder` | `:324` | yes (dialog title) | REQUIRED (06) |
| `Import a shotAI project package`, filter name `shotAI package` | `:626-628` | yes | REQUIRED (09) |
| `Choose a folder for the exports` | `src/main/export.ts:136` | yes | REQUIRED (09) |
| `Error invoking remote method '<channel>': ` prefix | Electron runtime | yes, on every error banner | ELECTRON-ONLY (7.9) |

---

## 3. Constants

| Name | Value | Unit | Meaning | Citation |
|---|---|---|---|---|
| Channel count, all | `87` | channels | values in `IpcChannels`; rows in the native channel map (7.4) | `src/shared/ipc.ts:175-278` |
| Invoke channels | `74` | channels | `ipcMain.handle` registrations (72 in `ipc.ts`, 2 in `main.ts`) | `src/main/ipc.ts:255-980`, `src/main/main.ts:456`, `:469` |
| Fire-and-forget channels | `3` | channels | `claude:cancel`, `region:complete`, `region:cancel` | `src/main/ipc.ts:896`, `src/main/RegionService.ts:32`, `:35` |
| Push events | `10` | channels | 2.4.9 | `src/shared/ipc.ts:206-277` |
| `CAPTURE_MODES` | `auto`, `window`, `area`, `screen` | set | valid `target.mode` | `src/main/ipc.ts:90-95` |
| `CLICK_BUTTONS` | `left`, `right`, `middle`, `other` | set | valid `click.button`; anything else becomes `left` | `:139-144`, `:153-156` |
| `STEP_KINDS` | `shot`, `text` | set | valid `patch.kind` | `:186` |
| `EXPORT_FORMATS` | `html`, `html-plain`, `pdf`, `markdown`, `docx`, `pptx` (this order) | list | valid export format; the order is the error message's order | `:176-184`, `src/shared/ipc.ts:129` |
| Marker color pattern | `/^#[0-9a-fA-F]{3,8}$/` | regex | valid `patch.markerColor`; natively `^#[0-9a-fA-F]{3,8}\z` with `RegexOptions.CultureInvariant` (04 7.5) | `src/main/ipc.ts:209` |
| Report zoom clamp | `[1, 6]` | factor | `patch.reportZoom = max(1, min(6, v))`; the floor normalizes legacy sub-1 values on the next write | `:213-216` |
| Report pan clamp | `[0, 1]` | fraction | `patch.reportPanX`, `reportPanY` | `:217-218` |
| `SOP_CUSTOM_INSTRUCTIONS_MAX` | `2000` | UTF-16 code units | `customInstructions` slice | `src/shared/sop.ts:78`, `src/main/ipc.ts:244-246` |
| Import image maximum | `60 * 1024 * 1024` = `62914560` | bytes | larger imports are refused with `Image too large (max 60 MB)`; exactly 62914560 is accepted | `src/main/ipc.ts:451` |
| Append sentinel | `Number.MAX_SAFE_INTEGER` = `9007199254740991` | index | a non-finite `atIndex` for `add-text-step`, `capture:single`, `capture:screenshot` means append (the store clamps it to the step count) | `:519`, `:917`, `:934` |
| Default detail scale | `1` | factor | `view:set-detail` scale when the argument is not a number | `src/main/main.ts:457` |
| `MIN_DRAG` | `4` | CSS px (DIP) | smaller area selections are a cancel | `src/main/RegionService.ts:21` |
| `REBUILD_DEFER_MS` | `120` | ms | brand menu rebuild deferral (ELECTRON-ONLY, 03) | `src/main/menu.ts:104` |
| Capture scale coercion | min `0.5`, max `1`, default `0.85` | factor | `setCaptureScale` of a non-number or non-finite value stores `0.85` (10) | `src/shared/project.ts:18-20`, `src/main/settings.ts:31-35` |
| Archive age coercion | default `90`; `<= 0` becomes `0` (never); else `round` into `[1, 1825]` | days | `setArchiveAgeDays` (10) | `src/main/settings.ts:37-43`, `:50` |
| User name cap | `120` | UTF-16 code units | `setUserName` slice (10) | `src/main/settings.ts:45-48` |
| Theme values | `light`, `dark`, `system`; default `system` | enum | `setTheme` coercion (10) | `src/main/settings.ts:25-28` |
| External-link hosts | `anthropic.com`, any `*.anthropic.com`, exactly `github.com`; scheme `https:` only | allowlist | 2.5.1 | `src/main/ipc.ts:285-287` |
| `AppInfo.name` | `shotAI` | string | literal, not the executable name | `src/main/ipc.ts:258` |
| `DEFAULT_SUPPORT_URL` | `https://github.com/Armadillon44/shotAI/issues` | URL | `AuthStatus.supportUrl` when federation is configured without a SupportUrl; always opens because `github.com` is on the base allowlist (08 owns the constant) | `src/main/entra/config-validate.ts:79-82`, `src/main/ipc.ts:814` |
| Exit flush budget (native) | `5` | s | total time the UI thread may wait at exit for queued project and settings writes (7.10) | IMPROVEMENT; 01 7.12 |
| UI dispatch priority (native) | `DispatcherPriority.Normal` | WPF priority | the single priority every marshaled service event uses, so arrival order is kept (7.6) | IMPROVEMENT; 03 INV-SHELL-21 |

---

## 4. Invariants

**INV-IPC-1 [SECURITY]. No credential material is reachable from the UI assemblies.** `IAuthService` and the value types it returns expose no access token, identity token, assertion, refresh token, JWT claim, expiry, scope, request id or token prefix. `AuthStatus` has exactly seven members: `Mode`, `FederationAvailable`, `SignedIn`, `Account`, `EncryptionAvailable`, `HasStoredCiphertext`, `SupportUrl`. Why: #63, `b25a40e` ("Verbs, not values"; "a countdown in the UI is not worth leaking the token's lifetime shape"). Citation: `src/shared/ipc.ts:53-79`, `:236-241`, `src/main/ipc.ts:817-826`. Test: `ServiceBoundary.AuthSurfaceTests` (reflection over `IAuthService`, `AuthStatus`, `ApiKeyStatus`, `TestConnectionResult`: member names equal the listed sets; no member type is or contains `Microsoft.Identity.Client.AuthenticationResult`, `EntraToken`, `MintedToken`, or a string property whose name matches `(?i)token|assertion|claim|secret|expires`).

**INV-IPC-2 [SECURITY]. The API key is write-only from the UI.** The UI can ask whether a key exists and where from, store one, and clear it; no member reachable from `ShotAI.App` returns the key. Why: the key has had no getter channel since the first IPC surface (`b25a40e`: "getApiKey has no IPC handler at all"). Citation: `src/shared/ipc.ts:49`, `:537-542`. Test: `ServiceBoundary.AuthSurfaceTests.ApiKeyNeverReturned` (no method on any catalog interface returns `string` from a key source; `IApiKeyStore.GetApiKeyAsync` is `internal` to Core, 08 7.12).

**INV-IPC-3 [SECURITY]. External links open only through `IExternalLinks.OpenAsync`, with the 2.5.1 allowlist unchanged.** `https` only; host `anthropic.com`, a subdomain of `anthropic.com`, or exactly `github.com`; otherwise only an exact-origin match with a configured SupportUrl while federation is configured. Refusals log the origin only. No other code path passes a URL to the shell. Why: the update link and the Request access link were silently refused before the allowlist grew (`037858d`, #63); github.com must not widen to user-content hosts. Citation: `src/main/ipc.ts:268-310`. Test: `Links.ExternalLinkPolicyTests` (Linux, table in 8.2) and `Architecture.SingleUrlLauncherTests` (only `ShellUrlLauncher` references `ProcessStartInfo.UseShellExecute` with a URL).

**INV-IPC-4. Every Electron channel maps to exactly one native member, and nothing is left unmapped.** The map (7.4) has 87 rows: 74 invoke, 3 send, 10 push. Each row names one member (method, property, event, command or progress parameter) that exists. Why: the rewrite must not silently drop a capability the Electron UI had. Citation: `src/shared/ipc.ts:175-278`. Test: `ServiceBoundary.ChannelInventoryTests` (Linux: the checked-in `channel-map.json` has 87 unique channels with the 74/3/10 split, and equals the `IpcChannels` values while `src/shared/ipc.ts` exists) and `ServiceBoundary.ChannelMapResolutionTests` (Windows: every target resolves by reflection).

**INV-IPC-5. Service events keep their emission order on the UI thread.** A service raises events on the producing thread, outside its locks, in the same order Electron sent them (for a captured step: `StepLanded` before `StateChanged`). Every App subscriber marshals with `IUiDispatcher.Post`, which queues at one priority and never runs inline, so the UI observes events in raise order across all subscribers. Why: the pill's flash and the recording panel both depend on the step count arriving after the step (`src/main/CaptureController.ts:1455-1463`); 03 INV-SHELL-21. Test: `Threading.EventOrderTests` (Windows STA: a fake `ICaptureService` raises `StepLanded`, `StateChanged` from a pool thread; two subscribers record the order).

**INV-IPC-6. The UI thread never synchronously waits on a service.** No `.Result`, `.Wait()`, `GetAwaiter().GetResult()` or `Dispatcher.Invoke` in `ShotAI.App` outside the exit flush (7.10). Synchronous service members that the UI calls (`ICaptureService.GetState`, `ISettingsService.Current`, `IUpdateService.Pending`, `IAppInfo.Current`, `IProjectSession.Current`, the `IShellNavigationState` properties; the T9 list of 7.6 and ARCHITECTURE 6.2) are lock-bounded snapshots that do no IO. Why: in Electron the renderer could not block main; natively a blocked UI thread deadlocks a cross-thread `SetWindowDisplayAffinity` or a UIA call into our own window (02 7.3). Citation: 02 7.3, 03 7.5. Test: analyzer `VSTHRD002` as an error and the App banned-symbol list (7.12); `Threading.NoSyncWaitTests` (Roslyn syntax scan of `ShotAI.App` sources, allowlisting `ShutdownFlush.cs`).

**INV-IPC-7. State-bearing services support subscribe-then-read, and the UI converges on the latest state.** A subscriber subscribes first, then reads the current value; on each event it re-reads the service's current value (or compares a version) instead of trusting an event payload that may be older than what it already applied. Applies to `ICaptureService` (`StateChanged` then `GetState()`), `IUpdateService` (`UpdateAvailable` then `Pending`), `ISettingsService` (`Changed` then `Current`). Why: `webContents.send` does not buffer and the update notice was lost to a startup race (`037858d`); in-process events do not buffer either. Citation: `src/main/update-state.ts:1-11`, `src/renderer/project/App.tsx:57-72`. Test: `Threading.SubscribeThenReadTests` (a service changes state between subscribe and read, and again between read and event processing; the view model ends on the latest state).

**INV-IPC-8. Progress for an operation reaches only the view that started it, and never after that view is gone.** Progress is an `IProgress<T>` argument created by the caller's view model on the UI thread (`Progress<T>`); the view model ignores reports after it is disposed or after a newer run started (a run token). Why: Electron sent progress only to the requesting `webContents` and guarded `sender.isDestroyed()`. Citation: `src/main/ipc.ts:565-571`, `:888-891`. Test: `Threading.StaleProgressTests`.

**INV-IPC-9. Validations of external input stay; validations that only guarded an untrusted renderer are replaced by types.** The kept set is exactly the "Stays" rows of 7.5: import bytes (empty, 60 MB, magic bytes), manifest-derived paths (confinement before any read, including the OCR source), settings coercions on load and write, policy values, package contents, network responses, OCR rectangles in image pixels, and the value-domain clamps of a step patch and SOP settings. Each lives in Core at the point where the external value enters, not in a view. Why: the process boundary disappears; the files, JSON, registry, network and OCR results do not. Citation: 7.5. Test: the tests named in 7.5's last column; `Validation.ImportLimitsTests` here.

**INV-IPC-10 [SECURITY]. A path that comes from a manifest is confined before it is read.** The auto-redact scan reads the step's screenshot only through `PathConfine.ConfineNoLinks(dir, step.Screenshot, probe)` (01's static `PathConfine` with an `IPathProbe` argument, R-ARCH-17); an escaping path yields "no suggestions", never a read. Why: "a hand-edited manifest could carry a traversal screenshot path" (`src/main/ipc.ts:535-540`); project folders are shared with macOS and between users. Test: 04's editor source-load confinement test, requested here as `EditorSourceLoaderTests.TraversalScreenshotIsRefused` (section 10), named in 7.5 V14.

**INV-IPC-11. Value-domain rules are enforced once, in Core, at construction.** The report zoom `[1, 6]` and pan `[0, 1]` clamps, the marker color pattern, the click normalization and the annotation filter live in `StepPatchValidator` (04); the SOP custom instructions cap of 2000 UTF-16 code units, the user name cap of 120, the capture scale and archive age coercions and the theme and brand coercions live in 10's coercer. View models and controls may pre-limit input (for example `TextBox.MaxLength`) but never replace the Core rule. Why: the rules define what `project.json` and `settings.json` may contain, and those files are also written by the other app and by hand. Citation: `src/main/ipc.ts:209-225`, `:244-246`, `src/main/settings.ts:25-48`. Test: 04 `StepPatchValidatorTests`, 10 `SettingsCoercerTests`; `Validation.BoundaryRulesLiveInCoreTests` here (reflection: no view model in App calls `Math.Clamp` on a zoom, pan or scale property; the App's patch construction goes through `StepPatchValidator`).

**INV-IPC-12 [SECURITY]. Every auth status request re-reads managed policy.** `IAuthService.GetStatusAsync` invalidates the federation configuration cache before resolving it, so an administrator's correction applies when Settings or the SOP panel next asks, without a restart. Why: the invalidator shipped uncalled while the ADMX help text and the Intune README promised this behavior (`1ecebd0`). Citation: `src/main/ipc.ts:790-800`. Test: 08 `AuthServiceTests.StatusInvalidatesFederationCache`; `ServiceBoundary.StatusRereadsPolicyTests` here (fake policy source changes between two calls; the second status reflects it).

**INV-IPC-13 [SECURITY]. A remote-visibility change is applied to the open windows as soon as the displayed value changes, and re-applied on rollback.** Why: a toggle that bites only on the next launch "reads as the toggle does nothing" (`53045c6`); with optimistic settings the windows must always match the switch the user sees. Citation: `src/main/ipc.ts:654-665`. Test: `Settings.RemoteVisibilityApplierTests` (optimistic apply, then a failed write re-applies the old value).

**INV-IPC-14. The View, Brand menu derives from the open manifest's raw `theme`, and a choice passes `null` through untouched.** No separate "unrecognised" flag travels anywhere; `null` clears the pin and a brand id (the default one included) pins it. Why: #77 (`772e381`: folding `null` into the default brand made pinning the default impossible), #95, #107 (`38908cd`: ticking "App default" for an unreadable pin turned a real change into an apparent no-op). Citation: `src/main/main.ts:459-486`, `src/renderer/project/App.tsx:419-427`. Test: 03 `BrandMenuModelTests`; `ServiceBoundary.BrandChoicePassThroughTests` here (the menu command hands `null` and `"shotAI"` to the session as different operations).

**INV-IPC-15. An area selection result is accepted only from the selection that is pending.** A result from an overlay of an earlier or finished selection is ignored. Why: every window shared the preload, so any renderer could fire `region:complete`; natively a stale overlay (a second `SelectAreaAsync` superseding the first) must not resolve the new request. Citation: `src/main/RegionService.ts:114-120`. Test: 03 `AreaSelectionServiceTests.StaleOverlayCannotResolve` and `NewSelectionResolvesPreviousNull`.

**INV-IPC-16. The requester is hidden for an area selection and restored and activated afterwards on every path.** Success, cancel, exception and cancellation all restore it, unless it was closed meanwhile. Citation: `src/main/ipc.ts:943-960`. Test: 03 `AreaSelectionServiceTests.RestoresMainWindowOnCancelAndError` and `CancellationResolvesNull`.

**INV-IPC-17. User-visible error text is the service's exception message, verbatim, with no transport prefix; unexpected failures show one generic sentence; cancellation shows nothing.** Rules in 7.9. Why: Electron's `Error invoking remote method ...` prefix was transport noise (07 EDGE-SOP-23); a .NET message for an unexpected exception is not user text (06 D-HOME-26). Test: `Errors.UserMessageTests`.

**INV-IPC-18 [SECURITY]. Boundary log lines name the operation, never its sensitive arguments.** Never the API key, a token, an assertion, the account UPN, captions, step text, file contents or full URLs (only origins). The brand-menu state line is the one line that logs values: the open flag, the raw theme string and the app brand. Why: `src/main/ipc.ts:776-777`, `:786-787`, `src/shared/ipc.ts:69-71`. Test: `Logging.BoundaryLogTests` (a capturing logger records every line produced by a scripted session that sets a key, signs in, renames a project and opens a refused URL; none contains the key, the UPN, the title or the path of the URL).

**INV-IPC-19. `ShotAI.Core` references no Windows, WPF or WinRT assembly, and every interface that Core logic consumes lives in Core.** Why: the Linux test run (dotnet/README.md) and the fixed placement rule. Test: `Architecture.CoreReferencesTests` (Linux: the Core assembly's references exclude `PresentationCore`, `PresentationFramework`, `WindowsBase`, `System.Windows.Forms`, `Microsoft.Windows.SDK.NET`, `Microsoft.Web.WebView2.Core`, `Microsoft.Web.WebView2.Wpf`, `Microsoft.Identity.Client.Broker`, `Microsoft.Win32.Registry`, and any assembly produced from a `.winmd`; the platform compatibility analyzer `CA1416` is an error in Core).

**INV-IPC-20 [SECURITY]. The only hosted web engine is the PDF renderer, and it keeps renderer-grade hardening.** WebView2 is used only by 09's `WebView2PdfRenderer`, with script, web messages, host objects, DevTools, context menus, downloads, new windows and every navigation or sub-resource other than the one local document disabled or refused. No other view hosts WebView2 or any browser control. Why: the Electron CSP, sandbox and navigation confinement existed because a renderer displays content; the PDF host is the one place that is still true. Citation: `src/main/main.ts:88-106`, `:381-401`; 09 7.7, INV-EXP-20. Test: `Architecture.SingleWebViewTests` (Windows: only types in the `ShotAI.Platform.Export` namespace, namely `WebView2PdfRenderer` and the version probe `WebView2RuntimeInfo` of 7.3.5, reference `Microsoft.Web.WebView2.Core`; `ShotAI.App` has no reference to any `Microsoft.Web.WebView2.*` assembly; no XAML contains `WebView2`).

**INV-IPC-21. Queued persistence is never abandoned: a started write finishes, and the app drains the queues before exiting.** Cancellation of a UI operation is observed only before a store or settings job starts; at exit the UI thread waits at most 5 s in total for the project and settings queues. Why: Electron could lose a pending write chain at process exit (01 D-18); a half-applied cancel would break the atomic-write guarantee. Citation: 01 7.7, 7.12. Test: `Shutdown.ExitFlushTests`.

**INV-IPC-22. Services are singletons, view models are transient, and per-project state lives in a per-project session.** Exactly one instance of each catalog service per process; one `IProjectSession` per open project, created by `IProjectSessionFactory` and disposed on Back; no view model is shared between views, except those an owning spec registers as a singleton by name (today only 06's `CaptureModePickerViewModel`, 06 EDGE-HOME-57, and 03's `AppMenuViewModel` and `CapturePillViewModel`), which `Composition.ContainerTests` allowlists. Why: Electron main held one instance of every module and one renderer store per window; the single-instance lock (03) makes "one per process" also "one per user session". Test: `Composition.ContainerTests` (`ValidateOnBuild`, `ValidateScopes`, lifetimes per 7.10).

**INV-IPC-23. Fire-and-forget verbs never throw to the caller.** `IClaudeService.Cancel()` and the menu and overlay notifications return without exceptions whether or not anything is in flight. Why: `ipcRenderer.send` cannot report an error; the Cancel button must always work. Citation: `src/main/ipc.ts:894-899`, `src/main/RegionService.ts:32-37`. Test: 07 `ClaudeServiceTests.CancelWithNothingInFlight`; `ServiceBoundary.FireAndForgetTests` here.

**INV-IPC-24. A service event handler's exception never breaks the service or the other subscribers.** Services raise events through `EventRaiser.Raise`, which invokes each delegate of the invocation list in a `try`, logs a warning `event handler failed: <EventName>` with the exception, and continues. Why: a renderer's listener error could never reach main; natively a subscriber runs on the service's thread until it marshals. Test: `Threading.EventRaiserTests`.

**INV-IPC-25. A one-shot screenshot never reports a recording status and ends with exactly one idle state event.** `CaptureScreenshotAsync` raises no `StepLanded` and no `StateChanged` while it grabs, and exactly one `StateChanged` (idle) when it finishes, on success and on failure after target validation; subscribers treat idle-to-idle as a no-op. Why: "a recording status would unmount the report view mid-grab" (`src/main/CaptureController.ts:870-876`), while the final emit keeps every window's state in step (`:891-895`). Test: `ServiceBoundary.ScreenshotTargetTests.ScreenshotRaisesOneIdleStateAndNoStep` (fake engine seams) and 02's engine tests.

**INV-IPC-26. The update notice appears at most once per process and never shows an error.** The notice view model subscribes to `UpdateAvailable`, then reads `Pending`; whichever delivers first shows the notice, the other is a no-op; after the user dismisses it, nothing re-shows it until the next launch; a failure to read `Pending` or a faulted startup check shows nothing ("an update nudge is never worth an error"). Why: `src/renderer/project/App.tsx:53-72`, `src/main/update-state.ts:9-11` ("Whichever happens first, the notice appears exactly once"). Test: `Threading.SubscribeThenReadTests.UpdatePendingBeforeSubscribeIsShownOnce`, `UpdateEventAfterReadIsShownOnce`, and `DismissedNoticeStaysDismissed`.

---

## 5. Edge cases and hard-won fixes

**EDGE-IPC-1. A push that lands before anyone listens is lost.** Situation: the startup update check finished 40 s before the renderer's first IPC in dev; `webContents.send` does not buffer, so the notice never appeared. Required: natively the same race exists between the startup check (thread pool) and Home's view model subscribing; `IUpdateService` keeps `Pending` and the view model subscribes, then reads `Pending` (INV-IPC-7). REQUIRED. Source: `037858d`, `src/main/update-state.ts:1-25`, `src/renderer/project/App.tsx:53-72`.

**EDGE-IPC-2. Every error banner carries an IPC prefix.** Situation: an error thrown in main reaches the renderer as `Error invoking remote method '<channel>': Error: <message>`, and the UI shows the whole string. Required: native shows the bare message (7.9). ELECTRON-ONLY. Source: Electron runtime behavior, `src/renderer/project/App.tsx:92-93`, 07 EDGE-SOP-23.

**EDGE-IPC-3. The two brand fields narrow differently on purpose.** Situation: the app brand falls back to the default brand when unreadable; the project pin falls back to `null` ("App default", what the document actually renders) when it names a brand this build does not know. Required: 03's `BrandMenuModel` keeps both rules (10's `BrandPalette.CoerceBrand` for the app, `BrandPalette.PinnedBrand` for the project, R-ARCH-14); natively it reads the raw `theme` string from the open manifest instead of receiving a pushed projection. REQUIRED. Source: #95, `src/main/main.ts:459-486`.

**EDGE-IPC-4. An unreadable pin must tick nothing.** Situation: `pinnedBrand` returns `null` both for "no pin" and for "a pin this build cannot read"; binding the radio group to it ticked "App default", and clicking the already-ticked row deleted the pin and re-dated the project. Windows needed a separate `projectPinUnrecognised` flag through six files because the renderer narrows the raw value away. Required: natively the menu reads the raw value (`BrandPalette.PinIsUnrecognised(raw)`, 10, R-ARCH-14), so no flag crosses anything; nothing is ticked in that state and every item stays clickable. REQUIRED behavior, ELECTRON-ONLY plumbing. Source: #107, `38908cd`, `src/renderer/project/theme-wiring.test.ts:264-341`.

**EDGE-IPC-5. The default brand is pinnable, and `null` must not become it.** Situation: the renderer folded `null` into `DEFAULT_BRAND` before sending, and the store treated "set the default" as "clear"; with the app brand on LFI every menu entry produced the same document. Required: `null` clears, a brand id pins (default included), and the no-op check compares RAW values. REQUIRED. Source: #77, `772e381`, `src/renderer/project/App.tsx:408-427`.

**EDGE-IPC-6. A menu rebuild inside the menu's own click crashed the app.** Situation: click, IPC, write, state push, `Menu.setApplicationMenu` all within tens of milliseconds tore down the native menu under its own teardown; the process exited 0 with no error. Electron's defences: the click records its own choice so the echo rebuilds nothing, rebuilds are deferred 120 ms and coalesced, and the rebuild is wrapped so a throw cannot reject the IPC. Required: WPF menus are data-bound; the Brand items raise `PropertyChanged` only when a computed label, check or enabled state changes (03 EDGE-SHELL-12), and a failure to update the menu is logged and never surfaces as the failure of the brand change. REQUIRED intent, ELECTRON-ONLY mechanism. Source: `772e381`, `src/main/menu.ts:85-129`.

**EDGE-IPC-7. A wired-at-one-end function is invisible to typecheck and unit tests.** Situation: `invalidateFederationConfig` was exported and correct but had zero callers, so the documented "reopen Settings to re-read policy" behavior did not exist; `federation-cache-wiring.test.ts` was added to assert the call sits inside the `auth:status` handler. Required: natively the invalidation is inside `IAuthService.GetStatusAsync` itself (INV-IPC-12), and the boundary test asserts the behavior (a second status call sees a policy change), not source text. REQUIRED. Source: `1ecebd0`.

**EDGE-IPC-8. `github.com` exact, `anthropic.com` by suffix, SupportUrl by origin.** Situation: the update link needed github.com; a suffix match would admit `raw.github.com` and other user-content hosts. The Request access link needed an admin-delivered URL; it is matched by exact origin, only while federation is configured, and a malformed configured URL never widens the list. Required: INV-IPC-3 with the 8.2 test table. REQUIRED [SECURITY]. Source: `037858d`, `src/main/ipc.ts:281-303`.

**EDGE-IPC-9. A refused URL is logged by origin only; an unparseable one is not logged at all.** Required: parity: `refused openExternal for non-allowlisted URL: <origin>` at warning for a parsed, refused URL; no line for a non-string or unparseable one. Origin format: `scheme://host` plus `:port` when not the scheme's default, lower-case host, `null` for non-hierarchical schemes (WHATWG origin serialization). REQUIRED. Source: `src/main/ipc.ts:273-279`, `:304-306`.

**EDGE-IPC-10. The remote-visibility cache is set before the write, the windows after it.** Situation: `setRemoteVisible` sets the synchronous cache immediately ("the next grab must see this"), then `applyRemoteVisibility` runs only after the write resolves; if the write fails the cache already holds the new value while the windows keep the old one until the next shield release restores to the cache. Required: IMPROVEMENT: the optimistic settings change updates `RemoteVisibleNow()` at once (it reads the lock-free `Current` snapshot), and `RemoteVisibilityApplier` starts the window re-application in the same change notification, off the UI thread (ARCHITECTURE DL1, 02 7.8; 7.3.6); a rollback reverts both (INV-IPC-13). Source: `src/main/settings.ts:277-284`, `src/main/ipc.ts:654-665`.

**EDGE-IPC-11. A removed setting must not come back through a leftover key.** Situation: `settings:get-capture-no-hide` and `settings:set-capture-no-hide` were removed end to end; "A leftover `captureNoHide` key in an existing settings file is simply not parsed, and drops off disk the next time settings are written." Required: native never reads `captureNoHide` and has no behavior for it; whether the key is preserved or dropped on write is 10's unknown-key rule (Q-IPC-8). REQUIRED (no resurrection). Source: `f24b3dc`.

**EDGE-IPC-12. Progress goes only to the requesting window, and bulk exports have none.** Situation: `projects:export` streams `projects:export-progress` to its sender; `export-to-dir` and `export-to-own-folder` pass no `onProgress`, so Home's bulk run shows no per-image progress. Required: `IExportService.ExportWithSaveDialogAsync` takes `IProgress<ExportProgress>?`; the two bulk methods take none. REQUIRED. Source: `src/main/ipc.ts:558-600`.

**EDGE-IPC-13. SOP cancel is global.** Situation: `claude:cancel` aborts "the in-flight estimate or generateSop request", whichever it is, for whichever project. Required: `IClaudeService.Cancel()` keeps that meaning; view models additionally cancel their own `CancellationTokenSource` (07 7.10). REQUIRED. Source: `src/main/ipc.ts:894-899`.

**EDGE-IPC-14. Any window could fire the region channels.** Situation: the preload exposed `region.complete` and `region.cancel` to the project window and the pill as well as the overlays; `RegionService.finish` ignored senders that were not an overlay of the pending selection. Required: overlays are in-process objects bound to a selection generation (INV-IPC-15). REQUIRED intent, ELECTRON-ONLY mechanism. Source: `src/main/RegionService.ts:114-120`.

**EDGE-IPC-15. The requesting window may be gone when the selection ends.** Situation: the restore after `region:select-area` runs only when the window still exists. Required: 03's `SelectAreaAsync` restores only an open requester. REQUIRED. Source: `src/main/ipc.ts:953-957`.

**EDGE-IPC-16. A no-click screenshot needs an explicit surface.** Situation: `capture:screenshot` with no target or `auto` throws `A screenshot needs an explicit target (screen, window, or area).` because Auto classifies off a clicked window that does not exist. Required: `ICaptureService.CaptureScreenshotAsync` throws the same message (as a `ShotAIException`) for a null or Auto target; the typed `CaptureTarget` does not make Auto unrepresentable, so the check stays. REQUIRED. Source: `src/main/ipc.ts:925-930`; 02 2.2.1.

**EDGE-IPC-17. Start options travel as an object.** Situation: the preload passes `capture.start`'s `opts` verbatim so a new field cannot be dropped at the bridge; main keeps `createdThisSession === true` and `insertAt = max(0, round(v))` when finite. Required: `CaptureStartOptions` is a record (02 7.2); the engine clamps a negative `InsertAt` to 0 (the rounding is the caller's, whose indices are integers). REQUIRED. Source: `src/preload/preload.ts:196-204`, `src/main/ipc.ts:104-113`.

**EDGE-IPC-18. A non-finite index means append.** Situation: `add-text-step`, `capture:single` and `capture:screenshot` map a non-number or non-finite `atIndex` to `Number.MAX_SAFE_INTEGER`; `import-step` maps it to `null`; the store clamps either to the end. Required: native signatures take `double? atIndex` (store) or `int insertAt` (capture) and callers pass `null` or the step count for "append"; a `NaN` is unrepresentable from the UI. ELECTRON-ONLY sentinel, REQUIRED append semantics. Source: `src/main/ipc.ts:455`, `:519`, `:917`, `:934`.

**EDGE-IPC-19. A render PNG that is not binary is dropped silently.** Situation: `update-step` and `merge-steps` accept `Uint8Array` or `ArrayBuffer`; anything else becomes `null` and the patch is applied without a render write; the preload turns `undefined` into `null`. Required: `ReadOnlyMemory<byte>` with `default` meaning "no render" (01 7.8); `IsEmpty` is the test. ELECTRON-ONLY conversion, REQUIRED semantics. Source: `src/main/ipc.ts:420-425`, `src/preload/preload.ts:80`.

**EDGE-IPC-20. An empty file and a huge file are distinct refusals.** Situation: Insert Image with a 0-byte file shows `No image data received`; above 62914560 bytes shows `Image too large (max 60 MB)`; both before the magic-byte check. Required: parity messages and order, enforced in Core (`ImportLimits.Check(long length)`) and called by `IProjectService.ImportStepAsync` before the magic-byte check. IMPROVEMENT on top: the report checks `FileInfo.Length` with the same rule BEFORE reading the file into memory, so a multi-gigabyte pick is refused without allocating it. Source: `src/main/ipc.ts:444-451`.

**EDGE-IPC-21. The step patch's `callout` and `crop` keys distinguish "absent" from "clear".** Situation: `'callout' in v` with an invalid value CLEARS the callout (becomes `undefined`, and the manifest drops the key); `crop: null` writes JSON `null`; an invalid crop object also becomes `null`. Required: 04's `Optional<T>` fields carry the distinction; the editor and report construct patches through `StepPatchValidator` (INV-IPC-11). REQUIRED. Source: `src/main/ipc.ts:204-208`; 04 7.5.

**EDGE-IPC-22. The zoom floor normalizes legacy values.** Situation: `reportZoom` is clamped to `[1, 6]` at the boundary; a stored legacy value below 1 is fixed on the next write of that field. Required: the clamp stays in `StepPatchValidator` even though 05's operation clamps to its own `[1, 4]` UI range first. REQUIRED. Source: `src/main/ipc.ts:213-216`; 05 7.4 R1.

**EDGE-IPC-23. SOP settings patch drops invalid fields instead of failing.** Situation: an unknown model, tone or effort is dropped and the rest of the patch applies; `customInstructions` is sliced to 2000 UTF-16 code units (possibly splitting a surrogate pair, parity). Required: 10's coercer keeps both rules for values read from `settings.json`; the UI cannot produce an invalid enum value. REQUIRED (coercer), ELECTRON-ONLY (patch shape check). Source: `src/main/ipc.ts:229-248`.

**EDGE-IPC-24. A non-number capture scale or archive age stores the default, not "off".** Situation: `setArchiveAgeDays('x')` becomes `NaN` and stores `90`, not `0`; `setCaptureScale('x')` stores `0.85`. Required: 10's coercer keeps these rules for `settings.json`; the typed setters cannot pass a non-number. REQUIRED (coercer). Source: `src/main/ipc.ts:670-677`, `:715-722`, `src/main/settings.ts:31-43`.

**EDGE-IPC-25. An empty rename resets the title.** Situation: `projects:rename` coerces a non-string to `''`, and the store stores `title.trim() || defaultTitle()`, so an empty rename replaces the title with a new timestamped default. Home prevents it in the UI (06). Required: parity at the store (01); the native Home keeps its own guard. REQUIRED. Source: `src/main/ipc.ts:365`, `src/main/project-store.ts:528`.

**EDGE-IPC-26. `projects:open` returns an id that only exists for `shot://`.** Situation: the result carries an opaque `projectId` the renderer uses to build image URLs, never a path. Required: natively `OpenProjectAsync` returns `OpenedProject(Dir, Manifest)` and images load through `ProjectStore.ResolveImage`; no session id registry exists. ELECTRON-ONLY. Source: `src/shared/ipc.ts:340-344`, `src/main/project-store.ts:430-435`.

**EDGE-IPC-27. The redaction scan swallows a missing step but not an unknown project.** Situation: a missing step, an empty screenshot or an escaping path return `[]`; an unknown project path throws the gate message because `getProjectForRead` runs first. Required: natively the scan runs on the editor's in-memory decoded image (04 D-EDIT-11), so the step lookup and the gate happened when the editor opened; the confinement rule is kept for the source read (INV-IPC-10). REQUIRED intent. Source: `src/main/ipc.ts:525-543`.

**EDGE-IPC-28. The projects-folder picker does not clear recents.** Situation: Electron's `projects:choose-dir` only creates the folder and persists it; recents from the previous root still merge into Home. macOS clears recents on a folder switch. Required: Electron parity (Q-IPC-13). REQUIRED. Source: `src/main/ipc.ts:334-336`, `macOS:shotAI/AppModel.swift:648-665`.

**EDGE-IPC-29. The export-folder picker is parented to the focused window, the others to the sender.** Situation: `chooseExportDirectory` uses `BrowserWindow.getFocusedWindow()`; `choose-dir` and `import-package` use the sender. In practice both are the project window. Required: native dialogs are owned by the main window (the only window that can start them). REQUIRED behavior. Source: `src/main/export.ts:134`, `src/main/ipc.ts:322`, `:624`.

**EDGE-IPC-30. Sign-out without federation is a no-op, sign-in is an error.** Situation: `auth:sign-out` uses optional chaining and succeeds silently; `auth:sign-in` throws `shotAI is not set up for Microsoft sign-in on this machine.`. Required: parity (08 7.12). REQUIRED. Source: `src/main/ipc.ts:817-834`.

**EDGE-IPC-31. A manual update check refreshes the stash but does not push.** Situation: `update:check` stamps `lastUpdateCheckAt` and calls `setPendingUpdate(result)` (keeping only an available result, so an up-to-date answer clears a stale stash) but sends no `update:available`; because App never remounts, a manual find does not raise the Home notice during that session. Required: parity: `CheckNowAsync` updates `Pending` and does not raise `UpdateAvailable` (Q-IPC-7). REQUIRED. Source: `src/main/ipc.ts:754-768`.

**EDGE-IPC-32. `pause` and `resume` emit state even with no session.** Situation: both set `paused` only when a session exists, but always disarm the menu arm, log and emit `capture:state-changed`. Required: 02's `Pause()`/`Resume()` raise `StateChanged` unconditionally (idle state), parity. REQUIRED. Source: `src/main/CaptureController.ts:929-943`.

**EDGE-IPC-33. The no-click one-shot suppresses the per-step events but emits one idle state at the end.** Situation: `capture:screenshot` captures with `broadcast: false`, so neither `capture:step-added` nor the per-step `capture:state-changed` fires, and it deliberately does not emit when it arms ("a recording status would unmount the report view mid-grab"). Its `finally` then clears the session, restores the window and calls `emitState()`, so every screenshot that passed target validation ends with exactly one `capture:state-changed` carrying the idle state, on success and on failure; the caller returns the manifest. Required: 02 keeps both halves (no `StepLanded`, no recording-status `StateChanged`, one idle `StateChanged` at the end); subscribers must treat an idle-to-idle `StateChanged` as a no-op; the report adopts the returned manifest (05 P3). REQUIRED. Source: `src/main/CaptureController.ts:870-876`, `:891-895`, `:1455-1463`.

**EDGE-IPC-34. `view:set-detail` accepts `NaN` as a number.** Situation: the coercion tests `typeof scale === 'number'`, so `NaN` reaches `detailWindowWidth`. The renderer always passes its committed scale, so it never happened. Required: native `SetDetailView(bool, double)` receives `DocScale.Clamp(...)` output from 05 (a non-finite input clamps to 1, 05 INV-REP-10) and `DocScale.DetailWindowWidth` never returns NaN (05 INV-REP-35). IMPROVEMENT (defensive; unreachable either way). Source: `src/main/main.ts:456-458`.

**EDGE-IPC-35. Capture events reach every window, including the pill and overlays.** Situation: `broadcast` sends to all windows; the project window and the pill both subscribe to state and error, and the main window also to steps. Required: native subscribers are the recording panel view model (06), the pill presenter (03 `RecordingVisibilityController`) and the report (05); each marshals on its own but through the same dispatcher at one priority (INV-IPC-5). REQUIRED. Source: `src/main/CaptureController.ts:1470-1476`.

**EDGE-IPC-36. Both windows read state and subscribe in a way that can apply a stale reply.** Situation: the pill calls `capture.getState()` and registers `onStateChanged` in the same effect; the project window calls `getState()` in one effect (`src/renderer/project/App.tsx:187`) and subscribes in a later one (`:238`). In both, a push that arrives before the invoke reply is overwritten by the older reply. Required: IMPROVEMENT: subscribe first, then read, and on each event re-read `GetState()` (INV-IPC-7). Source: `src/renderer/toolbar/App.tsx:8-12`, `src/renderer/project/App.tsx:186-189`, `:237-250`.

**EDGE-IPC-37. Two unused channels.** Situation: `projects:list-recent` has no renderer caller (only the self-test calls the store function) and `capture:single` has none (legacy). Required: `ListRecentProjectsAsync` stays on `IProjectService` for the self-test (10); `capture:single` is not ported and maps to its functional successor `CaptureScreenshotAsync` (02 Q-CAP-1). ELECTRON-ONLY (`capture:single`), REQUIRED (`list-recent` for the self-test). Source: `src/main/selftest.ts:40`, `src/main/CaptureController.ts:751`.

**EDGE-IPC-38. A second `capture:start` for the same project returns the current state.** Situation: a start for another project throws `A recording is already in progress for another project`; for the same project it is idempotent. Required: parity in 02's `StartAsync`. REQUIRED. Source: `src/main/CaptureController.ts:680-686`.

**EDGE-IPC-39. Export brand is read at export time.** Situation: `brandForExport` reads `getBrand()` per export "so switching brand in Settings takes effect on the next export without a restart"; `export-theme.test.ts` asserts all three export handlers pass `brand:`. Required: `IExportService` reads `ISettingsService.Current.Brand` at call time; the project pin wins inside the engine (09). REQUIRED. Source: `src/main/ipc.ts:545-556`, `src/shared/export-theme.test.ts:214-224`.

**EDGE-IPC-40. Modal dialogs re-enter the UI.** Situation (macOS): the bulk export chooser spins a nested run loop, during which a queued second export could pass the `!exporting` guard; macOS claims the flag before showing any modal. Required: WPF `ShowDialog` and the common file dialogs also pump a nested message loop; every command that shows a modal sets its busy state first, and commands use `AsyncRelayCommand` with concurrent execution disallowed (its default). IMPROVEMENT (Electron's dialogs ran in main while the renderer stayed responsive, and its `busy` guards were set before the invoke). Source: `macOS:shotAI/AppModel.swift:1030-1039`.

**EDGE-IPC-41. After an await, the thing being edited may be gone.** Situation: Electron checked `sender.isDestroyed()` before each progress push; macOS re-checks `opened?.dir == path` after each await ("navigated away mid-write") and uses a write sequence number so a slow older write cannot clobber a newer value. Required: view models check their own disposal or session identity after every await before touching state (05 INV-REP-31); the optimistic session (01 7.10) orders writes. REQUIRED. Source: `src/main/ipc.ts:570`, `:890`, `macOS:shotAI/AppModel.swift:410-479`, `:540-551`.

**EDGE-IPC-42. The contract's own doc comments are stale in three places.** Situation: `ShotaiApi.openExternal` says "Main allows only https URLs on an anthropic.com host allowlist" (the handler also admits exactly `github.com` and a configured SupportUrl origin, 2.5.1); `settings.setUserName` says "(trimmed/capped)" and `coerceUserName` says "Trim + cap" but the code only slices to 120 (no trim); the doc comment "Validate/normalize a CaptureTarget arriving over IPC" sits above `parseStartOpts`, not above `parseCaptureTarget`. Required: follow the code, not the comments; native XML doc comments on the catalog describe the actual rule. REQUIRED (code behavior). Source: `src/shared/ipc.ts:284-288`, `:482-483`, `src/main/settings.ts:45-48`, `src/main/ipc.ts:99-115`.

**EDGE-IPC-43. A manual update check can reject after it succeeded.** Situation: `update:check` awaits `setLastUpdateCheckAt(Date.now())` after the network check; if that settings write fails, the invoke rejects (Settings shows its thrown-check message) and `setPendingUpdate(result)` is never reached, so a found update is neither returned nor stashed. Required: IMPROVEMENT (D-IPC-17): `CheckNowAsync` logs a failed stamp write at Warning and still sets `Pending` and returns the result, keeping its "never throws" contract (7.9 X7). Source: `src/main/ipc.ts:754-768`.

**EDGE-IPC-44. A one-shot screenshot holds a capture session while it grabs.** Situation: `captureScreenshot` sets `this.session` (with `single`, `createdThisSession: false`) before hiding the window and clears it in `finally`. During the grab `capture:get-state` reports `recording` for that project although no state was emitted; a second `capture:screenshot` or a `capture:single` throws `A recording is already in progress`; and a `capture:start` for the SAME project returns the current state instead of starting (the same-project idempotence of EDGE-IPC-38), while a start for another project throws `A recording is already in progress for another project`. The shipped UI cannot trigger these (the window is hidden during the grab). Required: parity in 02 unless 02 decides otherwise (Q-IPC-23); view models never read `GetState()` to decide whether a screenshot is running (they await the call). REQUIRED. Source: `src/main/CaptureController.ts:666-686`, `:806-814`, `:857-876`.

**EDGE-IPC-45. Dismissing the update notice is only the initial state again.** Situation: the dismiss button calls `setUpdate(null)`, the same value the component starts with; there is no dismissed flag, so a later pull reply or push would re-show it. It never happens only because both deliveries occur once, near startup. Required: IMPROVEMENT: an explicit dismissed flag in the native notice view model (INV-IPC-26), so a future second delivery (for example a periodic check) cannot re-raise a dismissed notice. Source: `src/renderer/project/App.tsx:56-72`, `:552`.

**EDGE-IPC-46. Error precedence is part of the observable contract.** Situation: for `projects:import-step` the checks run in this order: empty, then 60 MB (both in `ipc.ts`), then `projectPath must be a string`, then (inside the store job) the magic bytes `Unsupported file \u2014 please choose a PNG or JPEG image.`, and only then the known-project gate; for `projects:merge-steps` `cannot merge a step into itself` comes before the gate; for `projects:redact-scan` `stepId must be a string` comes before `projectPath must be a string`. Required: native `ImportStepAsync` checks `ImportLimits`, then the magic bytes, then the gate, in that order, so an unsupported file is reported as unsupported even for a project that is no longer known; the merge self-check precedes the gate (01). The string-type checks disappear (V1). REQUIRED. Source: `src/main/ipc.ts:444-456`, `:525-532`, `src/main/project-store.ts:808-810`, `:1048-1053`.

**EDGE-IPC-47. `auth:status` has no partial result.** Situation: any throw from `auth.federation()`, `auth.entra()`, `entra.signedInAccount()` or `getApiKeyStatus()` rejects the whole status; Settings loads it in one `Promise.all` with twelve other reads, so one failure leaves the whole panel at `Loading…` (06 EDGE-HOME-25); the SOP panel catches it silently and "leave[s] the affordance disabled rather than guessing", re-reading on every window focus. Required: parity of the contract (08 owns whether a failing account read degrades to `SignedIn = false`); the native Settings view model loads status independently of the other reads (06). REQUIRED. Source: `src/main/ipc.ts:788-816`, `src/renderer/project/Settings.tsx:149-165`, `src/renderer/project/SopPanel.tsx:70-91`.

---

## 6. macOS port notes

The Swift port has no IPC. Its UI calls one `@MainActor @Observable final class AppModel`, which calls the `ProjectStore` actor and the other kits directly: "All disk work happens inside the ProjectStore actor; this object just mirrors results onto the main actor" (`macOS:shotAI/AppModel.swift:13-18`).

| Topic | macOS | Lesson for Windows |
|---|---|---|
| Boundary shape | One facade of 1469 lines holding projects, export, packages, SOP, brand, document scale, tour, bulk operations, merge undo and auth state (`macOS:shotAI/AppModel.swift:16-1469`). | Do not build one `AppModel`. Split by the channel namespaces into the catalog services (7.3) and per-view view models; the facade's size is where macOS's cross-feature state leaks came from (the next rows). |
| Threading | `@MainActor` class plus an `actor` store: Swift's compiler enforces "UI state on the main thread, IO serialized off it". | The C# equivalent is a contract, not a compiler rule: the UI dispatcher plus 01's `SerialWriteQueue`, enforced by analyzers (7.12) and tests. |
| Errors | Every operation catches and assigns `error.localizedDescription` to `errorMessage`, `exportError`, `importError` or `sopError`, sometimes with a prefix: `Couldn't save the document size. `, `Couldn't change this project's theme. ` (`:476`, `:548`). Logs record the error type publicly and the message privately (`:124`). | Native shows the bare exception message (7.9); logs record the exception type always and never personal data (INV-IPC-18). |
| Stale results | A write sequence number (`docScaleWriteSeq`) so "a slow write landing after a newer one cannot yank the layout back" (`:410-479`); `guard opened?.dir == path` after awaits, "navigated away mid-write" (`:544`). | EDGE-IPC-41: re-check identity after each await; 01's session orders writes. |
| Re-entrancy | `exporting = true` is claimed BEFORE any modal, because the chooser "spins a nested (common-mode) run loop" during which a queued export could pass the guard (`:1030-1039`). | EDGE-IPC-40: WPF dialogs pump too; set busy first, disallow concurrent command execution. |
| Cancellation | One `sopTask: Task<Void, Never>`; `cancelSop()` cancels it; `resetSopState()` cancels on every navigation (`:566`, `:828`, `:835-842`). | Same design in 07 (`IClaudeService.Cancel` plus per-view tokens); navigation cancels (7.8). |
| Brand menu | `brandMenuSelection` reads the raw `opened?.manifest.theme` and returns `.unrecognised` for an unknown brand; `.unrecognised` is never offered and is ignored as an instruction (`:510-531`). | The native menu reads the raw value too; the Electron-only `projectPinUnrecognised` plumbing disappears (EDGE-IPC-4). |
| Known-project gate | `revealInFinder` refuses a path not in the listed projects (`:1151-1155`). | Keep the gate in `IShellReveal.RevealProjectAsync` (7.3). |
| API key | `setApiKey` and `clearApiKey` return an error string or nil; "The key value never returns to the caller" (`:690-700`). | INV-IPC-2. |
| Update check | Suppressed at launch while recording or exporting (`:67-73`). | Divergence: Electron does not suppress; keep Electron parity (10). |
| Projects folder | `setProjectsDir` clears recents ("A folder switch is a deliberate reset") (`:648-665`). | Divergence: Electron keeps recents; keep Electron parity (EDGE-IPC-28, Q-IPC-13). |
| Refresh coalescing | `autoRefresh` skips while a refresh is in flight or a project is open (`:97-111`). | 06 owns the refresh policy (D-HOME-1). |
| Merge undo | One-level undo held in the facade (`:1172-1177`, `:1431-1445`). | Not in Electron; not ported (05). |

What macOS did not need and Windows does: an explicit dispatcher abstraction (Swift's `@MainActor` hides it), a channel inventory (there was never a channel), and analyzers for blocking waits (Swift's `await` cannot block the main actor).

---

## 7. Native design (C#)

### 7.1 Principles

1. **Direct calls, typed arguments.** A view model calls a service interface method. No message bus, no string channel names, no serialization. The compiler checks every call (the successor of `const api: ShotaiApi`, 2.1 row 4).
2. **Interfaces at the seams that tests need.** Every service the UI calls is an interface in the catalog (7.3), faked in view-model tests. Implementations are internal where possible.
3. **Core is free-threaded; the App owns the UI thread.** Core and Platform services never capture a `SynchronizationContext` implicitly and use `ConfigureAwait(false)`. The App marshals every service event to the UI thread through `IUiDispatcher` (7.6).
4. **Validate where the external value enters.** Files, JSON, registry, network and OCR results are validated in Core at the point of entry; typed UI values are not re-validated (7.5).
5. **One write rule.** UI edits go through `IProjectSession.Apply` (optimistic, serialized, rollback with a notice) or `ISettingsService.UpdateAsync` (same rule); the editor save and every path that writes a file besides `project.json` go through `IProjectSession.ApplyDurable` inside an open project (flatten, persist, then report success), or call the store directly where no session exists (Home's pre-export flatten, 04 7.7) (01 7.10, 05 7.5, ARCHITECTURE 7.4 to 7.6). Anything that must wait for pending writes waits on `IProjectSession.WhenIdleAsync`, the one wait-for-writes primitive, or on `IProjectSettle.WhenSettledAsync(path)`, which awaits it (R-ARCH-6).
6. **Errors are exceptions with user text.** Expected failures throw a `ShotAIException` whose `Message` is the Electron string; the UI shows it bare (7.9).

### 7.2 Placement

| Project | Namespace | Types defined by this spec |
|---|---|---|
| ShotAI.Core | `ShotAI.Core.Threading` | `IUiDispatcher`, `EventRaiser`, `IAppLifetime` |
| ShotAI.Core | `ShotAI.Core.Errors` | `ShotAIException`, `UserMessage` |
| ShotAI.Core | `ShotAI.Core.Links` | `IExternalLinks`, `ExternalLinks`, `ExternalLinkPolicy`, `IUrlLauncher`, `UrlOrigin` (the contract; 10 owns the registration and any settings) |
| ShotAI.Core | `ShotAI.Core.Shell` | `IShellReveal` |
| ShotAI.Core | `ShotAI.Core.Store` | `IProjectService` (implemented by 01's `ProjectStore`), `IProjectSession`, `IProjectSessionFactory`, `ImportLimits`; `IProjectSettle` (declared by 01 7.10, requested by 07) and the session's event types `ManifestChangeKind`, `ManifestChangedEventArgs`, `PersistFailedEventArgs` live here too (ARCHITECTURE 7.4, R-ARCH-6, R-ARCH-20) |
| ShotAI.Core | `ShotAI.Core.Logging` | `ServiceLog` (`LoggerMessage` source-generated) |
| ShotAI.Core | `ShotAI.Core.Diagnostics` | `IWebView2RuntimeInfo { string? GetVersion(); }` (consumed by `AppInfoProvider`) |
| ShotAI.Core | `ShotAI.Core.Composition` | `CoreServiceCollectionExtensions.AddShotAICore` |
| ShotAI.Platform | `ShotAI.Platform.Shell` | `ShellReveal` (implements `IShellReveal`), `ShellUrlLauncher` (implements `IUrlLauncher`), `StaThread` |
| ShotAI.Platform | `ShotAI.Platform.Export` | `WebView2RuntimeInfo` (implements `IWebView2RuntimeInfo`; version probe only) |
| ShotAI.Platform | `ShotAI.Platform.Composition` | `PlatformServiceCollectionExtensions.AddShotAIPlatform` |
| ShotAI.App | `ShotAI.App` | `ViewModelBase` (ARCHITECTURE 5.1; placed in WP-A12) |
| ShotAI.App | `ShotAI.App.Threading` | `WpfUiDispatcher`, `AppLifetime`, `ShutdownFlush`, `UiDeferral` (04's helper, R-ARCH-18) |
| ShotAI.App | `ShotAI.App.Services` | `IAppInfo`, `AppInfo` (record), `AppInfoProvider`, `IFileDialogs`, `WpfFileDialogs`, `IAppStartup`, `RemoteVisibilityApplier` |
| ShotAI.App | `ShotAI.App.Composition` | `AppServiceCollectionExtensions.AddShotAIApp`, `ServiceProviderFactory` |
| tests | `ShotAI.Core.Tests.ServiceBoundary`, `.Threading`, `.Errors`, `.Links`, `.Validation`, `.Logging`, `.Architecture`; `ShotAI.App.Tests.ServiceBoundary`, `.Threading`, `.Composition`, `.Shutdown`, `.Settings`, `.Architecture` | 8.2 |

Types owned by other specs appear in the catalog by reference; their members are restated only where this spec fixes or reconciles a signature.

### 7.3 Service catalog

| Service | Project, namespace | Implemented by (spec) | Lifetime | Thread contract | Replaces (2.4 rows) |
|---|---|---|---|---|---|
| `IProjectService` | Core, `ShotAI.Core.Store` | `ProjectStore` (01) | singleton | free-threaded; writes on the store queue | P1 to P7, P9 to P11, S1 to S6, S8 to S10, E1 |
| `IProjectSession` (per project), `IProjectSessionFactory`, `IProjectSettle` | Core, `ShotAI.Core.Store` | `ProjectSession`, `ProjectSessionFactory` (01; one factory instance also serves `IProjectSettle`) | factory singleton; one session per open project, created only by `IProjectSessionFactory.Create` on the UI thread (R-ARCH-5) | `Apply`, `Current`: UI thread; events on the UI context captured at creation; `WhenIdleAsync` and `WhenSettledAsync` any thread | report paths of S1, S3 to S6, S8 to S11; E10's effect |
| `ICaptureService` | Core, `ShotAI.Core.Capture` | `CaptureEngine` (02) | singleton | free-threaded; events on engine threads | K1 to K4, K6 to K10, E5 to E7 |
| `IAreaSelectionService` | App, `ShotAI.App.Shell` | `AreaSelectionService` (03) | singleton | UI thread only | K5, K11, K12 |
| `IMainWindowLayout` | App, `ShotAI.App.Shell` | `MainWindowSizer` (03) | singleton | UI thread only | I3 |
| `IShellNavigationState` | App, `ShotAI.App.Shell` | `NavigationState` (06) | singleton | UI thread only | I4 |
| `AppMenuViewModel` (commands, events) | App, `ShotAI.App.Shell` | 03 | singleton | UI thread only | E8, E9, E10 |
| `IShellReveal` | Core, `ShotAI.Core.Shell` | `ShellReveal` (Platform, this spec) | singleton | any thread; work on a short-lived STA thread | P8 (and 09's reveals) |
| `IExternalLinks` | Core, `ShotAI.Core.Links` | `ExternalLinks` (this spec's algorithm, registered by 10) | singleton | any thread | I2 |
| `IAppInfo` | App, `ShotAI.App.Services` | `AppInfoProvider` (this spec) | singleton | any thread (immutable) | I1 |
| `ISettingsService` | Core, `ShotAI.Core.Settings` | `SettingsService` (10) | singleton | any thread; `Changed` on the writer's thread | G1 to G20, P2's persistence |
| `IUpdateService` | Core, `ShotAI.Core.Updates` | `UpdateService` (10) | singleton | any thread | U1, U2, E4 |
| `IInstallInfo` | Core, `ShotAI.Core.Install` | the Platform object `InstallInfoReader.Read` returns (12 7.10.4), registered as that instance | singleton instance | any thread (immutable) | no channel: the install scope is new (06 INV-HOME-45) |
| `IAuthService` | Core, `ShotAI.Core.Auth` | `AuthService` (08) | singleton | any thread; interactive sign-in dispatched to the UI thread internally | C1 to C7 |
| `IClaudeService` | Core, `ShotAI.Core.Sop` | `ClaudeService` (07) | singleton | any thread; progress through the caller's `IProgress<T>` | C8 to C10, E3 |
| `ISensitiveRegionScanner` | Core, `ShotAI.Core.Redaction` | `SensitiveRegionScanner` (04) | singleton | any thread | S7 |
| `IStepFlattener` | Core, `ShotAI.Core.Rendering` (owned by 04, R-ARCH-7) | `StepFlattener` (04) | singleton | any thread; rasterizes on 04's `StaRenderThread` | S1's re-bake path (`sop-prepare.ts`) |
| `IExportService` | App, `ShotAI.App.Export` | `ExportService` (09) | singleton | entry on the UI thread (dialogs); work on the pool; WebView2 on the UI thread | X1 to X7, E2 |
| `IFileDialogs` | App, `ShotAI.App.Services` | `WpfFileDialogs` (this spec) | singleton | UI thread only | P2's dialog |
| `INoticeService`, `IConfirmService` | App, `ShotAI.App.Chrome` | 06 | singleton | UI thread only | the renderer's error banners and `window.confirm` |
| `IUiDispatcher` | Core, `ShotAI.Core.Threading` | `WpfUiDispatcher` (App, this spec) | singleton | any thread | the transport itself |
| `ICaptureTargetSelection` (`CaptureTarget BuildTarget()`) | App, `ShotAI.App.Home` | `CaptureModePickerViewModel` (06, the named singleton of INV-IPC-22) | singleton (the view model instance) | UI thread only | no channel: the renderer shared the Home picker's target in-process; 05 reads it for Resume capturing (R-ARCH-26) |

ARCHITECTURE 4.4 is the consolidated catalog with the canonical names after 15.3; this table and it list the same interfaces.

#### 7.3.1 Threading primitives (this spec)

```csharp
namespace ShotAI.Core.Threading;

/// The only way Core, Platform and App code reaches the UI thread.
public interface IUiDispatcher
{
    /// True on the UI thread.
    bool CheckAccess();

    /// Queues `action` at the single UI priority. NEVER runs inline, even on the UI thread,
    /// so a sequence of Posts runs in call order after everything already queued (INV-IPC-5).
    /// After the dispatcher has shut down, the call is dropped silently.
    void Post(Action action);

    /// Runs on the UI thread and completes when it has run. Inline when CheckAccess() is true.
    /// A token canceled before the work starts cancels the task and the work never runs.
    Task InvokeAsync(Action action, CancellationToken ct = default);
    Task<T> InvokeAsync<T>(Func<T> func, CancellationToken ct = default);
    Task<T> InvokeAsync<T>(Func<Task<T>> func, CancellationToken ct = default);   // 08: MSAL interactive
    /// Required: without it, `InvokeAsync(async () => { ... })` binds to InvokeAsync<Task>(Func<Task>) and returns
    /// a Task<Task> that completes when the lambda reaches its first await, not when it finishes.
    Task InvokeAsync(Func<Task> func, CancellationToken ct = default);
}

/// App-wide shutdown signal. Stopping is canceled at the start of App.OnExit.
public interface IAppLifetime { CancellationToken Stopping { get; } }

public static class EventRaiser
{
    /// Invokes each delegate of the invocation list in order, each inside try/catch.
    /// A throwing handler is logged at Warning "event handler failed: {eventName}" with the exception;
    /// the remaining handlers still run and nothing propagates to the raiser (INV-IPC-24).
    public static void Raise<T>(EventHandler<T>? handler, object sender, T args, ILogger log, string eventName);
    public static void Raise(EventHandler? handler, object sender, ILogger log, string eventName);
}
```

`WpfUiDispatcher(Dispatcher d)` (App): `CheckAccess() => d.CheckAccess()`; `Post(a) => d.BeginInvoke(DispatcherPriority.Normal, a)`; `InvokeAsync(a, ct) => d.CheckAccess() ? RunInline(a) : d.InvokeAsync(a, DispatcherPriority.Normal, ct).Task` (the `Func<Task<T>>` and `Func<Task>` overloads unwrap with `.Unwrap()`; inline, they return the function's own task); `RunInline` returns `Task.CompletedTask` or a faulted task, never throws synchronously. Constructed once in `App.OnStartup` with `Dispatcher.CurrentDispatcher` and registered as a singleton instance.

Test doubles in `ShotAI.Core.Tests` (Linux): `ManualUiDispatcher` (a FIFO queue; `RunPending()` drains it on the calling thread and reports `CheckAccess() == true` only while draining; `Post` never runs inline) and `ThreadUiDispatcher` (one dedicated thread with a `BlockingCollection<Action>` loop, for ordering tests across threads).

#### 7.3.2 Store (01), with the members that replace channels

```csharp
namespace ShotAI.Core.Store;

/// ProjectStore's public instance surface (01 7.8). 09's "IProjectStore" is this interface (R-ARCH-4);
/// every consumer outside the store depends on it, never on the concrete ProjectStore.
/// It has no WhenIdleAsync(path): waiting for pending writes is IProjectSettle over IProjectSession (R-ARCH-6).
public interface IProjectService       // ProjectStore also implements IDisposable and IAsyncDisposable (7.10 rule 2); consumers never dispose it
{
    /// E1. Raised (EventRaiser, any thread) after AutoArchiveStaleAsync moved at least one project.
    event EventHandler? ProjectsChanged;

    Task<string> GetProjectsDirAsync();                                                   // P1
    Task SetProjectsDirAsync(string dir);                                                 // P2 (after the dialog)
    Task<string> ResolveKnownProjectAsync(string projectPath);                            // the gate (P8 via IShellReveal)
    Task<IReadOnlyList<ProjectSummary>> ListProjectsAsync(CancellationToken ct = default);        // P4
    Task<IReadOnlyList<ProjectSummary>> ListRecentProjectsAsync(CancellationToken ct = default);  // P3 (self-test only)
    Task<ProjectSummary> CreateProjectAsync(string? title);                               // P5
    Task<ProjectSummary> CreateProjectFromImportAsync(ProjectManifest manifest, IReadOnlyList<ImportFile> files); // 09
    Task<ProjectSummary> RenameProjectAsync(string projectPath, string title);            // P6
    Task DeleteProjectAsync(string projectPath);                                          // P7
    Task<ProjectSummary> ArchiveProjectAsync(string projectPath);                         // P9
    Task<ProjectSummary> UnarchiveProjectAsync(string projectPath);                       // P10
    Task<int> AutoArchiveStaleAsync(int ageDays, CancellationToken ct = default);         // startup (raises ProjectsChanged)
    Task<OpenedProject> OpenProjectAsync(string projectPath);                             // P11
    Task<OpenedProject> GetProjectForReadAsync(string projectPath);
    Task<ProjectManifest> MutateAsync(string projectPath, Func<ProjectManifest, ValueTask<MutateResult>> fn); // always bumps updatedAt unless fn returns Unchanged; open item: the first ProjectOperation that overrides BumpsUpdatedAt adds an optional bool bumpUpdatedAt = true here and in ARCHITECTURE 7.4 (01 Q-MODEL-25)
    Task<ProjectManifest> UpdateStepAsync(string projectPath, string stepId, StepPatch patch,
                                          ReadOnlyMemory<byte> flattenedPng = default);   // S1
    Task<ProjectManifest> MergeStepsAsync(string projectPath, string keepId, string dropId, StepPatch patch,
                                          ReadOnlyMemory<byte> flattenedPng = default);   // S5
    Task<ProjectManifest> DeleteStepAsync(string projectPath, string stepId);             // S3
    Task<ProjectManifest> DeleteStepsAsync(string projectPath, IReadOnlyCollection<string> stepIds); // 02 discard
    Task<ProjectManifest> ReorderStepsAsync(string projectPath, IReadOnlyList<string> orderedIds);   // S4
    Task<ProjectManifest> AddTextStepAsync(string projectPath, double atIndex, string? callout);     // S6
    Task<ProjectManifest> ImportStepAsync(string projectPath, ReadOnlyMemory<byte> bytes, double? atIndex); // S2
    Task AddStepAsync(string projectPath, ProjectStep step);                              // 02
    Task InsertStepAtAsync(string projectPath, ProjectStep step, double? atIndex);        // 02
    Task<ProjectManifest> SetProjectIntroAsync(string projectPath, SopIntro? intro);      // S8
    Task<ProjectManifest> SetProjectDisplayScaleAsync(string projectPath, double? scale); // S9
    Task<ProjectManifest> SetProjectThemeAsync(string projectPath, string? brand);        // S10: null or a known brand id (BrandPalette.IsBrandId, R-ARCH-14)
    Task FlushAsync(TimeSpan timeout);                                                    // exit (7.10)
}

public static class ImportLimits
{
    public const long MaxBytes = 60L * 1024 * 1024;                     // 62914560
    public const string EmptyMessage = "No image data received";
    public const string TooLargeMessage = "Image too large (max 60 MB)";
    /// Throws ShotAIException(EmptyMessage) when length == 0, ShotAIException(TooLargeMessage) when length > MaxBytes.
    public static void Check(long length);
}

/// 01 7.10's ProjectSession behind an interface, so view models can be tested with a fake.
/// The contract is ARCHITECTURE 7.4 (rules S1 to S10, R-ARCH-23); this block restates its members.
public interface IProjectSession : IAsyncDisposable
{
    string ProjectDir { get; }
    ProjectManifest Current { get; }                                    // UI thread
    int PendingCount { get; }
    event EventHandler<ManifestChangedEventArgs>? Changed;              // UI context
    event EventHandler<PersistFailedEventArgs>? PersistFailed;          // UI context
    Task<ProjectManifest> Apply(ProjectOperation op);                   // optimistic (UI thread)
    Task<ProjectManifest> ApplyDurable(Func<IProjectService, Task<ProjectManifest>> call);   // durable (R-ARCH-5)
    /// THE wait-for-writes primitive (R-ARCH-6): completes when every operation queued and every durable
    /// call started before this call has finished, persisted or rolled back (ARCHITECTURE 7.4 S6).
    Task WhenIdleAsync(CancellationToken ct = default);                 // 07 INV-SOP-28, 09 INV-EXP-28
}

public interface IProjectSessionFactory
{
    /// Must be called on the UI thread: captures SynchronizationContext.Current for the session's events.
    /// Registers the session so IProjectSettle can find it by path; the registration lasts until every operation
    /// and durable call the session accepted has finished, which can be after DisposeAsync (ARCHITECTURE 7.4 S9, R-ARCH-27).
    /// The only way to obtain a session; no caller writes `new ProjectSession` (R-ARCH-5).
    IProjectSession Create(OpenedProject opened);
}

/// Declared by 01 7.10 (requested by 07 7.13). Implemented by ProjectSessionFactory (the same instance).
/// Finds every registered session for Path.GetFullPath(projectPath) (OrdinalIgnoreCase, 02 D9), the open one and
/// any disposed one still draining, and awaits their WhenIdleAsync; with none registered it completes at once
/// (ARCHITECTURE 7.4 S7, R-ARCH-6, R-ARCH-27). This is the member
/// 09 requested as IProjectService.WhenIdleAsync(path); 07, 09 and every other egress caller use it.
public interface IProjectSettle { Task WhenSettledAsync(string projectPath, CancellationToken ct); }
```

`ManifestChangeKind` (`Local`, `Persisted`, `RolledBack`, `Durable`, `External`), `ManifestChangedEventArgs(Kind, AffectedStepIds)` and `PersistFailedEventArgs(Operation, Error)` are declared in `ShotAI.Core.Store`, not in 05's `ShotAI.Core.Report`, and `ProjectOperation` carries a virtual `AffectedStepIds` that returns `null` (R-ARCH-20, ARCHITECTURE 7.4). There is exactly one way to wait for a project's pending writes: `IProjectSession.WhenIdleAsync`, reached from outside the open view through `IProjectSettle.WhenSettledAsync(path)` (R-ARCH-6). Before any egress the caller runs the three steps of ARCHITECTURE 7.7: `IStepFlattener.EnsureFlattenedAsync`, then `IProjectSettle.WhenSettledAsync`, then `GetProjectForReadAsync` with every image through the render gate.

Deltas this spec asks of 01 (section 10): the interface itself; `ProjectsChanged`; `ImportStepAsync` calls `ImportLimits.Check(bytes.Length)` before the magic-byte check; `ApplyDurable` takes `IProjectService` instead of the concrete `ProjectStore` (R-ARCH-5); `IProjectSession`, the factory and `IProjectSettle` with the ARCHITECTURE 7.4 contract (R-ARCH-6, R-ARCH-23); every Core.Store exception derives from `ShotAIException`; `ProjectStore` implements `IDisposable` next to `IAsyncDisposable` (7.10 rule 2, R-ARCH-10). Corrected in WP-B2: `AddStepAsync` and `InsertStepAtAsync` return `Task<ProjectManifest>`, the manifest as written, from which 02's engine reads the landed step (02 D5).

`SetProjectThemeAsync(path, brand)`: a non-null `brand` that is not a known brand id throws `ArgumentException` (IMPROVEMENT D-IPC-9; Electron coerced it to the default brand, reachable only from a malformed renderer call). The menu only produces `null` or a known id. As built in WP-A18: the store applies 05's `SetProjectThemeOperation`, whose constructor makes this check, so the store and the session path share one rule.

#### 7.3.3 Capture (02) and shell (03)

`ICaptureService` is exactly 02 7.2. This spec adds only the subscriber rules of 7.7 and fixes that `CaptureScreenshotAsync` throws a `ShotAIException` (02's `CaptureException`) with the message `A screenshot needs an explicit target (screen, window, or area).` for a null or `Auto` target (EDGE-IPC-16), and that `StartAsync` clamps a negative `InsertAt` to 0 (EDGE-IPC-17). As built in WP-B2: `CaptureScreenshotAsync` takes a `CaptureTarget?` for that check (02 7.2), and `ICaptureService` resolves from WP-B6 on, when the last capture seam is registered (8.2).

```csharp
namespace ShotAI.App.Shell;

public interface IAreaSelectionService      // 03 7.4.4, unchanged
{ Task<Rect?> SelectAreaAsync(Window requester, CancellationToken ct = default); }

public interface IMainWindowLayout          // 03 7.4.2 MainWindowSizer
{ void SetDetailView(bool open, double scale); }     // UI thread; callers pass DocScale.Clamp output (EDGE-IPC-34)

public interface IShellNavigationState      // 03 consumer, 06 NavigationState implements
{
    bool ProjectOpen { get; }
    string? OpenProjectPath { get; }
    string? RawProjectTheme { get; }        // the manifest's raw "theme" string, unknown values included
    event EventHandler? Changed;            // UI thread
}
```

The pill and the other capture subscribers marshal with `IUiDispatcher.Post` only, never `InvokeAsync` (R-ARCH-11).

`AppMenuViewModel` (03 7.4.5) exposes `event EventHandler? OpenSettingsRequested`, `event EventHandler? ImportProjectRequested` (neither raised while a capture session exists) and `IRelayCommand<string?> ChooseBrandCommand`. `ChooseBrandCommand(brand)` reads `IShellNavigationState.OpenProjectPath` at execution; null: return; else `session.Apply(new SetProjectThemeOperation(brand))` on the open project's session, where `brand` is passed exactly as clicked (`null` for App default, `"shotAI"` for the default brand) (INV-IPC-14). It recomputes and logs the state line of 7.11 when any of `ProjectOpen`, `RawProjectTheme`, `ISettingsService.Current.Brand` changes. Corrected in WP-A18: the command does not hold a session, because the open session is the project view's own (05 INV-REP-31) and the menu is a singleton: with a project open it raises `ProjectThemeChosen` with a `ProjectThemeChoice(ProjectPath, Brand)`, the shell hands it to `ProjectDetailViewModel.SetProjectTheme(path, brand)`, and that applies `SetProjectThemeOperation(brand)` to its session while `path` is still the project open (03 7.4.5). The value passes through untouched on the way, and an id outside the catalog throws `ArgumentException` from the operation's constructor before anything is applied.

```csharp
namespace ShotAI.Core.Shell;

public interface IShellReveal
{
    /// P8: projectPath through IProjectService.ResolveKnownProjectAsync (throws the gate message), then RevealInExplorerAsync(resolved).
    Task RevealProjectAsync(string projectPath, CancellationToken ct = default);
    /// Opens Explorer on the parent folder with `path` selected (Electron shell.showItemInFolder).
    Task RevealInExplorerAsync(string path);
    /// Opens Explorer on `directory` (Electron shell.openPath). The caller checks it is a directory (09).
    Task OpenFolderAsync(string directory);
}
```

`ShellReveal` (Platform): `RevealInExplorerAsync` runs on `StaThread.RunAsync` (a new background `Thread` with `ApartmentState.STA`, joined through a `TaskCompletionSource`): `SHParseDisplayName(path, null, out pidl, 0, out _)`, then `SHOpenFolderAndSelectItems(pidl, 0, null, 0)`, then `ILFree(pidl)` in `finally`; a failing `HRESULT` throws `IOException` with the Win32 message. `OpenFolderAsync` re-checks `Directory.Exists(directory)` on the STA thread immediately before the launch and returns without launching when it fails (ShellExecute `open` on a FILE runs it, which is exactly what Electron's directory check exists to prevent: "so it can never be coaxed into opening (executing) a file", `src/main/export.ts:912-917`), then runs `Process.Start(new ProcessStartInfo(directory) { UseShellExecute = true, Verb = "open" })` on the same kind of STA thread. IMPROVEMENT (D-IPC-6): Electron ran these on its main thread; a reveal of an unreachable network path can block for seconds, and natively that thread would be the UI thread.

06's `reveal.RevealInExplorer(path)` for a project row is `RevealProjectAsync(path)` (P8, with the known-project gate) (section 10).

As built in WP-A19a: `ShellReveal` is internal and sealed and registered as `IShellReveal` only; it makes its two shell calls through an internal seam, `IShellCalls` (`OpenFolderAndSelect`, `Start`), which `ShellRevealTests` records. A failed HRESULT that carries a Win32 error throws `IOException` with that error's system message (`Marshal.GetPInvokeErrorMessage`), any other with the runtime's text for it. `StaThread` starts a background thread named `shotAI shell` per call. `RevealProjectAsync` checks its token after the gate, before the shell call.

#### 7.3.4 External links (algorithm here, registration in 10)

```csharp
namespace ShotAI.Core.Links;

public interface IExternalLinks
{
    /// I2. True when the URL was handed to the shell; false when refused. Throws only if the launcher throws
    /// (callers such as 06 wrap the call and show nothing, R-ARCH-25).
    Task<bool> OpenAsync(string url, CancellationToken ct = default);
}
public interface IUrlLauncher { Task LaunchAsync(string absoluteUri); }   // Platform: ShellUrlLauncher

public static class ExternalLinkPolicy
{
    public static bool IsBaseAllowed(Uri u) =>
        u.Scheme == Uri.UriSchemeHttps &&
        (Host(u) == "anthropic.com" || Host(u).EndsWith(".anthropic.com", StringComparison.Ordinal) || Host(u) == "github.com");
    static string Host(Uri u) => u.IdnHost.ToLowerInvariant();
}

public static class UrlOrigin
{
    /// WHATWG origin serialization: for http, https, ws, wss, ftp: scheme + "://" + lower-case IdnHost,
    /// plus ":" + port when not the scheme default; for every other scheme: "null".
    public static string Of(Uri u);
}
```

`ExternalLinks.OpenAsync(url, ct)`, exactly 2.5.1:

1. `url` is null: `false`.
2. `Uri.TryCreate(url, UriKind.Absolute, out var u)` fails: `false` (no log).
3. `ExternalLinkPolicy.IsBaseAllowed(u)`: go to 6.
4. `u.Scheme == "https"` and `await supportUrls.IsAllowedAsync(u, ct)` (08 7.13: exact origin of a configured SupportUrl, only while federation is configured, malformed configured value never widens): go to 6.
5. Log Warning `refused openExternal for non-allowlisted URL: {UrlOrigin.Of(u)}`; return `false`.
6. `await launcher.LaunchAsync(u.AbsoluteUri)`; return `true`.

`ShellUrlLauncher.LaunchAsync(uri)` runs `Process.Start(new ProcessStartInfo(uri) { UseShellExecute = true })` on `StaThread.RunAsync`. It is the only code in the solution that passes a URL to the shell (INV-IPC-3).

Known parsing differences, accepted: .NET `Uri` rejects a few inputs WHATWG accepts (`https:anthropic.com` without slashes) and both refuse or accept the same host set for every URL shotAI produces (constants, the update check's validated release URL, the policy SupportUrl). `ExternalLinkPolicyTests` pins the table in 8.2.

As built in WP-A19b: the types are as specified, and `ExternalLinks` is Core's, registered by `AddShotAICore` (Q-IPC-14). `IsBaseAllowed` refuses a relative URI. `UrlOrigin.Of` writes an IPv6 host in brackets, as WHATWG does (`IdnHost` gives the bare address), and a relative URI as `null`. `Uri.AbsoluteUri` keeps a Unicode host where WHATWG's `toString()` writes it in punycode; the allowlist compares the punycode form, and every URL shotAI opens is ASCII. `ShellUrlLauncher` refuses anything that is not an absolute `https` URI, and passes the URI to the shell as `ProcessStartInfo.FileName` with no verb and no arguments.

#### 7.3.5 App info and dialogs (this spec)

```csharp
namespace ShotAI.App.Services;

public sealed record AppInfo(string Name, string Version, string Platform, string Arch,
                             string DotNetVersion, string? WebView2Version);
public interface IAppInfo { AppInfo Current { get; } }
```

`AppInfoProvider` computes once: `Name = "shotAI"` (literal, parity); `Version` = `AssemblyInformationalVersionAttribute` of the entry assembly with any `+<commit>` suffix removed; `Platform = "win32"` (kept because support scripts and users quote it, 06 7.12); `Arch` = `RuntimeInformation.ProcessArchitecture` lower-cased (`x64`, `arm64`); `DotNetVersion = Environment.Version.ToString()`; `WebView2Version` = `IWebView2RuntimeInfo.GetVersion()`, a Platform member (`ShotAI.Platform.Export.WebView2RuntimeInfo`, next to 09's `WebView2PdfRenderer`) that calls `CoreWebView2Environment.GetAvailableBrowserVersionString()` inside a `try` and returns `null` when the runtime is missing (`WebView2RuntimeNotFoundException`) or the call fails. The App never references `Microsoft.Web.WebView2.Core` itself and has no WebView2 package reference (INV-IPC-20, INV-ARCH-5, R-ARCH-12). The probe is called lazily on first read of `Current`, not at startup. 03's About dialog shows it (`AboutText.Detail`, `not installed` when `null`; 03 7.2 and 7.4.5, AC-SHELL-24); 06's Settings About line does not display it (06 7.12). ELECTRON-ONLY: `electron`, `chrome`, `node` (D-IPC-12). As built in WP-A15: `Version` is `AppVersion.Current.Display` (10 7.6.1), which startup step 1 sets from the entry assembly's informational version cut at its `+`; `Arch` is `AppLogging.ArchName`, the startup banner's spelling; the snapshot is a `Lazy<AppInfo>` in `PublicationOnly` mode, so no read takes a lock and two first reads may both probe, the first result winning; `AppInfo` has a file of its own beside `IAppInfo` (ARCHITECTURE 2.4).

```csharp
public interface IFileDialogs                   // UI thread; owner is the main window
{
    /// Microsoft.Win32.OpenFolderDialog { Title, InitialDirectory, Multiselect = false }; null on cancel.
    string? PickFolder(Window owner, string title, string? initialDirectory);
    /// Microsoft.Win32.OpenFileDialog { Title, Filter, Multiselect = false, CheckFileExists = true }; null on cancel.
    string? PickOpenFile(Window owner, string title, string filter);
}
```

P2 natively: `SettingsViewModel.ChangeProjectsFolderAsync()`: `current = await projects.GetProjectsDirAsync()`; `dir = dialogs.PickFolder(main, "Choose shotAI projects folder", current)`; null: return; `await projects.SetProjectsDirAsync(dir)`; refresh Home (06). Recents are not cleared (EDGE-IPC-28). 09's `IExportDialogs` may be implemented on top of `IFileDialogs`.

#### 7.3.6 Settings and updates (10), the members this spec depends on

```csharp
namespace ShotAI.Core.Settings;

public interface ISettingsService      // SettingsService also implements IDisposable (7.10 rule 2)
{
    AppSettings Current { get; }                    // immutable snapshot, lock-free read, any thread
    /// The fixed write rule: `change` is applied to Current (then coerced) immediately and Changed is raised;
    /// the write is queued (serialized, atomic). On failure Current becomes the last persisted snapshot with
    /// every still-pending change re-applied in order, Changed is raised with IsRollback = true, and the task faults.
    /// The returned snapshot is the stored (coerced) value.
    Task<AppSettings> UpdateAsync(Func<AppSettings, AppSettings> change, CancellationToken ct = default);
    event EventHandler<SettingsChangedEventArgs>? Changed;          // EventRaiser, any thread
    Task FlushAsync(TimeSpan timeout);
}
public sealed record SettingsChangedEventArgs(AppSettings Previous, AppSettings Current, bool IsRollback);
```

The `AppSettings` properties used by the channel map: `Sop`, `RemoteVisible`, `CaptureScale`, `HasSeenTour`, `UserName`, `IncludeNameInReports`, `ArchiveAgeDays`, `Theme`, `Brand`, `UpdateCheckEnabled`, `LastUpdateCheckAt`, `ProjectsDir`, `Recents` (10 defines the record, the `settings.json` keys and the coercer). The same instance implements 02's `ICaptureSettings` (`CaptureScaleNow()`, `RemoteVisibleNow()` read `Current`) and 01's `IProjectStoreSettings`.

`RemoteVisibilityApplier : IAppStartup` (App): on `Start()` subscribes to `ISettingsService.Changed`; when `e.Previous.RemoteVisible != e.Current.RemoteVisible` (the optimistic change and its rollback alike), it calls 02's `CaptureShield.ApplyRemoteVisibility` through `Task.Run`, never on the UI thread and never through `IUiDispatcher.Post`, because no UI-thread code path may take `CaptureShield`'s lock (ARCHITECTURE DL1, 02 7.8; INV-IPC-13, D-IPC-8). The applies are serialized latest-wins: a change marks the applier dirty and, when no apply is running, starts one pool loop that applies `settings.Current.RemoteVisible` (re-read, T7) and repeats while a further change arrived meanwhile, so quick toggles can never finish on an older value; a failed apply is logged at Warning (a `_ =` fire-and-forget with a logged continuation, VSTHRD110). It never applies at startup (ARCHITECTURE 4.2 step 10, 03 7.4.1, does that in its fail-closed order). As built in WP-B5: `ShotAI.App.Services.RemoteVisibilityApplier`, registered as itself and forwarded as the first `IAppStartup`; a failed apply is logged at warning as `remote visibility could not be applied to the windows:` inside the pool loop, which never faults; `Dispose` unsubscribes, and a second call does nothing.

```csharp
namespace ShotAI.Core.Updates;

public sealed record UpdateCheckResult(bool Available, string? Version = null, string? Url = null, string? Error = null);
public interface IUpdateService
{
    UpdateCheckResult? Pending { get; }             // U1: only an Available result is ever held
    event EventHandler<UpdateCheckResult>? UpdateAvailable;         // E4: at most once per launch, startup check only
    Task RunStartupCheckAsync(CancellationToken ct = default);      // throttle; stash BEFORE raising; never throws
    Task<UpdateCheckResult> CheckNowAsync(CancellationToken ct = default);   // U2: ignores the throttle, stamps
                                                                    // LastUpdateCheckAt, sets Pending, never raises;
                                                                    // a failed stamp write is logged, not thrown (D-IPC-17)
}
```

#### 7.3.7 Auth (08), SOP (07), editor (04), export (09)

These are the owning specs' interfaces, used as-is: `IAuthService` (08 7.12: `GetStatusAsync`, `SignInAsync(nint ownerWindow, ct)`, `SignOutAsync`, `GetApiKeyStatusAsync`, `SetApiKeyAsync`, `ClearApiKeyAsync`, `TestConnectionAsync`, `AuthStatusChanged`); `IClaudeService` (07 7.8: `EstimateAsync`, `GenerateAsync(path, IProgress<SopProgress>?, ct)`, `Cancel()`); `ISensitiveRegionScanner` (defined by 04 itself in `ShotAI.Core.Redaction`, 04 7.8: `Task<(OcrScanStatus Status, IReadOnlyList<Rect> Rects)> ScanAsync(PremultipliedImage image, string fileName, CancellationToken ct)`, implemented by `SensitiveRegionScanner`); `IStepFlattener` (04 7.7, in `ShotAI.Core.Rendering`, owned by 04, R-ARCH-7: `EnsureFlattenedAsync` with a direct-store overload for Home and an overload taking the open project's `IProjectSession` that every caller inside a project uses (R-ARCH-5), and `FlattenStepAsync` for merges); `IExportService` (09 7.13, whose seven members are the only export entry names, R-ARCH-9). Where 07 and 08 overlapped, 08's `IAuthService` and value types are canonical and 07's `VerifyFederationAsync` and `IApiKeyStore` are internal to Core (R-ARCH-2); `IAuthService.TestConnectionAsync` is the one connection-test entry point and `IClaudeService` has no `TestConnectionAsync` (R-ARCH-3). Anthropic clients come only from 08's `IAnthropicClientFactory` (R-ARCH-15), which no view model sees. `SignInAsync`'s `ownerWindow` is `new WindowInteropHelper(Application.Current.MainWindow).Handle`, read on the UI thread by the Settings view model.

### 7.4 Channel map

Every channel, one native member each (INV-IPC-4). **Mode**: `Read` (query or snapshot, no write), `Optimistic` (UI first through `IProjectSession.Apply` or `ISettingsService.UpdateAsync`, rollback with a notice), `Durable` (awaited before the UI shows the result: `IProjectSession.ApplyDurable` or a direct store call), `Command` (a side effect with no model write), `Event`, `Progress` (an `IProgress<T>` parameter), `Local` (in-process inside the App, no service). "Also reached by" names the native paths that replace the Electron call sites of the same channel when they are expressed differently. This table is also checked in as `dotnet/tests/ShotAI.Core.Tests/ServiceBoundary/channel-map.json` (8.2).

#### 7.4.1 Invoke channels (74)

| # | Channel | Native member | Mode | Native caller | Also reached by | Class |
|---|---|---|---|---|---|---|
| I1 | `app:get-info` | `IAppInfo.Current` | Read | 06 `SettingsText.AppInfoLine` | 03 `AboutWindow` (`AboutText.Message` and `AboutText.Detail`: `Version`, `DotNetVersion`, `WebView2Version`, `Arch`; 03 7.4.5, R-ARCH-12) | REQUIRED (fields: ELECTRON-ONLY `electron`, `chrome`, `node` replaced, D-IPC-12) |
| I2 | `shell:open-external` | `IExternalLinks.OpenAsync` | Command | 06 notice link, Settings links | | REQUIRED [SECURITY] |
| I3 | `view:set-detail` | `IMainWindowLayout.SetDetailView` | Local | 05 on open, Back and each committed scale; 06 on Home | | REQUIRED |
| I4 | `view:set-brand-menu` | `IShellNavigationState.Changed` (the menu pulls `ProjectOpen`, `RawProjectTheme`, and `ISettingsService.Current.Brand`) | Local | 03 `AppMenuViewModel` | | REQUIRED behavior, ELECTRON-ONLY push (D-IPC-10) |
| P1 | `projects:get-dir` | `IProjectService.GetProjectsDirAsync` | Read | 06 Settings | | REQUIRED |
| P2 | `projects:choose-dir` | `IProjectService.SetProjectsDirAsync` | Durable | 06 Settings after `IFileDialogs.PickFolder(main, "Choose shotAI projects folder", current)` | | REQUIRED |
| P3 | `projects:list-recent` | `IProjectService.ListRecentProjectsAsync` | Read | 10 self-test only | | REQUIRED (no UI caller, EDGE-IPC-37) |
| P4 | `projects:list` | `IProjectService.ListProjectsAsync` | Read | 06 Home refresh | | REQUIRED |
| P5 | `projects:create` | `IProjectService.CreateProjectAsync` | Durable | 06 New project, New empty project | | REQUIRED |
| P6 | `projects:rename` | `IProjectService.RenameProjectAsync` | Optimistic at the Home list (06 D-HOME-7) | 06 row rename | | REQUIRED |
| P7 | `projects:delete` | `IProjectService.DeleteProjectAsync` | Optimistic at the Home list (06 D-HOME-7) | 06 row and bulk delete | | REQUIRED |
| P8 | `projects:reveal` | `IShellReveal.RevealProjectAsync` | Command | 06 row menu | | REQUIRED |
| P9 | `projects:archive` | `IProjectService.ArchiveProjectAsync` | Durable (writes the zip; 01 7.10) | 06 row and bulk | | REQUIRED |
| P10 | `projects:unarchive` | `IProjectService.UnarchiveProjectAsync` | Durable | 06 row and bulk | | REQUIRED |
| P11 | `projects:open` | `IProjectService.OpenProjectAsync` | Read (may back-fill an id, 01) | 05 `ProjectDetailViewModel.OpenAsync`; 06 capture flow; 06 row export | | REQUIRED (the `projectId`: ELECTRON-ONLY, EDGE-IPC-26) |
| S1 | `projects:update-step` | `IProjectService.UpdateStepAsync` | Durable | 04 editor save (`IProjectSession.ApplyDurable`); 04 `IStepFlattener.EnsureFlattenedAsync` (the `sop-prepare.ts` re-bake) | report field edits become `IProjectSession.Apply` of 05's `SetReportZoomOperation`, `SetReportPanOperation`, `SetCaptionOperation`, `SetBodyOperation`, `SetTextOperation`, `SetCalloutOperation`, persisted through `IProjectService.MutateAsync` | REQUIRED; IMPROVEMENT for the report paths (05 7.5) |
| S2 | `projects:import-step` | `IProjectService.ImportStepAsync` | Durable | 05 P2 (after `ImportLimits.Check(new FileInfo(path).Length)`) | | REQUIRED |
| S3 | `projects:delete-step` | `IProjectService.DeleteStepAsync` | Optimistic | | 05 `IProjectSession.Apply(DeleteStepOperation)` (R6, R11) | REQUIRED; IMPROVEMENT (optimistic) |
| S4 | `projects:reorder-steps` | `IProjectService.ReorderStepsAsync` | Optimistic | | 05 `IProjectSession.Apply(MoveStepOperation)` (R3) | REQUIRED; IMPROVEMENT (intent-based move) |
| S5 | `projects:merge-steps` | `IProjectService.MergeStepsAsync` | Durable | 05 `MergeCoordinator` through `IProjectSession.ApplyDurable` | | REQUIRED |
| S6 | `projects:add-text-step` | `IProjectService.AddTextStepAsync` | Optimistic | | 05 `IProjectSession.Apply(AddTextStepOperation)` (P1) | REQUIRED; IMPROVEMENT |
| S7 | `projects:redact-scan` | `ISensitiveRegionScanner.ScanAsync` | Read | 04 `EditorViewModel.AutoRedactAsync` | | REQUIRED; IMPROVEMENT (in-memory source, distinct Unavailable and Failed notices, 04 D-EDIT-11) |
| S8 | `projects:set-intro` | `IProjectService.SetProjectIntroAsync` | Optimistic | | 05 `IProjectSession.Apply(SetIntroOperation)` (R10) | REQUIRED; IMPROVEMENT |
| S9 | `projects:set-display-scale` | `IProjectService.SetProjectDisplayScaleAsync` | Optimistic | | 05 `IProjectSession.Apply(SetDisplayScaleOperation)` (P4) | REQUIRED; IMPROVEMENT |
| S10 | `projects:set-theme` | `IProjectService.SetProjectThemeAsync` | Optimistic | | 03 `ChooseBrandCommand` then `IProjectSession.Apply(SetProjectThemeOperation)` (05 P8) | REQUIRED; IMPROVEMENT |
| S11 | `projects:revert-sop` | `IProjectSession.Apply(RevertSopOperation)` | Optimistic | 07 `SopPanelViewModel.RevertCommand` | | REQUIRED; IMPROVEMENT (07 7.9) |
| X1 | `projects:export` | `IExportService.ExportWithSaveDialogAsync` | Command | 05 export button; 06 row export | | REQUIRED |
| X2 | `projects:export-to-dir` | `IExportService.ExportToDirectoryAsync` | Command | 06 bulk to one folder | | REQUIRED |
| X3 | `projects:export-to-own-folder` | `IExportService.ExportToOwnFolderAsync` | Command | 06 bulk to each project | | REQUIRED |
| X4 | `projects:choose-export-dir` | `IExportService.ChooseExportDirectoryAsync` | Local | 06 bulk | | REQUIRED |
| X5 | `projects:reveal-export-dir` | `IExportService.RevealExportDirectoryAsync` | Command | 06 bulk end | | REQUIRED |
| X6 | `projects:export-package` | `IExportService.ExportPackageAsync` | Command | 05 package export | | REQUIRED |
| X7 | `projects:import-package` | `IExportService.ImportPackageAsync` | Durable | 06 Import and File, Import Project… | | REQUIRED |
| G1 | `settings:get-sop` | `ISettingsService.Current.Sop` | Read | 06 Settings, 05 SOP panel visibility, 07 | | REQUIRED |
| G2 | `settings:set-sop` | `ISettingsService.UpdateAsync(s => s with { Sop = ... })` | Optimistic | 06 Settings AI group | | REQUIRED |
| G3 | `settings:get-remote-visible` | `ISettingsService.Current.RemoteVisible` | Read | 06 | | REQUIRED |
| G4 | `settings:set-remote-visible` | `ISettingsService.UpdateAsync(s => s with { RemoteVisible = v })` | Optimistic (plus `RemoteVisibilityApplier`) | 06 | | REQUIRED [SECURITY]; IMPROVEMENT (D-IPC-8) |
| G5 | `settings:get-capture-scale` | `ISettingsService.Current.CaptureScale` | Read | 06 | | REQUIRED |
| G6 | `settings:set-capture-scale` | `ISettingsService.UpdateAsync(s => s with { CaptureScale = v })` | Optimistic | 06 | | REQUIRED |
| G7 | `settings:get-has-seen-tour` | `ISettingsService.Current.HasSeenTour` | Read | 06 shell | | REQUIRED |
| G8 | `settings:set-has-seen-tour` | `ISettingsService.UpdateAsync(s => s with { HasSeenTour = v })` | Optimistic | 06 tour close | | REQUIRED |
| G9 | `settings:get-user-name` | `ISettingsService.Current.UserName` | Read | 06 | | REQUIRED |
| G10 | `settings:set-user-name` | `ISettingsService.UpdateAsync(s => s with { UserName = v })` | Optimistic | 06 | | REQUIRED |
| G11 | `settings:get-include-name` | `ISettingsService.Current.IncludeNameInReports` | Read | 06 | | REQUIRED |
| G12 | `settings:set-include-name` | `ISettingsService.UpdateAsync(s => s with { IncludeNameInReports = v })` | Optimistic | 06 | | REQUIRED |
| G13 | `settings:get-archive-age` | `ISettingsService.Current.ArchiveAgeDays` | Read | 06; 03 startup auto-archive | | REQUIRED |
| G14 | `settings:set-archive-age` | `ISettingsService.UpdateAsync(s => s with { ArchiveAgeDays = v })` | Optimistic | 06 | | REQUIRED |
| G15 | `settings:get-theme` | `ISettingsService.Current.Theme` | Read | 06 `ThemeManager` | | REQUIRED |
| G16 | `settings:set-theme` | `ISettingsService.UpdateAsync(s => s with { Theme = v })` | Optimistic | 06 | | REQUIRED |
| G17 | `settings:get-brand` | `ISettingsService.Current.Brand` | Read | 06 `ThemeManager`, 03 menu, 09 export fallback | | REQUIRED |
| G18 | `settings:set-brand` | `ISettingsService.UpdateAsync(s => s with { Brand = v })` | Optimistic | 06 | | REQUIRED |
| G19 | `settings:get-update-check` | `ISettingsService.Current.UpdateCheckEnabled` | Read | 06; 10 startup check | | REQUIRED |
| G20 | `settings:set-update-check` | `ISettingsService.UpdateAsync(s => s with { UpdateCheckEnabled = v })` | Optimistic | 06 | | REQUIRED |
| U1 | `update:pending` | `IUpdateService.Pending` | Read | 06 Home shell (after subscribing to `UpdateAvailable`) | | REQUIRED |
| U2 | `update:check` | `IUpdateService.CheckNowAsync` | Command | 06 Check now | | REQUIRED |
| C1 | `claude:key-status` | `IAuthService.GetApiKeyStatusAsync` | Read | 06 Settings; 07 SOP panel | | REQUIRED |
| C2 | `claude:set-key` | `IAuthService.SetApiKeyAsync` | Durable | 06 Settings | | REQUIRED [SECURITY] |
| C3 | `claude:clear-key` | `IAuthService.ClearApiKeyAsync` | Durable | 06 Settings | | REQUIRED |
| C4 | `auth:status` | `IAuthService.GetStatusAsync` | Read (invalidates the policy cache) | 06 Settings; 07 SOP panel | | REQUIRED [SECURITY] |
| C5 | `auth:sign-in` | `IAuthService.SignInAsync` | Command | 06 Settings; 07 sign-in from error | | REQUIRED |
| C6 | `auth:sign-out` | `IAuthService.SignOutAsync` | Command | 06 Settings | | REQUIRED |
| C7 | `claude:test-connection` | `IAuthService.TestConnectionAsync` | Command | 06 Settings | | REQUIRED (R-ARCH-3) |
| C8 | `claude:estimate` | `IClaudeService.EstimateAsync` | Read | 07 Preparing | | REQUIRED |
| C9 | `claude:generate-sop` | `IClaudeService.GenerateAsync` (then 07's `ApplySopPlanOperation` through `IProjectSession.Apply`) | Command, then Optimistic | 07 Send | | REQUIRED; IMPROVEMENT (07 7.9) |
| K1 | `capture:start` | `ICaptureService.StartAsync` | Command | 06 New project and Resume capturing; 05 + Capture | | REQUIRED |
| K2 | `capture:single` | `ICaptureService.CaptureScreenshotAsync` (functional successor; the click-armed path is not ported) | Durable | none | | ELECTRON-ONLY (02 Q-CAP-1, D-IPC-11) |
| K3 | `capture:screenshot` | `ICaptureService.CaptureScreenshotAsync` | Durable | 05 P3 through `IProjectSession.ApplyDurable` | | REQUIRED |
| K4 | `capture:list-targets` | `ICaptureService.ListTargetsAsync` | Read | 06 target chooser; 05 capture insert modal | | REQUIRED |
| K5 | `region:select-area` | `IAreaSelectionService.SelectAreaAsync` | Local | 06 target chooser; 05 insert modal | | REQUIRED |
| K6 | `capture:pause` | `ICaptureService.Pause` | Command | 03 pill; 06 recording panel | | REQUIRED |
| K7 | `capture:resume` | `ICaptureService.Resume` | Command | 03 pill; 06 recording panel | | REQUIRED |
| K8 | `capture:stop` | `ICaptureService.StopAsync` | Command | 03 pill; 06 recording panel | | REQUIRED |
| K9 | `capture:discard` | `ICaptureService.DiscardAsync` | Command | 03 pill after `DiscardConfirmWindow` | | REQUIRED |
| K10 | `capture:get-state` | `ICaptureService.GetState` | Read | 03 pill; 06 shell and recording panel; 03 menu guard | | REQUIRED |

#### 7.4.2 Fire-and-forget channels (3)

| # | Channel | Native member | Mode | Native caller | Class |
|---|---|---|---|---|---|
| C10 | `claude:cancel` | `IClaudeService.Cancel` | Command (never throws) | 07 Cancel buttons; project view close | REQUIRED |
| K11 | `region:complete` | `AreaSelectionService.Finish(generation, rect)` (03, internal) | Local | 03 `AreaOverlayWindow.MouseUp` | REQUIRED behavior, ELECTRON-ONLY transport |
| K12 | `region:cancel` | `AreaSelectionService.Finish(generation, null)` (03, internal) | Local | 03 overlay Escape, non-left button, click without drag, `Closed` | REQUIRED behavior, ELECTRON-ONLY transport |

#### 7.4.3 Push events (10)

| # | Channel | Native member | Raised on | Subscriber (native) | Class |
|---|---|---|---|---|---|
| E1 | `projects:changed` | `IProjectService.ProjectsChanged` | the thread that ran `AutoArchiveStaleAsync` | 06 `HomeViewModel` (marshals, then refreshes) | REQUIRED |
| E2 | `projects:export-progress` | the `IProgress<ExportProgress>? progress` parameter of `IExportService.ExportWithSaveDialogAsync` | the pool (reported), delivered on the UI thread by `Progress<T>` | 05 `ExportViewModel` | REQUIRED |
| E3 | `claude:sop-progress` | the `IProgress<SopProgress>? progress` parameter of `IClaudeService.GenerateAsync` | as E2 | 07 `SopPanelViewModel` | REQUIRED |
| E4 | `update:available` | `IUpdateService.UpdateAvailable` | the startup check's thread | 06 `NoticeCenter` (after reading `Pending`) | REQUIRED |
| E5 | `capture:state-changed` | `ICaptureService.StateChanged` | engine threads | 03 `RecordingVisibilityController` (pill), 06 `RecordingPanelViewModel`, 06 shell | REQUIRED |
| E6 | `capture:step-added` | `ICaptureService.StepLanded` | the capture worker | 06 `RecordingPanelViewModel`; 05 report adoption after stop | REQUIRED |
| E7 | `capture:error` | `ICaptureService.CaptureFailed` | the capture worker | 03 pill presenter; 06 banner `Capture error: <message>` | REQUIRED |
| E8 | `menu:open-settings` | `AppMenuViewModel.OpenSettingsRequested` | UI thread | 06 `ShellViewModel` | REQUIRED |
| E9 | `menu:import-project` | `AppMenuViewModel.ImportProjectRequested` | UI thread | 06 `ShellViewModel` (then `IExportService.ImportPackageAsync`) | REQUIRED |
| E10 | `menu:set-project-theme` | `AppMenuViewModel.ChooseBrandCommand` | UI thread | the command raises `ProjectThemeChosen`, and the project view applies `SetProjectThemeOperation` to the open session in the same call (corrected in WP-A18: the menu holds no session, 7.3.3) | REQUIRED; ELECTRON-ONLY round trip |

### 7.5 Validation disposition

Every check the Electron boundary performs, with its native fate. **Stays** checks guard external input or define a stored value's domain; **Goes** checks existed only because types were erased and the renderer was untrusted.

| # | Electron check | Input origin | Native fate | Where it lives natively | Test |
|---|---|---|---|---|---|
| V1 | `asString` on every path, id and dir argument (`src/main/ipc.ts:82-88`) | renderer | Goes: parameters are `string` (non-nullable reference types, `Nullable` enabled, warnings as errors) | compiler | none needed |
| V2 | the known-project gate on every project path (`resolveKnownProject`) | renderer (then) | Stays as defense in depth (01 INV-MODEL-27): paths also come from recents in `settings.json` and from the self-test | 01 `ProjectStore` | 01 `KnownProjectGateTests` |
| V3 | `parseStartOpts` (`:104-113`) | renderer | Goes (typed `CaptureStartOptions`), except `InsertAt < 0` clamps to 0 in the engine | 02 `CaptureEngine.StartAsync` | 02 `StartOptionsTests` |
| V4 | `parseCaptureTarget` mode set and field dropping (`:115-137`) | renderer | Goes (typed `CaptureTarget` with a `CaptureMode` enum); a window target that no longer resolves is 02's runtime error | 02 | 02 |
| V5 | `capture:screenshot` explicit-target check (`:925-930`) | caller | Stays (Auto is representable) | 02 `CaptureScreenshotAsync` | `ServiceBoundary.ScreenshotTargetTests` (fake engine contract) and 02 |
| V6 | `parseStepPatch` and `parseClick` shape checks (`:139-227`) | renderer | Goes for shapes; Stays for value domains (V7) | 04 `StepPatchValidator` | 04 `StepPatchValidatorTests` |
| V7 | zoom `[1, 6]`, pan `[0, 1]`, marker color pattern, click button default, positive `radius` and `imageScale`, annotation filter (`:153-225`) | the loaded manifest (external file) round-tripped through the editor and report | Stays | 04 `StepPatchValidator.ForEditorSave` and the operation constructors (05) | 04 `StepPatchValidatorTests`; `Validation.BoundaryRulesLiveInCoreTests` |
| V8 | `png` binary conversion to `Buffer` or `null` (`:420-425`) | renderer | Goes (`ReadOnlyMemory<byte>`, empty = none) | 01 | none needed |
| V9 | import empty and 60 MB checks (`:444-451`) | a user-picked file | Stays, plus the pre-read length check (EDGE-IPC-20) | `ImportLimits.Check` in `IProjectService.ImportStepAsync` and in 05's file pick | `Validation.ImportLimitsTests` |
| V10 | import magic bytes PNG `89 50 4E 47`, JPEG `FF D8 FF` (store) | a user-picked file | Stays | 01 `ImportStepAsync` | 01 |
| V11 | `orderedIds` array of strings (`:475-477`) | renderer | Goes (`IReadOnlyList<string>`; the report uses `MoveStepOperation`) | compiler | none needed |
| V12 | `atIndex` finite check and the append sentinel (`:455`, `:519`, `:917`, `:934`) | renderer | Goes (typed `double?` or `int`); the store's clamp to `[0, count]` Stays (01) | 01 `JsMath.ClampIndex` | 01 |
| V13 | `isCalloutKind` for `add-text-step` (`:516`) | renderer | Goes at the boundary; Stays in `CalloutKinds` for values read from manifests (01) | 01, 05 operation constructors | 01 |
| V14 | redact-scan confinement of `step.screenshot` (`:535-540`) | the manifest (external file) | Stays [SECURITY] | 04 editor source load through `PathConfine.ConfineNoLinks(dir, path, probe)` (R-ARCH-17) | 04 `EditorSourceLoaderTests.TraversalScreenshotIsRefused` (requested) |
| V15 | OCR output rectangles in image pixels | Windows OCR (external engine) | Stays: clamped to the image and filtered by 04's detector before becoming blur regions | 04 `SensitiveTextDetector` | 04 |
| V16 | `parseExportFormat` (`:176-184`) | renderer | Goes (`ExportFormat` enum) | compiler | none needed |
| V17 | `revealExportDir` directory check (`src/main/export.ts:918-925`) | a path from a dialog, possibly deleted since | Stays | 09 `RevealExportDirectoryAsync` | 09 |
| V18 | package validation (size, entry whitelist, marker, format version, magic bytes) | a user-picked zip (untrusted) | Stays [SECURITY] | 09 `PackageImporter` | 09 |
| V19 | `parseSopPatch` shape and enum filtering (`:229-248`) | renderer | Goes at the boundary; enum and length rules Stay in 10's coercer for `settings.json` | 10 `SettingsCoercer` (07 `SopSettingsCoercer`) | 10, 07 |
| V20 | `customInstructions` slice to 2000 (`:244-246`) | renderer and `settings.json` | Stays (coercer) and `TextBox.MaxLength = 2000` (06) | 10, 06 | 10 |
| V21 | `=== true` boolean coercions for settings, `includeOriginals`, `projectOpen`, `projectPinUnrecognised` | renderer | Goes (`bool`) | compiler | none needed |
| V22 | capture scale, archive age, user name, theme, brand coercions (`src/main/settings.ts:25-48`, `:413-419`) | renderer and `settings.json` | Stays (coercer, for the file) | 10 | 10 `SettingsCoercerTests` |
| V23 | `setProjectTheme` brand coercion to the default (store) | renderer and manifests | Changes: the setter rejects an unknown non-null id with `ArgumentException` (D-IPC-9); reading an unknown `theme` from a manifest keeps it raw (01, 10 `BrandPalette`, R-ARCH-14) | 01, 05 operation | `ServiceBoundary.BrandChoicePassThroughTests` |
| V24 | `setProjectIntro` coercion (`coerceIntro`) | renderer and manifests | Stays for manifests (01 codec); the operation constructor trims (05 R10) | 01, 05 | 01, 05 |
| V25 | `setDisplayScale` clamp (`clampScale`) | renderer and manifests | Stays (`DocScale.Clamp` in the operation and the store) | 01, 05 | 05 `DocScaleTests` |
| V26 | `openExternal` allowlist (2.5.1) | URLs from constants, the update check (GitHub API response) and policy | Stays [SECURITY] | `ExternalLinks` | `Links.ExternalLinkPolicyTests` |
| V27 | `claude:set-key` string check (`:778`) | renderer | Goes; the trim and empty check Stay (`API key is empty.`) | 08 `ApiKeyStore.SetAsync` | 08 |
| V28 | `auth:sign-in` "not configured" check (`:820-822`) | policy (registry, baked values) | Stays | 08 `AuthService.SignInAsync` | 08 |
| V29 | `region:complete` sender check (`src/main/RegionService.ts:119-120`) | any renderer | Changes: generation check | 03 `AreaSelectionService.Finish` | 03 |
| V30 | `region:complete` `parseRect` and `MIN_DRAG` (`:33`, `:122`) | renderer | Goes for the shape; `MIN_DRAG` Stays (a UI rule) | 03 `AreaSelectionMath.IsSelection` | 03 |
| V31 | `view:set-brand-menu` `pinnedBrand` and `coerceBrand` (`src/main/main.ts:477-485`) | renderer | Changes: the menu model applies the same narrowing to the raw manifest value and the app setting | 03 `BrandMenuModel` | 03 `BrandMenuModelTests` |
| V32 | `view:set-detail` `open === true`, numeric scale (`src/main/main.ts:456-458`) | renderer | Goes; callers pass `DocScale.Clamp` output (EDGE-IPC-34) | 05 `DocScale`, 03 `WindowLayout` | 05 `DocScaleTests`, 03 `WindowLayoutTests` |
| V33 | `sender.isDestroyed()` before progress pushes (`src/main/ipc.ts:570`, `:890`) | window lifetime | Changes: view models ignore progress after disposal or a newer run (INV-IPC-8) | 05, 07 view models | `Threading.StaleProgressTests` |
| V34 | the `shot://` extension allowlist and resolver (`src/main/main.ts:54-86`) | renderer | Changes: `ProjectStore.ResolveImage` (confine plus `.png`, `.jpg`, `.jpeg`) before any in-process image load | 01 | 01 |
| V35 | CSP, sandbox, navigation confinement, `window.open` denial (2.7) | web content | Goes for WPF; Stays for the one WebView2 host (INV-IPC-20) | 09 `WebView2PdfRenderer` | 09, `Architecture.SingleWebViewTests` |

### 7.6 Threading and UI marshaling rules

| # | Rule | Enforcement |
|---|---|---|
| T1 | The WPF UI thread (STA, one `Dispatcher`) owns every window, view model, `ObservableObject`, `ProjectSession.Current`, and every `INoticeService` and `IConfirmService` call. | view models are created on the UI thread by DI resolution from UI code; `Threading.ViewModelAffinityTests` |
| T2 | Core and Platform service methods are free-threaded: callable from any thread, internally synchronized, and they never touch UI objects. | code review; `Architecture.CoreReferencesTests` (no WPF reference is possible in Core) |
| T3 | Core and Platform `await`s use `ConfigureAwait(false)`, except the three UI-affine types that must resume on the UI thread: 08's `MsalGateway.AcquireInteractiveAsync`, 09's `WebView2PdfRenderer`, and 01's `ProjectSession` event raising (which posts to its captured context). | `CA2007` as error in Core and Platform, suppressed per file with a justification comment for `MsalGateway.cs` and `WebView2PdfRenderer.cs`. `ProjectSession` posts explicitly and awaits with `ConfigureAwait(false)`, so it needs none (corrected in WP-A9) |
| T4 | App code (view models, windows) awaits WITHOUT `ConfigureAwait(false)`, so continuations return to the UI thread through the `DispatcherSynchronizationContext`. | `CA2007` disabled in App |
| T5 | A service raises its events on the thread that caused them, outside its locks, through `EventRaiser.Raise` (INV-IPC-24). It never raises on a captured context unless its constructor takes one explicitly (01 `ProjectSession`). | code review; `Threading.EventRaiserTests` |
| T6 | Every App subscriber to a service event marshals with `IUiDispatcher.Post` and does all work inside the posted action; it checks its own disposal first. No subscriber uses `Dispatcher.Invoke`, `Dispatcher.BeginInvoke` or `Dispatcher.InvokeAsync` directly, or a priority other than `Normal`. | BannedApiAnalyzers in App (7.12) bans `Dispatcher.Invoke*`, `Dispatcher.BeginInvoke` and `Dispatcher.InvokeAsync` outside `WpfUiDispatcher.cs` and the two non-service allowlisted files of 7.12 (`StaRenderThread.cs`, `UiDeferral.cs`); `Threading.EventOrderTests` |
| T7 | Subscribe-then-read, and re-read on each event (INV-IPC-7): the handler posts "apply `service.Current` or `service.GetState()`", not "apply the event payload", for state snapshots. Payload-carrying events that are not snapshots (`StepLanded`, `CaptureFailed`, `UpdateAvailable`) are applied from the payload. | `Threading.SubscribeThenReadTests` |
| T8 | Progress is an `IProgress<T>` created by the calling view model on the UI thread (`new Progress<T>(handler)`), so reports post to the UI context; the handler checks a run token (INV-IPC-8). | `Threading.StaleProgressTests` |
| T9 | The UI thread never blocks on a service (INV-IPC-6). Synchronous members callable from the UI thread are the snapshot readers only: `ICaptureService.GetState`, `ISettingsService.Current`, `IUpdateService.Pending`, `IAppInfo.Current`, `IProjectSession.Current`, `IShellNavigationState` properties. `ICaptureService.Pause` and `Resume` are called through `Task.Run` (03 7.4.3) because they may take the engine lock while a job runs. | VSTHRD002 and BannedApiAnalyzers (`Task.Wait`, `Task.Result`, `GetAwaiter().GetResult()`) as errors in App, one allowlisted file (`ShutdownFlush.cs`) |
| T10 | CPU-bound work started from a view model runs through the service (which uses the pool) or `Task.Run`; a view model never decodes, encodes, zips or hashes on the UI thread. | code review; 05 7.11 and 09 7.14 name the pool work |
| T11 | Commands are `AsyncRelayCommand` (CommunityToolkit.Mvvm) with concurrent execution disallowed (the default), so a second click while running is ignored; a command that shows a modal sets its busy state before the modal (EDGE-IPC-40). | `Composition.CommandConventionsTests` (reflection: every `IAsyncRelayCommand` on a view model has `AllowConcurrentExecutions == false` unless allowlisted) |
| T12 | `async void` exists only for WPF event handlers, which wrap their body in `try` and route failures to `INoticeService` or the log. | VSTHRD100 as error; per-site suppression with justification |

Threads that exist at run time and their owners:

| Thread | Owner spec | Talks to the UI by |
|---|---|---|
| WPF UI thread | 03 | itself |
| store queue consumer (a pool worker running `SerialWriteQueue`'s loop) | 01 | the caller's awaited task; `ProjectSession` posts `Changed` and `PersistFailed` to its captured context |
| settings queue consumer | 10 | `ISettingsService.Changed` (marshaled by subscribers) |
| input hook thread, capture dispatcher, capture worker, poll task, two UIA MTA threads | 02 | `ICaptureService` events (marshaled by subscribers) |
| `StaRenderThread` | 04 | awaited tasks |
| short-lived STA threads for shell reveals and URL launches | this spec (`StaThread`) | awaited tasks |
| thread pool | all | awaited tasks, `IProgress<T>` |
| WebView2 browser processes | 09 | WebView2 events on the UI thread |

### 7.7 Events and progress

| Electron push | Native | Delivery rule |
|---|---|---|
| broadcast to all windows (E5 to E7) | `ICaptureService` events | every subscriber marshals on its own (T6); one priority keeps a global FIFO (INV-IPC-5); `StepLanded` then `StateChanged` for each landed step, as 02 raises them |
| to the requesting window (E2, E3) | `IProgress<T>` parameter | only the caller receives it; `null` means no progress (the bulk exports, EDGE-IPC-12) |
| to the project window (E1, E4) | `IProjectService.ProjectsChanged`, `IUpdateService.UpdateAvailable` | subscribers marshal; `UpdateAvailable` is paired with `Pending` (INV-IPC-7) |
| menu to the project window (E8 to E10) | `AppMenuViewModel` events and command | already on the UI thread; no marshaling |
| unsubscribe function returned by `on*` | `-=` in the subscriber's `Dispose` | every subscriber that subscribes to a singleton unsubscribes in `Dispose` (a singleton would otherwise keep a closed view alive); `Composition.SubscriberDisposalTests` |

Subscriber template (the only shape used for a service event):

```csharp
public sealed partial class RecordingPanelViewModel : ObservableObject, IDisposable
{
    private readonly ICaptureService _capture; private readonly IUiDispatcher _ui; private bool _disposed;

    public RecordingPanelViewModel(ICaptureService capture, IUiDispatcher ui)
    {
        _capture = capture; _ui = ui;
        _capture.StateChanged += OnStateChanged;      // 1. subscribe
        Apply(_capture.GetState());                    // 2. then read (UI thread)
    }

    private void OnStateChanged(object? sender, CaptureState _) =>
        _ui.Post(() => { if (!_disposed) Apply(_capture.GetState()); });   // 3. marshal, re-read

    public void Dispose() { _disposed = true; _capture.StateChanged -= OnStateChanged; }
}
```

### 7.8 Cancellation

| Rule | Detail |
|---|---|
| K1 | Every async service member that performs IO or waits on the network, a dialog, OCR or the user takes a `CancellationToken ct` as its last parameter, `= default` unless the owning spec makes it required (08's `IAuthService` members take a required `ct`; callers always pass the view's token anyway) (except the store's mutation methods, which follow 01: a queued job observes cancellation only before it starts, and most take no token because they are short and must not half-apply). |
| K2 | View models own the tokens: one `CancellationTokenSource` per operation, linked to the view's lifetime token (canceled on Back, window close) and to `IAppLifetime.Stopping` (canceled at the start of `App.OnExit`). |
| K3 | A user cancel surfaces as `OperationCanceledException`, which view models catch `when (token.IsCancellationRequested)` and ignore; `UserMessage.From` returns `null` for it (7.9). |
| K4 | Electron's global cancel verb survives as `IClaudeService.Cancel()` (C10); view models call it AND cancel their own token (07 7.10). |
| K5 | `IAreaSelectionService.SelectAreaAsync(ct)`: cancellation finishes the selection with `null` on the UI thread and restores the requester (03). |
| K6 | A token canceled after a durable store job started does not stop the job; the view learns the result or ignores it after disposal (INV-IPC-21, EDGE-IPC-41). |
| K7 | `ISensitiveRegionScanner.ScanAsync(ct)`: the WinRT OCR call is not interruptible; the result is discarded after cancellation (04 D-EDIT-12). |
| K8 | Services never create their own timeouts from UI tokens; timeouts are the owning spec's (for example 09's 60 s WebView2 navigation and 120 s print watchdog, 08's 60 s exchange client). |

Per-member summary (the members that replace channels):

| Member | Token honored | Effect of cancel |
|---|---|---|
| `IProjectService.ListProjectsAsync`, `ListRecentProjectsAsync`, `AutoArchiveStaleAsync` | between projects | partial work discarded, `OperationCanceledException` |
| other `IProjectService` members | not taken | n/a (queued, atomic) |
| `IProjectSession.WhenIdleAsync`, `IProjectSettle.WhenSettledAsync` | yes | stops waiting; the pending writes still complete (R-ARCH-6) |
| `ICaptureService.StartAsync`, `CaptureScreenshotAsync`, `ListTargetsAsync` | per 02 | per 02 |
| `IAreaSelectionService.SelectAreaAsync` | yes | resolves `null` |
| `IExportService` members | before each phase (09 7.14) | no partial file is left (09) |
| `IClaudeService.EstimateAsync`, `GenerateAsync` | yes, plus `Cancel()` | `OperationCanceledException`; nothing applied |
| `IAuthService` members | per 08 7.15 | per 08 |
| `IUpdateService.CheckNowAsync` | yes | `OperationCanceledException`; no stamp written |
| `ISettingsService.UpdateAsync` | before the write starts | the optimistic change is rolled back and `Changed(IsRollback: true)` raised |
| `ISensitiveRegionScanner.ScanAsync` | before and after recognition | result discarded |
| `IStepFlattener.EnsureFlattenedAsync` | before each step's bake | earlier bakes stay persisted |
| `IExternalLinks.OpenAsync` | before the SupportUrl lookup | `false` is not returned; `OperationCanceledException` |
| `IShellReveal.RevealProjectAsync` | before the gate | `OperationCanceledException` |

### 7.9 Errors and user messages

```csharp
namespace ShotAI.Core.Errors;

/// Base of every exception whose Message is user-facing text (the Electron strings, verbatim).
public class ShotAIException : Exception
{
    public ShotAIException(string message) : base(message) { }
    public ShotAIException(string message, Exception? inner) : base(message, inner) { }
}

public static class UserMessage
{
    public const string Generic = "Something went wrong. See the log for details.";

    /// null: show nothing (cancellation). Otherwise the text to show.
    public static string? From(Exception e)
    {
        if (e is AggregateException { InnerExceptions.Count: 1 } a) return From(a.InnerExceptions[0]);
        if (e is OperationCanceledException) return null;
        if (e is ShotAIException or IOException or UnauthorizedAccessException)
            return string.IsNullOrWhiteSpace(e.Message) ? Generic : e.Message;
        return Generic;                                    // the caller logs e at Error
    }

    /// Added in WP-A16, for 06's notice center: From shows Generic because e is not an expected
    /// failure, the case the caller logs at Error (X4). False for a cancellation and for an
    /// expected failure whose message is blank.
    public static bool IsUnexpected(Exception e);
}
```

| # | Rule | Class |
|---|---|---|
| X1 | The text shown is the exception's `Message` exactly; no `Error invoking remote method ...` prefix and no `Error: ` prefix (EDGE-IPC-2). | ELECTRON-ONLY prefix removed |
| X2 | Expected failures throw `ShotAIException` or a subclass (01's `ProjectNotKnownException`, `StepNotFoundException`, `UnsupportedImageException`, `ImportRejectedException`, `ArchiveException`, `ManifestCorruptException`; 04's `FlattenException`, `RenderGateException` (with a `(string, Exception)` constructor, the only gate exception name, R-ARCH-8) and `RefusedRenderPathException`; 07's `SopException`, `SopDisabledException`, `NoScreenshotsException`, `NothingToRevertException`; 08's `SignInRequiredException`, `FederationExchangeException`, `FederationNotConfiguredException` (the "not set up" error), `EntraSignInFailedException`, `ApiKeyStoreException` (08 INV-AUTH-36); 09's `ExportException`, `PdfRenderException`; 02's capture errors; `ImportLimits`). Their messages are the Electron strings. Any spec that currently names `InvalidOperationException` for a user-facing message (01 names `InvalidOperationException` for `cannot merge a step into itself`, 01 section 7 error table) uses a `ShotAIException` subclass instead (section 10). 08 already complies (`FederationNotConfiguredException`, 08 INV-AUTH-36). | REQUIRED text; IMPROVEMENT mechanism |
| X3 | `IOException` and `UnauthorizedAccessException` show their OS message, as Electron showed Node's system error text (parity); 09's sharing-violation mapping (`FileInUse`) runs before this. | REQUIRED |
| X4 | Anything else shows `UserMessage.Generic` and is logged at Error with the exception (06 D-HOME-26). | IMPROVEMENT |
| X5 | `OperationCanceledException` shows nothing (K3). A service that turns a timeout into user text does so itself before the exception leaves it (07 7.12 row 1). | REQUIRED |
| X6 | Where the text is shown is the caller's spec: 06's notice (`INoticeService.ShowError(Exception)` calls `UserMessage.From`), 05's prefixed notices (`Import failed: `, `Export failed: `, the rollback notice), the Settings inline `Error: ` block (06), 07's panel error, the pill's error row (03, which substitutes `A capture failed \u2014 see the log for details.` for an empty message). | REQUIRED (each spec) |
| X7 | Services that "never throw" by contract keep that contract: `IUpdateService.CheckNowAsync` (failures are `{ Available = false, Error }`), `IAuthService.TestConnectionAsync` (expected failures in the result), `IExternalLinks.OpenAsync` (refusal is `false`, never an exception; it throws only if the launcher throws, or `OperationCanceledException` on cancel, and 06's callers wrap the call and show nothing, R-ARCH-25), `IClaudeService.Cancel`, `IExportService.RevealExportDirectoryAsync`. | REQUIRED |

### 7.10 Composition root, lifetimes, disposal and shutdown

`App.OnStartup` step 6 (ARCHITECTURE 4.2, 03 7.4.1) builds the container:

```csharp
var services = new ServiceCollection()
    .AddShotAILogging(loggerFactory)      // 10: the bootstrap LoggerFactory of startup step 1 as ILoggerFactory, plus ILogger<T>
    .AddShotAICore()                      // Core: store, sessions, capture engine and shield, SOP, auth, settings forwarders, updates, links, editor services
    .AddShotAIPlatform()                  // Platform: Win32, UIA, WinRT, DPAPI, registry, WebView2 PDF, libavif, shell
    .AddShotAIApp(Dispatcher.CurrentDispatcher, settings, installInfo);   // App: dispatcher, the SettingsService loaded at step 5b, the IInstallInfo read at step 1b, dialogs, windows' services, view models
var provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateOnBuild = true, ValidateScopes = true });
```

ARCHITECTURE 4.3 is the consolidated registration table (with the resolutions of 15.3 applied) and wins where the table below is shorter; the rows below are the ones this spec owns or depends on.

| Registration | Implementation | Lifetime | Registered by |
|---|---|---|---|
| `TimeProvider` | `TimeProvider.System` | singleton instance | Core |
| `AtomicFile`, `ArchiveEngine` | same (01) | singleton | Core |
| `IProjectService` | `ProjectStore` (also `IDisposable` and `IAsyncDisposable`, R-ARCH-10) | singleton | Core |
| `IProjectSessionFactory`, `IProjectSettle` (01 declares, 07 requested) | `ProjectSessionFactory` (one instance for both, via `sp => sp.GetRequiredService<ProjectSessionFactory>()`) | singleton | Core |
| `ICaptureService` | `CaptureEngine` (also `IDisposable`, R-ARCH-10) | singleton | Core (seams from Platform) |
| `CaptureShield`, `IScreenCapture` | `CaptureShield`, `ShieldedScreenCapture` (02) | singleton | Core |
| `ISettingsService`, `ICaptureSettings`, `IProjectStoreSettings` | the one `SettingsService` loaded synchronously at startup step 5b (ARCHITECTURE 4.2), registered by App as that instance (as `SettingsService`), with Core forwarding all three interfaces to it (corrected in WP-A10: the forwarders resolve the concrete type, so none casts one interface to another); `Dispose` idempotent, since the container disposes it through each forwarder | singleton instance | App (the instance), Core (the forwarders) (10) |
| `IUpdateService`, `ReleaseFeed`, `AppVersion` | 10 | singleton | Core (10) |
| `IExternalLinks` | `ExternalLinks` (this spec's algorithm, 10's registration) | singleton | Core |
| `IAuthService`, `IAnthropicClientFactory`, `ISupportUrlAllowlist`, `ISharedHttp`, `IApiKeyStore` (internal), `EntraSession`, `FederationConfigProvider` | 08 (the only Anthropic client factory, R-ARCH-15) | singleton, lazy | Core (08) |
| `IClaudeService` | `ClaudeService` | singleton | Core (07) |
| `IStepFlattener`, `IStepRenderWriter`, `IRenderGate`, `ISensitiveRegionScanner`, `Flattener` | 04 (`IStepFlattener` in `ShotAI.Core.Rendering`, R-ARCH-7) | singleton | Core (04) |
| `IShellReveal`, `IUrlLauncher`, `IWebView2RuntimeInfo` | `ShellReveal`, `ShellUrlLauncher`, `WebView2RuntimeInfo` | singleton | Platform |
| every Platform seam of 01, 02, 04, 08, 09 (`IPathProbe`, `IRenameRetryClassifier`, `ITriggerSource`, `IMonitorCapture`, `IWindowInfoProvider`, `IElementLocator`, `IWindowProtection`, `IOwnWindows`, `IImageCodec`, `IOcrEngine`, `IMsalGatewayFactory`, `ISecretProtector`, `IPolicyValueSource`, `IPdfRenderer`, the AVIF encoder) | per spec | singleton | Platform |
| `IUiDispatcher` | `WpfUiDispatcher` | singleton instance | App |
| `IAppLifetime` | `AppLifetime` | singleton | App |
| `IAppInfo`, `IFileDialogs` | this spec | singleton | App |
| `IInstallInfo` | the object `InstallInfoReader.Read` returned at startup step 1b (12 7.10.4) | singleton instance | App (the instance) |
| `IAppPaths` | `AppPaths` (10 7.4.4, ARCHITECTURE 10.2) | singleton | App |
| `IExportService`, `IExportDialogs`, `ExportEngine` | 09 | singleton | App (service, dialogs), Core (engine) |
| `IAreaSelectionService`, `IMainWindowLayout`, `AppMenuViewModel`, `RecordingVisibilityController`, `CapturePillViewModel`, `PopupExclusion` | 03 | singleton | App |
| `IShellNavigationState` | `NavigationState` (06) | singleton | App |
| `INoticeService`, `IConfirmService`, `ThemeManager` | 06 | singleton | App |
| `IAppStartup` (multiple): `RemoteVisibilityApplier`, `RecordingVisibilityController`, `ThemeManager` | as listed | singleton | App |
| view models (`HomeViewModel`, `SettingsViewModel`, `ProjectDetailViewModel`, `ReportViewModel`, `EditorViewModel`, `SopPanelViewModel`, `ExportViewModel`, and the rest) | per spec | transient, except the named singletons of INV-IPC-22 (06's `CaptureModePickerViewModel`, also registered as `ICaptureTargetSelection`, R-ARCH-26; 03's `AppMenuViewModel` and `CapturePillViewModel`) | App |

Rules:

1. No scoped services (there are no scopes). `ValidateScopes` still catches a singleton that captures a transient view model.
2. Every disposable singleton implements `IDisposable` (and optionally `IAsyncDisposable`), because `ServiceProvider.Dispose()` throws `InvalidOperationException` for a resolved service that implements only `IAsyncDisposable`, and `OnExit` is synchronous. Their `Dispose` must not need the UI thread to be free (it runs ON the UI thread). This applies to 02's `ICaptureService`, which 02 7.2 declares as `ICaptureService : IAsyncDisposable`: `CaptureEngine` also implements `IDisposable` (its `Dispose` is `Teardown()` plus releasing its seams without awaiting in-flight jobs, which step 2 of the shutdown table already stopped), as 02 7.2 now states (R-ARCH-10); likewise `ProjectStore : IProjectService, IDisposable, IAsyncDisposable` (01, R-ARCH-10). 01 7.12's former `Task.Run(...).Wait(6 s)` flush and `await provider.DisposeAsync()` are superseded by this section (R-ARCH-10). Likewise 01's `ProjectSession : IAsyncDisposable` is not container-owned (sessions are created by the factory and disposed on Back), so it is exempt.
3. After the main window exists (03 7.4.1 between steps 9 and 10), the App resolves `IEnumerable<IAppStartup>` and calls `Start()` on each, in registration order. `Start()` subscribes; it does no IO.
4. Nothing resolves services through a static service locator; windows and view models receive their dependencies by constructor. The one exception is the composition root itself (`App`).

Shutdown (`App.OnExit`, UI thread), in order:

| Step | Action | Budget |
|---|---|---|
| 1 | `AppLifetime` cancels `Stopping` (every linked operation token cancels) | none |
| 2 | `ICaptureService.Teardown()` (synchronous, 03 INV-SHELL-19) | 02 |
| 3 | `ShutdownFlush.Run(TimeSpan.FromSeconds(5))`: `Task.WhenAll(projects.FlushAsync(Timeout.InfiniteTimeSpan), settings.FlushAsync(Timeout.InfiniteTimeSpan)).Wait(t)` (corrected in WP-A12: a drain given `t` would complete at `t` without faulting, 01 7.7, so only the wait is bounded). Both flushes run their jobs on pool threads and never need the UI thread, which is why this one blocking wait is safe (INV-IPC-21). A timeout logs Warning `exit: pending writes not flushed within 5 s` and continues. | 5 s total |
| 4 | `ActivationListener.Dispose()`, the instance lock (03) | none |
| 5 | `provider.Dispose()` | each `Dispose` bounded by its spec |
| 6 | log `exiting (code <n>)` (03) and flush the log sink (10) | 10 |

`SessionEnding` (logoff, shutdown) calls `Teardown()` first (03) and lets WPF proceed to `OnExit`, which runs the same steps. IMPROVEMENT (D-IPC-7): Electron could lose a write chain queued just before quit (01 D-18).

### 7.11 Logging

| # | Rule | Class |
|---|---|---|
| L1 | Category `svc` for boundary lines (Electron: `ipc`). | IMPROVEMENT (name) |
| L2 | Each catalog method that replaces an invoke or send channel logs one Debug line on entry: `call: {Service}.{Member}`, for example `call: IProjectService.CreateProjectAsync`, through a `LoggerMessage` source-generated method `ServiceLog.Call(ILogger, string service, string member)`. Snapshot readers (`GetState`, `Current`, `Pending`) do not log (they are read on every event natively). No arguments are logged (INV-IPC-18). | REQUIRED intent (a trace of what the UI asked for, the gap `772e381` hit); IMPROVEMENT format |
| L3 | `AppMenuViewModel` logs Debug `menu: brand state open={open} project={rawTheme ?? "null"} app={appBrand}` whenever the computed brand menu state changes (the successor of `ipc: view:set-brand-menu ...`). As built in WP-A18: once at construction and then once per change of `(ProjectOpen, RawProjectTheme, AppBrand)`, with `open` written `true` or `false` as Electron's template string wrote it (`AppMenuViewModelTests.BrandStateIsLoggedOnChange`). | REQUIRED |
| L4 | `ExternalLinks` logs Warning `refused openExternal for non-allowlisted URL: {origin}` (2.5.1). | REQUIRED |
| L5 | `EventRaiser` logs Warning `event handler failed: {eventName}` with the exception. | IMPROVEMENT |
| L6 | `ShutdownFlush` logs Warning `exit: pending writes not flushed within 5 s` on timeout, and Warning `exit: flushing pending writes failed:` with the exception when a flush faults (added in WP-A12); the exit goes on after either. | IMPROVEMENT |
| L7 | Unexpected exceptions shown as `UserMessage.Generic` are logged at Error by the view model that caught them, with the operation name and the exception (type, message, stack); never the user's text content. | IMPROVEMENT |
| L8 | Never logged anywhere on the boundary: the API key, tokens, assertions, the account UPN, captions, step text, custom instructions, file contents, and URL paths or queries. | REQUIRED [SECURITY] |

### 7.12 Analyzers and enforcement

Packages (versions only in `dotnet/Directory.Packages.props`; `PrivateAssets="all"`): `Microsoft.VisualStudio.Threading.Analyzers`, `Microsoft.CodeAnalysis.BannedApiAnalyzers`. Severity in `dotnet/.editorconfig` (warnings are errors per `Directory.Build.props`):

| Rule | Core | Platform | App | Why |
|---|---|---|---|---|
| `CA2007` ConfigureAwait | error | error | none | T3, T4 |
| `CA2016` forward the `CancellationToken` | error | error | error | K1 |
| `CA1416` platform compatibility | error | error | error | INV-IPC-19 |
| `CA2012` use `ValueTask` correctly | error | error | error | 01's `ValueTask` mutations |
| `VSTHRD002` no synchronous waits | error | error | error (one allowlisted file) | T9 |
| `VSTHRD100` no `async void` | error | error | error (per-site suppression for event handlers) | T12 |
| `VSTHRD101` no async lambdas to void delegates | error | error | error | T12 |
| `VSTHRD110` observe async results | error | error | error | fire-and-forget only through an explicit `_ =` with a logged continuation |
| `VSTHRD200` `Async` suffix | error | error | error | naming (hence `RevealInExplorerAsync`). Conflicts with 01's fixed names `ProjectSession.Apply` and `ApplyDurable` (both return `Task<ProjectManifest>`, 01 7.10) and this spec's `IProjectSession.Apply`/`ApplyDurable`: Q-IPC-21 |
| `VSTHRD001`, `VSTHRD003`, `VSTHRD004`, `VSTHRD010`, `VSTHRD012`, `VSTHRD111` | none | none | none | JoinableTaskFactory rules that do not apply; ConfigureAwait is `CA2007`. Corrected in WP-A1: `VSTHRD003` (await of a task held in a field or parameter) belongs here too; in-flight joins (10's `UpdateService`) and the test dispatchers' `InvokeAsync` await such tasks by design |

`BannedSymbols.txt` (BannedApiAnalyzers, diagnostic `RS0030`). Entries are documentation comment IDs, one overload each. Corrected in WP-A1: an `M:` entry written without a parameter list bans only the parameterless overload (the deliberate violation showed `M:System.Math.Round` banning nothing), so the files list every overload by its full ID, and "every overload" below means one line per overload (ARCHITECTURE 14.9). The analyzer has no per-file allowlist, so "Allowed only in" is implemented as a `.editorconfig` section for exactly that file (`[**/WpfUiDispatcher.cs]` with `dotnet_diagnostic.RS0030.severity = none`), never with a project-wide `NoWarn`:

| Project | Banned (one entry per line) | Allowed only in |
|---|---|---|
| App | `M:System.Windows.Threading.Dispatcher.Invoke`, `M:System.Windows.Threading.Dispatcher.BeginInvoke`, `M:System.Windows.Threading.Dispatcher.InvokeAsync` and `System.Windows.Threading.DispatcherExtensions` (every overload of each) | `WpfUiDispatcher.cs`; `StaRenderThread.cs` (it drives its own dispatcher, 04 7.10.6); `UiDeferral.cs` (focus and layout deferrals at a named priority, 04 7.10.5, R-ARCH-18). Service-event marshaling stays `IUiDispatcher.Post` only (T6) |
| App | `M:System.Threading.Tasks.Task.Wait`, `M:System.Threading.Tasks.Task.WaitAll`, `M:System.Threading.Tasks.Task.WaitAny` (every overload of each), ``P:System.Threading.Tasks.Task`1.Result``, ``P:System.Threading.Tasks.ValueTask`1.Result``, and `GetResult` of `TaskAwaiter`, ``TaskAwaiter`1``, `ValueTaskAwaiter`, ``ValueTaskAwaiter`1`` and the four configured awaiters | `ShutdownFlush.cs` |
| all | `M:System.Diagnostics.Process.Start` (every overload) | `ShellUrlLauncher.cs`, `ShellReveal.cs`, `ProcessStarter.cs` (12 7.10.4) |
| all | `T:Microsoft.Web.WebView2.Wpf.WebView2`, `T:Microsoft.Web.WebView2.WinForms.WebView2` | nowhere (09 uses the Core controller, INV-IPC-20); the App project additionally has no package reference to WebView2 at all |
| Core | `P:System.Threading.SynchronizationContext.Current`, `M:System.Threading.SynchronizationContext.SetSynchronizationContext(System.Threading.SynchronizationContext)` | `ProjectSessionFactory.cs` (captures the UI context at creation) |
| Core | `M:System.Math.Round` (every overload) and the other half-to-even roundings of ARCHITECTURE 14.9 | `JsMath.cs` (JS rounding goes through `JsMath.Round`; .NET `Math.Round` rounds half to even, ARCHITECTURE Q-ARCH-5) |

This table and ARCHITECTURE 14.9 describe the same `BannedSymbols.txt`; ARCHITECTURE 14.9 is canonical. Where they differ, the file and its `.editorconfig` allowlist sections follow ARCHITECTURE 14.9, which also carries the Core namespace entries (`N:Microsoft.Win32`, `N:Windows`) and the `MessageBox.Show` entry that this spec does not own.

### 7.13 APIs and packages

| Kind | Items |
|---|---|
| NuGet (new, this spec) | `Microsoft.Extensions.DependencyInjection` (App), `Microsoft.Extensions.DependencyInjection.Abstractions` and `Microsoft.Extensions.Logging.Abstractions` (Core, for the `Add*` extensions and `ILogger`), `CommunityToolkit.Mvvm` (Core presenters per 03, App view models), `Microsoft.VisualStudio.Threading.Analyzers`, `Microsoft.CodeAnalysis.BannedApiAnalyzers` |
| CsWin32 (`src/ShotAI.Platform/NativeMethods.txt`) | `SHParseDisplayName`, `SHOpenFolderAndSelectItems`, `ILFree` |
| WPF | `Dispatcher.BeginInvoke`, `Dispatcher.InvokeAsync`, `DispatcherPriority.Normal`, `DispatcherSynchronizationContext`, `Microsoft.Win32.OpenFolderDialog`, `Microsoft.Win32.OpenFileDialog`, `WindowInteropHelper` |
| BCL | `Uri`, `Uri.IdnHost`, `Process.Start` with `UseShellExecute`, `Thread.SetApartmentState(ApartmentState.STA)`, `TaskCompletionSource` with `RunContinuationsAsynchronously`, `Progress<T>`, `CancellationTokenSource.CreateLinkedTokenSource`, `RuntimeInformation.ProcessArchitecture`, `AssemblyInformationalVersionAttribute` |
| WebView2 (version string only, called from Platform's `WebView2RuntimeInfo`) | `CoreWebView2Environment.GetAvailableBrowserVersionString`, `WebView2RuntimeNotFoundException` |

### 7.14 Divergence register

| ID | Change | Class | Justification |
|---|---|---|---|
| D-IPC-1 | The IPC transport, preload, `contextBridge`, `IpcChannels` and structured-clone copying are replaced by direct interface calls | ELECTRON-ONLY | one process; the compiler checks what `const api: ShotaiApi` checked |
| D-IPC-2 | Errors show the bare message, no `Error invoking remote method` prefix | ELECTRON-ONLY | transport artifact (EDGE-IPC-2) |
| D-IPC-3 | Type-shape validation of arguments is removed; value-domain rules move into Core constructors and coercers | IMPROVEMENT | the renderer threat is gone; the files are not (7.5) |
| D-IPC-4 | Unexpected exceptions show one generic sentence and are logged | IMPROVEMENT | .NET messages are not user text (06 D-HOME-26) |
| D-IPC-5 | State subscribers re-read the current snapshot on each event | IMPROVEMENT | removes the stale-reply race of EDGE-IPC-36 |
| D-IPC-6 | Shell reveals and URL launches run on short-lived STA threads | IMPROVEMENT | Electron's main thread was not the UI thread; ours is, and an unreachable network path can block |
| D-IPC-7 | Queued project and settings writes are drained (5 s) at exit | IMPROVEMENT | a save issued just before quit reaches disk |
| D-IPC-8 | Remote visibility is applied to windows with the optimistic setting change, and re-applied on rollback | IMPROVEMENT | Electron's cache and windows could disagree after a failed write (EDGE-IPC-10) |
| D-IPC-9 | `SetProjectThemeAsync` rejects an unknown non-null brand id instead of pinning the default | IMPROVEMENT | unreachable from the UI; a programming error must not silently pin a brand |
| D-IPC-10 | The brand menu reads the open manifest's raw theme; the pushed `setBrandMenu` state and `projectPinUnrecognised` flag disappear | ELECTRON-ONLY | the menu and the manifest are in one process (EDGE-IPC-4) |
| D-IPC-11 | `capture:single` is not ported | ELECTRON-ONLY | no UI caller; `CaptureScreenshotAsync` serves the intent (02 Q-CAP-1) |
| D-IPC-12 | `AppInfo` reports .NET and WebView2 versions instead of Electron, Chromium and Node | ELECTRON-ONLY replaced | no Electron (06 D-HOME-22) |
| D-IPC-13 | The import length check also runs on `FileInfo.Length` before reading | IMPROVEMENT | no multi-gigabyte allocation for a refused file (EDGE-IPC-20) |
| D-IPC-14 | Boundary debug lines are `call: Service.Member` in category `svc`, and snapshot reads are not logged | IMPROVEMENT | natively snapshots are read on every event; the old channel names would be meaningless |
| D-IPC-15 | The sandbox, CSP, navigation confinement, `window.open` denial and `shot://` scheme disappear for the UI; their intent survives only in the WebView2 PDF host | ELECTRON-ONLY | no hosted web content in the UI (INV-IPC-20) |
| D-IPC-16 | Home export, bulk export and package import open dialogs owned by the main window (Electron mixed the focused and the sender window) | IMPROVEMENT (consistency) | they were the same window in practice (EDGE-IPC-29) |
| D-IPC-17 | `CheckNowAsync` does not fail when only the `LastUpdateCheckAt` stamp write fails; it logs a Warning, sets `Pending` and returns the result | IMPROVEMENT | the throttle stamp is a convenience; losing a found update to it contradicts the "never throws" contract (EDGE-IPC-43) |

---

## 8. Tests

### 8.1 Electron tests for this subsystem

No Electron test file is assigned to this subsystem: `src/main/ipc.ts`, `src/shared/ipc.ts` and `src/preload/preload.ts` import `electron` and are never loaded by vitest (which runs with `environment: 'node'`). Three test files in other subsystems assert on this subsystem's SOURCE TEXT because the wiring was otherwise untestable. Their IPC-specific cases, and where each intent lands natively:

| Electron file and case | Purpose | Native disposition | Target |
|---|---|---|---|
| `src/main/entra/federation-cache-wiring.test.ts` `imports the invalidator into the IPC layer`, `CALLS it, not merely imports it`, `calls it inside the auth:status handler, which Settings hits on mount` (`call - handler < 1600` characters) | the invalidator shipped uncalled (`1ecebd0`) | behavior test instead of source text: status reflects a policy change without restart. Ports (Linux). | `ShotAI.Core.Tests/ServiceBoundary/StatusRereadsPolicyTests` (with 08's `AuthServiceTests.StatusInvalidatesFederationCache`) |
| same file, `still exports the invalidator config.ts is expected to provide` | the function exists | ELECTRON-ONLY (module shape); covered by the behavior test | none |
| same file, `keeps the administrator doc and the code telling the same story` | the Intune README's "reopening Settings" promise is backed by code | ports as a doc-contract test owned by 08 and 12 (the README moves with the ADMX) | 08 `AdmxContractTests` |
| `src/renderer/project/theme-wiring.test.ts` describe `View -> Brand is driven by what is actually open`: `App pushes the state, and pushes it on every input that changes it`, `App pushes from the top level, not from the project view`, `App subscribes to the menu, or the items do nothing at all`, `passes the choice through untouched, null included`, `main arms the rebuilder BEFORE the first menu build`, `main coerces the pushed state instead of trusting it`, `the menu refuses to rebuild when nothing changed`, `the Brand submenu is disabled when no project is open`, `offers every brand, the default one included`, `keeps the rebuild off the click path`, `never lets a menu rebuild fail the call that triggered it`, `spells the View menu out so the standard entries survive` | the global menu describing a per-project setting fails silently in every direction (`701d4aa`, `772e381`) | push, arm-before-build and coercion cases: ELECTRON-ONLY (no push, no rebuild). Pass-through, disabled-when-closed, every-brand and no-failure intents port as behavior tests (Windows, WPF menu) | 03 `BrandMenuModelTests`, 03 `AppMenuViewModelTests`; `ShotAI.App.Tests/ServiceBoundary/BrandChoicePassThroughTests` |
| same file, describe `the brand menu never ticks a state the project is not in (#107)`: `distinguishes an unreadable pin from no pin at all`, `and pinnedBrand still answers null for BOTH, which is why the flag exists`, `ticks App default only when nothing is pinned`, `never offers the unrecognised state as something to pick`, `carries the flag through every layer between the manifest and the menu`, `clears the flag when the project closes` | #107 | the tick rules port (Linux, Core `BrandMenuModel`); `carries the flag through every layer` and `clears the flag` are ELECTRON-ONLY (no flag natively, D-IPC-10) | 03 `BrandMenuModelTests.UnrecognisedPinTicksNothing` and siblings |
| `src/shared/export-theme.test.ts` `the IPC layer supplies the app brand as the fallback` (three export handlers each pass `brand:`) | a setting that persists but never reaches exports (#77) | ports as behavior (Linux): each `IExportService` path reads `ISettingsService.Current.Brand` at call time | 09 `ExportEngineTests.BrandIsReadAtExportTime` (INV-EXP-14); `ShotAI.App.Tests/ServiceBoundary/ExportReadsBrandAtCallTimeTests` |

### 8.2 New tests the native boundary needs

**ShotAI.Core.Tests (Linux and Windows)**

| Class | Cases |
|---|---|
| `ServiceBoundary.ChannelInventoryTests` | `MapHas87UniqueChannels`; `SplitIs74Invoke3Send10Push`; `EveryRowHasOneTargetAndAClass`; `MatchesElectronWhileItExists` (finds the repo root by walking up from `AppContext.BaseDirectory` to the first directory that contains `dotnet/ShotAI.slnx`, then reads `src/shared/ipc.ts` there when present, extracts the `IpcChannels` values with the regex `^\s+\w+:\s*'([a-z-]+:[a-z-]+)',` in multiline mode, and asserts set equality with `channel-map.json`; skipped with a message when the file is absent after cutover) |
| `Threading.UiDispatcherContractTests` (against `ManualUiDispatcher` and `ThreadUiDispatcher`) | `PostNeverRunsInline`; `PostsRunInCallOrder`; `PostFromManyThreadsKeepsPerThreadOrder`; `InvokeAsyncInlineOnUiThread`; `InvokeAsyncCanceledBeforeRunNeverRuns`; `InvokeAsyncFaultsWithHandlerException`; `InvokeAsyncOfAsyncLambdaCompletesWhenLambdaCompletes` (the `Func<Task>` overload) |
| `Threading.EventRaiserTests` | `AllHandlersRunInOrder`; `ThrowingHandlerIsLoggedAndOthersStillRun` (log text `event handler failed: StateChanged`); `NullHandlerIsNoop` |
| `Threading.SubscribeThenReadTests` | with a fake state service and `ManualUiDispatcher`: `ChangeBetweenSubscribeAndReadIsSeen`; `OlderPayloadProcessedLateDoesNotWin` (state A raised, then B set, then the A event processed: the subscriber shows B); `UpdatePendingBeforeSubscribeIsShownOnce`; `UpdateEventAfterReadIsShownOnce`; `DismissedNoticeStaysDismissed` (INV-IPC-26); `PendingReadFailureShowsNothing` |
| `Threading.StaleProgressTests` | `ReportsAfterDisposeIgnored`; `ReportsFromPreviousRunIgnored` (run token) |
| `Errors.UserMessageTests` | `ShotAIExceptionShowsMessage` (verbatim, including `\u2014`); `EmptyMessageShowsGeneric`; `CanceledShowsNothing` (`OperationCanceledException`, `TaskCanceledException`); `SingleInnerAggregateUnwraps`; `IOExceptionShowsOsMessage`; `UnauthorizedAccessShowsMessage`; `ArgumentExceptionShowsGeneric`; `NullReferenceShowsGeneric`; `GenericTextIsExact` (`Something went wrong. See the log for details.`); added in WP-A16: `UnexpectedIsTheGenericCaseOnly` |
| `Links.ExternalLinkPolicyTests` | allowed: `https://anthropic.com`, `https://console.anthropic.com/settings/keys`, `https://docs.anthropic.com/x?y=1`, `https://github.com/org/repo/releases/tag/v1.2.3`, `https://GITHUB.com/x` (host lower-cased), `https://Console.Anthropic.COM/`; refused: `http://anthropic.com`, `https://evil-anthropic.com`, `https://anthropic.com.evil.example`, `https://raw.github.com/x`, `https://gist.github.com/x`, `https://objects.github.com`, `https://codeload.github.com`, `javascript:alert(1)`, `file:///C:/x`, `mailto:a@b.c`, `not a url`, `""`; SupportUrl: `https://help.example.org/access` configured allows `https://help.example.org/other` and refuses `https://help.example.org:8443/x`, `http://help.example.org/x`, `https://sub.help.example.org/x`; a configured `not a url` allows nothing extra; no federation config allows nothing extra; the refusal log line is `refused openExternal for non-allowlisted URL: https://evil-anthropic.com` (origin only, no path); an unparseable URL logs nothing; the launcher receives `Uri.AbsoluteUri`. As built in WP-A19b: the SupportUrl rows go through a fake `ISupportUrlAllowlist`; the exact-origin match they describe is 08's `Auth/SupportUrlAllowlistTests` (WP-D4), and with the no-federation implementation nothing extra is allowed |
| `Links.UrlOriginTests` | `https://a.b/c` gives `https://a.b`; default port omitted; `https://a.b:8443/` keeps `:8443`; IDN host gives punycode; `mailto:x@y` gives `null`. As built in WP-A19b: also `ws`, `wss` and `ftp` with and without their default ports, an upper-case host, user info dropped, an IPv6 host in brackets, and `javascript:`, `file:`, `data:`, `urn:` and a relative URI giving `null` |
| `Validation.ImportLimitsTests` | `ZeroBytesIsNoImageData` (`No image data received`); `ExactlyMaxIsAccepted` (62914560); `OneOverMaxIsTooLarge` (62914561, `Image too large (max 60 MB)`); `ChecksRunBeforeMagicBytes` (a 0-byte PNG-named input gives the empty message, not `Unsupported file \u2014 ...`) |
| `Validation.BoundaryRulesLiveInCoreTests` | the report operation constructors and `StepPatchValidator` clamp zoom to `[1, 6]` at the store boundary and pan to `[0, 1]`; `customInstructions` of 2001 UTF-16 units is stored as 2000 through `ISettingsService.UpdateAsync` (fake store); a user name of 121 units is stored as 120 |
| `ServiceBoundary.AuthSurfaceTests` | `AuthStatusHasExactlySevenMembers`; `ApiKeyStatusHasExactlyFourMembers`; `TestConnectionResultMembers` (`Ok`, `Mode`, `Model`, `Error`, `Leg`); `NoTokenShapedMembers` (the regex of INV-IPC-1 over every public property reachable from `IAuthService` return types); `ApiKeyNeverReturned` (no public catalog method returns a `string` that is not a status or message) |
| `ServiceBoundary.StatusRereadsPolicyTests` | two `GetStatusAsync` calls with a fake policy source changed in between: `FederationAvailable` flips; a failing policy read degrades to baked values, not to `false` |
| `ServiceBoundary.FireAndForgetTests` | `ClaudeCancelWithNothingInFlightReturns`; `ClaudeCancelTwiceReturns` |
| `ServiceBoundary.ScreenshotTargetTests` | the engine contract (fake seams): `NullTargetThrowsExactMessage`, `AutoTargetThrowsExactMessage` (`A screenshot needs an explicit target (screen, window, or area).`, as `ShotAIException`); `ScreenshotRaisesOneIdleStateAndNoStep` (INV-IPC-25, on success and on a failed grab) |
| `ServiceBoundary.UpdateCheckNowTests` | `StampWriteFailureStillReturnsAndSetsPending` (D-IPC-17); `UpToDateClearsPending`; `ErrorResultIsStampedAndNotPending`; `NeverRaisesUpdateAvailable` (EDGE-IPC-31) |
| `Logging.BoundaryLogTests` | a capturing `ILoggerProvider`: `CallLinesNameServiceAndMember` (`call: IProjectService.CreateProjectAsync`); `SnapshotReadsDoNotLog`; `NoSecretsInAnyLine` (scripted session: set key `sk-ant-test-XXXX`, sign in as `user@example.test`, rename a project to `Payroll Q3`, refuse `https://evil.example/path?q=secret`; no line contains `sk-ant`, `user@example.test`, `Payroll`, `/path` or `q=secret`); `BrandMenuStateLineFormat` (`menu: brand state open=True project=null app=shotAI`) |
| `Architecture.CoreReferencesTests` | `CoreReferencesNoWindowsAssemblies` (the list in INV-IPC-19); `CatalogInterfacesConsumedByCoreLiveInCore` |

**ShotAI.App.Tests (Windows only; STA test host)**

| Class | Cases |
|---|---|
| `ServiceBoundary.ChannelMapResolutionTests` | `EveryTargetResolves` (each `channel-map.json` row's `member` string resolves by reflection across the Core, Platform and App assemblies to a method, property, event, command property or parameter of the named type); `DroppedChannelHasSuccessor` (`capture:single` resolves to `ICaptureService.CaptureScreenshotAsync`) |
| `Threading.WpfUiDispatcherTests` | `PostFromPoolRunsOnUiThread`; `PostFromUiThreadDoesNotRunInline`; `OrderAcrossTwoSubscribersIsRaiseOrder`; `InvokeAsyncCanceledBeforeRun`; `PostAfterShutdownIsDropped` |
| `Threading.EventOrderTests` | a fake `ICaptureService` raises `StepLanded`, `StateChanged`, `StepLanded`, `StateChanged` from a pool thread; `RecordingPanelViewModel` and a pill presenter observe `[S, T, S, T]` with step counts non-decreasing |
| `Threading.NoSyncWaitTests` | scan of `src/ShotAI.App/**/*.cs`: no `.Wait(`, `.Result`, `GetAwaiter().GetResult()`, `Dispatcher.Invoke(` outside the allowlisted files (belt and braces for the analyzers). Corrected in WP-A12: a text scan with comments and string literals removed, not a Roslyn syntax scan, so the tests take no compiler package; each rule is also run on a snippet to prove it fires |
| `Threading.ViewModelAffinityTests` | every view model constructed off the UI thread throws in debug builds (`Dispatcher.VerifyAccess` in `ObservableObject` base `ViewModelBase`). Corrected in WP-A12: the check is behind `ViewModelBase.CheckAffinity`, on by default only in a Debug build, and throws with the view model and property named; the tests turn it on, since CI runs the Release build |
| `Composition.ContainerTests` | `BuildsWithValidateOnBuild`; `EveryCatalogInterfaceResolves` (every interface of 7.3 and ARCHITECTURE 4.4); `SettleAndFactoryAreOneInstance` (`IProjectSettle` and `IProjectSessionFactory` resolve to the same `ProjectSessionFactory`, R-ARCH-6); `SingletonsAreSingletons`; `ViewModelsAreTransient` (except the named singleton view models of INV-IPC-22); `NoAsyncOnlyDisposables` (every registered disposable singleton type implements `IDisposable`); `StartupsStartInRegistrationOrder`. As built in WP-B2: `EveryCatalogInterfaceResolves` has a `ResolvableFrom` map for a catalog interface that exists before its implementation can be registered; each entry names the WP that registers it, and the test fails as soon as that interface resolves, so the entry goes with the registration. As built in WP-B6: the last entry, `ICaptureService`'s, went with the engine's registration, which leaves the map empty |
| `Composition.ViewModelDependencyTests` | reflection over view model constructors: parameters are catalog interfaces (7.3 and ARCHITECTURE 4.4, including `ICaptureTargetSelection`, R-ARCH-26), 06's chrome services, the factories of ARCHITECTURE 4.1 C6, `IUiDispatcher` (any view model), the registered `TimeProvider` (added in WP-A16, for 06's `HomeViewModel`), `ILogger<T>`, value types or other view models; one named addition: `EditorViewModel`, which `EditorFactory` constructs, may also take the Core types `Flattener`, `IRenderCodec` and `IPathProbe` and the App interface `IColorPicker` (04 7.10.1); never `ProjectStore`, `EntraSession`, `IApiKeyStore`, `MsalGateway`, `CaptureEngine`, `SettingsService` (concrete) or any Platform type (INV-ARCH-3) |
| `Composition.CommandConventionsTests` | every `IAsyncRelayCommand` on a view model disallows concurrent execution unless allowlisted |
| `Composition.SubscriberDisposalTests` | after `Dispose`, a view model subscribed to each singleton event (`StateChanged`, `StepLanded`, `CaptureFailed`, `ProjectsChanged`, `UpdateAvailable`, `Changed`) is no longer in the invocation list |
| `Shutdown.ExitFlushTests` | a queued project write and a queued settings write both complete before `OnExit` returns; a store that never completes makes `OnExit` return after 5 s with the warning line; nothing in the flush needs the UI thread (the test blocks the UI thread during the flush and still completes) |
| `Settings.RemoteVisibilityApplierTests` | `OptimisticChangeAppliesImmediately` (fake `CaptureShield` records `true` before the write completes); `RollbackReappliesOldValue`; `UnchangedValueDoesNotApply`; `StartupDoesNotApply`; `ApplyNeverRunsOnUiThread` (the fake shield records that `ApplyRemoteVisibility` ran on a pool thread, never the UI thread, ARCHITECTURE DL1); `RapidTogglesEndOnLatestValue` (on, off, on raised back to back: the last applied value is `true`). As built in WP-B5: the six over the real shield and a recording protection, plus `AFailedApplyIsLoggedAndTheNextStillApplies`, `DisposeStopsFollowing`, `TheContainerStartsTheApplierFirst` and `ArgumentsAreChecked` |
| `ServiceBoundary.BrandChoicePassThroughTests` | `AppDefaultSendsNull`; `DefaultBrandSendsItsId` (`"shotAI"`, a different operation from `null`); `NoProjectOpenDoesNothing`; `UnknownIdRejectedByStore` (`ArgumentException`). As built in WP-A18: the operations reaching the session are recorded by a wrapping session factory, and the writes by the test store; the rejection is the operation's constructor's, before anything is applied (the store's own is Core `Store/ProjectThemeKeyTests.AnUnknownBrandIsAProgrammingError`); also `TheTickedRowWritesNothing` and `AppDefaultClearsAnUnrecognisedPin` (AC-IPC-14's automated half) |
| `ServiceBoundary.ExportReadsBrandAtCallTimeTests` | change `ISettingsService.Current.Brand` between two exports; each export receives the brand current at its call |
| `Shell.ShellRevealTests` | `RevealProjectRejectsUnknownProject` (gate message); `RevealRunsOnStaThread` (the fake shell records `Thread.CurrentThread.GetApartmentState() == STA` and not the UI thread); `OpenFolderUsesShellExecute`. As built in WP-A19a: in `ShotAI.Platform.Tests`, where the shell seam is visible, over a real `ProjectStore`; also `RevealProjectRevealsTheResolvedPath`, `OpenFolderNeverLaunchesAFile`, `AShellFailureFaultsTheTask`, `ShellFailureCarriesTheSystemMessage`, and `Shell.StaThreadTests` |
| `Architecture.SingleWebViewTests` | only `WebView2PdfRenderer` and `WebView2RuntimeInfo` (both `ShotAI.Platform.Export`) reference `Microsoft.Web.WebView2.Core`; `ShotAI.App` references no `Microsoft.Web.WebView2.*` assembly; no BAML resource contains a WebView2 element. Landed in WP-A15 with the package and the probe: `OnlyPlatformReferencesTheWebView2Core` (the compiled assemblies' references), `OnlyPlatformExportNamesWebView2` (no `.cs` or `.xaml` under `dotnet/src` outside `ShotAI.Platform/Export/` names it), `TheAppHasNoWebView2PackageReference`, `TheControlAssembliesAreNotInTheOutput`; the PDF host (WP-D10) extends it |
| `Architecture.SingleUrlLauncherTests` | IL scan: only `ShellUrlLauncher`, `ShellReveal` and `ProcessStarter` (12 7.10.4, which starts an exe with `UseShellExecute = false` and never a URL) call `Process.Start`. As built in WP-A19b: the scan reads the three product assemblies with `System.Reflection.Metadata`, every `call`, `callvirt`, `newobj`, `ldftn`, `ldvirtftn` and `jmp` of any `System.Diagnostics.Process.Start`, and counts a lambda's closure or a state machine as the type that declares it; `NothingImportsShellExecute` also checks that no product assembly imports a `ShellExecute` function. New beside it: Platform `Shell.ShellUrlLauncherTests` (the STA thread, the start info, the refusals, a shell failure faulting the task) |

The `channel-map.json` format (one object per channel, in 2.4 order):

```json
{ "channel": "projects:update-step", "kind": "invoke", "row": "S1",
  "member": "ShotAI.Core.Store.IProjectService.UpdateStepAsync", "mode": "Durable",
  "class": "REQUIRED", "owner": "01" }
```

`member` is the full name of the interface or type, a `.`, and the member, written as the 7.4 tables name it. Two forms go further (added in WP-A1, which wrote the file): a settings row names the property path through the snapshot, `ShotAI.Core.Settings.ISettingsService.Current.Sop`, and a progress row names the method with the parameter in parentheses, `ShotAI.Core.Sop.IClaudeService.GenerateAsync(progress)`. `ChannelMapResolutionTests` resolves both forms.

---

## 9. Acceptance criteria

**AC-IPC-1.** `ChannelInventoryTests` passes: `channel-map.json` lists 87 unique channels (74 invoke, 3 send, 10 push) and, while `src/shared/ipc.ts` exists, equals its `IpcChannels` values.

**AC-IPC-2.** `ChannelMapResolutionTests` passes on Windows: every row's member resolves.

**AC-IPC-3.** `AuthSurfaceTests` passes: `AuthStatus` has exactly the seven members of INV-IPC-1, and no member reachable from `IAuthService` is token-shaped; no catalog method returns the API key.

**AC-IPC-4.** `ExternalLinkPolicyTests` passes with the full table of 8.2; manual: in the running app (a per-user or unpackaged build; a per-machine install shows no download action, 06 INV-HOME-45), the update notice's `Open the download page` opens the GitHub release page, and a test build that calls `OpenAsync("https://raw.github.com/x")` opens nothing and writes `refused openExternal for non-allowlisted URL: https://raw.github.com` to the log.

**AC-IPC-5.** `EventOrderTests` and `WpfUiDispatcherTests` pass: two subscribers see capture events in raise order, and a `Post` from the UI thread runs after previously queued work.

**AC-IPC-6.** The solution builds with `VSTHRD002`, `VSTHRD100`, `VSTHRD101`, `VSTHRD110`, `CA2007` (Core, Platform), `CA2016` and `CA1416` as errors and the banned-symbol lists of 7.12, and `NoSyncWaitTests` passes.

**AC-IPC-7.** `SubscribeThenReadTests` passes, including `OlderPayloadProcessedLateDoesNotWin`; manual: start a recording and click rapidly five times; the pill's count and the recording panel's count both end at the recorded step count.

**AC-IPC-8.** `StaleProgressTests` passes; manual: start a PDF export of a 20-step project and press Back immediately; no exception is logged and the report of the next project opened shows no stale progress.

**AC-IPC-9.** `UserMessageTests` passes; manual: import a 0-byte file with Insert Image and the notice reads exactly `Import failed: No image data received` (no `Error invoking remote method`, no `Error: `).

**AC-IPC-10.** `ImportLimitsTests` passes; manual: Insert Image with a 70 MB PNG shows `Import failed: Image too large (max 60 MB)` and the process working set does not grow by 70 MB (the length check runs before the read).

**AC-IPC-11.** `StatusRereadsPolicyTests` passes; manual on a managed machine: remove the `HKLM\SOFTWARE\Policies\shotAI\Federation` values while Settings is closed, reopen Settings: the Microsoft sign-in group is gone without a restart.

**AC-IPC-12.** `RemoteVisibilityApplierTests` passes; manual: during a Teams screen share, turn on `Show shotAI in remote sessions & screen shares`; the viewer sees the window within one second, without a restart; make `settings.json` read-only and toggle it off: the switch rolls back with the standard notice and the window stays visible to the viewer, matching the switch.

**AC-IPC-13.** `BrandChoicePassThroughTests` passes; manual: with the app brand LFI, open a project, choose View, Brand, shotAI: `project.json` gains `"theme": "shotAI"`; choose App default: the key is removed.

**AC-IPC-14.** Manual: open a project whose `project.json` has `"theme": "future-brand"`; View, Brand ticks nothing; choosing App default removes the key and re-dates the project; choosing nothing leaves the file byte-identical.

**AC-IPC-15.** `BoundaryLogTests` passes; manual: after a session that sets an API key, signs in and exports, `shotai.log` contains `call: IAuthService.SetApiKeyAsync` and no substring of the key, the UPN or any caption.

**AC-IPC-16.** `CoreReferencesTests` passes on Linux.

**AC-IPC-17.** `ContainerTests`, `ViewModelDependencyTests`, `CommandConventionsTests` and `SubscriberDisposalTests` pass.

**AC-IPC-18.** `ExitFlushTests` passes; manual: rename a project and within 100 ms choose File, Exit; after relaunch the new title is shown.

**AC-IPC-19.** `SingleWebViewTests` and `SingleUrlLauncherTests` pass.

**AC-IPC-20.** Manual: Home, row menu, Reveal in Explorer on a project whose folder is on a disconnected network share: the app stays responsive (the window repaints and accepts input) while Explorer times out, and an error notice appears if the reveal fails.

**AC-IPC-21.** Manual: area selection from the target chooser: the main window hides, the overlay appears on every monitor, Esc restores and activates the main window; repeat with a drag: the chosen rectangle is shown in the chooser and the main window is active.

**AC-IPC-22.** Manual parity walk: every row of 7.4 whose Native caller is a UI surface is exercised once in the native app against the same project in the Electron build, with the same visible outcome (the only intended differences are the D-IPC items and the optimistic timing of 05 and 06).

**AC-IPC-23.** `ScreenshotTargetTests` passes, and `FireAndForgetTests` passes.

**AC-IPC-24.** `ExportReadsBrandAtCallTimeTests` passes; manual: switch the app brand in Settings and export again without restarting; the new export uses the new brand unless the project pins one.

**AC-IPC-25.** `UpdateCheckNowTests` and the `ScreenshotRaisesOneIdleStateAndNoStep` case pass; manual: dismiss the update notice on a build that finds an update, open and close Settings and a project: the notice does not return until the next launch.

---

## 10. Interfaces with other subsystems

| Spec | This subsystem consumes | This subsystem provides, or requests |
|---|---|---|
| 01 Model and store | `ProjectStore` and its method set, `ProjectSession`, `ProjectOperation`, `MutateResult`, `OpenedProject`, `ProjectSummary`, `ProjectManifest`, `StepPatch`, `SopIntro`, `ImportFile`, the known-project gate, `ResolveImage`, `FlushAsync` | REQUESTS: `ProjectStore : IProjectService` (7.3.2, the name that replaces 09's `IProjectStore`); `event ProjectsChanged` raised after `AutoArchiveStaleAsync` moved at least one project; `ImportStepAsync` calls `ImportLimits.Check(bytes.Length)` first; `IProjectSession`, `IProjectSessionFactory` and `IProjectSettle` with the ARCHITECTURE 7.4 contract (R-ARCH-23): `IProjectSession.WhenIdleAsync` is the one wait-for-writes primitive and `IProjectSettle.WhenSettledAsync(path)` finds the open session and awaits it, or completes at once (R-ARCH-6); sessions only from `IProjectSessionFactory.Create` on the UI thread (R-ARCH-5); `ManifestChangeKind` and the event args in `ShotAI.Core.Store`, `ProjectOperation.AffectedStepIds` virtual (R-ARCH-20); `ApplyDurable(Func<IProjectService, Task<ProjectManifest>>)`; store exceptions derive from `ShotAIException` (the merge message included); `IDisposable` on `ProjectStore` and the 7.10 exit flush replacing 01 7.12's (R-ARCH-10) |
| 02 Capture | `ICaptureService` (7.2), `CaptureState`, `CaptureStartOptions`, `DiscardResult`, `CaptureTargets`, `StepLandedEventArgs`, `CaptureErrorEventArgs`, `RecordingChangedEventArgs`, `CaptureShield.ApplyRemoteVisibility`, `ICaptureSettings` | the subscriber rules (T5 to T7); REQUESTS: `CaptureScreenshotAsync` throws the explicit-target message as `ShotAIException`; `StartAsync` clamps `InsertAt < 0` to 0; events raised through `EventRaiser`; `capture:single` dropped (answers Q-CAP-1); `CaptureEngine` implements `IDisposable` as well as `IAsyncDisposable` (7.10 rule 2, R-ARCH-10); an idle-to-idle `StateChanged` at the end of every `CaptureScreenshotAsync` (EDGE-IPC-33); `RemoteVisibilityApplier` calls `CaptureShield.ApplyRemoteVisibility` through `Task.Run`, never on the UI thread (7.3.6, ARCHITECTURE DL1). All adopted by 02 7.2 and 7.8 |
| 03 Windows and shell | `IAreaSelectionService`, `IMainWindowLayout` (`MainWindowSizer`), `AppMenuViewModel` (`OpenSettingsRequested`, `ImportProjectRequested`, `ChooseBrandCommand`), `BrandMenuModel`, `RecordingVisibilityController`, the startup and exit order (7.4.1), crash handlers | `IUiDispatcher` (capture events reach the shell only through `IUiDispatcher.Post`, never `InvokeAsync`, R-ARCH-11, as 03 7.4.6 and 7.5 now state; its `StateChanged` handler applies `GetState()` read at processing time, T7; `PillPresenter.OnState` still flashes on any count increase), the About dialog's WebView2 version through `IAppInfo.Current.WebView2Version` and Platform's `IWebView2RuntimeInfo`, with no WebView2 reference in the App (R-ARCH-12), the exit flush step inserted into `OnExit`, `IAppStartup`, `IShellReveal` (03's reveal needs), the brand-state log line; REQUEST: `ChooseBrandCommand` passes the clicked value untouched |
| 04 Editor and redaction | `StepPatchValidator`, `StepPatch`, `IStepFlattener` (`ShotAI.Core.Rendering`, owned by 04, R-ARCH-7), `SensitiveRegionScanner`, `IOcrEngine`, `OcrScanStatus`, `RenderGateException` (R-ARCH-8) | PROVIDED by 04 already: `ISensitiveRegionScanner` (04 7.8, `ShotAI.Core.Redaction`, `ScanAsync(PremultipliedImage, string, CancellationToken)`); `IStepFlattener.EnsureFlattenedAsync` with the `IProjectSession` overload (04 7.7, R-ARCH-5); the banned-symbol allowlist entries `StaRenderThread.cs` and `UiDeferral.cs` (R-ARCH-18). REQUESTS: an editor source-load test `EditorSourceLoaderTests.TraversalScreenshotIsRefused`; `FlattenException`, `RenderGateException` and `RefusedRenderPathException` derive from `ShotAIException` (04 7.1 states it) |
| 05 Report | the operations (`SetReportZoomOperation` and the rest), `DocScale`, `MergeCoordinator`, `ExportViewModel`, the notices | the channel map rows S1 to S10, X1, X6, K3; the progress rule (INV-IPC-8); `ImportLimits.Check` on the picked file's length before reading; `ICaptureTargetSelection` (06, `ShotAI.App.Home`) for Resume capturing (R-ARCH-26); export through 09 7.13's member names only (R-ARCH-9); `IProjectService` and `IProjectSessionFactory`, never the concrete `ProjectStore` or `new ProjectSession` (R-ARCH-4, R-ARCH-5) |
| 06 Home and settings UI | `INoticeService`, `IConfirmService`, `NavigationState` (implements `IShellNavigationState`), the view models | `UserMessage.From` behind `INoticeService.ShowError(Exception)`; `IFileDialogs`; `IAppInfo`; the subscribe-then-read rule for `IUpdateService` and `ISettingsService`; REQUEST: 06's references to 07's `IAuthService.StatusAsync` and `SignInAsync(ct)` use 08's `GetStatusAsync` and `SignInAsync(ownerWindow, ct)` (R-ARCH-2); 06's `reveal.RevealInExplorer(path)` is `IShellReveal.RevealProjectAsync(path)` (row Reveal); 06's callers of `IExternalLinks.OpenAsync` wrap the call and show nothing (R-ARCH-25); 06 provides `ICaptureTargetSelection` in `ShotAI.App.Home`, implemented by the singleton `CaptureModePickerViewModel` (R-ARCH-26) |
| 07 SOP generation | `IClaudeService` (`EstimateAsync`, `GenerateAsync`, `Cancel`), `ApplySopPlanOperation`, `RevertSopOperation`, `SopProgress`, `SopEstimate`, `IProjectSettle` | PROVIDED by 07 already (formerly REQUESTS, resolved): `claude:test-connection` maps to 08's `IAuthService.TestConnectionAsync`; `IClaudeService.TestConnectionAsync` is removed and 07's models-retrieve leg (leg 3) sits behind the internal seam `SopModelProbe` (07 2.5, 7.6, 7.8; R-ARCH-3); 07's `IAuthService` sketch (7.11) is superseded by 08's, and `VerifyFederationAsync` and `IApiKeyStore` are internal to Core (07 7.11; R-ARCH-2); `SopPanelViewModel` depends on `IProjectSession`, `IClaudeService`, `IAuthService`, `ISettingsService`, `IStepFlattener`, `IConfirmService`, plus `IUiDispatcher` and `ILogger<SopPanelViewModel>` (07 7.10; R-ARCH-16); Anthropic clients come only from 08's `IAnthropicClientFactory` (R-ARCH-15); egress waits with `IProjectSettle.WhenSettledAsync` (R-ARCH-6) |
| 08 Auth, secrets and policy | `IAuthService`, `AuthStatus`, `ApiKeyStatus`, `TestConnectionResult`, `ISupportUrlAllowlist`, `ISharedHttp`, the registrations of 7.1 | `IUiDispatcher.InvokeAsync` for MSAL interactive (08 7.5); INV-IPC-1, INV-IPC-2, INV-IPC-12 as the boundary contract; singleton lifetimes and exit disposal (7.10); the "not set up" error is already `FederationNotConfiguredException : ShotAIException` (08 INV-AUTH-36), nothing further requested |
| 09 Export | `IExportService` (7.13), `ExportResult`, `PackageResult`, `ExportProgress`, `ExportFormat`, `WebView2PdfRenderer` hardening | PROVIDED by 09 already (formerly REQUESTS, resolved): 09 depends on `IProjectService`, not an `IProjectStore` (09 7.16; R-ARCH-4); 09's former `IProjectService.WhenIdleAsync(path)` is `IProjectSettle.WhenSettledAsync(path)` over the session's `WhenIdleAsync` (09 Q-EXP-11; R-ARCH-6); `IRenderGate` injected and `RenderGateException` caught, never `RenderRefusedException` (R-ARCH-8); `IShellReveal.RevealInExplorerAsync` and `OpenFolderAsync` (async names, 09 section 10); export reads `ISettingsService.Current.Brand` at call time (09 INV-EXP-14); `ExportException` derives from `ShotAIException`; the WebView2 host stays the only web engine (INV-IPC-20), with its user data folder under `IAppPaths.LocalDataDirectory` (`%LOCALAPPDATA%\LFI\shotAI\WebView2`, R-ARCH-13) |
| 10 Brand, settings, logging, update check, links | `BrandPalette` (`PinnedBrand`, `CoerceBrand`, `IsBrandId`, `PinIsUnrecognised`, R-ARCH-14), `IAppPaths` (with `LocalDataDirectory`, R-ARCH-13; `settings.json` and the logs stay under `%APPDATA%\shotAI` as the Electron build keeps them), `ISettingsService` (7.3.6 contract), `AppSettings`, the coercer, `IUpdateService`, `UpdateCheckResult`, the log sink and categories, the self-test switches | REQUESTS: `UpdateAsync` follows the optimistic rule with `Changed(IsRollback)` (7.3.6); `CheckNowAsync` updates `Pending` without raising `UpdateAvailable`, and a failed `LastUpdateCheckAt` write does not fail it (D-IPC-17); `RunStartupCheckAsync` stashes before raising; `IExternalLinks` implemented exactly as 7.3.4; the `svc` log category; `FlushAsync` for the exit flush |
| 12 Packaging and CI | the Linux job for `ShotAI.Core.Tests`, the Windows job for `ShotAI.App.Tests` (STA host) | the analyzer packages and `.editorconfig` severities of 7.12; `BannedSymbols.txt` files; `channel-map.json` as a test asset; deletion of `src/preload/`, `src/shared/ipc.ts`, `src/main/ipc.ts` and `src/renderer/shotai-api.d.ts` at cutover |

---

## 11. Open questions and risks

**Q-IPC-1. Two `IAuthService` shapes.** 07 7.11 sketches `StatusAsync`, `SignInAsync(ct)`, `SignOutAsync`, `VerifyFederationAsync` plus a public `IApiKeyStore`; 08 7.12 defines `GetStatusAsync`, `SignInAsync(ownerWindow, ct)`, `SignOutAsync`, key status, set, clear, `TestConnectionAsync` and `AuthStatusChanged`, with the key store internal. 06 references 07's names. Resolved by R-ARCH-2: 08's interface and value types are canonical (including the enum `AuthStatus.Mode`); 07's `VerifyFederationAsync` and `IApiKeyStore` are internal to Core; view models use `GetApiKeyStatusAsync`, `SetApiKeyAsync`, `ClearApiKeyAsync`. 06 and 07 align to it. Applied: 06 7.12 and 07 7.11 (sketch superseded) now follow 08's interface; closed.

**Q-IPC-2. Who owns `claude:test-connection`.** 07 puts `TestConnectionAsync` on `IClaudeService`; 08 on `IAuthService` (through `ConnectionTester`). Resolved by R-ARCH-3: `IAuthService.TestConnectionAsync` (08) is the one public entry point; leg 3 (the models retrieve call) is implemented by 07's client code behind an internal seam; `IClaudeService.TestConnectionAsync` is removed. Applied: 07 2.5, 7.6 (`SopModelProbe`), 7.8 and 7.11 now say so; closed.

**Q-IPC-3. `IProjectStore` versus `IProjectService`.** 09 7.3 names the store interface `IProjectStore`; 01's section 10 says "the ProjectStore method set as the IProjectService surface". Resolved by R-ARCH-4: `IProjectService`, one interface, implemented by `ProjectStore`; every consumer outside the store (05, 07's `SopRequestAssembler`, 09) depends on it, never on the concrete `ProjectStore`. Implemented in WP-A6, which registers the store only as `IProjectService` (01 7.14).

**Q-IPC-4. Drop `capture:single`?** Recommended default: yes (02 Q-CAP-1); the map records `CaptureScreenshotAsync` as its successor so the inventory stays complete. Decided in WP-B2: yes; `ICaptureService` has no single-shot member (02 Q-CAP-1, D16).

**Q-IPC-5. Keep `ListRecentProjectsAsync` with no UI caller?** Recommended default: keep; the self-test (10) uses it and it is a few lines. Decided in WP-A6: keep, the default.

**Q-IPC-6. `ProjectsChanged` on the store, or a startup-only signal?** 03 describes the startup code raising it. Resolved by R-ARCH-24: an event on `IProjectService`, raised by `AutoArchiveStaleAsync` when at least one project moved, so any future caller (for example a scheduled archive) gets it for free; the startup code just calls the method (ARCHITECTURE 4.2 step 13), and 01 7.14's direct `homeViewModel.RequestRefresh()` call is superseded.

**Q-IPC-7. Should a manual update find raise the Home notice?** Electron sets `Pending` but never pushes, and App never re-reads it, so the notice does not appear during that session (EDGE-IPC-31); the comment in `src/main/ipc.ts:763-766` suggests the intent was the opposite. Recommended default: parity (no push); revisit together with 06's Check now message, which already reports the update in Settings.

**Q-IPC-8. Unknown keys in `settings.json`.** Electron drops them on the next write (`captureNoHide`, `f24b3dc`); 06 describes 10 as preserving them. Recommended default: preserve (harmless, and it protects a rollback to the Electron build from losing a key the native build does not know); in either case never act on `captureNoHide`.

**Q-IPC-9. Analyzer noise.** `VSTHRD200` (Async suffix) and `VSTHRD100` may flag WPF event handlers and CommunityToolkit-generated members. Recommended default: adopt the table in 7.12; if generated code trips a rule, suppress it for `*.g.cs` only, not globally. Decided in WP-A1: the 7.12 table, plus `VSTHRD003` off; `dotnet/.editorconfig` marks `**.g.cs` as generated code and suppresses nothing globally; `RS0030` still analyzes generated code (the analyzer's default), and nothing generated calls a banned API today.

**Q-IPC-10. Exit flush by blocking wait.** The alternative is cancelling `MainWindow.Closing`, awaiting the flush, and closing again; `Application.Shutdown` paths (File, Exit; session end) make that fragile. Recommended default: the bounded blocking wait of 7.10 step 3, justified by the flush never needing the UI thread; `ExitFlushTests` proves it.

**Q-IPC-11. Debug call logging volume.** One Debug line per service call may be noisy at Debug level during a long report session. Recommended default: keep at Debug (off in release unless the user enables verbose logging, 10); snapshot reads never log.

**Q-IPC-12. `ShotAIException` across all specs.** Several specs name BCL exception types for user-facing messages. Recommended default: PLAN.md adds a foundation task that introduces `ShotAI.Core.Errors` first, and each subsystem PR derives its user-text exceptions from it; `UserMessageTests` plus each spec's message tests catch misses. Decided in WP-A1: default adopted; `ShotAI.Core.Errors` (`ShotAIException`, `UserMessage.From`, `UserMessage.Generic`) and `Errors/UserMessageTests` are in place.

**Q-IPC-13. Clearing recents on a projects-folder change.** macOS clears; Electron does not. Recommended default: Electron parity for 2.0.0; raise a cross-platform issue.

**Q-IPC-14. Where `IExternalLinks` is implemented.** 08 assigns "the base openExternal allowlist and opener" to 10; this spec fixes the algorithm because it lives in `src/main/ipc.ts`. Recommended default: the algorithm and tests here are normative; 10 registers `ExternalLinks` and may add settings-related links but must not change the allowlist. Decided in WP-A19b: the default. `ExternalLinks` is Core's (`ShotAI.Core.Links`), with this algorithm, and `AddShotAICore` registers it.

**Q-IPC-15. Pause and Resume on the UI thread.** 03 routes them through `Task.Run`; they are short. Recommended default: keep 03's `Task.Run`, since the engine lock can be held by a running job's short critical sections and T9 forbids UI waits on engine locks. Decided in WP-B7: `Task.Run` (spec 03 `CapturePillViewModelTests.PauseAndResumeRunOffTheUiThread`).

**Q-IPC-16. Risk: image decoding moves into the privileged process.** Electron decoded screenshots and imported images in a sandboxed renderer ("the one OS-level containment for the renderer, which decodes attacker-influenceable image pixels", `src/main/main.ts:131-140`). Natively, WIC decodes them inside `shotAI.exe` with the user's full rights, including images from imported packages (untrusted input, 09). Resolved by R-ARCH-21: accept with mitigations. Decode only after the magic-byte check (PNG `89 50 4E 47`, JPEG `FF D8 FF`), then create the decoder explicitly with WIC `IWICImagingFactory::CreateDecoder(GUID_ContainerFormatPng or GUID_ContainerFormatJpeg, ...)` and `Initialize` it on the stream, never `CreateDecoderFromStream` or any other content-sniffing factory, so a third-party codec installed on the machine is never invoked; undecodable input fails closed. There is one WIC COM decode path shared by capture, editor, flatten, report and export; neither WPF's `BitmapDecoder` classes nor WinRT's `BitmapDecoder` is used (ARCHITECTURE 3.5, 9.1 item 2, D-ARCH-3). Windows Update supplies WIC fixes. 01, 04, 05 and 09 follow the same rule. Built in WP-A17: 05's report decoder and size probe follow it (05 7.11) through Platform's `WicDecoding`, which also refuses a decoder whose vendor is not `GUID_VendorMicrosoft` (the vendor the built-in decoders report).

**Q-IPC-17. Risk: a subscriber that forgets to marshal.** A view model handler that touches an `ObservableObject` from an engine thread throws a cross-thread exception or corrupts bindings. Recommended default: T6 plus `ViewModelAffinityTests`; in debug builds `ViewModelBase.OnPropertyChanged` calls `Dispatcher.VerifyAccess()`.

**Q-IPC-18. Risk: a singleton keeps a closed view alive.** An unremoved event handler roots the view model and its view. Recommended default: `SubscriberDisposalTests`; consider WPF `WeakEventManager` only if a leak is found in practice (it hides lifetime bugs).

**Q-IPC-19. Channel map maintenance before cutover.** Electron is feature-frozen during the port, but a hotfix could add a channel. Recommended default: `MatchesElectronWhileItExists` fails the Linux CI job when `src/shared/ipc.ts` gains or loses a channel, forcing the map (and this spec) to be updated in the same PR. Decided in WP-A1: default adopted; `ChannelInventoryTests.MatchesElectronWhileItExists` runs in every Core test run.

**Q-IPC-20. The `Dispatcher` priority for events.** `DispatcherPriority.Normal` (9) is ABOVE `DataBind` (8), `Render` (7), `Loaded` (6) and `Input` (5) (Microsoft Learn, `DispatcherPriority` enum), so every queued `Normal` item runs before the next layout, render or input pass: a long burst of posted capture events delays the pill's repaint and input until the queue drains. It is also the priority `await` continuations and `Progress<T>` use through `DispatcherSynchronizationContext`, so a lower priority for events alone would break the FIFO relation between events and awaited results. Recommended default: `Normal` for everything (FIFO is the invariant), and keep each posted action short: state subscribers already re-read `GetState()` (T7), so a subscriber MAY coalesce by skipping a post while one of its own is still queued (a per-subscriber `_pending` flag), which bounds the queue to one item per subscriber for `StateChanged`; `StepLanded` and `CaptureFailed` are never coalesced. Measure during Phase B with a 20-clicks-in-5-seconds script. As built in WP-B7: `Normal` for every event; spec 03's `RecordingVisibilityController` posts one short action per event, which re-reads `GetState()`, and coalesces none yet. The coalescing and the measurement stay with WP-B9, which owns this question.

**Q-IPC-21. `VSTHRD200` against `Apply` and `ApplyDurable`.** 01 fixes `ProjectSession.Apply` and `ApplyDurable`, which return `Task<ProjectManifest>` without the `Async` suffix, and 03, 05 and 07 call them by those names; `VSTHRD200` as an error rejects them. Recommended default: keep the names (every other spec cites them) and suppress `VSTHRD200` with `[SuppressMessage("Usage", "VSTHRD200", Justification = "Name fixed by spec 01 7.10")]` on exactly those two members of `IProjectSession` and `ProjectSession`; the alternative (rename to `ApplyAsync`, `ApplyDurableAsync`) needs a coordinated edit of 01, 03, 05 and 07.

**Q-IPC-22. Re-reading `GetState()` can run ahead of `StepLanded`.** The engine updates its step count before raising `StepLanded` then `StateChanged`; a `StateChanged` handler that re-reads `GetState()` at processing time (T7) can therefore see the count of a later step whose `StepLanded` post is still queued, so a panel that shows both a count and a list can show the count one ahead for one dispatcher turn. Electron applied the payloads in order and could not show this. Recommended default: the pill uses only the count (a lead is harmless, the flash fires on any increase); the recording panel shows `max(state.StepCount, landedSteps.Count)` only after both have been applied, or shows the list length; `EventOrderTests` asserts the count never decreases, not that it equals the list length at every turn.

**Q-IPC-23. `StartAsync` while a one-shot screenshot holds the session.** Electron returns the current (recording) state for the same project and starts nothing (EDGE-IPC-44). Recommended default: 02 decides; parity is harmless because the UI cannot reach it, and a native `StartAsync` that throws `A recording is already in progress` when the existing session is a one-shot would be the safer IMPROVEMENT if 02 prefers it. Decided in WP-B2: the IMPROVEMENT. `StartAsync` throws `A recording is already in progress` for any project while a screenshot holds its session (02 D21, `StartDuringScreenshotThrows`).
