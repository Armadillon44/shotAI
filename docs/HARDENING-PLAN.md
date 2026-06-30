# shotAI — Hardening & Cleanup Plan

Implementation plan for **all** structure-review + security-review findings (both run 2026-06-30 via adversarial multi-agent workflows, then a third workflow that produced this dependency-ordered plan and red-teamed the sequencing). Full finding detail lives in the review backlog; this doc is the **execution spec**.

**Threat model:** shotAI is a *local single-user Windows desktop tool*. Primary assets: (1) the Anthropic **API key** (main-process-only), (2) the **redaction guarantee** (blur/crop must be baked into the `flattened` PNG before any send to Claude or any export).

## Conventions
- **Branch off `main`** per track; keep separate from unrelated work.
- **Verification gates (every phase):** `npx tsc --noEmit` + `npm run lint` clean (only the pre-existing `CaptureController` non-null-assertion warning is allowed).
- **Packaging-affecting phases** (P3 sandbox switch, P5 extraResource, P9 rename): also `npm run make`/`npm run package` or at least `npm run build`.
- **Security-critical / runtime-heavy phases:** live `npm start` test of the specific scenario, **plus** direct-call/unit tests where the real UI can't reach the fixed code (see Testing infrastructure below).
- One commit per phase (bisectable); PR granularity per the table.

## Testing infrastructure (prerequisite — "P0.5")
The repo has **no test runner** (`package.json` has no `test` script; no vitest/jest). Several fixes below (S3 merge-invalidation, atomic-write serialization, the extracted pure functions) are **unreachable through the real UI** and need direct-call tests. Add a minimal **vitest** dev-dep + `test` script before P1/P2 so the security-critical fixes are actually verifiable. Pure functions to cover: `confinePath`, `resolveSendableRender`, `applyPatchAndInvalidate`, `parseRect`/`parsePoint`, `capture-geometry` (`unionRect`/`cropToRegion`/`clickBox`/`captureModeFor` golden values), `markerColorFor`.

## Execution DAG
```
P0 ─┬─> P1 ─> P2 ─┬─────────────> P6 ─┐
    │             │                   ├─> P9  (rename — LAST)
    │   P4 ─> P3 ─┘ ─> P5 ───────────┘
    │   P4 ─> P7
    └─> P8                  (P8 independent)
```
**Shipping-value order:** security track `P0 → P1 → P2 → P3 → P5`; structure track `P4 → P6 → P7 → P8 → P9`. P0/P4/P8 are low-risk and interleave.

---

