# shotAI native (C# / .NET 10 / WPF)

The native Windows rewrite of shotAI. It lives beside the Electron app in this repo and
is merged to `main` in small PRs. The Electron app keeps shipping from `main` until
cutover, when native releases as 2.0.0 and the Electron code is removed in one PR.

- Why and what: [docs/NATIVE-WINDOWS-FEASIBILITY.md](../docs/NATIVE-WINDOWS-FEASIBILITY.md)
- How, in order: [docs/native/PLAN.md](../docs/native/PLAN.md)
- Behavior to reproduce, subsystem by subsystem: [docs/native/spec/](../docs/native/spec/)

**Status:** foundations (WP-A1): analyzers, supply-chain rules, and Core's error,
threading and composition types. The shared conformance suite is wired in with its
round-trip cases skipped until the `project.json` codec lands.

## Layout

| Path | Target | Holds |
|---|---|---|
| `src/ShotAI.Core` | `net10.0` | Everything testable without Windows: the model and `project.json` codec, store logic, geometry, SOP request and apply logic, export templates, brand palette, settings schema. **Must not reference Windows APIs.** |
| `src/ShotAI.Platform` | `net10.0-windows10.0.19041.0` | Windows services: hooks, capture, UI Automation, display affinity, DPAPI, policy registry, OCR, WebView2 PDF host, libavif. |
| `src/ShotAI.App` | `net10.0-windows10.0.19041.0` | The WPF app (`shotAI.exe`): windows, views, view models, composition root. |
| `tests/ShotAI.Core.Tests` | `net10.0` | xunit.v3 tests for Core, including the shared `contract/conformance` suite. Runs on Linux and Windows. |

Windows 10 2004 (10.0.19041) is the minimum because it is the first build with
`WDA_EXCLUDEFROMCAPTURE`, which keeps shotAI's windows out of its own screenshots.

## Build and test

Run everything from this folder. `global.json` opts `dotnet test` into
Microsoft.Testing.Platform, and `dotnet` only finds it when started here or below.

```sh
dotnet build ShotAI.slnx -c Release
dotnet test --project tests/ShotAI.Core.Tests/ShotAI.Core.Tests.csproj -c Release   # any OS
dotnet test --solution ShotAI.slnx -c Release                                         # Windows: every test project
dotnet run --project src/ShotAI.App                                                   # Windows only
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
- **Keep behavior identical to the Electron app** unless the spec marks an item as an
  improvement. The Electron source under `src/` is the reference until cutover.
