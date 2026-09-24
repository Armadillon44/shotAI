# shotAI native (C# / .NET 10 / WPF)

The native Windows rewrite of shotAI. It lives beside the Electron app in this repo and
is merged to `main` in small PRs. The Electron app keeps shipping from `main` until
cutover, when native releases as 2.0.0 and the Electron code is removed in one PR.

- Why and what: [docs/NATIVE-WINDOWS-FEASIBILITY.md](../docs/NATIVE-WINDOWS-FEASIBILITY.md)
- How, in order: [docs/native/PLAN.md](../docs/native/PLAN.md)
- Behavior to reproduce, subsystem by subsystem: [docs/native/spec/](../docs/native/spec/)

**Status:** foundations (WP-A1), JSON with JavaScript semantics (WP-A2), the model
and `project.json` codec (WP-A3), the brand palette (WP-A4), the store's file primitives (WP-A5),
the project store's project and step operations and imports (WP-A6, WP-A7), the archive
engine (WP-A8), the optimistic project session (WP-A9), the settings service (WP-A10), logging
(WP-A11), the app host (WP-A12) and the window base with the single instance (WP-A13): analyzers, supply-chain
rules, Core's error, threading and composition types, `JsJson` (reads what `JSON.parse`
reads, writes the bytes `JSON.stringify` writes), and `ManifestCodec`, which writes the
same bytes as Electron for every golden in `tests/ShotAI.Core.Tests/Golden/codec/`. The
shared conformance suite runs its round trips: every `agreed` case passes, and the one
`open` case is reported (see `dotnet test ... --output Detailed` below). `BrandPalette` is
generated from `contract/brand.json` by `tools/ShotAI.GenBrand` and carries the same
contract stamp as the Electron and macOS tables. `AtomicFile`, `SerialWriteQueue` and
`PathConfine` are the primitives every writer uses; the junction and reparse-tag cases run
in `tests/ShotAI.Platform.Tests` on the Windows jobs, x64 and arm64. `ProjectStore` gates,
lists, creates, opens, renames, deletes and changes projects with Electron's persistence
rules, behind `IProjectService`, and adds, moves, deletes and imports steps and packages without
losing a step to a duplicate id or writing through a link. `ArchiveEngine` packs a project's
`shots/` and `export/` into `archive.zip`, checks every entry's length and CRC-32 before it
deletes an original, and restores zips Electron wrote (`tests/ShotAI.Core.Tests/Golden/archive/`);
the store archives, restores on open, and auto-archives stale projects. `IProjectSession` shows an
edit at once, writes it through the store's queue, and on a failed write rolls back only that
edit, with the others still pending shown again on top of the disk; `IProjectSettle` lets an export
or SOP run wait for those writes. `SettingsService` reads `settings.json` once at startup, applies each
change at once and writes it through its own queue, rolling back only a failed change; it keeps
every key a newer build wrote, where it was, and writes the bytes Electron's settings module
writes for every golden in `tests/ShotAI.Core.Tests/Golden/settings/`. `FileLoggerProvider` writes
`shotai.log` in electron-log's line format, so both builds share one readable log; a call only
formats and queues its line, and one writer appends per batch, rotates past 5 MiB and keeps at most
`shotai.log` and `shotai.old.log`. `shotAI.exe` starts: `Program.Main` restricts the DLL search
first, `App` logs the banner, loads the settings, builds and validates the container, shows a
placeholder window, logs the runtime line, and on exit cancels, flushes the queued writes for at
most 5 s and disposes in the specified order; `--selftest` runs the store self-test against a
settings file and projects folder of its own and exits 0 on `[selftest] PASS`. Every window the app
shows is excluded from capture before it is first visible: a `ShotAIWindow` registers its HWND
when it is created, and a hook on the UI thread registers any other window it shows (tooltips,
menus, drop-downs, system dialogs) just before the show. One instance runs per user session: a
second launch surfaces the first one's window and exits.

## Layout