## P0 — Quick-win cleanup `[T3c, T3d, T3e, T3f, DOC]` — risk: low
Land all zero-behavioral-risk tidy fixes so later phases start clean.
- **T3d** delete dead `shapeParams()` (`ClaudeService.ts:104-107`; remove `ModelParams` from its import) + `isAnnotation()` (`annotations.ts:184-186`). Grep-confirm zero importers.
- **T3c** delete private `clickMarkerRadius` (`flatten.ts:172-175`), `import { clickMarkerRadius } from './annotations'` (no cycle — annotations doesn't import flatten).
- **T3e** fix `selftest.ts` — **BOTH** broken assertions (folders are UUID-named now, `ProjectStore.ts:148,153`): line 57 `basename==='Self Test Project'` → UUID-shape regex; line 61 `sanitizedName==='Flow AB - C'` → assert `manifest.title` instead (or drop). ⚠️ Running `SHOTAI_SELFTEST=1` IS a live run that mutates real `projectsDir`/recents (restores in `finally`) — treat as live-verified, confirm it prints PASS.
- **T3f** reword stale `export.ts:17` comment ("mirror ProjectStore" — no longer true).
- **DOC** rewrite `README.md`: drop "Phase 0", remove nonexistent `scripts/postinstall-electron.mjs` ref, move shipped deps out of "Planned", document `overlay/` + `native/`. **Keep** the model default as `claude-sonnet-4-6` (`sop.ts:32`).
- ⚠️ P0-reviewer note: leave `confineProjectFile` (`ClaudeService.ts:110-115`, adjacent to the shapeParams deletion) for P1.
- **PR:** 1. **Verify:** tsc+eslint; selftest prints PASS; grep zero importers.

## P1 — Convergence ①: shared path confinement `[T1b, S5, S6]` — risk: med — depends: P0
- **T1b** add `export function confinePath(dir, rel): string|null` to ProjectStore (exact body from `:239-244`); refactor `resolveProjectFile` to call it. Delete the verbatim private `confineProjectFile` copies in `ClaudeService.ts:110-115` (call site 185) and `export.ts:38-43` (call site 105), import `confinePath`. ProjectStore is a leaf module → no cycle.
- **S6** (per red-team: per-site wrap is PRIMARY, not a normalizeSteps rewrite — `confinePath` returns *absolute*, but step paths are stored/consumed *relative*; rewriting would break `shot://` + manifest round-trip). Wrap each manifest-sourced join: `redactScan` (`ipc.ts:429`), `deleteSteps` (`ProjectStore.ts:524-526`). Optionally add a read-time **validator** in `normalizeSteps` that *nulls/drops* a step whose rel path escapes (keep the relative string when in-bounds).
- **S5** (per red-team: SEPARATE from S6 — `stepId`/`keepId` are NOT path fields, never pass through normalizeSteps). Add a UUID-shape check on `stepId`/`keepId` before the `.render` writes (`ProjectStore.ts:402,459`) or at the IPC layer.
- **PR:** 1 (theme: path confinement). **Verify:** tsc+eslint; live shot:// serve + Auto-redact + discard-session + delete-step; manual hand-edited `../` in `project.json` is refused; unit test `confinePath` fuzz vectors.

## P2 — Convergence ②: redaction backstop + egress pin `[S3, T1a, S10]` — risk: high — depends: P1
- **S3 + extraction** pull `updateStep`'s invalidation else-branch (`:408-417`, null `flattened`+`markerBaked` when `'annotations' in patch || 'crop' in patch` and no fresh PNG) into `applyPatchAndInvalidate(step, patch, hasFreshPng)`; call from `updateStep` **and** `mergeSteps` (`:439-474`, which currently has **no** else-branch).
- **T1a** create `src/main/render-gate.ts` exporting `resolveSendableRender(dir, step, stepLabel, verb) → {abs, mediaType, ext}` — the fail-closed gate (`hasBlur`; `rel=step.flattened`; throw if `!rel && (hasBlur||crop)`; `relToRead=rel ?? screenshot`; `abs=confinePath(...)`; throw if `!abs`; derive ext/mediaType). Replace `ClaudeService.ts:173-186` (verb `send`, label = `n` over `!aiInserted`-filtered source) and `export.ts:96-106` (verb `export`, label = `shotNo` over full steps; keep its extra `fs.stat` after). ⚠️ Caller passes its own precomputed `stepLabel` — the two use different numbering.
- **S10** `makeClient(apiKey) → new Anthropic({ apiKey, baseURL: 'https://api.anthropic.com' })`, use at all 3 sites (`ClaudeService.ts:84,239,271`). Batched here (same hot file).
- **PR:** 1 (highest-value security). **Verify:** tsc+eslint; **redaction fail-closed live test** (add blur→save→send/export show redacted; change blur WITHOUT re-save → both REFUSE); **direct-call test** of `applyPatchAndInvalidate` (merge else-branch is dead on every UI path — UI test proves nothing); real SOP gen; **negative test:** bogus `ANTHROPIC_BASE_URL` → all 3 sites still hit api.anthropic.com.

## P3 — Electron hardening `[S2, S4, S8, S9]` — risk: high — depends: P4
- **S2** decouple `--no-sandbox` from the HW-accel branch (`main.ts:96-105`): keep GPU switches under `SHOTAI_ENABLE_GPU!=='1'`, gate `--no-sandbox` behind its own `SHOTAI_NO_SANDBOX==='1'` so the packaged app ships sandbox-**ON**. Fix the misleading comment.
- **S4/S8** register `app.on('web-contents-created', …)` before `createWindows()`: `setWindowOpenHandler(()=>({action:'deny'}))`; `will-navigate` + `will-redirect` `preventDefault` unless local origin (`shot:`/`file:`/dev URL). (S8 = accepted residual under single-window + contextIsolation; this guard covers the navigation surface, NOT per-handler sender checks — state that explicitly.)
- **S9** `RegionService` `isOverlaySender(event)` guard at the top of both `ipcMain.on` handlers (`:30,33`); ignore non-overlay senders. ⚠️ depends on P4 (RegionService imports shared `parseRect` first — same lines, avoids conflict).
- **PR:** 1 (Electron theme). **Verify:** tsc+eslint; ⚠️ **sandbox-on cannot be verified on this ARM VM** — needs real x64 hardware or a packaged smoke-test (otherwise a renderer-fails-under-sandbox regression ships unseen); Area capture still works post-S9; `will-navigate` + `will-redirect` + `window.open` all denied; `npm run build`.

## P4 — Tier-3 dedup `[T3b, T3a]` — risk: low
- **T3b** add pure `parseRect`/`parsePoint` (+ local `isNum`) to `shared/project.ts` (next to Rect/Point; keep dependency-free). Delete locals in `ipc.ts:90-103` (import) and the private one in `RegionService.ts:38-51` (import; call site 31 → `parseRect(rect)`).
- **T3a** add `export function markerColorFor(step)` to `annotations.ts` (hoist `merge.ts:21-23` verbatim; export `RIGHT_CLICK_COLOR='#2563eb'` beside `ACCENT`). Replace inline copies in `merge.ts:21-23`, `Report.tsx:48-49`, `sop-prepare.ts:52`. ⚠️ **Do NOT touch `Editor.tsx`** — it seeds `markerColor ?? ACCENT` with no right-click branch; swapping would silently flip un-set right-click markers rose→blue.
- **PR:** 1. **Verify:** tsc+eslint; live Area capture + editor click-reposition + marker colors unchanged. **Lands before P3's S9.**

## P5 — Supply-chain + offline assets `[S1, S7]` — risk: med — depends: P3
- **S1** in `postinstall.mjs` `fetchNpmTarball`: load `package-lock.json`, compute `sha512` of the downloaded buffer, compare to `lock.packages[key].integrity`, throw on mismatch (augments the `buf.length<1024` check). Callers: node-screenshots (`:86-91`), koffi (`:107-112`). ⚠️ **residual:** `get-windows` downloads via `node-pre-gyp` (`:67-74`) and stays unverified — either extend a pinned-hash check or document as accepted residual.
- **S7** vendor `vendor/tessdata/eng.traineddata.gz`; append to `forge.config.ts` extraResource (wire resource FIRST); in `ocr.ts:42` pass `langPath` (packaged: `process.resourcesPath/tessdata`; dev: `app.getAppPath()/vendor/tessdata`) + `gzip:true`.
- **PR:** 1 (packaging-affecting). **Verify:** tsc+eslint; fresh install logs integrity-verified + a tampered lockfile-integrity char throws; first-run OCR works network-blocked; `npm run build`.

## P6 — SOP extraction + atomic writes `[T2d, T1c]` — risk: med — depends: P1, P2
- **T1c** route `writeManifest` (`:106-115`) **and** `createProject`'s inline write (`:169-173`) through `writeFileAtomic` (already imported by settings/secrets). Do **before** T2d.
- **T2d** add `export function mutate(projectPath, fn)` (writeQueue-serialized read-modify-write; keep `writeQueue`/`readManifest`/`writeManifest` private). Create `src/main/sop-apply.ts`; move `applySopEdits`, `revertSop`, `makeTextStep` out, rewriting bodies to call `mutate(...)`.
- ⚠️ **BLOCKER fix (red-team):** `ipc.ts:480` calls `projectStore.revertSop` — add `ipc.ts` to this phase and repoint it to `import { revertSop } from './sop-apply'`. (Also update `ClaudeService.ts:21` `applySopEdits` import.) Graph stays acyclic: `claude-service → sop-apply → project-store`.
- **PR:** 1. **Verify:** tsc+eslint; SOP generate/revert/regenerate-twice (first snapshot preserved); **direct concurrent-call test** for mutate serialization + atomic write (not non-deterministic "rapid capture"); grep no other `makeTextStep` importer.

## P7 — Module extraction `[T2a, T2b, T2c]` — risk: high — depends: P4
- **T2a** extract pure `capture-geometry.ts` (`unionRect`/`cropToRegion`/`clickBox`/`captureModeFor`) + `click-caption.ts` (`controlWord`/`buildClickCaption`) from CaptureController. **DEFER** the `MenuTracker` class (touches `this.natives`/timing; preserve same-arm guards `:549,578,584`).
- **T2b** extract `BlurRegion.tsx` (`Editor.tsx:111-192`, all-props) + `editor-geometry.ts` (`clampRectToImage:77-86`, optionally `computeCropView:286-306`). Don't extract `eventImagePoint`/`pointer` (read stageRef).
- **T2c** extract `Home.tsx` from `App.tsx` (JSX + picker state + row actions); Home owns picker via callbacks; App keeps `onRecord` + recording HUD. **Do NOT** lift picker state to the store.
- **PR:** 3 commits/PRs (one per extraction). **Verify:** tsc+eslint each; **golden-value before/after** for the pure geometry fns (`clickBox`/`cropToRegion` are redaction-adjacent — pixel drift = marker/crop misplacement); live capture (incl. right-click→menu), editor draw/resize/crop/save, Home create/open/rename/delete/export. ⚠️ don't claim to verify same-arm guards (not moved this phase).

## P8 — CSS + IPC tidy `[T3h, T3j]` — risk: med — independent
- **T3h** cut `.ed__*` block (`project.css:1500-1757`) into `src/renderer/editor/editor.css`; `import './editor.css'` in `editor/Editor.tsx` (or project `main.tsx`) — same change, or styles vanish.
- **T3j** (red-team: 2 ends, not 3 — `CaptureController.start` already takes opts) pass the `capture.start` opts object through: `preload.ts:106-112` send the object; `ipc.ts` handler receive an opts object. CaptureController unchanged.
- **PR:** 1 (2 commits). **Verify:** tsc+eslint; editor styles render identically; capture for both fresh-this-session and existing project (createdThisSession preserved).

## P9 — Function-bag kebab-renames `[T3g]` — risk: low — depends: P1, P2, P5, P6 (LAST)
- `git mv` `ProjectStore.ts→project-store.ts`, `ClaudeService.ts→claude-service.ts`, `ElementLocator.ts→element-locator.ts`, `atomicWrite.ts→atomic-write.ts`. Keep `CaptureController`/`RegionService` (real classes).
- Update import specifiers (ipc, main, sop-apply, render-gate, claude-service, settings, secrets, + any tsc surfaces). ONE commit. No renderer file affected (talks to main only via IPC).
- **PR:** 1, isolated mechanical. **Verify:** tsc+eslint; `npm run build`; smoke `npm start`.

## Deferred
- **T3i** regroup flat `src/main/` into feature folders — low-payoff/medium-churn; would re-touch every import right after P9. Revisit if `src/main/` grows.
- **T3k** move `flatten.ts`/`annotations.ts` to a `render/` dir — optional; must land after T3a/T3b/T3c/T2b to avoid double-editing import sites. Standalone follow-up if desired.

## Coverage
S1→P5, S2→P3, S3→P2, S4→P3, S5→P1, S6→P1, S7→P5, S8→P3, S9→P3, S10→P2 · T1a→P2, T1b→P1, T1c→P6 · T2a/b/c→P7, T2d→P6 · T3a/b→P4, T3c/d/e/f→P0, T3g→P9, T3h/j→P8, T3i/k→deferred · DOC→P0.

## Status (branch `hardening`, as of 2026-06-30)
- [x] P0.5 vitest · [x] P0 · [x] P1 · [x] P2 · [x] P3 · [x] P4 · [x] P5 (S1 + S7) · [x] P6 · [x] P8 · [x] P9
- [x] **P5/S7** — vendored `vendor/tessdata/eng.traineddata.gz` (2.82 MB LSTM `best_int`) + forge extraResource → `resources/tessdata` + `ocr.ts` langPath; verified offline OCR reads the local model (no CDN). `npm run make` packaging path still untested.
- [ ] **P7** (split CaptureController / Editor / App→Home) — DEFERRED: behavior-preserving but needs live capture/editor/home testing; to be done together.

Verified for the landed phases: `tsc --noEmit` + eslint clean (only the pre-existing CaptureController non-null warning), 33 vitest tests green, selftest PASS. Pending hardware/live checks (flagged in commits): sandbox-ON launch on real x64 (S2), live redaction fail-closed in the editor (S3/T1a), bogus `ANTHROPIC_BASE_URL` negative (S10), live SOP apply/revert + concurrent-write serialization (P6).

## Testing on a physical x64 Windows machine

This is **much simpler than the arm64 dev VM** — no Rust/MSVC/Python needed (the `element_locator.dll` is committed; native deps are x64 prebuilts npm installs directly), and none of the `SHOTAI_NO_SANDBOX` / arm64-repair steps apply. Prereqs: **Node 24 LTS + Git**.

```sh
git clone https://github.com/Armadillon44/shotAI.git
cd shotAI && git checkout hardening
npm install        # installs x64 binaries natively; if koffi/get-windows don't load: node scripts/postinstall.mjs
npm start          # real Electron + real main.ts → OS sandbox ON (no env vars) = the shipped default
```

Three levels of testing, easiest first:
1. **`npm start`** (dev) — fastest; verifies the sandbox-on default + lets you run all the live UI checks. (GPU is disabled by default even here — a VM workaround that currently ships to all users; `set SHOTAI_ENABLE_GPU=1 && npm start` to see HW accel. Doesn't affect the sandbox check.)
2. **`npm run package`** then run `.\out\shotAI-win32-x64\shotAI.exe` — the real **packaged** app (asar, `resources/tessdata`, `.ico`, fuses, sandbox) without needing the Squirrel installer. Best single check for S7's packaging path + the exe icon.
3. **`npm run make`** — full Squirrel installer (`out\make\squirrel.windows\x64\*Setup.exe`). Never run end-to-end; `electron-winstaller` will likely need an npm-11 allow-scripts approval first. Do last, for release.

Notes: don't copy `node_modules` from the arm64 VM (install fresh on x64); paste a Claude API key into Settings for the SOP checks.

### Pending-verification checklist (do on the x64 machine)
- [ ] **S2 — sandbox on**: `npm start` with NO env vars; confirm the window paints + renderers run (i.e. the OS sandbox doesn't break startup on real hardware).
- [ ] **S3 / T1a — redaction fail-closed**: open a step, add a blur, **Save** (bakes the render); confirm SOP-send + all export formats show the redacted image. Then add/change a blur or crop and **don't** re-save → both SOP-send and export must **refuse** (not emit raw pixels).
- [ ] **S10 — egress pin**: set `ANTHROPIC_BASE_URL` to a junk host (e.g. `http://127.0.0.1:1`), run a SOP generate + key test + estimate; all must still reach api.anthropic.com (not the junk host).
- [ ] **P6 — SOP apply/revert + atomic writes**: generate a SOP, Revert (restores the pristine pre-AI snapshot), regenerate twice (first snapshot preserved, no compounding); capture rapidly while editing to sanity-check serialization.
- [ ] **S7 / packaging** (via `npm run package`): first-run **Auto-redact** OCR works with the network off (reads the vendored `resources/tessdata` model); the packaged `.exe` shows the shotAI icon.
