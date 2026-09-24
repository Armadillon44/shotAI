# 02 Capture engine

> Spec for the native rewrite. Sources read: `src/main/CaptureController.ts` (1485 lines), `src/main/capture-geometry.ts` (65), `src/main/click-caption.ts` (64), `src/main/element-locator.ts` (126), `native/element-locator/src/lib.rs` (204), `native/element-locator/Cargo.toml` (26), `src/main/remote-visibility.ts` (90), `src/main/capture-selftest.ts` (246), `scripts/protection-probe.cjs` (122); tests `src/main/capture-geometry.test.ts` (78), `src/main/capture-shield.test.ts` (213), `src/main/click-caption.test.ts` (50). Context read: `src/main/main.ts` (595), `src/main/ipc.ts` (981), `src/main/project-store.ts` (1099), `src/main/settings.ts` (434), `src/main/RegionService.ts` (151), `src/shared/project.ts` (554), `src/shared/ipc.ts` (624), `src/renderer/toolbar/App.tsx` (180), `src/renderer/project/merge.ts` (106), `node_modules/get-windows/Sources/windows/main.cc` (299, get-windows 9.3.0), `node_modules/uiohook-napi/libuiohook/src/windows/input_hook.c` (722, uiohook-napi 1.5.5), `node_modules/node-screenshots/index.d.ts` (node-screenshots 0.2.8, binary only, no source); git history of every source file (commits `53045c6`, `58131a8`, `f24b3dc`; history before `9da70df` is squashed into that import commit). macOS: `Packages/CaptureKit/Sources/CaptureKit/*.swift` (13 files, 2731 lines), `Packages/CaptureKit/Tests/CaptureKitTests/*.swift` (7 files, 1402 lines), `shotAI/Capture/CaptureCoordinator.swift` (331), `shotAI/Capture/AppOwnWindows.swift` (39), `shotAI/Capture/CoordinateSpaces.swift` (25). Verification pass also read: `src/renderer/project/App.tsx` (step-added consumer), `src/renderer/toolbar/App.tsx`, `src/preload/preload.ts`, `dotnet/src/ShotAI.Platform/CaptureExclusion.cs`, `dotnet/Directory.Packages.props`, spec 01 store and `PathConfine` signatures, Microsoft Learn pages for `LowLevelMouseProc`, `SetWindowDisplayAffinity`, `IUIAutomation2` and WIC threading. Status: extracted and verified. Consolidated with ARCHITECTURE.md R-ARCH-1 to R-ARCH-26 on 2026-09-23.

**Notation used in this document.**

- Several Electron strings contain the character U+2014 (EM DASH). This document never prints that character. Wherever it occurs in a quoted string it is written as the escape `\u2014`, and the C# literal must contain that exact character (C# accepts the same `\u2014` escape inside a string literal, so copying the quoted text into C# source is exact).
- `round(x)` everywhere below means **JavaScript `Math.round`**, ported only as `ShotAI.Core.Json.JsMath.Round` (01 7.2.3, R-ARCH-1), whose exact definition is `f = Math.Floor(x); return (x - f >= 0.5) ? f + 1 : f;` with NaN and infinities returned unchanged (INV-CAP-18). Descriptively (not as a definition): the nearest integer, halves toward positive infinity. It is NOT C# `Math.Round` (banker's rounding), NOT `MidpointRounding.AwayFromZero` (which differs for negative halves), and NOT the expression `Math.Floor(x + 0.5)`, which is wrong at `0.49999999999999994` (it returns 1, JavaScript returns 0).
- `floor`, `min`, `max`, `abs` are the ordinary functions. Integer division never appears; every division is real division unless wrapped in `floor`.
- "px" means physical pixels in the global virtual-desktop space unless stated otherwise ("image px" = pixels of the stored PNG, "logical px" = physical px divided by the monitor scale factor).

---

## 1. Scope

### 1.1 Owned by this subsystem

| Area | What |
|---|---|
| Session state machine | start (new or append), pause, resume, stop, discard, no-click screenshot insert, multi-step insert with a rolling cursor, the legacy single-click insert, orphan seeding of the filename counter, the one-session-at-a-time guard |
| Triggers | the global mouse hook (per button semantics), the global hotkey `Ctrl+Shift+S`, double-click collapse, right-click context-menu arming, the poll timer, the proximity gate, the chain limit, the poll frame cap |
| Capture queue | FIFO serialization of captures, backlog suppression on pause and stop, error surfacing |
| Grab paths | menu selection, window (picked or auto), area, auto region, fullscreen; the auto-mode classifier; monitor resolution and fallbacks; crop math |
| Self-exclusion | the own-window guard (pid, foreground and geometric), the single shielded grab funnel, the reference-counted display-affinity shield (remote visibility), the hide-settle delay |
| Output | global px vs image px coordinates, the downscale policy and readability floor, PNG encoding, `step-NNNN.png` naming and exclusive writes, the `ProjectStep` a capture produces, auto captions |
| Element at point | UI Automation hit test, the climb to an actionable ancestor, the allowlist, the timeout, warm-up |
| Target listing | the window and monitor lists for the Window and Screen choosers (`listTargets`) |
| Events | state changed, step landed, capture error, recording changed (the hide or show hook) |
| Diagnostics | the capture self-test and the protection probe, and their native equivalents |

### 1.2 Not owned (and who owns it)

| Not owned | Owner |
|---|---|
| The `project.json` schema, `ProjectStep` fields, `addStep`, `insertStepAt`, `deleteSteps`, `deleteProject`, `openProject`, `renumber`, the store write queue, path confinement primitives | 01 |
| The capture pill window, the main-window hide and restore, the area-select overlay and its DIP to physical conversion, the Discard confirmation dialog, window creation order (protected first) | 03 |
| The editor, click marker rendering, flatten | 04 |
| The report's insert gaps, the capture-insert modal, the right-click merge (`merge.ts`) | 05 |
| The home capture-mode picker, Settings controls for screenshot quality and remote visibility | 06 |
| How captions, `window.app`, `window.title` feed the SOP prompt | 07 |
| `settings.json` keys `captureScale` and `remoteVisible`, their clamps and synchronous caches, logging infrastructure, self-test switches | 10 |
| The C# service interface surface and UI-thread marshaling rules | 11 |
| Packaging (no Rust DLL, CsWin32 build, ARM64) | 12 |

---

## 2. Reference behavior (Electron)

### 2.1 Types the engine uses

From `src/shared/project.ts` and `src/shared/ipc.ts` (the schema itself is spec 01).

| Type | Shape | Citation |
|---|---|---|
| `CaptureMode` | `'auto' \| 'window' \| 'area' \| 'screen'` | `src/shared/project.ts:22` |
| `CaptureTarget` | `{ mode; monitorId?: number; window?: { id; pid; title }; area?: Rect }` (area in global physical px) | `src/shared/project.ts:25-33` |
| `WindowInfo` | `{ id; pid; title; app }` | `src/shared/project.ts:36-41` |
| `MonitorInfo` | `{ id; name; width; height; isPrimary }` | `src/shared/project.ts:44-50` |
| `CapturedWindow` | `{ app; title; pid; bounds: Rect \| null }` | `src/shared/project.ts:88-94` |
| `CapturedMonitor` | `{ id; bounds: Rect; scaleFactor }` | `src/shared/project.ts:96-100` |
| `StepClick` | `{ global: Point; image: Point; button: 'left'\|'right'\|'middle'\|'other'; radius?; imageScale? }` | `src/shared/project.ts:102-116` |
| `StepElement` | `{ available; name: string\|null; controlType: string\|null; bounds: Rect\|null }` | `src/shared/project.ts:119-124` |
| `CaptureStatus` | `'idle' \| 'recording' \| 'paused'` | `src/shared/ipc.ts:31` |
| `CaptureState` | `{ status; projectPath: string\|null; projectTitle: string\|null; stepCount; willDeleteProjectOnDiscard }` | `src/shared/ipc.ts:33-44` |
| `Session` (internal) | `projectPath, projectTitle, paused, stepCount, target, stepCountAtStart, createdThisSession, addedStepIds[], single?: {insertAt, fired?}, insertCursor?` | `src/main/CaptureController.ts:221-244` |
| `menuFollowUp` (internal) | `{ until; ownerBounds: Rect\|null; lastPoint; menuFrame: {image, monitor}\|null; chain }` or `null` | `src/main/CaptureController.ts:294-302` |
| `Grab` (internal) | `{ png; originX; originY; monitor }` (origin = global px of the image top-left) | `src/main/CaptureController.ts:214-219` |

`CAPTURE_SCALE_MIN = 0.5`, `CAPTURE_SCALE_MAX = 1`, `CAPTURE_SCALE_DEFAULT = 0.85` (`src/shared/project.ts:18-20`). The engine reads the current value synchronously through `captureScaleNow()` (`src/main/settings.ts:291`), primed at startup (`src/main/main.ts:415`). REQUIRED.

### 2.2 Session lifecycle

#### 2.2.1 Entry points

| Call | Behavior | Citation |
|---|---|---|
| `start(projectPath, {attachHook=true, target=DEFAULT_TARGET, createdThisSession=false, insertAt=null})` | See 2.2.2. Returns `CaptureState`. | `src/main/CaptureController.ts:666-743` |
| `captureSingle(projectPath, insertAt)` | Legacy one-click insert. See 2.2.4. No renderer caller exists (only `src/preload/preload.ts:205`). | `src/main/CaptureController.ts:751-792` |
| `captureScreenshot(projectPath, target, insertAt)` | No-click one-shot for the report "+ Screenshot". See 2.2.5. Returns the updated manifest. | `src/main/CaptureController.ts:806-897` |
| `pause()` | `session.paused = true` (if a session exists), disarm the menu, log `recording paused`, emit state, return state. Runs even with no session (emits idle). | `src/main/CaptureController.ts:929-935` |
| `resume()` | `session.paused = false` (if a session exists), disarm the menu, log `recording resumed`, emit state. | `src/main/CaptureController.ts:937-943` |
| `stop()` | See 2.2.6. | `src/main/CaptureController.ts:945-955` |
| `discard()` | See 2.2.7. Returns `{ state, projectDeleted }`. | `src/main/CaptureController.ts:962-990` |
| `getState()` | See 2.2.8. | `src/main/CaptureController.ts:629-646` |
| `listTargets()` | See 2.10. | `src/main/CaptureController.ts:1061-1085` |
| `teardown()` | Synchronously detach triggers (app quit). Wired to `before-quit` together with `globalShortcut.unregisterAll()` "so the uiohook worker thread can't keep the process alive (zombie) on Windows". | `src/main/CaptureController.ts:925-927`, `1480-1483` |
| `captureStep(trigger, point, button='left', opts)` | The single capture routine (2.6). Public so the self-test drives it. | `src/main/CaptureController.ts:1088-1465` |

IPC coercion that the engine relied on (the native engine must enforce these itself, spec 11): `capture:start` opts become `{ createdThisSession: v.createdThisSession === true, insertAt?: max(0, round(v.insertAt)) }` when `insertAt` is a finite number (`src/main/ipc.ts:105-113`); `capture:single` and `capture:screenshot` use `Number.MAX_SAFE_INTEGER` (append) when `atIndex` is not a finite number (`src/main/ipc.ts:917`, `934`); `capture:screenshot` throws `'A screenshot needs an explicit target (screen, window, or area).'` when the target is missing or `auto` (`src/main/ipc.ts:927-930`); `parseCaptureTarget` returns `undefined` for a `null`/`undefined` target (so `start` falls back to `DEFAULT_TARGET`), throws `'target must be an object'` for a non-object and `'target.mode is invalid'` for a mode outside the four, then keeps only the fields for the chosen mode and silently drops malformed ones (a `window` target whose `id`/`pid` are not finite numbers or whose `title` is not a string becomes `{ mode: 'window' }`; an `area` with any non-finite field becomes `{ mode: 'area' }`) (`src/main/ipc.ts:115-138`; `isNum` = finite number, `:97`). A `{ mode: 'window' }` recording then warns `picked window not found \u2014 falling back to monitor capture` on every step; a `{ mode: 'area' }` recording silently captures the click monitor (2.8).

#### 2.2.2 `start` in order

1. If a session exists: same `projectPath` string, return the current state (target, `insertAt` and `createdThisSession` of the second call are ignored); different path, throw `'A recording is already in progress for another project'` (`:682-687`).
2. Load natives (`:689`).
3. `projectStore.openProject(projectPath)`: confined read, marks recently opened, throws if the project is unknown (`:692`).
4. `mkdir(<project>/shots, recursive)` (`:695-696`).
5. `stepCountAtStart = manifest.steps.length` (`:700`).
6. Orphan seed: `stepCount = manifest.steps.length`, then for every file name in `shots/` matching `/^step-(\d+)\.png$/i`, `stepCount = max(stepCount, Number(m[1]))`. A `readdir` failure falls back to the manifest length (`:704-712`).
7. `insertCursor = insertAt == null ? undefined : max(0, min(round(insertAt), manifest.steps.length))` (`:716-719`).
8. Install the session: `paused=false`, `target`, `stepCountAtStart`, `createdThisSession = opts.createdThisSession ?? false`, `addedStepIds=[]`, `insertCursor` (`:721-731`).
9. Log `recording started: "<title>" [mode=<mode>]` plus ` [insert@<n>]` when inserting, plus ` (<k> existing steps, next #<stepCount+1>) at <path>` (`:733-735`).
10. `warmUpElementLocator()` (`:738`).
11. If `attachHook`, `attachTriggers()` (mouse hook and hotkey) (`:739`).
12. `onRecordingChange(true)` (main window hides, pill shows) (`:740`).
13. Emit state; return state (`:741-742`).

Note: steps 2 to 7 are awaited before the session is installed, so two concurrent `start` calls both pass step 1 (a TOCTOU gap). See EDGE-CAP-45.

#### 2.2.3 Append, multi-step insert (rolling cursor)

- **Append** (no `insertAt`): each step goes through `projectStore.addStep` (push then renumber) (`:1450`).
- **Multi-step insert** (report "+ Capture" at a gap, `insertAt` set on `start`): each captured step is spliced at `session.insertCursor` through `projectStore.insertStepAt`, and after a successful insert `insertCursor = insertIndex + 1` (`:1438-1448`). The cursor is read and advanced inside the serialized `captureStep`, so rapid clicks land at i, i+1, i+2 in order (`:238-243`). A step that fails before the insert does not advance the cursor.
- A fixed `opts.insertAt` (single and screenshot paths) wins over the cursor and never advances it (`:1438-1447`).
- `insertStepAt` clamps again inside the store: `i = atIndex == null ? length : max(0, min(round(atIndex), length))`, splices, renumbers (`src/main/project-store.ts:1013-1037`).
- The store renumbers `order = index + 1` on every add and insert (`src/main/project-store.ts:748`, `838-842`), so `step.order` in the manifest is the array position, not the filename counter.

#### 2.2.4 `captureSingle` (legacy, unreachable from the UI)

1. If any session exists, throw `'A recording is already in progress'` (`:752-754`).
2. Load natives, open the project, mkdir `shots/`, orphan seed (same regex) (`:755-769`).
3. `at = max(0, min(round(insertAt), manifest.steps.length))` (`:771`).
4. Session: `target = DEFAULT_TARGET` (auto), `stepCountAtStart = length`, `createdThisSession=false`, `single = { insertAt: at }` (`:772-782`).
5. Log `single-shot capture armed: insert at index <at> into "<title>"` (`:783-785`).
6. `attachTriggers({ hotkey: false })`: mouse only, because "the hotkey path can't carry the insert index and wouldn't auto-stop" (`:786-788`).
7. `onRecordingChange(true)` (pill shows), emit state, return state (`:789-791`).
8. On the first mousedown not on an own window: `single.fired = true`, enqueue `captureStep('click', point, button, { insertAt, elementPromise })`, then `void this.stop()` fire-and-forget (awaiting would deadlock because `stop` awaits the queue that contains this task). A null step logs `single-shot: click at (<x>,<y>) captured nothing \u2014 ending the session (the window is restored; retry the insert)` (`:325-343`). Any button counts. No double-click or menu logic.

Classification: ELECTRON-ONLY (dead path). See Q-CAP-1.

#### 2.2.5 `captureScreenshot` (no-click one-shot, report "+ Screenshot") in order

1. If any session exists, throw `'A recording is already in progress'` (`:811-813`).
2. Load natives, `openProject`, mkdir `shots/` (`:814-817`).
3. Validate the explicit target BEFORE hiding (`:822-840`):
   - `window`: if `resolveWindow(target.window)` (2.10.3) is null, throw `'That window is no longer open \u2014 reopen it and try the screenshot again.'`
   - `area`: on screen iff `area` exists and intersects some monitor: `a.x < m.x + m.width && a.x + a.width > m.x && a.y < m.y + m.height && a.y + a.height > m.y`. Otherwise throw `'That screen area is off-screen now \u2014 drag the area again and retry.'`
   - `screen`: no validation (a missing or stale `monitorId` falls back to the click monitor, which with no point is the primary).
4. Orphan seed; `at = max(0, min(round(insertAt), length))` (`:843-853`).
5. Session: `target`, `createdThisSession=false`, `single = { insertAt: at, fired: true }` (`fired` is belt and braces, no hook is attached) (`:857-867`). Log `no-click screenshot armed: [mode=<mode>] insert at index <at> into "<title>"`.
6. `onRecordingChange(true, { pill: false })`: hide the main window, do NOT show the pill. Deliberately NO state emit ("a recording status would unmount the report view mid-grab") (`:871-876`).
7. `try`: wait `HIDE_SETTLE_MS` (350 ms); `captureStep('hotkey', null, 'left', { insertAt: at, broadcast: false, skipOwnWindowGuard: true })`; a null step throws `'Could not capture the screen \u2014 make sure the target is visible, then try again.'` (`:877-890`).
8. `finally`: `session = null`, `onRecordingChange(false)` (restore and focus), emit state (idle) (`:891-895`).
9. Return `projectStore.openProject(projectPath)` (the re-read manifest) (`:896`).

The step has `trigger: 'hotkey'`, `click: null`, and the hotkey caption (2.9.3) naming whatever window is foreground after the hide.

Further facts (verified against source):

- `captureStep` is called directly, NOT through `enqueue`, so any exception it throws (an `EEXIST` collision, a store failure, an unknown project) propagates to the IPC caller as a rejected promise. It is never broadcast as `capture:error` (EDGE-CAP-58).
- The session exists from step 5 until the `finally` (at least 350 ms). During that window: `start(sameProjectPath)` passes the session check and returns a `recording` state with no hook attached; `start(otherPath)` throws `'A recording is already in progress for another project'`; `stop()`, `discard()` or `pause()` from IPC clear or pause the session, so `captureStep` returns null and the call throws `'Could not capture the screen \u2014 ...'` (and `stop` or `discard` also run `onRecordingChange(false)` early). No UI does this today (the pill is not shown and the report awaits the call). See EDGE-CAP-51 and D21.
- `onRecordingChange(true, { pill: false })` runs before the `try`; if it threw, the session would stay installed. It does not throw in practice (`src/main/main.ts:426-446` only calls `hide`, `show`, `focus`).

#### 2.2.6 `stop`

`wasRecording = session !== null`; `count = session?.stepCount ?? 0` (read BEFORE the drain, so the logged count is the filename counter and excludes captures that commit during the drain, EDGE-CAP-55); `detachTriggers()` (which disarms the menu); `await queue` (errors swallowed) so in-flight and already-queued captures finish; `session = null`; log `recording stopped (<count> steps total)`; `onRecordingChange(false)` only if `wasRecording`; emit state; return state (`:945-955`).

Queued captures run to completion during the drain because the session is still set and not paused. REQUIRED.

#### 2.2.7 `discard`

`s = session`; `detachTriggers()`; `await queue` (the session is still set, so in-flight steps still record their ids); `session = null`; if `s`: if `discardDeletesProject(s)` then `projectStore.deleteProject(s.projectPath)`, `projectDeleted = true`, log `capture discarded \u2014 deleted new project at <path>`; else if `s.addedStepIds.length` then `projectStore.deleteSteps(s.projectPath, s.addedStepIds)` and log `capture discarded \u2014 removed <n> session step(s) from <path>`; else log `capture discarded \u2014 nothing was captured this session`. Any cleanup error is logged at warn (`discard cleanup failed:`) and swallowed (a failed whole-project delete returns `projectDeleted: false`). Then, still inside `if (s)`, `onRecordingChange(false)`. Always emit state (outside the `if`). Return `{ state, projectDeleted }` (`:962-990`).

A PNG whose store write failed was never added to `addedStepIds` (the push happens after the store call, `:1453`), so discard does not delete it; it stays as an orphan that seeds the next counter (EDGE-CAP-52).

`discardDeletesProject(s) = s.createdThisSession && s.stepCountAtStart === 0 && !s.single` (`:654-656`). The same predicate feeds `willDeleteProjectOnDiscard`, "one source of truth for discard() and the pill's R5 warning" (`:648-653`).

`deleteSteps` removes the ids, renumbers, and best-effort deletes each removed step's `screenshot` and `flattened` files after symlink-hardened confinement (`src/main/project-store.ts:871-903`).

#### 2.2.8 `getState`

Idle: `{ status: 'idle', projectPath: null, projectTitle: null, stepCount: 0, willDeleteProjectOnDiscard: false }`. Otherwise `{ status: paused ? 'paused' : 'recording', projectPath, projectTitle, stepCount: session.stepCount, willDeleteProjectOnDiscard: discardDeletesProject(session) }` (`:629-646`).

`stepCount` is the **filename counter** (seeded past orphans), not the number of steps captured this session. See EDGE-CAP-49 and the IMPROVEMENT in 7.10.

#### 2.2.9 Session state machine

States: `Idle`, `Recording`, `Paused`, `Draining` (inside `stop` or `discard`, session still set, triggers detached), `SingleArmed`, `SingleFired`, `Screenshot` (inside `captureScreenshot`).

| State | Event | Guard | Action | Next |
|---|---|---|---|---|
| Idle | `start(p)` | natives load, project opens | seed, install session, warm up UIA, attach hook and hotkey, `onRecordingChange(true)`, emit | Recording |
| Idle | `start(p)` | `openProject` throws | propagate | Idle |
| Recording, Paused | `start(p)` | same path string | return state | unchanged |
| Recording, Paused | `start(q)` | different path | throw `'A recording is already in progress for another project'` | unchanged |
| Idle | `captureSingle(p, i)` | project opens | seed, session with `single`, attach hook only, `onRecordingChange(true)`, emit | SingleArmed |
| any non-Idle | `captureSingle` or `captureScreenshot` | | throw `'A recording is already in progress'` | unchanged |
| Idle | `captureScreenshot(p, t, i)` | target valid | seed, session with `single.fired`, `onRecordingChange(true, {pill:false})`, no emit | Screenshot |
| Idle | `captureScreenshot` | window or area invalid | throw the validation message, nothing hidden | Idle |
| Screenshot | settle elapsed | | `captureStep(hotkey, null, left, {insertAt, broadcast:false, skipOwnWindowGuard:true})` | Screenshot |
| Screenshot | step done or thrown | | `session=null`, `onRecordingChange(false)`, emit idle; throw `'Could not capture the screen \u2014 ...'` if the step was null | Idle |
| Recording | `pause()` | | `paused=true`, disarm menu, emit | Paused |
| Paused | `resume()` | | `paused=false`, disarm menu, emit | Recording |
| Paused | `pause()` | | disarm menu, emit (idempotent) | Paused |
| Recording | `resume()` | | disarm menu, emit (idempotent) | Recording |
| Idle | `pause()` or `resume()` | | disarm, log, emit idle | Idle |
| Recording, Paused, SingleArmed, SingleFired | `stop()` | | detach triggers, drain queue | Draining |
| Draining (from stop) | queue drained | | `session=null`, log, `onRecordingChange(false)`, emit | Idle |
| Idle | `stop()` | | detach (no-op), drain, log `(0 steps total)`, no recording change, emit idle | Idle |
| Recording, Paused, SingleArmed | `discard()` | | detach, drain | Draining |
| Draining (from discard) | queue drained | whole-project predicate | delete project, `onRecordingChange(false)`, emit | Idle |
| Draining (from discard) | queue drained | not whole, ids present | delete session steps, `onRecordingChange(false)`, emit | Idle |
| Idle | `discard()` | | drain, emit idle, `projectDeleted=false`, no recording change | Idle |
| Recording | mouse down | see 2.3 | enqueue capture(s) | Recording |
| Paused | mouse down or hotkey | | ignored | Paused |
| Recording | hotkey | | enqueue `captureStep('hotkey', null)` | Recording |
| SingleArmed | mouse down | not on own window | `fired=true`, enqueue, then `stop()` | SingleFired |
| SingleFired | mouse down | | ignored | SingleFired |
| SingleFired | capture done | | `void stop()` | Draining |
| any | app `before-quit` | | `teardown()`: detach triggers; `globalShortcut.unregisterAll()` | unchanged (process exits) |

### 2.3 Mouse trigger (`onMouseDown`)

Source: `uiohook-napi` 1.5.5. Its Windows backend installs `WH_MOUSE_LL` and `WH_KEYBOARD_LL` on a worker thread with a `GetMessage` loop (`input_hook.c:653-654`, `688`) and dispatches each event to the JS main thread through a non-blocking threadsafe function (`src/lib/addon.c:20`). Only `mousedown` is subscribed (`:902`). Details that matter:

| Fact | Detail | Citation |
|---|---|---|
| Buttons | `WM_LBUTTONDOWN` → 1, `WM_RBUTTONDOWN` → 2, `WM_MBUTTONDOWN` → 3, `WM_XBUTTONDOWN` XBUTTON1 → 4, XBUTTON2 → 5 | `input_hook.c:499-534` |
| Mapping | `1 → 'left'`, `2 → 'right'`, `3 → 'middle'`, anything else → `'other'` | `src/main/CaptureController.ts:248-260` |
| Coordinates | `MSLLHOOKSTRUCT.pt` cast to `int16_t` (global physical px) | `input_hook.c:340-341` |
| Injected input | NOT filtered (no `LLMHF_INJECTED` check). Remote-control tools inject input, and those clicks are captured. | `input_hook.c:304-345` |
| uiohook click count | computed but unused by shotAI | `input_hook.c:304-338` |

Per event, in this exact order (`src/main/CaptureController.ts:311-461`):

| # | Condition | Action | Citation |
|---|---|---|---|
| 0 | no session, or paused | return | `:312` |
| 1 | always | `point = {x, y}` (physical px), `button = mapButton(...)`; start `elementPromise = getElementAtPoint(point.x, point.y)` NOW, before the click changes the UI | `:313-320` |
| 2 | `session.single` | if `fired` return; if `pointHitsOwnWindow(point)` return; else `fired=true`, enqueue the single capture, then `void stop()` (2.2.4) | `:325-343` |
| 3 | `button === 'left'` | `isDouble = last && now - last.at <= 400 && withinDist(point, last.point, 6)`; ALWAYS set `lastLeftClick = { at: now, point }`; if `isDouble`, log debug `double-click: ignoring 2nd click at (<x>,<y>)` and return | `:347-359` |
| 4 | `button === 'right'` | arm: `menuFollowUp = { until: now + 30000, ownerBounds: focusedWindowBounds(), lastPoint: point, menuFrame: null, chain: 0 }`; `startMenuPolling()`; log `menu: armed by right-click at (<x>,<y>)`; enqueue `captureStep('click', point, 'right', { elementPromise })` (plain capture of the right-clicked target); return | `:361-385` |
| 5 | `button === 'left' && fu && now < fu.until && nearMenuPoint(point, fu.lastPoint)` | menu selection (2.4.3); return | `:393-446` |
| 6 | otherwise | if armed, log why it was not a selection; `disarmMenu()`; enqueue `captureStep('click', point, button, { elementPromise })` | `:448-460` |

Consequences, all REQUIRED:

- Middle and X-button clicks produce ordinary steps (`button: 'middle'` or `'other'`) and disarm the menu (reason `button=middle`).
- A right-click while already armed re-arms from scratch with `chain: 0` (step 4 runs before step 5).
- Double-click collapse applies only to left clicks. Because `lastLeftClick` updates on every left mousedown, a burst of left clicks each within 400 ms and 6 logical px of the previous one collapses to ONE step (chained collapse). The check runs BEFORE the menu-selection check, so the second click of a double-click on a menu item is also dropped.
- `lastLeftClick` is an instance field and is never reset by pause, resume, stop or start. REQUIRED only in the weak sense that a new session starting within 400 ms of the last click is not a real scenario; the native engine may reset it on start (harmless).
- The system double-click time and distance (`GetDoubleClickTime`, `SM_CXDOUBLECLK`) are NOT used.
- Electron has NO own-window check in `onMouseDown` outside single mode: a click on the pill updates `lastLeftClick`, a left click on the pill disarms an armed menu (or is consumed as a menu selection when it is near the menu point), and a RIGHT-click on the pill arms the menu and starts the 400 ms poll, which then grabs the monitor up to 32 times. Only `captureStep` suppresses the step itself. Natively D1 moves the gate first (EDGE-CAP-54).
- `Date.now()` is read separately at each check (`:348`, `:369`, `:397`, `:426`, `:454`), not once per event. The difference is sub-millisecond; the native engine may read the clock once per event. Corrected in WP-B3: not at `:426`, the submenu re-arm, which follows the click-time grab (tens to hundreds of milliseconds); the native engine reads the clock again there (`TheSubmenuWindowStartsAfterTheClickTimeGrab`).
- The element query of step 1 starts for EVERY mousedown that passes step 0, including collapsed double-clicks, single-mode clicks that hit an own window, and clicks on the pill. Its result is simply dropped when no capture is enqueued (D2 changes this natively).

The disarm reason log (`:448-458`): `menu: disarmed \u2014 click at (<x>,<y>) not a selection (<reason>)` where reason is `button=<button>` if not left, else `window expired` if `now >= until`, else `too far from (<lastX>,<lastY>)`.

#### 2.3.1 Distance helpers

Both use the scale factor of the monitor containing the NEW click point (`Monitor.fromPoint(point)`), `1` when none or on error. As built in WP-B3: the native helpers keep Electron's `?? 1` (`:468`, `:503`), so a factor of 0 is kept; only 2.8.2's click box and region crop treat 0 as 1, as their `|| 1` does (`ProximityScalesWithMonitorFactor`, `AFailedScaleLookupCountsAsOne`).

| Helper | Formula | Citation |
|---|---|---|
| `withinDist(a, b, logicalPx)` | `max = logicalPx * sf(a)`; `abs(a.x - b.x) <= max && abs(a.y - b.y) <= max` (per axis, Chebyshev) | `:500-509` |
| `nearMenuPoint(point, last)` | `abs(point.x - last.x) <= 640 * sf(point) && abs(point.y - last.y) <= 680 * sf(point)` | `:465-476` |

### 2.4 Context-menu machinery

#### 2.4.1 Why

A right-click opens a context menu, a separate top-level popup (`#32768`) that per-window capture cannot see and that dismisses on mouse-up. So the right-click step is captured plainly, and the NEXT nearby left click is treated as a menu selection whose image comes from a monitor frame taken while the menu was still painted (`:117-127`, `:377-383`, `:387-392`). The report can later merge the two (spec 05, `src/renderer/project/merge.ts`).

#### 2.4.2 Arm state machine

