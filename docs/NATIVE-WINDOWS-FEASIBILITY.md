# shotAI on native Windows (C#/.NET): feasibility assessment

**Date:** 2026-09-22 · **Source analyzed:** this repo at v1.3.0 + `main` (`38908cd`), the macOS
port [Armadillon44/shotAI_MacOS](https://github.com/Armadillon44/shotAI_MacOS) at `f445bca`, and the
official Anthropic C# SDK (`anthropics/anthropic-sdk-csharp` at `2beeb9f`).

Question: can shotAI drop Electron and Node and be rebuilt on native Windows libraries, and is
that practical?

> **Follow-up:** the rewrite is planned in [docs/native/](native/README.md) (work packages,
> architecture and per-subsystem specs). The scaffold is in [dotnet/](../dotnet/README.md).

## Verdict

**Possible: yes.** Every subsystem has a native Windows equivalent, most of them first-party
Microsoft APIs, and nothing blocks the port. Several get better: OCR, Entra sign-in, the token
cache, networking, and packaging.

**Feasible: yes, at a real but bounded cost.** Business logic and exports port close to
one-to-one. The UI (about 8.4k lines of React plus 4k lines of CSS) and the Konva annotation
editor have no reuse and are rebuilt from scratch. Estimate: **6-8 weeks to v1.3.0 parity** at
the pace this project has run, then a pilot (basis under [Effort](#effort)).

**Worth it: yes, if the operational gains matter to you.** Users get no new features. The app
will feel quicker, though less of that depends on the rewrite than it seems (see
[Will it feel faster?](#will-it-feel-faster)). What mainly changes is how the app is patched,
deployed and supported:

- **Patching.** Electron bundles its own Chromium and Node, so every Chromium security fix means
  an Electron upgrade, a rebuild and an Intune redeploy. A native build is patched by Microsoft
  (the .NET runtime through Microsoft Update, WebView2 through Edge's updater).
- **Deployment.** An MSI replaces the per-user Squirrel installer: per-machine when Intune
  deploys it, per-user with no administrator rights when a person installs it (spec 12 7.4.5).
- **ARM64.** The app can run natively on ARM64 instead of emulated with the GPU turned off.
- **Fewer moving parts.** No IPC bridge, preload, renderer sandbox, `shot://` protocol handler,
  asar unpack rules or native-module packaging workarounds.

If the only motivation is "native feels better," it isn't worth two months and the regression
risk.

**The original reason for Electron is gone.** [PLAN.md](PLAN.md) chose Electron for "one
codebase for Windows + macOS." macOS has since shipped as a separate native Swift app. There are
already two codebases, so a native Windows rewrite swaps one of them rather than adding one.

## Calibration: this has already been done once

The macOS app is a native port of this repo, and its history is the best estimate available for
a Windows port:

| | Windows (Electron, this repo) | macOS (native Swift) |
|---|---|---|
| Source (non-test) | ~26.5k lines: main 11.4k, renderer TSX 8.4k, CSS 4.0k, shared 2.5k, preload 0.2k | ~23.8k lines of Swift |
| Tests | ~6.8k lines (~557 cases) | ~8.4k lines (301 tests at its 2026-07-28 audit) |
| Timeline | 1.0.0-rc1 in 8 days (2026-06-24 to 07-02); v1.3.0 on 2026-09-16 | Phase A on 2026-07-02; **1.0.0 on 2026-07-21 (19 days)**; near-parity by 2026-09-17 |
| Parity | reference | Functional parity per its `PARITY.md`, except `.docx`/`.pptx` export (deferred) |

The macOS port also proved the three things a Windows port depends on:

1. A second codec can keep `project.json` byte-compatible. `contract/` plus the shared
   conformance suite enforce it.
2. A native editor can keep the fail-closed redaction bake (`EditorKit/Flatten.swift` plus tests).
3. A native report can match export geometry when tests pin it
   (`DocScaleTests.reportFigureMatchesExport`).

## Recommended stack

- **C# on .NET 10 (LTS, supported to November 2028) with WPF.**
  - WPF over WinUI 3 because the area-select overlay and the capture pill need per-pixel
    transparent, topmost, borderless windows. WPF does this with `AllowsTransparency`; Microsoft
    documents that WinUI 3 doesn't support transparent backgrounds.
  - WPF also has mature custom-drawing primitives for the editor: `DrawingVisual`, adorners,
    `Thumb` and `RenderTargetBitmap`.
  - WinRT APIs (OCR, WIC) are still callable from WPF by targeting a Windows TFM.
- **Framework-dependent deployment.** Deploy the .NET Desktop Runtime through Intune and let
  Microsoft Update service it on Patch Tuesday. A self-contained build would freeze the runtime
  until the next shotAI release.
- **Installer:** one MSI (WiX), deployed per-machine in device context as an Intune Win32 app
  and installable per-user by hand. The plan chose it over MSIX (spec 12 7.1, 7.4.5).
- **Architectures:** x64 and native ARM64.
- **Win32 interop:** [CsWin32](https://github.com/microsoft/CsWin32) (Microsoft's P/Invoke source
  generator) for hooks, capture, window info, display affinity and DPI.

## Subsystem map

| Concern | Today (Electron) | Native Windows | Delta |
|---|---|---|---|
| Global clicks | `uiohook-napi` | `WH_MOUSE_LL` on a **dedicated thread with its own message loop** that hands each event to a queue and returns immediately (Microsoft's guidance), or Raw Input (`RIDEV_INPUTSINK` + `GetCursorPos`) | Same OS mechanism uiohook uses. Design constraint below. |
| Global hotkey | `globalShortcut` | `RegisterHotKey` | Trivial |
| Screenshots | `node-screenshots` (BitBlt monitor grabs, per-window capture) | The same Win32 calls through CsWin32, or DXGI Desktop Duplication. **Not** Windows.Graphics.Capture: an unpackaged app gets a yellow border on every capture, and borderless needs MSIX, a restricted capability and a user consent prompt. | Direct port |
| Own-window exclusion | `setContentProtection` (= `WDA_EXCLUDEFROMCAPTURE`) + the ref-counted shield in `src/main/remote-visibility.ts` | `SetWindowDisplayAffinity` directly. The shield logic ports as-is. | Same mechanism |
| Active window | `get-windows` | `GetForegroundWindow`, `GetWindowTextW`, `QueryFullProcessImageNameW`, `DwmGetWindowAttribute(DWMWA_EXTENDED_FRAME_BOUNDS)` | Trivial. Removes the dependency with no ARM64 prebuild. |
| Element at click | Rust cdylib (`native/element-locator/`) loaded via `koffi` | UI Automation from C# (FlaUI.UIA3 or direct COM interop), same climb-to-actionable-ancestor walk, on an MTA worker thread. The existing DLL can also be kept via P/Invoke at first. | Removes koffi and, once ported, the Rust toolchain |
| Context-menu capture | Poll timer keeps the latest monitor frame (`CaptureController.ts`) | Ports as-is. Optionally, Desktop Duplication keeps a "latest frame" without polling. | Same design available |
| Resize / PNG encode | `nativeImage` | WIC through WPF (`TransformedBitmap`, `PngBitmapEncoder`), off the UI thread | Direct |
| Annotation editor | Konva / react-konva (~1.8k lines incl. flatten) | Custom WPF canvas: vector annotations, selection and resize adorners, inline `TextBox` for text, crop/pan/zoom | **Biggest rebuild** |
| Redaction bake | Canvas 2D in the renderer (`src/renderer/editor/flatten.ts`) | Offscreen `RenderTargetBitmap`/`WriteableBitmap`: one code path in one process. Port the flatten tests first and treat them as the spec. | Simpler, still security-critical |
| OCR pre-scan | `tesseract.js` (WASM) + vendored `eng.traineddata.gz` (~3 MB) | `Windows.Media.Ocr` (Windows 10+, offline, word bounding boxes). `redact-detect.ts` ports 1:1. Needs the OCR language installed (present on en-US installs). | Better, nothing vendored |
| AVIF (styled HTML export) | `@jsquash/avif` (libavif compiled to WASM) | **No dependable built-in path.** WIC writes AVIF only when the HEIF and AV1 codec extensions from the Store and an AV1 encoder are present. Bundle native libavif (BSD) with an AV1 encoder. | The one new native dependency |
| Word / PowerPoint | `docx`, `pptxgenjs` | Open XML SDK (`DocumentFormat.OpenXml`, MIT, Microsoft) | Lower-level API, more lines. Also the one feature macOS never built. |
| PDF | Hidden `BrowserWindow` + `printToPDF` | WebView2 `PrintToPdfAsync` of the same export HTML, JavaScript off. The Evergreen runtime ships with Windows 11 and most Windows 10 devices, and Microsoft 365 Apps installs it. Alternative: draw natively with PDFsharp/MigraDoc (MIT), as macOS draws with CoreText. | Same output via WebView2 |
| HTML / Markdown export | String templates (`export.ts`, `export-css.ts`) | Ported verbatim, as macOS did | Direct |
| Package / archive | `jszip` | `System.IO.Compression` | Direct |
| Claude | `@anthropic-ai/sdk` + Zod | Official C# SDK (NuGet `Anthropic`): streaming, token counting, `OutputConfig.Format` JSON schema. No Zod equivalent, so the schema is hand-written or generated from C# types; keep a test pinning it, as `claude-service.ts` does today for `minItems`. | Direct |
| Entra federation | `msal-node` + the TS SDK's `oidcFederationProvider` | MSAL.NET with the WAM broker, feeding the C# SDK's `WorkloadIdentityCredentials` through an `IIdentityTokenProvider` that calls `AcquireTokenSilent`. The fields map 1:1 (`FederationRuleId`, `OrganizationId`, `ServiceAccountId`, `WorkspaceId`, pinned `BaseUrl`), and the client wraps the provider in a proactive-refresh token cache. | Better, see below |
| Token cache | Hand-rolled `safeStorage` `ICachePlugin`, **no cross-process lock** (see the comment in `src/main/main.ts`) | `Microsoft.Identity.Client.Extensions.Msal`: DPAPI-encrypted, cross-process locked | Closes a known gap |
| API key storage | `safeStorage` | DPAPI (`ProtectedData`) or Credential Manager | Direct |
| HTTPS | API traffic on Node's `fetch` (Node's CA bundle, no system proxy); only sign-in uses `net.fetch` (`src/main/entra/auth-core.ts`) | `HttpClient` uses the Windows certificate store and system proxy for **all** traffic, SSE streaming included | Better on corporate networks |
| Policy (ADMX) | `HKLM\SOFTWARE\Policies\shotAI\Federation` | Same key via `Microsoft.Win32.Registry`. The ADMX and Intune profiles don't change. | Unchanged |
| Settings, logs, update check | `settings.json`, `electron-log`, `fetch` | Same JSON in `%APPDATA%\shotAI`, `Microsoft.Extensions.Logging` with a rotating file sink, `HttpClient` | Direct |
| IPC / preload / sandbox / CSP / `shot://` | `src/main/ipc.ts` (74 handlers), `src/shared/ipc.ts`, `src/preload/preload.ts`: ~1.8k lines plus hardening | **Deleted.** One process, direct calls. | ~1.8k lines and a security surface removed |
| Packaging workarounds | `forge.config.ts` node_modules copy, asar unpack, `scripts/postinstall.mjs`, `gpu-policy.ts`, `arp-icon.ts` | Deleted | Gone |
| Installer | Squirrel, per-user, user-context only, unsigned | MSI, signed: per-machine through Intune, per-user by hand | See [Alternatives](#alternatives-to-a-full-rewrite) |

## What gets better

1. **Patch responsibility moves to Microsoft.** Electron ships a new major about every 8 weeks
   and supports only the latest three. Staying patched means an Electron upgrade, a rebuild and
   an Intune redeploy several times a year. Native: the .NET runtime is serviced through
   Microsoft Update, and the Evergreen WebView2 runtime (PDF only) takes Edge's security updates
   automatically.
2. **Footprint.** One process instead of Electron's main, GPU and renderer processes. Tens of MB
   installed instead of a bundled Chromium and Node (the macOS feasibility study put the Electron
   build at about 250 MB).
3. **Deployment.** The MSI installs per-machine in device context with standard detection rules,
   and a person can still install it per-user without administrator rights. This removes the
   user-context-only restriction, the `--silent` Squirrel quirks and the
   "Installed apps" icon workaround.
4. **Native ARM64.** The dev machine is Windows on ARM. It runs shotAI as x64 under emulation
   with the GPU forced off (`gpu-policy.ts`), and `get-windows` has no ARM64 prebuild, which is
   what forces x64 today.
5. **Sign-in.** WAM gives single sign-on with the Windows account, satisfies device-compliance
   Conditional Access policies natively, and binds refresh tokens to the device. The MSAL
   extension cache adds the cross-process lock that the single-instance lock currently stands in
   for.
6. **Networking.** Proxy and TLS-inspection environments work for SOP generation the same way
   they already work for sign-in.
7. **One language.** C# replaces TypeScript, React, CSS and Rust. For a team that writes
   PowerShell, this is the same .NET class library they already use.

## What gets harder, and the costs

1. **The UI is rebuilt.** Report (`Report.tsx`, 1.2k lines), editor (1.2k), settings (1k), app
   shell (0.9k), project detail (0.7k), home list (0.6k), SOP panel, tour and capture-insert
   modal, plus 4k lines of CSS become XAML and C#. None of it is reusable.
2. **The report stops matching the export for free.** Today the report *is* the export HTML
   ([report-matches-export.test.ts](../src/shared/report-matches-export.test.ts)). A native
   report re-derives the geometry and has to be pinned by tests, which is what macOS did. Hosting
   the report in WebView2 would keep the match free, but it brings back a web UI and a message
   bridge, which is most of what this rewrite removes.
3. **The capture heuristics are hard-won.** The right-click menu arming, submenu chains,
   double-click collapse, poll frames and remote-visibility shield all took many iterations.
   Port them behavior-for-behavior with their tests, as macOS did (59 capture tests).
4. **Hook timing under .NET.** A low-level hook that misses `LowLevelHooksTimeout` (1 s max on
   Windows 10 1709+) is **silently removed** on Windows 7 and later, with no notification. Keep
   the hook thread allocation-free, do no work in the callback, and add a watchdog that
   re-installs the hook. Alternatively, use Raw Input, which has no timeout.
5. **AVIF** needs a bundled native encoder. It is the only place the port adds a third-party
   native binary.
6. **Open XML SDK is verbose** compared with `docx`/`pptxgenjs`. Expect the Word and PowerPoint
   exporters to grow.
7. **Transition overhead.** Three codebases run in parallel until cutover. Freeze Electron
   features during the port, or every change lands three times.
8. **CI.** Today's suite runs on Linux because no test loads a Windows module (see
   [ci.yml](../.github/workflows/ci.yml)). Keep the model, geometry, SOP and export logic in a
   platform-neutral `net10.0` library so those tests stay on Linux. The WPF and Win32 layer needs
   a Windows runner.
9. **Credentials don't carry over.** Each user signs in once after cutover (usually silent under
   WAM) or re-enters their API key. Project folders, `project.json` and the policy key carry over
   unchanged.

## Will it feel faster?

Yes, but only part of today's "spongey" feel comes from Electron. There are three separate
causes, and they need different fixes.

1. **The test machine.** On the Windows-on-ARM dev VM, shotAI runs as x64 under emulation with
   the GPU forced off (`gpu-policy.ts`), so Chromium draws everything on the CPU. That's the
   worst case for an Electron app, and a native ARM64 build removes both problems. To see what
   the fleet actually gets, judge the current app on an x64 PC with a GPU before crediting the
   whole difference to Electron.
2. **Edits wait for the disk (app design, not Electron).** Eleven edit paths in `Report.tsx`
   work the same way:
   - The change goes to the main process, which re-reads `project.json` and rewrites it
     atomically.
   - The whole step list comes back and every card re-renders. None are memoized.
   - A caption edit shows the old text until the write returns.
   - Reorder, delete and merge drop any click that arrives mid-write (`busyRef`).

   The macOS app uses the same wait-for-disk design. Its round trip is just much shorter:
   in-process, APFS, and no antivirus scanning the temp file. A native Windows port that copied
   it would still pay the NTFS and Defender cost on every edit. The fix is to update the screen
   first and save in the background, and it works in the current Electron app too (mostly
   `Report.tsx` and `store.ts`). The editor's save path carries the redaction bake and should
   stay as it is.
3. **Electron's own overhead.** The IPC hop and manifest serialization on every edit, the
   separate GPU and renderer processes, and a slower cold start. This is the part only a
   rewrite removes.

What won't change: SOP generation time, which is bound by the network and the model. Exports get
somewhat faster, because today's AVIF encoder is the single-threaded WASM build and a native
encoder can use every core.

Set expectations accordingly. A native WPF app will feel like a responsive Windows app, not like
the Mac app: part of the Mac app's feel comes from macOS's own controls and scrolling, which WPF
doesn't reproduce.

## Effort

Estimate: **6-8 weeks to v1.3.0 parity**, then a 2-4 week pilot. This is extrapolated from the
macOS history; it hasn't been measured on Windows. The basis:

- macOS reached 1.0.0 in 19 days against a smaller target: no Entra, brands, document scale,
  AVIF, Word or PowerPoint.
- macOS then spent about 8 more weeks catching up while Windows kept shipping features.
- A Windows port targets a **frozen** v1.3.0 with **two** reference implementations. macOS
  already solved the native report and native editor designs, so those carry over as
  architecture even though the code doesn't.

Relative size: UI + editor > capture engine > exports (Word/PowerPoint/AVIF) > auth > model and
store. The model and store are mostly mechanical, and the conformance suite checks them.

## Phased plan

| Phase | Scope | Exit test |
|---|---|---|
| **A: Model + viewer** | C# `project.json` codec, `ProjectStore` (atomic writes, path confinement including reparse points), archive, a third brand generator from `contract/brand.json`, read-only report | `contract/conformance` green; opens projects written by both existing apps |
| **B: Capture engine** | Hook thread, hotkey, BitBlt/per-window grabs, region modes, overlay and pill windows, display-affinity shield, UIA element-at-point, menu and double-click heuristics, auto-captions | Record the same flow in both Windows builds; step counts, crops and captions match |
| **C: Editor + redaction** | WPF annotation canvas, flatten/bake, click markers, crop/pan/zoom, Windows OCR auto-redact | Ported flatten tests green; a redacted export provably contains no original pixels |
| **D: SOP + exports** | C# SDK client, review-before-send, render gate, MSAL.NET + WAM + `WorkloadIdentityCredentials`, the three-leg connection test; HTML/Markdown verbatim, PDF via WebView2, Word/PowerPoint via Open XML SDK, AVIF via libavif | HTML and Markdown exports byte-identical to Electron's for the same project; PDF, Word and PowerPoint visually equivalent |
| **E: Ship** | Signed MSI or MSIX, Desktop Runtime dependency, same HKLM policy key and ADMX, update check, pilot group | Pilot group runs native; the Electron build stays installable as a rollback (both read the same project folders) |

## Alternatives to a full rewrite

1. **Stay on Electron and fix the deployment pain directly.** Electron can ship as an MSI
   (Forge's WiX maker) or MSIX, the build can be signed, and the pill can be made
   non-activating (`focusable: false`). This
   gets per-machine Intune deployment without a rewrite. It does **not** fix the Chromium patch
   cadence, the footprint, or ARM64 (blocked by `get-windows`). **If deployment is the only pain
   point, do this instead.**
2. **Hybrid: WPF shell with the existing React UI in WebView2.** This keeps the UI and the Konva
   editor, moves capture, exports and auth to C#, and replaces Electron with the OS-serviced
   WebView2. It saves the UI rebuild. It costs a C#-to-JavaScript bridge in place of today's IPC,
   two languages indefinitely, and Node staying in the build toolchain. It also makes Windows the
   only platform with a web UI. It's a viable middle path, but not the recommended end state.

## Bottom line

This is a green light technically. Nothing blocks it, and Microsoft supplies first-party
replacements for all but one dependency (the AVIF encoder). The case for doing it is
operational: Microsoft patches the runtime, the installer fits Intune, the app runs natively on
ARM64, and there are fewer moving parts to maintain. The cost is roughly two months plus a
pilot, concentrated in the UI and editor rebuild. If those operational gains are worth two months
to you, build it on .NET 10 + WPF using the phases above and keep `contract/` as the shared
spec. If only deployment hurts, fix the Electron installer instead.

## Sources

- SetWindowDisplayAffinity / `WDA_EXCLUDEFROMCAPTURE` (Windows 10 2004+):
  https://learn.microsoft.com/windows/win32/api/winuser/nf-winuser-setwindowdisplayaffinity
- LowLevelMouseProc timeout and silent removal:
  https://learn.microsoft.com/windows/win32/winmsg/lowlevelmouseproc
- Windows.Graphics.Capture border and borderless consent:
  https://learn.microsoft.com/uwp/api/windows.graphics.capture.graphicscapturesession.isborderrequired
- Windows.Media.Ocr: https://learn.microsoft.com/uwp/api/windows.media.ocr.ocrengine
- WIC HEIF codec and AV1 (codec availability caveat):
  https://learn.microsoft.com/windows/win32/wic/heif-codec
- WebView2 `PrintToPdfAsync`:
  https://learn.microsoft.com/microsoft-edge/webview2/how-to/print
- WebView2 Evergreen availability:
  https://learn.microsoft.com/microsoft-edge/webview2/concepts/distribution
- WinUI 3 transparency limitation:
  https://learn.microsoft.com/microsoft-edge/webview2/platforms/winui3-windows-app-sdk
- MSAL.NET with WAM: https://learn.microsoft.com/entra/msal/dotnet/acquiring-tokens/desktop-mobile/wam
- MSAL.NET token cache serialization (desktop):
  https://learn.microsoft.com/entra/msal/dotnet/how-to/token-cache-serialization
- .NET support policy and Microsoft Update servicing:
  https://learn.microsoft.com/dotnet/core/releases-and-support ·
  https://learn.microsoft.com/dotnet/core/install/windows
- Anthropic C# SDK (`WorkloadIdentityCredentials`, `IIdentityTokenProvider`,
  `ClientOptions.Credentials`): https://github.com/anthropics/anthropic-sdk-csharp
