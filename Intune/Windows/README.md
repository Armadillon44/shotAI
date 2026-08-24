# Managed configuration — Windows

`shotAI.admx` + `en-US/shotAI.adml` let an administrator turn on **Microsoft Entra
sign-in** for Claude, so users never handle an Anthropic API key.

The policy writes eight `REG_SZ` values to:

```
HKLM\SOFTWARE\Policies\shotAI\Federation
```

`HKLM` is deliberate. It is the Windows integrity analog of macOS's
`objectIsForced`: only an administrator or MDM can write it, so a standard user
cannot repoint their own client at a different tenant or federation rule. shotAI
reads **HKLM only, never HKCU**, and queries the 64-bit view explicitly so a 32-bit
build is not silently redirected to `Wow6432Node`.

## When you need this

You mostly don't. The values are normally **baked into the installer at build time**,
so a managed deployment needs nothing configured and nothing typed. This policy
exists for the case the build cannot cover: **a rotated federation rule**, or any
value that has to change without shipping a new installer. Policy values override the
baked ones per key.

Delivering nothing is a valid state. With no policy and no baked values, shotAI asks
each user for their own Anthropic API key, which is the correct behavior for anyone
outside the organization.

## It fails closed

The configuration is validated as a whole. If any **required** value is missing or
malformed, shotAI ignores the entire policy and reverts to bring-your-own-key rather
than attempting a half-configured sign-in — which would fail with an opaque `401`
that names no cause.

Five values are required: `TenantId`, `AudienceAppId`, `FederationRuleId`,
`OrganizationId`, `ServiceAccountId`. Set them together.

Three are optional, and two of those still fail closed if present and malformed,
because they take part in the token exchange:

| Value | Behavior when malformed |
|---|---|
| `ClientAppId` | fails closed |
| `WorkspaceId` | fails closed |
| `SupportUrl` | ignored, falls back to the project's issues page |

A **blank** policy value counts as "not set" and does not clear a baked value, so
half-clearing a key cannot silently disable federation for the fleet.

## Import into Intune

**Settings catalog route.** Devices → Configuration → Create → Windows → *Import
custom ADMX and ADML*. Upload `shotAI.admx` first, then `en-US/shotAI.adml`. Then
create a Settings-catalog profile and search for **shotAI → Authentication →
Configure Microsoft Entra sign-in for Claude**.

**OMA-URI fallback.** If custom ADMX import is unavailable in your tenant, a Custom
profile driving the same CSP achieves an identical registry outcome:

```
./Device/Vendor/MSFT/Policy/ConfigOperations/ADMXInstall/shotAI/Policy/shotAIFederation
```

**Group Policy.** The same pair works unchanged in a domain Central Store
(`\\<domain>\SYSVOL\<domain>\Policies\PolicyDefinitions`). Both routes write the same
key, so the app is indifferent to which you used.

## Where the values come from

| Value | Source |
|---|---|
| `TenantId`, `AudienceAppId`, `ClientAppId` | Microsoft Entra admin center → App registrations |
| `FederationRuleId`, `OrganizationId`, `ServiceAccountId`, `WorkspaceId` | Claude Console → Settings → Workload identity |

`AudienceAppId` must be the **bare** application (client) ID GUID, with **no `api://`
prefix**. A prefixed value fails validation, and on the Anthropic side an audience
left blank does not disable the check — it substitutes Anthropic's default audience,
which an Entra token never carries.

Leave `ClientAppId` blank when one app registration serves as both the desktop client
and the token audience, which is the usual arrangement.

## None of this is secret

The Entra JWT is what authenticates, and an OAuth client id is expected to be public
— that is what PKCE is for. These values do identify your tenant and your Anthropic
organization, so treat them as internal rather than published; that is why they are
not committed to this repository.

## Verify a deployment

```cmd
reg query "HKLM\SOFTWARE\Policies\shotAI\Federation" /reg:64
```

Then in the app: **Settings → AI**. A configured machine shows a *Microsoft sign-in*
group instead of the API-key field. **Test connection** runs three legs separately
(sign-in, Claude access, Claude API) and names the one that failed, which is the only
way to tell "not signed in" from "not assigned the `shotAI.User` role" from a scope
problem — every access denial from Anthropic is deliberately the same opaque `401`.

Changing policy requires restarting shotAI, or reopening Settings, before the new
values are read.

## Granting and revoking access

Access is an Entra **app role assignment** (`shotAI.User`) on the audience app's
service principal, enforced by a condition on the Anthropic federation rule. Removing
the assignment revokes Claude access on the next token exchange. No app change, no
key rotation, no redeploy.

One caveat that costs real debugging time: after you grant the role, a user must sign
in **again**. A silent retry re-serves a cached Entra token (valid 60–90 minutes) that
predates the assignment and carries no role claim, which looks exactly like a broken
rule. The app's Sign in button forces a fresh interactive sign-in for this reason.