| State | Event | Guard | Action | Next |
|---|---|---|---|---|
| Unarmed | right mousedown | session active, not paused | `until = now + 30000`, `ownerBounds = focusedWindowBounds()` (sync), `lastPoint = point`, `menuFrame = null`, `chain = 0`, start polling, enqueue plain right-click capture | Armed(0) |
| Armed(k) | right mousedown | | re-arm exactly as above (chain resets to 0) | Armed(0) |
| Armed(k) | left mousedown | `now < until` and `nearMenuPoint` and not a collapsed double-click | `preGrab = menuFrame ?? grabClickMonitorSync(point)`; `chain' = k + 1`; if `chain' < 4`: re-arm `{ until: now + 6000, ownerBounds (kept), lastPoint: point, menuFrame: null, chain: chain' }` and restart polling, else log `menu: chain limit (4) reached \u2014 disarming` and disarm; enqueue `captureStep('click', point, 'left', { menuPopup: true, menuOwnerBounds: ownerBounds, preGrab, elementPromise })` | Armed(k+1) or Unarmed |
| Armed(k) | left mousedown | expired or too far | log disarm reason, disarm, enqueue plain capture | Unarmed |
| Armed(k) | middle or other mousedown | | log `button=...`, disarm, enqueue plain capture | Unarmed |
| Armed(k) | `pause()`, `resume()`, `stop()`, `discard()`, `teardown()` | | disarm (via `disarmMenu` or `detachTriggers`) | Unarmed |
| Armed(k) | poll tick | `now >= until` or paused | stop polling (the arm itself stays until the next click) | Armed(k), not polling |

So one right-click yields at most 4 menu-selection steps: selections 1, 2 and 3 re-arm, selection 4 disarms, and the fifth nearby click is a plain click (macOS `CaptureEngineTests.swift:226-247` pins this).

The selection log (`:404-416`): `menu: selection at (<x>,<y>) \u2014 using polled frame`; or `menu: selection at (<x>,<y>) \u2014 no polled frame yet, using click-time grab`; or (warn) `menu: selection at (<x>,<y>) \u2014 NO frame available; the menu will probably be missing from this step`.

`focusedWindowBounds()`: the node-screenshots window whose `isFocused()` is true, if its `x > -10000 && y > -10000`, as `{x, y, width, height}`, else null; errors give null (`:512-523`). As built in WP-B3: `FocusedOwnerBounds`, the foreground's frame bounds else its window rect, null when minimized or at or below the sentinel, or when the read throws (`AFailedOwnerReadArmsWithoutAnOwner`). Late fill: in `captureStep`, if `button === 'right'` and a `menuFollowUp` exists with `ownerBounds === null`, fill it from the step's `focused` window (`:1143-1152`). Note the late fill writes into whatever arm is current, which may already be a newer arm. REQUIRED as is.

`grabClickMonitorSync(point)`: monitor = `fromPoint(point) ?? primary ?? first`; returns `{ image: grabMonitorSync(mon), monitor }`, or null on error with warn `synchronous menu grab failed:` (`:480-497`). It runs synchronously in the mousedown handler, "where any async delay lets the popup dismiss" (`:203`, `:478-479`).

#### 2.4.3 Poll timer (`startMenuPolling`)

Called on every arm and re-arm (`:552-598`):

1. `stopMenuPolling()` first; capture `fu = menuFollowUp`; return if none.
2. `frames = 0` (closure-local, per arm).
3. `setInterval` every 400 ms. Each tick:
   - `cur = menuFollowUp`; if `cur !== fu`, stop (disarmed or re-armed).
   - if `now >= cur.until` or `session.paused`, stop.
   - if `frames >= 32`, stop ("reuse the last frame").
   - if `menuPolling` (a capture is in flight), skip this tick WITHOUT counting a frame.
   - `mon = fromPoint(cur.lastPoint) ?? primary ?? first`; if none, skip without counting.
   - `menuPolling = true`; `frames++`; `grabMonitor(mon)` (shielded, async). On success, if `menuFollowUp === cur`, set `cur.menuFrame = { image, monitor: mon }` (a late frame never lands on a newer arm). On failure warn `menu poll capture failed:`. Finally, only if `menuFollowUp === cur`, set `menuPolling = false`.
4. `stopMenuPolling()` clears the interval AND unconditionally clears `menuPolling`, because a stale in-flight capture's finally will not clear it, and a leaked `true` would suppress the next arm's polling (`:526-535`).

The first polled frame exists only after 400 ms plus grab latency; a faster selection uses the synchronous click-time grab. The polled frame is the monitor at `lastPoint` (the right-click point, or the previous selection point), not necessarily the monitor of the selection click. Timer-driven, NOT mouse-move-driven: "an earlier mousemove-driven version missed exactly that case" (clicking an item without moving) (`:140-149`). The interval is kept "ABOVE the per-capture latency (~185ms emulated)" (`:147-149`). 32 frames at 400 ms is about 13 s, "comfortably longer than a real menu interaction (the slowest observed was ~10s)" (`:151-155`).

### 2.5 Hotkey trigger

| Aspect | Behavior | Citation |
|---|---|---|
| Accelerator | `'CommandOrControl+Shift+S'` (Windows: Ctrl+Shift+S) | `:45` |
| Registration | `globalShortcut.register(DEFAULT_HOTKEY, onHotkey)` in `attachTriggers` when `opts.hotkey ?? true` and not already registered. The boolean result is stored; a failure (chord taken by another app) is silent, and the recording continues mouse-only. A later `start` retries. | `:906-908` |
| Lifetime | Registered only while a session exists (from `start` to `stop`/`discard`/`teardown`), NOT unregistered on pause. While registered it is consumed system-wide: Ctrl+Shift+S does not reach the focused app. | `:899-922` |
| Handler | if no session or paused, return; else enqueue `captureStep('hotkey', null)` | `:600-603` |
| Rebinding | **None.** There is no setting, UI or code path to change the chord. The chord is also hard-coded in UI text: `'Click anything to capture a step · Ctrl+Shift+S'` (`src/renderer/toolbar/App.tsx:173`), `src/renderer/project/App.tsx:611`, `src/renderer/project/Tour.tsx:37`, `:161`, `README.md:121`. | as cited |

REQUIRED: the same fixed chord, the same lifetime, silent tolerance of a failed registration. Rebinding is not a parity feature (Q-CAP-2).

### 2.6 `captureStep` pipeline

In order (`src/main/CaptureController.ts:1088-1465`). `opts` fields: `menuPopup`, `menuOwnerBounds`, `preGrab`, `insertAt`, `elementPromise`, `broadcast` (default true), `skipOwnWindowGuard`.

1. If no session or paused, return null ("so a pause/stop that lands while tasks are queued actually suppresses the backlog") (`:1113-1115`).
2. `active = await activeWindow()` (get-windows, the foreground window, 2.10.1); `focused = Window.all().find(w => w.isFocused())` (node-screenshots) (`:1118-1119`).
3. Own-window guard unless `skipOwnWindowGuard` (2.7.1): return null (silently, no error) if `activeIsOwn || focusedIsOwn || (point && pointHitsOwnWindow(point))` (`:1121-1138`).
4. Right-click late owner fill (2.4.2) (`:1143-1152`).
5. `elementPromise = opts.elementPromise ?? (point ? getElementAtPoint(point) : resolve(null))` (`:1159-1160`).
6. `mode = session.target.mode`; `clickMonitor = (point ? fromPoint(point) : null) ?? primary ?? first ?? null` (`:1162-1170`).
7. `autoMode = mode === 'auto' ? captureModeFor(active) : null` (`:1174`).
8. `grabbed = await grab()` (2.8). If null, return null (silent; only a `warn` log from inside `grab`) (`:1356-1358`).
9. `{ png: outPng, scale: imageScale } = downscalePng(png)` (2.11). If `grabMs + downMs > 120`, debug log `capture timing: grab(async)=<g>ms downscale(sync)=<d>ms` (`:1364-1369`).
10. `order = ++session.stepCount` (the counter is burned even if the write fails); `filename = 'step-' + String(order).padStart(4, '0') + '.png'` (`:1371-1372`).
11. `writeFile(<project>/shots/<filename>, outPng, { flag: 'wx' })` (exclusive create; fails loudly on collision) (`:1373-1377`).
12. `window = active ? { app: active.owner.name, title: active.title, pid: active.owner.processId, bounds: active.bounds ?? null } : null` (`:1378-1385`).
13. `element = (await elementPromise) ?? { available: false, name: null, controlType: null, bounds: null }` (`:1388-1393`).
14. `appName = window?.app ?? 'screen'` (`:1394`).
15. Build the step (`:1396-1432`):

| Field | Value |
|---|---|
| `id` | `randomUUID()` (lowercase v4 UUID) |
| `order` | the filename counter when built. The store's `addStep` and `insertStepAt` push THIS object into the manifest array and `renumber` it in place (`src/main/project-store.ts:744-748`, `1025-1026`, `838-842`), so after the store call (and therefore in the `capture:step-added` payload and the returned step) `step.order` is the landed position plus 1. Only the local `order` variable (used in the filename and the log) keeps the counter value |
| `screenshot` | `shots/<filename>` |
| `trigger` | `'click'` or `'hotkey'` |
| `click` | `point ? { global: point, image: { x: round((point.x - originX) * imageScale), y: round((point.y - originY) * imageScale) }, button, ...(imageScale !== 1 ? { imageScale } : {}) } : null` |
| `monitor` | `monitor ? { id: monitor.id(), bounds: { x, y, width, height } (physical px), scaleFactor: monitor.scaleFactor() } : null` |
| `window` | step 12 |
| `element` | step 13 |
| `caption` | `trigger === 'click' ? buildClickCaption(button, !!opts.menuPopup, appName, element) : 'Capture: ' + (window?.title ?? 'screen')` |
| `crop` | `null` |
| `annotations` | `[]` |

No other keys are written (no `kind`, `heading`, `body`, `flattened`, `renderRev`, `markerColor`, `captionEditedByUser`, `aiInserted`). The codec (spec 01) decides key order on disk.

16. Placement: `insertIndex = opts.insertAt ?? session.insertCursor ?? null`; insert (and advance the cursor only when it came from the cursor) or `addStep` (`:1438-1451`).
17. `session?.addedStepIds.push(step.id)` (`:1453`). It runs only after the store call succeeded; a store exception skips it and escapes to the queue's error handler, leaving the PNG from step 11 on disk untracked (EDGE-CAP-52).
18. Log at info, exactly (`:1454-1456`): `` `step #${order} [${trigger}/${autoMode ? `auto:${autoMode}` : mode}${button === 'right' ? ' right' : opts.menuPopup ? ' menu-select' : ''}]${opts.insertAt != null ? ` (insert@${opts.insertAt})` : ''} ${window?.app ?? 'screen'}${element.name ? ` el='${element.name}'(${element.controlType})` : ''} -> ${filename} (${round(outPng.length / 1024)} KB${imageScale !== 1 ? ` @${imageScale.toFixed(2)}x` : ''})` ``. So: `order` is the filename counter; ` right` wins over ` menu-select`; ` (insert@n)` appears only for a FIXED `opts.insertAt` (screenshot and single paths), never for a rolling-cursor insert session; the `el=` part appears only when `element.name` is non-null; the scale suffix only when `imageScale !== 1`, with two decimals. This logs the element name at info level (Q-CAP-17). Corrected in WP-B2: the `el=` test is JavaScript truthiness, so an empty name shows no `el=` part either.
19. If `opts.broadcast !== false`: broadcast `capture:step-added` with the step, then emit state (`:1460-1463`).
20. Return the step.

