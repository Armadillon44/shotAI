import { describe, it, expect } from 'vitest';
import {
  createSafeStorageCachePlugin,
  type CacheFileIo,
  type SafeStorageLike,
} from './cache-plugin';
import type { TokenCacheContext } from '@azure/msal-node';

const FILE = 'C:\\users\\x\\AppData\\Roaming\\shotAI\\entra-cache.bin';

/** Fake safeStorage: reversible "encryption" so round-trips can be asserted. */
function storageStub(over: Partial<SafeStorageLike> & { available?: boolean } = {}): SafeStorageLike & {
  encrypted: string[];
} {
  const encrypted: string[] = [];
  const base: SafeStorageLike = {
    isEncryptionAvailable: () => over.available ?? true,
    encryptStringAsync: async (plain) => {
      encrypted.push(plain);
      return Buffer.from(`enc:${plain}`, 'utf8');
    },
    decryptStringAsync: async (cipher) => {
      const s = cipher.toString('utf8');
      if (!s.startsWith('enc:')) throw new Error('undecryptable');
      return { shouldReEncrypt: false, result: s.slice(4) };
    },
  };
  return Object.assign(base, over, { encrypted });
}

function ioStub(initial?: Buffer): CacheFileIo & { files: Map<string, Buffer>; removed: string[] } {
  const files = new Map<string, Buffer>();
  const removed: string[] = [];
  if (initial) files.set(FILE, initial);
  return {
    files,
    removed,
    read: async (p) => {
      const v = files.get(p);
      if (!v) throw Object.assign(new Error('ENOENT'), { code: 'ENOENT' });
      return v;
    },
    write: async (p, d) => {
      files.set(p, d);
    },
    remove: async (p) => {
      removed.push(p);
      files.delete(p);
    },
  };
}

/** Minimal TokenCacheContext double. */
function ctxStub(changed: boolean, serialized = '{"RefreshToken":{}}') {
  const state = { deserialized: null as string | null };
  const ctx = {
    cacheHasChanged: changed,
    tokenCache: {
      serialize: () => serialized,
      deserialize: (s: string) => {
        state.deserialized = s;
      },
    },
  } as unknown as TokenCacheContext;
  return { ctx, state };
}

describe('beforeCacheAccess', () => {
  it('deserializes the DECRYPTED STRING, not the safeStorage result object', async () => {
    // Electron resolves { shouldReEncrypt, result }. #63's snippet passes the whole
    // object to deserialize(); this asserts we hand over `.result`.
    const io = ioStub(Buffer.from('enc:{"RefreshToken":{"a":1}}', 'utf8'));
    const plugin = createSafeStorageCachePlugin({ file: () => FILE, storage: storageStub(), io });
    const { ctx, state } = ctxStub(false);
    await plugin.beforeCacheAccess(ctx);
    expect(state.deserialized).toBe('{"RefreshToken":{"a":1}}');
  });

  it('is a no-op when the cache file is absent (normal first run)', async () => {
    const plugin = createSafeStorageCachePlugin({ file: () => FILE, storage: storageStub(), io: ioStub() });
    const { ctx, state } = ctxStub(false);
    await expect(plugin.beforeCacheAccess(ctx)).resolves.toBeUndefined();
    expect(state.deserialized).toBeNull();
  });

  it('treats an undecryptable cache as a MISS, never an error', async () => {
    // Local State reset, a new Windows profile, or a moved userData all land here.
    // Degrade to "signed out", not "app broken".
    const io = ioStub(Buffer.from('garbage-not-our-ciphertext', 'utf8'));
    const lines: string[] = [];
    const plugin = createSafeStorageCachePlugin({
      file: () => FILE,
      storage: storageStub(),
      io,
      log: (l) => lines.push(l),
    });
    const { ctx, state } = ctxStub(false);
    await expect(plugin.beforeCacheAccess(ctx)).resolves.toBeUndefined();
    expect(state.deserialized).toBeNull();
    expect(lines.join(' ')).toMatch(/sign-in required/);
  });

  it('does not read the file at all when OS encryption is unavailable', async () => {
    // Linux with no secret store; the repo does ship maker-deb/maker-rpm.
    let reads = 0;
    const io = ioStub(Buffer.from('enc:x', 'utf8'));
    const wrapped: CacheFileIo = { ...io, read: async (p) => { reads++; return io.read(p); } };
    const plugin = createSafeStorageCachePlugin({
      file: () => FILE,
      storage: storageStub({ available: false }),
      io: wrapped,
    });
    await plugin.beforeCacheAccess(ctxStub(false).ctx);
    expect(reads).toBe(0);
  });

  it('re-encrypts when the OS signals a key rotation', async () => {
    const io = ioStub(Buffer.from('enc:PAYLOAD', 'utf8'));
    const storage = storageStub({
      decryptStringAsync: async (c) => ({ shouldReEncrypt: true, result: c.toString('utf8').slice(4) }),
    });
    const plugin = createSafeStorageCachePlugin({ file: () => FILE, storage, io });
    await plugin.beforeCacheAccess(ctxStub(false).ctx);
    // Honoring shouldReEncrypt is the entire reason to prefer the async API;
    // ignoring it strands the cache on a superseded key.
    expect(storage.encrypted).toContain('PAYLOAD');
  });

  it('still loads the cache when re-encryption fails', async () => {
    const io = ioStub(Buffer.from('enc:PAYLOAD', 'utf8'));
    const failing: CacheFileIo = { ...io, write: async () => { throw new Error('EPERM'); } };
    const storage = storageStub({
      decryptStringAsync: async (c) => ({ shouldReEncrypt: true, result: c.toString('utf8').slice(4) }),
    });
    const plugin = createSafeStorageCachePlugin({ file: () => FILE, storage, io: failing });
    const { ctx, state } = ctxStub(false);
    await expect(plugin.beforeCacheAccess(ctx)).resolves.toBeUndefined();
    // The decrypt succeeded this time, so the session must still work.
    expect(state.deserialized).toBe('PAYLOAD');
  });
});