| Path | Target | Holds |
|---|---|---|
| `src/ShotAI.Core` | `net10.0` | Everything testable without Windows: the model and `project.json` codec, store logic, geometry, SOP request and apply logic, export templates, brand palette, settings schema. **Must not reference Windows APIs.** |
| `src/ShotAI.Platform` | `net10.0-windows10.0.19041.0` | Windows services: hooks, capture, UI Automation, display affinity, DPAPI, policy registry, OCR, WebView2 PDF host, libavif. |
| `src/ShotAI.App` | `net10.0-windows10.0.19041.0` | The WPF app (`shotAI.exe`): windows, views, view models, composition root. |
| `tests/ShotAI.Core.Tests` | `net10.0` | xunit.v3 tests for Core, including the shared `contract/conformance` suite. Runs on Linux and Windows. |
| `tests/ShotAI.Platform.Tests` | `net10.0-windows10.0.19041.0` | xunit.v3 tests that need Windows: junctions, reparse tags, real sharing violations, the DLL search, the single-instance lock, window styles, the show hook, the own-window registry, WIC. Builds everywhere, runs on Windows only. |
| `tests/ShotAI.App.Tests` | `net10.0-windows10.0.19041.0` | xunit.v3 tests of the App on the in-repo STA harness (`Support/Sta.cs`): the container, the dispatcher, the exit order and flush, crash logging, the paths, window and popup registration, the activation listener, and the real exe run as a process (the self-test, a second launch, closing the main window). Builds everywhere, runs on Windows only. |
| `tools/ShotAI.GenBrand` | `net10.0` | The brand generator: writes `src/ShotAI.Core/Brand/BrandPalette.Generated.cs` from `contract/brand.json`; `--check` fails when it is stale. BCL only, so it builds when the table does not. |

Windows 10 2004 (10.0.19041) is the minimum because it is the first build with
`WDA_EXCLUDEFROMCAPTURE`, which keeps shotAI's windows out of its own screenshots.

## Build and test

Run everything from this folder. `global.json` opts `dotnet test` into
Microsoft.Testing.Platform, and `dotnet` only finds it when started here or below.

```sh
dotnet build ShotAI.slnx -c Release
dotnet test --project tests/ShotAI.Core.Tests/ShotAI.Core.Tests.csproj -c Release   # any OS
dotnet test --project tests/ShotAI.Core.Tests/ShotAI.Core.Tests.csproj -c Release --output Detailed \
  --filter-class ShotAI.Core.Tests.Conformance.ConformanceTests                       # shows the open cases
dotnet run --project tools/ShotAI.GenBrand -- --check                                 # is the brand table current?
dotnet test --solution ShotAI.slnx -c Release                                         # Windows: every test project
dotnet run --project src/ShotAI.App                                                   # Windows only
dotnet run --project src/ShotAI.App -- --selftest                                     # Windows: exits 0 on PASS
```

The whole solution **builds** on Linux and macOS (`EnableWindowsTargeting`), so a broken
WPF or interop build is caught without Windows. Only Windows can run the app and the
Windows-only tests. CI: [.github/workflows/dotnet.yml](../.github/workflows/dotnet.yml).

Requires the .NET 10 SDK.

A restore that cannot reach the certificate revocation servers fails with NU3018,
because `nuget.config` requires signed packages. This happens in cloud sessions, whose
sandbox blocks those servers. There, run `export NUGET_CERT_REVOCATION_MODE=offline` in
the same shell before `dotnet`. It checks revocation against cached lists only, so never
set it in a workflow; CI's online check is the one that gates merges (12 7.8).

## Rules

- **Warnings are errors**, analyzers on (`Directory.Build.props`).
- **Package versions live only in `Directory.Packages.props`.** Restore is locked to the
  committed `packages.lock.json` files, from nuget.org only, with repository signatures
  required (`nuget.config`). After a package change, run
  `dotnet restore ShotAI.slnx --force-evaluate` and commit the lock files.
- **Banned APIs** are listed per project in `src/*/BannedSymbols.txt` (RS0030). An
  allowance is an `.editorconfig` section for exactly one file, and it lifts every ban
  in that file, so keep allowlisted files small (ARCHITECTURE 14.9).
- **P/Invoke comes from CsWin32:** add the API name to `src/ShotAI.Platform/NativeMethods.txt`.
  Don't hand-write `DllImport` signatures.
- **`contract/` is read in place, never copied.** It is byte-identical with the macOS repo,
  and a change to it must land there too (see the root README).
- **Never hand-edit `src/ShotAI.Core/Brand/BrandPalette.Generated.cs`.** After a change to
  `contract/brand.json`, run `dotnet run --project tools/ShotAI.GenBrand` here and
  `npm run gen:brand` from the repo root, and commit both tables with the contract.
- **Keep behavior identical to the Electron app** unless the spec marks an item as an
  improvement. The Electron source under `src/` is the reference until cutover.
