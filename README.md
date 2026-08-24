# shotAI

A **local-first SOP builder** for Windows. Record a process on screen; shotAI captures a
screenshot and the clicked UI element for each step, then turns them into an editable,
annotated step-by-step guide. Its differentiator: **Claude** rewrites the captured steps
into a polished Standard Operating Procedure — overview, per-step instructions, and
cautions/callouts.

Everything runs and is stored **on your machine**, and every network call it makes is named.
Three of them happen **only when you act**: Anthropic's API when you ask shotAI to write the
SOP, Microsoft (`login.microsoftonline.com`) when you sign in and when shotAI renews that
sign-in for a request you made, and a
token exchange with Anthropic (`POST /v1/oauth/token`) that turns that sign-in into a
short-lived Claude credential. Exactly one is **automatic**: a once-a-day check for a newer
shotAI release (switchable off in Settings). **Windows first**, macOS later.

> **Status:** **1.1.6** — shotAI now **tells you when a newer version is available**:
> once a day at startup it checks the Releases page and shows a notice with a download
> link. It never installs anything itself, says nothing when you're up to date, and can
> be switched off in **Settings → About** (it's the only time the app contacts the
> internet on its own). 1.1.5 made the **HTML export paste into a knowledge-base article**
> (Freshservice and similar): step cards hold the document column instead of stretching
> to the editor's full width, and screenshots are embedded as **AVIF**, which cuts a
> 13-step SOP from ~48 MB to ~170 KB — small enough for the editor to accept, while
> carrying enough detail to stay readable. Export shows per-image progress, and the
> home list no longer carries its scroll position into a project. 1.1.4 made
> **HTML (for Word)** size its screenshots so they
> paste into Word / Google Docs at a sane width instead of full resolution, the
> **capture pill surfaces an error** if a capture fails mid-recording (the main
> window is hidden then, so a long recording could previously fail silently), and
> the installer graphic is darker, rounded and smaller. 1.1.3 was a cross-platform
> **parity** pass: the report and HTML
> export now render at the same dimensions as the macOS app (matching column /
> `.doc` widths and section-divider styling). 1.1.2 added non-counted **section
> dividers** (mark a new phase with a heading that isn't a numbered step, in the
> report and every export) and **centers narrow captures** in a uniform column
> width. 1.1.1 added **step
> framing** (every step reads as a distinct card in the report and all exports),
> **choose where to save an export** (single Save dialog, or bulk to each project's
> own folder / one shared folder; Markdown saves as a self-contained folder), a
> **home list that auto-refreshes**, and an Arial-styled simple HTML export. 1.1.0
> added **project search** (by title *and* in-project text), **mid-report inserts**
> (drop a recorded **+Capture** session or a no-click **+Screenshot** into any gap),
> **bounded log rotation**, and removed the redundant per-step *note* field. The 1.0
> foundation — capture engine, inline Konva
> annotation editor, redaction (manual + local-OCR auto-redact), Claude SOP generation with
> review-before-send + one-click revert, element-at-point captions (native UI Automation),
> export to HTML / Word / PowerPoint / PDF / Markdown + a shareable round-trip package,
> project archiving, and light/dark themes — is all in place. See
> [docs/PLAN.md](docs/PLAN.md) for the product roadmap and
> [docs/HARDENING-PLAN.md](docs/HARDENING-PLAN.md) for the hardening/feature history.

## How it works

1. **Record.** Choose a capture mode — whole **Screen**, a single **Window**, a dragged
   **Area**, or **Auto** (picks per click) — name the project, and click through your
   process in any app. For each click shotAI records a **screenshot**, the **active
   window**, the **name of the UI element** you clicked (via native UI Automation), and
   **where** you clicked (drawn as a marker on the step). A small always-on-top toolbar
   shows progress, and the app hides itself so it never lands in the shots. Press
   **Ctrl+Shift+S** to grab the current screen on demand — useful for surfaces the mouse
   hook can't see, like the Windows Start menu. Right-click menus are captured as their own
   step, and double-clicks are collapsed into one.