describe('afterCacheAccess', () => {
  it('writes the encrypted cache when it changed', async () => {
    const io = ioStub();
    const storage = storageStub();
    const plugin = createSafeStorageCachePlugin({ file: () => FILE, storage, io });
    await plugin.afterCacheAccess(ctxStub(true, '{"RT":1}').ctx);
    expect(storage.encrypted).toEqual(['{"RT":1}']);
    expect(io.files.get(FILE)?.toString('utf8')).toBe('enc:{"RT":1}');
  });

  it('does NOT write when nothing changed', async () => {
    const io = ioStub();
    const plugin = createSafeStorageCachePlugin({ file: () => FILE, storage: storageStub(), io });
    await plugin.afterCacheAccess(ctxStub(false).ctx);
    expect(io.files.size).toBe(0);
  });

  it('NEVER writes plaintext when encryption is unavailable', async () => {
    // The one invariant that must not regress: no plaintext refresh token on disk,
    // and no setUsePlainTextEncryption(true) anywhere.
    const io = ioStub();
    const plugin = createSafeStorageCachePlugin({
      file: () => FILE,
      storage: storageStub({ available: false }),
      io,
    });
    await plugin.afterCacheAccess(ctxStub(true, 'SECRET-REFRESH-TOKEN').ctx);
    expect(io.files.size).toBe(0);
  });

  it('survives a failed write rather than breaking the sign-in in flight', async () => {
    const io = ioStub();
    const failing: CacheFileIo = { ...io, write: async () => { throw new Error('EACCES'); } };
    const lines: string[] = [];
    const plugin = createSafeStorageCachePlugin({
      file: () => FILE,
      storage: storageStub(),
      io: failing,
      log: (l) => lines.push(l),
    });
    await expect(plugin.afterCacheAccess(ctxStub(true).ctx)).resolves.toBeUndefined();
    expect(lines.join(' ')).toMatch(/could not persist/);
  });

  it('never logs cache contents', async () => {
    const lines: string[] = [];
    const io = ioStub();
    const failing: CacheFileIo = { ...io, write: async () => { throw new Error('EACCES'); } };
    const plugin = createSafeStorageCachePlugin({
      file: () => FILE,
      storage: storageStub(),
      io: failing,
      log: (l) => lines.push(l),
    });
    await plugin.afterCacheAccess(ctxStub(true, 'SUPER-SECRET-REFRESH').ctx);
    expect(lines.join('\n')).not.toContain('SUPER-SECRET-REFRESH');
  });
});

describe('clear', () => {
  it('removes the cache file so sign-out leaves nothing on disk', async () => {
    const io = ioStub(Buffer.from('enc:x', 'utf8'));
    const plugin = createSafeStorageCachePlugin({ file: () => FILE, storage: storageStub(), io });
    await plugin.clear();
    expect(io.removed).toEqual([FILE]);
    expect(io.files.size).toBe(0);
  });

  it('is a no-op when there is no file', async () => {
    const io = ioStub();
    const plugin = createSafeStorageCachePlugin({ file: () => FILE, storage: storageStub(), io });
    await expect(plugin.clear()).resolves.toBeUndefined();
  });
});