A hotkey step (`point === null`) has no click, uses the hotkey caption, and resolves `clickMonitor` as the primary (not the cursor's monitor). The window and area paths still use the monitor under the window's or area's top-left, so `step.monitor` for a hotkey step is the primary only on the region, fullscreen and screen-fallback paths. The self-test calls `captureStep('hotkey', {x:10, y:10})`, which yields a hotkey step WITH a click (`src/main/capture-selftest.ts:75`); only the test does that.

### 2.7 Self-exclusion

#### 2.7.1 Own-window guard

| Signal | Rule | Citation |
|---|---|---|
| Own pids | `{ process.pid }` plus each BrowserWindow's renderer `getOSProcessId()` | `:1002-1012` |
| `activeIsOwn` | `active && (ownPids.has(active.owner.processId) \|\| OWN_WINDOW_TITLES.has(active.title))`, `OWN_WINDOW_TITLES = { 'shotAI', 'shotAI \u2014 Capture' }` | `:46`, `:1125-1127` |
| `focusedIsOwn` | `focused && ownPids.has(focused.pid())`. Both are checked because "our content-protected windows make get-windows return null, but node-screenshots still reports the focused window's owning pid" | `:1121-1128` |
| `pointHitsOwnWindow(point)` | convert the physical point to DIP (`screen.screenToDipPoint`, raw point on error); for each BrowserWindow that is not destroyed and is visible, half-open test `b.x <= x < b.x + b.width && b.y <= y < b.y + b.height` | `:1015-1036` |
| Why geometric | "catches clicks on the always-on-top pill, which doesn't reliably report as the active/focused window (and is content-protected)" | `:1129-1131` |
| Skip | `skipOwnWindowGuard` only for the no-click screenshot: "a just-hidden window can still momentarily report as the active/focused window (which would abort the grab)" | `:1105-1110`, `:884` |
| Single mode | also checks `pointHitsOwnWindow` at mousedown | `:327` |

A suppressed step returns null silently: no counter burn, no error event. REQUIRED.

#### 2.7.2 The grab funnel

"EVERY screen grab goes through one of these two. Nothing may call captureImage/captureImageSync directly, and capture-shield.test.ts fails the build if anything does" (`:179-193`):

```ts
async function grabMonitor(mon)      { const release = shieldOwnWindows(); try { return await mon.captureImage(); } finally { release(); } }
function grabMonitorSync(mon)        { const release = shieldOwnWindows(); try { return mon.captureImageSync(); } finally { release(); } }
```

(`:194-211`). The seven grab sites: click-time sync menu grab (`:492`), the poll (`:587`), menu fallback grab (`:1215`), window path (`:1271`), area path (`:1296`), region path (`:1328`), fullscreen path (`:1343`). The shield covers only the pixel read; crop and PNG encode happen after release.

#### 2.7.3 Remote-visibility shield

Why: `setContentProtection(true)` is `WDA_EXCLUDEFROMCAPTURE`, which excludes the window from every capture, including Teams or Splashtop; "There is no flag that says 'exclude from this capture but not that one'", so the only way to be both remotely visible and absent from shotAI's screenshots is TEMPORAL (`src/main/remote-visibility.ts:1-19`, commit `53045c6`).

| Element | Behavior | Citation |
|---|---|---|
| `setProtection(on)` | for every BrowserWindow not destroyed, `setContentProtection(on)` | `remote-visibility.ts:24-28` |
| `shieldDepth` | outstanding shields, starts 0 | `:31` |
| `shieldRestore` | what to restore at depth 0; starts `true` | `:33` |
| `applyRemoteVisibility(visible)` | `shieldRestore = !visible`; if `shieldDepth > 0` return (defer); else `setProtection(!visible)` | `:43-47` |
| `shieldOwnWindows()` | if depth 0: `shieldRestore = !remoteVisibleNow()` (synchronous cache) and `setProtection(true)`; `depth++`; return an idempotent release: first call only, `depth--`, and at 0 `setProtection(shieldRestore)` | `:70-85` |
| Callers of `applyRemoteVisibility` | at startup after the async setting load (windows are constructed protected first: "Protected-then-relaxed cannot leak; the reverse can") and after the Settings toggle | `src/main/main.ts:500-502`, `591-593`; `src/main/ipc.ts:655-663` |
| New windows | the area overlay seeds its protection from `remoteVisibleNow()` at creation (fix `58131a8`) | `src/main/RegionService.ts:90` |
| No-op mode | with `remoteVisible` off, a shield is a redundant `true` then `true` pair: "one code path, so the shielded grab helpers cannot drift" | `remote-visibility.ts:63-65` |
| Scope | `setProtection` walks `BrowserWindow.getAllWindows()`, which includes hidden windows (the hidden main window and the hidden pill are toggled too); destroyed windows are skipped | `remote-visibility.ts:24-28` |
| Sync cache default | `remoteVisibleNow()` returns `false` (fully protected) until `getRemoteVisible()` loads the setting; `setRemoteVisible` updates the cache synchronously before persisting, "the next grab must see this, not the persisted value" | `src/main/settings.ts:260-285` |

Measured (commit `53045c6`, `scripts/protection-probe.cjs`): protection ON makes a visible window contribute 0 px to a node-screenshots monitor capture (554041 magenta px to 0); toggling OFF restores the identical pixel count; the toggle takes effect at a 0 ms delay, worst of 5 runs at each of 8 delays. So a shield costs one Win32 call per window per grab and needs no settle.

#### 2.7.4 Hide and hide-settle

- Recording hides the main window unconditionally and shows the pill (`src/main/main.ts:426-446`); the "Keep shotAI visible during capture" toggle and `forceHide` were removed in `f24b3dc`. A leftover `captureNoHide` key in `settings.json` is ignored.
- `HIDE_SETTLE_MS = 350` is used only by `captureScreenshot`, between the hide and the grab (`:66`, `:878`). The comment was corrected in `53045c6`: BitBlt does NOT grab a content-protected window, so the hide is not what keeps shotAI out of the pixels. It still matters because "focus moves to the target app, and the user can see the thing they are capturing" (`:53-65`).

### 2.8 Grab paths (`grab()` inside `captureStep`)

Monitor facts (node-screenshots): `Monitor.all()`, `fromPoint(x, y)` returns null off every monitor, `x/y/width/height` in physical px, `scaleFactor()`, `isPrimary()`, `id()`, `name()`. `Window.all()` is sorted by z order; `x/y/width/height`, `isFocused()`, `isMinimized()`, `pid()`, `title()`, `appName()`, `id()` (`node_modules/node-screenshots/index.d.ts`).

Decision cascade, first match wins (`:1176-1352`):

| Path | Condition | Monitor and image | Region | Fallback | Citation |
|---|---|---|---|---|---|
| A. Menu selection | `opts.menuPopup` | `preGrab` (monitor and image) if present; else `mon = clickMonitor`, overridden by the picked monitor (`screen` with `monitorId`, else `clickMonitor`) or by `fromPoint(winRect.x, winRect.y) ?? clickMonitor` in window mode; null mon returns null; `grabMonitor(mon)` (warn `menu-popup capture failed:` and return null on error) | `screen`: none (whole monitor). Else `base = window: winRect; area: target.area ?? null; auto: opts.menuOwnerBounds ?? null`; if `point`, `box = clickBox(point, mon.scaleFactor())`, `base = base ? unionRect(base, box) : box` | on crop or encode error warn `menu-popup crop failed:` and return null (NO fallback to other paths) | `:1184-1251` |
| B. Window | `mode === 'window' \|\| autoMode === 'window'` | `win = window ? resolveWindow(target.window) : focused`; `winRect = win && win.x > -10000 && win.y > -10000 ? {x,y,w,h} : null`; `mon = fromPoint(winRect.x, winRect.y) ?? clickMonitor` | `cropRect(mon, winRect)`: tight, NO click box ("that bloated normal captures and pulled in neighboring windows") | error: warn `window capture failed, falling back to monitor:`; `winRect` null in window mode: warn `picked window not found \u2014 falling back to monitor capture`; then fall through to C/D/E | `:1260-1284` |
| C. Area | `mode === 'area' && target.area` | `mon = fromPoint(a.x, a.y) ?? clickMonitor` | area crop (formula 2.8.2, NOT `cropRect`) | error: warn `area capture failed, falling back to monitor:`; fall through | `:1287-1307` |
| (resolve) | remaining | `mon = clickMonitor`; `screen` with `monitorId`: `Monitor.all().find(id) ?? clickMonitor`; null returns null | | | `:1310-1314` |
| D. Region | `autoMode === 'region' && point` | `mon` from resolve | region crop (2.8.2) | error: warn `region capture failed, falling back to full monitor:`; fall through | `:1319-1338` |
| E. Fullscreen | always | `mon` from resolve | whole monitor | error: warn `monitor capture failed:`, return null | `:1341-1351` |

Window capture is deliberately a MONITOR BitBlt cropped to the window, not PrintWindow: "PrintWindow grabs only the app's CLIENT area, so it misses the DWM-drawn title bar AND any popup/dropdown (a separate top-level window). The monitor BitBlt is WYSIWYG" (`:1253-1259`). Consequences: an occluded picked window captures whatever covers it; a window spanning two monitors is clipped to the monitor of its top-left; the mouse cursor is not in the image.

Consequences of the cascade (REQUIRED):

- Hotkey in auto mode: `autoMode` from the foreground window; `window` crops to `focused`; `region` has no point so falls to fullscreen of the PRIMARY monitor; `fullscreen` gives the primary monitor.
- Auto `window` with `focused` unresolved (or minimized): no warn, falls to fullscreen of `clickMonitor` (not a region).
- Window mode with a minimized picked window: fullscreen of `clickMonitor` (primary for a hotkey or screenshot).
- Area mode with no `target.area` (EDGE-CAP-60): path C is skipped silently and the click monitor is captured whole; a menu selection in that state has `base = null`, so it crops to the click box alone.
- Screen mode whose `monitorId` no longer exists: silently the click monitor (no warning).
- Auto mode: the classifier reads get-windows' `active`, but path B crops to node-screenshots' `focused`; the two come from different libraries and could disagree for an instant (natively both come from one `Foreground()` call, 7.5).
- Screen mode menu selection with a `preGrab` uses the preGrab monitor, which may not be the chosen monitor (EDGE-CAP-33; native IMPROVEMENT in 7.10).

#### 2.8.1 Auto classification (`captureModeFor`)

`src/main/capture-geometry.ts:39-47`, with `active = { owner: { name }, title }` from get-windows:

| # | Rule (first match) | Result |
|---|---|---|
| 1 | `!active` | `'fullscreen'` ("unknown focus → full context, never a guessed crop") |
| 2 | `owner.name === 'Windows Explorer' && title === 'Program Manager'` | `'fullscreen'` (desktop) |
| 3 | `owner.name === 'Windows Explorer' && title.trim() === ''` | `'region'` (taskbar, system tray) |
| 4 | `SHELL_HOST_RE.test(owner.name)` | `'region'` (Start, Search, shell hosts) |
| 5 | otherwise | `'window'` |

`SHELL_HOST_RE = /experience host|searchhost|shellexperiencehost|startmenuexperiencehost|searchapp|textinputhost|cortana/i` (`src/main/capture-geometry.ts:13-14`). It is tested against the owner name, which is "sometimes the friendly name, sometimes the exe" (`:10-12`). Comparisons in rules 2 and 3 are case-sensitive and exact. `'Windows Explorer'` is the FileDescription of `explorer.exe` (2.10.1).

#### 2.8.2 Crop formulas

All inputs in global physical px; `mon = { x, y, width, height }` of the monitor whose image is being cropped; outputs are monitor-local px; the crop's global origin is `(mon.x + cropX, mon.y + cropY)`.

**`cropRect(mon, region)`** (menu and window paths; `src/main/capture-geometry.ts:54-65`, applied by `cropToRegion`, `CaptureController.ts:267-276`):

```
lx    = round(region.x - mon.x)
ly    = round(region.y - mon.y)
cropX = max(0, min(lx, mon.width - 1))
cropY = max(0, min(ly, mon.height - 1))
cropW = max(1, min(lx + round(region.width),  mon.width)  - cropX)
cropH = max(1, min(ly + round(region.height), mon.height) - cropY)
```

The right and bottom edges come from the UNCLAMPED `lx`/`ly`, so a region hanging off the left or top shrinks correctly. Never zero size.

**Area crop** (area path, inline at `CaptureController.ts:1292-1295`), deliberately different:

```
cropX = max(0, min(round(a.x - mon.x), mon.width - 1))
cropY = max(0, min(round(a.y - mon.y), mon.height - 1))
cropW = max(1, min(round(a.width),  mon.width  - cropX))
cropH = max(1, min(round(a.height), mon.height - cropY))
```

An area hanging off the monitor's left edge keeps its full width extending right from the edge (the macOS port preserved this quirk: `macOS:Packages/CaptureKit/Sources/CaptureKit/GrabMath.swift:34-48`). REQUIRED.

**Region crop** (auto shell click, `CaptureController.ts:1321-1327`):

```
sf    = mon.scaleFactor() || 1
boxW  = min(round(820 * sf), mon.width)
boxH  = min(round(640 * sf), mon.height)
cx    = point.x - mon.x
cy    = point.y - mon.y
cropX = max(0, min(cx - floor(boxW / 2), mon.width  - boxW))
cropY = max(0, min(cy - floor(boxH / 2), mon.height - boxH))
crop  = (cropX, cropY, boxW, boxH)
```

The box is shifted, not shrunk, to stay on the monitor. The old 520 x 400 box "was too tight" (`:1316-1318`).

**`clickBox(point, sf)`** (`src/main/capture-geometry.ts:28-31`): `half = round(620 * (sf || 1))`; `{ x: point.x - half, y: point.y - half, width: 2 * half, height: 2 * half }`. `sf || 1` treats `0` and `NaN` as 1. Symmetric because "menus flip up/left near screen edges".

**`unionRect(a, b)`** (`:17-23`): `x = min(a.x, b.x)`, `y = min(a.y, b.y)`, `width = max(a.x + a.width, b.x + b.width) - x`, `height = max(a.y + a.height, b.y + b.height) - y`.

### 2.9 Captions

#### 2.9.1 `controlWord(controlType)` (`src/main/click-caption.ts:6-35`)

| controlType | word |
|---|---|
| `Button`, `SplitButton` | `button` |
| `Hyperlink` | `link` |
| `CheckBox` | `checkbox` |
| `RadioButton` | `option` |
| `Tab`, `TabItem` | `tab` |
| `Edit` | `field` |
| `ComboBox` | `dropdown` |
| `ListItem`, `TreeItem`, `DataItem` | `item` |
| `Slider` | `slider` |
| `Spinner` | `spinner` |
| anything else, `null`, `undefined` (including `MenuItem`) | `''` (omit the noun) |

#### 2.9.2 `buildClickCaption(button, isMenuSelect, appName, element)` (`:42-64`)

With `name = element?.name` and `tail = word ? ' ' + word : ''`:

| # | Condition | Caption |
|---|---|---|
| 1 | `name` truthy and `controlType === 'MenuItem'` | `Select '<name>' in <appName>` |
| 2 | `name` truthy and `button === 'right'` | `Right-click '<name>'<tail> in <appName>` |
| 3 | `name` truthy | `Click '<name>'<tail> in <appName>` (left, middle, other) |
| 4 | `button === 'right'` | `Right-click in <appName>` |
| 5 | `isMenuSelect` | `Select from context menu in <appName>` |
| 6 | otherwise | `Click in <appName>` |

Rule 1 ignores `isMenuSelect` on purpose: "The proximity gate sometimes flags a click in a dialog the menu opened (e.g. an OK button in a Properties dialog) as a 'selection'" (`:50-53`). Names are inserted verbatim (no escaping, no trimming). An empty-string name is falsy and falls to rules 4 to 6.

#### 2.9.3 Hotkey caption

`'Capture: ' + (window?.title ?? 'screen')` (`CaptureController.ts:1429`). `??` means an empty title gives `'Capture: '`. `window` is the foreground window at capture time, including for the no-click screenshot. The macOS port uses `"Capture: \(windowTitle ?? "screen")"` for hotkeys and a different `buildManualCaption` for its immediate capture (`macOS:Packages/CaptureKit/Sources/CaptureKit/Captions.swift:49-65`).

`appName = window?.app ?? 'screen'`, so a click with no resolvable foreground window captions `Click in screen`.

### 2.10 Window and monitor information

#### 2.10.1 Foreground window (`get-windows` 9.3.0, `activeWindow()`)

`node_modules/get-windows/Sources/windows/main.cc`:

| Field | Derivation | Line |
|---|---|---|
| hwnd | `GetForegroundWindow()`; NULL gives `null` | `267`, `145` |
| `owner.processId` | `GetWindowThreadProcessId` | `151` |
| process | `OpenProcess(PROCESS_QUERY_LIMITED_INFORMATION)`; failure gives `null` | `153-157` |
| `owner.path` | `QueryFullProcessImageNameW(h, 0, ...)` (MAX_PATH buffer) | `96-99` |
| `owner.name` | `FileDescription` from the exe's version info: `GetFileVersionInfoSizeW`, `GetFileVersionInfoW`, `VerQueryValueW("\VarFileInfo\Translation")` taking the FIRST entry, then `"\StringFileInfo\%04x%04x\FileDescription"` (lowercase hex, language then codepage); if empty or absent, the exe FILE NAME with extension (for example `SearchHost.exe`). The fallback when the Translation query fails is the struct literal `LANGANDCODEPAGE codePage{0x040904E4}`, which on little-endian x64 and ARM64 lays out as `wLanguage = 0x04E4`, `wCodePage = 0x0409`, i.e. the key `\StringFileInfo\04e40409\FileDescription` (a byte-order bug in get-windows; it only matters for exes whose version resource has no Translation entry). Parity requires the same fallback key | `68-118`, `76` |
| UWP | if the exe file name is exactly `ApplicationFrameHost.exe` (case-sensitive), `EnumChildWindows` stops at the first child whose owning process opens and whose image path differs, and adopts that child's path and name. `owner.processId` is NOT replaced: it stays the ApplicationFrameHost pid (`:196` uses the outer `processId`), so `CapturedWindow.pid` of a UWP app is the frame host's pid (EDGE-CAP-53) | `161-169`, `122-139`, `196` |
| Widgets | `owner.name === "Widgets.exe"` gives `null` | `171-173` |
| memory | `GetProcessMemoryInfo` failure gives `null` | `175-182` |
| `bounds` | `GetWindowRect` (includes the invisible resize borders on Windows 10 and 11); if `GetWindowRect` OR `GetClientRect` fails the whole result is `null` | `184-206` |
| `id` | `(LONG)hwnd` (unused by shotAI) | `224` |
| `title` | `GetWindowTextW` | `57-66`, `225` |

REQUIRED: `CapturedWindow.app`, `.title`, `.pid`, `.bounds` and the classifier input must come from the same derivation, because captions (`Click in Notepad`), the auto classifier (`'Windows Explorer'`, `SHELL_HOST_RE`) and the SOP prompt (`App: ...`, `Window: ...`, `src/main/claude-service.ts:407-408`) depend on it.

#### 2.10.2 `listTargets()` (`CaptureController.ts:1061-1085`)

Windows: iterate `Window.all()` (z order); skip own pids, minimized windows, and windows whose trimmed title is empty; dedupe on `` `${pid}::${title}` `` (trimmed title), keeping the first (topmost); emit `{ id, pid, title (trimmed), app: appName() ?? '' }`. Monitors: `Monitor.all()` mapped to `{ id, name: name() || 'Display ' + id, width, height, isPrimary }`. Debug log `listTargets: <n> windows, <m> monitors`.

#### 2.10.3 `resolveWindow(target.window)` (`:1043-1055`)

Against a fresh `Window.all()`: first `id === target.id`, else `pid === target.pid && title() === target.title` (untrimmed live title versus the trimmed stored title), else `pid === target.pid`, else null. A null `target.window` gives null. The pid-only fallback can pick another window of the same app (REQUIRED).

### 2.11 Downscale policy (`downscalePng`, `:94-115`)

```
if width < 2 or height < 2:                         return (png, 1)
floorScale = min(1, 1100 / max(width, height))      // readability floor, never upscale
target     = max(captureScaleNow(), floorScale)
if target >= 1:                                     return (png, 1)
targetW    = max(1, round(width * target))
if targetW >= width:                                return (png, 1)
resized    = resize to width targetW, aspect preserved, quality 'good'
out        = PNG(resized); if empty:                return (png, 1)
return (out, resized.width / width)                 // the ACTUAL applied scale
any exception:                                      return (png, 1)   // fail open
```

- Aspect-preserving height: Electron `nativeImage.resize({ width })` derives the height from the aspect ratio with rounding; the macOS port writes it as `targetH = max(1, round(h * targetW / w))` (`macOS:Packages/CaptureKit/Sources/CaptureKit/ImageOutput.swift:70`). Use that formula.
- `'good'` not `'best'`: "'best' is a slow Lanczos resample that blocked the main thread ~250ms/click, tripping the mouse-hook timeout and dropping clicks" (`:106-107`).
- `imageScale` is the WIDTH ratio and is applied to both axes of `click.image`.
- `captureScale` is read per capture, so a Settings change mid-recording applies to the next capture.
- Every path (click, menu, hotkey, screenshot) is downscaled.

Examples (default 0.85): a 1920 x 1080 monitor becomes 1632 x 918 (`imageScale` 0.85). A 1200 x 700 window: `floorScale = 1100/1200 = 0.91667`, target 0.91667, `targetW = 1100`. A 300 x 200 area: `floorScale = 1`, not downscaled.

### 2.12 Element at point

#### 2.12.1 JS side (`src/main/element-locator.ts`)

| Aspect | Behavior | Line |
|---|---|---|
| Platform | returns null off `win32` | `49` |
| DLL | `<appPath>/native/element-locator/element_locator.dll` (dev) or `<resourcesPath>/element_locator.dll` (packaged); missing logs `element-locator: dll not found \u2014 element names disabled` | `31-44`, `51-54` |
| Load | koffi `lib.func('int element_at_point(int x, int y, uint8_t *out, int cap)')`; success logs info `element-locator: loaded <dllPath>`; failure logs `element-locator: failed to load \u2014 element names disabled:`. The load runs once per process (`loadPromise` is memoized, including a null result, so a failed load is never retried) | `46-68` |
| Warm-up | `warmUpElementLocator()` at `start` so "the FIRST click of a recording isn't delayed ~seconds while it loads lazily" | `72-74`, `CaptureController.ts:736-738` |
| Call | `fn.async(x, y, buf, 8192, cb)` on a libuv worker thread (MTA COM, off Electron's STA main thread) | `105-112` |
| Timeout | `QUERY_TIMEOUT_MS = 600`; the first of result or timeout wins; a late result is discarded (the native call keeps running). The timer starts only AFTER `await ensureLoaded()` resolves, so a query issued before the lazy load finishes waits for the load plus up to 600 ms (EDGE-CAP-57). A koffi callback error or a synchronous throw from `fn.async` resolves `-1` (null) | `24`, `94-112` |
| Pool | koffi `.async` runs on the libuv thread pool (default 4 threads, shared with Node `fs`); a hung UIA provider holds a pool thread until it returns. ELECTRON-ONLY; the native design uses its own threads (7.6) | `105-112` |
| Result | `n <= 0 \|\| n > 8192` gives null; `JSON.parse(utf8[0..n])` failure gives null | `113`, `114-125` |
| Mapping | `name = o.actionable && o.name ? o.name : null`; `{ available: !!name, name, controlType: o.controlType ?? null, bounds: { x: o.x, y: o.y, width: o.w, height: o.h } }` | `115-122` |

When the hit element is not actionable, `controlType` and `bounds` describe the RAW hit element and `available` is false. The Rust JSON still carries the raw hit element's `name` (possibly field or page content) across the FFI boundary; JavaScript discards it via `o.actionable`. Natively the climb must read each examined element's Name to test it, but the name of a non-chosen element is never returned, persisted or logged (7.6). When the query fails, `captureStep` uses `{ available: false, name: null, controlType: null, bounds: null }`.

#### 2.12.2 Native algorithm (`native/element-locator/src/lib.rs`)

1. `ensure_uia()`: one `IUIAutomation` per thread (`thread_local`), `CoInitializeEx(None, COINIT_MULTITHREADED)` (result ignored), `CoCreateInstance(&CUIAutomation, None, CLSCTX_INPROC_SERVER)`; failure returns `-1` (`:24-45`, `:137-140`).
2. `hit = ElementFromPoint(POINT{x, y})`; failure returns `-2` (`:141-144`). Coordinates are global physical px.
3. `walker = ControlViewWalker()`; failure returns `-3` (`:148-151`).
4. Climb (`:152-165`): `cur = hit`, `depth = 0`; loop while `cur`: if `depth >= 6` break; if `CurrentName` is non-empty AND `is_actionable(CurrentControlType)`, choose it and break; `cur = walker.GetParentElement(cur).ok()`; `depth++`. So at most 6 elements are examined (self plus 5 ancestors); the NEAREST qualifying element wins. "Non-empty" is Rust `String::is_empty`, so a whitespace-only Name (for example `" "`) qualifies and later yields a caption such as `Click ' ' button in <app>` (JavaScript `o.name` is truthy for it too). REQUIRED parity; EDGE-CAP-56. A `GetParentElement` error ends the climb (`.ok()` gives `None`).
5. `actionable = chosen.is_some()`; `el = chosen ?? hit` (`:167-168`).
6. Read `CurrentName` (errors give `""`), `CurrentControlType` (errors give 0), `CurrentClassName` (errors give `""`), `CurrentBoundingRectangle` (errors give all zeros) (`:169-180`).
7. JSON (`:182-193`): `{"name", "controlType": control_type_name(ct), "controlTypeId": ct, "className", "actionable", "x": left, "y": top, "w": right - left, "h": bottom - top}`.
8. Copy `min(len, cap)` bytes and return the count; null `out` or `cap <= 0` returns the needed length (`:195-201`).
9. Everything inside `catch_unwind`; a panic returns `-99` (`:136`, `:203`).

`control_type_name` maps UIA ids 50000 to 50040 to `Button, Calendar, CheckBox, ComboBox, Edit, Hyperlink, Image, ListItem, List, Menu, MenuBar, MenuItem, ProgressBar, RadioButton, ScrollBar, Slider, Spinner, StatusBar, Tab, TabItem, Text, ToolBar, ToolTip, Tree, TreeItem, Custom, Group, Thumb, DataGrid, DataItem, Document, SplitButton, Window, Pane, Header, HeaderItem, Table, TitleBar, Separator, SemanticZoom, AppBar` in that order; any other id gives `"Unknown"` (`:48-93`).

As built in WP-B1: the climb is Core's `ElementMapping.Choose` over `ElementFacts` (each examined element's name and control type id, read lazily), and `ElementMapping.ToStep` maps the result to the step's element. `Choose` stops without asking for the parent of the sixth element examined; the Rust loop fetches it and then leaves on its depth check (IMPROVEMENT: one cross-process call fewer when nothing qualifies; the element chosen is the same).

**Actionable allowlist** (`is_actionable`, `:98-117`), exactly 15 ids: 50000 Button, 50002 CheckBox, 50003 ComboBox, 50004 Edit ("Name = the field label"), 50005 Hyperlink, 50007 ListItem, 50011 MenuItem, 50013 RadioButton, 50015 Slider, 50016 Spinner, 50018 Tab, 50019 TabItem, 50024 TreeItem, 50029 DataItem, 50031 SplitButton. Excluded on purpose: types "whose Name is often free content (Text/Document/Pane) \u2014 excluded so we never put a field's contents (or a block of page text) in a caption" (`:95-97`). Chromium exposes a UIA tree, so web content resolves too (`:8-9`).

`Cargo.toml`: crate `element-locator` 0.1.0, `cdylib` named `element_locator`, `serde_json = "1"`, `windows = "0.58"` with features `Win32_UI_Accessibility`, `Win32_System_Com`, `Win32_Foundation`; release `opt-level = 2`, `lto = true`, `strip = true` (`native/element-locator/Cargo.toml:1-26`).

### 2.13 Events broadcast to the UI

`broadcast(channel, payload)` sends to every non-destroyed BrowserWindow (main window, pill, overlays) (`CaptureController.ts:1469-1476`).

| Channel | Payload | Emitted by | Citation |
|---|---|---|---|
| `capture:state-changed` | `CaptureState` (2.2.8) | `start` (after `onRecordingChange(true)`), `captureSingle`, `captureScreenshot` (only in `finally`, idle), `pause`, `resume`, `stop`, `discard`, every committed step with `broadcast !== false` (after the step event) | `:658-660`, `:741`, `:790`, `:894`, `:933`, `:941`, `:953`, `:988`, `:1462` |
| `capture:step-added` | the `ProjectStep` object after the store call; its `order` has been renumbered in place by the store to the landed position plus 1 (2.6 table), NOT the filename counter | every committed step with `broadcast !== false` | `:1461` |
| `capture:error` | `string`: `e.message` of an Error, else `String(e)` | any exception escaping a queued capture | `:992-999` |

Hook (not a channel): `onRecordingChange(recording, { pill })`: `(true)` on `start` and `captureSingle`; `(true, { pill: false })` on `captureScreenshot`; `(false)` on `stop` when a session existed, on `discard` when a session existed, and in the `captureScreenshot` finally (`:740`, `:789`, `:876`, `:893`, `:952`, `:986`). The handler hides the main window and shows the pill (docked top-center on first show) or the reverse (`src/main/main.ts:426-446`, spec 03).

UI consumers (for context; strings owned by specs 03 and 06): the main window shows `` `Capture error: ${message}` `` (`src/renderer/project/App.tsx:243`); the pill shows the message or `'A capture failed \u2014 see the log for details.'` for an empty message (`src/renderer/toolbar/App.tsx:34-36`), clears it when `stepCount` climbs (`:40-46`), and words the Discard confirmation from `willDeleteProjectOnDiscard`: `'Discard this capture? This is a new project, so the entire project will be deleted.'` or `'Discard this capture? Steps recorded in this session will be deleted.'` (`:67-69`).

### 2.14 Capture queue (`enqueue`, `:992-999`)

`queue = queue.then(fn).catch(e => { log error 'capture failed:'; broadcast(capture:error, message) })`. FIFO; one job at a time; an exception is surfaced and the chain continues ("so a long recording can't fail silently"). A null step (soft failure) is NOT an error and broadcasts nothing. Loud failures are: the exclusive write colliding (`EEXIST`), a store write failing (including `resolveKnownProject` rejecting a project that was deleted or unregistered mid-recording, `src/main/project-store.ts:742`), any unexpected exception (for example a `TypeError` if the session were null at `++this.session.stepCount`). The error is logged with the full error object (`captureLog.error('capture failed:', e)`) but broadcast as the message only. Captures run by `captureScreenshot` bypass the queue, so their errors reach the IPC caller instead (EDGE-CAP-58). The single-shot task wraps its `captureStep` in the queue too, so its errors are broadcast, and its `void this.stop()` still runs only when `captureStep` returned (a throw skips it and leaves the single session armed with `fired = true`; unreachable from the UI).

### 2.15 User-visible strings

| String | Where | Citation |
|---|---|---|
| `A recording is already in progress for another project` | thrown by `start` | `:684` |
| `A recording is already in progress` | thrown by `captureSingle`, `captureScreenshot` | `:753`, `:812` |
| `That window is no longer open \u2014 reopen it and try the screenshot again.` | `captureScreenshot` | `:824` |
| `That screen area is off-screen now \u2014 drag the area again and retry.` | `captureScreenshot` | `:838` |
| `Could not capture the screen \u2014 make sure the target is visible, then try again.` | `captureScreenshot` | `:889` |
| `A screenshot needs an explicit target (screen, window, or area).` | IPC guard (engine guard natively) | `src/main/ipc.ts:929` |
| caption templates | 2.9 | `src/main/click-caption.ts:54-63`, `CaptureController.ts:1429` |
| `Display <id>` | monitor name fallback in the Screen chooser | `:1078` |
| the raw exception message | `capture:error` payload | `:994-997` |

### 2.16 Self-test and probe

`SHOTAI_CAPTURE_TEST=1` runs `runCaptureTest()` and quits (`src/main/main.ts:409-414`):

| Check | What it does | Citation |
|---|---|---|
| natives | `activeWindow()`, `Monitor.all()[0].captureImageSync().toPngSync()`, load uiohook; log `[capture-test] ... OK` or `FAILED` | `capture-selftest.ts:22-62` |
| pipeline | temp projects dir, create `Capture Pipeline Test`, `start(attachHook:false)`, `captureStep('hotkey', {x:10, y:10})`, `stop`; pass iff one step in the manifest with the same id and a non-empty PNG; restore the projects dir and recents | `:64-108` |
| modes | `listTargets` has at least 1 monitor; click at primary `(x+100, y+100)`: `screen` PNG equals monitor size; `area` 300 x 200 at `(x+100, y+100)` is exactly 300 x 200; `window` (first listed window) at least 1 x 1; `menu(window)` width within 1 and monitor width; `menu(auto)` with owner `(x+200, y+200, 900, 600)` strictly smaller than the monitor; `menu(screen)` equals monitor size | `:116-232` |
| verdict | `[capture-test] PASS` or `[capture-test] FAIL` | `:234-246` |

The `screen` and `menu(screen)` size assertions predate the downscale: at the default 0.85 a 1920 x 1080 monitor stores 1632 x 918, so those checks fail today (EDGE-CAP-34). The user's setting cannot rescue them: the capture test runs at `src/main/main.ts:409-414`, BEFORE `void getCaptureScale()` primes the cache at `:415`, so `captureScaleNow()` is always the default 0.85 there, and any primary monitor whose long edge exceeds 1100 px fails.

Further self-test facts: the pipeline and mode checks call `captureStep` directly with `attachHook: false` and without `skipOwnWindowGuard`, so an own window in the foreground would suppress the step (none is open in test mode); the `menu(...)` runs pass no `preGrab`, so they exercise the post-click fallback grab of path A; each mode uses a fresh project `Mode <label>` in a temp projects dir `shotai-modes-<pid>` (pipeline: `shotai-capture-<pid>`), and the `finally` restores the projects dir and recents and deletes the temp root. Each check logs `[capture-test] mode <label padded to 13> = <w>x<h>` or `= (no step)`, and `mode window        = (no pickable windows \u2014 skipped)` when no window is listed. The selftest and the probe call `captureImageSync` directly (unshielded); `capture-shield.test.ts` scans only `src/main/CaptureController.ts`, so diagnostics are outside the funnel invariant. Natively the source scan covers all of `dotnet/src` and the diagnostics must either use `IScreenCapture` or be explicitly allowlisted (8.4).

Probe outputs not listed above: a baseline of 0 prints `[probe] INCONCLUSIVE: window not captured even unprotected.` and quits; the restore loop reports the worst restored percentage per delay (`FULL` when above 95); the verdict prints `Smallest reliably-clean exclude delay: +<d>ms (worst of 5 runs).` or, if no delay was clean, `No tested delay reliably excluded the window. Per-shot toggling is NOT safe;`.

`scripts/protection-probe.cjs`: a 700 x 500 frameless always-on-top magenta window at (120, 120), level `screen-saver`, settled 900 ms; counts magenta pixels (tolerance 12, both BGRA and RGBA orders) in `Monitor.all()[0].captureImageSync()`; for delays `[0, 4, 8, 16, 32, 64, 120, 250]` ms, 5 reps each with a 160 ms reset, measures the worst leak after `setContentProtection(true)` and the worst restore after `(false)`; prints the smallest reliably clean delay (`scripts/protection-probe.cjs:22-122`). Result recorded in `53045c6`: clean at 0 ms.

---

## 3. Constants

| Name | Value | Unit | Meaning | Citation |
|---|---|---|---|---|
| `DEFAULT_HOTKEY` | `CommandOrControl+Shift+S` (Ctrl+Shift+S) | chord | manual capture hotkey | `CaptureController.ts:45` |
| `OWN_WINDOW_TITLES` | `shotAI`, `shotAI \u2014 Capture` | titles | foreground titles treated as own | `:46` |
| `DEFAULT_TARGET` | `{ mode: 'auto' }` | | target when none given | `:47` |
| `HIDE_SETTLE_MS` | 350 | ms | wait after hiding before a no-click grab | `:66` |
| `MIN_CAPTURE_LONG_EDGE` | 1100 | image px | readability floor for the longer edge | `:84` |
| `MENU_FOLLOWUP_WINDOW_MS` | 30000 | ms | arm window after a right-click | `:128` |
| `SUBMENU_FOLLOWUP_WINDOW_MS` | 6000 | ms | re-arm window after each selection | `:132` |
| `MENU_PROXIMITY_X` | 640 | logical px (times monitor scale) | selection proximity, x axis | `:138` |
| `MENU_PROXIMITY_Y` | 680 | logical px (times monitor scale) | selection proximity, y axis | `:139` |
| `MENU_POLL_MS` | 400 | ms | poll interval while armed | `:150` |
| `MAX_POLL_FRAMES` | 32 | frames | poll frames per arm | `:156` |
| `MAX_MENU_CHAIN` | 4 | selections | max selections per right-click | `:162` |
| `DOUBLE_CLICK_MS` | 400 | ms | double-click collapse window | `:165` |
| `DOUBLE_CLICK_DIST` | 6 | logical px (times monitor scale), per axis | double-click collapse distance | `:166` |
| Off-screen sentinel | -10000 | px | a window with `x` or `y` at or below this is treated as unresolvable (minimized windows sit at -32000) | `:516`, `:1188`, `:1264` |
| Region box | 820 x 640 | logical px (times monitor scale) | auto shell-region crop | `:1322-1323` |
| Click box half-size | 620 | logical px (times monitor scale) | half side of the box unioned around a menu selection | `capture-geometry.ts:29` |
| Timing log threshold | 120 | ms | debug-log captures whose grab plus downscale exceed this | `CaptureController.ts:1367` |
| Filename pad | 4 | digits | `step-` + zero-padded counter (more digits past 9999) + `.png` | `:1372` |
| Orphan regex | `/^step-(\d+)\.png$/i` | | shot files that seed the counter | `:707`, `:764`, `:846` |
| `SHELL_HOST_RE` | `/experience host\|searchhost\|shellexperiencehost\|startmenuexperiencehost\|searchapp\|textinputhost\|cortana/i` | | shell hosts classified `region` | `capture-geometry.ts:13-14` |
| Desktop identity | owner `Windows Explorer`, title `Program Manager` | | classified `fullscreen` | `capture-geometry.ts:43` |
| `QUERY_TIMEOUT_MS` | 600 | ms | element query cap | `element-locator.ts:24` |
| `BUF_SIZE` | 8192 | bytes | element JSON buffer | `element-locator.ts:25` |
| Climb depth | 6 | elements | self plus 5 ancestors examined | `lib.rs:156` |
| Actionable ids | 50000, 50002, 50003, 50004, 50005, 50007, 50011, 50013, 50015, 50016, 50018, 50019, 50024, 50029, 50031 | UIA control type ids | label-bearing types | `lib.rs:98-117` |
| Rust return codes | -1 UIA unavailable, -2 ElementFromPoint failed, -3 walker failed, -99 panic | | | `lib.rs:139`, `143`, `150`, `203` |
| `CAPTURE_SCALE_MIN` / `MAX` / `DEFAULT` | 0.5 / 1 / 0.85 | factor | screenshot-quality range | `src/shared/project.ts:18-20` |
| `shieldRestore` initial | `true` | | protection on before the setting loads | `remote-visibility.ts:33` |
| Probe delays | 0, 4, 8, 16, 32, 64, 120, 250 | ms | protection toggle measurement | `protection-probe.cjs:75` |
| Probe reps / tolerance / reset / settle | 5 / 12 / 160 ms / 900 ms | | | `protection-probe.cjs:76`, `23`, `84`, `61` |
| Native `HOTKEY_ID` (new) | `0x5348` | | id passed to `RegisterHotKey` | 7.4 |
| Native watchdog interval (new) | 2000 | ms | hook liveness check | 7.4 |
| Native UIA timeouts (new) | 500 | ms | `IUIAutomation2` connection and transaction timeouts | 7.6 |
| Native orphan clamp (new) | 1000000 | | max parsed orphan number (macOS `CaptureConstants.swift:87`) | 7.10 |
| get-windows translation fallback | `0x040904E4` as `{ wLanguage, wCodePage }` (little-endian: language `0x04E4`, codepage `0x0409`) | | FileDescription key when the version resource has no Translation | `main.cc:76` |
| Native UIA request deadline (new) | 600 | ms after enqueue | stale requests are dropped unexecuted | 7.6 |
| Native hook join and attach timeouts (new) | 2000 | ms | `Attach` install wait, `Detach` join | 7.4 |
| Native dispatcher ring (new) | 256 | events | hook to dispatcher buffer | 7.3 |

---

## 4. Invariants

**INV-CAP-1 [SECURITY]. Every screen read goes through one shielded funnel.** No code outside the funnel may read screen pixels. Why: with remote visibility on, shotAI's windows are capturable and the pill is on screen for the whole recording, so one unshielded read puts it in a screenshot (`53045c6`). Citation: `CaptureController.ts:179-211`. Test: `CaptureFunnelSourceTests.OnlyFunnelReadsScreenPixels` (source scan, 8.4) plus `ShieldedCaptureTests.EveryGrabTakesAndReleasesTheShield` (fake raw capture).

**INV-CAP-2 [SECURITY]. The shield is reference counted and its release is idempotent.** Protection is applied once at depth 0 and restored only when the count returns to 0; a second release of the same token is a no-op. Why: the menu poller grabs on a timer outside the queue, so grabs overlap; without the count the first release un-protected while the second grab was capturing (`58131a8`). Citation: `remote-visibility.ts:70-85`. Test: `CaptureShieldTests` re-entrancy group (ported from `capture-shield.test.ts:125-172`).

**INV-CAP-3 [SECURITY]. The restore value is the setting, latched at the first shield and updated by `ApplyRemoteVisibility`; a change while a shield is held is deferred to the final release.** Why: restoring a constant would make the feature work once, or leave a protection-on user exposed; toggling during a grab would un-protect mid-capture (`53045c6`, `58131a8`). Citation: `remote-visibility.ts:43-47`, `70-85`. Test: `CaptureShieldTests.RestoresToVisibleWhenSettingOn`, `.TogglingDuringGrabDefersToRelease`, `.MidGrabSwitchToNotVisibleRestoresProtection`.

**INV-CAP-4 [SECURITY]. The shield spans the pixel read in try/finally; a throwing read still restores.** Why: otherwise the app stays permanently invisible remotely, or permanently exposed. Citation: `CaptureController.ts:194-211`; `capture-shield.test.ts:38-47`. Test: `ShieldedCaptureTests.ThrowingGrabStillReleases`.

**INV-CAP-5 [SECURITY]. The remote-visibility value is read synchronously inside the shield.** Why: "An async read here would open a gap in which the pill is capturable, which is the exact leak this prevents" (`remote-visibility.ts:67-68`; `settings.ts:260-266`). Test: design review plus `CaptureShieldTests` using a synchronous settings fake; the native seam is the synchronous `bool ICaptureSettings.RemoteVisibleNow()`, never a `Task<bool>`.

**INV-CAP-6 [SECURITY]. A click on, or with focus in, shotAI's own UI never produces a step.** Guard: foreground window owned by our process, or the click point inside any visible own top-level window (half-open, physical px). Skipped only for the no-click screenshot. Why: the pill and report must never appear as SOP steps (and the report can show project content). Citation: `CaptureController.ts:1121-1138`. Test: `CaptureEngineTests.PillClicksCreateNoSteps`, `.ForegroundOwnWindowSuppressesStep`, `.ScreenshotSkipsOwnWindowGuard`.

**INV-CAP-7 [SECURITY]. Every own top-level window is registered with the shield and the hit test before it is first shown, and is created excluded from capture; it is relaxed only when the setting allows and no shield is held.** Why: fail-closed startup (`53045c6`) and the overlay regression (`58131a8`). Citation: `src/main/main.ts:280`, `334`, `496-502`; `RegionService.ts:78-90`. Test: `OwnWindowRegistryTests.RegisterWhileShieldHeldStartsExcluded`, `.RegisterSeedsFromSetting`; App test `AllWindowsRegisteredTests` (Windows).

**INV-CAP-8. A capture never overwrites an existing file.** The counter is seeded past every `step-N.png` and the manifest length, and the PNG is created exclusively (`FileMode.CreateNew`); a collision fails loudly as a capture error. Citation: `CaptureController.ts:702-712`, `1373-1377`. Test: `CaptureEngineTests.FilenameCounterSeedsPastOrphans`, `.CollisionFailsLoudlyAndLeavesOriginalBytes`.

**INV-CAP-9. The counter increments before the write; numbers may skip, never repeat.** Citation: `:1371`. Test: `CaptureEngineTests.FailedWriteBurnsTheNumber`.

**INV-CAP-10. Captures are strictly FIFO, one at a time; the insert cursor is read and advanced inside the serialized step.** Citation: `:7`, `:238-243`, `:992-999`, `:1438-1448`. Test: `CaptureEngineTests.RapidClicksLandInOrder`, `.InsertSessionLandsStepsAtCursorInOrder`.

**INV-CAP-11. Pause and stop suppress the queued backlog: the step re-checks the session at its start.** Citation: `:1113-1115`. Test: `CaptureEngineTests.PauseSuppressesQueuedBacklog`.

**INV-CAP-12. Stop and discard detach triggers first, then drain the queue, then clear the session; discard removes exactly this session's steps, or the whole project iff `createdThisSession && stepCountAtStart == 0 && !single`, and `WillDeleteProjectOnDiscard` uses the same predicate.** Citation: `:648-656`, `:945-990`. Test: `CaptureEngineTests.DiscardDeletesExactlyThisSessionsSteps`, `.DiscardDeletesWholeNewProject`, `.StateFlagMatchesDiscardPredicate`, `.StopDrainsInFlightCaptures`.

**INV-CAP-13. `click.image = (round((global.x - originX) * imageScale), round((global.y - originY) * imageScale))`, where origin is the global px of the stored image's top-left before downscale and `imageScale` is the actual stored width divided by the pre-downscale width; `imageScale` is written only when it is not exactly 1.** The report's merge recovers the origin as `global - image / imageScale` (`src/renderer/project/merge.ts:64-68`). Citation: `CaptureController.ts:1401-1411`. Test: `CaptureEngineTests.ClickImageCoordinatesFollowSchema`, `.ImageScaleOmittedWhenOne`.

**INV-CAP-14. The downscale never goes below the readability floor, never upscales, and fails open to the original image with scale 1.** Citation: `:94-115`. Test: `DownscalePolicyTests` (all cases in 8.4).

**INV-CAP-15. The element query starts at mousedown, is capped at 600 ms, discards late results, and can never fail or delay a capture beyond the cap.** Citation: `CaptureController.ts:316-320`; `element-locator.ts:94-125`. Test: `CaptureEngineTests.ElementTimeoutDegradesToUnavailable`, `UiaElementLocatorTests.HungProviderTimesOut` (Windows).

**INV-CAP-16 [SECURITY]. A caption names an element only when it is on the 15-type actionable allowlist with a non-empty name.** Why: never put a field's contents or page text in a caption, which then reaches Claude. Citation: `lib.rs:95-117`; `element-locator.ts:116`. Test: `ElementMappingTests.OnlyAllowlistedTypesYieldNames`, `.NonActionableHitHasNoName`.

**INV-CAP-17. The low-level hook callback does no work: it copies the event into a preallocated buffer, signals, and returns `CallNextHookEx`.** Why: a hook that misses `LowLevelHooksTimeout` (1000 ms max on Windows 10 1709+) is silently removed (Microsoft Learn, LowLevelMouseProc); the Electron history shows slow main-thread work dropping clicks (`CaptureController.ts:106-107`, `263-266`, `1353-1355`). UNVERIFIED mechanism: uiohook's own hook proc only copies the event (`malloc`) and queues it with `napi_tsfn_nonblocking` (`node_modules/uiohook-napi/src/lib/addon.c:13-26`), so it does not wait for the JavaScript main thread; the comments' claim that main-thread stalls tripped the hook timeout is not explained by the hook code read here. The native requirement stands regardless, because a native hook proc that did real work would be exposed directly. Test: `MouseHookTests.CallbackIsAllocationFree` (GC allocation counter around synthetic `SendInput`), `.WatchdogReinstallsRemovedHook` (Windows).

**INV-CAP-18. Every `Math.round` in this spec is ported as `JsMath.Round`, never `Math.Round`.** `JsMath` has exactly one copy, in `ShotAI.Core.Json`, owned by spec 01 (01 7.2.3, R-ARCH-1); capture code calls it and never defines its own. Its definition, the exact port of ECMAScript `Math.round` for finite doubles: `double f = Math.Floor(x); return (x - f >= 0.5) ? f + 1 : f;` (the naive `Math.Floor(x + 0.5)` returns 1 for `0.49999999999999994` because the addition rounds up; JavaScript returns 0). NaN and infinities are returned unchanged. Why: .NET's default is banker's rounding, which moves crops and click markers by one pixel at halves. Citation: formulas in 2.8.2, 2.11, 2.6. Enforcement: `Math.Round` (every overload) is a banned symbol in all of ShotAI.Core with only `JsMath.cs` allowlisted (ARCHITECTURE 14.9, Q-ARCH-5 default); the capture source scan `JsMathTests.NoMathRoundInCapture` stays as the capture-specific check. Test: `JsMathTests.RoundMatchesJavaScript` (cases 0.5, 1.5, 2.5, -0.5, -1.5, -2.5, 0.49999999999999994, 4503599627370497), which exercises 01's type with the capture cases and complements 01's `Json/JsHelpersTests`.

**INV-CAP-19. A polled menu frame never lands on a newer arm, the in-flight flag belongs to one arm, and polling stops on disarm, re-arm, pause, expiry or 32 frames.** Citation: `:526-598`. Test: `MenuPollTests.LateFrameDoesNotLandOnNewArm`, `.StoppedPollDoesNotSuppressNextArm`, `.PollStopsAfter32Frames`, `.PollStopsOnPause`.

**INV-CAP-20. One right-click yields at most 4 menu-selection steps.** Citation: `:157-162`, `:423-436`. Test: `CaptureEngineTests.MenuChainIsBoundedAtFour`.

**INV-CAP-21. A capture exception is surfaced as a capture error and the queue keeps running.** Citation: `:992-999`. Test: `CaptureEngineTests.ErrorIsSurfacedAndQueueContinues`.

**INV-CAP-22. At most one session exists; starting the same project is idempotent; the check is atomic across concurrent calls.** Citation: `:680-687`, `:752`, `:811`. Test: `CaptureEngineTests.SessionGuards`, `.ConcurrentStartsCannotBothInstall` (IMPROVEMENT over Electron, EDGE-CAP-45).

**INV-CAP-23 [SECURITY]. The `shots/` directory and every PNG path are confined to the project folder and refused through reparse points (symlinks, junctions).** IMPROVEMENT (macOS `CaptureEngine.swift:625-635`, `760-768`); Electron confines only the project open. Test: `CaptureEngineTests.ShotsReparsePointRefusesStart`, `.ShotsSwappedMidSessionRefusesWrite`.

**INV-CAP-24. ShotAI.Core capture code references no Windows API.** Every OS dependency is behind an interface declared in Core and implemented in ShotAI.Platform (INV-ARCH-1). Why: the engine tests run on Linux CI (feasibility doc, "What gets harder" 8). Test: the Core project builds as `net10.0` without Windows targeting; `Architecture.CoreReferencesTests` (spec 11, AC-IPC-16; reflection over referenced assemblies), which replaces the `ArchitectureTests.CoreHasNoWindowsReferences` name this spec used earlier.

**INV-CAP-25 [SECURITY]. Screen pixels are read only with GDI BitBlt (or DXGI Desktop Duplication), never `Windows.Graphics.Capture`.** Why: an unpackaged app gets a yellow capture border (fixed decision). Test: `CaptureFunnelSourceTests.NoGraphicsCaptureUsage` (source scan for `Windows.Graphics.Capture`).

**INV-CAP-26. All capture geometry is in global physical px; the process is Per-Monitor-V2 DPI aware; no Win32 call on the capture path is DPI virtualized.** Citation: `dotnet/src/ShotAI.App/app.manifest`; `CaptureController.ts:14-15`, `1018`. Test: `DpiAwarenessTests.ProcessIsPerMonitorV2` (Windows, `GetAwarenessFromDpiAwarenessContext(GetThreadDpiAwarenessContext())` on the hook, dispatcher and capture threads).

**INV-CAP-27. The step is on disk (PNG written, manifest persisted) before the step-landed event fires.** Why: consumers read the PNG from the path in the event. This is not an optimistic UI edit; the capture path is persist-then-notify. Citation: `:1373-1463`. Test: `CaptureEngineTests.StepEventFiresAfterPersist`.

**INV-CAP-28. Injected mouse input is captured like hardware input.** Why: remote-control sessions deliver clicks as injected input, and uiohook does not filter them (`input_hook.c:304-345`). Test: `MouseHookTests.InjectedClicksAreDelivered` (Windows, `SendInput`).

**INV-CAP-29 [SECURITY]. No UI-thread code path waits on the capture shield's lock, and every own window is excluded from capture before the shield learns of it.** Why: a cross-thread `SetWindowDisplayAffinity` serviced by the UI thread would otherwise deadlock (Q-CAP-22), and a window must never be capturable for a frame (fail-closed, `src/main/main.ts:496-502`). Citation: 7.8 deadlock rule; ARCHITECTURE DL1 and DL2. Test: `ShieldDeadlockTests.RegisterAndToggleWhileShieldHeldCompletes`, `AllWindowsRegisteredTests`.

**INV-CAP-30. `StepLanded.Step.Order` is the landed position plus 1, and `StepLanded.Index` is the landed index.** Why: Electron's payload already carries the renumbered order (the store renumbers the broadcast object in place); consumers place the step from the index (D5, EDGE-CAP-26). Citation: `src/main/project-store.ts:744-748`, `1025-1026`; `CaptureController.ts:1445-1461`. Test: `CaptureEngineTests.StepLandedCarriesRenumberedOrderAndIndex`.

**INV-CAP-31. The no-click screenshot never raises `CaptureFailed` or `StepLanded`; its failures are thrown to the caller and its only state event is the final idle.** Citation: `CaptureController.ts:871-895`. Test: `CaptureEngineTests.ScreenshotErrorsAreThrownNotRaised`, `.ScreenshotInsertsAtIndexWithoutStepEvent`.

---

## 5. Edge cases and hard-won fixes

Electron history before `9da70df` is squashed; where no commit exists, the source comment is the provenance.

**EDGE-CAP-1. The menu dismisses on mouse-up.** Grab the selection frame at mousedown: prefer the polled frame, else a synchronous grab in the mousedown handler; never an async hop first. Source comment `CaptureController.ts:387-392`, `:478-479`.

**EDGE-CAP-2. Clicking a menu item without moving the mouse.** The poll is timer-driven; "an earlier mousemove-driven version missed exactly that case". `:140-149`.

**EDGE-CAP-3. A menu dismissed with Esc never sends a disarming click.** Cap the poll at 32 frames (about 13 s) and then reuse the last frame. `:151-156`.

**EDGE-CAP-4. Runaway re-arm: "one right-click spawned ~10 false steps".** Chain limit 4. `:157-162`, `:421-422`.

**EDGE-CAP-5. Grabbing the menu on the right-click raced its render.** Capture the right-click plainly; the selection click carries the menu. `:377-383`.

**EDGE-CAP-6. PrintWindow misses the title bar and popups.** Window mode crops a monitor BitBlt. `:1253-1259`.

**EDGE-CAP-7. A click box on ordinary window captures "bloated normal captures and pulled in neighboring windows".** Tight window crop; only the menu path unions the click box. `:1272-1274`.

**EDGE-CAP-8. Auto-mode menu selections used to capture the entire screen.** Crop to owner bounds unioned with the click box. `capture-selftest.ts:195-196`.

**EDGE-CAP-9. The 520 x 400 region box was too tight for Start tiles and flyouts.** 820 x 640 logical. `:1316-1318`.

**EDGE-CAP-10. Shell host windows are huge or transparent and capture as a black swath.** Classify as `region`. `capture-geometry.ts:10-14`.

**EDGE-CAP-11. The proximity gate flags an OK button in a dialog opened from the menu as a selection.** Caption by element type: only `MenuItem` reads `Select '<name>'`. `click-caption.ts:50-53`.

**EDGE-CAP-12. A slow 'best' resize (~250 ms per click) and synchronous encodes dropped rapid clicks.** Natively: resize and encode on the capture worker; the hook thread never waits. `:106-107`, `:263-266`, `:1353-1355`.

**EDGE-CAP-13. The first click of a recording stalled seconds on the lazy DLL load.** Warm the element locator at session start. `:736-738`.

**EDGE-CAP-14. A just-hidden main window still reports as the foreground window.** The no-click screenshot skips the own-window guard. `:1105-1110`.

**EDGE-CAP-15. The always-on-top pill does not reliably report as the foreground window.** Geometric own-window hit test. `:1129-1131`.

**EDGE-CAP-16. A picked window closed or an area now off-screen.** Validate before hiding, with the two exact messages, so `grab()`'s silent monitor fallback is not used for a deliberate screenshot. `:819-840`.

**EDGE-CAP-17. Single-shot: a fast second click before stop; awaiting stop inside the queue deadlocks.** `fired` guard; fire-and-forget stop. `:236`, `:326`, `:337-340`.

**EDGE-CAP-18. Triple clicks and click bursts.** Chained collapse to one step (2.3). `:347-359`; macOS `CaptureEngineTests.swift:175-189`.

**EDGE-CAP-19. A stale in-flight poll leaked `menuPolling = true` and silenced the next arm's polling.** Clear unconditionally on stop (Electron) or make the flag per arm (macOS, recommended natively). `:531-534`; macOS `CaptureEngine.swift:72-76`.

**EDGE-CAP-20. Overlapping grabs un-protected the pill mid-capture.** Reference-counted shield. Commit `58131a8`.

**EDGE-CAP-21. Toggling remote visibility during a grab.** Deferred to the release. Commit `58131a8`.

**EDGE-CAP-22. The area overlay hard-coded protection on, so Area selection was invisible over a remote session.** Windows created after startup are seeded from the current setting (natively, from the registry, and excluded if a shield is held). Commit `58131a8`, `RegionService.ts:78-90`.

**EDGE-CAP-23. The HIDE_SETTLE rationale was wrong.** Protection already excludes the window from BitBlt; the hide stays for focus and visibility. Commit `53045c6`.

**EDGE-CAP-24. The "Keep shotAI visible during capture" toggle.** Removed with `forceHide`; the hide is unconditional; a leftover `captureNoHide` settings key is ignored. Commit `f24b3dc`. Natively: do not implement the toggle; do not read the key.

**EDGE-CAP-25. Deleted steps leave orphan PNGs.** Seed the counter past them; the store renumbers `order` so the report does not show "1, 2, 3, 6" (`project-store.ts:744-748`).

**EDGE-CAP-26. Insert sessions: the project view appends every `capture:step-added` payload to the END of its list (`setSteps((prev) => [...prev, step])`, `src/renderer/project/App.tsx:239-240`), although the step was spliced at the insert cursor.** The payload's `order` is already the renumbered landed position (the store mutates the same object, 2.6 table), but the view ignores it, and the other steps' renumbered `order` values are not sent at all. Electron consumers reload on stop; the no-click screenshot suppresses the event for this reason (`:1101-1103`). Natively the event also carries the landed index so consumers can place it (7.10 D5).

**EDGE-CAP-27. A hostile orphan file name such as `step-9223372036854775807.png` or `step-99999999999999999999.png`.** JavaScript parses both as floats (`Number(m[1])`): the second becomes `1e20`, `++stepCount` stays `1e20` (above 2^53 adding 1 is lost), so the first capture writes `step-100000000000000000000.png` and every later capture in that project collides with it and fails with `EEXIST` (a surfaced `capture:error` per click). C# `int.Parse` would throw instead. Natively: accept only ASCII digits (JavaScript `\d` is ASCII; do not use `char.IsDigit`, which accepts other Unicode digits), parse with `long.TryParse(..., NumberStyles.None, CultureInfo.InvariantCulture)`, IGNORE the file when parsing fails (a 20-digit number overflows `long`), and clamp a parsed value to 1000000. This is exactly macOS: `step-9223372036854775807.png` clamps to 1000000 and `step-99999999999999999999.png` is ignored (`CaptureConstants.swift:83-99`, `ReviewFixTests.swift:57-78`). IMPROVEMENT.

**EDGE-CAP-28. A session cleared or replaced while a capture is suspended.** Electron is single-threaded and mostly safe; the native multi-threaded engine must check a session generation token after every await and before committing, or it writes `step-0000.png` or re-inserts into a discarded manifest (macOS #13/#4, `ReviewFixTests.swift:13-36`).

**EDGE-CAP-29. Minimized windows report `x, y = -32000`.** Treat `x <= -10000 || y <= -10000` as unresolvable (and check `IsIconic` natively). `:516`, `:1188`, `:1264`.

**EDGE-CAP-30. A maximized window's rect starts off-monitor (negative invisible border), and a window can span monitors.** `fromPoint(origin) ?? clickMonitor`; the crop clamps to one monitor. `:1268`.

**EDGE-CAP-31. Area crop keeps full width when the area hangs off the left edge.** Preserve the separate formula (2.8.2). `:1292-1295`; macOS `GrabMath.swift:34-48`.

**EDGE-CAP-32. Hotkey steps have no point.** The monitor is the primary, a `region` classification becomes fullscreen, and there is no click marker. `:1166-1170`, `:1319`.

**EDGE-CAP-33. Screen-mode menu selection with a polled frame uses the frame's monitor, which may not be the chosen monitor.** macOS fixed it: reuse the frame only when it is the chosen monitor, else grab the chosen monitor fresh (`CaptureEngine.swift:898-911`). IMPROVEMENT natively (7.10).

**EDGE-CAP-34. The Electron self-test's full-monitor size assertions ignore the downscale.** The native self-test asserts `JsMath.Round(dim * effectiveScale)` (macOS `CaptureEngineTests.swift:7-16`).

**EDGE-CAP-35. Remote-control clicks are injected input.** Do not filter `LLMHF_INJECTED` (INV-CAP-28).

**EDGE-CAP-36. Middle and X buttons.** Steps with `button: 'middle'` or `'other'`, caption `Click ...`, and they disarm the menu. 2.3.

**EDGE-CAP-37. The title check `OWN_WINDOW_TITLES` false-positives on a File Explorer window whose folder is named `shotAI`** (its title is exactly `shotAI`), silently dropping those clicks. Natively the pid check is exact in one process; drop the title check (IMPROVEMENT, 7.10).

**EDGE-CAP-38. An empty foreground title gives the hotkey caption `Capture: ` and an empty app name gives `Click in `.** `??` semantics, REQUIRED. `:1394`, `:1429`.

**EDGE-CAP-39. The Discard confirmation is a native `window.confirm` that is not a BrowserWindow**, so it is neither shielded nor hit-tested, and a click elsewhere while it is open captures it. Natively the dialog is an own WPF window registered with the shield and the hit test (spec 03). IMPROVEMENT.

**EDGE-CAP-40. Ctrl+Shift+S is swallowed system-wide while recording (for example Save As in many apps), and a registration failure is silent.** REQUIRED; natively log the failure at warning level.

**EDGE-CAP-41. A hotkey press while an own window is foreground (the pill just clicked) is suppressed silently.** REQUIRED (the guard applies to hotkeys). `:1133-1138`.

**EDGE-CAP-42. GDI leaves the alpha byte of a 32 bpp DIB undefined (often 0), so an unmodified BitBlt frame encoded as BGRA PNG is transparent.** Force alpha to 255 before encoding. Native only.

**EDGE-CAP-43. An element JSON longer than 8192 bytes is truncated and fails to parse, so the element is unavailable.** Natively there is no buffer (7.10).

**EDGE-CAP-44. Some frameworks expose a ComboBox's selected value (or an Edit's text) as its UIA Name.** Electron names them anyway; macOS refuses a popup button's title for this reason (`ElementLocator.swift:190-195`). Q-CAP-10.

**EDGE-CAP-45. Two concurrent `start` calls both pass the session check before either installs a session.** Natively the check-and-reserve is atomic (INV-CAP-22).

**EDGE-CAP-46. If the hook fails to install after the session is set, Electron is left with a phantom recording that can never produce a step.** Natively fail the start and clear the session (macOS `CaptureEngine.swift:220-230`).

**EDGE-CAP-47. uiohook narrows coordinates to `int16`.** Natively use 32-bit coordinates (harmless IMPROVEMENT for virtual desktops beyond 32767 px).

**EDGE-CAP-48. The hook worker kept the process alive after quit (zombie).** Tear down on quit; natively the hook thread is a background thread and teardown posts `WM_QUIT`. `:1478-1483`.

**EDGE-CAP-49. `CaptureState.stepCount` is the filename counter,** so a recording appended to a 5-step project whose `shots/` holds `step-0037.png` shows `Capturing · 37` from the start. macOS reports the session count (`CaptureEngine.swift:175-180`). IMPROVEMENT (7.10).

**EDGE-CAP-50. Clicking an inactive window in auto mode.** The step reads the foreground window when it starts; if activation has not happened yet, auto mode crops to the previously focused window. Electron's dispatch latency usually hides this (UNVERIFIED); macOS needed `windowAt(point)` (`CaptureEngine.swift:851-864`). Q-CAP-4.

**EDGE-CAP-51. The no-click screenshot's session is live for at least 350 ms with no pill and no hook.** A `start` for the same project during it returns a `recording` state for a session that has no hook and ends when the screenshot finishes; `stop`, `discard` or `pause` during it make the grab return null and the call throw `'Could not capture the screen \u2014 ...'`. `CaptureController.ts:682-687`, `857-895`. Natively D21: while a screenshot is in flight, `StartAsync` throws `A recording is already in progress` for any path, and `Pause`, `Resume`, `StopAsync`, `DiscardAsync` are no-ops that return the idle state (the screenshot owns its own cleanup).

**EDGE-CAP-52. A store failure after the PNG write leaves an untracked orphan PNG.** The counter was burned and the file exists, but `addedStepIds` never gets the id, so Discard does not delete the file; the next session seeds past it. `:1371-1377`, `:1445-1453`. REQUIRED (numbers may skip, never repeat); natively also log the orphan path at warning.

**EDGE-CAP-53. UWP apps report the ApplicationFrameHost pid.** get-windows replaces the owner's path and name with the child app's but keeps the frame host's `processId` (`main.cc:161-169`, `196`), so `CapturedWindow.pid` is the frame host's pid while `app` is the UWP app's FileDescription. REQUIRED for parity of the persisted metadata; the own-window pid check is unaffected (shotAI is not UWP).

**EDGE-CAP-54. A right-click on the pill arms the menu in Electron.** `onMouseDown` has no own-window gate outside single mode, so the pill's right-click starts the 400 ms monitor poll and the next nearby left click (for example on the pill's Stop button) is consumed as a menu selection, whose step is then suppressed by `captureStep`'s guard. `:311-461`, `:1133-1138`. Natively D1.

