# Phase 3 — Claude SOP Generation + Export

Branch: `phase-3-claude-sop` (off `main` `7dca5b7`). One PR at the end. Commit + live-verify each sub-step.

## Context

Phases 0–2 are merged: shotAI records a process, renders an editable annotated report, and has a non-destructive Konva editor with **flatten-on-output** (`export/.render/<stepId>.png`) where **redaction is baked destructively into the pixels** (security-critical, already shipped). The manifest already reserves `sop: unknown | null`.

Phase 3 is the differentiator: **Claude turns the captured + edited steps into a polished SOP** (overview, prerequisites, per-step instructions, cautions), then exports it locally (HTML / PDF / Markdown). Everything stays local except the one Claude API call. Per `docs/PLAN.md` Phase 3 + the Claude API skill: Messages API + **vision** + **structured outputs (Zod)**, streaming, cached system prompt, `count_tokens` pre-count, **review-before-send + redaction enforcement**. **Default model `claude-sonnet-4-6`** (user choice — balanced/cheaper), changeable in Settings.

## Hard rules (security & privacy)

1. **API key lives only in main**, encrypted via Electron `safeStorage`. Never in the renderer, never logged, never committed. Dev fallback: `ANTHROPIC_API_KEY` env. Renderer only ever learns a boolean "key is set".
2. **Only flattened/redacted renders leave the machine** — never raw `shots/*.png`. Main reads `step.flattened` and **fails closed**: a shot step that has a `blur` annotation but no current flattened render throws rather than sending raw pixels. The renderer guarantees every shot step is flattened *before* generate, so the review screen shows exactly what is transmitted.
3. **Explicit consent before any send** — a review-before-send screen lists every transmitted image + metadata + the token/cost estimate; the user confirms before the network call.
4. **AI is a switch.** When SOP generation is turned off (Settings master toggle), no Claude UI is shown and the `claude.*` IPC handlers refuse — the app is fully usable as a local recorder/editor/exporter with zero external calls.

## Settings (new in Phase 3)

A **Settings panel** in the renderer, backed by `settings.ts` (non-secret) + `secrets.ts` (key only):

| Setting | Key | Store | Default | Notes |
|---|---|---|---|---|
| AI SOP generation on/off | `sopEnabled` | settings.ts | `true` | Master toggle. Off → hide all Claude UI + guard `claude.*` IPC. |
| API key | — | **secrets.ts** (safeStorage) | unset | Encrypted ciphertext in `userData/secrets.json`. Renderer sees only "set ✓". |
| Model | `sopModel` | settings.ts | `claude-sonnet-4-6` | Validated against the curated allowlist below. |
| Tone | `sopTone` | settings.ts | `professional` | Preset → system-prompt modifier. |
| Custom instructions | `sopCustomInstructions` | settings.ts | `''` | Optional free text appended to the system prompt (length-capped). |

`settings.ts` `Settings` gains these fields with the same defensive read defaulting it already uses; unknown `sopModel`/`sopTone` values coerce back to the default. The key is **never** in `settings.json`.

### Curated model map (single source for picker + request shaping + pricing)

`src/main/claude-models.ts` exports a frozen map — the picker, the per-model request shaping, and the cost estimate all read from it, so adding a model is one entry and a bad model string can never reach the API:

| Model id | Label | In $/1M | Out $/1M | thinking | effort |
|---|---|---|---|---|---|
| `claude-sonnet-4-6` | Sonnet 4.6 — balanced (default) | 3 | 15 | adaptive | medium |
| `claude-opus-4-8` | Opus 4.8 — most capable | 5 | 25 | adaptive | high |

**Why the map matters:** it centralizes per-model pricing (for the estimate), request shaping (`thinking:{type:'adaptive'}` + `output_config:{effort}` — identical for both models today), and guards against a bad model string reaching the API. Both support **vision + structured outputs + adaptive thinking + effort**. The map is also where a future model with different capabilities (e.g. one where `effort` 400s) would declare them, so the request shaper adapts without touching call sites.

### Tone presets (system-prompt modifier)

| `sopTone` | Effect |
|---|---|
| `professional` (default) | Formal, third-person, SOP-standard phrasing. |
| `friendly` | Warm, second-person, approachable. |
| `concise` | Minimal words, action-first. |
| `detailed` | Thorough; explains the "why" and adds context. |

The system prompt = base SOP instructions + the tone modifier + (optional) custom instructions. Tone/custom changes invalidate the cached system prompt (rare, expected).

## Architecture decisions

