# Managed configuration: the federation contract

The eight values that let shotAI call the Anthropic API with no API key on any machine.
Users sign in with Microsoft Entra ID, their token is exchanged for a short-lived Anthropic
token, and access is an Entra role assignment.

This page is the shared contract, and the first thing to be clear about is what "shared"
means. The **values** are the same on both platforms and are resolved the same way. The
**key names are not**, and neither is the required set:

| Value | Windows (`REG_SZ`) | macOS (managed preference) |
|---|---|---|
| Entra tenant | `TenantId` | `federationEntraTenantId` |
| Entra client app | `ClientAppId` *(optional)* | `federationEntraClientId` *(required)* |
| Entra audience app | `AudienceAppId` | `federationEntraAudienceAppId` |
| Federation rule | `FederationRuleId` | `federationRuleId` |
| Anthropic organization | `OrganizationId` | `federationOrganizationId` |
| Service account | `ServiceAccountId` | `federationServiceAccountId` |
| Workspace | `WorkspaceId` *(optional)* | `federationWorkspaceId` *(required)* |
| Request-access URL | `SupportUrl` *(optional)* | no equivalent |

So Windows has eight names of which five are required, and macOS has seven of which all
seven are required. Verified against
[`FederationConfig.swift`](https://github.com/Armadillon44/shotAI_MacOS/blob/main/Packages/EntraKit/Sources/EntraKit/FederationConfig.swift),
not inferred.

An earlier version of this page claimed the names were shared verbatim, citing a comment
in `config-validate.ts` that said so. Both were wrong. A profile written from the Windows
names would deliver nothing on macOS, and vice versa, so do not "align" either side: an
administrator's deployed configuration is keyed on these exact strings.

What genuinely IS matched: the values themselves, the precedence (baked, then overridden
by managed configuration), and failing closed on an incomplete set rather than attempting
a half-configured sign-in. The rest of this page is the **Windows** implementation,
verified in this repo; macOS specifics are owned by
[the macOS SSO doc](https://github.com/Armadillon44/shotAI_MacOS/blob/main/docs/SSO-WIF.md).

The Windows delivery sections below overlap deliberately with
[`Intune/Windows/README.md`](../Intune/Windows/README.md), which stays the operational
reference for importing and verifying policy. Where the two disagree, the code and the
ADMX contract test win.

Audience: IT and developers. Registry paths and command lines are appropriate here. The
end-user documentation does not mention any of this.

## None of these is a credential

The Entra JWT is what authenticates. An OAuth client id is expected to be public, which is
what PKCE exists for. Holding all eight values grants nothing on its own: using them
requires an Entra token from the tenant carrying the `shotAI.User` app role, and the
Anthropic federation rule enforces that server-side.

They do identify one Entra tenant and one Anthropic organization. Treat them as internal
rather than published. A repository naming a tenant and an Anthropic org is free
reconnaissance that would live in git history forever, which is why the filled-in values
are gitignored: on Windows they go in `src/main/entra/federation.local.json`, with
[`federation.example.json`](../src/main/entra/federation.example.json) committed as the
template. A fresh clone of the public repo has no local file, so it is simply unconfigured
and behaves as bring-your-own-key, which is correct for anyone outside the organization.

## The eight values

| Name | Required | Shape | Source | If malformed |
|---|---|---|---|---|
| `TenantId` | required | GUID | Entra admin center, directory (tenant) ID | fails closed |
| `AudienceAppId` | required | bare GUID, no `api://` prefix | Entra admin center, app registration used as the token audience | fails closed |
| `FederationRuleId` | required | `fdrl_` + alphanumeric | Claude Console, Settings then Workload identity | fails closed |
| `OrganizationId` | required | GUID | Claude Console | fails closed |
| `ServiceAccountId` | required | `svac_` + alphanumeric | Claude Console | fails closed |
| `ClientAppId` | optional | GUID | Entra admin center | fails closed |
| `WorkspaceId` | optional | `wrkspc_` + alphanumeric | Claude Console | fails closed |
| `SupportUrl` | optional | `https` URL | your own ticket form | ignored, default used |

Seven of the eight fail closed. `SupportUrl` is the single exception, because it is
presentational, takes no part in the token exchange, and a typo in it must not cost the
whole organization its sign-in. When it is missing or malformed the app falls back to the
shotAI project's issues page.

`ClientAppId` and `WorkspaceId` are optional but still fail closed when present and
malformed. They fail in **different** places, which matters when triaging:

- A malformed `WorkspaceId` takes part in the token exchange, so it produces the same
  opaque `401` as a missing app role. That is the expensive one: it looks exactly like an
  entitlement problem and gets diagnosed as one.
- A malformed `ClientAppId` aims the sign-in at a client that does not exist, so it fails
  earlier and louder, as an opaque `AADSTS` error in the browser. `Test connection`
  reports it under `Microsoft sign-in:`, not `Claude access:`.

Notes on the values themselves:

- **`AudienceAppId` must be the bare application (client) ID GUID.** A value with an
  `api://` prefix fails validation. On the Anthropic side, an expected audience left blank
  does not disable the check: it substitutes Anthropic's default audience, which an Entra
  token never carries.
- **Leave `ClientAppId` blank when one app registration serves as both the desktop client
  and the token audience**, which is the usual arrangement and what both platforms deploy.
  When it is absent the audience registration is used as the client. Set it only if a
  separate client application was registered.
- **Leave `WorkspaceId` blank to let a single-workspace federation rule pick its own
  workspace.** Anthropic requires it only when the rule spans more than one.
- **`ServiceAccountId` is shared.** Every signed-in user's token acts as that one service
  account, so usage and rate limits are attributed to it collectively.

Every delivered value is trimmed, and braces around a GUID (`{...}`) are stripped, so a
value pasted from Windows tooling is accepted as-is.

## Precedence

1. **Baked at build time.** This is the normal path: nothing to deploy and nothing for a
   user to type. Windows bakes `src/main/entra/federation.local.json` into the main bundle;
   macOS bundles `shotAI/Resources/Federation.plist`.
2. **Platform-managed configuration overrides the baked value, per key.** This is the
   exception, not the requirement. It exists for the case a build cannot cover, chiefly a
   rotated federation rule that has to change without shipping a new installer. Overriding
   one key leaves the other seven at their baked values.
3. **A blank managed value counts as "not set" and never clears a baked value.** Blank
   means unset everywhere in this contract, and an administrator half-clearing one key must
   not silently disable federation for the fleet.
4. **The merged set is then validated as a whole, and fails closed.** If any required value
   is missing or malformed, or an optional exchange value is present and malformed, the
   entire configuration is rejected and the app reverts to bring-your-own-key. It does not
   attempt a half-configured sign-in, which would fail with an error naming no cause.
5. **Delivering nothing at all is a valid state.** With no baked values and no managed
   configuration, the app asks each user for their own Anthropic API key and says nothing
   about sign-in anywhere in its UI. A rejected configuration is different: an
   administrator needs to see that, and it is logged.

Signing in takes precedence over a stored API key, but a machine with federation configured
and nobody signed in still works off an existing key, so a rollout strands nobody.

## Delivery

Both platforms deliver through an **administrator-only** store. That is the requirement,
not a detail of convenience: a user-writable store would let a standard user rewrite these
values and repoint their own client at another tenant or another federation rule.

### Windows

Eight `REG_SZ` values under:

```
HKLM\SOFTWARE\Policies\shotAI\Federation
```

`HKLM` only, never `HKCU`, and the 64-bit view is queried explicitly. Only `REG_SZ` is part
of the contract: a value of another type reads as **absent from policy** rather than being
coerced into something mangled.

Note what that means on a normal deployment, because it is the opposite of what "fails
closed" would suggest: since the values are baked into the installer, an absent policy value
leaves the **baked** value standing, so a wrong-typed override is silently ignored instead of
taking effect. It fails closed only where nothing else supplies that required value. If an
override appears to do nothing, check the value type first.

The ADMX and ADML, the Intune and Group Policy import routes, and the per-value help text
are in [`Intune/Windows/README.md`](../Intune/Windows/README.md).

### macOS

Managed preferences delivered by a configuration profile, with `objectIsForced` set.
`HKLM` is the Windows integrity analog of exactly that.

The profile mechanics and the macOS baked-value path are documented on the macOS side:
<https://github.com/Armadillon44/shotAI_MacOS/blob/main/docs/SSO-WIF.md>.

## Granting and revoking access

Access is an Entra **app role assignment**, role value **`shotAI.User`**, on the audience
app's service principal, enforced by a condition on the Anthropic federation rule. Removing
the assignment revokes Claude access at the next token exchange. No app change, no key
rotation, no redeploy.

shotAI's own check for the role is **fail-open by design**: it logs what it saw and never
gates on it, so the federation rule remains the sole authority.

One trap costs real debugging time: **after you grant the role, the user must sign in
again.** A silent retry re-serves a cached Entra token, valid 60 to 90 minutes, that
predates the assignment and carries no role claim. That looks exactly like a broken
federation rule. The Sign in button forces a fresh interactive sign-in for this reason,
which is why the in-app message on a failed test tells the user to sign in again after
requesting access.

## Verify a deployment

### Windows

```cmd
reg query "HKLM\SOFTWARE\Policies\shotAI\Federation" /reg:64
```

Then in the app, **Settings → AI**, with **AI SOP generation** switched on. A configured
machine shows a **Microsoft sign-in** group, with the API-key field still available but
collapsed under **Use my own Anthropic API key instead**.

**Sign in first**: the **Test connection** button is only rendered once an account is
signed in. It runs three legs separately and prefixes a failure with the leg that failed: `Microsoft sign-in:`, `Claude access:`, or
`Claude API:`. That prefix is the only way to tell "not signed in" from "not assigned the
`shotAI.User` role" from a scope or API problem, because every access denial from Anthropic
is deliberately the same opaque `401`.

If the group does not appear, there are three possibilities, cheapest first: **AI SOP
generation** is switched off (the whole group lives behind that toggle), nothing was
delivered, or the configuration was delivered and **rejected**. A rejection is logged as
`federation: config rejected, falling back to API key`
with the offending value **names** (never their values, so the line is safe to attach to a
ticket).

The resolved configuration is cached, and the cache is dropped whenever the app reads auth
status, which happens when **Settings** opens and when a project opens. So opening Settings
is enough to pick up a policy correction; a restart is never required.

That behavior depends on one call site, so it is pinned by
`src/main/entra/federation-cache-wiring.test.ts`. It shipped broken once: the invalidator
existed, its comment said Settings called it, and nothing did, which made this paragraph and
the ADMX help text false while typechecking perfectly.

### macOS

Confirm the configuration profile is installed on the device, then open the app's settings
and confirm it offers sign-in rather than asking for an Anthropic API key. Sign in and
generate one SOP to exercise the exchange end to end. The macOS doc linked above is
authoritative for that platform's verification steps.

## The names are frozen

The eight value names are **identical on both platforms**. They are one contract, declared
once as `FEDERATION_KEYS` in `src/main/entra/config-validate.ts`, and delivered under those
exact names by the ADMX on Windows and by the managed preference on macOS.

Renaming a value on one platform only produces either a value no policy can set, or a
policy value the app silently ignores. Neither shows up in a typecheck. On Windows,
`src/main/entra/admx-contract.test.ts` asserts the binding: that the ADMX declares exactly
the value names the app reads, that exactly the five required values are marked
`required="true"`, and that the policy targets the machine hive at the key the app queries.

Treat the names as frozen. Changing one is a coordinated change across both repositories
and both delivery mechanisms, and the ADMX contract test is the tripwire on the Windows
side.
