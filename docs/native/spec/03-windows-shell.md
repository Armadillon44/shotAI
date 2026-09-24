# 03 Windows, app lifecycle, capture pill, area overlay and app menu

> Spec for the native rewrite. Sources read: `src/main/main.ts` (595 lines), `src/main/RegionService.ts` (151), `src/renderer/overlay/App.tsx` (91), `src/renderer/overlay/main.tsx` (15), `src/renderer/overlay/overlay.css` (75), `src/renderer/toolbar/App.tsx` (180), `src/renderer/toolbar/main.tsx` (15), `src/renderer/toolbar/toolbar.css` (310), `src/main/menu.ts` (271), `src/main/gpu-policy.ts` (58), `src/main/arp-icon.ts` (102), `src/main/paths.ts` (44), `index.html` (12), `toolbar.html` (12), `overlay.html` (12); tests `src/main/gpu-policy.test.ts` (65), `src/main/arp-icon.test.ts` (119). Context read: `src/main/ipc.ts` (region and settings handlers, `:650-663`, `:943-959`), `src/main/CaptureController.ts` (`getState`, `discard`, `createCaptureController`, `:629-660`, `:955-990`, `:1466-1485`), `src/shared/doc-scale.ts` (`:18-165`), `src/shared/theme-palette.ts` (`:240-311`), `src/main/settings.ts` (`:268-278`), `src/main/logger.ts`, `src/renderer/project/App.tsx` (`:170-180`, `:220-256`, `:340-431`), `node_modules/electron-squirrel-startup/index.js` (electron-squirrel-startup 1.0.1), the Electron 42.5.0 role table (`lib/browser/api/menu-item-roles.ts`, read from the shipped binary), `docs/native/spec/02-capture.md`, `docs/NATIVE-WINDOWS-FEASIBILITY.md`, `dotnet/README.md` and the scaffold under `dotnet/src`. Git history of every source file (commits `38908cd`, `77adda3`, `772e381`, `701d4aa`, `62b4b7d`, `ec76adb`, `f24b3dc`, `53045c6`, `d79bc3b`, `037858d`, `58131a8`, `c3f7707`; history before `9da70df` is squashed into that import commit). macOS: `shotAI/shotAIApp.swift` (247), `shotAI/Capture/CapturePill.swift` (334), `shotAI/Capture/AreaSelect.swift` (250), `shotAI/Capture/RecordSheet.swift` (154), `shotAI/Capture/ImmediateCaptureSheet.swift` (117), `shotAI/ContentView.swift` (468), `shotAI/UpdateBadge.swift` (117), and for context `shotAI/Capture/CaptureCoordinator.swift` (`:130-331`). Verification pass also read: every source above end to end again, `src/main/settings.ts:265-284`, `src/main/remote-visibility.ts:1-80`, `src/main/CaptureController.ts:625-660`, `:735-792`, `:920-991`, `:1466-1485`, `src/main/ipc.ts:650-665`, `:943-960`, `src/shared/ipc.ts:31-34`, `:207-277`, `package.json:1-5`, the Electron 42.5.0 role table in `node_modules/electron/dist/electron` (`roleList`), the commit bodies of `772e381` and `d79bc3b`, the scaffold (`dotnet/src/ShotAI.App/*`, `dotnet/src/ShotAI.Platform/*`, `dotnet/Directory.Packages.props`), specs 01, 02, 05, 06, 09, 10, 11 and 12 (the sections that name 03), and Microsoft Learn (`WindowStartupLocation`, `Window.ShowActivated`, `SystemParameters.ClientAreaAnimation`, `SPI_GETCLIENTAREAANIMATION`). Status: extracted and verified. Consolidated with ARCHITECTURE.md R-ARCH-1 to R-ARCH-26 on 2026-09-23.

**Notation used in this document.**

- Several Electron strings contain U+2014 (EM DASH). This document never prints that character. Wherever it occurs in a quoted string it is written as the escape `\u2014`, and the C# literal must contain that exact character (C# accepts the same `\u2014` escape inside a string literal, so copying the quoted text into C# source is exact). Other non-ASCII characters are printed as themselves and named once: `·` U+00B7 MIDDLE DOT, `×` U+00D7 MULTIPLICATION SIGN, `…` U+2026 HORIZONTAL ELLIPSIS, `❚` U+275A HEAVY VERTICAL BAR, `▶` U+25B6, `■` U+25A0, `✕` U+2715, `⚠` U+26A0.
- `round(x)` means JavaScript `Math.round`, ported only as `ShotAI.Core.Json.JsMath.Round` (one copy, owned by spec 01 7.2.3; R-ARCH-1), whose exact definition is `f = Math.Floor(x); return (x - f >= 0.5) ? f + 1 : f;` with NaN and infinities returned unchanged (INV-CAP-18). Descriptively (not as a definition): the nearest integer, halves toward positive infinity. It is NOT C# `Math.Round` (banker's rounding, banned in all of ShotAI.Core with only `JsMath.cs` allowlisted, ARCHITECTURE 14.9), NOT `MidpointRounding.AwayFromZero` (wrong for negative halves), and NOT the expression `Math.Floor(x + 0.5)` (wrong at `0.49999999999999994`: it returns 1, JavaScript returns 0).
- "DIP" is a device-independent pixel (1/96 inch; Electron's "DIP" and WPF's "device-independent unit" are the same unit). A CSS px inside an Electron window equals one DIP at zoom 100%. "px" or "physical px" is a physical screen pixel in the global virtual-desktop space (per-monitor DPI aware v2). `s` is a monitor's scale factor, `dpi / 96`.
- CSS `rem` is 16 DIP (Chromium default font size; the app never changes it), so `0.72rem` is 11.52 DIP.
- Spec numbers used below (verified against the files in `docs/native/spec/`): 01 model and store, 02 capture engine, 03 this spec, 04 editor, 05 report, 06 home and settings UI, 07 SOP generation, 08 Entra sign-in, secrets and policy, 09 exports (PDF host, fonts), 10 brand contract, settings, logging, update check and self-tests (it owns the brand palette, `BrandPalette.PinnedBrand` and the rest of the brand narrowing), 11 service boundary and threading (`IUiDispatcher`, the composition root and the exit order), 12 packaging and installer (the per-machine MSI, `Program.Main`, `LegacyInstanceGuard`). An earlier draft of this spec assumed brand and theme was 08; every such reference now points to 10.

---

## 1. Scope

### 1.1 Owned by this subsystem

| Area | What |
|---|---|
| Process lifecycle | entry point, startup order, single-instance lock and second-instance activation, quit when the main window closes, window-all-closed, session end, shutdown teardown ordering, crash and gone logging |
| Main project window | creation parameters, initial placement, the list (narrow) and detail (report) widths, grow-only resize on document-scale commits, centering and clamping to the work area, hide while recording and restore after, hide during area selection and restore after, second-instance surfacing |
| Capture pill | the always-on-top recording HUD: window style, size, docking, drag, both rows, every state, the hint, the confirmation flash, the in-session error surface and its clear rules, the Discard confirmation dialog, non-activation |
| Area-select overlay | one overlay per display, drag, confirm, cancel, dimming, size badge, DIP to physical conversion, single resolution |
| Recording visibility | the handler for the capture engine's `RecordingChanged(recording, showPill)` event (spec 02, 2.13) |
| Content-protection ordering | every own window created excluded from capture, relaxed only after the remote-visibility setting loads; windows created later seeded from the current effective state |
| Application menu | every item, accelerator and enable rule, View then Brand state rules, the About dialog |
| Electron-only machinery | what disappears (custom protocol, CSP, navigation confinement, OS sandbox switches, GPU policy, Squirrel and ARP icon fix, preload, load diagnostics, HTML entry pages) and what replaces each intent |
| Runtime diagnostics | the startup runtime line, the ARM64 emulation check (as a diagnostic only), the render tier line |

### 1.2 Not owned (and who owns it)

