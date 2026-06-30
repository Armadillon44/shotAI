# shotAI

A **local-first SOP builder** — a Scribe-style desktop app. You record a process on screen; shotAI captures a screenshot + the clicked UI element for each step and produces an editable, annotated step-by-step guide. Its differentiator: **Claude** turns the captured steps into a polished Standard Operating Procedure (overview, prerequisites, per-step instructions, cautions/callouts).

Everything runs locally except the Claude API call. **Windows first**, macOS later.

> **Status:** working app. Capture engine, inline Konva annotation editor, Claude SOP generation + one-click revert, element-at-point captions (native UI-Automation), auto-redaction (local OCR), callouts, and HTML / PDF / Markdown / "HTML for Word" export are all implemented. See [docs/PLAN.md](docs/PLAN.md) for the product roadmap and [docs/HARDENING-PLAN.md](docs/HARDENING-PLAN.md) for the in-progress hardening/cleanup work.

## Tech stack

- **Electron** (main + sandboxed renderers) via **Electron Forge** + **Vite**
- **React 19** + **TypeScript** in the renderer; **Zustand** state; **Zod** validation
- Three windows: a frameless always-on-top **capture toolbar** (`toolbar.html`), the main **project window** (`index.html`), and a transparent **area-select overlay** (`overlay.html`)
- Capture/runtime native deps: `node-screenshots` (screenshots), `uiohook-napi` (clicks), `get-windows` (active window), `koffi` (FFI to a small Rust UI-Automation addon in `native/element-locator/` for the clicked-element name)
- `konva` / `react-konva` (annotation editor), `tesseract.js` (local OCR for auto-redaction), `@anthropic-ai/sdk` (SOP generation)

## Architecture

```
Main process (Node)          Renderer (React + Vite)
- CaptureController          - Capture toolbar window   (toolbar.html)
- RegionService             - Project window           (index.html)
- ProjectStore                · step list, report, Konva editor, SOP, export
- ClaudeService             - Area-select overlay      (overlay.html)
- ElementLocator (koffi → native/element-locator .dll)
- export / ocr / secrets
   └── typed contextBridge IPC ──┘   (contextIsolation on, nodeIntegration off, sandbox on)
```

## Prerequisites

- **Node.js** LTS (developed on Node 24) and **npm** 11
- Windows 10/11. On **Windows on ARM**, see the x64 note below.

## Getting started

```sh
npm install      # restores the x64 native binaries (see x64 note)
npm start        # launch the app (Forge + Vite dev servers)
npm test         # vitest unit tests
npm run lint     # eslint
```

Packaging (x64 Windows):

```sh
npm run package  # unpacked app  (--arch=x64)
npm run make     # Squirrel installer (--arch=x64)
```

Rebuild the native element-locator addon (needs Rust via rustup; sets a local `CARGO_TARGET_DIR`):

```sh
npm run build:element-locator
```

The Claude API key is **bring-your-own**: set it in Settings (stored encrypted via Electron `safeStorage`) or via the `ANTHROPIC_API_KEY` environment variable. The default model is `claude-sonnet-4-6` (Opus 4.8 selectable in Settings).

## x64-first on Windows-on-ARM

shotAI builds and ships **x64** and runs emulated on Windows-on-ARM — one architecture, one set of native binaries. [scripts/postinstall.mjs](scripts/postinstall.mjs) runs on every `npm install` to restore the x64 Electron binary and the x64 prebuilds for `node-screenshots` / `koffi` (npm prunes these win32-x64 optional packages on an arm64 host). Full rationale and the native-module plan are in [docs/PLAN.md](docs/PLAN.md).

> Software rendering is the **default** (the dev VM's virtualized GPU can't create a graphics context). On GPU-capable hardware, set `SHOTAI_ENABLE_GPU=1` before `npm start` to enable hardware acceleration.

## Project layout

```
index.html / toolbar.html / overlay.html   renderer entry HTML (3 windows)
src/main/                    Electron main process (capture, store, Claude, export, OCR, IPC)
src/preload/                 sandboxed contextBridge preload
src/renderer/project/        project window: home, report, settings, SOP panel (React)
src/renderer/editor/         inline Konva annotation editor + flatten/redaction bake
src/renderer/toolbar/        capture toolbar window (React)
src/renderer/overlay/        area drag-select overlay (React)
src/shared/                  IPC contract + types shared by main and renderer
native/element-locator/      Rust cdylib (UI-Automation element-at-point) loaded via koffi
assets/                      app icon (png + ico)
scripts/                     build/postinstall helpers
docs/                        product plan, phase notes, hardening plan
```

## License

MIT
