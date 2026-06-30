# Plan: shotAI — Local Scribe-style SOP Builder (Electron + TypeScript)

## Context

We're building **shotAI**, a local-first clone of Scribe (scribehow.com): the user records a process they perform on screen, the app auto-captures a screenshot + click for each step, and it produces an editable, annotated step-by-step guide. shotAI's differentiator is **Claude**, which turns the captured steps into polished **Standard Operating Procedure (SOP)** write-ups (overview, prerequisites, per-step instructions, cautions) — richer than Scribe's deterministic "Click X" captions.

Everything runs locally except the Claude API call. **Windows first**, **macOS** later.

**Why Electron + TypeScript** (the platform the user asked me to recommend, confirmed): one codebase for Windows + macOS, Chromium renders/edits/exports the HTML report natively, Claude has a first-class TypeScript SDK, and mature native modules exist for global input hooks and screen capture. The rival (.NET + Avalonia) only wins for a deeply C#-invested team willing to write per-OS native capture with weaker HTML rendering.

## Confirmed scope & decisions

| Area | Decision |
|---|---|
| App name | **shotAI** (npm package `shotai`) |
| Platform | **Electron + TypeScript**, **Electron Forge** (Vite + React + TS template) |
| Product model | **Scribe clone**, local-first; Claude writes the SOP |
| Capture scope | **System-wide** (any app) |
| Capture region (user-selectable) | **a window** · **a user-drawn area** · **one screen** · **auto** (multi-monitor "all screens" is intentionally out of scope) |
| Capture trigger | **Both** auto-on-click **and** a configurable global hotkey |
| Per step | Screenshot, click coords, active app + window title, auto-caption, user note, vector annotations; UI-element name best-effort (phased) |
| NOT captured | Keystrokes / typed text, timestamps |
| Inline screenshot editing | crop · pan · zoom · rounded rectangle · arrow · blur/redact · numbered step stamps · text |
| Storage | **User-chosen folder, default `~/shotAI Projects/`**; each recording = a **discrete project**; existing projects fully **re-editable** |
| Claude surface | **Messages API + vision + structured outputs** (NOT Managed Agents); `claude-opus-4-8` |
| Sharing | **Local export** (HTML / PDF / Markdown; optional .docx). No hosted share-links (would need a backend — out of scope for local-first). |
| Source control | **Private** GitHub repo `shotAI`; **GitHub Desktop + command line** mix |

## Application windows (UX)

