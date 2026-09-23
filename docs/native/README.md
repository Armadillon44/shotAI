# Native Windows rewrite: plan and spec

These documents take shotAI from the Electron app in `src/` to a native C# / .NET 10 / WPF
app in [`dotnet/`](../../dotnet/), released as 2.0.0. Why this is being done, and why this
stack, is in [NATIVE-WINDOWS-FEASIBILITY.md](../NATIVE-WINDOWS-FEASIBILITY.md).

## Start here

A coding session reads [PLAN.md](PLAN.md) section 2 first. It gives the read order, how to
pick and claim the next work package, the branch and PR rules, the commands, and how a
Linux cloud session handles Windows-only work. [`dotnet/CLAUDE.md`](../../dotnet/CLAUDE.md)
is the short version.

## What's here

| Document | Holds |
|---|---|
| [PLAN.md](PLAN.md) | 70 work packages in five phases (A model and viewer, B capture, C editor and redaction, D SOP and exports, E ship), each with inputs, deliverables, tests, acceptance criteria and dependencies. Also milestones and exit tests, the pilot, cutover and rollback plans, the risk register, the traceability and test-port matrices, and the progress checklist |
| [ARCHITECTURE.md](ARCHITECTURE.md) | What every subsystem plugs into: project layout and dependency rules, packages, composition, MVVM, threading, state and persistence, errors and logging, security, data locations, performance budgets, testing, CI, conventions, and the decisions log. Section 15.3 resolves conflicts between specs and overrides them |
| [spec/01-model-store.md](spec/01-model-store.md) | `project.json` schema and codec, the project store, path confinement, atomic writes, archives |
| [spec/02-capture.md](spec/02-capture.md) | Recording sessions, the mouse hook and hotkey, menu and double-click handling, screen grabs, own-window exclusion, UI Automation, captions |
| [spec/03-windows-shell.md](spec/03-windows-shell.md) | Main window, capture pill, area overlay, app menu, single instance, startup and shutdown |
| [spec/04-editor-redaction.md](spec/04-editor-redaction.md) | Annotation editor, the flatten and redaction bake, render freshness and the render gate, OCR auto-redact |
| [spec/05-report.md](spec/05-report.md) | Project detail and report view, document scale, optimistic edits |
| [spec/06-home-settings-ui.md](spec/06-home-settings-ui.md) | Home list, settings, tour, notices, theme and design tokens |
| [spec/07-sop-generation.md](spec/07-sop-generation.md) | Claude SOP generation: prompt (verbatim), request, estimate, structured output, apply and revert, errors |
| [spec/08-auth-secrets-policy.md](spec/08-auth-secrets-policy.md) | Entra sign-in with WAM, federation token exchange, API key storage, managed policy (ADMX) |
| [spec/09-export.md](spec/09-export.md) | HTML, HTML for Word, PDF, Markdown, Word, PowerPoint, the shareable package |
| [spec/10-brand-settings-infra.md](spec/10-brand-settings-infra.md) | Brand contract and generator, `settings.json`, logging, update check, self-tests |
| [spec/11-service-boundary.md](spec/11-service-boundary.md) | Every Electron IPC channel mapped to the C# service interface that replaces it |
| [spec/12-packaging-deploy-ci.md](spec/12-packaging-deploy-ci.md) | MSI (per-user by hand, per-machine through Intune), signing, prerequisites, x64 and ARM64, Intune deployment, pilot coexistence, CI, releases |

Every spec has the same sections: scope, the Electron reference behavior, constants,
invariants (`INV-`), edge cases and hard-won fixes (`EDGE-`), macOS port notes, the native
design, tests, acceptance criteria (`AC-`), interfaces, and open questions (`Q-`).

## Conventions

- **IDs are stable.** `AC-CAP-12` means the same thing in every document and every PR.
  IDs are never renumbered; superseded items keep their ID with rewritten text.
- **Citations:** Electron code as `path:line`, macOS code as `macOS:path:line`.
- **Every behavior is classified:**
  - **REQUIRED** means parity with the Electron app.
  - **IMPROVEMENT** is a deliberate change, with its reason.
  - **ELECTRON-ONLY** disappears, and the spec says what replaces its intent.
- **`\u2014` inside a quoted string stands for the em dash** in the product string. A normal
  C# string literal turns the escape into the character, which is correct. A raw (`"""`)
  or verbatim (`@"..."`) literal keeps the six characters and silently changes the text.
- **`UNVERIFIED`** marks a claim that couldn't be checked from source or documentation,
  for example behavior only a real Windows machine can show. Such claims have an
  acceptance criterion or open question that settles them.

## How these were produced and checked

1. **Extraction.** Each spec was written by an agent that read its subsystem's Electron
   source end to end, the tests that pin it, the macOS implementation and the relevant
   git history.
2. **Adversarial verification.** A second agent re-read the same sources, assumed the spec
   was wrong, and corrected it in place: between 27 and 52 corrections per spec.
3. **Synthesis and review.** The architecture and the plan were written from the verified
   specs. Two reviews followed: coverage (every README feature, IPC channel, setting,
   menu item, export option, test file and hardening item), and whether a new session
   could follow the plan. Their findings were applied.
4. **Consolidation.** Every spec was edited to agree with the architecture's conflict
   resolutions. Cross-document notes were applied to their targets.
5. **Mechanical checks:**
   - Every acceptance criterion is mapped to a work package.
   - Every work package depends only on earlier ones.
   - All 49 Electron test files have a port target or a reason they don't port.
   - 82 of 86 source constants appear with their exact values (the other four are
     escaping artifacts or browser-only).
   - The SOP system prompt matches the source exactly.
   - No real ids or secrets appear anywhere.

These are specs for implementation, not proof of it. Manual acceptance criteria need a
person on real Windows hardware (PLAN 2.6).

## Decisions that need a person

Each has a recommended default in the doc named, so work can continue, but these belong
to IT or the maintainer rather than a coding session:

| Decision | Where | Needed by |
|---|---|---|
| Add the WAM broker redirect URI to the existing Entra client registration | spec 08 Q-AUTH-3 | before the pilot (WP-D3) |
| Code signing: Azure Artifact Signing eligibility under LFI, or an OV certificate | spec 12 Q-PKG-2 | WP-E4 |
| WiX Toolset licence and fee terms for commercial use | spec 12 Q-PKG-1 | WP-E3 |
| How internal builds with baked federation values reach IT, or policy-only configuration | spec 12 Q-PKG-5 | before the pilot |
| The reference x64 laptop and ARM64 machine for performance budgets | ARCHITECTURE Q-ARCH-1 | milestone M-A |
| Whether PCs outside Intune need a second, per-user MSI that carries its own .NET runtime (Microsoft Update would not patch it) | spec 12 Q-PKG-33 | before 2.0.0 (WP-E8) |
| Whether AppLocker or App Control for Business is enforced on PCs where users would install their own copy | spec 12 Q-PKG-34 | before the pilot (WP-E7) |
| Who runs manual acceptance tests, and where results are recorded | PLAN Q-PLAN-2 | WP-A1 |