**A. Claude runs in the MAIN process.** `@anthropic-ai/sdk` imported in main, **externalized** in `vite.main.config.ts` (like `electron-log`). Pure-JS SDK → no native ABI concern. Renderer triggers via IPC; main streams progress back as `main→renderer` events (the capture-event pattern) so the long call shows a live indicator. `zod` can be bundled.

**B. Secret storage — `src/main/secrets.ts`.** `safeStorage.isEncryptionAvailable()` gate → `encryptString` → store base64 ciphertext in `userData/secrets.json` (separate file so plaintext `settings.json` never holds the key). `getApiKey()` (main-only) prefers the decrypted stored key, falls back to `process.env.ANTHROPIC_API_KEY`. `setApiKey` / `clearApiKey` / `hasApiKey`. On a platform where encryption is unavailable, surface a warning and refuse to persist plaintext.

**C. `src/main/ClaudeService.ts`.** Reads `sopModel`/`sopTone`/`sopCustomInstructions` from settings + the model map; constructs `new Anthropic({ apiKey })` lazily per call (key/model may change). Methods:
- `testKey()` — cheap validation (`models.retrieve(sopModel)` or a 1-token message); maps typed errors (`AuthenticationError`, `APIConnectionError`, `RateLimitError`) to friendly strings.
- `buildSystemPrompt()` — base SOP instructions + tone modifier + custom instructions; `cache_control: ephemeral`.
- `assembleRequest(projectPath)` (internal) — `system` + ordered `messages`: per shot step an **image block** (base64 of the flattened render, read via `resolveProjectFile`) **+** a text block (order, app/window title, caption, note, element if available); authored **text steps** become text blocks tagged as author prose to keep/weave in. Enforces rule #2 (throws on blur-without-flattened).
- `shapeParams(model)` — per the model map: `thinking:{type:'adaptive'}` + `output_config:{effort}`, `max_tokens`, `stream:true`.
- `estimate(projectPath)` → `client.messages.countTokens(...)` → `{ inputTokens, estCostUSD }` using the **selected model's** in/out pricing (+ a rough output allowance) for the review screen.
- `generateSop(projectPath, onProgress)` → `client.messages.stream({ model: sopModel, ...shapeParams, output_config:{ format: zodOutputFormat(SopSchema) }, system, messages })`; emit progress on deltas; `await stream.finalMessage()`; extract text → `JSON.parse` → `SopSchema.parse` → save to `manifest.sop` via the `writeQueue`. Handles `stop_reason` `refusal` / `max_tokens`.
- Every `claude.*` path first checks `sopEnabled` (rule #4) and throws a clear error if off.

**D. SOP schema — `src/shared/sop.ts`.** Typed `SopDoc`: `title`, `overview`, `prerequisites: string[]`, `steps: { stepId: string; heading: string; instruction: string; caution?: string; tip?: string; screenshotStepId?: string }[]`, optional `conclusion`. A matching Zod schema (kept within the structured-output subset — no `minLength`/`maxLength`/numeric bounds). Replace `manifest.sop: unknown | null` with `SopDoc | null`; `readManifest` coerces malformed `sop` to `null`. Steps reference real step ids so the renderer can pair each SOP step with its flattened image. (Storing `sopModel`/`sopTone` used at generation time on the SopDoc is a nice-to-have for provenance.)

**E. Flatten-on-generate.** Before generate, the renderer ensures every **shot** step has a current flattened render: for any shot step missing `flattened`, load via `shot://` + run the existing `editor/flatten.ts` + persist via `projects.updateStep`. Result: main only ever reads `step.flattened`; the review screen shows exactly the transmitted images.

**F. Render + edit the SOP (renderer).** New **SOP view** in the detail screen (tab/section beside the report), shown only when `sopEnabled`. `SopDoc` → formatted HTML with each step's flattened image (via `shot://`). Inline-edit fields (reuse the `InlineInput` helper); **Save edits** (`projects.saveSop`); **Regenerate** (full re-run through the review screen). SOP persists in `manifest.sop`.

**G. Export — `src/main/export.ts` (main process).**
- **HTML** — self-contained: inlined CSS + flattened images as base64 `data:` URIs → portable single file in `export/<title>.html`.
- **PDF** — hidden `BrowserWindow` loads the HTML → `webContents.printToPDF` → `export/<title>.pdf` (no new dep, fully offline).
- **Markdown** — assemble from `SopDoc`; write images to `export/images/` referenced relatively → `export/<title>.md`.
- `.docx` deferred (optional).
- IPC `projects.export(projectPath, format)`; on success `shell.showItemInFolder`. Path-confined writes under `export/`. (Export targets the generated SOP; available whenever a SOP exists.)

## Sequenced sub-steps

### 3a — Settings (toggle / key / model / tone) + ClaudeService scaffold + connectivity (de-risk)
De-risks: SDK-in-Electron-main, `safeStorage`, packaging, key hygiene, per-model request shaping.
- Add deps `@anthropic-ai/sdk` + `zod`; externalize the SDK in `vite.main.config.ts`; **re-run `npm run postinstall`** to restore `node-screenshots-win32-x64-msvc` (the known arm64-host optional-deps prune).
- `src/main/claude-models.ts` (model map). `src/main/secrets.ts` (safeStorage). `settings.ts` gains `sopEnabled`/`sopModel`/`sopTone`/`sopCustomInstructions` with defensive defaulting + allowlist coercion. `src/main/ClaudeService.ts` (`testKey`, `buildSystemPrompt`, `shapeParams`).
- IPC + preload + `ShotaiApi`: `settings.getSop()` / `settings.setSop(partial)` (toggle/model/tone/custom), `claude.hasApiKey()` / `setApiKey(key)` / `clearApiKey()` / `testKey()`. Channels in `shared/ipc.ts`.
- Renderer **Settings** panel: master **AI on/off** toggle at top; below it (enabled only when on) → API key enter/Save (encrypted)/Test/Clear (shows "set ✓", never the value), **model** dropdown (from the map), **tone** dropdown, **custom instructions** textarea.
- **Verify:** master toggle hides/shows the AI section + Claude UI; real key Tests OK on the selected model; settings persist across restart; `settings.json` holds `sopModel`/`sopTone`/`sopEnabled` but **never the key** (only `secrets.json` ciphertext); key absent from logs + renderer.

### 3b — SOP generation (the core)
- `shared/sop.ts` (`SopDoc` + Zod); `manifest.sop` retyped; `readManifest` coercion.
- Flatten-on-generate (renderer) + `ClaudeService.assembleRequest/estimate/generateSop` (main) using the selected model+tone, with redaction enforcement + per-model param shaping; `ProjectStore.saveSop` (writeQueue).
- IPC: `claude.estimate(projectPath)`, `claude.generateSop(projectPath)`, `claude.onProgress(cb)`, `projects.saveSop(projectPath, sop)`.
- Renderer: **review-before-send** modal (transmitted flattened thumbnails + metadata + the **selected model** + token/cost estimate at that model's rates + consent) → progress → **SOP view** (render + inline-edit + Save + Regenerate).
- **Verify** (PLAN step 3): with a key, generate a SOP **on Sonnet 4.6** for a small project that includes a **redacted** step + an **authored text step**; switch the model to Opus 4.8 and confirm it generates and the estimate reflects the right price; the review screen lists exactly the flattened images (redaction visible); the call streams; a structured SOP renders; tone changes visibly alter the output; edit a field + Save persists; reopen shows the saved SOP; with the master toggle **off**, all of this is hidden and `claude.*` refuses.

### 3c — Export (HTML / PDF / Markdown)
- `src/main/export.ts` (HTML self-contained, PDF via `printToPDF`, MD + `export/images/`); IPC `projects.export`; renderer Export buttons + reveal-in-folder.
- **Verify:** export each format; HTML opens standalone with images; PDF renders with screenshots; MD references images; all land under `export/`.

## Risks & mitigations (ranked)
1. **API-key leakage** → main-only, `safeStorage`-encrypted, never logged/sent/committed; env fallback for dev; renderer sees only a boolean.
2. **Redaction leaking to Claude** (security-critical) → send only flattened renders; main fails closed on blur-without-flattened; renderer flattens-first; review screen shows the transmitted images.
3. **Bad/unsupported model strings** → the `claude-models.ts` capability map is the only source of model ids; the picker offers only mapped models; unknown `sopModel` coerces to the default; the map shapes each request so a future model with different thinking/effort support can't cause a 400.
4. **SDK / packaging on the arm64-host x64 build** → externalize `@anthropic-ai/sdk`; re-run `postinstall` to restore the node-screenshots x64 prebuild after `npm install`.
5. **Structured-output schema limits** → keep the Zod schema in the supported subset; validate `finalMessage` with Zod; handle `refusal` / `max_tokens` stop reasons.
6. **Long calls / timeouts** → stream + `finalMessage`; progress events; model-appropriate thinking/effort.
7. **Cost surprises** → per-model `count_tokens` pre-count + explicit consent before any send.
8. **Network / no-key / rate-limit** → typed-error handling surfaced to the UI; nothing fails silently (mirrors the capture `onError` banner).
9. **`printToPDF` on the software-rendered VM** → verify on the user's box; HTML+MD are the always-works fallbacks.