- **Capture toolbar** — a small, frameless, always-on-top, draggable window (Scribe's recorder pill). Controls: **Start / Pause-Resume**, **Stop & Process**, **Delete session**, plus the **capture-region selector** (auto / window / area / screen). Stays out of the way during recording.
- **Region-selector overlay** — a transparent, full-screen, always-on-top window (one per display) used to drag-select an **area** or click-pick a **window**; returns the chosen rect/window and closes.
- **Main / project window** — recent-projects list, the step list, the **HTML report with the inline image editor**, SOP generation, and export. Where all editing happens.
- **Tray icon** — quick start/stop and show-window.

shotAI's own windows are excluded from click-triggered capture (filter by our own process IDs) so clicking the toolbar never creates a step.

## Architecture

Native work in **main**; UI/report/editor in **renderer** (sandboxed: `contextIsolation: true`, `nodeIntegration: false`); a typed preload `contextBridge`.

```
┌─ Main process (Node) ──────────────────────────────────────┐
│ CaptureController ← uiohook-napi clicks                     │
│                   ← globalShortcut manual hotkey            │
│   per trigger (respecting capture region), synchronously:   │
│     • click x/y (uiohook)  • active app/window (get-windows)│
│     • screenshot via node-screenshots (Monitor/Window) +    │
│       crop to region       • element-at-point [phased]      │
│     • skip if active window is one of shotAI's own          │
│ RegionService   → window/area/screen selection + overlay    │
│ HotkeyService   → register/rebind global hotkey             │
│ ProjectStore    → user-chosen dir; project.json + shots/    │
│ ClaudeService   → @anthropic-ai/sdk (vision → SOP)          │
│ PermissionService (macOS, phased)                           │
└───────────────┬───────────────── IPC ──────────────────────┘
                │ contextBridge (typed)
┌───────────────┴─────────────────────────────────────────────┐
│ Renderer (React + Vite + TS)                                 │
│  • Capture toolbar window                                    │
│  • Project window: recent projects, step list,               │
│    HTML report + Konva inline image editor, SOP, export      │
└──────────────────────────────────────────────────────────────┘
```

## Capture engine

**Region modes** (set on the toolbar, applied to every capture in the session):
- **Window** — pick a window (list via `node-screenshots` `Window.all()`, or click-pick via overlay); capture that window's *current* bounds each step (re-resolve in case it moved).
- **Area** — drag a rectangle on the overlay; capture the containing monitor and crop to the fixed rect each step.
- **Screen** — capture the chosen monitor (`Monitor`), or the monitor where the click landed (`Monitor.fromPoint`).
- **Auto** — smart per-click surface (app window / OS-shell region / desktop fullscreen), classified per click.

> Multi-monitor **"all screens"** capture is intentionally **out of scope** — pick a single screen, or let auto/window/area handle it. (Area selection is always bound to a single monitor.)

**Triggers:** auto-on-click via **uiohook-napi**; manual via Electron **`globalShortcut`** (rebindable at runtime). **Pause** detaches the click listener; **Stop & Process** finalizes; **Delete** discards the project folder.

**Libraries (verified current, 2026):**

| Concern | Library | Notes |
|---|---|---|
| System-wide clicks | **uiohook-napi** (1.5.5) | Prebuilt, main-process, click x/y. macOS: Input Monitoring permission. |
| Global hotkey | **Electron `globalShortcut`** | Built-in; rebindable. |
| Screenshot | **node-screenshots** (0.2.8, Rust/napi-rs) | Prebuilt; per-monitor + per-window + `Monitor.fromPoint`; returns PNG Buffer; `scaleFactor` for DPI. macOS: Screen Recording permission. |
| Active app/window | **get-windows** (9.3.0) | App, title, bounds, pid. |
| UI element at click | **custom napi-rs (Rust) addon, per OS** | No maintained Node binding exists — central risk. Win: `uiautomation` crate (`ElementFromPoint`). mac: `accessibility-sys`/`objc2-accessibility` (`AXUIElementCopyElementAtPosition`). Best-effort; falls back to app+window+coords. |

Store both **global** and **image-relative** click coords (global − region origin × scaleFactor) so markers land correctly on multi-monitor / mixed-DPI displays.

## Project model & storage

User picks a projects directory (**default: `~/shotAI Projects/`**, configurable). Each recording is a **discrete, self-contained project folder** — portable and directly consumable by the Claude step:

```
<projects-dir>/<Project Name>/
  project.json          # manifest: title, captureSettings, steps[], sop
  shots/step-001.png …  # original captures (never overwritten)
  export/               # generated HTML / PDF / MD
```

`project.json` (v1 schema):
```jsonc
{
  "version": 1, "title": "…", "createdWith": "shotAI",
  "captureSettings": { "region": "auto|window|area|screen", "target": {…} },
  "steps": [{
    "id": "uuid", "order": 1,
    "screenshot": "shots/step-001.png",
    "click": { "global": {…}, "image": {…}, "button": "left" },
    "monitor": { "bounds": {…}, "scaleFactor": 1.5 },
    "window": { "app": "chrome.exe", "title": "Invoices — Acme", "bounds": {…}, "pid": 4321 },
    "element": { "available": false, "name": null, "controlType": null, "bounds": null },
    "caption": "Click 'Save'",        // auto-generated, editable
    "note": "",                        // user free-text annotation
    "crop": null,                      // optional crop rect
    "annotations": []                  // vector objects (see editor)
  }],
  "sop": null                          // Claude-generated SOP structure
}
```
`element.available` is the forward-compat lever (v1 writes `false`; the element addon fills it later with no schema change). No timestamp/keystroke fields.

**Editing existing projects:** open a project to **add steps** (resume capture appending to it, or import an image / take a one-off screenshot), **remove/reorder steps**, **edit any screenshot**, and **regenerate or hand-edit the SOP**. The capture engine therefore supports *append-to-existing-project*, not just new sessions.

## Inline image editor (the Scribe-style annotator)

Built in the renderer with **Konva.js** (via `react-konva`) — a stage with a z/pan-able image layer + a vector annotation layer + a `Transformer` for select/move/resize. Tools: **crop, pan, zoom, rounded rectangle, arrow, blur/redact, numbered step stamp, text**.

- **Numbered stamps** auto-place at the captured click point using the step order; user can move/restyle.
- **Annotations are non-destructive vector objects** stored in `annotations[]`, so every edit stays re-editable.
- **Flatten on output:** for export *and* for sending to Claude, render image + annotations to a single PNG (Konva `toCanvas`/`toDataURL`). Crop is applied here.
- **Redaction is security-critical:** blur/redact must be *baked into* the flattened PNG so original pixels never leave the machine; the on-disk original is preserved, with an option to permanently apply redaction to it. Claude and exports always receive the flattened/redacted render — never raw `shots/*.png`.

## Report, auto-captions & click registers

The report renders each step as the (edited) screenshot with an overlaid **click-register marker** at the image-relative coords + the step caption + the user note. **Auto-captions** are generated locally at capture time (`Click '{element.name}'` / `Click in {window.title}`) so the guide is instantly useful before any AI pass — matching Scribe — and are then enriched by Claude.

## Claude SOP generation

**Messages API + vision + structured outputs** via `@anthropic-ai/sdk`, called from **main** (API key stays out of the renderer; stored via Electron `safeStorage`).

- **Not "an agent in Console":** Managed Agents are for hosted autonomous tool-loops — wrong shape and overkill. Analyzing a fixed bundle of images+metadata → a document is one (or a few) Messages calls: simpler, cheaper, local.
- **Request:** for each step, a base64 **image block** (the flattened/redacted render) + a text block of metadata (order, app, window title, element if available, caption, user note); send the full ordered sequence so the model sees the whole flow.
- **Output:** `client.messages.parse()` with a **Zod schema** → a structured SOP (title, overview, prerequisites, ordered steps = heading + instruction + optional caution + screenshot index). Renderer renders it to HTML; user can edit and re-render.
- **Params:** `model: "claude-opus-4-8"`, `thinking: {type:"adaptive"}`, **stream** + `.finalMessage()` (SOPs run long). Cache the stable system prompt (`cache_control: ephemeral`).
- **Smart cropping for cost/relevance:** even when the user captured a full/all-screen shot, crop to the focused window/region before sending to cut image tokens (~1.6K–4.8K each) and sharpen relevance. `countTokens()` to show an estimated cost before sending a long project.

## Privacy & redaction

Screenshots leave the machine only on SOP generation. Provide a **review-before-send** screen listing exactly what's transmitted, enforce that the **flattened/redacted** render is what's sent (never raw originals), and require explicit per-project consent. All capture/editing is local until the user clicks generate.

## Renderer stack

**React + Vite + TypeScript** (Electron Forge Vite+TS template; multiple renderer entry points for the toolbar vs project windows), **Konva/react-konva** for the editor, lightweight state via **Zustand**. (Swappable if the team prefers Vue/Svelte — React is the safe, well-supported default.)

## Version control & GitHub (set up first, used throughout)

shotAI lives in a **private** GitHub repo named `shotAI`. You'll use a **mix**: GitHub Desktop for everyday commit/branch/push, the command line (`git` + `gh`) for setup and power tasks. I can run the CLI parts for you and explain each as we go; mirror them in GitHub Desktop to build the habit. (I'll only ever commit or push when you ask.)

