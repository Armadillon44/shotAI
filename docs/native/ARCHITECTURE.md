# shotAI native architecture

> The cross-cutting design that every subsystem spec under [`docs/native/spec/`](spec/) plugs into. Sources read: [`docs/NATIVE-WINDOWS-FEASIBILITY.md`](../NATIVE-WINDOWS-FEASIBILITY.md) (274 lines), [`dotnet/README.md`](../../dotnet/README.md), the scaffold (`dotnet/Directory.Build.props`, `dotnet/Directory.Packages.props`, `dotnet/global.json`, `dotnet/ShotAI.slnx`, the four `.csproj` files, `dotnet/src/ShotAI.Platform/NativeMethods.txt`, `CaptureExclusion.cs`, `App.xaml`, `MainWindow.xaml.cs`, `app.manifest`, the harness under `dotnet/tests/ShotAI.Core.Tests/Conformance/`), [`.github/workflows/dotnet.yml`](../../.github/workflows/dotnet.yml), and all twelve subsystem specs (sections 7, 10 and 11 in full, the invariant, edge-case and test sections where this document cites them). Spot reads of the Electron reference (`src/main/main.ts`, `src/main/project-store.ts`, `src/main/remote-visibility.ts`, `src/main/entra/auth-core.ts`, `src/renderer/project/Report.tsx`), the macOS port (`macOS:shotAI/AppModel.swift`, `macOS:Packages/ShotModel/`) and the Anthropic C# SDK project file. Status: written against the twelve verified specs; the cross-spec conflicts found while reading them are resolved in 15.3 and the ones that need an owner's decision are open in 15.4. The twelve specs were consolidated with the 15.3 resolutions on 2026-09-23; the Status column of 15.3 records where each one is applied.

**How to read this document.**

