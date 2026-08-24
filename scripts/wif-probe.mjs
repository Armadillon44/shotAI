// Windows analogue of the macOS repo's Scripts/wif-probe.sh (#63).
//
// Exercises the three legs of federated auth SEPARATELY and names the one that
// failed, so "not signed in", "not assigned the role", and "config is wrong" are
// distinguishable before any UI exists. Every assertion denial from Anthropic is
// the same opaque 401 by design, so leg isolation is the only local diagnosis.
//
//   node --experimental-transform-types scripts/wif-probe.mjs
//
// Imports the real src/main/entra modules rather than reimplementing them, so the
// probe cannot drift from what ships. Node 24 runs the TypeScript directly.
//
// PROBE-ONLY CLIENT ID. This signs in as the Azure CLI's well-known public client,
// which Microsoft ships with http://localhost registered and which the audience app
// already pre-authorizes for user_impersonation — the same thing `az login --scope`
// does, and what the macOS probe relies on. It exists so the exchange can be
// verified WITHOUT first adding a redirect URI to the tenant. The shipping app uses
// its own client id from federation config and must never use this one.
import fs from 'node:fs';
import { spawn } from 'node:child_process';
import Anthropic from '@anthropic-ai/sdk';
import { validateFederationConfig } from '../src/main/entra/config-validate.ts';
import {
  ANTHROPIC_BASE_URL,
  REQUIRED_APP_ROLE,
  exchangeAssertion,
  hasAppRole,
} from '../src/main/entra/federation.ts';
import { scopeFor } from '../src/main/entra/msal.ts';
import { createAuth } from '../src/main/entra/auth-core.ts';

const AZURE_CLI_CLIENT_ID = '04b07795-8ddb-461a-bbee-02f9e1bf7b46';
const LOCAL = 'src/main/entra/federation.local.json';

const ok = (m) => console.log(`  [ok]   ${m}`);
const bad = (m) => console.log(`  [FAIL] ${m}`);
const info = (m) => console.log(`         ${m}`);
const leg = (n, m) => console.log(`\nLeg ${n}: ${m}`);

/** Decode a JWT payload for reporting. Never prints the token itself. */
function peek(jwt) {
  try {
    return JSON.parse(Buffer.from(jwt.split('.')[1], 'base64url').toString('utf8'));
  } catch {
    return null;
  }
}

function fail(msg) {
  bad(msg);
  process.exitCode = 1;
  return null;
}

// ---------------------------------------------------------------- Leg 0: config
leg(0, 'federation config');
if (!fs.existsSync(LOCAL)) {
  fail(`${LOCAL} not found. Copy federation.example.json and fill it in.`);
  process.exit(1);
}
const parsed = validateFederationConfig(JSON.parse(fs.readFileSync(LOCAL, 'utf8')));
if (!parsed.ok) {
  bad(`config rejected — missing: [${parsed.missing.join(', ')}] invalid: [${parsed.invalid.join(', ')}]`);
  process.exit(1);
}
const cfg = parsed.config;
ok('config valid');
info(`tenant ${cfg.tenantId}`);
info(`audience ${cfg.audienceAppId}${cfg.clientAppId === cfg.audienceAppId ? ' (also the client)' : ''}`);
info(`rule ${cfg.federationRuleId} / workspace ${cfg.workspaceId ?? '(rule default)'}`);

// ------------------------------------------------------------ Leg 1: Entra token
leg(1, 'Microsoft Entra sign-in (system browser)');
info(`scope ${scopeFor(cfg)}`);
info('probe client: Azure CLI public client (see the header note)');

// Prove MSAL actually drives our INetworkModule rather than its own client. This
// is the mitigation for msal-node v5 dropping proxyUrl/customAgentOptions; the app
// passes Electron net.fetch here so Chromium's proxy and cert-store handling apply.
// The probe counts the calls, because "the option is typed" is not the same as
// "MSAL used it".
let netCalls = 0;
const countingFetch = (url, init) => {
  netCalls++;
  return fetch(url, init);
};

