// Atomic file write with a Windows-lock-tolerant rename.
//
// Windows transiently fails rename-over-an-existing-file with EPERM/EACCES/EBUSY
// when antivirus, the search indexer, or another reader briefly holds the
// destination open. The lock virtually always clears within a few hundred ms, so
// retry with backoff before giving up (the same approach as write-file-atomic).
// Shared by settings.ts (settings.json) and secrets.ts (secrets.json).
import { promises as fs } from 'node:fs';
import path from 'node:path';

const RENAME_RETRY_DELAYS_MS = [10, 25, 50, 100, 200, 350, 600];

export async function renameWithRetry(
  from: string,
  to: string,
  onRetry?: (code: string) => void,
): Promise<void> {
  for (let attempt = 0; ; attempt++) {
    try {
      await fs.rename(from, to);
      return;
    } catch (e) {
      const code = (e as NodeJS.ErrnoException).code ?? '';
      const retriable = code === 'EPERM' || code === 'EACCES' || code === 'EBUSY';
      if (!retriable || attempt >= RENAME_RETRY_DELAYS_MS.length) throw e;
      if (attempt === 0) onRetry?.(code);
      await new Promise((r) => setTimeout(r, RENAME_RETRY_DELAYS_MS[attempt]));
    }
  }
}

/**
 * Write `data` to `file` atomically: write a sibling tmp file, then rename over
 * the destination (retried on transient Windows locks). The tmp is cleaned up if
 * the rename ultimately fails, so an interrupted write can't corrupt the target.
 */
export async function writeFileAtomic(
  file: string,
  data: string | Buffer,
  opts?: { mode?: number; onRetry?: (code: string) => void },
): Promise<void> {
  await fs.mkdir(path.dirname(file), { recursive: true });
  const tmp = `${file}.${process.pid}.tmp`;
  await fs.writeFile(tmp, data, { encoding: 'utf8', mode: opts?.mode });
  try {
    await renameWithRetry(tmp, file, opts?.onRetry);
  } catch (e) {
    await fs.rm(tmp, { force: true }).catch(() => undefined);
    throw e;
  }
}