- It fixes what is shared: project layout, packages, composition, threading, persistence, errors, logging, security, data locations, budgets, testing, build and conventions. Subsystem behavior lives in the specs and is cited by ID (for example INV-CAP-3, D-EDIT-2, Q-PKG-4), never restated.
- Where two specs disagree, 15.3 records which one wins and why (`R-ARCH-n`). The resolutions are binding: every spec named in a row states what the resolution says (applied in the 2026-09-23 consolidation, see the row's Status). A row still marked pending, and any conflict found later, is followed from this document until the named spec is edited.
- Architecture-level items carry their own IDs: `INV-ARCH-n` (invariants), `D-ARCH-n` (deliberate changes from Electron), `R-ARCH-n` (cross-spec resolutions), `PB-n` (performance budgets), `AC-ARCH-n` (acceptance criteria), `Q-ARCH-n` (open questions).
- Electron code is cited as `path:line` (repo-relative), macOS code as `macOS:path:line`, other specs by number and ID.
- `\u2014` inside a quoted string stands for the em dash the product string contains; this document never types the character.
- Classes: **REQUIRED** (parity), **IMPROVEMENT** (a deliberate native change, justified), **ELECTRON-ONLY** (disappears natively; the replacement of its intent is named).

**Spec index** (the numbers every document uses):

| No. | File | Prefix | Owns |
|---|---|---|---|
| 01 | [01-model-store.md](spec/01-model-store.md) | MODEL | `project.json` model and codec, JS-semantics JSON, store, write queue, atomic file, path confinement, archive, `ProjectSession` |
| 02 | [02-capture.md](spec/02-capture.md) | CAP | recording engine, hook, hotkey, grabs, shield, UI Automation, captions |
| 03 | [03-windows-shell.md](spec/03-windows-shell.md) | SHELL | startup and exit, single instance, main window, pill, area overlay, menu, crash logging |
| 04 | [04-editor-redaction.md](spec/04-editor-redaction.md) | EDIT | annotation editor, flatten, redaction bake, render gate, OCR auto-redact |
| 05 | [05-report.md](spec/05-report.md) | REP | project detail view, report, optimistic report operations |
| 06 | [06-home-settings-ui.md](spec/06-home-settings-ui.md) | HOME | Home list, Settings, tour, notices, confirm, theme resources |
| 07 | [07-sop-generation.md](spec/07-sop-generation.md) | SOP | Claude SOP generation, request, schema, streaming, apply and revert |
| 08 | [08-auth-secrets-policy.md](spec/08-auth-secrets-policy.md) | AUTH | Entra sign-in, federation, API key, managed policy, shared HTTP |
| 09 | [09-export.md](spec/09-export.md) | EXP | exports (HTML, PDF, Markdown, Word, PowerPoint), shareable package |
| 10 | [10-brand-settings-infra.md](spec/10-brand-settings-infra.md) | INFRA | brand contract and generator, `settings.json`, logging, update check, links, self-tests, fonts |
| 11 | [11-service-boundary.md](spec/11-service-boundary.md) | IPC | service catalog replacing IPC, threading primitives, errors, DI, shutdown, analyzers |
| 12 | [12-packaging-deploy-ci.md](spec/12-packaging-deploy-ci.md) | PKG | build, MSI, signing, native dependency provenance, CI, release, pilot, cutover |

The ordered implementation plan is `docs/native/PLAN.md` (Q-PKG-31 closed). This document is its design input.

---

## 1. Goals and constraints

### 1.1 Goals

| # | Goal | What it means in practice | Where it is enforced |
|---|---|---|---|
| G1 | **Behavioral parity with Electron 1.3.0** | Every behavior in section 2 of each spec is REQUIRED unless that spec classifies it IMPROVEMENT or ELECTRON-ONLY. Users get no new features (feasibility, "Worth it"). | each spec's section 8 and 9; the manual parity walk AC-IPC-22 |
| G2 | **Shared files stay byte-compatible** | `project.json` round-trips through `contract/conformance` on all three codecs; `settings.json` keeps Electron's layout and unknown keys; the log line format is electron-log's; the brand table carries the same contract stamp. Rollback to Electron 1.3.x must keep working (INV-PKG-25). | 01 7.2, 7.11; 10 7.4.2, 7.5.2; INV-INFRA-1, INV-INFRA-14 |
| G3 | **No security invariant gets weaker** | Every Electron guarantee survives (redaction fail-closed, self-exclusion, egress pin, path confinement, credential isolation, link allowlist, policy precedence), and several get stronger (section 9). The renderer sandbox disappears, so what it contained is re-guarded in-process (Q-IPC-16). | section 9; every `[SECURITY]` invariant |
| G4 | **Responsive by construction** | The UI thread never waits on disk, network, capture or a lock held by a worker (INV-IPC-6, INV-SHELL-21). Edits that write only `project.json` show at once and persist in the background (05 7.5). No click is dropped (the `busyRef` pattern of `src/renderer/project/Report.tsx:424`, `:503-507` disappears). | sections 6, 7, 11 |
| G5 | **Operational gains that justify the rewrite** | Microsoft patches the runtime (framework-dependent, INV-PKG-5); one signed MSI per architecture, installed per-machine by Intune and per-user with no administrator rights by hand (INV-PKG-1, INV-PKG-7, INV-PKG-35); native ARM64 (D-PKG-2); fewer moving parts (no IPC, preload, sandbox, `shot://`, asar). | 12 |
| G6 | **Testable on Linux wherever possible** | Every rule that can be stated without Windows lives in `ShotAI.Core` and runs in the Linux CI job; Windows-only code is thin and sits behind a Core interface. | INV-ARCH-1, INV-IPC-19; section 12 |
| G7 | **Side-by-side development in small PRs** | Native code lands in `dotnet/` beside the shipping Electron app; Electron is frozen to fixes (Q-PKG-27); cutover is one release (2.0.0) and one cleanup PR. | `dotnet/README.md`; 12 7.13 |

### 1.2 Non-goals for 2.0.0

No new product features (feasibility). Specifically not in 2.0.0, each by an owning spec's recommended default: editor undo (Q-EDIT-9), hotkey rebinding (Q-CAP-2), title editing in the detail view (Q-REP-3), window size persistence (Q-SHELL-15), auto-update (12 7.14 "NOT ADOPTED"), a hosted web UI (feasibility "What gets harder" 2; INV-IPC-20), macOS-only menu extras (Q-SHELL-10), importing the Electron API key (Q-AUTH-2).

### 1.3 Hard constraints

| Constraint | Source | Consequence |
|---|---|---|
| Windows 10 2004 (10.0.19041) minimum | `dotnet/Directory.Build.props` (`ShotAIMinWindows`) | first build with `WDA_EXCLUDEFROMCAPTURE`; earlier builds silently treat it as `WDA_MONITOR` (`dotnet/src/ShotAI.Platform/CaptureExclusion.cs` remarks) |
| Per-Monitor V2 DPI, long paths | `dotnet/src/ShotAI.App/app.manifest` | capture and window geometry are in physical px (02, 03 7.7); paths over 260 characters are legal (Q-MODEL-13) |
| `contract/` is shared byte for byte with the macOS repo | `dotnet/README.md` rules | read in place, never copied (INV-INFRA-1, INV-PKG-21) |
| Public repository | repository policy | no tenant, organization, client or workspace id, nothing from `*.local.json`, placeholder ids in tests (for example a made-up SID `S-1-5-21-1-2-3-1001`, 03 8.3); public release builds are bring-your-own-key (INV-PKG-14) |
| Warnings are errors, analyzers on | `dotnet/Directory.Build.props` | every analyzer rule in 14.9 is a build break, not advice |
| Central package versions | `dotnet/Directory.Packages.props` | no `Version=` in any `.csproj` (3.1) |
| The whole solution builds on Linux | `EnableWindowsTargeting` in `Directory.Build.props` | Windows-only projects compile everywhere and run only on Windows; the WiX project is outside the solution (INV-PKG-22) |
| Electron keeps shipping until cutover | feasibility "Phased plan" | the two builds share user data during the pilot (10.5) and cannot both run in one session (INV-PKG-26) |

### 1.4 The security invariants this architecture exists to protect

In priority order (section 9 has the owner, mechanism and tests of each):

1. No pixel a user redacted, and no pixel outside a crop, ever leaves the machine or reaches an export (INV-EDIT-1, INV-EDIT-6, INV-EXP-1, INV-SOP-3).
2. shotAI's own windows never appear in its captures, and never appear in a remote viewer's capture unless the user chose remote visibility (INV-CAP-1, INV-CAP-7, INV-SHELL-1).
3. Credentials never reach a view model, a log, a file other than their DPAPI store, or any host but `api.anthropic.com` (INV-IPC-1, INV-IPC-2, INV-AUTH-19, INV-SOP-1).
4. A path read from a manifest, package or setting is confined before any read or write, and never followed through a link (INV-MODEL-13, INV-MODEL-14, INV-MODEL-34).
5. Managed policy comes only from `HKLM\SOFTWARE\Policies\shotAI\Federation` and baked build values, and validation is all-or-nothing (INV-AUTH-1, INV-AUTH-5).

### 1.5 Responsiveness principles

| # | Principle | Mechanism | IDs |
|---|---|---|---|
| P1 | The UI thread only renders, handles input and applies snapshots | services are free-threaded and awaited; the only synchronous service members the UI calls are lock-bounded snapshot readers | T9, INV-IPC-6 |
| P2 | Edits that write only `project.json` are optimistic | `IProjectSession.Apply` updates `Current` on the UI thread, then queues the same pure operation | 01 7.10, 05 7.5, section 7 |
| P3 | The editor save is durable and synchronous by design | flatten, persist, then report success | INV-EDIT-10 |
| P4 | Input capture does no work in the hook | preallocated ring, kernel event, return | INV-CAP-17 |
| P5 | Images decode off the UI thread at display size and never hold the file open | `ReportImageLoader` | INV-REP-32 |
| P6 | Logging never blocks a caller | bounded channel, drop-and-count | 10 7.5.4 |
| P7 | Nothing in auth or the network runs at launch except the throttled update check | lazy singletons | INV-AUTH-10, INV-INFRA-23 |
| P8 | No work item is posted to the UI dispatcher that could be done elsewhere, and each posted item is short | subscribers re-read snapshots and may coalesce | T7, Q-IPC-20 |

---

## 2. Solution layout

### 2.1 Projects

| Path (under `dotnet/`) | Target | Output | References | Runs on | Status |
|---|---|---|---|---|---|
| `src/ShotAI.Core` | `net10.0` | library | BCL plus the Core package allowlist (3.2) | Linux, Windows | exists (scaffold) |
| `src/ShotAI.Platform` | `net10.0-windows10.0.19041.0` (`$(ShotAIWindowsTfm)`) | library, `AllowUnsafeBlocks` | Core | Windows (builds on Linux) | exists |
| `src/ShotAI.App` | `$(ShotAIWindowsTfm)`, `UseWPF`, `AssemblyName` `shotAI` | `WinExe` `shotAI.exe` | Core, Platform | Windows | exists |
| `tests/ShotAI.Core.Tests` | `net10.0`, xunit.v3 on Microsoft.Testing.Platform | test exe | Core, `tools/ShotAI.GenBrand`, `tools/ShotAI.Release` | Linux, Windows | exists; gains the tool references (10 7.3, 12 7.15) |
| `tests/ShotAI.Platform.Tests` | `$(ShotAIWindowsTfm)`, xunit.v3 | test exe | Platform, Core | Windows only | added by the first Platform PR (01 8.2, 02 8.4, 03 8.3) |
| `tests/ShotAI.App.Tests` | `$(ShotAIWindowsTfm)`, `UseWPF`, xunit.v3 with an in-repo STA harness (Q-HOME-15) | test exe | App, Platform, Core | Windows only | added by the first App PR (03, 05, 06, 11 8.2) |
| `tools/ShotAI.GenBrand` | `net10.0` | console | none (BCL only, EDGE-INFRA-48) | Linux, Windows | 10 7.3 |
| `tools/ShotAI.Release` | `net10.0` | console | Core | Linux, Windows | 12 7.15 |
| `tools/ShotAI.ProtectionProbe` | `$(ShotAIWindowsTfm)` | console, not shipped | Platform | Windows, interactive desktop | 02 8.4 |
| `tools/ShotAI.WifProbe` | `$(ShotAIWindowsTfm)` | console, not shipped | Core, Platform | Windows | 08 7.19 |
| `installer/ShotAI.Installer.wixproj` | WiX MSBuild SDK | MSI | the published payload | Windows | 12 7.4; NOT in `ShotAI.slnx` (INV-PKG-22) |
| `native/avif/` | C shim plus `pins.json` | `shotai_avif.dll` per architecture | libavif, libaom (pinned sources) | built by CI on Windows | 09 7.6, 12 7.7 |

`ShotAI.Core.Tests` references the two tool projects so their logic is tested on Linux; if referencing an executable project from the Microsoft.Testing.Platform test executable causes friction, the shared logic moves into a small `net10.0` library that both reference (10 verifier note). `ShotAI.slnx` gains `/tests/` entries for the two Windows test projects and a `/tools/` folder for the four tools. `dotnet test --solution ShotAI.slnx` is a Windows-runner command only; the Linux job runs `tests/ShotAI.Core.Tests` by project (`.github/workflows/dotnet.yml`).

### 2.2 Dependency rules

| ID | Rule | Why | Enforcement |
|---|---|---|---|
| INV-ARCH-1 | `ShotAI.Core` references no Windows, WPF, WinRT or Win32 assembly and no Windows-only package, and every interface that Core logic consumes is declared in Core. | Linux test run (G6); the fixed placement rule | `Architecture.CoreReferencesTests` (INV-IPC-19, AC-IPC-16), `CA1416` as error, the Core package allowlist (3.2) |
| INV-ARCH-2 | Dependencies point one way: App to Platform and Core; Platform to Core; Core to nothing of ours. Platform never references App. Tools reference Core at most (the probes also Platform). | no cycles; the one near-cycle (12's first `IProcessSnapshot` draft) was moved into Platform (12 7.10.3) | project references; `Composition.ViewModelDependencyTests` |
| INV-ARCH-3 | View models depend only on catalog interfaces (11 7.3 and 4.4, including `ICaptureTargetSelection`, R-ARCH-26), 06's chrome services, the factories of 4.1 C6 (`EditorFactory`, the `ReportViewModel` and `DocScaleEditor` factories, `SopPanelViewModelFactory`), `IUiDispatcher`, `ILogger<T>`, value types and other view models; never on `ProjectStore`, `EntraSession`, `IApiKeyStore`, `MsalGateway`, `CaptureEngine`, `SettingsService` (concrete) or any Platform type. One named addition: 04's `EditorViewModel`, which `EditorFactory` constructs, also receives the Core types `Flattener`, `IRenderCodec` and `IPathProbe` (none holds a secret; 04 7.10.1), and App interfaces such as 04's `IColorPicker` whose implementation wraps a Platform helper. | secrets never reach the UI (INV-IPC-1); fakes for tests | `Composition.ViewModelDependencyTests` (11 8.2) |
| INV-ARCH-4 | A Platform type that implements a Core seam is `internal sealed` and registered only as its Core interface. Platform helpers the App calls directly (03's `SingleInstanceLock`, `ExistingInstance`, `WindowStyles`, `MonitorQueries`, `Foreground`, the registration surface of `OwnWindowRegistry`, 12's `DllSearchHardening`, `ProcessSnapshot`, `ProcessStarter` and `InstallInfoReader`, the existing `CaptureExclusion`, the `AddShotAIPlatform` extension) are public and have no Core counterpart; `InstallInfoReader.Read` returns 12's internal `InstallInfo` typed as the Core interface `IInstallInfo`. | the shield funnel depends on `GdiMonitorCapture` being reachable only through `ShieldedScreenCapture` (02 7.7) | `CaptureFunnelSourceTests` (02 8.4); reflection test in `Composition.ContainerTests` |
| INV-ARCH-5 | Exactly one assembly references `Microsoft.Web.WebView2.Core`: `ShotAI.Platform`, in `ShotAI.Platform.Export`. The App has no WebView2 package reference at all. | the only hosted web engine keeps renderer-grade hardening (INV-IPC-20, INV-EXP-20) | `Architecture.SingleWebViewTests` |
| INV-ARCH-6 | `InternalsVisibleTo` names only the matching test project (Core to Core.Tests, Platform to Platform.Tests, App to App.Tests). | keep `internal` meaningful | code review |

### 2.3 What goes where

The decision rule, applied top to bottom:

1. Can the rule be stated and tested without a Windows API, a window or a pixel on screen? It goes in **Core**, including presenters and state machines that the UI merely renders (03's `PillPresenter`, 04's `EditorDocument`, 05's `ReportEditState` and `DocScaleEditor`, 06's `HomeListPipeline`, 03's `BrandMenuModel`). Core may implement `INotifyPropertyChanged` through `CommunityToolkit.Mvvm` because that package is platform-neutral (03 7.1).
2. Does it call Win32, COM, WinRT, the registry, DPAPI, WebView2 or a native library? It goes in **Platform**, behind an interface declared in Core, and the Platform type stays as thin as the API allows.
3. Does it touch a WPF type (`Window`, `Dispatcher`, `DrawingContext`, `BitmapSource`)? It goes in the **App**.

Platform seams (each declared in Core, implemented in Platform, faked in Core.Tests):

| Seam | Implementation | Owner |
|---|---|---|
| `IPathProbe`, `IRenameRetryClassifier` | `WindowsPathProbe`, `WindowsRenameRetryClassifier` | 01 7.5, 7.6 |
| `ITriggerSource`, `IMonitorCapture`, `IWindowInfoProvider`, `IElementLocator`, `IWindowProtection`, `IOwnWindows`, `IImageCodec` | `Win32TriggerSource`, `GdiMonitorCapture`, `Win32WindowInfoProvider`, `UiaElementLocator`, `DisplayAffinityProtection`, `OwnWindowRegistry`, `WicImageCodec` | 02 7.2 |
| `IRenderCodec`, `IOcrEngine` | `WicImageCodec`, `WindowsOcrEngine` | 04 7.4, 7.9 |
| `IImageSizeProbe` | `WicImageSizeProbe` | 05 7.1 |
| `ISystemAppearance` | `SystemAppearanceMonitor` | 06 7.14 |
| `IPolicyValueSource`, `IMsalGatewayFactory`, `ISecretProtector` | `RegistryPolicySource`, `MsalGatewayFactory`, `DpapiSecretProtector` | 08 7.1 |
| `IExportImageCodec`, `IAvifEncoder`, `IPdfRenderer`, `IExportFileProbe` | `WicExportImageCodec`, `LibavifEncoder`, `WebView2PdfRenderer`, `WindowsExportFileProbe` | 09 7.1 |
| `IUrlLauncher`, `IShellReveal`, `IWebView2RuntimeInfo` | `ShellUrlLauncher`, `ShellReveal`, `WebView2RuntimeInfo` | 11 7.2 |
| `IAppPaths` | `AppPaths` (App, because it reads `AppContext.BaseDirectory` and known folders) | 10 7.4.4 |
| `IUiDispatcher`, `IAppLifetime` | `WpfUiDispatcher`, `AppLifetime` (App) | 11 7.3.1 |

### 2.4 Namespaces and folders

Folder equals namespace, one public type per file, file named after the type (`CaptureShield.cs`). A `*.Generated.cs` suffix marks checked-in generator output (10 7.3).

| Project | Namespace (folder) | Owner spec | Holds (headline types) |
|---|---|---|---|
| Core | `ShotAI.Core.Json` | 01 (04 adds `JsValue`, `JsPath`) | `JsJson`, `JsNumber`, `JsMath`, `JsString`, `IsoTime`, `JsValue`, `JsPath` |
| Core | `ShotAI.Core.Model` | 01 | `ProjectManifest`, `ProjectStep`, `SopIntro`, `SopBackup`, `Rect`, `Point`, `CalloutKinds`, `StepList`, `ProjectSummary` |
| Core | `ShotAI.Core.Codec` | 01 | `ManifestCodec`, `ManifestKeys` |
| Core | `ShotAI.Core.Store` | 01, 11 | `ProjectStore`, `IProjectService`, `IProjectSession`, `IProjectSessionFactory`, `ProjectSessionFactory`, `IProjectSettle`, `OpenedProject`, `ImportFile`, `ProjectOperation`, `MutateResult`, `ManifestChangeKind`, `ManifestChangedEventArgs`, `PersistFailedEventArgs`, `SerialWriteQueue`, `AtomicFile`, `PathConfine`, `IPathProbe`, `ReparseSafeDelete`, `ArchiveEngine`, `ImportLimits`, the store exceptions of 01 7.13 (`ProjectNotKnownException`, `StepNotFoundException`, `MergeIntoItselfException`, `UnsupportedImageException`, `ImportRejectedException`, `ArchiveException`, `ManifestCorruptException`); the full list is 01 7.1 |
| Core | `ShotAI.Core.Geometry` | 05 (01 uses `DocScale.Clamp`) | `DocScale`, `DocWidths`, `ReportGeometry` |
| Core | `ShotAI.Core.Capture` | 02 | `CaptureEngine`, `ICaptureService`, `CaptureShield`, `ShieldedScreenCapture`, seams, `CaptureGeometry`, `ClickCaptions` |
| Core | `ShotAI.Core.Shell` | 03, 11 | `WindowLayout`, `PillPresenter`, `RecordingVisibilityPlanner`, `AreaSelectionMath`, `BrandMenuModel`, `ShellStrings`, `IShellReveal` |
| Core | `ShotAI.Core.Editor` | 04 | `EditorDocument`, annotation views, `AnnotationFactory`, `TransformMath`, `HitTesting` |
| Core | `ShotAI.Core.Rendering` | 04 | `Flattener`, `RedactionBaker`, `StepPatch`, `StepPatchValidator`, `StepPatchApplier`, `IStepRenderWriter`, `IStepFlattener` (R-ARCH-7) |
| Core | `ShotAI.Core.Redaction` | 04 | `RenderGate`, `IRenderGate`, `RenderGateException`, `SensitiveTextDetector`, `ISensitiveRegionScanner`, `IOcrEngine` |
| Core | `ShotAI.Core.Report`, `.Report.Operations` | 05 | `ReportEditState`, `DocScaleEditor`, `CardListDiff`, `PopoverPlacement`, `IImageSizeProbe` (the seam of 2.3, 05 7.1), the report operations |
| Core | `ShotAI.Core.Home`, `.Tour`, `.SettingsUi`, `.Theme` | 06 | `HomeListPipeline`, `DateGroups`, `HomeSelection`, `TourLayout`, `SettingsText`, `ThemeTokenSet`, `ISystemAppearance` (in `.Theme`, the seam of 2.3, 06 7.1) |
| Core | `ShotAI.Core.Sop`, `.Sop.Transport` | 07 | `IClaudeService`, `ClaudeService`, `SopPrompt`, `SopEditSchema`, `ApplySopPlanOperation`, `SopErrorMapper` |
| Core | `ShotAI.Core.Auth`, `ShotAI.Core.Net` | 08 | `IAuthService`, `AuthService`, `FederationConfigProvider`, `EntraSession`, `IAnthropicClientFactory`, `ApiKeyStore`, `ISharedHttp` |
| Core | `ShotAI.Core.Export`, `.Export.Office`, `.Export.Package` | 09 | `ExportEngine`, `StepCollector`, `HtmlDocumentBuilder`, `DocxBuilder`, `PptxBuilder`, `PackageReader` |
| Core | `ShotAI.Core.Brand`, `.Settings`, `.Logging`, `.Updates`, `.Links`, `.SelfTest`, `.Paths` | 10 (11 for `Links` algorithm) | `BrandPalette`, `SettingsService`, `FileLoggerProvider`, `UpdateService`, `ExternalLinks`, `IAppPaths` |
| Core | `ShotAI.Core.Install` | 12 | `InstallScope`, `IInstallInfo`, `InstallScopeRules` (12 7.10.4) |
| Core | `ShotAI.Core.Threading`, `.Errors`, `.Diagnostics`, `.Composition` | 11 | `IUiDispatcher`, `EventRaiser`, `IAppLifetime`, `ShotAIException`, `UserMessage`, `CoreServiceCollectionExtensions` |
| Platform | `ShotAI.Platform.FileSystem` | 01 | `WindowsPathProbe`, `WindowsRenameRetryClassifier` |
| Platform | `ShotAI.Platform.Capture` | 02 (04 extends the codec) | hook, capture, UIA, affinity, own-window registry, `WicImageCodec` (including the `IRenderCodec` members 04 adds; it stays in this namespace, 02 7.1, 04 7.1) |
| Platform | `ShotAI.Platform.Shell` | 03, 10, 11 | `SingleInstanceLock`, `WindowStyles`, `MonitorQueries`, `ShellReveal`, `ShellUrlLauncher`, `StaThread`, `ConsoleAttach` |
| Platform | `ShotAI.Platform.Imaging`, `.Ocr`, `.Dialogs` | 02, 04, 05 | `WicFactory` (the one MTA `IWICImagingFactory`, 02 7.1), `ReportImageDecoder` and `WicImageSizeProbe` (in `.Imaging`, 05 7.1), `WindowsOcrEngine` (`.Ocr`), `Win32ColorDialog` (`.Dialogs`, 04 7.1) |
| Platform | `ShotAI.Platform.Auth` | 08 | `RegistryPolicySource`, MSAL gateway and cache, `DpapiSecretProtector` |
| Platform | `ShotAI.Platform.Export` | 09, 11 | `WebView2PdfRenderer`, `PdfHostWindow`, `LibavifEncoder`, `NativeAvif`, `WebView2RuntimeInfo` |
| Platform | `ShotAI.Platform.Theme`, `.Processes`, `.Install`, `.Composition` | 06, 12, 11 | `SystemAppearanceMonitor`, `ProcessSnapshot`, `ProcessStarter`, `InstallInfoReader` (and its internal `InstallInfo`), `PlatformServiceCollectionExtensions` |
| Platform | root | 12, scaffold | `NativeMethods.txt`, `CaptureExclusion` (existing), `DllSearchHardening` |
| App | `ShotAI.App` | 03 | `Program` (explicit `Main`), `App` (composition root), `AppPaths` |
| App | `ShotAI.App.Shell`, `.Capture`, `.Startup` | 03, 02, 12 | windows, `AreaSelectionService`, `RecordingVisibilityController`, `AppMenuViewModel`, `PopupExclusion`, `LegacyInstanceGuard`, `PersonalCopyGuard` |
| App | `ShotAI.App.Home`, `.Settings`, `.Tour`, `.Chrome` | 06 | view models, `NoticeCenter`, `ConfirmService`, `ThemeManager`, `Themes/*.xaml` |
| App | `ShotAI.App.Report`, `.Editor`, `.Sop`, `.Export`, `.Auth` | 05, 04, 07, 09, 08 | views, view models, `ReportImageLoader`, `StaRenderThread`, `EditorFactory`, `IColorPicker` and `Win32ColorPicker` (in `.Editor`, 04 7.1), `SopPanelViewModelFactory`, `ExportService`, `EmbeddedBakedFederationSource` |
| App | `ShotAI.App.Threading`, `.Services`, `.Composition` | 11 (04 for `UiDeferral`) | `WpfUiDispatcher`, `UiDeferral` (in `.Threading`, beside the dispatcher; R-ARCH-18), `AppLifetime`, `ShutdownFlush`, `IFileDialogs`, `IAppInfo`, `AppServiceCollectionExtensions` |

Test projects mirror the namespace of what they test (`ShotAI.Core.Tests.Store.AtomicFileTests` tests `ShotAI.Core.Store.AtomicFile`); cross-cutting guard tests live under `Architecture`, `ServiceBoundary`, `Threading` and `SourceGuards` (11 8.2, Q-HOME-1).

### 2.5 Non-code files

| Path | Holds | Owner |
|---|---|---|
| `dotnet/Directory.Build.props` | shared properties, `<Version>` (single source, INV-PKG-10), lock-file and audit switches | 12 7.2.1 |
| `dotnet/Directory.Packages.props` | every package version | 3 |
| `dotnet/global.json` | SDK `10.0.100` with `latestFeature`, the Microsoft.Testing.Platform opt-in | scaffold |
| `dotnet/nuget.config` | nuget.org only, source mapping, signature validation | 12 7.8 |
| `dotnet/.editorconfig` | analyzer severities, per-file `RS0030` allowlists | 11 7.12, 14.9 |
| `dotnet/src/*/BannedSymbols.txt` | banned APIs per project | 11 7.12 |
| `dotnet/**/packages.lock.json` | locked restore (committed) | 12 7.8 |
| `dotnet/assets/fonts/` | `Archivo.ttf`, `OFL.txt`, `static/` instances with `SOURCES.md` | 10 7.9 |
| `dotnet/native/avif/` | the shim source and `pins.json` | 09 7.6, 12 7.7 |
| `dotnet/installer/` | WiX project, release notes | 12 7.4, 7.12 |
| `dotnet/tests/ShotAI.Core.Tests/Golden/` | golden files and fixtures, each subfolder with a README naming its origin | 12.5 |
| `dotnet/tests/ShotAI.Core.Tests/ServiceBoundary/channel-map.json` | the 87-channel map | 11 8.2 |
| `dotnet/federation.local.json` | baked federation values for internal builds, gitignored | 08 7.4 |
| `contract/` (repo root) | `brand.json`, `conformance/` | shared with macOS, read in place |

---

## 3. Package inventory

### 3.1 Version policy

| # | Rule | Source |
|---|---|---|
| V1 | Every `PackageVersion` lives in `dotnet/Directory.Packages.props`; no `.csproj` carries `Version=`. | scaffold rule |
| V2 | Exact versions (no floating ranges). A version is changed only in a PR whose title names the package, with the lock files regenerated in the same PR. | 12 7.8 |
| V3 | `RestorePackagesWithLockFile=true`; CI restores in locked mode; `packages.lock.json` committed per project. | INV-PKG-20 |
| V4 | One source, nuget.org, with source mapping and `signatureValidationMode=require`. | INV-PKG-20, Q-PKG-29 |
| V5 | `NuGetAudit=true`, `NuGetAuditMode=all`; high and critical advisories fail every build, low and moderate fail the release workflow only. Mechanism: `Directory.Build.props` puts `NU1901;NU1902` in `WarningsNotAsErrors` only when `'$(ShotAIStrictAudit)' != 'true'`, and the `release.yml` test job passes `-p:ShotAIStrictAudit=true`. | Q-PKG-14, 12 7.2.1 |
| V6 | Dependabot opens weekly PRs for `nuget` under `/dotnet` and for `github-actions`. | 12 7.8 |
| V7 | Analyzers and source generators are referenced with `PrivateAssets="all"`. | 11 7.12 |
| V8 | A package may be added to Core only if it is platform-neutral and on the Core allowlist below; adding one to the allowlist is a change to this document. | INV-ARCH-1 |
| V9 | Security-sensitive SDKs (`Anthropic`, MSAL) are upgraded deliberately, never by Dependabot auto-merge, and the tripwire tests (`AnthropicClientFactoryTests`, `MsalCachePersistenceTests`) must pass on the new version. | 08 Risk R3 |
| V10 | Every shipped package and native library appears in `THIRD-PARTY-NOTICES.txt`, generated by `ShotAI.Release notices` and checked in CI. | INV-PKG-29 |

### 3.2 NuGet packages

"Licence" is the package's licence as recorded by the owning spec or the package source; entries marked UNVERIFIED must be confirmed by the notices generator in the PR that adds the package.

| Package | Project | Purpose | Owner | Licence | Version line (pinned exactly in the props file) |
|---|---|---|---|---|---|
| `Microsoft.Windows.CsWin32` | Platform (`PrivateAssets=all`) | P/Invoke and COM projections from `NativeMethods.txt` | scaffold | MIT | `0.3.335` (pinned today) |
| `xunit.v3` | all test projects | tests on Microsoft.Testing.Platform | scaffold | Apache-2.0 | `4.0.1` (pinned today) |
| `Microsoft.Extensions.Logging.Abstractions` | Core | `ILogger<T>`, `LoggerMessage` | 01, 10, 11 | MIT | .NET 10 line |
| `Microsoft.Extensions.DependencyInjection.Abstractions` | Core | the `AddShotAICore` extension signature | 11 7.13 | MIT | .NET 10 line |
| `CommunityToolkit.Mvvm` | Core (presenters), App | `ObservableObject`, source-generated properties and commands | 03 7.11, 11 7.13 | MIT | latest stable 8.x |
| `System.IO.Hashing` | Core | CRC-32 for archive verification | 01 7.9 | MIT | .NET 10 line |
| `DocumentFormat.OpenXml` | Core | Word and PowerPoint builders | 09 7.16 | MIT | latest stable 3.x |
| `Anthropic` | Core | Messages, count tokens, models, `WorkloadIdentityCredentials` | 07 7.1, 08 7.1 | MIT | `12.50.0` (the SDK commit read, `anthropic-sdk-csharp` `src/Anthropic/Anthropic.csproj`) |
| `Microsoft.Identity.Client` | Platform | MSAL.NET | 08 7.1 | MIT | 4.61.0 or later |
| `Microsoft.Identity.Client.Broker` | Platform | WAM broker, brings `msalruntime` per RID | 08 7.1 | MIT (package); `msalruntime` binaries under Microsoft terms (UNVERIFIED) | same line as MSAL |
| `Microsoft.Identity.Client.Extensions.Msal` | Platform | DPAPI token cache with a cross-process lock | 08 7.6 | MIT | compatible with MSAL line |
| `System.Security.Cryptography.ProtectedData` | Platform | DPAPI for the API key | 08 7.11 | MIT | .NET 10 line |
| `Microsoft.Web.WebView2` | Platform | PDF printing host; runtime version probe | 09 7.16, 11 7.13 | Microsoft WebView2 SDK licence (BSD-style, UNVERIFIED wording) | latest stable |
| `Microsoft.Extensions.DependencyInjection` | App | the container | 03 7.11, 11 7.13 | MIT | .NET 10 line |
| `Microsoft.Extensions.Logging` | App | `LoggerFactory` | 10 7.11 | MIT | .NET 10 line |
| `Microsoft.Extensions.Logging.Debug` | App (Debug configuration only) | debugger output in place of electron-log's console transport | 10 7.5.1 | MIT | .NET 10 line |
| `Microsoft.VisualStudio.Threading.Analyzers` | all projects (`PrivateAssets=all`) | `VSTHRD` rules | 11 7.12 | MIT | latest stable |
| `Microsoft.CodeAnalysis.BannedApiAnalyzers` | all projects (`PrivateAssets=all`) | `RS0030` banned symbols | 11 7.12 | MIT | latest stable |
| `Microsoft.Extensions.TimeProvider.Testing` | Core.Tests (and the Windows test projects as needed) | `FakeTimeProvider` | 01 7.6 | MIT | .NET 10 line |

Transitive packages worth knowing: `Anthropic` 12.50.0 brings `System.Text.Json` 10.0.6, `System.Net.ServerSentEvents` 10.0.1 and `Microsoft.Extensions.AI.Abstractions` 10.5.1 (read from `src/Anthropic/Anthropic.csproj`); the .NET 10 SDK prunes packages the shared framework already provides, and the lock files record what remains. No third-party logging, DI, MVVM, UI Automation (FlaUI is not used: 02 7.6 uses CsWin32 COM) or JSON package is used.

### 3.3 Native binaries and OS components

| Item | How it arrives | Purpose | Licence | Provenance rule |
|---|---|---|---|---|
| `shotai_avif.dll` (libavif 1.x plus libaom, statically linked, C shim) | built in CI per architecture, placed next to `shotAI.exe` | AVIF for styled HTML export | BSD-2-Clause (both) | pinned commits and source SHA-256 in `dotnet/native/avif/pins.json`, build attestation, never committed as a binary (INV-PKG-19); bound with `LibraryImport`, the one sanctioned exception to "CsWin32 for every P/Invoke" (Q-EXP-1, I-3 in 15.2) |
| `msalruntime*.dll` | `Microsoft.Identity.Client.Broker`, flattened by the RID publish | WAM broker | Microsoft | Microsoft-signed; architecture checked by `verify-payload` (12 7.2.4) |
| `WebView2Loader.dll` | `Microsoft.Web.WebView2` | WebView2 bootstrap | Microsoft | same |
| .NET 10 Desktop Runtime | Intune dependency, serviced by Microsoft Update | framework-dependent host | MIT | apphost searches global locations only (INV-PKG-17) |
| WebView2 Evergreen Runtime | Windows 11 and Microsoft 365 installs; Intune prerequisite otherwise | PDF export only | Microsoft | a missing runtime gives `PdfRuntimeMissing` (D-EXP-4) |
| Windows OCR recognizer (Feature on Demand `Language.OCR~~~en-US~0.0.1.0`) | present on en-US installs; IT deploys elsewhere | auto-redact pre-scan | Windows | missing recognizer gives the `Unavailable` notice (04 7.9, Q-EDIT-5) |
| Archivo (variable plus static instances) | `dotnet/assets/fonts/`, installed under `Fonts\` | LFI brand face | SIL OFL 1.1 | byte-identical to the Electron copy, `OFL.txt` beside every copy (INV-INFRA-31, INV-INFRA-32) |

### 3.4 MSBuild SDKs and toolchain

| Item | Use | Policy |
|---|---|---|
| .NET SDK 10.0.1xx | everything | `global.json` `10.0.100` with `rollForward: latestFeature` (scaffold) |
| WiX Toolset MSBuild SDK (`WixToolset.Sdk`), optionally `WixToolset.UI.wixext` | the MSI | version and licence decided before the installer PR (Q-PKG-1) |
| MSVC, CMake, NASM (x64 libaom assembly) | `shotai_avif.dll` | NASM from a pinned, hash-checked download (Q-PKG-13) |
| Azure Artifact Signing | Authenticode for PEs and MSIs, timestamped | 12 7.6, Q-PKG-2 |

### 3.5 Package conflicts between specs, resolved

| Conflict | Specs | Resolution |
|---|---|---|
| 03 7.11 puts `Microsoft.Web.WebView2` in the App for the About line's runtime version | 03 vs 11 | Platform only; the App reads `IAppInfo.Current.WebView2Version` through `IWebView2RuntimeInfo` (INV-ARCH-5, R-ARCH-12). |
| `Microsoft.Extensions.Logging.Abstractions` "added by whoever lands first" | 01, 02, 05, 10 | added by the foundation PR together with `ShotAI.Core.Errors` and `ShotAI.Core.Threading` (Q-IPC-12), so every subsystem PR finds it. |
| `CommunityToolkit.Mvvm` in Core | 03 (Core presenters) vs the "Core has no UI" reading | allowed: platform-neutral `netstandard` package; Core presenters use only `ObservableObject` and `INotifyPropertyChanged`, never commands bound to WPF types. |
| `Anthropic` in Core | 07 Q-SOP-14 | allowed: platform-neutral; keeps the request, stream and error tests on Linux. |
| FlaUI suggested by the feasibility doc for UI Automation | feasibility vs 02 7.6 | not used; UI Automation through CsWin32 COM on MTA workers (fixed decision, 02 D15). |
| A second image codec (WinRT `BitmapDecoder`) for the report | 05 Q-REP-19 vs 02, 04 | not used; one WIC COM decode path shared by capture, editor, flatten, report and export, so orientation and colour can never disagree (05 Q-REP-19 default). |
| PDF by PDFsharp or MigraDoc (feasibility alternative) | feasibility vs fixed decision | not used; WebView2 `PrintToPdfAsync` of the same export HTML (fixed decision). |

---

## 4. Composition root and dependency injection

### 4.1 Principles

| # | Principle | Source |
|---|---|---|
| C1 | `Microsoft.Extensions.DependencyInjection`, one container per process, built once in `App.OnStartup` and disposed in `App.OnExit`. | fixed decision; 11 7.10 |
| C2 | Constructor injection only. Nothing resolves services through a static locator; the composition root (`App`) is the one place that calls `GetRequiredService`. | 11 7.10 rule 4 |
| C3 | `ValidateOnBuild = true` and `ValidateScopes = true`; there are no scopes, and `ValidateScopes` still catches a singleton that captures a transient view model. | 11 7.10 rule 1 |
| C4 | Services are singletons, view models are transient, per-project state lives in an `IProjectSession` made by a factory (INV-IPC-22). | 11 7.10 |
| C5 | Every disposable singleton implements `IDisposable` (optionally also `IAsyncDisposable`), because `App.OnExit` is synchronous and `ServiceProvider.Dispose()` throws for a resolved service that implements only `IAsyncDisposable`; each `Dispose` must not need the UI thread to be free. | 11 7.10 rule 2; this overrides 01 7.12's `await provider.DisposeAsync()` (R-ARCH-10) |
| C6 | Objects that need a runtime argument (a session, a step, a project path) come from a small factory type registered as a singleton, never from `IServiceProvider` passed around: `IProjectSessionFactory` (11), `EditorFactory` (04 7.10.1), the per-open `ReportViewModel` and `DocScaleEditor` factories (05 7.19), the `SopPanelViewModel` factory (07 7.10). | C2 |
| C7 | Each project registers its own services in one extension method: `AddShotAICore` (Core), `AddShotAIPlatform` (Platform), `AddShotAIApp` (App). A subsystem PR adds its registrations to the extension of the project its types live in, never to `App.OnStartup` directly. | 11 7.2 |

### 4.2 Bootstrap before the container

Some work must happen before any service exists. The order is 03 7.4.1 with 12 and 10 inserted; this table is the canonical sequence.

| Step | Where | Action | Why it precedes the container | IDs |
|---|---|---|---|---|
| 0 | `Program.Main` (`[STAThread]`) | `ShotAI.Platform.DllSearchHardening.Apply()` (`SetDefaultDllDirectories(LOAD_LIBRARY_SEARCH_DEFAULT_DIRS)`), `Environment.FailFast` on failure; then `new App()`, `InitializeComponent()`, `Run()` | must precede any native load, WPF's own included | INV-PKG-16, Q-PKG-30 |
| 1 | `App.OnStartup` | crash handlers (`CrashLogging.Install`), then the bootstrap `LoggerFactory` with 10's `FileLoggerProvider` (opens the log with `FileShare.ReadWrite` per batch); banner lines | a crash or early exit must be logged | 03 7.4.9, 10 7.5 |
| 1b | same | `PersonalCopyGuard.TryHandOff(args, selfTestMode)` with `InstallInfoReader.Read(AppContext.BaseDirectory)` and `new ProcessStarter()`: a personal (per-user) copy starts the copy installed for all users with the same arguments and `Shutdown(0)`; in a self-test mode it only logs; the returned `IInstallInfo` is kept for step 6 | a process holding the mutex would make the started copy activate it and quit | INV-PKG-38, 12 7.10.4 |
| 2 | same | single-instance mutex `Local\shotAI.SingleInstance.<SID>`; on failure activate the first instance and `Shutdown(0)` without touching settings, store or windows | INV-SHELL-5 | 03 7.4.8 |
| 2a | same | `LegacyInstanceGuard.FindRunningElectron()` constructed directly with `new ProcessSnapshot()`; on a hit log, show its notice and exit | before any settings or project IO | INV-PKG-26, Q-SHELL-19 |
| 3 | same | self-test switches (`--selftest`, `--capture-selftest`, `--update-selftest`, and the `SHOTAI_SELFTEST` and `SHOTAI_CAPTURE_TEST` variables): build isolated instances, run, `Shutdown(exitCode)` | self-tests never touch the user's settings or open a window | 10 7.8, INV-INFRA-30 |
| 4 | same | `SHOTAI_ENABLE_GPU=0` forces WPF software rendering | must precede the first window | 03 D17 |
| 5 | same | environment sanitization: remove `ANTHROPIC_CUSTOM_HEADERS` from the process environment (INV-AUTH-35); remove the proxy variables per Q-ARCH-3 (default: remove `HTTPS_PROXY`, `HTTP_PROXY`, `ALL_PROXY`, `NO_PROXY` and their lower-case spellings), logging the NAMES removed at Warning, never the values | the SDK reads `ANTHROPIC_CUSTOM_HEADERS` once in a static constructor; `HttpClient.DefaultProxy` reads the proxy variables on first use | INV-AUTH-35, Q-AUTH-17, Q-SOP-20 |
| 5b | same | `SettingsService.Load(paths, atomic, time, log)`, synchronous; primes `ICaptureSettings` | windows need `RemoteVisible` and the theme before the first frame | 10 7.4.3, 03 step 5 |
| 6 | same | build the container (4.3): `AddShotAILogging(loggerFactory).AddShotAICore().AddShotAIPlatform().AddShotAIApp(Dispatcher.CurrentDispatcher, settings, installInfo)`, where `settings` is the `SettingsService` loaded at step 5b and `installInfo` the `IInstallInfo` read at step 1b | | 11 7.10 |
| 7 | same | `PopupExclusion.Install()` | before any window | INV-SHELL-1 |
| 8 | same | create `MainWindow`, create the pill and `EnsureHandle()` it (registered and excluded, not shown); `ThemeManager.ApplyInitial` | fail-closed exclusion; theme before first frame (D-HOME-10) | INV-SHELL-2 |
| 9 | same | `main.Show()`; resolve `IEnumerable<IAppStartup>` and call `Start()` on each in registration order | `Start()` subscribes only, no IO | 11 7.10 rule 3 |
| 10 | same | `CaptureShield.ApplyRemoteVisibility(settings.Current.RemoteVisible)`, the only startup application of the setting, called synchronously on the UI thread: the one reasoned exception to DL1 (6.4) | protected first, relaxed after | INV-SHELL-2, 02 7.8 rule 2 |
| 11 | same | `ActivationListener.Start(sid)` | second launches can reach us | 03 7.4.8 |
| 12 | same | runtime diagnostic line; the startup timing line of PB-9 | | 03 7.9, 11 |
| 13 | same | fire and forget on the pool, both under `IAppLifetime.Stopping`: `IProjectService.AutoArchiveStaleAsync` (raises `ProjectsChanged`) and `IUpdateService.RunStartupCheckAsync` | nothing blocks the first frame; failures logged, never shown | 03 7.4.1 step 13, INV-INFRA-23 |

### 4.3 Registrations

`AddShotAILogging(loggerFactory)` registers the bootstrap factory as `ILoggerFactory` and the open generic `ILogger<T>`. The rest, consolidated from 01 7.14, 10 7.11, 11 7.10, 05 7.19 and 08 7.1 with the resolutions of 15.3 applied:

| Registration | Implementation | Lifetime | Extension | Owner |
|---|---|---|---|---|
| `TimeProvider` | `TimeProvider.System` | singleton instance | Core | 11 |
| `AtomicFile`, `ArchiveEngine` | same | singleton | Core | 01 |
| `IProjectService` | `ProjectStore` (also `IDisposable`, `IAsyncDisposable`) | singleton | Core | 01, 11 |
| `IProjectSessionFactory`, `IProjectSettle` | `ProjectSessionFactory` (one instance forwarded to both) | singleton | Core | 11, 07 |
| `IInstallInfo` | the object `InstallInfoReader.Read` returned at step 1b, passed to `AddShotAIApp(dispatcher, settings, installInfo)` and registered as that instance | singleton instance | App | 12 7.10.4 |
| `ISettingsService`, `IProjectStoreSettings`, `ICaptureSettings` | the `SettingsService` loaded at step 5b, passed to `AddShotAIApp(dispatcher, settings, installInfo)` and registered as that instance (plus forwarding factories; `Dispose` idempotent) | singleton | App (instance), Core (forwarders) | 10, 11 7.10 |
| `ICaptureService` | `CaptureEngine` (also `IDisposable`) | singleton | Core, with Platform seams | 02, 11 |
| `CaptureShield`, `IScreenCapture` (`ShieldedScreenCapture`) | same | singleton | Core | 02 |
| `IStepFlattener`, `IStepRenderWriter`, `IRenderGate`, `ISensitiveRegionScanner`, `Flattener` | 04 types | singleton | Core | 04 |
| `IClaudeService`, `SopRequestAssembler` | `ClaudeService`, same | singleton | Core | 07 |
| `SopPanelViewModelFactory` | same (C6) | singleton | App | 07 7.10 |
| `IAuthService`, `IAnthropicClientFactory`, `ISupportUrlAllowlist`, `ISharedHttp`, `IApiKeyStore` (internal), `EntraSession`, `FederationConfigProvider` | 08 types, lazy (INV-AUTH-10) | singleton | Core | 08 |
| `IUpdateService`, `ReleaseFeed`, `AppVersion` | 10 types | singleton | Core | 10 |
| `IExternalLinks` | `ExternalLinks` | singleton | Core | 11 algorithm, 10 registration |
| every Platform seam of 2.3 | per spec | singleton | Platform | per spec |
| `IShellReveal`, `IUrlLauncher`, `IWebView2RuntimeInfo` | `ShellReveal`, `ShellUrlLauncher`, `WebView2RuntimeInfo` | singleton | Platform | 11 |
| `IUiDispatcher` | `WpfUiDispatcher` over the dispatcher passed to `AddShotAIApp` (`Dispatcher.CurrentDispatcher` at step 6) | singleton instance | App | 11 |
| `IAppLifetime`, `IAppInfo`, `IFileDialogs`, `IAppPaths` | `AppLifetime`, `AppInfoProvider`, `WpfFileDialogs`, `AppPaths` | singleton | App | 11, 10 |
| `IExportService`, `IExportDialogs`, `ExportEngine` | 09 types | singleton | App (service, dialogs), Core (engine) | 09 |
| `IAreaSelectionService`, `IMainWindowLayout`, `AppMenuViewModel`, `RecordingVisibilityController`, `CapturePillViewModel`, `PopupExclusion`, `OwnWindowRegistry` wiring | 03 types | singleton | App | 03 |
| `IShellNavigationState` | `NavigationState` | singleton | App | 06 |
| `INoticeService`, `IConfirmService`, `ThemeManager` | `NoticeCenter`, `ConfirmService`, `ThemeManager` | singleton | App | 06 |
| `StaRenderThread`, `IOverlayRasterizer` (`WpfOverlayRasterizer`) | 04 types | singleton | App | 04 |
| `EditorFactory`, `IColorPicker` | `EditorFactory` (C6), `Win32ColorPicker` | singleton | App | 04 7.10.1 |
| `ReportImageLoader`, `ReportImageDecoder`, `IImageSizeProbe` | 05 types | singleton | App, Platform | 05 |
| `IAppStartup` (multiple, in this order) | `RemoteVisibilityApplier`, `RecordingVisibilityController`, `ThemeManager` | singleton | App | 11 7.10 |
| view models | per spec | transient; named singletons only where a spec says so (today 06's `CaptureModePickerViewModel`, EDGE-HOME-57) | App | INV-IPC-22 |
| factories (C6) | per spec | singleton | App or Core | 4.1 |

### 4.4 The service catalog is the backbone

The view models see the application through the interfaces of 11 7.3, with the canonical names after 15.3; this table and 11 7.3 list the same interfaces. Nothing else is reachable from a view model except what INV-ARCH-3 names (the C6 factories, `IUiDispatcher`, `ILogger<T>`, and the editor's named Core dependencies).

| Interface | Namespace | What the UI does with it | Owner | Thread contract |
|---|---|---|---|---|
| `IProjectService` | `ShotAI.Core.Store` | list, create, rename, delete, archive, open, import step, merge, set projects folder; `ProjectsChanged` | 01 | free-threaded; writes on the store queue |
| `IProjectSession`, `IProjectSessionFactory` | `ShotAI.Core.Store` | the open project's `Current`, optimistic `Apply`, `ApplyDurable`, `WhenIdleAsync`, `Changed`, `PersistFailed` | 01 (contract consolidated in 7.4) | `Apply` and `Current` on the UI thread; events on the captured UI context |
| `ICaptureService` | `ShotAI.Core.Capture` | start, pause, resume, stop, discard, screenshot, list targets; `StateChanged`, `StepLanded`, `CaptureFailed`, `RecordingChanged` | 02 | free-threaded; events on engine threads |
| `IAreaSelectionService`, `IMainWindowLayout`, `IShellNavigationState`, `AppMenuViewModel` | `ShotAI.App.Shell` | area selection, detail resize, navigation state for the menu | 03, 06 | UI thread only |
| `ICaptureTargetSelection` | `ShotAI.App.Home` | `BuildTarget()`: the Home picker's current capture target, read by 05 for Resume (R-ARCH-26); implemented by 06's `CaptureModePickerViewModel`, the named singleton of INV-IPC-22 | 06 | UI thread only |
| `IStepFlattener`, `ISensitiveRegionScanner` | `ShotAI.Core.Rendering`, `ShotAI.Core.Redaction` | pre-egress flatten, merge re-bake, auto-redact scan | 04 | free-threaded; rasterizes on `StaRenderThread` |
| `IClaudeService` | `ShotAI.Core.Sop` | estimate, generate (with `IProgress<SopProgress>`), cancel | 07 | free-threaded |
| `IAuthService` | `ShotAI.Core.Auth` | status, sign in (owner HWND), sign out, key status, set, clear, test connection; `AuthStatusChanged` | 08 | free-threaded; interactive MSAL on the UI thread internally |
| `IExportService` | `ShotAI.App.Export` | the seven export and package entry points of 09 7.13 | 09 | entry on the UI thread (dialogs), work on the pool, WebView2 on the UI thread |
| `ISettingsService` | `ShotAI.Core.Settings` | `Current`, optimistic `UpdateAsync`, `Changed` | 10 | any thread |
| `IUpdateService` | `ShotAI.Core.Updates` | `Pending`, `UpdateAvailable`, `CheckNowAsync` | 10 | any thread |
| `IInstallInfo` | `ShotAI.Core.Install` | `Scope`: on a per-machine install the update notice and `Check now` say who installs updates (06 INV-HOME-45) | 12 | any thread (immutable) |
| `IExternalLinks`, `IShellReveal`, `IAppInfo`, `IFileDialogs` | 11 7.2 | open allowlisted links, reveal in Explorer, about data, pickers | 11 | per 11 7.3 |
| `INoticeService`, `IConfirmService` | `ShotAI.App.Chrome` | global notices and confirm dialogs | 06 | UI thread only |
| `IUiDispatcher`, `IAppLifetime` | `ShotAI.Core.Threading` | marshaling and the shutdown token | 11 | any thread |

The 87 Electron channels map onto these members exactly once (INV-IPC-4); `channel-map.json` is the machine-checked inventory (AC-IPC-1, AC-IPC-2).

### 4.5 Disposal and shutdown

`App.OnExit` on the UI thread, in this order (11 7.10, 03 7.4.1):

| Step | Action | Budget |
|---|---|---|
| 1 | `AppLifetime` cancels `Stopping`; every linked operation token cancels | none |
| 2 | `ICaptureService.Teardown()`: detach hook and hotkey synchronously, stop the poll and UIA threads | bounded by the 2000 ms hook-thread join (02 7.13), INV-SHELL-19 |
| 3 | `ShutdownFlush.Run(TimeSpan.FromSeconds(5))`: `Task.WhenAll(projects.FlushAsync(t), settings.FlushAsync(t)).Wait(t)`; on timeout log Warning `exit: pending writes not flushed within 5 s` and continue | 5 s total, the one allowlisted blocking wait (INV-IPC-21, Q-IPC-10) |
| 4 | `ActivationListener.Dispose()`, then the instance lock (on the acquiring UI thread) | none |
| 5 | `provider.Dispose()` | each `Dispose` bounded by its spec (log provider drains with a 2 s cap, 10 7.10) |
| 6 | log `exiting (code <n>)`, flush the log sink | 10 |

`SessionEnding` (logoff, shutdown) calls `Teardown()` first and lets WPF continue to `OnExit`; nothing ever vetoes (03 7.4.1). Closing the main window ends the process (`ShutdownMode = OnMainWindowClose`, INV-SHELL-4).

---

## 5. MVVM conventions

### 5.1 Toolkit usage

| Rule | Detail | Source |
|---|---|---|
| Base class | Every view model derives from `ViewModelBase : ObservableObject`; in Debug builds `OnPropertyChanged` calls `Dispatcher.VerifyAccess()` so a cross-thread write fails loudly. | 11 Q-IPC-17, `Threading.ViewModelAffinityTests` |
| Properties | `[ObservableProperty]` on private fields; `SetProperty` raises only on change, which is what makes card diffing cheap (05 7.8). | CommunityToolkit.Mvvm |
| Commands | `[RelayCommand]`; async commands are `AsyncRelayCommand` with concurrent execution disallowed (the default), so a second click while running is ignored; `NotifyCanExecuteChanged()` is called explicitly when a guard changes, because the toolkit does not requery by itself. | T11, 03 7.4.3 |
| Busy before modal | A command that shows a modal sets its busy state before showing it. | EDGE-IPC-40 |
| Presenters | Pure state machines live in Core (`PillPresenter`, `EditorDocument`, `ReportEditState`, `DocScaleEditor`, `HomeSelection`, `RenameSession`, `TourLayout`, `BrandMenuModel`); the view model wraps one and exposes its state, so the behavior tables of the specs are tested on Linux. | 2.3 |
| No logic in code-behind | Code-behind is limited to input plumbing WPF cannot bind (pointer mapping in the editor canvas, drag and drop, `HwndSource` hooks, focus management) and forwards to the view model. | 04 7.10, 05 7.12 |
| Strings | User-visible strings are constants in a per-subsystem Core class (`ShellStrings`, `ReportStrings`, `SettingsText`, `SopMessages`, `ExportMessages`, `HomeText`) with a test that pins each against the spec's string table; XAML binds to them, it never contains product text. | each spec's string tables |

### 5.2 Views

- XAML binds only to its view model, never to the manifest or a service (05 7.8 "Card XAML never binds to the manifest").
- Every theme reference is a `{DynamicResource ...}` key from `ThemeTokenKeys`; no colour or radius literal outside `Themes/FixedColors.xaml` and the named exceptions (INV-HOME-24). `XamlResourceKeyGuardTests` and `XamlChromeGuardTests` run on Linux as source scans (06 Q-HOME-1).
- Every interactive element has an explicit `AutomationProperties.Name` equal to its Electron accessible name (06 7.13, 05 7.18).
- Every top-level window derives from `ShotAIWindow` (registers its HWND for capture exclusion in `OnSourceInitialized`, INV-SHELL-1); every `Popup` shotAI declares is a `ShotAIPopup` (03 7.4.7).

### 5.3 Navigation between Home, project detail and Settings

`ShellViewModel` (06 7.7) owns `CurrentView` (`Home`, `Project`, `Settings`, `Recording`) and `SettingsReturnsTo`, computed from 02's capture status, 05's open project and its own `SettingsOpen`, exactly the 06 2.1 state machine. `NavigationState` projects it for 03's menu and the theme manager (`IShellNavigationState`).

| Transition | What lives, what dies | IDs |
|---|---|---|
| Home to project | `ProjectDetailViewModel.OpenAsync(path)`: open generation incremented; `IProjectService.OpenProjectAsync`; `IProjectSessionFactory.Create(opened)` on the UI thread; stale results of an older open are discarded and their session disposed | 05 7.3, EDGE-REP-44 |
| Project to Home (Back) | the view token is cancelled (image loads, merge flatten, export flatten); the session unsubscribes and is disposed in the background, draining its writes on the shared queue; `IMainWindowLayout.SetDetailView(false, 1)` | 05 7.3, INV-REP-31 |
| Project to Settings and back | NOT Back: the session, `ReportEditState` and open drafts survive; only an in-flight export flatten is cancelled | 05 D-REP-25, EDGE-REP-41 |
| Any view to Settings | `SettingsView` and its view model are created fresh on every open (starts at the AI tab, re-reads auth status) | 06 7.7, INV-HOME-42 |
| Home to anything | `HomeView` is created once and kept hidden, so its scroll offset survives by construction; list controls reset on entry (parity) | 06 7.7, EDGE-HOME-3 |
| Recording start and end | `RecordingVisibilityController` hides the main window and shows the pill; the end restores and activates the main window; Settings and Import requests are ignored while a session exists | 03 7.4.6, INV-HOME-18, INV-SHELL-7 |

### 5.4 Windows, dialogs and overlays

Top-level windows are few and each one is registered before it is first shown:

| Window | Kind | Registered | Owner |
|---|---|---|---|
| Main window | WPF, standard chrome | yes | 03 |
| Capture pill | WPF, non-activating tool window | yes | 03 |
| Area overlays (one per monitor) | WPF, `AllowsTransparency`, `#01000000` fill | yes | 03 |
| Discard confirmation, About | small WPF dialogs | yes | 03 |
| PDF host | a never-shown Win32 popup (`CreateWindowEx`), not WPF | yes | 09 7.7 |
| Legacy-instance notice | Win32 `MessageBox`, before any window exists | no (Q-SHELL-19; removed at stage S5) | 12 7.10.3 |

Everything else that looks like a dialog is an element of the main window's in-window overlay layer (03 provides it, 03 7.4.10): the confirm dialog (`ConfirmHost`, 06 7.11), the tour, notices, Home's capture target dropdown (`TargetDropdownView`, 06 7.6, R-ARCH-19), the editor (`EditorOverlayView`, 04 7.10.1), the SOP review and progress dialogs (07 7.10), the report's popovers, insert menu, package dialog and capture-insert modal (05 7.13). The rule (R-ARCH-19): a new popover or modal is drawn in the overlay layer; a `ShotAIPopup` is used only where WPF requires a popup HWND (tooltips, context menus, `ComboBox` drop-downs, 06's `OverflowMenu`), and every such HWND is registered (INV-HOME-43, D15 of 03). File pickers go through `IFileDialogs` and `IExportDialogs` with the main window as owner (D-IPC-16).

### 5.5 Notices

One notice control, `ShotAI.App.Chrome.NoticeHost` (its contract is 03 7.4.10; its look and live-region behavior are 06 7.10), rendered by two hosts:

| Host | Slots | Used by |
|---|---|---|
| `NoticeCenter` (`INoticeService`, 06 7.10) | `Error`, `Update` | Home, Settings, capture errors on the main window (`Capture error: <message>`) |
| `NoticeStackViewModel` (05 7.16) | `ImportError` (`Import failed: `), `ExportError` (`Export failed: `), `SaveError` (the rollback notice), `Info` | the report |

Rules: the text is `UserMessage.From(exception)` (8.2) with the owning spec's prefix; a new message replaces the slot's previous one; no auto-dismiss (parity); errors announce assertively, info politely, and the host raises `LiveRegionChanged` itself because WPF does not (06 7.10). The rollback notice is `Your last change couldn't be saved and was undone. ` followed by the exception message (05 7.5, answering Q-MODEL-12). A failure of background work (refresh, theme apply, tour flag, menu state) is logged at Warning and never shown as a failure of something the user did (06 7.14, D-HOME-29).

### 5.6 Subscribing to a service event

Every view model that listens to a singleton uses the one template of 11 7.7: subscribe, then read the snapshot; on each event `IUiDispatcher.Post` a closure that checks disposal and re-reads the snapshot (payload events such as `StepLanded`, `CaptureFailed` and `UpdateAvailable` apply their payload); unsubscribe in `Dispose`. `Composition.SubscriberDisposalTests` proves no disposed view model stays in an invocation list.

---

## 6. Threading model

### 6.1 Threads

| Thread | Created by | Apartment | Runs | Must never | Reaches the UI by | Owner |
|---|---|---|---|---|---|---|
| WPF UI thread | `Program.Main` | STA | every window, view model, `ObservableObject`, `ProjectSession.Current` and its optimistic `op.Apply`, notices, confirms, WebView2 calls, MSAL interactive calls | wait synchronously on any service, lock or worker (except the exit flush) | itself | 03, 11 |
| Store queue consumer | `SerialWriteQueue` in `ProjectStore` | MTA pool | every `project.json` write and delete, in FIFO order, for all projects | touch a UI object | awaited tasks; `ProjectSession` posts events to its captured context | 01 7.7 |
| Settings queue consumer | a second `SerialWriteQueue` in `SettingsService` | MTA pool | `settings.json` writes, re-read inside each write | same | `ISettingsService.Changed` (marshaled by subscribers) | 10 7.4.3 |
| Input hook thread `shotAI.InputHook` | `Win32TriggerSource.Attach` | own message loop | `WH_MOUSE_LL`, `RegisterHotKey`, `GetMessageW` loop, writes a 256-entry preallocated ring | allocate, lock, log, or do anything but copy and `CallNextHookEx` | the ring and a kernel event | 02 7.3, 7.4, INV-CAP-17 |
| Capture dispatcher `shotAI.CaptureDispatcher` | engine | MTA | mousedown decisions, the synchronous click-time menu grab, starting element queries, enqueuing jobs | wait on the capture worker or the UI thread | engine events | 02 7.3 |
| Capture worker | engine (async loop over `Channel<CaptureJob>`) | MTA pool | one capture at a time: grab, downscale, encode, write, store call, events | run two jobs at once; be thread-affine | engine events (raised outside the engine lock, via `EventRaiser`) | 02 7.3 |
| Menu poll task | engine (`PeriodicTimer` 400 ms per arm) | MTA pool | refreshing the menu arm's frame | outlive its arm | none | 02 7.3 |
| UIA workers `shotAI.Uia.0`, `.1` | `UiaElementLocator.WarmUp` | MTA | UI Automation COM calls, 500 ms connection and transaction timeouts, 600 ms overall cap | touch WPF | awaited task | 02 7.6 |
| `StaRenderThread` `shotAI render` | lazily, 04 | STA with its own `Dispatcher` | `RenderTargetBitmap` overlay rasterization for the bake | touch the UI thread's objects | awaited task | 04 7.10.6 |
| Short-lived STA threads | `StaThread.RunAsync` | STA | `SHOpenFolderAndSelectItems`, `ShellExecute` for URLs and folders | outlive the call | awaited task | 11 7.3.3 |
| Log writer | `RotatingFileSink` (`LongRunning` task) | MTA | batched file appends, rotation | block a caller | none | 10 7.5.4 |
| Thread pool | BCL | MTA | file reads, WIC decode and encode, redaction bake, OCR recognition (serialized by a semaphore), export building, AVIF (multi-threaded inside libavif), zip, SDK calls, token acquisition, update check | touch UI objects | awaited tasks; `IProgress<T>` created on the UI thread | all |
| WebView2 browser processes | WebView2 runtime | out of process | print copy rendering | | WebView2 events on the UI thread | 09 7.7 |

### 6.2 Rules

The rules T1 to T12 of 11 7.6 are normative; in summary:

| # | Rule | Enforcement |
|---|---|---|
| T1 | The UI thread owns every window, view model, `IProjectSession.Current`, `INoticeService` and `IConfirmService` call. | `Threading.ViewModelAffinityTests` |
| T2, T3 | Core and Platform are free-threaded and use `ConfigureAwait(false)`, except the three UI-affine types: 08's `MsalGateway.AcquireInteractiveAsync`, 09's `WebView2PdfRenderer`, 01's `ProjectSession` event raising. | `CA2007` as error in Core and Platform, per-file suppression with justification for those three |
| T4 | App code awaits without `ConfigureAwait(false)`, so continuations return to the UI thread. | `CA2007` off in App |
| T5 | Services raise events on the thread that caused them, outside their locks, through `EventRaiser.Raise` (a throwing handler is logged and the rest still run). | `Threading.EventRaiserTests`, INV-IPC-24 |
| T6 | App subscribers marshal with `IUiDispatcher.Post` only; `Dispatcher.Invoke`, `BeginInvoke` and `InvokeAsync` are banned outside the allowlisted files of 14.9. | BannedApiAnalyzers |
| T7 | Subscribe, then read; re-read the snapshot on each event (INV-IPC-7). | `Threading.SubscribeThenReadTests` |
| T8 | Progress is a `Progress<T>` made by the calling view model on the UI thread, guarded by a run token and the view model's disposal (INV-IPC-8). | `Threading.StaleProgressTests` |
| T9 | The UI thread never blocks on a service; the only synchronous members it calls are snapshot readers (`ICaptureService.GetState`, `ISettingsService.Current`, `IUpdateService.Pending`, `IAppInfo.Current`, `IProjectSession.Current`, `IShellNavigationState`); `ICaptureService.Pause` and `Resume` go through `Task.Run` because they may take the engine lock. | `VSTHRD002` and banned `Task.Wait`, `.Result`, `GetAwaiter().GetResult()` in App, one allowlisted file (`ShutdownFlush.cs`) |
| T10 | A view model never decodes, encodes, zips or hashes on the UI thread. | code review; 05 7.11, 09 7.14 |
| T11 | Async commands disallow concurrent execution. | `Composition.CommandConventionsTests` |
| T12 | `async void` only for WPF event handlers, whose body is wrapped in `try` and routed to a notice or the log. | `VSTHRD100` as error with per-site suppression |

### 6.3 Marshaling

- `IUiDispatcher.Post` queues at `DispatcherPriority.Normal` and never runs inline, so every subscriber observes events in raise order (INV-IPC-5). It is also the priority `await` continuations and `Progress<T>` use, which is why events do not get a lower priority (Q-IPC-20).
- Because `Normal` runs before layout, render and input, each posted action is short. A snapshot subscriber (`StateChanged`) may coalesce: skip a post while its own previous post is still queued. Payload events (`StepLanded`, `CaptureFailed`, `UpdateAvailable`) are never coalesced (Q-IPC-20).
- `ProjectSession` captures `SynchronizationContext.Current` at creation (the factory must be called on the UI thread) and posts `Changed` and `PersistFailed` there. It is the only Core type that touches a synchronization context (banned elsewhere in Core, 11 7.12).
- Focus and layout deferrals inside a view (the inline text box's refocus at input priority, 04 7.10.5; Home's scroll restore after layout, 06 7.7) are not service marshaling. They use `LayoutUpdated` one-shot handlers or the App helper `UiDeferral` (one allowlisted file, R-ARCH-18), never a raw `Dispatcher.BeginInvoke`.

### 6.4 Deadlock rules

| # | Rule | Why | IDs |
|---|---|---|---|
| DL1 | No UI-thread code path takes `CaptureShield`'s lock. Window registration first applies `WDA_EXCLUDEFROMCAPTURE` without the lock, then posts the reconcile to the pool; every runtime change of the setting (11's `RemoteVisibilityApplier`, rollbacks included) calls `ApplyRemoteVisibility` through `Task.Run` in a serialized latest-wins loop, never through `IUiDispatcher.Post`. The one reasoned exception is the startup application at 4.2 step 10, which runs synchronously on the UI thread because no capture session exists yet, so no shield can be held; the only other lock holders then are the registration reconciles of step 8. If the Phase B probe (Q-CAP-15) shows that a cross-thread `SetWindowDisplayAffinity` is serviced by the owning UI thread, a reconcile could hold the lock waiting on the UI thread, and step 10 then also moves to `Task.Run` (the windows stay excluded until it runs, so the order still fails closed). | a cross-thread `SetWindowDisplayAffinity` may be serviced by the owning UI thread while the UI thread waits on the lock | 02 7.8, INV-CAP-29, Q-CAP-22, 11 7.3.6 |
| DL2 | The UI thread never waits synchronously on capture work; capture threads never wait on the UI thread. If the Phase B probe shows that affinity calls must run on the UI thread (Q-CAP-15), the grab thread posts the change and waits for its completion with a bounded timeout, and on timeout skips the grab (fail closed, a missed step with a `CaptureFailed` notice), never grabs unshielded (Q-ARCH-6). | INV-SHELL-21; INV-CAP-4 | 02 7.3 |
| DL3 | UIA calls never run on the UI thread, and the own-window gate runs before any element query, so a hit test never lands in shotAI's own UIA provider. | UIA into our own provider marshals to our UI thread (macOS crash) | 02 D1, INV-CAP-15 |
| DL4 | The exit flush blocks the UI thread, so no store job, settings job or `Dispose` may need the UI thread. | the only allowlisted synchronous wait | INV-IPC-21, `Shutdown.ExitFlushTests` |
| DL5 | MSAL interactive runs on the UI thread through `IUiDispatcher.InvokeAsync`, never awaited with `ConfigureAwait(false)` before the dispatch. | WAM needs the owner window and the UI synchronization context | 08 7.5, 7.15 |
| DL6 | WebView2 calls run on the UI thread and are awaited, never waited; one print at a time. | WebView2 is UI-affine | 09 7.7 |
| DL7 | `SetWindowsHookExW` and `UnhookWindowsHookEx` and `RegisterHotKey`/`UnregisterHotKey` happen on the hook thread; `Detach` posts to it and joins with a 2000 ms timeout. | hotkeys are per registering thread | 02 7.4 |

### 6.5 Cancellation

Token topology (11 7.8 K2):

```
IAppLifetime.Stopping (cancelled at the start of App.OnExit)
  +-- view lifetime token (per open project view, per Settings view, per editor)
        +-- operation token (one CancellationTokenSource per command run)
```

| Rule | Detail | IDs |
|---|---|---|
| K1 | Every async member that does IO or waits on the network, a dialog, OCR or the user takes a `CancellationToken` last. Store mutations do not: a queued job observes cancellation only before it starts and never half-applies. | 11 7.8 |
| K3 | A user cancel surfaces as `OperationCanceledException`, caught `when (token.IsCancellationRequested)` and ignored; `UserMessage.From` returns `null` for it. | 11 7.9 |
| K4 | `IClaudeService.Cancel()` survives as Electron's global cancel verb; view models call it and cancel their own token. | 07 7.10 |
| K6 | A token cancelled after a durable job started does not stop the job; the view ignores the result after disposal. | INV-IPC-21 |
| K7 | Non-interruptible work (WinRT OCR, a started atomic write, the WebView2 print) finishes and its result is discarded. | 04 D-EDIT-12 |
| K8 | Timeouts belong to the owning spec (UIA 600 ms, WebView2 60 s navigation and 120 s print watchdog, exchange 60 s, SDK 10 minutes), never to UI tokens. | 11 7.8 |
| CA2016 | Forwarding the token is an analyzer error. | 14.9 |

### 6.6 COM and apartments

| Component | Apartment | Note |
|---|---|---|
| UI thread | STA | WPF requirement; `[STAThread]` on `Program.Main` |
| UI Automation | MTA (dedicated threads) | 02 7.6 |
| WIC (`IWICImagingFactory`) | one MTA factory, owned by `ShotAI.Platform.Imaging.WicFactory` (02 7.1; no other type creates one), per-image objects per job | all in-box WIC codecs support MTA since Windows 7 (02 7.12) |
| Overlay rasterizer | dedicated STA with its own dispatcher | `RenderTargetBitmap` needs a dispatcher thread (04 7.10.6) |
| Shell reveal and URL launch | short-lived STA | Shell APIs expect STA (11 7.3.3) |
| `Windows.Media.Ocr.OcrEngine` | agile | called from the pool (04 7.9) |
| WebView2 | UI STA | 09 7.7 |

---

## 7. State and persistence

### 7.1 Who owns which state

| State | In memory | On disk | Write path | Read path | Owner |
|---|---|---|---|---|---|
| Project list | `ProjectSummary` records in `HomeViewModel` | each project's `project.json` | none (derived) | `IProjectService.ListProjectsAsync`, re-listed by Home's refresh policy | 01, 06 |
| Open project | `IProjectSession.Current` (`ProjectManifest`, steps as raw `JsonObject`) | `<project>/project.json` | `Apply` (optimistic) or `ApplyDurable` | `Current` on the UI thread | 01, 05 |
| Screenshots | none (decoded on demand) | `<project>/shots/step-NNNN.png` | capture engine, `ImportStepAsync` | `ProjectStore.ResolveImage` then a decode off the UI thread | 02, 01, 05 |
| Renders | none | `<project>/export/.render/<id>.png` | `IStepRenderWriter` inside the store job, atomically, rolled back if the manifest write fails | the render gate for egress, `ResolveImage` for the report | 04 |
| Settings | `ISettingsService.Current` (immutable `AppSettings`) | `%APPDATA%\shotAI\settings.json` | `UpdateAsync` (optimistic, own queue) | `Current`, lock-free | 10 |
| API key | never cached | `%APPDATA%\shotAI\secrets.dpapi.json` | `ApiKeyStore.SetAsync`, `ClearAsync` (not optimistic) | the client factory only | 08 |
| Token cache | MSAL | `%LOCALAPPDATA%\LFI\shotAI\entra\msal-cache.bin` (R-ARCH-13) | MSAL extension with its cross-process lock | MSAL | 08 |
| Policy | `FederationConfigProvider` cache, invalidated on every status read | HKLM policy key, baked resource | never written by shotAI | `GetStatusAsync` re-reads | 08 |
| Capture session | `CaptureEngine` under its gate lock | the steps it writes | store calls on the shared queue | `GetState()` snapshot | 02 |
| UI state (selection, drafts, zoom of the editor) | view models and Core presenters | nothing (parity: no drafts persisted) | none | none | 04, 05, 06 |

### 7.2 The in-memory project model

- A step is a `ProjectStep` wrapping its raw `JsonObject`, with typed lenient views whose setters keep key position; a decode never loses or reorders a key the app does not know (INV-MODEL-5, 01 7.3). Annotations stay a raw `JsonArray` read through typed views that never throw (04 7.2).
- JSON is System.Text.Json's `JsonNode` tree read by `JsJson.Parse` (ECMAScript `JSON.parse` semantics: lone surrogates, duplicate keys, U+FFFD, BOM skip) and written only by `JsJson.Stringify`, a hand-written `JSON.stringify` emitter (01 7.2, D-1, D-2; interpretation I-1 in 15.2). A tree may hold a non-finite double, so it is never handed to a System.Text.Json serializer.
- Root keys are emitted in canonical order (01 D-3); the conformance suite and the golden files compare decode-then-encode (Q-MODEL-3).
- Every number is a `double`; every JS rounding goes through `JsMath.Round`; every JS trim through `JsString.Trim`; ids are lowercase v4 GUIDs; timestamps are `IsoTime.ToIsoString` (01 7.2.3).

### 7.3 The write queue and the store job

- One `SerialWriteQueue` per `ProjectStore`, shared by every project, strictly FIFO in `Enqueue` order (a `Channel` with one consumer, never `SemaphoreSlim`), so captures, report edits, SOP applies and archive operations never interleave (01 7.7; Electron `src/main/project-store.ts:637`, `:647-670`).
- Each job re-reads the manifest from disk, applies its change, bumps `updatedAt` unless the change returned `Unchanged`, and writes atomically (01 2.9.3). This keeps Electron's persistence semantics under the optimistic layer.
- A job's exception completes only its own task; the queue continues. A job whose token is cancelled before it starts never runs; once started it completes (01 7.7, INV-IPC-21).
- Reads outside the queue (list, get-for-read) may overlap a write and see the old or the new file, never a torn one (INV-MODEL-12).

### 7.4 `IProjectSession`: the consolidated contract

01 7.10 defines the session; 05, 07, 09 and 11 each asked for additions. This is the contract the implementation must meet (R-ARCH-5, R-ARCH-6, R-ARCH-23):

```csharp
namespace ShotAI.Core.Store;

public enum ManifestChangeKind { Local, Persisted, RolledBack, Durable, External }   // moved here from 05's ShotAI.Core.Report
public sealed record ManifestChangedEventArgs(ManifestChangeKind Kind, IReadOnlyList<string>? AffectedStepIds);
public sealed record PersistFailedEventArgs(ProjectOperation Operation, Exception Error);

public abstract class ProjectOperation
{
    public abstract MutateResult Apply(ProjectManifest m);          // pure: no IO, no clock, no random ids
    public virtual bool BumpsUpdatedAt => true;                      // no carrier yet; see the note after S1 to S10 (01 Q-MODEL-25)
    public virtual IReadOnlyList<string>? AffectedStepIds => null;   // null = structural; 05's ReportOperation overrides
}

public interface IProjectSession : IAsyncDisposable
{
    string ProjectDir { get; }
    ProjectManifest Current { get; }                                 // UI thread
    int PendingCount { get; }
    event EventHandler<ManifestChangedEventArgs>? Changed;           // captured UI context
    event EventHandler<PersistFailedEventArgs>? PersistFailed;       // captured UI context
    Task<ProjectManifest> Apply(ProjectOperation op);                // optimistic; UI thread
    Task<ProjectManifest> ApplyDurable(Func<IProjectService, Task<ProjectManifest>> call);
    Task WhenIdleAsync(CancellationToken ct = default);
}

public interface IProjectSessionFactory { IProjectSession Create(OpenedProject opened); }   // UI thread only
public interface IProjectSettle { Task WhenSettledAsync(string projectPath, CancellationToken ct); }
```

| # | Rule | Requested by |
|---|---|---|
| S1 | `Apply(op)` runs `op.Apply` on a deep clone of `Current` on the UI thread. `Changed`: `Current` becomes the clone, `Changed(Local, op.AffectedStepIds)` is raised, and the same operation is queued as `MutateAsync(dir, m => op.Apply(m))`. `Unchanged`: nothing is queued; the task completes with `Current`. | 01 7.10 |
| S2 | If `op.Apply` throws on the clone, `Current` is untouched, nothing is queued, no event is raised, and the returned task is faulted with that exception (so `StepNotFoundException`, `SopNoLandingException` and `NothingToRevertException` reach the caller unwrapped). | 05 7.5 request 1, 07 Q-SOP-21 |
| S3 | When the last pending operation persists, `Current` becomes the persisted manifest. The event is `Changed(Persisted)` when it equals the previous `Current` apart from `updatedAt`, else `Changed(External)` (the disk carried a change this session did not make). | 05 7.5 request 4, 7.6 |
| S4 | When a queued operation fails, the session re-reads the disk (or uses the last persisted manifest if the read fails), re-applies every still-pending operation in order, sets `Current`, raises `Changed(RolledBack)` and `PersistFailed(op, error)`, and faults that operation's task. | 01 7.10, 05 7.5 request 5 |
| S5 | `ApplyDurable(call)` awaits `call(IProjectService)`; `Current` becomes its result with every optimistic operation issued after the durable call started re-applied on top; `Changed(Durable)` is raised. It takes the interface, not the concrete `ProjectStore`. | 05 7.5 request 2, 11 7.3.2 |
| S6 | `WhenIdleAsync` completes when every operation queued and every durable call started before the call has finished (persisted or rolled back). | 07 INV-SOP-28, 09 INV-EXP-28 |
| S7 | `IProjectSettle.WhenSettledAsync(path)` finds every registered session for `Path.GetFullPath(path)` (compared `OrdinalIgnoreCase`, 02 D9), the open one and any disposed one still draining (S9), and awaits their `WhenIdleAsync`; with none registered it completes at once. 09's requested `IProjectService.WhenIdleAsync(path)` is this member (R-ARCH-6). | 07 7.13, 09 Q-EXP-11, R-ARCH-27 |
| S8 | Events are posted (never sent) to the context captured by `Create`; handlers check `sender == currentSession` (INV-REP-31). | 01 7.12, 05 7.3 |
| S9 | `DisposeAsync` stops raising events at once and lets queued writes drain on the shared queue; it never cancels them. The session stays registered with `IProjectSettle` until every operation and durable call it accepted has finished (persisted or rolled back), and unregisters only then, so an export or SOP run started from Home right after Back still waits for those writes (superseded wording: "unregisters at `DisposeAsync`", R-ARCH-27). | 01 7.12, 05 7.3, R-ARCH-27 |
| S10 | Operations are pure and deterministic, with ids and timestamps fixed at construction, because the same instance runs twice (clone, then disk). | 01 7.10, 07 7.9 |

`VSTHRD200` would reject the fixed names `Apply` and `ApplyDurable`; they keep their names with a justified per-member suppression (Q-IPC-21 default).

Open item (01 Q-MODEL-25): `ProjectOperation.BumpsUpdatedAt` has no carrier today, because S1 queues `IProjectService.MutateAsync(dir, fn)`, whose job always bumps `updatedAt` unless `fn` returns `Unchanged`, and `MutateAsync` (11 7.3.2) has no parameter for it. No 2.0.0 operation overrides the property, so nothing changes now. The PR that first overrides it adds an optional `bool bumpUpdatedAt = true` parameter to `IProjectService.MutateAsync` here and in 11 7.3.2, and S1 passes `op.BumpsUpdatedAt`.

### 7.5 Which path each edit takes

| Edit | Path | Visible when | On failure | Owner |
|---|---|---|---|---|
| report zoom, pan, move step, caption, instructions, delete step, text step save, callout change, overview, add text step, document scale | `session.Apply(ReportOperation)` | immediately | rollback plus the rollback notice; text drafts re-open (05 R4, R5, R8, R10) | 05 7.5 |
| View, Brand pin | `session.Apply(SetProjectThemeOperation)`; the value passes through untouched, `null` clears (INV-IPC-14) | immediately | rollback plus notice | 03 7.4.5 |
| SOP apply, SOP revert | `session.Apply(ApplySopPlanOperation)`, `session.Apply(RevertSopOperation)` (revert also invalidates changed renders, INV-SOP-29) | immediately after generation returns | rollback, notice, and `SopMessages.Incomplete` for a no-landing disk run | 07 7.9 |
| editor save | durable and synchronous: flatten, then `ApplyDurable(s => s.UpdateStepAsync(path, id, patch, png))`, then close | after the render and manifest are on disk | the editor stays open with the message | 04 7.10.7, INV-EDIT-10 |
| merge | durable: flatten the kept step from its raw screenshot off the UI thread, then `ApplyDurable(s => s.MergeStepsAsync(...))` | after both writes | the merge alert | 05 7.14, INV-REP-19 |
| insert image, insert screenshot | durable (`ImportStepAsync`, `CaptureScreenshotAsync`) | after the write | `Import failed: <message>` | 05 P2, P3 |
| export and package export | durable pre-egress flatten through the session overload of `EnsureFlattenedAsync`, then the export | after the export | `Export failed: <message>` | 05 P5, P6 |
| capture steps during a recording | the engine calls the store directly on the shared queue; the project view is not shown while recording (INV-HOME-18). After the recording 05's `Adopt(dir, manifest)` takes the result: for the project already open it calls `session.ApplyDurable(_ => Task.FromResult(manifest))`, so `Current` becomes the adopted manifest with later optimistic operations re-applied (S5) and the session raises `Changed(Durable)`, which 05 reconciles exactly like `External`; with no open session (or another project open) it creates one from the manifest through `IProjectSessionFactory.Create` | when the recording ends | `CaptureFailed` on the pill | 02 7.12, 05 7.3 |
| Home rename, archive, restore, delete | list-level optimism in `HomeViewModel` over a queued `IProjectService` call; the row is restored with a notice on failure | immediately | row restored, notice | 06 D-HOME-7 |
| every Settings control | `ISettingsService.UpdateAsync` (applied and coerced at once, written on the settings queue, rolled back with `Changed(IsRollback: true)`) | immediately | rollback, notice | 10 7.4.3, 06 7.12 |
| remote visibility | the settings write above; `RemoteVisibilityApplier` applies the shield on every change, rollbacks included, through `Task.Run` in a serialized latest-wins loop (re-reading `settings.Current.RemoteVisible`), never on the UI thread (DL1) | immediately | the shield follows the rolled-back value | INV-IPC-13, D-IPC-8, 11 7.3.6 |
| API key set and clear | not optimistic; awaited; `AuthStatusChanged` after success | after the atomic write | `Error: <message>` in Settings | INV-AUTH-29 |
| tour seen flag | `UpdateAsync` fire and forget with a logged continuation | immediately | logged only | INV-HOME-20 |

### 7.6 The synchronous exception: editor save and flatten

The fixed decision's one exception is the editor save, because the redaction bake must be on disk before anything can read it. The flatten always starts from the original screenshot (INV-EDIT-5), the baked list is exactly the persisted list (INV-EDIT-32), and the store job writes the render first, then the manifest, restoring the previous render bytes if the manifest write fails, so a reader never sees a render that disagrees with the manifest (INV-EDIT-27, D-EDIT-5). Success is reported only after both writes (INV-EDIT-10). The same durable shape covers every other write of a file besides `project.json` (merge re-bake, image import, screenshot insert, pre-egress re-bakes).

A patch that touches `annotations` or `crop` without a fresh PNG clears `flattened` and `markerBaked` (INV-EDIT-7), and `UpdateStepAsync` and `MergeStepsAsync` use the one applier (INV-EDIT-8), so an optimistic path can never leave a stale render trusted by the gate.

### 7.7 Egress reads a settled, flattened project

Before any egress (a Claude request, an export, a package), the caller:

1. brings every shot step's render up to date with `IStepFlattener.EnsureFlattenedAsync` (the session overload inside an open project, the direct-store overload from Home) and does not proceed if that fails (INV-EXP-27, INV-REP-20, INV-HOME-11);
2. waits for the project's pending optimistic writes through `IProjectSettle.WhenSettledAsync` (INV-SOP-28, INV-EXP-28);
3. reads the project with `GetProjectForReadAsync` and obtains every image only through the render gate (INV-EDIT-6, INV-SOP-3, INV-EXP-1).

### 7.8 Settings persistence

`SettingsService` (10 7.4.3) is the settings analogue of the session: `Current` is an immutable snapshot read lock-free from any thread (the capture caches `CaptureScaleNow()` and `RemoteVisibleNow()` read it on the hook and capture threads, INV-INFRA-18); `UpdateAsync` folds pending changes over the last persisted snapshot, raises `Changed` at once, and queues a job on the service's own `SerialWriteQueue` that re-reads the file (so a hand edit made while running is merged), writes atomically, and on failure rolls back only the failed change. Unknown keys and their positions survive every write (INV-INFRA-14); unrecognised enum strings and nested `sop` keys are preserved while the user has not changed them (Q-INFRA-1, Q-INFRA-2); a corrupt file is backed up to `settings.json.bad` before it can be replaced (Q-INFRA-12). No secret is ever written to `settings.json` (INV-INFRA-19).

### 7.9 Reconciling with external changes

| Possible writer | How it is handled | IDs |
|---|---|---|
| Another native instance | prevented: the single-instance mutex per user session surfaces the first instance and exits | INV-SHELL-5 |
| The Electron build | prevented in one direction: native refuses to start while Electron runs in the same session; an Electron launch while native runs cannot be prevented | INV-PKG-26, EDGE-PKG-23 |
| The capture engine inside this process | the same FIFO store queue as the UI's edits | 02 7.12 |
| macOS, a sync client or a hand edit of a shared folder | every store job re-reads the disk; the session adopts the persisted result and reports `External` when it differs from what the session expected (S3); a queued operation that no longer applies faults and rolls back (S4) | 7.4 |
| Changes on disk while a project is open and nothing is being written | not observed until the next write, reopen or Home refresh (parity: Electron watches nothing either); a file watcher is not in 2.0.0 | Q-ARCH-7 |
| Home list | re-listed on the 20 s tick, on window activation and after user operations; the newest result wins; the tick pauses while renaming, selecting or busy | 06 2.18, D-HOME-1, D-HOME-2 |
| Settings file | re-read inside every settings write; otherwise read at startup only | 10 7.4.3 |
| Policy | re-read on every auth status request | INV-IPC-12, INV-AUTH-9 |

### 7.10 Atomic writes

Every file shotAI replaces is written through 01's `AtomicFile` (`project.json`, renders, `settings.json`, `secrets.dpapi.json`, single-file exports and packages, the archive rename): create the directory; write `<file>.<pid>.tmp` with `FileMode.Create`; flush and `Flush(flushToDisk: true)` (IMPROVEMENT D-5, cost measured in Phase A, Q-MODEL-5); rename over the target with `File.Move(overwrite: true)`, retrying transient lock failures (EPERM, EACCES, EBUSY) after 10, 25, 50, 100, 200, 350 and 600 ms (AC-MODEL-14); delete the tmp on any failure. The pid in the tmp name is safe because writes of one file are serialized and the app is single-instance. Cross-volume destinations are published by copying to a temp sibling in the destination folder and renaming there, never by a cross-volume move (09 D-EXP-2). Stale `project.json.*.tmp` siblings older than 24 hours are removed on open (Q-MODEL-20 default).

### 7.11 Exit and crash

Queued project and settings writes are drained for at most 5 s at exit (4.5 step 3, D-IPC-7, 01 D-18); a started write always completes. A crash on any thread is logged before the process ends (INV-SHELL-20), and the log sink is flushed synchronously from the unhandled-exception handler (10 7.5.4). Because every write is atomic, a crash leaves each file either old or new.

---

## 8. Errors, notices and logging

### 8.1 Exception taxonomy

Every expected, user-facing failure is a `ShotAIException` (11 7.9) whose `Message` is the Electron string verbatim (or the spec's IMPROVEMENT string). Anything else is a bug and shows the generic sentence.

| Spec | Types (all derive from `ShotAIException`) |
|---|---|
| 01 | `ProjectNotKnownException`, `StepNotFoundException`, `MergeIntoItselfException` (`cannot merge a step into itself`; replaces the earlier `InvalidOperationException`, 11 X2), `UnsupportedImageException`, `ImportRejectedException`, `ArchiveException`, `ManifestCorruptException`, all in `ShotAI.Core.Store` (01 7.13); `ImportLimits` messages |
| 02 | `CaptureException` (including the explicit-target message `A screenshot needs an explicit target (screen, window, or area).`) and `TriggerException`, both in `ShotAI.Core.Capture` (02 7.1) |
| 04 | `FlattenException` (failures `UnbakeableRedaction`, `CropUnapplied`, `UnknownAnnotation`, `EncodeFailed`), `RenderGateException` (with a `(string, Exception)` constructor, 07 D-SOP-22), `RefusedRenderPathException` |
| 07 | `SopException`, `SopDisabledException`, `NoScreenshotsException`, `NothingToRevertException`, `SopNoLandingException` (07 7.9). `SopNoLandingException` also derives from `ShotAIException`, but it is internal to the apply flow: its message is diagnostic, and the view model always shows `SopMessages.Incomplete` instead |
| 08 | `SignInRequiredException`, `FederationExchangeException`, `FederationNotConfiguredException`, `EntraSignInFailedException`, `ApiKeyStoreException` (INV-AUTH-36) |
| 09 | `ExportException`, `PdfRenderException` |

A spec that names a BCL exception for a user-facing message is corrected to a subclass (11 X2, Q-IPC-12); the foundation PR introduces `ShotAI.Core.Errors` first so each subsystem PR derives from it.

### 8.2 From exception to text

`UserMessage.From(Exception)` (11 7.9) is the one mapping:

| Exception | Text shown | Logged |
|---|---|---|
| `OperationCanceledException` (any, including `TaskCanceledException`) | nothing | no |
| `AggregateException` with one inner | the inner's mapping | as the inner |
| `ShotAIException`, `IOException`, `UnauthorizedAccessException` | `Message` verbatim; `UserMessage.Generic` if blank | at the caller's discretion (Warning for expected failures) |
| anything else | `Something went wrong. See the log for details.` | Error, with the operation name, type, message and stack, never user content (L7) |

No transport prefix is ever added (`Error invoking remote method ...` is ELECTRON-ONLY, D-IPC-2). Owning specs add their own prefixes (`Import failed: `, `Export failed: `, the Settings `Error: ` block, the pill's `A capture failed \u2014 see the log for details.` for an empty message). Services that "never throw" keep that contract (`CheckNowAsync`, `TestConnectionAsync`, `IExternalLinks.OpenAsync` refusal, `IClaudeService.Cancel`, `RevealExportDirectoryAsync`; 11 X7). SDK and MSAL errors are mapped by their owners before they leave the service (07 7.12, 08 7.17, INV-AUTH-24): `SopErrorMapper` walks the inner-exception chain, and connection errors are classified first (INV-SOP-13).

### 8.3 Crash handling

| Source | Handler | Behavior |
|---|---|---|
| UI thread | `Application.DispatcherUnhandledException` | log Error, `Handled = true`, show the generic notice if the main window is visible (Q-SHELL-16); an exception from `OnStartup` is not handled and terminates with a log line |
| any thread | `AppDomain.CurrentDomain.UnhandledException` | log Error with `terminating=`, flush the log synchronously |
| tasks | `TaskScheduler.UnobservedTaskException` | log Warning, `SetObserved()` |
| WebView2 | `CoreWebView2.ProcessFailed` | log Error with kind, reason, exit code |
| exit | `Application.Exit` | `exiting (code <n>)` |

(03 7.4.9, INV-SHELL-20.)

### 8.4 Logging

| Aspect | Rule | Source |
|---|---|---|
| API | `Microsoft.Extensions.Logging` with one first-party provider, `FileLoggerProvider` (Core, depends only on the abstractions); `Microsoft.Extensions.Logging.Debug` in Debug builds only; no third-party logging package | 10 7.5.1 |
| Files | `%APPDATA%\shotAI\logs\shotai.log`, rotated to `shotai.old.log` past 5 MiB (5 242 880 bytes); at most two files (INV-INFRA-20); the same files the Electron build writes (Q-INFRA-7) | 10 7.5.4 |
| Line format | `[yyyy-MM-dd HH:mm:ss.fff] [level] (label)    message`, local time, level names `error`, `warn`, `info`, `debug`, `silly`, label padded to width 11, `\r\n` line ends, UTF-8 without BOM: byte-compatible with electron-log so support reads old and new logs the same way | 10 7.5.2 |
| Levels | minimum `Information` in Release, `Debug` in Debug builds; `SHOTAI_LOG_LEVEL=debug` lowers a Release build for field troubleshooting; `Microsoft.*` and `System.*` categories at Warning | 10 7.5.1 |
| Categories | the type's namespace maps by longest prefix to a label: `projects` (Store, Settings, Archive), `capture`, `claude` (Sop, Auth), `ocr`, `svc` (Threading, Links, and the boundary `call:` lines), `main` (everything else), and the empty banner label; no label longer than `projects` | 10 7.5.3, 11 L1 |
| Sink | formatting on the caller's thread into a bounded channel of 10 000 lines that drops (and counts) instead of blocking; one writer opens, appends and closes per batch of up to 256 KiB, sharing the file (`FileShare.ReadWrite \| FileShare.Delete`), so the hook thread may log without risk | 10 7.5.4 |
| Hot paths | `LoggerMessage` source-generated methods for per-call lines (`ServiceLog.Call`), so disabled levels cost nothing | 11 L2 |
| Boundary trace | one Debug line per catalog call, `call: {Service}.{Member}`, no arguments; snapshot reads not logged | 11 L2, D-IPC-14 |
| Ownership | each spec owns its log lines verbatim (01 2.13, 02 2.15, 03 7.9, 04 2.23, 07 2.11, 08 2.20, 09 2.24, 10 2.11, 11 7.11); a new line is added to the owning spec's table in the same PR | specs |
| Third-party logs | MSAL bridged at Warning and above with PII off, prefixed `msal: ` | 08 7.18 |

### 8.5 The never-log list

Consolidated from 10 7.5.5, 11 L8, 08 INV-AUTH-6 and INV-AUTH-19, 07 INV-SOP-5, 04 INV-EDIT-18 and 02 Q-CAP-17. Tested by `Logging.BoundaryLogTests.NoSecretsInAnyLine` (11 8.2) and each spec's logging tests.

| Never logged | Examples |
|---|---|
| Credentials | the API key, `Authorization` and `x-api-key` values, OAuth access, refresh and ID tokens, the federation assertion, minted `sk-ant-oat01-` tokens, MSAL cache bytes, DPAPI blobs, secrets files |
| Organization-identifying policy values | federation ids; names of missing or invalid values are allowed |
| Personal data | the signed-in UPN or e-mail, `userName` |
| User content | pixels or base64, OCR text, captions, instructions, notes, project titles, SOP request and response bodies, custom instructions; UIA element names at Info (Debug only, Q-CAP-17 default) |
| File content | `settings.json`, `project.json`, package manifests; a parse failure logs its status |
| Full URLs of refused links | only the origin |

Allowed: file and folder paths, ids, counts, sizes, dimensions, durations, HTTP statuses, request ids, model ids, versions, exception types and BCL messages.

---

## 9. Security architecture

### 9.1 What changed from Electron

Electron contained the risky parts of the app in a sandboxed renderer: it decoded attacker-influenceable image pixels there (`src/main/main.ts:131-140`), and the IPC boundary validated every argument because the renderer was untrusted (11 2.3). Natively there is one process with the user's full rights. The consequences this architecture carries:

1. Validation that guarded external input stays; validation that only guarded the renderer becomes types (INV-IPC-9, 11 7.5). External input is: manifests (synced or hand-edited folders), imported packages, imported images, `settings.json`, the policy registry, the network (Anthropic, GitHub, Entra), environment variables, OCR output.
2. Image decoding moves into `shotAI.exe`. It happens only after a magic-byte check (PNG `89 50 4E 47`, JPEG `FF D8 FF`) and only with the explicit built-in PNG and JPEG decoders, never a content-sniffing factory that could invoke a third-party codec installed on the machine (Q-IPC-16 default, R-ARCH-21).
3. The one remaining web engine (WebView2, PDF only) carries renderer-grade hardening (INV-IPC-20, INV-EXP-20).

### 9.2 Cross-cutting invariants

| # | Invariant | Owning component | Mechanism | IDs | Key tests |
|---|---|---|---|---|---|
| S1 | **Redaction is baked or egress is refused.** Every redaction overlapping the exported region is baked before any overlay, or the flatten throws; a pixelate region contains only tile-derived values; solid is opaque black; the mosaic factor is at least 8; flatten starts from the original. | 04 `Flattener`, `RedactionBaker` | pure Core pixel code, fail-closed pre-checks (D-EDIT-2), box-average tiles (D-EDIT-1) | INV-EDIT-1 to INV-EDIT-5, D-EDIT-26 | `Rendering/FlattenerTests`, `RedactionBakerTests`, `EndToEndRedactionTests` (AC for phase C) |
| S2 | **Render freshness.** A patch that changes annotations or crop without a fresh PNG invalidates the render; update and merge share one applier; render and manifest writes succeed or fail together; the baked list equals the persisted list; a revert invalidates a render that changed since the snapshot. | 04 `StepPatchApplier`, `IStepRenderWriter`; 01 store job; 07 `RevertSopOperation` | one applier inside the store job; receipt rollback | INV-EDIT-7, INV-EDIT-8, INV-EDIT-27, INV-EDIT-32, INV-SOP-29 | `StepPatchApplierTests`, `StoreRenderTests`, `EditorSaveTests` |
| S3 | **The render gate is the only way to pick an egress image.** A shot step with a possible redaction (any unknown annotation type counts) or a crop and no current render is refused, never downgraded to the raw screenshot. | 04 `RenderGate`, `IRenderGate` | static function plus DI wrapper; 07 and 09 have no other image source | INV-EDIT-6, INV-EDIT-31, INV-SOP-3, INV-EXP-1 to INV-EXP-3 | `RenderGateTests`, `RenderGateMayRedactTests` |
| S4 | **Egress is flattened and settled first.** | 05, 06, 07 callers; 04 `IStepFlattener`; 01 `IProjectSettle` | 7.7 | INV-REP-20, INV-HOME-11, INV-SOP-28, INV-EXP-27, INV-EXP-28 | `OptimisticPolicyTests`, export and SOP settle tests |
| S5 | **Packages are safe by default.** A safe package carries no original pixels and no editable state that references them; the package dialog defaults to redacted-only every time. | 09 `PackageWriter`; 05 package dialog | whitelisted writer | INV-EXP-25, INV-REP-20, AC-REP-25 | 09 package tests |
| S6 | **shotAI never appears in its own captures.** Every screen read goes through one shielded funnel; the shield is reference counted, restores the setting and is exception-safe; every own HWND (windows, dialogs, popups, the PDF host) is registered and excluded before first shown; own-UI clicks never produce a step; no `Windows.Graphics.Capture`. | 02 `CaptureShield`, `ShieldedScreenCapture`, `OwnWindowRegistry`; 03 `ShotAIWindow`, `PopupExclusion` | type-level funnel (`GdiMonitorCapture` internal), fail-closed registration order, deadlock rule DL1 | INV-CAP-1 to INV-CAP-7, INV-CAP-25, INV-CAP-29, INV-SHELL-1 to INV-SHELL-3, INV-HOME-43 | `CaptureShieldTests`, `ShieldedCaptureTests`, `CaptureFunnelSourceTests`, `AllWindowsRegisteredTests`, `ShieldDeadlockTests`, `ShotAI.ProtectionProbe` |
| S7 | **Remote visibility matches the switch.** A change applies to open windows at once and on rollback; startup is protected first, then relaxed. Runtime applies run through `Task.Run` in a serialized latest-wins loop and never on the UI thread (DL1). | 11 `RemoteVisibilityApplier`; 03 startup step 10 | settings `Changed` subscription | INV-IPC-13, INV-HOME-41, INV-SHELL-2 | `Settings.RemoteVisibilityApplierTests` (including `ApplyNeverRunsOnUiThread`, `RapidTogglesEndOnLatestValue`), AC-IPC-12 |
| S8 | **Captions never carry field contents.** An element name is used only for the 15 actionable control types with a non-empty name. | 02 `UiaControlTypes` | allowlist | INV-CAP-16, Q-CAP-10 | `UiaControlTypesTests` |
| S9 | **Credentials never reach the UI.** `IAuthService` and its value types expose no token, assertion, claim, expiry or key; the API key is write-only; the UPN appears only in Settings; view models cannot depend on the key store or the MSAL gateway. | 08 `AuthService`; 11 catalog; 06 Settings | internal types, reflection guards | INV-IPC-1, INV-IPC-2, INV-AUTH-19, INV-AUTH-27, INV-AUTH-31, INV-HOME-30, INV-HOME-32, INV-ARCH-3 | `ServiceBoundary.AuthSurfaceTests`, `Composition.ViewModelDependencyTests` |
| S10 | **Secrets at rest are OS-encrypted.** The API key is DPAPI CurrentUser in its own file; the token cache is DPAPI with a cross-process lock and never plaintext (memory-only when encryption cannot be verified). | 08 `ApiKeyStore`, `DpapiSecretProtector`, `MsalCacheRegistration` | DPAPI, MSAL extension | INV-AUTH-25, INV-AUTH-27, Q-AUTH-5 | `ApiKeyStoreTests`, `MsalCachePersistenceTests` |
| S11 | **Egress is pinned.** All Claude traffic, including the federation exchange, goes to `https://api.anthropic.com` whatever the environment says; `ApiKey`, `AuthToken` and `BaseUrl` are set explicitly so the SDK never auto-resolves environment or profile credentials; no environment variable can add a header; a per-client guard refuses any other scheme, host or port. | 08 `IAnthropicClientFactory`, `AnthropicHostGuardHandler`; composition root step 5 | explicit options, guard handler, environment sanitization | INV-SOP-1, INV-SOP-2, INV-AUTH-15, INV-AUTH-35, D-SOP-16, R-ARCH-15 | `AnthropicClientFactoryTests`, `AnthropicHostGuardHandlerTests` |
| S12 | **Manifest, package and setting paths are confined.** Lexical confinement plus hostile-segment rejection before any read; link-refusing confinement (name-surrogate reparse points: symlinks, junctions, mount points) before any write or delete and for every egress read; the known-project gate on every project operation; reparse-safe traversal for listing, archiving and deletion. | 01 `PathConfine`, `IPathProbe`, `ReparseSafeDelete`, the gate | Core logic plus `WindowsPathProbe` (reparse tag name-surrogate bit) | INV-MODEL-13 to INV-MODEL-16, INV-MODEL-27, INV-MODEL-33, INV-MODEL-34, INV-CAP-23, INV-EDIT-9, INV-EDIT-30, INV-IPC-10, INV-REP-18, INV-EXP-32 | `PathConfineTests`, junction tests in Platform.Tests (need junction creation on the runner) |
| S13 | **Untrusted containers are bounded.** Package import checks sizes before and while inflating, whitelists sanitized names, rejects duplicates and `..` segments; archive restore writes only under `shots/` and `export/`. | 09 `PackageReader`; 01 `ArchiveEngine` | streaming, hard bounds | INV-EXP-26, INV-MODEL-16, D-13, D-25, D-EXP-12 | 09 package tests, `ArchiveTests` |
| S14 | **Policy precedence is fixed.** Federation config comes only from `HKLM\SOFTWARE\Policies\shotAI\Federation` (64-bit view, `REG_SZ`) merged over baked values, never HKCU, environment, a file or `settings.json`; validation is all-or-nothing; every status read re-reads policy; the installer never writes the key. | 08 `FederationConfigProvider`, `RegistryPolicySource` | Core validator, Platform reader | INV-AUTH-1, INV-AUTH-5, INV-AUTH-9, INV-IPC-12, INV-INFRA-19, INV-PKG-3 | `FederationConfigValidatorTests`, `StatusRereadsPolicyTests`, `AdmxContractTests` |
| S15 | **Links open only through the allowlist.** `https` to `anthropic.com`, its subdomains or exactly `github.com`, or the exact origin of the administrator's SupportUrl while federation is configured; the only code that hands a URL to the shell is `ShellUrlLauncher`. | 11 `ExternalLinks`; 08 `SupportUrlAllowlist` | one launcher, banned `Process.Start` elsewhere | INV-IPC-3, INV-INFRA-29, INV-AUTH-32, INV-HOME-31 | `Links.ExternalLinkPolicyTests`, `Architecture.SingleUrlLauncherTests` |
| S16 | **The web engine is locked down.** WebView2 renders only the print copy: scripts off, every other navigation and sub-resource refused, no new windows, downloads or permissions, an explicit user data folder, a CSP in the print copy. | 09 `WebView2PdfRenderer` | handlers plus CSP (D-EXP-25) | INV-IPC-20, INV-EXP-20, D-EXP-3 | `PdfRendererTests`, `Architecture.SingleWebViewTests` |
| S17 | **Reveals never execute files.** Folder opens re-check that the path is an existing directory immediately before the shell call. | 11 `ShellReveal`; 09 `RevealExportDirectoryAsync` | directory check on the STA thread | INV-EXP-24, 11 7.3.3 | `Shell.ShellRevealTests` |
| S18 | **The master switch really switches off.** With AI generation off, no Claude entry point looks up a credential, reads files for egress or touches the network. | 07 `ClaudeService` | settings gate first | INV-SOP-4 | 07 tests |
| S19 | **Only one unprompted network request.** The throttled startup update check is the only request without a user action, and it does not run when disabled. | 10 `UpdateService` | lazy auth (INV-AUTH-10) | INV-INFRA-23 | `UpdateServiceTests` |
| S20 | **Logs carry no secrets or content.** | 10 sink, every logger | the never-log list (8.5) | INV-IPC-18, INV-INFRA-21, INV-SOP-5, INV-EDIT-18 | `Logging.BoundaryLogTests`, `OcrLoggingTests` |
| S21 | **The process and its install are hardened.** DLL search restricted before any native load; apphost finds .NET only in global locations; startup hooks off; CET kept on; install directory never written by the app, and admin-only for the per-machine copy (a personal copy's folder is writable only by its owner, INV-PKG-15); `asInvoker`; every first-party PE and MSI signed and timestamped; native dependency built from pinned, hashed sources; locked NuGet restore from nuget.org; least-privilege release workflow with SHA-pinned actions; public releases carry no tenant data. | 12 | build properties, `Program.Main`, CI | INV-PKG-7, INV-PKG-13 to INV-PKG-20, INV-PKG-28, INV-PKG-32, INV-PKG-33 | `PayloadVerifierTests`, install smoke, `verify-payload --public` |

### 9.3 Trust boundaries

```
 untrusted or semi-trusted inputs                         trusted core
 --------------------------------                         ------------
 project folders (synced, hand-edited, macOS)  --> codec (JsJson, tolerant decode) --> PathConfine --> store
 imported packages (.zip)                      --> PackageReader (bounded, whitelisted) --> CreateProjectFromImportAsync
 imported images                               --> ImportLimits + magic bytes --> explicit PNG/JPEG decoder
 settings.json                                 --> SettingsCodec + SettingsCoercer
 HKLM policy, baked resource                   --> FederationConfigValidator (all or nothing)
 environment variables                         --> composition-root sanitization; SDK options set explicitly
 network responses (Anthropic, GitHub, Entra)  --> SDK + SopPlanParser, ReleaseFeed (final-host check), MSAL
 OCR output                                    --> SensitiveTextDetector (clamped, filtered)
 screen pixels (other apps)                    --> ShieldedScreenCapture only

 egress (leaves the machine or the project)
 ------------------------------------------
 Claude request  <-- RenderGate <-- EnsureFlattened <-- settled session
 exports, package <-- RenderGate (collector) <-- EnsureFlattened <-- settled session
 shell (URLs, folders) <-- ExternalLinks allowlist / directory check
```

### 9.4 Environment variables

| Variable | Native behavior | Class |
|---|---|---|
| `ANTHROPIC_API_KEY` | read as the fallback key when no key is stored (parity, INV-AUTH-28); never read by the SDK itself | REQUIRED |
| `ANTHROPIC_BASE_URL`, `ANTHROPIC_AUTH_TOKEN`, `ANTHROPIC_PROFILE` and the SDK credentials file | never applied (explicit `BaseUrl`, `ApiKey`, `AuthToken`) | REQUIRED intent (INV-SOP-1, INV-SOP-2) |
| `ANTHROPIC_CUSTOM_HEADERS` | removed from the process environment at startup step 5 | IMPROVEMENT [SECURITY] (INV-AUTH-35) |
| `HTTPS_PROXY`, `HTTP_PROXY`, `ALL_PROXY`, `NO_PROXY` | default: removed at startup step 5 so the system proxy settings apply, as Chromium's stack did for sign-in (Q-ARCH-3) | IMPROVEMENT [SECURITY], pending decision |
| `DOTNET_STARTUP_HOOKS` | ignored (`StartupHookSupport=false`) | IMPROVEMENT [SECURITY] (INV-PKG-18) |
| `DOTNET_ROOT` and app-local runtimes | ignored by the published apphost (`AppHostDotNetSearch=Global`) | IMPROVEMENT [SECURITY] (INV-PKG-17) |
| `SHOTAI_ENABLE_GPU` | `0` forces WPF software rendering | IMPROVEMENT (diagnostic, 03 D17) |
| `SHOTAI_LOG_LEVEL` | `debug` lowers the Release minimum level | IMPROVEMENT (10 7.5.1) |
| `SHOTAI_SELFTEST`, `SHOTAI_CAPTURE_TEST` | exactly `1` selects the self-test (switches are primary) | REQUIRED (10 7.8) |
| `SHOTAI_NO_SANDBOX` | no meaning | ELECTRON-ONLY (no renderer) |

### 9.5 Security checklist for every PR

1. Does new code read a path from a manifest, package, setting or dialog? It goes through `PathConfine` (write, delete or egress read: `ConfineNoLinks`) and the known-project gate.
2. Does new code read screen pixels? Only through `IScreenCapture`.
3. Does new code create a window, dialog or popup? `ShotAIWindow`, `ShotAIPopup` or the overlay layer; `AllWindowsRegisteredTests` covers it.
4. Does new code send an image anywhere? Only from the render gate, after `EnsureFlattenedAsync` and the settle.
5. Does new code make an HTTP request? Through `ISharedHttp`; an Anthropic request only through `IAnthropicClientFactory`.
6. Does new code log? Nothing on the never-log list; identifiers by name, not value.
7. Does new code open a URL or a folder? `IExternalLinks` or `IShellReveal`, never `Process.Start`. Does it change a file with an `RS0030` allowance (14.9)? The allowance covers the whole file, so check that the change adds no other banned API.
8. Does a new type expose anything from `ShotAI.Core.Auth` to the App? Only `IAuthService` and its value types.
9. Does new code add a package or native binary? Section 3 rules, the notices file and, for native code, 12 7.7 provenance.

---

## 10. Configuration and data locations

### 10.1 Locations

| Data | Path | Shared with Electron | Written by | Class |
|---|---|---|---|---|
| Settings | `%APPDATA%\shotAI\settings.json` (roaming) | yes, same file, same layout | 10 | REQUIRED |
| Corrupt settings backup | `%APPDATA%\shotAI\settings.json.bad` | no | 10 | IMPROVEMENT (Q-INFRA-12) |
| Logs | `%APPDATA%\shotAI\logs\shotai.log`, `shotai.old.log` | yes, same files and format | 10 | REQUIRED |
| API key | `%APPDATA%\shotAI\secrets.dpapi.json` | no (Electron's `secrets.json` is never read, written or deleted) | 08 | IMPROVEMENT (D3 of 08) |
| MSAL token cache | `%LOCALAPPDATA%\LFI\shotAI\entra\msal-cache.bin` plus the extension's lock file | no | 08 (MSAL extension) | IMPROVEMENT; path per R-ARCH-13 |
| WebView2 user data | `%LOCALAPPDATA%\LFI\shotAI\WebView2` | no | 09 | IMPROVEMENT; path per R-ARCH-13 |
| Projects root | `settings.projectsDir`, default `%USERPROFILE%\shotAI Projects` | yes | 01 | REQUIRED |
| A project | `<root>\<uuid>\project.json`, `shots\`, `export\`, `export\.render\`, `archive.zip` | yes (and with macOS through sync) | 01, 02, 04, 09 | REQUIRED (the `contract/` layout) |
| Export temp files | `<project>\export\.render\_print-<guid>.html` and `.pdf`, swept before each PDF | no | 09 | REQUIRED (html), IMPROVEMENT (pdf, D-EXP-2) |
| Self-test temp | `%TEMP%\shotai-selftest-<pid>` and its `.settings.json` sibling | no | 10 | IMPROVEMENT (INV-INFRA-30) |
| Install | `%ProgramFiles%\shotAI\` per-machine or `%LOCALAPPDATA%\Programs\shotAI\` per-user (payload of 12 7.2.3, scope of 12 7.4.5), never written by the app | no | MSI | IMPROVEMENT (INV-PKG-15, INV-PKG-35) |
| Fonts | `<install>\Fonts\Archivo.ttf`, `Fonts\static\*.ttf`, `OFL.txt` beside each | no | MSI | REQUIRED (INV-INFRA-31) |
| Federation policy | `HKLM\SOFTWARE\Policies\shotAI\Federation`, 64-bit view, eight `REG_SZ` values | yes (same key, same ADMX) | administrators only | REQUIRED |
| System theme | read: `HKCU\Software\Microsoft\Windows\CurrentVersion\Themes\Personalize` `AppsUseLightTheme` | n/a | Windows | REQUIRED (06 7.14) |
| Electron-only | `secrets.json`, `entra-cache.bin`, `tessdata\`, Chromium profile entries in `%APPDATA%\shotAI`, the Squirrel root `%LocalAppData%\shotai\` | never touched | Electron | ELECTRON-ONLY (INV-PKG-24) |

### 10.2 `IAppPaths`

10 7.4.4's contract with one addition (R-ARCH-13):

```csharp
public interface IAppPaths
{
    string UserDataDirectory { get; }    // %APPDATA%\shotAI
    string SettingsFile { get; }         // UserDataDirectory\settings.json
    string LogsDirectory { get; }        // UserDataDirectory\logs
    string LocalDataDirectory { get; }   // %LOCALAPPDATA%\LFI\shotAI  (MSAL cache, WebView2 data; never the Squirrel root)
    string DefaultProjectsDir { get; }   // %USERPROFILE%\shotAI Projects
    string TempDirectory { get; }        // Path.GetTempPath()
    string FontsDirectory { get; }       // AppContext.BaseDirectory\Fonts
}
```

No code composes these paths itself; Core receives them through `IAppPaths`, and tests pass a temp-folder implementation. An empty known-folder result is a fatal startup error, logged (10 7.4.4).

### 10.3 Registry

shotAI reads two registry locations (10.1) and writes none. The installer writes only its own Windows Installer and ARP entries and never anything under `HKLM\SOFTWARE\Policies` (INV-PKG-3). A fleet-wide update-check opt-out, if IT asks for one, is a new value under a new `HKLM\SOFTWARE\Policies\shotAI` subkey with an ADMX revision, never under `Federation` (Q-INFRA-4, Q-PKG-15).

### 10.4 Baked build values

Internal builds embed `dotnet/federation.local.json` (gitignored; fallback `src/main/entra/federation.local.json` until cutover) as the manifest resource `ShotAI.Federation.Baked.json`, read at runtime by `EmbeddedBakedFederationSource` and filtered by `BakedFederationParser` (08 7.4). Public builds pass `-p:ShotAIFederationFile=` (empty) and are bring-your-own-key; `verify-payload --public` fails a public payload that carries the resource (INV-PKG-14). The values are not secrets, but they identify the organization, so they never appear in this repository, a public release, a log or a test (INV-AUTH-6).

### 10.5 Compatibility with the Electron build's files

| File | Rule during the pilot and after rollback | IDs |
|---|---|---|
| `project.json` | the shared contract; every native write round-trips through `contract/conformance`; Electron 1.3.x must open every file the native build writes | 01 7.11, INV-PKG-25 |
| `settings.json` | one file, no migration; native writes are atomic, keep unknown keys and their positions, never change a key's type or meaning; native-only keys are additive | INV-INFRA-14, INV-PKG-25, 12 7.10.2 |
| Logs | same path, same line format; the sink shares the file | Q-INFRA-7 |
| Secrets and token cache | separate files per build; users sign in or re-enter the key once after cutover | 08 7.16, Q-AUTH-2 |
| `contract/brand.json` | read in place by all three generators; the C# table carries the same stamp | INV-INFRA-1 |
| Concurrent use | native refuses to start while Electron runs (same session); rollback targets must be 1.3.x | INV-PKG-26, INV-PKG-25 |

---

## 11. Performance budgets

No Electron baseline was measured for most of these, so the budgets marked **proposed** are targets set by this document (IMPROVEMENT goals, justified by the feasibility doc's "Will it feel faster?"); the others come from a spec's acceptance criterion or a hard platform limit. "Reference x64" and "ARM64 dev VM" are the two machines of Q-ARCH-1.

| ID | Metric | Budget | Source | How it is measured | Gate |
|---|---|---|---|---|---|
| PB-1 | Time inside `LowLevelMouseProc` per event | zero managed allocations; p99 under 50 microseconds; never near `LowLevelHooksTimeout` (at most 1 s, silent removal) | INV-CAP-17, feasibility "Hook timing"; p99 proposed | `MouseHookTests.CallbackIsAllocationFree` (02 8.4); a Platform perf test storing `QueryPerformanceCounter` stamps at entry and exit into a preallocated array over 1000 `SendInput` clicks; in the field, zero watchdog reinstall lines during the pilot | allocation test in Windows CI; latency manual |
| PB-2 | Mousedown to dispatcher pickup | p95 under 5 ms | proposed | hook stamp in the ring versus the dispatcher's dequeue stamp, Debug log | manual |
| PB-3 | Click-time synchronous menu grab (one 3840 x 2160 monitor) | under 50 ms on reference x64 | proposed; the menu must still be painted (02 Risk R3) | `Stopwatch` around `IScreenCapture.Grab` in a Debug timing line; AC-CAP-7 | manual |
| PB-4 | One capture job (grab, downscale at 0.85, PNG encode, write, store append) at 3840 x 2160 | p95 under 400 ms reference x64, under 800 ms ARM64; never delays the next click's decision (FIFO worker, separate dispatcher) | proposed | a Debug timing line per job (added to 02's log table in the capture PR); a 20-clicks-in-5-seconds script (Q-IPC-20) must record 20 steps | manual per phase B exit |
| PB-5 | `StepLanded` raised to pill flash painted | under 100 ms with 20 queued events | proposed; Q-IPC-20 coalescing | App.Tests perf case with a fake engine raising bursts from a pool thread | manual |
| PB-6 | Caption edit to render on a 100-step project | under 16 ms of UI-thread time (commit, `Apply` clone, `Sync`, one layout pass) | AC-REP-12 | `Stopwatch` around `Sync` plus one layout pass | manual on reference x64 |
| PB-7 | Report initial layout, 300 steps, excluding image decode | under 100 ms, else switch to `VirtualizingStackPanel` | Q-REP-11 | App.Tests perf case | manual in Phase A |
| PB-8 | Home with 500 projects | frames under 16 ms while scrolling; a refresh with no changes rebuilds no rows | AC-HOME-35, Q-HOME-14 | WPF frame timing (`CompositionTarget.Rendering` deltas) in an App.Tests perf case | manual |
| PB-9 | Cold start, process start to the main window's first `ContentRendered` | under 1.5 s reference x64, under 2.5 s ARM64; warm start under 0.8 s | proposed; nothing in auth or network runs at launch (INV-AUTH-10) | an Info line `startup: main window rendered in <n> ms` measured from `Process.StartTime` (added to 03's log table), read from the log over ten launches; ReadyToRun stays only if it saves at least 100 ms (Q-PKG-10) | manual per release candidate |
| PB-10 | Editor save of a 7680 x 2160 screenshot with ten pixelate redactions | under 2 s reference x64, under 4 s ARM64 | AC-EDIT-28 | `FlattenerPerfTests` (not a CI gate) | manual phase C exit |
| PB-11 | Editor open (read, decode, first render) of a 3840 x 2160 screenshot | under 500 ms reference x64 | proposed | App.Tests perf case | manual |
| PB-12 | Element query | capped at 600 ms, never delays a capture beyond the cap | INV-CAP-15 (a constant) | `UiaElementLocatorTests.HungProviderTimesOut` | Windows CI |
| PB-13 | Exit | teardown hook join at most 2 s; queued writes drained within 5 s total | 02 7.13, INV-IPC-21 | `Shutdown.ExitFlushTests` | Windows CI |
| PB-14 | One atomic `project.json` write of a 100-step manifest (with `FlushFileBuffers`, Defender on) | p95 under 50 ms; invisible to the user because edits are optimistic, but it bounds how fast a capture burst drains | proposed; Q-MODEL-5 | Platform perf case on NTFS | manual in Phase A |
| PB-15 | Styled HTML export of the reference SOP (13 images) | faster than Electron's 11.4 s; file size within 20 percent of 168 KB (the Freshservice paste budget) | D-EXP-5, Q-EXP-2 | stopwatch on both builds, same machine | manual phase D exit |
| PB-16 | Memory | no large-object-heap allocation per menu poll tick (pooled frames); report images decoded at display size; a refused 70 MB import grows the working set by less than its size | 02 D24, INV-REP-32, AC-IPC-10 | `dotnet-counters` during the scripted sessions | manual |
| PB-17 | A log call | never blocks; the cost is formatting only | 10 7.5.4 | `RotatingFileSinkTests` | Linux CI |

Measurement rules: perf tests carry the xunit trait `Category=Perf` and are excluded from CI runs (the runners are too noisy to gate on); results for each phase exit are recorded in the phase's tracking issue with the machine, build and commit. Timing lines are Debug level except the startup line (Info), and none logs user content.

---

## 12. Testing strategy

### 12.1 Test projects

| Project | Runs on | Holds | Host requirements |
|---|---|---|---|
| `ShotAI.Core.Tests` | Linux (CI gate) and Windows | every Core rule: codec, conformance, store with the managed probe, geometry, capture engine over fakes, shield, editor document, flatten and bake, gate, detector, report operations and session, Home pipeline, SOP request, schema, parser, wire mapping and error mapping through the real SDK with a fake inner handler, auth logic over the MSAL seam, export builders and CSS goldens, package reader, settings codec, log format, update check, links, architecture and source guards, the brand generator currency check, `ShotAI.Release` logic | none |
| `ShotAI.Platform.Tests` | Windows | hook and hotkey, GDI capture and display affinity, window info, UIA, WIC codec and orientation, OCR against a synthetic fixture, reparse and junction behavior, rename retry classification, DPAPI, registry policy reading, MSAL cache persistence, WebView2 PDF hardening, libavif, single-instance lock | junction creation allowed; an OCR recognizer installed; WebView2 runtime; an interactive desktop for capture and affinity cases |
| `ShotAI.App.Tests` | Windows | view models against faked catalog interfaces, window registration and styles, the pill's non-activation, overlays, menu, startup and exit order, composition container, dispatcher contract, rasterizer and painter parity, report and Home UI behavior, end-to-end redaction | an STA harness (a small in-repo helper that runs each test on a dedicated STA thread with a `Dispatcher` and pumps until the test's task completes; Q-HOME-15) |
| Install smoke | Windows x64 and ARM64 runners | MSI install, `--selftest`, `settings.json` untouched, uninstall, upgrade and downgrade refusal | the .NET Desktop Runtime installed from the official, hash-checked installer (12 7.11.4) |

### 12.2 Linux first

A rule that can be tested on Linux is tested on Linux. When a Windows behavior has a pure part (a formula, a state machine, an ordering, a mapping table), the pure part is a Core type with Linux tests and the Windows part is a thin adapter with a few Windows tests. The seams of 2.3 exist for this; each has a fake in `ShotAI.Core.Tests` (fake probe and classifier, fake trigger source, fake monitor capture, recording rasterizer, fake OCR engine, fake MSAL gateway, fake policy source, fake secret protector, fake PDF renderer, fake image codec), plus `ManualUiDispatcher`, `ThreadUiDispatcher` (11 7.3.1), `FakeTimeProvider` and a capturing `ILoggerProvider`.

### 12.3 Conformance

The shared `contract/conformance` suite runs natively through the existing harness (`dotnet/tests/ShotAI.Core.Tests/Conformance/`), linked from the repository root and never copied. 01 7.11 replaces the `Round_trips_through_the_codec` skip with the decode, stringify, parse and expect-path algorithm of `src/main/conformance.test.ts:70-139`, including the `ExpectPath.Canonical` fix for `-0`. Open cases report without failing; agreed cases fail. The report is the test's output, which `dotnet test` prints for a passing test only with `--output Detailed`, so the `dotnet.yml` Linux job re-runs the conformance class in a step of its own to show it (01 7.11, decided in WP-A3). A contract change lands in both repos (`dotnet/README.md`).

### 12.4 Golden files

| Golden | Produced by | Compared how | Owner |
|---|---|---|---|
| `project.json` bytes for representative manifests | `src/main/codec-golden.test.ts`, an Electron-side vitest that writes `Golden/codec/expected/` only when `SHOTAI_CODEC_GOLDENS=1` is set, from the inputs in `Golden/codec/inputs/`, the macOS fixture and the conformance cases (`Golden/codec/README.md`) | decode then encode equals the Electron bytes (`Codec/ElectronGoldenTests`) | Q-MODEL-18, AC-MODEL-3 |
| macOS fixture project | copied byte-identical from `macOS:Fixtures/b7e2c4d1-9f3a-4e8b-a2c5-6d1f8e9a0b3c/` into `Golden/macos-fixture/`, with a README naming commit `f445bca` and each file's SHA-256 | its codec output equals Electron's (`macos-fixture` golden) and keeps every `note` and annotation (AC-MODEL-6) | Q-MODEL-14 |
| Styled and plain export CSS | Node 22 run of the Electron generators; SHA-256 per brand and scale | checksum table (09 3.4) | 09 |
| HTML and Markdown exports | an Electron run mode `SHOTAI_EXPORT_GOLDENS=<fixtures root>` | text equality with replayed or normalized image payloads plus per-image media type and dimensions (image bytes can never match) | Q-EXP-5, Q-EXP-14 |
| Word and PowerPoint | the same Electron run | a layout dump (structure, text, geometry) compared against Electron's files, plus manual opens in Office | 09 8.2, AC-EXP-13, AC-EXP-14 |
| System prompt, request fixture, SOP schema | the 07 strings and measured schema | byte equality (UTF-8, no BOM, no trailing newline) | 07 7.3, 7.5 |
| Sensitive-text detector corpus | an Electron-side env-gated vitest writing JSON with synthetic data only | rect equality | Q-EDIT-15 |
| Brand table | `ShotAI.GenBrand --check` and `BrandContractTests.GeneratedFileIsCurrent` | stamp and bytes | 10 7.3 |
| Channel inventory | `channel-map.json` against `src/shared/ipc.ts` while it exists | set equality | 11 8.2 |

Rules: synthetic data only (this repository is public); LF line ends and no BOM (a `.gitattributes` entry per golden folder); each golden folder has a README naming its generator and the commit it was produced from; a golden is regenerated only by its generator, never edited by hand.

### 12.5 Source and architecture guards

These read source or reflect over assemblies, so a rule cannot silently erode:

| Guard | Asserts | Owner |
|---|---|---|
| `Architecture.CoreReferencesTests` | Core references no Windows assembly (INV-ARCH-1) | 11 |
| `Composition.ContainerTests`, `ViewModelDependencyTests`, `CommandConventionsTests`, `SubscriberDisposalTests` | 4.1 and 5.1 rules, INV-ARCH-3 (the allowed set includes the C6 factories, `ICaptureTargetSelection`, `IUiDispatcher` and `EditorViewModel`'s named Core dependencies) | 11 |
| `CaptureFunnelSourceTests` | only the funnel reads pixels; no `Windows.Graphics.Capture` anywhere under `dotnet/src` and `dotnet/tools` (the probe allowlisted) | 02 |
| `AllWindowsRegisteredTests`, `NoWin32MessageBoxInSource` | every shown HWND registered; no `MessageBox` except the allowlisted legacy guard | 03 |
| `Threading.NoSyncWaitTests`, `ShellEventOrderingTests.NoSynchronousDispatcherInvoke` | belt and braces for T6 and T9 | 11, 03 |
| `Architecture.SingleWebViewTests`, `SingleUrlLauncherTests` | INV-ARCH-5, S15 | 11 |
| `SourceGuards/XamlResourceKeyGuardTests`, `XamlChromeGuardTests` | DynamicResource theme keys only; no colour or radius literals | 06 |
| `JsMathTests.NoMathRoundInCapture`, `JsMathTests.NoJsMathCopyInCapture` and the Core-wide ban of `Math.Round` (14.5, 14.9) | JS rounding only, through the one `ShotAI.Core.Json.JsMath` (R-ARCH-1) | 02, this document |
| `ServiceBoundary.ChannelInventoryTests`, `ChannelMapResolutionTests` | 87 channels, each resolving to one member | 11 |
| `PayloadVerifierTests` | payload rules including no baked federation in a public build | 12 |

### 12.6 UI smoke tests

`ShotAI.App.Tests` creates real windows on the STA harness: the pill shows, hides and shows without activating (`CapturePillWindowTests.ShowDoesNotActivate`, Q-SHELL-21), one overlay per monitor with exact bounds, Esc and right-button cancel, the startup order excludes windows before the setting applies, the exit order matches 4.5, the editor's pointer mapping at several zooms, painter parity between preview and bake. These run on the Windows CI job; cases that need a specific monitor layout (mixed DPI) or a live screen share are manual.

### 12.7 Manual test scripts

Manual acceptance criteria stay in their specs; each phase exit runs its set on both architectures and records the result in the phase issue:

| Phase (feasibility) | Manual set |
|---|---|
| A: model and viewer | open projects written by both existing apps; AC-MODEL manual items; PB-6, PB-7, PB-14 |
| B: capture | AC-CAP-6 (same 10-click flow recorded in both Windows builds matches), AC-CAP-7, AC-CAP-9, AC-CAP-13 (protection probe), AC-CAP-23 (hook survival), AC-CAP-27, AC-CAP-33; the 100% plus 150% mixed-DPI rig (AC-SHELL-15, AC-SHELL-16); PB-1 to PB-5 |
| C: editor and redaction | the end-to-end redaction proof; AC-EDIT visual checks (Q-EDIT-3, Q-EDIT-4); OCR on the fixture (Q-EDIT-19); PB-10, PB-11 |
| D: SOP and exports | a live tenant run of the connection test and WAM claims refresh (AC-AUTH-27, the `ShotAI.WifProbe`); exports opened in Word and PowerPoint; the PDF from a hidden controller (Q-EXP-3); PB-15 |
| E: ship | the parity walk (AC-IPC-22), a Teams screen share with remote visibility (AC-IPC-12), install, upgrade and rollback drills (12 section 9), pilot exit criteria (AC-PKG-29), PB-9 |

### 12.8 How the Electron vitest suite maps across

The Electron suite (`vitest run`, `environment: 'node'`) keeps running in `ci.yml` until the cutover cleanup PR deletes it with the Electron tree (12 7.11.1). Each file is owned by one spec, whose section 8 maps it case by case:

| Electron test file | Owner (section) | Native disposition |
|---|---|---|
| `src/shared/project.test.ts` | 01 8.1 | ports to Core (Linux) |
| `src/main/conformance.test.ts` | 01 8.1, 7.11 | ports as the shared conformance harness |
| `src/main/normalize-steps.test.ts`, `manifest-extras.test.ts`, `mutate-serialize.test.ts`, `unknown-callout.test.ts`, `section-callout.test.ts`, `project-theme-key.test.ts` | 01 8.1 | port to Core (Linux) |
| `src/main/path-confine.test.ts`, `path-confine-symlink.test.ts` | 01 8.1 | port to Core (Linux, managed probe) plus junction cases in Platform.Tests |
| `src/main/archive.test.ts` | 01 8.1 | ports to Core (Linux) |
| `src/main/capture-geometry.test.ts`, `capture-shield.test.ts`, `click-caption.test.ts` | 02 8.1 to 8.3 | port to Core (Linux) |
| `src/main/gpu-policy.test.ts` | 03 8.1 | partly ports (`RuntimeDiagnosticsTests`, `RenderModePolicyTests`); the GPU policy itself is ELECTRON-ONLY |
| `src/main/arp-icon.test.ts` | 03 8.2 | ELECTRON-ONLY; the intent moves to the installer's `ARPPRODUCTICON` check (12) |
| `src/renderer/editor/editor-geometry.test.ts`, `src/shared/redact-detect.test.ts`, `src/main/render-gate.test.ts`, `src/main/step-render.test.ts` | 04 8.1 | port to Core (Linux) |
| `src/renderer/project/report-geometry.test.ts`, `src/shared/doc-scale.test.ts`, `src/shared/report-matches-export.test.ts` | 05 8.1 | port to Core (Linux) |
| `src/renderer/project/command-bar.test.ts` | 05 8.1 | ELECTRON-ONLY as written (it scans TSX and CSS); each intent gets a native test (`PopoverPlacement` in Core, the command bar in App.Tests) |
| `src/renderer/project/date-groups.test.ts` | 06 8.1 | ports to Core (Linux) |
| `src/renderer/project/app-chrome-tokens.test.ts` | 06 8.2 | ports as XAML source guards (Linux) plus the key-set comparison (App.Tests) |
| `src/renderer/project/theme-wiring.test.ts` | 06 8.3, 11 8.1 | the push, rebuild and coercion cases are ELECTRON-ONLY; the pass-through, disabled-when-closed, every-brand and tick rules port (`BrandMenuModelTests`, `BrandChoicePassThroughTests`) |
| `src/main/sop-input.test.ts`, `sop-prompt.test.ts`, `sop-landing.test.ts`, `claude-error-messages.test.ts`, `intro-authored.test.ts` | 07 8.1 to 8.5 | port to `ShotAI.Core.Tests.Sop` (Linux) |
| `src/main/entra/admx-contract.test.ts`, `auth-core.test.ts`, `cache-plugin.test.ts`, `config-sources.test.ts`, `config-validate.test.ts`, `federation-cache-wiring.test.ts`, `federation-local.test.ts`, `federation.test.ts`, `net-module.test.ts` | 08 8.1 to 8.9 | logic ports to Core (Linux); `cache-plugin` splits into Windows `MsalCachePersistenceTests`, a Core decision test and ELECTRON-ONLY cases; `net-module` is ELECTRON-ONLY (all 11), its intents covered by `SharedHttpTests`; the wiring cases become the behavior test `StatusRereadsPolicyTests`; the local-file cases skip unless a local federation file exists |
| `src/main/export-css.test.ts`, `export-geometry.test.ts`, `export-palette-source.test.ts`, `src/shared/export-theme.test.ts` | 09 8.1 | port to Core (Linux); the source-scan cases become builder-level assertions |
| `src/shared/brand-narrowing.test.ts` | 10 8.2 (also 09) | ports to Core (Linux) |
| `src/shared/theme-palette.test.ts` | 10 8.1 | ports to Core (Linux) over the generated table |
| `src/main/settings.test.ts`, `update-check.test.ts` | 10 8.3, 8.4 | port to Core (Linux) |

No Electron test exists for packaging (12 8.1) or for the IPC boundary itself (11 8.1); their native tests are new.

### 12.9 Test conventions

- Test names are the names the specs give (for example `SolidIsExactlyOpaqueBlack`, `ThrowingHandlerIsLoggedAndOthersStillRun`); the existing conformance harness keeps its names.
- No test sleeps: time comes from `FakeTimeProvider` or `ICaptureClock`; concurrency tests use `ManualUiDispatcher` or `ThreadUiDispatcher`.
- No test uses the network; live checks are manual (the update self-test, the WIF probe).
- A skip always says why and what unskips it (`Assert.Skip("...")`), as the scaffold's conformance skip does.
- Test data is synthetic: placeholder ids, made-up SIDs, `example.test` and `example.org` hosts, no real tenant or organization value, no screenshot of real data.
- Windows-only tests that need a capability the runner may lack (junction creation, OCR recognizer, an interactive desktop) check for it and skip with that reason, so a missing capability is visible rather than a false failure.

### 12.10 Architecture acceptance criteria

**AC-ARCH-1.** `ShotAI.Core.Tests` builds and passes on the Linux job, and `Architecture.CoreReferencesTests` passes (AC-IPC-16).

**AC-ARCH-2.** `Composition.ContainerTests` builds the full container with `ValidateOnBuild` and resolves every catalog interface of 4.4; `ViewModelDependencyTests` finds no forbidden constructor parameter.

**AC-ARCH-3.** Each banned-symbol list and each analyzer rule of 14.9 is proven once, in the PR that adds it, by a deliberate violation that fails the build.

**AC-ARCH-4.** `Store/ProjectSessionTests` (Core, Linux) covers S1 to S10 of 7.4: clone-throw queues nothing; `Unchanged` queues nothing; a durable result keeps later optimistic edits (`DurableResultKeepsLaterOptimisticEdits`); a persisted echo equal to `Current` raises `Persisted` and a differing one `External`; a failure re-applies pending operations and carries the failed operation; `WhenIdleAsync` and `IProjectSettle` complete only after earlier work; a session disposed while writes are pending stays registered until they finish, so `WhenSettledAsync` for its path waits for them (S9, R-ARCH-27).

**AC-ARCH-5.** `Shutdown.ExitFlushTests` passes, and a manual rename followed within 100 ms by File, Exit shows the new title after relaunch (AC-IPC-18, AC-MODEL-31).

**AC-ARCH-6.** An environment test starts the composition root with `ANTHROPIC_CUSTOM_HEADERS` and (if Q-ARCH-3 is adopted) `HTTPS_PROXY` set: both are gone from the process environment before the first `HttpClient` exists, and the log names them without values.

**AC-ARCH-7.** Manual: with the native build signed in and a PDF exported, uninstalling the Electron build through its Squirrel uninstaller leaves `%LOCALAPPDATA%\LFI\shotAI\` intact, and the native build stays signed in (R-ARCH-13, EDGE-PKG-22).

**AC-ARCH-8.** Every budget of section 11 has a recorded measurement on both reference machines at the phase exit that owns it.

---

## 13. Build, CI and release

Spec 12 is normative; this is its shape.

### 13.1 Build

| Item | Rule | IDs |
|---|---|---|
| Version | one source, `<Version>` in `Directory.Build.props` (today `2.0.0-alpha.0`); tag `v<Version>`, strictly increasing; native major at least 2 | INV-PKG-10 |
| MSI version | `X.Y.(Z*1000 + stage base + N)`, stage bases alpha 0, beta 300, rc 600, final 999, so 2.0.0 upgrades over its prereleases; `FileVersion` `X.Y.B.0` | 12 7.3 |
| Publish | framework-dependent on the .NET 10 Desktop Runtime, default `Minor` roll-forward, per RID (`win-x64`, `win-arm64`), ReadyToRun for first-party assemblies only, no single-file, no trimming, no PDBs in the payload | INV-PKG-5, Q-PKG-10, 12 7.2 |
| Hardening in the build | `AppHostDotNetSearch=Global`, `StartupHookSupport=false`, CET left on, `asInvoker`, `SetDefaultDllDirectories` first in `Main` | INV-PKG-16 to INV-PKG-18, INV-PKG-33 |
| Payload check | `ShotAI.Release verify-payload`: required and forbidden files, PE machine per architecture, runtimeconfig checks, no baked federation in a public build | 12 7.2.4 |
| Native dependency | `shotai_avif.dll` built in CI from pinned libavif and libaom sources with hashes and attestation, `/CETCOMPAT` on x64 | INV-PKG-19, 12 7.7 |
| Supply chain | locked restore, nuget.org only, source mapping, signature validation, audit, Dependabot | INV-PKG-20, 12 7.8 |

### 13.2 CI

| Workflow | Jobs | Notes |
|---|---|---|
| `ci.yml` (Electron) | unchanged until cutover | deleted in the cleanup PR |
| `dotnet.yml` | Linux: brand table check, notices check, build the whole solution, Core tests; `native-avif` (x64, arm64, skipped until `pins.json` exists); Windows x64 and ARM64: build and every test project; `package`: publish, `verify-payload --public`, MSI, install smoke | path-filtered (`dotnet/**`, `contract/**`, `assets/**`, fonts, `.gitattributes`); not required checks while path filters exist (EDGE-PKG-35); PR CI never signs |
| `release.yml` | `check` (tag grammar, props version, newer than existing tags), `test`, `native-avif` with attestation, `build`, optional `build-internal` (baked, workflow artifact only), `sign` (OIDC, two passes: payload then MSI, timestamped, protected environment), `smoke-arm64`, `draft-release`, `verify-published` | `permissions: {}` at top, per-job minimum, actions pinned by SHA (INV-PKG-28); a human publishes the draft |

### 13.3 Installer and deployment

One dual-purpose MSI per architecture (WiX MSBuild SDK): a double-click installs it per-user with no administrator rights, and Intune deploys it per-machine (`ALLUSERS=1`, System) as a Win32 app with the .NET 10 Desktop Runtime as a dependency and requirement rules routing x64 and ARM64 devices (INV-PKG-1, INV-PKG-35, INV-PKG-36, 12 7.4.5, 7.5). A per-user install needs the runtime already present and refuses beside a per-machine copy; a personal copy found beside a per-machine one hands off to it at launch (INV-PKG-37, INV-PKG-38). MSIX was rejected because it virtualizes new `AppData` files, which would split `settings.json` and the logs from the Electron build during the pilot (12 7.1, EDGE-PKG-21). The installer never launches the app, never writes the policy key, adds a Start menu shortcut (`shotAI Preview` for prereleases), no desktop shortcut by default, and keeps all per-user data on uninstall (INV-PKG-3, INV-PKG-4, 12 7.4).

### 13.4 Release, pilot, cutover, rollback

| Stage | Native | Electron | IDs |
|---|---|---|---|
| S1 pilot | `2.0.0-alpha.N`, `-beta.N` to the pilot group; GitHub prerelease, not latest, so Electron's update check never offers it | installed for everyone | INV-PKG-9 |
| S2 release candidate | `2.0.0-rc.N` to a wider group | installed | 12 7.13.1 |
| S3 GA | `2.0.0`, latest | removed per user by a user-context Intune Uninstall assignment with a wrapper script | 12 7.13.3, Q-PKG-7 |
| S4 cleanup | main is native only; `ci.yml`, npm files and the Electron tree deleted in one PR; `dotnet.yml` path filters removed and jobs made required | kept as a 1.3.x rollback asset | INV-PKG-27 |
| S5 | `LegacyInstanceGuard` removed 90 days after GA | Intune app deleted | Q-PKG-22 |

Rollback during the pilot is per user (unassign native; Electron is still there). Rollback after GA reassigns the retained Electron 1.3.x app; `project.json` and `settings.json` stay readable by 1.3.x by construction (INV-PKG-25). Any Electron release after 2.0.0 is published with `--latest=false` (INV-PKG-9). The 2.0.0 release notes open with the exact text of 12 7.12.3.

### 13.5 Tools in the repository

| Tool | Purpose | Runs in CI |
|---|---|---|
| `ShotAI.GenBrand` | generates `BrandPalette.Generated.cs` from `contract/brand.json`; `--check` | yes (Linux) |
| `ShotAI.Release` | `version`, `check-tag`, `verify-payload`, `notices`, `federation-check` | yes |
| `ShotAI.ProtectionProbe` | measures display-affinity exclusion for normal and layered windows | no (interactive) |
| `ShotAI.WifProbe` | drives the real auth path against a live tenant, legs 0 to 4 | no (live tenant) |

---

## 14. Coding conventions

### 14.1 Comments

This repository's comments explain why, cite where a rule came from, and say whether a fact was measured (for example `src/main/remote-visibility.ts:1-19`, `src/main/project-store.ts:76-83`, the scaffold's `CaptureExclusion.cs` remarks). The native code keeps that style:

| Rule | Example |
|---|---|
| A comment says why, not what the next line does. | "Fail-closed ordering: every window starts protected and is relaxed only after the setting is known." |
| Cite the origin: an issue (`#77`, `#95`), a commit (`772e381`), the Electron line being ported (`src/main/project-store.ts:647`), a macOS precedent (`macOS:shotAI/Capture/CaptureCoordinator.swift:228-234`) or a spec ID (`INV-CAP-3`, `D-EDIT-2`, `Q-CAP-15`). | `// #86: one bad step entry must not hide the whole project (01 EDGE-MODEL-33).` |
| Say whether a platform fact is measured or assumed; an unverified one names its open question. | `// UNVERIFIED (Q-SHELL-21): ShowActivated may apply to the first Show only; the test pins it.` |
| Public and protected members of Core and Platform carry XML doc comments: a one-line `<summary>`, the why in `<remarks>` (the scaffold's pattern). | `dotnet/src/ShotAI.Platform/CaptureExclusion.cs` |
| A deliberate divergence from Electron names its ID at the point of divergence. | `// IMPROVEMENT D-EDIT-1: box average, not bilinear sampling.` |
| No commented-out code. No `TODO` without an issue number. | |
| No em or en dash characters in comments or strings; write `\u2014` in a string and a comma, colon or parentheses in prose. | `ShellStrings.PillTitle = "shotAI \u2014 Capture"` |

### 14.2 Naming

| Item | Convention |
|---|---|
| Types, methods, properties, constants | PascalCase; interfaces `I`-prefixed; exceptions end in `Exception`; event argument records end in `EventArgs` |
| Fields | `_camelCase` for instance fields, `s_camelCase` for static fields (02 7.4 uses `s_proc`, `s_lastHookTick`) |
| Async methods | end in `Async` (`VSTHRD200` as error), except the spec-fixed `IProjectSession.Apply` and `ApplyDurable` (Q-IPC-21) |
| Namespaces and folders | one-to-one (2.4) |
| Wire strings (`"html-plain"`, `"professional"`, `"system"`) | explicit mapping functions (`ExportFormats.Wire`, `SopCatalog.ToWire`, `ThemePrefWire`), never `Enum.ToString()` |
| Tests | `<TypeUnderTest>Tests` classes; method names from the owning spec |
| Log categories | the type's namespace (8.4); an explicit category string only for the banner and `svc` |

### 14.3 Types and nullability

- `Nullable` enabled and warnings are errors; the null-forgiving `!` appears only with a comment that says why the value cannot be null.
- Classes are `sealed` unless designed for inheritance; value data are records; public APIs take and return `IReadOnlyList<T>`, `IReadOnlyCollection<T>` or `IReadOnlyDictionary<TKey, TValue>`.
- Snapshots the UI reads from other threads are immutable (`AppSettings`, `CaptureState`) and published with a volatile write.
- The only mutable model graph is the manifest (`ProjectManifest`, `ProjectStep` over raw `JsonObject`), and only the store job and the session's clone ever mutate it (7.4).
- `JsonNode` trees that may hold a non-finite double never go to a System.Text.Json serializer (01 7.2.1).

### 14.4 Async and threading

- Core and Platform: `ConfigureAwait(false)` on every await (`CA2007`), except the three UI-affine types of T3.
- The `CancellationToken` is the last parameter and is forwarded (`CA2016`).
- No `async void` except WPF event handlers (T12); fire-and-forget only as `_ = SomethingAsync()` with a continuation that logs a failure (`VSTHRD110`).
- No sync-over-async anywhere except `ShutdownFlush.cs` (T9).
- `Task.Run` in a service only for CPU-bound work, never to wrap IO that has an async API.
- Locks are short, never held across IO, a Win32 call that could block on another thread, an event raise or an await (02 7.3, 02 7.8 is the one reasoned exception).

### 14.5 Porting ECMAScript semantics

Behavior parity often hangs on a JavaScript detail. These are mandatory in ported logic:

| JavaScript | C# | Why |
|---|---|---|
| `Math.round` | `JsMath.Round` (floor, then compare the fraction with 0.5) | .NET `Math.Round` rounds half to even, so `DocScale.Clamp(0.825)` would give 0.8 where JS gives 0.85 (01 2.5, EDGE-REP-7), and `Math.Floor(x + 0.5)` is wrong for 0.49999999999999994 (INV-CAP-18, 01 7.2.3) |
| `String.prototype.trim` | `JsString.Trim` | `string.Trim()` trims U+0085 and not U+FEFF (01 7.2.3) |
| `$` and `\d` in regexes | `\z` and `[0-9]` with `RegexOptions.CultureInvariant`; `RegexOptions.ECMAScript` for the detector patterns | .NET `$` matches before a final newline and `\d` is Unicode (EDGE-MODEL-55, INV-EDIT-20) |
| `JSON.parse`, `JSON.stringify` | `JsJson.Parse`, `JsJson.Stringify` | 01 7.2 |
| `Number.prototype.toString` | `JsNumber.ToJsString` | 01 7.2.2 |
| `new Date().toISOString()`, `Date.parse` | `IsoTime.ToIsoString`, `IsoTime.TryParseJsDate` | three fractional digits; the ISO subset only (01 D-17) |
| `Array.prototype.sort` (stable) | LINQ `OrderBy` | `List<T>.Sort` is not stable (06 7.2) |
| `localeCompare` with `sensitivity: 'base'` | `CompareInfo.Compare` with `IgnoreCase \| IgnoreNonSpace \| IgnoreKanaType \| IgnoreWidth` on the current culture; never `InvariantGlobalization` | ICU on both (06 7.2) |
| `crypto.randomUUID()` | `Guid.NewGuid().ToString("D")` | lowercase v4; never `CreateVersion7` (01 7.2.3) |
| string indices | UTF-16 code units | both platforms (INV-EDIT-20) |
| truthiness, `ToNumber` | `JsValue.Truthy`, `JsValue.ToNumber` | 04 7.1 |

Culture: machine text (JSON, file names, log lines, wire values) uses `CultureInfo.InvariantCulture`; user-facing dates and numbers use `CultureInfo.CurrentCulture` (06 Q-HOME-4, 07 7.10). String comparison defaults to `StringComparison.Ordinal`; Windows path comparison uses `Path.GetFullPath` then `OrdinalIgnoreCase` where a spec says so (02 D9).

### 14.6 Strings

- Every user-visible string is a constant (or a formatter) in its subsystem's strings class in Core, copied verbatim from Electron or from the spec's IMPROVEMENT table, and pinned by a test against the spec (`ShellStringsTests`, `ReportStringsTests`, 07's golden files).
- Non-ASCII characters in source strings are written as `\uXXXX` escapes (`\u2014`, `\u2026`, `\u2192`), so a reader and a grep see exactly which code point is meant (02 notation, 05 notation).
- User text is never concatenated into a log line (8.5).

### 14.7 Interop

- Every Win32 and COM API comes from CsWin32: add the name once to `src/ShotAI.Platform/NativeMethods.txt` (no wildcards; whether 0.3.335 accepts them is UNVERIFIED, 03 7.10). Never a hand-written `DllImport`.
- The single exception is `shotai_avif.dll`, bound with source-generated `LibraryImport` in `ShotAI.Platform.Export.NativeAvif` (Q-EXP-1).
- `AllowUnsafeBlocks` only in Platform. Handles are `SafeHandle`s or released in `finally`; COM objects that reference other processes (UIA elements) are released on the thread that read them (02 7.6).
- CsWin32 output is `internal`; the App reaches a Win32 call only through a public Platform wrapper (`DllSearchHardening.Apply`, `CaptureExclusion.Apply`).
- WinRT APIs (OCR, `SoftwareBitmap`) come from the Windows TFM projection, no extra package (04 7.9).

### 14.8 XAML

- Theme keys through `{DynamicResource}` only; the palette dictionary is swapped as one merged dictionary by `ThemeManager` (06 7.5). A `StaticResource` to a theme key is a bug the source guard catches.
- No colour or radius literal outside `Themes/FixedColors.xaml` and the named exceptions (INV-HOME-24).
- Windows derive from `ShotAIWindow`; popups are `ShotAIPopup` or overlay-layer elements (5.4).
- Every interactive element has `AutomationProperties.Name`; live regions raise `LiveRegionChanged` explicitly (06 7.10).
- Positioning that must land on a specific monitor is done in physical px with `SetWindowPos` after `SourceInitialized`, never with `Left` and `Top` (03 7.7).
- Animations honour `SystemParameters.ClientAreaAnimation` (the Windows "Animation effects" setting, the analogue of `prefers-reduced-motion`; 03 7.4.3, 06 7.5).

### 14.9 Analyzers and `.editorconfig`

Severities live in `dotnet/.editorconfig`; warnings are errors, so "error" and "warning" behave the same in CI.

| Rule | Core | Platform | App | Why |
|---|---|---|---|---|
| Nullable warnings | error | error | error | 14.3 |
| `CA1416` platform compatibility | error | error | error | INV-ARCH-1 |
| `CA2007` ConfigureAwait | error (one reasoned per-file suppression: `ProjectSession.cs`, T3) | error (two reasoned per-file suppressions: `MsalGateway.cs`, `WebView2PdfRenderer.cs`, T3) | off | T3, T4 |
| `CA2016` forward the token | error | error | error | K1 |
| `CA2012` use `ValueTask` correctly | error | error | error | 01's `ValueTask` mutations |
| `VSTHRD002` no synchronous waits | error | error | error (one allowlisted file) | T9 |
| `VSTHRD100`, `VSTHRD101` no `async void`, no async lambdas to void delegates | error | error | error (per-site suppression for event handlers) | T12 |
| `VSTHRD110` observe async results | error | error | error | 14.4 |
| `VSTHRD200` `Async` suffix | error | error | error | 14.2; suppressed on `Apply` and `ApplyDurable` only |
| `VSTHRD001`, `VSTHRD003`, `VSTHRD004`, `VSTHRD010`, `VSTHRD012`, `VSTHRD111` | off | off | off | JoinableTaskFactory rules (11 7.12). Corrected in WP-A1: `VSTHRD003` joins them, because it flags every await of a task held in a field or parameter, which in-flight joins (10's `UpdateService`) and the test dispatchers' `InvokeAsync` do by design |
| `RS0030` banned symbols | error | error | error | the lists below |
| Generated code (`*.g.cs`, CsWin32 and toolkit output) | suppressed for that glob only, never globally | | | Q-IPC-9; `RS0030` still applies there (the analyzer's default), so generated code cannot call a banned API either |

Banned symbols (`BannedSymbols.txt`, 11 7.12, plus this document's additions). The analyzer has no per-file allowlist, so each allowance is an `.editorconfig` section for exactly that file:

| Project | Banned | Allowed only in |
|---|---|---|
| App | `Dispatcher.Invoke`, `Dispatcher.BeginInvoke`, `Dispatcher.InvokeAsync`, and the `DispatcherExtensions` `Invoke` and `BeginInvoke` extension methods (`System.Windows.Presentation`) | `WpfUiDispatcher.cs`; `StaRenderThread.cs` (it drives its own dispatcher, 04 7.10.6); `UiDeferral.cs` (focus and layout deferrals at a named priority, R-ARCH-18) |
| App | `Task.Wait`, `Task.WaitAll`, `Task.WaitAny`, `Task<T>.Result`, `ValueTask<T>.Result`, and `GetResult` of every task awaiter (`TaskAwaiter`, `TaskAwaiter<T>`, `ValueTaskAwaiter`, `ValueTaskAwaiter<T>` and the four `Configured...Awaiter` types) | `ShutdownFlush.cs` |
| all | `Process.Start` | `ShellUrlLauncher.cs`, `ShellReveal.cs`, `ProcessStarter.cs` (the personal-copy hand-off, 12 7.10.4) |
| all | `Microsoft.Web.WebView2.Wpf.WebView2`, `Microsoft.Web.WebView2.WinForms.WebView2` | nowhere (INV-ARCH-5) |
| Core | `SynchronizationContext.Current`, `SynchronizationContext.SetSynchronizationContext` | `ProjectSessionFactory.cs` |
| Core | every half-to-even rounding: `Math.Round`, `MathF.Round`, the static `Round` of `double`, `float`, `decimal` and `Half`, `IFloatingPoint<T>.Round`, and `Convert.ToByte` to `Convert.ToUInt64` from `double`, `float` or `decimal` | `JsMath.cs` (Q-ARCH-5 default) |
| Core | `N:Microsoft.Win32`, `N:Windows` (namespace entries; each also bans the namespaces nested in it, so `Microsoft.Win32.SafeHandles` too) | nowhere (AC-MODEL-26, INV-ARCH-1) |
| all | `MessageBox.Show` (WPF and WinForms) | `LegacyInstanceGuard.cs` until stage S5 (Q-SHELL-19) |

Corrected in WP-A1, from its deliberate violations against BannedApiAnalyzers 5.6.0 (AC-ARCH-3):

- An `M:` entry without a parameter list bans only the parameterless overload: `M:System.Math.Round` banned nothing, and `M:System.Diagnostics.Process.Start` banned only the instance `Start()`. Each `BannedSymbols.txt` therefore lists every overload by its full documentation ID, taken from the .NET 10 reference assemblies; an SDK that adds an overload needs a new line.
- `N:` entries hold, and each also bans the namespaces nested in it: `N:Microsoft.Win32` bans `Microsoft.Win32.SafeHandles`, so Core cannot name `SafeFileHandle` (01 opens files by path). A call whose banned return type is never named (`var h = File.OpenHandle(path)`) is not flagged.
- `await` is not flagged, although it compiles to `GetAwaiter().GetResult()`.
- An allowance turns `RS0030` off for the whole file, because the analyzer reports every ban under that one ID: an allowlisted file may use any banned API, not only the one it is allowlisted for. The allowlisted files stay small and single-purpose, 9.5 item 7 asks about them, and the other rules still apply in them (a synchronous wait also fails `VSTHRD002`, which is off only in `ShutdownFlush.cs`).
- The lists cover the look-alikes of each rule that the first draft missed: the other half-to-even roundings in Core, and in the App the `DispatcherExtensions` methods, `Task.WaitAny` and every awaiter's `GetResult`.
- The WebView2 and WinForms `MessageBox` entries were proved in a scratch project with the App's list, because no project references those assemblies yet.

### 14.10 Pull requests

- One subsystem slice per PR, merged to `main` beside the Electron app; the title starts with `native:` (as the scaffold commit `dcb4196` does).
- The description lists the spec IDs it implements (INV, D, AC) and any deviation; a behavior the spec does not describe is added to the spec in the same PR, or recorded as an open question there.
- New user-visible strings and log lines are added to the owning spec's tables in the same PR.
- Tests land with the code; a PR that adds a Windows-only behavior adds its Windows test project cases.
- An Electron fix made during the port (Electron is frozen to fixes, Q-PKG-27) is mirrored natively, or the native gap is recorded in the owning spec.
- `contract/` changes land in both repositories (`dotnet/README.md`).

---

## 15. Decisions log

### 15.1 Fixed decisions

These are not re-litigated. A real problem found with one is recorded as an open question (15.4), never worked around silently.

| # | Decision | Rationale | Applied in |
|---|---|---|---|
| F-1 | C# on .NET 10 LTS | supported to November 2028; one language for the whole app; the team already knows .NET (feasibility "What gets better" 7) | everywhere |
| F-2 | WPF, not WinUI 3 | the area overlay and the capture pill need per-pixel transparent, topmost, borderless windows, which WinUI 3 does not support; mature drawing primitives for the editor | 03, 04 |
| F-3 | CommunityToolkit.Mvvm | source-generated properties and commands, no framework lock-in | 5.1 |
| F-4 | Microsoft.Extensions.DependencyInjection | the standard .NET container; validation on build | 4 |
| F-5 | CsWin32 for every P/Invoke | generated, reviewable signatures from Win32 metadata; no hand-written `DllImport` | 14.7 (one exception, I-3) |
| F-6 | Framework-dependent deployment on the .NET Desktop Runtime serviced by Microsoft Update | patch responsibility moves to Microsoft; a self-contained build would freeze the runtime until the next release | 12, INV-PKG-5 |
| F-7 | x64 and ARM64 builds | native ARM64 removes emulation and the forced-off GPU (`gpu-policy.ts`) | 12, D-PKG-2 |
| F-8 | Screen capture through BitBlt or PrintWindow, or DXGI Desktop Duplication; never Windows.Graphics.Capture | an unpackaged app gets a yellow capture border; borderless needs MSIX, a restricted capability and consent | 02, INV-CAP-25 |
| F-9 | `SetWindowDisplayAffinity(WDA_EXCLUDEFROMCAPTURE)` for self-exclusion | the same mechanism as Electron's `setContentProtection` (`src/main/main.ts:280`, `:334`); the ref-counted shield ports as is | 02 7.8, 03 |
| F-10 | UI Automation through COM on an MTA worker | the Rust addon and koffi disappear; MTA avoids marshaling into our own UI thread | 02 7.6 |
| F-11 | `Windows.Media.Ocr` | offline, first-party, nothing vendored | 04 7.9 |
| F-12 | WebView2 `PrintToPdfAsync` of the same export HTML | the PDF matches the HTML export; the runtime is serviced with Edge | 09 7.7 |
| F-13 | Open XML SDK for Word and PowerPoint | Microsoft, MIT, platform-neutral (so the builders test on Linux) | 09 7.10, 7.11 |
| F-14 | Native libavif for AVIF | no dependable built-in AVIF encoder on Windows (WIC needs Store codec extensions) | 09 7.6 |
| F-15 | System.Text.Json | the BCL JSON stack | 01 7.2 (interpretation I-1) |
| F-16 | System.IO.Compression | the BCL zip stack; streaming archives | 01 7.9, 09 7.12 |
| F-17 | MSAL.NET with the WAM broker and `Microsoft.Identity.Client.Extensions.Msal` | SSO with the Windows account, device-bound tokens, native Conditional Access; the cache gains the cross-process lock Electron lacked (`src/main/main.ts:161-167`) | 08 7.5, 7.6 |
| F-18 | The Anthropic C# SDK with `WorkloadIdentityCredentials` for Entra federation | official SDK; the federation fields map one to one | 07 7.6, 08 7.9 |
| F-19 | `HttpClient` with the system proxy and the Windows certificate store | API and streaming traffic work behind corporate proxies and TLS inspection, which Node's fetch did not | 08 7.14 (interpretation I-5) |
| F-20 | A per-machine MSI or MSIX, spec 12 decides | device-context Intune deployment, signing | 12 7.1 chose the MSI (I-2); dual-purpose since 2026-09-23, per-machine through Intune and per-user by hand (12 7.4.5) |
| F-21 | The same `HKLM\SOFTWARE\Policies\shotAI\Federation` key and ADMX | administrators change nothing | 08, 10.3 |
| F-22 | The same `%APPDATA%\shotAI\settings.json`, project folders and `project.json` contract; `contract/` read in place | rollback and cross-platform projects keep working | 01, 10, 10.5 |
| F-23 | UI edits update the in-memory model first and persist through a serialized write queue with atomic writes, rolling back with a notice on failure; the editor save path (flatten, persist, then report success) stays synchronous | responsiveness (feasibility "Edits wait for the disk"); the redaction bake must be on disk before anything can read it | 7.4 to 7.6 |
| F-24 | Windows 10 2004 (10.0.19041) minimum | first build with `WDA_EXCLUDEFROMCAPTURE` | scaffold, 1.3 |

### 15.2 Interpretations of fixed decisions

| # | Interpretation | Why | Source |
|---|---|---|---|
| I-1 | "System.Text.Json" means its `Utf8JsonReader` and `JsonNode` tree plus a Core unescaper and a hand-written `JSON.stringify` writer. | the library cannot write `JSON.stringify`'s bytes (JS number text, lowercase escapes, lone surrogates) or read lone-surrogate escapes; golden files prove byte parity | Q-MODEL-2, Q-MODEL-18 |
| I-2 | The installer is one dual-purpose MSI per architecture (per-user by default, per-machine with `ALLUSERS=1`), deployed as an Intune Win32 app in its per-machine scope; no MSIX. Revised 2026-09-23 from per-machine only, at the maintainer's decision. | MSIX virtualizes new `AppData` files, splitting `settings.json` and logs from Electron; the per-folder opt-out needs build 20348; a person without administrator rights must still be able to install, as with Squirrel | 12 7.1, 7.4.5, INV-PKG-1, closes Q-INFRA-8 |
| I-3 | `shotai_avif.dll` is bound with `LibraryImport`, the one exception to F-5. | CsWin32 generates only from Win32 metadata and cannot describe a third-party DLL | Q-EXP-1 |
| I-4 | The `Anthropic` package is referenced by Core. | platform-neutral; keeps request, stream and error tests on Linux | Q-SOP-14 |
| I-5 | "The system proxy" means the Windows proxy settings (including WPAD and PAC), not proxy environment variables. | environment variables are an injection surface and Chromium's stack ignored them | Q-ARCH-3 (default pending) |
| I-6 | "UI Automation through COM" uses CsWin32 COM projections, not FlaUI. | one interop mechanism (F-5) | 02 7.6 |
| I-7 | Capture v1 uses GDI `BitBlt` with `CAPTUREBLT`; DXGI Desktop Duplication stays permitted for a later menu-frame optimization. | BitBlt polling is proven by Electron | 02 7.7, Q-CAP-14 |
| I-8 | The optimistic rule also governs Settings (10) and Home list operations (06); "durable" covers every write of a file besides `project.json`, not only the editor save. | the render or image must exist before the manifest references it | 01 7.10, 05 7.5, 06 D-HOME-7, D-HOME-8 |
| I-9 | The WebView2 print copy is loaded with `Navigate` to a file URI under an explicit user data folder, never `NavigateToString`. | `NavigateToString` refuses more than 2 MB; the default data folder beside the exe is read-only under Program Files | 09 7.7, EDGE-EXP-39, Q-EXP-17 |

### 15.3 Cross-spec resolutions

These resolutions are binding: each spec named in "Specs to align" must state what the resolution says, so a reader of that spec alone cannot implement a superseded design. The Status column records the 2026-09-23 consolidation, in which the twelve specs were edited to these rows; every row was checked against the named spec sections, and "pending in <spec>" marks a row whose spec text has not yet been edited. A later change to a row reopens it as pending in the specs it names.

| # | Conflict | Resolution | Specs to align | Status |
|---|---|---|---|---|
| R-ARCH-1 | Two homes for `JsMath` (02 lists `ShotAI.Core.Capture`, 01 `ShotAI.Core.Json`), and loose "floor(x + 0.5)" wording in 02's and 03's notation | one copy in `ShotAI.Core.Json` with the exact definition of 01 7.2.3 and INV-CAP-18; the notation wording is descriptive only | 02 7.1, 03 notation | applied 2026-09-23: 02 notation and INV-CAP-18, 7.1 (`JsMath` not in `ShotAI.Core.Capture`); 03 notation |
| R-ARCH-2 | Two `IAuthService` shapes (07 7.11 versus 08 7.12), `string` versus enum `AuthStatus.Mode` | 08's interface and value types are canonical; 07's `VerifyFederationAsync` and `IApiKeyStore` are internal to Core; view models use `GetApiKeyStatusAsync`, `SetApiKeyAsync`, `ClearApiKeyAsync` | 06, 07 (Q-IPC-1) | applied 2026-09-23: 06 7.12 and section 10; 07 7.11 (sketch superseded) and section 10 |
| R-ARCH-3 | Owner of the connection test | `IAuthService.TestConnectionAsync` is the one public entry point; 07's models-retrieve leg sits behind an internal seam; `IClaudeService.TestConnectionAsync` is removed | 07 (Q-IPC-2) | applied 2026-09-23: 07 2.5, 7.6 (`SopModelProbe` seam), 7.8, 7.11 |
| R-ARCH-4 | `IProjectStore` (09), concrete `ProjectStore` consumers (05 7.19, 07 `SopRequestAssembler`) | every consumer outside the store depends on `IProjectService` | 05, 07, 09 (Q-IPC-3) | applied 2026-09-23: 05 7.19 and section 10; 07 7.4 (`SopRequestAssembler`) and section 10; 09 7.16 and section 10 |
| R-ARCH-5 | Session construction and durable signature | sessions come from `IProjectSessionFactory.Create` on the UI thread (not `new ProjectSession`, 05 7.3); `ApplyDurable` takes `Func<IProjectService, ...>`; 04's `IStepFlattener` session overload takes `IProjectSession` | 01, 04, 05 | applied 2026-09-23: 01 7.10 and section 10; 04 7.1, 7.7, 7.10.1; 05 7.3, 7.15 |
| R-ARCH-6 | Three "wait for pending writes" APIs (07 `IProjectSettle.WhenSettledAsync`, 09 `IProjectService.WhenIdleAsync(path)`, 11 `IProjectSession.WhenIdleAsync`) | `IProjectSession.WhenIdleAsync` is the primitive; `IProjectSettle.WhenSettledAsync(path)` awaits every registered session for the path, open or draining, or completes at once when none is registered (wording updated by R-ARCH-27; it first read "finds the open session and awaits it"); 09 calls `IProjectSettle` | 01, 09 (Q-EXP-11) | applied 2026-09-23: 01 7.10 and section 10; 09 INV-EXP-28, 7.16, Q-EXP-11 |
| R-ARCH-7 | `IStepFlattener` namespace (04 `ShotAI.Core.Rendering`, 11 `ShotAI.Core.Editor`) | `ShotAI.Core.Rendering`, 04 owns it | 11 (Q-EDIT-20) | applied 2026-09-23: 11 7.3, 7.10 and section 10 |
| R-ARCH-8 | `RenderGateException` (04) versus `RenderRefusedException` (09) | `RenderGateException`, deriving from `ShotAIException`, with a `(string, Exception)` constructor (07 D-SOP-22); 09 injects `IRenderGate`, 07 calls the static `RenderGate` with its probe | 07, 09 (Q-EDIT-20) | applied 2026-09-23: 07 7.4 and section 10; 09 7.4 (collector), 7.16 |
| R-ARCH-9 | Export entry names (05 `ExportAsync`, 06 and 09 `ExportWithSaveDialogAsync`) | 09 7.13's seven members | 05 (Q-EXP-18) | applied 2026-09-23: 05 7.15 and section 10 (also 06 7.6 and section 10) |
| R-ARCH-10 | Exit flush and container disposal (01 7.12: `Task.Run(...).Wait(6 s)` and `await provider.DisposeAsync()`; 11 7.10: one 5 s flush of both queues and `provider.Dispose()`) | 11 7.10: `ShutdownFlush.Run(5 s)` over the project and settings queues, then `provider.Dispose()`; every disposable singleton (including `ProjectStore` and `CaptureEngine`) also implements `IDisposable` | 01, 02 | applied 2026-09-23: 01 7.7, 7.12, AC-MODEL-36; 02 7.1, 7.2 |
| R-ARCH-11 | Capture event marshaling (03 7.5 says `InvokeAsync`, 03 7.4.6 and 11 say `Post`) | `IUiDispatcher.Post` only | 03 7.5 | applied 2026-09-23: 03 INV-SHELL-21, 7.4.6, 7.5 |
| R-ARCH-12 | WebView2 version for About (03 7.11 references WebView2 from the App) | `IAppInfo.Current.WebView2Version` through Platform's `IWebView2RuntimeInfo`; the App has no WebView2 reference (INV-ARCH-5) | 03 | applied 2026-09-23: 03 7.2 (`AboutText`), 7.4.5 (`AboutWindow`), 7.11, D11 |
| R-ARCH-13 | Native local data under `%LOCALAPPDATA%\shotAI\` (08 MSAL cache, 09 WebView2 folder), which is the Squirrel install root `%LocalAppData%\shotai\` under case-insensitive paths, so removing Electron at cutover would delete it (EDGE-PKG-22) | `IAppPaths.LocalDataDirectory` = `%LOCALAPPDATA%\LFI\shotAI\`; MSAL cache at `entra\msal-cache.bin`, WebView2 data at `WebView2\` (12 Q-PKG-4 default adopted) | 08 7.6, 7.16; 09 7.7; 10 7.4.4 | applied 2026-09-23: 08 7.6, 7.16; 09 7.7; 10 7.4.4 |
| R-ARCH-14 | Spec numbering ("08 Brand and theme" in 06, 07, 09) and 09's brand names (`Brands.PinnedBrand`, `Brands.Coerce`, `BrandId`) | brand, palette, narrowing, settings, logging and links are 10; auth is 08; the brand functions are 10's `BrandPalette.PinnedBrand`, `CoerceBrand`, `IsBrandId`, `PinIsUnrecognised` with string brand ids | 06, 07, 09 (Q-AUTH-1, Q-INFRA-10) | applied 2026-09-23: 06 notation and 7.6; 07 section 10; 09 sections 1 and 10 |
| R-ARCH-15 | Two Anthropic client factories and handler chains (07 `ClaudeClientFactory` with `EgressPinHandler` and `ResponseHeaderCaptureHandler` on the shared handler; 08 `AnthropicClientFactory` with a per-client `AnthropicHostGuardHandler`) | 08's `IAnthropicClientFactory` is the only factory (07's `IClaudeClientFactory`, `ClaudeClientFactory` and `ISopCredentialSource` are superseded). The shared `SocketsHttpHandler` carries NO Anthropic-specific handler (it also serves MSAL, the update check and GitHub). Per client, `ClientOptions.Handlers` holds a fresh host guard enforcing 07's stricter rule (`https`, host `api.anthropic.com`, port 443) and 07's header capture; the exchange client gets its own guard over the shared handler. That `ClientOptions.Handlers` observe every retry attempt's response (the 429 classifier needs the last one) is UNVERIFIED and pinned by a 07 test | 07 7.6, 08 7.9, 7.14 | applied 2026-09-23: 07 7.1, 7.6; 08 7.9 (fresh `Handlers` per client), 7.14 |
| R-ARCH-16 | `SopPanelViewModel` dependencies (07 7.10 lists `ProjectSession`, `IApiKeyStore`, `IConfirmHost`) | `IProjectSession`, `IClaudeService`, `IAuthService`, `ISettingsService`, `IStepFlattener`, `IConfirmService` (06's name), plus `IUiDispatcher` and `ILogger<SopPanelViewModel>` for the event marshaling of 5.6 and T6; created by `SopPanelViewModelFactory.Create(IProjectSession)` (C6) | 07 | applied 2026-09-23: 07 7.10 (with `IUiDispatcher` and `ILogger<SopPanelViewModel>`) |
| R-ARCH-17 | 09 names `IPathConfine` and `IAtomicFile` | 01's static `PathConfine` with an `IPathProbe` argument, and the concrete `AtomicFile` (tests use temp folders, a fake classifier and `FakeTimeProvider`) | 09 | applied 2026-09-23: 09 7.12 (`PackageWriter`), 7.16 |
| R-ARCH-18 | 04's `StaRenderThread` and inline text box use `Dispatcher` APIs that 11 bans in the App | allowlisted per file: `StaRenderThread.cs` (its own dispatcher) and one App helper `UiDeferral` for focus and layout deferrals at a named priority; service-event marshaling stays `Post` only | 04 7.10.5, 7.10.6 | applied 2026-09-23: 04 7.1, 7.10.5 (`UiDeferral`), 7.10.6 |
| R-ARCH-19 | Popovers as overlay-layer elements (05 7.13) versus `ShotAIPopup` (06 7.8) | the overlay layer by default; `ShotAIPopup` where WPF needs a popup HWND (tooltips, context menus, `ComboBox` drop-downs, 06's `OverflowMenu`); every HWND registered | none (both allowed) | no spec edit required; cited by 03 7.4.10, 05 7.13 and 06 7.6 (target dropdown in the overlay layer) |
| R-ARCH-20 | `ManifestChangeKind` in 05's `ShotAI.Core.Report` while the session raises it; `AffectedStepIds` only on `ReportOperation` | `ManifestChangeKind` and the event args live in `ShotAI.Core.Store`; `ProjectOperation` gets a virtual `AffectedStepIds` returning null | 01, 05 | applied 2026-09-23: 01 7.1, 7.10; 05 7.1, 7.5 and section 10 |
| R-ARCH-21 | 04 7.13 and 05 7.11 decode with `IWICImagingFactory::CreateDecoderFromStream`, which picks a codec by sniffing and can invoke any installed third-party codec | after the magic-byte check, create the decoder explicitly with `CreateDecoder(GUID_ContainerFormatPng or GUID_ContainerFormatJpeg, ...)` and `Initialize` it on the stream (Q-IPC-16 default); undecodable input fails closed | 01, 04, 05, 09 | applied 2026-09-23: 01 EDGE-MODEL-39; 04 7.13; 05 7.11, EDGE-REP-40, Q-REP-19; 09 7.5 (and 02 7.12 `WicImageCodec`) |
| R-ARCH-22 | Monitor id types (02 `uint`, 06 `CaptureReadiness` `int?`) | `uint` (`(uint)HMONITOR`, 02 Q-CAP-11); the manifest's JSON number converts at the codec edge; never compared across launches | 06 | applied 2026-09-23: 06 7.3, 7.6 |
| R-ARCH-23 | The session contract is spread over requests in 05, 07, 09 and 11 | the consolidated contract of 7.4 (S1 to S10), tested by `Store/ProjectSessionTests` (AC-ARCH-4) | 01 7.10 | applied 2026-09-23: 01 7.10 |
| R-ARCH-24 | Startup auto-archive refresh (01 7.14 calls `homeViewModel.RequestRefresh()`) | `IProjectService.ProjectsChanged`, raised by `AutoArchiveStaleAsync` when at least one project moved (Q-IPC-6) | 01 | applied 2026-09-23: 01 7.8 (`AutoArchiveStaleAsync`), 7.14 |
| R-ARCH-25 | `IExternalLinks.OpenAsync` throwing (11: only if the launcher throws; 06: never) | 11's contract; 06's callers wrap the call and show nothing | 06 (Q-INFRA-22) | applied 2026-09-23: 06 INV-HOME-31 |
| R-ARCH-26 | `ICaptureTargetSelection` exists only in 06 | accepted: an App interface in `ShotAI.App.Home`, implemented by `CaptureModePickerViewModel`, consumed by 05 for Resume | 05, 11 | applied 2026-09-23: 05 7.19 and section 10; 06 7.6; 11 7.3, 8.2 |
| R-ARCH-27 | S9 unregistered a session from `IProjectSettle` at `DisposeAsync` while its writes still drained, so a Home export or SOP run started right after Back found no session, settled at once and could read before those writes landed (`GetProjectForReadAsync` runs outside the queue; raised by 05) | a disposed session stays registered until every operation and durable call it accepted has finished, then unregisters; `WhenSettledAsync(path)` awaits every registered session for the path, open or draining (7.4 S7, S9; AC-ARCH-4) | 01 7.10 (S7, S9, the `IProjectSessionFactory` comment) and 8.2 (`ProjectSessionTests`), 05 7.3 (Back row), 07 INV-SOP-28 and 7.13, 09 INV-EXP-28 and Q-EXP-11, 11 7.3.2 (the `IProjectSessionFactory` and `IProjectSettle` comments) | applied 2026-09-23 (added after the consolidation, then applied): 01 7.10 S7, S9 and the `IProjectSessionFactory` comment, 8.2 `ProjectSessionTests`; 05 7.3 Back row; 07 INV-SOP-28, 7.13; 09 INV-EXP-28, Q-EXP-11; 11 7.3.2 |

### 15.4 Open decisions

Each has a default to take if nobody decides before the PR that needs it. Owners are the specs named; `Q-ARCH` items are this document's.

| # | Question | Options | Default if undecided |
|---|---|---|---|
| Q-ARCH-1 | Which machines are "reference x64" and "ARM64 dev VM" for the budgets? | a named fleet laptop model; the CI runner; the developer's own machine | the most common fleet x64 laptop model (named by IT) and the existing Windows-on-ARM dev VM, recorded in `docs/native/PLAN.md` |
| Q-ARCH-2 | Are the proposed budgets of section 11 right? | adopt as targets; adopt as gates; drop | targets, not gates; revised once with phase B measurements |
| Q-ARCH-3 | Proxy environment variables (Q-SOP-20). With `DefaultProxyCredentials = DefaultCredentials` (08 7.14), a proxy set by an environment variable would also receive the user's integrated Windows credentials. | (a) remove `HTTPS_PROXY`, `HTTP_PROXY`, `ALL_PROXY`, `NO_PROXY` at startup step 5 so `HttpClient.DefaultProxy` uses the system settings; (b) keep 08 7.14 as written; (c) a Platform `IWebProxy` from WinHTTP's IE configuration | (a): smallest change, matches Chromium's behavior for sign-in; revisit if IT relies on environment proxies |
| Q-ARCH-6 | Display affinity from worker threads (Q-CAP-15, Q-CAP-22) | direct calls from the grab thread; marshal to the UI thread | decided by the phase B probe before the pill and overlay are built; if marshaling is needed, a bounded wait that skips the grab on timeout (fail closed, DL2), and the startup application of 4.2 step 10 moves to `Task.Run` (DL1) |
| Q-ARCH-7 | Watch the open project's folder for external changes? | `FileSystemWatcher`; none | none in 2.0.0 (parity); every write re-reads the disk |
| Q-PKG-1 | WiX Toolset licensing | current release with its fee terms; last MS-RL release; Advanced Installer | read the terms before the installer PR; pin the last release without a fee if the fee is not approved |
| Q-PKG-2 | Signing eligibility | Azure Artifact Signing under LFI; an OV certificate in Key Vault | IT owns the subscription and validation; OV in Key Vault if not eligible |
| Q-PKG-5 | Internal (baked) build distribution | 30-day workflow artifact for IT; public BYOK MSI plus ADMX only | workflow artifact; decide with IT before S1 |
| Q-PKG-8 | Legacy guard: block or warn | block; warn with "Open anyway" | block |
| Q-PKG-10 | ReadyToRun | on for first-party only; off | on for first-party, kept only if it saves at least 100 ms (PB-9) |
| Q-PKG-12 | ARM64 CI runner | hosted `windows-11-arm`; self-hosted; manual per release | hosted, falling back to manual per release |
| Q-PKG-30 | Restricted DLL search versus WPF native loads | keep; add `AddDllDirectory`; drop the hardening | keep, add `AddDllDirectory` for the WindowsDesktop framework folder if a load fails |
| Q-AUTH-3 | WAM redirect URI on the client registration | add to the existing registration; a separate client registration | add to the existing registration |
| Q-AUTH-4 | Forced silent refresh after interactive sign-in | keep; drop if redundant | keep until AC-AUTH-27 proves it redundant |
| Q-AUTH-5 | `MsalCacheHelper` behavior on an undecryptable file or failed write | rely on it; custom DPAPI cache with a named mutex | pin with `MsalCachePersistenceTests`; fall back to the custom design if they fail |
| Q-SOP-1 | Sonnet 5 price in the estimate | parity (3 and 15 per MTok); the published table | parity until confirmed on anthropic.com/pricing, then all three platforms together |
| Q-CAP-5 | Raw Input instead of the low-level hook | hook plus watchdog; Raw Input | hook plus watchdog; Raw Input only if field logs show reinstalls |
| Q-SHELL-3 | Tooltips and popups in the non-activating pill | WPF tooltips; `ShotAIPopup` on hover; a `WH_CALLWNDPROC` catch-all | build the tests first, then pick the first option that passes |
| Q-SHELL-19 | The legacy guard's `MessageBox` | keep until S5; a registered `ShotAIWindow` notice | keep, allowlisted |
| Q-SHELL-20 | Window widths include invisible borders | keep constants as outer widths; add the border | keep; measure once against Electron |
| Q-REP-2 | The native report is narrower than Electron's (770 versus 820 DIP at 100%) | accept; fix Electron's CSS before cutover | accept, and fix Electron's CSS if cheap so pilot users see one report |
| Q-REP-11 | Report virtualization | none; `VirtualizingStackPanel` | none unless PB-7 fails |
| Q-HOME-2 | Archivo in WPF | upstream static instances; self-instanced statics | upstream statics with `SOURCES.md` hashes |
| Q-HOME-12 | High contrast | `SystemColors` mapping; none | the `SystemColors` mapping, after design sign-off |
| Q-EDIT-1 | Editor marker colour default | `markerColorFor(step)`; parity (`ACCENT`) | `markerColorFor`, noted in the release notes |
| Q-EDIT-2 | Unknown annotation types at flatten | fail closed; skip | fail closed |
| Q-EDIT-5 | OCR recognizer missing | notices of 04 7.9; silent | the two notices; 12 documents the Feature on Demand |
| Q-EDIT-19 | Windows OCR splits hyphenated tokens | measure; join adjacent words | measure in phase C; add the join rule if splitting occurs |
| Q-EXP-2 | AVIF encoder parameters | squoosh's settings; libavif defaults | verify against `avif_enc.cpp` at the jsquash 2.1.1 tag before the shim is built |
| Q-EXP-3 | Printing from a hidden WebView2 controller | hidden; an off-screen visible window excluded from capture | hidden, with the fallback if the Windows test prints blank |
| Q-EXP-14 | What "byte-identical exports" means | text with normalized image payloads; full bytes | text with replayed or normalized payloads plus media type and dimensions |
| Q-MODEL-6 | Cloud placeholders and reparse points | D-15 behavior; parity | implement D-15; verify on a Files On-Demand folder |
| Q-IPC-10 | Exit flush as a bounded blocking wait | blocking wait; cancel `Closing` and close again | bounded blocking wait (DL4) |
| Q-IPC-21 | `VSTHRD200` against `Apply` and `ApplyDurable` | keep names and suppress; rename | keep and suppress |

Closed in the 2026-09-23 consolidation (kept so the IDs still resolve; each closure is stated in the owning spec or in this document):

| # | Question | Closed by |
|---|---|---|
| Q-ARCH-4 | `ANTHROPIC_CUSTOM_HEADERS`: mutate the environment or filter in a handler (Q-AUTH-17) | default adopted: removed at startup step 5 (4.2) with the per-client host guard kept (R-ARCH-15); 08 Q-AUTH-17 records the adoption |
| Q-ARCH-5 | Ban `Math.Round` in Core | default adopted: banned Core-wide with only `JsMath.cs` allowlisted (14.9, 11 7.12), together with every other half-to-even rounding (WP-A1); 02 INV-CAP-18 and 03's notation state it; `NoMathRoundInCapture` stays as the capture-specific check |
| Q-PKG-31 | `docs/native/PLAN.md` is missing | resolved: `PLAN.md` exists (12 Q-PKG-31) |
| R-ARCH-13 sign-off | Native-only local data location | adopted: new native-only local data lives under `%LOCALAPPDATA%\LFI\shotAI` (`IAppPaths.LocalDataDirectory`: the MSAL cache and the WebView2 user data); `settings.json`, the logs and the projects stay where the Electron build keeps them (10.1); applied in 08, 09, 10 and 12 (12 Q-PKG-4 resolved) |
| Q-IPC-16 | Image decoding in the privileged process | resolved by R-ARCH-21: accepted with the magic-byte check and explicit PNG and JPEG decoders (11 Q-IPC-16) |
| Q-INFRA-1, Q-INFRA-2, Q-INFRA-3, Q-INFRA-12 | Settings file changes (preserve unknown enum strings and nested `sop` keys, default a non-absolute `projectsDir`, back up a corrupt file) | adopted: 10 7.4.2 and 7.4.3 specify all four; 01 agreed to Q-INFRA-3 as 01 Q-MODEL-24 |
| Q-HOME-15 | STA test harness | adopted: the in-repo helper of 2.1 and 12.1 (06 Q-HOME-15) |
| Q-INFRA-5 | Update notice on managed devices | resolved 2026-09-23: the notice follows the install scope; per-machine installs show who installs updates and no download page (06 INV-HOME-45, 12 7.10.4) |
| Q-MODEL-1 | `displayScale` out of range | decided in WP-A3: default adopted, the codec clamps (Electron parity) and the shared case stays `open`; revisit with macOS on shotAI_MacOS#105 after cutover (01 Q-MODEL-1) |
| Q-MODEL-18 | Electron-side golden generator | decided in WP-A3: default adopted, `src/main/codec-golden.test.ts` gated on `SHOTAI_CODEC_GOLDENS=1`, landed with the native codec (01 Q-MODEL-18) |

### 15.5 Architecture-level divergences from Electron

| ID | Change | Class | Justification |
|---|---|---|---|
| D-ARCH-1 | Proxy environment variables do not affect shotAI's traffic (if Q-ARCH-3 (a) is adopted) | IMPROVEMENT [SECURITY] | integrated credentials and traffic metadata must not follow a proxy injected through the environment; Chromium's stack already ignored them for sign-in |
| D-ARCH-2 | Native machine-local (non-roaming) data lives under `%LOCALAPPDATA%\LFI\shotAI\` | IMPROVEMENT | the obvious `%LOCALAPPDATA%\shotAI\` is the Squirrel install root and would be deleted with Electron (R-ARCH-13) |
| D-ARCH-3 | Images decode in-process with explicit PNG and JPEG decoders after a magic-byte check | IMPROVEMENT [SECURITY] (replaces ELECTRON-ONLY sandbox containment) | the renderer sandbox no longer exists (9.1) |
| D-ARCH-4 | A startup timing line in the log | IMPROVEMENT (log only) | PB-9 needs a field measurement |
| D-ARCH-5 | The IPC bridge, preload, context isolation, renderer sandbox, CSP, navigation confinement, `shot://`, asar, fuses, GPU policy and Squirrel handling | ELECTRON-ONLY | one process with typed calls (D-IPC-1, D-IPC-15), the WebView2 host keeps the web hardening (INV-IPC-20), packaging replacements in 03 D16 and D-PKG-12 |

### 15.6 Consequences for `docs/native/PLAN.md`

The plan sequences PRs; this document only fixes what must exist before subsystem work can start, in this order:

1. **Guardrails**: `dotnet/.editorconfig`, the analyzer packages, `BannedSymbols.txt` per project (each proven by a deliberate violation), `nuget.config`, lock files, the package versions of 3.2 that the next steps need.
2. **Foundations in Core**: `ShotAI.Core.Errors` (`ShotAIException`, `UserMessage`), `ShotAI.Core.Threading` (`IUiDispatcher`, `EventRaiser`, `IAppLifetime`) with `ManualUiDispatcher` and `ThreadUiDispatcher`, the logging provider and line format, `IAppPaths` (Q-IPC-12).
3. **Model and store (phase A)**: `ShotAI.Core.Json`, `Model`, `Codec` with the conformance round trip unskipped; `AtomicFile`, `SerialWriteQueue`, `PathConfine`, `ProjectStore`, `ProjectSession` with the 7.4 contract; the Electron golden generator.
4. **Settings**: `SettingsService` with its codec, coercer and queue, and the brand generator plus its CI check.
5. **App skeleton**: `Program.Main` with DLL search hardening, `App` with the bootstrap of 4.2, the container of 4.3, `WpfUiDispatcher`, `ShutdownFlush`, `ShotAIWindow`, `PopupExclusion`, crash logging, and the two Windows test projects with the STA harness.
6. **Subsystems** in the feasibility order: viewer and report (05), capture and shell (02, 03), editor and redaction (04), SOP and auth (07, 08), exports (09), Home and Settings (06) as their pieces are needed, packaging and release (12) in parallel from step 5.