const auth = createAuth({
  // Counted, and used for BOTH the MSAL network module and the SDK token exchange.
  fetchImpl: countingFetch,
  openBrowser: async (url) => {
    info(`handing off authorize URL (scope present: ${new URL(url).searchParams.has('scope')})`);
    // NOT `cmd /c start`: cmd re-parses the command line it receives and '&' is its
    // command separator, so the URL arrives TRUNCATED at the first '&' — client_id
    // survives, scope does not, and Entra answers AADSTS900144. rundll32 takes the
    // URL as one argv with no shell parsing. Probe-only: the app uses
    // shell.openExternal, which never involves a shell at all.
    spawn('rundll32', ['url.dll,FileProtocolHandler', url], {
      detached: true,
      stdio: 'ignore',
    }).unref();
  },
  // No cachePlugin: memory-only, so EVERY run is a fresh sign-in. That sidesteps
  // the trap where a cached 60-90 minute token predates a role assignment, carries
  // no roles claim, and makes a correct rule look broken.
  getFederationConfig: async () => ({ ...cfg, clientAppId: AZURE_CLI_CLIENT_ID }),
  // Force the federated branch: a stray ANTHROPIC_API_KEY must not decide this.
  getApiKey: async () => null,
  log: (l) => info(l),
});

const entra = await auth.entra();
if (!entra) {
  bad('createAuth reported federation unavailable');
  process.exit(1);
}

let assertion;
try {
  assertion = await entra.signInInteractive();
} catch (e) {
  bad(`sign-in failed: ${e?.errorCode ?? e?.name ?? 'Error'} ${e?.message ?? ''}`.trim());
  info('AADSTS50011 => http://localhost is not a registered public-client redirect URI.');
  info('AADSTS650057 => the audience app does not pre-authorize this client id.');
  process.exit(1);
}
ok('Entra returned a delegated access token');
if (netCalls > 0) ok(`MSAL routed ${netCalls} request(s) through our INetworkModule`);
else bad('MSAL did NOT use our INetworkModule — the proxy mitigation is not in effect');

const claims = peek(assertion) ?? {};
const bytes = Buffer.byteLength(assertion, 'utf8');
info(`upn/preferred_username: ${claims.preferred_username ?? claims.upn ?? '(absent)'}`);
info(`aud ${claims.aud}  ${claims.aud === cfg.audienceAppId ? '(bare GUID, correct)' : '(UNEXPECTED - must be the bare audience GUID)'}`);
info(`tid ${claims.tid}  ${claims.tid === cfg.tenantId ? '(matches config)' : '(MISMATCH)'}`);
info(`iss ${claims.iss}  ${String(claims.iss ?? '').endsWith('/v2.0') ? '(v2.0, correct)' : '(NOT v2.0 - the rule expects the v2.0 issuer)'}`);
const lifetime = Number(claims.exp) - Number(claims.iat);
info(`lifetime ${Number.isFinite(lifetime) ? `${lifetime}s (${Math.round(lifetime / 60)} min)` : 'unknown'}`);
if (Number.isFinite(lifetime) && lifetime > 3600) {
  info('over an hour, and accepted below => the issuer max_jwt_lifetime_seconds is NOT at its 1-hour default.');
}
info(`assertion size ${(bytes / 1024).toFixed(1)} KiB of the 16 KiB cap`);

const role = hasAppRole(assertion, REQUIRED_APP_ROLE);
if (role === true) ok(`roles contains ${REQUIRED_APP_ROLE}`);
else if (role === false)
  info(`roles does NOT contain ${REQUIRED_APP_ROLE} — expect leg 2 to be refused. Note an elevated directory role bypasses assignment gates and also arrives with no roles claim, so an admin account proves nothing about ordinary staff.`);
else info('could not parse roles (advisory only; attempting the exchange anyway)');

