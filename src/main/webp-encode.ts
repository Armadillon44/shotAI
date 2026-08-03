// WebP encoding for the styled HTML export (#56).
//
// Electron's `nativeImage` can only WRITE png and jpeg — there is no WebP encoder in
// the main process, and adding a native image library for one export format isn't
// worth the dependency. Chromium itself encodes WebP, so this borrows it through a
// hidden, sandboxed renderer: hand it PNG bytes, get WebP bytes back.
//
// The macOS app solves the same problem with ImageIO/AVIF, which has no Electron
// equivalent (Chromium can decode AVIF but not encode it). WebP is the closest
// available codec that is both natively encodable here and dramatically smaller than
// PNG on screenshots.
//
// Everything degrades safely: a failure at any point returns null and the caller
// keeps its PNG bytes, so the worst case is a bigger file, never a broken export.
import { BrowserWindow } from 'electron';
import { mainLog } from './logger';

/** Magic bytes for a RIFF/WEBP container — cheap validation of what came back. */
function isWebp(buf: Buffer): boolean {
  return (
    buf.length > 12 &&
    buf.toString('ascii', 0, 4) === 'RIFF' &&
    buf.toString('ascii', 8, 12) === 'WEBP'
  );
}

/**
 * A short-lived hidden renderer used to encode a batch of images, then disposed.
 *
 * One window is reused for the whole export — creating one per image would dominate
 * the cost. Callers MUST call `dispose()` (a `finally` block) or the window leaks and
 * keeps the app alive.
 */
export class WebpEncoder {
  private win: BrowserWindow | null = null;
  /** null = not probed yet; false = this Chromium won't encode WebP, stop trying. */
  private supported: boolean | null = null;

  private async ensureWindow(): Promise<BrowserWindow | null> {
    if (this.win && !this.win.isDestroyed()) return this.win;
    try {
      const win = new BrowserWindow({
        show: false,
        webPreferences: {
          offscreen: true,
          sandbox: true,
          contextIsolation: true,
          nodeIntegration: false,
        },
      });
      // A blank document is all that's needed; nothing is loaded from disk or network.
      await win.loadURL('data:text/html,<!doctype html><meta charset="utf-8">');
      this.win = win;
      return win;
    } catch (e) {
      mainLog.warn('webp: could not create the encoder window:', e);
      return null;
    }
  }

  /**
   * Encode `png` as WebP at `quality` (0..1), or null to fall back to the PNG.
   *
   * The image is decoded and re-encoded inside the renderer via a canvas. It runs on
   * bytes that already passed the fail-closed redaction gate, so this can only ever
   * lose detail — it can't resurrect a redacted region.
   */
  async encode(png: Buffer, quality: number): Promise<Buffer | null> {
    if (this.supported === false) return null;
    const win = await this.ensureWindow();
    if (!win) return null;
    try {
      if (this.supported === null) {
        this.supported = await win.webContents.executeJavaScript(
          `document.createElement('canvas').toDataURL('image/webp').startsWith('data:image/webp')`,
        );
        if (!this.supported) {
          mainLog.warn('webp: this build cannot encode WebP; keeping PNG');
          return null;
        }
      }
      const dataUri = `data:image/png;base64,${png.toString('base64')}`;
      const out: string = await win.webContents.executeJavaScript(
        `(async () => {
          const img = new Image();
          img.src = ${JSON.stringify(dataUri)};
          await img.decode();
          const c = document.createElement('canvas');
          c.width = img.naturalWidth;
          c.height = img.naturalHeight;
          const ctx = c.getContext('2d');
          if (!ctx) return '';
          ctx.drawImage(img, 0, 0);
          return c.toDataURL('image/webp', ${quality});
        })()`,
      );
      if (!out.startsWith('data:image/webp;base64,')) return null;
      const buf = Buffer.from(out.slice('data:image/webp;base64,'.length), 'base64');
      // Only accept it if it's genuinely WebP AND actually smaller.
      if (!isWebp(buf) || buf.length === 0 || buf.length >= png.length) return null;
      return buf;
    } catch (e) {
      mainLog.warn('webp: encode failed, keeping PNG:', e);
      return null;
    }
  }

  dispose(): void {
    if (this.win && !this.win.isDestroyed()) this.win.destroy();
    this.win = null;
  }
}
