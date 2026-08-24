// The app-wide Auth (#63): auth-core.ts wired to Electron. MAIN-ONLY.
//
// Kept separate from auth-core.ts purely so the factory stays electron-free and the
// wif-probe can drive the real production path under plain node.
import { app, net, safeStorage, shell } from 'electron';
import { promises as fs } from 'node:fs';
import path from 'node:path';
import { writeFileAtomic } from './atomic-write';
import { claudeLog } from './logger';
import { getApiKey } from './secrets';
import { createSafeStorageCachePlugin } from './entra/cache-plugin';
import { getFederationConfig } from './entra/config';
import { createAuth, type Auth } from './entra/auth-core';

export { ANTHROPIC_BASE_URL, type AuthMode } from './entra/auth-core';

let instance: Auth | null = null;

/**
 * Built lazily, and that laziness is load-bearing twice over: net.fetch requires
 * app.whenReady(), and safeStorage.isEncryptionAvailable() only returns true after
 * ready on Windows. Nothing auth-related may fire at launch anyway — capture,
 * editing, annotation and export are entirely local, and gating them on an identity
 * round trip would break the product's local-first claim. Auth is triggered by the
 * first Generate SOP click or by opening Settings > AI, and nowhere else.
 */
export function appAuth(): Auth {
  if (!instance) {
    instance = createAuth({
      // Chromium's network stack, so the Windows system proxy, WPAD/PAC and the
      // Windows certificate store apply to sign-in and the token exchange. undici
      // trusts only its own bundled CA set and fails opaquely behind a
      // TLS-inspecting corporate proxy, which is indistinguishable from a broken
      // federation rule.
      fetchImpl: net.fetch as unknown as typeof fetch,
      openBrowser: async (url) => {
        // shell.openExternal, never a shell command: there is no command line to
        // re-parse, so a URL containing '&' cannot be truncated the way `cmd /c
        // start` truncates it.
        await shell.openExternal(url);
      },
      cachePlugin: createSafeStorageCachePlugin({
        // Sibling of secrets.json, same userData dir, same 0600 intent.
        file: () => path.join(app.getPath('userData'), 'entra-cache.bin'),
        storage: safeStorage,
        io: {
          read: (p) => fs.readFile(p),
          // writeFileAtomic passes { encoding: 'utf8' }, which Node ignores for a
          // Buffer, so the ciphertext round-trips byte-exact — the same property
          // secrets.ts already relies on.
          write: (p, data) =>
            writeFileAtomic(p, data, {
              mode: 0o600,
              onRetry: (code) => claudeLog.warn(`entra cache rename ${code}, retrying`),
            }),
          remove: (p) => fs.unlink(p),
        },
        log: (l) => claudeLog.info(l),
      }),
      getFederationConfig,
      getApiKey,
      log: (l) => claudeLog.info(l),
    });
  }
  return instance;
}