// ------------------------------------------------------------- Leg 2: exchange
leg(2, 'Anthropic token exchange (RFC 7523)');
let minted;
try {
  minted = await exchangeAssertion(assertion, cfg, {
    fetchImpl: fetch,
    log: (l) => info(l),
  });
  ok(`minted a token, expires_in ${minted.expiresInSeconds}s`);
  info(`scope ${minted.scope ?? '(none reported)'}`);
  info(`prefix ${minted.token.slice(0, 14)}... (rest withheld)`);
  // The mint is min(rule token_lifetime_seconds, 2x remaining JWT life), floor
  // 60s. Naming the binding constraint is the difference between "raise the rule"
  // and "the assertion was stale".
  const twoXjwt = Math.floor(((Number(claims.exp) * 1000 - Date.now()) * 2) / 1000);
  if (minted.expiresInSeconds <= twoXjwt) {
    info(`bound by the RULE's token_lifetime_seconds (${minted.expiresInSeconds}s); 2x remaining JWT life would have allowed ${twoXjwt}s.`);
    if (minted.expiresInSeconds <= 600) {
      info('600s is the Console wizard prefill. Raising the rule to 3600 removes the mid-generation refresh question almost entirely.');
    }
  } else {
    info(`bound by 2x remaining JWT life (${twoXjwt}s), not the rule.`);
  }
  if (minted.scope && minted.scope !== 'workspace:inference') {
    info(`NOTE: rule scope is ${minted.scope}. workspace:inference is the least-privilege fit for shotAI (Messages, Models, token counting); workspace:developer also grants Files, Skills and Managed Agents in the workspace.`);
  }
} catch (e) {
  bad(e?.message ?? String(e));
  if (role === false) info('The local role check already said this account lacks the app role.');
  process.exit(1);
}

// --------------------------------------------------------------- Leg 3: the API
leg(3, 'Anthropic API call with the minted token');
try {
  const client = new Anthropic({
    baseURL: ANTHROPIC_BASE_URL,
    authToken: minted.token,
    apiKey: null, // explicit: the constructor otherwise defaults it from env
  });
  const model = await client.models.retrieve('claude-sonnet-5');
  ok(`models.retrieve succeeded: ${model.id}`);
  info(`the minted token can reach the API under scope ${minted.scope ?? '(unreported)'}.`);
} catch (e) {
  bad(`${e?.constructor?.name ?? 'Error'}: ${e?.message ?? String(e)}`);
  info('A 403 AFTER a successful exchange means the rule scope is too narrow.');
  process.exit(1);
}

// ------------------------------------- Leg 4: the PRODUCTION auth path
leg(4, 'production path: SDK oidcFederationProvider + the client TokenCache');
try {
  const { client, mode } = await auth.makeClient();
  ok(`makeClient() chose mode: ${mode}`);
  if (mode !== 'federated') bad('expected the federated branch');
  const before = netCalls;
  const model = await client.models.retrieve('claude-sonnet-5');
  ok(`models.retrieve through the SDK credentials path: ${model.id}`);
  info(`the SDK made ${netCalls - before} exchange call(s) through our injected fetch`);
  // A second call proves the client TokenCache serves the cached token rather than
  // re-exchanging per request (oidcFederationProvider alone exchanges every time).
  const mid = netCalls;
  await client.models.retrieve('claude-sonnet-5');
  const extra = netCalls - mid;
  if (extra === 0) ok("second call re-used the cached token (0 extra exchanges)");
  else bad(`second call triggered ${extra} extra exchange(s) — the TokenCache is not engaged`);
} catch (e) {
  bad(`${e?.constructor?.name ?? 'Error'}: ${e?.message ?? String(e)}`);
  process.exit(1);
}

console.log('\nAll legs passed.');
if (role !== false) {
  console.log('');
  console.log('NOTE: this proves ACCEPTANCE only. A tid-only rule accepts every user in');
  console.log('the tenant and passes identically to a correctly scoped one, so proving');
  console.log('that the rule discriminates needs one run with a non-admin account that');
  console.log('has NO assignment.');
}
