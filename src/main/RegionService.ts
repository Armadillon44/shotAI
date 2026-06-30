// RegionService — the "Area" capture mode's drag-select overlay.
//
// Opens a transparent, always-on-top, frameless window over each display. The
// overlay renderer (src/renderer/overlay) lets the user drag a rectangle and
// reports it in CSS pixels relative to its window; here we convert that to
// GLOBAL PHYSICAL pixels (what CaptureTarget.area / the capture path expect):
//   client CSS px (== DIP within the window)
//     + window origin (DIP)            → global DIP rect
//     → screen.dipToScreenRect()       → global physical px rect
//
// Selection happens before recording starts, so the overlay and capture never
// coexist; the overlay is still content-protected for good measure.
import { BrowserWindow, ipcMain, screen, type IpcMainEvent } from 'electron';
import path from 'node:path';
import { IpcChannels } from '../shared/ipc';
import type { Rect } from '../shared/project';
import { parseRect } from '../shared/project';
import { mainLog } from './logger';

const MIN_DRAG = 4; // CSS px — anything smaller is a stray click, treat as cancel

export class RegionService {
  private readonly preloadPath: string;
  private pending: {
    resolve: (rect: Rect | null) => void;
    windows: BrowserWindow[];
  } | null = null;

  constructor(preloadPath: string) {
    this.preloadPath = preloadPath;
    ipcMain.on(IpcChannels.regionComplete, (event: IpcMainEvent, rect: unknown) => {
      this.finish(event, parseRect(rect));
    });
    ipcMain.on(IpcChannels.regionCancel, (event: IpcMainEvent) => {
      this.finish(event, null);
    });
  }

  /**
   * Open the overlay across all displays and resolve with the chosen rectangle
   * in global physical pixels, or null if the user cancelled.
   */
  selectArea(): Promise<Rect | null> {
    if (this.pending) this.teardown(null); // cancel any in-flight selection
    return new Promise<Rect | null>((resolve) => {
      const windows = screen.getAllDisplays().map((d) => this.createOverlay(d));
      this.pending = { resolve, windows };
      mainLog.debug(`region: overlay opened across ${windows.length} display(s)`);
    });
  }

  private createOverlay(display: Electron.Display): BrowserWindow {
    const { x, y, width, height } = display.bounds; // DIP
    const win = new BrowserWindow({
      x,
      y,
      width,
      height,
      frame: false,
      transparent: true,
      backgroundColor: '#00000000',
      hasShadow: false,
      alwaysOnTop: true,
      skipTaskbar: true,
      resizable: false,
      movable: false,
      minimizable: false,
      maximizable: false,
      fullscreenable: false,
      enableLargerThanScreen: true,
      show: false,
      webPreferences: {
        preload: this.preloadPath,
        contextIsolation: true,
        nodeIntegration: false,
        sandbox: true,
      },
    });
    win.setContentProtection(true);
    win.setAlwaysOnTop(true, 'screen-saver');

    if (MAIN_WINDOW_VITE_DEV_SERVER_URL) {
      void win.loadURL(`${MAIN_WINDOW_VITE_DEV_SERVER_URL}/overlay.html`);
    } else {
      void win.loadFile(
        path.join(__dirname, `../renderer/${MAIN_WINDOW_VITE_NAME}/overlay.html`),
      );
    }

    win.once('ready-to-show', () => {
      if (win.isDestroyed()) return;
      win.show();
      win.focus();
    });
    // If an overlay is dismissed out from under us (e.g. app quit), don't leave
    // selectArea() hanging.
    win.on('closed', () => {
      if (this.pending?.windows.includes(win)) this.teardown(null);
    });
    return win;
  }

  private finish(event: IpcMainEvent, cssRect: Rect | null): void {
    if (!this.pending) return;
    let result: Rect | null = null;
    if (cssRect && cssRect.width >= MIN_DRAG && cssRect.height >= MIN_DRAG) {
      const win = BrowserWindow.fromWebContents(event.sender);
      if (win && !win.isDestroyed()) {
        const b = win.getBounds(); // DIP
        const phys = screen.dipToScreenRect(win, {
          x: b.x + Math.round(cssRect.x),
          y: b.y + Math.round(cssRect.y),
          width: Math.round(cssRect.width),
          height: Math.round(cssRect.height),
        });
        result = { x: phys.x, y: phys.y, width: phys.width, height: phys.height };
        mainLog.info(
          `region selected: ${result.width}x${result.height} @ (${result.x},${result.y}) [physical px]`,
        );
      }
    } else {
      mainLog.debug('region: selection cancelled');
    }
    this.teardown(result);
  }

  private teardown(result: Rect | null): void {
    const pending = this.pending;
    this.pending = null;
    if (!pending) return;
    for (const w of pending.windows) {
      if (!w.isDestroyed()) w.close();
    }
    pending.resolve(result);
  }
}