**One-time setup (start of Phase 0):**
1. Install **Git**, **GitHub Desktop**, and (optional) the **GitHub CLI (`gh`)**. Sign in to GitHub Desktop with your account — that also handles push authentication.
2. Make this folder a repo and publish it **private**:
   - *Desktop:* File → Add local repository → pick `z:\local_software_proj\ScreenshotTool` → "create a repository here" → Publish, with **"Keep this code private" checked**.
   - *or CLI:* `git init` → `gh repo create shotAI --private --source=. --remote=origin --push`.
   - (The folder may stay named "ScreenshotTool" or be renamed to `shotAI` — cosmetic; the GitHub repo is `shotAI`.)
3. Add a `.gitignore` and `README.md` **before the first commit**.

**Golden rule — never commit secrets.** The Anthropic API key must never be committed: keep it out via `.gitignore` (`.env`), store it at runtime in the OS keychain (Electron `safeStorage`), and commit only a `.env.example` placeholder. Anything pushed to GitHub is effectively permanent even if later deleted.

**`.gitignore` essentials:** `node_modules/`, `out/`, `dist/`, `.vite/`, `*.log`, `.env`, `native/**/target/`, `.DS_Store`, `Thumbs.db`.

**Everyday loop (GitHub Desktop):** make changes → review in the Changes tab → write a short, clear commit message → **Commit** → **Push**. Commit small and often.

