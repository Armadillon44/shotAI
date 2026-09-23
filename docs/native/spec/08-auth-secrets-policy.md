# 08 Entra sign-in, federation, secrets and managed policy

> Spec for the native rewrite. Sources read: `src/main/entra/auth-core.ts` (190 lines), `src/main/entra/msal.ts` (164), `src/main/entra/federation.ts` (238), `src/main/entra/cache-plugin.ts` (123), `src/main/entra/config.ts` (82), `src/main/entra/config-sources.ts` (108), `src/main/entra/config-validate.ts` (218), `src/main/entra/net-module.ts` (97), `src/main/entra/federation.example.json` (26), `src/main/claude-auth.ts` (66), `src/main/secrets.ts` (112), `Intune/Windows/shotAI.admx` (68), `Intune/Windows/en-US/shotAI.adml` (85), `Intune/Windows/README.md` (117), `docs/MANAGED-CONFIG.md` (238), `scripts/wif-probe.mjs` (242). Tests: `src/main/entra/admx-contract.test.ts` (65), `src/main/entra/auth-core.test.ts` (91), `src/main/entra/cache-plugin.test.ts` (216), `src/main/entra/config-sources.test.ts` (207), `src/main/entra/config-validate.test.ts` (191), `src/main/entra/federation-cache-wiring.test.ts` (50), `src/main/entra/federation-local.test.ts` (55), `src/main/entra/federation.test.ts` (225), `src/main/entra/net-module.test.ts` (147). Supporting reads: `src/main/ipc.ts` (`:83`, `:270-310`, `:760-840`), `src/shared/ipc.ts` (`:50-110`, `:225-245`, `:515-555`), `src/main/claude-service.ts` (`:1-35`, `:136-305`, `:440-500`), `src/preload/preload.ts` (`:173-183`), `src/main/main.ts` (`:154-170`), `vite.main.config.ts` (`:10-37`), `.gitignore:111`, `src/renderer/project/Settings.tsx` (`:70-210`, `:300-386`, `:436-660`), `src/renderer/project/SopPanel.tsx` (`:60-110`, `:200-240`, `:280-290`), `src/main/claude-error-messages.test.ts` (247, skimmed; owned by 07), `README.md` (`:5-20`, `:175-210`, `:296-335`). Commits read (git show): `513a6d0` (validator, #63), `70ce4a7` (sources, baked plus HKLM), `850abc1` (net module, cache plugin, MSAL wrapper), `568aaab` (RFC 7523 exchange, probe), `d7d183b` (client factory on the SDK path), `f1b24a8` (Settings sign-in UI, three live-test bugs), `d79bc3b` (single instance, three-leg test), `49b0b98` (ADMX/ADML), `1ecebd0` (the uncalled invalidator, doc corrections), `679c19f` (key names are not shared with macOS), `992398f` (probe uses shotAI's own registration). `secrets.ts` history is squashed into `9da70df`. macOS (`/home/user/armadillon44/shotai_macos`, read-only): `Packages/EntraKit/Sources/EntraKit/FederationConfig.swift` (187), `CompositeCredentialProvider.swift` (74), `FederatedCredentialProvider.swift` (158), `EntraAuthClient.swift` (392), `FederationAccountStore.swift` (112), `JWTPeek.swift` (59), `InteractiveSignIn.swift` (35), `Pkce.swift` (71), `Package.swift` (31), `Packages/EntraKit/Tests/EntraKitTests/*.swift` (589, test names and comments), `Packages/SOPKit/Sources/SOPKit/Credential.swift` (129), `FederationExchange.swift` (91), `ApiKeyStore.swift` (140), `shotAI/AuthModel.swift` (116), `shotAI/Auth/WebAuthSignIn.swift` (87), `docs/SSO-WIF.md` (284). Anthropic C# SDK (`/home/user/anthropics/anthropic-sdk-csharp` at `2beeb9f`, read-only): `src/Anthropic/Credentials/IIdentityTokenProvider.cs`, `IAccessTokenProvider.cs`, `AccessToken.cs`, `WorkloadIdentityOptions.cs`, `WorkloadIdentityCredentials.cs`, `WorkloadIdentityException.cs`, `TokenCache.cs`, `CredentialsConstants.cs`, `AnthropicCredentials.cs`, `SecurityHelpers.cs`, `src/Anthropic/Core/ClientOptions.cs`, `src/Anthropic/Core/HttpClientPassthroughHandler.cs`, `src/Anthropic/AnthropicClient.cs` (`:149-250`, `:400-560`, `:810-845`). Microsoft Learn: "Using MSAL.NET with Web Account Manager (WAM)", "Token cache serialization" (desktop tab), "Desktop app that calls web APIs: Acquire a token by using WAM", "Acquiring tokens interactively", "Clearing the token cache". Context: `docs/NATIVE-WINDOWS-FEASIBILITY.md`, `dotnet/README.md`, `docs/native/spec/01-model-store.md`, `03-windows-shell.md`, `06-home-settings-ui.md` (section 2.25 in full). Verification (adversarial pass): every constant, string, regex and citation re-checked against source; Node's base64 and `Number` semantics re-measured under Node 22; the C# SDK claims re-read at `2beeb9f` (`AnthropicClient.cs`, `ClientOptions.cs`, `Core/ParamsBase.cs`, `Credentials/*.cs`); MSAL.NET WAM, the broker redirect URI, `WithParentActivityOrWindow(IntPtr)`, the `Prompt.SelectAccount` default and the 4.61.0 `net6.0-windows` removal confirmed on Microsoft Learn; corrections applied in place (test counts, the log category of the openExternal refusal, the `ipc.ts:821` citation, the probe details, the JWT base64 rule, compile errors in 7.5, 7.6, 7.10 and 7.11, the exception types required by 11 X2, the SDK error mapping); missing behaviors appended as INV-AUTH-35 to 36, EDGE-AUTH-36 to 45, D19 to D22 and Q-AUTH-17 to 18. Status: extracted and verified. Consolidated with ARCHITECTURE.md R-ARCH-1 to R-ARCH-26 on 2026-09-23.

**Notation used in this document.**

- Several Electron strings contain U+2014 (EM DASH). This document never prints that character. Wherever it occurs inside a quoted string it is written as the escape `\u2014`, and the C# literal must contain that exact character (C# accepts the same escape in a string literal, so copying the quoted text is exact). `…` is U+2026, `’` is U+2019, `⚙` is U+2699, `✓` is U+2713, `●` is U+25CF.
- `${x}` inside a quoted string is a JavaScript template substitution; the C# equivalent is an interpolated string with the same pieces in the same order. `A + B` means string concatenation with nothing inserted.
- `JsString.Trim` is spec 01's Core helper that trims exactly the JavaScript `String.prototype.trim` whitespace set (it differs from .NET `string.Trim()`: JS trims U+FEFF and does not trim U+0085). Every "trim" in this spec means `JsString.Trim` unless stated otherwise.
- Spec numbers (the files in `docs/native/spec/`): 01 model and store, 02 capture engine, 03 windows, shell and app menu, 04 editor and redaction, 05 report and project detail, 06 home list, settings UI and notices, 07 SOP generation and the Claude client, 08 this spec, 09 exports and packages, 10 brand contract, settings file, logging, update check, `openExternal` allowlist and self-tests ("the brand spec" is 10), 11 service boundary, threading, errors (`ShotAIException`, `UserMessage`) and DI, 12 packaging, installer and CI. This is the spec index of `docs/native/ARCHITECTURE.md` (R-ARCH-14), which `docs/native/PLAN.md` uses too; 06, 07 and 11 route auth to 08 (Q-AUTH-1, resolved by R-ARCH-14 and R-ARCH-2).
- Binding cross-spec resolutions: this spec follows ARCHITECTURE.md 15.3. The rows that shape it are R-ARCH-2 (08's `IAuthService` is canonical, `IApiKeyStore` is internal to Core), R-ARCH-3 (`IAuthService.TestConnectionAsync` is the one connection-test entry point), R-ARCH-10 (every disposable singleton also implements `IDisposable`), R-ARCH-13 (native local data under `%LOCALAPPDATA%\LFI\shotAI\`, so the MSAL cache is `%LOCALAPPDATA%\LFI\shotAI\entra\msal-cache.bin`), R-ARCH-14 (spec numbering) and R-ARCH-15 (08's `IAnthropicClientFactory` is the only Anthropic client factory; per-client handlers only, none on the shared handler).
- Classifications: **REQUIRED** (parity), **IMPROVEMENT** (a deliberate native change, with justification), **ELECTRON-ONLY** (disappears natively; the replacement for its intent is named).
- This repository is public. Every GUID and tagged id in this document is a placeholder (`11111111-...`, `fdrl_EXAMPLE`). Never paste a real tenant, client, organization, rule, service account or workspace id into this spec, its tests or its fixtures.

---

## 1. Scope

### 1.1 Owned by this subsystem

| Area | Electron location |
|---|---|
| Federation configuration: the eight value names, the baked (build-time) source, the HKLM policy source, per-key merge, validation (fail closed), the unconfigured signal, the resolved-config cache and its invalidation | `src/main/entra/config-validate.ts`, `config-sources.ts`, `config.ts`, `vite.main.config.ts:10-37`, `federation.example.json` |
| The ADMX/ADML policy contract and its tests (the files themselves are unchanged natively) | `Intune/Windows/shotAI.admx`, `en-US/shotAI.adml`, `Intune/Windows/README.md`, `docs/MANAGED-CONFIG.md` |
| Microsoft Entra sign-in: the MSAL client, authority, scope, account selection, silent and interactive acquisition, sign-out, the `SignInRequiredError` contract, the Conditional Access claims carry | `src/main/entra/msal.ts` |
| MSAL egress (the network module) and its proxy and certificate-store intent | `src/main/entra/net-module.ts` |
| The persisted MSAL token cache (encryption, miss-on-undecryptable, no plaintext) | `src/main/entra/cache-plugin.ts`, `src/main/claude-auth.ts:42-59` |
| The RFC 7523 diagnostic token exchange, its error wording, and the advisory `roles` check | `src/main/entra/federation.ts` |
| The credential decision and the Anthropic client factory: federated vs API key, the two refusal messages, the egress host pin, the explicit nulls, the SDK federation wiring (silent only) | `src/main/entra/auth-core.ts`, `src/main/claude-auth.ts` |
| The three-leg connection test (legs, order, messages, logging) | `auth-core.ts:107-136`, `src/main/claude-service.ts:256-304` |
| API key storage: encrypted file, env fallback, status shape, set and clear | `src/main/secrets.ts` |
| What the UI may learn: `AuthStatus`, `ApiKeyStatus`, `TestConnectionResult`; the `auth:*` verbs; the `auth:status` computation and its cache invalidation | `src/shared/ipc.ts:50-110`, `src/main/ipc.ts:760-834` |
| The Request access link's exact-origin extension of the `openExternal` allowlist | `src/main/ipc.ts:288-303` |
| The live-tenant probe (developer tooling) | `scripts/wif-probe.mjs` |

### 1.2 Not owned (consumed or delegated)

| Concern | Owner |
|---|---|
| The Settings AI tab layout, every control, its copy, disabled states, the key fallback disclosure, the test chip and the leg label prefixes (`Microsoft sign-in`, `Claude access`, `Claude API`) | 06 (section 2.25). This spec defines the service values those controls read. |
| The SOP panel's credential gate (`canGenerate = signedIn || hasKey`), its focus re-read, its "billed to" copy | 05 hosts the panel, 07 owns it |
| `friendlyError` (mapping SDK and auth exceptions to user text), `rateLimitMessage`, the estimate and generate calls, `models.retrieve` as the API leg call, the SOP settings (`enabled`, `model`) | 07 (natively `SopErrorMapper.Map`, `RateLimitClassifier`, `ResponseHeaderCaptureHandler` and the internal API-leg seam `SopModelProbe.RetrieveAsync`, 07 7.6 and 7.12; R-ARCH-3, R-ARCH-15). This spec states which of 07's branches the auth exceptions hit and adds one mapping 07 must implement (INV-AUTH-24). The connection test's orchestration and its one public entry point, `IAuthService.TestConnectionAsync`, are 08's (R-ARCH-3). |
| The base `openExternal` allowlist (`anthropic.com` and subdomains, `github.com` exact) and the call that opens the system browser | 10 |
| `settings.json` and the SOP settings schema | 10 |
| Logging infrastructure (the `claude` log category, rotation) | 10 |
| `AtomicFile` (temp file, flush, replace with sharing-violation retry) | 01 |
| Single-instance lock (a second instance surfaces the first window) | 03 |
| DI composition, the UI dispatcher, background work rules | 11 |
| Installer, the Desktop Runtime prerequisite, release build secrets (the baked federation file), CI runners | 12 |

Specs this one touches: 01, 03, 05, 06, 07, 10, 11, 12.

---

## 2. Reference behavior (Electron)

### 2.1 Shape of the subsystem and when it runs

- The auth factory is dependency-injected and Electron-free (`auth-core.ts:1-12`); `claude-auth.ts` is the thin shell that supplies Electron's `net.fetch`, `shell.openExternal`, the `safeStorage` cache plugin, `getFederationConfig` and `getApiKey` (`claude-auth.ts:27-66`). The split exists so `scripts/wif-probe.mjs` drives the real production factory rather than an imitation (`auth-core.ts:4-8`, commit `d7d183b`).
- `appAuth()` is built lazily on first use (`claude-auth.ts:19-28`). Nothing auth-related runs at launch: capture, editing, annotation and export are local, and gating them on an identity round trip would break the local-first claim. Auth is first triggered by the first Generate SOP click or by opening Settings then AI (`claude-auth.ts:22-25`); in practice `auth:status` is also read when the SOP panel mounts inside an open project (`SopPanel.tsx:67-92`). REQUIRED.
- BYO key remains the default and the fallback: with nothing configured the app behaves exactly as before federation existed (`auth-core.ts:10-12`). REQUIRED.
- Network calls this subsystem makes, all downstream of a user action (`README.md:9-15`, corrected by `1ecebd0`): `login.microsoftonline.com` on a Sign in click, on Test connection (silent acquisition), and on the SDK's refresh during a generation the user started; `POST https://api.anthropic.com/v1/oauth/token` for the exchange. The registry read is local. REQUIRED.

### 2.2 The eight value names

`FEDERATION_KEYS` in this exact order (`config-validate.ts:63-72`):

| # | Name | Required | Shape (after normalization) | Maps to | If malformed |
|---|---|---|---|---|---|
| 1 | `TenantId` | required | GUID | `tenantId` | fails closed |
| 2 | `ClientAppId` | optional | GUID | `clientAppId` (defaults to `audienceAppId`) | fails closed |
| 3 | `AudienceAppId` | required | GUID (bare, no `api://`) | `audienceAppId` | fails closed |
| 4 | `FederationRuleId` | required | `fdrl_` + alphanumeric | `federationRuleId` | fails closed |
| 5 | `OrganizationId` | required | GUID | `organizationId` | fails closed |
| 6 | `ServiceAccountId` | required | `svac_` + alphanumeric | `serviceAccountId` | fails closed |
| 7 | `WorkspaceId` | optional | `wrkspc_` + alphanumeric | `workspaceId` (omitted when absent) | fails closed |
| 8 | `SupportUrl` | optional | `https` URL | `supportUrl` (omitted when absent or bad) | ignored, the default is used |

The names are Windows-specific and frozen. macOS uses different preference keys (`federationEntraTenantId`, `federationEntraClientId`, `federationEntraAudienceAppId`, `federationRuleId`, `federationOrganizationId`, `federationServiceAccountId`, `federationWorkspaceId`) and requires all seven of its keys (`config-validate.ts:48-62`, `docs/MANAGED-CONFIG.md:11-30`, commit `679c19f`). Do not "align" them: deployed policy is keyed on these exact strings. REQUIRED.

None of the values is a secret (the Entra JWT authenticates; a public client id is public by design under PKCE), but together they identify one tenant and one Anthropic organization, so they are never logged by value, never sent to the renderer, and never committed (`config.ts:4-6`, `federation.example.json:2-18`, `docs/MANAGED-CONFIG.md:46-59`). REQUIRED.

### 2.3 Source 1: values baked at build time

- Build input: `src/main/entra/federation.local.json`, gitignored (`.gitignore:111`). `federation.example.json` is the committed template: a `_README` array plus placeholder values for `TenantId`, `AudienceAppId`, `FederationRuleId`, `OrganizationId`, `ServiceAccountId`, `WorkspaceId` (`federation.example.json:1-26`).
- `vite.main.config.ts:12-27` reads the file at build time: parse JSON; for each entry keep it only if the key does NOT start with `_`, the value is a string, and `value.trim()` is non-empty; store the trimmed value. Any read or parse failure yields `{}`. The result is inlined as the literal `__FEDERATION_BAKED__` (`vite.main.config.ts:33-37`). A missing file therefore means "unconfigured", never a build break for external contributors (commit `70ce4a7`).
- At runtime `bakedRecord()` returns that literal, or `{}` when the define is absent (`config.ts:23-28`).
- `pickBaked(baked)` narrows to the contract: for each name in `FEDERATION_KEYS`, keep `baked[k]` only when it is a string with non-empty `trim()`, storing the trimmed value; drop everything else (unknown keys, `_README`, blanks) (`config-sources.ts:74-82`).
- The build reads the file with `fs.readFileSync(..., 'utf8')` and `JSON.parse`, relative to the build's working directory (`vite.main.config.ts:14-16`). Node does not strip a UTF-8 byte order mark on that read and `JSON.parse` rejects a leading U+FEFF, so a BOM-prefixed file (for example one saved by Windows PowerShell 5.1 `Set-Content -Encoding UTF8`) silently bakes `{}` and the build ships unconfigured (EDGE-AUTH-37). A root that is not an object is handled by `Object.entries` (an array or a string yields index keys that `pickBaked` drops; `null` throws and yields `{}`).
- The committed template is not a valid config: its tagged placeholders `fdrl_REPLACE_ME`, `svac_REPLACE_ME` and `wrkspc_REPLACE_ME` contain `_` in the body, which the `TAGGED` regexes reject, while its all-zero GUIDs pass `GUID` (`federation.example.json:20-25`). A copied but unedited template is therefore a REJECTED config (`invalid: FederationRuleId,ServiceAccountId,WorkspaceId`), not an unconfigured one (EDGE-AUTH-36).

REQUIRED (native mechanism in 7.4).

### 2.4 Source 2: the HKLM policy key

- Key: `HKLM\SOFTWARE\Policies\shotAI\Federation` (`config-sources.ts:25`). HKLM only, never HKCU: only an administrator or MDM can write it, so a standard user cannot repoint their own client at another tenant or rule. `SOFTWARE\Policies` so it reads as policy and so Intune ADMX ingestion accepts it (the blocked namespaces are `System`, `Software\Microsoft`, `Software\Policies\Microsoft`) (`config-sources.ts:17-24`). REQUIRED [SECURITY].
- Read: one `reg.exe query <key> /reg:64` for the whole key, `/reg:64` so a 32-bit build is not redirected to `Wow6432Node` (`config-sources.ts:61-72`, `config-sources.test.ts:105-116`). `reg.exe` is spawned by absolute path `${SystemRoot ?? 'C:\Windows'}\System32\reg.exe`, no shell, `windowsHide: true` (`config.ts:15-21`). The absolute path and hidden console are ELECTRON-ONLY (natively the registry API is called directly).
- Non-Windows: returns `{}` without calling the runner (`config-sources.ts:65`).
- Any runner failure (key not found exits non-zero, access denied, anything) returns `{}`: no policy is the normal state of every machine (`config-sources.ts:57-72`). REQUIRED.
- Parse (`parseRegQuery`, `config-sources.ts:39-54`): split stdout on `/\r?\n/`; for each line match `/^\s+(\S+)\s+REG_SZ\s{2,}(.*)$/`; skip non-matching lines (the key header line, other types such as `REG_DWORD`, blank lines, error text); keep a match only when the name is one of `FEDERATION_KEYS` (exact, case-sensitive); store `value.trim()`. Consequences, all REQUIRED:
  - Only `REG_SZ` is part of the contract. `REG_EXPAND_SZ`, `REG_MULTI_SZ`, `REG_DWORD` and every other type read as absent from policy (not coerced).
  - Unknown value names under the key are ignored, so an unrelated value cannot inject a field.
  - A value containing single spaces is kept intact (`config-sources.test.ts:74-77`).
  - An empty `REG_SZ` value reads as `""`, which the merge treats as not set.
  - Name matching is case-sensitive: a hand-created `tenantid` is ignored.

### 2.5 Merge, validation input and the unconfigured signal

**Merge** (`mergeFederationSources(baked, policy)`, `config-validate.ts:208-218`): start from a copy of `baked`; for each name in `FEDERATION_KEYS`, if `policy[k]` is a string and `policy[k].trim().length > 0`, set `out[k] = policy[k]` (the policy value as delivered, untrimmed here; validation trims). A blank policy value does NOT clear a baked value, so half-clearing a policy key cannot disable federation for the fleet. Policy wins per key; overriding one key leaves the other seven at their baked values. REQUIRED.

Consequence documented in `docs/MANAGED-CONFIG.md:145-153`: a wrong-typed policy value (for example `REG_EXPAND_SZ`) reads as absent, so on a normal deployment the baked value stands and the override is silently ignored. It fails closed only where nothing else supplies that required value. REQUIRED.

**Normalization** (`norm`, `config-validate.ts:99-104`): if the value is not a string, `null`; else `t = trim(v)`, then if `t` matches `/^\{(.*)\}$/` replace it with the inner text, then `trim` again; `null` if empty, else `t`. So `"  {11111111-1111-1111-1111-111111111111}  "` becomes the bare GUID. `.` does not match a line terminator, so a value containing a newline inside the braces is not stripped. `(.*)` is greedy and the strip happens once: `{}` normalizes to `null` (missing, not invalid), `{{GUID}}` becomes `{GUID}` (invalid), and `{a}{b}` becomes `a}{b`. REQUIRED.

**Unconfigured** (`isUnconfigured`, `config-validate.ts:194-196`): `true` exactly when `norm(raw[k]) === null` for all eight names. It is the signal to behave exactly as before federation, with no mention of Entra anywhere in the UI. A rejected config is different: an administrator must see it (logged, 2.7). REQUIRED.

### 2.6 Validation (fail closed)

`validateFederationConfig(raw)` (`config-validate.ts:128-186`) returns `{ ok: true, config }` or `{ ok: false, missing: string[], invalid: string[] }` (names only, never values). The evaluation order fixes the order of names inside `missing` and `invalid`:

| Step | Name | Rule | On absent (`norm` is `null`) | On present but not matching |
|---|---|---|---|---|
| 1 | `TenantId` | `GUID` | push to `missing` | push to `invalid` |
| 2 | `AudienceAppId` | `GUID` | push to `missing` | push to `invalid` |
| 3 | `ClientAppId` | `GUID` | `clientAppId = audienceAppId` (the normalized audience value, or `""` when step 2 failed) | push to `invalid` |
| 4 | `FederationRuleId` | `TAGGED.fdrl` | `missing` | `invalid` |
| 5 | `OrganizationId` | `GUID` | `missing` | `invalid` |
| 6 | `ServiceAccountId` | `TAGGED.svac` | `missing` | `invalid` |
| 7 | `WorkspaceId` | `TAGGED.wrkspc` | `workspaceId` omitted | `invalid` |
| 8 | gate | if `missing.length || invalid.length` return `{ ok: false, missing, invalid }` | | |
| 9 | `SupportUrl` | `isHttpsUrl` | omitted | omitted (never fails the config) |

Regexes, verbatim (`config-validate.ts:84-91`):

- `GUID = /^[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}$/`
- `fdrl: /^fdrl_[0-9A-Za-z]+$/`, `svac: /^svac_[0-9A-Za-z]+$/`, `wrkspc: /^wrkspc_[0-9A-Za-z]+$/` (a bare prefix such as `fdrl_` is invalid).

`isHttpsUrl(v)` (`config-validate.ts:106-112`): `new URL(v).protocol === 'https:'`, `false` when the WHATWG URL parser throws. `http:`, `javascript:` and unparsable strings are dropped (`config-validate.test.ts:152-173`).

Result shape (`config-validate.ts:16-41`, `:174-185`): `tenantId`, `clientAppId`, `audienceAppId`, `federationRuleId`, `organizationId`, `serviceAccountId` always; `workspaceId` and `supportUrl` only when present and valid (the keys are absent otherwise, not `undefined`).

Why each optional value behaves as it does (`config-validate.ts:113-127`, `docs/MANAGED-CONFIG.md:79-88`): a malformed `ClientAppId` would aim sign-in at a client that does not exist and fail as an opaque `AADSTS` error in the browser (reported under the `Microsoft sign-in:` leg); a malformed `WorkspaceId` takes part in the exchange and yields the same opaque 401 as a missing role (the expensive misdiagnosis); `SupportUrl` is presentational and a typo must not cost the whole organization its sign-in. REQUIRED.

### 2.7 Resolution, caching, invalidation and logging

`resolveFederation({ baked, runner, platform })` (`config-sources.ts:94-108`) returns `{ raw, result, unconfigured }` with `raw = merge(pickBaked(baked), await readPolicyConfig(runner, platform))`, `result = validate(raw)`, `unconfigured = isUnconfigured(raw)`.

`getFederationConfig()` (`config.ts:44-72`) caches a `Promise<FederationConfig | null>` for the process lifetime:

| State | Event | Guard | Action | Next state |
|---|---|---|---|---|
| Empty | `getFederationConfig()` | | start `resolveFederationNow()`, store the promise | Pending |
| Pending or Filled | `getFederationConfig()` | | return the stored promise (no re-read) | same |
| Pending | resolution completes | `result.ok` | log info `federation: configured.`; resolve with `result.config` | Filled (config) |
| Pending | resolution completes | `!result.ok && !unconfigured` | log warn `` `federation: config rejected, falling back to API key (missing: ${missing.join(',') || 'none'}; invalid: ${invalid.join(',') || 'none'}).` ``; resolve `null` | Filled (null) |
| Pending | resolution completes | `unconfigured` | nothing logged; resolve `null` | Filled (null) |
| any | `invalidateFederationConfig()` | | drop the stored promise | Empty |

Callers get one nullable value: unconfigured and rejected both mean "behave exactly as today" (`config.ts:46-50`). REQUIRED.

The invalidation is called from exactly one place: the `auth:status` handler, before anything else in it (`ipc.ts:787-800`). Settings (on mount and after sign-in or sign-out) and the SOP panel (on mount and on window focus) both read `auth:status`, so an administrator's policy correction is picked up by opening Settings or a project, without a restart (`docs/MANAGED-CONFIG.md:208-215`, `Intune/Windows/README.md:104-105`, `Intune/Windows/en-US/shotAI.adml:30`). Re-reading is safe because a failing registry read degrades to the baked values rather than flipping federation off (`ipc.ts:795-797`). This call shipped missing once: the invalidator existed, its comment and two admin documents promised the behavior, and nothing called it (commit `1ecebd0`). REQUIRED.

Other readers do not invalidate: `makeClient`, `testConnection`, `auth:sign-in`, `auth:sign-out` and the `openExternal` SupportUrl check use whatever is cached. REQUIRED.

### 2.8 The Entra client (MSAL)

`createEntraClient(cfg, deps)` (`msal.ts:67-164`) wraps one `@azure/msal-node` `PublicClientApplication`:

- Flow: authorization code with PKCE in the SYSTEM browser via MSAL's loopback listener on `http://localhost:<ephemeral port>`. Never an embedded webview: it cannot satisfy device-based Conditional Access (only the real browser carries the Primary Refresh Token), has no FIDO or Windows Hello, is the pattern Microsoft documents against, and would put the auth code in a renderer origin (`msal.ts:1-10`). The browser is opened with `shell.openExternal` (never `cmd /c start`, which truncates the URL at the first `&`: commit `568aaab`) (`claude-auth.ts:36-41`).
- The `PublicClientApplication` is created lazily on first use and the PROMISE is cached, so two concurrent callers never construct two instances over the same cache file (`msal.ts:68-92`).
- `auth.clientId = cfg.clientAppId`; `auth.authority = 'https://login.microsoftonline.com/' + cfg.tenantId` (tenant-specific, never `common`); no `redirectUri` (msal-node throws on interactive if it is set without a broker; the loopback client owns it); `cache.cachePlugin` when supplied; `system.networkClient` when supplied (`msal.ts:74-88`).
- NOT set: `clientCapabilities: ['CP1']`. In a CAE session Entra extends the access token to up to 28 hours, which exceeds the Anthropic issuer's maximum JWT lifetime and would make every exchange fail; a custom audience is not a CAE resource anyway (`msal.ts:84-87`). REQUIRED.
- Scope: exactly one, `` `api://${cfg.audienceAppId}/user_impersonation` `` (`scopeFor`, `msal.ts:60-65`). Explicit, never `.default`: a client requesting its own `.default` can receive an id_token instead of an access token, and the exchange then fails with the opaque 401. REQUIRED.
- Signed-in account: `getTokenCache().getAllAccounts()[0] ?? null` (`msal.ts:94-97`).

`SignInRequiredError` (`msal.ts:24-36`): `name = 'SignInRequiredError'`, default message `Sign in with your Microsoft account to use Claude.`, optional `claims` (a Conditional Access claims challenge to replay on the interactive call). The UI turns it into a Sign in action, never an error dialog (`msal.ts:24-25`).

Operations:

| Operation | Behavior | Citation |
|---|---|---|
| `signedInAccount()` | first cached account or `null`; no network | `msal.ts:94-97`, `:100` |
| `acquireSilent({ forceRefresh?, claims? })` | no account: throw `SignInRequiredError()`. Else `acquireTokenSilent({ account, scopes: [scope], forceRefresh, claims })`. Missing `accessToken`: throw `SignInRequiredError()`. `InteractionRequiredAuthError`: throw a new `SignInRequiredError()` whose `claims` is the error's `claims` when that is a non-empty string. Any other error propagates unchanged. Never prompts. | `msal.ts:102-129` |
| `signInInteractive(claims?)` | `acquireTokenInteractive({ scopes: [scope], claims, openBrowser, successTemplate, errorTemplate })`, response mode left at the default (query). Missing `accessToken`: `SignInRequiredError()`. Success logs `entra: interactive sign-in completed.` Returns the token (callers discard it). | `msal.ts:131-156` |
| `signOut()` | for every cached account, `removeAccount`; log `entra: signed out.` | `msal.ts:158-162` |

Loopback page texts (plain text, because msal-node's loopback server writes no `Content-Type` and a browser would render markup literally; live-test bug 3 in commit `f1b24a8`): success `Signed in to shotAI. You can close this tab and return to the app.`; error `shotAI sign-in failed. Close this tab and try again from the app.` (`msal.ts:144-151`). ELECTRON-ONLY for the WAM path; REQUIRED for the browser fallback (7.5).

`forceRefresh` exists for the role-grant trap but no caller passes `true`; `claims` is attached to the thrown error but no caller replays it (`ipc.ts:817-826` calls `signInInteractive()` with no argument). See EDGE-AUTH-9 and EDGE-AUTH-10.

The `Auth` factory caches the `EntraClient` and rebuilds it only when `cfg.clientAppId` changes (`auth-core.ts:73-88`). A `TenantId` or `AudienceAppId` correction therefore keeps the old authority and scope until restart (EDGE-AUTH-8).

**Sign-in state machine (Electron, per process):**

| State | Event | Guard | Action | Next state |
|---|---|---|---|---|
| NoFederation | any auth verb | config `null` | `auth:sign-in` throws `shotAI is not set up for Microsoft sign-in on this machine.`; `auth:sign-out` is a no-op | NoFederation |
| SignedOut | `auth:status` | config valid, no cached account | report `signedIn: false` | SignedOut |
| SignedOut | Sign in click | | interactive in the system browser | Interactive |
| Interactive | browser completes | token returned | cache written through the plugin; log `entra: interactive sign-in completed.` | SignedIn |
| Interactive | error page or MSAL error | | error propagates to Settings (`Error: <message>`) | SignedOut |
| Interactive | user closes the tab | | the promise stays pending (shotAI configures no timeout); the button stays `Waiting for your browser…` | Interactive (EDGE-AUTH-11) |
| SignedIn | silent acquisition | refresh token valid | token returned | SignedIn |
| SignedIn | silent acquisition | `InteractionRequiredAuthError` | throw `SignInRequiredError` (with claims) | SignedIn (the account stays cached; status still reports signed in) |
| SignedIn | Sign in again click | | interactive (fresh claims) | Interactive |
| SignedIn | Sign out click | | remove every cached account; log `entra: signed out.`; stored API key untouched | SignedOut |

### 2.9 MSAL egress (network module)

`msal-node` 5.x removed `proxyUrl` and `customAgentOptions`; `system.networkClient: INetworkModule` remains (`net-module.ts:1-16`, commit `513a6d0`). `createNetworkModule(fetchImpl)` (`net-module.ts:35-97`) implements it over Electron `net.fetch` (Chromium's stack: Windows system proxy, WPAD/PAC, the Windows certificate store; Node's undici trusts only its bundled CAs and fails opaquely behind TLS inspection):

- `sendGetRequestAsync(url, options, timeout)` and `sendPostRequestAsync(url, options)`.
- A timeout is honored only when it is a number `> 0` (an `AbortController` plus a timer that is always cleared); MSAL passes one on GET only (`:52-54`, `:81-83`).
- GET never carries a body; POST sends `options.body ?? ''` (`:58-61`).
- Response headers are lowercased (`:64-67`).
- The body is parsed for EVERY status and a non-2xx never throws: Entra's 4xx JSON (`error`, `suberror`) is what MSAL reads to decide that a silent call needs interaction; throwing would collapse `InteractionRequiredAuthError` into a network failure (`:69-80`).
- `parseBody`: empty text gives `{}`; JSON when it parses; otherwise the raw text (a proxy login page stays visible) (`:86-96`).

ELECTRON-ONLY. Its intent (sign-in honors the system proxy and certificate store; Entra error bodies reach MSAL's classifier intact) is met natively by WAM (OS networking) and by MSAL.NET's own HTTP stack over the shared `HttpClient` (7.14).

### 2.10 The persisted token cache

`createSafeStorageCachePlugin` (`cache-plugin.ts:54-123`), file `%APPDATA%\shotAI\entra-cache.bin` (`claude-auth.ts:42-44`), written with `writeFileAtomic(path, buffer, { mode: 0o600 })` and the retry log `` `entra cache rename ${code}, retrying` `` (`claude-auth.ts:51-55`). The serialized MSAL cache holds the Entra refresh token and access token; it is main-only, never logged, never in diagnostics or exports (`cache-plugin.ts:5-8`). Only the refresh token is worth persisting; the minted `sk-ant-oat01-` token is never written anywhere (a replayable bearer credential at rest in backups, roaming profiles, EDR telemetry and crash dumps). The Entra access token rides along because MSAL cannot persist one without the other; accepted (`cache-plugin.ts:9-19`).

| Hook | Condition | Behavior | Log line |
|---|---|---|---|
| `beforeCacheAccess` | encryption unavailable | return without reading the file (memory-only session) | `entra: OS encryption unavailable, token cache is memory-only this session.` (logged on EVERY cache access, since MSAL calls the hook per operation; natively once per process, EDGE-AUTH-42) |
| `beforeCacheAccess` | file read fails (absent) | return (normal first run) | none |
| `beforeCacheAccess` | decrypt succeeds | `deserialize(result.result)` (the string, not the `{ shouldReEncrypt, result }` object) | none |
| `beforeCacheAccess` | decrypt succeeds, `shouldReEncrypt` | re-encrypt and write; a failed write is non-fatal | `entra: token cache re-encrypted after an OS key rotation.` or `entra: token cache re-encryption failed, will retry next launch.` |
| `beforeCacheAccess` | decrypt or deserialize throws | treat as a miss: signed out, not broken; one interactive sign-in self-heals it | `entra: no usable token cache, sign-in required.` |
| `afterCacheAccess` | `!cacheHasChanged` | no write | none |
| `afterCacheAccess` | encryption unavailable | no write, never plaintext | none |
| `afterCacheAccess` | write throws | swallow (costs one sign-in later; must not break the sign-in completing now) | `entra: could not persist the token cache.` |
| `clear()` | | delete the file, ignore failure | none |

Citations: `cache-plugin.ts:56-113`. `clear()` (`:115-121`) is not called anywhere: sign-out removes the accounts and the plugin writes the emptied cache (`ipc.ts:830-832`), so the file remains with no accounts (EDGE-AUTH-12).

Why undecryptable is common under Electron: `safeStorage` does not DPAPI-protect the ciphertext; Chromium OSCrypt keeps a random AES-256-GCM key in `Local State` (`os_crypt.encrypted_key`, DPAPI-protected), so losing `Local State` makes every entry undecryptable (`cache-plugin.ts:91-97`).

No cross-process lock: `@azure/msal-node-extensions` was avoided because it depends on archived `keytar` and about 44 MB of WAM binaries (commit `513a6d0`); the single-instance lock stands in for it (`main.ts:154-169`).

### 2.11 The diagnostic token exchange (RFC 7523)

Production traffic does not use this; the SDK's federation provider owns it. This exists for Test connection and the probe, where raw status and shotAI's own wording matter (`federation.ts:1-15`).

`exchangeAssertion(assertion, cfg, { fetchImpl, signal?, baseUrl?, log? })` (`federation.ts:70-166`). It throws only `FederationExchangeError(message, status = null, requestId = null)` whose messages are safe to show (`federation.ts:43-52`, `:63-69`).

1. `bytes = UTF-8 byte length of assertion`. If `!assertion || bytes === 0`: throw `No Microsoft sign-in token to exchange.` (no request).
2. If `bytes > 16384` (`MAX_ASSERTION_BYTES = 16 * 1024`): throw `` `Sign-in token is ${Math.ceil(bytes / 1024)} KiB; Anthropic rejects assertions over 16 KiB.` `` (no request).
3. `POST (baseUrl ?? 'https://api.anthropic.com') + '/v1/oauth/token'` with header `content-type: application/json` and body `JSON.stringify` of, in this key order: `grant_type: 'urn:ietf:params:oauth:grant-type:jwt-bearer'`, `assertion`, `federation_rule_id`, `organization_id`, `service_account_id`, and `workspace_id` only when `cfg.workspaceId` is truthy (omitted, never `null`: a null is a 400) (`:93-111`). No `anthropic-beta` header.
4. Transport failure (the fetch throws): `` `Could not reach Anthropic to sign in (${e instanceof Error ? e.message : String(e)}).` `` with `status = null` (`:112-118`). Distinguished so users do not re-check their role for a proxy or DNS problem.
5. `requestId = response header 'request-id'` or `null` (fetch `Headers.get`: several values are joined with `, `; an empty value is falsy, so every later `requestId ?` test treats it as absent and no ` (request-id )` piece or ` request-id=` log fragment is ever produced); `ms = Date.now() - started`.
6. Not ok: read the body as text (a read failure gives `''`), DISCARD it, log `` `federation exchange failed: HTTP ${status} in ${ms}ms` + (requestId ? ` request-id=${requestId}` : '') + ` (${detail.length}-byte body withheld)` ``, throw `explain(status, requestId)` (`:123-133`). `detail.length` is the body's UTF-16 length.
7. Ok: `resp.json()` failure: `Anthropic returned an unreadable sign-in response.` (with status and request id) (`:135-144`).
8. `body = data ?? {}`; `token = typeof body.access_token === 'string' ? body.access_token : ''`; `expiresIn = Number(body.expires_in)`. If `!token || !Number.isFinite(expiresIn) || expiresIn <= 0`: `Anthropic sign-in response was missing access_token or expires_in.` (`:145-154`).
9. Log `` `federated token minted in ${ms}ms (expires_in=${expiresIn}s).` `` and return `{ token, expiresInSeconds: expiresIn, expiresAt: Date.now() + expiresIn * 1000, scope: typeof body.scope === 'string' ? body.scope : null }`. `expires_in` is trusted and never replaced by a constant: the server mints `min(rule lifetime, 2 x remaining JWT life)` with a 60 s floor, so a stale assertion legitimately yields a shorter token (`:156-165`).

The token and assertion are never logged (`federation.test.ts:106-114`); the token is never written to disk (`federation.ts:55`).

`explain(status, requestId)` (`federation.ts:168-214`), with `rid = requestId ? ` (request-id ${requestId})` : ''`:

| Status | Message (exact) | Why |
|---|---|---|
| 401 | `'Anthropic refused the sign-in. Your account may not be assigned the shotAI role yet. ' + 'Administrators: the deny reason is in Claude Console > Settings > Workload identity > History' + rid + '.'` | Every assertion denial (issuer, audience, CEL miss, expired JWT, archived rule, missing workspace) is the same opaque 401 by design; the true reason exists only on the Console History page, and the request id makes the entry findable. Do not guess causes (external probing produced two confident wrong diagnoses during the macOS work). |
| 400 | `"shotAI's federation settings are invalid (rule, organization, or workspace id)." + rid` | Rejected before the organization is corroborated, so no History entry exists; the config is wrong, not the account. |
| 429 | `'Anthropic is rate limiting sign-in right now. Try again shortly.' + rid` | Ambiguous (throttle or spent budget); no cause asserted. |
| `>= 500` | `` `Anthropic sign-in is temporarily unavailable (HTTP ${status}).` `` + rid | |
| other | `` `Anthropic sign-in failed (HTTP ${status}).` `` + rid | |

Note the punctuation: for 401 the request id sits before the final period (`...History (request-id req_1).`); for every other status it follows the sentence's own period (`...workspace id). (request-id req_1)`). REQUIRED.

`JSON.stringify` does not escape non-ASCII; every field here is ASCII (validated ids and a base64url JWT), so any conforming JSON encoder produces the same bytes. `Number(...)` is JavaScript `ToNumber` (7.7 gives the exact rules the native code applies).

### 2.12 The advisory role check

`hasAppRole(jwt, role)` (`federation.ts:216-235`), `REQUIRED_APP_ROLE = 'shotAI.User'` (`:237-238`):

1. `part = jwt.split('.')[1]`; missing or empty: `null`.
2. Decode `part` with `-` replaced by `+` and `_` by `/` as base64, then UTF-8 (invalid sequences become U+FFFD). Node's decoder is lenient, measured under Node 22: characters outside the base64 alphabet are skipped, padding is optional, a dangling single sextet is dropped, and decoding STOPS at the first `=` (`Buffer.from('ey=Jh','base64')` is one byte, `7b`; `'eyJh=eyJh'` decodes only `eyJh`). An empty or one-character part decodes to an empty buffer, which then fails `JSON.parse` (so `a.b.c` gives `null`).
3. `JSON.parse`; any exception (including reading `.roles` of `null`): `null`.
4. `roles` not an array: `false` (Entra OMITS `roles` for an unassigned user rather than sending `[]`, so absence is a real signal).
5. Else `roles.includes(role)` (strict equality).

Advisory and fail-open: `null` means "could not tell" and the exchange is attempted anyway; a parse bug must not lock out a correctly assigned user. The federation rule's CEL condition is the authority (`federation.ts:216-221`, `docs/MANAGED-CONFIG.md:173-174`). An elevated directory role bypasses assignment gates and arrives with no `roles` claim, so an admin account proves nothing about staff (`wif-probe.mjs:161`, commit `70ce4a7`). REQUIRED.

### 2.13 The credential decision and the client factory

`makeClient()` (`auth-core.ts:138-188`):

1. `cfg = await federation()` (cached, 2.7).
2. `signedIn = cfg ? !!(await entraFrom(cfg).signedInAccount()) : false`. "Signed in" means an account is cached, not that a token is currently obtainable.
3. `key = signedIn ? null : await getApiKey()` (the key is not even read when signed in).
4. If `!signedIn && !key`: throw `SignInRequiredError(cfg ? 'Sign in with your Microsoft account to use Claude.' : 'Add an Anthropic API key in Settings to use Claude.')`.
5. Build the client (below) and return `{ client, mode: signedIn ? 'federated' : 'apiKey' }`.

Decision table (REQUIRED):

| Federation config | Cached account | API key (stored, else env) | Result |
|---|---|---|---|
| none (unconfigured or rejected) | not consulted | present | `apiKey` client |
| none | not consulted | absent | throw `Add an Anthropic API key in Settings to use Claude.` |
| valid | yes | not read | `federated` client |
| valid | no | present | `apiKey` client (a rollout strands nobody) |
| valid | no | absent | throw `Sign in with your Microsoft account to use Claude.` |

An account that is cached but whose refresh token is dead still selects `federated`; the failure surfaces during the request as `SignInRequiredError` and does NOT fall back to the key (EDGE-AUTH-14). The wording test guards the remedy: an external user with no tenant must never be told to sign in with Microsoft (`auth-core.test.ts:43-53`).

Client construction (`auth-core.ts:154-185`):

- `baseURL: 'https://api.anthropic.com'` always (`ANTHROPIC_BASE_URL`, `auth-core.ts:21-24`). Without it the SDK defaults to `process.env.ANTHROPIC_BASE_URL`, and a poisoned environment could redirect the credential and the screenshots. [SECURITY]
- `apiKey: signedIn ? null : key` and `authToken: null`, explicitly. `apiKey` defaults from `ANTHROPIC_API_KEY` and takes precedence over `credentials`, so a leftover env var would silently disable federation; even `ANTHROPIC_API_KEY=""` occupies the slot (`auth-core.ts:156-161`). [SECURITY]
- Federated only: `credentials: oidcFederationProvider({ identityTokenProvider: () => entraFrom(cfg).acquireSilent(), federationRuleId, organizationId, serviceAccountId, workspaceId, baseURL: 'https://api.anthropic.com', fetch: net.fetch })` (`auth-core.ts:162-183`). `baseURL` and `fetch` are both required fields (the #63 snippet omitted `fetch` and did not compile, commit `d7d183b`).
- Silent ONLY in the identity token provider: it can fire from a background refresh, where an interactive prompt would pop a browser with no user context (`auth-core.ts:171-173`). [SECURITY]
- The client wraps the provider in its own token cache: advisory refresh at `exp - 120 s`, mandatory at `exp - 30 s`, concurrent coalescing, and one invalidate-and-retry on a 401. No timer is scheduled by shotAI: a timer does not advance across S3 suspend, and NTP correction or VM resume can move the clock either way (`auth-core.ts:164-169`). Measured live by the probe: 0 extra exchanges on a second API call (commit `d7d183b`).
- API traffic stays on the SDK's default fetch (Node's), not `net.fetch`: SSE through `net.fetch` was untested (`auth-core.ts:29-34`). ELECTRON-ONLY; natively all traffic uses the system-proxy `HttpClient` (IMPROVEMENT, 7.14).

`makeClient` is called fresh by every estimate, generate and API-leg test (`claude-service.ts:278-280`, `:459`, `:495`), so each call builds a new client with a new token cache and performs one exchange on its first request.

`federation()`, `entra()` (the `EntraClient` or `null` when unconfigured) and `isSignedIn()` (`!!signedInAccount()` or `false`) are thin readers (`auth-core.ts:90-100`).

### 2.14 The three-leg connection test

`testConnection()` (`claude-service.ts:256-304`) returns `TestConnectionResult` `{ ok, mode?, model?, error?, leg? }`, with expected failures returned, not thrown (`src/shared/ipc.ts:96-110`).

| Step | Condition | Result or action |
|---|---|---|
| 0 | SOP generation disabled | return `{ ok: false, error: 'AI SOP generation is turned off.' }` (no mode, no leg, no network) |
| 1 | `federated = !!(await auth.federation()) && (await auth.isSignedIn())` | choose the branch |
| F1 | `verifyFederation()`: config vanished | `{ ok: false, leg: 'signIn', error: SignInRequiredError('shotAI is not set up for Microsoft sign-in on this machine.') }` (`auth-core.ts:107-117`) |
| F2 | `acquireSilent()` throws anything | `{ ok: false, leg: 'signIn', error }` (`:118-123`) |
| F3 | `hasRole = hasAppRole(assertion, 'shotAI.User')` | advisory, never gates (`:124-126`) |
| F4 | `exchangeAssertion(assertion, cfg, { fetchImpl: net.fetch, log })` throws | `{ ok: false, leg: 'exchange', error }` (`:127-134`) |
| F5 | legs 1 and 2 failed | log warn `` `connection test failed at the ${v.leg} leg.` ``; return `{ ok: false, mode: 'federated', leg, error: friendlyError(error, 'federated') }` |
| F6 | `hasRole === false` after a successful exchange | log info `connection test: exchange succeeded although the local roles check saw no shotAI.User.`; do not fail (the rule accepted the token; the local peek is advisory) |
| F7 | `makeClient()` then `client.models.retrieve(sop.model)` throws | `{ ok: false, mode: 'federated', leg: 'api', error: friendlyError(e, 'federated') }` (no log line on this branch) |
| F8 | success | log info `` `connection verified against ${sop.model} (mode=federated, 3 legs).` ``; `{ ok: true, mode: 'federated', model }` |
| K1 | not federated: `makeClient()`, `models.retrieve(sop.model)` | success: log info `` `credential validated against ${sop.model} (mode=${mode}).` ``; `{ ok: true, model, mode }` |
| K2 | not federated, any throw | `mode` stays `'apiKey'` if `makeClient` threw; log warn `` `connection test failed (mode=${mode}): ${error}` ``; `{ ok: false, error: friendlyError(e, mode), mode, leg: 'api' }` |

The legs are ordered: an exchange failure is never reported before the sign-in leg passed, because "the rule refused a real token" and "sign in first" have different remedies (`auth-core.test.ts:83-90`). The diagnostic exchange's token is discarded; leg 3 performs its own exchange through the SDK, so a passing federated test costs two exchanges. The 401 from Anthropic is deliberately opaque, which is why the legs exist at all (`auth-core.ts:52-62`). REQUIRED.

Message a user sees per failure (the leg prefix is 06's `legLabel + ': '`; the message is `error`):

| Leg | Cause | Full text shown (06 format `<Leg>: <error>`) |
|---|---|---|
| none | SOP generation off | `AI SOP generation is turned off.` |
| signIn | config vanished between checks | `Microsoft sign-in: shotAI is not set up for Microsoft sign-in on this machine.` |
| signIn | no cached account, interaction required, or no access token | `Microsoft sign-in: Sign in with your Microsoft account to use Claude.` |
| signIn | any other MSAL or network error | `Microsoft sign-in: ` + the error's raw message (07's `friendlyError` fallback) |
| exchange | empty assertion | `Claude access: No Microsoft sign-in token to exchange.` |
| exchange | oversized assertion | `Claude access: Sign-in token is 17 KiB; Anthropic rejects assertions over 16 KiB.` (for 16385 to 17408 bytes) |
| exchange | transport | `Claude access: Could not reach Anthropic to sign in (<transport message>).` |
| exchange | 401 | `Claude access: Anthropic refused the sign-in. Your account may not be assigned the shotAI role yet. Administrators: the deny reason is in Claude Console > Settings > Workload identity > History (request-id <id>).` |
| exchange | 400 | `Claude access: shotAI's federation settings are invalid (rule, organization, or workspace id). (request-id <id>)` |
| exchange | 429 | `Claude access: Anthropic is rate limiting sign-in right now. Try again shortly. (request-id <id>)` |
| exchange | 5xx | `Claude access: Anthropic sign-in is temporarily unavailable (HTTP 503). (request-id <id>)` |
| exchange | other 4xx | `Claude access: Anthropic sign-in failed (HTTP 403). (request-id <id>)` |
| exchange | unreadable 200 | `Claude access: Anthropic returned an unreadable sign-in response.` |
| exchange | 200 missing fields | `Claude access: Anthropic sign-in response was missing access_token or expires_in.` |
| api | offline | `Claude API: Could not reach Anthropic \u2014 check your network connection.` (07, `claude-service.ts:208`) |
| api | 401 (federated) | `Claude API: Your Claude access was refused. Check that your account still has the shotAI role.` (`:227`) |
| api | 403 (federated) | `Claude API: This sign-in does not have permission for that (the federation rule's scope).` (`:229`) |
| api | 404 (federated) | `Claude API: The selected model is unavailable for this sign-in.` (`:231`) |
| api | 401, 403, 404 (key) | `Claude API: Invalid API key.`, `Claude API: This key lacks permission for the selected model.`, `Claude API: The selected model is unavailable for this key.` (`:233-237`) |
| api | 429 | `Claude API: ` + 07's `rateLimitMessage(headers, mode)` (`:169-179`) |
| api | refresh needs interaction | `Claude API: Sign in with your Microsoft account to use Claude.` (the cause chain check, `:214-220`) |
| api | no credential at all (key path) | `Claude API: Add an Anthropic API key in Settings to use Claude.` or the Microsoft wording |

(When the request id is absent the ` (request-id <id>)` piece is absent too; the 401 text then ends `...History.`)

### 2.15 API key storage

`src/main/secrets.ts`:

- File `%APPDATA%\shotAI\secrets.json` (`userData`), shape `{ "apiKey": "<base64 of safeStorage ciphertext>" }`; never in `settings.json`, never logged, never sent to the renderer (`secrets.ts:1-9`, `:17-24`).
- `readSecrets()`: read and `JSON.parse`; `apiKey` kept only when a string; any failure gives `{}` (`:26-34`).
- `writeSecrets(s)`: `writeFileAtomic(file, JSON.stringify(s), { mode: 0o600, onRetry })`, retry log `` `secrets rename ${code} \u2014 retrying (lock likely transient)` `` (`:36-45`).
- `decryptStored(b64)`: if encryption unavailable, `null`; else `decryptString(base64 decode)`, `trim`, `null` if empty; a throw logs warn `secrets: stored key could not be decrypted` and gives `null` (`:47-57`).
- `envKey()`: `process.env.ANTHROPIC_API_KEY?.trim()`, `null` if empty (`:59-62`).
- `getApiKey()`: stored and decryptable wins, else the env key, else `null` (`:64-75`).
- `getApiKeyStatus()` (`:77-92`): `encryptionAvailable = isEncryptionAvailable()`; `hasStoredCiphertext = !!apiKey` (ciphertext present on disk whether or not it decrypts, so the UI can offer Clear for a key stranded by a machine move or a DPAPI change). Then: decryptable stored key gives `{ hasKey: true, source: 'stored', ... }`; else env key gives `{ hasKey: true, source: 'env', ... }`; else `{ hasKey: false, source: 'none', ... }`.
- `setApiKey(key)` (`:94-106`): `trimmed = key.trim()`; empty throws `API key is empty.`; encryption unavailable throws `Secure storage is unavailable on this system, so the API key cannot be saved. Set the ANTHROPIC_API_KEY environment variable instead.`; else write `{ apiKey: base64(encryptString(trimmed)) }`, log info `API key saved (encrypted).`
- `clearApiKey()` (`:108-112`): write `{}` (the file is kept, emptied); log info `Stored API key cleared.` The env fallback still applies.
- The IPC channel `claude:set-key` validates the argument is a string (`ipc.ts:83`, `:774-780`) and logs the channel name only.

REQUIRED, with the storage primitive and file name changed natively (7.11).

### 2.16 What the UI may learn

`AuthStatus` (`src/shared/ipc.ts:53-79`), the renderer's whole auth vocabulary together with the three `auth:*` verbs. Deliberately absent: `expiresAt`, scope, request ids, any token prefix, any JWT claim.

| Field | Type | Meaning |
|---|---|---|
| `mode` | `'federated' | 'apiKey' | 'none'` | the credential that would be used now |
| `federationAvailable` | bool | a valid config resolved (false for every external user: say nothing about Entra) |
| `signedIn` | bool | a usable Entra account is cached |
| `account` | string or null | the account's `username` (UPN) for "Signed in as". Personal data: never logged, never in diagnostics or exports |
| `encryptionAvailable` | bool | mirrors `ApiKeyStatus` |
| `hasStoredCiphertext` | bool | mirrors `ApiKeyStatus` |
| `supportUrl` | string or null | `null` when federation is unavailable; else the configured `SupportUrl` or `DEFAULT_SUPPORT_URL` |

The `auth:status` handler (`ipc.ts:787-816`), exactly:

1. `invalidateFederationConfig()`.
2. `cfg = await auth.federation()`; `entra = cfg ? await auth.entra() : null`; `account = entra ? await entra.signedInAccount() : null`; `key = await getApiKeyStatus()`; `signedIn = !!account`.
3. Return `{ mode: signedIn ? 'federated' : key.hasKey ? 'apiKey' : 'none', federationAvailable: !!cfg, signedIn, account: account?.username ?? null, encryptionAvailable: key.encryptionAvailable, hasStoredCiphertext: key.hasStoredCiphertext, supportUrl: cfg ? (cfg.supportUrl ?? DEFAULT_SUPPORT_URL) : null }`.

`ApiKeyStatus` `{ hasKey, source: 'stored' | 'env' | 'none', encryptionAvailable, hasStoredCiphertext }` (`src/shared/ipc.ts:81-91`). `TestConnectionResult` (2.14). Nothing that crosses the boundary returns a token, an assertion or a claim; `auth:sign-in` discards the interactive token (`ipc.ts:817-826`). REQUIRED [SECURITY].

Verbs (`src/shared/ipc.ts:236-241`, `:522-548`; `ipc.ts:817-834`):

| Verb | Behavior |
|---|---|
| `auth:status` | 2.16 above |
| `auth:sign-in` | `entra = await appAuth().entra()`; `null` throws `shotAI is not set up for Microsoft sign-in on this machine.`; else `await entra.signInInteractive()`, token discarded. Interactive EVERY time, by design: after IT grants the role, a silent retry re-serves a cached 60 to 90 minute Entra token that predates the assignment and carries no `roles` claim, which reads as a broken rule (`Settings.tsx:186-189`, `src/shared/ipc.ts:525-532`). |
| `auth:sign-out` | `entra?.signOut()`. A stored API key is deliberately untouched (`ipc.ts:827-834`). |
| `claude:key-status`, `claude:set-key`, `claude:clear-key` | 2.15 |
| `claude:test-connection` | 2.14 |

### 2.17 The Request access link and the allowlist

`shell:open-external` (`ipc.ts:271-310`, base rules owned by 10): https and host `anthropic.com`, `*.anthropic.com` or exactly `github.com`. The SupportUrl extension (`ipc.ts:288-303`): only when the base rule refused AND the protocol is `https:`, read `cfg = await getFederationConfig()` (cached, not invalidated); if `cfg.supportUrl` is set, allow when `new URL(cfg.supportUrl).origin === parsed.origin` (a malformed configured URL never widens the list). Exact ORIGIN (scheme, host, port), so it cannot widen to sibling hosts or subdomains. A refusal logs warn `` `refused openExternal for non-allowlisted URL: ${parsed.origin}` `` in the `ipc` log category, not `claude` (`ipcLog.warn`, `ipc.ts:304-307`; 10 owns the line). The SupportUrl lookup runs only for an `https:` candidate that the base rule refused, so a non-https candidate never triggers a policy read. Without this the link would be silently refused, which is exactly how the v1.1.6 update-check download link failed (`config-validate.ts:35-39`, commit `f1b24a8`).

`DEFAULT_SUPPORT_URL = 'https://github.com/Armadillon44/shotAI/issues'` (`config-validate.ts:78-82`), already allowlisted via `github.com`, so no organization URL is baked into the public repo. REQUIRED [SECURITY].

### 2.18 The ADMX/ADML contract

`Intune/Windows/shotAI.admx`: namespace target prefix `shotAI`, namespace `LFI.Policies.shotAI`; one policy `FederationConfig`, `class="Machine"`, `key="SOFTWARE\Policies\shotAI\Federation"`, category `shotAI` then `Federation`; eight `<text>` elements whose `id` equals `valueName`: `TenantId` (required, `maxLength="64"`), `AudienceAppId` (required, 64), `FederationRuleId` (required, 128), `OrganizationId` (required, 64), `ServiceAccountId` (required, 128), `ClientAppId` (64), `WorkspaceId` (128), `SupportUrl` (512). Text elements write `REG_SZ` (`shotAI.admx:28-67`). The ADML supplies every `$(string.*)` and `$(presentation.*)` reference, display name `Configure Microsoft Entra sign-in for Claude` under `shotAI` then `Authentication`, supportedOn `shotAI 1.2 and later, Windows`, a help text that promises reopen-Settings pickup, and one `textBox` per element (`shotAI.adml:1-85`). Import routes: Settings catalog custom ADMX import, the OMA-URI `./Device/Vendor/MSFT/Policy/ConfigOperations/ADMXInstall/shotAI/Policy/shotAIFederation`, or a Group Policy Central Store (`Intune/Windows/README.md:52-68`). The files do not change natively (fixed decision). REQUIRED.

Observation (no behavior depends on it): the ADMX references only `SUPPORTED_shotAI_Federation`, `Cat_shotAI`, `Cat_Federation`, `FederationConfig`, `FederationConfig_Explain` and `presentation.FederationConfig`. The per-value `<string>` entries (`TenantId`, `TenantId_Help` and the other fourteen, sixteen in all) are not referenced by any ADMX element (a `<text>` element has no `explainText`), so the Group Policy editor shows only the policy explain text and the eight textBox labels; the per-value help text is invisible in the editor (`shotAI.adml:32-54`, EDGE-AUTH-41, Q-AUTH-18). The ADMX contract test does not detect this because it checks references in one direction only (ADMX to ADML).

### 2.19 The probe

`scripts/wif-probe.mjs` imports the real modules and runs legs separately, naming the one that failed (`wif-probe.mjs:1-18`):

| Leg | What | Failure hints printed |
|---|---|---|
| 0 | `JSON.parse` the local file and pass it straight to `validateFederationConfig` (no `pickBaked`, no policy merge, so HKLM overrides are NOT exercised and the probe tests the baked file only); a missing file prints `` `${LOCAL} not found. Copy federation.example.json and fill it in.` `` and exits 1; print tenant, audience (with ` (also the client)` when equal), rule and workspace (`(rule default)` when absent) | `` `config rejected \u2014 missing: [${missing.join(', ')}] invalid: [${invalid.join(', ')}]` `` then exit 1 |
| 1 | interactive sign-in through `createAuth(...).entra()` with a counting fetch; prints the scope, which client, whether the authorize URL handed to the browser still carries `scope` (the truncation check), `upn/preferred_username`, the claims `aud` (must be the bare audience GUID), `tid` (must match), `iss` (must end `/v2.0`), lifetime `exp - iat` (over 3600 s and accepted means the issuer `max_jwt_lifetime_seconds` is not at its 1 hour default), assertion size `(bytes / 1024).toFixed(1)` KiB against 16 KiB, the role check (true, false with the admin-account caveat, or unparsable); asserts MSAL used the injected network module (`[FAIL]` if the counter is 0) | `AADSTS50011` redirect URI not registered; `AADSTS7000218` enable public client flows; `AADSTS65001` or consent prompt: client not authorized for the scope; `AADSTS650057` audience does not pre-authorize the client |
| 2 | `exchangeAssertion` over the global `fetch` (not the counting one); prints `expires_in`, scope, first 14 characters of the token; `twoXjwt = floor((exp * 1000 - now) * 2 / 1000)`: when `expires_in <= twoXjwt` the rule bound the mint, and then `expires_in <= 600` adds the Console wizard prefill note; otherwise the JWT bound it; a scope other than `workspace:inference` is flagged | the exchange error; when the role check said `false`, a note that it already predicted this |
| 3 | `new Anthropic({ baseURL, authToken: minted.token, apiKey: null }).models.retrieve('claude-sonnet-5')` | a 403 after a successful exchange means the rule scope is too narrow |
| 4 | the production path: `makeClient()` must choose `federated`; two `models.retrieve` calls; the second must cause 0 extra calls through the counting fetch (which counts MSAL traffic and exchanges alike); prints `second call re-used the cached token (0 extra exchanges)` | any other count is `[FAIL]` (the TokenCache is not engaged) |

Memory-only cache (every run is a fresh sign-in, sidestepping the stale-roles trap); `getApiKey: async () => null` so a stray key cannot decide the test; `WIF_PROBE_CLI_CLIENT=1` swaps in the Azure CLI's well-known public client (`AZURE_CLI_CLIENT_ID`, `wif-probe.mjs:37`) to tell "our registration is wrong" from "the rule rejected us"; the browser is opened with `rundll32 url.dll,FileProtocolHandler` because `cmd /c start` truncates at `&` (AADSTS900144) (`wif-probe.mjs:29-46`, `:97-123`). A pass proves acceptance, never rejection: a `tid`-only rule accepts everyone, so discrimination needs a run with a non-admin unassigned account (`wif-probe.mjs:235-242`).

### 2.20 Log lines owned here

All in the `claude` log category (10) except the `openExternal` refusal, which is `ipc` (and natively 10's). Values never appear; names, statuses, durations, request ids and model ids may. Note that `connection test failed (mode=...): ${error}` embeds the user-facing error text, which on the key path can be a raw MSAL or SDK message.

| Line (exact) | Level | Citation |
|---|---|---|
| `federation: configured.` | info | `config.ts:55` |
| `` `federation: config rejected, falling back to API key (missing: ${m || 'none'}; invalid: ${i || 'none'}).` `` with `m`, `i` comma-joined names | warn | `config.ts:62-66` |
| `entra: interactive sign-in completed.` | info | `msal.ts:154` |
| `entra: signed out.` | info | `msal.ts:161` |
| the five cache lines in 2.10 | info | `cache-plugin.ts` |
| `` `entra cache rename ${code}, retrying` `` | warn | `claude-auth.ts:54` |
| the two exchange lines in 2.11 | info | `federation.ts:127-131`, `:159` |
| the five connection test lines in 2.14 | info or warn | `claude-service.ts:269-301` |
| `secrets: stored key could not be decrypted` | warn | `secrets.ts:54` |
| `API key saved (encrypted).`, `Stored API key cleared.` | info | `secrets.ts:105`, `:111` |
| `` `secrets rename ${code} \u2014 retrying (lock likely transient)` `` | warn | `secrets.ts:43` |
| `` `refused openExternal for non-allowlisted URL: ${origin}` `` | warn, category `ipc` (10 owns it natively) | `ipc.ts:305` |
| `ipc: auth:status` and the other channel names | dev only | `ipc.ts:789`, ELECTRON-ONLY |

---

## 3. Constants

| Name | Value | Unit | Meaning | Citation |
|---|---|---|---|---|
| `POLICY_KEY` | `HKLM\SOFTWARE\Policies\shotAI\Federation` | registry path | the only managed source; 64-bit view | `config-sources.ts:25` |
| ADMX key attribute | `SOFTWARE\Policies\shotAI\Federation` with `class="Machine"` | | makes the policy HKLM | `shotAI.admx:47-51` |
| Registry view | 64-bit (`/reg:64`) | | never `Wow6432Node` | `config-sources.ts:67` |
| Honored value type | `REG_SZ` only | | every other type reads as absent | `config-sources.ts:45` |
| `FEDERATION_KEYS` | `TenantId`, `ClientAppId`, `AudienceAppId`, `FederationRuleId`, `OrganizationId`, `ServiceAccountId`, `WorkspaceId`, `SupportUrl` | ordered list | value names, frozen | `config-validate.ts:63-72` |
| Required set | `TenantId`, `AudienceAppId`, `FederationRuleId`, `OrganizationId`, `ServiceAccountId` | | missing or malformed fails closed | `config-validate.ts:145-158` |
| Optional, fails closed when malformed | `ClientAppId`, `WorkspaceId` | | | `config-validate.ts:150-166` |
| Optional, degrades | `SupportUrl` | | malformed is ignored | `config-validate.ts:170-172` |
| `GUID` | `/^[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}$/` | regex | tenant, audience, client, organization | `config-validate.ts:84` |
| `TAGGED.fdrl` | `/^fdrl_[0-9A-Za-z]+$/` | regex | federation rule | `config-validate.ts:88` |
| `TAGGED.svac` | `/^svac_[0-9A-Za-z]+$/` | regex | service account | `config-validate.ts:89` |
| `TAGGED.wrkspc` | `/^wrkspc_[0-9A-Za-z]+$/` | regex | workspace | `config-validate.ts:90` |
| Brace strip | `/^\{(.*)\}$/` replaced by `$1` | regex | GUID braces from Windows tooling | `config-validate.ts:101` |
| Reg query line | `/^\s+(\S+)\s+REG_SZ\s{2,}(.*)$/` over lines split by `/\r?\n/` | regex | ELECTRON-ONLY parser | `config-sources.ts:41-45` |
| Baked input file | `src/main/entra/federation.local.json` (gitignored) | path | build-time values | `vite.main.config.ts:15`, `.gitignore:111` |
| Baked filter | drop keys starting `_`, non-strings, blank after trim; trim the rest | | | `vite.main.config.ts:17-22` |
| `DEFAULT_SUPPORT_URL` | `https://github.com/Armadillon44/shotAI/issues` | URL | Request access fallback | `config-validate.ts:82` |
| `ANTHROPIC_BASE_URL` | `https://api.anthropic.com` | URL | pinned egress for API and exchange | `auth-core.ts:24`, `federation.ts:18` |
| `TOKEN_PATH` | `/v1/oauth/token` | path | exchange endpoint | `federation.ts:19` |
| `GRANT_TYPE_JWT_BEARER` | `urn:ietf:params:oauth:grant-type:jwt-bearer` | | RFC 7523 grant | `federation.ts:20` |
| `MAX_ASSERTION_BYTES` | `16384` (`16 * 1024`) | bytes, UTF-8 | local refusal threshold (`> 16384` refused) | `federation.ts:22-24` |
| Oversize KiB | `ceil(bytes / 1024)` | KiB | reported size | `federation.ts:89` |
| Minted expiry | `expiresAt = now_ms + expires_in * 1000` | ms | trusted server value, never a constant | `federation.ts:156-163` |
| Server mint lifetime | `min(rule token_lifetime_seconds, 2 x remaining JWT life)`, floor 60 | s | informational (server side) | `federation.ts:156-158`, `macOS:docs/SSO-WIF.md:283` |
| `REQUIRED_APP_ROLE` | `shotAI.User` | | advisory local check; the CEL rule enforces | `federation.ts:238` |
| Scope | `api://{audienceAppId}/user_impersonation` | | the one delegated scope | `msal.ts:63-65` |
| Authority | `https://login.microsoftonline.com/{tenantId}` | URL | tenant-specific | `msal.ts:77` |
| `clientCapabilities` | not set (never `CP1`) | | CAE would extend tokens to 28 h | `msal.ts:84-87` |
| Entra access token lifetime | 60 to 90 minutes typical (measured 68, 71, 94 min on the live tenant) | min | the stale-roles window | `docs/MANAGED-CONFIG.md:176-181`, commits `850abc1`, `992398f`, `568aaab` |
| SDK advisory refresh | `exp - 120` | s | background refresh, serve stale | `auth-core.ts:164-166`; `anthropic-sdk-csharp:src/Anthropic/Credentials/CredentialsConstants.cs:15` |
| SDK mandatory refresh | `exp - 30` | s | blocking refresh | same; `anthropic-sdk-csharp:src/Anthropic/Credentials/CredentialsConstants.cs:16` |
| SDK advisory backoff (C#) | `5` | s | skip a background refresh within 5 s of a failed one | `anthropic-sdk-csharp:src/Anthropic/Credentials/CredentialsConstants.cs:17` |
| SDK token exchange beta (C#) | `oauth-2025-04-20,oidc-federation-2026-04-01` | header value | sent by `WorkloadIdentityCredentials` | `anthropic-sdk-csharp:src/Anthropic/Credentials/CredentialsConstants.cs:5-11` |
| SDK API request beta (C#) | `oauth-2025-04-20` | header value | appended when token credentials are used | `anthropic-sdk-csharp:src/Anthropic/Credentials/CredentialsConstants.cs:10` |
| Entra cache file (Electron) | `%APPDATA%\shotAI\entra-cache.bin` | path | ELECTRON-ONLY name | `claude-auth.ts:44` |
| Secrets file (Electron) | `%APPDATA%\shotAI\secrets.json`, `{ "apiKey": base64 }` | path | ELECTRON-ONLY name | `secrets.ts:17-24` |
| Entra cache file (native) | `%LOCALAPPDATA%\LFI\shotAI\entra\msal-cache.bin` (`IAppPaths.LocalDataDirectory` + `entra\msal-cache.bin`), plus the extension's lock file | path | never `%LOCALAPPDATA%\shotAI\`, which is the Squirrel install root the Electron uninstall deletes (EDGE-PKG-22) | ARCHITECTURE 10.1, 10.2, R-ARCH-13 |
| Secrets file (native) | `%APPDATA%\shotAI\secrets.dpapi.json` (`IAppPaths.UserDataDirectory` + `secrets.dpapi.json`), `{ "apiKey": base64 }` | path | beside `settings.json`, never Electron's `secrets.json` (EDGE-AUTH-33) | ARCHITECTURE 10.1 |
| Host guard rule | scheme `https`, host `api.anthropic.com` (ordinal ignore case), port `443` | | every Anthropic request, API clients and the exchange client (INV-AUTH-35) | 07 7.6 rule adopted by R-ARCH-15 |
| File mode | `0o600` | POSIX mode | best effort; ignored on Windows | `secrets.ts:36-41`, `claude-auth.ts:52-53` |
| Env fallback | `ANTHROPIC_API_KEY` (trimmed, read-only) | env var | dev and CI key | `secrets.ts:59-62` |
| Loopback success text | `Signed in to shotAI. You can close this tab and return to the app.` | | plain text | `msal.ts:150` |
| Loopback error text | `shotAI sign-in failed. Close this tab and try again from the app.` | | plain text | `msal.ts:151` |
| ADMX `maxLength` | `64` (TenantId, AudienceAppId, OrganizationId, ClientAppId), `128` (FederationRuleId, ServiceAccountId, WorkspaceId), `512` (SupportUrl) | chars | policy editor limit | `shotAI.admx:56-64` |
| Probe model | `claude-sonnet-5` | model id | legs 3 and 4 | `wif-probe.mjs:204`, `:220`, `:226` |
| SDK environment inputs (C#) | `ANTHROPIC_BASE_URL`, `ANTHROPIC_API_KEY`, `ANTHROPIC_AUTH_TOKEN`, `ANTHROPIC_PROFILE`, `ANTHROPIC_CONFIG_DIR`, `ANTHROPIC_IDENTITY_TOKEN_FILE`, `ANTHROPIC_IDENTITY_TOKEN`, `ANTHROPIC_FEDERATION_RULE_ID`, `ANTHROPIC_ORGANIZATION_ID`, `ANTHROPIC_SERVICE_ACCOUNT_ID`, `ANTHROPIC_WORKSPACE_ID`, `ANTHROPIC_CUSTOM_HEADERS` (plus `XDG_CONFIG_HOME` for the profile directory) | env vars | every variable the SDK consults for host, credential or headers; INV-AUTH-15, INV-AUTH-35 | `anthropic-sdk-csharp:src/Anthropic/Credentials/CredentialsConstants.cs:27-36`, `Core/ClientOptions.cs:126-128`, `:199`, `:211-213`, `Core/ParamsBase.cs:34-45`, `Credentials/ConfigPaths.cs:10-39` |
| SDK exchange response rules (C#) | `token_type`, when present, must equal `Bearer` (ordinal ignore case); `expires_in` absent means no expiry; a JSON number is truncated to whole seconds; a JSON string must parse with `long.TryParse`; anything else throws | | stricter than the diagnostic leg (EDGE-AUTH-40) | `anthropic-sdk-csharp:src/Anthropic/Credentials/SecurityHelpers.cs:126-170` |

User-visible strings produced by this subsystem (the UI copy that frames them is 06's, section 2.25):

| Id | Text (exact) | Citation |
|---|---|---|
| S1 | `Sign in with your Microsoft account to use Claude.` | `msal.ts:32`, `auth-core.ts:149` |
| S2 | `Add an Anthropic API key in Settings to use Claude.` | `auth-core.ts:150` |
| S3 | `shotAI is not set up for Microsoft sign-in on this machine.` | `auth-core.ts:114`, `ipc.ts:821` |
| S4 | `No Microsoft sign-in token to exchange.` | `federation.ts:83` |
| S5 | `` `Sign-in token is ${Math.ceil(bytes / 1024)} KiB; Anthropic rejects assertions over 16 KiB.` `` | `federation.ts:89` |
| S6 | `` `Could not reach Anthropic to sign in (${message}).` `` | `federation.ts:116` |
| S7 | `Anthropic returned an unreadable sign-in response.` | `federation.ts:140` |
| S8 | `Anthropic sign-in response was missing access_token or expires_in.` | `federation.ts:150` |
| S9 | 401: `Anthropic refused the sign-in. Your account may not be assigned the shotAI role yet. Administrators: the deny reason is in Claude Console > Settings > Workload identity > History` + rid + `.` | `federation.ts:180-186` |
| S10 | 400: `shotAI's federation settings are invalid (rule, organization, or workspace id).` + rid | `federation.ts:191-195` |
| S11 | 429: `Anthropic is rate limiting sign-in right now. Try again shortly.` + rid | `federation.ts:200-204` |
| S12 | 5xx: `` `Anthropic sign-in is temporarily unavailable (HTTP ${status}).` `` + rid | `federation.ts:207-211` |
| S13 | other: `` `Anthropic sign-in failed (HTTP ${status}).` `` + rid | `federation.ts:213` |
| S14 | rid: `` ` (request-id ${requestId})` `` (leading space), or empty | `federation.ts:170` |
| S15 | `AI SOP generation is turned off.` | `claude-service.ts:258` |
| S16 | `API key is empty.` | `secrets.ts:97` |
| S17 | `Secure storage is unavailable on this system, so the API key cannot be saved. Set the ANTHROPIC_API_KEY environment variable instead.` | `secrets.ts:99-101` |
| S18 | `Microsoft sign-in is not fully set up for shotAI: the app registration is missing the Windows sign-in redirect URI (ms-appx-web://microsoft.aad.brokerplugin/ followed by the app's client id). Ask your administrator to add it under Mobile and desktop applications.` followed by ` Details: ` + MSAL's `Message` | native only (IMPROVEMENT D23, 7.5); no Electron counterpart, because the Electron build never used the broker redirect URI |

---

## 4. Invariants

**INV-AUTH-1 [SECURITY]. Federation config is read from `HKLM\SOFTWARE\Policies\shotAI\Federation` in the 64-bit view and from the baked build values, and from nowhere else.** No HKCU, no environment variable, no file beside the exe, no `settings.json` field, no command-line switch. Why: HKLM is the integrity boundary (only an administrator or MDM can write it), so a standard user cannot repoint their client at another tenant or rule (#63, `config-sources.ts:17-24`); a file beside the exe was rejected because it is user-writable under a per-user install and destroyed on update (commit `70ce4a7`). Citation: `config-sources.ts:25`, `:61-72`. Test: `RegistryPolicySourceTests.ProductionReaderTargetsHklmPolicyKeyIn64BitView` (Windows) and `FederationConfigProviderTests.OnlyBakedAndPolicySourcesContribute` (Core, env vars set to conflicting values have no effect).

**INV-AUTH-2. Only `REG_SZ` values whose name is exactly one of `FEDERATION_KEYS` (ordinal, case-sensitive) are read; everything else under the key is absent.** Why: a wrong-typed value must read as absent rather than arrive mangled; an unrelated value must not inject a field; during coexistence the Electron and native builds must resolve the same config from the same key. Citation: `config-sources.ts:28-54`. Test: `PolicyValueParserTests` (types `ExpandString`, `MultiString`, `DWord`, `QWord`, `Binary`, `Unknown` ignored; `tenantid` ignored; `Unrelated` ignored); `RegistryPolicySourceTests.EnumeratesRealValueKinds` (Windows, against a scratch HKCU key through the internal test constructor).

**INV-AUTH-3. A policy read failure of any kind (key absent, access denied, I/O error, non-Windows) yields "nothing delivered", never an exception and never "federation off".** Why: no policy is the default state of every machine; a transient failure must degrade to the baked values (commit `1ecebd0`). Citation: `config-sources.ts:57-72`, `ipc.ts:795-797`. Test: `FederationConfigProviderTests.PolicyReadFailureFallsBackToBaked`.

**INV-AUTH-4. Merge is per key and policy wins only with a non-blank string; a blank policy value never clears a baked one.** Why: a half-cleared ADMX key must not disable federation for the fleet (#63). Citation: `config-validate.ts:198-218`. Test: `FederationSourcesTests.Merge*` (ports `config-sources.test.ts:133-150`).

**INV-AUTH-5 [SECURITY]. Validation is all-or-nothing: any missing or malformed required value, or a present-and-malformed `ClientAppId` or `WorkspaceId`, rejects the whole config and the app behaves as unconfigured (BYO key).** It never returns a partial config. `SupportUrl` alone degrades to absent. Why: a half-configured federation fails opaquely at exchange time (the 401 names no cause) and would be misdiagnosed as an entitlement problem (#63, commit `513a6d0`). Citation: `config-validate.ts:1-13`, `:128-186`. Test: `FederationConfigValidatorTests` (every case in 8.5).

**INV-AUTH-6. `missing` and `invalid` contain value NAMES only, in validation order, and no value ever reaches a log line, an exception message, a UI string or a diagnostics bundle.** Why: the values are org-identifying and the rejection line is meant to be attached to support tickets (`config.ts:59-61`). Citation: `config-validate.ts:43-46`, `config.ts:62-66`. Test: `FederationConfigProviderTests.RejectedConfigLogsNamesNeverValues` (the captured log contains `TenantId` and not the GUID).

**INV-AUTH-7. Blank means unset everywhere: a value that normalizes to empty counts as missing (not invalid), and a config whose eight values all normalize to empty is `unconfigured`.** Why: a cleared ADMX setting must revert cleanly to BYO key rather than read as an error. Citation: `config-validate.ts:94-104`, `:188-196`. Test: `FederationConfigValidatorTests.BlankRequiredValueIsMissingNotInvalid` (theory over the five), `IsUnconfigured*`.

**INV-AUTH-8. Unconfigured and rejected both produce `null` config; only a rejection is logged (warn), and an unconfigured machine shows no Entra UI anywhere.** Why: every external user is unconfigured; a greyed-out SSO control in a public repo is a permanent support-question generator; an administrator must see a rejection (`Settings.tsx:381-386`, commit `f1b24a8`). Citation: `config.ts:46-69`. Test: `FederationConfigProviderTests.UnconfiguredLogsNothing`, `.RejectedLogsOneWarning`; `AuthStatusServiceTests.UnconfiguredStatusHasNoFederationAndNullSupportUrl`.

**INV-AUTH-9. Reading auth status drops the resolved-config cache before resolving, so opening Settings or a project picks up a policy correction without a restart; no other path invalidates.** Why: the ADML help text, `Intune/Windows/README.md` and `docs/MANAGED-CONFIG.md` promise it; it once shipped uncalled (commit `1ecebd0`). Citation: `ipc.ts:787-800`, `config.ts:39-43`, `:74-82`. Test: `AuthStatusServiceTests.GetStatusInvalidatesFederationCache` (a counting policy source is read once per `GetStatusAsync` and zero times by a second `CreateClientAsync`), plus `AdminDocConsistencyTests.NoRestartPromiseRequiresInvalidation`.

**INV-AUTH-10. Nothing in this subsystem runs at app launch: no registry read, no DPAPI call, no MSAL construction, no network.** Why: capture, editing and export are local-first; gating them on identity would break that claim (`claude-auth.ts:19-26`). Citation: `claude-auth.ts:27-66`. Test: `CompositionTests.AuthServicesAreLazy` (resolving the App's startup graph touches no auth source; counters stay at zero).

**INV-AUTH-11. The Entra scope is exactly `api://{AudienceAppId}/user_impersonation`, the authority is `https://login.microsoftonline.com/{TenantId}`, and client capabilities are never declared.** Why: `.default` can yield an id_token and an opaque 401; `common` breaks the WIF rule's `tid` pin; `CP1` makes CAE extend the token past the issuer's maximum JWT lifetime so every exchange fails (#63, commit `850abc1`). Citation: `msal.ts:60-89`. Test: `EntraSessionTests.ScopeIsAudienceUserImpersonation`; `MsalGatewayTests.BuilderUsesTenantAuthorityAndNoClientCapabilities` (Windows, inspects `IPublicClientApplication.AppConfig`).

**INV-AUTH-12 [SECURITY]. Silent acquisition never prompts, and the identity token provider handed to the Anthropic SDK only ever calls silent acquisition.** Why: the SDK refreshes in the background (C# `TokenCache` runs the provider on a thread-pool task with `CancellationToken.None`), where a prompt would appear with no user context (`auth-core.ts:171-173`; WAM guidance: "Invoke authentication based on user action"). Citation: `msal.ts:102-129`, `auth-core.ts:170-183`. Test: `EntraIdentityTokenProviderTests.NeverCallsInteractive` (fake gateway records calls; interaction-required becomes `SignInRequiredException`).

**INV-AUTH-13. Interactive sign-in happens only on a user action (Sign in with Microsoft, Sign in again) and every such click is interactive, never a silent retry.** Why: after IT grants the role, a silent retry re-serves a cached 60 to 90 minute token that predates the grant and carries no `roles` claim, which looks exactly like a broken rule (#63, `docs/MANAGED-CONFIG.md:176-181`). Citation: `ipc.ts:817-826`, `Settings.tsx:181-197`. Test: `AuthServiceTests.SignInIsAlwaysInteractive`; manual AC-AUTH-27.

**INV-AUTH-14 [SECURITY]. The credential decision is exactly the 2.13 table: signed in wins and the key is not read; signed out falls back to the stored-then-env key; neither refuses with S1 when federation is configured and S2 when it is not.** Why: a rollout must strand nobody; an external user must never be told to sign in with Microsoft; a leftover key must not shadow SSO (#63, `auth-core.test.ts:29-61`). Citation: `auth-core.ts:138-152`. Test: `AuthServiceModeSelectionTests` (the five rows).

**INV-AUTH-15 [SECURITY]. Every Anthropic client is built with `BaseUrl = "https://api.anthropic.com"` set explicitly, `ApiKey` and `AuthToken` set explicitly (`ApiKey = key` or `null`, `AuthToken = null`), and, when federated, `Credentials` set; no environment variable (`ANTHROPIC_BASE_URL`, `ANTHROPIC_API_KEY`, `ANTHROPIC_AUTH_TOKEN`, `ANTHROPIC_PROFILE`, `ANTHROPIC_CONFIG_DIR`, `ANTHROPIC_FEDERATION_RULE_ID`, `ANTHROPIC_ORGANIZATION_ID`, `ANTHROPIC_SERVICE_ACCOUNT_ID`, `ANTHROPIC_IDENTITY_TOKEN`, `ANTHROPIC_IDENTITY_TOKEN_FILE`, `ANTHROPIC_WORKSPACE_ID`, and `ANTHROPIC_CUSTOM_HEADERS` per INV-AUTH-35) and no SDK profile file can change the host, the credential or the workspace.** The one deliberate exception is shotAI's own read of `ANTHROPIC_API_KEY` as the key fallback in `ApiKeyStore` (2.15), which is passed to the SDK explicitly. Why: a poisoned environment could redirect the credential and the screenshots; a leftover key could disable federation; the C# SDK auto-resolves env and profile credentials whenever `Credentials` is null and neither `ApiKey` nor `AuthToken` was set explicitly (`anthropic-sdk-csharp:src/Anthropic/AnthropicClient.cs:170-209`) and defaults `BaseUrl` from `ANTHROPIC_BASE_URL` (`anthropic-sdk-csharp:src/Anthropic/Core/ClientOptions.cs:126-144`). Citation: `auth-core.ts:21-24`, `:154-161`. Test: `AnthropicClientFactoryTests.EnvironmentCannotRedirectOrReplaceCredential` (non-parallel collection; sets every variable above; asserts the captured request host, the `Authorization` or `x-api-key` value, and the absence of the other header).

**INV-AUTH-16. The federated identity token provider passes `FederationRuleId`, `OrganizationId`, `ServiceAccountId` and `WorkspaceId` (null when absent) from the validated config, with the exchange pinned to the same base URL, and never schedules its own refresh timer.** Why: the SDK's token cache owns advisory and mandatory refresh, coalescing and the 401 retry, which is correct across suspend and clock jumps; a timer is not (`auth-core.ts:164-169`, `macOS:Packages/EntraKit/Sources/EntraKit/FederatedCredentialProvider.swift:11-14`). Citation: `auth-core.ts:170-183`. Test: `AnthropicClientFactoryTests.FederatedClientWiresAllIdsAndReusesToken` (fake handler: one exchange for two API calls; body carries the four ids; `workspace_id` absent when not configured).

**INV-AUTH-17. The connection test runs its legs in order (sign-in, exchange, API), stops at the first failure, names that leg, and never fails on the advisory role check.** Why: the 401 is opaque by design; the legs are the only way to tell "not signed in" from "not assigned the role" from "scope or model" (#63, commit `d79bc3b`). Citation: `auth-core.ts:107-136`, `claude-service.ts:256-304`. Test: `ConnectionTesterTests` (each row of the 2.14 step table, plus "exchange leg never reported before sign-in passed").

**INV-AUTH-18 [SECURITY]. The exchange's response body is never shown, logged or attached to an error; only its length is logged.** Why: the body can echo request fields (organization id) back (`federation.ts:123-131`). Citation: `federation.ts:63-69`, `:123-133`. Test: `FederationExchangeTests.NeverLeaksResponseBody` (port of `federation.test.ts:175-185`), `.LogsBodyLengthOnly`.

**INV-AUTH-19 [SECURITY]. The assertion, the Entra access token, the refresh token, the minted `sk-ant-oat01-` token and the API key are never logged, never written to `settings.json`, diagnostics or exports, and never reach a view model.** The minted token is never written to disk at all. Why: they are bearer credentials (`cache-plugin.ts:9-19`, `secrets.ts:1-9`). Citation: `federation.ts:55`, `:77`, `secrets.ts:64-75`. Test: `FederationExchangeTests.LogsNeverContainAssertionOrToken`; `MintedTokenTests.ToStringRedactsToken`; `AuthFacadeSurfaceTests.NoMemberReturnsSecretMaterial` (reflection over the UI-facing interfaces).

**INV-AUTH-20. The exchange refuses locally, without a request, an empty assertion (S4) and one over 16384 UTF-8 bytes (S5).** Why: an oversized token must report its real size instead of an opaque server refusal (#63). Citation: `federation.ts:81-91`. Test: `FederationExchangeTests.RefusesEmptyWithoutRequest`, `.RefusesOversizeWithRealSize` (boundaries 16384 accepted, 16385 refused and reported `17 KiB`).

**INV-AUTH-21. A transport failure is reported as unreachable (S6), never as a refusal; 401, 400, 429, 5xx and other statuses map to S9 to S13 exactly, with the request id appended as S14 when present.** Why: users re-check their role for what is a proxy problem otherwise; a 400 has no History entry so the admin must not be sent there. Citation: `federation.ts:112-118`, `:168-214`. Test: `FederationExchangeTests` failure cases.

**INV-AUTH-22. `expires_in` is trusted and validated (finite, `> 0`, via JavaScript `ToNumber` semantics) and `access_token` must be a non-empty string; otherwise S8.** Why: the mint can legitimately be short (2x remaining JWT life); a constant would over-trust a stale token. Citation: `federation.ts:145-165`. Test: `FederationExchangeTests.RejectsMissingOrBadExpiry` (`{}`, `{access_token}`, `expires_in: 0`, `expires_in: "soon"`), `.AcceptsNumericStringExpiry` (`"600"`).

**INV-AUTH-23. The role check is advisory and fail-open: `true` or `false` only from a parsable payload, `false` when `roles` is absent or not an array, `null` when unparsable; nothing gates on it.** Why: the CEL rule is the authority; a parse bug must not lock out a correctly assigned user. Citation: `federation.ts:216-235`, `claude-service.ts:272-277`. Test: `JwtRolesTests` (ports `federation.test.ts:197-225`); `ConnectionTesterTests.RoleFalseAfterSuccessfulExchangeStillPasses`.

**INV-AUTH-24. A sign-in requirement is recognized anywhere in an exception's cause chain.** Natively: `SignInRequiredException` may arrive directly (from `CreateClientAsync`) or wrapped by the SDK as `WorkloadIdentityException("Failed to obtain identity token.", inner)` (`anthropic-sdk-csharp:src/Anthropic/Credentials/WorkloadIdentityCredentials.cs:62-75`), possibly inside `AggregateException`. 07's `SopErrorMapper.Map` must walk `InnerException` (and `AggregateException.InnerExceptions`) and return the `SignInRequiredException.Message`. Every other `WorkloadIdentityException` goes through `FederationErrorText.ForSdk` (7.7): a status code maps to S9 to S13 (no request id is available from the SDK); a direct `HttpRequestException` or timeout `OperationCanceledException` inner maps to S6; a direct `JsonException` inner, and every response-shape failure without an inner exception, maps to S7; any other inner exception is an identity token provider failure and shows that inner exception's `Message` (parity with Electron's raw-message fallback). The SDK's own `Message` (which embeds a redacted body and SDK configuration advice such as the `ANTHROPIC_WORKSPACE_ID environment variable`) is never shown. This mapping matters in production and not only at Test connection: when the minted token expires after a role removal, or when the forced refresh after an API 401 fails, the SDK throws the exchange's `WorkloadIdentityException` instead of the API's 401 (`anthropic-sdk-csharp:src/Anthropic/AnthropicClient.cs:431-442`, EDGE-AUTH-39). Why: offline must not look like a sign-in prompt, and a sign-in prompt must not look like an API error (`claude-service.ts:203-221`). Test: `FederationErrorTextTests` (Core) and 07's error-message tests.

**INV-AUTH-25 [SECURITY]. The token cache is never written in plaintext; when OS encryption cannot be verified the session is memory-only.** Why: the cache holds the refresh token (or, under WAM, the account and access tokens) (`cache-plugin.ts:100-106`). Citation: `cache-plugin.ts:56-65`, `:100-106`. Test: `TokenCachePersistenceTests.UnverifiablePersistenceMeansMemoryOnly` (Core decision logic with a fake that throws from `VerifyPersistence`); code review rule: `WithUnprotectedFile` never appears (`SourceScanTests.NoUnprotectedMsalCache`).

**INV-AUTH-26. A token cache that is absent, torn or undecryptable is a cache miss (signed out), never an error, and a failed cache write never fails the sign-in that caused it.** Why: one interactive sign-in self-heals it; "app broken" is the wrong failure (`cache-plugin.ts:88-112`). Test: `MsalCachePersistenceTests.GarbageCacheFileIsAMiss`, `.ReadOnlyCacheFileDoesNotFailSignInBookkeeping` (Windows).

**INV-AUTH-27 [SECURITY]. The API key is stored only DPAPI-encrypted (CurrentUser) in shotAI's own secrets file, is returned only to the client factory, and the UI learns only `ApiKeyStatus`.** The key input is never pre-filled because the key is never read back (06). Why: `secrets.ts:1-9`. Test: `ApiKeyStoreTests.FileNeverContainsPlaintext`, `AuthFacadeSurfaceTests`.

**INV-AUTH-28. `ApiKeyStatus` is computed exactly: stored and decryptable gives `stored`; else a non-blank trimmed `ANTHROPIC_API_KEY` gives `env`; else `none`; `hasStoredCiphertext` is true whenever a non-empty ciphertext string is on disk, even if it does not decrypt.** Why: the UI must be able to offer Clear for a stranded key instead of silently keeping it. Citation: `secrets.ts:77-92`. Test: `ApiKeyStoreTests.Status*` (truth table over stored-ok, stored-bad, absent x env set, unset).

**INV-AUTH-29. Saving a key trims it, refuses empty with S16 and refuses with S17 when encryption is unavailable, and writes atomically; clearing writes `{}` and leaves the env fallback in force; neither is optimistic.** Why: the UI must not claim a key is saved when it is not (the architecture's optimistic-write rule applies to project edits, not credentials). Citation: `secrets.ts:94-112`. Test: `ApiKeyStoreTests.Set*`, `.Clear*`.

**INV-AUTH-30. Sign-out removes every cached Entra account and leaves any stored API key untouched.** Why: signing out must not destroy the other credential (`ipc.ts:827-834`). Test: `AuthServiceTests.SignOutKeepsApiKey`.

**INV-AUTH-31 [SECURITY]. `AuthStatus` carries exactly the seven fields of 2.16 and nothing else: no expiry, scope, request id, token prefix or claim.** `Account` is the username (personal data) and never enters a log line. Why: the UI has no decision to make with them and a countdown is not worth leaking the token's lifetime shape (`src/shared/ipc.ts:53-60`). Test: `AuthStatusShapeTests.ExactlySevenProperties`; `LoggingTests.UsernameNeverLogged` (MSAL logging runs with PII off).

**INV-AUTH-32 [SECURITY]. The Request access URL is either `DEFAULT_SUPPORT_URL` or an administrator-delivered `https` `SupportUrl`, and the `openExternal` allowlist admits the latter by exact origin (scheme, host, port) only, only when federation is configured, and a malformed configured URL never widens the list.** Why: an admin-delivered URL is as trusted as the policy key, but must not widen to sibling hosts; without the extension the link is silently refused (the v1.1.6 failure). Citation: `ipc.ts:288-303`, `config-validate.ts:35-39`. Test: `SupportUrlAllowlistTests` (same origin allowed with any path; other port, `http`, sibling subdomain, parent domain refused; no config refused).

**INV-AUTH-33. The ADMX declares exactly the value names the app reads, marks exactly the five required values `required="true"`, writes the machine hive at the key the app reads, stays outside the blocked ingestion namespaces, and every ADML reference resolves with one textBox per element.** Why: drift in either direction is invisible to the compiler (#63, commit `49b0b98`). Citation: `admx-contract.test.ts`. Test: `AdmxContractTests` (Core, reads `Intune/Windows/` in place).

**INV-AUTH-34. At most one Entra account is cached at a time.** A successful interactive sign-in removes every other account from the cache. IMPROVEMENT (Electron keeps several and uses `getAllAccounts()[0]`, EDGE-AUTH-13). Why: "the signed-in account" must be the one the user just chose, deterministically. Test: `EntraSessionTests.InteractiveSignInLeavesExactlyOneAccount`.

**INV-AUTH-35 [SECURITY]. No process environment variable can add or alter an HTTP header on an Anthropic request.** The C# SDK reads `ANTHROPIC_CUSTOM_HEADERS` once per process in a static constructor and adds each `Name: value` line to every request before authentication headers are applied (`anthropic-sdk-csharp:src/Anthropic/Core/ParamsBase.cs:34-47`, `:242-256`). In key mode an injected `X-Api-Key` is added next to the real key; in either mode any other header (for example `anthropic-beta` or `anthropic-workspace-id`) reaches the wire. The native app therefore clears `ANTHROPIC_CUSTOM_HEADERS` from its own process environment in the composition root before any `Anthropic` type is touched (`Environment.SetEnvironmentVariable("ANTHROPIC_CUSTOM_HEADERS", null)`, ARCHITECTURE 4.2 step 5, which logs the removed variable's NAME at Warning and never its value), and a guard `DelegatingHandler` (`AnthropicHostGuardHandler`, a fresh instance in every client's `ClientOptions.Handlers` and one wrapping the exchange client, R-ARCH-15) refuses any request that is not `https` to host `api.anthropic.com` on port 443 by throwing `InvalidOperationException("Refusing to contact a host other than api.anthropic.com.")`; the refused URL is never logged. The shared `SocketsHttpHandler` carries no Anthropic-specific handler (R-ARCH-15). IMPROVEMENT [SECURITY] (Electron's TS SDK behavior for this variable was not verified; UNVERIFIED whether Electron is exposed). Test: `AnthropicClientFactoryTests.CustomHeadersEnvironmentHasNoEffect` (sets the variable before the first SDK use in a separate test process, asserts no injected header on the recorded request), `CompositionTests.ClearsCustomHeadersEnvironment`, `AnthropicHostGuardHandlerTests` (scheme, host and port refusals).

**INV-AUTH-36. Every user-facing failure this subsystem throws is a `ShotAIException` subclass whose `Message` is the exact Electron string.** `SignInRequiredException` (S1, S2, S3 on the sign-in leg), `FederationExchangeException` (S4 to S14), `FederationNotConfiguredException` (S3 from `SignInAsync`), `ApiKeyStoreException` (S16, S17) and `EntraSignInFailedException` (an interactive MSAL failure, `Message` = MSAL's message verbatim, including the `AADSTS` code; the one exception is the missing broker redirect URI, whose `Message` is S18 with MSAL's message appended after ` Details: `, D23). Why: 11's `UserMessage.From` shows `ShotAIException` text bare and replaces an `ArgumentException`, `InvalidOperationException` or other unexpected exception with the generic `Something went wrong. See the log for details.` (11 X2, `11-service-boundary.md:1127`), which would hide S3, S16, S17 and the `AADSTS` code an administrator needs. Test: `AuthErrorTypeTests` (reflection: each type derives from `ShotAIException`; each thrown path uses one of them).

---

## 5. Edge cases and hard-won fixes

**EDGE-AUTH-1. The invalidator that nothing called.** `invalidateFederationConfig` existed, its comment and two already-merged admin documents promised reopen-Settings pickup, and grep found only its definition. Required: status reads invalidate (INV-AUTH-9), and a test ties the admin document's promise to the code. Source: commit `1ecebd0`, `federation-cache-wiring.test.ts`.

**EDGE-AUTH-2. Key names are per platform.** A comment and a doc claimed the eight names were shared with macOS; none match, and macOS requires all seven of its keys. A profile written from the Windows names delivers nothing on macOS. Required: keep the Windows names exactly; `docs/MANAGED-CONFIG.md:224-238` ("The names are frozen") still says "identical on both platforms", contradicting its own table at `:11-30` (Q-AUTH-14). Source: commit `679c19f`.

**EDGE-AUTH-3. A wrong-typed policy override is silently ignored on a baked build.** `REG_EXPAND_SZ` reads as absent, so the baked value stands. Required: parity; the admin doc tells admins to check the type first. Source: `docs/MANAGED-CONFIG.md:145-153`, commit `1ecebd0`.

**EDGE-AUTH-4. Braces and whitespace from Windows tooling.** `  {GUID}  ` is accepted as the bare GUID. `{GUID` (one brace) is invalid. Required: parity (2.5). Source: `config-validate.test.ts:63-70`.

**EDGE-AUTH-5. `api://` audience.** An `AudienceAppId` of `api://<guid>` fails the GUID check (fails closed). On the Anthropic side, an expected audience left blank substitutes Anthropic's default audience, which an Entra token never carries. Required: parity. Source: `docs/MANAGED-CONFIG.md:91-94`, `federation-local.test.ts:32-38`, `macOS:docs/SSO-WIF.md:156-158`.

**EDGE-AUTH-6. Present-but-blank ClientAppId.** `ClientAppId = "   "` normalizes to absent, so the audience id is used as the client (the deployed single-registration topology). Required: parity. Source: `config-validate.ts:147-155`.

**EDGE-AUTH-7. Failed step 2 poisons step 3 harmlessly.** If `AudienceAppId` is invalid and `ClientAppId` absent, `clientAppId` is `""`, but the config is rejected at the gate anyway. Required: never expose `clientAppId` from a failed result. Source: `config-validate.ts:146-168`.

**EDGE-AUTH-8. A TenantId or AudienceAppId correction is not picked up until restart in Electron.** The cached `EntraClient` is rebuilt only when `clientAppId` changes (`auth-core.ts:76-88`), and its scope and authority were captured from the first config. Required natively: rebuild the MSAL gateway when `(TenantId, ClientAppId)` changes and compute the scope from the current config on every call. IMPROVEMENT (makes the documented "reopen Settings" promise true for all eight values).

**EDGE-AUTH-9. `forceRefresh` is wired but unused.** No caller passes it. Required natively: keep the parameter on the silent call; the sign-in path uses it once after an interactive success (7.5, IMPROVEMENT) to guarantee fresh claims (Q-AUTH-4).

**EDGE-AUTH-10. The Conditional Access claims challenge is captured and dropped.** `acquireSilent` attaches `claims` to `SignInRequiredError`, but `auth:sign-in` never replays it. Required natively: the Entra session remembers the last non-empty claims challenge seen on a silent failure and passes it to the next interactive call, then forgets it. IMPROVEMENT (the prompt then satisfies exactly the policy that failed, as `msal.ts:27-29` intended).

**EDGE-AUTH-11. Closing the browser tab strands the button.** msal-node's loopback keeps waiting (shotAI sets no timeout), so Settings keeps showing `Waiting for your browser…` and the Sign in button stays disabled. With WAM, a closed dialog returns `authentication_canceled` promptly. Required natively: a user cancel returns to the prior state with no error text (macOS lesson: "A cancel is a choice, not a failure", `macOS:shotAI/AuthModel.swift:95-96`). IMPROVEMENT.

**EDGE-AUTH-12. Sign-out leaves an emptied cache file.** `clear()` is never called; the file stays with no accounts. Required natively: parity is acceptable (the MSAL extension persists the emptied cache); no requirement to delete the file. Source: `cache-plugin.ts:115-121`, `ipc.ts:827-834`.

**EDGE-AUTH-13. Several cached accounts.** A "Sign in again" with a different account adds a second account; `getAllAccounts()[0]` may still be the old one. Required natively: INV-AUTH-34. IMPROVEMENT.

**EDGE-AUTH-14. Cached account with a dead refresh token does not fall back to the key.** `makeClient` picks `federated`; the first request throws `SignInRequiredError` (S1). Same on macOS (only "no session" falls back: `macOS:Packages/EntraKit/Sources/EntraKit/CompositeCredentialProvider.swift:48-53`). Required: parity; the user signs in again or signs out to use the key.

**EDGE-AUTH-15. Federation removed while an account is cached.** Status reports `federationAvailable: false`, `signedIn: false`; the cache is left as is; `auth:sign-out` is a no-op. Required: parity. Source: `ipc.ts:801-804`, `:830-833`.

**EDGE-AUTH-16. Status says signed in while the token cannot be refreshed.** `signedIn` is "an account is cached", not "a token is obtainable"; Test connection's sign-in leg is where the truth appears. Required: parity (status reads never touch the network).

**EDGE-AUTH-17. The Request access hint appears for any failed test when federation is configured.** It reads "Your sign-in worked." even when the failed leg was `signIn`, and it also appears in the Microsoft group when the failed test was the API-key test (one shared `testResult`). Required: parity in 2.0 (06 owns the copy); raised as Q-AUTH-9.

**EDGE-AUTH-18. Test connection during a key-path failure before a mode is known.** When `makeClient` throws (no credential), the result carries `mode: 'apiKey'`, `leg: 'api'`, and S1 or S2, so the UI shows `Claude API: Add an Anthropic API key in Settings to use Claude.` Required: parity. Source: `claude-service.ts:287-303`.

**EDGE-AUTH-19. A passing federated test costs two exchanges.** Leg 2's token is discarded; leg 3's client exchanges again. Required: parity (diagnostic cost, not production cost).

**EDGE-AUTH-20. Every estimate and generation builds a fresh client and exchanges once.** `makeClient` is per call. Required: parity (Q-AUTH-8 discusses caching).

**EDGE-AUTH-21. The msal-node loopback page rendered markup literally.** No `Content-Type` header; templates must read as plain text. Required natively for the browser fallback: the same plain-text strings (7.5). Source: commit `f1b24a8`, bug 3.

**EDGE-AUTH-22. `cmd /c start` truncated the authorize URL at `&`.** Entra answered `AADSTS900144` (missing scope), which reads like a tenant misconfiguration. Required: never launch a URL through a shell; natively WAM needs no browser launch and MSAL.NET's fallback opens the browser itself. Source: commit `568aaab`.

**EDGE-AUTH-23. msal-node v5 had no proxy knob.** Solved by injecting a network module over `net.fetch`; the probe counted MSAL's calls to prove it was used. Natively the risk does not exist (WAM uses OS networking; MSAL.NET takes an `IMsalHttpClientFactory`). Source: commits `513a6d0`, `850abc1`. ELECTRON-ONLY.

**EDGE-AUTH-24. `safeStorage.decryptStringAsync` resolves an object.** The #63 snippet would have passed `{ shouldReEncrypt, result }` to `deserialize`. ELECTRON-ONLY; the native lesson is to test the persistence layer against the real OS primitive, not only a stub. Source: commit `850abc1`.

**EDGE-AUTH-25. Generation ignored a successful sign-in.** The SOP panel gated on `keyStatus().hasKey` only, and read status once per mount. Fixed with `canGenerate = signedIn || hasKey` and a focus re-read. Required natively (07 and 05): gate on `AuthStatus`, and refresh on the `AuthStatusChanged` event (7.12) as well as on window activation. Source: commit `f1b24a8`, bugs 1 and 2.

**EDGE-AUTH-26. Wrong remedy in the refusal.** An unconfigured machine must be told to add an API key, never to sign in with Microsoft. Source: `auth-core.test.ts:43-53`, commit `d79bc3b`.

**EDGE-AUTH-27. The stale-roles trap.** Right after IT grants `shotAI.User`, the cached Entra token (60 to 90 minutes) has no `roles` claim; a silent retry keeps failing with the opaque 401. Required: the Sign in button is always interactive (INV-AUTH-13), the copy tells the user to sign in again after requesting access (06), and the native path adds a forced silent refresh after interactive success (7.5). Source: `docs/MANAGED-CONFIG.md:176-181`, `Intune/Windows/README.md:107-117`.

**EDGE-AUTH-28. Admin accounts prove nothing.** Elevated directory roles bypass app assignment gates and receive a token with no `roles` claim; a probe pass with an admin says nothing about staff. Required: acceptance testing uses a non-admin assigned account and a non-admin unassigned account (AC-AUTH-28). Source: commit `70ce4a7`, `wif-probe.mjs:161`.

**EDGE-AUTH-29. The issuer maximum JWT lifetime.** A 1 hour issuer maximum would reject normal 60 to 90 minute Entra tokens intermittently; measured tokens of 68 to 94 minutes were accepted, so this tenant's issuer is above the default. Required: nothing in code; keep the probe's lifetime line. Source: commits `568aaab`, `850abc1`, `992398f`.

**EDGE-AUTH-30. Rule lifetime at the wizard prefill.** `token_lifetime_seconds = 600` makes mid-generation refresh routine; the live rule was raised to 3600. Required: nothing in code (the SDK cache refreshes); the probe flags 600. Source: commit `568aaab`.

**EDGE-AUTH-31. Quota exhaustion surfaces at generation, not sign-in.** The exchange keeps succeeding when the budget is spent; the error belongs to the SOP path (07's `rateLimitMessage`). Source: `macOS:docs/SSO-WIF.md:275-282`.

**EDGE-AUTH-32. A 200 with a JSON `null`, an array, or a numeric-string `expires_in`.** `null` and arrays read as missing fields (S8); `"600"` is accepted as 600 by `Number()`; `true` would be accepted as 1. Required: the native `ToNumber` of 7.7 reproduces this. Source: `federation.ts:145-148`.

**EDGE-AUTH-33. Electron's API key ciphertext is unreadable natively, and vice versa.** Electron's `secrets.json` holds OSCrypt (AES-GCM under a key in `Local State`) ciphertext; native uses DPAPI directly. If both used the same file, each app would show the other's key as "couldn't be read" and a Clear would destroy it, breaking the rollback path. Required natively: separate file names (7.16); IMPROVEMENT. Source: feasibility doc "Credentials don't carry over", `cache-plugin.ts:91-97`.

**EDGE-AUTH-34. `reg.exe` output is OEM code page text.** A non-ASCII `SupportUrl` path could be mangled by Electron's parse; native reads the exact string. IMPROVEMENT (only affects `SupportUrl`; every other value is ASCII by validation).

**EDGE-AUTH-35. WHATWG URL vs `System.Uri`.** `new URL('https:help.example.com')` normalizes to `https://help.example.com/`; `System.Uri` may reject it or parse differently; spaces, IDN hosts and default ports normalize differently. Required: 7.2 defines `IsHttpsUrl` and `Origin` with test vectors; values that differ between the parsers are documented, and the `SupportUrl` degrade rule means a divergence costs only the link, never the sign-in.

**EDGE-AUTH-36. The committed template is a rejected config, not an unconfigured one.** `federation.example.json` uses `fdrl_REPLACE_ME`, `svac_REPLACE_ME`, `wrkspc_REPLACE_ME`; the underscore in the body fails the `TAGGED` regexes. A developer who copies the template and fills in only the GUIDs gets `federation: config rejected, falling back to API key (missing: none; invalid: FederationRuleId,ServiceAccountId,WorkspaceId).` and a BYO-key app. Required: parity (the validator is unchanged); the native template (Q-AUTH-10) keeps the same placeholders so the behavior stays the same on both builds, and `FederationLocalFileTests.PassesValidation` is what surfaces it. Source: `federation.example.json:20-25`, `config-validate.ts:87-91`.

**EDGE-AUTH-37. A UTF-8 byte order mark silently unconfigures the Electron build.** `JSON.parse` rejects a leading U+FEFF and the vite reader swallows the error, so a BOM-prefixed `federation.local.json` bakes `{}`; `federation-local.test.ts` then fails "is valid JSON", which is the only signal. Required natively: `BakedFederationParser` strips one leading UTF-8 BOM before parsing (IMPROVEMENT: a release-pipeline secret file written by Windows PowerShell 5.1 would otherwise ship an unconfigured organization build with no error), and the build warns when a passed `ShotAIFederationFile` path does not exist (7.4). Source: `vite.main.config.ts:12-27`.

**EDGE-AUTH-38. Node's base64 stops at the first `=`.** The advisory role check decodes the payload with `Buffer.from(part, 'base64')`, which ignores characters outside the alphabet and stops at the first `=`. A base64url JWT payload carries no `=`, so this never matters for a real Entra token, but a native port that throws on invalid characters or decodes past `=` would turn a parsable-in-Electron token into `null` (or the reverse). Required: 7.8 reproduces the measured rule. Source: `federation.ts:226`, measured under Node 22.

**EDGE-AUTH-39. After a role removal, production fails with the exchange error, not the API 401.** The C# SDK refreshes the minted token before a request (mandatory at `exp - 30 s`) and force-refreshes after an API 401; both run the exchange, and a refused exchange throws `WorkloadIdentityException` (status 401) out of the request instead of the API's `401` exception (`anthropic-sdk-csharp:src/Anthropic/AnthropicClient.cs:431-442`, `:520-541`, `Credentials/WorkloadIdentityCredentials.cs:138-161`). So the text a signed-in user sees after revocation is S9 (via `ForSdk`), not 07's `Your Claude access was refused. Check that your account still has the shotAI role.`, which only appears when the API itself answers 401 with a token that is still valid. Required: INV-AUTH-24 mapping; AC-AUTH-28 accepts either text.

**EDGE-AUTH-40. The diagnostic leg is more lenient than the production exchange.** Electron's `Number(body.expires_in)` accepts `"600.5"`, `"0x258"`, `" 600 "` and `true`; the C# SDK accepts only a JSON number or a string that `long.TryParse` accepts, and it rejects a `token_type` other than `Bearer` (`anthropic-sdk-csharp:src/Anthropic/Credentials/SecurityHelpers.cs:126-170`). A server response in that gap would pass Test connection leg 2 and fail every generation. Required: parity for the diagnostic (7.7 keeps `ToNumber`), and `ConnectionTester` additionally logs info `connection test: exchange response would be rejected by the SDK (<reason>).` when the diagnostic response has a `token_type` other than `Bearer` or an `expires_in` the SDK would not parse, without failing leg 2. IMPROVEMENT (early warning only; the leg outcome is unchanged).

**EDGE-AUTH-41. The per-value ADML help strings are unreachable.** Sixteen `<string>` entries in the ADML are referenced by nothing in the ADMX, so an administrator never sees, for example, the `Begins with fdrl_` or `bare GUID` hints in the Group Policy editor (the textBox labels carry part of it). Required: nothing natively (the policy files are unchanged, fixed decision); recorded as Q-AUTH-18.

**EDGE-AUTH-42. The memory-only log line repeats.** Electron's `beforeCacheAccess` logs `entra: OS encryption unavailable, token cache is memory-only this session.` on every MSAL cache access. Natively the decision is made once when the cache is registered, so the line is logged once per process. IMPROVEMENT (same meaning, no log spam). Source: `cache-plugin.ts:63-66`.

**EDGE-AUTH-43. The probe does not see policy.** Leg 0 validates `federation.local.json` directly (no `pickBaked`, no HKLM merge), so a machine whose working config comes from policy fails leg 0, and a policy override is never probed. Required natively: the probe tool (7.19) keeps the local-file default for parity and adds `--effective` to resolve through `FederationConfigProvider.ResolveNow()` (baked plus HKLM). IMPROVEMENT. Source: `wif-probe.mjs:63-72`.

**EDGE-AUTH-44. `ANTHROPIC_CUSTOM_HEADERS`.** A C# SDK environment input with no Electron-side counterpart in this subsystem's analysis: it adds headers to every request (INV-AUTH-35). Required natively: neutralized at startup. Source: `anthropic-sdk-csharp:src/Anthropic/Core/ParamsBase.cs:34-47`.

**EDGE-AUTH-45. `ClientOptions` is a `record struct`.** Setting a property on a copy (for example inside a helper that takes `ClientOptions` by value) silently does nothing to the caller's instance, and the `AnthropicClient` constructor stores its own copy. Required: build the options in one local variable and set `Credentials` before `new AnthropicClient(options)` (7.9). Source: `anthropic-sdk-csharp:src/Anthropic/Core/ClientOptions.cs:13`, `AnthropicClient.cs:169-246`.

---

## 6. macOS port notes

| Topic | macOS implementation | Divergence from Electron | Lesson for Windows |
|---|---|---|---|
| Sign-in library | Hand-rolled OAuth 2.0 authorization code plus PKCE in `EntraKit`, zero third-party dependencies, no MSAL by choice (`macOS:Packages/EntraKit/Package.swift:1-16`, `macOS:docs/SSO-WIF.md:242-246`) | Electron uses msal-node | Windows does not hand-roll: MSAL.NET with WAM is first-party, gives device-bound refresh tokens and native Conditional Access. The macOS AADSTS classification tables (`macOS:Packages/EntraKit/Sources/EntraKit/EntraAuthClient.swift:299-378`) are a useful reference for mapping WAM errors in logs, not code to port. |
| Browser hop | `ASWebAuthenticationSession`, the only file importing AuthenticationServices; system browser, callback by custom URL scheme; `prefersEphemeralWebBrowserSession = false` so an existing session SSOs; account switching via `prompt=select_account` or `prompt=login`, never the ephemeral flag (`macOS:shotAI/Auth/WebAuthSignIn.swift:6-86`, `macOS:Packages/EntraKit/Sources/EntraKit/InteractiveSignIn.swift:10-18`) | Same principle (never an embedded webview) | WAM is the Windows analog of the system session. The macOS `@Sendable` trap (the completion handler inherited main-actor isolation and trapped on an XPC queue, twice, with every test green because tests stub the browser) is the general lesson: the sign-in path is not done until the signed app is driven by hand (`macOS:docs/SSO-WIF.md:256-271`). AC-AUTH-26 and AC-AUTH-27 are that step for Windows. |
| Scope | `api://<audience>/user_impersonation offline_access openid profile`; never `.default` (`macOS:Packages/EntraKit/Sources/EntraKit/EntraAuthClient.swift:32-43`) | Electron requests only the resource scope (msal-node adds the OIDC scopes itself) | MSAL.NET also adds `openid profile offline_access` itself; request only the resource scope. |
| Authority | tenant-specific, never `common` (`macOS:Packages/EntraKit/Sources/EntraKit/EntraAuthClient.swift:9-11`) | same | same (INV-AUTH-11) |
| Config sources | `ChainedFederationConfig([ManagedFederationConfigStore(), BundledFederationConfig()])`: the FIRST source that yields a complete config wins as a whole; a source that yields a partial config is reported only if no later source is complete (`macOS:Packages/EntraKit/Sources/EntraKit/FederationConfig.swift:120-140`, test `testCompleteSourceBeatsAnEarlierPartialOne`) | Windows merges PER KEY and then validates the merged set; a half-delivered policy over a complete baked set can still produce a valid config, and a malformed policy value over a good baked one fails closed (`config-sources.test.ts:196-207`) | Keep the Windows per-key rule (REQUIRED); do not copy the macOS whole-source rule. |
| Managed trust | only values with `objectIsForced` are trusted in Release; Debug accepts `defaults write` (`macOS:Packages/EntraKit/Sources/EntraKit/FederationConfig.swift:155-186`) | HKLM is the Windows analog | No debug override natively: developers use `federation.local.json` or write HKLM as an administrator. |
| Required set | all seven keys required; `workspaceId` may be the literal `default` (`macOS:Packages/EntraKit/Sources/EntraKit/FederationConfig.swift:25-54`, `macOS:Packages/SOPKit/Sources/SOPKit/FederationExchange.swift:7-21`) | Windows: five required, `ClientAppId` and `WorkspaceId` optional | Keep Windows rules. The C# SDK documents `"default"` as a legal `WorkspaceId`; the Windows validator rejects it (`wrkspc_` only). Parity: keep rejecting (Q-AUTH-12). |
| Persisted secret | Keychain generic password holding `{ refreshToken, accountLabel, hadRequiredRole, tenantId }`, `kSecAttrAccessibleAfterFirstUnlock` so a silent refresh works while the screen is locked (`macOS:Packages/EntraKit/Sources/EntraKit/FederationAccountStore.swift:11-100`) | Electron persists the whole MSAL cache | WAM keeps the refresh token device-bound outside the app; the MSAL extension cache holds accounts and access tokens. DPAPI CurrentUser works while locked, so no equivalent of the accessibility flag is needed. |
| Refresh token rotation | persisted BEFORE the new access token is used; a crash in the other order leaves a stale token and silently signs the user out (`macOS:Packages/EntraKit/Sources/EntraKit/FederatedCredentialProvider.swift:116-127`) | MSAL handles it | MSAL.NET plus WAM handle rotation; nothing to write. |
| Minted token cache | actor with lazy use-time refresh, 120 s renew margin, in-flight coalescing, never a timer; absolute wall-clock expiry, never monotonic (a Mac asleep for hours barely advances a monotonic clock) (`macOS:Packages/EntraKit/Sources/EntraKit/FederatedCredentialProvider.swift:4-102`, `macOS:Packages/SOPKit/Sources/SOPKit/FederationExchange.swift:81-89`) | Electron and C# use the SDK `TokenCache` (same thresholds) | The C# SDK `TokenCache` compares `ExpiresAt` against `DateTimeOffset.UtcNow` (wall clock), which is the right choice across S3 suspend. Do not wrap it in a timer. |
| Local role pre-flight | if the Entra token lacks `shotAI.User`, the provider throws `notEntitled` BEFORE the exchange, so the user is told "your sign-in worked, you do not have access yet" (`macOS:Packages/EntraKit/Sources/EntraKit/FederatedCredentialProvider.swift:129-135`, test `testMissingAppRoleIsCaughtLocally`) | Electron never gates on the local check (advisory, fail-open) and lets the rule decide | Keep Electron's rule (INV-AUTH-23). macOS's gate can wrongly refuse a user whose rule does not require the role claim or whose token shape surprises the parser. |
| Fallback to key | only "no session" falls back to a stored key; a signed-in user lacking the role must see that, not be switched silently (`macOS:Packages/EntraKit/Sources/EntraKit/CompositeCredentialProvider.swift:28-53`) | same as Electron | Same (EDGE-AUTH-14). |
| Misconfigured state | the UI shows a `misconfigured(fields:)` row naming missing fields (`macOS:shotAI/AuthModel.swift:17-53`) | Electron logs the rejection and shows nothing | Parity is "log only"; 06 records the macOS row as a possible later adoption. |
| Credential type | `ClaudeCredential` with private storage, no accessor, redacted `description`, `debugDescription` and `customMirror`, deliberately not `Equatable` so a failing assertion cannot print it (`macOS:Packages/SOPKit/Sources/SOPKit/Credential.swift:3-65`) | Electron passes strings | Adopt the idea natively: `MintedToken` and the API key wrapper override `ToString` and use `[DebuggerDisplay("<redacted>")]` (INV-AUTH-19). |
| Exchange errors | 401 and 403 map to `federationRefused` and never through the API-key message ("Invalid API key." is the worst sentence for someone who has no key) (`macOS:Packages/SOPKit/Sources/SOPKit/FederationExchange.swift:41-76`) | Electron's `explain()` table | Keep Electron's table (2.11). |
| Exchange response | `workspace_id` always sent (required config); an absent or non-integer `expires_in` defaults to `600` s (`(obj["expires_in"] as? Int) ?? 600`); only status `200` counts as success (`macOS:Packages/SOPKit/Sources/SOPKit/FederationExchange.swift:57-89`) | Electron omits an absent `workspace_id`, rejects a missing `expires_in` (S8), and treats any 2xx as success | Keep Electron's rules (2.11, INV-AUTH-22): a guessed lifetime is exactly the constant the Electron comment forbids. |
| API key store | login Keychain, env fallback, `storedButUnreadable` flag (`macOS:Packages/SOPKit/Sources/SOPKit/ApiKeyStore.swift:4-140`) | Electron: `safeStorage` file plus `hasStoredCiphertext` | Keep Electron's status shape (INV-AUTH-28); DPAPI file natively. |
| Public releases carry the baked values | the public DMG is built with the config baked in, a considered trade (reconnaissance, not access) (`macOS:docs/SSO-WIF.md:126-146`) | Windows: not recorded | Q-AUTH-11. |
| Operational traps | CEL rule needs `"roles" in claims &&` before the membership test; Console claim conditions cannot match `roles`; v2.0 issuer; never delete the audience registration; `check_jti` and `max_jwt_lifetime_seconds` are issuer-level (`macOS:docs/SSO-WIF.md:60-86`, `:148-185`) | none (server-side) | Keep them in the admin docs; no code. |

---

## 7. Native design (C#)

### 7.1 Placement

| Type | Project and namespace | Responsibility |
|---|---|---|
| `FederationKeys`, `FederationPolicy` | Core, `ShotAI.Core.Auth` | the eight names in order, the required five, the policy key path and hive constants |
| `FederationConfig`, `FederationConfigResult`, `RawFederationConfig` | Core | validated record, result union, raw name to string map |
| `FederationNormalizer`, `FederationConfigValidator`, `FederationSources` | Core | `Norm`, `Validate`, `PickBaked`, `Merge`, `IsUnconfigured` (2.5, 2.6) |
| `PolicyValue`, `PolicyValueParser`, `IPolicyValueSource` | Core | platform-neutral view of registry values and the REG_SZ filter (2.4) |
| `BakedFederationParser`, `IBakedFederationSource` | Core | the vite filter plus `PickBaked` (2.3) |
| `FederationConfigProvider` | Core | resolve, cache, invalidate, log (2.7) |
| `FederationExchange`, `FederationExchangeException`, `MintedToken`, `FederationErrorText` | Core | diagnostic exchange, the `explain` table, the SDK error mapping for 07 (2.11, INV-AUTH-24). The `ToNumber` coercion it needs is 04's `ShotAI.Core.Json.JsValue.ToNumber(JsonNode?)` (7.7), not an Auth type (Corrected in WP-D4) |
| `JwtRoles` | Core | advisory role check (2.12) |
| `SignInRequiredException`, `FederationNotConfiguredException`, `EntraSignInFailedException`, `ApiKeyStoreException` | Core, all deriving from 11's `ShotAI.Core.Errors.ShotAIException` | the sign-in contract (2.8) and the user-facing failures (INV-AUTH-36) |
| `IMsalGateway`, `IMsalGatewayFactory`, `EntraAppConfig`, `EntraAccount`, `EntraToken`, `EntraUiRequiredException`, `EntraSignInCanceledException` | Core | the seam around MSAL.NET, so all sign-in logic tests on Linux |
| `EntraSession` | Core | gateway lifetime, scope, first account, silent, interactive, sign-out, claims carry, one-account rule (2.8, INV-AUTH-34) |
| `EntraIdentityTokenProvider` | Core | `Anthropic.Credentials.IIdentityTokenProvider` over `EntraSession.AcquireSilentAsync` (INV-AUTH-12) |
| `AnthropicClientFactory` (`IAnthropicClientFactory`), `AnthropicHostGuardHandler` | Core | the credential decision and client construction (2.13); the ONLY Anthropic client factory in the app, used by 07 and by `ConnectionTester` (R-ARCH-15: 07's `IClaudeClientFactory`, `ClaudeClientFactory`, `ISopCredentialSource` and `EgressPinHandler` are superseded); the host guard (`https`, `api.anthropic.com`, port 443), a fresh instance per client and one on the exchange client (INV-AUTH-35, R-ARCH-15) |
| `ConnectionTester` | Core | the three legs (2.14), behind `IAuthService.TestConnectionAsync`, the one public entry point (R-ARCH-3) |
| `ApiKeyStore` (`IApiKeyStore`, `internal`), `ISecretProtector`, `ApiKeyStatus`, `ApiKeySource` | Core | secrets file logic over an injected protector (2.15); `IApiKeyStore` is internal to Core (R-ARCH-2) |
| `AuthStatus`, `AuthStatusMode`, `AuthMode`, `ConnectionLeg`, `TestConnectionResult`, `SignInOutcome` | Core | UI-facing value types (2.16) |
| `AuthService` (`IAuthService`) | Core | the UI facade: status, sign-in, sign-out, key status, set, clear, test; raises `AuthStatusChanged` |
| `SupportUrlAllowlist` (`ISupportUrlAllowlist`) | Core | the exact-origin extension (2.17), consumed by 10 |
| `SharedHttp` (`ISharedHttp`) | Core, `ShotAI.Core.Net` | one `SocketsHttpHandler` with no Anthropic-specific handler, per-consumer `HttpClient` wrappers (7.14) (Q-AUTH-13 on ownership) |
| `RegistryPolicySource` | Platform, `ShotAI.Platform.Auth` | enumerates the HKLM key (7.3); `internal sealed`, registered only as `IPolicyValueSource` (INV-ARCH-4) |
| `MsalGatewayFactory`, `MsalGateway`, `MsalCacheRegistration`, `MsalHttpClientFactory`, `MsalLogBridge` | Platform | MSAL.NET, WAM, the extension cache (7.5, 7.6); all `internal sealed`, `MsalGatewayFactory` registered only as `IMsalGatewayFactory` (INV-ARCH-4) |
| `DpapiSecretProtector` | Platform | `ProtectedData` CurrentUser (7.11); `internal sealed`, registered only as `ISecretProtector` (INV-ARCH-4) |
| `EmbeddedBakedFederationSource` | App, `ShotAI.App.Auth` | reads the manifest resource `ShotAI.Federation.Baked.json` (7.4); registered as `IBakedFederationSource` |
| DI registration | each project's extension method (ARCHITECTURE C7, 4.3): `AddShotAICore` registers `IAuthService`, `IAnthropicClientFactory`, `ISupportUrlAllowlist`, `ISharedHttp`, `IApiKeyStore` (internal), `EntraSession` and `FederationConfigProvider`; `AddShotAIPlatform` registers `IPolicyValueSource`, `IMsalGatewayFactory` and `ISecretProtector`; `AddShotAIApp` registers `IBakedFederationSource` | singletons, all lazy (INV-AUTH-10) |

NuGet (versions only in `dotnet/Directory.Packages.props`, ARCHITECTURE 3.1 and 3.2): `Microsoft.Identity.Client`, `Microsoft.Identity.Client.Broker`, `Microsoft.Identity.Client.Extensions.Msal` (Platform); `System.Security.Cryptography.ProtectedData` (Platform); `Anthropic` `12.50.0` (Core, shared with 07). `Anthropic` and MSAL are upgraded deliberately, never by Dependabot auto-merge, with `AnthropicClientFactoryTests` and `MsalCachePersistenceTests` as the tripwires (ARCHITECTURE V9). Use MSAL.NET 4.61.0 or later (the first line without the `net6.0-windows` binary; the broker package is the supported path). The broker package brings the native `msalruntime` binaries per RID; 12 must ship both `win-x64` and `win-arm64`. No CsWin32 entries are needed: the owner HWND comes from WPF's `WindowInteropHelper`, the registry from `Microsoft.Win32.Registry`, DPAPI from `ProtectedData`.

Core references neither MSAL nor any Windows API (the `dotnet/README.md` rule, INV-ARCH-1). Every class in Core above is exercised on Linux by `ShotAI.Core.Tests`. No auth type composes a user-data path itself: `ApiKeyStore` and `MsalCacheRegistration` receive `IAppPaths` (10 7.4.4, ARCHITECTURE 10.2) and use `UserDataDirectory` and `LocalDataDirectory` respectively.

Visibility (INV-ARCH-3, INV-ARCH-6, ARCHITECTURE 9.5 item 8): the App reaches auth only through `IAuthService` and its value types, which `Composition.ViewModelDependencyTests` enforces for `EntraSession`, `IApiKeyStore` and `MsalGateway` among others. `IApiKeyStore` is `internal` (R-ARCH-2); the implementation classes `ApiKeyStore`, `AuthService` and `AnthropicClientFactory` are `internal sealed` behind their public interfaces, so a public constructor never exposes the internal `IApiKeyStore`. `EntraSession`, `FederationExchange`, `JwtRoles`, `FederationConfigProvider` and the `IAnthropicClientFactory` interface stay public because the probe tool (7.19) references Core without `InternalsVisibleTo` (which names only `ShotAI.Core.Tests`, INV-ARCH-6).

### 7.2 Configuration (Core)

```csharp
namespace ShotAI.Core.Auth;

public static class FederationKeys
{
    public const string TenantId = "TenantId", ClientAppId = "ClientAppId", AudienceAppId = "AudienceAppId",
        FederationRuleId = "FederationRuleId", OrganizationId = "OrganizationId",
        ServiceAccountId = "ServiceAccountId", WorkspaceId = "WorkspaceId", SupportUrl = "SupportUrl";
    // Order is load-bearing (INV-AUTH-6 ordering of names, ADMX contract test).
    public static readonly ImmutableArray<string> All =
        [TenantId, ClientAppId, AudienceAppId, FederationRuleId, OrganizationId, ServiceAccountId, WorkspaceId, SupportUrl];
    public static readonly ImmutableArray<string> Required =
        [TenantId, AudienceAppId, FederationRuleId, OrganizationId, ServiceAccountId];
    public static bool IsKnown(string name) => All.Contains(name, StringComparer.Ordinal);
}

public static class FederationPolicy
{
    public const string KeyPath = @"SOFTWARE\Policies\shotAI\Federation"; // relative to HKLM
    public const string DisplayPath = @"HKLM\SOFTWARE\Policies\shotAI\Federation";
    public const string DefaultSupportUrl = "https://github.com/Armadillon44/shotAI/issues";
}

// name -> delivered string (null or absent = not delivered). Ordinal keys.
public sealed class RawFederationConfig : Dictionary<string, string?> { public RawFederationConfig() : base(StringComparer.Ordinal) {} }

public sealed record FederationConfig(
    string TenantId, string ClientAppId, string AudienceAppId, string FederationRuleId,
    string OrganizationId, string ServiceAccountId, string? WorkspaceId, string? SupportUrl);

public abstract record FederationConfigResult
{
    public sealed record Ok(FederationConfig Config) : FederationConfigResult;
    public sealed record Rejected(ImmutableArray<string> Missing, ImmutableArray<string> Invalid) : FederationConfigResult;
}
```

`FederationConfig` overrides `ToString()` to print value names only (`FederationConfig { TenantId = <set>, ... }`) so a stray interpolation cannot log a value (INV-AUTH-6). IMPROVEMENT.

Algorithms (exact):

```csharp
static readonly Regex Guid   = new(@"^[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}\z", RegexOptions.CultureInvariant);
static readonly Regex Fdrl   = new(@"^fdrl_[0-9A-Za-z]+\z", RegexOptions.CultureInvariant);
static readonly Regex Svac   = new(@"^svac_[0-9A-Za-z]+\z", RegexOptions.CultureInvariant);
static readonly Regex Wrkspc = new(@"^wrkspc_[0-9A-Za-z]+\z", RegexOptions.CultureInvariant);
static readonly Regex Braces = new(@"^\{(.*)\}\z", RegexOptions.CultureInvariant); // '.' excludes '\n' as in JS

public static string? Norm(string? v)
{
    if (v is null) return null;
    var t = JsString.Trim(v);
    var m = Braces.Match(t);
    if (m.Success) t = m.Groups[1].Value;
    t = JsString.Trim(t);
    return t.Length == 0 ? null : t;
}
```

- `\z`, not `$`: .NET `$` also matches before a final `\n`, JavaScript's does not. Never use `\d` or `\w` (Unicode in .NET). JavaScript `.` excludes `\n`, `\r`, U+2028 and U+2029; .NET `.` excludes only `\n`. After `JsString.Trim` a trailing terminator is impossible; an interior `\r`, U+2028 or U+2029 inside braces would strip natively but not in Electron. Handle it exactly: before the brace match, if `t` contains any of `\r`, U+2028, U+2029, skip the strip. REQUIRED (parity).
- `Validate(RawFederationConfig raw)` follows the step table of 2.6 literally, appending to two `List<string>` in step order, returning `Rejected` at step 8 when either list is non-empty.
- `IsHttpsUrl(string v) => Uri.TryCreate(v, UriKind.Absolute, out var u) && u.Scheme == Uri.UriSchemeHttps && u.Host.Length > 0 && !v.Contains('\\')`. WHATWG treats `\` as `/` in special URLs and `System.Uri` treats it differently; rejecting it keeps the result a subset of WHATWG's accepts. Known divergence: a scheme-relative oddity such as `https:help.example.com` (WHATWG accepts and normalizes) may be rejected natively; the consequence is only the default support link (EDGE-AUTH-35). Test vectors in 8.5.
- `PickBaked(IReadOnlyDictionary<string, string?> baked)`: for each name in `All`, keep `JsString.Trim(v)` when `v` is non-null and the trimmed value is non-empty.
- `Merge(baked, policy)`: copy `baked`; for each name in `All`, if `policy.TryGetValue(k, out var v) && v is not null && JsString.Trim(v).Length > 0` then `out[k] = v`.
- `IsUnconfigured(raw) => FederationKeys.All.All(k => Norm(raw.GetValueOrDefault(k)) is null)`.

`BakedFederationParser.Parse(ReadOnlySpan<byte> utf8Json)`: parse with `JsonDocument` (`AllowTrailingCommas = false`, `CommentHandling = Disallow`, matching `JSON.parse`); the root must be an object, else `{}`; for each property (last duplicate wins, as `JSON.parse`), keep it when the name does not start with `_`, the value kind is `String`, and `JsString.Trim(value).Length > 0`, storing the trimmed value; any `JsonException` gives `{}` (vite parity: a malformed local file is "unconfigured"). Then `PickBaked`. REQUIRED.

`PolicyValueParser.ToRaw(IEnumerable<PolicyValue> values)` where `record PolicyValue(string Name, PolicyValueKind Kind, string? StringValue)`: keep an entry only when `Kind == PolicyValueKind.String` (REG_SZ) and `FederationKeys.IsKnown(Name)` (ordinal) and `StringValue is not null`; store `JsString.Trim(StringValue)`. `PolicyValueKind` mirrors `Microsoft.Win32.RegistryValueKind` (`String`, `ExpandString`, `MultiString`, `DWord`, `QWord`, `Binary`, `None`, `Unknown`) without referencing it. REQUIRED.

`FederationConfigProvider`:

```csharp
public sealed class FederationConfigProvider(IBakedFederationSource baked, IPolicyValueSource policy, ILogger<FederationConfigProvider> log)
{
    Task<FederationConfig?>? _cached; readonly Lock _gate = new();
    public Task<FederationConfig?> GetAsync(CancellationToken ct)
    { lock (_gate) return _cached ??= ResolveAndLogAsync(); } // ct is not passed into the shared task
    public void Invalidate() { lock (_gate) _cached = null; }
    public ResolvedFederation ResolveNow(); // raw, result, unconfigured; for diagnostics and the probe
}
```

- `ResolveAndLogAsync` runs on the thread pool (`Task.Run`): `raw = Merge(baked.Read(), PolicyValueParser.ToRaw(policy.Read()))`; result and logging exactly as the 2.7 table. `baked.Read()` is computed once per process (the resource cannot change). REQUIRED.
- A faulted task is never cached: if resolution throws unexpectedly (it should not; every source swallows its own failures), clear `_cached`, log the exception type (not message) and return `null`. IMPROVEMENT (defensive; Electron would cache the rejected promise forever).
- `Invalidate()` is called only by `AuthService.GetStatusAsync` (INV-AUTH-9).

### 7.3 Policy source (Platform)

```csharp
internal sealed class RegistryPolicySource : IPolicyValueSource   // INV-ARCH-4: registered only as IPolicyValueSource
{
    public RegistryPolicySource() : this(RegistryHive.LocalMachine, RegistryView.Registry64, FederationPolicy.KeyPath) {}
    internal RegistryPolicySource(RegistryHive hive, RegistryView view, string path) { ... } // tests only (InternalsVisibleTo Platform.Tests)
    public RegistryHive Hive { get; } public RegistryView View { get; } public string Path { get; }

    public IReadOnlyList<PolicyValue> Read()
    {
        if (!OperatingSystem.IsWindows()) return [];
        try
        {
            using var root = RegistryKey.OpenBaseKey(Hive, View);
            using var key = root.OpenSubKey(Path, writable: false);
            if (key is null) return [];
            var list = new List<PolicyValue>();
            foreach (var name in key.GetValueNames())
            {
                if (name.Length == 0) continue; // the (Default) value
                var kind = key.GetValueKind(name);
                var s = kind == RegistryValueKind.String
                    ? key.GetValue(name, null, RegistryValueOptions.DoNotExpandEnvironmentNames) as string
                    : null;
                list.Add(new PolicyValue(name, Map(kind), s));
            }
            return list;
        }
        catch (Exception)
        { return []; } // any failure is "nothing delivered", as Electron's bare catch (INV-AUTH-3)
    }
}
```

REQUIRED (INV-AUTH-1 to 3). The production constructor is the only public one; `AddShotAIPlatform` registers the type as `IPolicyValueSource` (INV-ARCH-4, ARCHITECTURE C7); no code path constructs a HKCU reader outside tests. The native reader returns the exact string (no OEM code page round trip, EDGE-AUTH-34, IMPROVEMENT) and does not split on newlines (an interior newline stays in the value and fails validation, which is the fail-closed outcome).

### 7.4 Baked values (build)

- Input file: `dotnet/federation.local.json` (gitignored; the implementing PR adds the line `dotnet/federation.local.json` to `.gitignore`), with a fallback to `src/main/entra/federation.local.json` until cutover, so a developer who configured the Electron build configures both. Same shape and names as `federation.example.json` (Q-AUTH-10).
- `ShotAI.App.csproj`:

```xml
<PropertyGroup>
  <ShotAIFederationFile Condition="'$(ShotAIFederationFile)' == '' and Exists('$(MSBuildThisFileDirectory)..\..\federation.local.json')">$(MSBuildThisFileDirectory)..\..\federation.local.json</ShotAIFederationFile>
  <ShotAIFederationFile Condition="'$(ShotAIFederationFile)' == '' and Exists('$(MSBuildThisFileDirectory)..\..\..\src\main\entra\federation.local.json')">$(MSBuildThisFileDirectory)..\..\..\src\main\entra\federation.local.json</ShotAIFederationFile>
</PropertyGroup>
<ItemGroup Condition="'$(ShotAIFederationFile)' != ''">
  <EmbeddedResource Include="$(ShotAIFederationFile)" LogicalName="ShotAI.Federation.Baked.json" />
</ItemGroup>
```

- `EmbeddedBakedFederationSource.Read()`: `typeof(App).Assembly.GetManifestResourceStream("ShotAI.Federation.Baked.json")`; `null` stream gives `{}` (unconfigured); else read all bytes and `BakedFederationParser.Parse`. No file is read at runtime (parity with the inlined literal). The release pipeline passes `-p:ShotAIFederationFile=<path>` from a secret (12).
- The embedded bytes are the file verbatim (including `_README`); filtering happens at runtime in `BakedFederationParser`, so the exe also carries the template's comment strings. Accepted (they are not secret). A build-time MSBuild `Warning` (not an error, so external contributors and CI never break) is raised when `ShotAIFederationFile` is set but the file does not exist, which catches a mistyped release secret path (IMPROVEMENT; Electron silently baked `{}`).
- `BakedFederationParser.Parse` strips one leading UTF-8 BOM (`EF BB BF`) before `JsonDocument.Parse` (EDGE-AUTH-37, IMPROVEMENT).
- The values are not encrypted or obfuscated inside the exe, deliberately (anything shipped is extractable; none is a credential): `macOS:Packages/EntraKit/Sources/EntraKit/FederationConfig.swift:81-97`. REQUIRED.

### 7.5 Entra sign-in: `EntraSession` (Core) and `MsalGateway` (Platform)

Seam:

```csharp
public sealed record EntraAppConfig(string TenantId, string ClientId);
// No MSAL object crosses the seam: the Platform gateway re-resolves the IAccount by id
// (IClientApplicationBase.GetAccountAsync(string)). An `internal` member here could not be
// set from the Platform assembly anyway.
public sealed record EntraAccount(string HomeAccountId, string? Username);
public sealed record EntraToken(string AccessToken, EntraAccount Account);
public sealed class EntraUiRequiredException(string? claims, Exception? inner) : Exception("interaction required", inner) { public string? Claims { get; } = claims; }
public sealed class EntraSignInCanceledException() : Exception("sign-in canceled");
// User-facing (INV-AUTH-36); ShotAIException is 11's base type in ShotAI.Core.Errors.
public sealed class EntraSignInFailedException(string message, Exception? inner = null) : ShotAIException(message, inner);
public sealed class FederationNotConfiguredException() : ShotAIException("shotAI is not set up for Microsoft sign-in on this machine.");
public sealed class ApiKeyStoreException(string message) : ShotAIException(message);

public interface IMsalGateway : IDisposable, IAsyncDisposable   // Dispose for the synchronous exit path (R-ARCH-10)
{
    Task<IReadOnlyList<EntraAccount>> GetAccountsAsync(CancellationToken ct);
    Task<EntraToken> AcquireSilentAsync(EntraAccount account, IReadOnlyList<string> scopes, bool forceRefresh, CancellationToken ct);
    Task<EntraToken> AcquireInteractiveAsync(IReadOnlyList<string> scopes, string? claims, nint ownerWindow, CancellationToken ct);
    Task RemoveAsync(EntraAccount account, CancellationToken ct);
}
public interface IMsalGatewayFactory { Task<IMsalGateway> CreateAsync(EntraAppConfig app, CancellationToken ct); }

public sealed class SignInRequiredException(string message = SignInRequiredException.DefaultMessage) : ShotAIException(message)
{
    public const string DefaultMessage = "Sign in with your Microsoft account to use Claude.";
    public string? Claims { get; init; }
}
public enum SignInOutcome { Completed, Canceled }
```

`EntraSession` (singleton, `IDisposable` and `IAsyncDisposable`; `App.OnExit` disposes the container synchronously, so the synchronous `Dispose` disposes the current gateway and must not need the UI thread, ARCHITECTURE C5, R-ARCH-10):

| Member | Behavior |
|---|---|
| `static IReadOnlyList<string> ScopesFor(FederationConfig c)` | `[$"api://{c.AudienceAppId}/user_impersonation"]`, computed on every call from the config passed in (fixes EDGE-AUTH-8) |
| gateway selection | under a `SemaphoreSlim(1,1)`: if the current gateway's `EntraAppConfig` differs (ordinal) from `(c.TenantId, c.ClientAppId)`, dispose it and `await factory.CreateAsync(...)`. The in-flight creation task is shared, so concurrent callers never create two gateways over the same cache (parity with the cached promise, `msal.ts:68-92`). IMPROVEMENT: the key includes `TenantId`. |
| `SignedInAccountAsync(c, ct)` | `(await gw.GetAccountsAsync(ct)).FirstOrDefault()`; no network under the MSAL extension cache (cache read only) |
| `AcquireSilentAsync(c, forceRefresh, ct)` | account `null`: throw `SignInRequiredException()`. Else `gw.AcquireSilentAsync(account, ScopesFor(c), forceRefresh, ct)`; `EntraUiRequiredException e`: remember `e.Claims` when non-empty (EDGE-AUTH-10), throw `new SignInRequiredException { Claims = e.Claims }`; empty `AccessToken`: throw `SignInRequiredException()`; any other exception propagates unchanged (REQUIRED, 2.8). Returns the access token string. |
| `SignInInteractiveAsync(c, ownerWindow, ct)` | serialized by a second `SemaphoreSlim(1,1)`. `claims = Interlocked.Exchange(ref _pendingClaims, null)`. `token = await gw.AcquireInteractiveAsync(ScopesFor(c), claims, ownerWindow, ct)`; `EntraSignInCanceledException`: restore `_pendingClaims` and return `Canceled` (EDGE-AUTH-11, IMPROVEMENT). Empty token: throw `SignInRequiredException()`. Then remove every cached account whose `HomeAccountId` differs from `token.Account.HomeAccountId` (INV-AUTH-34). Then `try { await AcquireSilentAsync(c, forceRefresh: true, ct); } catch (Exception e) { log info $"entra: post-sign-in refresh skipped ({e.GetType().Name})." }` (EDGE-AUTH-27, IMPROVEMENT, Q-AUTH-4). Log info `entra: interactive sign-in completed.` Return `Completed`. The token is never returned to the caller. |
| `SignOutAsync(c, ct)` | for each account, `gw.RemoveAsync`; log info `entra: signed out.` (REQUIRED) |

`MsalGatewayFactory.CreateAsync` (Platform):

```csharp
var builder = PublicClientApplicationBuilder.Create(app.ClientId)
    .WithAuthority($"https://login.microsoftonline.com/{app.TenantId}")       // tenant-specific (INV-AUTH-11)
    .WithDefaultRedirectUri();                                                 // http://localhost, browser fallback only
#if !SHOTAI_NO_BROKER                                                          // AC-AUTH-32 test builds only; release never defines it
builder = builder.WithBroker(new BrokerOptions(BrokerOptions.OperatingSystems.Windows) { Title = "shotAI" });
#else
log.LogInformation("entra: broker disabled in this build (test build)."); // the factory's ILogger, category claude
#endif
var pca = builder
    .WithHttpClientFactory(msalHttpClientFactory)                              // shared handler, system proxy (7.14)
    .WithLegacyCacheCompatibility(false)
    .WithClientName("shotAI").WithClientVersion(appVersion)
    .WithLogging(msalLogBridge, Microsoft.Identity.Client.LogLevel.Warning, enablePiiLogging: false, enableDefaultPlatformLogging: false)
    .Build();
// Never: .WithClientCapabilities(...)  (INV-AUTH-11)
await cacheRegistration.RegisterAsync(pca.UserTokenCache, ct);             // 7.6
return new MsalGateway(pca, cacheRegistration, uiDispatcher);
```

`MsalGateway`:

- `GetAccountsAsync`: `(await pca.GetAccountsAsync()).Select(a => new EntraAccount(a.HomeAccountId?.Identifier ?? "", a.Username))`.
- `AcquireSilentAsync`: `var a = await pca.GetAccountAsync(account.HomeAccountId)`; `null` (removed meanwhile, for example by the probe or a sign-out in flight): `throw new EntraUiRequiredException(null, null)`. Else `pca.AcquireTokenSilent(scopes, a).WithForceRefresh(forceRefresh).ExecuteAsync(ct)` and map the result to `EntraToken(result.AccessToken, new EntraAccount(...))`; `catch (MsalUiRequiredException e) { throw new EntraUiRequiredException(e.Claims, e); }`. Every other exception propagates unchanged (2.8 parity: the sign-in leg shows its raw `Message`).
- `AcquireInteractiveAsync`: runs on the UI thread through `IUiDispatcher.InvokeAsync` (the `Func<Task<T>>` overload, 11 7.3.1), because MSAL's interactive calls on Windows need the UI synchronization context and WAM needs the owner window (ARCHITECTURE DL5). `MsalGateway.cs` is one of the three UI-affine files with a reasoned per-file `CA2007` suppression (ARCHITECTURE T3, 14.9); nothing in it awaits with `ConfigureAwait(false)` before the dispatch:

```csharp
pca.AcquireTokenInteractive(scopes)
   .WithParentActivityOrWindow(ownerWindow)          // HWND captured by the caller on the UI thread
   .WithPrompt(Prompt.SelectAccount)                 // MSAL default; lets "Sign in again" switch accounts
   .WithClaims(claims)                               // only when non-null
   .WithSystemWebViewOptions(new SystemWebViewOptions
   {   // browser fallback only (WAM unavailable): the Electron loopback texts, verbatim
       HtmlMessageSuccess = "Signed in to shotAI. You can close this tab and return to the app.",
       HtmlMessageError = "shotAI sign-in failed. Close this tab and try again from the app.",
   })
   .ExecuteAsync(ct);
```

  `catch (MsalClientException e) when (e.ErrorCode == MsalError.AuthenticationCanceledError) { throw new EntraSignInCanceledException(); }`; `catch (MsalException e) when (IsMissingRedirectUri(e)) { throw new EntraSignInFailedException(S18 + " Details: " + e.Message, e); }` (D23); `catch (MsalException e) { throw new EntraSignInFailedException(e.Message, e); }`. `IsMissingRedirectUri(e)` is `e.Message.Contains("AADSTS50011", StringComparison.Ordinal)` or any `e.AdditionalExceptionData` value containing it (AADSTS50011 is Entra's redirect URI mismatch code). Every interactive `MsalException` is also logged at warn as `$"entra: interactive sign-in failed ({e.GetType().Name}, {e.ErrorCode})."` (type and code only, never the message, which can carry the UPN; INV-AUTH-31), so AC-AUTH-32 and AC-AUTH-33 can record the shape from the log. 06 shows the `Message` as the Settings error through 11's `UserMessage.From` (parity with Electron, which showed msal-node's message raw; `AADSTS` codes in the message are what administrators need; a raw `MsalException` would be replaced by 11's generic text, INV-AUTH-36).
- `RemoveAsync`: `var a = await pca.GetAccountAsync(account.HomeAccountId)`; when non-null, `pca.RemoveAsync(a)`.
- `DisposeAsync` (and the synchronous `Dispose` that `EntraSession.Dispose` calls at exit, R-ARCH-10): unregister the cache from this PCA (`MsalCacheHelper.UnregisterCache` is synchronous and needs no UI thread).

The owner window: the Settings view model reads `new WindowInteropHelper(Application.Current.MainWindow).Handle` on the UI thread (the Settings view is hosted in the main window, so this equals `Window.GetWindow(view)`; 11 names the main window) before calling `IAuthService.SignInAsync(hwnd, ct)`; the HWND value (not a WPF object) crosses into Core. The per-call `.WithParentActivityOrWindow(IntPtr)` overload exists on `AcquireTokenInteractiveParameterBuilder` (Microsoft Learn, MSAL.NET 4.84.2 reference) and WAM requires a parent handle ("it is now required to provide the window handle", Microsoft Learn, "Using MSAL.NET with Web Account Manager"). If the window handle is `0` (window closing), sign-in is refused with `EntraSignInFailedException("shotAI had no window to present sign-in from.")` (the macOS wording, `macOS:Packages/EntraKit/Sources/EntraKit/InteractiveSignIn.swift:31`). IMPROVEMENT.

**App registration change an administrator must make before the native pilot** (IMPROVEMENT, recorded for the rollout doc; Q-AUTH-3):

| Change | Where | Why |
|---|---|---|
| Add redirect URI `ms-appx-web://microsoft.aad.brokerplugin/{ClientAppId}` (lowercase host as documented, the client's own application id) | the client registration (in the deployed single-registration topology, the audience registration), platform **Mobile and desktop applications** | required by WAM; MSAL does not need it configured in code |
| Keep `http://localhost` under Mobile and desktop applications | same | MSAL's browser fallback; already registered for the Electron build (commit `992398f`) |
| No change | the Anthropic federation rule, the audience, the `shotAI.User` role, the v2.0 issuer, `requestedAccessTokenVersion: 2` | WAM returns the same delegated v2.0 access token for the same scope (verify live, AC-AUTH-27) |

WAM requires an interactive Windows session (not a service, not `runas`); shotAI always runs in one.

**Interactive failure modes beyond the happy WAM path** (recorded for AC-AUTH-32 and AC-AUTH-33; the implementing PR replaces each UNVERIFIED cell with what the manual run observed, quoting the exception type, `ErrorCode` and `Message` from the warn line above and the Settings text):

| Case | What MSAL.NET does | Exception and message | shotAI result | Status |
|---|---|---|---|---|
| WAM cannot be used on this OS (Microsoft Learn: WAM needs Windows 10 1703 or later, or Windows Server 2019 or later; "MSAL will automatically fallback to a browser if WAM cannot be used") | opens the system browser; loopback on `http://localhost:<port>` (from `WithDefaultRedirectUri()`); the tab shows the `SystemWebViewOptions` texts | none on success | `Signed in as <UPN>`; everything after the interactive call (7.5 table) is unchanged | documented fallback; the exact trigger on a supported .NET 10 OS is UNVERIFIED, so AC-AUTH-32 uses the `SHOTAI_NO_BROKER` test build, which runs the same browser code path |
| Broker native runtime (`msalruntime`) missing from the install | does NOT fall back | `MsalClientException`, `ErrorCode` `wam_runtime_init_failed` (Microsoft Learn, "Desktop app that calls web APIs: Acquire a token by using WAM", Troubleshooting) | `Error: <MSAL message>` in Settings, button re-enabled | documented; prevented by 12 shipping `msalruntime` for both RIDs (Risk R1) |
| Broker redirect URI `ms-appx-web://microsoft.aad.brokerplugin/{ClientAppId}` not registered on the client (Q-AUTH-3) | the request reaches Entra through WAM and fails | expected an `MsalServiceException` whose `Message` contains `AADSTS50011` (UNVERIFIED: Microsoft Learn documents neither the type nor the text for the broker path; WAM may show the Entra error page in its own dialog first) | `Error: ` + S18 + ` Details: <MSAL message>` in Settings, no crash, `Sign in with Microsoft` enabled again | UNVERIFIED (Q-AUTH-19); if the observed message lacks `AADSTS50011`, `IsMissingRedirectUri` is changed to match the observed `ErrorCode` or `AdditionalExceptionData` key and this row is updated |

### 7.6 The persisted token cache (Platform, `MsalCacheRegistration`)

```csharp
// %LOCALAPPDATA%\LFI\shotAI\entra (R-ARCH-13). Never %LOCALAPPDATA%\shotAI: that is the Squirrel install
// root %LocalAppData%\shotai\ (paths are case-insensitive), deleted when Electron is uninstalled (EDGE-PKG-22).
var dir = Path.Combine(paths.LocalDataDirectory, "entra");                     // paths: IAppPaths (10 7.4.4)
var props = new StorageCreationPropertiesBuilder("msal-cache.bin", dir).Build(); // Windows: DPAPI CurrentUser; lock file next to it (name `msal-cache.bin.lockfile` UNVERIFIED, check the pinned package)
var helper = await MsalCacheHelper.CreateAsync(props);
try { helper.VerifyPersistence(); }
catch (MsalCachePersistenceException) // no variable: an unused one is CS0168, and warnings are errors
{
    log.LogInformation("entra: OS encryption unavailable, token cache is memory-only this session.");
    return; // do not register: memory-only (INV-AUTH-25); never WithUnprotectedFile()
}
helper.RegisterCache(pca.UserTokenCache);
```

- One `MsalCacheHelper` per process, created lazily on the first gateway, verified once; each gateway registers and, on dispose, unregisters its `UserTokenCache`.
- Cross-process lock: the helper's lock file serializes reads and writes across processes (closes the gap Electron's single-instance lock stood in for, `main.ts:161-167`). The single-instance lock (03) stays. IMPROVEMENT.
- Location `%LOCALAPPDATA%\LFI\shotAI\entra\msal-cache.bin` (`IAppPaths.LocalDataDirectory` + `entra`, R-ARCH-13, ARCHITECTURE 10.1; 12 Q-PKG-4 default adopted). Not `%LOCALAPPDATA%\shotAI\entra\`: that folder is inside the Squirrel install root that the Electron build's uninstall removes at cutover, which would sign every pilot user out (EDGE-PKG-22, AC-ARCH-7). Not roaming: under WAM the refresh token is device-bound and kept by the broker, so a roamed cache is useless elsewhere, and token material should not ride roaming profiles (the exposure `cache-plugin.ts:13-16` names). IMPROVEMENT. The Electron file `%APPDATA%\shotAI\entra-cache.bin` is neither read nor deleted (the Electron build stays installable as a rollback).
- What the file holds under WAM: account metadata, id tokens and access tokens (MSAL keeps them even with the broker; Microsoft Learn: "It's important to persist the MSAL token cache because MSAL continues to store ID tokens and account metadata there"). The refresh token lives in WAM.

Electron behaviors mapped:

| Electron (2.10) | Native | Class |
|---|---|---|
| encryption unavailable: memory-only, no read | `VerifyPersistence` fails: do not register | REQUIRED |
| absent file: no-op | helper treats absent as empty | REQUIRED |
| deserialize the decrypted string, not the result object | not applicable | ELECTRON-ONLY |
| `shouldReEncrypt` rotation | DPAPI keeps its master key history and decrypts with superseded keys; nothing to do | ELECTRON-ONLY |
| undecryptable or torn: miss | helper read failure yields an empty cache; pinned by `MsalCachePersistenceTests.GarbageCacheFileIsAMiss`. If the helper were ever observed to throw instead, wrap the registration in custom `SetBeforeAccessAsync`/`SetAfterAccessAsync` callbacks that catch and treat as a miss (fallback design; Q-AUTH-5) | REQUIRED |
| no write when unchanged | helper writes only when `HasStateChanged` | REQUIRED |
| never plaintext | never `WithUnprotectedFile` (source scan test) | REQUIRED [SECURITY] |
| failed write does not break sign-in | pinned by `MsalCachePersistenceTests.ReadOnlyCacheFileDoesNotFailSignInBookkeeping`; same fallback as above if violated | REQUIRED |
| never logs contents | MSAL logging at Warning with PII off; our lines are status only | REQUIRED |
| `clear()` (unused) | not provided | ELECTRON-ONLY |

### 7.7 Diagnostic exchange (Core)

```csharp
public sealed class FederationExchangeException(string message, int? status = null, string? requestId = null) : ShotAIException(message)
{ public int? Status { get; } = status; public string? RequestId { get; } = requestId; }

public sealed record MintedToken(string Token, DateTimeOffset ExpiresAt, double ExpiresInSeconds, string? Scope)
{ public override string ToString() => $"MintedToken {{ ExpiresAt = {ExpiresAt:O}, Scope = {Scope ?? "-"} }}"; } // never the token

public static class FederationExchange
{
    public const string BaseUrl = "https://api.anthropic.com";
    public const string TokenPath = "/v1/oauth/token";
    public const string GrantTypeJwtBearer = "urn:ietf:params:oauth:grant-type:jwt-bearer";
    public const int MaxAssertionBytes = 16 * 1024;
    public const string BetaHeaderValue = "oauth-2025-04-20,oidc-federation-2026-04-01"; // IMPROVEMENT, see below

    public static Task<MintedToken> ExchangeAsync(string assertion, FederationConfig cfg, HttpClient http,
        ILogger log, TimeProvider clock, CancellationToken ct, string baseUrl = BaseUrl);
}
```

Steps, each as 2.11 with these exact native choices:

1. `bytes = Encoding.UTF8.GetByteCount(assertion)` (a lone surrogate counts 3, as Node's replacement does). `assertion.Length == 0 || bytes == 0`: S4. `bytes > 16384`: S5 with `(int)Math.Ceiling(bytes / 1024.0)`.
2. Body: a `Utf8JsonWriter` writing, in order, `grant_type`, `assertion`, `federation_rule_id`, `organization_id`, `service_account_id`, then `workspace_id` only when `cfg.WorkspaceId` is non-null (it is never empty after validation). Encoder `JavaScriptEncoder.UnsafeRelaxedJsonEscaping` so the bytes equal `JSON.stringify`'s for this ASCII content. Content type `application/json` (no charset suffix; set `MediaTypeHeaderValue("application/json")` explicitly because `StringContent` adds `; charset=utf-8`; parity with the Electron header).
3. Header `anthropic-beta: oauth-2025-04-20,oidc-federation-2026-04-01`: IMPROVEMENT. The production exchange in the C# SDK sends exactly this (`WorkloadIdentityCredentials.cs` "Token exchange uses BOTH beta headers"), so the diagnostic leg now tests the request production makes. Electron's diagnostic sent none and was verified live without it; if a live run shows the header changes the answer, drop it (Q-AUTH-6).
4. `HttpRequestException`, `TaskCanceledException` not caused by `ct`, `IOException`: S6 with `e.Message` (the .NET transport message; its wording differs from Node's, accepted). Caller cancellation (`ct` fired) propagates as `OperationCanceledException` (IMPROVEMENT: a cancelable diagnostic).
5. `requestId`: the values of response header `request-id` joined with `, ` (fetch `Headers.get` semantics; in practice there is one), and `null` when the header is absent OR empty (an empty value is falsy in JavaScript, 2.11 step 5). `ms = elapsed via clock` (integer milliseconds).
6. Non-success: `detail = await ReadAsStringAsync()` with read failures giving `""`; log info `$"federation exchange failed: HTTP {status} in {ms}ms" + (requestId is null ? "" : $" request-id={requestId}") + $" ({detail.Length}-byte body withheld)"`; throw `FederationErrorText.Explain(status, requestId)`. "Ok" means `IsSuccessStatusCode` (200 to 299), as `fetch`'s `ok`.
7. Parse the body bytes with 01's `JsJson.Parse(ReadOnlySpan<byte>)` (it strips one UTF-8 BOM as `Response.json()` does and keeps `JSON.parse` semantics); `JsJsonException`: S7 with status and request id. (Corrected in WP-D4: the exchange works on `JsonNode`, so no `JsonElement` conversion is needed.)
8. `root` is `null` (the JSON `null` literal) or not a `JsonObject`: treat as `{}`. `token = root["access_token"]` when its `GetValueKind()` is `String`, else `""`. `expiresIn = root.TryGetPropertyValue("expires_in", out var n) ? JsValue.ToNumber(n) : double.NaN` (the property test stands in for JavaScript `undefined`, because a `JsonNode?` of `null` means the JSON `null` literal). `token.Length == 0 || !double.IsFinite(expiresIn) || expiresIn <= 0`: S8 with status and request id.
9. Log info `$"federated token minted in {ms}ms (expires_in={JsNumber.ToJsString(expiresIn)}s)."` and return `new MintedToken(token, clock.GetUtcNow().AddMilliseconds(expiresIn * 1000), expiresIn, root.scope is String ? value : null)`.

`JsValue.ToNumber(JsonNode? v)` in `ShotAI.Core.Json` (04's member, 04 7.1; ECMAScript `ToNumber` restricted to JSON values). This table is its full contract; 04's `EffectiveBlock` and this exchange both use it (Corrected in WP-D4: formerly an Auth-local `JsNumber.ToNumber(JsonElement?)`). Classify by `GetValueKind()`, never by the CLR type of the value (01 7.2.1):

| JSON value | Result |
|---|---|
| absent (undefined) | `NaN`, decided at the call site (`TryGetPropertyValue` false); `JsValue.ToNumber` itself never sees it |
| `null` (a `null` `JsonNode?`) | `0` |
| `true` / `false` | `1` / `0` |
| number | its `double` value |
| string | `JsString.Trim`; empty gives `0`; `Infinity`, `+Infinity`, `-Infinity` give the infinities; `0x`/`0X` + hex digits, `0o`/`0O` + octal, `0b`/`0B` + binary (no sign allowed) give that integer; otherwise it must match `^[+-]?([0-9]+\.?[0-9]*([eE][+-]?[0-9]+)?|\.[0-9]+([eE][+-]?[0-9]+)?)\z` and parses with `double.Parse(..., CultureInfo.InvariantCulture)`; anything else `NaN` |
| array | empty gives `0`; one element gives `ToNumber` of that element's JavaScript string form (`null` element gives `""` so `0`; a nested one-element array recurses); two or more gives `NaN` |
| object | `NaN` |

`JsNumber.ToJsString(double)` is spec 01's member (01 7.2.2, ECMAScript `Number::toString`); this spec only calls it.

`FederationErrorText.Explain(int status, string? requestId)` returns `FederationExchangeException` with exactly S9 to S14. `FederationErrorText.ForSdk(WorkloadIdentityException e)` (for 07's `SopErrorMapper.Map`, INV-AUTH-24), first match wins, each rule verified against how `WorkloadIdentityCredentials` constructs its exceptions (`anthropic-sdk-csharp:src/Anthropic/Credentials/WorkloadIdentityCredentials.cs:62-189`, `SecurityHelpers.cs:106-170`):

| # | Condition | Result | SDK source of that shape |
|---|---|---|---|
| 1 | the inner chain contains `SignInRequiredException s` | `s.Message` (S1) | `"Failed to obtain identity token."` wrapping the provider's exception |
| 2 | `e.StatusCode is { } sc` | `Explain((int)sc, null).Message` (S9 to S13, no request id) | `"Token exchange failed with status N: ..."` |
| 3 | `e.InnerException is HttpRequestException h` | `$"Could not reach Anthropic to sign in ({h.Message})."` (S6) | `"Failed to connect to token endpoint."` |
| 4 | `e.InnerException is OperationCanceledException o` | `$"Could not reach Anthropic to sign in ({o.Message})."` (S6) | `"Token endpoint request timed out."` (the 60 s `Exchange.Timeout`) |
| 5 | `e.InnerException is JsonException`, or `e.InnerException is null` | `Anthropic returned an unreadable sign-in response.` (S7) | non-JSON 2xx; missing or null `access_token`; bad `expires_in`; non-Bearer `token_type` |
| 6 | any other inner exception | `e.InnerException.Message` (the identity token provider failed, for example an MSAL service or network error; parity with Electron's raw-message fallback) | `"Failed to obtain identity token."` |

The SDK's own `Message` (which embeds a redacted body and configuration advice) is never returned. IMPROVEMENT (TS SDK errors fell through to their raw message). Note rule 5 folds the SDK's "missing field" failures into S7 rather than S8 because the SDK does not distinguish them by type; the diagnostic leg still reports S8 for its own checks.

### 7.8 Advisory role check (Core, `JwtRoles`)

```csharp
public const string RequiredAppRole = "shotAI.User";
public static bool? HasAppRole(string jwt, string role)
```

1. `parts = jwt.Split('.')`; `parts.Length < 2 || parts[1].Length == 0`: `null`.
2. `b64 = parts[1].Replace('-', '+').Replace('_', '/')`; truncate at the first `=` (Node stops decoding there, EDGE-AUTH-38); remove every remaining character outside `[A-Za-z0-9+/]` (Node skips them); a remainder of 1 modulo 4 drops the final character (Node discards a dangling sextet); append `=` to a multiple of 4. `Convert.FromBase64String`; failure: `null` (it cannot fail after this normalization, but keep the guard).
3. `Encoding.UTF8.GetString` (replacement characters for invalid sequences, as Node).
4. `JsonDocument.Parse`; failure: `null`. Root `null` literal: `null` (reading `.roles` of null throws in JS). Root not an object (number, string, array, boolean): `false`.
5. Property `roles` (last duplicate wins, as `JSON.parse`): absent or not an array: `false`. Else `true` when any element has kind `String` and `string.Equals(value, role, StringComparison.Ordinal)`, else `false`.

REQUIRED.

### 7.9 Credential decision and client factory (Core, `AnthropicClientFactory`)

```csharp
// The only Anthropic client factory (R-ARCH-15). IAnthropicClient is the SDK's interface
// (anthropic-sdk-csharp:src/Anthropic/IAnthropicClient.cs:23); the concrete type is AnthropicClient.
public interface IAnthropicClientFactory
{
    // Throws SignInRequiredException (S1 or S2). The caller owns and disposes the client.
    Task<(IAnthropicClient Client, AuthMode Mode)> CreateAsync(CancellationToken ct);
}

// internal sealed class AnthropicClientFactory : IAnthropicClientFactory (7.1 visibility)
public async Task<(IAnthropicClient, AuthMode)> CreateAsync(CancellationToken ct)
{
    var cfg = await _federation.GetAsync(ct);
    var signedIn = cfg is not null && await _entra.SignedInAccountAsync(cfg, ct) is not null;
    var key = signedIn ? null : await _apiKeys.GetApiKeyAsync(ct);
    if (!signedIn && key is null)
        throw new SignInRequiredException(cfg is not null
            ? "Sign in with your Microsoft account to use Claude."
            : "Add an Anthropic API key in Settings to use Claude.");

    // ClientOptions is a record struct (EDGE-AUTH-45): build it in this one local and hand it over once.
    var options = new ClientOptions
    {
        BaseUrl = FederationExchange.BaseUrl,        // explicit: ANTHROPIC_BASE_URL can never apply (INV-AUTH-15)
        ApiKey = signedIn ? null : key,              // explicit even when null: blocks env and profile auto-resolution
        AuthToken = null,                            // explicit: blocks ANTHROPIC_AUTH_TOKEN
        Timeout = TimeSpan.FromMinutes(10),          // 07 7.6's value, carried here by R-ARCH-15 (parity with the TS SDK default)
        MaxRetries = null,                           // SDK default 2 (parity, 07 7.6)
        HttpClient = _http.CreateApiClient(),        // per-client wrapper over the shared handler (7.14)
        // NEW instances per client: the SDK refuses a handler whose InnerHandler is already set
        // (ClientOptions.cs:69-88, :324-350). The first entry is the outermost (R-ARCH-15, INV-AUTH-35).
        Handlers = [new AnthropicHostGuardHandler(), new ResponseHeaderCaptureHandler()],
    };
    if (signedIn)
        options.Credentials = new WorkloadIdentityCredentials(new WorkloadIdentityOptions
        {
            FederationRuleId = cfg!.FederationRuleId,
            OrganizationId = cfg.OrganizationId,
            ServiceAccountId = cfg.ServiceAccountId,
            WorkspaceId = cfg.WorkspaceId,           // null when not configured: the field is omitted on the wire
            IdentityTokenProvider = new EntraIdentityTokenProvider(_entra, cfg),
            BaseUrl = FederationExchange.BaseUrl,
            HttpClient = _http.Exchange,             // long-lived, host-guarded (7.14), not owned by the credentials
        });
    return (new AnthropicClient(options), signedIn ? AuthMode.Federated : AuthMode.ApiKey);
}

sealed class EntraIdentityTokenProvider(EntraSession entra, FederationConfig cfg) : IIdentityTokenProvider
{
    // Silent ONLY (INV-AUTH-12). Called by the SDK TokenCache, sometimes from a background task.
    public Task<string> GetIdentityTokenAsync(CancellationToken ct) => entra.AcquireSilentAsync(cfg, forceRefresh: false, ct);
}
```

How the SDK then behaves (verified in the SDK source, REQUIRED to rely on and pinned by tests):

- `AnthropicClient` wraps `Credentials` in its `TokenCache` (`anthropic-sdk-csharp:src/Anthropic/AnthropicClient.cs:236-244`): a cached token with more than 120 s left is served; 30 to 120 s left starts one background refresh (skipped within 5 s of a failed one) and serves the cached token; 30 s or less blocks on a refresh (`anthropic-sdk-csharp:src/Anthropic/Credentials/TokenCache.cs:30-82`). Refreshes are coalesced by a semaphore. A 401 on an API call force-refreshes once and retries once when the token changed (`anthropic-sdk-csharp:src/Anthropic/AnthropicClient.cs:418-458`). Expiry is wall-clock (`DateTimeOffset.UtcNow`), correct across suspend.
- `WorkloadIdentityCredentials.GetTokenAsync` ignores `forceRefresh` and always exchanges; the identity token provider is called with the SDK's token, or `CancellationToken.None` from the background refresh (`anthropic-sdk-csharp:src/Anthropic/Credentials/WorkloadIdentityCredentials.cs:53-60`, `anthropic-sdk-csharp:src/Anthropic/Credentials/TokenCache.cs:153-160`). A provider exception other than `OperationCanceledException` or `WorkloadIdentityException` is wrapped as `WorkloadIdentityException("Failed to obtain identity token.", inner)` (INV-AUTH-24).
- `expires_in` absent in the exchange response gives a token with no expiry, which the cache serves until a 401 (`anthropic-sdk-csharp:src/Anthropic/Credentials/SecurityHelpers.cs:122-150`); the diagnostic leg rejects that response (S8), so Test connection exposes it (Q-AUTH-7).
- When token credentials are active the client strips any `X-Api-Key` and `Authorization` added from defaults and sets `Authorization: Bearer <minted>` plus `anthropic-beta: oauth-2025-04-20` (`anthropic-sdk-csharp:src/Anthropic/AnthropicClient.cs:520-545`). "Active" means `Credentials` is set and neither `ApiKey` nor `AuthToken` is explicitly non-null (`:499-517`), which is why the federated branch sets both to `null`.
- The token refresh runs inside the request attempt: `BeforeSend` receives the attempt's linked token (the caller's token plus the SDK's per-attempt timeout, default 10 minutes), so the identity token provider sees that token, not the caller's (`:628-641`). A `WorkloadIdentityException` thrown there is not retried (`ShouldRetry` retries only `IOException`, `AnthropicIOException` and `OperationCanceledException`, `:792-808`) and reaches 07 directly.
- `ClientOptions.Handlers` wrap the SDK's passthrough handler, the first entry outermost (`anthropic-sdk-csharp:src/Anthropic/Core/ClientOptions.cs:324-350`), so `AnthropicHostGuardHandler` sees the final request (after `BeforeSend`): it throws `InvalidOperationException("Refusing to contact a host other than api.anthropic.com.")` unless `request.RequestUri.Scheme == "https"`, `Host` equals `api.anthropic.com` (ordinal ignore case) and `Port == 443`, and removes nothing (07's stricter rule, adopted by R-ARCH-15; INV-AUTH-35). This one class replaces 07's `EgressPinHandler` (superseded). `ResponseHeaderCaptureHandler` is 07's type (`ShotAI.Core.Sop.Transport`, 07 7.6): inert unless `ResponseHeaderCapture.Current` is set around an SDK call, so attaching it to every client, including the connection test's, changes nothing where no capture is active. The shared `SocketsHttpHandler` carries no Anthropic-specific handler, because it also serves MSAL, the update check and GitHub (R-ARCH-15). That `ClientOptions.Handlers` observe every retry attempt's response (the 429 classifier needs the last one) is UNVERIFIED and pinned by 07's `RateLimitHeaderCaptureTests.HeadersReachClassifierThroughSdk`; if it fails, the capture moves into the per-client `HttpClient` chain returned by `CreateApiClient`, still per client. The SDK disposes attached handlers with the client (`ClientOptions.cs:364-379`).

Per-call construction is parity (EDGE-AUTH-20). The caller (07's `ClaudeService`, or `ConnectionTester` for the API leg) disposes the client after the estimate, generation or test: `AnthropicClient.Dispose` disposes the `TokenCache`, the `WorkloadIdentityCredentials` (which does not dispose a passed-in `HttpClient`) and the client's `HttpClient` wrapper, never the shared handler (7.14).

### 7.10 Connection test (Core, `ConnectionTester`)

```csharp
public sealed record TestConnectionResult(bool Ok, AuthMode? Mode = null, string? Model = null, string? Error = null, ConnectionLeg? Leg = null);
public enum ConnectionLeg { SignIn, Exchange, Api }

public async Task<TestConnectionResult> TestAsync(CancellationToken ct)
```

Implements the 2.14 step table literally, with these native specifics:

- SOP settings come from 10's `ISettingsService.Current.Sop` (07's `SopSettings`: `Enabled`, `Model`), a lock-bounded snapshot read once at the start of the test (11 INV-IPC-6). Disabled: `new(false, Error: "AI SOP generation is turned off.")`. (Corrected in WP-D4: formerly an undefined `ISopSettingsReader.GetAsync`.)
- Leg 1 calls `EntraSession.AcquireSilentAsync(cfg, forceRefresh: false, ct)`; config vanished between the check and the leg gives `SignInRequiredException("shotAI is not set up for Microsoft sign-in on this machine.")`.
- Leg 2 calls `FederationExchange.ExchangeAsync(assertion, cfg, _http.Exchange, log, clock, ct)`; the minted token is dropped immediately.
- Leg 3 and the key path: `var (client, builtMode) = await _factory.CreateAsync(ct); using (client) { await SopModelProbe.RetrieveAsync(client, model, ct); }` (07's internal API-leg seam around `client.Models.Retrieve`, 07 7.6; R-ARCH-3: `IClaudeService` has no connection-test member, and `IAuthService.TestConnectionAsync` is the only public entry point). A `ValueTuple` is not `IDisposable`, so `using var built = ...` on the tuple does not compile; dispose the client itself. On the key path `mode` is assigned `builtMode` only after `CreateAsync` returns, so a throw from it keeps `ApiKey` (2.14 K2).
- Error text: 07's `SopErrorMapper.Map(exception, mode, headers: null)` (07 7.12; the test's models call attaches no `ResponseHeaderCapture`, so 07's row 11 classifies a 429 with `RateLimitClassifier.Message(null, mode)`), which applies INV-AUTH-24 through `FederationErrorText.ForSdk`. (Corrected in WP-D4: formerly `07.FriendlyError`.)
- Log lines exactly as 2.14. `OperationCanceledException` from `ct` propagates (the UI never cancels today).
- The result type is what 06 maps to `legLabel` (`SignIn` to `Microsoft sign-in`, `Exchange` to `Claude access`, `Api` to `Claude API`).

REQUIRED.

### 7.11 API key storage (Core `ApiKeyStore`, Platform `DpapiSecretProtector`)

```csharp
public enum ApiKeySource { Stored, Env, None }
public sealed record ApiKeyStatus(bool HasKey, ApiKeySource Source, bool EncryptionAvailable, bool HasStoredCiphertext);

public interface ISecretProtector { bool IsAvailable(); byte[] Protect(byte[] plain); byte[] Unprotect(byte[] cipher); }

internal interface IApiKeyStore                          // internal to Core (R-ARCH-2); the UI uses IAuthService's key members
{
    Task<string?> GetApiKeyAsync(CancellationToken ct);   // for the factory only; never exposed to the UI facade
    Task<ApiKeyStatus> GetStatusAsync(CancellationToken ct);
    Task SetAsync(string key, CancellationToken ct);
    Task ClearAsync(CancellationToken ct);
}
```

- File: `Path.Combine(IAppPaths.UserDataDirectory, "secrets.dpapi.json")`, that is `%APPDATA%\shotAI\secrets.dpapi.json` (roaming, beside `settings.json`; ARCHITECTURE 10.1; `ApiKeyStore` never composes the folder itself, 10.2), JSON `{"apiKey":"<base64 of DPAPI ciphertext>"}`; cleared state `{}`. A different name from Electron's `secrets.json` so the two builds never read each other's ciphertext (EDGE-AUTH-33). IMPROVEMENT. Electron's file is never read, written or deleted.
- `DpapiSecretProtector`: `ProtectedData.Protect(plain, Entropy, DataProtectionScope.CurrentUser)` with `static readonly byte[] Entropy = "shotAI.apiKey.v1"u8.ToArray()` (a UTF-8 literal is a `ReadOnlySpan<byte>`; the `byte[]` overloads need an array). `IsAvailable()` runs a protect/unprotect round trip of a fixed 16-byte probe once per process and caches a success (a failure is re-probed on the next call); DPAPI fails, for example, under some mandatory or temporary profiles. The primitive change (OSCrypt to DPAPI) is IMPROVEMENT (no `Local State` dependency, so "undecryptable after `Local State` reset" disappears).
- Read: `File.ReadAllBytes`, parse; `apiKey` kept only when a JSON string; any read or parse failure gives `{}` (REQUIRED). Decrypt: protector unavailable gives `null` silently; else base64-decode, `Unprotect`, UTF-8 decode, `JsString.Trim`, empty gives `null`; any exception gives `null` and logs warn `secrets: stored key could not be decrypted`.
- `GetApiKeyAsync`: stored and decryptable, else `JsString.Trim(Environment.GetEnvironmentVariable("ANTHROPIC_API_KEY") ?? "")` when non-empty, else `null`.
- `GetStatusAsync`: exactly INV-AUTH-28 (`HasStoredCiphertext = !string.IsNullOrEmpty(apiKey)`).
- `SetAsync`: `trimmed = JsString.Trim(key)`; empty: `ApiKeyStoreException("API key is empty.")`; unavailable: `ApiKeyStoreException(S17)` (INV-AUTH-36: an `ArgumentException` would be shown as 11's generic text); write `{"apiKey": base64(Protect(UTF8(trimmed)))}` via 01's `AtomicFile.WriteAsync` (retry log `$"secrets rename {code} \u2014 retrying (lock likely transient)"`); log info `API key saved (encrypted).`; `AuthService` then raises `AuthStatusChanged`. The write goes through `AtomicFile` directly, not through a `SerialWriteQueue`, and is awaited (not optimistic, ARCHITECTURE 7.5 "API key set and clear"); the exit flush does not cover it, and because the write is atomic an exit or crash during it leaves the old or the new file, never a torn one (ARCHITECTURE 7.10, 7.11).
- `ClearAsync`: write `{}` atomically; log info `Stored API key cleared.`; `AuthService` then raises `AuthStatusChanged`.
- Writes are serialized by a `SemaphoreSlim(1,1)`; reads do not take it (an atomic replace never exposes a torn file). Not optimistic (INV-AUTH-29).
- The key string is held in memory only for the duration of client construction; `ApiKeyStore` never caches it. The UI's key field is a WPF `PasswordBox` (06).

### 7.12 The UI facade and status (Core, `AuthService`)

```csharp
public enum AuthStatusMode { None, Federated, ApiKey }
public enum AuthMode { Federated, ApiKey }
public sealed record AuthStatus(AuthStatusMode Mode, bool FederationAvailable, bool SignedIn, string? Account,
    bool EncryptionAvailable, bool HasStoredCiphertext, string? SupportUrl);

// Canonical (R-ARCH-2): 07's former IAuthService sketch, its VerifyFederationAsync and its IApiKeyStore are superseded.
public interface IAuthService
{
    Task<AuthStatus> GetStatusAsync(CancellationToken ct);
    Task<SignInOutcome> SignInAsync(nint ownerWindow, CancellationToken ct);
    Task SignOutAsync(CancellationToken ct);
    Task<ApiKeyStatus> GetApiKeyStatusAsync(CancellationToken ct);
    Task SetApiKeyAsync(string key, CancellationToken ct);
    Task ClearApiKeyAsync(CancellationToken ct);
    Task<TestConnectionResult> TestConnectionAsync(CancellationToken ct);   // the one connection-test entry point (R-ARCH-3)
    event EventHandler? AuthStatusChanged;   // raised after sign-in completes, sign-out, key set, key clear
}
```

- `AuthStatusChanged` is raised on the thread that completed the change, outside any lock, through 11's `EventRaiser.Raise` (ARCHITECTURE T5); subscribers follow the one template of ARCHITECTURE 5.6 (subscribe, then read; marshal with `IUiDispatcher.Post`, T6 and T7).
- `TestConnectionAsync` never throws for an expected failure (it returns `TestConnectionResult`, 2.14; ARCHITECTURE 8.2 "never throw" services); only caller cancellation propagates.

- `GetStatusAsync`: `_federation.Invalidate()` first (INV-AUTH-9), then exactly the 2.16 computation; `Account = account?.Username` (an empty username stays `""`, parity).
- `SignInAsync`: `cfg = await _federation.GetAsync(ct)`; `null`: `FederationNotConfiguredException()` (S3); else `EntraSession.SignInInteractiveAsync(cfg, ownerWindow, ct)`; on `Completed` raise `AuthStatusChanged`. Returns the outcome, never a token.
- `SignOutAsync`: `cfg` `null`: no-op; else `EntraSession.SignOutAsync`; raise `AuthStatusChanged`. The API key is untouched (INV-AUTH-30).
- `AuthStatusChanged` lets the SOP panel (07) and Settings refresh without polling; 05/07 also keep a refresh on window activation (the policy key can change while the app runs). IMPROVEMENT over the focus-only re-read (EDGE-AUTH-25).
- The UI assemblies depend only on `IAuthService` and the value types (INV-ARCH-3, enforced by `Composition.ViewModelDependencyTests`). `IApiKeyStore` is `internal` to Core (R-ARCH-2); `IAnthropicClientFactory`, `EntraSession` and `FederationExchange` are consumed only by Core services (07 and `ConnectionTester`) and the probe tool, and are public only for the probe (7.1 visibility; `InternalsVisibleTo` names only `ShotAI.Core.Tests`, INV-ARCH-6) (INV-AUTH-19, INV-AUTH-31).

### 7.13 Request access allowlist (Core, `SupportUrlAllowlist`)

```csharp
public interface ISupportUrlAllowlist { Task<bool> IsAllowedAsync(Uri candidate, CancellationToken ct); }
```

`IsAllowedAsync`: `candidate.Scheme != "https"`: `false`; `cfg = await _federation.GetAsync(ct)` (cached, not invalidated; parity); `cfg?.SupportUrl` `null`: `false`; `Uri.TryCreate(cfg.SupportUrl, UriKind.Absolute, out var s)` fails: `false`; else `Origin(s) == Origin(candidate)` where `Origin(u) = (u.Scheme.ToLowerInvariant(), u.IdnHost.ToLowerInvariant(), u.Port)` (`Port` is the effective port, so an omitted 443 equals an explicit 443, as in WHATWG origins). 10 consults it only after its base rules refuse and logs the refusal line of 2.17 with the candidate's origin (`scheme://host[:port]`). REQUIRED [SECURITY].

### 7.14 Networking (Core, `SharedHttp`)

```csharp
public interface ISharedHttp : IDisposable   // singleton; Dispose at exit (ARCHITECTURE C5)
{
    HttpClient Exchange { get; }           // long-lived; diagnostic exchange and WorkloadIdentityCredentials; Timeout = 60 s;
                                           // new HttpClient(new AnthropicHostGuardHandler { InnerHandler = handler }, disposeHandler: false)
    HttpClient CreateApiClient();          // new HttpClient(handler, disposeHandler: false) { Timeout = InfiniteTimeSpan } per AnthropicClient
    HttpMessageHandler Handler { get; }    // the bare shared SocketsHttpHandler: MSAL's IMsalHttpClientFactory, 10's update check
}
```

- The shared `SocketsHttpHandler` carries NO Anthropic-specific handler (R-ARCH-15): it also serves MSAL, the update check and GitHub. Anthropic egress is guarded per client instead: every `AnthropicClient` gets fresh handlers in `ClientOptions.Handlers` (7.9), and the long-lived `Exchange` client has its own `AnthropicHostGuardHandler` over the shared handler (the SDK's `WorkloadIdentityCredentials` calls `HttpClient.SendAsync` on it directly, so it never passes through `ClientOptions.Handlers`, `anthropic-sdk-csharp:src/Anthropic/Credentials/WorkloadIdentityCredentials.cs:114-116`). The `Exchange` client is disposed only by `SharedHttp.Dispose` at exit, which then disposes the handler.

- One `SocketsHttpHandler { UseProxy = true, Proxy = null (HttpClient.DefaultProxy: the Windows system proxy, including WPAD/PAC), DefaultProxyCredentials = CredentialCache.DefaultCredentials, AutomaticDecompression = DecompressionMethods.All, PooledConnectionLifetime = TimeSpan.FromMinutes(5) }` for the process. `HttpClient.DefaultProxy` on Windows reads the `HTTPS_PROXY`, `HTTP_PROXY`, `ALL_PROXY` and `NO_PROXY` environment variables before the system settings, so "the system proxy" holds only because the composition root removes those variables (and their lower-case spellings) at startup step 5, before the first `HttpClient` exists, logging the names removed at Warning and never the values (ARCHITECTURE 4.2 step 5, 9.4, interpretation I-5; default of Q-ARCH-3, pending decision). On Windows TLS validation uses the Windows certificate store, so enterprise TLS-inspection roots work. This now covers ALL traffic (API, SSE streaming, exchange, MSAL's non-broker calls), where Electron covered only sign-in and the exchange. IMPROVEMENT (feasibility "Networking"); `DefaultProxyCredentials` additionally makes integrated-auth proxies work (IMPROVEMENT).
- Per-client wrappers use `disposeHandler: false` because the SDK's `HttpClientPassthroughHandler` disposes its `HttpClient` when the client is disposed (`anthropic-sdk-csharp:src/Anthropic/Core/HttpClientPassthroughHandler.cs:33-41`); disposing the shared handler would break every later request. REQUIRED for correctness.
- `CreateApiClient` sets `Timeout = InfiniteTimeSpan` as the SDK's own default client does (`anthropic-sdk-csharp:src/Anthropic/Core/ClientOptions.cs:25-30`), so the SDK's timeout logic governs streaming.
- `Exchange.Timeout = 60 s`: IMPROVEMENT (Electron's exchange had no timeout; a hung proxy must not hang Test connection forever; 60 s exceeds any observed exchange by two orders of magnitude). A timeout surfaces as S6.
- `MsalHttpClientFactory : IMsalHttpClientFactory` returns one `new HttpClient(Handler, disposeHandler: false)`. Under WAM most token traffic runs in the OS broker and uses OS networking, which already honors the system proxy.
- Ownership: 08 defines it because 08 had the Electron-side proxy requirement; it lives in `ShotAI.Core.Net` and is registered by `AddShotAICore` (ARCHITECTURE 2.4, 4.3); 10 (update check) consumes `Handler`, and 07 consumes it only through the factory (07 no longer builds clients, R-ARCH-15) (Q-AUTH-13).

### 7.15 Threading, cancellation and disposal

| Operation | Thread | Cancellation | Notes |
|---|---|---|---|
| `GetStatusAsync` | thread pool (registry, DPAPI, cache file under the MSAL lock) | honored before each await; the shared config task ignores it | the Settings view model awaits it from the UI thread without `ConfigureAwait(false)` (ARCHITECTURE T4); the work runs on the pool because Core awaits with `ConfigureAwait(false)` (T2), so the UI thread never waits on it (T9) |
| `SignInAsync` | caller on the UI thread; the MSAL interactive call is dispatched to the UI thread by `MsalGateway` through `IUiDispatcher.InvokeAsync` (DL5) | passed to MSAL; the UI does not cancel | never `ConfigureAwait(false)` inside `MsalGateway.AcquireInteractiveAsync` before the dispatch (T3, the per-file `CA2007` suppression of `MsalGateway.cs`) |
| silent acquisition | any thread, including the SDK's background refresh task with `CancellationToken.None` | honored | WAM silent calls never show UI |
| `TestConnectionAsync` | thread pool | honored | |
| key set and clear | thread pool, serialized | honored before the write starts | |
| gateway rebuild | under the session semaphore | | the old gateway is disposed after the new one is published |

`AuthService`, `EntraSession`, `SharedHttp` and the cache registration are DI singletons disposed at shutdown by the synchronous `provider.Dispose()` of `App.OnExit` (ARCHITECTURE 4.5 step 5). Each disposable one implements `IDisposable` (optionally also `IAsyncDisposable`), because the container throws for a resolved service that implements only `IAsyncDisposable`, and no `Dispose` needs the UI thread (ARCHITECTURE C5, DL4, R-ARCH-10). Nothing auth-related is written by the exit flush (4.5 step 3 drains only the project and settings queues). No auth type starts a timer or a background loop.

### 7.16 Persistence

| File | Location | Content | Written by | Electron counterpart |
|---|---|---|---|---|
| MSAL cache | `%LOCALAPPDATA%\LFI\shotAI\entra\msal-cache.bin` (`IAppPaths.LocalDataDirectory`, R-ARCH-13; plus the extension's lock file) | DPAPI-protected MSAL cache (accounts, id and access tokens) | MSAL extension | `%APPDATA%\shotAI\entra-cache.bin` (untouched) |
| API key | `%APPDATA%\shotAI\secrets.dpapi.json` (`IAppPaths.UserDataDirectory`) | `{"apiKey":"<base64 DPAPI>"}` or `{}` | `ApiKeyStore` via `AtomicFile` | `%APPDATA%\shotAI\secrets.json` (untouched) |
| Policy | HKLM key | eight `REG_SZ` | administrators only; shotAI never writes it; the installer never writes it | same |
| Baked config | inside `shotAI.exe` | JSON manifest resource | build | inlined literal |

Nothing auth-related is written to `settings.json`. Nothing native is kept under the Squirrel root `%LocalAppData%\shotai\` (12 INV-PKG-24, EDGE-PKG-22), so uninstalling Electron at cutover signs nobody out (ARCHITECTURE AC-ARCH-7). After cutover each user signs in once (usually a single WAM confirmation) or re-enters the key (feasibility "Credentials don't carry over"; Q-AUTH-2 for an optional key import).

### 7.17 Error handling

| Source | Native type | Surfaces as |
|---|---|---|
| No credential | `SignInRequiredException` S1 or S2 | 07 shows the message (SOP panel and Test connection) |
| Silent needs interaction | `SignInRequiredException` (claims remembered) | S1; the user clicks Sign in |
| Interactive canceled | `SignInOutcome.Canceled` | nothing (EDGE-AUTH-11) |
| Interactive failed | `EntraSignInFailedException` (MSAL's `Message` verbatim) | 06 `Error: <message>` |
| Broker redirect URI not registered | `EntraSignInFailedException` S18 + ` Details: ` + MSAL's `Message` (D23) | 06 `Error: <message>` |
| No owner window | `EntraSignInFailedException` `shotAI had no window to present sign-in from.` | 06 `Error: <message>` |
| Sign in with no config | `FederationNotConfiguredException` S3 | 06 `Error: <message>` |
| Diagnostic exchange | `FederationExchangeException` S4 to S14 | Test connection `Claude access:` |
| SDK exchange | `WorkloadIdentityException` | `FederationErrorText.ForSdk` (INV-AUTH-24) |
| Key save | `ApiKeyStoreException` S16 or S17, I/O exceptions | 06 `Error: <message>`; I/O text follows 11's `UserMessage.From` (an `IOException` shows its OS message) |
| Policy, baked, cache read failures | none (swallowed by design) | status only |

### 7.18 Logging

Category `claude` (10) for every line in 2.20 that is `claude` in Electron, with identical text (10 7.5.3 maps `ShotAI.Core.Auth` and `ShotAI.Platform.Auth` to `claude`, ARCHITECTURE 8.4); the `openExternal` refusal line belongs to 10's link service and its category. MSAL's own log is bridged at `Warning` and above with `enablePiiLogging: false`, prefixed `msal: `, and never includes tokens (MSAL guarantees it with PII off). The UPN never appears in a log line (INV-AUTH-31). Native-only lines (D23, category `claude`): warn `entra: interactive sign-in failed ({ExceptionType}, {ErrorCode}).` and, in `SHOTAI_NO_BROKER` test builds only, info `entra: broker disabled in this build (test build).` Channel-name dev logging (`ipc: auth:status`) is ELECTRON-ONLY.

### 7.19 The probe (developer tooling)

`dotnet/tools/ShotAI.WifProbe` (Windows console app, not shipped; references Core and Platform and sits in the `/tools/` folder of `ShotAI.slnx`, ARCHITECTURE 2.1) builds its own container with `AddShotAICore` and `AddShotAIPlatform` (it cannot see Core internals, INV-ARCH-6) and drives the real Core services. It supplies its own `IAppPaths` whose `UserDataDirectory` and `LocalDataDirectory` are a fresh temporary folder deleted at exit, so every run starts with an empty MSAL cache (the Electron probe's memory-only cache) and no stored key; a `--use-app-cache` switch points `LocalDataDirectory` at the app's `%LOCALAPPDATA%\LFI\shotAI` instead, for AC-AUTH-22 (R-ARCH-13). It removes `ANTHROPIC_API_KEY` from its own process environment before the first resolution, so no key can decide the test (the Electron probe's `getApiKey: async () => null`; `IApiKeyStore` is internal, R-ARCH-2). It prints legs 0 to 4 exactly as 2.19 (same hints, same facts: `aud`, `tid`, `iss`, lifetime, assertion size, role, mint binding, scope, the 0-extra-exchange check measured with a counting `DelegatingHandler`). It keeps the `WIF_PROBE_CLI_CLIENT=1` fallback with the Azure CLI's well-known public client id taken from the Electron probe constant. REQUIRED as tooling before the pilot (it is how the native path is proven against the live tenant); it adds a project to the solution (12, ARCHITECTURE 2.1 and 13.5). It must never print a value from the local config file except to the console of the developer running it, and never the assertion or token beyond the 14-character prefix.

### 7.20 Divergences from Electron

| # | Divergence | Class | Justification |
|---|---|---|---|
| D1 | WAM broker instead of the system browser loopback | IMPROVEMENT | device-bound refresh tokens, native Conditional Access and device compliance, Windows Hello and FIDO, SSO with the Windows account; the browser remains MSAL's automatic fallback |
| D2 | MSAL extension cache with a cross-process lock, DPAPI, in `%LOCALAPPDATA%\LFI\shotAI\entra\` (R-ARCH-13, ARCHITECTURE D-ARCH-2) | IMPROVEMENT | closes the lock gap; no roaming of token material; outside the Squirrel root that the Electron uninstall deletes (EDGE-PKG-22) |
| D3 | API key in DPAPI under `secrets.dpapi.json` | IMPROVEMENT | OS primitive without `Local State`; separate name protects the rollback build |
| D4 | Registry API instead of `reg.exe` | IMPROVEMENT | no process spawn, exact strings; same filter semantics |
| D5 | Embedded resource instead of an inlined literal | ELECTRON-ONLY mechanism, same intent | |
| D6 | All HTTP on the system-proxy handler with default proxy credentials | IMPROVEMENT | API and SSE traffic now work behind TLS inspection and authenticating proxies |
| D7 | Gateway rebuilt on `TenantId` or `ClientAppId` change; scope computed per call | IMPROVEMENT | fixes EDGE-AUTH-8 |
| D8 | One cached account after interactive sign-in | IMPROVEMENT | fixes EDGE-AUTH-13 |
| D9 | Forced silent refresh after interactive success | IMPROVEMENT | guards the stale-roles trap under WAM (Q-AUTH-4) |
| D10 | Claims challenge replayed on the next interactive sign-in | IMPROVEMENT | EDGE-AUTH-10 |
| D11 | Cancel is not an error | IMPROVEMENT | EDGE-AUTH-11 |
| D12 | Diagnostic exchange sends the SDK's beta header, has a 60 s timeout and is cancelable | IMPROVEMENT | tests what production sends; no hang (Q-AUTH-6) |
| D13 | `WorkloadIdentityException` mapped to the `explain` wording | IMPROVEMENT | never shows an SDK message with a redacted body |
| D14 | `AuthStatusChanged` event | IMPROVEMENT | replaces focus polling as the primary refresh (EDGE-AUTH-25) |
| D15 | Faulted config resolution is not cached | IMPROVEMENT | defensive |
| D16 | `MintedToken`, `FederationConfig` redacted `ToString` | IMPROVEMENT | macOS lesson (INV-AUTH-19) |
| D17 | No IPC channels, `asString` validation, preload bridge, dev channel logging | ELECTRON-ONLY | replaced by the typed `IAuthService` surface |
| D18 | The network module, the `safeStorage` plugin and its re-encrypt path | ELECTRON-ONLY | replaced by WAM, MSAL.NET and DPAPI |
| D19 | User-facing failures are `ShotAIException` subclasses (`FederationNotConfiguredException`, `ApiKeyStoreException`, `EntraSignInFailedException`) | IMPROVEMENT (mechanism; texts unchanged) | 11 X2: other exception types show generic text (INV-AUTH-36) |
| D20 | `ANTHROPIC_CUSTOM_HEADERS` cleared at startup (ARCHITECTURE 4.2 step 5) and a host guard handler (`https`, `api.anthropic.com`, port 443) fresh per client and on the exchange client, none on the shared handler (R-ARCH-15) | IMPROVEMENT [SECURITY] | a C# SDK environment input that could inject headers (INV-AUTH-35) |
| D21 | Baked file: one UTF-8 BOM stripped; a build warning for a passed but missing file | IMPROVEMENT | Electron silently shipped `{}` (EDGE-AUTH-37) |
| D22 | Memory-only cache logged once; the SDK-rejectable exchange response logged at Test connection; the probe's `--effective` mode | IMPROVEMENT | EDGE-AUTH-40, 42, 43 |
| D23 | A missing broker redirect URI is reported with S18 (MSAL's message appended); interactive MSAL failures log type and code at warn | IMPROVEMENT | the likeliest first-run failure on a pilot tenant is an administrator step (Q-AUTH-3), and MSAL's text alone does not say which URI to add; no config value enters the text (INV-AUTH-6) |

---

## 8. Tests

The nine Electron test files hold 115 cases (6 + 8 + 13 + 22 + 28 + 5 + 4 + 18 + 11, counted from the `it` and `it.each` calls, each `it.each` over the five required keys counting as five). Target projects: `tests/ShotAI.Core.Tests` (xunit.v3 on Microsoft.Testing.Platform, Linux and Windows), `tests/ShotAI.Platform.Tests` and `tests/ShotAI.App.Tests` (Windows only, added by the plan). Test classes live under an `Auth/` folder in each project. Tests that mutate process environment variables go in one xunit collection with parallelization disabled (`[Collection("ProcessEnvironment")]`). Fixtures use placeholder ids only (`11111111-1111-1111-1111-111111111111`, `22222222-...`, `fdrl_EXAMPLE01`, `svac_EXAMPLE01`, `wrkspc_EXAMPLE01`); the Electron fixtures' GUID prefixes are not copied.

### 8.1 `src/main/entra/admx-contract.test.ts` (65 lines, 6 cases)

Purpose: the ADMX is a separate file from the code; drift in either direction is invisible to a typecheck.

| Case | Disposition | Target |
|---|---|---|
| declares exactly the value names the app reads (sorted `valueName` set equals `FEDERATION_KEYS`) | Core (Linux), reads `Intune/Windows/shotAI.admx` in place | `Auth/AdmxContractTests.ValueNamesEqualFederationKeys` |
| writes to the MACHINE hive at the key config-sources reads (`class="Machine"`, `key="SOFTWARE\Policies\shotAI\Federation"`) | Core; asserts against `FederationPolicy.KeyPath` rather than a literal | `.MachineHiveAtPolicyKey` |
| stays outside the namespaces Intune ADMX ingestion refuses (`system`, `software\microsoft`, `software\policies\microsoft` prefixes, case-insensitive) | Core | `.OutsideBlockedIngestionNamespaces` |
| marks exactly the five required values `required="true"` | Core; compares with `FederationKeys.Required` | `.RequiredFlagsMatchValidator` |
| resolves every ADML reference (`$(string.X)`, `$(presentation.X)`) | Core, reads `en-US/shotAI.adml` | `.EveryAdmlReferenceResolves` |
| gives every element a textBox (sorted `refId` set equals sorted `text id` set) | Core | `.EveryElementHasTextBox` |

Parse with `System.Xml.Linq` (not regex) and keep one regex-based assertion per case for parity with the original technique; both must agree.

### 8.2 `src/main/entra/auth-core.test.ts` (91 lines, 8 cases)

Purpose: mode selection and leg isolation without MSAL or the network (a failing fetch and a throwing browser prove nothing is reached).

| Group | Case | Disposition | Target |
|---|---|---|---|
| mode selection | reports federation unavailable when nothing is configured (`federation()`, `entra()` null, `isSignedIn()` false) | Core | `Auth/AuthServiceModeSelectionTests.UnconfiguredReportsNoFederation` |
| | builds an API-key client when a key exists and federation is unconfigured | Core | `.UnconfiguredWithKeyBuildsApiKeyClient` |
| | refuses with a KEY-shaped message when nothing is configured and no key (`/API key/i`) | Core; assert the exact S2 | `.UnconfiguredWithoutKeyRefusesWithKeyMessage` |
| | refuses with a SIGN-IN message when federation is configured but nobody signed in (`/Microsoft/i`) | Core; exact S1 | `.ConfiguredSignedOutWithoutKeyRefusesWithSignInMessage` |
| | falls back to a stored key on a federation-configured machine | Core | `.ConfiguredSignedOutWithKeyUsesKey` |
| leg isolation | signIn leg when federation is not configured (`/not set up for Microsoft sign-in/i`) | Core; exact S3 | `Auth/VerifyFederationTests.UnconfiguredFailsSignInLeg` |
| | signIn leg when nobody has signed in, without touching the network | Core; the fake HTTP handler asserts zero requests | `.NoAccountFailsSignInLegWithoutNetwork` |
| | never reports the exchange leg before the signIn leg passed | Core | `.ExchangeNeverReportedBeforeSignIn` |

The fakes are `FakeMsalGatewayFactory` (accounts list, scripted silent and interactive results) and a `RecordingHttpHandler`.

### 8.3 `src/main/entra/cache-plugin.test.ts` (216 lines, 13 cases)

Purpose: the `safeStorage` plugin's invariants (no plaintext, miss on undecryptable, no write when unchanged, failed writes survive, nothing logged).

| Group | Case | Disposition | Target |
|---|---|---|---|
| beforeCacheAccess | deserializes the decrypted STRING, not the result object | ELECTRON-ONLY (Electron's object-shaped API) | none |
| | no-op when the file is absent | Windows | `Platform.Tests/Auth/MsalCachePersistenceTests.AbsentCacheFileMeansNoAccounts` |
| | undecryptable cache is a MISS, never an error | Windows (write random bytes to `msal-cache.bin`, then `GetAccountsAsync` returns empty and does not throw) | `.GarbageCacheFileIsAMiss` |
| | does not read the file when OS encryption is unavailable | Core (decision logic: when `VerifyPersistence` throws, the cache is not registered) | `Core.Tests/Auth/TokenCachePersistenceTests.UnverifiablePersistenceMeansMemoryOnly` |
| | re-encrypts on OS key rotation | ELECTRON-ONLY (DPAPI handles master key history) | none |
| | still loads the cache when re-encryption fails | ELECTRON-ONLY | none |
| afterCacheAccess | writes the encrypted cache when it changed | Windows (after a fake-authority silent token write, the file exists and does not contain the token text) | `MsalCachePersistenceTests.ChangedCacheIsWrittenEncrypted` |
| | does NOT write when nothing changed | Windows (file timestamp unchanged after a read-only access) | `.UnchangedCacheIsNotRewritten` |
| | NEVER writes plaintext when encryption is unavailable | Core (decision) plus source scan | `TokenCachePersistenceTests.UnverifiablePersistenceMeansMemoryOnly`, `Core.Tests/Auth/SourceScanTests.NoUnprotectedMsalCache` |
| | survives a failed write | Windows (cache file marked read-only; sign-in bookkeeping completes) | `MsalCachePersistenceTests.ReadOnlyCacheFileDoesNotFailSignInBookkeeping` |
| | never logs cache contents | Windows (captured MSAL and app logs contain no token text) | `.NoCacheContentInLogs` |
| clear | removes the file | ELECTRON-ONLY (`clear()` unused; sign-out removes accounts) | covered by `EntraSessionTests.SignOutRemovesEveryAccount` |
| | no-op when there is no file | ELECTRON-ONLY | none |

Windows cache tests drive a real `PublicClientApplication` with the real `MsalCacheHelper` over a temp directory and seed it through `ITokenCacheSerializer` (`DeserializeMsalV3`) with a synthetic cache blob, so no tenant is contacted.

### 8.4 `src/main/entra/config-sources.test.ts` (207 lines, 22 cases)

Purpose: registry output to parse to merge to validate, without the real hive.

| Group | Cases | Disposition | Target |
|---|---|---|---|
| `POLICY_KEY` (2) | reads the machine hive, never per-user; lives under `Policies` | Core (constants) and Windows (production reader properties) | `Core.Tests/Auth/FederationPolicyTests`, `Platform.Tests/Auth/RegistryPolicySourceTests.ProductionReaderTargetsHklmPolicyKeyIn64BitView` |
| `parseRegQuery` (7) | extracts known REG_SZ; ignores unknown names; ignores non-REG_SZ; ignores the header line; keeps single spaces; LF as well as CRLF; empty or unrelated output | Core: re-expressed over `PolicyValue` lists (known, unknown, `DWord`, `ExpandString`, `MultiString`, value with spaces, empty list). The line-format cases (header line, LF vs CRLF) are ELECTRON-ONLY (no text output natively) | `Core.Tests/Auth/PolicyValueParserTests` |
| `readPolicyConfig` (3) | never touches the registry off Windows; missing key is nothing delivered; queries the 64-bit view | Core (non-Windows reader returns empty via the `IPolicyValueSource` contract) and Windows (scratch HKCU key through the internal constructor: missing key gives empty; the view property is `Registry64`) | `RegistryPolicySourceTests.MissingKeyIsEmpty`, `.EnumeratesRealValueKinds`, `.UsesRegistry64View` |
| `pickBaked` (2) | keeps known keys, drops `_README` and unknown; drops blanks and trims | Core | `Core.Tests/Auth/FederationSourcesTests.PickBaked*` |
| `mergeFederationSources` (3) | policy overrides baked; keeps baked values policy does not mention; blank policy does not clear baked | Core | `FederationSourcesTests.Merge*` |
| `resolveFederation` (5) | baked only validates; policy corrects a rotated rule; neither source means unconfigured; half-delivered is rejected not unconfigured; malformed policy over good baked fails closed with `FederationRuleId` in `invalid` | Core (`FederationConfigProvider` with fake sources) | `Core.Tests/Auth/FederationConfigProviderTests.Resolve*` |

### 8.5 `src/main/entra/config-validate.test.ts` (191 lines, 28 cases)

Purpose: the fail-closed rule.

| Group | Cases | Target (all Core, `Auth/FederationConfigValidatorTests`) |
|---|---|---|
| happy path (5) | the required values accepted, optionals omitted; `ClientAppId` defaults to the audience; a separate `ClientAppId` is used; valid `WorkspaceId` and https `SupportUrl` accepted; trims and strips braces | `AcceptsRequiredSetAndOmitsOptionals`, `ClientDefaultsToAudience`, `SeparateClientIsUsed`, `AcceptsWorkspaceAndHttpsSupportUrl`, `TrimsAndStripsBraces` |
| fails closed (16) | missing each required key (theory of 5); blank each required key is missing not invalid (theory of 5); non-GUID tenant; wrong tagged prefixes (`FederationRuleId` and `ServiceAccountId` swapped); bare prefix `fdrl_`; never a partial config (`TenantId: 'nope'`, only `FederationRuleId`); malformed `ClientAppId`; malformed `WorkspaceId` | `[Theory] MissingRequiredKeyRejects`, `[Theory] BlankRequiredValueIsMissingNotInvalid`, `NonGuidTenantIsInvalid`, `WrongTaggedPrefixesAreInvalid`, `BarePrefixIsInvalid`, `NeverReturnsPartialConfig`, `MalformedClientAppIdFailsClosed`, `MalformedWorkspaceIdFailsClosed` |
| SupportUrl degrades (3) | malformed dropped; `http` dropped; `javascript:` dropped | `MalformedSupportUrlDropped`, `HttpSupportUrlDropped`, `DangerousSchemeDropped` |
| `isUnconfigured` (4) | nothing delivered; all blank; partial is false; complete is false | `IsUnconfigured*` |

Additional native vectors in the same class: order of names in `missing` and `invalid` follows 2.6 (`{}` gives `missing = [TenantId, AudienceAppId, FederationRuleId, OrganizationId, ServiceAccountId]`); a value with a trailing `\n` inside braces behaves as `norm` does in JS; `IsHttpsUrl` vectors: `https://help.example.com/shotai` true, `https://help.example.com:8443/x` true, `https://x.example.com/a b` true, `http://help.example.com` false, `javascript:alert(1)` false, `not a url` false, `https://` false, `https:\\help.example.com` false; U+FEFF around a value is trimmed and U+0085 is not (`JsString.Trim`).

### 8.6 `src/main/entra/federation-cache-wiring.test.ts` (50 lines, 5 cases)

Purpose: the invalidator is actually called from the status path, and the admin doc and the code tell the same story.

| Case | Disposition | Target |
|---|---|---|
| imports the invalidator into the IPC layer | ELECTRON-ONLY as text matching; replaced by a behavioral test | `Core.Tests/Auth/AuthStatusServiceTests.GetStatusInvalidatesFederationCache` (a counting policy source is read on every `GetStatusAsync`) |
| CALLS it, not merely imports it | same behavioral test | same |
| calls it inside the `auth:status` handler | same, plus: `CreateClientAsync` and `TestConnectionAsync` do NOT re-read policy | `.OnlyStatusInvalidates` |
| config.ts still exports the invalidator | ELECTRON-ONLY | none |
| the administrator doc and the code tell the same story (if `Intune/Windows/README.md` matches `/reopening Settings|without a restart/i`, the invalidation must exist) | Core, reads the README in place and runs the behavioral check when the promise is present | `Core.Tests/Auth/AdminDocConsistencyTests.NoRestartPromiseRequiresInvalidation` |

### 8.7 `src/main/entra/federation-local.test.ts` (55 lines, 4 cases, skipped without the file)

Purpose: a developer's local baked file is valid, so a typo fails at test time naming the key, not as an opaque 401. Nothing prints a value.

| Case | Disposition | Target |
|---|---|---|
| is valid JSON | Core; skips (`Assert.Skip`) when neither `dotnet/federation.local.json` nor `src/main/entra/federation.local.json` exists | `Core.Tests/Auth/FederationLocalFileTests.IsValidJson` |
| passes validation (message lists names only) | Core, same skip | `.PassesValidation` |
| bare audience GUID, no `api://` | Core, same skip | `.AudienceHasNoApiPrefix` |
| resolves a client id (the audience when `ClientAppId` absent) | Core, same skip | `.ResolvesClientAppId` |

### 8.8 `src/main/entra/federation.test.ts` (225 lines, 18 cases)

Purpose: the exchange's request shape, local guards, error wording and the advisory role check.

| Group | Case | Target (Core) |
|---|---|---|
| success (4) | token and expiry derived from `expires_in` (`900` gives `expiresAt >= before + 900000 ms`, scope carried) | `Auth/FederationExchangeTests.ReturnsTokenWithDerivedExpiry` (uses `FakeTimeProvider`) |
| | RFC 7523 grant with the four ids and `workspace_id` to `https://api.anthropic.com/v1/oauth/token` | `.SendsGrantWithIds` (also asserts key order, `content-type`, and the beta header per D12) |
| | OMITS `workspace_id` when the config has none | `.OmitsWorkspaceWhenAbsent` |
| | logs status and duration but never the assertion or token | `.LogsNeverContainAssertionOrToken` |
| local guards (2) | empty assertion refused without a request | `.RefusesEmptyWithoutRequest` |
| | oversized assertion refused locally with its real size (`/16 KiB/`) | `.RefusesOversizeWithRealSize` (16384 bytes goes out; 16385 refused with `17 KiB`) |
| failures (7) | transport failure is unreachable, not a refusal | `.TransportFailureIsUnreachable` |
| | 401 points at the History page and carries the request id | `.Maps401WithRequestId` (exact S9 text, with and without id) |
| | 400 blames shotAI config and does not mention Workload identity | `.Maps400` |
| | 429 asserts no cause | `.Maps429` |
| | 5xx is temporary | `.Maps5xx` |
| | NEVER leaks the response body | `.NeverLeaksResponseBody` |
| | a 200 missing `access_token` or with a bad `expires_in` (`{}`, token only, `0`, `"soon"`) | `.RejectsMissingOrBadExpiry` |
| `hasAppRole` (5) | finds the role; absent `roles` is false; roles without it is false; base64url with `-` and `_` (payload containing `a?b>c~d` and `ÿÿÿ`); unparsable is `null` (`not-a-jwt`, `""`, `a.b.c`) | `Auth/JwtRolesTests` (one method each) |

### 8.9 `src/main/entra/net-module.test.ts` (147 lines, 11 cases)

Purpose: the msal-node `INetworkModule` over `net.fetch`: both methods; status, parsed body, lowercased headers; POST pass-through; no body on GET; no throw on 4xx with the parsed error body; no throw on 5xx; raw text for non-JSON; `{}` for empty; abort signal only with a timeout; GET aborted on timeout; zero or negative timeout ignored.

Disposition: ELECTRON-ONLY, all 11. MSAL.NET owns its HTTP handling; WAM uses OS networking. The intents that remain are covered by new tests: `SharedHttpTests.HandlerUsesSystemProxyAndDefaultCredentials`, `SharedHttpTests.ApiClientDisposalDoesNotDisposeHandler`, `MsalGatewayTests.UsesSharedHttpClientFactory` (Windows), and the live AC-AUTH-29 behind a TLS-inspecting proxy.

### 8.10 New tests the native code needs

| Test class (project) | What it proves |
|---|---|
| `Auth/FederationConfigProviderTests` (Core) | caching (one resolution per fill), `Invalidate`, the three log outcomes (INV-AUTH-6, 8), policy failure falls back to baked (INV-AUTH-3), faulted resolution not cached (D15), environment variables and `settings.json` have no influence (INV-AUTH-1) |
| `Auth/BakedFederationParserTests` (Core) | `_` keys, non-strings, blanks dropped; trimmed; malformed JSON gives `{}`; last duplicate wins; non-object root gives `{}`; a leading UTF-8 BOM is stripped (EDGE-AUTH-37); the committed `federation.example.json`, read in place, parses and is REJECTED with `invalid = [FederationRuleId, ServiceAccountId, WorkspaceId]` (EDGE-AUTH-36) |
| `Auth/EntraSessionTests` (Core) | scope from the current config (INV-AUTH-11, EDGE-AUTH-8); gateway rebuilt on `TenantId` or `ClientAppId` change and not on `AudienceAppId` change; concurrent callers share one gateway; first account; silent maps UI-required to `SignInRequiredException` with claims; claims replayed once on the next interactive call; cancel returns `Canceled` and keeps the claims; one account after interactive success (INV-AUTH-34); forced refresh after success, and its failure does not fail sign-in; sign-out removes all accounts |
| `Auth/EntraIdentityTokenProviderTests` (Core) | only silent calls, even when silent throws; exceptions propagate unwrapped from our side (INV-AUTH-12) |
| `Auth/AnthropicClientFactoryTests` (Core, env collection) | the 2.13 table end to end with a recording handler: host is `api.anthropic.com` whatever the environment (INV-AUTH-15); federated requests carry `Authorization: Bearer <minted>` and no `x-api-key`; key requests carry `x-api-key` and no `Authorization`; the exchange body carries the four ids; two API calls cost one exchange (INV-AUTH-16); a 401 on the API triggers exactly one re-exchange and one retry; a `SignInRequiredException` from the provider arrives wrapped in `WorkloadIdentityException` and `FederationErrorText.ForSdk` returns S1; after a successful first exchange, a 401 from the exchange on the forced refresh surfaces as `WorkloadIdentityException` with status 401 and maps to S9 (EDGE-AUTH-39); `CustomHeadersEnvironmentHasNoEffect` (INV-AUTH-35, run in a child test process because the SDK reads the variable in a static constructor); `EachClientGetsFreshHandlers` (two clients from one factory each carry their own `AnthropicHostGuardHandler` then `ResponseHeaderCaptureHandler`, in that order, and the second `CreateAsync` does not throw `Handler is already attached to a client`; R-ARCH-15); `OptionsCarryExplicitTimeoutAndRetries` (`Timeout` 10 minutes, `MaxRetries` null) |
| `Auth/ConnectionTesterTests` (Core) | every row of 2.14 with exact texts and log lines; role-false still passes (INV-AUTH-23); disabled SOP makes no request; exchange never before sign-in (INV-AUTH-17); a diagnostic response with `token_type: "mac"` or `expires_in: "600.5"` passes leg 2 and logs the EDGE-AUTH-40 info line |
| `Auth/FederationErrorTextTests` (Core) | `Explain` exact strings for 400, 401, 403, 429, 500, 503 with and without request id, and an empty request id treated as absent; `ForSdk` for each of the six rows of the 7.7 table, built with the SDK's real `WorkloadIdentityException` constructors (INV-AUTH-24) |
| `Auth/AuthErrorTypeTests` (Core) | every user-facing exception type of INV-AUTH-36 derives from `ShotAIException`; `SetAsync("  ")` throws `ApiKeyStoreException` with S16; `SignInAsync` with no config throws `FederationNotConfiguredException` with S3; 11's `UserMessage.From` returns each message verbatim |
| `Auth/AnthropicHostGuardHandlerTests` (Core) | a request to `https://api.anthropic.com/` passes untouched; any other host, `http://api.anthropic.com/`, and `https://api.anthropic.com:8443/` throw `InvalidOperationException` with `Refusing to contact a host other than api.anthropic.com.` and the refused URL appears in no log line; a fresh handler per client (two clients from one factory never share an instance); the `ISharedHttp.Exchange` client refuses a foreign host the same way (R-ARCH-15) |
| `Auth/JwtRolesExtraTests` (Core) | the EDGE-AUTH-38 vectors: a payload with an interior `=` decodes only up to it; invalid characters are skipped; a one-character payload gives `null`; a JSON `null` payload gives `null`; a numeric or string payload gives `false`; a non-string `roles` element never matches |
| `Json/JsValueToNumberTests` (Core; shared with 04's `EffectiveBlock` vectors) | the 7.7 table (`"600"`, `" 60 "`, `""`, `"0x10"`, `"1e3"`, `"soon"`, `true`, `null`, `[]`, `[5]`, `[1,2]`, `{}`, `Infinity`) |
| `Auth/ApiKeyStoreTests` (Core, env collection, fake protector) | status truth table (INV-AUTH-28); set trims and refuses S16 and S17; clear writes `{}`; the file never contains the plaintext key (INV-AUTH-27); undecryptable logs the warn line and is not returned; writes serialized; `AuthStatusChanged` raised |
| `Auth/AuthStatusServiceTests` (Core) | exact status for unconfigured, configured signed out, configured signed in, key only, env only, rejected config; `SupportUrl` default and delivered; invalidation (INV-AUTH-9) |
| `Auth/AuthStatusShapeTests` (Core) | `AuthStatus` has exactly the seven properties of 2.16 (reflection) (INV-AUTH-31) |
| `Auth/AuthFacadeSurfaceTests` (Core) | no public member of `IAuthService` returns or accepts a token, an `IApiKeyStore`, an `AnthropicClient` or `IAnthropicClient`, or a `FederationConfig` (INV-AUTH-19) |
| `Auth/SupportUrlAllowlistTests` (Core) | same origin with any path allowed; different port, `http`, sibling subdomain, parent domain, userinfo trick (`https://evil@help.example.com` is origin `help.example.com`, allowed only when that is the configured host) refused; no config refused; malformed configured URL never widens (INV-AUTH-32) |
| `Auth/MintedTokenTests` (Core) | `ToString` of `MintedToken` and `FederationConfig` contains no token and no id value |
| `Auth/SourceScanTests` (Core) | no `WithUnprotectedFile`, no `WithClientCapabilities`, no `.default"` scope literal, no `AcquireTokenInteractive` outside `MsalGateway`, no `ProtectedData` outside `DpapiSecretProtector`, no `Environment.GetEnvironmentVariable("ANTHROPIC_` outside `ApiKeyStore` (scans `dotnet/src`) |
| `Auth/SharedHttpTests` (Core) | handler settings; disposing an API client leaves the handler usable; exchange timeout 60 s; `Handler` is the bare `SocketsHttpHandler` with no `DelegatingHandler` in front of it, so the update check and MSAL never pass through an Anthropic guard (R-ARCH-15) |
| `Auth/CompositionTests` (App.Tests, Windows) | building the startup graph touches no auth source (INV-AUTH-10); `IPolicyValueSource` resolves to the production Platform type (asserted by type name `RegistryPolicySource`, since the type is `internal sealed`, INV-ARCH-4); `ANTHROPIC_CUSTOM_HEADERS` is absent from the process environment after startup (INV-AUTH-35) |
| `Auth/RegistryPolicySourceTests` (Platform.Tests, Windows) | production properties (HKLM, `Registry64`, the key path); real value kinds through a scratch HKCU key; missing key and access denied give empty |
| `Auth/MsalGatewayTests` (Platform.Tests, Windows) | builder: tenant authority, broker enabled, no client capabilities, PII logging off, shared HTTP factory; `MsalUiRequiredException` maps to `EntraUiRequiredException` with claims; `authentication_canceled` maps to `EntraSignInCanceledException`; an `MsalServiceException` whose message contains `AADSTS50011` maps to `EntraSignInFailedException` with S18 + ` Details: ` + the message, and any other `MsalException` maps to its message verbatim (D23); the default build enables the broker, so `SHOTAI_NO_BROKER` is not defined (exception mapping tested through a thin seam around the MSAL builders) |
| `Auth/MsalCachePersistenceTests` (Platform.Tests, Windows) | 8.3 cases, plus `CacheLivesUnderLocalDataDirectory` (with a temp-folder `IAppPaths`, the helper's file is `<LocalDataDirectory>\entra\msal-cache.bin` and nothing is written elsewhere; that the production `AppPaths.LocalDataDirectory` is `%LOCALAPPDATA%\LFI\shotAI` is 10's test, R-ARCH-13) |
| `Auth/DpapiSecretProtectorTests` (Platform.Tests, Windows) | round trip; entropy required (unprotect without it fails); `IsAvailable` true in a normal session |
| `Auth/EmbeddedBakedFederationSourceTests` (App.Tests, Windows) | no resource gives `{}`; a test assembly with a resource gives the parsed values |
| `Auth/AdmxContractTests`, `Auth/AdminDocConsistencyTests` (Core) | 8.1, 8.6 |

---

## 9. Acceptance criteria

**AC-AUTH-1.** `AdmxContractTests` passes on Linux against the unchanged `Intune/Windows/shotAI.admx` and `en-US/shotAI.adml`, and `git diff --stat main -- Intune/` is empty for the native work.

**AC-AUTH-2.** `FederationConfigValidatorTests`, `FederationSourcesTests`, `PolicyValueParserTests`, `BakedFederationParserTests` and `FederationConfigProviderTests` pass on Linux, covering every case listed in 8.4, 8.5 and 8.10.

**AC-AUTH-3.** For each of these fixtures, the native `ResolveNow()` returns the same `ok`, `missing`, `invalid`, `unconfigured` and config fields as Electron's `resolveFederation` (run once under Node and recorded as a golden file): baked only; policy only; policy overriding one key; blank policy over baked; malformed policy over baked; half-delivered baked; braces and whitespace; `api://` audience; each optional malformed.

**AC-AUTH-4.** Manual, Windows, administrator: set the eight values with `reg add "HKLM\SOFTWARE\Policies\shotAI\Federation" /v <Name> /t REG_SZ /d <placeholder> /reg:64` on a build with no baked file. Open Settings then AI with AI SOP generation on: the `Microsoft sign-in` group appears. Change `FederationRuleId` to `bad`, close and reopen Settings without restarting: the group disappears and the log contains exactly one line `federation: config rejected, falling back to API key (missing: none; invalid: FederationRuleId).`

**AC-AUTH-5.** Manual: create `FederationRuleId` as `REG_EXPAND_SZ` over a baked build: the baked value is used (no rejection line). Create the same value under `HKCU\SOFTWARE\Policies\shotAI\Federation` only: it has no effect.

**AC-AUTH-6.** A build without `federation.local.json` shows the AI tab with no mention of Entra, Microsoft or sign-in (compare against 06's unconfigured presentation), and `AuthStatus` is `{ Mode: None or ApiKey, FederationAvailable: false, SignedIn: false, Account: null, ..., SupportUrl: null }`.

**AC-AUTH-7.** `CompositionTests.AuthServicesAreLazy` passes, and a Process Monitor trace of a cold launch to the Home screen shows no read of `HKLM\SOFTWARE\Policies\shotAI\Federation`, no access to `msal-cache.bin` or `secrets.dpapi.json`, and no connection to `login.microsoftonline.com` or `api.anthropic.com`.

**AC-AUTH-8.** `AuthServiceModeSelectionTests` passes: the five rows of the 2.13 table with the exact S1 and S2 texts.

**AC-AUTH-9.** `AnthropicClientFactoryTests.EnvironmentCannotRedirectOrReplaceCredential` passes with `ANTHROPIC_BASE_URL=https://attacker.example`, `ANTHROPIC_API_KEY=sk-ant-env`, `ANTHROPIC_AUTH_TOKEN=tok`, `ANTHROPIC_PROFILE=x`, `ANTHROPIC_FEDERATION_RULE_ID=fdrl_ENV`, `ANTHROPIC_ORGANIZATION_ID=22222222-2222-2222-2222-222222222222`, `ANTHROPIC_SERVICE_ACCOUNT_ID=svac_ENV`, `ANTHROPIC_IDENTITY_TOKEN=jwt`, `ANTHROPIC_WORKSPACE_ID=wrkspc_ENV` set: in federated mode every request goes to `api.anthropic.com` with the minted bearer and no `x-api-key`; in key mode the stored key (not the env key) is sent when both exist.

**AC-AUTH-10.** `AnthropicClientFactoryTests.FederatedClientWiresAllIdsAndReusesToken`: two `models.retrieve` calls on one federated client cause exactly one exchange; the exchange body has `federation_rule_id`, `organization_id`, `service_account_id` and, only when configured, `workspace_id`.

**AC-AUTH-11.** `EntraIdentityTokenProviderTests.NeverCallsInteractive` passes, and `SourceScanTests` confirms `AcquireTokenInteractive` appears only in `MsalGateway`.

**AC-AUTH-12.** `ConnectionTesterTests` passes with each 2.14 message produced byte for byte (the C# strings compared to literals copied from this spec, `\u2014` escapes included).

**AC-AUTH-13.** `FederationExchangeTests` passes, including `NeverLeaksResponseBody` and `LogsNeverContainAssertionOrToken`.

**AC-AUTH-14.** `JwtRolesTests` passes, and `ConnectionTesterTests.RoleFalseAfterSuccessfulExchangeStillPasses` shows a passing test with the info line `connection test: exchange succeeded although the local roles check saw no shotAI.User.`

**AC-AUTH-15.** `ApiKeyStoreTests` passes; after `SetApiKeyAsync(" sk-ant-test ")` the file `%APPDATA%\shotAI\secrets.dpapi.json` exists, contains `apiKey`, and a byte search for `sk-ant-test` in it finds nothing.

**AC-AUTH-16.** Manual: with a key saved, copy `secrets.dpapi.json` to another Windows user's profile and start shotAI as that user: Settings shows ` A previously saved key couldn’t be read on this machine \u2014 clear it and enter a new one.`, `Clear` is offered, and generation refuses with S2 (unconfigured) rather than crashing.

**AC-AUTH-17.** Manual: on a machine with Electron shotAI 1.3.0 installed and a key saved there, install native, save a different key, then launch Electron again: Electron still uses its own key and shows no unreadable-key warning (the two files are independent).

**AC-AUTH-18.** `AuthStatusShapeTests` and `AuthFacadeSurfaceTests` pass.

**AC-AUTH-19.** `SupportUrlAllowlistTests` passes; manually, with `SupportUrl=https://help.example.com/shotai` delivered, the Request access link opens that page, and a crafted call to open `https://evil.example.com/` is refused with the log line `refused openExternal for non-allowlisted URL: https://evil.example.com` (written by 10's link service, not under `claude`).

**AC-AUTH-20.** `MsalCachePersistenceTests` passes on Windows (x64 and ARM64 runners).

**AC-AUTH-21.** Manual: sign in, quit, overwrite `%LOCALAPPDATA%\LFI\shotAI\entra\msal-cache.bin` with random bytes, relaunch, open Settings: it shows `Sign in with Microsoft` (signed out), no error; one sign-in restores normal operation. Nothing is created under `%LOCALAPPDATA%\shotAI\` (the Squirrel install root, R-ARCH-13, EDGE-PKG-22; the Electron-uninstall check is ARCHITECTURE AC-ARCH-7). (Corrected per PLAN 1.5: formerly `%LOCALAPPDATA%\shotAI\entra\`.)

**AC-AUTH-22.** Manual: the single-instance lock (03) prevents a second app process, so run the probe tool pointed at the app's cache directory concurrently with the app, signing in with each: neither corrupts the cache (both read the accounts afterwards), demonstrating the extension's cross-process lock.

**AC-AUTH-23.** Manual: on a machine where `ms-appx-web://microsoft.aad.brokerplugin/{ClientAppId}` is registered, `Sign in with Microsoft` shows the WAM dialog parented to the shotAI window (not behind it), and on success Settings shows `Signed in as **<UPN>**` and `Sign in again`.

**AC-AUTH-24.** Manual: open the WAM dialog and close it: Settings returns to `Sign in with Microsoft` with no error text.

**AC-AUTH-25.** Manual: sign out: the account disappears, a previously saved API key is still reported (`A key is saved (encrypted) on this machine ✓` under the fallback disclosure), and generation uses the key.

**AC-AUTH-26.** Manual, live tenant, non-admin assigned account: sign in, Test connection shows `Connected (claude-sonnet-5).`, generate an SOP with no API key present anywhere; relaunch and generate again with no prompt (the cache and WAM restored the session).

**AC-AUTH-27.** Manual, live tenant, the stale-roles trap: with a non-admin account that has NO `shotAI.User` assignment, sign in and run Test connection: it fails with the `Claude access:` 401 text (S9) and the Request access hint. Grant the role, click `Sign in again`, run Test connection within five minutes: it passes. (If it fails until the old token expires, Q-AUTH-4's forced refresh is not sufficient under WAM and must be revisited.)

**AC-AUTH-28.** Manual, live tenant: remove the role assignment from a signed-in user; the next generation (after the minted token's lifetime, or immediately after sign-out and sign-in) fails with the federated 401 text of 07 (`Your Claude access was refused. Check that your account still has the shotAI role.`) or, more commonly natively, S9 from the SDK's refused re-exchange (EDGE-AUTH-39); Test connection reports S9 on the `Claude access` leg.

**AC-AUTH-29.** Manual: behind a TLS-inspecting, authenticating corporate proxy configured as the Windows system proxy (WPAD or PAC), sign-in, Test connection and a streaming SOP generation all succeed without any shotAI setting.

**AC-AUTH-30.** Manual: the probe tool's legs 0 to 4 all pass against the live tenant with shotAI's own client id, leg 4 reporting 0 extra exchanges on the second call; with `WIF_PROBE_CLI_CLIENT=1` legs 1 to 3 also pass.

**AC-AUTH-31.** A repository grep for GUIDs in `dotnet/` and `docs/native/` (`[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-`) finds only placeholder patterns (repeated digits) and the documented Microsoft well-known ids referenced by constant name, never a tenant, client, organization, rule, service account or workspace id.

**AC-AUTH-32.** (WP-D7) Manual, Windows, on both x64 and ARM64 (Risk R1): on a device where WAM cannot be used, or with a test build compiled with `SHOTAI_NO_BROKER` (the log shows `entra: broker disabled in this build (test build).`), `Sign in with Microsoft` opens the system browser, the tab shows `Signed in to shotAI. You can close this tab and return to the app.` after sign-in, the loopback completes, and Settings shows `Signed in as **<UPN>**` and `Sign in again`. Test connection then passes as in AC-AUTH-26. The run records in 7.5 (failure modes table) whether any `msal: ` warn line appeared and, if a natural WAM-unavailable device was used, which one.

**AC-AUTH-33.** (WP-D7) Manual, Windows: against a client registration without `ms-appx-web://microsoft.aad.brokerplugin/{ClientAppId}` (a placeholder test registration, never the pilot's), `Sign in with Microsoft` fails with the Settings error `Error: ` followed by S18 exactly (`Microsoft sign-in is not fully set up for shotAI: the app registration is missing the Windows sign-in redirect URI (ms-appx-web://microsoft.aad.brokerplugin/ followed by the app's client id). Ask your administrator to add it under Mobile and desktop applications.`) and ` Details: ` plus MSAL's message; the app does not crash; `Sign in with Microsoft` is enabled again; the log has one `entra: interactive sign-in failed (<Type>, <ErrorCode>).` line and no UPN. The run records the observed exception type, `ErrorCode` and message in the 7.5 failure modes table and closes Q-AUTH-19.

**AC-AUTH-34.** (R-ARCH-15) `AnthropicHostGuardHandlerTests`, `SharedHttpTests` and `AnthropicClientFactoryTests.EachClientGetsFreshHandlers` pass on Linux: a request that is not `https` to `api.anthropic.com` on port 443 is refused on every API client and on the `ISharedHttp.Exchange` client; two sequential clients from one factory each carry fresh handlers; and `ISharedHttp.Handler` has no Anthropic-specific handler in front of it.

---

## 10. Interfaces with other subsystems

| Spec | Consumes from it | Provides to it |
|---|---|---|
| 01 Model and store | `AtomicFile.WriteAsync` (temp, flush, replace with sharing-violation retry and its `onRetry` callback); `JsString.Trim`; `JsJson.Parse`; `JsNumber.ToJsString` | nothing |
| 04 Editor and redaction | `ShotAI.Core.Json.JsValue.ToNumber(JsonNode?)` (whose contract is the 7.7 table) | the 7.7 `ToNumber` table |
| 03 Windows and shell | single-instance lock; the main window (for the WAM owner HWND via the Settings view) | nothing |
| 05 Report and project detail | hosts the SOP panel; window activation events | `IAuthService.GetStatusAsync`, `AuthStatusChanged` (through 07's panel) |
| 06 Home, settings UI | the Settings AI tab calls `IAuthService` (status, sign-in with the HWND, sign-out, key status, set, clear, test) and renders the values; maps `ConnectionLeg` to its labels; shows exception `Message` as `Error: <message>` | `AuthStatus`, `ApiKeyStatus`, `TestConnectionResult`, `SignInOutcome`, S1 to S18, `FederationPolicy.DefaultSupportUrl` |
| 07 SOP generation | `SopSettings` (`Enabled`, `Model`, read through 10's `ISettingsService.Current.Sop`); `SopErrorMapper.Map(Exception, AuthMode, ResponseHeaderCapture?)` for the API leg; the API-leg seam `SopModelProbe.RetrieveAsync` (internal, R-ARCH-3); `ResponseHeaderCaptureHandler`, which the factory attaches to every client (R-ARCH-15) | `IAnthropicClientFactory.CreateAsync` (the only Anthropic client factory; 07's `IClaudeClientFactory`, `ClaudeClientFactory`, `ISopCredentialSource` and `EgressPinHandler` are superseded, R-ARCH-15), `AuthMode`, `SignInRequiredException`, `FederationExchangeException`, `FederationErrorText.ForSdk` (INV-AUTH-24), `IAuthService.TestConnectionAsync` as the one connection-test entry point (R-ARCH-3), the canonical `IAuthService` (R-ARCH-2) |
| 10 Settings, logging, update check, allowlist | `ISettingsService.Current.Sop` (the connection test's gate and model, 7.10); `IAppPaths.UserDataDirectory` (API key file) and `IAppPaths.LocalDataDirectory` = `%LOCALAPPDATA%\LFI\shotAI` (MSAL cache, R-ARCH-13) (10 7.4.4); the `claude` log category and file sink; the base `openExternal` allowlist and opener | `ISupportUrlAllowlist.IsAllowedAsync` (the exact-origin extension), `ISharedHttp.Handler` for the update check |
| 11 Service boundary, threading, DI | `IUiDispatcher.InvokeAsync` (MSAL interactive on the UI thread, DL5), `EventRaiser.Raise` for `AuthStatusChanged`, singleton lifetimes, synchronous shutdown disposal (`IDisposable` on every disposable singleton, R-ARCH-10), `ShotAIException` and `UserMessage.From` (INV-AUTH-36), the composition-root step 5 that clears `ANTHROPIC_CUSTOM_HEADERS` and the proxy variables before any SDK or `HttpClient` use (INV-AUTH-35, ARCHITECTURE 4.2) | the registrations listed in 7.1 (`AddShotAICore`, `AddShotAIPlatform`, `AddShotAIApp`) |
| 12 Packaging and CI | `-p:ShotAIFederationFile=` from a release secret; shipping `msalruntime` for `win-x64` and `win-arm64`; Windows runners for Platform and App tests; the probe project | the `.gitignore` line for `dotnet/federation.local.json`; the requirement that the installer never writes the policy key; the requirement that release builds never define `SHOTAI_NO_BROKER` (AC-AUTH-32 test builds only) |

---

## 11. Open questions and risks

**Q-AUTH-1. Spec numbering.** Resolved by R-ARCH-14: auth is 08 and brand, palette, settings, logging and links are 10, in ARCHITECTURE.md's spec index; the remaining 07 `IAuthService` sketch is superseded by R-ARCH-2 (08's interface and value types are canonical, 07's `VerifyFederationAsync` and `IApiKeyStore` are internal to Core). Earlier record, resolved at verification: the spec files are `10-brand-settings-infra.md` (the brand spec) and this file for auth; 06 (its section 1.2 and notation) and 11 (Q-IPC-1, `IAuthService` canonical here) already route API key storage, Entra sign-in, auth status and the connection test to 08, with the SOP panel at 07. Only 07's superseded `IAuthService` sketch remains to be corrected (11 Q-IPC-1).

**Q-AUTH-2. Import the Electron API key once?** Electron's `secrets.json` is decryptable natively (read `%APPDATA%\shotAI\Local State`, DPAPI-unprotect `os_crypt.encrypted_key` after its `DPAPI` prefix, then AES-256-GCM decrypt the `v10` + 12-byte nonce payload). Recommended default: no import in 2.0 (the feasibility doc accepts one re-entry; importing adds crypto code for a one-time convenience); revisit if the pilot reports friction.

**Q-AUTH-3. The broker redirect URI is a tenant change.** WAM needs `ms-appx-web://microsoft.aad.brokerplugin/{ClientAppId}` on the client registration, which in the deployed topology is also the object the Anthropic rule matches by id (the #63 concern about redirect-URI churn on that object). Recommended default: add the URI to the existing registration (a redirect URI does not change the application id the rule matches); if the organization prefers to keep that object untouched, register a separate client and deliver `ClientAppId`, which needs no code change (2.6). If the step is missed, the user sees S18 (D23, AC-AUTH-33).

**Q-AUTH-4. Does WAM return fresh claims on an interactive call?** If WAM serves its own cached access token after "Sign in again", the stale-roles trap returns. Recommended default: keep the forced silent refresh after interactive success (7.5, D9) and prove it with AC-AUTH-27; remove it only if the live test shows it is redundant and costly.

**Q-AUTH-5. `MsalCacheHelper` failure behavior.** This spec relies on the helper treating an unreadable file as empty and not throwing on write failures. Recommended default: pin both with `MsalCachePersistenceTests`; if either fails, switch to the fallback design (custom `SetBeforeAccessAsync`/`SetAfterAccessAsync` over `ProtectedData` plus a named `Mutex` `Local\shotAI.entra.cache` for the cross-process lock).

**Q-AUTH-6. Beta header on the diagnostic exchange.** Electron's diagnostic sent none; the C# SDK's production exchange sends `oauth-2025-04-20,oidc-federation-2026-04-01`. Recommended default: send it (D12) so leg 2 tests what production sends; drop it if a live run shows any difference in the answer.

**Q-AUTH-7. `expires_in` absent in production.** The C# SDK treats an absent `expires_in` as a token that never expires and relies on the 401 retry. Recommended default: accept (the server returns it; the diagnostic leg rejects its absence, so a server change would show at Test connection); do not wrap `WorkloadIdentityCredentials`.

**Q-AUTH-8. Cache the federated client?** Every estimate and generation builds a client and spends one exchange (EDGE-AUTH-20); the estimate-then-generate flow therefore exchanges twice within seconds. Recommended default: parity in 2.0; a later IMPROVEMENT can cache one federated `AnthropicClient` keyed by the config and the account id, dropped on sign-in, sign-out and any config change.

**Q-AUTH-9. Request access hint conditions.** The hint says "Your sign-in worked." for any failed test, including a sign-in leg failure and the API-key test (EDGE-AUTH-17). Recommended default: parity in 2.0 (06 owns the copy); propose showing it only when `Leg == Exchange` on both platforms together.

**Q-AUTH-10. Where the baked file lives after cutover.** Recommended default: `dotnet/federation.local.json` with the `src/main/entra/` fallback until the Electron code is removed, then move `federation.example.json` to `dotnet/` in the cutover PR and drop the fallback.

**Q-AUTH-11. Do public Windows release artifacts carry the baked values?** macOS decided yes (reconnaissance, not access). The Windows record is silent. Recommended default: decide explicitly in 12 before the first public 2.0 release; until decided, public CI builds are unconfigured (no secret passed) and organization builds pass the file.

**Q-AUTH-12. `WorkspaceId = "default"`.** The C# SDK and the macOS port accept the literal `default`; the Windows validator rejects it. Recommended default: parity (reject); the ADML says `Begins with wrkspc_`.

**Q-AUTH-13. Who owns `SharedHttp`?** Resolved (ARCHITECTURE 2.4, 4.3; R-ARCH-15): 08 owns `SharedHttp` (`ISharedHttp`) in `ShotAI.Core.Net`, registered by `AddShotAICore`; 11 does not declare it (11 lists `ISharedHttp` among 08's registrations). 7.14 is the type's definition and requirements list; R-ARCH-15 fixes its content (no Anthropic-specific handler on the shared handler, a guarded `Exchange` client). Background: it is defined here because the proxy requirement came from sign-in, but 07 and 10 use it more. Recommended default (rewritten per R-ARCH-15; it formerly proposed that 11 own the type): 08 owns and defines `SharedHttp` in `ShotAI.Core.Net`, registered by `AddShotAICore`; this spec's 7.14 is its definition and requirements list. ARCHITECTURE 2.4 and 4.3 place it in `ShotAI.Core.Net`, registered by `AddShotAICore`, with 08 as the owning spec; R-ARCH-15 fixes its content (no Anthropic-specific handler on the shared handler, a guarded `Exchange` client).

**Q-AUTH-14. `docs/MANAGED-CONFIG.md` contradicts itself.** Its closing section "The names are frozen" says the eight names are identical on both platforms, contrary to its own table (EDGE-AUTH-2). Recommended default: fix the doc in a separate small PR (not part of the native work); the native code follows the table.

**Q-AUTH-15. Silent SSO with the Windows account.** WAM offers `PublicClientApplication.OperatingSystemAccount` for a zero-click first sign-in. Recommended default: not used in 2.0: sign-in stays an explicit click (parity, and WAM guidance "invoke authentication based on user action"); `Prompt.SelectAccount` already lists the Windows account in one click.

**Q-AUTH-16. Probe tool scope.** Recommended default: build it before the pilot (7.19, AC-AUTH-30) as an unshipped console project; it is the only way to prove the native production path against the live tenant.

**Q-AUTH-17. `ANTHROPIC_CUSTOM_HEADERS` handling.** The C# SDK adds the variable's headers to every request (INV-AUTH-35). Recommended default: clear it in the composition root and keep the host guard handler (adopted as ARCHITECTURE 4.2 step 5, the default of Q-ARCH-4; the guard's placement is fixed by R-ARCH-15); if 11 prefers not to mutate the process environment, the fallback is a guard handler that removes every header named in the variable's value (captured before the SDK reads it) and fails the request when an `X-Api-Key` or `Authorization` value appears twice. Whether Electron's TS SDK reads the same variable was not verified (UNVERIFIED); if it does, the same exposure exists in 1.3.0 and is out of scope here.

**Q-AUTH-18. The unreferenced ADML help strings.** EDGE-AUTH-41: the per-value `_Help` strings never display. Recommended default: leave the policy files unchanged for the native work (fixed decision); propose, in a separate Electron-era PR, moving the essential hints into the `FederationConfig_Explain` text or the textBox labels, and extend the ADMX contract test to flag ADML strings nothing references.

**Q-AUTH-19. The exact MSAL.NET failure for a missing broker redirect URI.** Microsoft Learn documents the required URI but not the exception type, `ErrorCode` or message MSAL.NET raises through WAM when it is absent. Recommended default: detect `AADSTS50011` in the message or `AdditionalExceptionData` (D23) and confirm or correct the rule with AC-AUTH-33 before the pilot. A related choice, not taken in 2.0: on `wam_runtime_init_failed`, rebuild the application without the broker and retry in the browser; rejected because it hides a packaging defect that 12 must fix.

**Risk R1.** WAM unavailable on a device: MSAL falls back to the browser with `http://localhost`, which is already registered (AC-AUTH-32). Corrected: a missing broker native runtime (`msalruntime`, for example a packaging gap on one architecture) does not fall back; MSAL.NET throws `MsalClientException` `wam_runtime_init_failed` and Settings shows its message. 12 must ship the runtime for `win-x64` and `win-arm64`, and AC-AUTH-23 and AC-AUTH-32 must be run on both architectures.

**Risk R2.** Conditional Access policies that allowed the Electron browser flow may behave differently under WAM (for example, token protection enforcement). Recommended: pilot with the organization's actual CA policies before cutover.

**Risk R3.** The SDK is young; `WorkloadIdentityCredentials`, the beta header values and the `TokenCache` thresholds may change between versions. Pin the `Anthropic` version centrally and keep `AnthropicClientFactoryTests` as the tripwire.
