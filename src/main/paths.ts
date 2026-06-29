import { app } from 'electron';
import { existsSync } from 'node:fs';
import path from 'node:path';

/**
 * Absolute path to the app icon (shotAI.png) — used for the window/taskbar icon
 * and the About dialog. Prefers the packaged resources/ copy (Forge
 * extraResource, reliably readable outside the asar), falling back to the app
 * root in dev.
 */
export function appIconPath(): string {
  const packaged = process.resourcesPath
    ? path.join(process.resourcesPath, 'shotAI.png')
    : '';
  if (packaged && existsSync(packaged)) return packaged;
  return path.join(app.getAppPath(), 'shotAI.png');
}