**Branches & pull requests (the core GitHub habit):** keep `main` always-working; do each phase/feature on its own branch (Desktop: Current Branch → New Branch, e.g. `phase-1-capture-engine`), commit, Push, then **Create Pull Request** (opens the web) → review the diff → **Merge** → delete the branch.

**Command line, when it's better** — quick status/history, creating the repo/PRs, scripted tasks. Cheat-sheet:
- `git status` — what's changed · `git log --oneline --graph` — history
- `git switch -c phase-2-editor` — new branch
- `git add -A && git commit -m "…"` — stage + commit
- `git push -u origin HEAD` — push the branch
- `gh pr create --fill` / `gh pr merge` — open / merge a PR

**Track the work (recommended):** one GitHub **Issue** per phase (or a Projects board) — a clear roadmap you check off. **Releases (Phase 4–5):** attach packaged installers to tagged GitHub Releases.

## Phased roadmap

- **Phase 0 — Repo + scaffold + project model:** GitHub setup above (private `shotAI`, `.gitignore`, README, first commit); Forge (Vite+React+TS); capture-toolbar + project windows; sandboxed preload + typed IPC; `ProjectStore` (user-chosen dir default `~/shotAI Projects/`, discrete projects, recent list, create/open). No capture yet.
- **Phase 1 — Capture engine (Windows MVP):** uiohook-napi + `globalShortcut`; region modes (auto/window/area/screen) + region overlay; node-screenshots capture + crop; get-windows app/title; exclude own windows; pause/resume/stop/delete; auto-captions; write `project.json` + shots; live step list.
- **Phase 2 — Report + inline editor + edit-existing:** Konva editor (crop/pan/zoom/rounded-rect/arrow/blur-redact/numbered-stamp/text), non-destructive annotations + flatten-on-output; HTML report with click markers; open existing projects to add (recapture/import)/remove/reorder steps and edit screenshots.
- **Phase 3 — Claude SOP + export:** `ClaudeService` (vision + Zod) on flattened/redacted images; review-before-send + redaction enforcement; smart crop + token pre-count; render/edit SOP; export HTML/PDF/Markdown (optional .docx).
- **Phase 4 — Windows element-level + hardening:** napi-rs `uiautomation` addon; multi-monitor/DPI correctness; double-click debounce; capture-latency tuning; **automatic sensitive-data redaction pre-scan** (local OCR via Tesseract.js → SSN / credit-card-Luhn / API-key detectors → suggested redactions surfaced in review-before-send, baked via the existing flatten path; best-effort assist on top of the manual gate — see `docs/PHASE-3-PLAN.md`); Authenticode signing.
- **Phase 5 — macOS port:** permissions wizard (Input Monitoring / Accessibility / Screen Recording detect + deep-link); verify native deps on mac; macOS element addon; Developer ID signing + notarization + hardened runtime + entitlements.

~80% is shared cross-platform; the platform-specific delta is the per-OS element addon and the macOS permission + signing story.

## Key risks (ranked)

1. **UI-element-at-point** — custom native work, no off-the-shelf binding, inconsistent accessibility trees. Mitigation: phase it; always fall back to app+window+coords.
2. **macOS permissions/signing** — runtime-granted TCC permissions + notarization. Mitigation: dedicated Phase 5 + permissions wizard.
3. **Redaction correctness** — a baking bug would leak sensitive pixels to Claude/exports. Mitigation: single flatten path; redaction destructive on the flattened render; tested.
4. **Capture accuracy/latency** — fast, debounced synchronous gather; exact marker placement under multi-monitor/mixed-DPI and after crop.
5. **Cost & data sensitivity** of many screenshots. Mitigation: crop-to-window, token pre-count, redaction, review-before-send.
6. **Native module / Electron ABI** churn. Mitigation: Forge auto-rebuild; prebuilt deps; pin Electron for the napi-rs addon.