**EDGE-CAP-55. The stop log count is read before the drain.** `recording stopped (<n> steps total)` reports the filename counter before queued captures commit, so it can be lower than the final counter and higher than the number of steps taken this session. `:946-951`. Natively log both the session count and the counter after the drain (IMPROVEMENT, log-only).

**EDGE-CAP-56. A whitespace-only UIA Name qualifies as a label.** Rust `is_empty` and JavaScript truthiness both accept `" "`, producing `Click ' ' button in <app>`. `lib.rs:159`, `element-locator.ts:116`, `click-caption.ts:48-49`. REQUIRED parity (captions are editable; SOP generation rewrites them).

**EDGE-CAP-57. The 600 ms element timeout starts after the lazy DLL load.** A click before `warmUpElementLocator`'s load completes waits for the load plus 600 ms, but the capture itself does not wait on the element until after the grab and the PNG write (`:1388`). `element-locator.ts:94-106`. Natively the timeout starts at enqueue and `WarmUp` completes synchronously at `StartAsync`.

**EDGE-CAP-58. `captureScreenshot` errors are thrown, not broadcast.** It calls `captureStep` outside the queue, so a collision or store failure rejects the IPC call (the report shows it) and `capture:error` never fires. `:877-890`. REQUIRED: `CaptureScreenshotAsync` throws; it never raises `CaptureFailed`.

**EDGE-CAP-59. A single-shot capture that throws never stops its session.** The `void this.stop()` follows the awaited `captureStep` in the same task, so an exception skips it, leaving the pill up and the hook attached with `fired = true` (every later click ignored) until the user presses Stop. `:330-341`. ELECTRON-ONLY (the path is not ported, Q-CAP-1).

**EDGE-CAP-60. A `window` or `area` start target that failed IPC parsing records silently degraded steps.** `{ mode: 'window' }` without a window warns `picked window not found \u2014 falling back to monitor capture` and captures the click monitor on every step; `{ mode: 'area' }` without an area captures the click monitor with no warning. `src/main/ipc.ts:115-138`; `:1260-1314`. REQUIRED for the recording path; natively `StartAsync` logs one warning at start when the target is incomplete.

---

## 6. macOS port notes

| Topic | macOS implementation | Divergence from Electron and why | Lesson for Windows |
|---|---|---|---|
| Structure | `CaptureKit` package: a platform-neutral `CaptureEngine` actor behind protocols `Screenshotter`, `ActiveWindowProviding`, `ElementLocating`, `OwnWindowChecking`, `TriggerSource` (`CaptureTypes.swift:118-170`), with fakes (`Tests/CaptureKitTests/Fakes.swift`) and 79 headless `@Test` cases in 6 suites (59 excluding the macOS-only `PermissionLedgerTests`) | none in behavior | Put the engine in ShotAI.Core behind the same seams (7.1); port the macOS engine tests to Linux xunit. |
| Two serialization layers | inputs flow through an `AsyncStream` consumed serially (decisions), captures chain on a FIFO task tail (`CaptureEngine.swift:12-19`, `468-476`, `602-618`) | same as Electron's handler plus promise queue | Dispatcher thread for decisions, single capture worker for jobs (7.3). |
| Generation token and `tearingDown` | `generation` bumped per session; every suspension re-checks `sessionAlive(gen)`; `tearingDown` blocks buffered inputs during a drain (`:119-130`, `:595-600`, `:747-752`) | Electron relies on single-threaded JS | Mandatory natively (EDGE-CAP-28). |
| Own-window gate first | the geometric own-window check runs before the element query, because an AX hit test over its own SwiftUI window crashed (`:485-496`, `ReviewFixTests.swift:38-55`) | ordering change | Adopt: a UIA query into our own WPF provider would marshal to our UI thread and can stall; skipping it for own clicks is free (7.10). |
| Frontmost-own suppression | only when a real main-capable own window is visible, to avoid eating the first click after hiding (`AppOwnWindows.swift:24-38`) | narrower than Electron's pid check | On Windows the pill is non-activating (spec 03), so after the hide the foreground is another app; keep Electron's pid rule. |
| Triggers | listen-only mouse-down `CGEventTap` on a dedicated thread; re-enables itself on `tapDisabledByTimeout` (`SystemTriggers.swift:105-182`); Carbon `RegisterEventHotKey` Shift+Cmd+S; registration failure silent (`:189-230`) | re-enable on timeout is the analog of the Windows watchdog | Dedicated hook thread plus watchdog reinstall (7.4). |
| Attach failure | a trigger attach failure fails `start` (`CaptureEngine.swift:220-230`) | Electron leaves a phantom session | Adopt (EDGE-CAP-46). |
| State `stepCount` | session count `addedStepIds.count` (`:175-180`) | Electron reports the filename counter | Adopt (EDGE-CAP-49). |
| Grab failure | emits one error per failure run: `"A screenshot could not be captured. If this keeps happening, re-check Screen Recording permission in System Settings."`, only when `sessionAlive(gen)` (a teardown-caused nil is silent); a success clears the run (`:731-745`) | Electron is silent | Adopt with a Windows string (7.10, Q-CAP-7). |
| Error prefix | thrown capture errors are surfaced as `"Capture failed: <description>"` (`CaptureEngine.swift:620-623`) | Electron broadcasts the bare message | Keep Electron: the bare message (the pill adds no prefix; the main window adds `Capture error: `). |
| Menu poll | per-arm `polling` flag (`:66-84`), `menuArm === arm` identity checks, skipped ticks do not count (`:647-677`) | cleaner than Electron's shared flag | Adopt the per-arm flag. |
| Screen-mode menu | reuses the pre-grab only when it is the chosen monitor (`:898-911`) | fixes EDGE-CAP-33 | Adopt. |
| Auto window target | uses the window under the click when the click is outside the active window's bounds (`:851-864`); falls back to a region crop, not fullscreen, when auto-window cannot crop (`:1006-1021`); menu-bar band override (`:866-877`) | macOS focus lags clicks; menu bar is macOS-only | Keep Electron behavior; verify manually (Q-CAP-4). The menu-bar band has no Windows analog. |
| Auto classifier | Finder with an empty title (the desktop) is `region`, not fullscreen; shell hosts by bundle id (`AutoClassify.swift:16-30`, `CaptureConstants.swift:66-76`) | platform taxonomy | Keep the Windows rules exactly (2.8.1). |
| Units | global POINTS, so constants are not multiplied by scale (`CaptureConstants.swift:3-6`) | Windows multiplies logical constants by the monitor scale factor because its space is physical px | Keep the multiplication natively. |
| Image output | crop edges rounded independently in pixel space, downscale `targetH = round(h * targetW / w)`, `imageScale = pixelScale * downscale` (`ImageOutput.swift:27-87`) | macOS has a point-to-pixel factor; Windows is already in px (pixelScale 1) | Use `targetH` formula; `imageScale` is only the downscale. |
| Immediate capture | no window hide (the ScreenCaptureKit filter excludes own windows); caption from `buildManualCaption`; window metadata from the target; `stepAdded` emitted (`CaptureEngine.swift:237-320`) | diverges | Keep Electron: hide plus 350 ms settle, hotkey caption, foreground metadata, no step event (Q-CAP-6). |
| Insert sessions | `insertBase + addedStepIds.count` (`:809-818`) | equivalent to the rolling cursor | Keep Electron's cursor. |
| `captureSettings` | the coordinator persists the target into the manifest (`CaptureCoordinator.swift:115`) | Electron never writes it (always `null`) | Keep Electron (Q-CAP-12). |
| Confinement | `shots/` and each PNG path confined, symlinks refused, message `"This project's shots folder resolves outside the project (it may be a symlink). Recording was refused so screenshots aren't written elsewhere."` (`CaptureEngine.swift:21-35`, `625-635`) | IMPROVEMENT | Adopt with reparse-point rules from spec 01 (INV-CAP-23). |
| Element locator | AX hit test, climb of 6, the same 15-type allowlist in the UIA vocabulary, 600 ms cap on a different queue from the work queue so it can fire, concurrent work queue so one hung app does not back up later queries, global AX messaging timeout 0.5 s, label-only names, secure fields yield nothing (`ElementLocator.swift:1-325`) | privacy stricter on popup buttons | Use a small pool of MTA threads plus `IUIAutomation2` timeouts (7.6). |
| No `captureSingle` | not ported | dead path | Do not port (Q-CAP-1). |
| Coordinates | AppKit to CG flip about the primary display only (`CoordinateSpaces.swift:1-25`) | macOS only | Windows needs no flip; PMv2 physical px everywhere. |

---

## 7. Native design (C#)

### 7.1 Placement

| Project | Namespace | Types |
|---|---|---|
| ShotAI.Core | `ShotAI.Core.Capture` | `CaptureConstants`, `CaptureGeometry`, `AutoClassifier`, `ClickCaptions`, `DownscalePolicy`, `ShotNaming`, `UiaControlTypes`, `ElementMapping`, `CaptureShield`, `ShieldedScreenCapture` (implements `IScreenCapture` over an `IMonitorCapture` and the `CaptureShield`; in Core so `ShieldedCaptureTests` run on Linux), `CaptureEngine` (implements `ICaptureService` and `IDisposable`, R-ARCH-10), `MenuArm` (internal), `CaptureSession` (internal), `CaptureException` and `TriggerException` (both derive from `ShotAIException`, 7.2), event arg records, and the seam interfaces `ITriggerSource`, `IMonitorCapture` (raw), `IScreenCapture` (shielded), `IWindowInfoProvider`, `IElementLocator`, `IOwnWindows`, `IWindowProtection`, `IImageCodec`, `ICaptureClock`, `ICaptureSettings` (declared here, implemented by spec 10's `SettingsService`; landed by WP-A10). `JsMath` is NOT in this namespace: the one copy is `ShotAI.Core.Json.JsMath` (spec 01, R-ARCH-1) |
| ShotAI.Platform | `ShotAI.Platform.Capture` | `Win32TriggerSource` (hook thread, hotkey, watchdog), `GdiMonitorCapture` (implements `IMonitorCapture`), `Win32WindowInfoProvider`, `UiaElementLocator`, `WicImageCodec`, `DisplayAffinityProtection` (uses the existing `CaptureExclusion.Apply`), `OwnWindowRegistry` (its queries are the internal `OwnWindows : IOwnWindows`, corrected in WP-B5), `PlatformCaptureRegistration` (an internal static helper called from `AddShotAIPlatform`, ARCHITECTURE C7; the one registration file that names `IMonitorCapture`). Every type here that implements a Core seam is `internal sealed` and registered only as its Core interface (INV-ARCH-4); the exception is `OwnWindowRegistry`'s registration surface (`Register`, `Unregister`), which is public because the App calls it (03's `ShotAIWindow` and `ShotAIPopup`, INV-SHELL-1); its other caller is 09's `WebView2PdfRenderer` in this assembly, which registers the never-shown PDF host popup (`PdfHostWindow`) right after `CreateWindowEx` and before any controller is attached (09 7.7, ARCHITECTURE 5.4) |
| ShotAI.Platform | `ShotAI.Platform.Imaging` | `WicFactory` (internal static, landed by WP-A13; corrected in WP-A13 from public, since every type that uses it is in Platform; its `Instance` refuses a caller that is not on an MTA thread; the one lazily created MTA `IWICImagingFactory`, created once on an MTA thread as 7.12 requires, shared by `WicImageCodec`, 05's `ReportImageDecoder` and `WicImageSizeProbe`, and 09's `WicExportImageCodec`; no other type calls `CoCreateInstance(CLSID_WICImagingFactory)`) |
| ShotAI.App | none owned by this spec | The App consumers of `ICaptureService` belong to other specs: 03's `RecordingVisibilityController` (the `RecordingChanged` handler: hide main, show pill), `CapturePillViewModel` and `ShotAIWindow`/`ShotAIPopup` (which register every own HWND with `OwnWindowRegistry` in `OnSourceInitialized`, INV-SHELL-1), 06's `RecordingPanelViewModel` and `Capture error: ` banner, and 11's `RemoteVisibilityApplier`. Each subscriber marshals with `IUiDispatcher.Post` only (ARCHITECTURE T6, R-ARCH-11), never `Dispatcher.Invoke`, `BeginInvoke` or `InvokeAsync` |

Model types (`CaptureTarget`, `Rect`, `Point`, `ProjectStep`, `StepClick`, `StepElement`, `CapturedWindow`, `CapturedMonitor`, `WindowInfo`, `MonitorInfo`, `ProjectManifest`) come from spec 01; store calls go through `IProjectService` (`ShotAI.Core.Store`), never the concrete `ProjectStore` (R-ARCH-4). Core uses `Microsoft.Extensions.Logging.Abstractions` (platform neutral), which the foundation PR adds to `dotnet/Directory.Packages.props` together with `ShotAI.Core.Errors` and `ShotAI.Core.Threading` (ARCHITECTURE 3.5, WP-A1), so the capture PRs find it; capture adds no package of its own.

Corrected in WP-B1: `WindowInfo` and `MonitorInfo` are not spec 01 types. They are the Window and Screen choosers' shapes (`src/shared/project.ts:36-50`), never persisted, so they are declared in `ShotAI.Core.Capture` beside `CaptureTargets`, their only user. As built in WP-B1: `ICaptureService` is declared by WP-B2 with `CaptureEngine`, because it is a catalog interface (ARCHITECTURE 4.4, 11 7.3) and `ContainerTests.EveryCatalogInterfaceResolves` requires every catalogued interface that exists to resolve; `CaptureException` and `TriggerException` arrive with WP-B2 as well. The seams, records and pure rules of this section and 7.2 are in, with these surfaces: `CaptureGeometry.UnionRect`, `ClickBox`, `CropRect`, `AreaCrop` (the area path's crop, 2.8.2) and `RegionCrop` (the auto shell-region crop); `AutoClassifier.Classify(ForegroundInfo?)` and `Classify(app, title)`; `ClickCaptions.ControlWord`, `Build`, `Hotkey` and `AppName` (`screen` with no window); `DownscalePolicy.Compute`, which gives a `DownscaleTarget` or null, and `Encode`, which gives an `EncodedShot` (the PNG, its size and the width ratio applied; the fallback encodes the frame as grabbed, and a failure of that encode fails the capture, which Electron cannot hit because its input is already a PNG); `ShotNaming.Format`, `OrphanNumber` and `Seed`; `UiaControlTypes.Name`, `IsActionable` and `ActionableIds`. The shell host test and the orphan pattern fold ASCII case only, through the internal `AsciiCase`, as a JavaScript `/i` without `u` does, whatever the culture: `ToUpperInvariant` would turn the long s (U+017F) into `S`, a case-insensitive `Regex` matches the Kelvin sign (U+212A) with `k`, and a Turkish culture lowers `I` to a dotless i.

As built in WP-B2: `ICaptureService`, `CaptureEngine` (a `public sealed partial` class in `CaptureEngine.cs`, `.Sessions.cs`, `.Step.cs` and `.Log.cs`), `CaptureException`, `TriggerException`, `CaptureMessages` (the 2.15, D6, D7, D10 and D18 texts as constants), the internal `CaptureSession`, `SessionKind` and `CaptureJob`, and `TimeProviderCaptureClock` (the `ICaptureClock` over a `TimeProvider`, which WP-B6 registers over `TimeProvider.System`) are in. Corrected in WP-B2: declaring `ICaptureService` with `CaptureEngine` does not make it resolvable. The engine takes every capture seam and the Platform ones land in WP-B4 to WP-B6, so `ICaptureService` resolves from WP-B6 on. Until then `Composition.ContainerTests.EveryCatalogInterfaceResolves` names it in `ResolvableFrom` with that WP and fails as soon as it resolves, so the entry goes with the registration (11 8.2). `MenuArm` and the click decisions of 2.3 and 2.4 are WP-B3's.

As built in WP-B4: the hand-off from the hook to the dispatcher is Core's, so its rules are tested on Linux: `InputRecord` (a mousedown or a hotkey press, with the hook's `Stopwatch` stamp), `InputRing` (the 256-record single-producer single-consumer ring of 7.3) and `TriggerDispatcher` (the `shotAI.CaptureDispatcher` thread). Platform's `Win32TriggerSource`, internal sealed and registered as `ITriggerSource` by `PlatformCaptureRegistration` (which WP-B5 and WP-B6 extend), owns the hook thread and makes a ring and a dispatcher for each attach.

As built in WP-B5: `PixelFrame` is disposable, and a frame rented from Core's `FramePool` gives its buffer back when it is disposed (D24); reading a disposed frame's pixels throws. Core also has `Opaque` (the alpha rule of EDGE-CAP-42) and `PixelFrame.CopyRect` (the crop of 7.12), and `AddShotAICore` registers the one `CaptureShield` and `IScreenCapture` as `ShieldedScreenCapture`. Platform's `GdiMonitorCapture`, `DisplayAffinityProtection`, `OwnWindows` and `WicImageCodec` are internal sealed, registered by `PlatformCaptureRegistration` as `IMonitorCapture`, `IWindowProtection`, `IOwnWindows` and `IImageCodec`. Corrected in WP-B5: `OwnWindowRegistry` does not implement `IOwnWindows`, because a public Platform type may implement no Core seam (INV-ARCH-4, `ContainerTests.PlatformSeamsAreInternalAndRegisteredAsCoreInterfaces`); the registry keeps the public registration surface, `OwnWindows` answers the queries over it, and `DisplayAffinityProtection` walks it for the shield.

### 7.2 Core contracts

