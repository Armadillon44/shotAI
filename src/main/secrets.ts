/**
 * Encrypted secret storage for the Anthropic API key.
 *
 * The key lives ONLY in the main process. It is encrypted with Electron's
 * `safeStorage` (OS keychain / DPAPI) and written as base64 ciphertext to
 * `userData/secrets.json` — never to settings.json, never logged, never sent to
 * the renderer (which only ever learns a boolean status). For dev/CI, the
 * `ANTHROPIC_API_KEY` env var is used as a read-only fallback.
 */
import { app, safeStorage } from 'electron';
import { promises as fs } from 'node:fs';
import path from 'node:path';
import type { ApiKeyStatus } from '../shared/ipc';
import { writeFileAtomic } from './atomic-write';
import { claudeLog } from './logger';

interface SecretsFile {
  /** base64(safeStorage ciphertext of the API key). */
  apiKey?: string;
}

function secretsFile(): string {
  return path.join(app.getPath('userData'), 'secrets.json');
}

async function readSecrets(): Promise<SecretsFile> {
  try {
    const raw = await fs.readFile(secretsFile(), 'utf8');
    const parsed = JSON.parse(raw) as Partial<SecretsFile>;
    return { apiKey: typeof parsed.apiKey === 'string' ? parsed.apiKey : undefined };
  } catch {
    return {};
  }
}

async function writeSecrets(s: SecretsFile): Promise<void> {
  // Atomic, Windows-lock-tolerant write (shared with settings.ts). mode 0600 is
  // best-effort (honored on POSIX; ignored on Windows where userData is already
  // user-scoped and the value is safeStorage-encrypted anyway).
  await writeFileAtomic(secretsFile(), JSON.stringify(s), {
    mode: 0o600,
    onRetry: (code) =>
      claudeLog.warn(`secrets rename ${code} — retrying (lock likely transient)`),
  });
}

/** Decrypt a stored ciphertext to the plaintext key, or null if it can't be read. */
function decryptStored(b64: string): string | null {
  try {
    if (!safeStorage.isEncryptionAvailable()) return null;
    const plain = safeStorage.decryptString(Buffer.from(b64, 'base64')).trim();
    return plain.length ? plain : null;
  } catch {
    claudeLog.warn('secrets: stored key could not be decrypted');
    return null;
  }
}

function envKey(): string | null {
  const k = process.env.ANTHROPIC_API_KEY?.trim();
  return k && k.length ? k : null;
}

/**
 * MAIN-ONLY: the usable API key — the stored (decrypted) key if present, else the
 * env var, else null. NEVER expose the return value to the renderer.
 */
export async function getApiKey(): Promise<string | null> {
  const { apiKey } = await readSecrets();
  if (apiKey) {
    const k = decryptStored(apiKey);
    if (k) return k;
  }
  return envKey();
}

/** Renderer-safe status: whether a key exists and how — never the key itself. */
export async function getApiKeyStatus(): Promise<ApiKeyStatus> {
  const encryptionAvailable = safeStorage.isEncryptionAvailable();
  const { apiKey } = await readSecrets();
  // Ciphertext present on disk regardless of whether it currently decrypts — lets
  // the UI offer "Clear" for a key that became unreadable (machine migration,
  // OS keychain/DPAPI change, corrupted blob) instead of silently stranding it.
  const hasStoredCiphertext = !!apiKey;
  if (apiKey && decryptStored(apiKey)) {
    return { hasKey: true, source: 'stored', encryptionAvailable, hasStoredCiphertext };
  }
  if (envKey()) {
    return { hasKey: true, source: 'env', encryptionAvailable, hasStoredCiphertext };
  }
  return { hasKey: false, source: 'none', encryptionAvailable, hasStoredCiphertext };
}

/** Store the key encrypted. Throws if it's empty or secure storage is unavailable. */
export async function setApiKey(key: string): Promise<void> {
  const trimmed = key.trim();
  if (!trimmed) throw new Error('API key is empty.');
  if (!safeStorage.isEncryptionAvailable()) {
    throw new Error(
      'Secure storage is unavailable on this system, so the API key cannot be saved. Set the ANTHROPIC_API_KEY environment variable instead.',
    );
  }
  const cipher = safeStorage.encryptString(trimmed).toString('base64');
  await writeSecrets({ apiKey: cipher });
  claudeLog.info('API key saved (encrypted).');
}

/** Remove the stored key (the env-var fallback, if any, still applies). */
export async function clearApiKey(): Promise<void> {
  await writeSecrets({});
  claudeLog.info('Stored API key cleared.');
}
