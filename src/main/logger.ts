// Centralized operational + error logging for the main process.
//
// Writes to the console AND to a file at userData/logs/shotai.log (so a user
// can send logs for troubleshooting), and auto-captures uncaughtException /
// unhandledRejection. Use the scoped loggers below so each line is tagged with
// its subsystem.
import log from 'electron-log/main';
import { app } from 'electron';
import path from 'node:path';

let initialized = false;

/** Configure logging once, as early as possible in the main process. */
export function initLogging(): void {
  if (initialized) return;
  initialized = true;

  const dev = !app.isPackaged;
  log.transports.console.level = dev ? 'debug' : 'info';
  log.transports.file.level = dev ? 'debug' : 'info';
  log.transports.file.resolvePathFn = () =>
    path.join(app.getPath('userData'), 'logs', 'shotai.log');
  log.transports.console.format = '[{h}:{i}:{s}.{ms}] [{level}]{scope} {text}';

  // Bound the log file so it can NEVER grow without limit. electron-log rotates
  // when the active file passes maxSize: shotai.log is renamed to shotai.old.log
  // (replacing any previous archive) and a fresh shotai.log starts — so on-disk
  // logs stay capped at ~2 files (one active + one archived) ≈ 2× maxSize. Pinned
  // explicitly rather than trusting the library default (currently 1 MB) so the
  // cap is guaranteed and visible even if that default changes.
  log.transports.file.maxSize = 5 * 1024 * 1024; // 5 MB per file → ~10 MB total

  // Surface uncaught errors + unhandled rejections (otherwise they vanish).
  log.errorHandler.startCatching({ showDialog: false });

  log.info(
    `shotAI starting — ${process.platform}/${process.arch} · electron ${process.versions.electron} · packaged=${app.isPackaged}`,
  );
  log.info(`logs: ${log.transports.file.getFile().path}`);
}

export const mainLog = log.scope('main');
export const captureLog = log.scope('capture');
export const ipcLog = log.scope('ipc');
export const projectsLog = log.scope('projects');
export const claudeLog = log.scope('claude');
export const ocrLog = log.scope('ocr');

export default log;