```csharp
namespace ShotAI.Core.Capture;

public enum MouseButton { Left, Right, Middle, Other }          // JSON: "left","right","middle","other"
public enum CaptureStatus { Idle, Recording, Paused }
public enum AutoMode { Window, Region, Fullscreen }
public enum StepTrigger { Click, Hotkey }

public sealed record CaptureState(CaptureStatus Status, string? ProjectPath, string? ProjectTitle,
                                  int StepCount, bool WillDeleteProjectOnDiscard)
{ public static readonly CaptureState Idle = new(CaptureStatus.Idle, null, null, 0, false); }

public sealed record CaptureStartOptions(CaptureTarget? Target = null, bool CreatedThisSession = false,
                                         int? InsertAt = null, bool AttachTriggers = true);
public sealed record DiscardResult(CaptureState State, bool ProjectDeleted);
public sealed record CaptureTargets(IReadOnlyList<WindowInfo> Windows, IReadOnlyList<MonitorInfo> Monitors);

// Events (raised on engine threads, outside the engine lock, through ShotAI.Core.Threading.EventRaiser.Raise,
// so a throwing handler is logged and the others still run; ARCHITECTURE T5). App subscribers marshal with
// IUiDispatcher.Post only (T6, R-ARCH-11).
public sealed record StepLandedEventArgs(ProjectStep Step, int Index, string ProjectPath);
public sealed record CaptureErrorEventArgs(string Message);
public sealed record RecordingChangedEventArgs(bool Recording, bool ShowPill);

// Every user-facing message the engine throws (2.15, D7, D10, D18) is a ShotAIException (ARCHITECTURE 8.1, 11 X2).
public sealed class CaptureException : ShotAIException { public CaptureException(string message) : base(message) { } }
public sealed class TriggerException : ShotAIException { /* carries the Win32 error; StartAsync maps it to the D7 message */ }

// CaptureEngine : ICaptureService, IDisposable. Dispose() is what provider.Dispose() calls at exit (7.13, R-ARCH-10).
public interface ICaptureService : IAsyncDisposable
{
    CaptureState GetState();                                                        // lock-bounded snapshot (T9)
    Task<CaptureState> StartAsync(string projectPath, CaptureStartOptions options, CancellationToken ct = default);
    Task<ProjectManifest> CaptureScreenshotAsync(string projectPath, CaptureTarget target, int insertAt, CancellationToken ct = default);
    CaptureState Pause();                              // may take the engine lock: UI callers use Task.Run (T9)
    CaptureState Resume();                             // same
    Task<CaptureState> StopAsync();
    Task<DiscardResult> DiscardAsync();
    Task<CaptureTargets> ListTargetsAsync(CancellationToken ct = default);
    void Teardown();                                   // synchronous, app exit
    event EventHandler<CaptureState> StateChanged;
    event EventHandler<StepLandedEventArgs> StepLanded;
    event EventHandler<CaptureErrorEventArgs> CaptureFailed;
    event EventHandler<RecordingChangedEventArgs> RecordingChanged;
}

// Seams (all implemented in ShotAI.Platform; faked in Core.Tests).
public readonly record struct MouseDown(int X, int Y, MouseButton Button, uint TimeMs);
public interface ITriggerSource
{
    /// Installs the mouse hook and (optionally) the hotkey. Throws TriggerException if the hook cannot be installed.
    /// Returns false for HotkeyRegistered when the chord is taken (silent, logged).
    TriggerAttachResult Attach(Action<MouseDown> onMouseDown, Action? onHotkey);
    void Detach();                                     // idempotent, synchronous
}
public sealed record TriggerAttachResult(bool HotkeyRegistered);

public sealed record MonitorDescriptor(uint Id, string Name, Rect Bounds, double ScaleFactor, bool IsPrimary);
public sealed class PixelFrame                        // top-down BGRA32, alpha forced to 255
{ public required int Width { get; init; } public required int Height { get; init; } public required byte[] Bgra { get; init; } }
public interface IMonitorCapture                      // RAW: only CaptureShield-wrapped code may call it
{
    IReadOnlyList<MonitorDescriptor> Monitors();
    PixelFrame Capture(MonitorDescriptor monitor);    // synchronous pixel read
}
public interface IScreenCapture                       // SHIELDED funnel, the only one the engine sees
{
    IReadOnlyList<MonitorDescriptor> Monitors();
    MonitorDescriptor? FromPoint(int x, int y);       // half-open contains, null off every monitor
    PixelFrame Grab(MonitorDescriptor monitor);       // shield taken and released around the raw read
}

public sealed record ForegroundInfo(nint Hwnd, int Pid, string App, string Title, Rect? WindowRect, Rect? FrameBounds, bool Minimized);
public interface IWindowInfoProvider
{
    ForegroundInfo? Foreground();                     // get-windows semantics (7.5)
    IReadOnlyList<ListedWindow> ListWindows();        // z order, top first
    ListedWindow? Resolve(CaptureTargetWindow target);
}
public sealed record ListedWindow(uint Id, int Pid, string Title, string App, Rect FrameBounds, bool Minimized, bool Focused);

public interface IElementLocator { void WarmUp(); Task<StepElement?> ElementAtAsync(int x, int y); } // never throws
public interface IOwnWindows { int ProcessId { get; } bool PointHitsOwnWindow(int x, int y); bool IsOwnWindow(nint hwnd); }
public interface IWindowProtection { void SetAllExcluded(bool excluded); }   // every registered own window
public interface IImageCodec
{
    PixelFrame Crop(PixelFrame frame, int x, int y, int width, int height);
    PixelFrame Resize(PixelFrame frame, int width, int height);              // high-quality downscale
    byte[] EncodePng(PixelFrame frame);
}
public interface ICaptureClock { long NowMs(); Task Delay(int ms, CancellationToken ct); }
public interface ICaptureSettings { double CaptureScaleNow(); bool RemoteVisibleNow(); }   // synchronous caches (spec 10)
```

Why a synchronous `Grab`: the menu path needs a synchronous read at mousedown (EDGE-CAP-1), and the other paths call it on the capture worker thread, so no async variant is needed. This also makes the shield a plain `using` scope.

As built in WP-B1: the clock's delay is `ICaptureClock.DelayAsync(int ms, CancellationToken ct)`, since the analyzers require the suffix on a method that returns a `Task` (VSTHRD200). `ShieldedScreenCapture.FromPoint` enumerates the monitors afresh on each call and tests `left <= x < right` and `top <= y < bottom`; `Monitors()` and `FromPoint` read no pixels, so they take no shield.

Service-boundary requests from spec 11 section 10, adopted here: `CaptureScreenshotAsync` throws `CaptureException("A screenshot needs an explicit target (screen, window, or area).")` for a null or `Auto` target (EDGE-IPC-16, D18); `StartAsync` clamps a negative `InsertAt` to 0 (EDGE-IPC-17; 2.2.2 step 7 already clamps to `[0, length]`); every `CaptureScreenshotAsync` that passed target validation ends with exactly one idle `StateChanged`, on success and on failure (EDGE-IPC-33, INV-CAP-31); `capture:single` has no member (Q-CAP-1); events go through `EventRaiser` (T5); `CaptureEngine` implements `IDisposable` as well as `IAsyncDisposable` (R-ARCH-10, 7.13).

As built in WP-B2: `CaptureScreenshotAsync` takes a `CaptureTarget?`, because the null check is the engine's (EDGE-IPC-16, D18); the four events are declared `event EventHandler<T>?`; `CaptureException` also has a `(string message, Exception? inner)` constructor, which `StartAsync` uses to keep the `TriggerException` behind the D7 message, and `TriggerException(string message, int win32Error)` exposes `Win32Error` for the log. Teardown is permanent: after it, `StartAsync` and `CaptureScreenshotAsync` throw `ObjectDisposedException` (7.13). The requests above are covered by 11's `ServiceBoundary.ScreenshotTargetTests` (the explicit target and the one idle `StateChanged`), `InsertAtIsClamped`, `EventsGoThroughEventRaiser` and `DisposeIsSynchronousAndIdempotent`.

As built in WP-B5: `IWindowProtection` also has `void SetExcluded(nint hwnd, bool excluded)` (one registered window; a gone or unregistered one is skipped) and `event EventHandler<nint>? WindowAdded` (raised on the registering thread once a window joined the set, excluded), for 7.8 rule 1's reconcile. `PixelFrame : IDisposable` has `IsDisposed` and `CopyRect(x, y, width, height)`. A frame a seam returns is the caller's to dispose, and `IImageCodec` never disposes a frame it is given.

### 7.3 Threads and the engine loop

| Thread | Name | Owns | Must never |
|---|---|---|---|
| Hook thread | `shotAI.InputHook` | `SetWindowsHookExW(WH_MOUSE_LL)`, `RegisterHotKey`, the `GetMessageW` loop, the preallocated input ring | allocate, lock, log, or call anything but `CallNextHookEx` inside the hook proc |
| Dispatcher | `shotAI.CaptureDispatcher` | the mousedown and hotkey decision logic (2.3, 2.4), the synchronous click-time menu grab, starting element queries, enqueuing jobs | wait on the capture worker or the UI thread |
| Capture worker | one async loop reading `Channel<CaptureJob>` (single reader, FIFO, `await`ing each job before reading the next); after its first `await` it runs on thread-pool (MTA) threads, so nothing in it may be thread-affine | `captureStep` (2.6), downscale, encode, file write, store calls, events | run two jobs at once |
| Poll task | one `PeriodicTimer(400 ms)` loop per arm, on the thread pool | refreshing `MenuArm.Frame` | outlive its arm (cancelled on disarm, re-arm, pause, expiry, 32 frames) |
| UIA workers | 2 dedicated MTA threads, `shotAI.Uia.0` and `.1` | UIA COM calls | touch WPF |
| UI thread | WPF Dispatcher | windows | synchronously wait on any capture thread (it would deadlock a cross-thread `SetWindowDisplayAffinity` or a UIA call into our own provider) |

State: all mutable engine state (`_session`, `_generation`, `_starting`, `_tearingDown`, `_menuArm`, `_lastLeftClick`, `_lastGrabFailed`) lives in `CaptureEngine` under one `object _gate` lock. Critical sections are short and never include I/O, a grab, a UIA call or an event raise. After every `await`, the capture worker re-checks `SessionAlive(gen)` (`_session != null && _generation == gen && !_tearingDown`) before mutating session state or committing (EDGE-CAP-28). Events are raised outside the lock through `EventRaiser.Raise` (ARCHITECTURE T5), in the order Electron broadcasts them (`StepLanded` before `StateChanged`, INV-IPC-5). All Core and Platform capture code awaits with `ConfigureAwait(false)` (T2, `CA2007` as error); none of it is UI-affine.

Corrected in WP-B2: there is no `_tearingDown` that stop and discard set. Their drain must run the queued captures against the live session (2.2.6, REQUIRED), and a flag inside `SessionAlive` would drop exactly the captures the drain exists to keep. As built: `_stopping`, a count that stop and discard hold for the drain, blocks only new trigger input (`OnMouseDown`, `OnHotkey`); `_tornDown`, which only `Teardown` sets and nothing clears, is the flag in `SessionAlive(gen)` = `_session != null && _session.Generation == gen && _generation == gen && !_tornDown`; `_screenshotPending` marks a screenshot from its reservation to its end (D21); `_lastGrabFailed` is the D6 run. A mousedown that passed the session check before a stop and is queued after the next start fails the generation check (EDGE-CAP-28, `AStaleClickLandsInNoLaterSession`).

Corrected in WP-B3: the poll task is not a `PeriodicTimer`. It is a loop over `ICaptureClock.DelayAsync(400, arm.Token)`, so a test drives it with the engine's clock like every other delay (a `PeriodicTimer` takes a `TimeProvider`, not the clock seam), and each tick starts its grab without awaiting it, so the ticks keep Electron's `setInterval` cadence and a tick that finds this arm's grab still running is skipped without counting a frame (2.4.3). The 7.11 sketch awaited the grab inside the loop, where the in-flight flag could never be seen set and a slow grab stretched the interval. Each delay starts when the previous tick's checks end, a drift of microseconds per tick. The loop and its grab are `PollAsync` and `PollGrabAsync` in `CaptureEngine.Menu.cs`.

Dispatcher input: the hook thread writes `MouseDown` records into a 256-entry single-producer single-consumer ring (preallocated struct array, indices published with `Volatile.Write`) and signals an `AutoResetEvent` (a kernel `SetEvent`, no managed lock and no allocation; a `SemaphoreSlim.Release` would take a managed lock, which the hook proc must not); a full ring drops the event and increments a counter logged by the dispatcher (a full ring means the dispatcher is stalled, which is itself logged). Hotkey presses enter the same ring as a flagged record, so mouse and hotkey order is preserved.

As built in WP-B4: the dispatcher is Core's `TriggerDispatcher`, which `Win32TriggerSource.Attach` makes with the engine's two callbacks, so it is the trigger source's thread, not the engine's (ARCHITECTURE 6.1, corrected). It waits on the `AutoResetEvent`, drains the ring, and delivers each record in order; a callback that throws is logged at error (`trigger callback failed:`) and the next record still arrives; the drops are logged once per drain at warning (`input ring full: <n> events dropped; the capture dispatcher was stalled`); each pickup is logged at debug with its latency from the hook's stamp (`input: <mousedown|hotkey> picked up <n> us after the hook`, PB-2). A callback may stop the dispatcher without waiting for itself. Records still in the ring at a detach are dropped, since the engine detaches only when it ends its session (`InputRingTests`, `TriggerDispatcherTests`).

Capture job:

```csharp
internal sealed record CaptureJob(StepTrigger Trigger, (int X, int Y)? Point, MouseButton Button,
    bool MenuPopup, Rect? MenuOwnerBounds, (PixelFrame Frame, MonitorDescriptor Monitor)? PreGrab,
    int? InsertAt, Task<StepElement?>? Element, bool Broadcast, bool SkipOwnWindowGuard, int Generation);
```

As built in WP-B2: `CaptureJob(Trigger, Point, Button, InsertAt, Element, Broadcast, SkipOwnWindowGuard, Generation)`; WP-B3 adds `MenuPopup`, `MenuOwnerBounds` and `PreGrab` with the menu path. The channel is a `Channel<QueueItem>`, where an item is a job or a drain marker whose `TaskCompletionSource` the worker completes when it reaches it; stop and discard await that marker (7.11).

As built in WP-B3: the job has the sketch's fields in the sketch's order, and `PreGrab` is a `MonitorFrame(PixelFrame Frame, MonitorDescriptor Monitor)` record, the type `MenuArm.Frame` holds.

