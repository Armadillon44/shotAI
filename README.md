# shotAI

A **local-first SOP builder** — a Scribe-style desktop app. You record a process on screen; shotAI captures a screenshot + click for each step and produces an editable, annotated step-by-step guide. Its differentiator: **Claude** turns the captured steps into a polished Standard Operating Procedure (overview, prerequisites, per-step instructions, cautions).

Everything runs locally except the Claude API call. **Windows first**, macOS later.

> **Status:** early development — Phase 0 (scaffold). See [docs/PLAN.md](docs/PLAN.md) for the full product plan and phased roadmap.

## Tech stack

- **Electron** (main + sandboxed renderers) via **Electron Forge** + **Vite**
- **React 19** + **TypeScript** in the renderer
- Two windows: a frameless always-on-top **capture toolbar** and the main **project window**
- Planned: `node-screenshots` (capture), `uiohook-napi` (clicks), `get-windows` (active window), Konva (annotation editor), `@anthropic-ai/sdk` (SOP generation)

## Architecture (target)

```
Main process (Node)        Renderer (React + Vite)
- CaptureController        - Capture toolbar window  (toolbar.html)
- RegionService           - Project window          (index.html)
- ProjectStore              · step list, report, Konva editor, SOP, export
- ClaudeService
   └── typed contextBridge IPC ──┘   (contextIsolation on, nodeIntegration off)
```

## Prerequisites

- **Node.js** LTS (developed on Node 24) and **npm** 11
- Windows 10/11. On **Windows on ARM**, see the x64 note below.

## Getting started

```sh
npm install      # also fetches the x64 Electron binary (see x64 note)
npm start        # launch the app (Forge + Vite dev servers)
```

Packaging (x64 Windows):

```sh
npm run package  # unpacked app  (--arch=x64)
npm run make     # Squirrel installer (--arch=x64)
```

## x64-first on Windows-on-ARM

shotAI builds and ships **x64** and runs emulated on Windows-on-ARM — one architecture, one set of native binaries. The x64 Electron binary is fetched by [scripts/postinstall-electron.mjs](scripts/postinstall-electron.mjs) on every `npm install` (this also works around npm 11's `allow-scripts` gating, which otherwise blocks Electron's binary download). Full rationale and the native-module plan are in [docs/PLAN.md](docs/PLAN.md).

> In a headless/CI environment with no usable GPU, set `SHOTAI_DISABLE_GPU=1` before `npm start` to fall back to software rendering. Not needed on a normal desktop.

## Project layout

```
index.html / toolbar.html   renderer entry HTML (project / toolbar windows)
src/main/                    Electron main process
src/preload/                 sandboxed contextBridge preload
src/renderer/project/        project window (React)
src/renderer/toolbar/        capture toolbar window (React)
scripts/                     build/postinstall helpers
docs/PLAN.md                 product plan + roadmap
```

## License

MIT