| Not owned | Owner |
|---|---|
| The capture session state machine, `CaptureState`, triggers, grabs, the shield (`CaptureShield`), `OwnWindowRegistry`, `DisplayAffinityProtection`, `HIDE_SETTLE_MS` | 02 |
| `setProjectTheme` write rules (null clears the key, a brand pins it, raw no-op comparison), the store write queue, `resolveProjectFile` confinement | 01 |
| The report view, the document scale slider, `committedScale`, the "+ Capture" and "+ Screenshot" flows, `CaptureInsertModal` | 05 |
| The home list, the capture-mode chooser (it calls area selection), Settings, the in-window `Capture error: ` banner, the recording panel inside the main window, the import flow started by File then Import Project | 06 |
| `BRANDS`, `BRAND_IDS`, `DEFAULT_BRAND`, `pinnedBrand`, `coerceBrand`, `pinIsUnrecognised` (Core `BrandPalette` generated from `contract/brand.json`) | 10 |
| The `SetProjectThemeOperation` the Brand menu applies to the open project's session | 05 (session), 01 (write rule) |
| `LegacyInstanceGuard` (refuse to run beside an Electron 1.x instance), `PersonalCopyGuard` (a personal, per-user copy hands off to the copy installed for all users), `Program.Main` and `SetDefaultDllDirectories` | 12 (this spec only gives them their slot in the startup order) |
| The PDF WebView2 host (hidden window, JavaScript off, navigation blocked), `brandFontPath` consumers | 09 |
| `settings.json` (including `archiveAgeDays` and its clamp), `remoteVisible` and its synchronous cache, logging sinks and rotation, the update check and pending-update stash, self-test switches | 10 |
| Auto-archive selection and archiving (`autoArchiveStale`, INV-MODEL-32, `IProjectService.AutoArchiveStaleAsync` raising `ProjectsChanged`, spec 11 Q-IPC-6) | 01 (this spec owns only the startup trigger, 7.4.1 step 13, and AC-SHELL-33) |
| UI-thread marshaling rules and the service interfaces the UI calls | 11 |
| `IAppInfo` and `AppInfoProvider` (the About dialog's version, .NET, WebView2 and architecture values), and the `IWebView2RuntimeInfo` seam whose Platform implementation sits in `ShotAI.Platform.Export` (R-ARCH-12) | 11 |
| Removing `ANTHROPIC_CUSTOM_HEADERS` and the proxy variables from the environment (INV-AUTH-35, Q-ARCH-3) | 08 and ARCHITECTURE 4.2 (this spec only gives it its slot, 7.4.1 step 5) |
| Installer, ARP entry and icon, Start menu shortcut, code signing, Desktop Runtime prerequisite | 12 |

### 1.3 Specs this one touches

02 (events it handles, windows it registers), 05 and 06 (callers of `SetDetailView` and `SelectAreaAsync`, sources of the open-project and brand state), 09 (the only remaining web engine), 10 (brand catalog, settings load order, logging, diagnostics, update check), 11 (threading, composition root, exit order), 12 (what replaces Squirrel, `Program.Main`, `LegacyInstanceGuard`).

---

## 2. Reference behavior (Electron)

### 2.1 Process entry and startup sequence

Everything below runs in the Electron main process, top to bottom of `src/main/main.ts`.

| # | Step | Citation | Class |
|---|---|---|---|
| 1 | `initLogging()`: file `userData/logs/shotai.log`, 5 MB rotation, console and file level `debug` in dev and `info` packaged, `errorHandler.startCatching({ showDialog: false })`, log `shotAI starting \u2014 <platform>/<arch> · electron <v> · packaged=<bool>` and `logs: <path>` | `src/main/main.ts:29`, `src/main/logger.ts:14-39` | REQUIRED (logging content owned by 10) |
| 2 | Register the `shot` scheme as privileged (`standard`, `secure`, `supportFetchAPI`, `stream`, `corsEnabled`) before `ready` | `src/main/main.ts:36-52` | ELECTRON-ONLY (2.10) |
| 3 | GPU decision (`decideGpu(process.env, process.platform, process.arch)`, fail toward GPU on); if disabled: `app.disableHardwareAcceleration()` plus switches `disable-gpu`, `disable-gpu-compositing`, `disable-software-rasterizer`, `in-process-gpu`; log either `GPU disabled \u2014 software rendering [<reason>] (SHOTAI_ENABLE_GPU=1 to force on)` or `GPU enabled [<reason>] (SHOTAI_ENABLE_GPU=0 to force off)` | `src/main/main.ts:114-129` | ELECTRON-ONLY (2.10.5) |
| 4 | `SHOTAI_NO_SANDBOX === '1'` appends `no-sandbox` and warns `OS sandbox DISABLED (SHOTAI_NO_SANDBOX=1) \u2014 dev/VM workaround only` | `src/main/main.ts:137-140` | ELECTRON-ONLY |
| 5 | Squirrel gate: if `electron-squirrel-startup` handled a lifecycle argument, run `fixArpIconOnSquirrelEvent(argv, execPath, <resources>\shotAI_icon.ico, log)` and `app.quit()` | `src/main/main.ts:143-153` | ELECTRON-ONLY (2.10.6) |
| 6 | `else if (!app.requestSingleInstanceLock())`: log `another instance already holds the lock \u2014 exiting.` and `app.quit()` | `src/main/main.ts:154-169` | REQUIRED (2.2) |
| 7 | `else`: register `second-instance` (surface the main window) | `src/main/main.ts:170-181` | REQUIRED (2.2) |
| 8 | `whenReady`: diagnostic GPU line after `getGPUInfo('complete')`: `GPU: compositing=<s>, webgl=<s>, 2d_canvas=<s> \| renderer=<glRenderer or ?>`; failure warns `GPU info query failed:` | `src/main/main.ts:370-380` | ELECTRON-ONLY |
| 9 | Deny `window.open` and confine navigation for every web contents | `src/main/main.ts:387-401` | ELECTRON-ONLY (2.10.3) |
| 10 | `SHOTAI_SELFTEST === '1'`: run `runSelfTest()`, quit. `SHOTAI_CAPTURE_TEST === '1'`: run `runCaptureTest()`, quit. Both before any window exists | `src/main/main.ts:403-414` | REQUIRED as switches (10, and 02 2.16) |
| 11 | `void getCaptureScale()` primes the screenshot-quality cache | `src/main/main.ts:415` | REQUIRED (10, 02) |
| 12 | Create the capture controller with the `onRecordingChange` handler (2.6); it also hooks `before-quit` to `controller.teardown()` and `globalShortcut.unregisterAll()` | `src/main/main.ts:416-449`, `src/main/CaptureController.ts:1468-1485` | REQUIRED |
| 13 | `new RegionService(preloadPath)` | `src/main/main.ts:450` | REQUIRED (2.5) |
| 14 | `registerShotProtocol()`, `applyContentSecurityPolicy()`, `registerIpcHandlers(capture, region)` | `src/main/main.ts:451-453` | ELECTRON-ONLY |
| 15 | IPC `view:set-detail` (2.3.4) and `view:set-brand-menu` (2.8.3) | `src/main/main.ts:456-485` | REQUIRED behavior, ELECTRON-ONLY transport |
| 16 | `armBrandMenu(ref)` BEFORE `installAppMenu(ref)` | `src/main/main.ts:486-493` | REQUIRED intent (EDGE-SHELL-11) |
| 17 | `createWindows()`: log `runtime: <platform>/<arch> · electron <v> · chrome <v>`, create the project window (shown on `ready-to-show`), create the pill (hidden) | `src/main/main.ts:354-360`, `494` | REQUIRED |
| 18 | `getRemoteVisible().then(applyRemoteVisibility)`: relax protection only after the async setting load. `.catch(() => undefined)`: a failed load is swallowed silently and every window stays protected (fail closed; no log line). `getRemoteVisible` also fills the synchronous `remoteVisibleNow()` cache, which is `false` (protected) until it resolves (`src/main/settings.ts:265-275`) | `src/main/main.ts:495-502` | REQUIRED [SECURITY] (2.7) |
| 19 | Background: `autoArchiveStale(await getArchiveAgeDays())`; if `archived > 0`, send `projects:changed` to the main window; failure warns `startup auto-archive failed (non-fatal):` | `src/main/main.ts:504-515` | REQUIRED (01 owns the logic, INV-MODEL-32; this spec owns the trigger, AC-SHELL-33) |
| 20 | Background: daily update check; stash then push `update:available`; failures logged only | `src/main/main.ts:517-558` | REQUIRED (10 owns the logic) |

Placement quirk: steps 8 to 20 are registered with `app.whenReady().then(...)` at module level (`src/main/main.ts:363`), OUTSIDE the `else` branch that won the lock, and the crash listeners and `window-all-closed` (`:566-584`) are registered unconditionally too. A losing second instance (step 6) and a Squirrel lifecycle launch (step 5) call `app.quit()` before `ready`. Whether Electron 42 still emits `ready` after an early `app.quit()` (which would run the self-tests, auto-archive, the update check and window creation in the losing process for a moment) is UNVERIFIED; no log from a second launch was available. EDGE-SHELL-45. Natively the losing branch returns from `OnStartup` before anything else is constructed (INV-SHELL-5).

### 2.2 Single instance and second launch

| State | Event | Guard | Action | Next |
|---|---|---|---|---|
| Launching | process start | argv carries `--squirrel-install`, `--squirrel-updated`, `--squirrel-uninstall` or `--squirrel-obsolete` (as `argv[1]`, see 2.10.6) | ARP icon fix (install and updated only), spawn `Update.exe`, quit. The lock is NOT requested | Exited |
| Launching | process start | `argv[1]` is `--squirrel-firstrun` (or any other argument) | `electron-squirrel-startup` returns `false` (it handles only the four commands above), so the launch is ordinary and contends for the lock like any other | next rows |
| Launching | process start | lock already held by another process | log `another instance already holds the lock \u2014 exiting.`; `app.quit()`; no window is created, no store is touched | Exited |
| Launching | process start | lock acquired | continue startup (2.1) | Running |
| Running | `second-instance` (another launch attempted) | `projectWindow` exists and is not destroyed | `if (isMinimized()) restore()`; `show()`; `focus()` | Running |
| Running | `second-instance` | no project window | nothing | Running |

Citations: `src/main/main.ts:143-181`, `node_modules/electron-squirrel-startup/index.js:14-36`. Two different argv readers exist: `electron-squirrel-startup` looks only at `process.argv[1]`, while `squirrelCommand` (`src/main/arp-icon.ts:38-40`) finds the first argument starting with `--squirrel-` anywhere. The ARP fix therefore runs only when both agree (the gate is `started`). The second launch's command line and working directory are ignored. The surfacing runs even while recording (the main window then appears over the capture; EDGE-SHELL-31).

Why a lock (`src/main/main.ts:161-167`, commit `d79bc3b`): "two instances share one projects directory and one userData. ProjectStore's writeQueue serializes manifest writes WITHIN a process and cannot see another one, and the Entra token cache (#63) is a hand-rolled safeStorage ICachePlugin with no cross-process lock". "A torn cache write self-heals into one interactive sign-in; a torn manifest write does not." Natively the MSAL extension cache has its own cross-process lock, but the store argument stands, so the lock is REQUIRED.

Why the Squirrel gate comes first (`src/main/main.ts:155-159`): a lifecycle invocation "must NOT be gated on the lock, or it would lose the race against an already-running app and skip fixArpIconOnSquirrelEvent above". ELECTRON-ONLY natively (EDGE-SHELL-1).

### 2.3 Main project window

#### 2.3.1 Creation

| Property | Value | Citation |
|---|---|---|
| width, height | 720, 740 DIP | `src/main/main.ts:235`, `265-266` |
| minWidth, minHeight | 680, 560 DIP | `:267-268` |
| position | not given, so Electron centers the window | `:264-278` |
| title | `shotAI` (the page `<title>` is also `shotAI` and is never changed at runtime) | `:270`, `index.html:6` |
| icon | `appIconPath()`: `<resources>\shotAI_icon.png` if it exists, else `<appPath>\assets\shotAI_icon.png` | `:271`, `src/main/paths.ts:38-44` |
| show | `false`, shown on `ready-to-show` | `:269`, `:302` |
| frame | default (standard Windows frame with the application menu bar) | `:264-278` |
| content protection | `setContentProtection(true)` immediately after construction, before load | `:280` |
| web preferences | preload, `contextIsolation: true`, `nodeIntegration: false`, `sandbox: true` | `:272-277` (ELECTRON-ONLY) |
| on `closed` | `projectWindow = null`; destroy the pill if it exists (2.9) | `:283-292` |

There is no persistence of the window position or size across launches. REQUIRED (parity): the native app does not persist it either (Q-SHELL-15).

#### 2.3.2 List and detail widths

The renderer calls `window.shotai.setDetailView(!!openPath, committedScale)` from an effect keyed on `[openPath, committedScale]` (`src/renderer/project/App.tsx:340-350`), so it runs on startup (open = false), on entering and leaving a project, and on every committed scale change. The IPC handler coerces: `open === true`, `scale` if `typeof scale === 'number'` else `1` (`src/main/main.ts:456-458`). `NaN` and the infinities pass the `typeof` test; `clampScale` then maps any non-finite value to 1, so they behave exactly like scale 1. The `setDetailView` default parameter `scale = 1` is never used (the handler always passes a number).

`detailWindowWidth(scale, workAreaWidth)` (`src/shared/doc-scale.ts:44-56`, `128-146`, `158-165`), exactly:

```
clampScale(v):  n = (v is number) ? v : NaN
                if (!isFinite(n)) return 1
                pct = round(n * 100); pct = min(125, max(65, pct)); pct = round(pct / 5) * 5
                return pct / 100
htmlCol(s)    = round(816 * clampScale(s))
repFrame(s)   = htmlCol(s) + 32 * 2
want          = repFrame(s) + 130                      // 130 = 1010 - 880
usable        = (isFinite(W) && W > 0) ? floor(W) : want
detailWindowWidth(s, W) = max(1010, min(want, usable))
```

Worked values: scale 1 gives 1010; 1.25 gives `round(1020) + 64 + 130 = 1214`; 0.65 gives `max(1010, 530 + 64 + 130 = 724) = 1010`; scale 1.25 on a 1100 DIP work area gives 1100; any scale on a 900 DIP work area gives 1010 (wider than the work area; EDGE-SHELL-34).

`setDetailView(open, scale)` (`src/main/main.ts:239-261`), exactly:

```
if (!win || win.isDestroyed()) return
if (open && win.isMaximized()) return                      // #70 review fix
b      = win.getBounds()                                   // DIP, outer window rect
wa0    = screen.getDisplayMatching(b).workArea
target = detailWindowWidth(scale, wa0.width)
newW   = open ? max(b.width, target) : 720                 // GROW-ONLY while open
if (b.width === newW) return
centerX = b.x + b.width / 2
wa      = screen.getDisplayMatching(b).workArea
x       = max(wa.x, min(round(centerX - newW / 2), wa.x + wa.width - newW))
win.setBounds({ x, y: b.y, width: newW, height: b.height })
```

`getDisplayMatching` picks the display with the greatest intersection with `b`. `wa0` and `wa` are the same query made twice on the same unchanged `b`, so they are always equal; natively one query is enough. `y` and `height` never change. There is no clamp of `newW` itself, and no vertical clamp. REQUIRED, with the divergences in 7.12 (maximized or full screen on leave, D6).

#### 2.3.3 Hide and restore

| Trigger | Hide | Restore | Citation |
|---|---|---|---|
| Recording starts (`onRecordingChange(true, ...)`) | `projectWindow.hide()`, unconditional | on `onRecordingChange(false)`: `show()` then `focus()` | `src/main/main.ts:426-448` |
| Area selection (`region:select-area`) | the requesting window (`BrowserWindow.fromWebContents(event.sender)`, in practice the main window) `hide()` | in `finally`, if not destroyed: `show()` then `focus()` | `src/main/ipc.ts:943-959` |
| No-click screenshot | hidden through `onRecordingChange(true, { pill: false })` | restored through `onRecordingChange(false)` in the engine's `finally` | `src/main/CaptureController.ts:876`, `:893` |
| Legacy single-shot (`captureSingle`) | hidden through `onRecordingChange(true)`, pill shown exactly as for a recording | restored by the `stop()` the single-shot task fires | `src/main/CaptureController.ts:789`; spec 02 2.2.4. Unreachable from the UI and not ported (spec 02 Q-CAP-1): ELECTRON-ONLY |

While hidden the window has no taskbar button, and the pill is `skipTaskbar`, so shotAI has no taskbar presence during a recording. REQUIRED.

The "Keep shotAI visible during capture" toggle and `forceHide` were removed (`f24b3dc`); the hide is unconditional; a leftover `captureNoHide` key in `settings.json` is ignored (EDGE-SHELL-18). REQUIRED.

### 2.4 Capture pill

#### 2.4.1 Window

| Property | Value | Citation |
|---|---|---|
| size | 380 x 74 DIP ("Two rows while recording") | `src/main/main.ts:313-317` |
| frame | `false` (frameless) | `:319` |
| resizable, maximizable, fullscreenable | `false`, `false`, `false` | `:320-322` |
| alwaysOnTop | `true` (default level, which is `HWND_TOPMOST` on Windows) | `:323` |
| skipTaskbar | `true` | `:324` |
| title | `shotAI \u2014 Capture` (also the page title) | `:325`, `toolbar.html:6` |
| show | `false`; created once at startup, shown only while recording | `:318`, `:350`, `:359` |
| transparent | not set: an opaque rectangle; `.toolbar` paints `#1f2330` over the whole client area | `toolbar.css:34-46` |
| content protection | `setContentProtection(true)` immediately | `:334` |
| `toolbarPositioned` | reset to `false` whenever a pill window is created | `:337` |
| on `closed` | `toolbarWindow = null` | `:338-340` |
| activation | Electron's `show()` activates the window: the pill takes the foreground when a recording starts, and clicking it activates it | `:441` |

The macOS pill is non-activating (`NSPanel` with `.nonactivatingPanel`, `macOS:shotAI/Capture/CapturePill.swift:5-9`, `:67-79`), described there as "a deliberate UX improvement over the Windows pill, which activates on click". The native Windows pill is non-activating too: IMPROVEMENT (7.6.2, INV-SHELL-6).

#### 2.4.2 Docking

`dockToolbarTopCenter(win)` (`src/main/main.ts:195-208`), best effort (any throw is swallowed):

```
display = (projectWindow exists and not destroyed) ? screen.getDisplayMatching(projectWindow.getBounds())
                                                   : screen.getPrimaryDisplay()
area    = display.workArea                                  // DIP
width   = win.getBounds().width                             // 380
x       = round(area.x + (area.width - width) / 2)
y       = area.y + 8
win.setPosition(x, y, false)
```

Called only when a recording shows the pill and `toolbarPositioned` is `false`; then `toolbarPositioned = true` (`:437-440`). Because the pill is created once per run, it docks once per app run; afterwards the user's drag position is kept for every later session ("don't re-dock on resume", `:189-191`). The main window is hidden BEFORE docking, but `getBounds()` of a hidden window still reports its last bounds, so the pill docks on the monitor the main window was on. REQUIRED.

#### 2.4.3 Drag

Drag is `-webkit-app-region: drag` (the OS moves the window). Regions:

| Element | Drag | Citation |
|---|---|---|
| `.toolbar__drag` (grip plus status label, row 1 left, `flex: 1`) | drag | `toolbar.css:177-186` |
| `.toolbar__controls` (Pause or Resume, Stop, divider, Discard) | no-drag | `toolbar.css:205-212` |
| `.toolbar__hint` (row 2 hint) | drag | `toolbar.css:57-65` |
| `.toolbar__err` (row 2 error row) | drag | `toolbar.css:76-86` |
| `.toolbar__err-glyph`, `.toolbar__err-msg` | no-drag, `cursor: help`, so the hover tooltip works | `toolbar.css:88-106` |
| `.toolbar__err-dismiss` | no-drag | `toolbar.css:111-127` |
| padding and gaps of `.toolbar` outside those elements | not a drag region (default) | `toolbar.css:34-46` |

No position clamp: the pill can be dragged partly or wholly off screen. The `.toolbar__drag` element carries the tooltip `Drag to move` (`src/renderer/toolbar/App.tsx:82`). REQUIRED (drag areas and tooltip), with EDGE-SHELL-25 (off-screen) as IMPROVEMENT.

#### 2.4.4 State and rendering

Inputs: the `CaptureState` from `capture.getState()` on mount and every `capture:state-changed` push (`src/renderer/toolbar/App.tsx:8-12`), and every `capture:error` push (`:30-38`). The mount-time `getState()` and the subscription race: a push that lands before the `getState()` reply is overwritten by the older reply. Harmless in practice (the pill is created at startup, long before any session), and natively moot because every state event re-reads `GetState()` when it is processed (spec 11 T7). The Pause, Resume and Stop buttons also apply the state their own call returns, and Discard applies `r.state`; every call ends in `.catch(ignore)`. Derived: `status = state?.status ?? 'idle'`, `count = state?.stepCount ?? 0`, `active = status === 'recording' || status === 'paused'` (`:14-16`).

| status | Root classes | Row 1 left (label) | Rec dot | Row 1 right (controls) | Row 2 |
|---|---|---|---|---|---|
| `idle` | `toolbar toolbar--idle` | `shotAI` | none | none | none |
| `recording` | `toolbar toolbar--recording` (+ `toolbar--err` when showing an error) | `Capturing · <count>` | green `#34d399`, pulsing | `❚❚ Pause`, `■ Stop`, divider, `✕` | error row if `showError`, else hint `Click anything to capture a step · Ctrl+Shift+S` |
| `paused` | `toolbar toolbar--paused` (+ `toolbar--err`) | `Paused · <count>`, amber `#fcd34d` | amber `#fcd34d`, static | `▶ Resume`, `■ Stop`, divider, `✕` | error row if `showError`, else hint `Paused \u2014 press Resume to keep capturing` in amber `#fcd34d` |

Citations: `src/renderer/toolbar/App.tsx:80-175`, `toolbar.css:67-69`, `273-304`. Idle is reachable only if the pill is shown while idle, which the shell never does (2.6), but the rendering is defined. REQUIRED.

Control actions (`:58-77`, `:95-131`):

| Control | Text | Tooltip (`title`) | Accessible name | Action |
|---|---|---|---|---|
| Pause (recording only) | `❚❚ Pause` | `Pause` | from text | `capture.pause().then(setState)`, errors ignored |
| Resume (paused only) | `▶ Resume` | `Resume` | from text | `capture.resume().then(setState)` |
| Stop | `■ Stop` | `Stop & finish` (the JSX source writes `Stop &amp; finish`; JSX decodes the entity) | from text | `capture.stop().then(setState)` |
| Discard | `✕` | `Discard this capture` | `Discard this capture` | confirmation (2.4.7), then `capture.discard().then(r => setState(r.state))` |
| Error dismiss | `Dismiss` | `Dismiss this error` | `Dismiss this capture error` | `setError(null)` |

The count shown is `CaptureState.stepCount`, which Electron defines as the filename counter (spec 02, EDGE-CAP-49). Spec 02 D4 redefines it natively as steps committed this session; the pill displays whatever the engine reports. REQUIRED.

#### 2.4.5 Error surface state machine

Variables: `error: string | null` (initially `null`), `prevCount` (a ref, initially the first rendered `count`, which is 0). `showError = active && error !== null` (`src/renderer/toolbar/App.tsx:54`).

| State | Event | Guard | Action | Next |
|---|---|---|---|---|
| any | `capture:error(msg)` | `msg && msg.trim()` is truthy | `error = msg` (untrimmed) | Error set |
| any | `capture:error(msg)` | message empty or whitespace | `error = 'A capture failed \u2014 see the log for details.'` | Error set |
| Error set | state push with `active && count > prevCount` | | `error = null`, replay flash | Clear |
| any | `count` or `active` changes (the effect is keyed on `[count, active]`) | | `prevCount = count` (after the check) | same |
| Error set | `active` becomes `false` | | `error = null` | Clear |
| Error set | Dismiss click | | `error = null` | Clear |
| Error set | new `capture:error` | | replaced by the newer message | Error set |

Citations: `:29-51`, `:164`. Display: the error takes row 2 in place of the hint ("a failure outranks guidance, and row 1 has no room for the message beside the controls", `:136-139`); glyph `⚠` (aria-hidden) and the message both carry the full message as a tooltip because the row ellipsizes at 380 DIP (`:143-155`); the row has `role="alert"` (`:142`); the top accent bar turns red (`toolbar.css:292-295`). REQUIRED. Origin: `c3f7707` (#52), macOS parity.

Quirk: an error that arrives while `active` is already `false` stays in `error` (the `[active]` effect does not re-run) and would show at the start of the next session until a step lands (EDGE-SHELL-22). Natively the error is cleared at session start (IMPROVEMENT, as macOS `CapturePill.swift:29`).

#### 2.4.6 Confirmation flash

When `active && count > prevCount`, `flashKey++`; a `<span key={flashKey} className="toolbar__flash">` remounts to replay a one-shot animation (`src/renderer/toolbar/App.tsx:18-23`, `40-46`, `177`). It covers the whole pill (`position: absolute; inset: 0`), ignores the pointer, and draws `box-shadow: inset 0 0 0 2px #34d399` animated by `tb-flash 0.7s ease-out forwards` (`toolbar.css:135-156`):

| Keyframe | opacity | inset ring width |
|---|---|---|
| 0% | 0 | 3 DIP |
| 25% | 1 | (interpolated) |
| 100% | 0 | 2 DIP |

CSS applies `animation-timing-function` to EACH keyframe interval of each property, not once over the whole run: opacity eases out (CSS `ease-out` is `cubic-bezier(0, 0, 0.58, 1)`) from 0 to 1 over 0 to 175 ms and again from 1 to 0 over 175 to 700 ms, and the ring width (keyframed only at 0% and 100%) eases out from 3 to 2 DIP over the whole 700 ms. `forwards` keeps the 100% frame (opacity 0), so the span stays mounted but invisible until the next remount. `border-radius: inherit` resolves to 0 (`.toolbar` sets no radius).

`prefers-reduced-motion: reduce` replaces it with `tb-flash-static 0.7s linear forwards`: opacity 1 from 0% to 70%, 0 at 100%, with the base rule's constant 2 DIP ring; and stops the rec-dot pulse (`toolbar.css:158-174`). The pulse `tb-pulse 1.4s ease-in-out infinite` has a single 50% keyframe (opacity 0.3), so it runs 1 to 0.3 over 700 ms and back over 700 ms, each half with `ease-in-out` (`cubic-bezier(0.42, 0, 0.58, 1)`) (`toolbar.css:281`, `306-310`). Chromium maps that media query on Windows to the system "Show animations in Windows" setting (`SPI_GETCLIENTAREAANIMATION`). REQUIRED.

Quirk: on a session that appends to a non-empty project, the pill flashes once at start, because the idle state carried `stepCount: 0` and the first recording state carries the seeded counter (`src/main/CaptureController.ts:629-646`). ELECTRON-ONLY artifact (EDGE-SHELL-22); natively the flash fires only for a step committed during the session.

#### 2.4.7 Discard confirmation

`window.confirm(message)` in the pill renderer (`src/renderer/toolbar/App.tsx:61-77`):

| `state.willDeleteProjectOnDiscard` | Message |
|---|---|
| `true` | `Discard this capture? This is a new project, so the entire project will be deleted.` |
| `false` | `Discard this capture? Steps recorded in this session will be deleted.` |

Buttons are `OK` and `Cancel`; OK proceeds. The dialog is drawn by Electron's JavaScript dialog manager as a native modal message box over the pill (its exact title, icon and default button are UNVERIFIED; no Windows run was possible), so it is a separate HWND that no `setContentProtection` call ever reached. `willDeleteProjectOnDiscard = createdThisSession && stepCountAtStart === 0 && !single` (`src/main/CaptureController.ts:648-656`). While the dialog is open the recording continues and the hook still captures clicks elsewhere (spec 02, EDGE-CAP-39). The message wording is REQUIRED; the dialog chrome is IMPROVEMENT (7.6.3).

#### 2.4.8 Visual tokens (DIP)

| Element | Rule | Citation |
|---|---|---|
| font | `'Segoe UI', -apple-system, BlinkMacSystemFont, Roboto, Helvetica, Arial, sans-serif`; `user-select: none` | `toolbar.css:25-32` |
| accent tokens | `--accent: #4f46e5`, `--accent-press: #4338ca`, `--radius: 8px` (unused by the pill) | `:3-7` |
| root | flex column, `justify-content: center`, height 100%, padding 4.8 top and bottom, 9.6 left and right, row gap 2.4, background `#1f2330`, color `#f4f5f7` | `:34-46` |
| active top bar | `box-shadow: inset 0 2px 0 #4f46e5` for recording and paused; `#ef4444` while `toolbar--err` | `:285-295` |
| row 1 | flex, `align-items: center`, gap 8 | `:49-53` |
| drag area | flex, gap 8, `flex: 1`, `min-width: 0`, height 100%, padding-left 4 | `:177-186` |
| grip | 10 x 14, dots `radial-gradient(#6b7280 1px, transparent 1px)` on a 4 x 4 tile, opacity 0.8 | `:188-194` |
| label | font 13.6, weight 600, letter-spacing 0.01em, no wrap; paused color `#fcd34d` | `:196-203`, `:297-299` |
| rec dot | 9 x 9 circle, `#34d399`, margin-right 6, `tb-pulse 1.4s ease-in-out infinite` (50%: opacity 0.3); paused `#fcd34d`, no animation | `:273-282`, `:301-310` |
| controls | flex, no wrap, `flex: none`, gap 5.6 | `:205-212` |
| button | 28 x 28, no border, radius 7, background `#2c3142`, color `#f4f5f7`, font 11.52, hover `#3a4159`, disabled opacity 0.4 | `:214-235` |
| labeled button | width auto, padding 0 9.6, gap 4.8, weight 600 | `:238-243` |
| Stop | background `#4f46e5`, hover `#4338ca` | `:247-253` |
| divider | 1 x 16, `#3a4159`, horizontal margin 1.6 | `:256-261` |
| Discard | color `#fca5a5`; hover background `#7f1d1d`, color `#fff` | `:263-270` |
| hint | font 11.52, color `#aeb4c7`, no wrap, ellipsis, padding-left 5.6 | `:57-65` |
| error row | flex, gap 5.6, `min-width: 0`, padding-left 5.6, color `#fecaca`, font 11.52, weight 600 | `:76-86` |
| error message | `min-width: 0`, ellipsis, no wrap | `:99-106` |
| Dismiss | height 18, padding 0 7.2, radius 5, background `#7f1d1d`, color `#fecaca`, font 10.56, weight 600, `margin-left: auto`; hover background `#991b1b`, color `#fff` | `:111-132` |
| focus ring | `:focus-visible` outline 2 `#4f46e5`, offset 2, radius 4 | `:13-17` |
| cursors | buttons and Dismiss `pointer`; disabled buttons `default`; glyph and message `help`; everything else the default arrow (the OS drag regions show the arrow too) | `:225`, `:234`, `:96`, `:105`, `:125` |
| hover guards | `.toolbar__btn:hover:not(:disabled)` and the Stop hover are skipped on a disabled button; the Discard hover (`.toolbar__btn--discard:hover`) has no `:disabled` guard. No button is ever disabled in Electron, so this only matters for D4 natively: a disabled Discard shows no hover | `:228`, `:251`, `:267` |

The Dismiss control is worded on purpose: "row 1's far-right control is the DESTRUCTIVE Discard ✕, ~2px above and on the same edge. A different shape can't be mistaken for it" (`src/renderer/toolbar/App.tsx:156-158`, `c3f7707`). REQUIRED.

### 2.5 Area-select overlay

#### 2.5.1 Selection service (`RegionService`)

| State | Event | Guard | Action | Next |
|---|---|---|---|---|
| Idle | `selectArea()` | | for every display in `screen.getAllDisplays()`, create an overlay (2.5.2); store `{ resolve, windows }`; debug `region: overlay opened across <n> display(s)` | Pending |
| Pending | `selectArea()` | | `teardown(null)` (the earlier promise resolves `null`, its overlays close), then create a new set | Pending (new) |
| Pending | `region:complete(rect)` | sender is one of the pending overlays | see 2.5.4; `teardown(result)` | Idle |
| Pending | `region:cancel` | sender is one of the pending overlays | debug `region: selection cancelled`; `teardown(null)` | Idle |
| Pending | `region:complete` or `region:cancel` | sender is not a pending overlay | ignored ("the same preload exposes region.* to every window") | Pending |
| Pending | an overlay emits `closed` | it is in the pending set | `teardown(null)` ("If an overlay is dismissed out from under us (e.g. app quit), don't leave selectArea() hanging") | Idle |
| Idle | any complete, cancel or closed | | ignored | Idle |

`teardown(result)`: take and clear `pending` first, `close()` every non-destroyed overlay, then resolve. Each close fires `closed`, which finds `pending` already null. The promise resolves exactly once.

Guard clauses in `finish` (`:114-140`): no pending selection returns at once; a sender that is not one of the pending overlays returns without resolving; a valid rectangle (4 or more on both sides) from an overlay that `isDestroyed()` resolves `null` WITHOUT the `region: selection cancelled` debug line (the only silent `null`); an unparseable rectangle (`parseRect` returned `null`) logs the cancel line like a real cancel.

Unhandled case: `selectArea()` stores `pending` with the overlays created from `screen.getAllDisplays()`; with an empty list the promise would never resolve. Chromium always reports at least one display, so this is not reachable in Electron; natively `MonitorQueries.All()` is not trusted to be non-empty and an empty list resolves `null` at once (7.4.4, EDGE-SHELL-50).

Re-entrancy quirk (EDGE-SHELL-47): a second `region:select-area` while one is pending runs `win.hide()`, then `selectArea()` resolves the first promise `null`; the FIRST handler's `finally` then runs `win.show(); win.focus()` on a microtask, after the second handler hid the window, so the main window reappears over the new overlays and takes the focus from them. Not reachable in practice (the main window is hidden during a selection, so nothing can ask again). Citations: `src/main/RegionService.ts:30-51`, `106-111`, `114-150`. The IPC handler hides the requesting window before and restores it after (2.3.3). REQUIRED.

#### 2.5.2 Overlay window

| Property | Value | Citation |
|---|---|---|
| bounds | `display.bounds` (the whole monitor in DIP, taskbar included, not the work area) | `src/main/RegionService.ts:54-59` |
| frame, transparent, backgroundColor, hasShadow | `false`, `true`, `#00000000`, `false` | `:60-63` |
| alwaysOnTop | `true`, then `setAlwaysOnTop(true, 'screen-saver')` (on Windows every level is `HWND_TOPMOST`) | `:64`, `:91` |
| skipTaskbar, resizable, movable, minimizable, maximizable, fullscreenable | `true`, `false`, `false`, `false`, `false`, `false` | `:65-70` |
| enableLargerThanScreen | `true` | `:71` |
| title | `shotAI \u2014 Select area` (page title) | `overlay.html:6` |
| content protection | `setContentProtection(!remoteVisibleNow())`, seeded from the setting at creation (fix `58131a8`) | `:80-90` |
| show | on `ready-to-show`: return if `isDestroyed()` (the selection may already be over), else `show()` then `focus()`; the last overlay to become ready ends up focused | `:101-105` |
| owner | none (not a child of the main window, so hiding the main window does not hide it) | `:55-79` |
| web preferences | preload, `contextIsolation: true`, `nodeIntegration: false`, `sandbox: true` | `:73-78` (ELECTRON-ONLY) |

#### 2.5.3 Overlay renderer state machine

Variables `start`, `cur` (client DIP points, `null` initially). `dragging = start !== null`. `rect = (start && cur) ? { x: min(start.x, cur.x), y: min(start.y, cur.y), width: abs(cur.x - start.x), height: abs(cur.y - start.y) } : null` (`src/renderer/overlay/App.tsx:14-26`).

| State | Event | Guard | Action | Next |
|---|---|---|---|---|
| Waiting | keydown `Escape` (window listener) | overlay focused | `region.cancel()` | Closed |
| Waiting | mousedown | `button !== 0` | `region.cancel()` | Closed |
| Waiting | mousedown | `button === 0` | `start = cur = (clientX, clientY)` | Dragging |
| Waiting | mousemove | | ignored | Waiting |
| Waiting | mouseup | | `rect` is null, so `region.cancel()` | Closed |
| Dragging | mousemove | | `cur = (clientX, clientY)` | Dragging |
| Dragging | mousedown | `button !== 0` | `region.cancel()` | Closed |
| Dragging | mouseup | `rect.width >= 4 && rect.height >= 4` | `region.complete(rect)` | Closed |
| Dragging | mouseup | otherwise | `region.cancel()` | Closed |
| Dragging | keydown `Escape` | | `region.cancel()` | Closed |
| Dragging | mousedown | `button === 0` (only reachable if a left-down arrives without an up, for example after the capture was lost) | `start = cur = (clientX, clientY)`: the drag restarts | Dragging |
| any | keydown other than `Escape` | | ignored | same |
| Closed | any further event before the window closes | | the renderer may send `complete` or `cancel` again; main ignores it because `pending` is already null | Closed |

Citations: `:28-56`. `mouseup` does not check which button was released. Coordinates are unrounded client DIPs. Chromium captures the mouse on mousedown, so a drag that leaves the overlay keeps delivering moves with coordinates outside the window (EDGE-SHELL-26). REQUIRED.

#### 2.5.4 Result conversion (main side)

`finish(event, parseRect(rect))` (`src/main/RegionService.ts:114-140`; `parseRect` accepts only four finite numbers, `src/shared/project.ts:72-79`):

```
if (cssRect && cssRect.width >= 4 && cssRect.height >= 4 && !sender.isDestroyed()):
    b    = sender.getBounds()                                  // overlay bounds, DIP
    phys = screen.dipToScreenRect(sender, { x: b.x + round(cssRect.x), y: b.y + round(cssRect.y),
                                            width: round(cssRect.width), height: round(cssRect.height) })
    result = { x: phys.x, y: phys.y, width: phys.width, height: phys.height }   // global physical px
    info  "region selected: <w>x<h> @ (<x>,<y>) [physical px]"
else result = null
```

Chromium's `ScreenWin::DIPToScreenRect` maps the origin relative to the display (`physicalOrigin + (dip - dipOrigin) * s`) and, as far as can be determined without the Chromium source in this repo, floors the origin and ceils the size (Q-SHELL-5). The result feeds `CaptureTarget.area` (spec 02, which crops on the monitor containing the top-left and clips). REQUIRED.

#### 2.5.5 Overlay drawing

| Element | When | Rule | Citation |
|---|---|---|---|
| surface | always | `position: fixed; inset: 0; cursor: crosshair`; transparent body, desktop visible | `overlay.css:12-25` |
| hint box | while not dragging | centered horizontally, top at 14% of the overlay height, flex column centered, gap 4.8, padding 13.6 by 24, radius 12, background `rgba(17, 24, 39, 0.85)`, color `#ffffff`, font 16.8, weight 600, shadow `0 10px 35px rgba(0, 0, 0, 0.5)`, ignores the pointer | `overlay.css:27-44`, `App.tsx:65-70` |
| hint text | | line 1 `Drag to select a capture area`, line 2 `Press Esc to cancel` (font 13.12, weight 400, color `#c7cdda`) | `App.tsx:67-68`, `overlay.css:46-50` |
| selection | whenever `rect` exists (from the mousedown on, so a zero-size rect already dims the whole overlay) | box at `rect`, border 2 `#6366f1` inside the box (`box-sizing: border-box`), transparent interior, everything outside dimmed `rgba(0, 0, 0, 0.4)` (a `100vmax` spread shadow) | `overlay.css:52-61`, `App.tsx:71-80` |
| size badge | `rect.width >= 40 && rect.height >= 22` | inside the selection at 4, 4 from the padding edge (6, 6 from the outer edge), padding 1.92 by 7.2, radius 6, background `#6366f1`, color `#ffffff`, font 12.48, weight 600, tabular numerals, no wrap; text `<round(w * dpr)> × <round(h * dpr)>px`, for example `1280 × 720px` | `overlay.css:63-75`, `App.tsx:81-86` |

Each overlay draws independently: overlays on other displays keep their hint and stay undimmed while one overlay is being dragged. REQUIRED.

### 2.6 Recording visibility handler

`onRecordingChange(recording, opts)` with `showPill = opts?.pill ?? true` (`src/main/main.ts:426-448`):

| Event (from spec 02) | Actions, in order |
|---|---|
| `(true)` from `start` (and from the unreachable legacy `captureSingle`, `src/main/CaptureController.ts:789`) | `main.hide()`; if `pill` exists and `!toolbarPositioned`: `dockToolbarTopCenter(pill)`, `toolbarPositioned = true`; `pill.show()` |
| `(true, { pill: false })` from the no-click screenshot | `main.hide()` only ("no recording HUD, and the pill can't become the focused own-window") |
| `(false)` from `stop`, `discard` or the screenshot `finally` | `pill.hide()`; `main.show()` ("no-op if never hidden; also refocuses after recording"); `main.focus()` |

Each action is skipped when its window is null or destroyed. `(false)` is emitted only when a session existed (`stop` checks `wasRecording`, `discard` checks `s`, `src/main/CaptureController.ts:945-991`), so a Stop with no session does not show the main window. The engine emits the state push after this handler (spec 02, 2.2.2 steps 12 and 13), so the pill is visible before it receives the recording state. REQUIRED.

### 2.7 Content-protection ordering

| Window | Initial protection | Later changes | Citation |
|---|---|---|---|
| Main | `true` at construction | `applyRemoteVisibility(remoteVisible)` after the async settings load; the Settings toggle re-applies; grabs shield | `src/main/main.ts:280`, `495-502`, `src/main/ipc.ts:655-663` |
| Pill | `true` at construction | same | `src/main/main.ts:334` |
| Overlay | `!remoteVisibleNow()` at creation (synchronous cache) | grabs shield (every grab walks `getAllWindows()`) | `src/main/RegionService.ts:80-90` |
| `activate` re-created windows (macOS only) | `true` | re-apply after load | `src/main/main.ts:586-595` |

The rule, verbatim: "Windows are constructed with contentProtection ON and only opened up afterwards if the setting says so. That ordering is deliberate and fail-closed: the setting load is async, so seeding it at construction would leave a window briefly capturable on every launch. Protected-then-relaxed cannot leak; the reverse can." (`src/main/main.ts:495-499`, `53045c6`). REQUIRED [SECURITY]. The shield mechanics are spec 02 (2.7.3, 7.8).

### 2.8 Application menu

#### 2.8.1 Items

Built by `installAppMenu(getProjectWindow)` with `Menu.setApplicationMenu` (`src/main/menu.ts:133-259`). On Windows the menu bar sits in the main window (the only framed window). Role labels and accelerators come from Electron 42.5.0's role table.

| Menu | Item | Accelerator (Windows) | Enabled | Action | Citation |
|---|---|---|---|---|---|
| File | `Import Project…` | `Ctrl+O` (`CmdOrCtrl+O`) | always | send `menu:import-project` to the main window; the renderer ignores it while recording, else runs the Home import flow | `src/main/menu.ts:211-214`, `src/renderer/project/App.tsx:220-235` |
| File | `Settings` | `Ctrl+,` (`CmdOrCtrl+,`) | always | send `menu:open-settings`; ignored while recording, else `setShowSettings(true)` | `menu.ts:216-219`, `App.tsx:225-227` |
| File | separator | | | | `menu.ts:220` |
| File | `Exit` (role `quit`, Windows label) | none on Windows | always | `app.quit()` | `menu.ts:221` |
| Edit | `Undo` | `Ctrl+Z` | always | focused web contents `undo()` | `menu.ts:224` (role `editMenu`) |
| Edit | `Redo` | `Ctrl+Y` (the role table gives `Control+Y` on Windows only; other platforms use `Shift+CommandOrControl+Z`) | always | `redo()` | role table |
| Edit | separator | | | | |
| Edit | `Cut`, `Copy`, `Paste` | `Ctrl+X`, `Ctrl+C`, `Ctrl+V` (not registered as accelerators; the web contents handles the keys) | always | `cut()`, `copy()`, `paste()` | role table |
| Edit | `Delete` | none | always | `delete()` | role table |
| Edit | separator | | | | |
| Edit | `Select All` | `Ctrl+A` | always | `selectAll()` | role table |
| View | `Reload` | `Ctrl+R` | always | reload the page | `menu.ts:231` |
| View | `Force Reload` | `Shift+Ctrl+R` | always | reload ignoring cache | `:232` |
| View | `Toggle Developer Tools` | `Ctrl+Shift+I` | always | DevTools | `:233` |
| View | separator | | | | `:234` |
| View | `Actual Size` | `Ctrl+0` | always | `zoomLevel = 0` | `:235` |
| View | `Zoom In` | `Ctrl+Plus` (role accelerator `CommandOrControl+Plus`; Electron maps `Plus` to the `=`/`+` key WITH Shift, so on a US layout the chord is `Ctrl+Shift+=`, and `Ctrl+=` and numpad `+` probably do nothing: UNVERIFIED, no Windows run was possible) | always | `zoomLevel += 0.5` | `:236` |
| View | `Zoom Out` | `Ctrl+-` | always | `zoomLevel -= 0.5` | `:237` |
| View | separator | | | | `:238` |
| View | `Toggle Full Screen` | `F11` | always | `setFullScreen(!isFullScreen())` | `:239` |
| View | separator | | | | `:240` |
| View | `Brand` (submenu) | | `brandState.projectOpen` | 2.8.2 | `:241-248` |
| Window | `Minimize` | `Ctrl+M` | always | minimize the focused window if it is `minimizable` (role code `e.minimizable && e.minimize()`) | `:251` (role `windowMenu`) |
| Window | `Zoom` | none | always | nothing on Windows (the role has no Windows action) | role table |
| Window | `Close` | `Ctrl+W` | always | close the focused window; closing the main window quits (2.9) | role table |
| Help | `About shotAI` | none | always | 2.8.4 | `:252-255` |

The menu uses no `&` mnemonics. The Electron zoom factor is `1.2 ^ zoomLevel` (Chromium), clamped by Chromium to the factor range 0.25 to 5.0. REQUIRED except where 7.12 says otherwise (Reload, Force Reload, DevTools and Window then Zoom are ELECTRON-ONLY).

#### 2.8.2 Brand submenu

State `BrandMenuState` (`src/main/menu.ts:31-66`), initial `{ projectOpen: false, projectTheme: null, projectPinUnrecognised: false, appBrand: 'shotAI' }`.

Items (`:171-203`), in order:

| Item | Label | Type | Checked | Click |
|---|---|---|---|---|
| App default | `App default (<BRANDS[appBrand].label>)`, so `App default (shotAI)` or `App default (LFI)` | radio | `!projectPinUnrecognised && projectTheme === null` | `choose(null)` |
| each id of `BRAND_IDS` in catalog order (`shotAI`, `lfi`) | `BRANDS[id].label` (`shotAI`, `LFI`) | radio | `projectTheme === id` | `choose(id)` |

Truth table (brands shotAI and lfi):

| projectOpen | projectTheme | unrecognised | Submenu | App default | shotAI | LFI |
|---|---|---|---|---|---|---|
| false | any | any | disabled | (as computed, not reachable) | | |
| true | null | false | enabled | checked | | |
| true | null | true | enabled | | | |
| true | `shotAI` | false | enabled | | checked | |
| true | `lfi` | false | enabled | | | checked |

`choose(brand)` (`:172-183`): record `brandState.projectTheme = brand` without rebuilding (Electron already moved the radio dot natively, and the renderer's echo then matches, so nothing is rebuilt on the click path), then send `menu:set-project-theme(brand)` to the main window. The renderer (`src/renderer/project/App.tsx:417-431`) reads the store imperatively; if no project is open it does nothing; else `projects.setProjectTheme(projectPath, choice)` (spec 01), `applyManifest` on success, `fail(e)` on error; the resulting store change pushes a new state, which corrects the menu if the write failed or was refused.

The default brand is offered as a named entry on purpose: `App default` writes no key (follows the app setting) while a named brand pins it, the default included (`:154-170`, `772e381`, #77). Unrecognised pin: nothing is ticked, and the unrecognised state is not offered as a choice (`:36-55`, `38908cd`, #107; macOS reached the same design, their #119). REQUIRED.

#### 2.8.3 Brand state intake and rebuild

Push from the renderer (`src/renderer/project/App.tsx:392-407`), effect keyed on `[openPath, projectTheme, projectPinUnrecognised, brand]`: `{ projectOpen: !!openPath, projectTheme: openPath ? projectTheme : null, projectPinUnrecognised: openPath ? projectPinUnrecognised : false, appBrand: brand }`; a rejection is swallowed.

Main-side coercion (`src/main/main.ts:469-485`): debug `ipc: view:set-brand-menu open=<bool> project=<raw> app=<raw>`; `projectOpen: s.projectOpen === true`; `projectTheme: pinnedBrand(s.projectTheme)` (unknown or absent gives `null`, never the default brand, #95); `projectPinUnrecognised: s.projectPinUnrecognised === true`; `appBrand: coerceBrand(s.appBrand)` (unknown gives `shotAI`). The two brand fields narrow differently on purpose (`:463-468`).

`setBrandMenuState(next)` (`src/main/menu.ts:118-129`): if all four fields equal the current state, return (a rebuild under the cursor closes an open menu); else store and `scheduleRebuild()`: clear any pending timer, `setTimeout(REBUILD_DEFER_MS = 120)`, then `rebuildMenu()` inside `try`, warning `brand menu rebuild failed (non-fatal):` (`:88-104`). The rebuild re-runs `installAppMenu` (whole-menu rebuild, because item mutation on Windows is unreliable and radio items must be set together, `:106-117`). `armBrandMenu` registers the rebuilder and must run before the first build, or an early push is stored and never drawn (`:261-271`, `src/main/main.ts:488-492`). The deferral exists because rebuilding inside the native menu's teardown after a click is "a known way to lose the window on Windows" (`:74-87`, `772e381`). The commit body is explicit that the crash (a `projects:set-theme`, the manifest written 4 ms later, then exit code 0) was NOT reproduced and that this mechanism is the best fit, not a proven cause. `choose` updates only `projectTheme` in `brandState`, not `projectPinUnrecognised`, so choosing a row while the pin is unrecognised still triggers one (deferred) rebuild when the echo clears the flag; this is harmless. ELECTRON-ONLY mechanics (a WPF menu is bound, not rebuilt); REQUIRED intent (7.4.5).

#### 2.8.4 About dialog

`dialog.showMessageBox(win?, options)` (`src/main/menu.ts:136-152`):

| Field | Value |
|---|---|
| type | `info` |
| title | `About shotAI` |
| message | `<app.getName()> <app.getVersion()>`, for example `shotAI 1.3.0` |
| detail | `Local-first SOP builder \u2014 capture a process and let Claude write the guide.` + `\n\n` + `Electron <v> · Chromium <v>` + `\n` + `<process.platform>/<process.arch>`, for example `win32/x64` |
| buttons | `OK` |
| icon | `appIconPath()` resized to 64 x 64; omitted when the image is empty |
| parent | the main window if it exists, else none |

REQUIRED except the runtime line (7.12 D11).

### 2.9 Quit and crash reporting

| Event | Behavior | Citation | Class |
|---|---|---|---|
| Main window `closed` | `projectWindow = null`; `toolbarWindow.destroy()` if it exists, because the pill "is a hidden, skipTaskbar helper window, on its own it keeps the app alive (and out of the taskbar) after the main window's X is clicked, so the process lingers invisibly" | `src/main/main.ts:283-292` | REQUIRED intent (INV-SHELL-4) |
| `window-all-closed` | `app.quit()` unless macOS | `:580-584` | REQUIRED |
| `before-quit` | `capture.teardown()` (detach hook and hotkey synchronously) and `globalShortcut.unregisterAll()` "so the uiohook worker thread can't keep the process alive (zombie) on Windows" | `src/main/CaptureController.ts:1478-1483` | REQUIRED |
| `activate` | macOS dock re-create | `src/main/main.ts:586-595` | ELECTRON-ONLY |
| `render-process-gone` | error `renderer gone: reason=<r> exitCode=<n>` | `:566-570` | ELECTRON-ONLY (no renderer); intent kept by 7.9 |
| `child-process-gone` | error `child process gone: type=<t> reason=<r> exitCode=<n>` | `:571-575` | ELECTRON-ONLY; the WebView2 PDF host keeps the intent (spec 09) |
| `uncaughtException` | error `uncaught exception in main:` plus the error; the process keeps running | `:576-578` | REQUIRED (7.9) |
| electron-log `startCatching` | logs uncaught errors and unhandled rejections, no dialog | `src/main/logger.ts:33-34` | REQUIRED (7.9) |

Why the logging exists (`:561-565`, `772e381`): "A window vanishing with no trace in the log is nearly impossible to diagnose after the fact \u2014 a renderer that dies looks identical to a clean quit, because the app then exits 0 through window-all-closed below."

There are no quit confirmations and no unsaved state (every edit is persisted as it happens). The macOS port makes quit unconditional for the same reason (`macOS:shotAI/shotAIApp.swift:220-231`). REQUIRED.

### 2.10 Electron-only machinery

| # | Machinery | What it did | Citation | Native replacement of the intent |
|---|---|---|---|---|
| 2.10.1 | `shot://<projectId>/<relpath>` protocol | let the sandboxed renderer read a project's `.png`, `.jpg`, `.jpeg` (MIME map; the relative path is `decodeURIComponent(url.pathname).replace(/^\/+/, '')`, the extension compared lowercased, the project id is `url.hostname`), 403 `Unsupported type` for others, 404 `Not found` when `resolveProjectFile` refuses, 500 `Error` on exceptions, `Access-Control-Allow-Origin: *` and `Cache-Control: no-cache` so the canvas stays untainted | `src/main/main.ts:31-86` | One process: views load images from a path returned by spec 01's confined resolver (`ResolveProjectFile`, reparse points refused). The extension allowlist is kept at the image loader (spec 01 or 04). No URL scheme exists. |
| 2.10.2 | Content-Security-Policy (packaged only) | `default-src 'self'; img-src 'self' shot: data: blob:; style-src 'self' 'unsafe-inline'; font-src 'self' data:; script-src 'self'; connect-src 'self'` | `:88-106` | No web UI. The one web engine (WebView2 for PDF, spec 09) runs with scripts disabled and all navigation cancelled except the initial navigation to the print copy's file URI (corrected per spec 09 EDGE-EXP-39 and Q-EXP-17: `NavigateToString` refuses content over 2 MB, and a PDF page with embedded PNGs routinely exceeds it), under the explicit user data folder `%LOCALAPPDATA%\LFI\shotAI\WebView2` (`IAppPaths.LocalDataDirectory`, R-ARCH-13). It lives only in `ShotAI.Platform.Export`; no other assembly references WebView2 (INV-ARCH-5). |
| 2.10.3 | `window.open` deny and navigation confinement to `shot:`, `file:` or the dev server; warn `blocked navigation to <url>` | stop injected content from reaching the IPC surface | `:381-401` | Nothing navigates. Outbound links (update badge, release page) open the default browser only through spec 11's `IExternalLinks.OpenAsync` (the link allowlist, ARCHITECTURE 9.2 S15), whose Platform `ShellUrlLauncher` is the only code that hands a URL to the shell; `Process.Start` is a banned symbol everywhere else (ARCHITECTURE 14.9). |
| 2.10.4 | Preload, `contextIsolation`, `sandbox`, `nodeIntegration: false`, `SHOTAI_NO_SANDBOX` | renderer containment | `:131-140`, `:272-277` | Deleted: no renderer process. The image-decoding attack surface moves in-process to WIC; spec 04 and 01 keep the confined path rule. |
| 2.10.5 | GPU policy (`gpu-policy.ts`, `SHOTAI_ENABLE_GPU` tri-state, post-init GPU log) | keep Chromium from aborting startup when an x64 build runs under ARM64 emulation | `src/main/gpu-policy.ts:1-58`, `src/main/main.ts:108-129`, `364-380` | WPF has no GPU process and falls back to software rendering by itself. Native ships ARM64, so the emulation case is rare. Kept as diagnostics only: a startup log line with the render tier and an emulation flag, and `SHOTAI_ENABLE_GPU=0` forces WPF software rendering (7.4.1 step 4, IMPROVEMENT). |
| 2.10.6 | Squirrel lifecycle and ARP icon fix (`arp-icon.ts`, `electron-squirrel-startup`) | on `--squirrel-install` or `--squirrel-updated` copy `<resources>\shotAI_icon.ico` to `<InstallRoot>\app.ico` and run `reg add HKCU\Software\Microsoft\Windows\CurrentVersion\Uninstall\shotai /v DisplayIcon /t REG_SZ /d <InstallRoot>\app.ico /f` (only if `app.ico` exists), best effort; `electron-squirrel-startup` spawns `..\Update.exe --createShortcut=shotAI.exe` (install, updated) or `--removeShortcut=shotAI.exe` (uninstall) and quits, quits on obsolete, and reads only `process.argv[1]` | `src/main/arp-icon.ts:1-102`, `src/main/main.ts:142-153`, `node_modules/electron-squirrel-startup/index.js:15-33` | The per-machine MSI (spec 12, INV-PKG-1 and INV-PKG-30) owns the Start menu shortcut and the "Installed apps" icon (`ARPPRODUCTICON` from `assets/shotAI_icon.ico`). The app never writes the registry and never handles lifecycle arguments. |
| 2.10.7 | `wireLoadDiagnostics` (`<label> window loaded`, `<label> window FAILED to load (<code> <desc>): <url>`) | surface a renderer page that failed to load | `src/main/main.ts:210-223` | XAML is compiled; a view that fails to construct throws, which the crash handlers log (7.9). |
| 2.10.8 | HTML entry pages `index.html`, `toolbar.html`, `overlay.html` and their `main.tsx` React roots | host the three renderers; each throws `Root element #root not found` if the root is missing | `index.html:1-12`, `toolbar.html:1-12`, `overlay.html:1-12`, `src/renderer/overlay/main.tsx:1-15`, `src/renderer/toolbar/main.tsx:1-15` | The WPF windows themselves. Their titles carry over (`shotAI`, `shotAI \u2014 Capture`, `shotAI \u2014 Select area`). |
| 2.10.9 | `appIconPath()` resources-then-dev lookup | find the PNG icon inside or outside the asar | `src/main/paths.ts:38-44` | The exe's `ApplicationIcon` is already set in the scaffold (`dotnet/src/ShotAI.App/ShotAI.App.csproj`, `$(RepoRoot)assets/shotAI_icon.ico`), and a WPF window whose `Icon` is unset shows the exe's icon. The pack URI `pack://application:,,,/Assets/shotAI_icon.ico` that the About dialog and `Window.Icon` use does NOT exist yet: add `<Resource Include="$(RepoRoot)assets/shotAI_icon.ico" Link="Assets/shotAI_icon.ico" />` to `ShotAI.App.csproj`. The Electron About icon was the PNG (`shotAI_icon.png`); the `.ico` carries the same artwork. |
| 2.10.10 | `brandFontPath()` (returns `''` when missing, so an export degrades to the fallback stack) | give the PDF page the brand face file | `src/main/paths.ts:11-36` | Kept as `AppPaths.BrandFontPath()` next to the exe (`Path.Combine(IAppPaths.FontsDirectory, "Archivo.ttf")`, that is `<AppContext.BaseDirectory>\Fonts\Archivo.ttf`), returning `""` when missing. `AppPaths` is the App type in `ShotAI.App` that implements spec 10's `IAppPaths` (ARCHITECTURE 10.2) and spec 09's `IBrandFontSource`; no other code composes the path. Consumer is spec 09. REQUIRED. |
| 2.10.11 | Menu rebuild deferral and changed-check | work around native menu teardown | `src/main/menu.ts:68-129` | A bound WPF menu (7.4.5). |

### 2.11 User-visible strings

| String | Where | Citation |
|---|---|---|
| `shotAI` | main window title; pill label when idle | `src/main/main.ts:270`; `src/renderer/toolbar/App.tsx:87` |
| `shotAI \u2014 Capture` | pill window title | `src/main/main.ts:325`, `toolbar.html:6` |
| `shotAI \u2014 Select area` | overlay window title | `overlay.html:6` |
| `Capturing · <n>`, `Paused · <n>` | pill label | `toolbar/App.tsx:88` |
| `❚❚ Pause`, `▶ Resume`, `■ Stop`, `✕` | pill buttons | `:102`, `:111`, `:120`, `:130` |
| `Pause`, `Resume`, `Stop & finish`, `Discard this capture`, `Drag to move` | pill tooltips | `:82`, `:99`, `:108`, `:117`, `:126` |
| `Discard this capture` (accessible name), `Dismiss this capture error` (accessible name) | pill | `:127`, `:163` |
| `Click anything to capture a step · Ctrl+Shift+S` | pill hint, recording | `:173` |
| `Paused \u2014 press Resume to keep capturing` | pill hint, paused | `:172` |
| `A capture failed \u2014 see the log for details.` | pill error fallback | `:35` |
| `⚠` | pill error glyph | `:151` |
| `Dismiss`, tooltip `Dismiss this error` | pill error dismiss | `:162`, `:166` |
| `Discard this capture? This is a new project, so the entire project will be deleted.` | Discard confirmation | `:67-68` |
| `Discard this capture? Steps recorded in this session will be deleted.` | Discard confirmation | `:69` |
| `OK`, `Cancel` | Discard confirmation buttons (Electron's `window.confirm` dialog) | `:70` |
| `Drag to select a capture area` | overlay hint | `overlay/App.tsx:67` |
| `Press Esc to cancel` | overlay hint sub-line | `:68` |
| `<W> × <H>px` | overlay size badge | `:83-85` |
| `File`, `Import Project…`, `Settings`, `Exit` | File menu | `src/main/menu.ts:208-221`, role table |
| `Edit`, `Undo`, `Redo`, `Cut`, `Copy`, `Paste`, `Delete`, `Select All` | Edit menu | role table |
| `View`, `Reload`, `Force Reload`, `Toggle Developer Tools`, `Actual Size`, `Zoom In`, `Zoom Out`, `Toggle Full Screen`, `Brand` | View menu | `menu.ts:229-248`, role table |
| `App default (<brand label>)`, `shotAI`, `LFI` | Brand submenu | `menu.ts:186`, `196` |
| `Window`, `Minimize`, `Zoom`, `Close` | Window menu | role table |
| `Help`, `About shotAI` | Help menu | `menu.ts:253-254` |
| `About shotAI`, `shotAI <version>`, `Local-first SOP builder \u2014 capture a process and let Claude write the guide.`, `OK` | About dialog | `menu.ts:141-147` |

### 2.12 Log lines owned here

| Level | Text | Citation |
|---|---|---|
| info | `another instance already holds the lock \u2014 exiting.` | `src/main/main.ts:168` |
| info | `runtime: <platform>/<arch> · electron <v> · chrome <v>` | `:355-357` |
| debug | `<label> window loaded`; error `<label> window FAILED to load (...)` | `:212-221` |
| debug | `region: overlay opened across <n> display(s)` | `RegionService.ts:49` |
| info | `region selected: <w>x<h> @ (<x>,<y>) [physical px]` | `RegionService.ts:132-134` |
| debug | `region: selection cancelled` | `RegionService.ts:137` |
| debug | `ipc: view:set-brand-menu open=<b> project=<raw> app=<raw>` | `main.ts:471-475` |
| warn | `brand menu rebuild failed (non-fatal):` | `menu.ts:95` |
| error | `renderer gone: ...`, `child process gone: ...`, `uncaught exception in main:` | `main.ts:566-578` |
| info | `GPU disabled \u2014 software rendering [<reason>] (SHOTAI_ENABLE_GPU=1 to force on)` or `GPU enabled [<reason>] (SHOTAI_ENABLE_GPU=0 to force off)`; reasons `forced ON (SHOTAI_ENABLE_GPU=1)`, `forced OFF (SHOTAI_ENABLE_GPU=0)`, `auto: x64 under ARM64 emulation \u2014 GPU context unavailable`, `auto: default ON`, `auto: default ON (detection error)` (ELECTRON-ONLY) | `main.ts:118`, `126-128`, `gpu-policy.ts:52-57` |
| info; warn | `GPU: compositing=<s>, webgl=<s>, 2d_canvas=<s> \| renderer=<glRenderer or ?>`; `GPU info query failed:` (ELECTRON-ONLY) | `main.ts:375-380` |
| warn | `OS sandbox DISABLED (SHOTAI_NO_SANDBOX=1) \u2014 dev/VM workaround only` (ELECTRON-ONLY) | `main.ts:139` |
| info; error | `shot:// protocol registered`; `shot:// handler error:` (ELECTRON-ONLY) | `main.ts:85`, `81` |
| warn | `blocked navigation to <url>` (ELECTRON-ONLY) | `main.ts:396` |
| warn | `startup auto-archive failed (non-fatal):`; update-check lines `update check skipped (<reason>)` (debug), `update check could not complete: <error>` (info), `update check: up to date (<v>)` (debug), `update available: <v>` (info), `startup update check failed (non-fatal):` (warn); spec 10 owns them | `main.ts:513`, `529-556` |
| info or warn | `arp-icon: wrote <path> from bundled icon (<cmd>)`, `arp-icon: bundled icon missing at <path> \u2014 leaving app.ico as-is`, `arp-icon: failed to write app.ico`, `arp-icon: set DisplayIcon -> <path> (<cmd>)`, `arp-icon: failed to set DisplayIcon` | `arp-icon.ts:80-98` |

Log text is not a contract (spec 10 owns the format). The native lines are listed in 7.9.

---

## 3. Constants

| Name | Value | Unit | Meaning | Citation |
|---|---|---|---|---|
| `LIST_WIDTH` | 720 | DIP | main window width on the home list, and its initial width | `src/main/main.ts:235` |
| Initial height | 740 | DIP | main window initial height | `:266` |
| `minWidth`, `minHeight` | 680, 560 | DIP | main window minimum size | `:267-268` |
| `DETAIL_WINDOW_BASE` | 1010 | DIP | detail width at scale 1 and the detail floor | `src/shared/doc-scale.ts:103` |
| `WINDOW_CHROME` | 130 | DIP | `1010 - 880`, window width around the report frame | `doc-scale.ts:104` |
| `HTML_COL_BASE` | 816 | DIP | document column at scale 1 | `doc-scale.ts:69` |
| `HTML_DOC_PAD` | 32 | DIP per side | document padding (does not scale) | `doc-scale.ts:71` |
| Scale clamp | 65 to 125, snapped to 5 | percent | `clampScale` | `doc-scale.ts:44-56` |
| Pill size | 380 x 74 | DIP | pill window | `src/main/main.ts:316-317` |
| Pill dock gap | 8 | DIP | distance from the work area top | `:203` |
| `MIN_DRAG` | 4 | DIP (CSS px) | smallest accepted selection side, both in the renderer and in main | `RegionService.ts:21`, `overlay/App.tsx:4` |
| Badge thresholds | width 40, height 22 | DIP | size badge shown at or above both | `overlay/App.tsx:81` |
| Overlay hint top | 14 | percent of overlay height | hint box position | `overlay.css:30` |
| Overlay dim | `rgba(0, 0, 0, 0.4)` | color | outside the selection | `overlay.css:59` |
| Selection color | `#6366f1`, border 2 | color, DIP | selection border and badge | `overlay.css:57`, `69` |
| Hint box color | `rgba(17, 24, 39, 0.85)` | color | overlay hint background | `overlay.css:38` |
| `REBUILD_DEFER_MS` | 120 | ms | brand menu rebuild deferral (ELECTRON-ONLY) | `src/main/menu.ts:104` |
| About icon | 64 x 64 | px | About dialog icon | `menu.ts:138` |
| Flash | 0.7, ease-out; reduced motion hold to 70% | s | pill confirmation flash | `toolbar.css:141`, `163-172` |
| Rec dot pulse | 1.4, ease-in-out, 50% at opacity 0.3 | s | recording dot | `toolbar.css:281`, `306-310` |
| Pill colors | background `#1f2330`, text `#f4f5f7`, accent `#4f46e5`, accent press `#4338ca`, error `#ef4444`, live `#34d399`, paused `#fcd34d`, button `#2c3142`, button hover `#3a4159`, discard text `#fca5a5`, danger `#7f1d1d`, danger hover `#991b1b`, error text `#fecaca`, hint `#aeb4c7`, grip `#6b7280` | colors | 2.4.8 | `toolbar.css` |
| Hotkey text | `Ctrl+Shift+S` | chord | shown in the pill hint | `toolbar/App.tsx:173` |
| Menu accelerators | `Ctrl+O`, `Ctrl+,`, `Ctrl+Z`, `Ctrl+Y`, `Ctrl+X`, `Ctrl+C`, `Ctrl+V`, `Ctrl+A`, `Ctrl+R`, `Shift+Ctrl+R`, `Ctrl+Shift+I`, `Ctrl+0`, `Ctrl+Plus`, `Ctrl+-`, `F11`, `Ctrl+M`, `Ctrl+W` | chords | 2.8.1 | `menu.ts`, role table |
| Zoom step and range | 0.5 level, factor `1.2 ^ level`, factor 0.25 to 5.0 | | View zoom | Chromium |
| `ARP_KEY` | `HKCU\Software\Microsoft\Windows\CurrentVersion\Uninstall\shotai` | registry | ELECTRON-ONLY | `arp-icon.ts:27` |
| Squirrel commands | `--squirrel-install`, `--squirrel-updated`, `--squirrel-uninstall`, `--squirrel-obsolete`, `--squirrel-firstrun` | argv | ELECTRON-ONLY | `arp-icon.ts:35-40` |
| Env switches | `SHOTAI_ENABLE_GPU` (`1`, `0`), `SHOTAI_NO_SANDBOX` (`1`), `SHOTAI_SELFTEST` (`1`), `SHOTAI_CAPTURE_TEST` (`1`) | env | 2.1 | `main.ts:113-140`, `403-414` |
| `PROCESSOR_ARCHITEW6432` x86 family | `amd64`, `x64`, `x86` (case-insensitive) | env value | emulation check | `gpu-policy.ts:40-42` |
| Scheme and MIME | `shot`; `.png` `image/png`, `.jpg` and `.jpeg` `image/jpeg` | | ELECTRON-ONLY | `main.ts:38`, `54-58` |
| Native: mutex name (new) | `Local\shotAI.SingleInstance.<user SID>` | name | single-instance lock | 7.4.1 |
| Native: activation window name (new) | `shotAI.Activation.<user SID>` | name | window name of the message-only window a second launch looks up | 7.4.8 |
| Native: activation message (new) | `RegisterWindowMessageW("shotAI.ActivateMainWindow")` | message | second launch asks the first to surface | 7.4.1 |
| Native: overlay hit-test fill (new) | `#01000000` | ARGB | alpha 1/255 so a layered overlay receives the mouse everywhere | 7.6.4 |
| Native: pill tooltip delay (new) | 400 | ms | tooltip open delay on the pill (Chromium's native tooltip delay is similar; UNVERIFIED value) | 7.6.2 |
| CSS `ease-out` | `cubic-bezier(0, 0, 0.58, 1)` | curve | flash easing per keyframe interval; WPF `KeySpline="0,0 0.58,1"` | `toolbar.css:141` (CSS Easing spec) |
| CSS `ease-in-out` | `cubic-bezier(0.42, 0, 0.58, 1)` | curve | rec-dot pulse per half; WPF `KeySpline="0.42,0 0.58,1"` | `toolbar.css:281` |
| Flash opacity peak | 25 | percent of 700 ms (175 ms) | opacity 1 keyframe | `toolbar.css:149-151` |
| Reduced-motion hold | 70 | percent of 700 ms (490 ms) | opacity held at 1 | `toolbar.css:165-172` |

---

## 4. Invariants

**INV-SHELL-1 [SECURITY]. Every top-level HWND the app shows is registered with `OwnWindowRegistry` and receives its capture exclusion before it is first visible.** That includes the main window, the pill, every overlay, every WPF dialog (Discard confirmation, About), every WPF popup HWND (tooltips, context menus, combo-box drop-downs) and every system common dialog shotAI opens (spec 04's `ChooseColor`, the folder, open and save dialogs behind spec 11's `IFileDialogs` and spec 09's `IExportDialogs`; 7.4.7, Q-EDIT-21, Q-SHELL-22). The one exception is spec 12's `LegacyInstanceGuard` notice, which runs before the registry exists and exits the process (Q-SHELL-19). Why: a window that is visible for one frame without the affinity can be captured by shotAI's own grab, and during a recording with remote visibility on the pill and its tooltip are on screen (`53045c6`, `58131a8`; spec 02 INV-CAP-7). Citation: `src/main/main.ts:280`, `334`; `RegionService.ts:90`. Test: `AllWindowsRegisteredTests.EveryShownHwndIsRegistered` (App.Tests: enumerates `EnumThreadWindows` of the UI thread after opening each window, dialog and tooltip, and asserts each visible HWND is in the registry and has the expected `GetWindowDisplayAffinity`).

**INV-SHELL-2 [SECURITY]. Windows are created excluded from capture and relaxed only after the remote-visibility setting is known (protected, then relaxed).** Even when the setting is already loaded synchronously, the order is: register (excluded), then `CaptureShield.ApplyRemoteVisibility(setting)`. Why: "Protected-then-relaxed cannot leak; the reverse can." (`src/main/main.ts:495-499`, `53045c6`). Test: `StartupOrderTests.WindowsExcludedBeforeSettingApplied` (App.Tests, a fake `IWindowProtection` records the call order: every `SetAllExcluded(true)` or per-window exclude precedes the first relax).

**INV-SHELL-3 [SECURITY]. A window created after startup takes its initial exclusion from the shield's current effective state (`CaptureShield.ExcludedForNewWindow`), not from a constant and not from the raw setting alone.** Why: the hard-coded overlay made Area selection invisible over a remote session (`58131a8`), and a window created while a grab holds the shield must start excluded. Citation: `RegionService.ts:80-90`. Test: `OwnWindowRegistryTests.RegisterWhileShieldHeldStartsExcluded` and `.RegisterSeedsFromSetting` (spec 02), plus `AreaOverlayTests.OverlaySeededFromSetting` (App.Tests).

**INV-SHELL-4. Closing the main window ends the process.** The pill, overlays, dialogs and the activation listener never keep it alive. Why: the hidden `skipTaskbar` pill kept the process alive invisibly (`src/main/main.ts:285-291`). Citation: `:283-292`, `580-584`. Test: `LifecycleTests.ClosingMainWindowShutsDown` (App.Tests: `Application.ShutdownMode == OnMainWindowClose` and closing the main window raises `Exit` with the pill created and hidden); AC-SHELL-3.

**INV-SHELL-5. At most one shotAI process runs per user session; a second launch surfaces the first and exits without creating a window or touching the projects folder or settings.** Why: one projects directory, one store write queue per process (`src/main/main.ts:161-167`, `d79bc3b`). Test: `SingleInstanceTests.SecondAcquireFails` and `.SecondLaunchSignalsFirst` (Platform.Tests, child process); AC-SHELL-4.

**INV-SHELL-6. The pill never becomes the foreground window and never appears in the taskbar or Alt+Tab.** Showing it, clicking any of its buttons, and dragging it leave `GetForegroundWindow()` unchanged. Why: the macOS pill is non-activating (`macOS:shotAI/Capture/CapturePill.swift:5-9`); an activating pill steals focus from the app being recorded, and the first click after recording starts can land on shotAI instead of the target. IMPROVEMENT turned invariant. Test: `CapturePillWindowTests.ShowDoesNotActivate`, `.ClickDoesNotActivate`, `.HasToolWindowAndNoActivateStyles` (App.Tests).

**INV-SHELL-7. The pill is visible exactly while a recording session with `showPill` exists, paused included.** It is hidden on stop, discard and every return to idle, and never shown for the no-click screenshot. Citation: `src/main/main.ts:426-448`. Test: `RecordingVisibilityPlannerTests` (Core) and `RecordingChangedHidesAndShows` (App.Tests).

**INV-SHELL-8. The pill docks to the top-center of the main window's monitor work area on its first show of the app run; later shows keep the user's position.** (Native adds: unless that position no longer intersects any monitor, EDGE-SHELL-25.) Citation: `src/main/main.ts:189-208`, `437-440`. Test: `PillDockingTests` (Core) and `CapturePillWindowTests.DocksOnceThenKeepsPosition` (App.Tests).

**INV-SHELL-9. The main window is hidden for the whole session and for the whole area selection, and in both cases is shown and activated again when it ends, however it ends (stop, discard, screenshot failure, selection cancel, selection error).** Citation: `src/main/main.ts:426-448`, `src/main/ipc.ts:943-959`. Test: `RecordingChangedHidesAndShows`, `AreaSelectionServiceTests.RestoresMainWindowOnCancelAndError` (App.Tests).

**INV-SHELL-10. While a project is open, a document-scale commit only ever grows the main window, never shrinks it, and never touches a maximized (native: or full-screen) window.** Why: resetting the width on every slider nudge discarded a window the user had widened or maximized (`62b4b7d`, #70). Citation: `src/main/main.ts:242-255`. Test: `WindowLayoutTests.DetailIsGrowOnly`, `.MaximizedIsNotResized`.

**INV-SHELL-11. The x position after any list or detail resize is `max(wa.x, min(round(centerX - newW / 2), wa.x + wa.width - newW))` with `round` = `JsMath.Round`; y and height are unchanged.** Citation: `src/main/main.ts:257-260`. Test: `WindowLayoutTests.CenterPreservedAndClamped`, `.RoundsLikeJavaScriptForNegativeHalves`.

**INV-SHELL-12. An in-session capture error is visible on the pill until a step lands, the session ends, a newer error replaces it, or the user dismisses it; it is never shown on an idle pill; an empty message shows `A capture failed \u2014 see the log for details.`** Why: the main window is hidden while recording, so the pill is the only surface ("a long recording can fail silently", `c3f7707`, #52). Citation: `src/renderer/toolbar/App.tsx:25-54`. Test: `PillPresenterTests` error group.

**INV-SHELL-13. Discard never runs without an explicit confirmation, and the confirmation text is chosen by `willDeleteProjectOnDiscard` from the state the pill is showing.** Why: Discard deletes steps or a whole project (R5). Citation: `src/renderer/toolbar/App.tsx:61-77`. Test: `PillPresenterTests.DiscardMessageFollowsState`; `CapturePillWindowTests.DiscardCancelDoesNotCallService` (App.Tests).

**INV-SHELL-14. An area selection resolves exactly once: to a rectangle for a qualifying drag, and to `null` for Esc, a non-left button, a drag under 4 DIP on either side, a mouse-up with no drag, any overlay closing, cancellation, or a newer selection starting.** Citation: `src/main/RegionService.ts:44-51`, `106-150`, `src/renderer/overlay/App.tsx:28-56`; macOS states the same guarantee (`macOS:shotAI/Capture/AreaSelect.swift:4-9`). Test: `AreaSelectionServiceTests` resolution group (App.Tests) and `AreaSelectionMathTests.MinDrag` (Core).

**INV-SHELL-15. The area result is a global physical-pixel rectangle computed from the overlay's own monitor rectangle and DPI, and only an overlay belonging to the current selection can resolve it.** Citation: `src/main/RegionService.ts:114-131`. Test: `AreaSelectionMathTests.ToPhysical*` (Core); `AreaSelectionServiceTests.StaleOverlayCannotResolve` (App.Tests).

**INV-SHELL-16. View then Brand ticks `App default` if and only if the open project has no pin and no unrecognised pin; ticks a named brand if and only if the project pins exactly it; ticks nothing when the pin is unrecognised; and the submenu is disabled when no project is open.** Why: #77 (default brand pinnable), #95 (unknown pin must not read as the default brand), #107 (unknown pin must not read as App default). Citation: `src/main/menu.ts:184-201`, `246`. Test: `BrandMenuModelTests` truth table.

**INV-SHELL-17. A brand menu choice is applied only to the project open at the moment of the click, only through the open project's session (spec 05 `session.Apply(new SetProjectThemeOperation(brand))`, which carries spec 01's `SetProjectTheme` rule: null clears, a brand pins, the default included, raw no-op compare), with the clicked value passed untouched, and never by the menu writing `project.json` itself.** Citation: `src/main/menu.ts:172-183`, `src/renderer/project/App.tsx:417-431`; spec 11 7.3.3 (INV-IPC-14). Test: `AppMenuViewModelTests.BrandChoiceCallsStoreForOpenProject`, `.BrandChoiceIgnoredWithNoProject`.

**INV-SHELL-18. File then Import Project and File then Settings do nothing while a capture session exists.** Citation: `src/renderer/project/App.tsx:220-235`. Test: `AppMenuViewModelTests.ImportAndSettingsIgnoredWhileRecording`.

**INV-SHELL-19. Capture triggers (mouse hook and hotkey) are released synchronously on every exit path (main window closed, File then Exit, Window then Close, session end, unhandled-exception shutdown) before the process exits.** Why: "so the uiohook worker thread can't keep the process alive (zombie) on Windows" (`src/main/CaptureController.ts:1478-1483`). Test: `LifecycleTests.ExitCallsCaptureTeardown` (App.Tests, fake `ICaptureService`).

**INV-SHELL-20. No crash or unhandled exception on any thread ends or disturbs the process without a log line.** Why: `772e381`, a window vanishing was indistinguishable from a clean quit. Citation: `src/main/main.ts:561-578`, `src/main/logger.ts:33-34`. Test: `CrashLoggingTests` (App.Tests, raises on the Dispatcher, on a pool thread via an unobserved task, and asserts the log sink received an error entry).

**INV-SHELL-21. The UI thread never synchronously waits on capture work, and capture events reach the shell in the order the engine raised them.** Why: a cross-thread wait deadlocks affinity changes and UIA calls (spec 02 7.3); the pill must be visible before its first recording state arrives (2.6). Test: `ShellEventOrderingTests.RecordingChangedBeforeStateChanged` (App.Tests); code review rule: no `.Result`, `.Wait()` or `Dispatcher.Invoke` from capture threads into the UI in `ShotAI.App`. Spec 11 enforces this with analyzers (T6, T9): capture events reach the UI only through `IUiDispatcher.Post` (R-ARCH-11); `Dispatcher.Invoke`, `Dispatcher.BeginInvoke` and `Dispatcher.InvokeAsync` are banned in `ShotAI.App` outside the three allowlisted files `WpfUiDispatcher.cs`, `StaRenderThread.cs` (spec 04) and `UiDeferral.cs` (focus and layout deferrals only, R-ARCH-18; never service-event marshaling); and the only allowlisted blocking wait is `ShutdownFlush.cs` (ARCHITECTURE 14.9).

**INV-SHELL-22. Every user-visible string in 2.11 (except the removed Electron-only menu items) is reproduced exactly, including U+2014 where shown.** Test: `ShellStringsTests` (Core).

**INV-SHELL-23. The overlays together cover every monitor completely, each overlay exactly its monitor's `rcMonitor` in physical pixels, and every pixel of every overlay receives the mouse.** Why: a layered WPF window is click-through where its alpha is 0 (EDGE-SHELL-28). Test: `AreaOverlayTests.OneOverlayPerMonitorWithExactBounds`, `.TransparentAreaReceivesMouse` (App.Tests).

---

## 5. Edge cases and hard-won fixes

**EDGE-SHELL-1. A Squirrel lifecycle launch must not contend for the single-instance lock.** It would lose the race to a running app and skip the ARP icon fix. Required natively: nothing to port (no Squirrel, no lifecycle arguments); the principle carries to spec 12: an installer custom action must never launch `shotAI.exe` expecting it to run to completion while another instance holds the lock. From `d79bc3b`. `src/main/main.ts:154-159`. ELECTRON-ONLY.

**EDGE-SHELL-2. The hidden pill kept the process alive after the main window's X.** Required: closing the main window shuts the app down (WPF `ShutdownMode.OnMainWindowClose`), and the main window's `Closed` closes the pill, overlays and dialogs explicitly. From the import commit (`9da70df` squash). `src/main/main.ts:283-292`.

**EDGE-SHELL-3. Seeding protection from the async setting at construction leaves a window briefly capturable on every launch.** Required: INV-SHELL-2. From `53045c6`. `src/main/main.ts:495-502`.

**EDGE-SHELL-4. The overlay hard-coded protection on, so Area selection was invisible over a remote session** (the viewer saw a bare desktop for the whole drag). Required: INV-SHELL-3. From `58131a8`. `src/main/RegionService.ts:80-90`.

**EDGE-SHELL-5. The detail resize ran on every scale commit and discarded a hand-widened window.** Required: grow-only while open (INV-SHELL-10). From `62b4b7d` (#70 adversarial review). `src/main/main.ts:246-255`.

**EDGE-SHELL-6. Resizing a maximized window un-maximizes it.** Required: skip when maximized on open. From `62b4b7d`. `src/main/main.ts:242-243`. Native extends the skip to the leave direction and to full screen (D6).

**EDGE-SHELL-7. Rebuilding the application menu inside the native menu's own click teardown is the best-fit explanation for a lost window** (the log showed `projects:set-theme`, a manifest write, then exit 0; the commit says the crash was not reproduced). Required natively: no teardown hazard exists for a WPF `Menu`, but the intent holds: a Brand click must not trigger a synchronous re-creation of the menu it came from. The native menu is bound to a view model and never rebuilt. From `772e381`. `src/main/menu.ts:74-104`.

**EDGE-SHELL-8. The default brand was filtered off the menu, so with the app brand set to LFI every entry produced the same document and the control looked broken.** Required: every brand is listed, the default included, plus `App default (<label>)`. From `772e381` (#77). `src/main/menu.ts:154-170`, `194-201`.

**EDGE-SHELL-9. An unrecognised pin (a brand a newer build wrote) ticked `App default`, and clicking the ticked row silently deleted the pin and re-dated the project.** Required: nothing ticked (INV-SHELL-16). From `38908cd` (#107, macOS #119). `src/main/menu.ts:36-55`, `188-191`.

**EDGE-SHELL-10. Coercing an unknown incoming theme to the default brand ticked a brand the project is not pinned to.** Required: an unknown pin maps to null (`PinnedBrand`), and the tick follows INV-SHELL-16; `appBrand` still falls back to the default. From `77adda3` (#95). `src/main/main.ts:463-484`.

**EDGE-SHELL-11. A brand state push that arrives before the rebuilder is registered is stored and never drawn.** Required natively: the menu reads the view model at all times, so there is no registration order; the view model must be constructed before the main window's first render. From `701d4aa`. `src/main/main.ts:488-492`.

**EDGE-SHELL-12. A no-op rebuild under the cursor closes an open menu.** Required natively: property-change notifications for the Brand items fire only when a computed value changes (the view model compares before raising `PropertyChanged`), so an open submenu is not disturbed. From `701d4aa`. `src/main/menu.ts:114-126`.

**EDGE-SHELL-13. A menu listener re-registered on every project change is missing for a frame, and a click in that frame does nothing.** Required natively: the Brand commands resolve the open project at execution time from the navigation state, not from a captured value. From `701d4aa`. `src/renderer/project/App.tsx:409-431`.

**EDGE-SHELL-14. Capture errors during a recording reached only the hidden main window.** Required: the pill shows them (INV-SHELL-12). From `c3f7707` (#52). `src/renderer/toolbar/App.tsx:25-38`.

**EDGE-SHELL-15. An Error with no message painted a red row with a glyph and no text.** Required: the fallback string when `string.IsNullOrWhiteSpace(message)`. From `c3f7707`. `src/renderer/toolbar/App.tsx:31-36`.

**EDGE-SHELL-16. The error-row dismiss was a bare ✕ about 2 DIP below the destructive Discard ✕.** Required: the worded `Dismiss` chip. From `c3f7707` (second commit in #52). `src/renderer/toolbar/App.tsx:156-167`.

**EDGE-SHELL-17. Elements inside an OS drag region never receive hover, so the truncated error's tooltip never appeared.** Required natively: the glyph and message are not drag handles, and their tooltip must open while the pill is inactive and non-activating (Q-SHELL-3). From `c3f7707`. `toolbar.css:88-106`.

**EDGE-SHELL-18. "Keep shotAI visible during capture" parked the window over the capture and its warning was never true.** Required: the hide is unconditional; a leftover `captureNoHide` key is ignored and not written back (spec 10 preserves unknown keys per `2b14b79`, so "not written" means not interpreted). From `f24b3dc`. `src/main/main.ts:420-425`.

**EDGE-SHELL-19. `webContents.send` does not buffer, so an update result pushed before the renderer subscribed was lost.** Required natively: spec 10's `UpdateService` stores `Pending` before it raises `UpdateAvailable` (INV-INFRA-26); the subscriber (spec 06's notice host) first subscribes, posting each event with `IUiDispatcher.Post`, then reads `Pending` (spec 11 T7 subscribe-then-read), so a result that landed before the view existed is still shown. From `037858d` (#54). `src/main/main.ts:548-554`.

**EDGE-SHELL-20. A dead renderer looked like a clean quit in the log.** Required: INV-SHELL-20. From `772e381`. `src/main/main.ts:561-578`.

**EDGE-SHELL-21. `getGPUFeatureStatus()` read at `ready` reports software defaults that flip to enabled a moment later.** ELECTRON-ONLY. The native render tier is read after the main window's `SourceInitialized`. From HARDENING C1 (`docs/HARDENING-PLAN.md:167`). `src/main/main.ts:364-380`.

**EDGE-SHELL-22. The pill flashes once when a recording starts on a non-empty project, and an error that arrived while idle would reappear at the next session start.** Both come from comparing against a count left over from the previous state. Required natively: the presenter resets its baseline count, flash token and error at every session start (macOS `CapturePill.swift:27-38`), and flashes only for an increase within a session. IMPROVEMENT. Derived from `src/renderer/toolbar/App.tsx:22-51` and `src/main/CaptureController.ts:629-646`.

**EDGE-SHELL-23. Alt+F4 on the (activating) Electron pill closes it mid-recording; the recording continues with no pill and a hidden main window until a relaunch surfaces the main window.** Required natively: the pill cannot be activated (INV-SHELL-6), and its `Closing` is cancelled unless the application is shutting down. IMPROVEMENT. Derived from `src/main/main.ts:338-340`.

**EDGE-SHELL-24. Stop and Discard await the capture queue before the session ends; during that wait the pill still shows its buttons, so a second Stop or a second Discard confirmation is possible.** Required natively: the pill disables all controls from the moment Stop is clicked or Discard is confirmed until the next state push, and shows no second confirmation. IMPROVEMENT. Derived from `src/main/CaptureController.ts:945-990`.

**EDGE-SHELL-25. The pill keeps its dragged position across sessions with no clamp, so after a monitor is disconnected or rearranged it can be shown entirely off screen.** Required natively: at each show, if the pill rectangle does not intersect any monitor work area, dock again. IMPROVEMENT. Derived from `src/main/main.ts:189-191`.

**EDGE-SHELL-26. A drag that starts on one overlay and ends over another monitor produces a rectangle that extends past the first overlay's monitor; only the first overlay draws it, and spec 02 crops on the monitor of the top-left and clips.** Required: parity (no clamp in the overlay; the drawn rectangle is clipped to the starting overlay). Q-SHELL-6. Derived from `src/renderer/overlay/App.tsx:46-48` and spec 02 2.8.

**EDGE-SHELL-27. Esc only reaches the focused overlay, which in Electron is whichever overlay became ready last.** Required natively: activate the overlay on the monitor containing the cursor after all overlays are shown, and handle Esc on every overlay. IMPROVEMENT. `src/main/RegionService.ts:101-105`.

**EDGE-SHELL-28. A WPF `AllowsTransparency` window is hit-test transparent wherever a pixel's alpha is 0, so a fully transparent overlay lets clicks through to the desktop.** Required: the overlay's background is `#01000000` (INV-SHELL-23). Native pitfall, no Electron counterpart (Electron's transparent window receives the mouse everywhere).

**EDGE-SHELL-29. A window with `WS_EX_NOACTIVATE` that is moved through the system move loop (`DragMove`, `HTCAPTION`) is dragged with an outline rather than live content on some Windows versions, and the move loop can activate it.** Required natively: the pill implements its own drag (capture the mouse, track `GetCursorPos` deltas, `SetWindowPos` with `SWP_NOACTIVATE`). Native pitfall; Q-SHELL-4 records the verification.

**EDGE-SHELL-30. Restoring the main window after a no-click screenshot or an area selection happens with no fresh user input to shotAI, and `SetForegroundWindow` is only honored when "the calling process received the last input event" (or the other listed conditions).** The user's click on shotAI is the last input, so it normally works; input in between (the user types during the 350 ms settle) makes Windows flash the taskbar button instead. Required: call `Activate()` and accept the flash fallback; never use `AttachThreadInput` or simulated input to force it. Source: `SetForegroundWindow` remarks (Microsoft Learn).

**EDGE-SHELL-31. A second launch during a recording surfaces the main window over the capture.** Required: parity. The window is an own window, so clicks on it are ignored by capture and it is excluded from grabs. `src/main/main.ts:171-180`.

**EDGE-SHELL-32. Leaving a project while maximized calls `setBounds` on a maximized window (Electron guards only the open direction).** Behavior in Electron is unverified. Required natively: skip in both directions (D6). `src/main/main.ts:243`.

**EDGE-SHELL-33. In full screen (F11) `isMaximized()` is false, so Electron resizes a full-screen window on a scale commit.** Required natively: skip while full screen (D6). Derived from `src/main/main.ts:243`.

**EDGE-SHELL-34. `detailWindowWidth` never returns less than 1010, even on a work area narrower than 1010 DIP; `x` then clamps to `wa.x` and the window runs past the right edge.** Required: parity (the report measures its real width). Example: 1024 x 768 at 125% has a work area about 819 DIP wide. `src/shared/doc-scale.ts:158-165`.

**EDGE-SHELL-35. The initial 740 DIP height exceeds the work area of common small screens (1366 x 768 at 125% gives about 574 DIP of work area height), so the window starts under the taskbar.** Required natively: initial height `min(740, workArea.Height)`, never below `MinHeight` 560, and centered in the work area. IMPROVEMENT.

**EDGE-SHELL-36. In a .NET Release build a `Mutex` held only by a local variable can be collected, releasing the single-instance lock while the app runs.** Required: hold the lock object in a field of the `App` for the process lifetime and dispose it in `OnExit`. Native pitfall.

**EDGE-SHELL-37. The Electron Discard confirmation is a native message box raised by the web contents, not a BrowserWindow: not shielded, not in the own-window hit test, and not topmost relative to the target app.** Required natively: a WPF dialog owned by the pill, topmost, registered (INV-SHELL-1), activated (the user just clicked, so activation is allowed). IMPROVEMENT. Spec 02 EDGE-CAP-39.

**EDGE-SHELL-38. The app quitting while overlays are open (for example Exit from the taskbar jump list, or session end) closes them; the pending selection must resolve `null` rather than leave its caller waiting.** Required: parity. `src/main/RegionService.ts:106-110`.

**EDGE-SHELL-39. The badge shows `round(w * dpr)` of the unrounded DIP width while the result converts the rounded DIP width with Chromium's rounding, so the badge can differ from the captured size by 1 px.** Required natively: the badge shows exactly the width and height of the rectangle that `ToPhysical` would return for the current drag (IMPROVEMENT: what you see is what is captured). `src/renderer/overlay/App.tsx:81-86`, `RegionService.ts:125-130`.

**EDGE-SHELL-40. Tooltips, context menus and drop-downs are separate top-level HWNDs that no window-level registration covers.** Required: INV-SHELL-1 through popup class handlers (7.4.7). Native pitfall; Electron had the same gap for Chromium tooltip windows.

**EDGE-SHELL-41. Settings replaces the project view in the same window without closing the project, so View then Brand stays enabled while Settings is shown and applies to the project behind it.** Required: parity (`projectOpen` is "a project is open", not "the report is visible"). `src/renderer/project/App.tsx:374-381`, `392-407`.

**EDGE-SHELL-42. Window then Close (`Ctrl+W`) closes the main window, which quits the app.** Required: parity. Role table.

**EDGE-SHELL-43. Window then Zoom is present but does nothing on Windows.** ELECTRON-ONLY; not ported (D9).

**EDGE-SHELL-44. The overlay's first click comes into an inactive window.** On Windows an inactive window receives the button-down that activates it, so the drag starts on the first click (macOS needed `acceptsFirstMouse`, `AreaSelect.swift:106-109`). Required: the overlay does not return `MA_NOACTIVATEANDEAT` or otherwise swallow the activating click.

**EDGE-SHELL-45. The Electron startup handlers are registered outside the lock branch.** `app.whenReady().then(...)`, the crash listeners and `window-all-closed` are module-level (`src/main/main.ts:363`, `:566-584`), so a losing second instance or a Squirrel launch that has called `app.quit()` still has them registered; whether `ready` fires after an early quit is UNVERIFIED. Required natively: the losing branch (and every early exit: self-test, `LegacyInstanceGuard`) returns from `OnStartup` before any service, window, timer or background task exists (INV-SHELL-5). Derived from `src/main/main.ts:143-181`, `:363`.

**EDGE-SHELL-46. An owned WPF window is hidden, minimized and closed with its owner and always stays above it.** Setting `Owner = mainWindow` on the pill or an overlay would make `main.Hide()` hide the pill for the whole recording. Required: the pill and the overlays have no `Owner`; only the About dialog (owner main) and the Discard dialog (owner pill) are owned. Electron creates both without a parent (`src/main/main.ts:312-332`, `src/main/RegionService.ts:55-79`). Native pitfall.

**EDGE-SHELL-47. A second area selection restores the main window from the first one's `finally` over the new overlays.** See 2.5.1 (re-entrancy quirk). Required natively: the `TaskCompletionSource` is created with `TaskCreationOptions.RunContinuationsAsynchronously`, and a caller's `finally` restores the requester only if no newer selection is pending (generation check). IMPROVEMENT; not reachable from the UI today. Derived from `src/main/ipc.ts:943-959`, `src/main/RegionService.ts:44-51`.

**EDGE-SHELL-48. Moving a PerMonitorV2 window programmatically onto a monitor with a different DPI makes Windows send `WM_DPICHANGED`, and WPF then applies the suggested rectangle, which rescales the size just set.** Setting a physical rectangle computed for the target monitor in one `SetWindowPos` can therefore end up scaled twice. Required: every cross-monitor placement (main window initial placement, pill docking, overlays) moves the window onto the target monitor first, lets WPF process the DPI change, then sets the final physical rectangle (or re-applies it from the `DpiChanged` handler, as the overlays do). Whether a HIDDEN window receives `WM_DPICHANGED` when moved is UNVERIFIED, so the pill docking must be checked on a mixed-DPI rig (AC-SHELL-7). Native pitfall.

**EDGE-SHELL-49. A second launch that runs while the first instance is still inside `OnStartup` (a fast double-click) finds the lock held but no activation window yet.** It logs `second instance: no running window found` and exits; the first instance then shows its window normally. Required: parity in outcome (Electron's `second-instance` handler found `projectWindow` still null and did nothing). Derived from `src/main/main.ts:171-180`.

**EDGE-SHELL-50. With no display reported, Electron's `selectArea()` would never resolve.** `screen.getAllDisplays()` is never empty in practice. Required natively: an empty `MonitorQueries.All()` resolves `null` at once and restores the requester (macOS does the same, `macOS:shotAI/Capture/AreaSelect.swift:31`). IMPROVEMENT. `src/main/RegionService.ts:44-51`.

**EDGE-SHELL-51. The pill's `Closing` veto (EDGE-SHELL-23) must not block the app's own shutdown.** WPF has no public "shutting down" flag, so the App sets its own `IsShuttingDown = true` BEFORE it closes other windows: at the start of `MainWindow.Closed`, before `Application.Shutdown()` from File then Exit, and in `SessionEnding`. Otherwise the main window's `Closed` handler asks the pill to close and the pill cancels. Native pitfall.

---

## 6. macOS port notes

| Topic | macOS implementation | Divergence from Electron | Lesson for Windows |
|---|---|---|---|
| Pill window | `NSPanel` `[.nonactivatingPanel, .borderless, .utilityWindow]`, level `.statusBar`, `canJoinAllSpaces`, `fullScreenAuxiliary`, `transient`, `ignoresCycle`, `becomesKeyOnlyIfNeeded`, `hidesOnDeactivate = false`, `isMovableByWindowBackground`, clear background, shadow, `sharingType = .none`, not restorable (`macOS:shotAI/Capture/CapturePill.swift:65-92`) | non-activating; rounded 10 pt; 380 x 62 | Adopt non-activation (`WS_EX_NOACTIVATE`, `ShowActivated = false`). Keep Electron's 74 DIP height and rectangle (Windows hint text is one line longer in pixels; Q-SHELL-2 for corners). |
| First click on a non-activating panel | `FirstMouseHostingView.acceptsFirstMouse` returns true, or pill buttons were dead by mouse (`:115-122`) | none in Electron | WPF buttons in a `WS_EX_NOACTIVATE` window receive the first click, but WPF must not try to focus them: set `Focusable = false` on the window's controls (7.6.2) and test the first click (AC-SHELL-9). |
| Docking | top-center of `mainWindow?.screen ?? NSScreen.main`, `visibleFrame`, 8 pt gap, once per panel instance (`:94-102`) | same rule | Same rule, computed in physical px (7.4.3). |
| Pill state reset | `show()` clears error and flash token each session (`:27-38`) | Electron does not reset | Adopt (EDGE-SHELL-22). |
| Error surface | compact `Error` chip in row 1 with the message in a tooltip and "Click to dismiss" (`:212-240`) | Windows puts the message in row 2 (`c3f7707`: row 1 would be about 425 DIP wide) | Keep the Windows row-2 design. |
| Discard confirmation | `NSAlert` "Discard this capture?" with the informative line, buttons `Discard` and `Cancel`, `.warning`, `.modalPanel` level, after `NSApp.activate()`; deferred off the button's own stack to avoid re-entrancy (`macOS:shotAI/Capture/CaptureCoordinator.swift:226-266`) | real buttons instead of OK | Defer the dialog with `IUiDispatcher.Post` (never a raw `Dispatcher.BeginInvoke`, which is banned in the App, ARCHITECTURE 14.9) so the pill button's handler unwinds first; use worded buttons (7.6.3, Q-SHELL-7). |
| Recording start | hides the main window and calls `NSApp.deactivate()` regardless, so the first capture click is not eaten by the own-app-frontmost guard (`CaptureCoordinator.swift:300-316`) | Electron activates the pill | On Windows hiding the foreground main window already hands the foreground to another window, and the non-activating pill never takes it back. No explicit deactivate call is needed; AC-SHELL-10 checks the first click. |
| Area overlay | one borderless window per `NSScreen`, level `.screenSaver`, `sharingType = .none`, crosshair via tracking area, confirm on mouse-up at 4 x 4 pt, right or other mouse-down cancels, Esc (keyCode 53) cancels, dims only after a drag exists, 2 pt `#6366f1` border, badge `<w> × <h>px` from `backingScaleFactor` at 40 x 22, hint box at 14% with the same two strings (`macOS:shotAI/Capture/AreaSelect.swift:48-236`) | matches Electron closely; result rounded with `.rounded()` in points | Port the Electron numbers; the macOS file confirms them independently. |
| Overlay activation | `NSApp.activate(ignoringOtherApps:)` after ordering all overlays so Esc works and the first click starts the drag (`AreaSelect.swift:27-31`) | Electron focuses the last-ready overlay | Activate the overlay under the cursor (EDGE-SHELL-27). |
| Single resolution | a new `selectArea` finishes the previous with nil; every overlay closing finishes (`AreaSelect.swift:15-45`) | same | Same. |
| Main window sizing | `WindowLayout.home = 800`, `detail = 1040`, `reportChrome = 160`; driven by the committed scale, grow-only, skipped when zoomed in BOTH directions (the model for D6), centered and clamped to `visibleFrame` with the WIDTH also capped at the visible width (Electron does not cap it, EDGE-SHELL-34), animated 0.28 s; held while the pointer is on the stepper (`macOS:shotAI/ContentView.swift:9-18`, `173-205`, `294-334`) | different widths (macOS chrome differs), animation, stepper hold | Keep Electron's 720, 1010 and 130. Do not animate (Electron does not). The stepper hold is a spec 05 question (Q-SHELL-13). |
| Window restoration | state restoration disabled (`NSQuitAlwaysKeepsWindows` false), but SwiftUI's frame autosave is KEPT (position and size are remembered); the persisted frame width is coerced to Home before launch and orphaned autosave keys purged (#56) (`macOS:shotAI/shotAIApp.swift:13-22`, `158-202`) | Electron persists nothing | Persist nothing (Q-SHELL-15), which avoids the whole class of bugs. |
| Quit | `applicationShouldTerminate` returns `.terminateNow`; Quit menu tears down triggers then `exit(0)`; sheets were replaced by overlays because a sheet vetoes quit (`shotAIApp.swift:63-73`, `220-231`; `ContentView.swift:229-233`) | Electron has no veto | WPF has no veto either; make sure no `Closing` handler on the main window cancels (INV-SHELL-4). |
| Menu | About panel with credits `Local-first SOP builder \u2014 record a process and let Claude write the step-by-step guide.`; `Check for Updates…`; `Quit shotAI`; File then Export and Import Package; View then Brand as an inline picker with `App Default (<label>)` and a fourth, unticked `.unrecognised` state (#118); Help then `Export shotAI Logs…` (`shotAIApp.swift:51-134`) | several extra items; capital D in "Default" | Keep the Windows menu. The macOS extras are candidates after cutover (Q-SHELL-10). |
| Record and immediate sheets | `RecordSheet` and `ImmediateCaptureSheet` pick a target from lists; there is no click-to-pick-a-window mode on either platform (`macOS:shotAI/Capture/RecordSheet.swift:83-127`, `macOS:shotAI/Capture/ImmediateCaptureSheet.swift:59-94`) | macOS `captureAreaNow` goes straight to the overlay (`CaptureCoordinator.swift:134-157`) | Window selection is a list (spec 06). No window click-pick exists to port (Q-SHELL-9). |
| Update badge | inline capsule on Home, never over a report or during a recording (`macOS:shotAI/UpdateBadge.swift:4-9`) | Windows equivalent is in the renderer | Spec 10 and 06; the shell only guarantees the event is not lost (EDGE-SHELL-19). |

---

## 7. Native design (C#)

### 7.1 Placement

| Project | Namespace | Types |
|---|---|---|
| ShotAI.Core | `ShotAI.Core.Shell` | `ShellConstants`, `ShellStrings`, `DipRect`, `PixelRect`, `WindowLayout`, `PillDocking`, `PillPresenter`, `PillViewState`, `PillStatusRow`, `RecordingVisibilityPlanner`, `ShellAction`, `AreaSelectionMath`, `BrandMenuModel`, `BrandMenuItem`, `BrandMenuInput`, `UiZoom`, `AboutText`, `RuntimeDiagnostics`, `RenderModePolicy`, `SingleInstanceIdentity` |
| ShotAI.Platform | `ShotAI.Platform.Shell` | `SingleInstanceLock`, `ExistingInstance`, `WindowStyles`, `MonitorQueries`, `MonitorDescriptorEx`, `Foreground`, `ProcessMachine` |
| ShotAI.App | `ShotAI.App` | `Program` (explicit `Main`, spec 12 7.9.1), `App` (composition root), `AppPaths` (implements spec 10's `IAppPaths` and spec 09's `IBrandFontSource`, 2.10.10) |
| ShotAI.App | `ShotAI.App.Shell` | `ShotAIWindow` (base class, 7.4.7), `ShotAIPopup`, `MainWindow`, `MainWindowSizer` (implements `IMainWindowLayout`), `IMainWindowLayout`, `IAreaSelectionService`, `CapturePillWindow`, `CapturePillViewModel`, `DiscardConfirmWindow`, `AreaOverlayWindow`, `AreaSelectionService` (implements `IAreaSelectionService`), `RecordingVisibilityController`, `AppMenuViewModel`, `AboutWindow`, `ActivationListener`, `PopupExclusion`, `WindowRegistration`, `CrashLogging` |

The scaffold's `src/ShotAI.App/MainWindow.xaml` and `MainWindow.xaml.cs` (namespace `ShotAI.App`, `dotnet/src/ShotAI.App/MainWindow.xaml.cs:5`, `dotnet/src/ShotAI.App/MainWindow.xaml:1`) move to `src/ShotAI.App/Shell/` with namespace `ShotAI.App.Shell` and `x:Class="ShotAI.App.Shell.MainWindow"` in the WP that makes it derive from `ShotAIWindow` (PLAN WP-A13); `StartupUri` (`dotnet/src/ShotAI.App/App.xaml:4`) is already dropped by 7.4.1.

Placement follows ARCHITECTURE 2.4 (folder equals namespace, one public type per file). `IAreaSelectionService` and `IMainWindowLayout` are App interfaces in `ShotAI.App.Shell` (spec 11 7.3 catalog, ARCHITECTURE 4.4), UI thread only. Spec 12's `LegacyInstanceGuard` lives in `ShotAI.App.Startup`, and the Platform `ProcessSnapshot` it uses in `ShotAI.Platform.Processes` (ARCHITECTURE 2.4). The Platform types above are public helpers the App calls directly and have no Core counterpart (ARCHITECTURE INV-ARCH-4). Every view model here (`CapturePillViewModel`, `AppMenuViewModel`) derives from spec 11's `ViewModelBase : ObservableObject` (ARCHITECTURE 5.1) and depends only on catalog interfaces, value types and `ILogger<T>` (INV-ARCH-3).

Core stays free of WPF and Win32 types: geometry is plain records, strings are constants, and the presenters are plain classes with `INotifyPropertyChanged` from `CommunityToolkit.Mvvm` (platform neutral, allowed in Core by ARCHITECTURE 3.5). `JsMath` is spec 01's `ShotAI.Core.Json.JsMath`, the one and only copy (spec 01 7.1 placement table; R-ARCH-1 removed the second home spec 02 once listed under `ShotAI.Core.Capture`). `Rect` is spec 01's `ShotAI.Core.Model.Rect`; in `ShotAI.App` files that also import `System.Windows` it collides with `System.Windows.Rect`, so those files use an alias (`using ModelRect = ShotAI.Core.Model.Rect;`). Pure logic that needs a brand uses spec 10's static `ShotAI.Core.Brand.BrandPalette`.

### 7.2 Core contracts

```csharp
namespace ShotAI.Core.Shell;

public readonly record struct DipRect(double X, double Y, double Width, double Height);
public readonly record struct PixelRect(int X, int Y, int Width, int Height)
{ public int Right => X + Width; public int Bottom => Y + Height;
  public bool Intersects(PixelRect o) => X < o.Right && Right > o.X && Y < o.Bottom && Bottom > o.Y; }

public static class ShellConstants
{
    public const double ListWidth = 720, InitialHeight = 740, MinWidth = 680, MinHeight = 560;
    public const double PillWidth = 380, PillHeight = 74, PillDockGap = 8;
    public const double MinDrag = 4, BadgeMinWidth = 40, BadgeMinHeight = 22;
    public const double OverlayHintTopFraction = 0.14;
    public const int FlashMs = 700, PulseMs = 1400, PillTooltipDelayMs = 400;
}

public static class ShellStrings
{
    public const string AppName = "shotAI";
    public const string PillTitle = "shotAI \u2014 Capture";
    public const string OverlayTitle = "shotAI \u2014 Select area";
    public const string PillIdleLabel = "shotAI";
    public static string PillActiveLabel(bool paused, int count) => $"{(paused ? "Paused" : "Capturing")} · {count}";
    public const string HintRecording = "Click anything to capture a step · Ctrl+Shift+S";
    public const string HintPaused = "Paused \u2014 press Resume to keep capturing";
    public const string ErrorFallback = "A capture failed \u2014 see the log for details.";
    public const string Pause = "❚❚ Pause", Resume = "▶ Resume", Stop = "■ Stop", Discard = "✕", ErrorGlyph = "⚠";
    public const string PauseTip = "Pause", ResumeTip = "Resume", StopTip = "Stop & finish",
                        DiscardTip = "Discard this capture", DragTip = "Drag to move";
    public const string Dismiss = "Dismiss", DismissTip = "Dismiss this error", DismissName = "Dismiss this capture error";
    public const string DiscardWholeProject = "Discard this capture? This is a new project, so the entire project will be deleted.";
    public const string DiscardSessionSteps = "Discard this capture? Steps recorded in this session will be deleted.";
    public const string OverlayHint = "Drag to select a capture area", OverlayHintSub = "Press Esc to cancel";
    public static string Badge(int w, int h) => $"{w} × {h}px";
    public const string AboutTitle = "About shotAI";
    public const string AboutTagline = "Local-first SOP builder \u2014 capture a process and let Claude write the guide.";
    public static string BrandAppDefault(string resolvedLabel) => $"App default ({resolvedLabel})";
    // menu labels: "File", "Import Project…", "Settings", "Exit", "Edit", "Undo", "Redo", "Cut", "Copy",
    // "Paste", "Delete", "Select All", "View", "Actual Size", "Zoom In", "Zoom Out", "Toggle Full Screen",
    // "Brand", "Window", "Minimize", "Close", "Help", "About shotAI"
}
```

**WindowLayout** (all DIP, one monitor's scale for every input):

```csharp
public static class WindowLayout
{
    /// Electron setDetailView (2.3.2) plus D6. Returns null for "no change".
    public static DipRect? DetailResize(DipRect current, DipRect workArea, bool open, double scale,
                                        bool maximized, bool fullScreen)
    {
        if (maximized || fullScreen) return null;                               // D6 (Electron: open && maximized only)
        double target = DocScale.DetailWindowWidth(scale, workArea.Width);       // formula 2.3.2
        double newW = open ? Math.Max(current.Width, target) : ShellConstants.ListWidth;
        if (current.Width == newW) return null;
        double centerX = current.X + current.Width / 2;
        double x = Math.Max(workArea.X, Math.Min(JsMath.Round(centerX - newW / 2), workArea.X + workArea.Width - newW));
        return new DipRect(x, current.Y, newW, current.Height);
    }

    /// Initial placement (EDGE-SHELL-35): centered in the work area, height clamped.
    public static DipRect Initial(DipRect workArea)
    {
        double h = Math.Max(ShellConstants.MinHeight, Math.Min(ShellConstants.InitialHeight, workArea.Height));
        double w = ShellConstants.ListWidth;
        return new DipRect(JsMath.Round(workArea.X + (workArea.Width - w) / 2),
                           JsMath.Round(workArea.Y + Math.Max(0, (workArea.Height - h) / 2)), w, h);
    }
}
```

`DocScale.DetailWindowWidth` is the Core port of `detailWindowWidth` (owned by spec 05 with the rest of `doc-scale.ts`); its formula is fixed in 2.3.2 and pinned by `WindowLayoutTests` regardless of owner. `current.Width == newW` compares doubles that are integral in practice (the App rounds physical sizes back to DIP with the same scale); the App passes widths rounded to 1/1000 DIP to avoid a false inequality from floating error.

**PillDocking** (physical px, INV-SHELL-8):

```csharp
public static class PillDocking
{
    public static (int X, int Y) TopCenter(PixelRect workArea, int pillWidthPx, double scale) =>
        (workArea.X + (int)JsMath.Round((workArea.Width - pillWidthPx) / 2.0),
         workArea.Y + (int)JsMath.Round(ShellConstants.PillDockGap * scale));
    public static bool NeedsRedock(PixelRect pill, IReadOnlyList<PixelRect> workAreas) =>
        !workAreas.Any(w => w.Intersects(pill));                                 // EDGE-SHELL-25
}
```

Equivalence with Electron: Electron computes `round(area.x + (area.width - 380) / 2)` in DIP on the monitor; multiplying through by `s` gives the physical form above to within 1 px.

**PillPresenter** (the pill's logic; the WPF view model wraps it):

```csharp
public enum PillStatus { Idle, Recording, Paused }
public enum PillStatusRow { None, Hint, Error }

public sealed record PillViewState(
    PillStatus Status, string Label, bool ShowRecDot, bool RecDotPulsing, bool ShowControls,
    bool ShowPause, bool ShowResume, bool ControlsEnabled, PillStatusRow Row2, string? Hint,
    string? Error, bool AccentBar, bool AccentIsError, int FlashToken, bool DiscardDeletesProject);

public sealed class PillPresenter
{
    public PillViewState View { get; }                       // recomputed after every call below
    public event EventHandler? Changed;
    public void OnSessionShown(CaptureState s);             // resets error, flash token and baseline to s.StepCount (EDGE-SHELL-22)
    public void OnState(CaptureState s);                    // flash when active && s.StepCount > baseline (then clears error); baseline = s.StepCount;
                                                            // !active clears error; ControlsEnabled = true on any push
    public void OnError(string? message);                   // IsNullOrWhiteSpace -> ShellStrings.ErrorFallback, else message unchanged
    public void Dismiss();                                  // error = null
    public void OnStopRequested();                          // ControlsEnabled = false (EDGE-SHELL-24)
    public string DiscardMessage { get; }                   // from the last state's WillDeleteProjectOnDiscard
    public void OnDiscardConfirmed();                       // ControlsEnabled = false
}
```

`View` mapping, exactly 2.4.4: `Label = Idle ? "shotAI" : ShellStrings.PillActiveLabel(paused, count)`; `ShowRecDot = active`; `RecDotPulsing = Recording`; `ShowPause = Recording`; `ShowResume = Paused`; `Row2 = !active ? None : (Error != null ? Error : Hint)`; `Hint = Paused ? HintPaused : HintRecording`; `AccentBar = active`; `AccentIsError = active && Error != null`. The error is stored whenever it arrives but surfaced only while active (INV-SHELL-12).

**RecordingVisibilityPlanner** (pure, INV-SHELL-7):

```csharp
public enum ShellAction { HideMain, DockPill, ShowPill, HidePill, ShowMain, ActivateMain }
public sealed class RecordingVisibilityPlanner
{
    bool _pillDocked;                                       // once per app run (the pill lives for the run)
    public IReadOnlyList<ShellAction> Plan(bool recording, bool showPill, bool pillOffScreen)
    {
        if (recording)
        {
            var a = new List<ShellAction> { ShellAction.HideMain };
            if (showPill)
            {
                if (!_pillDocked || pillOffScreen) { a.Add(ShellAction.DockPill); _pillDocked = true; }
                a.Add(ShellAction.ShowPill);
            }
            return a;
        }
        return [ShellAction.HidePill, ShellAction.ShowMain, ShellAction.ActivateMain];
    }
}
```

**AreaSelectionMath** (INV-SHELL-14, INV-SHELL-15):

```csharp
public static class AreaSelectionMath
{
    public static DipRect Normalize(double sx, double sy, double cx, double cy) =>
        new(Math.Min(sx, cx), Math.Min(sy, cy), Math.Abs(cx - sx), Math.Abs(cy - sy));
    public static bool IsSelection(DipRect r) => r.Width >= ShellConstants.MinDrag && r.Height >= ShellConstants.MinDrag;
    public static bool ShowBadge(DipRect r) => r.Width >= ShellConstants.BadgeMinWidth && r.Height >= ShellConstants.BadgeMinHeight;

    /// Overlay-local DIP -> global physical px. monitor = rcMonitor, scale = overlay DPI / 96.
    /// Parity with Electron: round the DIP rect to integers first, then Chromium's DIPToScreenRect
    /// (origin floored, size ceiled; Q-SHELL-5).
    public static Rect ToPhysical(DipRect sel, PixelRect monitor, double scale)
    {
        double x0 = JsMath.Round(sel.X), y0 = JsMath.Round(sel.Y);
        double w0 = JsMath.Round(sel.Width), h0 = JsMath.Round(sel.Height);
        return new Rect(monitor.X + Math.Floor(x0 * scale), monitor.Y + Math.Floor(y0 * scale),
                        Math.Ceiling(w0 * scale), Math.Ceiling(h0 * scale));
    }
    public static string BadgeText(DipRect sel, PixelRect monitor, double scale)
    { var p = ToPhysical(sel, monitor, scale); return ShellStrings.Badge((int)p.Width, (int)p.Height); }   // EDGE-SHELL-39
}
```

`Rect` is spec 01's model rectangle (doubles holding integral values), the type `CaptureTarget.Area` uses.

**BrandMenuModel** (INV-SHELL-16):

```csharp
public sealed record BrandMenuInput(bool ProjectOpen, string? RawProjectTheme, string? AppBrand);
public sealed record BrandMenuItem(string Label, string? BrandId, bool IsChecked);   // BrandId null = App default
public static class BrandMenuModel
{
    public static bool SubmenuEnabled(BrandMenuInput i) => i.ProjectOpen;
    public static IReadOnlyList<BrandMenuItem> Items(BrandMenuInput i)
    {
        string? pinned = i.ProjectOpen ? BrandPalette.PinnedBrand(i.RawProjectTheme) : null;          // unknown -> null (#95)
        bool unrecognised = i.ProjectOpen && BrandPalette.PinIsUnrecognised(i.RawProjectTheme);        // #107
        string app = BrandPalette.CoerceBrand(i.AppBrand);                                            // unknown -> default
        var items = new List<BrandMenuItem>
            { new(ShellStrings.BrandAppDefault(BrandPalette.Get(app).Label), null, !unrecognised && pinned is null) };
        foreach (var id in BrandPalette.BrandIds)                                                     // every brand, default included (#77)
            items.Add(new(BrandPalette.Get(id).Label, id, string.Equals(pinned, id, StringComparison.Ordinal)));
        return items;
    }
}
```

`BrandPalette` is spec 10's static Core class in `ShotAI.Core.Brand` (`BrandIds` in `contract/brand.json` order, `Get(id).Label`, `PinnedBrand`, `CoerceBrand`, `PinIsUnrecognised` with the exact rule "a non-empty string that is not a brand id" (INV-INFRA-11), and `IsBrandId` using an ordinal key lookup, never a prototype-like fallback). The Electron main process narrows `projectTheme` with `pinnedBrand` and `appBrand` with `coerceBrand` (`src/main/main.ts:476-484`); the model above applies the same two functions to the raw manifest value, so the result is identical. When `ProjectOpen` is false the items are still computed (the submenu is disabled), exactly as Electron built a disabled submenu from `projectTheme: null`. Natively the menu reads the raw `theme` string from the open manifest, as macOS does, so the Electron-only `projectPinUnrecognised` IPC field is unnecessary (IMPROVEMENT, simpler, same result).

**UiZoom**: `Factor(level) = Math.Clamp(Math.Pow(1.2, level), 0.25, 5.0)`; `ZoomIn(level) = level + 0.5`, `ZoomOut(level) = level - 0.5`, each then clamped to the level range whose factor stays in `[0.25, 5.0]`; `ActualSize = 0`.

**AboutText**: `Message(version) = $"shotAI {version}"`; `Detail(dotnetVersion, webView2Version, arch) = ShellStrings.AboutTagline + "\n\n" + $".NET {dotnetVersion} · WebView2 {webView2Version ?? "not installed"}" + "\n" + $"win32/{arch}"` with `arch` in `x64` or `arm64` (D11). Every argument comes from spec 11's `IAppInfo.Current` (`AppInfo(Name, Version, Platform, Arch, DotNetVersion, WebView2Version)`, spec 11 7.3.5): `version` = `AppInfo.Version` (the informational version without the `+<commit>` suffix), `dotnetVersion` = `AppInfo.DotNetVersion`, `arch` = `AppInfo.Arch`, and `webView2Version` = `AppInfo.WebView2Version`, which `AppInfoProvider` reads from Platform's `IWebView2RuntimeInfo.GetVersion()` (seam declared in `ShotAI.Core.Diagnostics`, implemented by `ShotAI.Platform.Export.WebView2RuntimeInfo`) and which is `null` when the runtime is missing or the probe fails, shown as `not installed` (R-ARCH-12, INV-ARCH-5).

**RuntimeDiagnostics** (the diagnostic remnant of `gpu-policy.ts`):

```csharp
public enum Machine { X86, X64, Arm64, Other }
public static class RuntimeDiagnostics
{
    /// True when an x86-family process runs on a non-x86 native machine (x64 under ARM64 emulation).
    /// WOW64 (x86 on x64) is not emulation. Same rule as isEmulatedOnArm (gpu-policy.ts:32-43).
    public static bool IsEmulated(Machine process, Machine native) =>
        (process is Machine.X64 or Machine.X86) && native is not (Machine.X64 or Machine.X86);
    public static string Name(Machine m) => m switch
        { Machine.X86 => "x86", Machine.X64 => "x64", Machine.Arm64 => "arm64", _ => "other" };   // Electron's process.arch spelling
    public static string RuntimeLine(Machine process, Machine native, string dotnet, string os, int renderTier) =>
        $"runtime: win32/{Name(process)} · native {Name(native)}{(IsEmulated(process, native) ? " (emulated)" : "")} · .NET {dotnet} · os {os} · render tier {renderTier}";
}
public static class RenderModePolicy
{
    /// SHOTAI_ENABLE_GPU: "0" forces software rendering; anything else (including "1" and unset) is the WPF default.
    public static bool ForceSoftware(string? shotaiEnableGpu) => shotaiEnableGpu == "0";
}
```

**SingleInstanceIdentity**: `MutexName(sid) = $"Local\\shotAI.SingleInstance.{sid}"`; `ActivationWindowName(sid) = $"shotAI.Activation.{sid}"`; `ActivationMessageName = "shotAI.ActivateMainWindow"`. `sid` is the string SID of the process user (`WindowsIdentity.GetCurrent().User.Value`, read in App and passed in), which never contains characters invalid in a kernel object name (a SID string has no backslash; the only backslash is the `Local\` namespace separator).

`RuntimeLine` arguments: `dotnet` is `Environment.Version.ToString()`, `os` is `Environment.OSVersion.Version.ToString()` (for example `10.0.26100.0`), `renderTier` is `RenderCapability.Tier >> 16` (0, 1 or 2).

### 7.3 Platform types

```csharp
namespace ShotAI.Platform.Shell;

public sealed class SingleInstanceLock : IDisposable
{
    /// var m = new Mutex(initiallyOwned: false, mutexName);
    /// bool owned; try { owned = m.WaitOne(0); } catch (AbandonedMutexException) { owned = true; }
    /// if (!owned) { m.Dispose(); return null; }  return new SingleInstanceLock(m);
    /// (The constructor never throws AbandonedMutexException; only a wait reports abandonment, which is why
    /// the lock is taken with WaitOne(0) rather than initiallyOwned: true plus createdNew.)
    public static SingleInstanceLock? TryAcquire(string mutexName);
    public void Dispose();                               // ReleaseMutex, then Dispose; idempotent; MUST run on the
                                                         // acquiring thread (the UI thread): a Mutex is thread-affine and
                                                         // ReleaseMutex from another thread throws ApplicationException
}

public static class ExistingInstance
{
    /// hwnd = FindWindowExW(HWND_MESSAGE, null, null, windowName); if null return false.
    /// GetWindowThreadProcessId(hwnd, out pid); AllowSetForegroundWindow(pid);   // we are the foreground launch, so we may grant it
    /// PostMessageW(hwnd, RegisterWindowMessageW(messageName), 0, 0); return true.
    public static bool Activate(string windowName, string messageName);
}

public static class WindowStyles
{
    /// GWL_EXSTYLE |= WS_EX_NOACTIVATE | WS_EX_TOOLWINDOW; clears WS_EX_APPWINDOW;
    /// SetWindowPos(hwnd, HWND_TOPMOST, 0,0,0,0, SWP_NOMOVE|SWP_NOSIZE|SWP_NOACTIVATE|SWP_FRAMECHANGED).
    public static void MakeNonActivatingToolWindow(nint hwnd);
    /// GWL_EXSTYLE |= WS_EX_TOOLWINDOW (overlays: out of Alt+Tab).
    public static void MakeToolWindow(nint hwnd);
    public static void MoveNoActivate(nint hwnd, int x, int y);                  // SWP_NOSIZE|SWP_NOZORDER|SWP_NOACTIVATE
    public static void SetBoundsNoActivate(nint hwnd, PixelRect r, bool topmost); // SWP_NOACTIVATE (+ HWND_TOPMOST)
    public static PixelRect GetWindowRect(nint hwnd);
}

public sealed record MonitorDescriptorEx(nint Handle, PixelRect Bounds, PixelRect WorkArea, double Scale, bool IsPrimary);
public static class MonitorQueries
{
    public static IReadOnlyList<MonitorDescriptorEx> All();         // EnumDisplayMonitors + GetMonitorInfoW + GetDpiForMonitor(MDT_EFFECTIVE_DPI)
    public static MonitorDescriptorEx ForWindow(nint hwnd);          // MonitorFromWindow(MONITOR_DEFAULTTONEAREST): greatest intersection
    public static MonitorDescriptorEx ForPoint(int x, int y);        // MonitorFromPoint(MONITOR_DEFAULTTONEAREST)
    public static MonitorDescriptorEx Primary();                     // MonitorFromPoint(0,0, MONITOR_DEFAULTTOPRIMARY)
    public static (int X, int Y) CursorPosition();                   // GetCursorPos
}

public static class Foreground
{
    public static bool TryActivate(nint hwnd);   // if IsIconic: ShowWindow(SW_RESTORE); SetForegroundWindow; returns GetForegroundWindow() == hwnd
}

public static class ProcessMachine
{
    public static (Machine Process, Machine Native) Current();       // IsWow64Process2(GetCurrentProcess(), out processMachine, out nativeMachine);
                                                                     // IMAGE_FILE_MACHINE_UNKNOWN process machine means "not WOW64": use RuntimeInformation.ProcessArchitecture
}
```

All of these are public helpers the App calls directly (ARCHITECTURE INV-ARCH-4); CsWin32 output stays `internal` behind them (ARCHITECTURE 14.7). `MonitorQueries` shares its enumeration code with spec 02's `GdiMonitorCapture.Monitors()`; if spec 02 lands first, extend its descriptor with `WorkArea` instead of adding a second enumerator. (Spec 02's `MonitorDescriptor` is `(uint Id, string Name, Rect Bounds, double ScaleFactor, bool IsPrimary)`, with `Rect` of doubles; `MonitorDescriptorEx` here uses integer `PixelRect` and adds `Handle` and `WorkArea`, `WorkArea = MONITORINFO.rcWork`.) `ExistingInstance.Activate` must not block: `PostMessageW`, never `SendMessage`, so a hung first instance cannot hang the second launch.

### 7.4 App design

#### 7.4.1 Composition root and startup (`App`)

The canonical sequence is ARCHITECTURE 4.2 (steps 0 to 13); the numbering below matches it, with ARCHITECTURE's steps 1b, 2a, 5 and 5b folded into steps 1, 2 and 5 here. Entry (step 0) is spec 12's explicit `ShotAI.App.Program.Main` (`[STAThread]`, first statement `ShotAI.Platform.DllSearchHardening.Apply()`, which calls `SetDefaultDllDirectories(LOAD_LIBRARY_SEARCH_DEFAULT_DIRS)` and `Environment.FailFast` on failure, spec 12 7.9.1, INV-PKG-16), which then runs `new App().Run()` (with `app.InitializeComponent()` for the XAML resources). Because of the explicit `Main`, `App.xaml` is built as a `Page`, not as `ApplicationDefinition` (set `<ApplicationDefinition Remove="App.xaml" />` and `<Page Include="App.xaml" />` in `ShotAI.App.csproj`, or WPF generates a second `Main` and the build fails), and it drops the scaffold's `StartupUri="MainWindow.xaml"`. `ShutdownMode = OnMainWindowClose`. `App.OnStartup(StartupEventArgs e)`, in order:

1. `CrashLogging.Install()`, then the bootstrap `LoggerFactory` with spec 10's first-party `FileLoggerProvider` (no third-party logging package, ARCHITECTURE 8.4): the first log line is the startup banner; the sink opens the log per batch with `FileShare.ReadWrite | FileShare.Delete` so a second instance can write its one line. Then (ARCHITECTURE step 1b) spec 12's `PersonalCopyGuard.TryHandOff(e.Args, selfTestMode)` (`selfTestMode` is `StartupModeParser.Parse(e.Args, Environment.GetEnvironmentVariable).Kind != StartupModeKind.Normal`, spec 10 7.8), constructed directly with `InstallInfoReader.Read(AppContext.BaseDirectory)`, `new ProcessStarter()` and the bootstrap logger: when this process is a personal (per-user) copy and a copy for all users exists, it starts that copy with the same arguments and returns true, and `OnStartup` calls `Shutdown(0)` and returns before the mutex, so the started copy can take the mutex or activate a running instance (spec 12 7.10.4, INV-PKG-38). Nothing else has run. The returned `IInstallInfo` is kept and registered as an instance in step 6 by `AddShotAIApp` (06 reads its scope for the update notice, 06 INV-HOME-45).
2. `string sid = WindowsIdentity.GetCurrent().User!.Value`. `_instanceLock = SingleInstanceLock.TryAcquire(SingleInstanceIdentity.MutexName(sid))` (a field; EDGE-SHELL-36). If `null`: log info `another instance already holds the lock \u2014 exiting.`; `ExistingInstance.Activate(...)` (log debug if it returns false); `Shutdown(0)`; return. No settings, store or window is touched (INV-SHELL-5, EDGE-SHELL-45). Then (step 2a) spec 12's `LegacyInstanceGuard.FindRunningElectron()` (INV-PKG-26), constructed directly with `new ProcessSnapshot()` because the container does not exist yet: on a hit it logs, shows its notice, and `Shutdown(0)`; return. It runs after the mutex and before any settings or project I/O (spec 12 7.10.3). Its notice is a `MessageBox` shown before any window exists, the one allowlisted exception to this spec's no-`MessageBox` rule until stage S5 (Q-SHELL-19; ARCHITECTURE 14.9 allowlists `LegacyInstanceGuard.cs`).
3. Self-test switches (spec 10, spec 02 2.16): run, `Shutdown(exitCode)`, return.
4. `RenderModePolicy.ForceSoftware(Environment.GetEnvironmentVariable("SHOTAI_ENABLE_GPU"))` sets `RenderOptions.ProcessRenderMode = RenderMode.SoftwareOnly` and logs `render: software forced (SHOTAI_ENABLE_GPU=0)`.
5. Environment sanitization (ARCHITECTURE 4.2 step 5; spec 08 INV-AUTH-35, Q-ARCH-3): remove `ANTHROPIC_CUSTOM_HEADERS` from the process environment, and (Q-ARCH-3 default) `HTTPS_PROXY`, `HTTP_PROXY`, `ALL_PROXY`, `NO_PROXY` and their lower-case spellings, logging the NAMES removed at warning, never the values; this must precede the first `HttpClient` and the Anthropic SDK's static constructor. Then (ARCHITECTURE step 5b) load settings synchronously with spec 10's `SettingsService.Load(paths, atomic, time, log)`, which primes `ICaptureSettings` (the capture-scale and remote-visible caches), because the windows need `RemoteVisible` and the theme before the first frame.
6. Build the DI container (`Microsoft.Extensions.DependencyInjection`, `ValidateOnBuild` and `ValidateScopes`, ARCHITECTURE 4.1) through the three extension methods `AddShotAICore`, `AddShotAIPlatform` and `AddShotAIApp` (ARCHITECTURE C7): Core services (store, capture engine, shield), Platform services, and this spec's App singletons in `AddShotAIApp` (`IAreaSelectionService` as `AreaSelectionService`, `IMainWindowLayout` as `MainWindowSizer`, `AppMenuViewModel`, `RecordingVisibilityController`, `CapturePillViewModel`, `PopupExclusion`, the `OwnWindowRegistry` wiring; ARCHITECTURE 4.3). The `SettingsService` loaded in step 5 is registered as an instance.
7. `PopupExclusion.Install()` (7.4.7), before any window.
8. Create `MainWindow`, set `MainWindow = main`; create `CapturePillWindow` and call `new WindowInteropHelper(pill).EnsureHandle()` so it has an HWND (registered and excluded) without being shown. Both register with `OwnWindowRegistry` in `OnSourceInitialized` through `WindowRegistration` (INV-SHELL-1). Then spec 06's `ThemeManager.ApplyInitial` so the theme is in place before the first frame (D-HOME-10).
9. `main.Show()`. Then resolve `IEnumerable<IAppStartup>` and call `Start()` on each in registration order (spec 11 7.10 rule 3: `RemoteVisibilityApplier`, `RecordingVisibilityController`, `ThemeManager`); `Start()` only subscribes, no IO.
10. `CaptureShield.ApplyRemoteVisibility(settings.Current.RemoteVisible)` (INV-SHELL-2; the call happens after both windows exist even though the setting is already in memory). `RemoteVisibilityApplier` never applies at startup; this step is the only startup application (spec 11). It runs on the UI thread only because no capture session can exist yet, so the shield lock is uncontended (spec 02 7.8); every later change reaches `ApplyRemoteVisibility` through `Task.Run`, never inline on the UI thread (ARCHITECTURE DL1), and if this call ever moves after a session could start it goes through `Task.Run` too.
11. `ActivationListener.Start(sid)` (7.4.8).
12. Log the runtime line (`RuntimeDiagnostics.RuntimeLine`, render tier from `RenderCapability.Tier >> 16` read after the main window's `SourceInitialized`) and, on the main window's first `ContentRendered`, the startup timing line `startup: main window rendered in <n> ms` at info, `<n>` measured from `Process.GetCurrentProcess().StartTime` (ARCHITECTURE PB-9, D-ARCH-4).
13. Fire and forget, both on the thread pool (`Task.Run`) with exceptions logged and swallowed, both under `IAppLifetime.Stopping`: startup auto-archive: `IProjectService.AutoArchiveStaleAsync(settings.Current.ArchiveAgeDays, lifetime.Stopping)` (spec 01 INV-MODEL-32 owns the logic; spec 10 owns the setting), which raises `ProjectsChanged` when at least one project moved, and 06's `HomeViewModel` marshals it to the UI thread and re-lists (spec 11 E1; the warning keeps the Electron wording `startup auto-archive failed (non-fatal):`; AC-SHELL-33), and `IUpdateService.RunStartupCheckAsync(lifetime.Stopping)` (spec 10; EDGE-SHELL-19). Both take `IAppLifetime.Stopping` so they end at exit.

`App.OnExit` runs spec 11 7.10's shutdown table (ARCHITECTURE 4.5), in this order: cancel `IAppLifetime.Stopping`; `ICaptureService.Teardown()` (synchronous; INV-SHELL-19); `ShutdownFlush.Run(TimeSpan.FromSeconds(5))` over the project and settings queues together (the one allowlisted blocking wait; R-ARCH-10); `ActivationListener.Dispose()`, then `_instanceLock.Dispose()` on the UI thread that acquired it; `provider.Dispose()` (never `DisposeAsync`, because `OnExit` is synchronous; R-ARCH-10) (spec 11 rule 2: every disposable singleton implements `IDisposable`, because `ServiceProvider.Dispose()` throws for a service that implements only `IAsyncDisposable`; spec 02's `ICaptureService : IAsyncDisposable` implementation must therefore also implement `IDisposable`); log `exiting (code <n>)` and flush the log sink. `App.SessionEnding` (logoff, shutdown): set `IsShuttingDown` (EDGE-SHELL-51), call `Teardown()` first, then let WPF proceed to `Shutdown` and `OnExit`. Nothing is ever vetoed (no unsaved state; spec 12 EDGE-PKG-29 relies on this for upgrades while running).

#### 7.4.2 Main window (`MainWindow`, `MainWindowSizer`)

XAML: `Title="shotAI"`, `Width="720"`, `Height="740"`, `MinWidth="680"`, `MinHeight="560"`, `WindowStartupLocation="Manual"`, `Icon` from the pack URI, `ResizeMode="CanResize"`, the menu (7.4.5) docked top.

- Placement, before the first `Show`: `m = MonitorQueries.ForPoint(cursor)` (the monitor the user launched from; matches WPF `CenterScreen` semantics); `waDip = m.WorkArea / m.Scale`; `r = WindowLayout.Initial(waDip)`; after `SourceInitialized` (still before the window is visible), first `MoveNoActivate(hwnd, m.WorkArea.X, m.WorkArea.Y)` so the HWND adopts the target monitor's DPI and WPF processes any `WM_DPICHANGED`, then `SetBoundsNoActivate(hwnd, r * m.Scale rounded with JsMath.Round, topmost: false)` so the window is placed in physical px on the right monitor regardless of the primary monitor's DPI (EDGE-SHELL-48). IMPROVEMENT (EDGE-SHELL-35; Q-SHELL-14 for the monitor choice).
- `OnSourceInitialized`: `WindowRegistration.Register(this)` (replaces the scaffold's direct `CaptureExclusion.Apply`).
- `Closed`: set `App.IsShuttingDown = true` first (EDGE-SHELL-51), then close the pill and any overlay or dialog (`Application.Current.Windows`), which also resolves a pending selection `null` (EDGE-SHELL-38). Shutdown follows from `ShutdownMode`.
- No `Closing` handler on the main window cancels (INV-SHELL-4).
- `MainWindowSizer.SetDetailView(bool open, double scale)` (the implementation of spec 11's catalog interface `IMainWindowLayout`, `ShotAI.App.Shell`, singleton): UI thread only. `hwnd`; `m = MonitorQueries.ForWindow(hwnd)`; `s = m.Scale`; `cur = GetWindowRect(hwnd) / s` (DIP, rounded to 1/1000); `wa = m.WorkArea / s`; `r = WindowLayout.DetailResize(cur, wa, open, scale, WindowState == Maximized, _fullScreen)`; if not null, `SetBoundsNoActivate(hwnd, (r * s) with JsMath.Round per edge, topmost: false)`. The outer window rect (`GetWindowRect`, with the invisible resize borders on Windows 10 and 11) is used on both sides, and WPF's `Width` means the same outer size, so the 720 and 1010 constants keep one meaning in the native app. Whether Electron's `getBounds` and `BrowserWindow` `width` include those invisible borders is UNVERIFIED (spec 02 7.9 records the same gap), so the visible width may differ from Electron's by about 14 px (Q-SHELL-20). Callers: spec 05 and 06 on entering or leaving a project and on every committed scale change, exactly like the Electron effect (2.3.2).
- Recording hide and restore through `RecordingVisibilityController` (7.4.6).
- `ShowFromSecondInstance()`: `if (WindowState == Minimized) WindowState = Normal; Show(); Activate();` then `Foreground.TryActivate(hwnd)` (2.2).
- Full screen (View then Toggle Full Screen, F11): store `WindowStyle`, `ResizeMode`, `WindowState` and bounds; set `WindowStyle = None`, `ResizeMode = NoResize`, then bounds = `rcMonitor` of the window's monitor; the menu stays visible (Q-SHELL-11); F11 again restores the stored values. `_fullScreen` feeds `DetailResize` (D6).

#### 7.4.3 Capture pill (`CapturePillWindow`, `CapturePillViewModel`)

Window properties: `WindowStyle="None"`, `AllowsTransparency="False"` (opaque rectangle, parity; Q-SHELL-2), `ResizeMode="NoResize"`, `ShowInTaskbar="False"`, `Topmost="True"`, `ShowActivated="False"`, `Focusable="False"`, `Width="380"`, `Height="74"`, `Title="shotAI \u2014 Capture"`, `Background="#1f2330"`, `FontFamily="Segoe UI"`, `SnapsToDevicePixels="True"`, `UseLayoutRounding="True"`, no `Owner` (EDGE-SHELL-46).

`OnSourceInitialized`: `WindowRegistration.Register(this)`; `WindowStyles.MakeNonActivatingToolWindow(hwnd)`; `HwndSource.AddHook(WndProc)` where `WM_MOUSEACTIVATE` returns `MA_NOACTIVATE` (belt and braces; the pill must never be activated by a click, and the click must still be delivered). `Closing`: `e.Cancel = !App.IsShuttingDown` (EDGE-SHELL-23).

Layout (DIP, 2.4.8 converted): a `Border` root with `Padding="9.6,4.8"`, vertical `StackPanel` centered with 2.4 between rows; the top accent bar is a 2 DIP `Rectangle` at the top edge (`#4f46e5`, or `#ef4444` when `AccentIsError`), visible when `AccentBar`. Row 1 is a `Grid` with columns `*` (drag area) and `Auto` (controls) and 8 between. Drag area: grip (a 10 x 14 `Canvas` with six 2 DIP dots `#6b7280` at centers (2,2), (6,2), (2,6), (6,6), (2,10), (6,10), opacity 0.8; visual equivalence is enough), 8 gap, label (`FontSize="13.6"`, `FontWeight="SemiBold"`, letter spacing via `TextElement` not needed at 0.01em, `TextWrapping="NoWrap"`), rec dot `Ellipse 9x9` with right margin 6. Controls: `StackPanel Horizontal` spacing 5.6; buttons `Height="28"`, corner radius 7, `Padding="9.6,0"` for labeled buttons, discard `Width="28"`; fonts 11.52; colors and hovers per 2.4.8; divider `Rectangle 1x16` margin `1.6,0`. Row 2: either the hint `TextBlock` (11.52, `#aeb4c7` or `#fcd34d`, `TextTrimming="CharacterEllipsis"`, left margin 5.6) or the error `Grid` (glyph, message with ellipsis, `Dismiss` button 18 high, radius 5, `Padding="7.2,0"`, font 10.56).

Every `Button` on the pill: `Focusable="False"`, `IsTabStop="False"` (WPF must never call `SetFocus` into a non-active window). Tooltips per 2.11 with `ToolTipService.InitialShowDelay="400"` and `ToolTipService.ShowOnDisabled` false. The error row has `AutomationProperties.LiveSetting="Assertive"` and raises `LiveRegionChanged` when the error text changes (parity with `role="alert"`). Buttons carry `AutomationProperties.Name` per 2.11 so Narrator can invoke them (the window is not keyboard reachable; Q-SHELL-8).

Drag (EDGE-SHELL-29): the drag area, the hint and the empty part of the error row handle `PreviewMouseLeftButtonDown`: record `GetCursorPos` and the window's physical origin, `CaptureMouse()`; `MouseMove` while captured: `MoveNoActivate(hwnd, origin + (cursor - start))`; `MouseLeftButtonUp` or `LostMouseCapture`: release. The glyph, the message and the buttons do not start a drag. No clamping while dragging (parity).

Flash: an overlay `Border` spanning the pill, `IsHitTestVisible="False"`, `BorderBrush="#34d399"`, restarted whenever `FlashToken` increases: a `Storyboard` of 700 ms with `FillBehavior="HoldEnd"` (CSS `forwards`): opacity `SplineDoubleKeyFrame`s 0 at 0 ms, 1 at 175 ms, 0 at 700 ms, BOTH segments with `KeySpline="0,0 0.58,1"` (CSS applies `ease-out` per keyframe interval, 2.4.6), and a `ThicknessAnimation` of `BorderThickness` from 3 to 2 over the whole 700 ms with the same ease-out spline (`EasingFunction` is not equivalent; use a `ThicknessAnimationUsingKeyFrames` with one `SplineThicknessKeyFrame`). When `SystemParameters.ClientAreaAnimation` is `false` (the Windows animation setting, what Chromium maps `prefers-reduced-motion` to): opacity 1 from 0 to 490 ms, then linear to 0 at 700 ms, constant thickness 2, and the rec dot does not pulse. The rec dot pulse: opacity 1 to 0.3 over 700 ms and 0.3 to 1 over the next 700 ms, each half with `KeySpline="0.42,0 0.58,1"`, `RepeatBehavior="Forever"`, only while `RecDotPulsing`; stopping it (pause, or animations off) returns the dot to opacity 1 at once. `SystemParameters.ClientAreaAnimation` is read when each animation starts, and `SystemParameters.StaticPropertyChanged` for `ClientAreaAnimation` restarts the pulse with the new rule (Chromium re-evaluates the media query live; exact live-update timing UNVERIFIED).

`CapturePillViewModel` (derives from `ViewModelBase : ObservableObject`, ARCHITECTURE 5.1) wraps `PillPresenter` and exposes `PauseCommand`, `ResumeCommand`, `StopCommand`, `DiscardCommand`, `DismissErrorCommand` (`RelayCommand` or `AsyncRelayCommand` with `CanExecute = View.ControlsEnabled`; every change of `View.ControlsEnabled` calls `NotifyCanExecuteChanged()` on all five, because CommunityToolkit commands do not requery on their own; `AsyncRelayCommand`'s default `AllowConcurrentExecutions = false` additionally blocks a second Stop while the first runs). Command bodies call `ICaptureService` off the UI thread (`await Task.Run(...)` for the synchronous `Pause`/`Resume`, spec 11 T9); failures are logged at warning `pill: <action> failed:` and otherwise ignored (parity: Electron ignores them); state arrives through `StateChanged`.

Discard (7.6.3 dialog): `DiscardCommand` defers with `IUiDispatcher.Post(...)` (spec 11: never inline; `Dispatcher.BeginInvoke` is banned in the App outside `WpfUiDispatcher.cs`, `StaRenderThread.cs` and `UiDeferral.cs`, ARCHITECTURE 14.9) so the button handler unwinds first (macOS lesson, `macOS:shotAI/Capture/CaptureCoordinator.swift:228-234`), then shows `DiscardConfirmWindow` with the presenter's `DiscardMessage`; on confirm, `PillPresenter.OnDiscardConfirmed()` and `await capture.DiscardAsync()`.

#### 7.4.4 Area selection (`AreaSelectionService`, `AreaOverlayWindow`)

```csharp
public interface IAreaSelectionService
{
    /// Hides `requester` (in practice the main window), shows one overlay per monitor, returns the rect in
    /// global physical px or null; restores and activates `requester` in finally (INV-SHELL-9).
    Task<Rect?> SelectAreaAsync(Window requester, CancellationToken ct = default);
}
```

State: `_pending` = `(int Generation, TaskCompletionSource<Rect?> Tcs, List<AreaOverlayWindow> Windows)?`, UI thread only.

`SelectAreaAsync`: UI thread only. If `ct.IsCancellationRequested`, return `null` without hiding anything. If `_pending` exists, `Finish(pending.Generation, null)` first. `requester.Hide()`. `gen = ++_generation`; `tcs = new TaskCompletionSource<Rect?>(TaskCreationOptions.RunContinuationsAsynchronously)` (EDGE-SHELL-47); for each `m` in `MonitorQueries.All()`: create an overlay with `(gen, m)`; `using var reg = ct.Register(() => ui.Post(() => Finish(gen, null)))` (spec 11 `IUiDispatcher`; the registration is disposed when the method returns, so a later cancellation of a long-lived token cannot touch a newer selection). If no monitors, `Finish(gen, null)` immediately (EDGE-SHELL-50). After all overlays are shown, activate the overlay whose monitor contains the cursor (EDGE-SHELL-27). Log debug `region: overlay opened across <n> display(s)`. `try { return await tcs.Task; } finally { if (requester is still open && _pending is null) { requester.Show(); requester.Activate(); Foreground.TryActivate(hwnd); } }` (a newer selection that is pending keeps the requester hidden; its own `finally` restores it).

`Finish(gen, Rect? r)`: if `_pending is null || _pending.Generation != gen` return (stale overlay; INV-SHELL-15); take and clear `_pending`; close every overlay that is not already closed; `Tcs.TrySetResult(r)`; log info `region selected: <w>x<h> @ (<x>,<y>) [physical px]` or debug `region: selection cancelled` (natively every `null` logs the cancel line, including the destroyed-sender case that Electron resolved silently, 2.5.1).

`AreaOverlayWindow(gen, MonitorDescriptorEx m)` properties: `WindowStyle="None"`, `AllowsTransparency="True"`, `Background="#01000000"` (EDGE-SHELL-28), `ResizeMode="NoResize"`, `ShowInTaskbar="False"`, `Topmost="True"`, `ShowActivated="True"`, `Cursor="Cross"`, `Title="shotAI \u2014 Select area"`, `WindowStartupLocation="Manual"`, no `Owner` (EDGE-SHELL-46). If `Finish` has already run for `gen` when an overlay reaches `Loaded` (the Electron `isDestroyed()` guard), it closes itself instead of showing. `OnSourceInitialized`: `WindowRegistration.Register(this)` (initial exclusion from `ExcludedForNewWindow`, INV-SHELL-3), `WindowStyles.MakeToolWindow(hwnd)`, then `SetBoundsNoActivate(hwnd, m.Bounds, topmost: true)`. Handle `DpiChanged`: set `e.Handled`-equivalent behavior by re-applying `SetBoundsNoActivate(hwnd, m.Bounds, true)` after WPF processes the new DPI, so the overlay stays exactly on `rcMonitor` (7.7). `scale` for conversion = `VisualTreeHelper.GetDpi(this).DpiScaleX` after that, which must equal `m.Scale`.

Input, exactly 2.5.3: `PreviewKeyDown` Esc: `Finish(gen, null)`. `MouseDown`: if `ChangedButton != Left`, `Finish(gen, null)`; else `start = cur = e.GetPosition(this)`, `CaptureMouse()`. `MouseMove` while dragging: `cur = e.GetPosition(this)` (may be outside the window; EDGE-SHELL-26). `MouseUp` (any button): `ReleaseMouseCapture()`; if dragging and `AreaSelectionMath.IsSelection(rect)`: `Finish(gen, AreaSelectionMath.ToPhysical(rect, m.Bounds, scale))`; else `Finish(gen, null)`. `LostMouseCapture` while dragging with the left button still down (for example a system dialog stole it): keep the drag state and let the next `MouseUp` decide (the `LostMouseCapture` raised by our own `ReleaseMouseCapture()` is ignored; Electron's behavior when capture is lost mid-drag is UNVERIFIED). `Closed`: `Finish(gen, null)`. The overlay does not swallow the activating click (EDGE-SHELL-44).

Drawing: a `Canvas` with (a) the hint (`Border` radius 12, background `#D9111827` (0.85 alpha), padding `24,13.6`, drop shadow blur 35, offset 10, opacity 0.5; title 16.8 SemiBold white; sub 13.12 `#c7cdda`; centered horizontally, `Canvas.Top = ActualHeight * 0.14`), visible only while not dragging; (b) once dragging, a `Path` with `FillRule=EvenOdd` whose geometry is the whole overlay plus the selection rect, filled `#66000000` (0.4 alpha), which dims everything outside the selection and leaves the inside showing the desktop through the `#01000000` background; (c) a 2 DIP `#6366f1` border drawn inside the selection; (d) the badge (`Border` radius 6, background `#6366f1`, padding `7.2,1.92`, text 12.48 SemiBold white with tabular figures via `Typography.NumeralAlignment="Tabular"`) at `(rect.X + 6, rect.Y + 6)` when `ShowBadge(rect)`, text `AreaSelectionMath.BadgeText(rect, m.Bounds, scale)`.

#### 7.4.5 Application menu (`AppMenuViewModel` and the `Menu` in `MainWindow`)

A WPF `Menu` docked at the top of the main window, bound to `AppMenuViewModel`. Headers carry an access key on the first letter (`_File`, `_Edit`, `_View`, `_Window`, `_Help`), which renders as the plain labels in 2.11 until Alt is pressed (IMPROVEMENT: Alt+F opens File, which a Win32 menu bar without `&` also does by first letter). Items and bindings:

| Menu | Header | `InputGestureText` | Command, key binding | Enabled | Class |
|---|---|---|---|---|---|
| File | `Import Project…` | `Ctrl+O` | `ImportProjectCommand`; `KeyBinding Ctrl+O` on the main window | always; execution is a no-op while a capture session exists (INV-SHELL-18) | REQUIRED |
| File | `Settings` | `Ctrl+,` | `OpenSettingsCommand`; `KeyBinding Ctrl+OemComma` | same | REQUIRED |
| File | separator, then `Exit` | none | set `App.IsShuttingDown = true` (EDGE-SHELL-51), then `Application.Current.Shutdown()` | always | REQUIRED |
| Edit | `Undo`, `Redo`, separator, `Cut`, `Copy`, `Paste`, `Delete`, separator, `Select All` | `Ctrl+Z`, `Ctrl+Y`, `Ctrl+X`, `Ctrl+C`, `Ctrl+V`, none, `Ctrl+A` | `ApplicationCommands.Undo`, `.Redo`, `.Cut`, `.Copy`, `.Paste`, `.Delete`, `.SelectAll` routed to the focused element; set `InputGestureText` explicitly (WPF would show `Del` for Delete; Electron shows none) | WPF `CanExecute` of the focused element | REQUIRED items, IMPROVEMENT enablement (Electron's are always enabled and do nothing outside a text field) |
| View | `Actual Size`, `Zoom In`, `Zoom Out` | `Ctrl+0`, `Ctrl+Plus`, `Ctrl+-` | `UiZoom` applied as a `LayoutTransform` `ScaleTransform` on the main window's content root; key bindings `Ctrl+D0`; `Ctrl+Shift+OemPlus` (the Electron chord, 2.8.1), `Ctrl+OemPlus` and `Ctrl+Add`; `Ctrl+OemMinus` and `Ctrl+Subtract` | always | REQUIRED items and the Electron chords (Q-SHELL-12); `Ctrl+=`, numpad `+`, numpad `-` are IMPROVEMENT (D21) |
| View | separator, `Toggle Full Screen` | `F11` | 7.4.2 | always | REQUIRED |
| View | separator, `Brand` submenu | | items from `BrandMenuModel.Items`; each `MenuItem` has `IsCheckable="False"` and `IsChecked` bound one way, so WPF never toggles it by itself; click runs `ChooseBrandCommand(brandIdOrNull)`, including a click on the row that is already ticked (Electron fires `click` for it too; the store's raw compare makes it a no-op, 01) | submenu `IsEnabled = BrandMenuModel.SubmenuEnabled` | REQUIRED (the WPF default template draws a check mark where the Win32 radio item drew a bullet; accepted, Risk R4) |
| Window | `Minimize` | `Ctrl+M` | `WindowState = Minimized`; `KeyBinding Ctrl+M` | always | REQUIRED |
| Window | `Close` | `Ctrl+W` | `Close()` on the main window (quits, EDGE-SHELL-42); `KeyBinding Ctrl+W` | always | REQUIRED |
| Help | `About shotAI` | none | shows `AboutWindow` owned by the main window | always | REQUIRED |

Removed: `Reload`, `Force Reload`, `Toggle Developer Tools` and Window then `Zoom` (ELECTRON-ONLY, D9). The two separators around the removed items collapse: View is `Actual Size`, `Zoom In`, `Zoom Out`, separator, `Toggle Full Screen`, separator, `Brand`.

`AppMenuViewModel` consumes `IShellNavigationState` (provided by spec 05 and 06: `bool ProjectOpen`, `string? OpenProjectPath`, `string? RawProjectTheme`, raising `Changed`), `ISettingsService.Current.Brand` (spec 10, re-read on `ISettingsService.Changed`) and `ICaptureService.GetState()`. `ChooseBrandCommand` is an `IRelayCommand<string?>` (spec 11 7.3.3). `ChooseBrandCommand(brand)`: read `OpenProjectPath` at execution (EDGE-SHELL-13); if null, return; else `session.Apply(new SetProjectThemeOperation(brand))` on the open project's session (spec 05 P8 and spec 11 7.3.3; `brand` passed exactly as clicked: `null` for App default, `"shotAI"` for the default brand), which follows the fixed rule: update the in-memory manifest first (the menu re-derives immediately from it through `IShellNavigationState.Changed`), persist through the serialized write queue, and on failure roll back and show the standard notice. A failure to update the menu itself is logged and never surfaces as a failure of the brand change (spec 11 EDGE-IPC-6). The menu never touches `project.json` (INV-SHELL-17). The Brand items raise `PropertyChanged` only when their computed label, checked state or enabled state changes (EDGE-SHELL-12). Whenever the computed state changes the view model logs debug `menu: brand state open={open} project={rawTheme ?? "null"} app={appBrand}` (spec 11 L3, the successor of `ipc: view:set-brand-menu ...`). `ImportProjectCommand` and `OpenSettingsCommand` raise `ImportProjectRequested` and `OpenSettingsRequested` for spec 06 unless `GetState().Status != Idle`.

`AboutWindow`: a small WPF dialog (a `ShotAIWindow`) owned by the main window, `ResizeMode="NoResize"`, `ShowInTaskbar="False"`, `WindowStartupLocation="CenterOwner"`, title `About shotAI`, the 64 x 64 icon, message `AboutText.Message(info.Version)` in bold, detail `AboutText.Detail(info.DotNetVersion, info.WebView2Version, info.Arch)` selectable, where `info = IAppInfo.Current` (spec 11 7.3.5; a lock-free snapshot the UI thread may read, T9; the WebView2 probe runs lazily on the first read), one `OK` button (`IsDefault` and `IsCancel`). The App never references WebView2 for this (R-ARCH-12, INV-ARCH-5). Registered like every window (INV-SHELL-1). A Win32 `MessageBox` is not used anywhere in the app, because it cannot be registered before it is shown; the only exception is spec 12's `LegacyInstanceGuard` notice before any window exists, allowlisted until stage S5 (Q-SHELL-19, ARCHITECTURE 14.9).

#### 7.4.6 Recording visibility (`RecordingVisibilityController`)

Subscribes to `ICaptureService.RecordingChanged` and `StateChanged` and `CaptureFailed`, and marshals all three onto the UI thread through one ordered path: spec 11's `IUiDispatcher.Post(action)` (one priority, `Normal`, never inline, so the actions run in arrival order; INV-SHELL-21, INV-IPC-5). Never `Dispatcher.Invoke`, and no direct `Dispatcher.BeginInvoke` or `InvokeAsync` (spec 11 T6 and ARCHITECTURE 14.9 ban them in the App outside `WpfUiDispatcher.cs`, `StaRenderThread.cs` and `UiDeferral.cs`; service events use `IUiDispatcher.Post` only, R-ARCH-11, R-ARCH-18). It is an `IAppStartup`: it subscribes in `Start()` (7.4.1 step 9) and unsubscribes when disposed.

For `RecordingChanged(recording, showPill)`: `actions = planner.Plan(recording, showPill, PillDocking.NeedsRedock(pillRect, workAreas))`, then execute in order:

| Action | Implementation |
|---|---|
| `HideMain` | `main.Hide()` (window state and bounds are preserved by WPF) |
| `DockPill` | `m = MonitorQueries.ForWindow(mainHwnd)` (main may be hidden; its rect is still valid), or `Primary()` if the main window is gone; first `MoveNoActivate(pillHwnd, m.WorkArea.X, m.WorkArea.Y)` so the pill adopts that monitor's DPI; then `w = GetWindowRect(pillHwnd).Width`; `(x, y) = PillDocking.TopCenter(m.WorkArea, w, m.Scale)`; `MoveNoActivate(pillHwnd, x, y)`. If the hidden pill did not adopt the new DPI (EDGE-SHELL-48, UNVERIFIED), `w` is not `JsMath.Round(380 * m.Scale)`; in that case use `JsMath.Round(380 * m.Scale)` for the centering and re-dock once from the pill's `DpiChanged` after it is shown |
| `ShowPill` | `presenter.OnSessionShown(capture.GetState())`; then `pill.Show()` with `ShowActivated = false`, and nothing else. Do NOT also call `ShowWindow` directly: a raw `SW_SHOWNOACTIVATE` desynchronizes WPF's `Visibility`, and a following `pill.Show()` on an already visible HWND could activate it. Microsoft Learn documents `ShowActivated` as applying "when first shown"; whether WPF honors it on every later `Show()` after `Hide()` is UNVERIFIED (Q-SHELL-21). `CapturePillWindowTests.ShowDoesNotActivate` must show, hide and show again and assert the foreground window is unchanged each time. |
| `HidePill` | `pill.Hide()`; close an open `DiscardConfirmWindow` as cancelled |
| `ShowMain` | `if (main.WindowState == Minimized) main.WindowState = Normal; main.Show();` |
| `ActivateMain` | `main.Activate(); Foreground.TryActivate(mainHwnd)` (EDGE-SHELL-30) |

For `StateChanged`: `presenter.OnState(capture.GetState())`, reading the snapshot when the posted action runs rather than applying the payload (spec 11 T7; the flash still fires on any count increase, and two steps whose state events are processed after both landed produce one flash; accepted, since the pill cannot show two 700 ms flashes for steps that landed together anyway). For `CaptureFailed(e)`: `presenter.OnError(e.Message)` (a payload event, applied from the payload). The main window's own banner (`Capture error: <message>`) is spec 06 and subscribes independently.

#### 7.4.7 Window registration and popups (`WindowRegistration`, `PopupExclusion`)

- `WindowRegistration.Register(Window w)`: `hwnd = new WindowInteropHelper(w).Handle`; `ownWindows.Register(hwnd)` (spec 02 7.9: applies `ExcludedForNewWindow` immediately); `w.Closed += (_, _) => ownWindows.Unregister(hwnd)`. Called from each window's `OnSourceInitialized`, which WPF raises after the HWND exists and before it is first shown. A base class `ShotAIWindow : Window` does this so no window can forget (`AllWindowsRegisteredTests` enforces it).
- `PopupExclusion.Install()`: `EventManager.RegisterClassHandler(typeof(ToolTip), ToolTip.OpenedEvent, handler)`, the same for `ContextMenu.OpenedEvent`, and for `Popup` (including `ComboBox` drop-downs) `EventManager.RegisterClassHandler(typeof(Popup), Popup.OpenedEvent, ...)` is not available as a routed event, so every `Popup` in shotAI's XAML is created through a `ShotAIPopup : Popup` subclass whose `OnOpened` registers. The handler finds the popup's `HwndSource` (`PresentationSource.FromVisual(child) as HwndSource`) and registers its HWND. `ToolTip.OpenedEvent` and `ContextMenu.OpenedEvent` are routed events, so the class handlers work; `Popup.Opened` is a CLR event. Gap: a `ComboBox` drop-down is the `Popup` inside the theme's control template, not shotAI XAML, so `ShotAIPopup` does not reach it unless every `ComboBox` gets an implicit style whose template uses `ShotAIPopup` (or the catch-all hook in Q-SHELL-3 is adopted). The same applies to any third-party control with a templated `Popup`. WPF opens the popup HWND and paints it in the same dispatcher turn as `Opened`; Q-SHELL-3 covers the one-frame window and its test.
- System common dialogs (Q-EDIT-21). `ChooseColor` and the `Microsoft.Win32` `OpenFolderDialog`, `OpenFileDialog` and `SaveFileDialog` create top-level HWNDs on the UI thread that no `ShotAIWindow`, `ShotAIPopup` or `PopupExclusion` handler sees, so each needs its own registration before it is first visible, and an `Unregister` when the modal call returns. `ChooseColor`: spec 04's `ShotAI.Platform.Dialogs.Win32ColorDialog` opens it with `CC_ENABLEHOOK` and a hook procedure that calls `OwnWindowRegistry.Register(hwnd)` on `WM_INITDIALOG` (the dialog is not yet shown then). File dialogs: the WPF wrappers expose no hook, so `WpfFileDialogs` (11) and `WpfExportDialogs` (09) wrap each `ShowDialog` in the thread-local show hook of Q-SHELL-3, installed just before the call and removed after it, which registers any new top-level HWND of the UI thread before it is shown; the fallback, if that hook proves unreliable, is to drive `IFileDialog` directly and register the HWND from `IFileDialogEvents` through `IOleWindow.GetWindow` (Q-SHELL-22). `AllWindowsRegisteredTests` has a case for each dialog.

#### 7.4.8 Single-instance activation (`ActivationListener`)

`Start(sid)`: `HwndSource` with `new HwndSourceParameters(SingleInstanceIdentity.ActivationWindowName(sid)) { WindowClassStyle = 0, WindowStyle = 0, ParentWindow = new IntPtr(-3) /* HWND_MESSAGE */ }`, created on the UI thread; since `HwndSource` does not let the caller name the window class, the second instance searches by window name instead: `FindWindowExW(HWND_MESSAGE, null, null, windowName)` (7.3 `ExistingInstance.Activate` takes the name for the lookup). The hook handles the registered `ActivateMainWindow` message: `mainWindow.ShowFromSecondInstance()` on the UI thread (the hook already runs there). The second instance's `AllowSetForegroundWindow(pid)` lets the first take the foreground (the launching process is the foreground process at that moment). After a personal-copy hand-off (12 7.10.4) the second instance was started by the personal copy, not by the shell; `ProcessStarter` passes the personal copy's foreground right on with `AllowSetForegroundWindow(<started pid>)`, so this still holds. Only same-session, same-user, same-integrity instances can see each other, which matches the lock scope. `Dispose()`: `HwndSource.Dispose()`.

#### 7.4.9 Crash logging (`CrashLogging`)

| Source | Handler | Behavior |
|---|---|---|
| UI thread | `Application.DispatcherUnhandledException`, hooked as the UI dispatcher's own `Dispatcher.UnhandledException`, which it is raised from, so the tests raise it without an `Application` (corrected in WP-A12) | log error `unhandled exception on the UI thread:` with the exception; `e.Handled = true` (parity: Electron's main process keeps running after `uncaughtException`); if the main window is visible, show the generic notice `Something went wrong. See the log for details.` through `INoticeService` (Q-SHELL-16 default, ARCHITECTURE 8.3; the notice joins with `INoticeService` in WP-A16); an exception from `OnStartup` is not handled and terminates the process with its log line |
| Any thread | `AppDomain.CurrentDomain.UnhandledException` | log error `unhandled exception (terminating=<bool>):`; flush the log synchronously (the process is ending) |
| Tasks | `TaskScheduler.UnobservedTaskException` | log warning `unobserved task exception:`; `e.SetObserved()` (parity with electron-log's unhandled rejection capture) |
| WebView2 | `CoreWebView2.ProcessFailed` in the PDF host | log error `webview2 process failed: kind=<k> reason=<r> exitCode=<n>` (spec 09; the intent of `child-process-gone`) |
| Exit | `Application.Exit` | info `exiting (code <n>)`, so a clean quit is distinguishable in the log from a crash (EDGE-SHELL-20) |

#### 7.4.10 In-window overlay layer and the shared notice control

ARCHITECTURE 5.4 and 5.5 make this spec the owner of two main-window facilities that other specs place content in:

- **Overlay layer.** The top layer of the main window's content, above the Home, project and Settings views (spec 06's `ShellView` hosts it as its top grid layer). Everything that looks like a dialog or popover inside the main window is an element of this layer, not a separate HWND: spec 06's `ConfirmHost`, `NoticeHost`, tour and Home's capture target dropdown (`TargetDropdownView`, 06 7.6, moved out of a `ShotAIPopup` by R-ARCH-19), spec 04's `EditorOverlayView`, spec 07's SOP review and progress dialogs, and spec 05's popovers, insert menu, package dialog and capture-insert modal. Because these draw inside the already registered main window, they need no capture-exclusion registration of their own (INV-SHELL-1 holds through the main window). The rule (R-ARCH-19): a new popover or modal is drawn in the overlay layer by default; a `ShotAIPopup` (7.4.7) is used only where WPF requires a popup HWND (tooltips, context menus, `ComboBox` drop-downs, spec 06's `OverflowMenu`), and every such HWND is registered. The UI zoom `LayoutTransform` (7.4.5) sits on the content root, so it scales the layer with the views.
- **Notice control.** One notice control (`ShotAI.App.Chrome.NoticeHost`, look and live-region behavior specified by spec 06 7.10) is rendered by two hosts: spec 06's `NoticeCenter` (`INoticeService`: `Error` and `Update` slots, used by Home, Settings and the main window's `Capture error: <message>`) and spec 05's `NoticeStackViewModel` (the report's import, export, save and info slots). Text is `UserMessage.From(exception)` with the owning spec's prefix; no auto-dismiss; errors announce assertively (ARCHITECTURE 5.5).

### 7.5 Threading, cancellation and disposal

| Concern | Rule |
|---|---|
| Thread | Every window, the menu, the presenters' view models and `AreaSelectionService` live on the single WPF UI thread (STA). Core presenters are not thread-safe and are only called there. |
| Capture events | Raised on engine threads (spec 02 7.3); marshaled by `RecordingVisibilityController` in arrival order with `IUiDispatcher.Post` only (7.4.6; superseded `InvokeAsync` wording, R-ARCH-11). |
| Calls into capture | Pill commands `await` `ICaptureService` methods; synchronous engine methods run through `Task.Run`. No UI-thread blocking (INV-SHELL-21). |
| Affinity calls | `CaptureShield` may call `SetWindowDisplayAffinity` from a capture thread for UI-thread windows; spec 02 Q-CAP-15 decides whether that must be marshaled (Q-ARCH-6: if so, a bounded wait that skips the grab on timeout, ARCHITECTURE DL2). The shell never waits on it, and no UI-thread path takes `CaptureShield`'s lock except the uncontended startup application of 7.4.1 step 10 (ARCHITECTURE DL1). |
| Cancellation | `SelectAreaAsync(ct)`: cancellation finishes the selection with `null` on the UI thread and restores the requester. |
| Disposal | Overlays close on finish; the pill and dialogs close with the main window; `ActivationListener`, the lock and the container dispose in `OnExit`. |

### 7.6 Window specifics

#### 7.6.1 Summary

| Window | WPF | Win32 extras | Registered | Activation |
|---|---|---|---|---|
| Main | standard chrome, `720 x 740`, min `680 x 560`, `Title="shotAI"` | none | yes | normal |
| Pill | `WindowStyle=None`, `AllowsTransparency=False`, `Topmost`, `ShowInTaskbar=False`, `ShowActivated=False`, `Focusable=False`, `380 x 74` | `WS_EX_NOACTIVATE`, `WS_EX_TOOLWINDOW`, `HWND_TOPMOST`, `WM_MOUSEACTIVATE` returns `MA_NOACTIVATE`, manual drag with `SWP_NOACTIVATE` | yes | never |
| Overlay (one per monitor) | `WindowStyle=None`, `AllowsTransparency=True`, `Background=#01000000`, `Topmost`, `ShowInTaskbar=False`, `Cursor=Cross` | `WS_EX_TOOLWINDOW`, bounds `rcMonitor` via `SetWindowPos`, re-applied on `DpiChanged` | yes | the one under the cursor |
| Discard confirmation | owned by the pill, `Topmost`, `ShowInTaskbar=False`, `WindowStartupLocation=CenterOwner`, `ResizeMode=NoResize`, `SizeToContent=WidthAndHeight` | none | yes | activated (the user just clicked the pill) |
| About | owned by main, `CenterOwner`, `NoResize` | none | yes | normal |

#### 7.6.2 Why these pill choices

- `ShowActivated=False` covers the first show only; `WS_EX_NOACTIVATE` covers clicks; `MA_NOACTIVATE` covers clicks if a WPF update ever resets the style; `Focusable=False` on the window and its controls keeps WPF from calling `SetFocus`, which would activate a top-level window of the calling thread.
- `WS_EX_TOOLWINDOW` removes the pill from Alt+Tab; `ShowInTaskbar=False` alone leaves an unowned window in Alt+Tab.
- `Topmost=True` maps to `HWND_TOPMOST`, the same as Electron's `alwaysOnTop` on Windows.
- Opaque rectangle (`AllowsTransparency=False`) matches Electron exactly and avoids layered-window cost and the unverified affinity on layered windows for the one window on screen during every grab (spec 02 Q-CAP-15). Rounded corners, if wanted, come from `DwmSetWindowAttribute(DWMWA_WINDOW_CORNER_PREFERENCE, DWMWCP_ROUND)` on Windows 11, not from transparency (Q-SHELL-2).

#### 7.6.3 Discard confirmation dialog (`DiscardConfirmWindow`)

Title `shotAI`; icon none; message text `ShellStrings.DiscardWholeProject` or `ShellStrings.DiscardSessionSteps` (verbatim, REQUIRED); buttons `Discard` and `Cancel`, `Cancel` is `IsDefault` and `IsCancel` (IMPROVEMENT over Electron's `OK` and `Cancel`: the destructive button says what it does, as on macOS, and Enter does not destroy; Q-SHELL-7). Shown with `ShowDialog()` owned by the pill; `Loaded`: `Activate()`. Closed as cancelled if the session ends while it is open (the pill hides).

### 7.7 Per-monitor DPI

- The app manifest already declares PerMonitorV2 (`dotnet/src/ShotAI.App/app.manifest`). WPF on .NET 10 handles `WM_DPICHANGED` for every window.
- All positioning that must land on a specific monitor is done in physical px with `SetWindowPos` after `SourceInitialized`, never with `Left`/`Top`, whose DIP meaning on a secondary monitor depends on the DPI the window currently has. A move across a DPI boundary is two steps: move onto the monitor first, then set the final rectangle, because WPF applies the `WM_DPICHANGED` suggested rectangle and would rescale a size set in the same call (EDGE-SHELL-48).
- Overlays: created per monitor from `MonitorQueries.All()`; each is moved to its `rcMonitor`; when WPF raises `DpiChanged` for the move, the overlay re-applies its bounds (Windows' suggested rectangle scales the size and would no longer match the monitor). Conversion uses the overlay's own DPI (INV-SHELL-15).
- Main window resize math divides every physical input by the same `s` (the window's current monitor) and multiplies back (7.4.2), so mixed-DPI setups cannot mix units.
- Pill docking moves the pill onto the target monitor first, then measures and positions it (7.4.6).

### 7.8 Persistence

The shell persists nothing: no window bounds, no pill position (per run only), no zoom level (Q-SHELL-12). The brand choice persists through spec 01's store. Settings are read, not written, by the shell. It composes no data path itself: every location comes from `IAppPaths` (ARCHITECTURE 10.2), and if a later change ever needs native-only local data it goes under `IAppPaths.LocalDataDirectory` (`%LOCALAPPDATA%\LFI\shotAI\`, never the Squirrel root `%LOCALAPPDATA%\shotai\`, R-ARCH-13), while `settings.json`, the logs and the projects folder stay where the Electron build keeps them.

### 7.9 Native log lines

| Level | Text |
|---|---|
| info | `another instance already holds the lock \u2014 exiting.` |
| debug | `second instance: activation signal sent` or `second instance: no running window found` |
| info | `runtime: win32/<arch> · native <arch>[ (emulated)] · .NET <v> · os <v> · render tier <n>` |
| info | `render: software forced (SHOTAI_ENABLE_GPU=0)` |
| info | `startup: main window rendered in <n> ms` (ARCHITECTURE PB-9, D-ARCH-4; measured from the process start time to the main window's first `ContentRendered`) |
| debug | `second launch: surfacing the main window` |
| debug | `region: overlay opened across <n> display(s)` |
| info | `region selected: <w>x<h> @ (<x>,<y>) [physical px]` |
| debug | `region: selection cancelled` |
| warn | `pill: <action> failed:` |
| debug | `menu: brand state open=<b> project=<rawTheme or null> app=<appBrand>` (spec 11 L3) |
| warn | `startup auto-archive failed (non-fatal):` (Electron wording kept) |
| info | `legacy shotAI 1.x is running (pid <pid>, <path>); exiting.` (spec 12 `LegacyInstanceGuard`) |
| info | `personal copy of shotAI; opening the copy installed for all users (<path>).` and, in a self-test mode, `personal copy of shotAI; a copy for all users exists (<path>); the self-test runs this copy.` (spec 12 `PersonalCopyGuard`) |
| warn | `personal copy of shotAI; could not open the copy for all users (<path>): <exception message>. Continuing with this copy.` (spec 12 `PersonalCopyGuard`) |
| error, warn, info | crash handler lines (7.4.9) |

### 7.10 `NativeMethods.txt` additions (CsWin32)

`SetWindowLongPtr`, `GetWindowLongPtr`, `SetWindowPos`, `GetWindowRect`, `ShowWindow`, `IsIconic`, `IsWindow`, `IsWindowVisible`, `SetForegroundWindow`, `GetForegroundWindow`, `AllowSetForegroundWindow`, `FindWindowEx`, `PostMessage`, `RegisterWindowMessage`, `GetWindowThreadProcessId`, `EnumThreadWindows`, `GetWindowDisplayAffinity` (tests), `MonitorFromWindow`, `MonitorFromPoint`, `GetMonitorInfo`, `MONITORINFOEXW`, `EnumDisplayMonitors`, `GetDpiForMonitor`, `GetDpiForWindow`, `GetCursorPos`, `IsWow64Process2`, `GetCurrentProcess`, `DwmSetWindowAttribute` (only if Q-SHELL-2 adopts corners), constants `WS_EX_NOACTIVATE`, `WS_EX_TOOLWINDOW`, `WS_EX_APPWINDOW`, `GWL_EXSTYLE`, `HWND_TOPMOST`, `HWND_MESSAGE`, `SWP_*`, `SW_SHOWNOACTIVATE`, `SW_RESTORE`, `WM_MOUSEACTIVATE`, `MA_NOACTIVATE`, `MONITOR_DEFAULTTONEAREST`, `MONITOR_DEFAULTTOPRIMARY`, `MDT_EFFECTIVE_DPI`, `IMAGE_FILE_MACHINE_*`. Several overlap with spec 02's list; add each name once. CsWin32 generates the parameter enums of every requested function by itself (`WINDOW_EX_STYLE`, `SET_WINDOW_POS_FLAGS`, `SHOW_WINDOW_CMD`, `WINDOW_LONG_PTR_INDEX`, `MONITOR_FROM_FLAGS`, `MONITOR_DPI_TYPE`, `IMAGE_FILE_MACHINE`), so the `WS_EX_*`, `SWP_*`, `SW_*`, `GWL_EXSTYLE`, `MONITOR_DEFAULTTO*`, `MDT_*` and `IMAGE_FILE_MACHINE_*` names above arrive as members of those enums; list only the loose constants (`WM_MOUSEACTIVATE`, `MA_NOACTIVATE`, `HWND_TOPMOST`, `HWND_MESSAGE`) explicitly. Whether the pinned CsWin32 0.3.335 accepts `*` wildcards in `NativeMethods.txt` is UNVERIFIED; do not rely on them. `SetWindowLongPtr` and `GetWindowLongPtr` exist as real exports only in 64-bit `user32.dll`, which is all the app ships (x64 and ARM64).

### 7.11 NuGet

Per ARCHITECTURE 3.2: `CommunityToolkit.Mvvm` (Core presenters and App view models, commands), `Microsoft.Extensions.DependencyInjection` (App, composition root), `Microsoft.Extensions.Logging` (App, `LoggerFactory`) with spec 10's first-party `FileLoggerProvider` in Core (no third-party sink) and `Microsoft.Extensions.Logging.Debug` in Debug builds only, `Microsoft.Extensions.Logging.Abstractions` (Core), and `Microsoft.Windows.CsWin32` (Platform, already present). This spec adds NO WebView2 reference (superseded by R-ARCH-12): the About line's runtime version is `IAppInfo.Current.WebView2Version`, which spec 11's `AppInfoProvider` reads through Platform's `IWebView2RuntimeInfo`; `ShotAI.Platform.Export.WebView2RuntimeInfo` calls `CoreWebView2Environment.GetAvailableBrowserVersionString()` and returns `null` on `WebView2RuntimeNotFoundException` or any failure, which `AboutText` shows as `not installed`. `Microsoft.Web.WebView2` is referenced by `ShotAI.Platform` only, and the App has no WebView2 package reference at all (INV-ARCH-5, `Architecture.SingleWebViewTests`). No new native dependency. Versions go only in `dotnet/Directory.Packages.props` (exact pins, lock files, ARCHITECTURE 3.1), which at the time of writing pins only `Microsoft.Windows.CsWin32` and `xunit.v3`.

### 7.12 Divergences from Electron

| # | Change | Class | Justification |
|---|---|---|---|
| D1 | The pill is non-activating (`WS_EX_NOACTIVATE`, `ShowActivated=False`, `MA_NOACTIVATE`, manual drag) and out of Alt+Tab | IMPROVEMENT | macOS parity; the recorded app keeps focus; the first click after start reaches the target; no Alt+F4 on the pill (EDGE-SHELL-23). |
| D2 | The pill cancels `Closing` unless the app is shutting down | IMPROVEMENT | EDGE-SHELL-23. |
| D3 | Pill presenter resets error, flash baseline and token at every session start; flashes only on an in-session increase | IMPROVEMENT | EDGE-SHELL-22, macOS parity. |
| D4 | Pill controls disabled from Stop or a confirmed Discard until the next state | IMPROVEMENT | EDGE-SHELL-24. |
| D5 | Pill re-docks if its saved position no longer intersects any work area | IMPROVEMENT | EDGE-SHELL-25. |
| D6 | `DetailResize` skips a maximized or full-screen window in both directions | IMPROVEMENT | Electron guards only open plus maximized; resizing a maximized or full-screen window breaks its state (EDGE-SHELL-32, EDGE-SHELL-33). |
| D7 | Initial height clamped to the work area and the window centered in the work area of the cursor's monitor | IMPROVEMENT | EDGE-SHELL-35. |
| D8 | Discard confirmation is an own WPF dialog with `Discard` and `Cancel` (Cancel default), registered and topmost | IMPROVEMENT | EDGE-SHELL-37, macOS parity, destructive default avoided. Message text unchanged. |
| D9 | View then `Reload`, `Force Reload`, `Toggle Developer Tools`, Window then `Zoom` removed | ELECTRON-ONLY | No web content to reload or inspect; Zoom does nothing on Windows. |
| D10 | Edit items enabled by WPF `CanExecute` | IMPROVEMENT | Electron's are always enabled and inert outside text fields. |
| D11 | About detail shows `.NET <v> · WebView2 <v or not installed>` instead of `Electron <v> · Chromium <v>`; version is the native version; every value comes from `IAppInfo.Current` (the WebView2 version through Platform's `IWebView2RuntimeInfo`, R-ARCH-12) | IMPROVEMENT | The runtime line exists for support; these are the native components. |
| D12 | Overlay under the cursor is activated; Esc works on every overlay | IMPROVEMENT | EDGE-SHELL-27. |
| D13 | Overlay size badge shows the exact physical size that will be returned | IMPROVEMENT | EDGE-SHELL-39. |
| D14 | Brand menu is bound to the view model: no rebuild, no 120 ms deferral, no IPC coercion, no `projectPinUnrecognised` transport field; the raw theme string is read from the open manifest | ELECTRON-ONLY mechanics, REQUIRED semantics | EDGE-SHELL-7, EDGE-SHELL-11 to 13; macOS reads the raw value the same way. |
| D15 | Popup HWNDs (tooltips, context menus, drop-downs) are registered for capture exclusion | IMPROVEMENT [SECURITY] | EDGE-SHELL-40. |
| D16 | Squirrel handling, ARP icon registry write, custom protocol, CSP, navigation confinement, sandbox switches, GPU switches, load diagnostics, HTML entry pages removed | ELECTRON-ONLY | 2.10 lists each replacement. |
| D17 | `SHOTAI_ENABLE_GPU=0` forces WPF software rendering; `1` has no effect; the emulation check is a log line only | IMPROVEMENT (diagnostic) | Keeps the troubleshooting switch without a GPU policy (2.10.5). |
| D18 | `SessionEnding` tears capture down before shutdown; exit and crash lines are logged | IMPROVEMENT | INV-SHELL-19, INV-SHELL-20. |
| D19 | Menu headers carry first-letter access keys | IMPROVEMENT | Standard WPF keyboard access; labels unchanged. |
| D20 | Single-instance scope is the user's logon session (`Local\` plus SID); activation by a message-only window and `AllowSetForegroundWindow` | REQUIRED behavior, native mechanism | Electron's lock is per user data directory; the practical scope (one user, one session) is the same (Q-SHELL-1). |
| D21 | Zoom In also answers `Ctrl+=` and numpad `+`, Zoom Out numpad `-` | IMPROVEMENT | Electron's `CommandOrControl+Plus` needs Shift on common layouts (2.8.1, UNVERIFIED); the Electron chord is kept as well. |
| D22 | An empty monitor list resolves the selection `null` at once | IMPROVEMENT | EDGE-SHELL-50 (Electron would hang; unreachable there). |
| D23 | A second area selection keeps the requester hidden until the newest selection ends | IMPROVEMENT | EDGE-SHELL-47. |
| D24 | Every `null` area result logs `region: selection cancelled`, including the destroyed-overlay case | IMPROVEMENT (log only) | 2.5.1. |

Everything else in section 2 is REQUIRED as written.

---

## 8. Tests

### 8.1 `src/main/gpu-policy.test.ts`

Purpose: pin the pure GPU decision so the x64-under-ARM64 case disables the GPU and nothing else does.

| Group | Cases | Native disposition | Target |
|---|---|---|---|
| `isEmulatedOnArm` | native x64, variable unset: false (`:5-7`); x64 process with `PROCESSOR_ARCHITEW6432=ARM64`: true (`:9-11`); ia32 on x64 (`AMD64`): false, WOW64 (`:13-15`); native arm64: false (`:17-19`); non-Windows platforms: false (`:21-24`); lowercase `arm64`: true (`:26-28`) | The rule ports as a diagnostic on machine enums (`IsWow64Process2` gives the native machine directly, so there is no environment variable and no string case). Cases 1 to 4 port; the platform case and the case-insensitivity case are ELECTRON-ONLY (Core is only called on Windows; enums have no case). | `ShotAI.Core.Tests/Shell/RuntimeDiagnosticsTests`: `NativeX64IsNotEmulated`, `X64OnArm64IsEmulated`, `X86OnX64IsWow64NotEmulation`, `NativeArm64IsNotEmulated`, plus new `X86OnArm64IsEmulated` |
| `decideGpu` | AUTO default ON, reason matches `/default ON/i` (`:32-36`); AUTO OFF under emulation, reason matches `/emulation/i` (`:38-42`); `=1` forces ON under emulation, reason `/forced ON/i` (`:44-48`); `=0` forces OFF, reason `/forced OFF/i` (`:50-54`); unrecognized value `yes` falls through to AUTO, both ON on a native box AND OFF under emulation (`:56-59`); default ON on macOS and Linux (`:61-64`) | ELECTRON-ONLY as a decision (WPF has no GPU process to disable and falls back by itself). Per case: default ON ports as "unset does not force software"; AUTO OFF under emulation is ELECTRON-ONLY (natively emulation only adds ` (emulated)` to the runtime line, covered by `RuntimeDiagnosticsTests`); `=1` ports as "1 does not force" (natively `1` has no effect at all); `=0` ports as `ZeroForcesSoftware`; the `yes` case ports for its first assertion only (the emulation half is ELECTRON-ONLY); macOS and Linux are ELECTRON-ONLY. The reason strings are ELECTRON-ONLY (the native line is `render: software forced (SHOTAI_ENABLE_GPU=0)`). | `ShotAI.Core.Tests/Shell/RenderModePolicyTests`: `ZeroForcesSoftware`, `OneDoesNotForce`, `UnsetDoesNotForce`, `UnrecognizedDoesNotForce` (the `yes` case), plus new `EmptyStringDoesNotForce` and `ZeroWithWhitespaceDoesNotForce` (`" 0"`: the Electron comparison is exact `=== '0'`) |

### 8.2 `src/main/arp-icon.test.ts`

Purpose: pin the Squirrel ARP icon fix without touching the real registry.

| Group | Cases | Native disposition | Target |
|---|---|---|---|
| `squirrelCommand` | finds `--squirrel-install` and `--squirrel-updated` among argv (`:12-16`); null for normal launches and other flags (`:17-20`) | ELECTRON-ONLY: no Squirrel. | none |
| `appIcoPathFor` | `<root>\app-1.0.0\shotAI.exe` gives `<root>\app.ico` (`:23-29`) | ELECTRON-ONLY | none |
| `displayIconRegArgs` | `reg add HKCU\...\Uninstall\shotai`, contains `DisplayIcon` and `/f`, `/d` value is the app.ico path (`:31-42`) | ELECTRON-ONLY | none |
| `fixArpIconOnSquirrelEvent` | install writes app.ico and sets DisplayIcon once (`:68-77`); updated does too (`:79-83`); uninstall, firstrun, obsolete and no args are no-ops (`:85-91`); missing bundled icon and no app.ico: no registry call, no throw (`:93-104`); existing app.ico with missing bundled icon still sets DisplayIcon (`:106-118`) | ELECTRON-ONLY. The intent (the "Installed apps" entry always shows the shotAI icon, offline too, without a personal URL) moves to spec 12: the per-machine MSI sets `ARPPRODUCTICON` from the bundled `.ico` (spec 12 INV-PKG-30; MSIX was rejected, INV-PKG-1). | spec 12 installer check (AC-SHELL-30 records the manual procedure) |

### 8.3 New tests (native only)

**ShotAI.Core.Tests (Linux and Windows), folder `Shell/`:**

| Class | Cases |
|---|---|
| `WindowLayoutTests` | `OpenAtScaleOneGives1010` (current 720 at x 100 on a 1920 work area at 0: newW 1010, x = max(0, min(round(460 - 505), 910)) = 0); `OpenAtScale125Gives1214`; `OpenAtScale065Gives1010`; `DetailClampedToWorkArea` (1.25 on 1100 gives 1100); `DetailFloorExceedsNarrowWorkArea` (900 gives 1010, x = wa.X; EDGE-SHELL-34); `DetailIsGrowOnly` (current 1300, target 1214: null); `CloseGoesTo720`; `SameWidthIsNoOp`; `MaximizedIsNotResized` (both directions); `FullScreenIsNotResized`; `CenterPreservedAndClamped` (right edge clamp); `RoundsLikeJavaScriptForNegativeHalves` (work area X -1920, width 1920; current X -999, width 721; open at scale 1: centerX = -638.5, -638.5 - 505 = -1143.5, JS round gives -1143, clamp to [-1920, -1010] keeps -1143; C# `Math.Round` would give -1144); `ClampScaleSnapsIntegerPercent` (0.825 gives 0.85, NaN gives 1, 2 gives 1.25); `InitialClampsHeight` (work area 1093 x 574 gives height 574; 1093 x 500 gives 560) |
| `PillDockingTests` | `TopCenterAt100Percent` (work area 0,0,1920,1040, width 380: (770, 8)); `TopCenterAt150PercentPhysical` (work area 0,0,2880,1560, width 570, scale 1.5: (1155, 12)); `OddWidthRoundsLikeJs`; `NegativeOriginMonitor`; `NeedsRedockWhenOffAllMonitors`; `NoRedockWhenPartlyVisible` |
| `PillPresenterTests` | label group (idle `shotAI`, `Capturing · 3`, `Paused · 3`); controls group (Pause only while recording, Resume only while paused, none while idle); hint group (recording and paused strings); error group (`EmptyMessageUsesFallback`, `WhitespaceMessageUsesFallback`, `MessageKeptUntrimmed`, `ClearedWhenStepLands`, `ClearedWhenSessionEnds`, `ClearedByDismiss`, `NewerErrorReplaces`, `HiddenWhileIdle`, `ClearedAtSessionStart`); flash group (`FlashOnIncreaseWhileActive`, `NoFlashAtSessionStartOnNonEmptyProject`, `NoFlashWhileIdle`, `TokenIncrementsPerStep`); accent group (`AccentRedOnlyWithError`); `DiscardMessageFollowsState`; `ControlsDisabledAfterStopUntilNextState` |
| `RecordingVisibilityPlannerTests` | `StartWithPillHidesDocksShows`; `SecondStartDoesNotRedock`; `RedocksWhenOffScreen`; `ScreenshotHidesMainOnly`; `EndHidesPillShowsAndActivatesMain` |
| `AreaSelectionMathTests` | `NormalizeAnyDirection` (drag up-left); `MinDragBoundary` (3.99 rejected, 4 accepted, per side); `BadgeThresholds` (39.9 x 22 hidden, 40 x 21.9 hidden, 40 x 22 shown); `ToPhysicalAt100Percent` (sel 10.4, 20.6, 300.5, 200.4 on monitor 0,0: 10, 21, 301, 200); `ToPhysicalAt150Percent` (sel 10, 10, 101, 51 on monitor 1920,0 at 1.5: 1935, 15, 152, 77); `ToPhysicalNegativeOrigin` (monitor -2560, 0 at 1.25); `BadgeEqualsResult`; `BadgeTextFormat` (`1280 × 720px`) |
| `BrandMenuModelTests` | truth table rows of 2.8.2 (`NoProjectDisablesSubmenu`, `NoPinTicksAppDefault`, `UnrecognisedPinTicksNothing`, `PinnedDefaultTicksShotAI`, `PinnedLfiTicksLfi`); `EmptyStringThemeTicksAppDefault` (`""` is not unrecognised); `UnknownAppBrandLabelsDefault` (`App default (shotAI)`); `AppBrandLfiLabel` (`App default (LFI)`); `EveryBrandListedInCatalogOrder`; `PrototypeNameIsUnrecognised` (`toString`, #89) |
| `UiZoomTests` | factor at 0, 0.5, -0.5; clamp at 0.25 and 5.0; `ActualSizeResets` |
| `AboutTextTests` | exact message and detail strings with `\u2014`, `·`, `\n\n` and `\n` placement; `NullWebView2VersionShowsNotInstalled` (the `AppInfo.WebView2Version` that `IWebView2RuntimeInfo` leaves `null` when the runtime is missing, R-ARCH-12) |
| `RuntimeDiagnosticsTests`, `RenderModePolicyTests` | 8.1 |
| `SingleInstanceIdentityTests` | names for a sample SID (use a made-up SID such as `S-1-5-21-1-2-3-1001`, never a real one); `Local\` prefix; stable across calls |
| `ShellStringsTests` | every constant equals the 2.11 text; the three em-dash strings contain U+2014 exactly once and no U+2013 |

**ShotAI.Platform.Tests (Windows only, new project):**

| Class | Cases |
|---|---|
| `SingleInstanceTests` | `SecondAcquireFails` (acquire in a child process started from the test, then `TryAcquire` in the test returns null); `ReleasedOnDispose`; `AbandonedCountsAsAcquired` (child exits without releasing); `SecondLaunchSignalsFirst` (child creates the message-only window; `ExistingInstance.Activate` returns true and the child observes the message) |
| `MonitorQueriesTests` | `AllMatchesEnumDisplayMonitors`; `WorkAreaInsideBounds`; `ScaleFromEffectiveDpi` |
| `ProcessMachineTests` | `CurrentMatchesRuntimeInformation` (on an x64 build on ARM64 hardware, native is ARM64) |

**ShotAI.App.Tests (Windows only, new project, the in-repo STA harness of ARCHITECTURE 12.1, Q-HOME-15):**

| Class | Cases |
|---|---|
| `AllWindowsRegisteredTests` | `EveryShownHwndIsRegistered` (main, pill, overlays, Discard dialog, About, a tooltip, a context menu, a combo-box drop-down, the `ChooseColor` dialog and the folder, open and save dialogs, Q-EDIT-21); `NoWin32MessageBoxInSource` (source scan of `ShotAI.App` for `MessageBox.Show` and `System.Windows.Forms`, allowlisting only spec 12's `LegacyInstanceGuard.cs` until stage S5, Q-SHELL-19 default and ARCHITECTURE 14.9) |
| `StartupOrderTests` | `WindowsExcludedBeforeSettingApplied`; `SecondInstanceCreatesNoWindow` |
| `LifecycleTests` | `ClosingMainWindowShutsDown`; `ExitCallsCaptureTeardown`; `SessionEndingCallsTeardown`; `PillCloseCancelledWhileRunning`; `MainWindowCloseClosesPillDespiteVeto` (EDGE-SHELL-51); `ExitOrderMatchesSpec11` (fake services record: Stopping cancelled, Teardown, flush, listener and lock disposed, container disposed, exit line) |
| `MainWindowTests` | `InitialSizeAndMinimums`; `TitleIsShotAI`; `SetDetailViewUsesPhysicalMath` (fake monitor at 150%); `ShowFromSecondInstanceRestoresMinimized`; `InitialPlacementOnSecondaryMonitorAtOtherDpi` (EDGE-SHELL-48: final physical size equals `WindowLayout.Initial` times the target scale) |
| `CapturePillWindowTests` | `HasToolWindowAndNoActivateStyles`; `ShowDoesNotActivate` (foreground HWND unchanged on the first show AND on a show after `Hide()`, Q-SHELL-21); `HasNoOwner` (EDGE-SHELL-46); `ClickDoesNotActivate` (synthesized click on Pause via `SendInput`, foreground unchanged, command invoked); `DragMovesWithoutActivating`; `DocksOnceThenKeepsPosition`; `DiscardCancelDoesNotCallService`; `DiscardConfirmCallsServiceOnce`; `ErrorTooltipShowsWhileInactive` (Q-SHELL-3) |
| `AreaOverlayTests` | `OneOverlayPerMonitorWithExactBounds`; `TransparentAreaReceivesMouse`; `OverlaySeededFromSetting`; `EscCancels`; `RightButtonCancels`; `SmallDragCancels`; `DragReturnsPhysicalRect` |
| `AreaSelectionServiceTests` | `NewSelectionResolvesPreviousNull`; `StaleOverlayCannotResolve`; `OverlayClosedResolvesNull`; `CancellationResolvesNull`; `RestoresMainWindowOnCancelAndError`; `EmptyMonitorListResolvesNull` (EDGE-SHELL-50); `SecondSelectionKeepsRequesterHidden` (EDGE-SHELL-47); `PreCancelledTokenDoesNotHide`; `CancellationRegistrationDisposed` (a token cancelled after the selection ended changes nothing) |
| `AppMenuViewModelTests` | `ItemsAndGestures` (the 7.4.5 table); `BrandChoiceCallsStoreForOpenProject`; `BrandChoiceIgnoredWithNoProject`; `BrandItemsNotifyOnlyOnChange`; `ImportAndSettingsIgnoredWhileRecording`; `CheckedIsNotToggledByWpf` (clicking a ticked row leaves the checked state to the model) |
| `ShellEventOrderingTests` | `RecordingChangedBeforeStateChanged`; `NoSynchronousDispatcherInvoke` (source scan of `ShotAI.App` for `Dispatcher.Invoke(`) |
| `CrashLoggingTests` | `DispatcherExceptionLoggedAndHandled`; `UnobservedTaskLogged` |
| `RecordingChangedHidesAndShows` | (named in spec 02) start hides main and shows the pill; screenshot hides main only; stop restores and activates main |

---

## 9. Acceptance criteria

**AC-SHELL-1.** `dotnet test` of `ShotAI.Core.Tests` passes every class in 8.3's Core table on Linux.

**AC-SHELL-2.** On Windows, `ShotAI.Platform.Tests` and `ShotAI.App.Tests` pass.

**AC-SHELL-3.** Manual: launch, then click the main window's X. Task Manager shows no `shotAI.exe` within 2 s. Repeat after one completed recording (so the pill has been shown and hidden). Same result.

**AC-SHELL-4.** Manual: with shotAI running and minimized, launch it again from the Start menu. No second process remains after 2 s, the existing window is restored and in the foreground, and the log contains `another instance already holds the lock \u2014 exiting.` once.

**AC-SHELL-5.** Manual: fresh launch on a 1920 x 1080 monitor at 100%: the main window is 720 x 740 DIP, centered in the work area; on 1366 x 768 at 125% its height fits above the taskbar.

**AC-SHELL-6.** Manual: open a project at 100% scale: the window grows to 1010 DIP, keeping its center (clamped to the work area); set the project to 125%: it grows to 1214; back to 100%: it stays at 1214; return Home: it becomes 720. Maximize, change the scale: the window stays maximized.

**AC-SHELL-7.** Manual: start a recording from a window on the secondary monitor. The main window disappears, the pill appears top-center of that monitor's work area 8 DIP below its top edge, and the app that was under shotAI is the foreground window (title bar active).

**AC-SHELL-8.** Manual: drag the pill by its label and by its hint row; it follows the cursor live without activating (the recorded app's title bar stays active). Stop, start again: the pill appears where it was left.

**AC-SHELL-9.** Manual: during a recording, click Pause once. The first click pauses (label `Paused · N` in amber, hint `Paused \u2014 press Resume to keep capturing`), the foreground window does not change, and no step is captured for that click.

**AC-SHELL-10.** Manual: immediately after starting a recording, click once on a button in another app. That first click produces a step (no "wake-up" click needed).

**AC-SHELL-11.** Manual: each capture flashes a green ring on the pill for about 0.7 s; with "Show animations in Windows" off the ring holds and then fades without motion and the dot does not pulse. Appending to a project that already has steps does not flash at start.

**AC-SHELL-12.** Manual (with a test hook that raises `CaptureFailed("")` and then `CaptureFailed("Disk full")`): the pill shows `A capture failed \u2014 see the log for details.`, then `Disk full`, in row 2 with a red top bar; hovering the text shows the full message; the next captured step clears it; `Dismiss` clears it; stopping clears it; the next session starts clean.

**AC-SHELL-13.** Manual: click the pill's `✕` on a new project's first recording: the dialog reads `Discard this capture? This is a new project, so the entire project will be deleted.`; Cancel keeps recording; Discard deletes the project and restores the main window. On an appended recording the text is `Discard this capture? Steps recorded in this session will be deleted.`

**AC-SHELL-14.** Manual: double-click Stop quickly: exactly one stop happens, the pill hides once, and the main window returns once.

**AC-SHELL-15.** Manual, two monitors at 100% and 150%: choose Area, the main window hides, both monitors show the hint `Drag to select a capture area` / `Press Esc to cancel` at 14% height and a crosshair; the overlay under the cursor has focus (Esc cancels without clicking first).

**AC-SHELL-16.** Manual: drag a 400 x 300 DIP rectangle on the 150% monitor: outside dims at 40% black, a 2 DIP indigo border is inside the rectangle, the badge reads `600 × 450px`; the capture target and the log line `region selected: 600x450 @ (...) [physical px]` agree with the badge; the main window returns in the foreground.

**AC-SHELL-17.** Manual: a click without drag, a 3 x 3 drag, a right click and Esc each cancel the selection and restore the main window; the chooser keeps its previous area.

**AC-SHELL-18.** Manual: with remote visibility ON and a Teams or Splashtop session watching, the Area overlay (dimming, rectangle, badge) is visible to the viewer; with it OFF, the viewer sees none of shotAI's windows.

**AC-SHELL-19.** `ShotAI.ProtectionProbe` (spec 02) run against the pill window with the setting OFF reports `CLEAN`, and a capture during a recording with the error tooltip open contains neither the pill nor the tooltip.

**AC-SHELL-20.** Manual: View then Brand is disabled on Home; with a project open and no pin, `App default (<app brand>)` is ticked; picking LFI ticks LFI and writes `"theme": "lfi"`; picking App default removes the key; a project whose `theme` is `solarpunk` shows nothing ticked, and picking App default removes the key.

**AC-SHELL-21.** Manual, with a debug build that re-raises the navigation state every second without changing it: open View then Brand and leave it open for 5 s. The submenu stays open and the tick does not flicker.

**AC-SHELL-22.** Manual: `Ctrl+O` on Home runs the import flow, `Ctrl+,` opens Settings; both do nothing while a recording is running (the main window is hidden; verify with a second launch surfacing it during the recording).

**AC-SHELL-23.** Manual: `Ctrl+Shift+=` (and `Ctrl+=`), `Ctrl+-`, `Ctrl+0` zoom the main window's content in half-level steps (factor `1.2 ^ 0.5`, about 1.095 per step) and back; F11 toggles full screen; `Ctrl+M` minimizes; `Ctrl+W` closes and exits.

**AC-SHELL-24.** Manual: Help then About shotAI shows title `About shotAI`, `shotAI <version>`, the tagline with the em dash, the `.NET` and `WebView2` line and `win32/<arch>`, the 64 x 64 icon, and one OK button. The WebView2 version comes from Platform's `IWebView2RuntimeInfo` via `IAppInfo.Current.WebView2Version` (shown as `not installed` when the runtime is missing), and the App has no WebView2 reference (R-ARCH-12, INV-ARCH-5; `Architecture.SingleWebViewTests` passes).

**AC-SHELL-25.** Log review after a normal session: the runtime line, `exiting (code 0)`; after a forced exception from a debug menu: an error entry with the stack.

**AC-SHELL-26.** Manual: disconnect the monitor where the pill was left, start a recording: the pill docks top-center of the main window's monitor.

**AC-SHELL-27.** Manual, while recording: sign out of Windows. The log shows teardown before exit; after signing back in no `shotAI.exe` remains and no mouse hook is installed (clicks feel normal; the next launch works).

**AC-SHELL-28.** Code review: no file in `ShotAI.App` calls `MessageBox.Show` (except the allowlisted `LegacyInstanceGuard.cs` until stage S5, Q-SHELL-19), `Dispatcher.Invoke`, `Dispatcher.BeginInvoke` or `Dispatcher.InvokeAsync` (except the allowlisted `WpfUiDispatcher.cs`, `StaRenderThread.cs` and `UiDeferral.cs`; capture events use `IUiDispatcher.Post` only, R-ARCH-11, R-ARCH-18), `.Result` or `.Wait()` on capture tasks; every window derives from `ShotAIWindow`.

**AC-SHELL-29.** Code review: `ShotAI.Core/Shell` references no `System.Windows`, `Windows.Win32` or `Microsoft.Win32` namespace (the Core project builds for `net10.0`).

**AC-SHELL-30.** Manual (spec 12): after installing the MSI offline (spec 12 AC-PKG-2 per-machine, and AC-PKG-36 per-user), Settings then Apps then Installed apps shows shotAI with the shotAI icon, and the Start menu shortcut launches `shotAI.exe`.

**AC-SHELL-31.** Manual, primary at 100% and secondary at 150%: launch with the cursor on the secondary. The main window appears centered in the secondary's work area at 720 DIP wide (1080 physical px), not rescaled a second time (EDGE-SHELL-48); start a recording from there and the pill docks top-center of the secondary at 380 DIP (570 px) wide.

**AC-SHELL-32.** Manual, with Notepad open behind shotAI: start and stop a recording three times. After every start the pill is visible and never the active window (Notepad's title bar turns active once shotAI's main window hides), including on the second and third show (Q-SHELL-21).

**AC-SHELL-33.** Manual (startup auto-archive, 7.4.1 step 13; needs Home, so it is checked from PLAN WP-A16 on): set `archiveAgeDays` to 90, give one project an `updatedAt` 91 days old and another 89 days old, launch. Without any interaction Home shows the 91-day project under Archive (its folder holds `archive.zip`) and the 89-day project under Projects. With `archiveAgeDays` 0 nothing moves. A locked project folder logs the per-project warning `auto-archive failed for <path> (non-fatal):` (`src/main/project-store.ts:629`) or, if the sweep itself throws, `startup auto-archive failed (non-fatal):`, and the app starts normally.

---

## 10. Interfaces with other subsystems

| Spec | This subsystem consumes | This subsystem provides |
|---|---|---|
| 01 Model and store | `ShotAI.Core.Model.Rect` (the area result type); `ShotAI.Core.Json.JsMath` (the one copy, R-ARCH-1); the `SetProjectTheme` write rule (null clears, a brand pins, raw no-op compare) that 05's operation applies | nothing new; brand choices through the store only (INV-SHELL-17) |
| 02 Capture | `ICaptureService` (`GetState`, `Pause`, `Resume`, `StopAsync`, `DiscardAsync`, `Teardown`, events `StateChanged`, `CaptureFailed`, `RecordingChanged(recording, showPill)`); `CaptureState.WillDeleteProjectOnDiscard`, `StepCount`; `OwnWindowRegistry.Register/Unregister`; `CaptureShield.ApplyRemoteVisibility`, `ExcludedForNewWindow` | every own HWND registered before show (INV-SHELL-1), including dialogs and popups; the pill's non-activating style (spec 02 section 6 relies on it); area rectangles in global physical px for `CaptureTarget.Area`; `JsMath` use for all shell rounding |
| 04 Editor | nothing | the main window as host; UI zoom transform applies above the editor (the editor's own zoom is separate); the rule that the `ChooseColor` dialog's HWND is registered on `WM_INITDIALOG` and its `AllWindowsRegisteredTests` case (7.4.7, Q-EDIT-21) |
| 05 Report | `DocScale.DetailWindowWidth` (Core, returns `int`); navigation state (`IShellNavigationState`: open project path, raw `theme`); the committed scale; the open project's session and `SetProjectThemeOperation` for View then Brand; the stepper hold (answers Q-SHELL-13) | `IMainWindowLayout.SetDetailView(bool open, double scale)`; `IAreaSelectionService.SelectAreaAsync(Window, ct)` for the insert modal; the in-window overlay layer and the shared notice control (7.4.10) |
| 06 Home and settings | navigation state (`NavigationState` implements `IShellNavigationState`); the shared notice control and the `Something went wrong. See the log for details.` wording (Q-SHELL-16) | `ImportProjectRequested`, `OpenSettingsRequested` events (not raised while recording); `IAreaSelectionService` for the chooser; `IMainWindowLayout.SetDetailView(false, 1)` on Home; the startup triggers behind `ProjectsChanged` (auto-archive) and `UpdateAvailable` (startup update check), whose subscribers marshal with `IUiDispatcher.Post` themselves (ARCHITECTURE 5.6, T6); the overlay layer and the notice control (7.4.10); `ShotAIPopup` and `PopupExclusion` (7.4.7) |
| 09 Exports | the correction of 2.10.2 (print copy navigated by file URI, EDGE-EXP-39, Q-EXP-17); `IBrandFontSource` (the interface `AppPaths` implements). No WebView2 package or call: the About line's runtime version comes through spec 11's `IAppInfo` (R-ARCH-12) | `AppPaths.BrandFontPath()`; the crash-logging expectation for `ProcessFailed` in the WebView2 host |
| 10 Brand, settings and infra | `BrandPalette` (`BrandIds`, `Get(id).Label`, `PinnedBrand`, `CoerceBrand`, `PinIsUnrecognised`); `IAppPaths` (`FontsDirectory`; `LocalDataDirectory` = `%LOCALAPPDATA%\LFI\shotAI\` for any native-only local data, R-ARCH-13), implemented by this spec's `AppPaths`; synchronous settings load before windows (`SettingsService.Load`); `Current.RemoteVisible`, `Current.Brand`; logger factory; self-test switches; `Current.ArchiveAgeDays`; update check entry point; pending-update stash | startup order (7.4.1), crash handlers, runtime line, exit line; the Brand submenu (INV-SHELL-16) |
| 11 Service boundary | `IUiDispatcher.Post` (the only marshaling path for service events, R-ARCH-11), `IAppLifetime.Stopping`, `IAppStartup`, `ShutdownFlush`, `ViewModelBase`, `IAppInfo.Current` for About (its `WebView2Version` read through Platform's `IWebView2RuntimeInfo`, R-ARCH-12), `IExternalLinks`, the composition root rules and the exit order (7.10), the analyzer bans (T6, T9) and their per-file allowlists (ARCHITECTURE 14.9, R-ARCH-18) | `RecordingVisibilityController` as the single ordered marshaling path for capture events; `IAreaSelectionService`, `IMainWindowLayout`, `AppMenuViewModel` (`ChooseBrandCommand` passes the clicked value untouched) |
| 12 Packaging | Start menu shortcut, ARP icon (`ARPPRODUCTICON`), the MSI (spec 12 decided MSI over MSIX, INV-PKG-1; one dual-purpose package, per-machine through Intune and per-user by hand, spec 12 7.4.5), PMv2 manifest (already present), x64 and ARM64 builds, `Program.Main` with `SetDefaultDllDirectories` first, `PersonalCopyGuard` (ARCHITECTURE step 1b, the second half of step 1, before the mutex) and `LegacyInstanceGuard` (7.4.1 step 2a, the second half of step 2) | the requirement that installer actions never launch the app expecting it to run to completion (EDGE-SHELL-1); AC-SHELL-30 |

---

## 11. Open questions and risks

**Q-SHELL-1. Single-instance scope across logon sessions.** One user signed in twice (console plus RDP) gets two instances sharing one projects folder, because `Local\` is per session and a cross-session instance could not be activated anyway. Recommended default: `Local\` plus SID (parity in practice); revisit only if support sees torn manifests from multi-session users, in which case add a `Global\` mutex check that shows `shotAI is already running in another Windows session.` and exits.

**Q-SHELL-2. Pill corners.** Electron's pill is a plain rectangle; macOS rounds 10 pt. Recommended default: rectangle (parity); optionally `DWMWCP_ROUND` on Windows 11 later, never `AllowsTransparency` for the pill.

**Q-SHELL-3. Tooltips and popups in a non-activating window.** Whether WPF opens `ToolTip`s for a window that is never active, and whether a popup HWND can be visible for one frame before `Opened` registers it, are both unverified. Recommended default: build `CapturePillWindowTests.ErrorTooltipShowsWhileInactive` and `AllWindowsRegisteredTests` first; if tooltips do not open, replace them on the pill with a `ShotAIPopup` opened on `MouseEnter` after 400 ms; if the popup HWND is visible before registration, set the popup's affinity from a `HwndSource` hook on `WM_CREATE` (`HwndSource.AddHook` via `PresentationSource.AddSourceChangedHandler`), or hide the tooltip during grabs. A catch-all candidate that also covers templated popups (the `ComboBox` drop-down, 7.4.7): a thread-local `SetWindowsHookEx(WH_CALLWNDPROC, ..., null, uiThreadId)` that, for any top-level window of the UI thread receiving `WM_WINDOWPOSCHANGING` with `SWP_SHOWWINDOW` (or `WM_SHOWWINDOW` with `wParam = TRUE`), registers the HWND before it becomes visible; the hook runs synchronously before the window procedure. Its reliability for WPF's layered popup windows is UNVERIFIED; `AllWindowsRegisteredTests` decides.

**Q-SHELL-4. `WS_EX_NOACTIVATE` and dragging.** The outline-drag and activation behavior of the system move loop for no-activate windows varies by Windows version. Recommended default: the manual drag in 7.4.3, which sidesteps the question; confirm live dragging with AC-SHELL-8.

**Q-SHELL-5. Chromium's exact `DIPToScreenRect` rounding.** The spec assumes origin floored and size ceiled after Electron's own `Math.round` of the DIP rect. Recommended default: implement as written; on a 125% and a 150% monitor compare the Electron log line `region selected:` with the native one for the same drag endpoints and adjust `ToPhysical` if they differ.

**Q-SHELL-6. Clamp cross-monitor drags?** Electron and macOS allow a drag to leave the starting overlay; the capture then clips. Recommended default: parity (no clamp); revisit if users report confusing partial rectangles.

**Q-SHELL-7. Discard dialog buttons.** Parity would be `OK` and `Cancel` with OK as default. Recommended default: `Discard` and `Cancel`, Cancel default (D8).

**Q-SHELL-8. Keyboard access to the pill.** A non-activating pill cannot take keyboard focus, so Tab cannot reach Pause or Stop (Electron's activating pill could be tabbed after a click). Narrator can still invoke buttons through UI Automation. Recommended default: accept for 2.0.0; add a global hotkey for Stop only if accessibility review requires it (that would be a spec 02 hotkey decision).

**Q-SHELL-9. Window click-pick.** Neither Electron nor macOS has a "click a window to pick it" mode; Window targets are picked from a list (spec 06). Recommended default: do not add one in the port.

**Q-SHELL-10. macOS menu extras** (`Check for Updates…`, File then Export, `Export shotAI Logs…`). Recommended default: not at 2.0.0 (parity with Windows 1.3.0); propose after cutover.

**Q-SHELL-11. Full-screen behavior details.** Electron's F11 on Windows may hide the menu bar and cover the taskbar; unverified. Recommended default: cover the monitor (taskbar included), keep the menu visible, restore exactly on the second F11.

**Q-SHELL-12. UI zoom persistence and scope.** Chromium may persist the zoom level per origin across launches; unverified for `file:` pages. Recommended default: do not persist; zoom applies to the main window's content only (not the pill, overlays or dialogs). If the pilot misses persistence, add a `uiZoomLevel` setting (spec 10).

**Q-SHELL-13. Stepper hold** (macOS delays the window resize while the pointer is on the scale stepper). Recommended default: not in this spec; spec 05 decides whether its scale control commits in a way that moves itself under the pointer. Answered by spec 05 (IMPROVEMENT, D-REP-17): `ProjectDetailViewModel` defers `SetDetailView(true, committed)` while the pointer is over the spinner buttons; `MainWindowSizer` needs no change.

**Q-SHELL-14. Initial monitor.** Electron's default centering monitor is not verified (it may be the primary display rather than the cursor's). Recommended default: the cursor's monitor (WPF `CenterScreen` semantics, documented by Microsoft), since that is where the user just launched the app.

**Q-SHELL-15. Remember window size and position?** Neither Electron nor native persists it; macOS persists it and needed workarounds (#56). Recommended default: do not persist.

**Q-SHELL-16. Mark UI-thread exceptions handled?** Parity keeps running after an uncaught exception, which can leave a view half-updated. Recommended default: log and `Handled = true`, and additionally show the standard error notice `Something went wrong. See the log for details.` in the main window if it is visible (wording confirmed by spec 06 Q-HOME-16); do not keep running after an exception from `OnStartup` (let it terminate and log). ARCHITECTURE 8.3 adopts this default; 7.4.9 states it as the design.

**Q-SHELL-17. Pill during a no-click screenshot's 350 ms settle.** Electron shows no pill; the main window is hidden and nothing tells the user a grab is pending. Recommended default: parity (no HUD), because any HUD would be one more window to shield.

**Q-SHELL-18. A second launch during a recording.** Parity surfaces the main window over the capture (EDGE-SHELL-31). Recommended default: parity; the main window's recording panel (spec 06) then offers Stop, which is a useful recovery path.

**Q-SHELL-19. The `LegacyInstanceGuard` notice is a `MessageBox`.** Spec 12 7.10.3 shows a Win32 `MessageBox` (caption `shotAI`) when an Electron 1.x instance is running, while this spec forbids `MessageBox` everywhere (7.4.5, AC-SHELL-28) because it cannot be registered for capture exclusion before it is shown. The notice appears before any shotAI window exists and the process exits after it, but it can be captured by the RUNNING Electron build's recording. Recommended default: keep spec 12's `MessageBox` for the pilot (it is removed at stage S5), allowlist that one call in `NoWin32MessageBoxInSource`, and revisit only if a pilot user reports it in a capture; the alternative is a registered `ShotAIWindow` notice, which needs the registry before step 2a. The default is the one ARCHITECTURE 15.4 records and 14.9 implements (the `MessageBox.Show` ban allowlists only `LegacyInstanceGuard.cs` until S5); AC-SHELL-28 and 7.4.5 carry the same single exception.

**Q-SHELL-20. Does Electron's `width` include the invisible resize borders?** WPF's `Width` and `GetWindowRect` include them (about 7 px per side on Windows 10 and 11). If Electron's 720 and 1010 were the visible width, the native windows would look about 14 px narrower. Recommended default: keep the constants as outer widths (parity with the numbers); on a Windows machine compare `DwmGetWindowAttribute(DWMWA_EXTENDED_FRAME_BOUNDS)` of the Electron window at Home with the native one and, if they differ, add the border to `WindowLayout` inputs once, in the App, never in Core.

**Q-SHELL-21. Does `ShowActivated="False"` apply to every `Show()` after a `Hide()`?** Microsoft Learn documents it as "when first shown". Recommended default: rely on it plus `WS_EX_NOACTIVATE`, pinned by `CapturePillWindowTests.ShowDoesNotActivate` (show, hide, show). If a later show activates, switch the pill to one path only: after its first `Window.Show()`, hide and show it exclusively with `ShowWindow(SW_HIDE)` and `ShowWindow(SW_SHOWNOACTIVATE)` and never call `Window.Show()` or `Hide()` on it again (never mix the two paths, 7.4.6).

**Q-SHELL-22. Registering the system file dialogs before they are visible.** `Microsoft.Win32.OpenFolderDialog`, `OpenFileDialog` and `SaveFileDialog` (behind spec 11's `IFileDialogs` and spec 09's `IExportDialogs`) give no access to their HWND before it is shown, and `ChooseColor`'s `WM_INITDIALOG` hook (spec 04, Q-EDIT-21) has no equivalent there. Recommended default: the thread-local show hook of Q-SHELL-3, scoped to each `ShowDialog` call (7.4.7); if `AllWindowsRegisteredTests` shows the dialog visible before registration, replace the WPF wrappers with a direct `IFileDialog` implementation that registers the HWND (`IOleWindow.GetWindow`) from its first `IFileDialogEvents` callback. Whether that first callback runs before the dialog is visible is UNVERIFIED; the test decides. Either path keeps the public `IFileDialogs` and `IExportDialogs` contracts unchanged.

**Risk R1 (high).** A popup, dialog or late-created window shown without its capture exclusion puts shotAI into a finished SOP silently. Mitigated by `ShotAIWindow`, `PopupExclusion`, INV-SHELL-1 and the HWND-sweep test.

**Risk R2 (medium).** WPF focus or activation logic re-activating the pill through an unexpected path (a tooltip, a `Focus()` call in a control template, a future style change). Mitigated by `Focusable=False`, `MA_NOACTIVATE` and the foreground assertions in `CapturePillWindowTests`.

**Risk R3 (medium).** Mixed-DPI positioning errors (overlay not exactly on its monitor, pill docked on the wrong monitor, resize off by one DPI factor). Mitigated by doing monitor-targeted positioning in physical px (7.7) and AC-SHELL-15 and AC-SHELL-16 on a 100% plus 150% rig.

**Risk R4 (low).** Menu parity drift: the WPF `Menu` looks different from the Win32 menu bar Electron used. Accepted; labels, order, gestures and enable rules are what this spec pins.
