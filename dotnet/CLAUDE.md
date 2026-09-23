# Working in dotnet/ (the native shotAI rewrite)

This folder is the native C# / WPF rewrite of the Electron app in `../src`. The work is
planned in detail; follow the plan instead of improvising scope.

1. **Read [docs/native/PLAN.md](../docs/native/PLAN.md) section 2 before writing code.**
   It gives the read order, how to pick and claim the next work package (WP), the
   branch and PR rules, and what else to update in the same PR.
2. **One work package per PR**, on a short-lived branch named `native/wp-<id>-<slug>`.
   Tick the WP in PLAN section 9 in that PR.
3. **The specs in [docs/native/spec/](../docs/native/spec/) are normative.** The Electron
   source is the reference behind them. When the code and a spec disagree about a
   REQUIRED behavior, the code wins and you correct the spec in the same PR, with the
   reason. [ARCHITECTURE.md](../docs/native/ARCHITECTURE.md) section 15.3 wins over a
   spec that contradicts it.
4. **Commands run from this folder** (see PLAN 2.5). `global.json` here opts `dotnet test`
   into Microsoft.Testing.Platform, and `dotnet` only finds it from here or below:
   - `dotnet build ShotAI.slnx -c Release`
   - `dotnet test --project tests/ShotAI.Core.Tests/ShotAI.Core.Tests.csproj -c Release`
5. **Cloud sessions run on Linux.** They can build everything and run the Core tests,
   but not the app or the Windows-only tests. Put every rule that can live in
   `ShotAI.Core` there, and push to read the Windows CI job for the rest (PLAN 2.6).
6. **Don'ts:**
   - Don't change `contract/` without the same change in the macOS repo (PLAN rule B5).
   - Don't change Electron code except for a parity fix made in both apps (B6).
   - Don't commit ids or anything from a `*.local.json` file. The repo is public (B8).
   - Don't skip a test to get CI green.
7. **Strings:** the docs write an em dash inside a quoted product string as `—`.
   A normal C# string literal turns that into the character, which is what you want.
   A raw (`"""`) or verbatim (`@"..."`) literal does not, and would change the text.
   Prompt and message strings must match the Electron source exactly.