2. **Review & annotate.** Each step is an editable card — reorder, retitle, or convert a
   text step to a **callout** (note / caution / warning), which then drops out of the
   numbered sequence and everything renumbers automatically. **Insert** new steps into any
   gap: a **text** block, an **image**, a fresh **+ Capture** recording (pick a mode and
   click through more steps, spliced in place), or a **+ Screenshot** one-shot (whole
   screen, a chosen window, or a dragged area — no click needed). Open the image editor to
   draw arrows, boxes, and text, adjust the click marker, **crop**, or **redact** sensitive
   regions. Redactions are **baked into a flattened copy** of the screenshot; the original
   pixels never leave your machine for any export or AI request (the export/send path is
   fail-closed and refuses a step whose redactions aren't baked). **Auto-redact** runs
   **local OCR** (offline) to find and suggest sensitive text. A quality slider controls
   how much screenshots are downscaled (file size vs. sharpness).

3. **Generate the SOP (optional).** Signed in with your **work account**, or with **your own**
   Anthropic API key, Claude reads the redaction-baked screenshots + captions and writes
   the guide **in place**: an overview,
   section headings, and per-step titles + instructions, in your chosen **tone** and
   **effort**. Before anything is sent you see exactly which screenshots go out and an
   **estimated cost**; a single click **reverts** to your pre-AI version.

4. **Export & share.** Export to **HTML**, **Word** (`.docx`), **PowerPoint** (`.pptx`),
   **PDF**, **Markdown**, or **HTML-for-Word** (paste into Word/Docs). Each is footed with
   "Created on <date>", optionally "by <your name>". Or export a **shareable package** that
   another shotAI user can **import** and keep editing.

5. **Manage.** The home screen lists projects with **search** (matches the project title
   *and* text inside it — step captions, instructions, and the SOP overview — ranking title
   hits first), sort + **date grouping** (This Week / Last Week / This Month / Last Month /
   Older), a **Draft / SOP-ready** status chip, and **multi-select** for bulk **archive /
   export / delete**. **Archiving** compresses a
   project in place (into a single zip) to save disk while keeping it listed under an
   **Archive** tab; opening an archived project restores it automatically, and old projects
   can auto-archive by age. A **light / dark / system** theme is under Settings → Appearance.

### Privacy & local-first

Projects (screenshots, manifest, exports) live in a folder you choose. No project content is
uploaded except in SOP-generation requests, which go only to Anthropic (`api.anthropic.com`,
pinned). The API key is yours, stored encrypted via Electron `safeStorage` (or read from
`ANTHROPIC_API_KEY`). **No telemetry.**

**Microsoft sign-in**, where an organization has configured it, sends nothing about your
projects. Clicking **Sign in with Microsoft** opens your browser against
`login.microsoftonline.com` for the configured tenant and requests one delegated scope on the
app registration your administrator set up; shotAI then posts the resulting Entra access token
to Anthropic's token endpoint and receives a short-lived Claude credential. The same silent
renewal reaches Microsoft again without a click, when **Test connection** runs and when a
generation you started needs a fresh token. The MSAL token
cache is persisted **encrypted via Electron `safeStorage`** and is never written in plaintext;
a cache that will not decrypt is treated as a cache miss, so you sign in again rather than
hitting an error. The minted Claude token is short-lived, never written to disk, and never
shown. What the renderer is told is deliberately narrow: the signed-in account, whether
federation is configured, whether a session is signed in, whether OS encryption is available,
and where **Request access** points. It carries no token, no expiry, no scope, no request id,
and no claims, because the renderer has no decision to make with them. Still **no
telemetry** in this mode either.

The only call shotAI makes **on its own** is the **update check**: once a day at startup, a
plain GET to GitHub's public releases API for this repo, to see whether a newer version
exists. It sends nothing about you or your projects, is unauthenticated, and can be turned off
in **Settings → About**. shotAI never downloads or installs an update by itself, it only
tells you one exists and offers the release page.

The renderer runs sandboxed, and its "open in browser" path is allowlisted to
`anthropic.com` (+ subdomains) for the API-key docs, `github.com` (matched exactly) for that
release page, and, when Microsoft sign-in is configured, the configured support URL. That last
one is matched by **exact origin**, so it cannot widen into sibling hosts.

## Tech stack

- **Electron** (main + sandboxed renderers) via **Electron Forge** + **Vite**
- **React 19** + **TypeScript** in the renderer; **Zustand** state; **Zod** validation
- Three windows: a frameless always-on-top **capture toolbar** (`toolbar.html`), the main
  **project window** (`index.html`), and a transparent **area-select overlay** (`overlay.html`)
- Capture/runtime native deps: `node-screenshots` (screenshots), `uiohook-napi` (global
  click hook), `get-windows` (active window), `koffi` (FFI to a small Rust UI-Automation
  addon in `native/element-locator/` for the clicked-element name)
- `konva` / `react-konva` (annotation editor), `tesseract.js` (local OCR for auto-redaction)
- `@anthropic-ai/sdk` (SOP generation, and its OIDC-federation credential provider);
  `@azure/msal-node` (Microsoft sign-in; its token cache is persisted by shotAI, encrypted
  via Electron `safeStorage`)
- `docx` / `pptxgenjs` / `jszip` (Word / PowerPoint / package export + import)

## Architecture

```
Main process (Node)                 Renderer (React + Vite)
- CaptureController (hook+screens)   - Capture toolbar window   (toolbar.html)
- RegionService (area overlay)       - Project window           (index.html)
- ProjectStore (+ archive)             · home list, report, Konva editor, SOP, export
- ClaudeService (SOP gen)            - Area-select overlay      (overlay.html)
- export / export-docx / export-pptx / export-package
- ElementLocator (koffi → native/element-locator .dll), ocr, secrets, settings
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
npm run package  # unpacked app       (--arch=x64)
npm run make     # Squirrel installer (--arch=x64) → out/make/squirrel.windows/x64/
```

Rebuild the native element-locator addon (needs Rust via rustup; sets a local `CARGO_TARGET_DIR`):

```sh
npm run build:element-locator
```

## Silent install (deployment)

The installer is a **Squirrel** `Setup.exe`, so it's already low-friction: a normal
double-click does a **per-user** install to `%LocalAppData%\shotAI` (no admin), shows a brief
progress animation, then launches the app. There's no Next/Next/Finish wizard. For scripted or
managed rollouts, pass `--silent`:

```sh
shotAI-<version>-Setup.exe --silent
```

`--silent` suppresses the progress animation and does **not** auto-launch the app after
installing. It's a Squirrel-style switch, so use `--silent` (not `/S`, `/quiet`, or `/qn` —
those belong to NSIS/MSI installers; shotAI isn't one).

Notes for deployment:

- **Deploy in user context.** shotAI installs into the signed-in user's profile, so a
  system/device-context push (e.g. Intune in device context) has no profile to install into.
- **Uninstall** via **Settings → Apps** (Installed apps), or the per-user Squirrel uninstaller
  under `%LocalAppData%\shotAI`.
- **Not code-signed yet.** `--silent` hides shotAI's own UI but does **not** bypass Windows
  SmartScreen/Defender — a managed environment may need to allow/trust the app until
  code-signing lands.

See the wiki [Installation](https://github.com/Armadillon44/shotAI/wiki/Installation) page for
the end-user walkthrough and the same deployment notes.

## Claude access: work account or API key

SOP generation needs one of two credentials, and is off until it has one. Capture, editing,
redaction, and export all work regardless; only the AI SOP step is unavailable without a
credential. shotAI uses **Claude Sonnet 5** by default in either mode; the **tone**
(Professional / Friendly / Concise / Detailed) and **effort** (Low / Medium / High) are
configurable.

**Microsoft sign-in (work account).** Where an administrator has configured federation for the
machine, **Settings → AI** grows a **Microsoft sign-in** group once **AI SOP generation** is
switched on: **Sign in with Microsoft**
opens your browser, and from then on shotAI uses that work account for AI features. No API key
is involved, and access is granted or revoked by the organization rather than by a key a user
holds. **Test connection** reports its three legs separately (**Microsoft sign-in**, **Claude
access**, **Claude API**), because every access denial from Anthropic is deliberately the same
opaque 401 and the legs are the only way to tell them apart. The trap worth knowing: once
access is granted, the user has to use **Sign in with Microsoft** again. A silent retry
re-serves a cached Entra token that predates the grant and carries no role claim, which looks
exactly like broken configuration.

**Bring-your-own-key.** Set a key in **Settings → AI** (stored encrypted via Electron
`safeStorage`) or via the `ANTHROPIC_API_KEY` environment variable. With federation configured
the key field is still there, collapsed under **Use my own Anthropic API key instead**; with
no federation configured the **Anthropic API key** group is shown outright, so nothing changes
for anyone outside a configured organization.

Precedence, exactly:

1. Federation configured **and** signed in: **federated**. The API key is not consulted at
   all, and `apiKey` is explicitly set to `null`, so a leftover `ANTHROPIC_API_KEY` cannot
   silently disable federation.
2. **Not signed in**: the API key (saved in-app, or `ANTHROPIC_API_KEY`).
3. **Neither**: generation refuses, with "Sign in with your Microsoft account to use Claude."
   when federation is configured and "Add an Anthropic API key in Settings to use Claude."
   when it is not.

Signing in therefore takes precedence over a stored key. A machine with federation configured
but nobody signed in still works off an existing key, which is deliberate: a rollout strands
nobody.

Administrators: the federation settings are normally baked into the installer at build time
and can be overridden by policy. See [docs/MANAGED-CONFIG.md](docs/MANAGED-CONFIG.md) for the
settings themselves and [Intune/Windows/README.md](Intune/Windows/README.md) for the ADMX and
the Intune-side rollout.

## Rendering (GPU)

Hardware acceleration is **on by default**. shotAI auto-disables the GPU **only** when it
detects x64 running under **Windows-on-ARM emulation** (where creating a graphics context
aborts startup). Override with `SHOTAI_ENABLE_GPU=1` (force on) or `SHOTAI_ENABLE_GPU=0`
(force off) before `npm start`.

## x64-first on Windows-on-ARM

shotAI builds and ships **x64** and runs emulated on Windows-on-ARM — one architecture, one
set of native binaries. [scripts/postinstall.mjs](scripts/postinstall.mjs) runs on every
`npm install` to restore the x64 Electron binary and the x64 prebuilds for `node-screenshots`
/ `koffi` / `get-windows` (npm prunes these `win32-x64` optional packages on an arm64 host).
Full rationale and the native-module plan are in [docs/PLAN.md](docs/PLAN.md).

## Project layout

```
index.html / toolbar.html / overlay.html   renderer entry HTML (3 windows)
src/main/                    Electron main process:
                               capture, project store + archive, Claude, export
                               (html/pdf/markdown/docx/pptx/package), OCR, settings, IPC
src/preload/                 sandboxed contextBridge preload
src/renderer/project/        project window: home list, report, settings, SOP panel (React)
src/renderer/editor/         inline Konva annotation editor + flatten/redaction bake
src/renderer/toolbar/        capture toolbar window (React)
src/renderer/overlay/        area drag-select overlay (React)
src/shared/                  IPC contract + types shared by main and renderer
native/element-locator/      Rust cdylib (UI-Automation element-at-point) loaded via koffi
assets/                      app icon (png + ico) + installer graphic
scripts/                     build / postinstall helpers
docs/                        product plan, phase notes, hardening/feature history
```

## License

MIT
