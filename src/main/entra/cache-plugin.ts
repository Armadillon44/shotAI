// MSAL token-cache persistence over Electron safeStorage (#63) — DPAPI on Windows,
// Keychain on macOS, the same primitive src/main/secrets.ts already uses for the
// BYO API key.
//
// MAIN-ONLY. The serialized cache contains the Entra REFRESH token and access
// token. It is never read by the renderer, never logged, never included in
// diagnostics or exports.
//
// What is persisted and why: only the refresh token is worth keeping. It is what
// makes a relaunch silent, it is revocable, it is Conditional-Access subject, and
// it is bound to a client id. The minted `sk-ant-oat01-` Anthropic token is
// deliberately NOT written anywhere — it lasts minutes, is cheap to re-mint, and at
// rest it would be a replayable bearer credential handed to backup, roaming
// profiles, AV/EDR file telemetry, and crash-dump collection for the rest of its
// life. One unavoidable compromise: MSAL's serialized cache holds the Entra ACCESS
// token alongside the refresh token and there is no knob to persist one without the
// other. Accepted, because the refresh token is the more valuable of the two and we
// are persisting it on purpose.
//
// Dependencies are injected so the invariants below (never write plaintext, an
// undecryptable cache is a miss and not a crash, no write when nothing changed) are
// unit-testable under plain node without electron.
import type { ICachePlugin, TokenCacheContext } from '@azure/msal-node';

/** The slice of Electron safeStorage this needs. */
export interface SafeStorageLike {
  isEncryptionAvailable(): boolean;
  encryptStringAsync(plainText: string): Promise<Buffer>;
  /** Electron resolves an OBJECT here, not a string: { shouldReEncrypt, result }.
   *  Passing the object straight to deserialize() silently corrupts the cache. */
  decryptStringAsync(encrypted: Buffer): Promise<{ shouldReEncrypt: boolean; result: string }>;
}

export interface CacheFileIo {
  read(path: string): Promise<Buffer>;
  write(path: string, data: Buffer): Promise<void>;
  remove(path: string): Promise<void>;
}

export interface CachePluginDeps {
  /** Lazy so app.getPath('userData') is only touched after ready. */
  file: () => string;
  storage: SafeStorageLike;
  io: CacheFileIo;
  /** Status only. Never cache contents, never token material. */
  log?: (line: string) => void;
}

export type ShotAiCachePlugin = ICachePlugin & {
  /** Sign-out: leave nothing on disk. */
  clear(): Promise<void>;
};

export function createSafeStorageCachePlugin(deps: CachePluginDeps): ShotAiCachePlugin {
  const { storage, io, log } = deps;

  return {
    async beforeCacheAccess(ctx: TokenCacheContext): Promise<void> {
      // Availability first. On Windows this only returns true after `ready`, and on
      // Linux with no secret store it is false for the whole session. That branch
      // must stay a real branch, not an assert: degrade to a memory-only session
      // (sign in each launch) rather than failing to start.
      if (!storage.isEncryptionAvailable()) {
        log?.('entra: OS encryption unavailable, token cache is memory-only this session.');
        return;
      }
      let cipher: Buffer;
      try {
        cipher = await io.read(deps.file());
      } catch {
        // Absent is the normal first-run state.
        return;
      }
      try {
        const { result, shouldReEncrypt } = await storage.decryptStringAsync(cipher);
        // `.result` is the string. The object itself is NOT the cache.
        ctx.tokenCache.deserialize(result);
        if (shouldReEncrypt) {
          // The OS key rotated or a stronger one became available. Rewriting now is
          // the entire reason for preferring the async API; skipping it leaves the
          // cache on a superseded key until it eventually stops decrypting.
          try {
            await io.write(deps.file(), await storage.encryptStringAsync(result));
            log?.('entra: token cache re-encrypted after an OS key rotation.');
          } catch {
            // Non-fatal: the cache still decrypted this time.
            log?.('entra: token cache re-encryption failed, will retry next launch.');
          }
        }
      } catch {
        // Torn write, or undecryptable because Local State was reset, the profile
        // is new, or userData moved. safeStorage does not DPAPI-protect our
        // ciphertext directly: Chromium OSCrypt keeps a random AES-256-GCM key,
        // DPAPI-protects THAT, and stores it as os_crypt.encrypted_key in Local
        // State. Losing Local State makes every cache entry undecryptable.
        // Degrade to "signed out", never to "app broken" — one interactive sign-in
        // self-heals it.
        log?.('entra: no usable token cache, sign-in required.');
      }
    },

    async afterCacheAccess(ctx: TokenCacheContext): Promise<void> {
      if (!ctx.cacheHasChanged) return;
      // Never write plaintext, and never call setUsePlainTextEncryption(true).
      if (!storage.isEncryptionAvailable()) return;
      try {
        await io.write(deps.file(), await storage.encryptStringAsync(ctx.tokenCache.serialize()));
      } catch {
        // A failed write costs one extra sign-in later; it must not break the
        // sign-in that is completing right now.
        log?.('entra: could not persist the token cache.');
      }
    },

    async clear(): Promise<void> {
      try {
        await io.remove(deps.file());
      } catch {
        // Already gone.
      }
    },
  };
}
