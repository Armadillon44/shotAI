import { app } from 'electron';
import { existsSync } from 'node:fs';
import path from 'node:path';

/**
 * Absolute path to the app icon (assets/shotAI_icon.png) — used for the
 * window/taskbar icon and the About dialog. Prefers the packaged resources/ copy
 * (Forge extraResource flattens it to resources/shotAI_icon.png, reliably
 * readable outside the asar), falling back to the repo assets/ dir in dev.
 */
export function appIconPath(): string {
  const packaged = process.resourcesPath
    ? path.join(process.resourcesPath, 'shotAI_icon.png')
    : '';
  if (packaged && existsSync(packaged)) return packaged;
  return path.join(app.getAppPath(), 'assets', 'shotAI_icon.png');
}