## Files to create first

- [package.json](package.json) — name `shotai`; Electron + Forge (Vite+React+TS), `@anthropic-ai/sdk`, `zod`, `konva`/`react-konva`, `zustand`, uiohook-napi, node-screenshots, get-windows; native-addon build scripts.
- [.gitignore](.gitignore) and [README.md](README.md) — committed before any code.
- [src/main/CaptureController.ts](src/main/CaptureController.ts) — clicks + hotkey → region-aware synchronous capture, own-window filtering.
- [src/main/RegionService.ts](src/main/RegionService.ts) — window/area/screen selection + the overlay window.
- [src/main/ProjectStore.ts](src/main/ProjectStore.ts) — user-chosen dir, discrete project folders, `project.json` + shots, recent-projects, open/append.
- [src/main/ClaudeService.ts](src/main/ClaudeService.ts) — vision + Zod structured-output SOP on flattened/redacted images; key via `safeStorage`.
- [src/preload/index.ts](src/preload/index.ts) — typed `contextBridge` IPC.
- [src/renderer/toolbar/](src/renderer/toolbar/) — capture toolbar window.
- [src/renderer/project/](src/renderer/project/) — project window: report + SOP + export.
- [src/renderer/editor/](src/renderer/editor/) — Konva annotation editor + flatten-to-PNG.
- [native/element-locator/](native/element-locator/) — napi-rs Rust crate exposing `getElementAtPoint(x,y)` (Phase 4/5).

## Verification (end-to-end)

1. **Capture (Phase 1):** Record with each region mode — pick a window, drag an area, one screen, auto — clicking in a few apps plus one manual-hotkey step. Confirm `project.json` has correctly ordered steps with right app/title, PNGs in `shots/`, auto-captions, and that clicking the toolbar created no step. Confirm pause/resume/stop/delete and a save location under `~/shotAI Projects/`.
2. **Editor (Phase 2):** Add a rounded rect, arrow, numbered stamp, text, and a **redaction** to a screenshot; confirm non-destructive re-edit; confirm the flattened export PNG has redaction baked in and the marker is correctly placed. Open an existing project and add/remove/reorder steps and import a screenshot.
3. **SOP (Phase 3):** With an API key set, generate the SOP; confirm review-before-send lists exactly what's transmitted, that **redacted** renders (not originals) are sent, the request streams, and a structured SOP renders to HTML and exports to HTML/PDF/Markdown.
4. **Element (Phase 4):** Click a native Win32 control → `element.name`/`controlType` populate; click an Electron/custom-drawn app → graceful `null`.
5. **macOS (Phase 5):** Permissions wizard detects + deep-links the three TCC permissions; repeat 1–3 on a Mac.

## Prerequisites & assumptions

- **GitHub account** (have it) + **Git**, **GitHub Desktop**, optional **`gh`** installed; private repo `shotAI`.
- **Anthropic API key** (app settings → `safeStorage`; `ANTHROPIC_API_KEY` for dev).
- Node.js LTS + Rust toolchain (Phase 4+ napi-rs addon); Apple Developer ID (Phase 5).
- **Assumption:** local export only (no hosted share-links / accounts), consistent with local-first. **Assumption:** React + Vite renderer. Tell me if either should change.


---

## x64-first on Windows-on-ARM (verified 2026-06-24)

**Decision:** Build/ship **x64**; run it **emulated** on the ARM dev box via the Windows 11 Prism emulator. One arch, one set of native binaries — no arm64/x64 juggling. Verified sound against Microsoft Learn + Electron/Forge docs (5-agent research workflow).

**Why it holds:** x64 emulation is GA and transparent on Windows 11 on ARM. shotAI is pure user-mode (Electron + N-API addons), so nothing hits the emulation exclusions: kernel/print/UMDF drivers, kernel anti-cheat, and DLLs injected into *native ARM64* processes (shell extensions, IMEs, cross-process hooks). uiohook-napi uses own-process low-level hooks (`WH_KEYBOARD_LL`/`WH_MOUSE_LL`), NOT DLL injection -> fine emulated (verify empirically). Keep every bundled native addon x64 (an arm64-only `.node` cannot load into an emulated x64 process). Do NOT enable the `ProcessDynamicCodePolicy` "prohibit dynamic code" mitigation (breaks V8/JIT); run from local disk (Chromium sandbox). Keep the dev box on patched Win11 24H2/25H2 so AVX/AVX2 emulation (KB5066835) is present.

