# shotAI native implementation plan

> The executable plan that a sequence of Claude Code sessions follows to take the native Windows app from the scaffold in `dotnet/` to release 2.0.0 and the cutover from Electron. Design inputs, read in full: [`docs/NATIVE-WINDOWS-FEASIBILITY.md`](../NATIVE-WINDOWS-FEASIBILITY.md), [`docs/native/ARCHITECTURE.md`](ARCHITECTURE.md), the twelve subsystem specs under [`docs/native/spec/`](spec/), [`dotnet/README.md`](../../dotnet/README.md) and the scaffold (`dotnet/ShotAI.slnx`, `Directory.Build.props`, `Directory.Packages.props`, `global.json`, the four `.csproj` files, `NativeMethods.txt`, `CaptureExclusion.cs`, `App.xaml`, `MainWindow.xaml.cs`, the conformance harness, `.github/workflows/dotnet.yml`). Status: written against the verified specs and the architecture at scaffold commit `dcb4196`, and aligned on 2026-09-23 with the consolidated specs (every R-ARCH resolution of ARCHITECTURE and every correction of 1.5 is applied in the spec text). `docs/native/` is committed on the branch `claude/native-windows-rewrite-feasibility-8tpy3t` together with the scaffold; prerequisite P0 (section 9) is merging that branch's PR to `main` (Q-PLAN-3).

**Conventions.**

- IDs: `WP-A1` ... `WP-E9` are work packages; `M-A` ... `M-E` are milestones; `Q-PLAN-n` are this plan's open decisions. Every other ID (`AC-`, `INV-`, `D-`, `Q-`, `R-ARCH-`, `PB-`) is defined in the spec or architecture section it names and is never restated here.
- Electron code is cited as `path:line` (repo-relative), macOS code as `macOS:path:line` (the read-only repo at `/home/user/armadillon44/shotai_macos` in cloud sessions).
- `\u2014` inside a quoted string stands for the em dash the product string contains; this document never types the character.
- Classes: **REQUIRED** (parity), **IMPROVEMENT** (deliberate, justified), **ELECTRON-ONLY** (disappears; its replacement is named). The specs classify every behavior; this plan only sequences them.
- Commands are run from `dotnet/` unless they start with `npm` or `npx`, which run from the repository root.

---

## 1. Outcome and definition of done

### 1.1 Outcome

| # | Outcome | Evidence |
|---|---|---|
| O1 | shotAI 2.0.0 ships as one signed, dual-purpose MSI for x64 and ARM64 (per-user with no administrator rights when a person installs it, per-machine when Intune does), framework-dependent on the .NET 10 Desktop Runtime, deployed by Intune as a Win32 app | WP-E3, WP-E4, WP-E8; 12 7.1 to 7.6 |
| O2 | The native app has behavioral parity with Electron 1.3.0 (G1 of ARCHITECTURE 1.1), except the IMPROVEMENT and ELECTRON-ONLY items the specs list | section 7 (every AC met or waived), WP-E6 parity walk (AC-IPC-22) |
| O3 | Every shared file stays compatible: `project.json` through `contract/conformance`, `settings.json` layout and unknown keys, the log format, the brand stamp; rollback to Electron 1.3.x keeps working | AC-MODEL-1, AC-MODEL-3, AC-INFRA-3, AC-INFRA-8, AC-PKG-21 to AC-PKG-23 |
| O4 | No security invariant of ARCHITECTURE 1.4 and 9.2 (S1 to S21) is weaker than in Electron | the `[SECURITY]` ACs of every spec, WP-D17 end-to-end redaction, WP-E6 audit |
| O5 | Electron is removed from the fleet and from `main` in one cleanup PR, with 1.3.x kept as a rollback asset for 90 days | WP-E8, WP-E9; 12 7.13 |

### 1.2 What parity means

Parity is reached when all of these hold:

1. **Every acceptance criterion** in `docs/native/spec/*.md` section 9 and `docs/native/ARCHITECTURE.md` 12.10 (424 in total) is met, or is waived in 1.5 with a reason. Section 7 maps each AC to exactly one work package (AC-MODEL-36, whose own text splits its two clauses between WP-A3 and WP-A12, is the one exception); the AC is met when that WP's PR is merged and, for a manual AC, the result is recorded in the phase tracking issue (2.6).
2. **Every Electron test file** under `src/` (49) is ported, or is ELECTRON-ONLY with the reason and the native test that carries its intent (section 8).
3. **Every divergence** (`D-*` in each spec's section 7, `D-ARCH-*`) is implemented as specified, or recorded as changed in the owning spec with the reason.
4. **Every open question** (`Q-*`) is decided: either its recommended default was applied (the default is the decision unless someone decides otherwise before the PR that needs it, ARCHITECTURE 15.4) or a different decision is recorded in the owning spec's section 11. Section 6 names the WP that owns each one.
5. **Every performance budget** of ARCHITECTURE 11 has a recorded measurement on both reference machines (AC-ARCH-8).
6. **Every new native test** named in a WP's Tests row (`New: ...`) exists under that name and passes in the job for its project (Linux for `ShotAI.Core.Tests`, Windows for `ShotAI.Platform.Tests` and `ShotAI.App.Tests`); a test that is dropped or renamed is recorded in the owning spec's section 8 in the same PR (DoD7). Many REQUIRED behaviors are specified only by these tests, which no AC names.

### 1.3 Definition of done for one work package

| # | Condition |
|---|---|
| DoD1 | Merged to `main` through one PR, with `ci.yml` green and, when the PR touches a `dotnet.yml` path, every `dotnet.yml` job green |
| DoD2 | Every automated AC the WP owns passes in CI (Linux job for Core tests, Windows jobs for Platform and App tests) |
| DoD3 | Every manual AC the WP owns has a numbered script in the PR description and a recorded result (pass, or a linked issue) in the phase tracking issue |
| DoD4 | The Electron test files the WP ports (section 8) have a named C# test per TypeScript case, reviewed with a checklist in the PR (AC-MODEL-2 pattern) |
| DoD5 | The specs were corrected in the same PR wherever they proved wrong, and every new user-visible string or log line was added to its owning spec's table (2.4) |
| DoD6 | The WP's checkbox in section 9 is ticked, with the PR number |
| DoD7 | Every test class and named case in the WP's Tests row (`New: ...`) exists under that name and passes in the job for its project (Linux for Core.Tests, Windows for Platform.Tests and App.Tests); a test that is dropped or renamed is recorded in the owning spec's section 8 in the same PR (1.2 item 6) |

### 1.4 Cutover criteria (gate for WP-E8)

| # | Criterion | Source |
|---|---|---|
| CC1 | WP-A1 to WP-E7 are ticked in section 9 | this plan |
| CC2 | Every AC that section 7 maps to WP-A1 to WP-E7 is met or waived (1.5); none is "manual pending" (AC-PKG-14 and AC-PKG-27 are met by WP-E8 and WP-E9 themselves); every new test in the Tests rows of WP-A1 to WP-E7 exists and passes, or its removal is recorded (1.2 item 6, DoD7) | 1.2 |
| CC3 | Pilot exit criteria AC-PKG-29 are met: two weeks of daily use on both architectures, AC-CAP-6 passes, no open blocker, AC-PKG-20 to AC-PKG-24 pass | 12 9 |
| CC4 | The release candidate `2.0.0-rc.N` ran for at least one week in the wider group (S2) with no blocker and no rollback | 12 7.13.1 |
| CC5 | IT confirms: the two Win32 apps (x64, ARM64) with the .NET 10 Desktop Runtime dependency and requirement rules exist; WebView2 and the OCR Feature on Demand are present or deployed; the WAM redirect URI `ms-appx-web://microsoft.aad.brokerplugin/{ClientAppId}` is on the client registration (Q-AUTH-3); the Electron Intune app is re-wrapped with the 7.13.3 uninstall script | 12 7.5, 7.13.3; 08 Q-AUTH-3 |
| CC6 | Signing works on the release workflow and every PE and MSI verifies (AC-PKG-5) | 12 7.6 |
| CC7 | Rollback R2 was rehearsed once on a lab device: uninstall native, reinstall Electron 1.3.x, same projects and settings | 12 7.13.4 |
| CC8 | The 2.0.0 release notes open with the exact text of 12 7.12.3 | 12 7.12.3 |

### 1.5 Waivers and corrections

Waivers (a part of an AC that will not be met as written):

| AC | Part waived | Reason | What replaces it |
|---|---|---|---|
| AC-PKG-13 | `gh release view --json isPrerelease,isLatest` returns `true,false` | Whether `gh release view --json` accepts `isLatest` is UNVERIFIED (12 7.11.3, EDGE-PKG-58) | `gh release view <tag> --json isPrerelease` plus `gh api repos/{owner}/{repo}/releases/latest --jq .tag_name`, exactly as the `verify-published` job checks. Spec 12's AC-PKG-13 already carries this replacement check and names the waived form, so WP-E4 and WP-E7 do not edit the spec |

Note on AC-MODEL-36 (formerly a waiver row): spec 01 now states the R-ARCH-10 form itself. Its first clause (`ConformanceCase.cs` loads cases through `JsJson.Parse`) is met in WP-A3; its second clause (`Composition.ContainerTests.NoAsyncOnlyDisposables` and `LifecycleTests.ExitOrderMatchesSpec11`, replacing the earlier `ServiceProvider.DisposeAsync()` clause, ARCHITECTURE C5) is met in WP-A12. Nothing of it is waived any more.

Corrections (a record: each was an AC or spec detail outdated by a later resolution, and each was applied to the spec text in the 2026-09-23 consolidation, so no WP edits a spec to catch up):

| AC or spec text | Correction | Resolution | Applied in |
|---|---|---|---|
| AC-AUTH-21 | the cache path is `%LOCALAPPDATA%\LFI\shotAI\entra\msal-cache.bin`, not `%LOCALAPPDATA%\shotAI\entra\` | R-ARCH-13 (Q-PKG-4) | 08 7.6, 7.16 and AC-AUTH-21 (2026-09-23) |
| 09 7.7 and Q-EXP-20 (no AC names the path) | the WebView2 user data folder is `%LOCALAPPDATA%\LFI\shotAI\WebView2` | R-ARCH-13 | 09 7.7, D-EXP-3, Q-EXP-20 (2026-09-23) |
| AC-SHELL-24 | the About line's WebView2 version comes from Platform's `IWebView2RuntimeInfo`, the App has no WebView2 reference | R-ARCH-12, INV-ARCH-5 | 03 7.2 (`AboutText`), 7.11, D11 and AC-SHELL-24 (2026-09-23) |
| AC-SOP-6, AC-SOP-13 and 07 7.6 | the client factory is 08's `IAnthropicClientFactory`; 07's `ClaudeClientFactory`, `EgressPinHandler` and `ISopCredentialSource` are superseded | R-ARCH-15 | 07 7.1, 7.6, 7.8, AC-SOP-6 and AC-SOP-13 (2026-09-23) |
| every 09 AC naming `IProjectStore`, `RenderRefusedException`, `Brands.*` | `IProjectService`, `RenderGateException`, `BrandPalette.*` | R-ARCH-4, R-ARCH-8, R-ARCH-14 | 09 7.1 to 7.4 and 10 (2026-09-23) |
| 10 7.4.4 | `IAppPaths` gains `LocalDataDirectory` = `%LOCALAPPDATA%\LFI\shotAI` (ARCHITECTURE 10.2); AC-INFRA-35 now names the member | R-ARCH-13 | 10 7.4.4 and AC-INFRA-35 (2026-09-23) |
| 09 3.4 and 8.x (`Golden/Export/`) | the export goldens live under `Golden/export/css/` and `Golden/export/fixtures/` (golden subfolders are lower case, 2.7) | this plan | 09 3.4 and 8.x (2026-09-23) |
| 03 2.10.2 (`NavigateToString`) | the PDF engine allows only the initial navigation to the print copy's file URI | Q-EXP-17, ARCHITECTURE I-9 | 03 2.10.2 (2026-09-23) |
| 01 7.10 (S7, S9, `ProjectSessionTests`) and 05 7.3 (Back row); AC-ARCH-4 | a session disposed while writes are pending stays registered with `IProjectSettle` until every operation and durable call it accepted has finished, then unregisters; `WhenSettledAsync(path)` awaits every registered session for the path, open or draining (superseded: "unregisters at `DisposeAsync`") | R-ARCH-27 | 01 7.10 (`spec/01-model-store.md:1501`, S9 at `:1527`, `ProjectSessionTests` at `:1751`) and 05 7.3 Back row (`spec/05-report.md:1008`) (2026-09-23, after the consolidation); met in WP-A9 |

Milestone redefinition: the feasibility doc's phase C exit test "a redacted export provably contains no original pixels" needs the exports of phase D. Phase C proves it at the render (the only image the gate hands to any egress, S3 of ARCHITECTURE 9.2); the export and Claude-request proof (AC-EDIT-5, AC-EXP-34) is part of M-D (section 4, Q-PLAN-4).

---

## 2. How a session uses these docs

### 2.1 Read order

Every session, before writing code:

| Step | Read | Why |
|---|---|---|
| 1 | This plan: section 2, then the WP it will implement (section 3), its rows in sections 6, 7 and 8 | scope, dependencies, the ACs to meet, the tests to port, the risks to watch |
| 2 | `dotnet/README.md` | build rules (warnings are errors, central versions, CsWin32 only, `contract/` read in place) |
| 3 | `docs/native/ARCHITECTURE.md`: always 2 (layout and dependency rules), 4 (composition), 5 (MVVM), 6 (threading), 8.5 (never-log list), 9.5 (security checklist), 14 (conventions), 15.3 (cross-spec resolutions); plus any section the WP cites | the rules every PR is reviewed against; 15.3 wins over a spec that disagrees |
| 4 | The spec sections the WP lists under "Spec inputs", and in every case that spec's sections 3 (constants), 4 (invariants), 5 (edge cases), 8 (tests), 9 (ACs), 10 (interfaces) and 11 (open questions) | the normative behavior; section 2 of the spec is the Electron reference |
| 5 | The Electron source each cited spec row names (`path:line`) | the behavioral reference until cutover; when the spec and the code disagree about a REQUIRED behavior, the code is the reference and the spec is corrected (2.4) |
| 6 | The macOS code the spec cites (`macOS:path:line`), read only | how a native port already solved the same problem |
| 7 | For WP-D2 to WP-D8: the Anthropic C# SDK source (`anthropics/anthropic-sdk-csharp`, NuGet `Anthropic`, read only; `/home/user/anthropics/anthropic-sdk-csharp` in cloud sessions) | the SDK facts 07 and 08 rely on |

### 2.2 Picking the next work package

1. Take the first unticked WP in section 9, in document order, whose "Depends on" WPs are all merged (a WP that is merged but waiting only on manual ACs counts as merged for this rule) and that has no open PR. WPs in different lanes (3.0) may run in parallel sessions.
2. Claim it by opening a draft PR early, titled `native: WP-<id> <title>` (ARCHITECTURE 14.10). A second session seeing a draft PR for a WP picks another one.
3. If the WP turns out to be larger than its size (3.0) allows, stop, split it into `WP-<id>a` and `WP-<id>b` in this plan (both in section 3 and section 9, ACs divided in section 7), and implement only the first.
4. If the WP needs something a spec does not define, add it to the owning spec in the same PR (2.4), or record it as an open question there and take its recommended default.

### 2.3 Branch and PR rules

| # | Rule |
|---|---|
| B1 | Short-lived branch off `main`, named `native/wp-<id>-<slug>` (for example `native/wp-a3-codec`), rebased on `main` before merge; no long-running feature branches |
| B2 | One work package per PR. A PR may also carry the spec corrections, plan updates and Electron golden-generator changes that WP needs; nothing else |
| B3 | Both CI workflows green: `ci.yml` (Electron, runs on every PR) and `dotnet.yml` (runs when a path in its filter changes: today `dotnet/**`, `contract/**` and the workflow; WP-A4 adds `.gitattributes`, WP-A14 adds `assets/**` and `src/renderer/fonts/**`, per 12 7.11.2). The `dotnet.yml` jobs are not required checks while path filters exist (12 EDGE-PKG-35), so the author checks them by hand |
| B4 | `main` builds and passes after every merge: `dotnet build ShotAI.slnx -c Release` on Linux and Windows, and the app still launches (from WP-A12 on). A WP that cannot land without breaking something is split (2.2 step 3) |
| B5 | No `contract/` change without the same change in the macOS repo (`Armadillon44/shotAI_MacOS`): open the macOS PR first, link it from this PR, and merge both the same day; a conformance case change also runs in both harnesses |
| B6 | Electron code changes only in two cases: (a) a parity fix, which is made in both apps (Electron and native, and macOS when it shares the bug), each with a test, released under the Electron freeze rules (Q-PKG-27); (b) the golden generators of 2.7 (test-only, or the one env-gated dev run mode of Q-PLAN-1), which change no shipped behavior |
| B7 | Commit messages and the PR description end with the attribution lines the session is given; the PR description follows 2.8 |
| B8 | No secrets, tenant, organization, client, rule, service account or workspace ids, and nothing from a `*.local.json` file in any commit, test, golden or log excerpt (public repository; ARCHITECTURE 1.3) |

### 2.4 What to update in the same PR

| Update | When |
|---|---|
| Section 9 of this plan: tick the WP's box and add the PR number; if manual ACs are still pending, write `merged in #NN, manual pending: AC-...` and tick only when they are recorded | every WP PR |
| Section 3 of this plan: the WP's deliverables if a type or file moved, with a one-line reason | when the implementation differs from the plan |
| The owning spec: a correction with the reason, marked `Corrected in WP-<id>: <reason>` next to the corrected text | when the Electron source, a measurement or the platform proved the spec wrong |
| The owning spec's section 11: `Decided in WP-<id>: <decision>` under the open question | when the WP settles an open question (default or not) |
| The owning spec's string and log tables | a new user-visible string or log line |
| `docs/native/ARCHITECTURE.md` 15.3 or 15.4 | a new cross-spec conflict and its resolution, or an architecture-level decision |
| `THIRD-PARTY-NOTICES.txt` (after WP-E2) | a new NuGet package or native library (V10) |
| Section 6 of this plan | a new risk found while implementing |

### 2.5 Commands

Run from `dotnet/` (the `global.json` there opts `dotnet test` into Microsoft.Testing.Platform, and `dotnet` finds it only from that folder or below):

| Purpose | Command | Where |
|---|---|---|
| Build everything | `dotnet build ShotAI.slnx -c Release` | Linux, Windows |
| Core tests (includes conformance, goldens, source guards) | `dotnet test --project tests/ShotAI.Core.Tests/ShotAI.Core.Tests.csproj -c Release` | Linux, Windows |
| One test class | `dotnet test --project tests/ShotAI.Core.Tests/ShotAI.Core.Tests.csproj -c Release -- --filter-class "ShotAI.Core.Tests.Conformance.ConformanceTests"` (xunit.v3 filter option; whether the `--` separator is needed with the .NET 10 SDK is UNVERIFIED, try without it first) | Linux, Windows |
| Exclude performance tests | add `-- --filter-not-trait "Category=Perf"` (same caveat) | Linux, Windows |
| Every test project | `dotnet test --solution ShotAI.slnx -c Release` | Windows only |
| Run the app | `dotnet run --project src/ShotAI.App` | Windows only (from WP-A12) |
| Self-test | `dotnet run --project src/ShotAI.App -- --selftest` (also `--capture-selftest`, `--update-selftest`) | Windows (from WP-A12, WP-B9, WP-E1) |
| Brand table check | `dotnet run --project tools/ShotAI.GenBrand -c Release -- --check` | Linux, Windows (from WP-A4) |
| Regenerate the brand table | `dotnet run --project tools/ShotAI.GenBrand` | after a `contract/brand.json` change |
| Notices check | `dotnet run --project tools/ShotAI.Release -c Release -- notices --check` | from WP-E2 |
| Publish one architecture (public variant) | `dotnet publish src/ShotAI.App/ShotAI.App.csproj -c Release -r win-x64 --self-contained false -p:ShotAIFederationFile= -o artifacts/publish/win-x64` | Windows (from WP-E2) |
| Verify a payload | `dotnet run --project tools/ShotAI.Release -- verify-payload artifacts/publish/win-x64 --arch x64 --public` | from WP-E2 |
| Regenerate lock files after a package change | `dotnet restore ShotAI.slnx --force-evaluate` | Linux, Windows (from WP-A1) |
| Electron suite (repository root) | `npm ci --ignore-scripts` with `ELECTRON_SKIP_BINARY_DOWNLOAD=1`, then `npm test` and `npm run gen:brand:check` | Linux (the `ci.yml` pattern) |
| Electron golden generators (repository root) | `SHOTAI_CODEC_GOLDENS=1 npx vitest run src/main/codec-golden.test.ts` and the others of 2.7 | Linux or Windows, as each says |

### 2.6 Linux sessions, Windows CI and manual acceptance

Cloud sessions run on Linux. They build the whole solution (EnableWindowsTargeting) and run `ShotAI.Core.Tests`; they cannot run the app, the Platform tests or the App tests.

| Situation | What the session does |
|---|---|
| Core logic | writes it with Linux tests; this is the default place for every rule (ARCHITECTURE 2.3, 12.2) |
| Restore fails with NU3018 (the sandbox cannot reach certificate revocation servers) | runs `export NUGET_CERT_REVOCATION_MODE=offline &&` before each `dotnet` command in that shell, never in a workflow (12 7.8) |
| Windows-only code (Platform, App) | writes it with its Platform.Tests or App.Tests cases, pushes, and reads the Windows job results; iterates on CI failures; never marks a Windows test as skipped to get green (a capability skip must say why, ARCHITECTURE 12.9) |
| Manual AC | writes a numbered script in the PR description (preconditions, steps, expected result, the exact strings to compare) and lists the AC as pending in section 9. A person (the maintainer, or the IT tester for Intune items) runs it on the reference machines and records the result in the phase tracking issue: pass, or fail with an issue link. A failed manual AC reopens the WP (a follow-up PR on the same WP id) |
| Side-by-side AC | follows the matching procedure of section 4 |
| Phase tracking issue | one GitHub issue per phase (`native phase A: model and viewer`, ...), created by the first WP of the phase; it holds manual results, measurements (machine, build commit, numbers), decisions taken and the milestone checklist of section 4. No real screenshots or data (public repository) |

### 2.7 Electron-side and cross-repository changes this plan schedules

| Change | Kind | WP | Rule |
|---|---|---|---|
| `src/main/codec-golden.test.ts` writing `dotnet/tests/ShotAI.Core.Tests/Golden/codec/` when `SHOTAI_CODEC_GOLDENS=1` | test-only generator (Q-MODEL-18) | WP-A3 | skipped in normal `npm test`; outputs committed; synthetic inputs only |
| `src/shared/redact-detect-golden.test.ts` writing `Golden/redact/` when `SHOTAI_REDACT_GOLDENS=1` | test-only generator (Q-EDIT-15, AC-EDIT-24) | WP-C11 | same |
| `src/main/entra/federation-golden.test.ts` writing `Golden/auth/` when `SHOTAI_FEDERATION_GOLDENS=1` | test-only generator (AC-AUTH-3) | WP-D1 | placeholder ids only (08 8 fixtures) |
| base prompt golden from `src/main/claude-service.ts:181-199`, request fixture comparison script | one-off, prompt golden committed, script not committed (AC-SOP-2, AC-SOP-3) | WP-D5 | |
| `SHOTAI_EXPORT_GOLDENS=<fixtures root>` run mode in the Electron main process | dev-only run mode (Q-EXP-5); needs Electron itself because the builders use `nativeImage` | WP-D9 | Q-PLAN-1: allowed as an env-gated switch that changes nothing unless the variable is set; its PR is Electron-only plus the committed outputs |
| Electron report CSS derived from the export widths (`.rep`, `.rep__bodywrap`) | optional parity fix (Q-REP-2) | decided at M-A (WP-A20) | if adopted: an Electron PR with a test, before the pilot, so pilot users see one report width |
| `contract/conformance/README.md:61-62` harness list gains the native harness | contract change | WP-E9 | both repositories (B5) |

Golden subfolders of `dotnet/tests/ShotAI.Core.Tests/Golden/` are lower case (`codec/`, `redact/`, `auth/`, `sop/`, `export/`, `macos-fixture/`): the Linux job's file system is case-sensitive, so one mixed-case reference fails there only.

### 2.8 PR description template

```
native: WP-<id> <title>

Implements: <spec sections>, <INV/D ids>
Acceptance criteria met here: <AC ids> (automated) / <AC ids> (manual, script below)
Electron tests ported: <files> (checklist of TS cases -> C# test names)
Spec corrections: <file section: what and why> (or none)
Open questions decided: <Q ids and decision> (or none)
Deviations from this plan: <what and why> (or none)
Security checklist (ARCHITECTURE 9.5): items 1 to 9 answered
Manual script:
  1. ...
<attribution lines>
```

---

## 3. Phases and work packages

### 3.0 How the work packages are cut

**Sizes.** S: one session, roughly up to 500 lines of product code plus tests. M: one or two sessions, roughly up to 1,500 lines. L: larger, and must be split (2.2 step 3); no WP in this plan is L. Calendar-bound WPs (the pilot) are sized by their code, with the duration stated.

**Ordering principles.**

1. Every PR leaves `main` building and green (B4).
2. Guardrails and foundations first (ARCHITECTURE 15.6): analyzers, errors, threading, then the JSON semantics and the `project.json` codec, which turns the scaffold's skipped conformance round trips into passes (WP-A3), and the C# brand generator with its CI check (WP-A4).
3. The app launches early and grows: from WP-A12 `shotAI.exe` starts; WP-A15 has the real main window and menu; WP-A16 lists projects; WP-A17 opens them. Each later WP adds a visible capability to the running app.
4. Core first, then the Windows adapter, then the view: a WP that needs a Windows API is preceded by the Core WP that holds its rules, so the rules are tested on Linux before any Windows code exists (ARCHITECTURE 12.2).
5. The riskiest unknowns are measured before the code that depends on them: the display-affinity probe (WP-B5) before the pill and overlay (WP-B7, WP-B8); the popup registration tests (WP-A13) before any popup; the MSAL cache behavior (WP-D3) before the Settings AI tab (WP-D7); the hidden WebView2 print (WP-D12) before the PDF UI.

**Lanes.** WPs in different lanes can run in parallel sessions once their dependencies are merged.

| Lane | Work packages (in order) | Earliest start |
|---|---|---|
| Core model | A1, A2, A3, A5, A6, A7, A8, A9 | now |
| Core infrastructure | A4, A10, A11 | after A1 |
| App shell | A12, A13, A14, A15, A16, A17, A18, A19, A20 | after A6, A10, A11 |
| Capture | B1 to B11 | B1 after A3 and A10; B4 after A5 |
| Editing | C8, C5, C6, C7 (Core) | C8 after A3; C6 after C8; C5 after A7; C7 after C5, C6, C8 and A9 |
| Report editing and editor UI | C1 to C4, C9 to C13 | after A17 |
| Auth | D1, D2, D3, D4 | after A12; D4 also after A19 |
| SOP | D5 to D8 | D5 after A9, A10 and C7 |
| Export | D9 to D16 | D9 after A17 and C7 |
| Release | E2, E3, E5 | E2 after A12 and D1 |

**Dependency summary.**

```
Phase A   A1 -> A2 -> A3 ;  A1 -> A4 ;  A1 -> A5 ;  A1 -> A11
          A3, A4, A5 -> A6 -> A7 ;  A6, A7 -> A8 ;  A6 -> A9 ;  A2, A4, A5 -> A10
          A6, A10, A11 -> A12 -> A13 ;  A4, A12 -> A14 ;  A13, A14 -> A15
          A15, A8 -> A16 ;  A16, A9 -> A17 -> A18 ;  A16, A13 -> A19 ;  A1..A19 -> A20
Phase B   A3, A10 -> B1 -> B2 (and A7) -> B3 ;  B1, A5 -> B4 ;  B1, A13 -> B5
          B1, B2, B4, B5 -> B6 ;  B3, B5, A15 -> B7 ;  B5, A15 -> B8
          B4, B6, B7, B8, A17, A19 -> B9 ;  A19, A14, B5 -> B10 ;  B1..B10 -> B11
Phase C   A9, A17 -> C1 -> C2 (and A19) -> C3 -> C4 (and B9)
          A7 -> C5 ;  A3 -> C8 -> C6 ;  C5, C6, C8, A9 -> C7 ;  C8, C2 -> C9
          C9, C7, B5 -> C10 -> C11 ;  C7, C2 -> C12 ;  C1..C12 -> C13
Phase D   A12 -> D1 ;  A10, A12 -> D2 -> D3 (and A5) ;  D1, D2, D3, A19 -> D4
          A9, A10, C7 -> D5 -> D6 (and D2) ;  D4, B10 -> D7 ;  D6, D7, C2 -> D8
          A17, A4, C7 -> D9 -> D10 (and C2) -> D11, D12 (and A15), D13 -> D14
          D9, A7, C2 -> D15 ;  D10, A19 -> D16 ;  D1..D16 -> D17
Phase E   D2, A19, B10 -> E1 ;  A12, D1 -> E2 -> E3 -> E4 (and D11, E1)
          A12, D3, D12, E1, E3 -> E5 ;  A20, B11, C13, D17, E1, E3, E5 -> E6 -> E7 -> E8 -> E9
```

---

### 3.1 Phase A: model and viewer

Goal (feasibility "Phased plan"): the C# `project.json` codec, the store with atomic writes and reparse-safe confinement, archive, the brand generator, settings and logging, the app host, and a Home list plus a read-only report that open projects written by both existing apps.

#### WP-A1. Guardrails and Core foundations

| Field | Content |
|---|---|
| Goal | Every later PR starts with the analyzers, the supply-chain rules and the three foundation namespaces every spec derives from (ARCHITECTURE 15.6 items 1 and 2) |
| Spec inputs | ARCHITECTURE 2.2 (INV-ARCH-1 to INV-ARCH-6), 3.1 (V1 to V10), 3.2, 8.1, 8.2, 14.9, 15.6; 11 7.2, 7.3.1, 7.4 (channel map), 7.9, 7.12, 8.2; 12 7.8 (INV-PKG-20); Q-IPC-9, Q-IPC-12, Q-IPC-19, Q-PKG-14, Q-PKG-29, Q-ARCH-5 |
| Deliverables | `dotnet/.editorconfig` with the ARCHITECTURE 14.9 severities in sections `[src/ShotAI.Core/**.cs]`, `[src/ShotAI.Platform/**.cs]` and `[src/ShotAI.App/**.cs]` (14.9 gives no severities for tests or tools), a `[{tests,tools}/**.cs]` section setting `VSTHRD200`, `CA2007` and `RS0030` to `none`, and the `*.g.cs` suppression; `dotnet/Directory.Packages.props` gains exact versions of `Microsoft.VisualStudio.Threading.Analyzers`, `Microsoft.CodeAnalysis.BannedApiAnalyzers`, `Microsoft.Extensions.Logging.Abstractions`, `Microsoft.Extensions.DependencyInjection.Abstractions`, `Microsoft.Extensions.TimeProvider.Testing`; `dotnet/Directory.Build.props` gains `RestorePackagesWithLockFile`, `RestoreLockedMode` when `CI` is `true`, `NuGetAudit`, `NuGetAuditMode` `all`, `WarningsNotAsErrors` `NU1901;NU1902` with `Condition="'$(ShotAIStrictAudit)' != 'true'"` (Q-PKG-14, 12 7.2.1; PR CI keeps low and moderate advisories as warnings, and WP-E4's release workflow passes `-p:ShotAIStrictAudit=true` so they fail there, ARCHITECTURE 3.1 V5) and the two analyzers (`PrivateAssets="all"`) for every project except `tools/ShotAI.GenBrand`, which stays BCL only with no package reference (10 7.3; `Condition="'$(MSBuildProjectName)' != 'ShotAI.GenBrand'"`); `dotnet/nuget.config`: `<clear/>`, the nuget.org source, `<packageSourceMapping>` with pattern `*`, `<config><add key="signatureValidationMode" value="require"/></config>` and `<trustedSigners><repository name="nuget.org" serviceIndex="https://api.nuget.org/v3/index.json">` with the nuget.org repository certificate fingerprints (`hashAlgorithm="SHA256"`, the current certificate and the announced next one, `allowUntrustedRoot="false"`, values taken from the NuGet documentation at implementation time, never guessed), because `require` with no trusted signer rejects every package (NU3034) (12 7.8); committed `packages.lock.json` per project; `BannedSymbols.txt` in `src/ShotAI.Core`, `src/ShotAI.Platform`, `src/ShotAI.App` with the ARCHITECTURE 14.9 lists (Core bans `Math.Round` with `JsMath.cs` allowlisted, Q-ARCH-5); Core `ShotAI.Core.Errors` (`ShotAIException`, `UserMessage` with `From` and `Generic`), `ShotAI.Core.Threading` (`IUiDispatcher`, `EventRaiser`, `IAppLifetime`), `ShotAI.Core.Composition.CoreServiceCollectionExtensions.AddShotAICore` (empty); test support `tests/ShotAI.Core.Tests/Support/ManualUiDispatcher.cs`, `ThreadUiDispatcher.cs`, `CapturingLoggerProvider.cs`; `tests/ShotAI.Core.Tests/ServiceBoundary/channel-map.json` (the 87 rows of 11 7.4); `.github/dependabot.yml` (nuget under `/dotnet`, github-actions, weekly, V6); `.gitignore` gains `dotnet/artifacts/` and `dotnet/federation.local.json` (12 7.2.1) |
| Tests | No Electron file. New: `Architecture/CoreReferencesTests`, `Errors/UserMessageTests`, `Threading/EventRaiserTests`, `Threading/UiDispatcherContractTests` (against both fakes), `ServiceBoundary/ChannelInventoryTests` (including `MatchesElectronWhileItExists` against `src/shared/ipc.ts`). One deliberate violation per banned list and per analyzer rule, shown failing in the PR description, then removed (AC-ARCH-3) |
| Acceptance criteria | AC-MODEL-26, AC-IPC-1, AC-IPC-16, AC-ARCH-1, AC-ARCH-3 |
| Depends on | none (P0 merged) |
| Size | M |
| Risks and de-risking | An `M:` banned entry without a parameter list may not ban every overload in the pinned analyzer (UNVERIFIED, 11 7.12): prove it with the violation and list overloads explicitly if needed. Locked restore and signature validation can fail on a package that is not repository-signed: pin its author certificate as a trusted signer (Q-PKG-29), never disable validation. `CA2007`, `VSTHRD200` and the banned lists must not fire in test or tool projects: the severities are scoped to the three `src/` projects and the `[{tests,tools}/**.cs]` section turns those rules off; if another rule proves noisy in the first test PR, add it there with a reason in ARCHITECTURE 14.9 |
| Demo | `dotnet build ShotAI.slnx -c Release` green on Linux; `dotnet restore ShotAI.slnx --locked-mode` passes on the Linux and Windows jobs; a scratch commit adding `Math.Round(1.5)` to Core fails with `RS0030` |

#### WP-A2. ECMAScript JSON semantics

| Field | Content |
|---|---|
| Goal | Core reads what `JSON.parse` reads and writes exactly the bytes `JSON.stringify` writes (interpretation I-1, Q-MODEL-2) |
| Spec inputs | 01 2.7, 2.8, 7.2 (7.2.1 reader, 7.2.2 writer and the `ToJsString` table, 7.2.3 helpers), D-1, D-2, D-4, D-21, D-26, EDGE-MODEL-28 to EDGE-MODEL-32, EDGE-MODEL-55; 02 INV-CAP-18; ARCHITECTURE 14.5, R-ARCH-1; Q-MODEL-4 |
| Deliverables | `src/ShotAI.Core/Json/`: `JsJson` (`Parse(ReadOnlySpan<byte>)`, `Parse(string)`, `Stringify(JsonNode?, int indent = 2)`, `MaxDepth = 1000`), internal unescaper that keeps lone surrogates, own-key ordering (`JsOrder`), `JsJsonException`, `JsNumber.ToJsString`, `JsMath` (`Round`, `ClampIndex`; the one file allowed to call `Math.*` rounding), `JsString.Trim`, `IsoTime` (`ToIsoString`, `TryParseJsDate`) |
| Tests | No Electron file. New: `Json/JsNumberTests` (the 7.2.2 table and the 10,000-sample round-trip property), `Json/JsJsonWriterTests`, `Json/JsJsonReaderTests`, `Json/JsHelpersTests`, `Json/JsMathTests` (the 02 8.4 values; `NoMathRoundInCapture` is added in WP-B1) |
| Acceptance criteria | AC-MODEL-4, AC-MODEL-5 |
| Depends on | WP-A1 |
| Size | M |
| Risks and de-risking | `Utf8JsonReader.TryGetDouble` on `1e400` is undocumented: the `double.Parse` fallback covers both outcomes and a test pins it. `"R"` shortest digits on .NET 10 is assumed: the property test proves round trip. Never call `reader.GetString()` (rejects lone surrogates) |
| Demo | `JsJson.Stringify(JsJson.Parse("{\"b\":1,\"10\":2,\"9\":3}"))` in a test prints `9, 10, b` order |

#### WP-A3. Model, codec and conformance

| Field | Content |
|---|---|
| Goal | The native codec decodes and re-encodes `project.json` byte for byte like Electron's `coerceManifest` plus `JSON.stringify`; the scaffold's skipped conformance round trips pass |
| Spec inputs | 01 1, 2.1 to 2.8, 2.12, 2.13 (codec lines), 3, 4 (codec invariants), 7.1, 7.3, 7.4, 7.11, 7.15 (D-3, D-16, D-26), 8.1, 8.2 (codec rows), EDGE-MODEL-10, EDGE-MODEL-33, EDGE-MODEL-58; `contract/conformance/README.md`; Q-MODEL-1, Q-MODEL-3, Q-MODEL-14, Q-MODEL-18 |
| Deliverables | `src/ShotAI.Core/Model/`: `ProjectManifest`, `ProjectStep` (wraps the raw `JsonObject`; typed lenient views whose setters keep key position), `SopIntro`, `SopBackup`, `StepClick`, `CapturedWindow`, `CapturedMonitor`, `StepElement`, `CaptureTarget`, `Rect`, `Point`, `CalloutKinds`, `CalloutGlyphs`, `StepNumbering`, `StepList`, `StepGeometry` (`ParseRect`, `ParsePoint`; not `Geometry`, which would clash with the namespace `ShotAI.Core.Geometry` and fail with CS0234, 01 7.1), `ProjectTitles`; `src/ShotAI.Core/Geometry/DocScale.cs` with `Clamp` only; `src/ShotAI.Core/Codec/ManifestCodec.cs` (exactly `FileName`, `Decode`, `NormalizeSteps`, `CoerceIntro`, `CoerceSopBackup`, `Encode`, `Serialize` and `Read`, the authoritative member list of 01 7.4; `Encode` returns the `JsonObject` that `JSON.stringify` sees, and there is no `ToJsonNode`), `ManifestKeys.cs` (`All`, the 15 names); `ManifestCorruptException` (namespace `ShotAI.Core.Store`, 01 7.13, deriving from `ShotAIException`, ARCHITECTURE 8.1), which `Decode` throws on a null root and `Read` throws around a parse error; the harness: `Round_trips_through_the_codec` unskipped with the algorithm of `src/main/conformance.test.ts:70-139`, `ConformanceCase.Load` through `JsJson.Parse`, the `ExpectPath.Canonical` fix for `-0`, open cases reported through the diagnostic message; `tests/ShotAI.Core.Tests/xunit.runner.json` with `"diagnosticMessages": true`, copied to the output (or `TestContext.Current.AddWarning` if that is what the pinned xunit.v3 surfaces under Microsoft.Testing.Platform): confirm the line appears in the `dotnet.yml` Linux job log and record which mechanism worked in 01 7.11 (`Decided in WP-A3`); `tests/ShotAI.Core.Tests/Golden/macos-fixture/b7e2c4d1-9f3a-4e8b-a2c5-6d1f8e9a0b3c/` (the whole `macOS:Fixtures/b7e2c4d1-9f3a-4e8b-a2c5-6d1f8e9a0b3c/` folder, byte-identical: `project.json`, `shots/step-0001.png` to `step-0003.png`, `export/.render/9a1b3c5d-7e2f-4b8a-9c1d-2e4f6a8b0c3e.png`, about 790 KB) with a README naming `f445bca` (Q-MODEL-14); `Golden/codec/` inputs and Electron outputs; `.gitattributes`: `dotnet/tests/ShotAI.Core.Tests/Golden/**/*.json text eol=lf` and the same for `*.css`, `*.html`, `*.md` and `*.txt`, plus `dotnet/tests/ShotAI.Core.Tests/Golden/**/*.<ext> binary` for each of `png`, `jpg`, `jpeg`, `avif`, `pdf`, `docx`, `pptx` and `zip` (an explicit `text` would turn off binary detection and rewrite bytes inside those files); Electron test-only generator `src/main/codec-golden.test.ts` (2.7) |
| Tests | Port `src/shared/project.test.ts` (`Model/StepGeometryTests`, `Model/CalloutKindTests`), `src/main/conformance.test.ts` (`Conformance/ConformanceTests`, `ExpectPathTests`), `src/main/normalize-steps.test.ts` (`Codec/NormalizeStepsTests`), `src/main/unknown-callout.test.ts` (`Model/CalloutKindTests`, `Model/StepNumberingTests`), `src/main/section-callout.test.ts` (`Codec/SectionCalloutTests`), and the codec cases of `src/main/manifest-extras.test.ts` (`Codec/ManifestExtrasTests`; its store cases land in WP-A6). New: `Codec/DecodeTableTests` (including `NullRootThrowsManifestCorrupt`), `RootShapeTests`, `SopBackupTests`, `StepPassThroughTests`, `KeyOrderTests`, `ElectronGoldenTests`, `RegexAnchorTests`, `Model/JsonObjectPositionTests` |
| Acceptance criteria | AC-MODEL-1, AC-MODEL-3, AC-MODEL-6, AC-MODEL-7, AC-MODEL-10, AC-MODEL-32, AC-MODEL-36 first clause (its second clause is WP-A12's, 1.5) |
| Depends on | WP-A2 |
| Size | M |
| Risks and de-risking | A typed decode that drops unknown step keys loses data silently (01 risk): `ProjectStep` stays a raw-object wrapper and `JsonObjectPositionTests` (AC-MODEL-10) is written first. The Emoji_Presentation list in `CalloutKindTests` is transcribed from Unicode `emoji-data.txt` and must be checked against the file. Golden generation needs the Electron dependencies: `npm ci --ignore-scripts`, then `SHOTAI_CODEC_GOLDENS=1 npx vitest run src/main/codec-golden.test.ts` |
| Demo | the Linux job log shows 7 agreed cases passing and `display-scale-out-of-range [open]` reporting its divergence |

#### WP-A4. Brand generator and palette

| Field | Content |
|---|---|
| Goal | A third generator reads `contract/brand.json` in place and writes a checked-in C# table with the same contract stamp; CI fails when the table is stale |
| Spec inputs | 10 2.1 to 2.5, 7.1, 7.2, 7.3, 7.12, 8.1, 8.2, 8.5 (generator, contract, parity and palette rows), INV-INFRA-1 to INV-INFRA-9, EDGE-INFRA-41 to EDGE-INFRA-48; 09 8.1 brand-narrowing additions; R-ARCH-14; Q-INFRA-17 |
| Deliverables | `dotnet/tools/ShotAI.GenBrand/ShotAI.GenBrand.csproj` (BCL only, no project or package reference), `Program.cs`, public `BrandGenerator` (`ContractHash`, `Generate`, `OutputRelativePath`), `GenBrandException`; `src/ShotAI.Core/Brand/`: `Palette`, `BrandRadii`, `BrandFont`, `BrandDefinition`, `PaletteRoles`, `ContrastMath`, `BrandPalette` (hand-written helpers `IsBrandId`, `CoerceBrand`, `PinnedBrand`, `PinIsUnrecognised`, `For`, `Get`, `HexNoHash`, `CssFontStack`, `CardRadiusPx`, `ImageRadiusPx`, `RetiredGreys`) and the generated `BrandPalette.Generated.cs`; `ShotAI.slnx` `/tools/` folder; `ShotAI.Core.Tests.csproj` references the tool and links `BrandPalette.Generated.cs` and `src/shared/brand-colors.generated.ts`; `.gitattributes` line `dotnet/src/ShotAI.Core/Brand/BrandPalette.Generated.cs text eol=lf`; `dotnet.yml` Linux job step `Brand table is current` before Build, and `.gitattributes` added to the path filter |
| Tests | Port `src/shared/brand-narrowing.test.ts` (`Brand/BrandNarrowingTests`, with the full 2.4 decision table and 09's native additions) and the Core cases of `src/shared/theme-palette.test.ts` (`Brand/BrandPaletteTests`; its CSS cases are ELECTRON-ONLY and their intent lands in WP-A14). New: `Brand/GenBrandTests`, `BrandContractTests`, `BrandParityWithElectronTests` |
| Acceptance criteria | AC-INFRA-1, AC-INFRA-2, AC-INFRA-3, AC-INFRA-4, AC-INFRA-5, AC-INFRA-6, AC-INFRA-33 |
| Depends on | WP-A1 |
| Size | M |
| Risks and de-risking | Referencing an executable from the Microsoft.Testing.Platform test executable may not work (10 verifier note): fall back to a small `net10.0` library `ShotAI.GenBrand.Core` referenced by both, and record it in 10 7.3. CRLF checkouts: run the check on the Windows job with `core.autocrlf=true`. AC-INFRA-3's macOS stamp is compared by hand against `macOS:Scripts/gen-brand.swift` output |
| Demo | editing one colour in a scratch copy of the contract makes `--check` print `gen-brand: BrandPalette.Generated.cs is STALE.` and exit 1 |

#### WP-A5. Atomic file, write queue and path confinement

| Field | Content |
|---|---|
| Goal | The three primitives every writer depends on, with the Windows reparse-point behavior proven on a Windows runner; the Windows Platform test project exists |
| Spec inputs | 01 2.10, 2.11, 7.5, 7.6, 7.7, INV-MODEL-13 to INV-MODEL-16, INV-MODEL-33, INV-MODEL-34, D-5, D-6, D-15, EDGE-MODEL-13, EDGE-MODEL-20; ARCHITECTURE 7.10, 9.2 S12; 12 Q-PKG-12; Q-MODEL-5, Q-MODEL-8 |
| Deliverables | Core `ShotAI.Core.Store`: `AtomicFile` (tmp `<file>.<pid>.tmp`, `Flush(flushToDisk: true)`, `File.Move(overwrite: true)`, retries at 10, 25, 50, 100, 200, 350, 600 ms through `TimeProvider`, `onRetry`, tmp removed on failure), `IRenameRetryClassifier` and its managed default, `SerialWriteQueue` (`Channel` with one consumer, FIFO, cancellation before start only, `DrainAsync`), `PathConfine` (`Confine`, `ConfineNoLinks`, `HasHostileSegment`), `IPathProbe`, `ManagedPathProbe`, `ReparseSafeDelete`; Platform `ShotAI.Platform.FileSystem`: `WindowsPathProbe` (name-surrogate reparse bit), `WindowsRenameRetryClassifier` (mapping checked against libuv `src/win/error.c`); new project `tests/ShotAI.Platform.Tests` (`net10.0-windows10.0.19041.0`, xunit.v3) in `ShotAI.slnx`; `dotnet.yml` windows job becomes the x64 plus arm64 matrix of 12 7.11.2 (`windows-11-arm`; if the label is unavailable, keep x64 only and record the manual ARM64 fallback in section 6, Q-PKG-12) |
| Tests | Port `src/main/path-confine.test.ts` (`Store/PathConfineTests`, including the Windows-only rows) and `src/main/path-confine-symlink.test.ts` (`Store/PathConfineSymlinkTests` on Linux with real symlinks, `Platform.Tests/FileSystem/JunctionConfineTests` with `mklink /J`). New: `Store/AtomicFileTests`, `Store/SerialWriteQueueTests`, `Store/HostileSegmentTests`, `Platform.Tests/FileSystem/WindowsPathProbeTests`, `RenameRetryClassifierTests` |
| Acceptance criteria | AC-MODEL-11, AC-MODEL-14, AC-MODEL-27, AC-MODEL-30 |
| Depends on | WP-A1 |
| Size | M |
| Risks and de-risking | A confinement mistake is a security regression that Linux CI cannot see (01 risk): the junction cases must execute on the runner, not skip (AC-MODEL-11 says so). `File.Move` flags and the Win32 to errno mapping are unverified: `RenameRetryClassifierTests` measures real sharing violations |
| Demo | the Windows job log lists `JunctionConfineTests` executed and passed |

#### WP-A6. Project store: projects

| Field | Content |
|---|---|
| Goal | `ProjectStore` gates, lists, searches, creates, opens, renames, deletes, mutates and sets per-project values with Electron's persistence semantics (01 2.9.3) |
| Spec inputs | 01 2.9.1 to 2.9.8, 2.9.10, 2.9.11, 2.13, 7.8, 7.12 to 7.14, INV-MODEL-17 to INV-MODEL-32, D-3, D-7, D-8, D-11, D-16, D-18, D-20, D-24; 11 7.3.2 (`IProjectService`, `ProjectsChanged`); ARCHITECTURE 7.3, R-ARCH-4, R-ARCH-10; Q-MODEL-9, Q-MODEL-15, Q-MODEL-16, Q-MODEL-19, Q-MODEL-20, Q-IPC-3, Q-IPC-5 |
| Deliverables | Core `ShotAI.Core.Store`: `IProjectService` (the members that exist after this WP; later WPs add theirs), `ProjectStore : IProjectService, IDisposable, IAsyncDisposable` (the 01 7.8 constructor without its `IStepRenderWriter renderWriter` parameter, which WP-C5 adds together with `UpdateStepAsync` and `MergeStepsAsync`, its only users, so no WP depends on a later one; known-project gate, `ListProjectsAsync`, `ListRecentProjectsAsync`, `CreateProjectAsync`, `OpenProjectAsync` with id back-fill in the queue, `RenameProjectAsync`, `DeleteProjectAsync` in the queue, `MutateAsync` with the `Unchanged` no-op, `SetProjectDisplayScaleAsync`, `SetProjectThemeAsync` (null or a known brand, else `ArgumentException`), `SetProjectIntroAsync`, `GetProjectForReadAsync`, `SetProjectsDirAsync`, `GetProjectsDirAsync`, `ResolveImage`, `FlushAsync`, stale-tmp sweep of Q-MODEL-20), `IProjectStoreSettings`, `ProjectSearch`, `ProjectSummary`, `OpenedProject`, `MutateResult`, `ProjectNotKnownException` (`ManifestCorruptException` comes from WP-A3); registrations in `AddShotAICore` |
| Tests | Port `src/main/mutate-serialize.test.ts` (`Store/MutateSerializeTests`), the store cases of `src/main/manifest-extras.test.ts` (`Store/ManifestExtrasStoreTests`) and `src/main/project-theme-key.test.ts` (`Store/ProjectThemeKeyTests`). New: `Store/KnownProjectGateTests`, `ListProjectsTests`, `ProjectSearchTests`, `ProjectSearchComparerTests`, `CreateProjectTests`, `OpenProjectTests`, `UpdatedAtSemanticsTests` (the rows whose operations exist), `Platform.Tests/FileSystem/ReparsePointTraversalTests` (list and delete cases) |
| Acceptance criteria | AC-MODEL-8, AC-MODEL-9, AC-MODEL-12, AC-MODEL-16, AC-MODEL-17, AC-MODEL-28, AC-MODEL-29 |
| Depends on | WP-A3, WP-A4, WP-A5 |
| Size | M |
| Risks and de-risking | .NET ICU `CompareInfo` against V8 `localeCompare` base sensitivity (01 verifier doubt): `ProjectSearchComparerTests` pins the cases; the Linux runner must have ICU (never `InvariantGlobalization`) |
| Demo | a Core test lists a temp root that holds an Electron-authored project and the macOS fixture |

#### WP-A7. Project store: steps and imports

| Field | Content |
|---|---|
| Goal | Every step operation and both import entry points, with no data loss on duplicate ids and link-refusing writes |
| Spec inputs | 01 2.9.9, 2.9.12, 7.8, D-9, D-10, D-22, D-23, EDGE-MODEL-19, EDGE-MODEL-25, EDGE-MODEL-49, EDGE-MODEL-50, EDGE-MODEL-52; 11 7.3.2 (`ImportLimits`), D-IPC-13; 05 7.1 (`TextStepFactory`); Q-MODEL-10 |
| Deliverables | `ProjectStore` members `AddStepAsync`, `InsertStepAtAsync`, `DeleteStepsAsync`, `ReorderStepsAsync`, `AddTextStepAsync` (builds the step with `ShotAI.Core.Report.Operations.TextStepFactory`, which lands here), `ImportStepAsync` (`ImportLimits.Check` first, magic bytes, `.jpg` for JPEG, counter past orphans, `ConfineNoLinks`), `CreateProjectFromImportAsync` (whitelist, duplicate abort, cleanup of exactly the new folder through `ReparseSafeDelete`); `ImportLimits`, `ImportFile`, `UnsupportedImageException`, `ImportRejectedException`, `StepNotFoundException` |
| Tests | No Electron file. New: `Store/StepOperationTests`, `ReorderNoDropTests`, `CreateFromImportTests` (with the Windows case-variant duplicate), `ImportStepConfineTests` (plus the junction duplicate in Platform.Tests), `DeleteStepsMalformedPathTests`, `Validation/ImportLimitsTests` |
| Acceptance criteria | AC-MODEL-18, AC-MODEL-19, AC-MODEL-20, AC-MODEL-21, AC-MODEL-34, AC-MODEL-35 |
| Depends on | WP-A6 |
| Size | M |
| Risks and de-risking | Cleanup deleting the wrong folder if confinement were wrong (Q-MODEL-10): delete only the exact `<root>/<new uuid>` path; a test plants a sibling and asserts it survives |
| Demo | tests |

#### WP-A8. Archive engine

| Field | Content |
|---|---|
| Goal | Archive, unarchive and auto-archive, streaming and CRC-verified, restoring zips written by Electron and writing zips Electron restores |
| Spec inputs | 01 2.9.13, 7.9, INV-MODEL-16, D-12, D-13, D-14, D-15, D-25, EDGE-MODEL-13 to EDGE-MODEL-15, EDGE-MODEL-48; 11 Q-IPC-6; ARCHITECTURE R-ARCH-24; Q-MODEL-6, Q-MODEL-7, Q-MODEL-13, Q-MODEL-21, Q-MODEL-23 |
| Deliverables | `ArchiveEngine` (`System.IO.Compression`, `System.IO.Hashing` CRC-32, size and CRC verification before the originals are removed, `..` and sanitized-name refusal, pack rename through the retry schedule); `ProjectStore.ArchiveProjectAsync`, `UnarchiveProjectAsync`, `AutoArchiveStaleAsync` (raises `ProjectsChanged` when at least one project moved), auto-unarchive on open; `ArchiveException`; `System.IO.Hashing` in `Directory.Packages.props` |
| Tests | Port `src/main/archive.test.ts` (`Store/ArchiveTests`). New: `Store/ArchiveVerifyTests`, `ArchiveNameRulesTests`, `AutoArchiveTests` (including `ProjectsChanged` raised exactly once when at least one project moved, not at all when none did, and a throwing handler not stopping a second one, R-ARCH-24, AC-MODEL-22), the archive case of `ReparsePointTraversalTests`, a 300-character-root create and archive test on Windows (Q-MODEL-13); `UpdatedAtSemanticsTests` completed with every row of 2.9.4 |
| Acceptance criteria | AC-MODEL-2 (the checklist over every 01 8.1 file, ported in WP-A3, WP-A5, WP-A6 and here), AC-MODEL-13, AC-MODEL-15, AC-MODEL-22, AC-MODEL-33 |
| Depends on | WP-A6, WP-A7 |
| Size | M |
| Risks and de-risking | Cloud placeholder files silently dropped by Electron's archive walk (Q-MODEL-6, unverified): native implements D-15 regardless, and M-A checks it on a Files On-Demand folder. AC-MODEL-13's cross-app half needs Electron 1.3.0 on the Windows test machine |
| Demo | archive a project natively, restore it in Electron 1.3.0: every image shows |

#### WP-A9. Project session

| Field | Content |
|---|---|
| Goal | The consolidated optimistic editing contract S1 to S10 (ARCHITECTURE 7.4) that 05, 07, 09 and 11 build on |
| Spec inputs | ARCHITECTURE 7.2 to 7.5, R-ARCH-5, R-ARCH-6, R-ARCH-20, R-ARCH-23, R-ARCH-27; 01 7.10, 7.12, Q-MODEL-25; 05 7.5 requests and Q-REP-14; 07 INV-SOP-28, Q-SOP-21; 09 INV-EXP-28, Q-EXP-11; 11 7.3.2; Q-IPC-21 |
| Deliverables | Core `ShotAI.Core.Store`: `ManifestChangeKind`, `ManifestChangedEventArgs`, `PersistFailedEventArgs`, `ProjectOperation` (`Apply`, `BumpsUpdatedAt`, virtual `AffectedStepIds`), `IProjectSession`, `ProjectSession`, `IProjectSessionFactory`, `ProjectSessionFactory` (the only Core file allowed `SynchronizationContext.Current`, with its `.editorconfig` allowance), `IProjectSettle`; `VSTHRD200` suppressions on `Apply` and `ApplyDurable` only (01 7.10 already points at ARCHITECTURE 7.4 as the contract, and 01 7.10 and 05 7.3 already state R-ARCH-27's disposed-while-draining registration, 1.5) |
| Tests | No Electron file. New: `Store/ProjectSessionTests` covering S1 to S10 (`DurableResultKeepsLaterOptimisticEdits`, clone-throw queues nothing, `Unchanged` queues nothing, `Persisted` versus `External`, re-apply after a failure with the failed operation in `PersistFailed`, `WhenIdleAsync`, `IProjectSettle` for open and closed projects and for a session disposed with writes still pending (`DisposedSessionStaysRegisteredUntilDrained`: `WhenSettledAsync` for its path waits for those writes, and the session unregisters only after them, S7, S9, R-ARCH-27), and `ShippedOperationsBumpUpdatedAt`, a reflection case that asserts every shipped `ProjectOperation` returns `BumpsUpdatedAt == true` and so picks up the operations later WPs add, Q-MODEL-25), plus a randomized interleaving test of `Apply`, `ApplyDurable` and injected failures |
| Acceptance criteria | AC-ARCH-4 |
| Depends on | WP-A6 |
| Size | M |
| Risks and de-risking | The re-apply rules under concurrent durable calls are subtle (05 risk on Q-REP-14): the randomized test compares the session's final `Current` with a sequential model; operations are pure and deterministic (S10), which the test also asserts |
| Demo | tests |

#### WP-A10. Settings service

| Field | Content |
|---|---|
| Goal | `settings.json` read, coerced and written exactly like Electron, optimistic with rollback, unknown keys and positions preserved |
| Spec inputs | 10 2.6 (2.6.1 to 2.6.6), 7.4 (7.4.1 to 7.4.4), INV-INFRA-10 to INV-INFRA-19, EDGE-INFRA-39 to EDGE-INFRA-46; 07 7.2 (settings and catalog); 11 7.3.6; ARCHITECTURE 7.8, 10.2, R-ARCH-13; Q-INFRA-1, Q-INFRA-2, Q-INFRA-3 (01 agrees in Q-MODEL-24), Q-INFRA-12, Q-INFRA-20, Q-IPC-8 |
| Deliverables | Core `ShotAI.Core.Settings`: `AppSettings`, `ThemePref`, `ThemePrefWire`, `SettingsDefaults`, `SettingsCoercer`, `SettingsCodec`, `ISettingsService`, `SettingsService` (`Load`, lock-free `Current`, `UpdateAsync` with its own `SerialWriteQueue`, re-read inside each write, `Changed` with `IsRollback`, `FlushAsync`, one `settings.json.bad` backup), `SettingsChangedEventArgs`, the `IProjectStoreSettings` implementation; Core `ShotAI.Core.Capture.ICaptureSettings` (`CaptureScaleNow`, `RemoteVisibleNow`, synchronous, 02 7.2; declared here so WP-B1's `CaptureShield` can consume it) and its implementation; Core `ShotAI.Core.Paths.IAppPaths` with `LocalDataDirectory` (`%LOCALAPPDATA%\LFI\shotAI`, 10 7.4.4, ARCHITECTURE 10.2, R-ARCH-13); Core `ShotAI.Core.Sop`: `SopCatalog`, `SopModelIds`, `SopModelOption`, `ModelParams`, `SopTone`, `SopEffort`, `SopSettings`, `SopSettingsCoercer` (07 7.2, needed to load the `sop` object). 01's agreement to Q-INFRA-3 is already recorded as Q-MODEL-24, so this WP edits no spec |
| Tests | Port `src/main/settings.test.ts` (`Settings/SettingsStoreTests`). New: `Settings/SettingsCoercionTests`, `SettingsCodecTests`, `SettingsServiceTests` (with the verification additions), `SettingsSchemaTests`, `Sop/SopSettingsCoerceTests` |
| Acceptance criteria | none owned (its manual criteria need the Settings view and are met in WP-B10) |
| Depends on | WP-A2, WP-A4, WP-A5 |
| Size | M |
| Risks and de-risking | Byte layout depends on `JsJson` (10 risk 1): `SettingsCodecTests.FreshFileBytes` compares exact bytes. Rollback of only the failed change: a three-queued, middle-fails test |
| Demo | tests |

#### WP-A11. Logging

| Field | Content |
|---|---|
| Goal | electron-log-compatible file logging that never blocks a caller |
| Spec inputs | 10 2.7, 2.11, 7.5 (7.5.1 to 7.5.5), INV-INFRA-20, INV-INFRA-21; ARCHITECTURE 8.4, 8.5; 11 7.11 (L1, L2); Q-INFRA-7, Q-INFRA-11, Q-IPC-11 |
| Deliverables | Core `ShotAI.Core.Logging`: `LogCategories`, `FileLogOptions`, `FileLoggerProvider`, `FileLogLineFormatter` (`[yyyy-MM-dd HH:mm:ss.fff] [level] (label)` padded to 11, CRLF, UTF-8 without BOM, invariant culture), `RotatingFileSink` (bounded channel of 10,000 lines, drop and count, batches of 256 KiB, `FileShare.ReadWrite \| FileShare.Delete`, rotation past 5,242,880 bytes, synchronous `Flush`), `ServiceLog` (`LoggerMessage` `call: {Service}.{Member}`) |
| Tests | No Electron file. New: `Logging/FileLogLineFormatterTests`, `RotatingFileSinkTests`, `LogCategoriesTests`, `LogPrivacyTests` |
| Acceptance criteria | AC-INFRA-15 |
| Depends on | WP-A1 |
| Size | S |
| Risks and de-risking | Log volume on the hook thread: the sink only formats on the caller and never waits (PB-17) |
| Demo | a test writes 12 MB of Debug lines and exactly `shotai.log` and `shotai.old.log` remain |

#### WP-A12. App host and composition root

| Field | Content |
|---|---|
| Goal | `shotAI.exe` starts through the canonical bootstrap, builds and validates the container, logs, exits in the specified order and runs `--selftest`; the App test project with its STA harness exists |
| Spec inputs | ARCHITECTURE 4 (4.1 to 4.5), 5.1, 6.1 to 6.4, 8.3, 10.2; 03 7.4.1, 7.4.9, 7.5, 8.1, D17, D18, INV-SHELL-19, INV-SHELL-20; 10 7.4.4, 7.5.1, 7.8; 11 7.3.1, 7.10, 7.12; 12 7.2.1 (`App.xaml` as a Page), 7.9.1 (INV-PKG-16); Q-IPC-10, Q-IPC-17, Q-IPC-18, Q-HOME-15, Q-HOME-16, Q-SHELL-16, Q-INFRA-16, Q-PKG-30 |
| Deliverables | App `Program` (explicit `[STAThread] Main`, first statement `DllSearchHardening.Apply()`), `App.xaml` built as a Page (no `StartupUri`), `App` composition root running steps 0, 1, 3, 4, 5b, 6, 9, 12 and 13 of ARCHITECTURE 4.2 (step 13's update check joins in WP-E1) (step 5 lands in WP-D2, steps 2, 7, 8, 11 in WP-A13, step 10 in WP-B5, step 2a in WP-E5), `AppPaths : IAppPaths` (`SettingsFile` and `LogsDirectory` under `%APPDATA%\shotAI`, `LocalDataDirectory` = `%LOCALAPPDATA%\LFI\shotAI`, R-ARCH-13, 10 7.4.4), `CrashLogging`, `ShotAI.App.Threading` (`WpfUiDispatcher`, `AppLifetime`, `ShutdownFlush` with the 5 s bound, the one allowlisted blocking wait), `ShotAI.App.Composition` (`AddShotAIApp`, `ServiceProviderFactory` with `ValidateOnBuild` and `ValidateScopes`), `ViewModelBase` (Debug `VerifyAccess`), `IAppStartup`, `SelfTestHost`; Platform `DllSearchHardening`, `ConsoleAttach`, `PlatformServiceCollectionExtensions.AddShotAIPlatform`; Core `ShotAI.Core.Shell.RuntimeDiagnostics`, `RenderModePolicy`, `ShotAI.Core.SelfTest` (`StartupMode`, `StartupModeParser`, `SelfTestOutcome`, `StoreSelfTest`); the runtime diagnostic line and the Info line `startup: main window rendered in <n> ms` (PB-9, D-ARCH-4), both already in 03 7.9's log table; packages `Microsoft.Extensions.DependencyInjection`, `Microsoft.Extensions.Logging`, `Microsoft.Extensions.Logging.Debug`, `CommunityToolkit.Mvvm`; new project `tests/ShotAI.App.Tests` (`UseWPF`) with the in-repo STA harness (Q-HOME-15) |
| Tests | Port `src/main/gpu-policy.test.ts` in part (`Shell/RuntimeDiagnosticsTests`, `Shell/RenderModePolicyTests`; the decision cases are ELECTRON-ONLY per 03 8.1). New: App `Composition/ContainerTests`, `ViewModelDependencyTests`, `CommandConventionsTests`, `SubscriberDisposalTests`, `Threading/WpfUiDispatcherTests`, `NoSyncWaitTests`, `ViewModelAffinityTests`, `Shutdown/ExitFlushTests`, `CrashLoggingTests`, `LifecycleTests.ExitOrderMatchesSpec11`, `SelfTest/SelfTestProcessTests`, `Shell/AppPathsTests` (`SettingsFileIsRoamingAppData`, `LogsDirectoryIsRoamingAppData`, `LocalDataDirectoryIsUnderLfi`, `NoPathUnderSquirrelRoot`, 10 8.5; `ContainerTests` includes `NoAsyncOnlyDisposables`); Core `SelfTest/StartupModeParserTests`, `StoreSelfTestTests`, `Packaging/SourceScanTests.SetDefaultDllDirectoriesIsFirstInMain` and `AppXamlIsPage`; Platform `Startup/DllSearchTests` |
| Acceptance criteria | AC-MODEL-36 second clause (`ContainerTests.NoAsyncOnlyDisposables` and `LifecycleTests.ExitOrderMatchesSpec11`; its first clause is WP-A3's, 1.5), AC-SHELL-25, AC-INFRA-14, AC-INFRA-25, AC-INFRA-27, AC-INFRA-35, AC-IPC-6, AC-IPC-17, AC-ARCH-2 |
| Depends on | WP-A6, WP-A10, WP-A11 |
| Size | M (upper bound; if review asks, the self-test moves to its own PR `WP-A12b`) |
| Risks and de-risking | Restricted DLL search against WPF's native loads (Q-PKG-30): measure the render tier and the loads with Process Monitor on both reference machines in this WP and add `AddDllDirectory` for the WindowsDesktop folder if needed; the formal AC-PKG-31 check runs on the installed MSI in WP-E3. The exit flush blocks the UI thread (DL4): `ExitFlushTests` blocks the UI thread during the flush and still completes |
| Demo | `dotnet run --project src/ShotAI.App` opens a placeholder main window and `shotai.log` starts with the banner line; `dotnet run --project src/ShotAI.App -- --selftest` prints `[selftest] PASS` and exits 0 |

#### WP-A13. Window base, popup exclusion and single instance

| Field | Content |
|---|---|
| Goal | Every HWND shotAI shows is excluded from capture before it is visible; a second launch surfaces the first instance and exits |
| Spec inputs | 03 2.2, 2.7, 7.4.7, 7.4.8, D15, D20, INV-SHELL-1 to INV-SHELL-5, risk R1; 02 7.9; ARCHITECTURE 4.2 steps 2, 7, 8, 11, 5.4, 9.2 S6; Q-SHELL-1, Q-SHELL-3, Q-SHELL-19 |
| Deliverables | App `ShotAIWindow` (registers and applies `WDA_EXCLUDEFROMCAPTURE` in `OnSourceInitialized`, before the first show), `ShotAIPopup`, `PopupExclusion` (class handlers; the `WH_CALLWNDPROC` catch-all of Q-SHELL-3 if the tests show a late registration), `WindowRegistration`, `ActivationListener`; `MainWindow` derives from `ShotAIWindow`, and in the same change the scaffold's `src/ShotAI.App/MainWindow.xaml` and `MainWindow.xaml.cs` (namespace `ShotAI.App`, `dotnet/src/ShotAI.App/MainWindow.xaml:1`, `dotnet/src/ShotAI.App/MainWindow.xaml.cs:5`) move to `src/ShotAI.App/Shell/` with namespace `ShotAI.App.Shell` and `x:Class="ShotAI.App.Shell.MainWindow"` (03 7.1); Platform `ShotAI.Platform.Shell`: `SingleInstanceLock` (`Local\shotAI.SingleInstance.<SID>`), `ExistingInstance`, `WindowStyles`, `Foreground`, `ProcessMachine`, and the registration surface of `OwnWindowRegistry` (its query side is completed in WP-B5); Platform `ShotAI.Platform.Imaging.WicFactory` (the shared MTA `IWICImagingFactory`, here because both WP-A17 and WP-B5 depend on this WP and run in parallel lanes); Core `SingleInstanceIdentity`, `ShellStrings` (first constants); startup steps 2, 7, 8 and 11 |
| Tests | No Electron file. New: Core `Shell/SingleInstanceIdentityTests` (made-up SID `S-1-5-21-1-2-3-1001`), `ShellStringsTests`; Platform `SingleInstanceTests`; App `AllWindowsRegisteredTests` (main window, a tooltip, a context menu, a `ComboBox` drop-down, and `NoWin32MessageBoxInSource` with the WP-E5 allowlist), `StartupOrderTests.SecondInstanceCreatesNoWindow`, `LifecycleTests.ClosingMainWindowShutsDown` |
| Acceptance criteria | AC-SHELL-4 |
| Depends on | WP-A12 |
| Size | M |
| Risks and de-risking | A popup HWND visible for one frame before registration (03 risk R1, Q-SHELL-3): `AllWindowsRegisteredTests` is written before any popup exists and decides between the class handler and the catch-all hook |
| Demo | launching twice leaves one process, restores the first window, and logs `another instance already holds the lock \u2014 exiting.` once |

#### WP-A14. Theme resources and fonts

| Field | Content |
|---|---|
| Goal | Every colour, radius and font reaches XAML through `DynamicResource` keys built from the brand table; the first frame is already themed; LFI text renders at the right weights |
| Spec inputs | 06 2.32 to 2.35, 7.4 (theme logic), 7.5, 7.14, 8.2, 8.3 (theme manager rows), 8.4 (theme classes), D-HOME-10, D-HOME-27, D-HOME-30, INV-HOME-24; 10 2.10, 7.9, INV-INFRA-31, INV-INFRA-32; ARCHITECTURE 14.8; Q-HOME-1, Q-HOME-2, Q-HOME-12, Q-INFRA-9, Q-INFRA-19 |
| Deliverables | Core `ShotAI.Core.Theme`: `Appearance`, `AppearanceResolver`, `ActiveBrandResolver`, `Rgb`, `ColorMix`, `ChromeTokens`, `ThemeTokenSet`, `ThemeTokenKeys`; Platform `ShotAI.Platform.Theme.SystemAppearanceMonitor : ISystemAppearance`; App `ShotAI.App.Chrome`: `ThemeManager` (`ApplyInitial` before `MainWindow.Show`, one merged dictionary swapped per change, the high-contrast mapping behind a switch until design sign-off), `ThemeResources`, `CapsuleCornerConverter`, `UpperCaseConverter`, `Themes/Controls.xaml`, `Themes/FixedColors.xaml`; fonts: `dotnet/assets/fonts/static/*.ttf` (upstream Archivo static instances, Regular 400 to ExtraBold 800 plus the wdth 62 SemiBold and Bold) with `SOURCES.md` hashes and `OFL.txt`; the variable `src/renderer/fonts/Archivo.ttf` and its `OFL.txt` linked into the App output `Fonts\` until WP-E9 moves them under `dotnet/assets/fonts/`; `dotnet.yml` path filter gains `assets/**` and `src/renderer/fonts/**` |
| Tests | Port `src/renderer/project/app-chrome-tokens.test.ts` as `SourceGuards/XamlChromeGuardTests` (Core.Tests, Linux source scan), the CSS intents of `src/shared/theme-palette.test.ts` as `SourceGuards/XamlResourceKeyGuardTests`, and the theme-manager cases of `src/renderer/project/theme-wiring.test.ts` as App `Chrome/ThemeManagerTests` (the rest of that file lands in WP-A18). New: Core `Theme/AppearanceResolverTests`, `ActiveBrandResolverTests`, `ColorMixTests`, `ThemeTokenSetTests`; App `Chrome/ThemeResourcesTests`, `Shell/ShellStartupTests.ThemeAppliedBeforeMainWindowShown`, `Fonts/FontPackagingTests`, `Fonts/ArchivoRenderingTests`; Platform `Theme/SystemAppearanceMonitorTests` |
| Acceptance criteria | AC-HOME-3, AC-HOME-4, AC-HOME-21, AC-INFRA-30 |
| Depends on | WP-A4, WP-A12 |
| Size | M |
| Risks and de-risking | WPF does not drive the variable font's axes (Q-HOME-2): the static instances and `ArchivoRenderingTests.NormalIsNotSemiBold` decide; if no upstream release matches `fontRevision` 2.001, instance with fontTools in a documented script and say so in `SOURCES.md` (Q-INFRA-19). A single `StaticResource` silently stops following the brand: the source guards run on Linux |
| Demo | with Windows in dark mode the first frame is dark; with `"brand": "lfi"` in `settings.json` the text is Archivo |

#### WP-A15. Main window, app menu and About

| Field | Content |
|---|---|
| Goal | The main window with Electron's size rules, the full menu except View, Brand, UI zoom, full screen and About |
| Spec inputs | 03 2.3, 2.8.1, 2.8.4, 2.11, 7.4.2, 7.4.5, 7.6, 7.7, D6, D7, D9, D10, D11, D19, D21; 11 7.3.5 (`IAppInfo`); ARCHITECTURE R-ARCH-12, INV-ARCH-5; Q-SHELL-10, Q-SHELL-11, Q-SHELL-12, Q-SHELL-14, Q-SHELL-15, Q-SHELL-20 |
| Deliverables | Core `WindowLayout` (`Initial`, `DetailResize`), `UiZoom`, `AboutText`, menu strings in `ShellStrings`; App `MainWindow` (720 x 740 DIP and the minimums), `MainWindowSizer : IMainWindowLayout` (physical px with `SetWindowPos` after `SourceInitialized`), `AppMenuViewModel` (File, Edit, View, Window, Help per 7.4.5, gestures including D21), `AboutWindow`, `ShellViewModel` skeleton (`CurrentView`), `NavigationState` skeleton, `IAppInfo` and `AppInfoProvider`; Core `ShotAI.Core.Diagnostics.IWebView2RuntimeInfo` (`string? GetVersion()`, 11 7.2); Platform `MonitorQueries`, `MonitorDescriptorEx`, and `ShotAI.Platform.Export.WebView2RuntimeInfo : IWebView2RuntimeInfo` (adds `Microsoft.Web.WebView2` to Platform only) |
| Tests | No Electron file. New: Core `Shell/WindowLayoutTests`, `UiZoomTests`, `AboutTextTests`; Platform `MonitorQueriesTests`, `ProcessMachineTests`; App `MainWindowTests`, `AppMenuViewModelTests.ItemsAndGestures` |
| Acceptance criteria | AC-SHELL-5, AC-SHELL-23, AC-SHELL-24 |
| Depends on | WP-A13, WP-A14 |
| Size | M |
| Risks and de-risking | Whether Electron's widths include the invisible borders (Q-SHELL-20): keep outer widths and measure both builds with `DWMWA_EXTENDED_FRAME_BOUNDS` in M-A. Initial placement on a secondary monitor at another DPI (EDGE-SHELL-48): `MainWindowTests.InitialPlacementOnSecondaryMonitorAtOtherDpi` |
| Demo | Help, About shows `.NET` and `WebView2` versions and `win32/<arch>`; `Ctrl+=` zooms the content |

#### WP-A16. Home list

| Field | Content |
|---|---|
| Goal | Home lists, searches, sorts and date-groups projects, refreshes itself, and hands the chosen project to the project view (WP-A17) |
| Spec inputs | 06 2.1, 2.2, 2.7 to 2.12, 2.17 to 2.19, 2.21, 7.2, 7.3 (`AutoRefreshPolicy`), 7.6, 7.7, 7.10, D-HOME-1 to D-HOME-5, D-HOME-14, D-HOME-15, D-HOME-25, D-HOME-28, D-HOME-29, D-HOME-34, INV-HOME-1 to INV-HOME-17 (list rules); 01 2.9.5, 2.9.6, 7.14 (startup auto-archive); 03 7.4.1 step 13 (AC-SHELL-33: WP-A12's startup call, WP-A8's `ProjectsChanged`, this WP's refresh); ARCHITECTURE 5.3, 5.5, R-ARCH-24; Q-HOME-3, Q-HOME-4, Q-HOME-13, Q-HOME-14, Q-MODEL-17, Q-SOP-13 |
| Deliverables | Core `ShotAI.Core.Home`: `HomeTab`, `HomeSortKey`, `DateBucket`, `DateGroups`, `DateGroup<T>`, `HomeListQuery`, `HomeListGroup`, `HomeListView`, `HomeListPipeline`, `AutoRefreshPolicy`, `HomeText` (list strings); App `ShotAI.App.Home`: `HomeView`, `HomeViewModel` (tabs, search, sort, refresh on the 20 s tick, activation and `ProjectsChanged`, newest result wins), `ProjectRowViewModel`, `GroupHeaderItem`; App `ShotAI.App.Shell.ShellView` (Home, Project, Settings hosts), `ScrollMemory`; App `ShotAI.App.Chrome`: `INoticeService`, `NoticeCenter`, `NoticeHost` (03's notice control) |
| Tests | Port `src/renderer/project/date-groups.test.ts` (`Home/DateGroupsTests`). New: Core `Home/DateGroupsDstTests`, `HomeListPipelineTests`, `AutoRefreshPolicyTests`, `HomeTextTests` (list literals); App `Home/HomeViewModelTests` (`OnEnterResetsListControls`, `RefreshTriggers`, `RefreshCoalesces`, `OlderResultIgnored`, `BackgroundRefreshKeepsError`, `BackgroundRefreshFailureIsSilent`, `OpenFailedShowsNotice`), `Chrome/NoticeCenterTests`, `Shell/ShellScrollTests` (Home part) |
| Acceptance criteria | AC-HOME-1, AC-HOME-5, AC-HOME-6, AC-HOME-12, AC-HOME-35, AC-SHELL-33 |
| Depends on | WP-A15, WP-A8 |
| Size | M |
| Risks and de-risking | Local midnights in DST gaps (EDGE-HOME-2): custom time zones in `DateGroupsDstTests`. 500 rows with a shadow each (Q-HOME-14): measure against AC-HOME-35 and replace the shadow if frames exceed 16 ms |
| Demo | pointing the projects folder at an Electron projects root shows the same rows under `THIS WEEK` and `LAST WEEK`; a query shows `MATCHES IN CONTENT` |

#### WP-A17. Read-only report

| Field | Content |
|---|---|
| Goal | Opening a project shows the report at export geometry, with images decoded off the UI thread from confined paths |
| Spec inputs | 05 2.1, 2.2, 2.6, 2.8 to 2.11 (display), 2.22, 3, 7.2, 7.3, 7.8, 7.9, 7.10 (display), 7.11, 7.18, INV-REP-1 to INV-REP-10, INV-REP-18, INV-REP-31, INV-REP-32, D-REP-4, D-REP-12, D-REP-13, D-REP-14, D-REP-18, D-REP-20, D-REP-23, D-REP-24, D-REP-26, D-REP-27, D-REP-29; 01 `ResolveImage`, EDGE-MODEL-39; ARCHITECTURE 9.1 item 2, R-ARCH-21; Q-REP-2, Q-REP-3, Q-REP-7, Q-REP-11, Q-REP-17, Q-REP-19, Q-IPC-16, Q-ARCH-7 |
| Deliverables | Core `ShotAI.Core.Geometry`: `DocScale` (detents, `IsLegal`, `DetentIndex`, `DetentAt`, `Widths`, `DetailWindowWidth`), `DocWidths`, `ReportGeometry` (`Fit`, `WrapContentSlack`), `ReportFit`, `ImageSize`, `ReportLayout`; Core `ShotAI.Core.Report`: `ReportPresentation`, `StepContext`, `WindowLine`, `CardListDiff`, `ReportStrings` (display strings); Platform `ShotAI.Platform.Imaging` (uses WP-A13's `WicFactory`): `ReportImageDecoder` (magic bytes first, explicit PNG and JPEG decoders, EXIF orientation, decode at display size, `FileShare.Delete`, R-ARCH-21), `WicImageSizeProbe : IImageSizeProbe`; App `ShotAI.App.Report`: `ProjectDetailView`, `ProjectDetailViewModel` (`OpenAsync` with an open generation, `Back`, `OpenFailed` shown on Home with the Q-MODEL-11 wording), `ReportView`, `StepCardTemplates` (four variants and the selector), `StepRail`, `ReportFigure` (fit, stored zoom and pan framing, marker overlay; no editing), `ReportImageLoader` (cache key `(flattened, renderRev)`), `NoticeStackViewModel` (display), the command bar skeleton (Back, title, step count), `IMainWindowLayout.SetDetailView` on open and Back |
| Tests | Port `src/renderer/project/report-geometry.test.ts` (`Geometry/ReportGeometryTests`), `src/shared/doc-scale.test.ts` (`Geometry/DocScaleTests`) and `src/shared/report-matches-export.test.ts` (`Geometry/ReportMatchesExportTests`). New: Core `Geometry/ReportLayoutTests`, `Report/ReportPresentationTests`, `Report/CardListDiffTests`; App `Report/ReportLayoutTests`, `ReportImageLoaderTests` (`DoesNotHoldTheFile`, `RefusesPathsOutsideTheProject`, `JpegExifOrientationIsApplied`, `DecodeSizeTracksZoom`), `Report/ProjectDetailStateTests` (open cases) |
| Acceptance criteria | AC-MODEL-25, AC-REP-1, AC-REP-3, AC-REP-5, AC-REP-28, AC-REP-32, AC-REP-33 |
| Depends on | WP-A16, WP-A9 |
| Size | M |
| Risks and de-risking | The native report is 50 DIP narrower than Electron's shipped report (Q-REP-2, EDGE-REP-1): decide at M-A whether the optional Electron CSS parity fix ships before the pilot (2.7). JS rounding in `DocScale` (05 risk): only `JsMath.Round`. WIC projection names are unverified: CsWin32 reports unknown names at build time. Image decoding now runs in the privileged process (Q-IPC-16): only explicit decoders after the magic-byte check |
| Demo | the macOS fixture opens with screenshots 738 DIP wide at 100%; a JPEG with EXIF orientation 6 is upright |

#### WP-A18. Brand submenu and navigation state

| Field | Content |
|---|---|
| Goal | View, Brand shows and changes the open project's pin exactly as #77, #95 and #107 require |
| Spec inputs | 03 2.8.2, 2.8.3, 7.2 (`BrandMenuModel`), 7.4.5, D14, INV-SHELL-16, INV-SHELL-17; 06 7.7, 8.3; 11 2.5.5, 8.1, D-IPC-9, D-IPC-10, INV-IPC-14; ARCHITECTURE 7.5 (View, Brand pin row) |
| Deliverables | Core `BrandMenuModel`, `BrandMenuItem`, `BrandMenuInput`; Core `ShotAI.Core.Report.Operations.SetProjectThemeOperation` (the value passes through untouched, `null` clears); App `NavigationState` complete (`ProjectOpen`, `OpenProjectPath`, `RawProjectTheme`, `ProjectViewVisible`, `Changed`), the Brand submenu and `ChooseBrandCommand` in `AppMenuViewModel`; `ThemeManager` follows `ActiveBrandResolver` |
| Tests | Port `src/renderer/project/theme-wiring.test.ts` (the portable cases per 06 8.3 and 11 8.1; push, rebuild, arm-before-build and coercion cases are ELECTRON-ONLY): Core `Shell/BrandMenuModelTests`; App `Shell/NavigationStateTests`, `ServiceBoundary/BrandChoicePassThroughTests`, `AppMenuViewModelTests` (`BrandChoiceCallsStoreForOpenProject`, `BrandChoiceIgnoredWithNoProject`, `BrandItemsNotifyOnlyOnChange`, `CheckedIsNotToggledByWpf`) |
| Acceptance criteria | AC-SHELL-20, AC-SHELL-21, AC-IPC-13, AC-IPC-14 |
| Depends on | WP-A17 |
| Size | S |
| Risks and de-risking | WPF toggling a checked `MenuItem` by itself (click echo): `CheckedIsNotToggledByWpf` |
| Demo | choosing LFI writes `"theme": "lfi"` and repaints the project view; App default removes the key |

#### WP-A19. Home row operations

| Field | Content |
|---|---|
| Goal | Rename, archive, restore, delete, reveal and multi-select on Home, optimistic at list level with rollback; the shell reveal and the link allowlist |
| Spec inputs | 06 2.12 to 2.16 (without export), 2.20, 2.23, 7.3 (`HomeSelection`, `RenameSession`, `BulkRunner`), 7.6 (archive, restore and their rollback, AC-HOME-39), 7.8, 7.11, D-HOME-5 to D-HOME-7, D-HOME-11 to D-HOME-13; 10 7.7; 11 7.3.3, 7.3.4, 7.10 (exit flush), D-IPC-6, D-IPC-7; ARCHITECTURE R-ARCH-19, R-ARCH-25; Q-HOME-6, Q-INFRA-21, Q-INFRA-22, Q-IPC-14 |
| Deliverables | Core `HomeSelection`, `RenameSession`, `RenameCommit`, `BulkRunner`, `BulkProgress`, `BulkOutcome`; Core `ShotAI.Core.Shell.IShellReveal`; Core `ShotAI.Core.Links` (`IExternalLinks`, `ExternalLinks`, `ExternalLinkPolicy`, `UrlOrigin`, `IUrlLauncher`) and the `ShotAI.Core.Auth.ISupportUrlAllowlist` interface with a no-federation implementation that WP-D4 replaces; Platform `ShellReveal`, `ShellUrlLauncher`, `StaThread`; App `OverflowMenu` (a `ShotAIPopup`), `MenuItemModel`, `IConfirmService`, `ConfirmService`, `ConfirmHost`, `BulkBarViewModel` (selection and count; the export actions arrive in WP-D16), the row operations in `HomeViewModel` with the rollback notice |
| Tests | No Electron file. New: Core `Home/HomeSelectionTests`, `RenameSessionTests`, `BulkRunnerTests`, `Links/ExternalLinkPolicyTests` (the full 10 and 11 tables), `Links/UrlOriginTests`; App `Home/HomeViewModelTests` (`RenameOptimisticRollsBackWithNotice`, `ArchiveOptimisticRollsBack`, `EscapeOrder`, `AnyBusyDisablesActions`, `DeleteNeedsConfirm`, `BulkDeleteNeedsConfirm`, `SearchEditClearsSelection`, `ClearButtonKeepsSelection`, `TabSwitchClearsSelection`, `RenameDoesNotSetRowBusy`), `Chrome/ConfirmServiceTests`, `Chrome/OverflowMenuTests`, `Shell/ShellRevealTests`, `Architecture/SingleUrlLauncherTests` |
| Acceptance criteria | AC-MODEL-31, AC-HOME-7, AC-HOME-10, AC-HOME-11, AC-HOME-31, AC-HOME-39, AC-IPC-18, AC-IPC-20, AC-ARCH-5 |
| Depends on | WP-A16, WP-A13 |
| Size | M |
| Risks and de-risking | A reveal on an unreachable share blocking the UI (D-IPC-6): the STA helper thread and AC-IPC-20 |
| Demo | rename a project and choose File, Exit within 100 ms: after relaunch the new title shows |

#### WP-A20. Phase A exit

| Field | Content |
|---|---|
| Goal | Run milestone M-A (section 4), record measurements and take the decisions phase A owns |
| Spec inputs | section 4 M-A; ARCHITECTURE 11 (PB-7, PB-14), 12.7 (phase A set), 15.4 (Q-ARCH-1, Q-ARCH-2); Q-MODEL-5, Q-MODEL-6, Q-REP-2, Q-REP-11, Q-SHELL-20 |
| Deliverables | the phase A tracking issue completed: the M-A results, PB-7 and PB-14 on both reference machines, the Q-SHELL-20 frame-bounds comparison, the Q-MODEL-6 Files On-Demand check (and an Electron issue if confirmed), the Q-REP-2 decision, the reference machines named in 4.0 (Q-ARCH-1) |
| Tests | none new |
| Acceptance criteria | none owned (it verifies the manual criteria of WP-A1 to WP-A19 were recorded) |
| Depends on | WP-A1 to WP-A19 |
| Size | S |
| Risks and de-risking | none beyond the procedures |
| Demo | the tracking issue |

---

### 3.2 Phase B: capture engine

Goal (feasibility): the hook thread, hotkey, BitBlt grabs, region modes, the overlay and pill windows, the display-affinity shield, UI Automation, the menu and double-click heuristics and the auto-captions, recording from Home into a project. Exit: the same flow recorded in both Windows builds gives the same steps, crops and captions.

#### WP-B1. Capture Core: rules, captions and the shield

| Field | Content |
|---|---|
| Goal | Every pure capture rule, the shield and the shielded funnel, tested on Linux before any Windows code exists |
| Spec inputs | 02 2.7 (2.7.1 to 2.7.3), 2.8, 2.9, 2.11, 2.12 (control types), 3, 7.1, 7.2, 7.7, 7.8, 8.1 to 8.3, INV-CAP-1 to INV-CAP-7, INV-CAP-13, INV-CAP-16, INV-CAP-18, risk R2; ARCHITECTURE 9.2 S6, S8, R-ARCH-1; Q-CAP-10 |
| Deliverables | Core `ShotAI.Core.Capture`: `CaptureConstants`, `CaptureGeometry`, `AutoClassifier`, `ClickCaptions`, `DownscalePolicy`, `ShotNaming`, `UiaControlTypes`, `ElementMapping`, `CaptureShield` (reference counted, restores the setting, exception-safe, `ApplyRemoteVisibility`, `ExcludedForNewWindow`), `ShieldedScreenCapture : IScreenCapture`; the seam interfaces `ITriggerSource`, `IMonitorCapture`, `IScreenCapture`, `IWindowInfoProvider`, `IElementLocator`, `IOwnWindows`, `IWindowProtection`, `IImageCodec`, `ICaptureClock` (`ICaptureSettings`, also a 02 7.2 seam, is declared by WP-A10 and consumed here by `CaptureShield(IWindowProtection, ICaptureSettings)`); the records of 7.2 (`CaptureState`, `CaptureStartOptions`, `DiscardResult`, `CaptureTargets`, `MouseDown`, `PixelFrame`, `MonitorDescriptor`, `ForegroundInfo`, `ListedWindow`, the event args) and `ICaptureService` |
| Tests | Port `src/main/capture-geometry.test.ts` (`Capture/CaptureGeometryTests`, `AutoClassifierTests`), `src/main/capture-shield.test.ts` (`Capture/CaptureShieldTests`, `CaptureFunnelSourceTests`) and `src/main/click-caption.test.ts` (`Capture/ClickCaptionsTests`, extended with every 2.9.1 row and the hotkey captions). New: `JsMathTests.NoMathRoundInCapture`, `JsMathTests.NoJsMathCopyInCapture` (no type named `JsMath` outside `ShotAI.Core.Json`, INV-CAP-18), `DownscalePolicyTests`, `ShotNamingTests`, `UiaControlTypesTests`, `ElementMappingTests`, `ShieldedCaptureTests`, `CaptureFunnelSourceTests.NoGraphicsCaptureUsage` |
| Acceptance criteria | AC-CAP-1, AC-CAP-3, AC-CAP-4 |
| Depends on | WP-A3, WP-A10 |
| Size | M |
| Risks and de-risking | One unshielded read leaks the pill into a finished SOP (02 R2): `GdiMonitorCapture` will be internal and reachable only through the funnel; the mutation check of AC-CAP-3 is done once and noted in the PR |
| Demo | tests |

#### WP-B2. Capture engine: sessions and the capture pipeline

| Field | Content |
|---|---|
| Goal | `CaptureEngine` runs sessions (new, append, rolling-cursor insert), the FIFO capture queue, every grab path, persistence and events, over fakes |
| Spec inputs | 02 2.1, 2.2 (2.2.1 to 2.2.9), 2.5, 2.6, 2.8.2, 2.10 (as consumed), 2.13 to 2.15, 7.3, 7.10 (D4 to D11, D16, D18, D21 to D24), 7.11, 7.12, 7.13, INV-CAP-8 to INV-CAP-12, INV-CAP-19 to INV-CAP-28; 11 section 10 requests to 02 (EventRaiser, explicit-target `ShotAIException`, `InsertAt` clamp, idle `StateChanged` after a screenshot, `IDisposable`); ARCHITECTURE R-ARCH-10; Q-CAP-1, Q-CAP-6, Q-CAP-7, Q-CAP-12, Q-CAP-17, Q-IPC-4, Q-IPC-23 |
| Deliverables | `CaptureEngine : ICaptureService, IDisposable` (`StartAsync` with atomic reservation, `Pause`, `Resume`, `StopAsync` draining in-flight captures, `DiscardAsync`, `CaptureScreenshotAsync` with the 350 ms hide settle, `ListTargetsAsync`, `Teardown`, a synchronous idempotent `Dispose` that `provider.Dispose()` calls at exit and that never awaits in-flight jobs, and a `DisposeAsync` kept only for tests and non-container owners, 02 7.13, R-ARCH-10), internal `CaptureSession`, the single-reader `Channel<CaptureJob>` worker with `SessionAlive(gen)` re-checks, the grab paths (window, area with its preserved crop quirk, auto region, fullscreen) through `IScreenCapture` only, orphan seeding (D11, D22), `shots/` confinement (D10), events through `EventRaiser` in Electron's order, `CaptureException` and `TriggerException` (both `ShotAIException`, 02 7.2), job failure messages through `UserMessage`, the log lines of 2.6 step 18; `captureSingle` not ported (D16) |
| Tests | No further Electron file. New: `Capture/CaptureEngineTests` (the session, persistence, screenshot and event cases of 02 8.4, for example `StopDrainsInFlightCaptures`, `CaptureBailsWhenSessionClearedMidFlight`, `InsertSessionLandsStepsAtCursorInOrder`, `ScreenshotWaits350MsAfterHide`, `RecordingChangedSequence`, `StepLandedCarriesRenumberedOrderAndIndex`, `StartDuringScreenshotThrows`, `LogLineFormatMatchesElectron`, and the AC-CAP-35 cases `DisposeIsSynchronousAndIdempotent`, `EventsGoThroughEventRaiser`, `JobFailureMessageUsesUserMessage`, `ThrownMessagesAreShotAIExceptions`), `ServiceBoundary/ScreenshotTargetTests` |
| Acceptance criteria | none owned (the engine's automated criterion AC-CAP-2 completes in WP-B3; AC-CAP-35, whose Linux cases this WP writes, also needs `CaptureEngine` resolvable in the full container, so it is met in WP-B6, where the last capture seam is registered) |
| Depends on | WP-B1, WP-A7, WP-A10 |
| Size | M |
| Risks and de-risking | Races between a job, stop and teardown (EDGE-CAP-28, EDGE-CAP-45): the gate lock never spans I/O or an await, and each await is followed by the generation check. Whether the store renumbers the caller's `ProjectStep` in place (02 verifier doubt): the engine sets `Order` from the store result itself |
| Demo | tests |

#### WP-B3. Capture engine: click decisions and menus

| Field | Content |
|---|---|
| Goal | The mousedown decisions in Electron's order: own-window gate, button mapping, double-click collapse, right-click arming, proximity, chains, submenus, the 400 ms menu poll |
| Spec inputs | 02 2.3 (2.3.1), 2.4 (2.4.1 to 2.4.3), 3, 7.3 (poll task), 7.10 D1, D2, D3, D12, D13, INV-CAP-14, INV-CAP-15; Q-CAP-20 |
| Deliverables | the decision path in `CaptureEngine` on the `shotAI.CaptureDispatcher` thread; internal `MenuArm` with its `PeriodicTimer` poll (first frame at 400 ms, at most 32 frames, per-arm in-flight flag, cancelled on disarm, re-arm, pause and expiry); pooled frame ownership handed to the selection capture |
| Tests | Complete `Capture/CaptureEngineTests` (`DoubleClickCollapsesChained`, `RightClickThenNearbyLeftIsMenuSelection`, `FarLeftClickDisarms`, `MiddleClickDisarms`, `MenuChainIsBoundedAtFour`, `ExpiredMenuWindowIsNotASelection`, `SubmenuWindowIsSixSeconds`, `ProximityScalesWithMonitorFactor`, `ScreenModeMenuUsesChosenMonitor`, `OwnWindowClickDoesNotQueryElement`, `MenuSelectionTakesPolledFrameOwnership`), `Capture/MenuPollTests` |
| Acceptance criteria | AC-CAP-2 |
| Depends on | WP-B2 |
| Size | M |
| Risks and de-risking | Timing-dependent logic: every delay comes from `ICaptureClock`; no test sleeps |
| Demo | tests |

#### WP-B4. Input hook and hotkey

| Field | Content |
|---|---|
| Goal | A low-level mouse hook that is allocation-free, never trips `LowLevelHooksTimeout`, and is reinstalled if Windows removes it; the Ctrl+Shift+S hotkey |
| Spec inputs | 02 7.3, 7.4, 7.14 (hook and hotkey names), D14, D19, INV-CAP-17, risk R1; ARCHITECTURE 6.1, DL7, PB-1, PB-2; feasibility "Hook timing under .NET"; Q-CAP-2, Q-CAP-3, Q-CAP-5, Q-CAP-19, Q-CAP-23 |
| Deliverables | Platform `ShotAI.Platform.Capture.Win32TriggerSource : ITriggerSource` (dedicated `shotAI.InputHook` thread with its own `GetMessageW` loop, `WH_MOUSE_LL` proc that copies into a preallocated 256-entry ring and signals a kernel event, `RegisterHotKey` with `MOD_NOREPEAT`, the cursor-movement watchdog, `Detach` posting to the thread and joining for 2000 ms, 32-bit coordinates); the `NativeMethods.txt` entries of 02 7.14 for hooks, messages and hotkeys; `PlatformCaptureRegistration` (first part) |
| Tests | No Electron file. New: Platform `Capture/MouseHookTests` (`SyntheticClickIsDelivered`, `InjectedClicksAreDelivered`, `EveryButtonMaps`, `CallbackIsAllocationFree`, `WatchdogReinstallsRemovedHook`, `DetachIsIdempotentAndJoins`), `HotkeyTests`, `DpiAwarenessTests`, and a `Category=Perf` hook-latency test for PB-1 |
| Acceptance criteria | none owned (the hook's manual criteria run end to end in WP-B9) |
| Depends on | WP-B1, WP-A5 |
| Size | M |
| Risks and de-risking | Silent hook removal (02 R1): allocation-free proc proven by `CallbackIsAllocationFree`, watchdog proven by `WatchdogReinstallsRemovedHook`, every reinstall logged so pilot logs reveal it; Raw Input stays the documented fallback (Q-CAP-5). `SetCursorPos` bypassing the hook could make the watchdog misfire (UNVERIFIED): the test moves the cursor with `SendInput` |
| Demo | the Windows job log shows the hook tests passing on x64 and arm64 |

#### WP-B5. Screen capture, display affinity and the protection probe

| Field | Content |
|---|---|
| Goal | Pixels are read only through the shielded funnel; shotAI's windows, including layered ones, are proven absent from its captures before the pill and overlay are built |
| Spec inputs | 02 2.7, 2.11, 7.7, 7.8, 7.9, 7.12, D24, INV-CAP-1 to INV-CAP-7, INV-CAP-25, INV-CAP-29, 8.4 (Platform rows and the probe); 03 2.7, INV-SHELL-2; 11 `RemoteVisibilityApplier`, INV-IPC-13, D-IPC-8; ARCHITECTURE 4.2 step 10, DL1, DL2; Q-CAP-11, Q-CAP-13, Q-CAP-14, Q-CAP-15, Q-CAP-16, Q-CAP-18, Q-CAP-21, Q-CAP-22, Q-ARCH-6 |
| Deliverables | Platform `GdiMonitorCapture : IMonitorCapture` (`internal sealed`, `BitBlt` with `SRCCOPY \| CAPTUREBLT`, alpha forced to 255, pooled buffers), `DisplayAffinityProtection : IWindowProtection` (over the existing `CaptureExclusion.Apply`), `OwnWindowRegistry : IOwnWindows` (query side), `WicImageCodec : IImageCodec`, which uses WP-A13's `WicFactory` (crop, Fant resize, PNG RGBA encode), the remaining 02 7.14 `NativeMethods.txt` entries for capture and WIC; `dotnet/tools/ShotAI.ProtectionProbe` (port of `scripts/protection-probe.cjs` with the same delays, repetitions, tolerance and verdict text, for a normal and an `AllowsTransparency` window; not shipped, not in CI); App `RemoteVisibilityApplier : IAppStartup`, which applies `CaptureShield.ApplyRemoteVisibility` through `Task.Run` with a serialized latest-wins loop and never on the UI thread (DL1, 11 7.3.6, 02 7.8), and startup step 10 (protected first, relaxed after the setting loads) |
| Tests | No Electron file. New: Platform `GdiMonitorCaptureTests` (`FrameMatchesMonitorSize`, `AlphaIsOpaque`, `ExcludedWindowContributesZeroPixels`, `LayeredWindowAcceptsAffinity`), `ShieldDeadlockTests`, `DisplayAffinityProtectionTests.SkipsDestroyedHwnd`, `WicImageCodecTests`; App `StartupOrderTests.WindowsExcludedBeforeSettingApplied`, `Settings/RemoteVisibilityApplierTests` (including that the apply call runs off the UI thread) |
| Acceptance criteria | AC-CAP-13 |
| Depends on | WP-B1, WP-A13 |
| Size | M |
| Risks and de-risking | Whether `SetWindowDisplayAffinity` works on WPF layered windows and from a non-UI thread is undocumented (Q-CAP-15, Q-CAP-22): run the probe and `LayeredWindowAcceptsAffinity` first; record the answer in 02 section 11; if calls must run on the UI thread, implement DL2's bounded, fail-closed marshaling (Q-ARCH-6) before WP-B7 starts |
| Demo | `ShotAI.ProtectionProbe` prints `CLEAN` for both windows at +0 ms |

#### WP-B6. Window information and UI Automation

| Field | Content |
|---|---|
| Goal | The foreground window, the window list and the element at a click point, with get-windows naming and hang-tolerant UI Automation |
| Spec inputs | 02 2.10 (2.10.1 to 2.10.3), 2.12 (2.12.1, 2.12.2), 7.5, 7.6, D15, D17, INV-CAP-15, INV-CAP-16, risk R4; ARCHITECTURE DL3, 6.6; Q-CAP-4, Q-CAP-8, Q-CAP-9 |
| Deliverables | Platform `Win32WindowInfoProvider : IWindowInfoProvider` (FileDescription rule, ApplicationFrameHost child walk, Widgets.exe exclusion, `DWMWA_EXTENDED_FRAME_BOUNDS`, the get-windows list filter, own-pid check), `UiaElementLocator : IElementLocator` (two MTA threads `shotAI.Uia.0` and `.1`, `IUIAutomation2` 500 ms timeouts, 600 ms overall cap, climb of 6, the 15-type allowlist, COM objects released on the reading thread); the `NativeMethods.txt` entries; `PlatformCaptureRegistration` complete; `CaptureEngine` (WP-B2) registered as `ICaptureService` in `AddShotAICore` now that every seam its constructor needs is registered (AC-CAP-35) |
| Tests | No Electron file. New: Platform `WindowInfoTests` (including `UwpAppKeepsFrameHostPid`), `UiaElementLocatorTests` (including `HungProviderTimesOut`, `StaleRequestsAreDroppedWhenBothThreadsHang`); App `Composition/ContainerTests` passes with `CaptureEngine` resolved as `ICaptureService` (`NoAsyncOnlyDisposables`, AC-CAP-35) |
| Acceptance criteria | AC-CAP-5, AC-CAP-35 |
| Depends on | WP-B1, WP-B2 (the `CaptureEngine` it registers), WP-B4, WP-B5 |
| Size | M |
| Risks and de-risking | App-name drift changes captions, the classifier and SOP prompts (02 R4): `WindowInfoTests` plus AC-CAP-30 in WP-D8. CsWin32 projection shapes for `IUIAutomation2` are unverified: the build reports unknown names |
| Demo | tests on x64 and arm64 |

#### WP-B7. Capture pill and recording visibility

| Field | Content |
|---|---|
| Goal | The non-activating pill with Electron's rendering, error, flash and Discard rules; the controller that hides the main window and shows the pill in event order |
| Spec inputs | 03 2.4 (2.4.1 to 2.4.8), 2.6, 7.2 (`PillDocking`, `PillPresenter`, `RecordingVisibilityPlanner`), 7.4.3, 7.4.6, 7.6 (7.6.1 to 7.6.3), D1 to D5, D8, INV-SHELL-6 to INV-SHELL-12, risk R2; 02 D20; 11 7.6, 7.7; ARCHITECTURE R-ARCH-11; Q-SHELL-2, Q-SHELL-3, Q-SHELL-4, Q-SHELL-7, Q-SHELL-8, Q-SHELL-21, Q-IPC-15, Q-IPC-20 |
| Deliverables | Core `PillDocking`, `PillPresenter`, `PillViewState`, `PillStatusRow`, `RecordingVisibilityPlanner`, `ShellAction`, pill strings in `ShellStrings`; App `CapturePillWindow` (`WS_EX_NOACTIVATE`, `WS_EX_TOOLWINDOW`, `MA_NOACTIVATE`, manual drag, controls not focusable, closing cancelled unless shutting down), `CapturePillViewModel`, `DiscardConfirmWindow` (`Discard` and `Cancel`, Cancel default, registered and topmost), `RecordingVisibilityController` (the single ordered `IUiDispatcher.Post` path for capture events); `Pause` and `Resume` through `Task.Run` |
| Tests | No Electron file. New: Core `Shell/PillDockingTests`, `PillPresenterTests`, `RecordingVisibilityPlannerTests`; App `CapturePillWindowTests` (`ShowDoesNotActivate` including a show after `Hide()`, `ClickDoesNotActivate`, `DragMovesWithoutActivating`, `ErrorTooltipShowsWhileInactive`, `DiscardConfirmCallsServiceOnce`), `LifecycleTests.PillCloseCancelledWhileRunning`, `MainWindowCloseClosesPillDespiteVeto`, `ShellEventOrderingTests`, `RecordingChangedHidesAndShows` (against a fake `ICaptureService`) |
| Acceptance criteria | none owned (the pill's manual criteria run with real recordings in WP-B9) |
| Depends on | WP-B3, WP-B5, WP-A15 |
| Size | M |
| Risks and de-risking | WPF re-activating the pill through a tooltip or a template `Focus()` (03 R2, Q-SHELL-3, Q-SHELL-21): the foreground assertions in `CapturePillWindowTests` are written first; if a later `Show()` activates, switch to `ShowWindow(SW_SHOWNOACTIVATE)` only |
| Demo | an App test run shows the pill driven by a fake engine: `Capturing · 3`, the green ring, the error row |

#### WP-B8. Area-select overlay

| Field | Content |
|---|---|
| Goal | One transparent overlay per monitor in physical pixels, returning the exact rectangle its badge shows |
| Spec inputs | 03 2.5 (2.5.1 to 2.5.5), 7.2 (`AreaSelectionMath`), 7.4.4, 7.7, D12, D13, D22, D23, D24, risk R3; 11 2.5.3, 2.5.6; Q-SHELL-5, Q-SHELL-6 |
| Deliverables | Core `AreaSelectionMath`, `DipRect`, `PixelRect`; App `AreaOverlayWindow` (`AllowsTransparency`, `#01000000` fill so it receives clicks, sized with `SetWindowPos` in physical px, re-applied on `DpiChanged`, overlay under the cursor activated, Esc on every overlay), `AreaSelectionService : IAreaSelectionService` (newest request wins, cancellation, requester restored) |
| Tests | No Electron file. New: Core `Shell/AreaSelectionMathTests`; App `AreaOverlayTests`, `AreaSelectionServiceTests` |
| Acceptance criteria | AC-SHELL-1, AC-SHELL-2, AC-SHELL-29 |
| Depends on | WP-B5, WP-A15 |
| Size | M |
| Risks and de-risking | Chromium's `DIPToScreenRect` rounding is assumed (Q-SHELL-5): compare the `region selected:` log lines of both builds at 125% and 150% in M-B and adjust `ToPhysical` |
| Demo | an App test harness opens overlays on every monitor with exact bounds |

#### WP-B9. Recording from Home and the project view

| Field | Content |
|---|---|
| Goal | A user records from Home (new project) or resumes into an open project, with the pill, overlay, recording panel and capture self-test all wired; the phase's manual criteria run here |
| Spec inputs | 06 2.3 to 2.6, 7.3 (`CaptureReadiness`), 7.6, 8.4 (Home recording rows); 05 2.3 (Resume capturing), 2.19 (picker rules shared with Home); 02 2.16, EDGE-CAP-34; 10 7.8 (`--capture-selftest`); 11 7.7 (subscribe then read); ARCHITECTURE 5.3 (recording row), R-ARCH-26; Q-CAP-24, Q-SHELL-9, Q-SHELL-18, Q-IPC-20, Q-IPC-22 |
| Deliverables | Core `CaptureReadiness`, the hero, picker and recording-panel strings in `HomeText`; App `CreateHeroViewModel`, `CaptureModePickerViewModel` (named singleton, implements `ICaptureTargetSelection`), `RecordingPanelViewModel` (shows the list length, Q-IPC-22), the new-recording flow (`StartAsync(path, new CaptureStartOptions(Target, CreatedThisSession: true))`) and the Empty Project flow, Resume capturing in the project command bar (the target is read from `ICaptureTargetSelection.BuildTarget()` at click time and carried by 05's `ResumeCaptureRequested`, R-ARCH-26), the `Recording` view of `ShellViewModel`; the `--capture-selftest` body with the macOS size corrections; the Debug timing line per capture job (PB-4) added to 02's log table |
| Tests | No Electron file. New: Core `Home/CaptureReadinessTests`, `HomeTextTests` (complete), `Threading/SubscribeThenReadTests`; App `Home/CaptureModePickerTests`, `Home/RecordingPanelTests`, `Home/HomeViewModelTests.CaptureCreatesAndStartsWithCreatedThisSession`, `EmptyProjectOpensWithoutCapture`, `Threading/EventOrderTests`, `Shell/ShellViewModelTests` (recording rows) |
| Acceptance criteria | AC-CAP-10, AC-CAP-14, AC-CAP-15, AC-CAP-19, AC-CAP-22, AC-CAP-23, AC-CAP-24, AC-CAP-25, AC-CAP-26, AC-CAP-28, AC-SHELL-3, AC-SHELL-7, AC-SHELL-8, AC-SHELL-9, AC-SHELL-10, AC-SHELL-11, AC-SHELL-12, AC-SHELL-13, AC-SHELL-14, AC-SHELL-15, AC-SHELL-16, AC-SHELL-17, AC-SHELL-19, AC-SHELL-26, AC-SHELL-27, AC-SHELL-31, AC-SHELL-32, AC-HOME-2, AC-HOME-13, AC-HOME-14, AC-INFRA-26, AC-IPC-5, AC-IPC-7, AC-IPC-21 |
| Depends on | WP-B4, WP-B6, WP-B7, WP-B8, WP-A17, WP-A19 |
| Size | M (code); its manual script is long and is run in one sitting on the reference x64 machine with a 100% plus 150% monitor pair, then repeated for the ARM64 subset |
| Risks and de-risking | A burst of capture events starving input and render (Q-IPC-20): the 20-clicks-in-5-seconds script must record 20 steps and the pill must stay responsive; `StateChanged` subscribers coalesce |
| Demo | from Home, record a five-click flow in Auto mode; stop; the report shows five captioned steps |

#### WP-B10. Settings view (non-AI groups) and the onboarding tour

| Field | Content |
|---|---|
| Goal | Settings with every control that does not need sign-in (Capture, Appearance, Storage, About, and the SOP options of the AI tab), writing optimistically with rollback; the first-run tour |
| Spec inputs | 06 2.24, 2.25 (SOP options only), 2.26 to 2.31, 7.4 (`SettingsText`, `TourLayout`), 7.8, 7.12, D-HOME-8, D-HOME-9, D-HOME-16 to D-HOME-21, D-HOME-31, D-HOME-32, INV-HOME-20, INV-HOME-41; 10 2.6.3, 7.4.3; 11 7.3.5 (`IFileDialogs`); 04 Q-EDIT-21 (the folder dialog's HWND registered for capture exclusion); Q-HOME-9, Q-HOME-10, Q-HOME-11, Q-INFRA-20, Q-IPC-13, Q-SOP-10 |
| Deliverables | Core `ShotAI.Core.SettingsUi` (`SettingsText` non-auth rows, `ArchiveAgeOption`, `ArchiveAgeOptions`, `CaptureScaleSteps`), `ShotAI.Core.Tour` (`TourAnchorId`, `TourStep`, `TourSteps`, `LayoutRect`, `TourPlacement`, `TourLayout`); App `SettingsView`, `SettingsViewModel` (created fresh on each open), `CaptureSettingsViewModel` (quality, remote visibility through `Task.Run` per DL1), `AppearanceSettingsViewModel` (theme and brand as two groups), `StorageSettingsViewModel` (projects folder through `IFileDialogs.PickFolder`, archive age), `AboutSettingsViewModel` (name, include name, update-check toggle, show intro tour; `Check now` arrives in WP-E1), the SOP options of the AI tab (model, tone, effort, custom instructions, master switch), `TourOverlay`, `TourViewModel`, `TourAnchor`; `ShotAI.App.Services.IFileDialogs`, `WpfFileDialogs` |
| Tests | No Electron file. New: Core `SettingsUi/SettingsTextTests` (non-auth rows), `ArchiveAgeOptionsTests`, `CaptureScaleStepsTests`, `Tour/TourStepsTests`, `TourLayoutTests`; App `Settings/SettingsViewModelTests` (`ShowsCoercedValue`, `RollsBackWithNoticeOnFailure`, `BlankNameDisablesAndClearsInclude`, `RemoteVisibleAppliesImmediately`, `BrandChoiceRepaintsImmediately`, `CustomInstructionsPersistOnUnload`, `UnchangedBlurDoesNotWrite`, `RollbackShowsCurrent`), `Settings/SettingsViewTests` (`TabKeyboard`, `AppearanceAndBrandSeparate`, `ComboBoxUsesShotAIPopup`, `FieldsUseFieldBg`), `Tour/TourViewModelTests`, `Tour/TourOverlayTests`, `Shell/ShellScrollTests` (Settings), `Shell/ShellViewModelTests` (Settings rows) |
| Acceptance criteria | AC-HOME-15, AC-HOME-16, AC-HOME-17, AC-HOME-18, AC-HOME-19, AC-HOME-20, AC-HOME-22, AC-HOME-23, AC-HOME-24, AC-HOME-38, AC-INFRA-7, AC-INFRA-8, AC-INFRA-9, AC-INFRA-10, AC-INFRA-11, AC-INFRA-12, AC-INFRA-13, AC-INFRA-32 |
| Depends on | WP-A19, WP-A14, WP-B5 |
| Size | M |
| Risks and de-risking | The notice text for a failed settings write needs 06's agreement (Q-INFRA-20): use the standard error notice and record it in 06 |
| Demo | change the theme to Dark: the window repaints at once and `settings.json` holds `"theme": "dark"`; make the file read-only and change it again: the switch rolls back with a notice |

#### WP-B11. Phase B exit

| Field | Content |
|---|---|
| Goal | Run milestone M-B: the side-by-side capture comparison, the screen-share checks, mixed DPI, and the capture budgets |
| Spec inputs | section 4 M-B; 02 9 (the side-by-side criteria), 11 AC-IPC-12; ARCHITECTURE 11 (PB-1 to PB-5), 12.7 (phase B set); Q-CAP-4, Q-CAP-8, Q-CAP-9, Q-CAP-13, Q-CAP-16, Q-CAP-21, Q-SHELL-5, Q-SHELL-20 |
| Deliverables | the phase B tracking issue completed with each AC result, PB-1 to PB-5 on both machines, and the decisions those questions need, written back into 02 and 03 section 11 |
| Tests | none new |
| Acceptance criteria | AC-CAP-6, AC-CAP-7, AC-CAP-8, AC-CAP-9, AC-CAP-11, AC-CAP-12, AC-CAP-21, AC-CAP-27, AC-CAP-29, AC-CAP-31, AC-CAP-32, AC-CAP-33, AC-CAP-34, AC-SHELL-18, AC-HOME-34, AC-HOME-37, AC-IPC-12 |
| Depends on | WP-B1 to WP-B10 |
| Size | S |
| Risks and de-risking | A mismatch in crop sizes of 14 to 16 px means Electron used `GetWindowRect` (Q-CAP-8, decided by AC-CAP-34): switch the rect source in `Win32WindowInfoProvider` in a follow-up PR on WP-B6 and repeat AC-CAP-6 and AC-CAP-34. An inactive-window mis-crop (AC-CAP-27 fails): implement the Q-CAP-4 fallback in WP-B3's code |
| Demo | the tracking issue |

---

### 3.3 Phase C: editor and redaction

Execution order: WP-C1, WP-C2, WP-C3, WP-C4, WP-C5, WP-C8, WP-C6, WP-C7, WP-C9 to WP-C13, the order of the sections below; WP-C8 comes before WP-C6 and WP-C7 because both depend on it, and the numbering is kept for ID stability (IDs are referenced across the docs).

Goal (feasibility): the report becomes editable (optimistic, with rollback), the WPF annotation canvas, flatten and bake, click markers, crop, pan and zoom, Windows OCR auto-redact, merge. Exit: the ported flatten tests are green and a saved render provably contains no original pixels of a redacted region (the export-level proof is part of M-D, 1.5).

#### WP-C1. Report operations (Core)

| Field | Content |
|---|---|
| Goal | Every report edit is a pure `ProjectOperation` with Electron's `Changed` and `Unchanged` rules, and the optimistic policy decides Apply or ApplyDurable per edit path |
| Spec inputs | 05 2.13 to 2.18, 2.20 (R1 to R11, P1 to P8), 7.4, 7.5, 7.6, INV-REP-11 to INV-REP-30, D-REP-1, D-REP-2, D-REP-5, D-REP-6, D-REP-7, D-REP-8; 01 7.10; ARCHITECTURE 7.4, 7.5; Q-REP-4, Q-REP-9, Q-MODEL-16 |
| Deliverables | Core `ShotAI.Core.Report.Operations`: `ReportOperation` (with `AffectedStepIds`) and the eleven operations of 05 7.4 (zoom, pan, move, caption, instructions, delete, text step save, callout change, overview, add text step, document scale); Core `ShotAI.Core.Report`: `ReportEditState`, `ReportEditPolicy`, `ReorderPlanner`, `StepNumberEntry`, edit strings in `ReportStrings` |
| Tests | No Electron file. New: `Report/ReportOperationsTests` (including `AddTextStepSerializesLikeElectron` and determinism), `OptimisticPolicyTests` (one case per R and P row, with a fake store of controllable completion and failure), `ReportEditStateTests`, `ReorderPlannerTests`, `StepNumberEntryTests`, `ReportStringsTests` |
| Acceptance criteria | AC-REP-9, AC-REP-11 |
| Depends on | WP-A9, WP-A17 |
| Size | M |
| Risks and de-risking | Without the session's change kinds, a write finishing would close an open editor (Q-REP-14): `ReportEditStateTests.ReconcileByKind` against the A9 contract |
| Demo | tests |

#### WP-C2. Report editing UI

| Field | Content |
|---|---|
| Goal | Captions, instructions, text steps and the overview edit in place and show at once; reorder by buttons and number entry; delete; the rollback notice with draft recovery |
| Spec inputs | 05 2.7, 2.12, 2.13, 2.14 (buttons, number entry), 2.15, 7.6, 7.8, 7.13 (overflow, confirm), 7.16, 7.18, D-REP-3, D-REP-11, D-REP-19, D-REP-22, D-REP-25; ARCHITECTURE 5.5; Q-REP-1, Q-REP-8, Q-REP-10, Q-REP-12, Q-REP-15, Q-REP-16, Q-REP-20, Q-MODEL-12 |
| Deliverables | App `StepCardViewModel` (inline editing bound to operations), `IntroViewModel`, `InlineEditBox`, `TextStepEditor`, `OverflowMenuButton` (menu per 05 `MenuFor`), delete confirmation through `IConfirmService`, move up and down and number entry, `NoticeStackViewModel` with the `SaveError` slot (`Your last change couldn't be saved and was undone. ` plus the message) and draft recovery, card list diffing so only changed cards re-render |
| Tests | No Electron file. New: App `Report/InlineEditBoxTests`, `ReportResponsivenessTests`, `DialogKeyboardTests` (confirm cases), `ReportAutomationTests` (names so far), `ProjectDetailStateTests.SettingsRoundTripKeepsDrafts` (draft part) |
| Acceptance criteria | AC-MODEL-23, AC-REP-6, AC-REP-7, AC-REP-8, AC-REP-12, AC-REP-26, AC-REP-30, AC-REP-31 |
| Depends on | WP-C1, WP-A19 |
| Size | M |
| Risks and de-risking | Rollback moving a card under the user (Q-REP-15) and re-opened drafts (Q-REP-16): accepted with the notice; the draft re-opens only when no editor of the same kind is open |
| Demo | edit a caption on a 100-step project: the text shows before the write; with `project.json` read-only it reverts within 2 s with the notice and the editor re-opens with the typed text |

#### WP-C3. Report figure, drag reorder, size control and text inserts

| Field | Content |
|---|---|
| Goal | Zoom and pan on the figure, drag and drop reorder with auto-scroll, the document size control with its detents and stepper hold, insert zones for text and callouts |
| Spec inputs | 05 2.5, 2.6, 2.11 (zoom, pan, controls), 2.14 (drag), 2.17, 7.7, 7.10, 7.12, D-REP-7, D-REP-8, D-REP-15, D-REP-16, D-REP-17, D-REP-27, D-REP-28; 03 Q-SHELL-13; Q-REP-6, Q-REP-13, Q-REP-18 |
| Deliverables | Core `DocScaleEditor`, `PopoverPlacement` (overflow drop-up); App `DocScaleControl`, `DocScaleViewModel` (commit on release, typed percent, stepper hold D-REP-17 deferring `SetDetailView`), `ReportFigure` (zoom buttons compounding from the displayed value, left-button pan, floating controls not hit-testable when idle), `DragReorderController` (`ShotAI.StepDrag` payload with the session id), `DragAutoScroller` (`DispatcherTimer` plus `GetCursorPos`, `DragOver` fallback), `InsertZone`, `InsertMenu` (text and callout inserts through `AddTextStepOperation`); Platform `CursorPosition` |
| Tests | No Electron file. New: Core `Report/DocScaleEditorTests`, `PopoverPlacementTests` (overflow rows); App `Report/StepFigureTests`, `DragReorderTests`, `DocScaleControlTests`, `ReportLayoutTests.NarrowWindowShrinksTheColumn` |
| Acceptance criteria | AC-SHELL-6, AC-REP-13, AC-REP-14, AC-REP-15, AC-REP-16, AC-REP-17, AC-REP-18, AC-REP-19, AC-REP-29 |
| Depends on | WP-C2 |
| Size | M |
| Risks and de-risking | Whether a `DispatcherTimer` ticks inside the OLE drag loop is unverified (05 7.12): `AutoScrollNearBottomEdge` decides, with `DragOver` as the fallback |
| Demo | drag the slider from 100% to 125%: the window grows once to 1214 DIP on release |

#### WP-C4. Capture and image insert

| Field | Content |
|---|---|
| Goal | The report's Image, Screenshot and Capture inserts at any gap, through the capture-insert modal |
| Spec inputs | 05 2.18, 2.19, 7.13, 7.14 (capture insert), D-REP-10; 02 2.2.3, 2.2.5; 11 `ImportLimits`, D-IPC-13; Q-REP-5, Q-SHELL-17 |
| Deliverables | Core `CaptureTargetPicker`; App `CaptureInsertDialog`, `CaptureInsertViewModel` (modes per variant, targets loaded on open, area through `IAreaSelectionService`), the insert handlers: Image (`IFileDialogs`, `ImportLimits.Check` on `FileInfo.Length` before reading, `ApplyDurable(s => s.ImportStepAsync(...))`), Screenshot (`ApplyDurable` over `CaptureScreenshotAsync`), Capture (`StartAsync` with `InsertAt`; after the recording its result is adopted into the open session through `ApplyDurable`, a `Durable` change the report reconciles exactly like an `External` one, 05 EDGE-REP-43, ARCHITECTURE 7.4) |
| Tests | No Electron file. New: Core `Report/CaptureTargetPickerTests`; App `Report/DialogKeyboardTests` (capture dialog cases), `ProjectDetailStateTests` (`TextDraftBlocksRecordingAndExport` recording part, `CaptureGuardShowsInfoWithoutPrefix`) |
| Acceptance criteria | AC-CAP-16, AC-CAP-17, AC-CAP-18, AC-CAP-20, AC-REP-22, AC-REP-34, AC-IPC-9, AC-IPC-10 |
| Depends on | WP-C3, WP-B9 |
| Size | M |
| Risks and de-risking | A 70 MB file read before the size check (AC-IPC-10): the length check runs on the file system entry, measured with `dotnet-counters` |
| Demo | insert a screenshot at gap 2 of a 5-step project in Screen mode: one new step 3, no pill, the main window returns focused |

#### WP-C5. Step patch, render writer and durable store updates

| Field | Content |
|---|---|
| Goal | The one applier that invalidates stale renders, the atomic render writer with rollback, and the store's `UpdateStepAsync` and `MergeStepsAsync` |
| Spec inputs | 04 2.17, 2.18, 7.5, INV-EDIT-7, INV-EDIT-8, INV-EDIT-9, INV-EDIT-27, INV-EDIT-32, D-EDIT-5, D-EDIT-22; 01 7.8 (update and merge), EDGE-MODEL-47, EDGE-MODEL-51; ARCHITECTURE 7.6, 9.2 S2; Q-MODEL-22, Q-EDIT-8 |
| Deliverables | Core `ShotAI.Core.Rendering`: `StepPatch`, `Optional<T>`, `StepPatchValidator` (`parseStepPatch` and `parseClick` rules, `\z` anchors), `StepPatchApplier.ApplyAndInvalidate`, `IStepRenderWriter`, `StepRenderWriter` (through `AtomicFile`, receipt with rollback, refusal of any id that is not one safe path segment), `RenderWriteReceipt`, `RefusedRenderPathException`; `ProjectStore.UpdateStepAsync` and `MergeStepsAsync` (render first, manifest second, previous render restored if the manifest write fails), the `IStepRenderWriter renderWriter` parameter of the 01 7.8 `ProjectStore` constructor that WP-A6 left out, and `MergeIntoItselfException` (`cannot merge a step into itself`, thrown when `keepId == dropId` before the gate and before queuing, 01 7.8, 7.13; `StepNotFoundException` is WP-A7's); Core `ShotAI.Core.Json.JsValue` (`Truthy`, `ToNumber`, with 04 7.1's signatures; this WP owns the type, WP-D4 only uses it) and `JsPath.ExtName`; 01 Q-MODEL-22 marked decided by D-EDIT-22 |
| Tests | Port `src/main/step-render.test.ts` (`Rendering/StepPatchApplierTests`). New: `Rendering/StepPatchValidatorTests`, `StepRenderWriterTests`, `Store/StoreRenderTests` (including the merge-into-itself refusal); Platform `Rendering/RenderWriteJunctionTests` |
| Acceptance criteria | none owned (the non-segment id refusal is verified through the editor save in WP-C10) |
| Depends on | WP-A7 |
| Size | M |
| Risks and de-risking | A torn or orphaned render that carries fewer redactions than the manifest (D-EDIT-5): `StoreRenderTests.RenderWrittenBeforeManifest` and `ManifestFailureRestoresPreviousRender` |
| Demo | tests |

#### WP-C8. Editor document (Core)

| Field | Content |
|---|---|
| Goal | The editor's state machines as pure Core: tools, gestures, selection, transforms, inline text, crop box, zoom, keyboard |
| Spec inputs | 04 2.1 to 2.14, 7.2, 7.3 (7.3.1 to 7.3.4), D-EDIT-13 to D-EDIT-17, D-EDIT-20, D-EDIT-23, D-EDIT-24; Q-EDIT-9, Q-EDIT-11, Q-EDIT-14, Q-EDIT-18 |
| Deliverables | Core `ShotAI.Core.Editor`: `AnnotationKind`, `AnnotationView` with the six typed views and `UnknownAnnotationView`, `AnnotationFactory` (exact key order), `AnnotationEdits`, `AnnotationStyle` (formulas, `MarkerColorFor`), `EditorGeometry` (`ClampRectToImage`, `ComputeCropView`, `CropView`), `EditorDocument`, `EditorTool`, `EditorSelection`, `TransformMath`, `HitTesting`, `CssColor` |
| Tests | Port `src/renderer/editor/editor-geometry.test.ts` (`Editor/EditorGeometryTests`, plus the negative-zero assertion). New: `Editor/AnnotationFactoryTests`, `AnnotationStyleTests`, `AnnotationEditsTests`, `TransformMathTests`, `EditorDocumentTests` |
| Acceptance criteria | none owned |
| Depends on | WP-A3 |
| Size | M |
| Risks and de-risking | Konva semantics that are hard to see without a canvas: every row of 04's 2.6, 2.7, 2.9, 2.12 and 2.14 tables is a test row |
| Demo | tests |

#### WP-C6. Flatten and redaction bake

| Field | Content |
|---|---|
| Goal | The pure flatten: crop, redaction bake before any overlay, fail-closed pre-checks, box-average mosaic, marker bake |
| Spec inputs | 04 2.16, 3, 7.4, INV-EDIT-1 to INV-EDIT-5, D-EDIT-1, D-EDIT-2, D-EDIT-7, D-EDIT-9, D-EDIT-10, D-EDIT-18, D-EDIT-26; `macOS:Packages/EditorKit/Tests/EditorKitTests/FlattenTests.swift`; ARCHITECTURE 9.2 S1; Q-EDIT-2, Q-EDIT-3, Q-EDIT-7 |
| Deliverables | Core `ShotAI.Core.Rendering`: `PremultipliedImage`, `Flattener`, `FlattenRequest`, `FlattenMarker`, `FlattenException` (`UnbakeableRedaction`, `CropUnapplied`, `UnknownAnnotation`, `EncodeFailed`), `RedactionBaker` (solid opaque black, box-average pixelate with block at least 8, Gaussian soft pass with the sigma cap), `OverlayScene` and its primitives, `IOverlayRasterizer`, `IRenderCodec`, `Compositor`; test support `RecordingRasterizer` |
| Tests | No Electron file (Electron has no flatten tests). New: `Rendering/FlattenerTests` (the eleven macOS ports and the new cases of 04 8.2), `RedactionBakerTests`, `FlattenerPerfTests` (`Category=Perf`) |
| Acceptance criteria | AC-EDIT-3, AC-EDIT-4, AC-EDIT-30 |
| Depends on | WP-A3, WP-C8 (the flatten uses C8's `AnnotationView.HasUsableGeometry`, `CssColor.OrAccent` and `AnnotationStyle.ClickMarkerRadius`, 04 7.4 steps 0, 4 and 5) |
| Size | M |
| Risks and de-risking | Electron's silent no-bake cases (non-numeric `blockSize`, non-number or negative geometry) must fail closed natively (D-EDIT-2): `MalformedBlurGeometryFailsClosed` and AC-EDIT-30 |
| Demo | tests |

#### WP-C7. Render gate and pre-egress flatten

| Field | Content |
|---|---|
| Goal | The gate is the only way to pick an egress image, and every egress first brings renders up to date |
| Spec inputs | 04 2.19, 2.20, 7.6, 7.7, INV-EDIT-6, INV-EDIT-31, D-EDIT-3, D-EDIT-4, D-EDIT-6; 07 D-SOP-22; ARCHITECTURE 7.7, 9.2 S3, S4, R-ARCH-7, R-ARCH-8; Q-EDIT-20, Q-SOP-16 |
| Deliverables | Core `ShotAI.Core.Redaction`: `RenderGate` (static), `IRenderGate`, `RenderGateService`, `SendableRender`, `EgressVerb`, `RenderGateException` (with the `(string, Exception)` constructor); Core `ShotAI.Core.Rendering`: `IStepFlattener`, `StepFlattener` (`EnsureFlattenedAsync` with the `IProjectSession` overload and the direct-store overload, `FlattenStepAsync`); 05, 07, 09 and 11 already use these names (R-ARCH-7, R-ARCH-8) |
| Tests | Port `src/main/render-gate.test.ts` (`Redaction/RenderGateTests`). New: `Redaction/RenderGateMayRedactTests`, `Rendering/StepFlattenerTests`; Platform `Redaction/RenderGateJunctionTests` |
| Acceptance criteria | none owned (the invalidation criterion's manual export half completes in WP-D10) |
| Depends on | WP-C5, WP-C6, WP-C8 (the gate's `AnnotationView.From(e).MayRedact` and `StepFlattener`'s `AnnotationStyle.MarkerColorFor`, 04 7.6, 7.7), WP-A9 (the session overload takes `IProjectSession`, R-ARCH-5) |
| Size | M |
| Risks and de-risking | Unknown annotation types reaching egress (macOS #109): they count as possible redactions (D-EDIT-3) |
| Demo | tests |

#### WP-C9. Editor canvas and interaction

| Field | Content |
|---|---|
| Goal | The WPF editor overlay: canvas, tools, selection handles, crop, zoom and pan, the inline text box, keyboard |
| Spec inputs | 04 2.4 to 2.13, 7.10.1 to 7.10.5, D-EDIT-8, D-EDIT-17, D-EDIT-20, D-EDIT-21; 02 INV-CAP-7; ARCHITECTURE 5.4, 9.2 S6, R-ARCH-18; Q-EDIT-18, Q-EDIT-21 |
| Deliverables | App `ShotAI.App.Editor`: `EditorOverlayView` (in the main window's overlay layer), `EditorViewModel`, `EditorCanvas` (pointer mapping at every zoom, `WM_MOUSEHWHEEL`, Ctrl+wheel zoom), `AnnotationPainter` (the preview painter), `SelectionAdorner`, `InlineTextBox`, `BlurPreviewCache`, `EditorFactory`, `IColorPicker` and `Win32ColorPicker` (the App wrapper over the Platform dialog, 04 7.10.1), `EditorRegistration`; the report card's Edit entry (it passes the view's `IProjectSession` to `EditorFactory.Create`, R-ARCH-5); App `ShotAI.App.Threading.UiDeferral` (`src/ShotAI.App/Threading/UiDeferral.cs`, beside `WpfUiDispatcher`, the one allowlisted helper for focus and layout deferrals, ARCHITECTURE 2.4 and 14.9, 04 7.1, R-ARCH-18); Platform `ShotAI.Platform.Dialogs.Win32ColorDialog`, opened with `CC_ENABLEHOOK` and a hook that registers the dialog's HWND with `OwnWindowRegistry` on `WM_INITDIALOG`, before it is first shown (Q-EDIT-21) |
| Tests | No Electron file. New: App `Editor/EditorPointerMappingTests`, `ViewportStabilityTests`, `InlineTextBoxTests`, `EditorKeyboardFocusTests`, and a color-dialog case in `AllWindowsRegisteredTests` (Q-EDIT-21) |
| Acceptance criteria | AC-EDIT-12, AC-EDIT-13, AC-EDIT-14, AC-EDIT-15, AC-EDIT-16, AC-EDIT-17, AC-EDIT-34 |
| Depends on | WP-C8, WP-C2 |
| Size | M |
| Risks and de-risking | WPF pointer mapping under scroll and zoom (INV-EDIT-14): `EditorPointerMappingTests` at zoom 0.5, 1 and 2 in both views |
| Demo | draw a box, an arrow, a number and a text on a screenshot; resize a stamp from a corner and it stays round |

#### WP-C10. Editor save and overlay rasterizer

| Field | Content |
|---|---|
| Goal | Save is durable and synchronous: flatten from the original, write render then manifest, report success only after both, one painter for preview and bake |
| Spec inputs | 04 2.15, 7.4 (rasterizer seam), 7.10.6, 7.10.7, 7.12, 7.13, INV-EDIT-10, INV-EDIT-32, D-EDIT-12, D-EDIT-25, D-EDIT-27; 05 7.11 (cache key); ARCHITECTURE 7.6, R-ARCH-18, R-ARCH-21; Q-EDIT-1, Q-EDIT-4, Q-EDIT-16 |
| Deliverables | App `WpfOverlayRasterizer : IOverlayRasterizer` (banded for wide scenes), `StaRenderThread` (its own dispatcher, allowlisted), the save pipeline in `EditorViewModel` (prepare, flatten on the pool, `ApplyDurable(s => s.UpdateStepAsync(path, id, patch, png))`, close on success, stay open with the message on failure, canvas read-only while saving), the marker colour default `AnnotationStyle.MarkerColorFor(step)` (Q-EDIT-1, noted for the release notes); Platform `WicImageCodec : IRenderCodec` (magic bytes first, then only the built-in PNG or JPEG decoder, never `CreateDecoderFromStream`, R-ARCH-21, D-EDIT-27; EXIF orientation, premultiplied, sRGB through `IWICColorTransform`) |
| Tests | No Electron file. New: Core `Editor/EditorSaveTests`; Platform `Capture/RenderCodecTests` (it tests `WicImageCodec`, so it mirrors `ShotAI.Platform.Capture`, 04 8.2, ARCHITECTURE 2.4; including `RejectsNonPngJpegMagic` and `UsesBuiltInDecoder`); App `Editor/WpfOverlayRasterizerTests`, `PainterParityTests`, `Report/ReportImageLoaderTests.ReloadsOnRenderRevOnly`, and the render-level half of 04's end-to-end test as `Editor/RenderRedactionProofTests` (no 4 by 4 block of original region pixels in the saved render) |
| Acceptance criteria | AC-EDIT-7, AC-EDIT-18, AC-EDIT-19, AC-EDIT-20, AC-EDIT-27, AC-EDIT-31, AC-EDIT-32, AC-EDIT-33, AC-EDIT-35, AC-REP-10, AC-REP-27, AC-REP-38 |
| Depends on | WP-C9, WP-C7, WP-B5 |
| Size | M |
| Risks and de-risking | `RenderTargetBitmap` size limits, text mode and software-rendering speed on the ARM64 VM are unverified (Q-EDIT-16): `WpfOverlayRasterizerTests` pins them and AC-EDIT-28 is measured in M-C |
| Demo | add a blur, save: the editor closes only after `export\.render\<id>.png` exists with the blur baked, and the report shows it |

#### WP-C11. Sensitive-text detection, OCR and auto-redact

| Field | Content |
|---|---|
| Goal | Auto-redact with Windows OCR and the ported detectors, with honest notices when OCR is unavailable |
| Spec inputs | 04 2.14, 2.21, 2.22, 7.8, 7.9, D-EDIT-11, INV-EDIT-18 to INV-EDIT-20; ARCHITECTURE 3.3 (OCR Feature on Demand); Q-EDIT-5, Q-EDIT-6, Q-EDIT-12, Q-EDIT-13, Q-EDIT-15, Q-EDIT-17, Q-EDIT-19 |
| Deliverables | Core `ShotAI.Core.Redaction`: `SensitiveTextDetector` (`RegexOptions.ECMAScript`, order-dependent merge), `OcrTextLine`, `OcrTextWord`, `IOcrEngine`, `OcrScanResult`, `OcrScanStatus`, `ISensitiveRegionScanner`, `SensitiveRegionScanner`; Platform `ShotAI.Platform.Ocr.WindowsOcrEngine` (en-US first, then profile languages, max-dimension scaling, one scan at a time); App auto-redact button, suggestions and the Unavailable, Failed and none-found notices; a checked-in synthetic OCR fixture PNG; Electron test-only recorder `src/shared/redact-detect-golden.test.ts` and its committed `Golden/redact/` output (2.7) |
| Tests | Port `src/shared/redact-detect.test.ts` (`Redaction/SensitiveTextDetectorTests`, with the macOS ports and new cases). New: `Redaction/SensitiveRegionScannerTests`, `OcrLoggingTests`, the detector golden test over the 50 recorded line sets; Platform `Ocr/WindowsOcrEngineTests` |
| Acceptance criteria | AC-EDIT-1, AC-EDIT-2, AC-EDIT-11, AC-EDIT-21, AC-EDIT-22, AC-EDIT-23, AC-EDIT-24 |
| Depends on | WP-C10 |
| Size | M |
| Risks and de-risking | Windows OCR may split hyphenated tokens (Q-EDIT-19) or recall less than Tesseract (Q-EDIT-6): measure on the fixture and the corpus; add the word-join rule and the 2x upscale only if measured. The recorder needs Tesseract words from Electron's OCR path under Node: if that is not practical, record them from a dev run of the Electron app and say so in the golden README |
| Demo | Auto-redact on the fixture adds solid redactions over the SSN, card and API key |

#### WP-C12. Merge

| Field | Content |
|---|---|
| Goal | Right-click merge: the kept step is re-baked from its raw screenshot with both rings and its redactions, the dropped step removed, in one queue job |
| Spec inputs | 05 2.16, 7.14 (merge), INV-REP-19; 04 D-EDIT-18, INV-EDIT-8; Q-EDIT-10, Q-SOP-7 |
| Deliverables | Core `MergePlanner`, `MergePlan`; App `MergeCoordinator` (flatten through `IStepFlattener.FlattenStepAsync` off the UI thread, then `ApplyDurable(s => s.MergeStepsAsync(...))`; only the two steps are locked), the merge banner |
| Tests | No Electron file. New: Core `Report/MergePlannerTests`, `ReportEditStateTests.MergeLocksBothSteps` |
| Acceptance criteria | AC-EDIT-26, AC-REP-20, AC-REP-21 |
| Depends on | WP-C7, WP-C2 |
| Size | S |
| Risks and de-risking | Parity keeps `captionEditedByUser` on a merge (Q-EDIT-10, answered by Q-SOP-7) |
| Demo | merge a right-click step into its selection step: one step with both rings and the `\u2192` caption |

#### WP-C13. Phase C exit

| Field | Content |
|---|---|
| Goal | Run milestone M-C: visual parity of flattened output, cross-app editing, keyboard reachability, the editor budgets |
| Spec inputs | section 4 M-C; 04 9; ARCHITECTURE 11 (PB-6, PB-10, PB-11), 12.7 (phase C set); Q-EDIT-3, Q-EDIT-4, Q-EDIT-6, Q-EDIT-16, Q-EDIT-19 |
| Deliverables | the phase C tracking issue completed |
| Tests | none new |
| Acceptance criteria | AC-MODEL-24, AC-EDIT-8, AC-EDIT-9, AC-EDIT-10, AC-EDIT-28, AC-EDIT-29 |
| Depends on | WP-C1 to WP-C12 |
| Size | S |
| Risks and de-risking | Text placed lower than Electron's (Q-EDIT-4): within `0.15 * fontSize` passes AC-EDIT-10; beyond it, add the em-box offset in a follow-up on WP-C10 |
| Demo | the tracking issue |

---

### 3.4 Phase D: SOP generation, auth and exports

Goal (feasibility): the C# SDK client with review-before-send and the render gate, MSAL.NET with WAM and `WorkloadIdentityCredentials`, the three-leg connection test; HTML and Markdown verbatim, PDF through WebView2, Word and PowerPoint through the Open XML SDK, AVIF through libavif, the shareable package. Exit: HTML and Markdown byte-identical to Electron's with replayed images (Q-EXP-14), PDF, Word and PowerPoint visually equivalent, the end-to-end redaction proof, a live-tenant generation.

#### WP-D1. Federation config and managed policy

| Field | Content |
|---|---|
| Goal | Federation values come only from the HKLM policy key over baked build values, validated all or nothing, re-read on every status request |
| Spec inputs | 08 2.2 to 2.7, 2.18, 7.2, 7.3, 7.4, INV-AUTH-1 to INV-AUTH-9, EDGE-AUTH-36, EDGE-AUTH-37, 8.1, 8.4, 8.5, 8.7; ARCHITECTURE 9.2 S14, 10.4; Q-AUTH-10, Q-AUTH-12, Q-AUTH-18 |
| Deliverables | Core `ShotAI.Core.Auth`: `FederationKeys`, `FederationPolicy`, `FederationConfig` (redacted `ToString`), `FederationConfigResult`, `RawFederationConfig`, `FederationNormalizer`, `FederationConfigValidator`, `FederationSources`, `PolicyValue`, `PolicyValueParser`, `IPolicyValueSource`, `BakedFederationParser`, `IBakedFederationSource`, `FederationConfigProvider`; Platform `ShotAI.Platform.Auth.RegistryPolicySource` (64-bit view, `REG_SZ` only); App `ShotAI.App.Auth.EmbeddedBakedFederationSource` and the `ShotAI.App.csproj` fragment that embeds `ShotAIFederationFile` (default `dotnet/federation.local.json`, fallback `src/main/entra/federation.local.json`) as `ShotAI.Federation.Baked.json`; Electron test-only generator `src/main/entra/federation-golden.test.ts` and the committed `Golden/auth/` results (placeholder ids only) |
| Tests | Port `src/main/entra/admx-contract.test.ts` (`Auth/AdmxContractTests`, reading `Intune/Windows/` in place), `src/main/entra/config-sources.test.ts` (`PolicyValueParserTests`, `FederationSourcesTests`, `FederationConfigProviderTests`, Platform `Auth/RegistryPolicySourceTests`; the reg.exe line-format cases are ELECTRON-ONLY), `src/main/entra/config-validate.test.ts` (`FederationConfigValidatorTests`), `src/main/entra/federation-local.test.ts` (`FederationLocalFileTests`, skipped without a local file). New: `BakedFederationParserTests`, `FederationPolicyTests`, the golden comparison test of AC-AUTH-3; App `Auth/EmbeddedBakedFederationSourceTests` |
| Acceptance criteria | AC-AUTH-1, AC-AUTH-2, AC-AUTH-3 |
| Depends on | WP-A12 |
| Size | M |
| Risks and de-risking | Organization-identifying values in the public repository (INV-AUTH-6): fixtures use `11111111-...`, `fdrl_EXAMPLE01` and friends; `git diff --stat main -- Intune/` stays empty (AC-AUTH-1) |
| Demo | tests; a scratch `reg add` under HKCU through the test constructor shows `MissingKeyIsEmpty` and `EnumeratesRealValueKinds` |

#### WP-D2. Shared HTTP, client factory, API key store and environment sanitization

| Field | Content |
|---|---|
| Goal | One system-proxy HTTP stack, the only Anthropic client factory (pinned host, explicit credentials), the DPAPI key store logic, Entra session logic over a seam, and a sanitized process environment |
| Spec inputs | 08 2.8, 2.13, 2.15, 7.5 (`EntraSession` in Core), 7.9, 7.11, 7.14, INV-AUTH-10 to INV-AUTH-16, INV-AUTH-25 to INV-AUTH-29, INV-AUTH-35, risk R3; 07 7.6 (transport), INV-SOP-1, INV-SOP-2; ARCHITECTURE 4.2 step 5, 9.2 S11, 9.4, R-ARCH-15; Q-ARCH-3, Q-ARCH-4, Q-AUTH-2, Q-AUTH-8, Q-AUTH-13, Q-AUTH-17, Q-SOP-17, Q-SOP-20, Q-PKG-16 |
| Deliverables | Core `ShotAI.Core.Net.SharedHttp : ISharedHttp` (one `SocketsHttpHandler` with no Anthropic-specific handler, exposed as `Handler`; system proxy, `DefaultProxyCredentials = DefaultCredentials`; `CreateApiClient()`, a new `HttpClient` per Anthropic client with `disposeHandler: false` and `Timeout = InfiniteTimeSpan`; and the long-lived `Exchange` client, guarded by its own `AnthropicHostGuardHandler` over the shared handler, `Timeout` 60 s, 08 7.14); Core `ShotAI.Core.Auth`: `IMsalGateway`, `IMsalGatewayFactory`, `EntraAppConfig`, `EntraAccount`, `EntraToken`, `EntraUiRequiredException`, `EntraSignInCanceledException`, `EntraSession`, `EntraIdentityTokenProvider` (silent only), `IAnthropicClientFactory`, `AnthropicClientFactory` (`ApiKey`, `AuthToken`, `BaseUrl` always explicit, `Timeout` 10 minutes, `MaxRetries = null`, 08 7.9; this WP ships `Handlers = [new AnthropicHostGuardHandler()]`, and WP-D6 appends `new ResponseHeaderCaptureHandler()`, which it creates), `AnthropicHostGuardHandler` (a fresh instance per client: `https`, `api.anthropic.com`, port 443), `IApiKeyStore` (internal), `ApiKeyStore` over `ISecretProtector` (file `secrets.dpapi.json`), `ApiKeyStatus`, `ApiKeySource`, `AuthMode`, `SignInRequiredException`, `ApiKeyStoreException`; App composition-root step 5 (remove `ANTHROPIC_CUSTOM_HEADERS` and the proxy variables, log the names at Warning, never the values); package `Anthropic` `12.50.0` in Core |
| Tests | No Electron file (the mode-selection cases of `auth-core.test.ts` land with the facade in WP-D4). New: Core `Auth/AnthropicClientFactoryTests` (in the `ProcessEnvironment` collection; `CustomHeadersEnvironmentHasNoEffect` in a child process; `EachClientGetsFreshHandlers`; `OptionsCarryExplicitTimeoutAndRetries`), `AnthropicHostGuardHandlerTests`, `EntraSessionTests`, `EntraIdentityTokenProviderTests`, `ApiKeyStoreTests`, `SharedHttpTests`; App `Auth/CompositionTests` (`ANTHROPIC_CUSTOM_HEADERS` absent after startup) |
| Acceptance criteria | AC-AUTH-9, AC-AUTH-10, AC-AUTH-15, AC-AUTH-34, AC-ARCH-6 |
| Depends on | WP-A10, WP-A12 |
| Size | M |
| Risks and de-risking | The SDK auto-resolves environment and profile credentials and disposes its `HttpClient` (08 findings): explicit options, a per-client `HttpClient`, and `AnthropicClientFactoryTests` as the tripwire for SDK upgrades (V9) |
| Demo | tests; `EnvironmentCannotRedirectOrReplaceCredential` passes with every `ANTHROPIC_*` variable of AC-AUTH-9 set |

#### WP-D3. Entra sign-in and the token cache (Platform)

| Field | Content |
|---|---|
| Goal | MSAL.NET with the WAM broker, the DPAPI extension cache with its cross-process lock under `%LOCALAPPDATA%\LFI\shotAI\entra\`, and DPAPI for the key file |
| Spec inputs | 08 2.9, 2.10, 7.5 (`MsalGateway`), 7.6, 7.11 (`DpapiSecretProtector`), 7.15, 7.16, 7.18, INV-AUTH-11, INV-AUTH-25, risks R1 and R2; ARCHITECTURE DL5, 10.1, R-ARCH-13; Q-AUTH-3, Q-AUTH-4, Q-AUTH-5, Q-AUTH-15 |
| Deliverables | Platform `ShotAI.Platform.Auth`: `MsalGatewayFactory`, `MsalGateway` (tenant authority, broker enabled, no client capabilities, PII off, the shared `MsalHttpClientFactory`, interactive calls on the UI thread through `IUiDispatcher.InvokeAsync`, forced silent refresh after interactive success), `MsalCacheRegistration` (memory-only and logged once when persistence cannot be verified), `MsalHttpClientFactory`, `MsalLogBridge` (Warning and above, `msal: ` prefix), `DpapiSecretProtector` (CurrentUser, with entropy); packages `Microsoft.Identity.Client`, `Microsoft.Identity.Client.Broker`, `Microsoft.Identity.Client.Extensions.Msal`, `System.Security.Cryptography.ProtectedData` (08 7.6, 7.16 and AC-AUTH-21 already state the R-ARCH-13 path, 1.5) |
| Tests | Port `src/main/entra/cache-plugin.test.ts` (Platform `Auth/MsalCachePersistenceTests`, Core `Auth/TokenCachePersistenceTests`, `SourceScanTests.NoUnprotectedMsalCache`; five cases ELECTRON-ONLY per 08 8.3). New: Platform `Auth/MsalGatewayTests`, `DpapiSecretProtectorTests`; Core `Auth/SourceScanTests` |
| Acceptance criteria | AC-AUTH-11, AC-AUTH-20 |
| Depends on | WP-D2, WP-A5 |
| Size | M |
| Risks and de-risking | `MsalCacheHelper` on an undecryptable file or a failed write is assumed, not verified (Q-AUTH-5): `MsalCachePersistenceTests` is written first; if it fails, implement the custom `ProtectedData` cache with the `Local\shotAI.entra.cache` mutex. The WAM redirect URI is a tenant change IT must make before the pilot (Q-AUTH-3): raised with IT in this WP |
| Demo | tests on x64 and arm64 |

#### WP-D4. Federation exchange, connection test and the auth facade

| Field | Content |
|---|---|
| Goal | `IAuthService` is the UI's only view of auth (status, sign-in, sign-out, key status, set, clear, three-leg test), exposing no token or key |
| Spec inputs | 08 2.11, 2.12, 2.14, 2.16, 2.17, 2.19, 7.7, 7.8, 7.10, 7.12, 7.13, 7.17, 7.19, INV-AUTH-17 to INV-AUTH-24, INV-AUTH-30 to INV-AUTH-36; 07 2.5, 7.6 (`SopModelProbe`), 7.12 (`SopErrorMapper`), INV-SOP-13; 11 7.3.7, INV-IPC-1, INV-IPC-2, INV-IPC-12; ARCHITECTURE 9.2 S9, R-ARCH-2, R-ARCH-3; Q-AUTH-6, Q-AUTH-7, Q-AUTH-16, Q-IPC-1, Q-IPC-2, Q-SOP-15 |
| Deliverables | Core `FederationExchange` (RFC 7523, beta header, 60 s timeout, cancelable), `FederationExchangeException`, `MintedToken` (redacted `ToString`), `FederationErrorText` (`Explain`, `ForSdk`), which uses `JsValue.ToNumber` (created by WP-C5; if WP-C5 has not merged, create `JsValue` with exactly 04 7.1's signatures and `Json/JsValueToNumberTests`, and WP-C5 then adds only `Truthy`), `JwtRoles`, `ConnectionTester` (sign-in, Claude access, Claude API legs; the models call through `IAnthropicClientFactory` and 07's internal `SopModelProbe.RetrieveAsync`, which this WP creates, 07 7.6), the leg-3 error text through 07's `SopErrorMapper.Map(e, mode, headers: null)`, which this WP creates with every row of 07 7.12 except row 15 together with `RateLimitClassifier` and the `ResponseHeaderCapture` record (row 15 matches exception types of WP-C7, WP-D5 and WP-D6, which the connection test never raises, and WP-D6 adds it, so the auth lane does not wait for the SOP lane), `AuthStatus` (seven members), `AuthStatusMode`, `ConnectionLeg`, `TestConnectionResult`, `SignInOutcome`, `IAuthService`, `AuthService` (`GetStatusAsync` invalidates the policy cache, `AuthStatusChanged`), `SupportUrlAllowlist : ISupportUrlAllowlist` (replaces WP-A19's stub), `FederationNotConfiguredException`, `EntraSignInFailedException`; `dotnet/tools/ShotAI.WifProbe` (legs 0 to 4, `--effective`, not shipped; per 08 7.19 it builds its own container from `AddShotAICore` and `AddShotAIPlatform` with its own `IAppPaths` over a fresh temporary folder deleted at exit, and `--use-app-cache` for AC-AUTH-22); 06 and 07 already use 08's interface names (R-ARCH-2, R-ARCH-3) |
| Tests | Port `src/main/entra/auth-core.test.ts` (`Auth/AuthServiceModeSelectionTests`, `VerifyFederationTests`), `src/main/entra/federation.test.ts` (`Auth/FederationExchangeTests`, `JwtRolesTests`) and `src/main/entra/federation-cache-wiring.test.ts` (`Auth/AuthStatusServiceTests`, `AdminDocConsistencyTests`, `ServiceBoundary/StatusRereadsPolicyTests`; the text-matching cases are ELECTRON-ONLY). `src/main/entra/net-module.test.ts` is ELECTRON-ONLY, its intents covered by `SharedHttpTests` (WP-D2) and `MsalGatewayTests` (WP-D3). New: `ConnectionTesterTests`, `FederationErrorTextTests`, `AuthErrorTypeTests`, `JwtRolesExtraTests`, `AuthStatusShapeTests`, `AuthFacadeSurfaceTests`, `SupportUrlAllowlistTests`, `MintedTokenTests`, `ServiceBoundary/AuthSurfaceTests`, `Json/JsValueToNumberTests` (the 08 7.7 table), `Sop/SopErrorMapperTests` for the rows this WP lands |
| Acceptance criteria | AC-AUTH-8, AC-AUTH-12, AC-AUTH-13, AC-AUTH-14, AC-AUTH-18, AC-AUTH-22, AC-AUTH-30, AC-IPC-3 |
| Depends on | WP-D1, WP-D2, WP-D3, WP-A19 (the `ISupportUrlAllowlist` interface and stub this WP replaces) |
| Size | M |
| Risks and de-risking | The SDK wraps provider errors in `WorkloadIdentityException` whose message embeds a redacted body (08 finding): `FederationErrorText.ForSdk` walks the chain and maps by status (INV-AUTH-24); `FederationErrorTextTests` uses the SDK's real constructors. AC-AUTH-22 and AC-AUTH-30 need the live tenant: the maintainer runs the probe |
| Demo | `ShotAI.WifProbe` legs 0 to 4 pass against the live tenant (maintainer machine) |

#### WP-D5. SOP request, schema and apply

| Field | Content |
|---|---|
| Goal | The request Claude receives is byte-identical to Electron's (prompt, blocks, schema), assembled only from settled, gated renders; apply and revert are pure operations |
| Spec inputs | 07 2.2, 2.3 (2.3.1 to 2.3.7), 2.8 (2.8.1 to 2.8.3), 2.9 (2.9.1 to 2.9.7), 2.13, 3, 7.3, 7.4, 7.5, 7.9, INV-SOP-3, INV-SOP-6 to INV-SOP-12, INV-SOP-28, INV-SOP-29, D-SOP-1, D-SOP-13, D-SOP-14, D-SOP-15, D-SOP-19, D-SOP-22; ARCHITECTURE 7.7, R-ARCH-4, R-ARCH-16; Q-SOP-1, Q-SOP-3, Q-SOP-4, Q-SOP-6, Q-SOP-12, Q-SOP-14, Q-SOP-21 |
| Deliverables | Core `ShotAI.Core.Sop`: `SopPrompt` (base prompt golden, tone fragments, title instruction), `SopInput`, `SopRequestAssembler(IProjectService projects, IPathProbe probe)` (R-ARCH-4): `GetProjectForReadAsync`, then images only through `RenderGate.ResolveSendableRender(..., EgressVerb.Send, probe)`; the settle (`IProjectSettle.WhenSettledAsync`) is `ClaudeService`'s, before client creation and assembly (WP-D6, 07 7.8), never the assembler's (07 7.4 already takes `IProjectService`, R-ARCH-4); `SopRequest`, `SopContentPart`, `SopEditPlan`, `SopStepEdit`, `SopEditSchema` (the measured wire schema, key order kept), `SopPlanParser`, `SopPlanParseResult`, `EffectiveEdits`, `ApplySopPlanOperation`, `RevertSopOperation` (invalidates renders changed since the snapshot), `SopNoLandingException`, `NothingToRevertException`, `SopMessages`, `SopEstimate` (3 and 15 USD per MTok, Q-SOP-1); `Golden/sop/` (base prompt from `src/main/claude-service.ts:181-199`, UTF-8 without BOM, no trailing newline) |
| Tests | Port `src/main/sop-input.test.ts` (`Sop/SopInputTests`), `src/main/sop-prompt.test.ts` (`Sop/SopPromptTests`), `src/main/sop-landing.test.ts` (`Sop/SopLandingTests`, `SopMessagesTests`; its `schema on the wire` case lands in WP-D6) and `src/main/intro-authored.test.ts` (`Sop/IntroAuthoredTests`). New: `SopPromptTextTests`, `SopRequestAssemblerTests`, `SopEditSchemaTests` (`MatchesElectronWireSchema`, `NoOtherConstraintKeywords`), `SopPlanParserTests`, `SopApplyTests`, `SopRevertTests` |
| Acceptance criteria | AC-SOP-2, AC-SOP-3 |
| Depends on | WP-A9, WP-A10 (`SopSettings`, `SopTone` and `SopCatalog`, which `BuildSystemPrompt` reads, 07 7.2, 7.3), WP-C7 |
| Size | M |
| Risks and de-risking | Revert restoring a stale `flattened` path (07 risk): D-SOP-13 and `InvalidatesRendersChangedSinceSnapshot`. AC-SOP-3's comparison script runs Electron's `assembleRequest` once in the PR and is not committed |
| Demo | tests |

#### WP-D6. SOP transport and service

| Field | Content |
|---|---|
| Goal | Streaming generation through the real SDK, with Electron's error wording, 429 classification, the 250 ms progress throttle and cancel |
| Spec inputs | 07 2.1, 2.4 to 2.7, 2.10 (2.10.1 to 2.10.4), 2.11, 7.2, 7.6, 7.7, 7.8, 7.12, 7.13, D-SOP-2 to D-SOP-8, D-SOP-16, D-SOP-18, D-SOP-20, D-SOP-21, INV-SOP-1, INV-SOP-2, INV-SOP-4, INV-SOP-5, INV-SOP-13, INV-SOP-21; ARCHITECTURE 9.2 S18, R-ARCH-15; Q-SOP-2, Q-SOP-8, Q-SOP-9, Q-SOP-11, Q-SOP-18 |
| Deliverables | Core `ShotAI.Core.Sop.Transport`: `SopWireMapper` (`CreateStreaming`, `CountTokens`, `OutputConfig` with `JsonOutputFormat.FromRawUnchecked`), `SopStreamConsumer` (`TimeProvider` throttle), `SopErrorMapper` completed with row 15 of 07 7.12 (WP-D4 created it with the other rows, `RateLimitClassifier` and the `ResponseHeaderCapture` record), `ResponseHeaderCaptureHandler`, and `new ResponseHeaderCaptureHandler()` appended to the `Handlers` list of 08's `AnthropicClientFactory` (08 7.9; WP-D2 shipped the factory with the guard only); Core `IClaudeService`, `ClaudeService` (`EstimateAsync`, `GenerateAsync` with `IProgress<SopProgress>`, `Cancel`; the master switch checked before any credential, file or network access), `SopProgress`, `SopStage`, `SopResponse`, `SopGeneration` (the `GenerateAsync` result, 07 7.8; `SopEstimate` is WP-D5's). 07 7.1, 7.6 and 7.8 already use 08's `IAnthropicClientFactory` with per-client handlers (R-ARCH-15) and have no `IClaudeService.TestConnectionAsync` (R-ARCH-3), 1.5 |
| Tests | Port `src/main/claude-error-messages.test.ts` (`Sop/SopErrorMessageTests`). New: `SopStreamConsumerTests`, `ClaudeServiceTests` (including `NoNullOptionalFieldsOnWire`, `CancelWithNothingInFlight`, `DisposesClientPerOperation`, 07 8.6), `EgressPinTests` (including `NoAnthropicHandlerOnSharedHandler`; `HandlerRejectsForeignHost` targets 08's `AnthropicHostGuardHandler`), `SopErrorMapperTests` (completed with row 15), `RateLimitHeaderCaptureTests`, `MasterSwitchTests`, `SopLoggingTests`, `SopEditSchemaTests.MinItemsReachesTheWire`, `ServiceBoundary/FireAndForgetTests` |
| Acceptance criteria | AC-SOP-1, AC-SOP-4, AC-SOP-14, AC-SOP-22, AC-IPC-23 |
| Depends on | WP-D5, WP-D2 |
| Size | M |
| Risks and de-risking | Whether `ClientOptions.Handlers` sees every retry attempt is UNVERIFIED (R-ARCH-15): `RateLimitHeaderCaptureTests.HeadersReachClassifierThroughSdk` decides; if not, move the capture into the per-client `HttpClient` chain. The former `ClaudeClientFactoryTests` of 07 is superseded by 08's `AnthropicClientFactoryTests` (WP-D2) |
| Demo | tests through the real SDK with a fake inner handler serving SSE |

#### WP-D7. Settings AI tab

| Field | Content |
|---|---|
| Goal | The AI tab's API key and Microsoft sign-in groups, driven only by `IAuthService`, appearing and disappearing with policy without a restart |
| Spec inputs | 06 2.25, 7.12 (AI rows), INV-HOME-30, INV-HOME-32, INV-HOME-42; 08 2.16, 2.17, 7.5 (failure modes table), 7.12, risk R1; ARCHITECTURE 9.2 S9; Q-AUTH-9, Q-AUTH-19 |
| Deliverables | App `AiSettingsViewModel` (write-only `PasswordBox`, status text, `Sign in with Microsoft` with the owner HWND, `Sign in again`, `Sign out`, `Test connection` with leg labels, the Request access link through `IExternalLinks`, the `Use my own Anthropic API key instead` disclosure); the auth rows of `SettingsText` (`KeyStatus`, `ConnectionMessage`, `AiHint`, `AiOff`) |
| Tests | No Electron file. New: Core `SettingsUi/SettingsTextTests` (auth rows); App `Settings/SettingsViewModelTests` (`KeyFieldWriteOnly`, `OpenReadsFreshAuthStatus`, `AuthStatusChangedRefreshes`, `SignInCanceledShowsNothing`, `AccountOnlyInSettings`, `ChangeClearsTest`, `TestResultSurvivesUnchangedBlur`), `Settings/SettingsViewTests` (`UnconfiguredShowsNoEntra`, `FederatedShowsSignInAndFallback`, `KeyFallbackIsExpandCollapse`), `Auth/CompositionTests.AuthServicesAreLazy` |
| Acceptance criteria | AC-HOME-25, AC-HOME-26, AC-HOME-27, AC-HOME-36, AC-AUTH-4, AC-AUTH-5, AC-AUTH-6, AC-AUTH-7, AC-AUTH-16, AC-AUTH-19, AC-AUTH-21, AC-AUTH-23, AC-AUTH-24, AC-AUTH-25, AC-AUTH-32, AC-AUTH-33, AC-IPC-11 |
| Depends on | WP-D4, WP-B10 |
| Size | M |
| Risks and de-risking | WAM parenting and cancel behavior (AC-AUTH-23, AC-AUTH-24) need a device with the redirect URI registered: run on both architectures (08 risk R1). The browser fallback (AC-AUTH-32) needs a WAM-unavailable device or a `SHOTAI_NO_BROKER` test build; the missing-redirect-URI error (AC-AUTH-33) needs a placeholder test registration, never the pilot's, and its run records the observed MSAL exception type, `ErrorCode` and message in 08 7.5, closing Q-AUTH-19 |
| Demo | on a machine with the policy values, Settings shows `Microsoft sign-in`; signing in shows `Signed in as` the UPN |

#### WP-D8. SOP panel

| Field | Content |
|---|---|
| Goal | Generate, review before send, progress, apply, regenerate and revert from the project's command bar |
| Spec inputs | 07 2.2 and 2.9 (the prompt tail and the untouched callouts of AC-SOP-24, built in WP-D5 and checked here end to end), 2.12, 7.10, D-SOP-8, D-SOP-9, D-SOP-10, D-SOP-12, D-SOP-15, D-SOP-17, D-SOP-23, D-SOP-24; 04 AC-EDIT-25 (flatten first); ARCHITECTURE 7.7, R-ARCH-16; Q-SOP-5, Q-SOP-19 |
| Deliverables | App `ShotAI.App.Sop`: `SopPanelViewModel` (with the `SopChanged` event, raised on the UI thread after a completed apply or revert, which 05 forwards, 07 7.10) and `SopPanelViewModelFactory.Create(IProjectSession)` (dependencies per R-ARCH-16 and 07 7.10, including `IUiDispatcher` and `ILogger<SopPanelViewModel>`), `SopPanelView`, `SopPreparingOverlay`, `SopReviewDialog`, `SopReviewViewModel`, `SopReviewItemViewModel`, `SopProgressDialog`, all in the overlay layer; Core `SopText`; `EnsureFlattenedAsync` before the estimate and before generation; apply and revert through `session.Apply` |
| Tests | No Electron file. New: App `Sop/SopPanelViewModelTests` (including `RefreshesOnAuthStatusChanged` and `RaisesSopChangedAfterApplyAndRevert`, 07 8.6), `Sop/SopReviewViewModelTests`; Core `Sop/SopTextTests` |
| Acceptance criteria | AC-CAP-30, AC-SOP-5, AC-SOP-8, AC-SOP-9, AC-SOP-10, AC-SOP-11, AC-SOP-12, AC-SOP-13, AC-SOP-15, AC-SOP-18, AC-SOP-24 |
| Depends on | WP-D6, WP-D7, WP-C2 |
| Size | M |
| Risks and de-risking | A long `Retry-After` wait holding the dialog (Q-SOP-8): Cancel works during generation (D-SOP-8); strip values over 60 s only if pilots report hangs |
| Demo | generate an SOP with an API key: review shows the thumbnails and the captions that are sent; the captions change after apply |

#### WP-D9. Export foundations and golden fixtures

| Field | Content |
|---|---|
| Goal | The fail-closed collector, export geometry, byte-exact CSS, names and the created line, and the Electron golden fixtures every later export WP compares against |
| Spec inputs | 09 2.1 to 2.5, 2.9, 2.10 (CSS), 2.15, 2.18 to 2.20, 3 (3.1 to 3.4), 7.1 to 7.4, 7.9, D-EXP-10, D-EXP-11, D-EXP-22, D-EXP-23; ARCHITECTURE 12.4, R-ARCH-9, R-ARCH-14, R-ARCH-17; Q-EXP-5, Q-EXP-6, Q-EXP-12, Q-EXP-14, Q-EXP-24 |
| Deliverables | Core `ShotAI.Core.Export`: `ExportFormat`, `ExportFormats`, `ExportProgress`, `ExportResult`, `PackageResult`, `ExportItem`, `ShotItem`, `TextItem`, `StepCollector` (through `IRenderGate`), `IExportFileProbe`, `ManagedExportFileProbe`, `ExportGeometry`, `CropRect`, `EmbedPolicy`, `ExportCss` (`DocCss`, `PlainCss`, built line by line with `\n`), `ExportTheme`, `ExportText`, `ExportNames`, `CreatedLine`, `IReportBylineSource`, `IAppBrandSource`, `IBrandFontSource`, `ExportException`, `ExportMessages`; goldens `Golden/export/css/{doc,plain}-{brand}-{scale}.css` (Node 22 run of the Electron generators, SHA-256 per 09 3.4), `Golden/export/fixtures/{mixed-default,lfi-065,unknown-pin-125,byline}/` from the Electron `SHOTAI_EXPORT_GOLDENS` run mode (2.7, Q-PLAN-1) including the recorded `InlineImage` lists, created lines, `.docx` and `.pptx`; the WP-A3 `.gitattributes` golden entries extended to any new extension these fixtures add (text types `eol=lf`, everything else `binary`). 09 already uses the R-ARCH-4, R-ARCH-8 and R-ARCH-14 names and the lower-case `Golden/export/` paths (2.7, 1.5) |
| Tests | Port `src/main/export-css.test.ts` (`Export/ExportCssTests`, with the corrected print regex of EDGE-EXP-46), `src/main/export-geometry.test.ts` (`Export/ExportGeometryTests`) and `src/shared/export-theme.test.ts` (`Export/ExportThemeTests`; its two packaging cases move to WP-E2 `Packaging/BrandFontShipsTests`, its print-copy case to WP-D12 `PrintHtmlTests`). New: `ExportCssGoldenTests` (also on the Windows job, catching a CRLF checkout), `StepCollectorTests`, `ZoomCropTests`, `ExportTextTests`, `ExportNamesTests`, `CreatedLineTests` |
| Acceptance criteria | AC-EXP-1, AC-EXP-19, AC-EXP-24 |
| Depends on | WP-A17, WP-A4, WP-C7 |
| Size | M |
| Risks and de-risking | The golden run mode touches Electron's main process (Q-PLAN-1); Electron 42's ICU may differ from Node 22's for the created line (Q-EXP-24): capture one line from the shipping build before generating the fixtures |
| Demo | `ExportCssGoldenTests` passes for 26 stylesheets and the three bad-scale cases |

#### WP-D10. HTML, HTML for Word and Markdown exports

| Field | Content |
|---|---|
| Goal | The three text exports byte-identical to Electron's (with replayed images), the image pipeline with the JPEG fallback, the export engine and service, and the report's export control |
| Spec inputs | 09 2.2, 2.6, 2.8, 2.10, 2.12, 2.16, 2.17, 2.23, 2.24, 7.3, 7.5, 7.8, 7.13 to 7.15, D-EXP-6 to D-EXP-9, D-EXP-19; 05 2.4, 7.15; 04 Q-EDIT-21 (the save dialogs' HWNDs registered for capture exclusion); ARCHITECTURE 7.7, 9.2 S4; Q-EXP-8, Q-EXP-13, Q-EXP-18, Q-EXP-19 |
| Deliverables | Core `IExportImageCodec`, `IAvifEncoder` (a null encoder until WP-D11), `AvifSanity`, `InlineImage`, `IHtmlImageEmbedder`, `HtmlImageEmbedder` (never upscale, never bigger, JPEG 85 fallback), `HtmlDocumentBuilder`, `PlainHtmlDocumentBuilder`, `MarkdownDocumentBuilder`, `MarkdownDocument`, `MarkdownImage`, `ExportEngine` (settle, destinations, atomic publish, progress, cancellation leaves nothing); Platform `WicExportImageCodec`, `WindowsExportFileProbe`; App `ShotAI.App.Export`: `IExportService` (the seven members of 09 7.13; this WP implements the single-project ones), `ExportService`, `IExportDialogs`, `WpfExportDialogs`; App `ExportButton` and export menu with the flip, `ExportViewModel` (flatten first, progress label, cancel on Back) |
| Tests | Port `src/renderer/project/command-bar.test.ts` by intent (it is ELECTRON-ONLY as written): Core `Report/PopoverPlacementTests.ExportMenuFlip`, App `Report/CommandBarTests`. New: Core `Export/HtmlImageEmbedderTests`, `HtmlDocumentBuilderTests`, `PlainHtmlDocumentBuilderTests`, `MarkdownDocumentBuilderTests`, `ExportGoldenTests`, `StylesheetStrippingTests`, `ExportEngineTests`, `Report/ExportFlowTests`; Platform `Export/WicExportImageCodecTests`; App `Export/ExportServiceTests`, `ServiceBoundary/ExportReadsBrandAtCallTimeTests` |
| Acceptance criteria | AC-EDIT-6, AC-REP-2, AC-REP-4, AC-REP-23, AC-REP-24, AC-REP-35, AC-REP-36, AC-REP-37, AC-EXP-3, AC-EXP-6, AC-EXP-11, AC-EXP-12, AC-EXP-17, AC-EXP-21, AC-EXP-22, AC-EXP-25, AC-EXP-32, AC-EXP-35, AC-IPC-24 |
| Depends on | WP-D9, WP-C7, WP-C2 |
| Size | M |
| Risks and de-risking | Byte identity is defined over text with replayed or normalized image payloads (Q-EXP-14); fixtures avoid images where the "never bigger" guard would flip the media type |
| Demo | export the `mixed-default` fixture to HTML: identical to Electron's output after payload normalization |

#### WP-D11. AVIF encoder

| Field | Content |
|---|---|
| Goal | Styled HTML embeds AVIF at quality 50 and speed 7 through a pinned, CI-built libavif, falling back to JPEG when the DLL is absent |
| Spec inputs | 09 2.7, 7.5, 7.6, D-EXP-5, D-EXP-21, risk R-EXP-1; 12 7.7 (7.7.1 to 7.7.4), INV-PKG-19; ARCHITECTURE 3.3, I-3; Q-EXP-1, Q-EXP-2, Q-EXP-23, Q-PKG-13, Q-PKG-28 |
| Deliverables | `dotnet/native/avif/` (C shim exporting `shotai_avif_encode`, `shotai_avif_free`, `shotai_avif_version`; `pins.json` with libavif and libaom commits and archive SHA-256; the CMake build); `dotnet.yml` job `native-avif` (x64 and arm64, hash-checked NASM, `/CETCOMPAT` on x64, skipped while `pins.json` is absent) whose artifact the windows jobs download; Platform `ShotAI.Platform.Export.NativeAvif` (source-generated `LibraryImport`, the sanctioned exception I-3), `LibavifEncoder : IAvifEncoder` (encoder settings verified against squoosh `avif_enc.cpp` at the jsquash 2.1.1 tag, all cores); the AVIF encode added to `--selftest` (Q-PKG-28) |
| Tests | No Electron file. New: Platform `Export/LibavifEncoderTests`; Core `Export/AvifPinsTests` (reads `pins.json`) |
| Acceptance criteria | AC-EXP-4, AC-EXP-5, AC-EXP-38 |
| Depends on | WP-D10 |
| Size | M |
| Risks and de-risking | libaom's ARM64 MSVC build and the NASM toolchain are unproven (Q-PKG-13): build natively on `windows-11-arm`, fall back to `-DAOM_TARGET_CPU=generic`; file sizes must stay within 20 percent of the 168 KB reference (Q-EXP-2) |
| Demo | the reference SOP's styled HTML embeds `image/avif` for every shot and pastes into a Freshservice article |

#### WP-D12. PDF export

| Field | Content |
|---|---|
| Goal | PDF from the same export HTML through a locked-down WebView2, printing hidden, atomically published |
| Spec inputs | 09 2.11, 7.7, D-EXP-1 to D-EXP-4, D-EXP-25, INV-EXP-19, INV-EXP-20, INV-EXP-35; 11 INV-IPC-20; ARCHITECTURE 9.2 S16, S19, DL6, I-9, R-ARCH-13; Q-EXP-3, Q-EXP-4, Q-EXP-15, Q-EXP-17, Q-EXP-20, Q-EXP-22, Q-EXP-25, Q-EXP-27 |
| Deliverables | Platform `ShotAI.Platform.Export`: `WebView2PdfRenderer : IPdfRenderer` (scripts off; navigation, frame, window, download, permission and resource blocking; `Navigate` to the print copy's file URI; user data folder `Path.Combine(IAppPaths.LocalDataDirectory, "WebView2")`, that is `%LOCALAPPDATA%\LFI\shotAI\WebView2`; SmartScreen off through `IsReputationCheckingRequired = false` on every controller before `Navigate` and the `PdfEngine.BrowserArguments` constant (`--disable-features=msSmartScreenProtection`, logged as `webview2 env: args=<args>`), INV-EXP-35; 60 s navigation and 120 s print watchdogs; one print at a time on the UI thread), `PdfHostWindow` (never-shown Win32 popup, registered and excluded); Core `PrintHtml` (face as `data:font/ttf`, CSP meta, print copy only), `IPdfRenderer`, `PdfRenderException`, the `%PDF-` and `%%EOF` checks (Q-EXP-15), the `_print-*` sweep; the AC-EXP-42 network trace run before the PDF UI ships, and any connection it shows handled per Q-EXP-27 (a further argument only if it stops the connection and `PdfRendererTests` still pass; an unstoppable one named in the README privacy section). 03 2.10.2 (Q-EXP-17) and 09 7.7 (R-ARCH-13) already say what this WP builds (1.5) |
| Tests | No further Electron file. New: Core `Export/PrintHtmlTests` (including `FaceOnlyInThePrintCopy` from `export-theme.test.ts`), `Threading/StaleProgressTests`; Platform `Export/PdfRendererTests` (including `ReputationCheckingIsOff` and `UserDataFolderIsUnderLocalData`); App `Architecture/SingleWebViewTests` |
| Acceptance criteria | AC-EXP-7, AC-EXP-8, AC-EXP-9, AC-EXP-10, AC-EXP-33, AC-EXP-39, AC-EXP-42, AC-IPC-8, AC-IPC-19 |
| Depends on | WP-D10, WP-A15 |
| Size | M |
| Risks and de-risking | Printing from a hidden controller on a never-shown window is undocumented (Q-EXP-3): `PdfRendererTests` on the Windows runner decides; fallback: a visible off-screen tool window excluded from capture. Whether `WebResourceRequested` covers `file://` sub-resources is unverified (Q-EXP-25): the CSP is the primary block and both layers are tested separately |
| Demo | the `mixed-default` fixture prints to Letter pages with card backgrounds |

#### WP-D13. Word export

| Field | Content |
|---|---|
| Goal | A `.docx` built with the Open XML SDK whose layout dump equals Electron's `docx` 9.7.1 output |
| Spec inputs | 09 2.13, 7.10, D-EXP-18, D-EXP-20, risk R-EXP-2; Q-EXP-7, Q-EXP-9, Q-EXP-10 |
| Deliverables | Core `ShotAI.Core.Export.Office`: `OfficeImage`, `DocxBuilder`, `DocxDefaultStyles` (the library defaults reproduced, `2E74B5` and `1F4D78` allowlisted); `DocumentFormat.OpenXml` in Core; the test helper `DocxDump` |
| Tests | No Electron file of its own (the source scan `export-palette-source.test.ts` completes in WP-D14). New: `Export/Office/DocxBuilderTests` (`OpenXmlValidator(Office2019)` clean), `OfficeLayoutDumpTests` (docx half) |
| Acceptance criteria | AC-EXP-13, AC-EXP-36 |
| Depends on | WP-D10 |
| Size | M |
| Risks and de-risking | Defaults that `docx` supplied silently (heading styles, compatibility settings): the layout dump against Electron's golden `.docx` and a manual open in Word |
| Demo | the fixture opens in Word without a repair prompt; each step is a bordered, shaded card |

#### WP-D14. PowerPoint export

| Field | Content |
|---|---|
| Goal | A `.pptx` whose slides match pptxgenjs 4.0.1's geometry, text and pictures |
| Spec inputs | 09 2.14, 3.3, 7.11; Q-EXP-21 |
| Deliverables | Core `PptxBuilder`, `PptxLayout`, `PptxText`, `PptxTheme`; the test helper `PptxDump`; the Q-EXP-21 decision recorded in 09 after opening Electron's deck in PowerPoint 365 |
| Tests | Port `src/main/export-palette-source.test.ts` (`Export/ExportPaletteSourceTests` over `ExportCss.cs`, `DocxBuilder.cs`, `PptxBuilder.cs`, `DocxDefaultStyles.cs`, with the per-match line check). New: `Export/Office/PptxBuilderTests`, `OfficeLayoutDumpTests` (pptx half) |
| Acceptance criteria | AC-EXP-2, AC-EXP-14, AC-EXP-41 |
| Depends on | WP-D13 |
| Size | M |
| Risks and de-risking | The literal CRLF inside `a:t` (Q-EXP-21): measure once before choosing parity or `a:br` |
| Demo | the fixture opens in PowerPoint without a repair prompt |

#### WP-D15. Shareable package

| Field | Content |
|---|---|
| Goal | Package export (safe by default) and import (bounded, whitelisted), the package dialog, and File, Import Project |
| Spec inputs | 09 2.21, 2.22, 7.12, D-EXP-12 to D-EXP-17, INV-EXP-25, INV-EXP-26; 01 2.9.9; 05 2.4 (package dialog), D-REP-9; 03 2.8.1; ARCHITECTURE 9.2 S5, S13; Q-EXP-16, Q-EXP-26 |
| Deliverables | Core `ShotAI.Core.Export.Package`: `PackageWriter` (whitelisted, reset fields, no zip64), `PackageReader` (declared sizes before inflating, bounded reads, JSZip name cleanup, duplicate and backslash rules, marker and manifest validation), `PackageLimits`, `JsZipNames`, `PackageImport`; `ExportService.ExportPackageAsync`, `ImportPackageAsync`; App `PackageExportDialog` (redacted-only on every open), File, Import Project (`Ctrl+O`) and the Home import flow |
| Tests | No Electron file. New: Core `Export/Package/PackageWriterTests`, `PackageReaderTests`, `PackageRoundTripTests`; Platform `Export/PackageWriterTests.OriginalsSkipLinkedReferences`; App `Report/ExportFlowTests.PackageDialogOpensRedacted`, `DialogKeyboardTests.PackageDialogEscapeCloses` |
| Acceptance criteria | AC-SHELL-22, AC-REP-25, AC-EXP-18, AC-EXP-26, AC-EXP-27, AC-EXP-28, AC-EXP-29, AC-EXP-30, AC-EXP-31, AC-EXP-37, AC-EXP-40 |
| Depends on | WP-D9, WP-A7, WP-C2 |
| Size | M |
| Risks and de-risking | Zip bombs and name tricks (EDGE-EXP-34, D-EXP-12): `UnderDeclaredEntryIsTruncatedNotInflated` and the AC-EXP-30 working-set check |
| Demo | a package exported natively imports in Electron 1.3.0 and natively |

#### WP-D16. Home row and bulk export

| Field | Content |
|---|---|
| Goal | Export from a Home row and for a selection, to each project's folder or one shared folder, flattening each project first |
| Spec inputs | 06 2.14 (row export), 2.16 (bulk export), 7.6 (`HomeExportFlow`), D-HOME-33; 09 7.13 (bulk members); ARCHITECTURE 9.2 S4; Q-HOME-5, Q-HOME-7 |
| Deliverables | App `HomeExportFlow`; `ExportService.ExportToOwnFolderAsync`, `ExportToDirectoryAsync`, `ChooseExportDirectoryAsync`, `RevealExportDirectoryAsync`; the export actions of `BulkBarViewModel` with `Exporting N of M…` progress |
| Tests | No Electron file. New: App `Home/HomeExportFlowTests`, `Home/HomeViewModelTests` (`BulkBarStaysVisibleWhileBusy`, `BulkRefreshesOnceThenClears`, `BulkFinalRefreshFailureStillClears`, `BulkWithNoVisibleTargetShowsNoDialog`, `RowExportRevealsWrittenFile`) |
| Acceptance criteria | AC-HOME-8, AC-HOME-9, AC-HOME-40, AC-EXP-20 |
| Depends on | WP-D10, WP-A19 |
| Size | S |
| Risks and de-risking | none beyond 09's |
| Demo | export three projects to one folder: one folder dialog, the folder opens once, three files |

#### WP-D17. Phase D exit

| Field | Content |
|---|---|
| Goal | Run milestone M-D: the end-to-end redaction proof through every egress, the live-tenant SOP runs, the proxy runs, and every format side by side |
| Spec inputs | section 4 M-D; 04 AC-EDIT-5, AC-EDIT-25; 07 9; 08 9 (live-tenant rows); 09 9; ARCHITECTURE 11 (PB-15), 12.7 (phase D set) |
| Deliverables | App `Editor/EndToEndRedactionTests` (04 8.2) and Core `Export/EgressRedactionTests` (09 8.2) running against every format and the safe package; the integration test per egress entry point of AC-EDIT-25; the phase D tracking issue completed |
| Tests | as above |
| Acceptance criteria | AC-EDIT-5, AC-EDIT-25, AC-SOP-6, AC-SOP-7, AC-SOP-16, AC-SOP-17, AC-SOP-19, AC-SOP-20, AC-SOP-21, AC-SOP-23, AC-AUTH-26, AC-AUTH-27, AC-AUTH-28, AC-AUTH-29, AC-EXP-15, AC-EXP-16, AC-EXP-23, AC-EXP-34, AC-IPC-15 |
| Depends on | WP-D1 to WP-D16 |
| Size | S |
| Risks and de-risking | The stale-roles trap under WAM (Q-AUTH-4): if AC-AUTH-27 fails until the old token expires, the forced refresh is insufficient and WP-D3 is reopened |
| Demo | the tracking issue |

---

### 3.5 Phase E: ship

Goal (feasibility): the signed MSI with the Desktop Runtime dependency, the same policy key and ADMX, the update check, the pilot, the cutover. Exit: the pilot group runs native, the Electron build stays installable as a rollback, then 2.0.0 replaces it.

#### WP-E1. Update check and the update notice

| Field | Content |
|---|---|
| Goal | The throttled startup check and `Check now`, over the system proxy, prerelease-aware, with the Home notice |
| Spec inputs | 10 2.8 (2.8.1 to 2.8.9), 7.6 (7.6.1 to 7.6.4), INV-INFRA-22 to INV-INFRA-29; 06 2.22, 7.10 (update slot), INV-HOME-45, D-HOME-35; 11 2.6, 7.3.6, D-IPC-17, INV-IPC-26; 12 7.10.4 (`IInstallInfo`), EDGE-PKG-65; ARCHITECTURE 4.3 (`IInstallInfo`), 9.2 S19; Q-INFRA-4, Q-INFRA-5 (resolved), Q-INFRA-6, Q-INFRA-13, Q-INFRA-14, Q-INFRA-15, Q-INFRA-18, Q-HOME-8, Q-HOME-17, Q-IPC-7, Q-PKG-15 |
| Deliverables | Core `ShotAI.Core.Updates`: `UpdateCheckResult`, `UpdateSkipReason`, `UpdateDecision`, `UpdateCheck`, `ReleaseFeed` (over `ISharedHttp`, `X-GitHub-Api-Version`, final-host check, 10 s timeout), `IUpdateService`, `UpdateService` (stash before raise, in-flight join, `CheckNowAsync` updating `Pending` without raising), `AppVersion`; Core `UpdateSelfTest` and `--update-selftest`; startup step 13's update check; App the update slot of `NoticeCenter` and `Check now` in About; Core `ShotAI.Core.Install` (`InstallScope`, `IInstallInfo`, `InstallScopeRules`) and Platform `ShotAI.Platform.Install.InstallInfoReader` (12 7.10.4), read at startup step 1b and registered by `AddShotAIApp` at step 6; the per-machine variant of the notice and of `Check now` (`SettingsText.UpdateAvailable`, 06 INV-HOME-45) |
| Tests | Port `src/main/update-check.test.ts` (`Updates/UpdateCheckTests`, with the native additions). New: `UpdateServiceTests`, `ServiceBoundary/UpdateCheckNowTests`, `Install/InstallScopeRulesTests`; Platform `Install/InstallInfoReaderTests`; App `Chrome/UpdateNoticeTests` (with `PerMachineHasNoDownloadAction`, `PerUserKeepsDownloadAction`), `Settings/SettingsViewModelTests.CheckNowPerMachineOpensNothing` |
| Acceptance criteria | AC-HOME-28, AC-HOME-29, AC-HOME-30, AC-INFRA-17, AC-INFRA-18, AC-INFRA-19, AC-INFRA-20, AC-INFRA-21, AC-INFRA-22, AC-INFRA-23, AC-INFRA-24, AC-INFRA-28, AC-INFRA-34, AC-IPC-4, AC-IPC-25 |
| Depends on | WP-D2, WP-A19, WP-B10 |
| Size | M |
| Risks and de-risking | A native prerelease published without the prerelease flag would be offered to every Electron user (10 risk, INV-PKG-9): the release workflow's flags and `verify-published` (WP-E4) |
| Demo | a build versioned below the latest release shows `shotAI <v> is available.` once (AC-HOME-41, the per-machine form, is recorded in WP-E6, once the MSI of WP-E3 exists) |

#### WP-E2. Release tool and payload verification

| Field | Content |
|---|---|
| Goal | One version source, the MSI version mapping, tag checks, payload verification and the notices file, all Linux-testable |
| Spec inputs | 12 7.2.1, 7.2.3, 7.2.4, 7.3, 7.9.3, 7.15, 8.2 (release and packaging rows), INV-PKG-10, INV-PKG-14, INV-PKG-17, INV-PKG-18, INV-PKG-29; ARCHITECTURE 3.1 V10, 13.1; 09 8.1 (`BrandFontShipsTests`); Q-PKG-3, Q-PKG-11, Q-PKG-19 |
| Deliverables | `dotnet/tools/ShotAI.Release` (`ReleaseVersion`, `MsiVersion`, `TagCheck`, `PropsReader`, `PeHeader`, `PayloadVerifier`, `NoticesGenerator`, `FederationCheck`; commands `version`, `check-tag`, `verify-payload`, `notices`, `federation-check`) in `ShotAI.slnx` and referenced by `ShotAI.Core.Tests`; committed `THIRD-PARTY-NOTICES.txt`; `Directory.Build.props` additions (`IncludeSourceRevisionInInformationalVersion` false, the `FileVersion` default); `ShotAI.App.csproj` publish properties (`RuntimeIdentifiers` `win-x64;win-arm64`, `SelfContained` false, `AppHostDotNetSearch` `Global`, `StartupHookSupport` false, `PublishReadyToRun` for first-party assemblies with every third-party assembly in `PublishReadyToRunExclude`, `SatelliteResourceLanguages` `en`); the same `RuntimeIdentifiers` in `ShotAI.Platform.csproj`; the `asInvoker` block in `app.manifest`; `dotnet.yml` Linux step `Third-party notices are current` |
| Tests | No Electron file. New: Core `Release/ReleaseVersionTests`, `MsiVersionTests`, `TagCheckTests`, `PropsReaderTests`, `PeHeaderTests`, `PayloadVerifierTests`, `NoticesTests`, `FederationCheckTests`; `Packaging/RepoHygieneTests`, `ManifestTests`, `SourceScanTests` (remaining cases), `BrandFontShipsTests` |
| Acceptance criteria | AC-PKG-4, AC-PKG-28 |
| Depends on | WP-A12, WP-D1 |
| Size | M |
| Risks and de-risking | ReadyToRun stripping third-party signatures (EDGE-PKG-47): the exclusion list; measured in WP-E4 (AC-PKG-33) |
| Demo | `dotnet run --project tools/ShotAI.Release -- version` prints `semver=2.0.0-alpha.0`, `msi=2.0.0` |

#### WP-E3. MSI, install smoke and the package job

| Field | Content |
|---|---|
| Goal | One dual-purpose MSI per architecture (per-user by default, per-machine with `ALLUSERS=1`) that installs in both scopes, runs its self-test, upgrades, refuses downgrades, refuses a personal copy beside a per-machine one or without the runtime, and uninstalls cleanly in CI |
| Spec inputs | 12 7.1, 7.2.2, 7.4 (7.4.1 to 7.4.5), 7.5, 7.9, 7.11.2 (package job), 7.11.4, 7.13.5 (the script), INV-PKG-1 to INV-PKG-6, INV-PKG-13, INV-PKG-15 to INV-PKG-18, INV-PKG-22, INV-PKG-30, INV-PKG-33, INV-PKG-35 to INV-PKG-37, EDGE-PKG-61 to EDGE-PKG-67; 03 8.2 (the ARP icon intent); 10 AC-INFRA-29, AC-INFRA-31; Q-PKG-1, Q-PKG-6, Q-PKG-9, Q-PKG-10, Q-PKG-20, Q-PKG-25, Q-PKG-26, Q-PKG-30 |
| Deliverables | `dotnet/installer/ShotAI.Installer.wixproj` (not in `ShotAI.slnx`) and `Package.wxs` (dual-purpose `Scope="perUserOrMachine"`, `HKMU` values `InstallFolder` and `DesktopShortcut`, the two per-user launch conditions with WiX NetFx `DotNetCompatibilityCheck` and their exact messages, the check's scheduling overridden to first per-user installs, no dialog set, pinned UpgradeCode, MajorUpgrade with `A newer version of shotAI is already installed.`, the build 19041 launch condition message, Start menu shortcut `shotAI` or `shotAI Preview`, `ARPPRODUCTICON`, `DESKTOPSHORTCUT` default 0, no custom action that runs the app, no policy writes, no per-user locations); `dotnet/installer/smoke.ps1` (7.11.4: the per-machine, guard, per-user and scope legs, the guard and scope legs on the probe build; every assertion throws); `dotnet/installer/remove-shotai-personal-copy.ps1` (7.13.5); `dotnet/installer/INTUNE.md` for IT (the install command with `ALLUSERS=1` and System install behavior only, with the reason, INV-PKG-36 and EDGE-PKG-61; application control and `DisableUserInstalls`, EDGE-PKG-66; removing personal copies before `DisableUserInstalls` is set, 7.13.5; runtime dependency and detection by folder, requirement rules, MSI detection, WebView2, OCR Feature on Demand `Language.OCR~~~en-US~0.0.1.0`, the WAM redirect URI, the Restart Manager note); `dotnet.yml` job `package` (public publish, `verify-payload --public`, MSI and probe build, smoke, 7-day artifact of the MSI only) on x64 and arm64; the WiX licence decision recorded in 12 section 11 (Q-PKG-1) |
| Tests | No Electron file (`src/main/arp-icon.test.ts` is ELECTRON-ONLY; its intent is `PackageSourceTests.ShortcutAndArp` here). New: Core `Packaging/PackageSourceTests` (each rule also asserted against a mutated copy); the CI smoke |
| Acceptance criteria | AC-SHELL-30, AC-INFRA-29, AC-INFRA-31, AC-PKG-2, AC-PKG-3, AC-PKG-6, AC-PKG-7, AC-PKG-8, AC-PKG-9, AC-PKG-10, AC-PKG-12, AC-PKG-18, AC-PKG-19, AC-PKG-26, AC-PKG-31, AC-PKG-32, AC-PKG-35, AC-PKG-36, AC-PKG-37 |
| Depends on | WP-E2 |
| Size | M |
| Risks and de-risking | WiX licensing (Q-PKG-1) must be decided before this PR: read the current terms; if the fee is not approved, pin the last release without it and generate explicit `File` elements from `ShotAI.Release` if that release lacks `Files`. The launch-condition integer comparison (Q-PKG-6): test on 1909 and 2004 VMs. ICE38, ICE43 and ICE57 with `HKMU` keypaths under `ALLUSERS=2` are UNVERIFIED (EDGE-PKG-64): fix any finding in the authoring, never by suppressing ICE105. Where Windows Installer registers a per-user product's Installed apps entry is UNVERIFIED (7.11.4): the first run decides the assertion |
| Demo | the `package` job artifact installs on a clean Windows 11 VM per-machine (`ALLUSERS=1`) and, for a standard user, per-user with no UAC prompt; Installed apps shows shotAI with its icon and publisher LFI |

#### WP-E4. Signing and the release workflow

| Field | Content |
|---|---|
| Goal | A tag produces a signed, attested, smoke-tested draft release with the right prerelease and latest flags; a human publishes it |
| Spec inputs | 12 7.6, 7.11.3, 7.12 (7.12.1 to 7.12.3), INV-PKG-7, INV-PKG-9, INV-PKG-14, INV-PKG-19, INV-PKG-28; 08 Q-AUTH-11; Q-PKG-2, Q-PKG-3, Q-PKG-5, Q-PKG-11, Q-PKG-18, Q-PKG-24 |
| Deliverables | `.github/workflows/release.yml` (jobs `check`, `test` (which passes `-p:ShotAIStrictAudit=true`, so low and moderate NuGet advisories fail the release workflow, ARCHITECTURE 3.1 V5, 12 7.2.1 and 7.11.3), `native-avif` with attestation, `build`, optional `build-internal`, `sign` in two passes with Azure Artifact Signing over OIDC in the protected `release` environment, `smoke-arm64`, `draft-release`, `verify-published`; `permissions: {}` at the top; actions pinned by SHA); `dotnet/installer/release-notes/2.0.0-alpha.1.md`; the repository `release` environment with required reviewers and the signing identifiers (set by the maintainer; IT owns the Azure side, Q-PKG-2); the internal-build decision recorded with IT (Q-PKG-5) |
| Tests | No Electron file. New: Core `Packaging/WorkflowContractTests` (16 cases of 12 8.2, including the three that read `smoke.ps1` and `INTUNE.md` from WP-E3); a dry run with `workflow_dispatch` on a throwaway tag producing a draft, then deleted |
| Acceptance criteria | AC-PKG-1, AC-PKG-5, AC-PKG-15, AC-PKG-16, AC-PKG-17, AC-PKG-30, AC-PKG-33 |
| Depends on | WP-E3, WP-D11, WP-E1 (AC-PKG-1 includes `InstallScopeRulesTests`) |
| Size | M |
| Risks and de-risking | Signing eligibility and the timestamp URL are unverified (Q-PKG-2): OV certificate in Key Vault as the fallback; confirm `TimeStamperCertificate` on the first signed build. A public release carrying baked tenant values (INV-PKG-14): public builds pass `-p:ShotAIFederationFile=` and `verify-payload --public` checks the extracted MSI (AC-PKG-16) |
| Demo | a draft release with both MSIs, `SHA256SUMS.txt`, symbols and notices, flags `--prerelease --latest=false` |

#### WP-E5. Coexistence guard and data paths

| Field | Content |
|---|---|
| Goal | Native refuses to start while Electron runs in the same session, never stores data under the Squirrel root, both builds share settings and projects safely, and a personal copy hands off to the copy installed for all users |
| Spec inputs | 12 7.10 (7.10.1 to 7.10.4), 7.13.5, INV-PKG-23 to INV-PKG-27, INV-PKG-38, EDGE-PKG-22, EDGE-PKG-23, EDGE-PKG-50, EDGE-PKG-62; 03 7.4.1 steps 1 and 2, Q-SHELL-19; ARCHITECTURE 4.2 steps 1b and 2a, 10.5, R-ARCH-13, AC-ARCH-7; Q-PKG-4, Q-PKG-8 |
| Deliverables | Platform `ShotAI.Platform.Processes.ProcessSnapshot` and `ProcessStarter`; App `ShotAI.App.Startup.LegacyInstanceGuard` at startup step 2a (the one allowlisted `MessageBox`, caption `shotAI`; in a self-test mode the exact stderr line and exit code 2, no dialog); App `ShotAI.App.Startup.PersonalCopyGuard` at startup step 1b, before the mutex (no dialog; in a self-test mode it only logs; 12 7.10.4) |
| Tests | No Electron file. New: Platform `Startup/ProcessSnapshotTests`, `Processes/ProcessStarterTests`; App `Startup/LegacyInstanceGuardTests` (with the per-user native path as a miss), `Startup/PersonalCopyGuardTests`; `Shell/AppPathsTests` (`NoPathUnderSquirrelRoot`, `SettingsFileIsRoamingAppData`, `LocalDataDirectoryIsUnderLfi`) exist from WP-A12 (AC-INFRA-35) and are not repeated here |
| Acceptance criteria | AC-AUTH-17, AC-PKG-20, AC-PKG-21, AC-PKG-22, AC-PKG-23, AC-PKG-24, AC-PKG-34, AC-PKG-38, AC-ARCH-7 |
| Depends on | WP-A12, WP-D3, WP-D12, WP-E1, WP-E3 |
| Size | S |
| Risks and de-risking | An Electron launch while native runs cannot be prevented (EDGE-PKG-23): the pilot notes tell users to use one build at a time; the Squirrel uninstall switches are unverified (AC-PKG-24 runs before the pilot) |
| Demo | with Electron 1.3.0 running, launching native shows the legacy notice and exits within 2 s |

#### WP-E6. Release readiness audit

| Field | Content |
|---|---|
| Goal | The parity walk, the code-review criteria, accessibility and the budgets, before any user sees the native app |
| Spec inputs | 11 AC-IPC-22 and `channel-map.json`; every spec's D register; 06 7.13, INV-HOME-45 (AC-HOME-41 on the installed MSI); ARCHITECTURE 11, 12.7 (phase E set), 9.5; Q-AUTH-14, Q-HOME-12 |
| Deliverables | the parity-walk record (every channel-map row with a UI caller exercised against the same project in Electron, differences only where a D item says so); the code-review record (no `MessageBox.Show` outside the guard, no sync waits, every window a `ShotAIWindow`, no never-log value in any `Log*` call, the GUID grep of AC-AUTH-31); the Narrator and Accessibility Insights passes and keyboard-only pass; the budget table for PB-1 to PB-17 on both machines; `App.Tests ServiceBoundary/ChannelMapResolutionTests`; the `docs/MANAGED-CONFIG.md` fix in its own small PR (Q-AUTH-14); the draft README deployment and licence sections for 2.0.0 (12 7.12.2 item 7); the high-contrast design sign-off or its deferral (Q-HOME-12) |
| Tests | `ServiceBoundary/ChannelMapResolutionTests` (App.Tests) |
| Acceptance criteria | AC-SHELL-28, AC-HOME-32, AC-HOME-33, AC-HOME-41, AC-AUTH-31, AC-INFRA-16, AC-IPC-2, AC-IPC-22, AC-PKG-11, AC-ARCH-8 |
| Depends on | WP-A20, WP-B11, WP-C13, WP-D17, WP-E1, WP-E3, WP-E5 |
| Size | M |
| Risks and de-risking | The D items make a side-by-side review look like regressions (06 R-HOME-2): the walk lists the D id for every intended difference |
| Demo | the audit issue |

#### WP-E7. Pilot (stages S1 and S2)

| Field | Content |
|---|---|
| Goal | Run the pilot of 5.1 to its exit criteria, then the release candidate for the wider group |
| Spec inputs | 12 7.12, 7.13.1, 7.13.2, 7.13.4 (R1), AC-PKG-29, Q-PKG-34; section 5.1 |
| Deliverables | release `2.0.0-alpha.1` through `release.yml` (then `alpha.N`, `beta.N` as needed); the pilot issue template `.github/ISSUE_TEMPLATE/native-pilot.md`; the Intune Win32 apps for x64 and ARM64 with the runtime dependency (IT); the pilot record; release `2.0.0-rc.1` to the wider group when the exit criteria hold |
| Tests | none new; every fix during the pilot is its own PR on the WP it touches |
| Acceptance criteria | AC-PKG-13, AC-PKG-25, AC-PKG-29 |
| Depends on | WP-E4, WP-E5, WP-E6 |
| Size | S (code); calendar: at least two weeks for S1 and one week for S2 |
| Risks and de-risking | Conditional Access behaving differently under WAM (08 risk R2): the pilot group uses the organization's real policies |
| Demo | the pilot record |

#### WP-E8. 2.0.0 and cutover (stage S3)

| Field | Content |
|---|---|
| Goal | Release 2.0.0 as latest, assign it fleet-wide and remove Electron per user |
| Spec inputs | 12 7.12, 7.13.2, 7.13.3, 7.13.5; section 1.4 and 5.2; Q-PKG-7, Q-PKG-21, Q-PKG-23 (resolved), Q-PKG-32, Q-PKG-33 |
| Deliverables | the release PR (`<Version>2.0.0</Version>`, `release-notes/2.0.0.md` opening with the 7.12.3 text), the published and verified release, the Intune changes of 5.2, the Electron uninstall assignment with the 7.13.3 wrapper |
| Tests | the release workflow; `verify-published` |
| Acceptance criteria | AC-PKG-14 |
| Depends on | WP-E7 and the cutover criteria of 1.4 |
| Size | S |
| Risks and de-risking | Supersedence across install contexts is unverified (Q-PKG-7): use the Uninstall assignment in user context, not supersedence |
| Demo | an Electron 1.3.0 client shows the 2.0.0 update notice |

#### WP-E9. Cleanup and legacy guard removal (stages S4 and S5)

| Field | Content |
|---|---|
| Goal | `main` holds only the native app; after 90 days the legacy guard and the Electron retention go |
| Spec inputs | 12 7.13.1 (S4, S5), 7.13.2, INV-PKG-27; 11 section 10 (deletions); 08 Q-AUTH-10; section 5.2 |
| Deliverables | S4 cleanup PR (5.2 items 9 to 14 and 17), the macOS PR (item 15), and the maintainer's required-checks change after the S4 merge (item 11a); S5 PR (item 16) removing `LegacyInstanceGuard`, `ProcessSnapshot` if unused, and the `MessageBox` allowlist, 90 days after GA |
| Tests | the Electron-comparison tests (`MatchesElectronWhileItExists`, `BrandParityWithElectronTests`, `StampEqualsTypeScriptStamp`) skip with their "after cutover" message; `dotnet.yml` path filters removed (the jobs are made required by the maintainer, 5.2 item 11a) |
| Acceptance criteria | AC-PKG-27 |
| Depends on | WP-E8 |
| Size | M |
| Risks and de-risking | Deleting a file the native build still links (fonts, `federation.example.json`, the ADMX README): move them under `dotnet/` first in the same PR and build before deleting |
| Demo | `main` builds and ships with no `package.json` |

---

## 4. Milestones and exit tests

### 4.0 Reference machines and the side-by-side rig

| Machine | Identity | Used for |
|---|---|---|
| Reference x64 | the most common fleet x64 laptop model with a GPU, named by IT and recorded here before M-A (Q-ARCH-1 default) | every manual AC and budget; two monitors, primary at 100% and secondary at 150% |
| ARM64 dev VM | the existing Windows-on-ARM development VM (software rendering) | the ARM64 half of every phase exit; PB budgets marked ARM64 |
| Installer VMs | clean Windows 11 x64; Windows 11 ARM64; Windows 10 1909 (build 18363) and 2004 (19041); one without the WebView2 runtime; one without the OCR Feature on Demand; one without the .NET 10 Desktop Runtime | WP-E3, WP-E6, AC-PKG-10 to AC-PKG-12, AC-EXP-9, AC-EDIT-22 |
| Live tenant | the organization's test tenant with the shotAI app registration, the Anthropic federation rule and a non-admin test account | WP-D4, WP-D7, WP-D17 (AC-AUTH-22 to AC-AUTH-30); never named in this repository |

Rules for every side-by-side comparison:

| # | Rule |
|---|---|
| SBS1 | Same machine, same monitor layout, same Windows display settings, both builds installed: Electron 1.3.0 through its Squirrel installer, native from the latest `main` (the `package` job MSI from WP-E3 on, before that `dotnet publish` output run from `artifacts/publish/<rid>`) |
| SBS2 | Never both running at once (INV-PKG-26; from WP-E5 the guard enforces it): quit one before launching the other |
| SBS3 | Work on copies: copy the parity fixtures to a scratch projects root and point both builds at it (Settings, Storage), so a comparison never edits real work; restore the copy between runs |
| SBS4 | Compare the observable result the AC names (step count, caption strings, PNG dimensions within 1 px, `click.image` within 1 px, numbering, widths, file bytes, dialog text); record both values, not only "same" |
| SBS5 | A difference is either a D item of the owning spec (cite it) or a defect (open an issue on the WP) |
| SBS6 | Record machine, both build versions and commits, and the result in the phase tracking issue; attach no screenshot of real data (public repository) |
| SBS7 | Parity fixtures: P1, an Electron-authored project of about 12 steps (a right-click with a menu selection, a double-click, a text step, note, caution, warning and section callouts, an unknown `tip` callout added by hand, a pixelate blur, a solid redaction, a crop, a step zoomed to 2 with a pan, an imported JPEG with EXIF orientation 6, pinned to `lfi`); P2, the macOS fixture (`Golden/macos-fixture`); P3, a synthetic 300-step project (PB-7); P4, an edge project (UTF-8 BOM, junk step entries, `displayScale` 9, `theme` `solarpunk`, an unknown root key); P5, a project in a OneDrive Files On-Demand folder with dehydrated screenshots (Q-MODEL-6). P1, P3 and P5 stay on the test machine; only synthetic fixtures are committed |

### 4.1 M-A: model and viewer

| # | Exit test | How | Pass rule |
|---|---|---|---|
| A-1 | Conformance green | Linux job | the 7 agreed cases pass, `display-scale-out-of-range` reports its open divergence (AC-MODEL-1) |
| A-2 | Byte parity with Electron | `ElectronGoldenTests` | every golden output byte-identical (AC-MODEL-3) |
| A-3 | Opens projects written by both apps | side by side: open P1 and P2 in Electron 1.3.0 and native; per step compare numbering (the `tip` step numbered as plain text), caption, text blocks, callout kind, the image shown (the render when flattened), window line; open P4 natively | same content everywhere; the native figure is exactly `HtmlImageMax(s)` wide (738 DIP at 100%) while Electron's is wider (EDGE-REP-1, recorded, Q-REP-2 decided); P4 lists and opens |
| A-4 | Home parity | side by side: tabs and counts, date groups, three search queries (title, caption, none) | same rows, groups and tiers |
| A-5 | Archive both ways | archive P1 natively and restore in Electron; archive in Electron and restore natively (AC-MODEL-13) | byte-identical `project.json`, every image shows |
| A-6 | Exit flush | rename and File, Exit within 100 ms (AC-MODEL-31) | new title after relaunch |
| A-7 | Budgets | PB-7 on P3, PB-14 on a 100-step manifest with Defender on | recorded on both machines; PB-7 over 100 ms switches the card list to `VirtualizingStackPanel` (Q-REP-11) |
| A-8 | Measurements for open questions | Q-SHELL-20 (frame bounds of both main windows at Home), Q-MODEL-6 (list and archive P5 in both builds) | recorded in 03 and 01 section 11 |

### 4.2 M-B: capture engine

| # | Exit test | How | Pass rule |
|---|---|---|---|
| B-1 | Same flow, both builds (the feasibility exit test) | AC-CAP-6: on the reference x64 machine, in Auto mode, record the same 10-click flow in Notepad and File Explorer (a double-click, a right-click with a two-level flyout, a Start menu click, a taskbar click) in Electron, then in native | equal step counts, captions string for string, PNG dimensions within 1 px per axis, `click.image` within 1 px |
| B-2 | Menus | AC-CAP-7, AC-CAP-8, AC-CAP-29 | selection steps show the open menu and submenu |
| B-3 | Clicks | AC-CAP-9, AC-CAP-21 (100% plus 150%), AC-CAP-27 (inactive window), AC-CAP-31, AC-CAP-32 (UWP Settings in both builds) | per AC |
| B-4 | Screen share | AC-CAP-11, AC-CAP-12, AC-SHELL-18, AC-HOME-34, AC-HOME-37, AC-IPC-12, with a Teams share viewed from a second machine | the viewer sees shotAI only when remote visibility is on; no saved PNG ever contains the pill, overlay, menus or tooltips |
| B-5 | Deadlock soak | AC-CAP-33 | 60 s with a toggle every 50 ms, no hang |
| B-6 | Budgets | PB-1 to PB-5; the 20-clicks-in-5-seconds script (Q-IPC-20) | 20 steps recorded; budgets recorded on both machines |
| B-7 | Rounding and naming checks | Q-SHELL-5 (`region selected:` log lines at 125% and 150%), Q-CAP-8 (crop widths), Q-CAP-9 (window chooser lists), Q-CAP-21 (screen chooser names), Q-CAP-16 (legibility at 0.5 and 0.85) | decisions written into 02 and 03 section 11 |

### 4.3 M-C: editor and redaction

| # | Exit test | How | Pass rule |
|---|---|---|---|
| C-1 | Ported flatten tests | Linux job | `FlattenerTests` (eleven macOS ports), `RedactionBakerTests`, `StepPatchApplierTests`, `RenderGateTests` green (AC-EDIT-1 to AC-EDIT-4) |
| C-2 | Render-level redaction proof | `Editor/RenderRedactionProofTests` (WP-C10) on the Windows job | no 4 by 4 block of original region pixels in any saved render; solid regions exactly opaque black |
| C-3 | Visual parity of flattened output | AC-EDIT-10: flatten P1's redacted and annotated steps in both builds and compare at 100% | crop, regions within 1 px, rings, arrow tips within 2 px, text offset within `0.15 * fontSize` |
| C-4 | Cross-app editing | AC-EDIT-8, AC-EDIT-9, AC-MODEL-24: edit natively, reopen in Electron and the macOS app, re-save in Electron | annotations, crop, click, marker colour, theme and intro intact; annotation JSON identical apart from `id` |
| C-5 | Report editing | AC-REP-7, AC-REP-8, PB-6 on a 100-step project | per AC; PB-6 under 16 ms |
| C-6 | Editor budgets and access | AC-EDIT-28 (PB-10), PB-11, AC-EDIT-29 | recorded; keyboard reachable, UIA names present |
| C-7 | OCR | AC-EDIT-21, AC-EDIT-22, Q-EDIT-19 on the fixture, Q-EDIT-6 recall on the corpus | notices correct; the word-join and upscale rules adopted only if measured necessary |

### 4.4 M-D: SOP and exports

| # | Exit test | How | Pass rule |
|---|---|---|---|
| D-1 | HTML and Markdown byte parity (the feasibility exit test, as defined by Q-EXP-14) | `ExportGoldenTests` | `html`, `html-plain` and Markdown byte-identical with replayed images; equal after payload normalization with the native embedder (AC-EXP-3) |
| D-2 | PDF, Word, PowerPoint equivalence | side by side: export P1 in both builds; open the PDFs together, the `.docx` in Word and the `.pptx` in PowerPoint | Letter pages with backgrounds; no repair prompt; the layout dumps equal (AC-EXP-7, AC-EXP-13, AC-EXP-14) |
| D-3 | End-to-end redaction | `EndToEndRedactionTests` and `EgressRedactionTests` over HTML, HTML for Word, Markdown, PDF, Word, PowerPoint, the safe package and a fake Claude request (AC-EDIT-5, AC-EXP-34) | no image contains a 4 by 4 block of original region pixels |
| D-4 | Fail-closed egress | AC-EDIT-25, AC-EXP-15, AC-EXP-16, AC-EXP-23 | per AC |
| D-5 | Live tenant | AC-AUTH-26 to AC-AUTH-28, AC-SOP-6, AC-SOP-7, AC-SOP-16, AC-SOP-17, AC-SOP-19 on the reference machines | generation works signed in with no API key present; the stale-roles trap is cleared by `Sign in again` |
| D-6 | Corporate proxy | AC-AUTH-29, AC-SOP-23, AC-SOP-20 behind a TLS-inspecting proxy configured only in Windows settings | sign-in, test connection, estimate and streaming generation succeed |
| D-7 | Logs | AC-SOP-21, AC-IPC-15 | no caption, body, key or UPN in `shotai.log` |
| D-8 | Export speed and size | PB-15 and AC-EXP-38 on the reference SOP in both builds | native not slower than Electron's 11.4 s; size within 20 percent of 168 KB |

### 4.5 M-E: ship

| # | Exit test | How | Pass rule |
|---|---|---|---|
| E-1 | Install lifecycle | the `package` job on x64 and arm64; AC-PKG-2, AC-PKG-6 to AC-PKG-12 on the installer VMs | per AC |
| E-2 | Signing and release | a real `v2.0.0-alpha.1` through `release.yml` | every PE and MSI `Valid` with a timestamp; flags correct; `verify-published` green (AC-PKG-5, AC-PKG-13) |
| E-3 | Coexistence | AC-PKG-20 to AC-PKG-24, AC-ARCH-7, AC-AUTH-17 on a PC with both builds | per AC |
| E-4 | Parity walk and audit | WP-E6 | every difference is a named D item |
| E-5 | Cold start | PB-9 over ten launches on both machines; ReadyToRun kept only if it saves at least 100 ms (Q-PKG-10) | recorded |
| E-6 | Pilot exit | AC-PKG-29 | section 5.1 exit criteria |

---

## 5. Pilot, cutover and rollback

### 5.1 Pilot plan

| Aspect | Plan |
|---|---|
| Entry criteria | M-A to M-D passed; WP-E1 to WP-E6 merged; M-E rows E-1, E-3 and E-4 passed (E-2 is the first pilot release itself); the WAM redirect URI registered (Q-AUTH-3); the internal-build decision taken with IT (Q-PKG-5); IT's answer on application control for personal copies (Q-PKG-34); Electron frozen to fixes (Q-PKG-27) |
| Who | 5 to 10 people chosen with IT: the Intune owner; two or three SOP authors who record weekly; at least one user on Windows on ARM (the dev VM owner counts); at least one LFI-brand user; at least one federated (signed-in) user and one API-key user; nobody whose only device is unmanaged. Every pilot user keeps Electron 1.3.x installed (S1 never removes it) |
| How long | S1 (alpha and beta): at least two weeks of daily use on both architectures (AC-PKG-29), extended until no blocker has been open for a week. S2 (release candidate): at least one week in a wider group (for example one department) |
| Delivery | each prerelease through `release.yml` as `--prerelease --latest=false`, imported by IT as a new Win32 app that supersedes the previous prerelease app for the pilot group (12 7.13.2); pilot users see `shotAI Preview` in the Start menu |
| What to measure | From `shotai.log` (collected by the user, reviewed for the never-log list before attaching): hook watchdog reinstall lines (target zero, Q-CAP-5), `startup: main window rendered in <n> ms` (PB-9), capture failures (`CaptureFailed` lines), rollback notices (persist-failure Warnings), crash lines (`terminating=`), export durations and AVIF fallback warnings, SOP outcomes by category (refusal, cutoff, 429, network), sign-in failures, legacy-guard hits. From users: a short weekly form (what failed, what felt slower or faster than Electron, what looked different). From IT: install and detection success per device, runtime dependency installs, WebView2 and OCR availability |
| Feedback loop | GitHub issues from the template `.github/ISSUE_TEMPLATE/native-pilot.md` (build version, architecture, steps, expected versus actual, log excerpt with no content); triage twice a week by the maintainer into: blocker (the user is rolled back per R1 the same day and the fix ships in the next prerelease), parity defect (fixed natively, or recorded as a D item with the reason), Electron defect (fixed in both apps under B6), post-2.0 request (labelled, not fixed now). Each fix is a PR on the WP it touches, then a new prerelease. The pilot record in the WP-E7 issue lists every issue with its class and outcome |
| Exit criteria | AC-PKG-29 (two weeks on both architectures, AC-CAP-6 passes, no open blocker, AC-PKG-20 to AC-PKG-24 pass), zero hook reinstall lines or a decision on Q-CAP-5, no crash line without a fix, and IT's sign-off on detection and deployment |

### 5.2 Cutover checklist

| # | Step | Owner | Reference |
|---|---|---|---|
| 1 | Cutover criteria CC1 to CC8 hold | maintainer | 1.4 |
| 2 | Release PR: `<Version>2.0.0</Version>` in `dotnet/Directory.Build.props`; `dotnet/installer/release-notes/2.0.0.md` opening with the exact 12 7.12.3 text, then the marker-colour change (Q-EDIT-1), the culture change for dates (Q-HOME-4), the "sign in or enter your key again" note and the re-pin note (Q-PKG-32) | session | 12 7.12.2, 7.12.3 |
| 3 | Push tag `v2.0.0`; `release.yml` builds, signs, smokes and drafts; a human publishes the draft with "Set as the latest release" checked; `verify-published` passes | maintainer | 12 7.12.1 |
| 4 | Confirm from an Electron 1.3.0 client that the update notice now offers 2.0.0 (AC-PKG-14) | maintainer | 12 9 |
| 5 | Intune: create or update the `shotAI (x64)` and `shotAI (ARM64)` Win32 apps for 2.0.0 with the runtime dependency, requirement rules and MSI detection; supersede the prerelease apps; assign Required to all shotAI users | IT | 12 7.5.2, 7.13.2 |
| 6 | Intune: on the Electron app, remove the Required assignment first, then add the Uninstall assignment in user context with the re-wrapped 7.13.3 script (`uninstall-shotai-electron.ps1`); for hand-installed Electron, run the same script as a platform script | IT | 12 7.13.3, EDGE-PKG-59 |
| 7 | Keep the Electron 1.3.x Intune app object and the `v1.3.*` release assets for 90 days (rollback asset, AC-PKG-27); never edit or delete those assets | IT, maintainer | INV-PKG-27 |
| 8 | Monitor for one week: install success, legacy-guard hits, crash lines, issues | maintainer, IT | 5.1 |
| 9 | S4 cleanup PR (one PR, WP-E9): delete `src/`, `native/element-locator/`, the Electron scripts under `scripts/` (`build-element-locator.mjs`, `gen-brand.mjs`, `make-loading-gif.cjs`, `postinstall.mjs`, `protection-probe.cjs`, `report-strut-probe.cjs`, `report-width-probe.cjs`, `ts-register.mjs`, `ts-resolve.mjs`, `wif-probe.mjs`), `package.json`, `package-lock.json`, `forge.config.ts`, `forge.env.d.ts`, the Electron HTML entry points `index.html`, `overlay.html` and `toolbar.html`, `.npmrc`, `vendor/` (the Tesseract `eng.traineddata.gz`), `assets/shotAI-install.gif` (the Squirrel loading gif), the Vite, TypeScript and ESLint configurations, and `.github/workflows/ci.yml`; before deleting, confirm with a grep of `dotnet/` and `.github/` that no native file references any of them | session | 12 7.13.1 S4, 11 section 10 |
| 10 | Same PR: move what the native build still uses under `dotnet/`: `src/renderer/fonts/Archivo.ttf` and `OFL.txt` to `dotnet/assets/fonts/`, `src/main/entra/federation.example.json` to `dotnet/` (Q-AUTH-10), and drop the `src/main/entra/federation.local.json` fallback from the App project | session | 08 Q-AUTH-10, 12 7.2.3 |
| 11 | Same PR: `dotnet.yml` path filters removed; the Electron comparison tests skip with their after-cutover message (`ChannelInventoryTests.MatchesElectronWhileItExists`, `BrandParityWithElectronTests`, `BrandContractTests.StampEqualsTypeScriptStamp`); the `src/shared/brand-colors.generated.ts` link removed from `ShotAI.Core.Tests.csproj` (added in WP-A4; an unconditional link to a deleted file fails the build with MSB3030), and `dotnet build ShotAI.slnx` confirmed on a tree with `src/` deleted | session | 12 7.11.2, EDGE-PKG-35 |
| 11a | After the S4 PR merges: add the `dotnet.yml` jobs (Linux, Windows x64, Windows ARM64, package) as required status checks on `main` (a branch-protection change that needs repository admin rights) | maintainer | Q-PLAN-5, 12 EDGE-PKG-35 |
| 12 | Same PR: `README.md` rewritten for the native app (install through the MSI, build from `dotnet/`, licences including the OFL for Archivo), and its sections whose Electron claims change natively rewritten from the specs: Privacy (`safeStorage` becomes DPAPI, the MSAL cache location `%LOCALAPPDATA%\LFI\shotAI\entra\`, from 08), Claude access ("opens your browser" becomes the WAM sign-in dialog, 08), Rendering (GPU) (03 D17) and x64-first on Windows-on-ARM (native ARM64 builds, 12), with the list of network calls matching ARCHITECTURE S19 and 09's PDF egress result (AC-EXP-42, Q-EXP-27); the wiki `Installation` page updated, `docs/PLAN.md` (the Electron-era plan) marked historical, links to `docs/NATIVE-WINDOWS-FEASIBILITY.md` kept | session | 12 7.12.2 item 7 |
| 13 | Same PR: `.gitattributes` and `.gitignore` entries for removed Electron paths dropped; `dotnet/README.md` rule "Keep behavior identical to the Electron app" reworded to "the specs under `docs/native/spec/` are the reference" | session | this plan |
| 14 | Cross-repository PR pair (B5): `contract/conformance/README.md:61-62` names the native harness `dotnet/tests/ShotAI.Core.Tests/Conformance/ConformanceTests.cs` instead of `src/main/conformance.test.ts`, in both repositories | session | 2.7 |
| 15 | macOS repository references to this repository updated in one macOS PR: `macOS:CLAUDE.md:13` (the `shotAI-original/` reference clone is now the native app; the behavioral reference is `docs/native/spec/`), `macOS:Scripts/gen-brand.swift:82` and `:98` and `macOS:.github/workflows/ci.yml:52` (the Windows table is `dotnet/src/ShotAI.Core/Brand/BrandPalette.Generated.cs`), `macOS:README.md:12` (the Windows app is native), `macOS:PARITY.md` rows that describe Electron mechanisms (`macOS:PARITY.md:139`, the AVIF and codec notes) | session (macOS repo) | this plan |
| 16 | S5, 90 days after GA with no Electron installs reported by IT: remove `LegacyInstanceGuard` and its `MessageBox` allowlist; IT deletes the Electron Intune app object; optionally the one-time Chromium profile cleanup of Q-PKG-17 (never `settings.json`, logs, `secrets.json` or `entra-cache.bin` without a separate decision) | session, IT | 12 7.13.2, Q-PKG-17, Q-PKG-22 |
| 17 | Same PR as item 9 (S4): `docs/MANAGED-CONFIG.md` and `Intune/Windows/README.md` updated for the native app: the bake input is `dotnet/federation.local.json` through `ShotAIFederationFile` (the `src/main/entra/federation.local.json` path that item 10 drops is gone), the per-machine MSI installs in device context, and the client registration needs the redirect URI `ms-appx-web://microsoft.aad.brokerplugin/{ClientAppId}` (08 Q-AUTH-3, CC5); if WP-E6's Q-AUTH-14 fix has not merged, it is folded in here | session | 08 Q-AUTH-3, Q-AUTH-10, Q-AUTH-14 |

### 5.3 Rollback plan

| Situation | Trigger | Action | Data safety | Reference |
|---|---|---|---|---|
| R1: one pilot user (S1, S2) | a blocker for that user | remove the user from the pilot group; assign the native app as Uninstall for them; they continue on Electron, which was never removed | shared `settings.json` and projects stay readable by 1.3.x by construction; Electron's own `secrets.json` and `entra-cache.bin` were never touched | 12 7.13.4, INV-PKG-24, INV-PKG-25 |
| R2: fleet after GA (S3, within 90 days) | a blocking regression affecting many users that a hotfix cannot fix within two working days | Uninstall assignment for both native apps; reassign the retained Electron 1.3.x app (user context, Required); edit the 2.0.0 GitHub release to prerelease only if the rollback will last more than a week (Q-PKG-21) | as R1; the rollback target must be 1.3.x (INV-PKG-25); native-written `project.json` is covered by the shared conformance suite | 12 7.13.4 |
| Native to an older native | a bad native patch release | uninstall the newer MSI, install the older one (MajorUpgrade refuses downgrades, AC-PKG-8) | settings and projects unaffected | 12 7.13.4 |
| Electron hotfix during the pilot or after GA | a 1.3.x defect that must ship | release it under the freeze rules; after 2.0.0 always with `--latest=false` (INV-PKG-9); mirror the fix natively (B6) | unchanged | 12 7.12.2 item 5 |

A rollback is rehearsed once on a lab device before WP-E8 (CC7). Rollback never deletes per-user data: the MSI keeps it on uninstall (AC-PKG-9) and Squirrel's uninstall leaves `%APPDATA%\shotAI` in place (AC-PKG-24).

---

## 6. Risk register

### 6.1 Cross-cutting risks

Built from every spec's risk statements (the `Risk` entries of each section 11 and the risks recorded with each verified spec). Probability and impact: H, M, L.

| # | Risk | P / I | Owning WP | Mitigation |
|---|---|---|---|---|
| X1 | `JSON.stringify` bytes not reproduced (number text, escapes, key order), so shared files drift (01, 10) | M / H | WP-A2, WP-A3 | hand-written writer and reader; `ElectronGoldenTests` over Electron-generated goldens (AC-MODEL-3); the settings codec reuses the same writer |
| X2 | A typed step model silently drops unknown fields (01) | M / H | WP-A3 | `ProjectStep` wraps the raw `JsonObject`; `JsonObjectPositionTests` (AC-MODEL-10); `StepPassThroughTests` |
| X3 | Windows path confinement weaker than Electron's (junctions, ADS, device names) (01) | M / H | WP-A5 | name-surrogate reparse detection; hostile-segment rules; junction tests that must execute (AC-MODEL-11, AC-MODEL-12) |
| X4 | Cloud placeholders treated as links and lost by the archive walk (01, Q-MODEL-6) | L / H | WP-A8, WP-A20 | D-15 implemented; measured on P5 in M-A |
| X5 | The low-level hook silently removed by Windows (02 R1) | M / H | WP-B4 | allocation-free proc, watchdog, reinstall logging, Raw Input fallback (Q-CAP-5) |
| X6 | One unshielded screen read puts the pill in a finished SOP (02 R2) | L / H | WP-B1, WP-B5 | type-level funnel, source scans, mutation check (AC-CAP-3) |
| X7 | A popup, tooltip or dialog shown before its capture exclusion (03 R1) | M / H | WP-A13 | `ShotAIWindow`, `PopupExclusion`, the HWND sweep test, the `WH_CALLWNDPROC` fallback |
| X8 | Display affinity fails on layered windows or from worker threads (02 Q-CAP-15) | M / H | WP-B5 | probe before WP-B7 and WP-B8; DL2 fail-closed marshaling |
| X9 | The pill re-activates and steals the first click (03 R2) | M / M | WP-B7 | non-activating styles, foreground assertions in tests, the `SW_SHOWNOACTIVATE` fallback |
| X10 | Mixed-DPI placement errors (03 R3) | M / M | WP-B7, WP-B8, WP-B11 | physical-px positioning after `SourceInitialized`; the 100% plus 150% rig |
| X11 | Timing differences change the foreground window at capture (02 R3) | M / M | WP-B11 | AC-CAP-6, AC-CAP-7, AC-CAP-27; the Q-CAP-4 fallback |
| X12 | App-name derivation drift changes captions and prompts (02 R4) | M / M | WP-B6 | `WindowInfoTests`, AC-CAP-30 |
| X13 | A redaction silently not baked while the save succeeds (04 security findings) | L / H | WP-C6, WP-C7 | fail closed on malformed geometry and unknown types (D-EDIT-2, D-EDIT-3); box-average tiles; end-to-end proofs in M-C and M-D |
| X14 | Render and manifest disagree after a failed write (04, D-EDIT-5) | L / H | WP-C5 | render first with receipt rollback; `StoreRenderTests` |
| X15 | Windows OCR missing or weaker than Tesseract (04 Q-EDIT-5, Q-EDIT-6) | M / M | WP-C11 | explicit notices; measured recall; Feature on Demand in the IT notes |
| X16 | Rasterizer slow or wrong under software rendering (04 Q-EDIT-16) | M / M | WP-C10 | banding; `WpfOverlayRasterizerTests`; PB-10 on the ARM64 VM |
| X17 | The session contract misses a case and edits are lost or editors close (05 Q-REP-14) | M / H | WP-A9 | consolidated S1 to S10, randomized interleaving test |
| X18 | Pilot users read the narrower native report as a regression (05 Q-REP-2) | M / L | WP-A20 | decide the Electron CSS parity fix at M-A; announce otherwise |
| X19 | Report memory and file locks on large projects (05) | M / M | WP-A17 | decode at display size, never hold files open, PB-7 |
| X20 | Hand-built drag, auto-scroll and spinner feel different from Chromium (05 Q-REP-13) | M / L | WP-C3 | tests plus the pilot form |
| X21 | Archivo renders at one weight (06 Q-HOME-2) | M / M | WP-A14 | static instances; `ArchivoRenderingTests` |
| X22 | A `StaticResource` stops following the brand (06) | M / M | WP-A14 | Linux source guards |
| X23 | About 30 deliberate Home and Settings changes look like regressions (06 R-HOME-2) | M / L | WP-E6 | the parity walk cites D-HOME ids |
| X24 | 429 classification needs headers the SDK does not expose (07) | M / M | WP-D6 | per-client header capture; `RateLimitHeaderCaptureTests` through the real retry loop |
| X25 | Egress reads the disk before optimistic edits land (07, 09) | M / H | WP-A9, WP-D5, WP-D10 | `IProjectSettle.WhenSettledAsync` before every egress; `WaitsForPendingEditsBeforeAssembly`, AC-EXP-32 |
| X26 | Sonnet 5 estimate price wrong (07 Q-SOP-1) | M / L | WP-D5 | parity until confirmed, then one change on all platforms |
| X27 | The SDK auto-resolves environment credentials or base URL; disposing a client disposes the handler (08) | M / H | WP-D2 | explicit options, per-client `HttpClient`, environment sanitization, tripwire tests (V9) |
| X28 | WAM returns stale role claims after "Sign in again" (08 Q-AUTH-4) | M / M | WP-D3, WP-D17 | forced silent refresh; AC-AUTH-27 on the live tenant |
| X29 | `MsalCacheHelper` misbehaves on a corrupt or read-only cache (08 Q-AUTH-5) | L / M | WP-D3 | `MsalCachePersistenceTests`; custom DPAPI cache fallback |
| X30 | Conditional Access differs under WAM (08 R2) | M / M | WP-E7 | pilot on real policies |
| X31 | The Anthropic SDK changes under us (08 R3) | M / M | WP-D2 | pinned version, deliberate upgrades only |
| X32 | The AVIF DLL: provenance, ARM64 build, CVEs (09 R-EXP-1) | M / M | WP-D11, WP-E4 | pinned sources, attestation, JPEG fallback |
| X33 | Hidden WebView2 printing fails or leaks requests (09 Q-EXP-3, Q-EXP-25) | M / M | WP-D12 | Windows tests decide; off-screen fallback; CSP plus request filter |
| X34 | Office fidelity gaps (09 R-EXP-2) | M / M | WP-D13, WP-D14 | layout dumps against Electron goldens; manual opens |
| X35 | Package import zip bomb or name tricks (09) | L / H | WP-D15 | declared sizes before inflating, bounded reads, whitelist after cleanup |
| X36 | Byte-identical exports undefinable for images (09 Q-EXP-14) | H / L | WP-D10 | the text-with-replayed-images definition |
| X37 | Settings byte layout or unknown keys drift and break rollback (10) | M / H | WP-A10 | codec byte tests; AC-INFRA-8; AC-PKG-22 |
| X38 | A native prerelease offered to Electron users, or an Electron hotfix taking "latest" (10, 12) | L / H | WP-E4 | flags from the tag grammar, `verify-published`, checklist item 5 |
| X39 | Image decoding moves into the privileged process (11 Q-IPC-16) | M / M | WP-A17 | magic bytes plus explicit decoders (R-ARCH-21) |
| X40 | Cross-spec interface names diverge between implementations (11 Q-IPC-1 to Q-IPC-3) | M / M | every WP | ARCHITECTURE 15.3 wins; each WP edits the specs it touches (2.4) |
| X41 | A subscriber forgets to marshal or keeps a closed view alive (11 Q-IPC-17, Q-IPC-18) | M / M | WP-A12 | `ViewModelBase.VerifyAccess`, `SubscriberDisposalTests` |
| X42 | The exit flush hangs the UI (11 Q-IPC-10) | L / M | WP-A12 | nothing in the flush needs the UI thread; `ExitFlushTests` |
| X43 | Native local data under the Squirrel root deleted at cutover (12 Q-PKG-4) | M / H | WP-A10, WP-E5 | `%LOCALAPPDATA%\LFI\shotAI` (R-ARCH-13); AC-ARCH-7 |
| X44 | Both builds write the same project at once (12 INV-PKG-26) | M / M | WP-E5 | the legacy guard; pilot notes |
| X45 | A public release carries baked tenant values (12 INV-PKG-14) | L / H | WP-E4 | public builds are BYOK; `verify-payload --public` on the extracted MSI |
| X46 | External facts unverified: WiX licence, signing eligibility, runner labels, launch condition (12) | M / M | WP-E3, WP-E4, WP-A5 | decide before the PR; fallbacks recorded in 6.2 |
| X47 | Electron keeps changing during the port (12 Q-PKG-27) | M / M | every WP | freeze to fixes; B6; `MatchesElectronWhileItExists` catches new channels |
| X48 | Cloud sessions cannot run Windows tests or the app, so defects surface late | H / M | every Windows WP | Windows CI on every PR; manual scripts in PR descriptions; phase exits on real hardware |
| X49 | The 6 to 8 week estimate slips (feasibility "Effort") | M / M | this plan | lanes run in parallel; WPs sized to one or two sessions; phase exits measure progress |
| X50 | An `RS0030` allowance lifts every ban in its file, so an allowlisted file can use another banned API unnoticed (found in WP-A1, ARCHITECTURE 14.9) | L / M | every WP that adds an allowlisted file | keep those files single-purpose; ARCHITECTURE 9.5 item 7; `VSTHRD002` still catches waits outside `ShutdownFlush.cs` |

### 6.2 Open questions by spec

Every open question of every spec, with the WP that owns its decision and the decision to apply unless someone decides otherwise before that WP (the spec's recommended default, ARCHITECTURE 15.4). "Closed" means already decided by the architecture or another spec.

#### 01 Model and store

| Q | Topic | Owner | Decision or mitigation |
|---|---|---|---|
| Q-MODEL-1 | `displayScale` out of range | WP-A3 | clamp in the codec (Electron parity); the case stays `open`; revisit with macOS after cutover |
| Q-MODEL-2 | System.Text.Json interpretation | WP-A2 | accepted as I-1; goldens prove bytes |
| Q-MODEL-3 | canonical root key order (D-3) | WP-A3 | accept; goldens compare decode-then-encode |
| Q-MODEL-4 | BOM stripping | WP-A2 | strip on read, never write a BOM |
| Q-MODEL-5 | `FlushFileBuffers` latency | WP-A5 | flush; measure PB-14 at M-A |
| Q-MODEL-6 | cloud placeholders as links | WP-A8 | implement D-15; verify on P5 in M-A; Electron issue if confirmed |
| Q-MODEL-7 | restore size caps | WP-A8 | no caps (streaming) |
| Q-MODEL-8 | hostile-segment rejection | WP-A5 | reject |
| Q-MODEL-9 | queued delete waits | WP-A6 | accept |
| Q-MODEL-10 | import cleanup scope | WP-A7 | exact new folder only, through `ReparseSafeDelete` |
| Q-MODEL-11 | unreadable-manifest text | WP-A17 | the recommended sentence; parser message in the log only |
| Q-MODEL-12 | rollback notice text | WP-C2 | `Your last change couldn't be saved and was undone. ` plus the message |
| Q-MODEL-13 | paths over 260 characters | WP-A8 | `longPathAware` already in the manifest; 300-character-root test on Windows |
| Q-MODEL-14 | macOS fixture location | WP-A3 | copy to `Golden/macos-fixture/` with its README |
| Q-MODEL-15 | unknown brand in `SetProjectThemeAsync` | WP-A6 | `ArgumentException` (D-IPC-9) |
| Q-MODEL-16 | no-op guard for reorder and intro | WP-C1 | yes, value-equal results are `Unchanged` |
| Q-MODEL-17 | listing cost | WP-A16 | parity; cache summaries only if PB-8 fails |
| Q-MODEL-18 | Electron golden generator | WP-A3 | `src/main/codec-golden.test.ts`, env-gated |
| Q-MODEL-19 | `toLowerCase` full mapping | WP-A6 | `ToLowerInvariant` for text and query |
| Q-MODEL-20 | stale tmp files | WP-A6 | delete `project.json.*.tmp` older than 24 hours on open |
| Q-MODEL-21 | archive name rules | WP-A8 | reject `.`, `..` and empty segments |
| Q-MODEL-22 | step ids as file names | WP-C5 | decided by D-EDIT-22: refuse non-segment ids |
| Q-MODEL-23 | `CompressionLevel.Optimal` mapping | WP-A8 | do not assert compressed sizes |
| Q-MODEL-24 | 01's answer to Q-INFRA-3 | closed | agreed: a non-absolute `projectsDir` loads as the default, so the store always sees a fully qualified root; WP-A10 edits no spec |
| Q-MODEL-25 | `ProjectOperation.BumpsUpdatedAt` has no carrier through `MutateAsync` | WP-A9 | keep the property; `ProjectSessionTests.ShippedOperationsBumpUpdatedAt` asserts every shipped operation returns `true`; the first operation that needs `false` adds an optional `bool bumpUpdatedAt = true` to `IProjectService.MutateAsync` (11 7.3.2, ARCHITECTURE 7.4, 01 7.8) in its own PR |

#### 02 Capture

| Q | Topic | Owner | Decision or mitigation |
|---|---|---|---|
| Q-CAP-1 | port `captureSingle` | WP-B2 | no (D16) |
| Q-CAP-2 | hotkey rebinding | WP-B4 | none in 2.0.0 |
| Q-CAP-3 | `MOD_NOREPEAT` | WP-B4 | use it |
| Q-CAP-4 | inactive-window clicks | WP-B11 | parity first; the `WindowFromPoint` fallback if AC-CAP-27 fails |
| Q-CAP-5 | Raw Input | WP-B4, WP-E7 | hook plus watchdog; Raw Input only if pilot logs show reinstalls |
| Q-CAP-6 | screenshot caption wording | WP-B2 | Electron parity |
| Q-CAP-7 | grab-failure wording | WP-B2 | the recommended sentence |
| Q-CAP-8 | window rect source | WP-B11 | `DWMWA_EXTENDED_FRAME_BOUNDS`; switch if crops differ by 14 to 16 px |
| Q-CAP-9 | window list filter | WP-B11 | get-windows filter; compare chooser lists |
| Q-CAP-10 | ComboBox and Edit names | WP-B1 | parity (kept on the allowlist) |
| Q-CAP-11 | monitor id | WP-B5 | `(uint)HMONITOR`, never compared across launches (R-ARCH-22) |
| Q-CAP-12 | `captureSettings` | WP-B2 | never written natively; preserved by the codec |
| Q-CAP-13 | `CAPTUREBLT` | WP-B11 | keep unless flicker without a menu benefit |
| Q-CAP-14 | DXGI Desktop Duplication | WP-B5 | not in 2.0.0 |
| Q-CAP-15 | affinity on layered windows and from workers | WP-B5 | probe first; DL2 if needed |
| Q-CAP-16 | resize interpolation | WP-B11 | WIC Fant; compare legibility |
| Q-CAP-17 | element names in logs | WP-B2 | Debug only |
| Q-CAP-18 | PNG pixel format | WP-B5 | RGBA |
| Q-CAP-19 | GC pauses on the hook thread | WP-B4 | allocation-free proc, pooled frames, watchdog |
| Q-CAP-20 | `lastLeftClick` across sessions | WP-B3 | reset on start |
| Q-CAP-21 | monitor names | WP-B11 | DisplayConfig friendly name; compare |
| Q-CAP-22 | cross-thread affinity and the shield lock | WP-B5 | DL1 plus `ShieldDeadlockTests` |
| Q-CAP-23 | why Electron dropped clicks | WP-B4 | no action; reinstalls logged |
| Q-CAP-24 | incomplete targets | WP-B9 | the picker cannot submit incomplete; D23 warning |

#### 03 Windows and shell

| Q | Topic | Owner | Decision or mitigation |
|---|---|---|---|
| Q-SHELL-1 | single-instance scope | WP-A13 | `Local\` plus SID |
| Q-SHELL-2 | pill corners | WP-B7 | rectangle |
| Q-SHELL-3 | tooltips and popups in the pill | WP-A13, WP-B7 | tests first; `ShotAIPopup` on hover or the hook fallback |
| Q-SHELL-4 | drag of a no-activate window | WP-B7 | manual drag |
| Q-SHELL-5 | `DIPToScreenRect` rounding | WP-B11 | implement as written; compare log lines |
| Q-SHELL-6 | clamp cross-monitor drags | WP-B8 | no clamp (parity) |
| Q-SHELL-7 | Discard dialog buttons | WP-B7 | `Discard` and `Cancel`, Cancel default |
| Q-SHELL-8 | keyboard access to the pill | WP-B7, WP-E6 | accept; revisit in the accessibility pass |
| Q-SHELL-9 | window click-pick | WP-B9 | not added |
| Q-SHELL-10 | macOS menu extras | WP-A15 | not in 2.0.0 |
| Q-SHELL-11 | full-screen details | WP-A15 | cover the monitor, keep the menu |
| Q-SHELL-12 | UI zoom persistence | WP-A15 | not persisted |
| Q-SHELL-13 | stepper hold | WP-C3 | answered by D-REP-17 |
| Q-SHELL-14 | initial monitor | WP-A15 | the cursor's monitor |
| Q-SHELL-15 | window size persistence | WP-A15 | no |
| Q-SHELL-16 | UI-thread exceptions | WP-A12 | log, handle, show the generic notice |
| Q-SHELL-17 | HUD during the screenshot settle | WP-C4 | none |
| Q-SHELL-18 | second launch while recording | WP-B9 | parity (surfaces the main window) |
| Q-SHELL-19 | the legacy guard's `MessageBox` | WP-E5 | keep until S5, allowlisted |
| Q-SHELL-20 | widths and invisible borders | WP-A20 | outer widths; measure once |
| Q-SHELL-21 | `ShowActivated` after `Hide` | WP-B7 | test; `SW_SHOWNOACTIVATE` path if needed |

#### 04 Editor and redaction

| Q | Topic | Owner | Decision or mitigation |
|---|---|---|---|
| Q-EDIT-1 | editor marker colour default | WP-C10 | `MarkerColorFor(step)`; in the release notes |
| Q-EDIT-2 | unknown annotation at flatten | WP-C6 | fail closed |
| Q-EDIT-3 | pixel parity is visual | WP-C13 | accept; AC-EDIT-10 is the bar |
| Q-EDIT-4 | text vertical metrics | WP-C13 | accept WPF placement; em-box offset if pilots notice |
| Q-EDIT-5 | OCR availability and notices | WP-C11 | two notices; Feature on Demand in `INTUNE.md` (WP-E3) |
| Q-EDIT-6 | OCR accuracy | WP-C11 | measure recall; 2x upscale only if lower |
| Q-EDIT-7 | outward rounding | WP-C6 | parity rounding |
| Q-EDIT-8 | versioned render names | WP-C5 | keep `<id>.png` with rollback |
| Q-EDIT-9 | undo | WP-C8 | none in 2.0.0 |
| Q-EDIT-10 | merge sets `captionEditedByUser` | WP-C12 | parity (Q-SOP-7) |
| Q-EDIT-11 | crop interior hit order | WP-C9 | adopt D-EDIT-14 |
| Q-EDIT-12 | card issuer prefixes | WP-C11 | none (parity) |
| Q-EDIT-13 | Auto-redact button | WP-C11 | visible |
| Q-EDIT-14 | arrow endpoint handles | WP-C9 | adopt |
| Q-EDIT-15 | detector golden corpus | WP-C11 | env-gated Electron recorder |
| Q-EDIT-16 | rasterizer on ARM64 | WP-C10 | banding; measure in M-C |
| Q-EDIT-17 | OCR not cancellable | WP-C11 | accept; one scan at a time |
| Q-EDIT-18 | double-click time | WP-C9 | the system value |
| Q-EDIT-19 | OCR token splitting | WP-C11 | measure; join rule if needed |
| Q-EDIT-20 | cross-spec names | WP-C7 | closed by R-ARCH-7 and R-ARCH-8; texts applied in the 2026-09-23 consolidation |
| Q-EDIT-21 | system dialogs are unregistered shotAI HWNDs | WP-C9 (color dialog); WP-B10 and WP-D10 (`IFileDialogs`, `IExportDialogs`) | `CC_ENABLEHOOK` hook registers the `ChooseColor` HWND with `OwnWindowRegistry` on `WM_INITDIALOG`, pinned in `AllWindowsRegisteredTests`; the file dialogs get the equivalent registration in the WP that adds them |

#### 05 Report

| Q | Topic | Owner | Decision or mitigation |
|---|---|---|---|
| Q-REP-1 | rollback wording | WP-C2 | as Q-MODEL-12, one slot, no auto-dismiss |
| Q-REP-2 | native report narrower | WP-A20 | accept; decide the optional Electron CSS fix at M-A |
| Q-REP-3 | title editing and meta line | WP-A17 | not in 2.0.0 |
| Q-REP-4 | deleting steps keeps files | WP-C1 | parity |
| Q-REP-5 | notice prefixes | WP-C4 | fix only the capture guard |
| Q-REP-6 | drop on the trailing gap | WP-C3 | add |
| Q-REP-7 | overview in the empty state | WP-A17 | hidden (parity) |
| Q-REP-8 | delete confirm mentions an image | WP-C2 | parity string |
| Q-REP-9 | `reportZoom` above 4 | WP-C1 | no read cap |
| Q-REP-10 | overview editor as a draft | WP-C2 | parity |
| Q-REP-11 | virtualization | WP-A20 | none unless PB-7 fails |
| Q-REP-12 | Tab between fields | WP-C2 | not in 2.0.0 |
| Q-REP-13 | grab cursors | WP-C3 | `Hand` and `SizeAll` |
| Q-REP-14 | session change kinds | WP-A9 | closed by ARCHITECTURE 7.4 |
| Q-REP-15 | rollback moves a card | WP-C2 | accept |
| Q-REP-16 | draft recovery | WP-C2 | only when no editor of that kind is open |
| Q-REP-17 | text heights differ | WP-A17 | accept |
| Q-REP-18 | slider moves on mouse down | WP-C3 | accept |
| Q-REP-19 | report decode path | WP-A17 | the shared WIC path (R-ARCH-21) |
| Q-REP-20 | drafts across Settings | WP-C2 | keep (D-REP-25) |

#### 06 Home, Settings and tour

| Q | Topic | Owner | Decision or mitigation |
|---|---|---|---|
| Q-HOME-1 | where source guards run | WP-A14 | `ShotAI.Core.Tests/SourceGuards` on Linux |
| Q-HOME-2 | Archivo in WPF | WP-A14 | upstream static instances with `SOURCES.md` |
| Q-HOME-3 | list state across navigation | WP-A16 | parity reset |
| Q-HOME-4 | culture for dates | WP-A16 | `CurrentCulture`; in the release notes |
| Q-HOME-5 | export from the Archive tab | WP-D16 | parity |
| Q-HOME-6 | default focus in destructive confirms | WP-A19 | parity |
| Q-HOME-7 | bulk failure reporting | WP-D16 | parity (last failure) |
| Q-HOME-8 | manual check raising the notice | WP-E1 | parity |
| Q-HOME-9 | recents after a folder change | WP-B10 | parity |
| Q-HOME-10 | tour bubble height | WP-B10 | the 220 DIP rule |
| Q-HOME-11 | blurbs for a third brand | WP-B10 | keyed by brand id |
| Q-HOME-12 | high contrast | WP-A14, WP-E6 | the `SystemColors` mapping after design sign-off; not a pilot blocker |
| Q-HOME-13 | emoji in labels | WP-A16 | accept monochrome |
| Q-HOME-14 | row shadows | WP-A16 | keep; replace if AC-HOME-35 fails |
| Q-HOME-15 | STA test harness | WP-A12 | closed (ARCHITECTURE 15.4): the in-repo helper of ARCHITECTURE 2.1 and 12.1; WP-A12 builds it |
| Q-HOME-16 | unexpected-exception wording | WP-A12 | `Something went wrong. See the log for details.` |
| Q-HOME-17 | update notice outside Home | WP-E1 | parity (any view) |
| R-HOME-1 | restated values go stale | WP-A14, WP-A16 | tests compare against Core and the generated table |
| R-HOME-2 | improvements look like regressions | WP-E6 | the D-HOME list in the parity walk |

#### 07 SOP generation

| Q | Topic | Owner | Decision or mitigation |
|---|---|---|---|
| Q-SOP-1 | Sonnet 5 price | WP-D5 | 3 and 15 USD per MTok until confirmed on anthropic.com/pricing, then all platforms together |
| Q-SOP-2 | estimate omits schema and thinking | WP-D6 | parity |
| Q-SOP-3 | root `$schema` description | WP-D5 | reproduce byte-equal |
| Q-SOP-4 | wholesale revert | WP-D5 | parity |
| Q-SOP-5 | provenance names the first generation | WP-D8 | parity |
| Q-SOP-6 | steps added after the first generation | WP-D5 | parity |
| Q-SOP-7 | answer to Q-EDIT-10 | WP-C12 | keep the flag on a merge |
| Q-SOP-8 | long `Retry-After` waits | WP-D6 | parity plus Cancel; strip above 60 s only if reported |
| Q-SOP-9 | image count and size checks | WP-D6 | parity |
| Q-SOP-10 | the tour mentions Claude when off | WP-B10 | parity |
| Q-SOP-11 | stalled stream timeout | WP-D6 | none; Cancel |
| Q-SOP-12 | "The 1 steps below" | WP-D5 | parity |
| Q-SOP-13 | `SOP ready` badge when off | WP-A16 | keep |
| Q-SOP-14 | `Anthropic` in Core | WP-D2 | allowed (I-4) |
| Q-SOP-15 | where auth is specified | closed | spec 08; 06 and 07 already cite it (2026-09-23 consolidation) |
| Q-SOP-16 | flatten error step number | WP-C7 | parity |
| Q-SOP-17 | client disposal | WP-D2 | closed: own `HttpClient` per client, `disposeHandler: false` |
| Q-SOP-18 | the estimate sends images | WP-D6 | keep |
| Q-SOP-19 | optimistic apply versus a refused disk run | WP-D8 | accept; `SopMessages.Incomplete` with the rollback notice |
| Q-SOP-20 | proxy environment variables | WP-D2 | Q-ARCH-3 option (a): remove them at startup step 5 |
| Q-SOP-21 | an operation throwing on the clone | WP-A9 | closed by S2 |

#### 08 Auth, secrets and policy

| Q | Topic | Owner | Decision or mitigation |
|---|---|---|---|
| Q-AUTH-1 | spec numbering | closed | R-ARCH-14; 07 already follows it (2026-09-23 consolidation) |
| Q-AUTH-2 | import the Electron API key | WP-D2 | no |
| Q-AUTH-3 | WAM redirect URI | WP-D3 (IT action before the pilot) | add to the existing registration |
| Q-AUTH-4 | forced silent refresh | WP-D3, WP-D17 | keep until AC-AUTH-27 proves it redundant |
| Q-AUTH-5 | `MsalCacheHelper` behavior | WP-D3 | pin with tests; custom DPAPI cache if they fail |
| Q-AUTH-6 | beta header on the diagnostic exchange | WP-D4 | send it |
| Q-AUTH-7 | absent `expires_in` | WP-D4 | accept |
| Q-AUTH-8 | cache the federated client | WP-D2 | parity (no cache) |
| Q-AUTH-9 | Request access hint conditions | WP-D7 | parity |
| Q-AUTH-10 | baked file location after cutover | WP-D1, WP-E9 | `dotnet/federation.local.json` with the fallback until cutover |
| Q-AUTH-11 | public releases and baked values | WP-E4 | public builds are BYOK (INV-PKG-14) |
| Q-AUTH-12 | `WorkspaceId = "default"` | WP-D1 | reject (parity) |
| Q-AUTH-13 | owner of `SharedHttp` | WP-D2 | 08 owns and defines the type (08 7.1, 7.14) in `ShotAI.Core.Net`, registered by `AddShotAICore` (ARCHITECTURE 2.4, 4.3); R-ARCH-15 fixes its content |
| Q-AUTH-14 | `docs/MANAGED-CONFIG.md` contradiction | WP-E6 | separate small docs PR |
| Q-AUTH-15 | silent SSO with the Windows account | WP-D3 | not used |
| Q-AUTH-16 | probe tool | WP-D4 | build before the pilot |
| Q-AUTH-17 | `ANTHROPIC_CUSTOM_HEADERS` | WP-D2 | remove at startup plus the host guard (Q-ARCH-4) |
| Q-AUTH-18 | unreferenced ADML help strings | WP-D1 | leave the policy files unchanged |
| Q-AUTH-19 | the MSAL.NET failure for a missing broker redirect URI | WP-D7 | detect `AADSTS50011` in the message or `AdditionalExceptionData` (D23); AC-AUTH-33 confirms or corrects the rule before the pilot |
| 08 R1 | WAM or broker runtime missing | WP-D7, WP-E7 | browser fallback; AC-AUTH-23 on both architectures |
| 08 R2 | Conditional Access differences | WP-E7 | pilot on real policies |
| 08 R3 | SDK volatility | WP-D2 | pinned version; tripwire tests |

#### 09 Export

| Q | Topic | Owner | Decision or mitigation |
|---|---|---|---|
| Q-EXP-1 | `LibraryImport` exception | WP-D11 | accepted (I-3) |
| Q-EXP-2 | AVIF encoder parameters | WP-D11 | verify against `avif_enc.cpp` at jsquash 2.1.1 before the shim |
| Q-EXP-3 | hidden WebView2 printing | WP-D12 | hidden; off-screen excluded window as fallback |
| Q-EXP-4 | PDF margins | WP-D12 | unset; measure against Electron |
| Q-EXP-5 | Electron golden generator | WP-D9 | the `SHOTAI_EXPORT_GOLDENS` run mode (Q-PLAN-1) |
| Q-EXP-6 | created line outside en-US | WP-D9 | accept (D-EXP-23) |
| Q-EXP-7 | A4 versus Letter for Word | WP-D13 | A4 (parity) |
| Q-EXP-8 | Markdown Save As folder rule | WP-D10 | as specified; file the macOS fix |
| Q-EXP-9 | undecodable renders in Office | WP-D13 | parity, logged |
| Q-EXP-10 | Office ignores brand fonts | WP-D13 | parity |
| Q-EXP-11 | `WhenIdleAsync` in 01 | WP-A9 | closed by R-ARCH-6 (`IProjectSettle`) |
| Q-EXP-12 | extended reserved names | WP-D9 | adopt |
| Q-EXP-13 | Save dialog behavior | WP-D10 | `AddExtension`, `OverwritePrompt`; check once against Electron |
| Q-EXP-14 | meaning of byte-identical | WP-D10 | text with replayed or normalized payloads |
| Q-EXP-15 | `%PDF-` check strength | WP-D12 | also require `%%EOF` |
| Q-EXP-16 | zip64 | WP-D15 | never written |
| Q-EXP-17 | 03's `NavigateToString` wording | closed | 03 2.10.2 corrected in the 2026-09-23 consolidation (1.5) |
| Q-EXP-18 | export method names | WP-D10 | 09 7.13 (R-ARCH-9) |
| Q-EXP-19 | resize filter | WP-D10 | HighQualityCubic; compare legibility |
| Q-EXP-20 | WebView2 data folder | WP-D12 | `%LOCALAPPDATA%\LFI\shotAI\WebView2`; kept on uninstall |
| Q-EXP-21 | CRLF in a PowerPoint run | WP-D14 | measure once, then parity or `a:br` |
| Q-EXP-22 | `PrintToPdfStreamAsync` | WP-D12 | keep the temp file |
| Q-EXP-23 | where `shotai_avif.dll` lives | WP-D11 | next to `shotAI.exe` |
| Q-EXP-24 | Electron 42's ICU | WP-D9 | capture one line from the shipping build first |
| Q-EXP-25 | request filter and `file://` | WP-D12 | CSP primary; both layers tested |
| Q-EXP-26 | `System.IO.Compression` leniency | WP-D15 | accept; pin with a test on the shipping runtime |
| Q-EXP-27 | WebView2 runtime egress beyond SmartScreen | WP-D12 | run AC-EXP-42 before the PDF UI ships; add `--disable-background-networking` or `--disable-component-update` only if the trace shows a connection they stop; name any unstoppable connection in the README privacy section (5.2 item 12) |
| R-EXP-1 | the AVIF DLL | WP-D11, WP-E4 | pins, attestation, fallback |
| R-EXP-2 | Office fidelity | WP-D13, WP-D14 | layout dumps, manual opens |

#### 10 Brand, settings and infrastructure

| Q | Topic | Owner | Decision or mitigation |
|---|---|---|---|
| Q-INFRA-1 | preserve unrecognized enum strings | WP-A10 | adopt |
| Q-INFRA-2 | preserve unknown `sop` keys | WP-A10 | adopt |
| Q-INFRA-3 | non-absolute `projectsDir` | WP-A10 | load as the default and warn; 01 agreed (Q-MODEL-24) |
| Q-INFRA-4 | fleet update-check policy | WP-E1 | none in 2.0.0 |
| Q-INFRA-5 | the notice on managed devices | closed | resolved 2026-09-23: the notice follows the install scope (06 INV-HOME-45, built in WP-E1) |
| Q-INFRA-6 | prerelease ordering | WP-E1 | adopt (`2.0.0-alpha.N` is older than `2.0.0`) |
| Q-INFRA-7 | same log file as Electron | WP-A11 | yes |
| Q-INFRA-8 | MSIX virtualization | closed | MSI (I-2); AC-INFRA-31 stays the gate (WP-E3) |
| Q-INFRA-9 | WPF and the variable Archivo | WP-A14 | static instances |
| Q-INFRA-10 | 06's attribution of the palette | closed | R-ARCH-14 |
| Q-INFRA-11 | `SHOTAI_LOG_LEVEL` | WP-A11 | `debug` only |
| Q-INFRA-12 | back up a corrupt file | WP-A10 | one `settings.json.bad` |
| Q-INFRA-13 | Electron's timeout message | WP-E1 | irrelevant natively |
| Q-INFRA-14 | redirect handling | WP-E1 | follow; final host must be `api.github.com` |
| Q-INFRA-15 | in-flight join | WP-E1 | adopt |
| Q-INFRA-16 | environment self-test triggers | WP-A12 | keep both, switches primary |
| Q-INFRA-17 | generator location | WP-A4 | `dotnet/tools/ShotAI.GenBrand` plus the checked-in table |
| Q-INFRA-18 | pending update across launches | WP-E1 | parity (not remembered) |
| Q-INFRA-19 | static Archivo source | WP-A14 | upstream release matching 2.001, else a documented fontTools script |
| Q-INFRA-20 | a write whose re-read fails | WP-A10, WP-B10 | as specified; the notice agreed with 06 in WP-B10 |
| Q-INFRA-21 | user info in links | WP-A19 | parity (allowed) |
| Q-INFRA-22 | does `OpenAsync` throw | WP-A19 | 11's contract (R-ARCH-25) |
| 10 risks | JsJson layout; three generators; GitHub API dependence; users switching builds | WP-A10, WP-A4, WP-E1, WP-E5 | byte tests; parity test plus manual macOS stamp check; `--update-selftest`; pilot notes |

#### 11 Service boundary

| Q | Topic | Owner | Decision or mitigation |
|---|---|---|---|
| Q-IPC-1 | two `IAuthService` shapes | WP-D4 | 08 canonical (R-ARCH-2) |
| Q-IPC-2 | owner of the connection test | WP-D4 | `IAuthService.TestConnectionAsync` (R-ARCH-3) |
| Q-IPC-3 | `IProjectStore` versus `IProjectService` | WP-A6 | `IProjectService` (R-ARCH-4); 09 already uses it (2026-09-23 consolidation) |
| Q-IPC-4 | drop `capture:single` | WP-B2 | yes |
| Q-IPC-5 | keep `ListRecentProjectsAsync` | WP-A6 | keep |
| Q-IPC-6 | `ProjectsChanged` | WP-A8 | event on `IProjectService` raised by auto-archive |
| Q-IPC-7 | a manual update find and the notice | WP-E1 | parity (no push) |
| Q-IPC-8 | unknown `settings.json` keys | WP-A10 | preserve |
| Q-IPC-9 | analyzer noise | WP-A1 | suppress only for `*.g.cs` |
| Q-IPC-10 | exit flush as a blocking wait | WP-A12 | bounded blocking wait (DL4) |
| Q-IPC-11 | debug call logging volume | WP-A12 | Debug level |
| Q-IPC-12 | `ShotAIException` everywhere | WP-A1 | foundation first; each spec derives |
| Q-IPC-13 | recents on a folder change | WP-B10 | parity |
| Q-IPC-14 | where `IExternalLinks` lives | WP-A19 | 11's algorithm, 10's registration |
| Q-IPC-15 | Pause and Resume off the UI thread | WP-B7 | `Task.Run` |
| Q-IPC-16 | image decoding in-process | WP-A17 | closed by R-ARCH-21 (ARCHITECTURE 15.4): explicit decoders after magic bytes; WP-A17 implements it |
| Q-IPC-17 | subscriber that forgets to marshal | WP-A12 | `VerifyAccess` in Debug, affinity tests |
| Q-IPC-18 | singleton keeping a view alive | WP-A12 | `SubscriberDisposalTests` |
| Q-IPC-19 | channel map maintenance | WP-A1 | `MatchesElectronWhileItExists` |
| Q-IPC-20 | dispatcher priority | WP-B9 | `Normal` plus coalescing; the 20-click script |
| Q-IPC-21 | `VSTHRD200` on `Apply` | WP-A9 | keep the names, suppress on two members |
| Q-IPC-22 | `GetState()` ahead of `StepLanded` | WP-B9 | panel shows the list length |
| Q-IPC-23 | `StartAsync` during a screenshot | WP-B2 | throws `A recording is already in progress` (D21) |

#### 12 Packaging, CI and release

| Q | Topic | Owner | Decision or mitigation |
|---|---|---|---|
| Q-PKG-1 | WiX licensing | WP-E3 | read the terms before the PR; pin a pre-fee release or budget the fee |
| Q-PKG-2 | signing eligibility and timestamp URL | WP-E4 | IT owns the subscription; OV in Key Vault as fallback |
| Q-PKG-3 | sign unsigned third-party PEs | WP-E4 | yes, never re-sign a valid one |
| Q-PKG-4 | local data under the Squirrel root | WP-A10 | closed by R-ARCH-13 (`IAppPaths.LocalDataDirectory`) |
| Q-PKG-5 | internal build distribution | WP-E4 | 30-day workflow artifact; decide with IT before S1 |
| Q-PKG-6 | launch-condition comparison | WP-E3 | keep; test on 1909 and 2004 |
| Q-PKG-7 | removing Electron | WP-E8 | Uninstall assignment in user context with the wrapper |
| Q-PKG-8 | legacy guard: block or warn | WP-E5 | block |
| Q-PKG-9 | desktop shortcut | WP-E3 | none; IT passes `DESKTOPSHORTCUT=1` every version |
| Q-PKG-10 | ReadyToRun | WP-E3, WP-E7 | first-party only; keep if it saves 100 ms (PB-9) |
| Q-PKG-11 | symbols | WP-E4 | zip on the release |
| Q-PKG-12 | ARM64 CI runner | WP-A5 | hosted `windows-11-arm`; manual per release otherwise |
| Q-PKG-13 | libaom build | WP-D11 | pinned NASM; native ARM64 build; generic fallback |
| Q-PKG-14 | audit severity in PR CI | WP-A1, WP-E4 | high and critical fail; NU1901 and NU1902 stay warnings unless `ShotAIStrictAudit` is `true`, which `release.yml` sets (12 7.2.1) |
| Q-PKG-15 | fleet update-check opt-out | WP-E1 | not in 2.0.0 |
| Q-PKG-16 | import the Electron key | WP-D2 | no |
| Q-PKG-17 | clean the Chromium profile | WP-E9 | not in 2.0.0; optional after S5 |
| Q-PKG-18 | ship `.intunewin` | WP-E4 | no |
| Q-PKG-19 | `StartupHookSupport` | WP-E2 | set; AC-PKG-18 proves it |
| Q-PKG-20 | x64 MSI on ARM64 | WP-E3 | allowed on Windows 11 on Arm; requirement rules route |
| Q-PKG-21 | Electron's nag after a rollback | WP-E8 | flip 2.0.0 to prerelease only for a rollback longer than a week |
| Q-PKG-22 | retention window | WP-E9 | 90 days after GA |
| Q-PKG-23 | unmanaged users | closed | resolved 2026-09-23: one dual-purpose MSI (12 7.4.5, WP-E3); the 7.12.3 text revised |
| Q-PKG-24 | SHA pins in other workflows | WP-E4 | `release.yml` only |
| Q-PKG-25 | Restart Manager on upgrade | WP-E3 | default; AC-PKG-8 |
| Q-PKG-26 | runtime detection | WP-E3 | by folder |
| Q-PKG-27 | Electron changes during the pilot | every WP | freeze to fixes; B6 |
| Q-PKG-28 | AVIF in the self-test | WP-D11 | extend `--selftest` |
| Q-PKG-29 | NuGet signature validation | WP-A1 | `require` |
| Q-PKG-30 | restricted DLL search and WPF | WP-A12 | measure; `AddDllDirectory` if needed |
| Q-PKG-31 | `docs/native/PLAN.md` missing | closed | this document |
| Q-PKG-32 | AppUserModelID and taskbar pins | WP-E8 | no compatibility attempt; release notes say re-pin |
| Q-PKG-33 | a per-user MSI that carries its own runtime, for PCs outside Intune | WP-E8 | not in 2.0; IT estimates how many PCs install outside Intune, decided before GA |
| Q-PKG-34 | application control where users install personal copies | WP-E7 | IT answers before the pilot; publisher rule or per-machine only |

#### ARCHITECTURE

| Q | Topic | Owner | Decision or mitigation |
|---|---|---|---|
| Q-ARCH-1 | reference machines | WP-A20 | named in 4.0 before M-A |
| Q-ARCH-2 | budgets as targets or gates | WP-A20 | targets; revised once after M-B |
| Q-ARCH-3 | proxy environment variables | WP-D2 | option (a), remove at startup |
| Q-ARCH-4 | `ANTHROPIC_CUSTOM_HEADERS` | WP-D2 | closed (ARCHITECTURE 15.4): default adopted, removed at startup step 5 with the per-client host guard kept (R-ARCH-15, 08 Q-AUTH-17); WP-D2 implements it |
| Q-ARCH-5 | ban `Math.Round` in Core | WP-A1 | closed (ARCHITECTURE 15.4): default adopted, banned Core-wide with only `JsMath.cs` allowlisted (14.9); WP-A1 implements it |
| Q-ARCH-6 | affinity from worker threads | WP-B5 | the probe decides; DL2 if needed |
| Q-ARCH-7 | watch the open project folder | WP-A17 | none in 2.0.0 |

### 6.3 This plan's open decisions

| Q | Question | Default | Decide by |
|---|---|---|---|
| Q-PLAN-1 | The `SHOTAI_EXPORT_GOLDENS` run mode is Electron main-process code, not test-only. Allowed under B6? | yes, as an env-gated switch that changes nothing unless the variable is set, reviewed as a dev tool; the alternative (recording goldens by hand from the Electron UI) is slower and not reproducible | WP-D9 |
| Q-PLAN-2 | Who runs manual ACs and where results live | the maintainer (IT for Intune and tenant items), in the phase tracking issue (2.6) | WP-A1 |
| Q-PLAN-3 | Where `docs/native/` lands (resolved) | resolved: the docs are committed on the branch `claude/native-windows-rewrite-feasibility-8tpy3t` together with the scaffold; P0 is merging that branch's PR to `main` before WP-A1 | P0 |
| Q-PLAN-4 | The feasibility phase C exit needs exports | prove at the render in M-C, through every egress in M-D (1.5) | WP-C13 |
| Q-PLAN-5 | Required status checks while `dotnet.yml` is path-filtered | not required (12 EDGE-PKG-35); authors check both workflows by hand (B3); the maintainer makes them required after the S4 PR removes the path filters (5.2 item 11a) | WP-E9 |

---

## 7. Traceability matrix

Every acceptance criterion of every spec (section 9) and of ARCHITECTURE 12.10, mapped to exactly one work package: the WP whose merged PR meets it (for a manual AC, whose recorded result meets it). 424 criteria; none unmapped. The one split is AC-MODEL-36, whose own text assigns its first clause to WP-A3 and its second to WP-A12 (1.5). Criteria added to the specs after the first pass are appended at the end of their table. The criterion text is abridged for orientation only; the spec text is normative. Waivers and corrections are in 1.5.


### 01 Model and store (MODEL)

| AC | WP | Criterion (abridged; the spec text is normative) |
|---|---|---|
| AC-MODEL-1 | WP-A3 | dotnet test --project tests/ShotAI.Core.Tests on Linux runs ... |
| AC-MODEL-2 | WP-A8 | Every Electron test file in 8.1 has its target class, and each TS case listed there has a ... |
| AC-MODEL-3 | WP-A3 | ElectronGoldenTests: for every golden input, the native output bytes equal the Electron ... |
| AC-MODEL-4 | WP-A2 | JsNumberTests passes the 7.2.2 table and the 10,000-sample round-trip property. |
| AC-MODEL-5 | WP-A2 | JsJsonWriterTests and JsJsonReaderTests pass, including lone surrogates, duplicate keys, BOM ... |
| AC-MODEL-6 | WP-A3 | Decoding and re-encoding the macOS fixture (Fixtures/b7e2c4d1-.../project.json) preserves ... |
| AC-MODEL-7 | WP-A3 | A manifest with steps: [good, null, 42, "x", [], true, {}] decodes to two steps (good and ... |
| AC-MODEL-8 | WP-A6 | MutateSerializeTests: 40 concurrent MutateAsync appends produce INIT followed by 40 x; no .tmp ... |
| AC-MODEL-9 | WP-A6 | ProjectThemeKeyTests pass all 13 cases in 8.1, including byte-identical files for the two ... |
| AC-MODEL-10 | WP-A3 | A test asserts that assigning an existing key through JsonObject's indexer keeps its position ... |
| AC-MODEL-11 | WP-A5 | PathConfineTests and PathConfineSymlinkTests pass on Linux; JunctionConfineTests passes on a ... |
| AC-MODEL-12 | WP-A6 | ReparsePointTraversalTests: after DeleteProjectAsync on a project containing a junction to ... |
| AC-MODEL-13 | WP-A8 | ArchiveTests and ArchiveVerifyTests pass; a project archived natively restores in Electron ... |
| AC-MODEL-14 | WP-A5 | AtomicFileTests asserts the exact delay sequence 10, 25, 50, 100, 200, 350, 600 ms and 8 ... |
| AC-MODEL-15 | WP-A8 | UpdatedAtSemanticsTests passes every row of 2.9.4. |
| AC-MODEL-16 | WP-A6 | KnownProjectGateTests: an outside path throws ProjectNotKnownException with the message ... |
| AC-MODEL-17 | WP-A6 | CreateProjectTests: a project created with brand shotAI has no theme key; with lfi it has ... |
| AC-MODEL-18 | WP-A7 | CreateFromImportTests: a package with a duplicate entry leaves no new folder under the root. |
| AC-MODEL-19 | WP-A7 | StepOperationTests: after each structural op, order equals index + 1 for all steps. |
| AC-MODEL-20 | WP-A7 | ReorderNoDropTests: the step count is unchanged by any ReorderStepsAsync call. |
| AC-MODEL-21 | WP-A7 | ImportStepAsync with a 3-byte FF D8 FF buffer succeeds and names the file .jpg; with 89 50 4E ... |
| AC-MODEL-22 | WP-A8 | AutoArchiveTests: with ages 91 and 89 days and ageDays = 90, exactly the 91-day project is ... |
| AC-MODEL-23 | WP-C2 | ProjectSessionTests pass; in the running app (manual), editing a caption shows the new text ... |
| AC-MODEL-24 | WP-C13 | Manual cross-app check: create a project in Electron v1.3.0, open and edit it natively, reopen ... |
| AC-MODEL-25 | WP-A17 | Manual: open a project whose project.json was saved with a BOM by Notepad; it lists and opens ... |
| AC-MODEL-26 | WP-A1 | ShotAI.Core has no reference to any Windows.* namespace, Microsoft.Win32 or a Windows TFM ... |
| AC-MODEL-27 | WP-A5 | HostileSegmentTests pass; PathConfine.Confine(dir, "shots/a.png:x") and "shots/CON.png" return ... |
| AC-MODEL-28 | WP-A6 | ListProjectsTests: searchText for a project with intro ("Overview","Body") and one step ... |
| AC-MODEL-29 | WP-A6 | ProjectSearchTests: query " OK " over one title hit and one content hit returns two groups ... |
| AC-MODEL-30 | WP-A5 | SerialWriteQueueTests: 1,000 concurrent enqueues complete in enqueue order. |
| AC-MODEL-31 | WP-A19 | App exit with a pending queued write completes that write (manual: rename a project and quit ... |
| AC-MODEL-32 | WP-A3 | CalloutKindTests: glyph code points are exactly U+2139, U+26A0, U+2501 and empty, and none is ... |
| AC-MODEL-33 | WP-A8 | ArchiveNameRulesTests: restoring a zip that contains export\..\project.json throws, keeps ... |
| AC-MODEL-34 | WP-A7 | ImportStepConfineTests: with shots/ replaced by a link to an outside folder, ImportStepAsync ... |
| AC-MODEL-35 | WP-A7 | DeleteStepsMalformedPathTests: DeleteStepsAsync over a step with screenshot: 42 returns ... |
| AC-MODEL-36 | WP-A3 (first clause), WP-A12 (second clause) | ConformanceCase.cs loads cases through JsJson.Parse; ContainerTests.NoAsyncOnlyDisposables and LifecycleTests.ExitOrderMatchesSpec11 ... |

### 02 Capture (CAP)

| AC | WP | Criterion (abridged; the spec text is normative) |
|---|---|---|
| AC-CAP-1 | WP-B1 | CaptureGeometryTests, AutoClassifierTests, ClickCaptionsTests, CaptureShieldTests pass on ... |
| AC-CAP-2 | WP-B3 | CaptureEngineTests and MenuPollTests pass on Linux (every case in 8.4). |
| AC-CAP-3 | WP-B1 | CaptureFunnelSourceTests passes, and introducing a direct IMonitorCapture.Capture call in ... |
| AC-CAP-4 | WP-B1 | JsMathTests passes; no Math.Round( call exists in ShotAI.Core/Capture (source scan in the same ... |
| AC-CAP-5 | WP-B6 | All ShotAI.Platform.Tests capture classes pass on a Windows x64 runner and on a Windows ARM64 ... |
| AC-CAP-6 | WP-B11 | Manual, same machine, same app window: record the same 10-click flow (including a ... |
| AC-CAP-7 | WP-B11 | Manual: right-click in File Explorer, choose View then Extra large icons. The recording ... |
| AC-CAP-8 | WP-B11 | Manual: right-click, press Esc, wait 20 s, click nearby within 30 s. The step still gets a ... |
| AC-CAP-9 | WP-B11 | Manual: five quick left clicks at one spot within 400 ms of each other yield exactly one step. |
| AC-CAP-10 | WP-B9 | Manual: clicks on the pill (every button and the drag area) produce no step and do not change ... |
| AC-CAP-11 | WP-B11 | Manual with remote visibility ON, viewed through a Teams screen share: the pill is visible to ... |
| AC-CAP-12 | WP-B11 | Manual with remote visibility OFF: the pill and main window are never visible in a Teams ... |
| AC-CAP-13 | WP-B5 | ShotAI.ProtectionProbe reports CLEAN at +0 ms for both the normal and the AllowsTransparency ... |
| AC-CAP-14 | WP-B9 | Manual: press Ctrl+Shift+S while recording with Notepad focused: a step Capture: <Notepad ... |
| AC-CAP-15 | WP-B9 | Manual: with another app holding Ctrl+Shift+S, start a recording: recording works mouse-only ... |
| AC-CAP-16 | WP-C4 | Manual: "+ Screenshot" with a closed picked window shows That window is no longer open  ... |
| AC-CAP-17 | WP-C4 | Manual: "+ Screenshot" in Screen mode inserts one step at the chosen gap, the pill never ... |
| AC-CAP-18 | WP-C4 | Manual: "+ Capture" at gap 2 of a 5-step project, three clicks: the new steps are positions 3 ... |
| AC-CAP-19 | WP-B9 | Manual: Discard on a project created for this recording deletes the folder; Discard on an ... |
| AC-CAP-20 | WP-C4 | Manual: delete step 3 of a 5-step project, then record one step: its file is step-0006.png and ... |
| AC-CAP-21 | WP-B11 | Manual, mixed DPI (100% primary, 150% secondary): a double-click 8 physical px apart on the ... |
| AC-CAP-22 | WP-B9 | Manual: with a hung app (a test app whose UI thread sleeps 10 s), clicking it still produces a ... |
| AC-CAP-23 | WP-B9 | Manual: LowLevelHooksTimeout set to 200 ms in the registry and a debugger breakpoint in the ... |
| AC-CAP-24 | WP-B9 | Manual: clicks from a Splashtop or Quick Assist remote session produce steps. |
| AC-CAP-25 | WP-B9 | Manual: a stored PNG opened in an image viewer is fully opaque (no transparency checkerboard). |
| AC-CAP-26 | WP-B9 | Manual: with a shots junction pointing outside the project, Start is refused with the D10 ... |
| AC-CAP-27 | WP-B11 | Manual: in auto mode, click an inactive window's content area once. The step crops to the ... |
| AC-CAP-28 | WP-B9 | --capture-selftest prints [capture-test] PASS on a Windows machine with at least one visible ... |
| AC-CAP-29 | WP-B11 | Manual: in Screen mode with monitor 2 chosen, right-click on monitor 1 and select a menu item ... |
| AC-CAP-30 | WP-D8 | Manual: the SOP review list (spec 07) shows the same App: and Window: values for a native step ... |
| AC-CAP-31 | WP-B11 | Manual: put a file step-99999999999999999999.png in a project's shots/ and record three ... |
| AC-CAP-32 | WP-B11 | Manual, UWP: record a click in Windows Settings in both builds. window.app matches and ... |
| AC-CAP-33 | WP-B11 | ShieldDeadlockTests passes on a Windows runner, and a diagnostic build that toggles remote ... |
| AC-CAP-34 | WP-B11 | Manual, same machine: the same 5-click flow in Window mode (normal, maximized, 150% monitor) and Area mode in both builds; PNG size within 1 px, same crop origin ... decides Q-CAP-8 |
| AC-CAP-35 | WP-B6 | CaptureEngineTests.DisposeIsSynchronousAndIdempotent, .EventsGoThroughEventRaiser ... pass on Linux, and ContainerTests.NoAsyncOnlyDisposables passes with CaptureEngine registered ... |

### 03 Windows and shell (SHELL)

| AC | WP | Criterion (abridged; the spec text is normative) |
|---|---|---|
| AC-SHELL-1 | WP-B8 | dotnet test of ShotAI.Core.Tests passes every class in 8.3's Core table on Linux. |
| AC-SHELL-2 | WP-B8 | On Windows, ShotAI.Platform.Tests and ShotAI.App.Tests pass. |
| AC-SHELL-3 | WP-B9 | Manual: launch, then click the main window's X. Task Manager shows no shotAI.exe within 2 s. ... |
| AC-SHELL-4 | WP-A13 | Manual: with shotAI running and minimized, launch it again from the Start menu. No second ... |
| AC-SHELL-5 | WP-A15 | Manual: fresh launch on a 1920 x 1080 monitor at 100%: the main window is 720 x 740 DIP ... |
| AC-SHELL-6 | WP-C3 | Manual: open a project at 100% scale: the window grows to 1010 DIP, keeping its center ... |
| AC-SHELL-7 | WP-B9 | Manual: start a recording from a window on the secondary monitor. The main window disappears ... |
| AC-SHELL-8 | WP-B9 | Manual: drag the pill by its label and by its hint row; it follows the cursor live without ... |
| AC-SHELL-9 | WP-B9 | Manual: during a recording, click Pause once. The first click pauses (label Paused · N in ... |
| AC-SHELL-10 | WP-B9 | Manual: immediately after starting a recording, click once on a button in another app. That ... |
| AC-SHELL-11 | WP-B9 | Manual: each capture flashes a green ring on the pill for about 0.7 s; with "Show animations ... |
| AC-SHELL-12 | WP-B9 | Manual (with a test hook that raises CaptureFailed("") and then CaptureFailed("Disk full")) ... |
| AC-SHELL-13 | WP-B9 | Manual: click the pill's ✕ on a new project's first recording: the dialog reads Discard this ... |
| AC-SHELL-14 | WP-B9 | Manual: double-click Stop quickly: exactly one stop happens, the pill hides once, and the main ... |
| AC-SHELL-15 | WP-B9 | Manual, two monitors at 100% and 150%: choose Area, the main window hides, both monitors show ... |
| AC-SHELL-16 | WP-B9 | Manual: drag a 400 x 300 DIP rectangle on the 150% monitor: outside dims at 40% black, a 2 DIP ... |
| AC-SHELL-17 | WP-B9 | Manual: a click without drag, a 3 x 3 drag, a right click and Esc each cancel the selection ... |
| AC-SHELL-18 | WP-B11 | Manual: with remote visibility ON and a Teams or Splashtop session watching, the Area overlay ... |
| AC-SHELL-19 | WP-B9 | ShotAI.ProtectionProbe (spec 02) run against the pill window with the setting OFF reports ... |
| AC-SHELL-20 | WP-A18 | Manual: View then Brand is disabled on Home; with a project open and no pin, App default (<app ... |
| AC-SHELL-21 | WP-A18 | Manual, with a debug build that re-raises the navigation state every second without changing ... |
| AC-SHELL-22 | WP-D15 | Manual: Ctrl+O on Home runs the import flow, Ctrl+, opens Settings; both do nothing while a ... |
| AC-SHELL-23 | WP-A15 | Manual: Ctrl+Shift+= (and Ctrl+=), Ctrl+-, Ctrl+0 zoom the main window's content in half-level ... |
| AC-SHELL-24 | WP-A15 | Manual: Help then About shotAI shows title About shotAI, shotAI <version>, the tagline with ... |
| AC-SHELL-25 | WP-A12 | Log review after a normal session: the runtime line, exiting (code 0); after a forced ... |
| AC-SHELL-26 | WP-B9 | Manual: disconnect the monitor where the pill was left, start a recording: the pill docks ... |
| AC-SHELL-27 | WP-B9 | Manual, while recording: sign out of Windows. The log shows teardown before exit; after ... |
| AC-SHELL-28 | WP-E6 | Code review: no file in ShotAI.App calls MessageBox.Show, Dispatcher.Invoke( from capture ... |
| AC-SHELL-29 | WP-B8 | Code review: ShotAI.Core/Shell references no System.Windows, Windows.Win32 or Microsoft.Win32 ... |
| AC-SHELL-30 | WP-E3 | Manual (spec 12): after installing the MSI offline (spec 12 AC-PKG-2), Settings then Apps then ... |
| AC-SHELL-31 | WP-B9 | Manual, primary at 100% and secondary at 150%: launch with the cursor on the secondary. The ... |
| AC-SHELL-32 | WP-B9 | Manual, with Notepad open behind shotAI: start and stop a recording three times. After every ... |
| AC-SHELL-33 | WP-A16 | Manual (startup auto-archive): with archiveAgeDays 90, a 91-day-old project is under Archive and an 89-day-old one under Projects without interaction ... |

### 04 Editor and redaction (EDIT)

| AC | WP | Criterion (abridged; the spec text is normative) |
|---|---|---|
| AC-EDIT-1 | WP-C11 | Editor/EditorGeometryTests, Redaction/SensitiveTextDetectorTests, Redaction/RenderGateTests ... |
| AC-EDIT-2 | WP-C11 | Every class in 8.2's ShotAI.Core.Tests table passes on the Linux CI job. |
| AC-EDIT-3 | WP-C6 | Rendering/FlattenerTests contains all eleven ported macOS flatten cases and passes. |
| AC-EDIT-4 | WP-C6 | RedactionBakerTests.PermutingPixelsWithinACellDoesNotChangeTheOutput and ... |
| AC-EDIT-5 | WP-D17 | EndToEndRedactionTests passes on the Windows runner: no exported or sent image contains ... |
| AC-EDIT-6 | WP-D10 | A step whose blur is added and then the render invalidated by a direct UpdateStepAsync(patch ... |
| AC-EDIT-7 | WP-C10 | Opening, editing and saving a step natively writes export\.render\<id>.png, sets flattened to ... |
| AC-EDIT-8 | WP-C13 | A project saved by the native editor opens in Electron v1.3.0 and in the macOS app with the ... |
| AC-EDIT-9 | WP-C13 | Annotations created natively serialize with the exact key order of 2.1 ... |
| AC-EDIT-10 | WP-C13 | Manual visual parity: the same project flattened by Electron and by native, compared side by ... |
| AC-EDIT-11 | WP-C11 | Every string in 2.22 marked REQUIRED appears verbatim in the native UI or error path (a ... |
| AC-EDIT-12 | WP-C9 | Tool behavior: for each row of the 2.6 table, a manual run (and EditorDocumentTests) produces ... |
| AC-EDIT-13 | WP-C9 | Transform behavior matches the 2.7 table in TransformMathTests; manually, a stamp resized from ... |
| AC-EDIT-14 | WP-C9 | Keyboard: Delete and Backspace remove the selection (marker, crop or annotation); Escape ... |
| AC-EDIT-15 | WP-C9 | Text: placing a text and typing shows the text live at the click point; Enter, Escape ... |
| AC-EDIT-16 | WP-C9 | View: a 400 px wide screenshot fills the canvas width at 100%; a tall screenshot is width-fit ... |
| AC-EDIT-17 | WP-C9 | A reopened step with a crop opens in the cropped view; Apply crop / Show full toggle and reset ... |
| AC-EDIT-18 | WP-C10 | Save with a 0.4 px wide blur inside the image fails with A redaction region could not be ... |
| AC-EDIT-19 | WP-C10 | Save completes (editor closes, report updated) only after project.json and the render are both ... |
| AC-EDIT-20 | WP-C10 | A step with a junctioned shots folder: the editor refuses to open it (Could not load the ... |
| AC-EDIT-21 | WP-C11 | Auto-redact on the Windows OCR fixture adds one solid redaction over each of the SSN, card and ... |
| AC-EDIT-22 | WP-C11 | Auto-redact on a machine without an OCR recognizer shows the Unavailable notice of 7.9 and ... |
| AC-EDIT-23 | WP-C11 | OCR logs contain only counts and the file name (OcrLoggingTests); a manual review of the ocr ... |
| AC-EDIT-24 | WP-C11 | The detector output for 50 recorded OCR line sets (words and boxes captured from Electron's ... |
| AC-EDIT-25 | WP-D17 | EnsureFlattenedAsync runs before every SOP estimate and generation, every in-project export ... |
| AC-EDIT-26 | WP-C12 | A merge (05) of a right-click step into the next step writes one render with both rings baked ... |
| AC-EDIT-27 | WP-C10 | markerBaked and the report overlay: after a native save of a step with a click, the report ... |
| AC-EDIT-28 | WP-C13 | Saving and flattening a 7680 by 2160 screenshot with ten pixelate redactions completes in ... |
| AC-EDIT-29 | WP-C13 | The editor is fully keyboard-reachable (Tab through rail, bars and buttons) and every tool ... |
| AC-EDIT-30 | WP-C6 | A hand-edited blur with blockSize: "abc" bakes as a block-12 mosaic, and one with width: "50" ... |
| AC-EDIT-31 | WP-C10 | Unknown annotation types in a project ({"type":"futurekind","id":"f1"}) survive a native ... |
| AC-EDIT-32 | WP-C10 | A step whose id is hand-edited to ../../shots/abc (or a/b) is refused by a native editor save ... |
| AC-EDIT-33 | WP-C10 | A step whose annotations hold a blur without an id, a null and a 42 opens in the native editor ... |
| AC-EDIT-34 | WP-C9 | Pressing Escape during a rect drag and during an arrow drag leaves no shape and no preview on ... |
| AC-EDIT-35 | WP-C10 | A step whose shots/ file is neither PNG nor JPEG by magic bytes is refused everywhere this subsystem decodes; only the built-in decoders ... |

### 05 Report (REP)

| AC | WP | Criterion (abridged; the spec text is normative) |
|---|---|---|
| AC-REP-1 | WP-A17 | Geometry/ReportGeometryTests, Geometry/DocScaleTests and Geometry/ReportMatchesExportTests ... |
| AC-REP-2 | WP-D10 | Report/PopoverPlacementTests and App.Tests Report/CommandBarTests cover every intent of ... |
| AC-REP-3 | WP-A17 | Geometry/ReportLayoutTests.FullWidthFigureEqualsExport passes, and App.Tests ... |
| AC-REP-4 | WP-D10 | Manual: the same project exported to HTML from the native app and opened in Edge at 100% zoom ... |
| AC-REP-5 | WP-A17 | Report/ReportPresentationTests.NumbersSkipKnownCalloutsOnly passes, and a project containing a ... |
| AC-REP-6 | WP-C2 | Report/OptimisticPolicyTests and App.Tests ... |
| AC-REP-7 | WP-C2 | Manual: with project.json made read-only, editing a caption shows the new caption at once ... |
| AC-REP-8 | WP-C2 | Manual: on a 20-step project, clicking "Move down" on step 1 five times as fast as possible ... |
| AC-REP-9 | WP-C1 | Every edit path R1 to R11 and P1 to P8 behaves per the 7.5 table: Report/OptimisticPolicyTests ... |
| AC-REP-10 | WP-C10 | Manual: an editor save (04) on a step with a new blur shows the updated render only after the ... |
| AC-REP-11 | WP-C1 | Report/ReportOperationsTests.AddTextStepSerializesLikeElectron passes: a native text insert ... |
| AC-REP-12 | WP-C2 | Report/CardListDiffTests.CaptionEditTouchesOneCard passes, and on a 100-step project a caption ... |
| AC-REP-13 | WP-C3 | Report/DocScaleEditorTests passes every row of the 2.5 event table and every typed sequence ... |
| AC-REP-14 | WP-C3 | Manual: dragging the size slider from 125% to 90% previews the layout continuously, the window ... |
| AC-REP-15 | WP-C3 | Manual: type 83 in the percent box and press Escape, then click elsewhere: the scale is ... |
| AC-REP-16 | WP-C3 | Manual: clicking the slider thumb without moving it and tabbing through the command bar leave ... |
| AC-REP-17 | WP-C3 | Manual: at zoom 1, dragging on a screenshot does nothing and writes nothing; at zoom 2.44 ... |
| AC-REP-18 | WP-C3 | Manual: zoom in is disabled at 4, zoom out and reset are disabled at 1, and two rapid zoom-in ... |
| AC-REP-19 | WP-C3 | App.Tests Report/StepFigureTests.IdleControlsAreNotHitTestable passes; manual: after deleting ... |
| AC-REP-20 | WP-C12 | Manual: a right-click step followed by a menu-selection step shows the merge banner; Merge ... |
| AC-REP-21 | WP-C12 | Manual: while a merge is in flight, Delete and Move on the two involved steps are disabled and ... |
| AC-REP-22 | WP-C4 | Manual: each insert menu entry works at gap 0, a middle gap and the last gap: Text and each ... |
| AC-REP-23 | WP-D10 | Manual: with a text step editor open, Resume capturing and Export are disabled with the ... |
| AC-REP-24 | WP-D10 | Manual: in a window narrowed to 680 DIP with the command bar wrapped onto two rows, the export ... |
| AC-REP-25 | WP-D15 | Manual: open the package dialog, check "Include original screenshots", export, reopen the ... |
| AC-REP-26 | WP-C2 | Manual: every string of 2.22 appears exactly as quoted (spot-check with the UIA tree or ... |
| AC-REP-27 | WP-C10 | App.Tests Report/ReportImageLoaderTests passes; manual: with the report open on a project ... |
| AC-REP-28 | WP-A17 | Manual: a project copied with a hand-edited flattened: "../../outside.png" shows the ... |
| AC-REP-29 | WP-C3 | Manual: dragging a step near the bottom edge of the report scrolls it; dropping on the ... |
| AC-REP-30 | WP-C2 | Manual: going Back while a slow write is pending and opening another project immediately shows ... |
| AC-REP-31 | WP-C2 | Manual: a failed persist while the report is scrolled to the bottom shows the rollback notice ... |
| AC-REP-32 | WP-A17 | Manual: a project whose project.json is damaged (invalid JSON) shows a Home notice when opened ... |
| AC-REP-33 | WP-A17 | Manual: an imported JPEG with EXIF orientation 6 displays upright in the report with its ... |
| AC-REP-34 | WP-C4 | Report/CaptureTargetPickerTests passes, and manually the capture-insert modal for each variant ... |
| AC-REP-35 | WP-D10 | Manual: with a text draft open, open Settings from the SOP panel and return: the project is ... |
| AC-REP-36 | WP-D10 | Manual: with the export menu open, one click on a step caption closes the menu and opens the ... |
| AC-REP-37 | WP-D10 | Report/ReportPresentationTests.FramingReadNormalization passes, and a hand-edited reportPanX ... |
| AC-REP-38 | WP-C10 | Manual: re-saving a step with a new crop replaces the report image and its box size in one ... |

### 06 Home, Settings, tour (HOME)

| AC | WP | Criterion (abridged; the spec text is normative) |
|---|---|---|
| AC-HOME-1 | WP-A16 | DateGroupsTests (all 8 ported cases) and DateGroupsDstTests pass on Linux and Windows. |
| AC-HOME-2 | WP-B9 | HomeListPipelineTests, HomeSelectionTests, RenameSessionTests, BulkRunnerTests ... |
| AC-HOME-3 | WP-A14 | XamlChromeGuardTests and XamlResourceKeyGuardTests pass on Linux, and each fails when a ... |
| AC-HOME-4 | WP-A14 | ThemeTokenSetTests passes and every value equals 10's generated table for shotAI and LFI in ... |
| AC-HOME-5 | WP-A16 | Manual: with 12 projects (3 archived) the tabs read Projects 9 and Archive 3; typing a query ... |
| AC-HOME-6 | WP-A16 | Manual: sorted by Modified descending with projects edited today, 9 days ago and 40 days ago ... |
| AC-HOME-7 | WP-A19 | Manual: click the checkbox of row 2, shift-click row 5: rows 2 to 5 are selected and the bar ... |
| AC-HOME-8 | WP-D16 | Manual: select 3 projects, ⤓ Export ▾, One shared folder…, HTML: one folder dialog; the bar ... |
| AC-HOME-9 | WP-D16 | Manual: cancelling the folder dialog in AC-HOME-8 writes nothing, shows nothing and keeps the ... |
| AC-HOME-10 | WP-A19 | Manual: Delete on a row shows Delete "<title>"? This removes the project folder and its ... |
| AC-HOME-11 | WP-A19 | Manual: rename a project to the same name with surrounding spaces and press Enter: no write ... |
| AC-HOME-12 | WP-A16 | Manual: with Home open, create a project folder from another machine or the macOS app in the ... |
| AC-HOME-13 | WP-B9 | Manual: Window mode with no windows open shows Pick a window above to start recording , that's ... |
| AC-HOME-14 | WP-B9 | Manual: type a name, press Enter in Screen mode: the window hides, the pill shows, and Discard ... |
| AC-HOME-15 | WP-B10 | Manual: scroll the Home list down, open a project, go back: the list is at the same offset ... |
| AC-HOME-16 | WP-B10 | Manual: on a fresh profile (no settings.json) the tour opens on first launch with Step 1 of 5 ... |
| AC-HOME-17 | WP-B10 | TourLayoutTests pass; manual: with the window 700 DIP tall, step 5's bubble sits below the ... |
| AC-HOME-18 | WP-B10 | Manual: Settings, Appearance, Theme Dark repaints the whole window at once, the focused ... |
| AC-HOME-19 | WP-B10 | Manual: open a project pinned to LFI while the app brand is shotAI: the project view is LFI ... |
| AC-HOME-20 | WP-B10 | Manual: Theme System; switch Windows "Choose your default app mode" between Light and Dark ... |
| AC-HOME-21 | WP-A14 | Manual: set Windows to dark app mode and Theme System, launch: the first frame of the main ... |
| AC-HOME-22 | WP-B10 | Manual: every Settings control writes the key in 2.30 (inspect settings.json after each ... |
| AC-HOME-23 | WP-B10 | Manual: make settings.json read-only, change Theme: the UI shows the change, then reverts it ... |
| AC-HOME-24 | WP-B10 | Manual: clear the name in About with Include my name on, click elsewhere: the toggle turns off ... |
| AC-HOME-25 | WP-D7 | Manual (unconfigured build, no federation policy): the AI tab shows the API-key group and no ... |
| AC-HOME-26 | WP-D7 | Manual (federation policy present): the AI tab shows Microsoft sign-in, Sign in with Microsoft ... |
| AC-HOME-27 | WP-D7 | Manual: save an API key: the field empties, the status reads A key is saved (encrypted) on ... |
| AC-HOME-28 | WP-E1 | UpdateNoticeTests.OpensThroughLauncher passes and every URL this subsystem opens ... |
| AC-HOME-29 | WP-E1 | Manual: run a build whose version is lower than the latest GitHub release: shotAI <v> is ... |
| AC-HOME-30 | WP-E1 | Manual: Settings, About, ↻ Check now on a current build shows You're up to date.; offline ... |
| AC-HOME-31 | WP-A19 | Manual: trigger an error while the list is scrolled down (for example rename a project whose ... |
| AC-HOME-32 | WP-E6 | Keyboard-only manual pass: every control in Home, Settings, the confirm dialog, the menus and ... |
| AC-HOME-33 | WP-E6 | Accessibility manual pass with Narrator and Accessibility Insights for Windows (FastPass): no ... |
| AC-HOME-34 | WP-B11 | SettingsViewModelTests.RemoteVisibleAppliesImmediately passes; manual: turn the ... |
| AC-HOME-35 | WP-A16 | Manual: with 500 projects the Home list scrolls without visible stutter and a refresh with no ... |
| AC-HOME-36 | WP-D7 | Manual [SECURITY]: with shotAI running and Settings closed, add (or remove) the federation ... |
| AC-HOME-37 | WP-B11 | Manual [SECURITY]: with remote visibility off, open a row's ⋯ menu, the bulk ⤓ Export ▾ menu ... |
| AC-HOME-38 | WP-B10 | Manual: open Settings from Home, then create a project folder in the projects folder from ... |
| AC-HOME-39 | WP-A19 | Manual (archiving from Home): Archive moves the row at once and leaves project.json plus archive.zip; bulk Archive counts; Restore; Open on an archived row; rollback ... |
| AC-HOME-40 | WP-D16 | Manual (bulk export to each project's own folder): 3 projects to Word, no dialog, Exporting 1 of 3... to 3 of 3, <title>.docx then <title> (1).docx ... |
| AC-HOME-41 | WP-E6 | Manual (INV-HOME-45): install a build versioned below the latest GitHub release per-machine (ALLUSERS=1, 12 AC-PKG-2) ... |

### 07 SOP generation (SOP)

| AC | WP | Criterion (abridged; the spec text is normative) |
|---|---|---|
| AC-SOP-1 | WP-D6 | Every test in 8.1 to 8.5 passes on Linux in ShotAI.Core.Tests, and each paired-wording test ... |
| AC-SOP-2 | WP-D5 | SopPromptTextTests.BaseIsVerbatim passes: the native base prompt is byte-equal to the Electron ... |
| AC-SOP-3 | WP-D5 | SopRequestAssemblerTests.BlockOrderAndSingleBreakpoint passes, and the same fixture project ... |
| AC-SOP-4 | WP-D6 | SopEditSchemaTests.MatchesElectronWireSchema and .MinItemsReachesTheWire pass. |
| AC-SOP-5 | WP-D8 | MasterSwitchTests.EveryEntryPointRefusesWithoutIo passes; manually, with the switch off, a ... |
| AC-SOP-6 | WP-D17 | EgressPinTests pass; manually, with ANTHROPIC_BASE_URL=https://example.invalid and ... |
| AC-SOP-7 | WP-D17 | Manual: on a project whose first item is a text callout followed by three screenshots ... |
| AC-SOP-8 | WP-D8 | Manual: with a blurred step whose render was deleted from export/.render/ (the manifest still ... |
| AC-SOP-9 | WP-D8 | Manual: the review dialog shows one row per non-AI step with the same numbers as the prompt, a ... |
| AC-SOP-10 | WP-D8 | Manual: Cancel on the preparing card returns to idle with no error even when clicked just as ... |
| AC-SOP-11 | WP-D8 | Manual: the progress dialog shows Preparing the request\u2026, then Claude is analyzing the ... |
| AC-SOP-12 | WP-D8 | ClaudeServiceTests pass; manually, disconnecting the network before Send shows exactly Could ... |
| AC-SOP-13 | WP-D8 | Manual: with a bogus API key, Test connection shows Claude API: Invalid API key. and Generate ... |
| AC-SOP-14 | WP-D6 | RateLimitHeaderCaptureTests pass (the transient and hard-stop messages reach the user through ... |
| AC-SOP-15 | WP-D8 | Manual: Regenerate asks the exact confirm; after it, Revert restores the pre-first-generation ... |
| AC-SOP-16 | WP-D17 | SopRevertTests.InvalidatesRendersChangedSinceSnapshot passes; manually, generate, remove a ... |
| AC-SOP-17 | WP-D17 | IntroAuthoredTests pass, and a project written by the macOS app with introEditedByUser: true ... |
| AC-SOP-18 | WP-D8 | SopPanelViewModelTests pass on the Windows runner. |
| AC-SOP-19 | WP-D17 | Manual: a signed-in federated user opening a project sees an enabled Generate button with no ... |
| AC-SOP-20 | WP-D17 | Manual: the review warning reads that the screenshots "have already been sent to Anthropic" ... |
| AC-SOP-21 | WP-D17 | Log review: after a failed and a successful generation, shotai.log contains the 2.11 lines ... |
| AC-SOP-22 | WP-D6 | The solution builds with warnings as errors on Linux and Windows with the Anthropic package ... |
| AC-SOP-23 | WP-D17 | Manual: behind a TLS-inspecting corporate proxy configured only in Windows settings (PAC or ... |
| AC-SOP-24 | WP-D8 | Manual: tone Concise and trimmed custom instructions end the captured system prompt exactly; author note and section callouts stay byte-identical after Generate ... |

### 08 Auth, secrets, policy (AUTH)

| AC | WP | Criterion (abridged; the spec text is normative) |
|---|---|---|
| AC-AUTH-1 | WP-D1 | AdmxContractTests passes on Linux against the unchanged Intune/Windows/shotAI.admx and ... |
| AC-AUTH-2 | WP-D1 | FederationConfigValidatorTests, FederationSourcesTests, PolicyValueParserTests ... |
| AC-AUTH-3 | WP-D1 | For each of these fixtures, the native ResolveNow() returns the same ok, missing, invalid ... |
| AC-AUTH-4 | WP-D7 | Manual, Windows, administrator: set the eight values with reg add ... |
| AC-AUTH-5 | WP-D7 | Manual: create FederationRuleId as REG_EXPAND_SZ over a baked build: the baked value is used ... |
| AC-AUTH-6 | WP-D7 | A build without federation.local.json shows the AI tab with no mention of Entra, Microsoft or ... |
| AC-AUTH-7 | WP-D7 | CompositionTests.AuthServicesAreLazy passes, and a Process Monitor trace of a cold launch to ... |
| AC-AUTH-8 | WP-D4 | AuthServiceModeSelectionTests passes: the five rows of the 2.13 table with the exact S1 and S2 ... |
| AC-AUTH-9 | WP-D2 | AnthropicClientFactoryTests.EnvironmentCannotRedirectOrReplaceCredential passes with ... |
| AC-AUTH-10 | WP-D2 | AnthropicClientFactoryTests.FederatedClientWiresAllIdsAndReusesToken: two models.retrieve ... |
| AC-AUTH-11 | WP-D3 | EntraIdentityTokenProviderTests.NeverCallsInteractive passes, and SourceScanTests confirms ... |
| AC-AUTH-12 | WP-D4 | ConnectionTesterTests passes with each 2.14 message produced byte for byte (the C# strings ... |
| AC-AUTH-13 | WP-D4 | FederationExchangeTests passes, including NeverLeaksResponseBody and ... |
| AC-AUTH-14 | WP-D4 | JwtRolesTests passes, and ConnectionTesterTests.RoleFalseAfterSuccessfulExchangeStillPasses ... |
| AC-AUTH-15 | WP-D2 | ApiKeyStoreTests passes; after SetApiKeyAsync(" sk-ant-test ") the file ... |
| AC-AUTH-16 | WP-D7 | Manual: with a key saved, copy secrets.dpapi.json to another Windows user's profile and start ... |
| AC-AUTH-17 | WP-E5 | Manual: on a machine with Electron shotAI 1.3.0 installed and a key saved there, install ... |
| AC-AUTH-18 | WP-D4 | AuthStatusShapeTests and AuthFacadeSurfaceTests pass. |
| AC-AUTH-19 | WP-D7 | SupportUrlAllowlistTests passes; manually, with SupportUrl=https://help.example.com/shotai ... |
| AC-AUTH-20 | WP-D3 | MsalCachePersistenceTests passes on Windows (x64 and ARM64 runners). |
| AC-AUTH-21 | WP-D7 | Manual: sign in, quit, overwrite %LOCALAPPDATA%\LFI\shotAI\entra\msal-cache.bin with random bytes ... |
| AC-AUTH-22 | WP-D4 | Manual: the single-instance lock (03) prevents a second app process, so run the probe tool ... |
| AC-AUTH-23 | WP-D7 | Manual: on a machine where ms-appx-web://microsoft.aad.brokerplugin/{ClientAppId} is ... |
| AC-AUTH-24 | WP-D7 | Manual: open the WAM dialog and close it: Settings returns to Sign in with Microsoft with no ... |
| AC-AUTH-25 | WP-D7 | Manual: sign out: the account disappears, a previously saved API key is still reported (A key ... |
| AC-AUTH-26 | WP-D17 | Manual, live tenant, non-admin assigned account: sign in, Test connection shows Connected ... |
| AC-AUTH-27 | WP-D17 | Manual, live tenant, the stale-roles trap: with a non-admin account that has NO shotAI.User ... |
| AC-AUTH-28 | WP-D17 | Manual, live tenant: remove the role assignment from a signed-in user; the next generation ... |
| AC-AUTH-29 | WP-D17 | Manual: behind a TLS-inspecting, authenticating corporate proxy configured as the Windows ... |
| AC-AUTH-30 | WP-D4 | Manual: the probe tool's legs 0 to 4 all pass against the live tenant with shotAI's own client ... |
| AC-AUTH-31 | WP-E6 | A repository grep for GUIDs in dotnet/ and docs/native/ ([0-9a-fA-F]{8}-[0-9a-fA-F]{4}-) finds ... |
| AC-AUTH-32 | WP-D7 | Manual, x64 and ARM64: where WAM cannot be used (or a SHOTAI_NO_BROKER test build), Sign in with Microsoft falls back to the system browser and completes ... |
| AC-AUTH-33 | WP-D7 | Manual: against a placeholder registration without the broker redirect URI, sign-in fails with Error: plus S18 exactly ... closes Q-AUTH-19 |
| AC-AUTH-34 | WP-D2 | AnthropicHostGuardHandlerTests, SharedHttpTests and AnthropicClientFactoryTests.EachClientGetsFreshHandlers pass on Linux ... |

### 09 Export (EXP)

| AC | WP | Criterion (abridged; the spec text is normative) |
|---|---|---|
| AC-EXP-1 | WP-D9 | ExportCssGoldenTests passes on Linux and Windows: 52 stylesheets plus the three bad-scale ... |
| AC-EXP-2 | WP-D14 | The five ported suites of 8.1 (ExportCssTests, ExportGeometryTests, ExportPaletteSourceTests ... |
| AC-EXP-3 | WP-D10 | ExportGoldenTests: for every fixture, native html, html-plain and Markdown text are ... |
| AC-EXP-4 | WP-D11 | On the Windows x64 reference PC, the styled HTML of the 13-step reference SOP embeds ... |
| AC-EXP-5 | WP-D11 | With shotai_avif.dll removed, the styled HTML export succeeds with image/jpeg images and ... |
| AC-EXP-6 | WP-D10 | HTML for Word embeds PNG at 1x with size attributes. Manual: pasting it into Word 365 lays ... |
| AC-EXP-7 | WP-D12 | The PDF of fixture 1 has Letter pages, step card backgrounds visible, and the 2924 px capture ... |
| AC-EXP-8 | WP-D12 | A PDF render that produces nothing shows Export failed: PDF rendering produced an empty ... |
| AC-EXP-9 | WP-D12 | On a VM with the WebView2 runtime uninstalled, PDF export shows the PdfRuntimeMissing message ... |
| AC-EXP-10 | WP-D12 | PdfRendererTests hardening cases pass: no script runs, no request reaches the local listener ... |
| AC-EXP-11 | WP-D10 | Markdown to the project folder twice gives <title>\<title>.md and <title> (1)\<title> (1).md ... |
| AC-EXP-12 | WP-D10 | Markdown Save As naming an existing folder that holds other files creates <name> (1)\ and ... |
| AC-EXP-13 | WP-D13 | The .docx of every fixture passes OpenXmlValidator(Office2019) with no errors, has w:pgSz ... |
| AC-EXP-14 | WP-D14 | The .pptx of every fixture validates with no errors, has items + 1 slides of 12192000 by ... |
| AC-EXP-15 | WP-D17 | A project with an unknown tip callout exports in all six formats with the step numbered as a ... |
| AC-EXP-16 | WP-D17 | A project with an unbaked redaction on step 3 of 5 fails every format with 04's refusal ... |
| AC-EXP-17 | WP-D10 | Deleting a flattened render from disk makes the export fail with Step {n}'s screenshot render ... |
| AC-EXP-18 | WP-D15 | A project whose only steps are empty text steps fails every document format with This project ... |
| AC-EXP-19 | WP-D9 | ExportNamesTests passes; an export of a project titled CON is named _CON.html, titled a/b:c? ... |
| AC-EXP-20 | WP-D16 | Bulk export of two projects titled Onboarding to one folder writes Onboarding.html and ... |
| AC-EXP-21 | WP-D10 | ExportServiceTests confirms the dialog titles Export and Export Markdown (saved as a folder ... |
| AC-EXP-22 | WP-D10 | A styled HTML export of a 3-shot project reports {0,3}, {1,3}, {2,3}, {3,3} in order; a bulk ... |
| AC-EXP-23 | WP-D17 | With includeNameInReports on and userName Jane , every format except HTML for Word shows ... |
| AC-EXP-24 | WP-D9 | With culture en-US and local time 2026-09-22 14:03:07, the created line is exactly Created on ... |
| AC-EXP-25 | WP-D10 | The brand precedence table of 2.3 holds for the styled HTML's accent color in each row. |
| AC-EXP-26 | WP-D15 | A safe package of fixture 1 is <title> (shotAI package).zip in export\, is revealed, contains ... |
| AC-EXP-27 | WP-D15 | A package with originals contains exactly the files the manifest references (screenshots and ... |
| AC-EXP-28 | WP-D15 | A package exported natively imports natively with identical steps and image bytes; a native ... |
| AC-EXP-29 | WP-D15 | PackageReaderTests produces each of the eleven Electron import messages from crafted zips ... |
| AC-EXP-30 | WP-D15 | A 2 MB zip whose single shots/a.png entry declares (truthfully) and inflates to 2 GB is ... |
| AC-EXP-31 | WP-D15 | A zip whose entries are shots/../shots/a.png, ../../b.png, ./shots/c.png, /shots/d.png and ... |
| AC-EXP-32 | WP-D10 | Typing a caption and immediately exporting (before the caption's write completes) exports the ... |
| AC-EXP-33 | WP-D12 | After a successful and after a failed PDF export, export\.render\ holds no _print-* file; an ... |
| AC-EXP-34 | WP-D17 | Export/EgressRedactionTests and 04's end-to-end redaction test pass for all six formats and ... |
| AC-EXP-35 | WP-D10 | Leaving the project during a styled HTML export of the reference SOP leaves no new file in ... |
| AC-EXP-36 | WP-D13 | Exporting over a .docx that is open in Word shows Export failed: Couldn't write <name>.docx ... |
| AC-EXP-37 | WP-D15 | File then Import Project shows Import a shotAI project package with the shotAI package filter ... |
| AC-EXP-38 | WP-D11 | The styled HTML export of the 13-step reference SOP completes in at most 11.4 s on the x64 ... |
| AC-EXP-39 | WP-D12 | The PDF print copy written to export\.render\ contains the D-EXP-25 CSP meta and (for LFI) the ... |
| AC-EXP-40 | WP-D15 | A package whose project.json is [] fails with Import failed: The package project.json is ... |
| AC-EXP-41 | WP-D14 | For a callout with a two-line body, the native slide's PptxDump equals Electron's paragraph ... |
| AC-EXP-42 | WP-D12 | Manual: with a network trace running, a PDF export (first with no WebView2 user data folder, then with it) makes no outbound connection ... (Q-EXP-27) |

### 10 Brand, settings, infra (INFRA)

| AC | WP | Criterion (abridged; the spec text is normative) |
|---|---|---|
| AC-INFRA-1 | WP-A4 | dotnet run --project tools/ShotAI.GenBrand -- --check (from dotnet/) prints gen-brand: up to ... |
| AC-INFRA-2 | WP-A4 | Changing one colour in contract/brand.json without regenerating makes the native CI job fail ... |
| AC-INFRA-3 | WP-A4 | The stamp in BrandPalette.Generated.cs equals the stamp in ... |
| AC-INFRA-4 | WP-A4 | BrandParityWithElectronTests.ValuesEqualTypeScriptTable passes: every value of the C# table ... |
| AC-INFRA-5 | WP-A4 | Setting shotAI's dark field to its surface2 value makes the generator exit 1 with gen-brand ... |
| AC-INFRA-6 | WP-A4 | BrandPaletteTests and BrandNarrowingTests pass on Linux, including AA contrast for all four ... |
| AC-INFRA-7 | WP-B10 | A native first launch on a machine that already has an Electron settings.json shows the same ... |
| AC-INFRA-8 | WP-B10 | Manual: add "somethingFuture": {"a": [1, 2]} and "captureNoHide": true to settings.json ... |
| AC-INFRA-9 | WP-B10 | Manual: set "archiveAgeDays": 5000, "captureScale": 2, "theme": "Dark", "recents": ["C:\\a" ... |
| AC-INFRA-10 | WP-B10 | Manual: replace settings.json with not json {{{, launch: the app starts with defaults, the log ... |
| AC-INFRA-11 | WP-B10 | Manual: save settings.json as "UTF-8 with BOM" in Notepad after editing userName; the native ... |
| AC-INFRA-12 | WP-B10 | SettingsServiceTests.RollbackOnlyTheFailedChange passes, and manually: with settings.json made ... |
| AC-INFRA-13 | WP-B10 | SettingsServiceTests.CachesReadCurrentSynchronously passes, and toggling remote visibility in ... |
| AC-INFRA-14 | WP-A12 | The log file is %APPDATA%\shotAI\logs\shotai.log; its first line matches ^\[\d{4}-\d{2}-\d{2} ... |
| AC-INFRA-15 | WP-A11 | RotatingFileSinkTests pass; manually, writing 12 MB of debug logs leaves exactly shotai.log ... |
| AC-INFRA-16 | WP-E6 | LogPrivacyTests.CanaryNeverLogged passes; a code review of every Log* call finds no value from ... |
| AC-INFRA-17 | WP-E1 | UpdateCheckTests (all 25 ported cases plus the new ones) and UpdateServiceTests pass on Linux. |
| AC-INFRA-18 | WP-E1 | Manual, on a build whose version is set below the latest release: first launch shows shotAI ... |
| AC-INFRA-19 | WP-E1 | Manual: turn "Check for updates" off, restart: no request is made at startup (network trace) ... |
| AC-INFRA-20 | WP-E1 | Manual: offline, ↻ Check now shows Couldn't check: <message>; with a local proxy that never ... |
| AC-INFRA-21 | WP-E1 | Manual: behind an authenticating corporate proxy (system proxy settings only, no environment ... |
| AC-INFRA-22 | WP-E1 | Manual: a pilot build versioned 2.0.0-alpha.3 checking a feed whose latest is 2.0.0 shows ... |
| AC-INFRA-23 | WP-E1 | ExternalLinkPolicyTests pass; manually, Open the download page opens the GitHub release page ... |
| AC-INFRA-24 | WP-E1 | A network trace of a native launch with default settings and no user action shows requests ... |
| AC-INFRA-25 | WP-A12 | Start-Process .\shotAI.exe -ArgumentList '--selftest' -Wait -PassThru -RedirectStandardOutput ... |
| AC-INFRA-26 | WP-B9 | --capture-selftest on a Windows desktop prints [capture-test] ... lines and exits 0 on PASS, 1 ... |
| AC-INFRA-27 | WP-A12 | Running --selftest leaves %APPDATA%\shotAI\settings.json byte-identical (compare hashes before ... |
| AC-INFRA-28 | WP-E1 | --update-selftest on a corporate network prints the endpoint, the result, and [update-test] ... |
| AC-INFRA-29 | WP-E3 | The installed app contains Fonts\Archivo.ttf (sha256 0e094a7d…) and Fonts\OFL.txt ... |
| AC-INFRA-30 | WP-A14 | Manual: with the LFI brand, body text renders at regular weight and uppercase micro-labels ... |
| AC-INFRA-31 | WP-E3 | Manual: from the installed package (the MSI, in each install scope, 12 INV-PKG-1 and 7.4.5), changing ... |
| AC-INFRA-32 | WP-B10 | Manual: while the app runs, open settings.json in a program that holds it with no sharing (for ... |
| AC-INFRA-33 | WP-A4 | BrandPaletteTests.AllIsFullyInitialized and HexNoHashRejectsTrailingNewline pass, and dotnet ... |
| AC-INFRA-34 | WP-E1 | With a Settings Check now clicked while the startup check is still in flight (a slow proxy) ... |
| AC-INFRA-35 | WP-A12 | Shell.AppPathsTests.LocalDataDirectoryIsUnderLfi and NoPathUnderSquirrelRoot pass: IAppPaths.LocalDataDirectory is %LOCALAPPDATA%\LFI\shotAI ... |

### 11 Service boundary (IPC)

| AC | WP | Criterion (abridged; the spec text is normative) |
|---|---|---|
| AC-IPC-1 | WP-A1 | ChannelInventoryTests passes: channel-map.json lists 87 unique channels (74 invoke, 3 send, 10 ... |
| AC-IPC-2 | WP-E6 | ChannelMapResolutionTests passes on Windows: every row's member resolves. |
| AC-IPC-3 | WP-D4 | AuthSurfaceTests passes: AuthStatus has exactly the seven members of INV-IPC-1, and no member ... |
| AC-IPC-4 | WP-E1 | ExternalLinkPolicyTests passes with the full table of 8.2; manual: in the running app, the ... |
| AC-IPC-5 | WP-B9 | EventOrderTests and WpfUiDispatcherTests pass: two subscribers see capture events in raise ... |
| AC-IPC-6 | WP-A12 | The solution builds with VSTHRD002, VSTHRD100, VSTHRD101, VSTHRD110, CA2007 (Core, Platform) ... |
| AC-IPC-7 | WP-B9 | SubscribeThenReadTests passes, including OlderPayloadProcessedLateDoesNotWin; manual: start a ... |
| AC-IPC-8 | WP-D12 | StaleProgressTests passes; manual: start a PDF export of a 20-step project and press Back ... |
| AC-IPC-9 | WP-C4 | UserMessageTests passes; manual: import a 0-byte file with Insert Image and the notice reads ... |
| AC-IPC-10 | WP-C4 | ImportLimitsTests passes; manual: Insert Image with a 70 MB PNG shows Import failed: Image too ... |
| AC-IPC-11 | WP-D7 | StatusRereadsPolicyTests passes; manual on a managed machine: remove the ... |
| AC-IPC-12 | WP-B11 | RemoteVisibilityApplierTests passes; manual: during a Teams screen share, turn on Show shotAI ... |
| AC-IPC-13 | WP-A18 | BrandChoicePassThroughTests passes; manual: with the app brand LFI, open a project, choose ... |
| AC-IPC-14 | WP-A18 | Manual: open a project whose project.json has "theme": "future-brand"; View, Brand ticks ... |
| AC-IPC-15 | WP-D17 | BoundaryLogTests passes; manual: after a session that sets an API key, signs in and exports ... |
| AC-IPC-16 | WP-A1 | CoreReferencesTests passes on Linux. |
| AC-IPC-17 | WP-A12 | ContainerTests, ViewModelDependencyTests, CommandConventionsTests and SubscriberDisposalTests ... |
| AC-IPC-18 | WP-A19 | ExitFlushTests passes; manual: rename a project and within 100 ms choose File, Exit; after ... |
| AC-IPC-19 | WP-D12 | SingleWebViewTests and SingleUrlLauncherTests pass. |
| AC-IPC-20 | WP-A19 | Manual: Home, row menu, Reveal in Explorer on a project whose folder is on a disconnected ... |
| AC-IPC-21 | WP-B9 | Manual: area selection from the target chooser: the main window hides, the overlay appears on ... |
| AC-IPC-22 | WP-E6 | Manual parity walk: every row of 7.4 whose Native caller is a UI surface is exercised once in ... |
| AC-IPC-23 | WP-D6 | ScreenshotTargetTests passes, and FireAndForgetTests passes. |
| AC-IPC-24 | WP-D10 | ExportReadsBrandAtCallTimeTests passes; manual: switch the app brand in Settings and export ... |
| AC-IPC-25 | WP-E1 | UpdateCheckNowTests and the ScreenshotRaisesOneIdleStateAndNoStep case pass; manual: dismiss ... |

### 12 Packaging, CI, release (PKG)

| AC | WP | Criterion (abridged; the spec text is normative) |
|---|---|---|
| AC-PKG-1 | WP-E4 | PackageSourceTests, ReleaseVersionTests, MsiVersionTests, TagCheckTests, PayloadVerifierTests ... |
| AC-PKG-2 | WP-E3 | Manual: after installing the MSI offline on a clean Windows 11 x64 VM, Settings, Apps ... |
| AC-PKG-3 | WP-E3 | The package job passes on x64 and ARM64: install exit 0, --selftest exit 0 with last line ... |
| AC-PKG-4 | WP-E2 | verify-payload --public passes for both payloads, and fails (non-zero, one release: line each) ... |
| AC-PKG-5 | WP-E4 | On a release build, Get-AuthenticodeSignature reports Valid with a non-null ... |
| AC-PKG-6 | WP-E3 | icacls "C:\Program Files\shotAI" shows no write, modify or full-control entry for ... |
| AC-PKG-7 | WP-E3 | Upgrade: installing 2.0.0-beta.1 then 2.0.0 leaves one ARP entry with DisplayVersion 2.0.999 ... |
| AC-PKG-8 | WP-E3 | Downgrade: installing 2.0.0-beta.1 over 2.0.0 fails with exit code 1603 and the log contains A ... |
| AC-PKG-9 | WP-E3 | After uninstall, %APPDATA%\shotAI\settings.json, %APPDATA%\shotAI\logs\, the projects folder ... |
| AC-PKG-10 | WP-E3 | Manual: on a VM without the .NET 10 Desktop Runtime, an Intune Required assignment installs ... |
| AC-PKG-11 | WP-E6 | Manual: with the WebView2 runtime uninstalled, the app launches, all exports except PDF ... |
| AC-PKG-12 | WP-E3 | Manual: on a Windows 10 1909 (build 18363) VM the MSI refuses with shotAI requires Windows 10 ... |
| AC-PKG-13 | WP-E7 | For the first native prerelease: the GitHub release shows Pre-release and not Latest; gh ... |
| AC-PKG-14 | WP-E8 | For 2.0.0: the release is Latest; an Electron 1.3.0 client shows the update notice for 2.0.0 ... |
| AC-PKG-15 | WP-E4 | release.yml refuses (job check fails) a tag v2.0.0 when Directory.Build.props says 2.0.0-rc.1 ... |
| AC-PKG-16 | WP-E4 | The public release assets contain no baked federation (verify-payload --public on the ... |
| AC-PKG-17 | WP-E4 | shotai_avif.dll in each payload: dumpbin /exports lists exactly shotai_avif_encode ... |
| AC-PKG-18 | WP-E3 | With DOTNET_ROOT and DOTNET_ROOT_X64/DOTNET_ROOT_ARM64 set to an empty folder and ... |
| AC-PKG-19 | WP-E3 | Process Monitor on first launch from a working directory containing a DLL named ... |
| AC-PKG-20 | WP-E5 | Manual: with Electron 1.3.0 running, launching native shows the exact legacy notice and exits ... |
| AC-PKG-21 | WP-E5 | Manual: during the pilot, a setting changed in native (theme) is visible in Electron after ... |
| AC-PKG-22 | WP-E5 | Manual: after native writes settings.json with native-only keys, Electron 1.3.0 starts, keeps ... |
| AC-PKG-23 | WP-E5 | Manual: a project edited in native opens in Electron 1.3.0 with the same steps, captions and ... |
| AC-PKG-24 | WP-E5 | Manual, before the pilot: on a PC with Electron 1.3.0 and native installed and signed in ... |
| AC-PKG-25 | WP-E7 | Intune detection: after install the Win32 app reports Installed (MSI product code and version ... |
| AC-PKG-26 | WP-E3 | The ARM64 MSI on a Windows 11 ARM64 device runs natively (Task Manager architecture column ... |
| AC-PKG-27 | WP-E9 | 90 days after GA the v1.3.* release assets are still downloadable and the Electron Intune app ... |
| AC-PKG-28 | WP-E2 | dotnet build ShotAI.slnx -c Release succeeds on Linux with the tools projects added and the ... |
| AC-PKG-29 | WP-E7 | Pilot exit criteria (manual, recorded in the pilot issue): at least two weeks of daily use by ... |
| AC-PKG-30 | WP-E4 | THIRD-PARTY-NOTICES.txt is present in C:\Program Files\shotAI and attached to the release, and ... |
| AC-PKG-31 | WP-E3 | On real x64 and ARM64 hardware with a GPU, the installed app (with DllSearchHardening.Apply() ... |
| AC-PKG-32 | WP-E3 | Installing the public MSI and then the internal MSI of the same version (and the reverse) ... |
| AC-PKG-33 | WP-E4 | PayloadVerifier on the first package job run lists every NotSigned PE; with the chosen ... |
| AC-PKG-34 | WP-E5 | With Electron 1.3.0 running, shotAI.exe --selftest exits 2 within 2 s with the EDGE-PKG-50 ... |
| AC-PKG-35 | WP-E3 | The package job on x64 and ARM64: the MSI build's ICE validation shows ICE105 ran with no error ... |
| AC-PKG-36 | WP-E3 | Manual: on a clean Windows 11 x64 VM with the .NET 10 Desktop Runtime installed, a standard user ... |
| AC-PKG-37 | WP-E3 | Manual: on a clean Windows 11 x64 VM without the .NET 10 Desktop Runtime, a standard user's ... |
| AC-PKG-38 | WP-E5 | Manual: as a standard user, install a personal copy; then, as an administrator, install the same ... |

### ARCHITECTURE.md (ARCH)

| AC | WP | Criterion (abridged; the spec text is normative) |
|---|---|---|
| AC-ARCH-1 | WP-A1 | ShotAI.Core.Tests builds and passes on the Linux job, and Architecture.CoreReferencesTests ... |
| AC-ARCH-2 | WP-A12 | Composition.ContainerTests builds the full container with ValidateOnBuild and resolves every ... |
| AC-ARCH-3 | WP-A1 | Each banned-symbol list and each analyzer rule of 14.9 is proven once, in the PR that adds it ... |
| AC-ARCH-4 | WP-A9 | Store/ProjectSessionTests (Core, Linux) covers S1 to S10 of 7.4: clone-throw queues nothing ... |
| AC-ARCH-5 | WP-A19 | Shutdown.ExitFlushTests passes, and a manual rename followed within 100 ms by File, Exit shows ... |
| AC-ARCH-6 | WP-D2 | An environment test starts the composition root with ANTHROPIC_CUSTOM_HEADERS and (if Q-ARCH-3 ... |
| AC-ARCH-7 | WP-E5 | Manual: with the native build signed in and a PDF exported, uninstalling the Electron build ... |
| AC-ARCH-8 | WP-E6 | Every budget of section 11 has a recorded measurement on both reference machines at the phase ... |


---

## 8. Test port matrix

All 49 Electron test files under `src/`. "Ports" means every TypeScript case gets a named C# test with the same inputs and expectations (DoD4). ELECTRON-ONLY files name the native tests that carry their intent.

| # | Electron test file | Owner spec | WP | Disposition and native target |
|---|---|---|---|---|
| 1 | `src/main/archive.test.ts` | 01 8.1 | WP-A8 | ports: `Store/ArchiveTests` (Linux) |
| 2 | `src/main/arp-icon.test.ts` | 03 8.2 | WP-E3 | ELECTRON-ONLY (Squirrel `--squirrel-*` events and the HKCU `DisplayIcon` write do not exist natively); intent: the MSI sets `ARPPRODUCTICON`, checked by `Packaging/PackageSourceTests.ShortcutAndArp` and AC-SHELL-30 |
| 3 | `src/main/capture-geometry.test.ts` | 02 8.1 | WP-B1 | ports: `Capture/CaptureGeometryTests`, `AutoClassifierTests` |
| 4 | `src/main/capture-shield.test.ts` | 02 8.2 | WP-B1 | ports: `Capture/CaptureShieldTests`; the source-routing cases become `CaptureFunnelSourceTests` |
| 5 | `src/main/claude-error-messages.test.ts` | 07 8.4 | WP-D6 | ports: `Sop/SopErrorMessageTests` (the empty-plan string predicate is ELECTRON-ONLY, replaced by `SopPlanParserTests` in WP-D5) |
| 6 | `src/main/click-caption.test.ts` | 02 8.3 | WP-B1 | ports: `Capture/ClickCaptionsTests` |
| 7 | `src/main/conformance.test.ts` | 01 8.1, 7.11 | WP-A3 | ports: `Conformance/ConformanceTests` (the scaffold's harness, unskipped), `ExpectPathTests` |
| 8 | `src/main/entra/admx-contract.test.ts` | 08 8.1 | WP-D1 | ports: `Auth/AdmxContractTests` |
| 9 | `src/main/entra/auth-core.test.ts` | 08 8.2 | WP-D4 | ports: `Auth/AuthServiceModeSelectionTests`, `VerifyFederationTests` |
| 10 | `src/main/entra/cache-plugin.test.ts` | 08 8.3 | WP-D3 | splits: Windows `Auth/MsalCachePersistenceTests`, Core `Auth/TokenCachePersistenceTests`, `SourceScanTests.NoUnprotectedMsalCache`; five cases ELECTRON-ONLY (the `safeStorage` object shape, re-encryption on key rotation, `clear()`) because DPAPI and MSAL own them |
| 11 | `src/main/entra/config-sources.test.ts` | 08 8.4 | WP-D1 | ports: `PolicyValueParserTests`, `FederationSourcesTests`, `FederationConfigProviderTests`, Platform `RegistryPolicySourceTests`; the reg.exe line-format cases are ELECTRON-ONLY (no text output natively) |
| 12 | `src/main/entra/config-validate.test.ts` | 08 8.5 | WP-D1 | ports: `Auth/FederationConfigValidatorTests` |
| 13 | `src/main/entra/federation-cache-wiring.test.ts` | 08 8.6, 11 8.1 | WP-D4 | the source-text cases become the behavior tests `AuthStatusServiceTests.GetStatusInvalidatesFederationCache`, `OnlyStatusInvalidates` and `ServiceBoundary/StatusRereadsPolicyTests`; the doc case ports as `AdminDocConsistencyTests`; the module-export case is ELECTRON-ONLY |
| 14 | `src/main/entra/federation-local.test.ts` | 08 8.7 | WP-D1 | ports: `Auth/FederationLocalFileTests` (skipped without a local file, as in Electron) |
| 15 | `src/main/entra/federation.test.ts` | 08 8.8 | WP-D4 | ports: `Auth/FederationExchangeTests`, `JwtRolesTests` |
| 16 | `src/main/entra/net-module.test.ts` | 08 8.9 | WP-D4 | ELECTRON-ONLY, all 11 cases (msal-node's `INetworkModule` over `net.fetch`; MSAL.NET and WAM own their HTTP); intent: `SharedHttpTests` (WP-D2), `MsalGatewayTests.UsesSharedHttpClientFactory` (WP-D3), AC-AUTH-29 |
| 17 | `src/main/export-css.test.ts` | 09 8.1 | WP-D9 | ports: `Export/ExportCssTests` (with the corrected print regex, EDGE-EXP-46) |
| 18 | `src/main/export-geometry.test.ts` | 09 8.1 | WP-D9 | ports: `Export/ExportGeometryTests` |
| 19 | `src/main/export-palette-source.test.ts` | 09 8.1 | WP-D14 | ports as C# source scans: `Export/ExportPaletteSourceTests` over `ExportCss.cs`, `DocxBuilder.cs`, `PptxBuilder.cs`, `DocxDefaultStyles.cs` |
| 20 | `src/main/gpu-policy.test.ts` | 03 8.1 | WP-A12 | partly ports: `Shell/RuntimeDiagnosticsTests`, `Shell/RenderModePolicyTests`; the GPU decision itself, the non-Windows and case-insensitivity cases and the reason strings are ELECTRON-ONLY (WPF has no GPU process to disable) |
| 21 | `src/main/intro-authored.test.ts` | 07 8.5 | WP-D5 | ports: `Sop/IntroAuthoredTests` (real store in a temp folder) |
| 22 | `src/main/manifest-extras.test.ts` | 01 8.1 | WP-A6 | ports: codec cases `Codec/ManifestExtrasTests` (in WP-A3), store cases `Store/ManifestExtrasStoreTests` |
| 23 | `src/main/mutate-serialize.test.ts` | 01 8.1 | WP-A6 | ports: `Store/MutateSerializeTests` |
| 24 | `src/main/normalize-steps.test.ts` | 01 8.1 | WP-A3 | ports: `Codec/NormalizeStepsTests` |
| 25 | `src/main/path-confine-symlink.test.ts` | 01 8.1 | WP-A5 | ports: `Store/PathConfineSymlinkTests` (Linux symlinks), `Platform.Tests/FileSystem/JunctionConfineTests` (Windows junctions) |
| 26 | `src/main/path-confine.test.ts` | 01 8.1 | WP-A5 | ports: `Store/PathConfineTests` plus the Windows-only rows |
| 27 | `src/main/project-theme-key.test.ts` | 01 8.1 | WP-A6 | ports: `Store/ProjectThemeKeyTests` |
| 28 | `src/main/render-gate.test.ts` | 04 8.1 | WP-C7 | ports: `Redaction/RenderGateTests` |
| 29 | `src/main/section-callout.test.ts` | 01 8.1 | WP-A3 | ports: `Codec/SectionCalloutTests` |
| 30 | `src/main/settings.test.ts` | 10 8.3 | WP-A10 | ports: `Settings/SettingsStoreTests` |
| 31 | `src/main/sop-input.test.ts` | 07 8.1 | WP-D5 | ports: `Sop/SopInputTests` |
| 32 | `src/main/sop-landing.test.ts` | 07 8.3 | WP-D5 | ports: `Sop/SopLandingTests`, `SopMessagesTests`; the `schema on the wire` case lands in WP-D6 as `SopEditSchemaTests.MinItemsReachesTheWire` |
| 33 | `src/main/sop-prompt.test.ts` | 07 8.2 | WP-D5 | ports: `Sop/SopPromptTests` (the title detector cases over `ProjectTitles.IsAutoGenerated`) |
| 34 | `src/main/step-render.test.ts` | 04 8.1 | WP-C5 | ports: `Rendering/StepPatchApplierTests` |
| 35 | `src/main/unknown-callout.test.ts` | 01 8.1 | WP-A3 | ports: `Model/CalloutKindTests`, `Model/StepNumberingTests` |
| 36 | `src/main/update-check.test.ts` | 10 8.4 | WP-E1 | ports: `Updates/UpdateCheckTests` |
| 37 | `src/renderer/editor/editor-geometry.test.ts` | 04 8.1 | WP-C8 | ports: `Editor/EditorGeometryTests` |
| 38 | `src/renderer/project/app-chrome-tokens.test.ts` | 06 8.2 | WP-A14 | ELECTRON-ONLY as written (it scans CSS); intent ports as `SourceGuards/XamlChromeGuardTests` (Linux) and the key-set comparison in App `Chrome/ThemeResourcesTests` |
| 39 | `src/renderer/project/command-bar.test.ts` | 05 8.1 | WP-D10 | ELECTRON-ONLY as written (it scans TSX and CSS); each intent ports: `Report/PopoverPlacementTests.ExportMenuFlip` (Linux) and App `Report/CommandBarTests` |
| 40 | `src/renderer/project/date-groups.test.ts` | 06 8.1 | WP-A16 | ports: `Home/DateGroupsTests` |
| 41 | `src/renderer/project/report-geometry.test.ts` | 05 8.1 | WP-A17 | ports: `Geometry/ReportGeometryTests` |
| 42 | `src/renderer/project/theme-wiring.test.ts` | 06 8.3, 11 8.1 | WP-A18 | partly ports: `Shell/BrandMenuModelTests`, App `NavigationStateTests`, `BrandChoicePassThroughTests`, `AppMenuViewModelTests` (and the theme-manager rows as `Chrome/ThemeManagerTests` in WP-A14); the push, arm-before-build, coercion and menu-rebuild cases are ELECTRON-ONLY (a WPF menu binds to its view model and is never rebuilt) |
| 43 | `src/shared/brand-narrowing.test.ts` | 10 8.2 (also 09) | WP-A4 | ports: `Brand/BrandNarrowingTests` with the full decision table |
| 44 | `src/shared/doc-scale.test.ts` | 05 8.1 | WP-A17 | ports: `Geometry/DocScaleTests` |
| 45 | `src/shared/export-theme.test.ts` | 09 8.1 | WP-D9 | ports: `Export/ExportThemeTests`; the print-copy case becomes `PrintHtmlTests.FaceOnlyInThePrintCopy` (WP-D12); the two Forge `extraResource` cases are ELECTRON-ONLY, intent in `Packaging/BrandFontShipsTests` (WP-E2); the IPC fallback case becomes `ExportReadsBrandAtCallTimeTests` (WP-D10) |
| 46 | `src/shared/project.test.ts` | 01 8.1 | WP-A3 | ports: `Model/StepGeometryTests`, `Model/CalloutKindTests` |
| 47 | `src/shared/redact-detect.test.ts` | 04 8.1 | WP-C11 | ports: `Redaction/SensitiveTextDetectorTests` |
| 48 | `src/shared/report-matches-export.test.ts` | 05 8.1 | WP-A17 | ports: `Geometry/ReportMatchesExportTests` (plus the native figure clause) |
| 49 | `src/shared/theme-palette.test.ts` | 10 8.1 | WP-A4 | Core cases port to `Brand/BrandPaletteTests`; the generated-stylesheet cases are ELECTRON-ONLY, intent in `SourceGuards/XamlResourceKeyGuardTests` and `ThemeManager.ApplyInitial` (WP-A14); the font packaging and rendering cases move to App `Fonts/FontPackagingTests` and `Fonts/ArchivoRenderingTests` (WP-A14) |

No Electron test exists for the IPC boundary itself (11 8.1) or for packaging (12 8.1); their native tests are new (WP-A1, WP-A12, WP-E2 to WP-E4). The Electron suite keeps running in `ci.yml` until the WP-E9 cleanup PR deletes it.

---

## 9. Progress checklist

Tick a box when the WP meets its definition of done (1.3), with the PR number. A WP that is merged but waiting only on manual ACs is written `merged in #NN, manual pending: AC-...` and counts as merged for dependencies (2.2).

**Prerequisite**

- [ ] P0. The PR of the branch `claude/native-windows-rewrite-feasibility-8tpy3t` (the scaffold, the architecture, the twelve specs and this plan) merged to `main` (Q-PLAN-3)

**Phase A: model and viewer**

- [ ] WP-A1. Guardrails and Core foundations
- [ ] WP-A2. ECMAScript JSON semantics
- [ ] WP-A3. Model, codec and conformance
- [ ] WP-A4. Brand generator and palette
- [ ] WP-A5. Atomic file, write queue and path confinement
- [ ] WP-A6. Project store: projects
- [ ] WP-A7. Project store: steps and imports
- [ ] WP-A8. Archive engine
- [ ] WP-A9. Project session
- [ ] WP-A10. Settings service
- [ ] WP-A11. Logging
- [ ] WP-A12. App host and composition root
- [ ] WP-A13. Window base, popup exclusion and single instance
- [ ] WP-A14. Theme resources and fonts
- [ ] WP-A15. Main window, app menu and About
- [ ] WP-A16. Home list
- [ ] WP-A17. Read-only report
- [ ] WP-A18. Brand submenu and navigation state
- [ ] WP-A19. Home row operations
- [ ] WP-A20. Phase A exit (M-A)

**Phase B: capture engine**

- [ ] WP-B1. Capture Core: rules, captions and the shield
- [ ] WP-B2. Capture engine: sessions and the capture pipeline
- [ ] WP-B3. Capture engine: click decisions and menus
- [ ] WP-B4. Input hook and hotkey
- [ ] WP-B5. Screen capture, display affinity and the protection probe
- [ ] WP-B6. Window information and UI Automation
- [ ] WP-B7. Capture pill and recording visibility
- [ ] WP-B8. Area-select overlay
- [ ] WP-B9. Recording from Home and the project view
- [ ] WP-B10. Settings view (non-AI groups) and the onboarding tour
- [ ] WP-B11. Phase B exit (M-B)

**Phase C: editor and redaction**

- [ ] WP-C1. Report operations (Core)
- [ ] WP-C2. Report editing UI
- [ ] WP-C3. Report figure, drag reorder, size control and text inserts
- [ ] WP-C4. Capture and image insert
- [ ] WP-C5. Step patch, render writer and durable store updates
- [ ] WP-C8. Editor document (Core) (before WP-C6 and WP-C7, which depend on it; numbering kept for ID stability)
- [ ] WP-C6. Flatten and redaction bake
- [ ] WP-C7. Render gate and pre-egress flatten
- [ ] WP-C9. Editor canvas and interaction
- [ ] WP-C10. Editor save and overlay rasterizer
- [ ] WP-C11. Sensitive-text detection, OCR and auto-redact
- [ ] WP-C12. Merge
- [ ] WP-C13. Phase C exit (M-C)

**Phase D: SOP generation, auth and exports**

- [ ] WP-D1. Federation config and managed policy
- [ ] WP-D2. Shared HTTP, client factory, API key store and environment sanitization
- [ ] WP-D3. Entra sign-in and the token cache (Platform)
- [ ] WP-D4. Federation exchange, connection test and the auth facade
- [ ] WP-D5. SOP request, schema and apply
- [ ] WP-D6. SOP transport and service
- [ ] WP-D7. Settings AI tab
- [ ] WP-D8. SOP panel
- [ ] WP-D9. Export foundations and golden fixtures
- [ ] WP-D10. HTML, HTML for Word and Markdown exports
- [ ] WP-D11. AVIF encoder
- [ ] WP-D12. PDF export
- [ ] WP-D13. Word export
- [ ] WP-D14. PowerPoint export
- [ ] WP-D15. Shareable package
- [ ] WP-D16. Home row and bulk export
- [ ] WP-D17. Phase D exit (M-D)

**Phase E: ship**

- [ ] WP-E1. Update check and the update notice
- [ ] WP-E2. Release tool and payload verification
- [ ] WP-E3. MSI, install smoke and the package job
- [ ] WP-E4. Signing and the release workflow
- [ ] WP-E5. Coexistence guard and data paths
- [ ] WP-E6. Release readiness audit
- [ ] WP-E7. Pilot (stages S1 and S2)
- [ ] WP-E8. 2.0.0 and cutover (stage S3)
- [ ] WP-E9. Cleanup and legacy guard removal (stages S4 and S5)
