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

export default log;