**Native deps — all ship win32-x64 prebuilts (zero compile on the x64-emulated path):**

| Module | win32-x64 prebuilt | win32-arm64 prebuilt |
|---|---|---|
| uiohook-napi 1.5.5 (prebuildify / node-gyp-build) | yes | yes |
| node-screenshots 0.2.8 (napi-rs optionalDep) | yes | yes |
| get-windows 9.3.0 (node-pre-gyp) | yes | NO (only x64 + ia32) |

All three are N-API -> ABI-stable across Electron/Node versions. **get-windows has no arm64 prebuild**, so x64-emulated is the *only* zero-compile path on this host — which further justifies x64-first.

**Concrete actions (Phase 0 — do once, then commit):**
1. Repo-root `.npmrc`:
   ```
   arch=x64
   target_arch=x64
   runtime=electron
   ```
2. package.json packaging scripts target x64 (the `--arch` Forge CLI flag; `packagerConfig.arch` is ignored, maker-squirrel has no arch key):
   ```
   "package": "electron-forge package --arch=x64",
   "make":    "electron-forge make --arch=x64",
   ```
3. Switch the Electron binary to x64: delete `node_modules` (+ electron cache), then `npm install`. Verify `node_modules/electron/path.txt` / `dist` is the win32-x64 build. (Arch is decided at INSTALL time — precedence `ELECTRON_INSTALL_ARCH || npm_config_arch || process.arch`; `electron-forge start` just launches whatever single binary is installed.)
4. Belt-and-suspenders for native modules (Phase 1): `npx electron-rebuild -f --arch x64 -v <electronVersion>`.

**Open risks to verify empirically on first native-module run (Phase 1):** (a) uiohook global hooks actually fire under emulation; (b) get-windows returns correct foreground-window titles under emulation; (c) node-screenshots capture path works under emulation. If any fail, the fallback is a native arm64 *dev* build for that debugging session (uiohook + node-screenshots have arm64 prebuilds; get-windows would compile) — NOT abandoning x64-first. Later optimization for compute-heavy native code: ARM64EC (native-speed hot code, rest stays emulated x64 in-process).

### Local dev note (this automated session only)
This box's automated shell has no usable GPU, so Electron aborts with `GPU process isn't usable`. Gated software-rendering fallback added in `src/main/main.ts` behind `SHOTAI_DISABLE_GPU=1` (disables HW accel + `--in-process-gpu`/`--no-sandbox`/`--disable-software-rasterizer`). Not needed on a normal interactive desktop; harmless when the env var is unset.

### npm 11 `allow-scripts` gating (resolved 2026-06-24)
npm 11 ships an `allow-scripts` allowlist (empty by default; `allow-scripts-pin=true`) that **blocks dependency install/postinstall scripts**. This silently skipped Electron's binary-download postinstall, so `npm install` left `node_modules/electron/dist` empty (no runnable Electron). esbuild survives this (its binary ships as an optional-dep package); **Electron does not** — its postinstall must run.

**Fix (committed):** a ROOT `postinstall` (`scripts/postinstall-electron.mjs`) — root scripts are NOT gated — runs Electron's installer with `ELECTRON_INSTALL_ARCH=x64`. So any `npm install` (dev, teammate, CI) reproducibly yields an **x64** Electron. This also let us drop the deprecated `arch=`/`target_arch=`/`runtime=` keys from `.npmrc` (npm 11 rejects them as "Unknown project config" and warns on every command).

**Still gated, handle later:** `electron-winstaller` (only needed for `npm run make` Squirrel installers) — allow it then via `npm approve-scripts electron-winstaller` or an allowlist entry.

**Verified 2026-06-24:** `electron.exe` PE machine type = `0x8664` (x64); `npm start` logs `runtime: win32/x64 · electron 42.5.0` with both windows loading (emulated on the arm64 dev box).