Job failure: any exception except `OperationCanceledException` during teardown is logged at error (`capture failed:`, exception type, message and stack, never user content, ARCHITECTURE 8.5) and raised as `CaptureFailed(UserMessage.From(ex))` with the queue continuing (INV-CAP-21). `UserMessage.From` (ARCHITECTURE 8.2, INV-IPC-17) keeps the message verbatim for a `ShotAIException`, `IOException` (including the `ERROR_FILE_EXISTS` collision, Electron's `EEXIST` case) and `UnauthorizedAccessException`, which is Electron's `e.message` parity (2.13); any other exception type shows `UserMessage.Generic` instead of the raw .NET message (IMPROVEMENT, 11 X4). No prefix is added here; 06's banner adds `Capture error: `. The pill's substitute text for an empty message (spec 03, INV-SHELL-12) stays as a defensive fallback.

### 7.4 Triggers: `Win32TriggerSource`

- **Hook thread start** (`Attach`): create a background `Thread` (not the thread pool; `IsBackground = true`, `Priority = AboveNormal`), and on it call `SetWindowsHookExW(WH_MOUSE_LL, s_proc, GetModuleHandleW(null), 0)`. `s_proc` is a static `HOOKPROC` delegate held in a static field so it is never collected (or a `[UnmanagedCallersOnly]` static function pointer, which needs no delegate at all). Before installing, the thread calls `PeekMessageW(out _, HWND.Null, 0, 0, PM_NOREMOVE)` so its message queue exists before `Attach` returns (otherwise an early `PostThreadMessageW` from `Detach` or the watchdog fails with `ERROR_INVALID_THREAD_ID`). If the hook result is null, `Attach` throws `TriggerException` with the Win32 error, and `StartAsync` fails (IMPROVEMENT, EDGE-CAP-46). `Attach` waits for the thread's install result with a 2000 ms timeout. The hook fires only while that thread pumps messages ("the thread that installed the hook must have a message loop", Microsoft Learn, LowLevelMouseProc).
- **Hook proc**: `if (nCode == HC_ACTION)`: for `WM_LBUTTONDOWN` (Left), `WM_RBUTTONDOWN` (Right), `WM_MBUTTONDOWN` (Middle), `WM_XBUTTONDOWN` (Other, both X buttons): write `(pt.x, pt.y, button, time)` into the ring and signal the event. For `WM_MOUSEMOVE`: `Volatile.Write(ref s_lastHookTick, Environment.TickCount64)`. Do not check `LLMHF_INJECTED` (INV-CAP-28). Always `return CallNextHookEx(default, nCode, wParam, lParam)`. `MSLLHOOKSTRUCT.pt` is in per-monitor-aware physical screen coordinates regardless of DPI awareness.
- **Hotkey**: on the hook thread, `RegisterHotKey(HWND.Null, 0x5348, MOD_CONTROL | MOD_SHIFT | MOD_NOREPEAT, 0x53 /* 'S' */)`. `WM_HOTKEY` with `wParam == 0x5348` in the thread's message loop enqueues a hotkey record. A `FALSE` result logs `hotkey Ctrl+Shift+S could not be registered (in use by another app); recording continues mouse-only` at warning (IMPROVEMENT: Electron is silent) and `TriggerAttachResult.HotkeyRegistered = false`. `MOD_NOREPEAT` is an IMPROVEMENT: holding the chord must not auto-repeat captures (Q-CAP-3). Single-shot mode (if ever ported) passes `onHotkey: null`.
- **Detach**: `PostThreadMessageW(hookThreadId, WM_APP + 2, 0, 0)`; the hook thread handles it by calling `PostQuitMessage(0)` (posting `WM_QUIT` directly is discouraged because `WM_QUIT` is not an ordinary queued message); the loop exits and, on the hook thread, calls `UnhookWindowsHookEx` and `UnregisterHotKey` (the hotkey must be unregistered by the registering thread); join with a 2000 ms timeout. Idempotent. A later `Attach` starts a new thread.
- **Watchdog** (IMPROVEMENT, the `LowLevelHooksTimeout` silent-removal risk): a `System.Threading.Timer` every 2000 ms while attached reads `GetCursorPos`. If the cursor moved since the previous tick and `s_lastHookTick` did not advance in that interval, the hook is presumed removed: post `WM_APP + 1` to the hook thread, which calls `UnhookWindowsHookEx` (ignoring failure) and `SetWindowsHookExW` again, and logs `mouse hook presumed removed by the system (LowLevelHooksTimeout); reinstalled` at warning. Clicks during the dead interval are lost; the log makes it diagnosable. A programmatic cursor move (`SetCursorPos` from another app) may not pass through the low-level hook (UNVERIFIED), which would cause a spurious reinstall; that is harmless (a reinstall loses nothing) but the log line must not be read as proof of removal.
- **Why not Raw Input**: Raw Input (`RegisterRawInputDevices` with `RIDEV_INPUTSINK` on a message-only window) has no timeout, but its mouse data is relative and the position must be read with `GetCursorPos` after the fact, which is not the event position. The hook keeps uiohook's exact semantics. Raw Input is the fallback if the watchdog fires in the field (Q-CAP-5).
- **GC**: the hook thread runs managed code and can be paused by a GC suspension; the proc is allocation-free, the app uses the default concurrent workstation GC, and the watchdog covers a pathological pause.

As built in WP-B4:

- One source is attached in the process at a time, since the hook procedure and what it writes to are static; a second attach, of the same source or another, throws `InvalidOperationException`. The hook thread and the dispatcher are made for each attach. A hook that cannot be installed logs `SetWindowsHookEx(WH_MOUSE_LL) failed (Win32 error <n>)` at error and throws `TriggerException` with the error, and nothing stays attached; a thread that has not reported within 2000 ms is abandoned the same way.
- The hook procedure is a `HOOKPROC` delegate in a static field, installed with `GetModuleHandleW(null)`. Every hook event, not only a move, stamps the last hook tick: the watchdog asks whether the hook is alive, and any event answers that.
- The hotkey warning is logged by `Attach` on the caller's thread, from the hook thread's report; the hook thread logs only the watchdog's reinstall, and a reinstall that fails logs `mouse hook could not be reinstalled (Win32 error <n>); clicks are not recorded` at error.
- `Detach` stops the watchdog, posts `WM_APP + 2`, joins the hook thread for 2000 ms (a miss logs `input hook thread did not stop within 2000 ms`), then stops the dispatcher, joining it for 2000 ms too.
- The tests reach the hook through internal members: `UnhookForTest` (removes the hook as Windows would), `HookThreadAllocatedBytesForTest` (read on the hook thread), `FailNextInstallForTest`, `TimeHookForTest` (PB-1's stamps), `LastButtonFlagsForTest`, `HookThreadForTest`, `HookThreadAwarenessForTest`, and a constructor that takes the watchdog's interval.

### 7.5 Window information: `Win32WindowInfoProvider`

`Foreground()` reproduces get-windows 9.3.0 (2.10.1) exactly: `GetForegroundWindow`; null gives null. `GetWindowThreadProcessId`; `OpenProcess(PROCESS_QUERY_LIMITED_INFORMATION)` (failure gives null); `QueryFullProcessImageNameW`; app name = `FileDescription` via `GetFileVersionInfoSizeW`, `GetFileVersionInfoW`, `VerQueryValueW(@"\VarFileInfo\Translation")` taking the first `(language, codepage)` pair (fallback when the query fails: language `0x04E4`, codepage `0x0409`, the get-windows byte-order quirk of 2.10.1), then `VerQueryValueW($@"\StringFileInfo\{lang:x4}{cp:x4}\FileDescription")`; empty or missing gives `Path.GetFileName(imagePath)`. If the image file name equals `ApplicationFrameHost.exe` (ordinal), `EnumChildWindows` and adopt the path and name of the first child whose process opens with `PROCESS_QUERY_LIMITED_INFORMATION` and whose image path differs; keep `Pid` = the frame host's pid (EDGE-CAP-53). If the resulting name equals `Widgets.exe` (ordinal), return null. If `GetWindowRect` or `GetClientRect` fails, return null (get-windows parity; the memory query that get-windows also gates on is not needed, a failure there is not reproduced). `Title = GetWindowTextW`. `WindowRect = GetWindowRect` (persisted in `CapturedWindow.bounds`, parity). `FrameBounds = DwmGetWindowAttribute(DWMWA_EXTENDED_FRAME_BOUNDS)` (used for crops, Q-CAP-8). `Minimized = IsIconic`. Cache the FileDescription per image path for the process lifetime (version resources are read from disk).

The native engine derives both Electron signals from this one call: `active` is the `ForegroundInfo`; `focused` is the same window when it is not minimized and its `FrameBounds` pass the -10000 sentinel.

`ListWindows()`: `EnumWindows` (top-down z order), keeping windows that pass `IsWindow`, `IsWindowEnabled` and `IsWindowVisible`, have no `WS_EX_TOOLWINDOW`, have `WS_CAPTION` (both bits of the `WS_CAPTION` mask) or `WS_POPUP`, are either unowned (`GetWindow(GW_OWNER) == null`) or `WS_EX_APPWINDOW`, have no `WS_CHILD`, and are not cloaked (`DWMWA_CLOAKED == 0`). This is get-windows' `openWindows` filter exactly (`main.cc:238-263`), chosen because node-screenshots' filter is not inspectable (Q-CAP-9). Note it drops disabled windows (a window behind a modal dialog), which node-screenshots may have listed. `App` uses the same FileDescription rule. `Id = (uint)hwnd`.

`listTargets` then applies 2.10.2 exactly (own pid, minimized, empty trimmed title, `pid::title` dedupe, trimmed title). `Resolve` applies 2.10.3 exactly (id, then pid plus untrimmed title, then pid).

### 7.6 Element at point: `UiaElementLocator`

- CsWin32 COM interop: `CUIAutomation`, `IUIAutomation`, `IUIAutomation2`, `IUIAutomationElement`, `IUIAutomationTreeWalker`, and `CoCreateInstance`, `CoInitializeEx` in `NativeMethods.txt`.
- Two dedicated threads, each created with `SetApartmentState(ApartmentState.MTA)` before `Start` (the CLR then initializes COM as MTA; an explicit `CoInitializeEx(null, COINIT_MULTITHREADED)` is harmless and returns `S_FALSE`), and one `IUIAutomation` created with `CoCreateInstance(CLSID_CUIAutomation, null, CLSCTX_INPROC_SERVER)`. If the object implements `IUIAutomation2` (Windows 8+), set its connection and transaction timeouts to 500 ms (IDL `put_ConnectionTimeout`, `put_TransactionTimeout`; CsWin32 may project them as the settable properties `ConnectionTimeout` and `TransactionTimeout`). The defaults are 2 s and 20 s (Microsoft Learn), far above the 600 ms cap. IMPROVEMENT: bounds a hung provider, as macOS bounds AX with 0.5 s. Requests go through a `BlockingCollection`; a request is served by whichever thread is free, so one hung app does not block the next query. Every request carries its deadline (enqueue time plus 600 ms); a worker that dequeues a request past its deadline completes it with null without calling UIA, so two hung threads cannot build an unbounded backlog of stale hit tests.
- `WarmUp()`: starts the threads and creates the automation objects (called by `StartAsync`, INV-CAP-15, EDGE-CAP-13).
- `ElementAtAsync(x, y)`: enqueue a `TaskCompletionSource<StepElement?>` created with `TaskCreationOptions.RunContinuationsAsynchronously` (so completing it never runs engine code on a UIA thread); return the first of it and a 600 ms timer (`WaitAsync(TimeSpan.FromMilliseconds(600))` with the `TimeoutException` mapped to null); on timeout let the work finish and be discarded. Never throws (every COM exception maps to null, like the Rust `-1/-2/-3/-99`). Release each RCW (`Marshal.ReleaseComObject` or `FinalReleaseComObject`) on the UIA thread after reading, so hit-test elements of other processes are not held until a GC.
- Algorithm: exactly 2.12.2: `ElementFromPoint(new POINT(x, y))`; `ControlViewWalker`; climb with `depth >= 6` break; choose the nearest element whose `CurrentName` is non-empty and whose `CurrentControlType` is in the allowlist; `el = chosen ?? hit`; read `CurrentControlType`, `CurrentBoundingRectangle`.
- Mapping (Core `UiaControlTypes`): `Name = chosen != null && name != "" ? name : null`; `Available = Name != null`; `ControlType = ControlTypeName(ct)` (the 41-name table, `"Unknown"` otherwise, including id 0); `Bounds = { x: left, y: top, width: right - left, height: bottom - top }` (all zeros when the rect call failed, as Rust does). `className` and `controlTypeId` are not persisted and need not be read.
- Platform: only on Windows by construction.

### 7.7 Screen capture: `GdiMonitorCapture` (raw) and `ShieldedScreenCapture` (funnel)

`Monitors()`: `EnumDisplayMonitors(null, null, cb)`; `GetMonitorInfoW` with `MONITORINFOEXW`: `Bounds = rcMonitor` (physical in PMv2), `IsPrimary = dwFlags & MONITORINFOF_PRIMARY`; `ScaleFactor = dpiX / 96.0` from `GetDpiForMonitor(hmon, MDT_EFFECTIVE_DPI)`; `Id = (uint)hmonitor` (Q-CAP-11); `Name` = the monitor friendly name: `GetDisplayConfigBufferSizes` plus `QueryDisplayConfig(QDC_ONLY_ACTIVE_PATHS)`, then for each path `DisplayConfigGetDeviceInfo(DISPLAYCONFIG_DEVICE_INFO_GET_SOURCE_NAME)` to get the GDI device name (`\\.\DISPLAYn`) that matches `MONITORINFOEXW.szDevice`, then `DISPLAYCONFIG_DEVICE_INFO_GET_TARGET_NAME` on the same path for `monitorFriendlyDeviceName`; empty when any step fails (the chooser then shows `Display <id>`). Whether this equals node-screenshots' `name()` is UNVERIFIED (Q-CAP-21). Re-enumerated on every call (displays change mid-session).

`Capture(monitor)`: `hdcScreen = GetDC(HWND.Null)`; `hdcMem = CreateCompatibleDC(hdcScreen)`; `CreateDIBSection` with `BITMAPINFOHEADER { biWidth = w, biHeight = -h (top-down), biPlanes = 1, biBitCount = 32, biCompression = BI_RGB }`; `SelectObject`; `BitBlt(hdcMem, 0, 0, w, h, hdcScreen, bounds.x, bounds.y, SRCCOPY | CAPTUREBLT)`; copy to a managed `byte[]`; set every fourth byte (alpha) to `0xFF` (EDGE-CAP-42); `DeleteObject`, `DeleteDC`, `ReleaseDC` in `finally`. A `BitBlt` failure throws `Win32Exception`. The cursor is not drawn (parity). `CAPTUREBLT` includes layered windows such as modern context menus (Q-CAP-13). Memory: a 3840 x 2160 frame is 33 MB, which lands on the large object heap; allocating one per poll tick (every 400 ms while armed) plus one per capture causes gen 2 collections, and every collection suspends the hook thread too (Q-CAP-19). Rent frame buffers from a small dedicated pool keyed by size (or `ArrayPool<byte>.Shared`) and return them when the frame is dropped (`PixelFrame` then implements `IDisposable`; the menu arm disposes a replaced frame, and the capture job disposes its frames after encoding). Ownership must be explicit because the poll task and the dispatcher touch the same arm from different threads: the dispatcher TAKES `arm.Frame` for a selection under `_gate` (reading it and setting it to null in one critical section), the poll stores a new frame under `_gate` and disposes the frame it replaced only after releasing the lock, and a frame that arrives for a stale arm is disposed immediately. The DIB section may be cached per monitor size for the same reason.

As built in WP-B5: `GdiMonitorCapture.Capture` reads as above, calls `GdiFlush` before it touches the DIB's bits, and copies them with `Opaque.Copy` into a frame rented from its `FramePool`, which keeps 2 buffers (the poll's cycle needs one and a capture's grab the other; at 3840 x 2160 that is 66 MB kept between recordings). A failed GDI call gives the frame back and throws `Win32Exception`. The DIB section is not cached, since it holds no managed memory. `Monitors()` enumerates with `EnumDisplayMonitors` and `GetMonitorInfoW` over `MONITORINFOEXW`, takes each name from the DisplayConfig map by its `szDevice` (a step that fails leaves the name empty), and ids each monitor `(uint)HMONITOR` (Q-CAP-11). The frames (D24): the capture step disposes each grabbed, cropped and resized frame once the PNG is encoded, and a monitor frame once its crop is copied out; the poll disposes the frame it replaces, and a frame that lands for a replaced arm, outside the lock; a disarm or a replacing arm takes the arm's frame under the lock, so no selection can take it too, and disposes it after; a selection's pre-grab is disposed when its job ends, whether the job ran, was suppressed or was refused by the closed queue, and in screen mode, when it is not the chosen monitor (D12), before that monitor is grabbed (`MenuPollTests.StaleFrameIsDisposed`, `CaptureEngineTests.EveryFrameIsDisposedAfterItsCapture`).

`ShieldedScreenCapture.Grab(monitor)` is the ONLY caller of `IMonitorCapture.Capture`:

```csharp
public PixelFrame Grab(MonitorDescriptor m) { using var _ = _shield.Take(); return _raw.Capture(m); }
```

`GdiMonitorCapture` is `internal sealed` in ShotAI.Platform; DI registers it only as `IMonitorCapture`, consumed only by `ShieldedScreenCapture`; the engine receives `IScreenCapture`. The source test (8.4) enforces this in the same spirit as `capture-shield.test.ts`.

DXGI Desktop Duplication: permitted by the fixed decisions but not used in v1; it could keep a latest frame for the menu path without polling (Q-CAP-14). `Windows.Graphics.Capture` is forbidden (INV-CAP-25).

### 7.8 Shield: `CaptureShield` (Core) and `DisplayAffinityProtection` (Platform)

```csharp
public sealed class CaptureShield
{
    readonly object _lock = new(); int _depth; bool _restore = true;       // true = excluded
    public CaptureShield(IWindowProtection p, ICaptureSettings s) { ... }
    public void ApplyRemoteVisibility(bool visible)                          // startup and Settings toggle
    { lock (_lock) { _restore = !visible; if (_depth > 0) return; _p.SetAllExcluded(!visible); } }
    public Releaser Take()
    { lock (_lock) { if (_depth == 0) { _restore = !_s.RemoteVisibleNow(); _p.SetAllExcluded(true); } _depth++; } return new Releaser(this); }
    public int DepthForTest { get { lock (_lock) return _depth; } }
    public bool ExcludedForNewWindow { get { lock (_lock) return _depth > 0 || _restore; } }
    public sealed class Releaser : IDisposable { int _released; public void Dispose() { if (Interlocked.Exchange(ref _released, 1) == 0) _owner.Release(); } }
    void Release() { lock (_lock) { if (--_depth == 0) _p.SetAllExcluded(_restore); } }
}
```

The Win32 call happens under the lock on purpose: two overlapping `Take` calls must not interleave "set false" after "set true". `DisplayAffinityProtection.SetAllExcluded(e)` walks `OwnWindowRegistry` and calls `CaptureExclusion.Apply(hwnd, e)` (`SetWindowDisplayAffinity`, `WDA_EXCLUDEFROMCAPTURE` or `WDA_NONE`) for each live HWND, skipping destroyed ones (`IsWindow == false`), logging a `false` result at warning with the Win32 error (IMPROVEMENT: Electron ignores the result). `OwnWindowRegistry.Register(hwnd)` (called by the App on `SourceInitialized`, before `Show`, and by 09's `WebView2PdfRenderer` right after it creates the never-shown PDF host popup, 09 7.7) applies `ExcludedForNewWindow` immediately (INV-CAP-7, EDGE-CAP-22). A layered (`AllowsTransparency`) WPF window must be verified to accept the affinity (Q-CAP-15).

Deadlock rule (correction found in verification). `SetWindowDisplayAffinity` requires only that "the window must belong to the current process" (Microsoft Learn); whether a call from a capture thread for a window owned by the UI thread is serviced synchronously by the UI thread is not documented (Q-CAP-15). If it is, the design above can deadlock: the capture thread holds `_lock` inside `SetAllExcluded` waiting on the UI thread, while the UI thread blocks on `_lock` in `Register` (reading `ExcludedForNewWindow`) or in `ApplyRemoteVisibility` (the Settings toggle). Required natively, regardless of the Q-CAP-15 answer:

1. `Register(hwnd)` on the UI thread first calls `CaptureExclusion.Apply(hwnd, excluded: true)` unconditionally WITHOUT taking the shield lock (fail-closed, exactly the existing `MainWindow.OnSourceInitialized` pattern), adds the HWND to the registry set (its own lock, never held across a Win32 call), then posts a reconcile to the thread pool: `lock (_lock) if (_depth == 0) Apply(hwnd, _restore)`.
2. Every runtime change of the setting reaches `ApplyRemoteVisibility` through `Task.Run`, never inline on the UI thread (ARCHITECTURE DL1). The caller is 11's `RemoteVisibilityApplier` (an `IAppStartup` subscribed to `ISettingsService.Changed`, which also re-applies on a rollback, INV-IPC-13); it must not `IUiDispatcher.Post` the call onto the UI thread. The one startup application (ARCHITECTURE 4.2 step 10) is the reasoned exception to DL1: it runs synchronously on the UI thread before any capture session exists, so no shield can be held. The lock is not uncontended, though: the pool reconciles that rule 1 posted for the windows registered at step 8 (main window, pill) take the same lock and call `SetWindowDisplayAffinity` under it. If the Phase B probe (Q-CAP-15) shows that a cross-thread `SetWindowDisplayAffinity` is serviced by the owning UI thread, such a reconcile could hold the lock while waiting on the UI thread that is blocked in step 10, so step 10 then also moves to `Task.Run` (the windows stay excluded until it runs, so the order still fails closed; ARCHITECTURE DL1, Q-ARCH-6). If that call ever moves after a session could start, it also goes through `Task.Run`.
3. No UI-thread code path takes `CaptureShield._lock` (ARCHITECTURE DL1, INV-CAP-29). `AllWindowsRegisteredTests` asserts the order; a `ShieldDeadlockTests` case (Windows) holds a shield on a worker while the UI thread registers a new window and toggles the setting, and must finish within 2 s.
4. If the Phase B probe shows that `SetWindowDisplayAffinity` must run on the owning UI thread (Q-CAP-15), the grab thread posts the change and waits for its completion with a bounded timeout, and on timeout skips the grab (fail closed: a missed step reported through `CaptureFailed`), never grabs unshielded (ARCHITECTURE DL2, Q-ARCH-6). The UI thread never waits on capture work in either direction.

This replaces Electron's "seed from `remoteVisibleNow()` at creation" (`RegionService.ts:90`) with protected-first then relaxed, which is the fail-closed order `src/main/main.ts:496-502` already uses for startup windows.

As built in WP-B5, rule 1's reconcile: the registry excludes the window, adds it, then raises its internal `Added` event on the registering thread (a throwing listener is logged and cannot fail the registration); `DisplayAffinityProtection` passes it on as `IWindowProtection.WindowAdded`; the shield, which subscribes when it is made, posts `Reconcile(hwnd)` to the pool (`Task.Run`), which takes the lock there and, with no shield held, gives that window the restore value through `SetExcluded`. A window registered before the shield exists (the main window, before step 9 resolves the applier) is covered by step 10's `ApplyRemoteVisibility`. `DisplayAffinityProtection` skips a handle that is not a window, checking again after a failed call so a window destroyed in between is skipped too, and logs a refusal at warning as `own window 0x<hwnd>: capture exclusion refused (Win32 error <n>)` or `own window 0x<hwnd>: capture exclusion could not be lifted (Win32 error <n>)`.

Q-CAP-15 and Q-CAP-22, measured on both Windows runners in WP-B5: `SetWindowDisplayAffinity` called from another thread of the process returns while the window's own thread pumps nothing, and the window, normal or `AllowsTransparency`, is out of the very next GDI read, five times out of five with no settle, and back at the same pixel count after (`GdiMonitorCaptureTests.AffinityDoesNotWaitForTheWindowsThread`, `ExcludedWindowContributesZeroPixels`, `LayeredWindowAcceptsAffinity`). So rule 4's marshaling is not built, and step 10 stays synchronous on the UI thread: no reconcile can hold the lock waiting for the UI thread. `ShieldDeadlockTests.RegisterAndToggleWhileShieldHeldCompletes` runs the rule end to end with the real registry, protection and shield.

As built in WP-B1: `DepthForTest` is `internal`, read by the Core tests through `InternalsVisibleTo`, so the shield's public surface is `ApplyRemoteVisibility`, `Take` (whose `Releaser` is the `IDisposable`) and `ExcludedForNewWindow`.

### 7.9 Own windows: `OwnWindowRegistry`

Thread-safe set of our top-level HWNDs (main, pill, overlays, dialogs). `PointHitsOwnWindow(x, y)`: for each registered HWND that `IsWindow`, `IsWindowVisible` and not `IsIconic`, take its visible rectangle and test `left <= x < right && top <= y < bottom`. The visible rectangle is `DwmGetWindowAttribute(DWMWA_EXTENDED_FRAME_BOUNDS)` for framed windows (a standard WPF window's `GetWindowRect` includes about 7 px of invisible resize border on Windows 10 and 11, which would swallow clicks just outside it), falling back to `GetWindowRect` when the DWM call fails (layered windows have no invisible border, so both agree). Physical px in PMv2. Callable from any thread without the UI dispatcher (IMPROVEMENT: no DIP conversion, no UI-thread hop). Electron's `getBounds()` semantics for framed windows were not verified; the pill and overlay are frameless, so the difference matters only for the main window and dialogs. `IsOwnWindow(hwnd)`: `GetWindowThreadProcessId(hwnd, out uint pid)` and compare `pid == (uint)Environment.ProcessId` (the function's RETURN value is the thread id, not the pid).

Added in WP-A13, the registration surface: `Register(hwnd)` applies the exclusion first (7.8 rule 1), then adds the window, and returns whether it was added; a window already in the set is left as it is, since its exclusion may have been relaxed on purpose since; a refusal is logged at warning as `own window 0x<hwnd>: capture exclusion refused (Win32 error <n>)` and the window is still added, so the shield keeps it in its walk. `Unregister(hwnd)` returns whether the window was in the set. `IsRegistered(hwnd)` is public too, because the App tests, which may not see Platform's internals (INV-ARCH-6), assert with it; the internal `Snapshot()` is the walk `DisplayAffinityProtection` will use. The pool reconcile of 7.8 rule 1 joins with `CaptureShield` in WP-B5; until then every registered window stays excluded (fail closed). Two App paths register: 03's `WindowRegistration`, from each window's `OnSourceInitialized`, and 03's show hook, which registers every top-level window of the UI thread before it is shown and unregisters it when it is destroyed (03 7.4.7).

As built in WP-B5: the queries are Platform's internal `OwnWindows : IOwnWindows` over the registry (7.1, corrected): a registered window counts while it is a window, visible and not minimized; its rectangle is `DWMWA_EXTENDED_FRAME_BOUNDS`, or `GetWindowRect` when DWM has none; `IsOwnWindow` compares the process id `GetWindowThreadProcessId` writes and refuses an id of 0 (`OwnWindowsTests`). The pool reconcile of 7.8 rule 1 is in (7.8, as built).

### 7.10 Divergences from Electron

| # | Change | Class | Justification |
|---|---|---|---|
| D1 | Own-window gate at mousedown, before double-click tracking, menu logic and the element query | IMPROVEMENT | Electron's own-window clicks already produce no step, but they still consumed a menu chain slot or disarmed a menu; and a UIA hit test into our own WPF provider marshals to our UI thread (macOS crashed on the equivalent, `ReviewFixTests.swift:38-55`). The `captureStep` guard stays as well. |
| D2 | Element query started only when a capture will be enqueued (after the collapse decision), still at mousedown on the dispatcher | IMPROVEMENT | A dropped double-click's query result is never used; saves a UIA round trip. |
| D3 | Title check `OWN_WINDOW_TITLES` dropped; pid check only | IMPROVEMENT | Exact in a single process; the title check false-positives on an Explorer folder named `shotAI` (EDGE-CAP-37). |
| D4 | `CaptureState.StepCount` = steps committed this session | IMPROVEMENT | The filename counter shows arbitrary numbers after orphan seeding (EDGE-CAP-49); matches macOS. The pill still flashes on increase. |
| D5 | `StepLanded` carries the step as persisted plus its landed index | IMPROVEMENT (the index); the renumbered `order` itself is REQUIRED parity, because Electron's payload already carries it (the store renumbers the same object in place, 2.6 table) | Consumers can place it without a reload (EDGE-CAP-26). |
| D6 | Grab failure (null step with a live session and no own-window suppression) raises `CaptureFailed` once per failure run: `A screenshot could not be captured. If this keeps happening, make sure the target is visible, then try again.` (Q-CAP-7) | IMPROVEMENT | Electron fails silently; macOS parity for "a long recording can't fail silently". A success clears the run. |
| D7 | Hook install failure fails `StartAsync` with `The global click listener could not be started. Restart shotAI and try again.`, and no session remains | IMPROVEMENT | No phantom session (EDGE-CAP-46). |
| D8 | Atomic session reservation: `StartAsync` sets `_starting` under the lock before any await; a second call for the same path awaits the first and returns its state; for another path throws the Electron message; `CaptureScreenshotAsync` throws `A recording is already in progress` while starting | IMPROVEMENT | TOCTOU (EDGE-CAP-45). |
| D9 | Project path comparison uses `Path.GetFullPath` plus `StringComparison.OrdinalIgnoreCase` | IMPROVEMENT | Windows paths are case-insensitive; Electron compares raw strings. |
| D10 | `shots/` and PNG paths confined, reparse points refused; start refused with `This project's shots folder resolves outside the project (it may be a symlink or junction). Recording was refused so screenshots aren't written elsewhere.` | IMPROVEMENT [SECURITY] | INV-CAP-23; macOS parity. |
| D11 | Orphan numbers parsed with `long.TryParse` and clamped to 1000000 (an unparseable or overflowing number ignores the file, D22) | IMPROVEMENT | EDGE-CAP-27. |
| D12 | Screen-mode menu selection uses the pre-grab only if it is the chosen monitor | IMPROVEMENT | EDGE-CAP-33; never capture a monitor the user did not pick. |
| D13 | Per-arm poll in-flight flag; poll as a cancellable `PeriodicTimer` task | IMPROVEMENT | EDGE-CAP-19; equivalent behavior, fewer leak paths. |
| D14 | Hook watchdog; hotkey `MOD_NOREPEAT`; hotkey failure logged | IMPROVEMENT | 7.4. |
| D15 | UIA on two MTA threads with `IUIAutomation2` timeouts; no 8192-byte JSON budget | IMPROVEMENT | Hang tolerance; the buffer was an FFI artifact (EDGE-CAP-43). |
| D16 | `captureSingle` not ported | ELECTRON-ONLY | No UI caller (Q-CAP-1). |
| D17 | No koffi, no Rust DLL, no DLL path search, no `process.platform` check | ELECTRON-ONLY | Replaced by in-process UIA COM (7.6). |
| D18 | No IPC target parsing | ELECTRON-ONLY | The engine validates its own inputs: `CaptureScreenshotAsync` throws `A screenshot needs an explicit target (screen, window, or area).` for `Auto`; a `Window` target without a window or an `Area` target without an area goes through the normal validation messages. |
| D19 | 32-bit hook coordinates | IMPROVEMENT | EDGE-CAP-47. |
| D20 | Own dialogs (Discard confirm) are registered windows | IMPROVEMENT | EDGE-CAP-39 (spec 03 builds them). |
| D21 | While `CaptureScreenshotAsync` is in flight, `StartAsync` throws `A recording is already in progress` for any path, and `Pause`, `Resume`, `StopAsync`, `DiscardAsync` do not touch the screenshot session (they return the idle state) | IMPROVEMENT | EDGE-CAP-51: Electron returns a phantom `recording` state or aborts the grab. |
| D22 | Orphan parse ignores (not clamps) numbers that overflow `long`, ASCII digits only | IMPROVEMENT | EDGE-CAP-27: Electron's float parse makes every capture after a 20-digit orphan collide; macOS parity. |
| D23 | `StartAsync` logs one warning when the target is `Window` without a window or `Area` without an area | IMPROVEMENT (log only) | EDGE-CAP-60. |
| D24 | Frame buffers are pooled and owned explicitly (7.7) | IMPROVEMENT | Avoids LOH churn that suspends the hook thread (Q-CAP-19). |

Everything else in section 2 is REQUIRED as written.

As built in WP-B2, for the divergences this WP owns:

- D4: `CaptureSession.Counter` is the filename counter and `Committed` the state's `StepCount` (`StepCountIsSessionCount`).
- D5: the store returns the manifest as written (01 7.8, corrected in WP-B2) and the engine finds its step there by id, so `StepLanded` carries the store's renumbered step and its index (`StepLandedCarriesRenumberedOrderAndIndex`).
- D6: raised for a recorded capture only; the screenshot throws its own message and raises nothing (INV-CAP-31). A landed step ends the run, and a new session starts a new one (`GrabFailureRaisesOncePerRun`).
- D7: logged at error as `mouse hook could not be installed (Win32 error <n>); the recording did not start`, with the session cleared before the message is thrown (`HookAttachFailureFailsStart`).
- D8: the reservation holds the pending start's path and task; a caller for the same path awaits that task and gets its state or its exception, and the reservation is released in a `finally` (`ConcurrentStartsCannotBothInstall`, `AFailedStartReleasesTheReservation`).
- D9: `Path.GetFullPath`, then `Path.TrimEndingDirectorySeparator`, compared `OrdinalIgnoreCase` (`SessionGuards`).
- D10: `shots/` is confined, created, then confined again, since a link can appear between the check and the create; each PNG path is confined again at write time (`ShotsReparsePointRefusesStart`, `ShotsSwappedMidSessionRefusesWrite`).
- D16: `ICaptureService` has no single-shot member.
- D21: the screenshot's session is `SessionKind.Screenshot`. `GetState` reports idle during it; `Pause`, `Resume`, `StopAsync` and `DiscardAsync` return idle and raise nothing; a start for any project and a second screenshot throw `A recording is already in progress` (`StartDuringScreenshotThrows`, `StopDuringScreenshotIsNoOp`).
- D23: the warning is `recording target mode=<mode> has no <window|area>: each capture falls back to the click monitor` (`IncompleteTargetLogsOneWarning`).
- D24: nothing to own yet. `PixelFrame` stays a plain buffer until WP-B5 pools the buffers in `GdiMonitorCapture`; the capture step then disposes the frames it grabbed and cropped after the encode (7.7).

As built in WP-B4, for the divergences this WP owns:

- D14: the watchdog, `MOD_NOREPEAT` and the hotkey warning, as 7.4 says (`WatchdogReinstallsRemovedHook`, `SecondRegistrationReportsFalse`).
- D19: `MSLLHOOKSTRUCT.pt` is two 32-bit integers, copied into the ring as they are (`SyntheticClickIsDelivered`).

As built in WP-B3, for the divergences this WP owns:

- D1: `OnMouseDown` returns before anything else when the point hits an own window: no element query, no double-click memory, no arm and no disarm (`OwnWindowClickDoesNotQueryElement`, `AnOwnWindowClickIsNoHalfOfADoubleClick`); the capture step's own guard stays (2.7.1).
- D2: the query starts after the double-click decision, for the three cases that enqueue a capture (`DoubleClickCollapsesChained` asserts the queries made).
- D12: path A in screen mode uses the pre-grab only when it is the chosen monitor, and otherwise grabs the chosen monitor at capture time; an unknown monitor id falls back to the click's monitor, as on the other paths (`ScreenModeMenuUsesChosenMonitor`, `AStaleMonitorIdSelectionUsesTheClickMonitor`).
- D13: `MenuArm` carries its own `Polling` flag (`StoppedPollDoesNotSuppressNextArm`), and its poll is the loop of 7.3 (corrected in WP-B3), cancelled through the arm's token when the arm is disposed: by a disarm, by the arm that replaces it, and by pause, resume, stop, discard and teardown, which all disarm; it ends by itself at expiry and after 32 frames (`MenuPollTests`).
- D24: the selection takes the polled frame under the lock, and a frame that arrives for a replaced arm lands on no arm (`MenuSelectionTakesPolledFrameOwnership`, `LateFrameDoesNotLandOnNewArm`). Disposing either waits for WP-B5's disposable frames, and `MenuPollTests.StaleFrameIsDisposed` moves there with them.

As built in WP-B5, for the divergence this WP owns:

- D24: the buffers come from `GdiMonitorCapture`'s pool and every frame has one owner at a time, as 7.7 (as built) lists; `StaleFrameIsDisposed` is in.

### 7.11 `CaptureEngine` algorithms (C# pseudo-code)

```csharp
// Dispatcher thread
void OnMouseDown(MouseDown e)
{
    CaptureSession? s; int gen;
    lock (_gate) { s = _session; gen = _generation; if (s is null || s.Paused || _tearingDown) return; }
    if (_own.PointHitsOwnWindow(e.X, e.Y)) return;                                   // D1
    var p = (e.X, e.Y); var now = _clock.NowMs();
    if (s.Single is { } single) { /* not ported, D16 */ return; }
    if (e.Button == MouseButton.Left)
    {
        bool dbl; lock (_gate) { var last = _lastLeftClick;
            dbl = last is { } l && now - l.At <= CaptureConstants.DoubleClickMs && WithinDist(p, l.Point, CaptureConstants.DoubleClickDist);
            _lastLeftClick = (now, p); }
        if (dbl) { _log.LogDebug("double-click: ignoring 2nd click at ({X},{Y})", e.X, e.Y); return; }
    }
    var element = _elements.ElementAtAsync(e.X, e.Y);                                 // D2: starts now
    if (e.Button == MouseButton.Right)
    {
        var owner = FocusedFrameBounds();                                             // sync, now
        Arm(new MenuArm(until: now + 30000, owner, lastPoint: p, chain: 0), gen);     // starts the poll
        Enqueue(Job(StepTrigger.Click, p, MouseButton.Right, element: element, gen: gen)); return;
    }
    MenuArm? arm; lock (_gate) arm = _menuArm;
    if (e.Button == MouseButton.Left && arm is not null && now < arm.Until && NearMenuPoint(p, arm.LastPoint))
    {
        (PixelFrame, MonitorDescriptor)? taken; lock (_gate) { taken = arm.Frame; arm.Frame = null; } // ownership moves to the job (7.7)
        var pre = taken ?? GrabClickMonitorSync(p);                                   // shielded, sync; null on failure (warn)
        int chain = arm.Chain + 1;
        if (chain < CaptureConstants.MaxMenuChain) Arm(new MenuArm(now + 6000, arm.OwnerBounds, p, chain), gen);
        else { _log.LogDebug("menu: chain limit (4) reached \u2014 disarming"); Disarm(); }   // exact Electron text
        LogSelection(e, usedPoll: taken is not null, pre);                           // the three 2.4.2 selection messages
        Enqueue(Job(StepTrigger.Click, p, MouseButton.Left, menuPopup: true, owner: arm.OwnerBounds, preGrab: pre, element: element, gen: gen));
        return;
    }
    if (arm is not null) LogDisarmReason(e, arm, now);
    Disarm();
    Enqueue(Job(StepTrigger.Click, p, e.Button, element: element, gen: gen));
}

double Sf(int x, int y) => _screen.FromPoint(x, y)?.ScaleFactor is double f && f > 0 ? f : 1;
bool WithinDist((int X,int Y) a, (int X,int Y) b, int logical) { var max = logical * Sf(a.X, a.Y); return Math.Abs(a.X-b.X) <= max && Math.Abs(a.Y-b.Y) <= max; }
bool NearMenuPoint((int X,int Y) p, (int X,int Y) last) { var sf = Sf(p.X, p.Y); return Math.Abs(p.X-last.X) <= 640*sf && Math.Abs(p.Y-last.Y) <= 680*sf; }
```

The capture step on the worker follows 2.6 exactly with these native specifics:

1. `lock`: if the session is null, paused, or `gen` is stale, return null.
2. `fg = _windows.Foreground()`; own guard unless `SkipOwnWindowGuard`: `fg is not null && fg.Pid == _own.ProcessId`, or the point hits an own window, returns null.
3. Right-click late owner fill under the lock (only if the current arm's `OwnerBounds` is null).
4. `element = job.Element ?? (point is null ? Task.FromResult<StepElement?>(null) : _elements.ElementAtAsync(...))`.
5. `grab` per 2.8 with `IScreenCapture` and `IImageCodec.Crop` (crop formulas from `CaptureGeometry`), D12 applied.
6. Null grab: await the element task (do not leak it), apply D6, return null.
7. `DownscalePolicy.Compute(w, h, _settings.CaptureScaleNow())` then `IImageCodec.Resize` and `EncodePng`, failing open to the unscaled encode (and to scale 1) on any exception.
8. `lock`: `SessionAlive(gen)` or return null; `order = ++s.StepCount` (the filename counter field).
9. `path = PathConfine.ConfineNoLinks(project, "shots/" + ShotNaming.Format(order), probe)`, a null result throwing the D10 message; write with `new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None)` (an existing file throws `IOException` with `HResult` `0x80070050` `ERROR_FILE_EXISTS`, the native `EEXIST`, surfaced through `CaptureFailed`). A store exception after this write leaves the PNG (EDGE-CAP-52): log its path at warning.
10. Build the step (2.6 table; `id = Guid.NewGuid().ToString("D")`, lowercase; `click.image` with `JsMath.Round`; `imageScale` omitted when exactly 1.0).
11. Insert or add through the store (spec 01), advance the cursor under the lock when it came from the cursor.
12. `lock`: if `SessionAlive(gen)`, `s.AddedStepIds.Add(step.Id)`.
13. Log (2.6 step 18, element name at debug only, Q-CAP-17); raise `StepLanded`, then `StateChanged`, unless `Broadcast == false`.

Poll loop per arm:

```csharp
async Task PollAsync(MenuArm arm, CancellationToken ct)
{
    using var timer = new PeriodicTimer(TimeSpan.FromMilliseconds(400)); int frames = 0;
    try {
    while (await timer.WaitForNextTickAsync(ct))                        // throws OperationCanceledException on Disarm
    {
        lock (_gate) { if (!ReferenceEquals(_menuArm, arm) || _session is null) return;
                       if (_clock.NowMs() >= arm.Until || _session.Paused) return; }
        if (frames >= 32) return;
        if (arm.Polling) continue;                                      // not counted
        var mon = _screen.FromPoint(arm.LastPoint.X, arm.LastPoint.Y) ?? Primary() ?? First();
        if (mon is null) continue;                                      // not counted
        arm.Polling = true; frames++;
        try { var f = await Task.Run(() => _screen.Grab(mon), ct);
              PixelFrame? drop = null;
              lock (_gate) { if (ReferenceEquals(_menuArm, arm)) { drop = arm.Frame?.Frame; arm.Frame = (f, mon); } else drop = f; }
              drop?.Dispose(); }                                      // outside the lock (7.7 ownership)
        catch (Exception ex) when (ex is not OperationCanceledException) { _log.LogWarning(ex, "menu poll capture failed:"); }
        finally { arm.Polling = false; }
    }
    } catch (OperationCanceledException) { /* disarmed, re-armed, paused or teardown: normal exit */ }
}
```

The loop is started with `_ = PollAsync(arm, arm.Cts.Token)` and must never fault: every exception other than cancellation is caught per tick, and cancellation ends the loop quietly, so no unobserved task exception reaches `TaskScheduler.UnobservedTaskException`. `Task.Run(..., ct)` does not abort a grab already running; a frame that completes after cancellation takes the stale-arm branch and is disposed.

`Disarm()` sets `_menuArm = null` and cancels the arm's `CancellationTokenSource`. Memory: a 4K frame is 33 MB; at most one current frame per arm plus one in flight.

Stop, discard, screenshot and start follow 2.2 exactly, with `_tearingDown = true` set before `Detach` and cleared after the session is cleared, and the capture worker drain implemented as "enqueue a sentinel job and await its completion".

Corrected in WP-B2: stop and discard hold `_stopping` for the drain instead, and never block `SessionAlive` (7.3). Step 6 does not await the element task: 2.6 step 8 returns at once, and awaiting held the serialized queue for up to the query's cap after each failed grab; a continuation observes the task's fault instead, so none goes unobserved. As built in WP-B2: step 8 increments `s.Counter`; step 11 moves the cursor to the requested index plus 1 (2.2.3), only while the session is alive; step 12 also counts the step in `Committed` (D4) and ends a D6 run; step 13 logs the info line with `el=(<controlType>)`, only for a non-empty name, and the name on its own debug line, `step #<n> el='<name>'(<controlType>)` (Q-CAP-17). A click suppressed by step 2 leaves the query it started at mousedown to finish, with its fault observed. Step 3 and D12 are WP-B3's.

Corrected in WP-B3: the sketch departs from Electron's order in two places. The selection's log line comes before the chain decision, so `menu: chain limit (4) reached \u2014 disarming` follows it (`:399-436`); and the re-arm reads the clock again after the click-time grab (`Date.now() + SUBMENU_FOLLOWUP_WINDOW_MS`, `:423-425`), so the 6 s run from after the grab (`TheSubmenuWindowStartsAfterTheClickTimeGrab`). The gate is WP-B2's (a recording, not paused, not stopping, not torn down) and the poll is 7.3's loop, not the `PeriodicTimer` shown.

As built in WP-B3: the mousedown path is `CaptureEngine.Menu.cs`. `Arm` refuses an arm whose session is no longer the recording it was made in, is paused, is stopping or is torn down, since a pause or a stop can land between the gate and the arm on another thread, and logs `menu: armed by right-click` only for an arm it installs (`AnArmForAPausedSessionIsDropped`, `AnArmForAStoppingSessionIsDropped`, `AnArmFromAnEndedSessionIsDropped`, `AnArmDuringTeardownIsDropped`). `Disarm()` takes `_menuArm` under the lock and disposes it outside, which cancels its token. Step 3 fills a missing owner from the step's foreground, its `FrameBounds` else its `WindowRect`, unfiltered like Electron's `focused` (`:1143-1152`), into whatever arm is current (`ALateOwnerFillFramesTheSelection`, `TheOwnerIsReadAtTheRightClick`, `OnlyARightClickFillsTheOwner`). Step 5's path A is `GrabMenuSelection`, with D12. The poll logs a monitor lookup that throws as it logs a failed grab, `menu poll capture failed:`, and skips the tick without counting, so the task never faults (`AFailedMonitorLookupIsLoggedAndTheNextTickGrabs`).

### 7.12 Persistence and encoding

- Store calls (spec 01 names and signatures, reached through `IProjectService`, R-ARCH-4; the engine never takes the concrete `ProjectStore`): `OpenProjectAsync(path)` (returns `OpenedProject(Dir, Manifest)`; `CaptureScreenshotAsync` returns its `Manifest`), `AddStepAsync(string projectPath, ProjectStep step)`, `InsertStepAtAsync(string projectPath, ProjectStep step, double? atIndex)`, `DeleteStepsAsync(string projectPath, IReadOnlyCollection<string> stepIds)`, `DeleteProjectAsync(path)`, plus `PathConfine.ConfineNoLinks(string dir, string rel, IPathProbe probe)` (returns null when the path escapes or crosses a reparse point) for the `shots/` directory at start and for every PNG path at write time. All store calls go through the store's serialized write queue, shared with UI edits. The engine passes the fixed or cursor index as an integer converted to `double?`; the store clamps again (2.2.3). Whether the native store renumbers the caller's `ProjectStep` instance in place (as Electron does) or a copy is spec 01's choice; the engine must build the `StepLanded` payload from the store's result or set `Order = index + 1` itself so the event carries the renumbered order either way (D5). Corrected in WP-B2: `AddStepAsync` and `InsertStepAtAsync` return `Task<ProjectManifest>`, the manifest as written, and the engine takes the landed step and its index from it by id, so it copies nothing of the store's renumbering. `OpenProjectAsync`'s folder is what every shot path is confined to; the state and the store calls keep the path as the caller gave it.
- `captureSettings` is never written (Q-CAP-12).
- `WicImageCodec`: CsWin32 WIC COM (`IWICImagingFactory` from `WicFactory` (7.1), which calls `CoCreateInstance(CLSID_WICImagingFactory)` once, `CreateBitmapFromMemory` with `GUID_WICPixelFormat32bppBGRA`, `CreateBitmapScaler` with `WICBitmapInterpolationModeFant`, PNG encoder `GUID_ContainerFormatPng`, frame pixel format `GUID_WICPixelFormat32bppBGRA` so the file is 8-bit RGBA like Electron's). Microsoft Learn states that since Windows 7 all in-box WIC codecs, PNG included, support the MTA, so the one `WicFactory` instance, created on an MTA thread, is usable from the capture worker's thread-pool (MTA) threads; per-image objects (bitmap, scaler, encoder, frame, `IWICStream`) are created and released inside one job and never shared. Byte-identical PNGs with Electron are not a goal; dimensions are. `IImageCodec` has no decode member; the same `WicImageCodec` class also implements 04's `IRenderCodec` (ARCHITECTURE 2.3), and the decode path that `IRenderCodec` extends (04 7.13) is fixed here (R-ARCH-21, ARCHITECTURE 9.1 item 2): (1) the magic-byte check (PNG: length >= 8 and bytes 0..3 `89 50 4E 47`; JPEG: length >= 3 and `FF D8 FF`, spec 01's `detectImage` rule), anything else throws before WIC is touched; (2) `IWICStream.InitializeFromMemory`, then `IWICImagingFactory.CreateDecoder(GUID_ContainerFormatPng or GUID_ContainerFormatJpeg, &GUID_VendorMicrosoftBuiltIn)`; the vendor argument is documented only as a preference, so the codec then reads `IWICBitmapDecoder.GetDecoderInfo` and `IWICComponentInfo.GetVendorGUID` and refuses a decoder whose vendor is not `GUID_VendorMicrosoft` (corrected in WP-A17: the built-in decoders report `GUID_VendorMicrosoft` as their vendor, and `GUID_VendorMicrosoftBuiltIn` is only the preference `CreateDecoder` takes; a check against it refused every decode on both Windows runners); (3) `IWICBitmapDecoder.Initialize(stream, WICDecodeMetadataCacheOnDemand)`, then `GetFrame(0)`. It never calls `CreateDecoderFromStream` or `CreateDecoderFromFilename`, which pick a codec by sniffing and can run any third-party codec installed on the machine; a failing magic check, `CreateDecoder`, vendor check or `Initialize` throws (fail closed). Orientation, color and pixel-format steps after `GetFrame(0)` are 04 7.13's. As built in WP-A17: steps (1) to (3), with 04 7.13's orientation, colour and pixel-format steps, are Platform's internal `WicDecoding`, landed with 05's report decoder, which `WicImageCodec` calls rather than repeats (one orientation implementation, 05 Q-REP-19).
- Crop is a row copy in Core (`PixelFrame` is a plain buffer), so crop geometry is testable on Linux.
- As built in WP-B5: `WicImageCodec.Crop` is `PixelFrame.CopyRect`. `Resize` copies the frame into an `IWICBitmap` (`CreateBitmapFromMemory`, 32bppBGRA) and scales it with Fant through `WicDecoding.Scale`. `EncodePng` uses Microsoft's PNG encoder (`CreateEncoder` with the `GUID_VendorMicrosoftBuiltIn` preference) over an `SHCreateMemStream` stream, with 32bppBGRA frames (an encoder that asks for another format is refused), so the file is 8-bit RGBA (Q-CAP-18) and decodes to the frame's own pixels. Every WIC object is released with its call (`WicScope`).

### 7.13 Disposal and exit

The exit order is ARCHITECTURE 4.5 (11 7.10, R-ARCH-10); capture owns steps 2 and 5 of it.

- `Teardown()` (idempotent; step 2 of `App.OnExit` on the UI thread, and called first by `SessionEnding`, 03 7.4.1): `Detach` triggers (synchronous, bounded by the 2000 ms join, PB-13), cancel the poll, stop the UIA threads (they are background threads). It does not wait for the capture queue (Electron parity). Store writes a running job has already queued are drained by the project queue's exit flush (step 3, `ShutdownFlush.Run(5 s)` over the project and settings queues), not by the engine.
- `Dispose()` (step 5: the container's synchronous `provider.Dispose()`; `App.OnExit` never calls `DisposeAsync`): `Teardown()` if it has not run, then complete the job channel and release the seams WITHOUT awaiting in-flight jobs (step 2 already stopped new input). It never needs the UI thread to be free, because it runs on the UI thread (ARCHITECTURE C5, DL4).
- `DisposeAsync()` (kept for tests and non-container owners): `Teardown()`, then complete the job channel and await the worker for up to 5000 ms. The container never calls it.
- `Composition.ContainerTests.NoAsyncOnlyDisposables` (11) fails if `CaptureEngine` implements only `IAsyncDisposable`.

As built in WP-B2: `Teardown` sets a flag that never clears and detaches the triggers; the poll and the UIA threads it also stops arrive with WP-B3 and WP-B5. `Dispose` is `Teardown` plus completing the channel, and releases nothing else, since the seams are the container's. A job still running stops at its next `SessionAlive` check, and an `OperationCanceledException` it throws after teardown is not reported. `DisposeAsync` waits up to 5 s for the worker and logs `capture worker did not stop within 5 s` at warning if it is still running (`DisposeIsSynchronousAndIdempotent`, `DisposeAloneTearsDown`, `NoSessionAfterTeardown`).

### 7.14 `NativeMethods.txt` additions

`SetWindowsHookEx`, `UnhookWindowsHookEx`, `CallNextHookEx`, `GetModuleHandle`, `GetMessage`, `PeekMessage`, `PostQuitMessage`, `PostThreadMessage`, `IsWindowEnabled`, `GetWindowInfo`, `GetClientRect`, `WindowFromPoint` and `GetAncestor` (Q-CAP-4 fallback), `DISPLAYCONFIG_SOURCE_DEVICE_NAME`, `DISPLAYCONFIG_TARGET_DEVICE_NAME`, `IWICBitmap`, `IWICBitmapFrameEncode`, `IWICStream`, `GUID_WICPixelFormat32bppBGRA`, `CLSID_WICImagingFactory` (the constant; the exact metadata name of the coclass is UNVERIFIED, CsWin32 reports unknown names at build time), `GetCurrentThreadId`, `MSLLHOOKSTRUCT`, `WM_*` mouse and hotkey constants, `RegisterHotKey`, `UnregisterHotKey`, `GetCursorPos`, `GetForegroundWindow`, `GetWindowThreadProcessId`, `OpenProcess`, `CloseHandle`, `QueryFullProcessImageName`, `GetFileVersionInfoSize`, `GetFileVersionInfo`, `VerQueryValue`, `GetWindowText`, `GetWindowTextLength`, `GetWindowRect`, `IsIconic`, `IsWindow`, `IsWindowVisible`, `EnumWindows`, `EnumChildWindows`, `GetWindow`, `GetWindowLongPtr`, `DwmGetWindowAttribute`, `EnumDisplayMonitors`, `GetMonitorInfo`, `MONITORINFOEXW`, `GetDpiForMonitor`, `QueryDisplayConfig`, `GetDisplayConfigBufferSizes`, `DisplayConfigGetDeviceInfo`, `GetDC`, `ReleaseDC`, `CreateCompatibleDC`, `CreateDIBSection`, `SelectObject`, `BitBlt`, `DeleteObject`, `DeleteDC`, `SetWindowDisplayAffinity` (present), `CoCreateInstance`, `CoInitializeEx`, `CUIAutomation`, `IUIAutomation`, `IUIAutomation2`, `IUIAutomationElement`, `IUIAutomationTreeWalker`, `WICImagingFactory`, `IWICImagingFactory`, `IWICBitmapScaler`, `IWICBitmapEncoder`, `GetThreadDpiAwarenessContext`, `GetAwarenessFromDpiAwarenessContext`, and the shared WIC decode entries of 7.12, listed here once for 04, 05 and 09 (R-ARCH-21): `GUID_ContainerFormatPng`, `GUID_ContainerFormatJpeg`, `GUID_VendorMicrosoftBuiltIn`, `GUID_VendorMicrosoft` (added in WP-A17, for the vendor check), `IWICBitmapDecoder`, `IWICBitmapDecoderInfo`, `IWICComponentInfo`, `WICDecodeOptions` (whether CsWin32 projects the `GUID_*` constants by these names is UNVERIFIED; if not, they are declared as `Guid` constants with their SDK values). As built in WP-B4: the hook and hotkey entries are in, as `GetModuleHandle`, `GetMessage`, `PeekMessage`, `PostQuitMessage`, `PostThreadMessage`, `MSLLHOOKSTRUCT`, `HC_ACTION`, `LLMHF_INJECTED`, `WM_MOUSEMOVE`, `WM_LBUTTONDOWN`, `WM_RBUTTONDOWN`, `WM_MBUTTONDOWN`, `WM_XBUTTONDOWN`, `WM_HOTKEY`, `WM_APP`, `RegisterHotKey`, `UnregisterHotKey`, `GetThreadDpiAwarenessContext` and `GetAwarenessFromDpiAwarenessContext`; `SetWindowsHookEx`, `UnhookWindowsHookEx`, `CallNextHookEx`, `GetCurrentThreadId` and `GetCursorPos` were already there for 03's show hook and 06's monitors. As built in WP-B5: the capture and encode entries are in, as `GetDC`, `ReleaseDC`, `CreateCompatibleDC`, `CreateDIBSection`, `SelectObject`, `BitBlt`, `GdiFlush` (added: the DIB's bits are read directly), `DeleteObject`, `DeleteDC`, `MONITORINFOEXW`, `QueryDisplayConfig`, `GetDisplayConfigBufferSizes`, `DisplayConfigGetDeviceInfo`, `DISPLAYCONFIG_SOURCE_DEVICE_NAME`, `DISPLAYCONFIG_TARGET_DEVICE_NAME`, `IsWindow`, `IsWindowVisible`, `DwmGetWindowAttribute`, `IWICBitmap`, `IWICBitmapEncoder`, `IWICBitmapFrameEncode` and `SHCreateMemStream` (added: the encoder's memory stream). No new NuGet package: capture uses CsWin32 (Platform, already pinned) and the foundation's `Microsoft.Extensions.Logging.Abstractions` (Core, ARCHITECTURE 3.2 and 3.5); UI Automation uses CsWin32 COM, not FlaUI (ARCHITECTURE I-6).

---

## 8. Tests

### 8.1 `src/main/capture-geometry.test.ts`

Purpose: pin the pure rectangle math and the auto classifier. Ports to **ShotAI.Core.Tests (Linux)**, class `ShotAI.Core.Tests.Capture.CaptureGeometryTests` and `AutoClassifierTests`.

| Group | Case | Expected | Line |
|---|---|---|---|
| unionRect | bounds both rects | `(0,0,10,10) ∪ (5,5,10,10) = (0,0,15,15)` | `:5` |
| unionRect | contained rect | `(0,0,100,100) ∪ (40,40,10,10) = (0,0,100,100)` | `:13` |
| clickBox | 1240 px box at scale 1 | `clickBox((800,600), 1) = (180,-20,1240,1240)` | `:24` |
| clickBox | scales half-size | `clickBox((1000,1000), 1.5) = (70,70,1860,1860)` | `:27` |
| clickBox | 0 or NaN scale is 1 | `clickBox((620,620), 0) = (0,0,1240,1240)`; add `double.NaN` natively | `:30` |
| captureModeFor | unknown focus | `null` gives Fullscreen (the `undefined` case collapses to `null` in C#) | `:36` |
| captureModeFor | desktop | `Windows Explorer` / `Program Manager` gives Fullscreen | `:40` |
| captureModeFor | taskbar / tray | `Windows Explorer` with `''` and `'   '` give Region | `:43` |
| captureModeFor | shell hosts | `SearchHost`/`Search`, `StartMenuExperienceHost`/`Start` give Region | `:47` |
| captureModeFor | normal app | `Notepad`/`Untitled`, `Windows Explorer`/`Documents` give Window | `:51` |
| cropRect | contained | `(100,100,200,150)` unchanged on a 1920 x 1080 monitor at 0,0 | `:59` |
| cropRect | overflow right/bottom | `(1800,1000,400,400)` gives `(1800,1000,120,80)` | `:62` |
| cropRect | secondary monitor origin | mon `(1920,0,1920,1080)`, region `(1900,50,100,100)` gives `(0,50,80,100)` | `:65` |
| cropRect | never zero | `(5000,5000,10,10)` gives width and height at least 1 | `:73` |

### 8.2 `src/main/capture-shield.test.ts`

Purpose: (1) the routing invariant, asserted from source, that every grab is shielded; (2) the shield's restore value, re-entrancy and toggle deferral with mocked windows and settings.

| Group | Case | Port | Target | Line |
|---|---|---|---|---|
| every screen grab is shielded | both helpers exist and use `shieldOwnWindows()`, `finally`, `release()` | ports as a source test | `CaptureFunnelSourceTests.ShieldedCaptureUsesUsingScope` (asserts `ShieldedScreenCapture.cs` contains `_shield.Take()` inside a `using`) | `:38` |
| | `captureImage`/`captureImageSync` only inside the helpers | ports as a source test | `CaptureFunnelSourceTests.OnlyFunnelReadsScreenPixels`: `BitBlt(` appears only in `GdiMonitorCapture.cs`; `IMonitorCapture` is referenced only by `ShieldedScreenCapture.cs`, `GdiMonitorCapture.cs`, `PlatformCaptureRegistration.cs` and test fakes; `.Capture(` on an `IMonitorCapture` appears only in `ShieldedScreenCapture.cs` | `:48` |
| | menu paths routed through helpers | ports: the engine has no `IMonitorCapture` dependency at all | `CaptureFunnelSourceTests.EngineDependsOnlyOnShieldedCapture` (reflection: `CaptureEngine` constructor parameters contain no `IMonitorCapture`) | `:62` |
| shieldOwnWindows | protects, restores to visible when setting on (`[[true],[true]]` then `[[true,false],[true,false]]`) | Linux | `CaptureShieldTests.RestoresToVisibleWhenSettingOn` | `:99` |
| | no-op pair when off (`[true,true]`) | Linux | `.NoOpPairWhenSettingOff` | `:109` |
| | skips destroyed windows | Linux (fake protection with a destroyed flag) plus Windows (`DisplayAffinityProtectionTests.SkipsDestroyedHwnd`) | `.SkipsDestroyedWindows` | `:116` |
| re-entrant | inner release does not un-protect (`[true]`, `[true]`, `[true,false]`) | Linux | `.InnerReleaseKeepsProtection` | `:131` |
| | restores exactly once at any depth; depth back to 0 | Linux | `.RestoresOnceAtAnyDepth` | `:147` |
| | double release idempotent; depth stays 1 | Linux | `.DoubleReleaseIsIdempotent` | `:154` |
| | balanced pair leaves depth 0 | Linux | `.BalancedPairLeavesDepthZero` | `:168` |
| toggling during a grab | defers to release (`[true]`, `[true]`, `[true,false]`) | Linux | `.TogglingDuringGrabDefersToRelease` | `:180` |
| | mid-grab switch to not visible restores on (`[true,true]`) | Linux | `.MidGrabSwitchToNotVisibleRestoresProtection` | `:191` |
| applyRemoteVisibility | visible means protection off (`[false]`) | Linux | `.ApplyVisibleClearsProtection` | `:204` |
| | not visible means on (`[true]`) | Linux | `.ApplyNotVisibleSetsProtection` | `:209` |

The fake `IWindowProtection` records `SetAllExcluded` calls per fake window exactly like the vitest mock's `p` arrays.

Corrected in WP-B1: `EngineDependsOnlyOnShieldedCapture` is `CaptureFunnelSourceTests.OnlyTheFunnelHoldsTheRawCapture`, a reflection check of every Core type's constructors, fields, properties and methods, so `CaptureEngine` is covered the moment WP-B2 adds it, and no other type can take an `IMonitorCapture` either. As built in WP-B1: `OnlyFunnelReadsScreenPixels` also lets `CaptureSeams.cs`, the interface's declaration, name `IMonitorCapture`; it refuses the other screen reads (`StretchBlt`, `PrintWindow`, `CopyFromScreen`, DXGI's `DuplicateOutput`) outside `GdiMonitorCapture.cs` as well as `BitBlt`, and the name `GdiMonitorCapture` outside its own file and `PlatformCaptureRegistration.cs`. `ShieldedCaptureUsesUsingScope` requires the funnel's one raw read to follow `using var _ = _shield.Take();` in `Grab`. Every source rule is also run on a changed copy, to prove it can fail. `SkipsDestroyedWindows` also covers a window destroyed while the shield is held. The fakes are in `Capture/CaptureFakes.cs`.

### 8.3 `src/main/click-caption.test.ts`

Purpose: pin caption phrasing. Ports to **ShotAI.Core.Tests (Linux)**, class `ClickCaptionsTests`.

| Group | Case | Expected | Line |
|---|---|---|---|
| controlWord | known types | `Button`→`button`, `SplitButton`→`button`, `Hyperlink`→`link`, `Edit`→`field`, `ComboBox`→`dropdown`, `ListItem`→`item` | `:13` |
| controlWord | unknown / null | `MenuItem`, `Whatever`, `null` give `''` (the `undefined` case collapses to `null`) | `:21` |
| buildClickCaption | named control with noun | `Click 'OK' button in Notepad`; `Click 'Docs' link in Chrome` | `:30` |
| | MenuItem as selection | `Select 'Copy' in Notepad` | `:34` |
| | right-click named | `Right-click 'File' button in Notepad` | `:37` |
| | unknown type omits noun | `Click 'Thing' in Notepad`; `Right-click 'Thing' in Notepad` (Custom) | `:40` |
| | no name fallbacks | `Click in Notepad`; `Right-click in Notepad`; `Select from context menu in Notepad`; `Click in Notepad` for `(null, 'Button')` | `:44` |

Extend with every row of 2.9.1 (theory data) and the hotkey caption cases (`Capture: Notepad`, `Capture: screen` for null, `Capture: ` for an empty title).

### 8.4 New tests (native only)

**ShotAI.Core.Tests (Linux):**

| Class | Cases |
|---|---|
| `JsMathTests` | exercises `ShotAI.Core.Json.JsMath` (01's type, R-ARCH-1; its own tests are 01's `Json/JsHelpersTests`) with the capture cases: `RoundMatchesJavaScript`: `Round(0.5)=1`, `(1.5)=2`, `(2.5)=3`, `(-0.5)=0` (JavaScript gives `-0`, equal to `0` as a double), `(-1.5)=-1`, `(-2.5)=-2`, `(1.4999)=1`, `(0.49999999999999994)=0`, `(4503599627370497)=4503599627370497`; `NoMathRoundInCapture` (source scan of `ShotAI.Core/Capture`, AC-CAP-4; the Core-wide `RS0030` ban of ARCHITECTURE 14.9 is the primary guard); `NoJsMathCopyInCapture` (no type named `JsMath` is declared outside `ShotAI.Core.Json`) |
| `CaptureGeometryTests` (extra) | area crop quirk: mon `(0,0,1000,600)`, area `(-50,10,300,200)` gives `(0,10,300,200)` while `CropRect` gives `(0,10,250,200)`; region crop centered, shifted at each edge, box larger than monitor clamps to monitor size; scale 1.25 and 1.5 boxes |
| `DownscalePolicyTests` | width or height below 2 passthrough; 1920 x 1080 at 0.85 gives 1632 x 918, scale 0.85; 1200 x 700 gives targetW 1100, `targetH = round(700 * 1100 / 1200) = 642`; 300 x 200 unchanged; `captureScale` 1 unchanged; 2001 x 1000 at 0.85 gives width 1701 and scale exactly 1701/2001 (macOS `downscaleContract`, `CaptureEngineTests.swift:418-430`); 4000 x 3000 at 0.5 gives floor-bound max(0.5, 0.275) = 0.5, 2000 x 1500; codec throws gives original and scale 1 |
| `ShotNamingTests` | `Format(1)="step-0001.png"`, `Format(12345)="step-12345.png"`; parse `STEP-0007.PNG`=7, `step-7.png`=7, `step-.png` null, `step-0007.jpg` null, `step-0007.png.tmp` null, `step-9223372036854775807.png` clamps to 1000000, `step-99999999999999999999.png` null (overflows `long`, ignored, D22), `step-\u0661.png` (Arabic-Indic digit one) null (ASCII digits only); seed = max(manifest count, max parsed); with only `step-9223372036854775807.png` present the first capture is `step-1000001.png` (macOS `ReviewFixTests.swift:59-70`) |
| `UiaControlTypesTests` | all 41 names by id; 0 and 50041 give `Unknown`; allowlist is exactly the 15 ids |
| `ElementMappingTests` | actionable named gives available true; non-actionable hit gives name null, controlType of hit, bounds of hit; empty name gives unavailable; failure gives the unavailable literal in the step |
| `AutoClassifierTests` | 8.1 rows plus `SearchHost.exe`, `Windows Shell Experience Host`, `TextInputHost`, `Cortana` give Region; `windows explorer` (lowercase) with title `Program Manager` gives Window (case-sensitive rule) |
| `CaptureEngineTests` (fakes for every seam, fake clock) | macOS list ported: `HotkeyInAutoModeCropsToForegroundWindow`, `ClickImageCoordinatesFollowSchema`, `ImageScaleOmittedWhenOne`, `AreaModeCropsExactly`, `ScreenModeKeepsChosenMonitor`, `WindowModeCropsToResolvedWindow`, `UnresolvableWindowFallsBackToMonitor`, `MinimizedWindowFallsBackToMonitor`, `AutoShellHostGetsRegionCrop`, `ElementNamesFlowIntoCaptions`, `PillClicksCreateNoSteps`, `ForegroundOwnWindowSuppressesStep`, `DoubleClickCollapsesChained`, `RightClickThenNearbyLeftIsMenuSelection` (owner `(100,80,700,450)` union click box), `FarLeftClickDisarms`, `MiddleClickDisarms`, `MenuChainIsBoundedAtFour`, `ExpiredMenuWindowIsNotASelection` (advance 30001 ms), `SubmenuWindowIsSixSeconds`, `ProximityScalesWithMonitorFactor`, `ScreenModeMenuUsesChosenMonitor` (D12), `PauseSuppressesQueuedBacklog`, `SessionGuards`, `ConcurrentStartsCannotBothInstall`, `FilenameCounterSeedsPastOrphans`, `CollisionFailsLoudlyAndLeavesOriginalBytes`, `FailedWriteBurnsTheNumber`, `DiscardDeletesExactlyThisSessionsSteps`, `DiscardDeletesWholeNewProject`, `DiscardOfAppendedSessionKeepsProject`, `StateFlagMatchesDiscardPredicate`, `StopDrainsInFlightCaptures`, `CaptureBailsWhenSessionClearedMidFlight` (no `step-0000.png`), `InsertSessionLandsStepsAtCursorInOrder`, `ScreenshotInsertsAtIndexWithoutStepEvent`, `ScreenshotValidatesWindowBeforeHiding`, `ScreenshotValidatesAreaBeforeHiding`, `ScreenshotRejectsAuto`, `ScreenshotWaits350MsAfterHide`, `ScreenshotSkipsOwnWindowGuard`, `ScreenshotNullGrabThrowsExactMessage`, `RecordingChangedSequence` (start, stop, discard, screenshot), `StateChangedEmittedAfterStepEvent`, `StepEventFiresAfterPersist`, `ErrorIsSurfacedAndQueueContinues`, `GrabFailureRaisesOncePerRun` (D6), `ElementTimeoutDegradesToUnavailable`, `OwnWindowClickDoesNotQueryElement` (D1), `HookAttachFailureFailsStart` (D7), `ShotsReparsePointRefusesStart`, `HotkeyUsesPrimaryMonitor`, `StepCountIsSessionCount` (D4), and (added in verification) `StepLandedCarriesRenumberedOrderAndIndex` (D5), `StartDuringScreenshotThrows` and `StopDuringScreenshotIsNoOp` (D21, EDGE-CAP-51), `StoreFailureAfterWriteSurfacesErrorAndKeepsPng` (EDGE-CAP-52), `ScreenshotErrorsAreThrownNotRaised` (EDGE-CAP-58), `IncompleteTargetLogsOneWarning` (D23, EDGE-CAP-60), `AreaTargetWithoutAreaCapturesClickMonitor`, `MenuSelectionTakesPolledFrameOwnership` (7.7), `LogLineFormatMatchesElectron` (2.6 step 18: ` right` over ` menu-select`, ` (insert@n)` only for a fixed index, `el=` only with a name, `@0.85x` two decimals), and (added in consolidation) `DisposeIsSynchronousAndIdempotent` (R-ARCH-10: `Dispose()` after `Teardown()` returns without awaiting an in-flight job, a second call is a no-op), `EventsGoThroughEventRaiser` (a throwing `StepLanded` handler is logged and the `StateChanged` handler still runs, T5), `JobFailureMessageUsesUserMessage` (an `IOException` message is passed verbatim, an `InvalidOperationException` becomes `UserMessage.Generic`), `ThrownMessagesAreShotAIExceptions` (every 2.15 string, D7, D10 and D18 message is thrown as a `ShotAIException`) |
| `MenuPollTests` | `PollsEvery400Ms`, `FirstFrameNotBefore400Ms`, `LateFrameDoesNotLandOnNewArm`, `StoppedPollDoesNotSuppressNextArm`, `PollStopsAfter32Frames`, `SkippedTicksDoNotCountFrames`, `PollStopsOnPause`, `PollStopsOnExpiry`, `SelectionUsesPolledFrame`, `SelectionWithoutFrameUsesSyncGrab`, `CancelledPollDoesNotFault` (no unobserved exception after disarm), `StaleFrameIsDisposed` |
| `CaptureShieldTests` | 8.2 plus `RegisterWhileShieldHeldStartsExcluded`, `RegisterSeedsFromSetting`, `ConcurrentTakesFromManyThreads` (1000 parallel take and release pairs end at depth 0 with the last call equal to the restore value) |
| `ShieldedCaptureTests` | `EveryGrabTakesAndReleasesTheShield`, `ThrowingGrabStillReleases` |
| `CaptureFunnelSourceTests` | 8.2 source scans plus `NoGraphicsCaptureUsage` (no `Windows.Graphics.Capture` string under `dotnet/src`); the scans cover every file under `dotnet/src` and `dotnet/tools`, with an explicit allowlist (in the test, reviewed in PRs) for the `ShotAI.ProtectionProbe` diagnostic, which must read raw pixels (Electron's scan covered only `CaptureController.ts`, 2.16) |
| `Architecture.CoreReferencesTests` (owned by spec 11) | Core references no Windows assembly (INV-CAP-24, INV-ARCH-1); this spec adds no separate architecture test class |

As built in WP-B1: new `CaptureConstantsTests` reads the named and inline numbers of section 3 from `CaptureController.ts`, `capture-geometry.ts`, `element-locator.ts` and `lib.rs`, and checks the native ones against the table. `CaptureShieldTests` adds `TheRestoreValueIsLatchedFromTheSettingAtTheFirstTake`; its `RegisterWhileShieldHeldStartsExcluded` and `RegisterSeedsFromSetting` are the shield's half of INV-CAP-7 (`ExcludedForNewWindow`), while the registry's half stays `OwnWindowRegistryTests` (WP-B5); `ConcurrentTakesFromManyThreads` also checks that every holder sees the windows excluded and that excludes and restores alternate. `ShieldedCaptureTests` adds the half-open `FromPoint` cases and a grab inside a held shield. `NoGraphicsCaptureUsage` reads every file under `dotnet/src` and `dotnet/tools`, not only C#, for `Windows.Graphics.Capture` and `GraphicsCapture`. `JsMathTests.NoMathRoundInCapture` also refuses `MathF`, `double`, `float`, `decimal` and `Half` rounding and a static import of `System.Math`, and requires the folder to call `JsMath.Round`. The JavaScript edges are pinned: `JsString.Trim` blanks a U+FEFF title and not a U+0085 one, U+0130 and U+017F do not fold, a Turkish culture changes nothing, a 5000 x 1 strip is not downscaled, a portrait's floor is on its height, a blank title outside Explorer is a window, and crops are checked on a monitor below the primary and for regions above or wholly off the monitor. New `AsciiCaseTests` pins the fold itself: A to Z, and no other character.

As built in WP-B2: `CaptureEngineTests` is one partial class in five files (`.Sessions`, `.Grabs`, `.Persistence`, `.Screenshot`, `.Events`) over `EngineHarness`, which has a fake for every seam, a manual clock, and the real `ProjectStore` in a temp folder. `OwnWindowClickDoesNotQueryElement` is `PillClicksCreateNoSteps`, which asserts that no element query ran. Beyond the list, 67 cases, most of them added for the mutation check: `ACancellationAfterTeardownIsNotLogged`, `ACancelledJobIsLoggedButNotRaised`, `ACancelledScreenshotGrabsNothingAndEndsIdle`, `ACancelledStartStartsNothing`, `AClickAfterTeardownQueriesNothing`, `AClickDuringAScreenshotIsDropped`, `ADanglingShotsLinkRefusesStart`, `ADiscardCleanupFailureIsSwallowed`, `ADownscaledShotRecordsTheRatioApplied`, `AFailedAreaGrabFallsThroughToTheMonitor`, `AFailedCropFallsThroughToTheMonitor`, `AFailedGrabAfterTeardownIsNotReported`, `AFailedRegionGrabFallsThroughToTheMonitor`, `AFailedStartFailsItsWaiterToo`, `AFailedStartReleasesTheReservation`, `AFolderNamedLikeAShotSeedsTheCounter`, `AForegroundWithoutFrameBoundsIsCroppedToItsWindowRect`, `AHotkeyWhilePausedStaysIgnoredAfterResume`, `AMiddleOrOtherClickRecordsItsButton`, `AMinimizedForegroundWindowCapturesTheMonitor`, `AnAreaIsGrabbedFromTheMonitorItIsOn`, `AnEmptyElementNameIsNotLogged`, `ANewSessionStartsANewFailureRun`, `AnOwnWindowOverTheClickByCaptureTimeSuppressesIt`, `AnUnlistableShotsFolderSeedsFromTheStepCount`, `AParkedForegroundWindowCapturesTheMonitor`, `AShotsLinkThatAppearsAtTheCreateRefusesStart`, `ASlowCaptureLogsItsTiming`, `AStaleClickLandsInNoLaterSession`, `ASuppressedCaptureBurnsNoNumber`, `ATitleAloneIsNotOwnWindow`, `AutoDesktopIsFullscreen`, `AutoWindowWithoutAFrameCapturesTheMonitor`, `AWindowHangingOffTheLeftEdgeIsCroppedOnTheClickMonitor`, `AWindowIsGrabbedFromTheMonitorItIsOn`, `CaptureBailsWhenSessionClearedDuringTheElementQuery`, `CaptureSettingsIsNeverWritten`, `DiscardWithNothingCapturedDeletesNothing`, `DisposeAloneTearsDown`, `EverySeamIsRequired`, `HostileOrphansDoNotBreakTheCounter`, `IdleStopAndDiscardOnlyReportIdle`, `InsertAtIsClamped`, `ListTargetsFiltersAndNames`, `NoMonitorIsAFailedGrab`, `NoSessionAfterTeardown`, `PauseAndResumeReportTheState`, `PauseWithNoSessionReportsIdle`, `RapidClicksLandInOrder`, `ScreenshotAreaTargetWithoutAnAreaIsOffScreen`, `ScreenshotIndexIsClampedAndSeeded`, `ScreenshotOfAnAreaOverlappingAMonitor`, `ScreenshotRefusesALinkedShotsFolderFirst`, `ShotsSwappedMidSessionRefusesWrite`, `StaleMonitorIdCapturesTheClickMonitor`, `StartInstallsTheSessionInOrder`, `StartWhileAScreenshotOpensItsProjectThrows`, `StartWithoutTriggersAttachesNothing`, `StepLandedIndexIsWhereTheStoreLandedIt`, `TeardownDuringAScreenshotHidesNothing`, `TeardownDuringAStartLeavesNoSession`, `TheEventCarriesThePathAsGiven`, `TheMessagesAreElectrons`, `TheNativeMessagesAreTheSpecs`, `TheStepHasElectronsKeys`, `TheStepRecordsTheForegroundWindow`, `TheStepRecordsTheMonitorItGrabbed`; plus new `TimeProviderCaptureClockTests`. The menu cases of the list (`DoubleClickCollapsesChained` to `ScreenModeMenuUsesChosenMonitor`, `MenuSelectionTakesPolledFrameOwnership`) and `MenuPollTests` are WP-B3's.

As built in WP-B3: the menu cases are `CaptureEngineTests.Menus.cs` (32 methods, 48 cases). Every name of the list is there: `ExpiredMenuWindowIsNotASelection` checks 29999 and 30000 ms, the boundary itself, rather than 30001; `ProximityScalesWithMonitorFactor` and `DoubleClickDistanceScalesWithTheMonitor` also pin Electron's `?? 1`, which keeps a factor of 0; and `OwnWindowClickDoesNotQueryElement` now exists beside WP-B2's `PillClicksCreateNoSteps`, for D1 with the menu. Beyond the list: `AFailedOwnerReadArmsWithoutAnOwner`, `AFailedScaleLookupCountsAsOne`, `ALateOwnerFillFramesTheSelection`, `AMenuCropFailureHasNoFallback`, `ANewSessionForgetsTheLastLeftClick`, `ANewSessionStartsUnarmed`, `ARightClickReArmsFromScratch`, `ASelectionIsFramedByThePickedWindowOrTheArea`, `ASelectionWithNoFrameWarnsAndGrabsLate`, `AStaleMonitorIdSelectionUsesTheClickMonitor`, `AWindowModeSelectionWithNoFrameGrabsTheWindowsMonitor`, `AnOwnWindowClickIsNoHalfOfADoubleClick`, `DoubleClickDistanceScalesWithTheMonitor`, `OnlyARightClickFillsTheOwner`, `PauseAndResumeDisarmTheMenu`, `TheOwnerCarriesThroughTheChain`, `TheOwnerIsReadAtTheRightClick`, `TheProximityGateIs640By680`, `TheReachIsFromTheLastSelection`, `TheSelectionBoxScalesWithTheMonitor`, `TheSubmenuWindowStartsAfterTheClickTimeGrab`. `MenuPollTests` (22 methods, 25 cases) has every case of the list but `StaleFrameIsDisposed`, which needs D24's disposable frames and moves to WP-B5 with them, and adds `AFailedMonitorLookupIsLoggedAndTheNextTickGrabs`, `AFailedPollGrabIsLoggedAndTheNextTickGrabsAgain`, `AnArmDuringTeardownIsDropped`, `AnArmForAPausedSessionIsDropped`, `AnArmForAStoppingSessionIsDropped`, `AnArmFromAnEndedSessionIsDropped`, `AnOffScreenSelectionGrabsThePrimaryAtMousedown`, `OffEveryMonitorThePollGrabsThePrimary`, `ThePollFollowsTheArmPoint`, `TheSessionsEndEndsThePoll`, `TicksWithNoMonitorDoNotCountFrames`. The harness clock never ends a poll delay on its own: `MenuPollTests.TickAsync` moves it one interval once the poll waits, then waits until the poll waits again with no grab in flight, so no test sleeps on the engine's time; `EngineHarness.ClickAsync` moves the clock 1 s before each click, so WP-B2's cases never collapse into double-clicks. The engine's test hooks are `ArmForTest` and `LastPollForTest`. New `MenuArmTests` (4 cases) pins the arm's lifetime: disposing it cancels its token once, and the token taken at construction stays readable after.

As built in WP-B5: `MenuPollTests.StaleFrameIsDisposed` is in, with `TheArmsFrameIsDisposedWithTheArm` (a far click, pause, stop and dispose), so AC-CAP-2 is met in full. New `CaptureEngineTests.Frames.cs` (8 methods, 12 cases): every path's frames disposed and encoded while live, a failed crop, the screenshot, a selection's polled frame, an unused pre-grab disposed before the chosen monitor's grab, a late selection grab, a queued selection that never runs and one the closed queue refuses. `CaptureShieldTests` adds `TheShieldListensForNewWindows`, `ANewWindowIsRelaxedWhenTheSettingAllows`, `ANewWindowStaysExcludedBeforeTheSettingIsApplied`, `AReconcileDuringAGrabWaitsForTheRelease`, `RegisteringNeverWaitsForTheShieldsLock` and `TheReconcileRunsOnThePool`. New `PixelFrameTests` (7 methods, 16 cases), `FramePoolTests` (10 methods, 14 cases) and `OpaqueTests` (4 methods, 8 cases); `AddShotAICoreTests.RegistersTheShieldAndTheFunnel`. The harness's fake screen and codec record the frames they make, and the fake codec refuses a disposed one.

**ShotAI.Platform.Tests (Windows only):**

| Class | Cases |
|---|---|
| `MouseHookTests` | `SyntheticClickIsDelivered` (`SendInput` left down at a known point, received with physical coordinates on a 150% monitor), `InjectedClicksAreDelivered`, `EveryButtonMaps` (left, right, middle, X1, X2 to Other), `CallbackIsAllocationFree` (`GC.GetAllocatedBytesForCurrentThread` on the hook thread unchanged across 1000 synthetic events), `WatchdogReinstallsRemovedHook` (test hook: force-unhook, move the cursor, expect reinstall within 4 s), `DetachIsIdempotentAndJoins` |
| `HotkeyTests` | `RegistersCtrlShiftS`, `SecondRegistrationReportsFalse` (register the chord in the test first), `UnregisteredOnDetach` |
| `ShieldDeadlockTests` | `RegisterAndToggleWhileShieldHeldCompletes` (7.8 deadlock rule, 2 s bound) |
| `GdiMonitorCaptureTests` | `FrameMatchesMonitorSize`, `AlphaIsOpaque`, `ExcludedWindowContributesZeroPixels` (magenta WPF window with the affinity set: 0 magenta px; cleared: baseline restored), `LayeredWindowAcceptsAffinity` (Q-CAP-15) |
| `WindowInfoTests` | `ExplorerFileDescriptionIsWindowsExplorer`, `FallsBackToExeName` (a test exe without version info), `ForegroundOfOwnWindowHasOwnPid`, `ListFiltersToolWindowsAndCloaked`, `ListDropsDisabledAndCaptionlessWindows`, `UwpAppKeepsFrameHostPid` (EDGE-CAP-53), `ResolveByIdThenPidTitleThenPid`, `IsOwnWindowUsesOutPid` |
| `UiaElementLocatorTests` | host a WPF test window (on its own STA thread, never the test's UIA thread) with a `Button` containing a `TextBlock`: clicking the text resolves `Button` with its name; a `TextBox` resolves `Edit`; a plain `TextBlock` resolves unavailable with `Text`; a provider that sleeps 2 s times out at 600 ms (`HungProviderTimesOut`); `StaleRequestsAreDroppedWhenBothThreadsHang` (7.6 deadline) |
| `WicImageCodecTests` | `PngIsRgba8`, `ResizeDimensions`, `CropCopiesRows` |
| `DpiAwarenessTests` | `ProcessIsPerMonitorV2` on hook, dispatcher and worker threads |

As built in WP-B4: `MouseHookTests` has the six cases of the list and `ClicksArriveInOrder`, `MovesAreNotClicks`, `TheCallbacksRunOnTheDispatcherThread`, `AnAttachAfterADetachStartsANewThread`, `AFailedInstallThrowsTriggerException` and `ASecondAttachIsRefused`. Every click goes to a small topmost window of the test's own (`Support/ClickTarget`), through `SendInput` absolute moves, so the hook sees every cursor move and the watchdog never reinstalls without cause; `SyntheticClickIsDelivered` runs at the runner's 100%, so its 150% half belongs to the mixed-DPI rig (AC-SHELL-15). `HotkeyTests` has its three and `NoHotkeyWithoutACallback`; `DpiAwarenessTests` has `ProcessIsPerMonitorV2` and `TheAppDeclaresPerMonitorV2`; `HookLatencyTests.HookP99IsUnder50Microseconds` is PB-1's perf case, `Category=Perf` and explicit, so CI reports it as not run (`--explicit on` runs it on the reference machines). The input tests share one collection that runs alone and makes the test process Per-Monitor V2, as the app is. New Core `InputRingTests` (7 cases) and `TriggerDispatcherTests` (13).

As built in WP-B5: `GdiMonitorCaptureTests` has the four of the list and `MonitorsDescribeTheDisplays`, `ADisposedFramesBufferIsTheNextGrabs` and `AffinityDoesNotWaitForTheWindowsThread` (normal and layered). Its magenta windows are WPF windows, normal and `AllowsTransparency`, each on a UI thread of its own (the test project now uses WPF), and the exclusion is set from the test's thread, never the window's. `ShieldDeadlockTests` has its one case; `DisplayAffinityProtectionTests` has `SkipsDestroyedHwnd` and five more; new `OwnWindowsTests` (9 methods: the half-open hit test, hidden, unregistered, destroyed and minimized windows, the invisible resize border, `IsOwnWindowUsesOutPid`); `OwnWindowRegistryTests` adds the registry's half of INV-CAP-7 (`RegisterWhileShieldHeldStartsExcluded`, `RegisterSeedsFromSetting`), `ARegistrationIsAnnounced` and `AThrowingListenerCannotFailTheRegistration`; `WicImageCodecTests` has its three and `ThePngDecodesToTheFramesPixels`, `ACropOutsideTheFrameThrows` and `AnEmptyResizeThrows`. The screen tests share a collection that runs alone, Per-Monitor V2; when a window never reaches a read, the failure names the window over it (`Support/ScreenDiagnostics`). Found on the arm64 runner: the windows-11-arm image opens a full-screen `Microsoft account` prompt (a `WWAHost` CoreWindow above every desktop window, topmost ones included), which kept every magenta window out of the screen reads, so the Windows job closes it before the tests (`.github/workflows/dotnet.yml`).

**ShotAI.App.Tests (Windows only):** `AllWindowsRegisteredTests` (every `Window` created by the app is in `OwnWindowRegistry` before `Show`, including the Discard dialog); `RecordingChangedHidesAndShows` (spec 03 owns the behavior; this test pins the event contract). As built in WP-B5: `StartupOrderTests.WindowsExcludedBeforeSettingApplied` (either setting: the main window is excluded when first shown, stays so through step 9 and takes the setting at step 10) and `Settings/RemoteVisibilityApplierTests` (spec 11's six and four more).

**Diagnostics:** `dotnet/tools/ShotAI.ProtectionProbe` (console, Windows): a C# port of `scripts/protection-probe.cjs` with the same delays, reps, tolerance and verdict text, run for a normal WPF window AND an `AllowsTransparency` window. Not in CI (it needs an interactive desktop). The capture self-test ports to a `--capture-selftest` switch (spec 10) with the macOS-corrected size assertions (EDGE-CAP-34). As built in WP-B5: `tools/ShotAI.ProtectionProbe` (WPF, Per-Monitor V2) has the probe's delays, repetitions, tolerance, reset, settle and verdict text, and runs four times: for a normal and an `AllowsTransparency` window, with the affinity set from the window's own thread (as Electron does) and from a worker thread that reads at once (as the shield does). It exits 0 when all four are clean at +0 ms. It reads the screen itself, as `GdiMonitorCapture` does, which `CaptureFunnelSourceTests` allows for its folder alone.

---

## 9. Acceptance criteria

**AC-CAP-1.** `CaptureGeometryTests`, `AutoClassifierTests`, `ClickCaptionsTests`, `CaptureShieldTests` pass on Linux with every case from 8.1 to 8.3.

**AC-CAP-2.** `CaptureEngineTests` and `MenuPollTests` pass on Linux (every case in 8.4). `MenuPollTests.StaleFrameIsDisposed` waits for D24's disposable frames and lands with them in WP-B5 (moved in WP-B3). Met in WP-B5.

**AC-CAP-3.** `CaptureFunnelSourceTests` passes, and introducing a direct `IMonitorCapture.Capture` call in `CaptureEngine.cs` makes it fail (mutation check done once, noted in the PR).

**AC-CAP-4.** `JsMathTests` passes; no `Math.Round(` call exists in `ShotAI.Core/Capture` (source scan in the same test class); no `JsMath` type is declared outside `ShotAI.Core.Json` (R-ARCH-1), and capture code calls `ShotAI.Core.Json.JsMath.Round`.

**AC-CAP-5.** All ShotAI.Platform.Tests capture classes pass on a Windows x64 runner and on a Windows ARM64 machine.

**AC-CAP-6.** Manual, same machine, same app window: record the same 10-click flow (including a double-click, a right-click with a two-level flyout, a Start menu click and a taskbar click) in Electron 1.3.0 and in native, both in auto mode. Step counts are equal, captions are equal string for string, stored PNG dimensions are equal within 1 px per axis, and `click.image` is equal within 1 px.

**AC-CAP-7.** Manual: right-click in File Explorer, choose View then Extra large icons. The recording contains the right-click step and two selection steps whose images show the open menu and submenu; the next ordinary click elsewhere is a plain `Click in ...` step.

**AC-CAP-8.** Manual: right-click, press Esc, wait 20 s, click nearby within 30 s. The step still gets a menu-selection capture from the last polled frame or the click-time grab, and polling stopped after at most 32 frames (debug log shows at most 32 poll grabs).

**AC-CAP-9.** Manual: five quick left clicks at one spot within 400 ms of each other yield exactly one step.

**AC-CAP-10.** Manual: clicks on the pill (every button and the drag area) produce no step and do not change the menu arm state (debug log shows no menu messages).

**AC-CAP-11.** Manual with remote visibility ON, viewed through a Teams screen share: the pill is visible to the viewer throughout a recording and absent from every saved screenshot, including right-click and menu-selection steps (inspect the PNGs).

**AC-CAP-12.** Manual with remote visibility OFF: the pill and main window are never visible in a Teams share, and never in screenshots.

**AC-CAP-13.** `ShotAI.ProtectionProbe` reports `CLEAN` at +0 ms for both the normal and the `AllowsTransparency` window (Q-CAP-15).

**AC-CAP-14.** Manual: press Ctrl+Shift+S while recording with Notepad focused: a step `Capture: <Notepad title>` of the Notepad window crop appears; hold the chord for 2 s: exactly one step (`MOD_NOREPEAT`).

**AC-CAP-15.** Manual: with another app holding Ctrl+Shift+S, start a recording: recording works mouse-only and the log shows the warning from 7.4.

**AC-CAP-16.** Manual: "+ Screenshot" with a closed picked window shows `That window is no longer open \u2014 reopen it and try the screenshot again.` and the main window never hides; with an area on a disconnected monitor shows `That screen area is off-screen now \u2014 drag the area again and retry.`

**AC-CAP-17.** Manual: "+ Screenshot" in Screen mode inserts one step at the chosen gap, the pill never appears, the report does not flash a recording state, and the main window returns focused.

**AC-CAP-18.** Manual: "+ Capture" at gap 2 of a 5-step project, three clicks: the new steps are positions 3, 4, 5 in click order and the old steps 3 to 5 are now 6 to 8.

**AC-CAP-19.** Manual: Discard on a project created for this recording deletes the folder; Discard on an appended recording removes only the new steps and their PNGs; the pill's confirmation text matches (spec 03).

**AC-CAP-20.** Manual: delete step 3 of a 5-step project, then record one step: its file is `step-0006.png` and nothing is overwritten; the report numbers it 5.

**AC-CAP-21.** Manual, mixed DPI (100% primary, 150% secondary): a double-click 8 physical px apart on the 150% monitor collapses (limit 9 px) and on the 100% monitor does not (limit 6 px); a Start menu click on the 150% monitor produces a 1230 x 960 region (before downscale).

**AC-CAP-22.** Manual: with a hung app (a test app whose UI thread sleeps 10 s), clicking it still produces a step within 1 s of the click, captioned `Click in <app>`.

**AC-CAP-23.** Manual: `LowLevelHooksTimeout` set to 200 ms in the registry and a debugger breakpoint in the dispatcher (not the hook) for 5 s: the hook survives (clicks after release are captured); forcing a hook removal is recovered by the watchdog within 4 s of cursor movement.

**AC-CAP-24.** Manual: clicks from a Splashtop or Quick Assist remote session produce steps.

**AC-CAP-25.** Manual: a stored PNG opened in an image viewer is fully opaque (no transparency checkerboard).

**AC-CAP-26.** Manual: with a `shots` junction pointing outside the project, Start is refused with the D10 message and nothing is written outside.

**AC-CAP-27.** Manual: in auto mode, click an inactive window's content area once. The step crops to the clicked window (Q-CAP-4 decides the fix if this fails).

**AC-CAP-28.** `--capture-selftest` prints `[capture-test] PASS` on a Windows machine with at least one visible window.

**AC-CAP-29.** Manual: in Screen mode with monitor 2 chosen, right-click on monitor 1 and select a menu item: the selection step shows monitor 2 (D12).

**AC-CAP-30.** Manual: the SOP review list (spec 07) shows the same `App:` and `Window:` values for a native step as Electron shows for the same window (FileDescription rule).

**AC-CAP-31.** Manual: put a file `step-99999999999999999999.png` in a project's `shots/` and record three clicks: native writes `step-0001.png` upward (or past the manifest length) with no error; Electron 1.3.0 is expected to fail the second click with an `EEXIST` capture error (EDGE-CAP-27, D22).

**AC-CAP-32.** Manual, UWP: record a click in Windows Settings in both builds. `window.app` matches and `window.pid` is the ApplicationFrameHost pid in both (EDGE-CAP-53).

**AC-CAP-33.** `ShieldDeadlockTests` passes on a Windows runner, and a diagnostic build that toggles remote visibility every 50 ms (through `Task.Run`, as the Settings view does) during a 60 s recording with repeated right-click menu polling never hangs the UI thread.

**AC-CAP-34.** Manual, same machine (Phase B exit, WP-B11 in `docs/native/PLAN.md`): record the same 5-click flow in Window mode (once each for a normal window, a maximized window, and a window on a 150% monitor) and in Area mode (a dragged 800 x 600 DIP area spanning a control) in Electron 1.3.0 and in native. For every step: stored PNG dimensions are equal within 1 px per axis, the crop origin matches (no strip of invisible resize border on any edge, and the title bar included or excluded exactly as in Electron's PNG), and `click.image` is equal within 1 px. Repeat once with "+ Screenshot" for a picked window and for a dragged area: PNG dimensions within 1 px per axis and the same crop origin (`click` is `null` in both builds, 2.2.5). This AC decides Q-CAP-8: if Electron's Window-mode PNGs are 14 to 16 px wider (and taller by the bottom border) than native's, switch the crop rect to `GetWindowRect` and re-run it.

**AC-CAP-35.** `CaptureEngineTests.DisposeIsSynchronousAndIdempotent`, `.EventsGoThroughEventRaiser`, `.JobFailureMessageUsesUserMessage` and `.ThrownMessagesAreShotAIExceptions` pass on Linux, and `Composition.ContainerTests.NoAsyncOnlyDisposables` (spec 11) passes with `CaptureEngine` registered as `ICaptureService` (R-ARCH-10, ARCHITECTURE C5).

---

## 10. Interfaces with other subsystems

| Spec | This subsystem consumes | This subsystem provides |
|---|---|---|
| 01 Model and store | `ProjectStep`, `StepClick`, `StepElement`, `CapturedWindow`, `CapturedMonitor`, `CaptureTarget`, `Rect`, `Point`, `ProjectManifest`; `IProjectService.OpenProjectAsync`, `AddStepAsync`, `InsertStepAtAsync`, `DeleteStepsAsync`, `DeleteProjectAsync` (serialized write queue, renumbering, clamping; the interface, never the concrete `ProjectStore`, R-ARCH-4); the static `PathConfine.ConfineNoLinks` with an `IPathProbe` argument and reparse-point refusal (R-ARCH-17); `ShotAI.Core.Json.JsMath.Round` (R-ARCH-1) | new steps (written through the store), `shots/step-NNNN.png` files; the rule that capture never writes `captureSettings` |
| 03 Windows and shell | the pill, overlay, main window and dialogs, each registered with `OwnWindowRegistry` on `SourceInitialized`; the area rect in global physical px from the overlay; the pill's non-activating style | `RecordingChanged(recording, showPill)`; `StateChanged`; `CaptureFailed`; `CaptureShield.ApplyRemoteVisibility` and `ExcludedForNewWindow` for window creation; `ICaptureService.Pause/Resume/StopAsync/DiscardAsync` for the pill |
| 04 Editor | nothing | `click.image`, `click.imageScale`, `click.button`, `monitor.scaleFactor` semantics (INV-CAP-13) for the marker |
| 05 Report | `StartAsync(path, { Target, InsertAt })` from "+ Capture"; `CaptureScreenshotAsync(path, target, index)` from "+ Screenshot" | `StepLanded(step, index)`; the manifest returned by the screenshot; the origin recovery formula used by the merge |
| 06 Home and settings | `StartAsync(path, { Target, CreatedThisSession: true })` from the new-recording flow; `ListTargetsAsync` for the choosers; the screenshot-quality and remote-visibility controls | `CaptureTargets` (windows, monitors), `CaptureState` |
| 07 SOP | nothing | captions (2.9), `window.app` and `window.title` derivation (2.10.1), `element` |
| 10 Settings and infra | `ICaptureSettings.CaptureScaleNow()` and `RemoteVisibleNow()` synchronous caches (implemented by `SettingsService`); `ILogger<T>` whose `ShotAI.Core.Capture` and `ShotAI.Platform.Capture` categories map to the log label `capture` (ARCHITECTURE 8.4); the `--capture-selftest` switch | log lines (2.6 step 18 and section 2 messages) |
| 11 Service boundary | `EventRaiser` (T5), `ShotAIException` and `UserMessage` (`ShotAI.Core.Errors`), the subscriber rules (T6, T7: `IUiDispatcher.Post` only, R-ARCH-11), `RemoteVisibilityApplier` calling `CaptureShield.ApplyRemoteVisibility` through `Task.Run` (DL1), the exit order (ARCHITECTURE 4.5: `Teardown()` then `provider.Dispose()`, R-ARCH-10) | `ICaptureService` (7.2) mapped to the IPC channels `capture:start`, `capture:single` (dropped), `capture:screenshot`, `capture:pause`, `capture:resume`, `capture:stop`, `capture:discard`, `capture:get-state`, `capture:list-targets`, and the events `capture:state-changed`, `capture:step-added`, `capture:error` |
| 12 Packaging | Windows 10 2004 minimum, PMv2 manifest, x64 and ARM64 builds | removal of the Rust `element-locator` build, koffi, uiohook-napi, node-screenshots and get-windows |

---

## 11. Open questions and risks

**Q-CAP-1. Port `captureSingle`?** It has no UI caller. Recommended default: do not port; drop `capture:single` in spec 11; keep 2.2.4 as documentation only. Decided in WP-B2: the default; `ICaptureService` has no single-shot member (D16).

**Q-CAP-2. Hotkey rebinding?** Electron has none and four UI strings hard-code `Ctrl+Shift+S`. Recommended default: no rebinding at 2.0.0; revisit as a feature after cutover (it would add a settings key, spec 10). Decided in WP-B4: the default; the chord is fixed at Ctrl+Shift+S.

**Q-CAP-3. Does Chromium's `globalShortcut` pass `MOD_NOREPEAT`?** Unverified. If it does not, holding the chord in Electron repeats captures. Recommended default: use `MOD_NOREPEAT` (IMPROVEMENT) regardless. Decided in WP-B4: the default; whether Chromium passes it stays unverified.

**Q-CAP-4. Inactive-window clicks in auto mode.** The native dispatcher may read the foreground window before Windows activates the clicked window (EDGE-CAP-50). Recommended default: parity first; run AC-CAP-27; if it fails, when the click point is outside the foreground window's frame bounds, use `GetAncestor(WindowFromPoint(pt), GA_ROOT)` for classification and crop, unless that window belongs to the foreground window's process (dropdown popups).

**Q-CAP-5. Raw Input instead of `WH_MOUSE_LL`.** Recommended default: the hook plus watchdog; switch to Raw Input only if field logs show watchdog reinstalls. Decided in WP-B4: the default; the watchdog's warning line is what the pilot's logs are read for (WP-E7).

**Q-CAP-6. No-click screenshot caption and metadata.** Electron captions it `Capture: <foreground title>` and records the foreground window (an arbitrary window after the hide); macOS uses `Area screenshot`, `Screen screenshot`, `Screenshot of <title>`. Recommended default: Electron parity at 2.0.0 (the caption is editable and SOP generation rewrites it); propose the macOS wording as a post-cutover change on both platforms. Decided in WP-B2: Electron parity; the screenshot is a hotkey step captioned `Capture: <foreground title>` (`ScreenshotInsertsAtIndexWithoutStepEvent`).

**Q-CAP-7. Wording of the new grab-failure error (D6).** Recommended default: `A screenshot could not be captured. If this keeps happening, make sure the target is visible, then try again.` Decided in WP-B2: the default, raised once per failure run of a recording (`GrabFailureRaisesOncePerRun`).

**Q-CAP-8. Which window rect node-screenshots used for window crops** (`GetWindowRect`, client rect, or DWM extended frame bounds). The package ships no source. Recommended default: `DWMWA_EXTENDED_FRAME_BOUNDS` (includes the title bar, excludes invisible borders, matching the comment at `CaptureController.ts:1253-1259`); decided by AC-CAP-34 (Window mode, normal, maximized and 150% cases; AC-CAP-6 covers only auto mode): switch to `GetWindowRect` if Electron's PNGs are 14 to 16 px wider.

**Q-CAP-9. node-screenshots' `Window.all()` filter** (which windows it lists and reports as focused) is not inspectable. Recommended default: the get-windows filter (7.5); compare the Window chooser lists of both builds on one machine.

**Q-CAP-10. ComboBox and Edit names can carry content in some frameworks (privacy).** Recommended default: parity (keep both on the allowlist) because captions and SOP quality depend on field labels; record known leaking apps and consider refusing `ValuePattern`-equal names later. Decided in WP-B1: the default; `UiaControlTypesTests.ComboBoxAndEditStayActionable` pins it.

**Q-CAP-11. Monitor id.** Electron used node-screenshots' id (believed to be the `HMONITOR` value, unverified). It is persisted only as metadata and used within a session. Recommended default: `(uint)HMONITOR`, re-resolved each step; never compare ids across launches. Resolved by R-ARCH-22: the monitor id type is `uint` (`(uint)HMONITOR`) in every spec (06's `CaptureReadiness` included), the manifest's JSON number converts at the codec edge, and ids are never compared across launches. Implemented in WP-B5: `GdiMonitorCapture` ids each monitor so, afresh on each enumeration (`MonitorsDescribeTheDisplays` checks they are distinct).

**Q-CAP-12. `captureSettings` in `project.json`.** Electron writes `null` always; macOS writes the target. Recommended default: Electron parity (never write it); spec 01 must preserve a value written by macOS. Decided in WP-B2: the default; the engine never writes it (`CaptureSettingsIsNeverWritten`).

**Q-CAP-13. `CAPTUREBLT` cursor flicker and layered menus.** Recommended default: `SRCCOPY | CAPTUREBLT`; if manual testing shows cursor flicker without a menu benefit, drop `CAPTUREBLT` and re-run AC-CAP-7. As built in WP-B5: `SRCCOPY | CAPTUREBLT`; the flicker check stays with AC-CAP-7 (WP-B11).

**Q-CAP-14. DXGI Desktop Duplication for the menu frame.** Recommended default: not in 2.0.0; BitBlt polling is proven. Revisit only if poll CPU on 4K monitors is a complaint. Decided in WP-B5: the default; the screen is read with `BitBlt` only.

**Q-CAP-15. Display affinity on WPF `AllowsTransparency` (layered, `UpdateLayeredWindow`) windows, and from a non-UI thread.** Neither is documented (Microsoft Learn requires only a top-level window of the current process and DWM composition). Whatever the answer, the 7.8 deadlock rule applies (Q-CAP-22). Recommended default: verify with `ShotAI.ProtectionProbe` and `GdiMonitorCaptureTests.LayeredWindowAcceptsAffinity` before building the pill and overlay (WP-B5, recording the answer here); if a worker-thread call fails or blocks, apply ARCHITECTURE DL2 and Q-ARCH-6 (7.8 rule 4): the grab thread posts the affinity change to the UI thread and waits for its completion with a bounded timeout, and on timeout skips the grab (fail closed, a missed step reported through `CaptureFailed`), never grabs unshielded; never `Dispatcher.Invoke` (banned in the App outside `WpfUiDispatcher.cs`, T6), and the UI thread never blocks on capture work (7.3 rule). Decided in WP-B5 on both Windows runners: an `AllowsTransparency` WPF window takes the affinity as a normal one does, and `SetWindowDisplayAffinity` from another thread returns without the window's thread and is in effect for the very next GDI read, with no settle, five times out of five, for both kinds of window (`GdiMonitorCaptureTests`, 7.8 as built). The shield calls it directly from the grab thread, and rule 4's marshaling is not built. The probe's timing table on the reference machines is AC-CAP-13's manual run.

**Q-CAP-16. Interpolation mode.** Electron's `'good'` resize is not reproducible exactly. Recommended default: WIC `Fant`; compare legibility of small text against Electron output at 0.5 and 0.85. As built in WP-B5: Fant (`WicImageCodec.Resize`); the legibility comparison stays with WP-B11.

**Q-CAP-17. Element names in logs.** Electron logs `el='<name>'` at info (`CaptureController.ts:1455`); names can be sensitive labels and logs travel with support tickets. Recommended default: log the element name at debug only (IMPROVEMENT), controlType at info. Decided in WP-B2: the default. The info line keeps the controlType as `el=(<controlType>)`, and a debug line `step #<n> el='<name>'(<controlType>)` carries the name (`LogLineFormatMatchesElectron`).

**Q-CAP-18. PNG pixel format.** 32 bpp RGBA (parity) versus 24 bpp RGB (about 25 percent smaller). Recommended default: RGBA for parity; revisit after the AVIF encoder (spec 09) is measured. Decided in WP-B5: the default (`WicImageCodecTests.PngIsRgba8`).

**Q-CAP-19. GC pauses on the hook thread.** A long blocking GC could exceed the hook timeout. Recommended default: the watchdog; keep the hook proc allocation-free; if reinstalls appear in logs, move the hook to Raw Input (Q-CAP-5). Decided in WP-B4: the default; `CallbackIsAllocationFree` shows the hook thread allocating nothing across a thousand clicks on both runners.

**Q-CAP-20. `lastLeftClick` persistence across sessions.** Electron never resets it. Recommended default: reset on `StartAsync` (no observable difference in practice). Decided in WP-B3: the default; `StartAsync` clears it when it installs the session (`ANewSessionForgetsTheLastLeftClick`).

**Q-CAP-21. Monitor names.** node-screenshots' `Monitor.name()` ships without source, so whether it returns the EDID friendly name, the GDI device name (`\\.\DISPLAY1`) or something else is UNVERIFIED. Recommended default: the DisplayConfig friendly name (7.7); compare the Screen chooser of both builds on one machine and switch to `szDevice` if Electron shows device names. As built in WP-B5: the DisplayConfig friendly name; the comparison stays with WP-B11.

**Q-CAP-22. Cross-thread `SetWindowDisplayAffinity` and the shield lock.** Folded into Q-CAP-15 but called out because it decides a deadlock: if the call is serviced by the owning UI thread, any UI-thread wait on `CaptureShield._lock` deadlocks. Recommended default: the 7.8 deadlock rule (no UI-thread path takes the lock) whatever the answer, plus `ShieldDeadlockTests`. This is now ARCHITECTURE DL1 (binding); what remains open is only the Q-CAP-15 measurement. Decided in WP-B5: the call is not serviced by the owning thread (Q-CAP-15), and DL1 holds as built: the registration's reconcile and every change of the setting take the shield's lock on the pool (`ShieldDeadlockTests`, `CaptureShieldTests.RegisteringNeverWaitsForTheShieldsLock`, `RemoteVisibilityApplierTests.ApplyNeverRunsOnUiThread`).

**Q-CAP-23. Why did Electron's main-thread stalls drop clicks?** uiohook's hook proc queues nonblocking (INV-CAP-17 note), so the documented symptom has no mechanism in the code read here. Recommended default: no action for the native port (its hook proc is independent of the dispatcher); record hook reinstalls in the log so any field recurrence is visible. Decided in WP-B4: the default; the hook procedure only copies into the ring, and every reinstall is logged at warning.

**Q-CAP-24. Incomplete targets from the UI.** Electron starts a recording for `{ mode: 'window' }` or `{ mode: 'area' }` without the window or area (EDGE-CAP-60). Recommended default: parity plus one start-time warning (D23); spec 06 should make the chooser impossible to submit incomplete.

**Risk R1 (high).** Hook removal by `LowLevelHooksTimeout` is silent and loses clicks. Mitigated by INV-CAP-17, the watchdog and AC-CAP-23.

**Risk R2 (high).** A single unshielded read leaks the pill into a finished SOP with nothing logged. Mitigated by the type-level funnel (7.7) and the source tests (AC-CAP-3).

**Risk R3 (medium).** Timing differences between the native dispatcher and Electron's event loop change which window is foreground at capture time (Q-CAP-4) and whether menus are still painted at the click-time grab. Mitigated by AC-CAP-6, AC-CAP-7 and AC-CAP-27.

**Risk R4 (medium).** App-name derivation drift (FileDescription) changes captions, the auto classifier and SOP prompts. Mitigated by `WindowInfoTests` and AC-CAP-30.
