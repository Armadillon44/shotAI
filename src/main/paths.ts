import { app } from 'electron';
import { existsSync } from 'node:fs';
import path from 'node:path';

/**
 * Absolute path to the app icon (assets/shotAI_icon.png) — used for the
 * window/taskbar icon and the About dialog. Prefers the packaged resources/ copy
 * (Forge extraResource flattens it to resources/shotAI_icon.png, reliably
 * readable outside the asar), falling back to the repo assets/ dir in dev.
 */
/**
 * Absolute path to the bundled brand face (#77 phase 3), or '' if it is missing.
 *
 * ONLY the PDF renderer needs this. The app loads the same file through the
 * renderer's stylesheet, and the .html export deliberately NAMES the face rather
 * than embedding it — as base64 it is ~0.84MB against a measured 0.8-1.5MB
 * Freshservice paste budget, so embedding there would consume most of the budget
 * and the images would silently drop.
 *
 * A PDF is different: it embeds the glyphs it draws with, nothing pastes it into
 * a size-limited editor, and it is the one export where the brand face actually
 * reaches a reader who has never installed it. macOS gets that for free from
 * CoreText; here the PDF is printed from HTML by a browser engine, so it embeds
 * whatever the PAGE resolved — which means the page has to be given the file.
 *
 * Returns '' rather than throwing: a missing face must degrade to the fallback
 * stack, not fail an export.
 */
export function brandFontPath(): string {
  const packaged = process.resourcesPath
    ? path.join(process.resourcesPath, 'Archivo.ttf')
    : '';
  if (packaged && existsSync(packaged)) return packaged;
  const dev = path.join(app.getAppPath(), 'src', 'renderer', 'fonts', 'Archivo.ttf');
  return existsSync(dev) ? dev : '';
}

export function appIconPath(): string {
  const packaged = process.resourcesPath
    ? path.join(process.resourcesPath, 'shotAI_icon.png')
    : '';
  if (packaged && existsSync(packaged)) return packaged;
  return path.join(app.getAppPath(), 'assets', 'shotAI_icon.png');
}
