import { app, BrowserWindow } from 'electron';
import path from 'node:path';
import started from 'electron-squirrel-startup';
import { registerIpcHandlers } from './ipc';

// Headless / VM / remote-desktop environments may lack a usable GPU, which
// otherwise aborts startup ("GPU process isn't usable"). Opt into software
// rendering there via SHOTAI_DISABLE_GPU=1 (used for CI / automated launches).
if (process.env.SHOTAI_DISABLE_GPU === '1') {
  app.disableHardwareAcceleration();
  app.commandLine.appendSwitch('no-sandbox');
  app.commandLine.appendSwitch('disable-gpu');
  app.commandLine.appendSwitch('disable-gpu-compositing');
  app.commandLine.appendSwitch('disable-software-rasterizer');
  app.commandLine.appendSwitch('in-process-gpu');
}

// Handle creating/removing shortcuts on Windows when installing/uninstalling.
if (started) {
  app.quit();
}

const preloadPath = path.join(__dirname, 'preload.js');

/** Surface renderer load success/failure in the terminal (load failures are otherwise console-only). */
const wireLoadDiagnostics = (win: BrowserWindow, label: string): void => {
  win.webContents.on('did-finish-load', () => {
    if (!app.isPackaged) {
      console.log(`[shotAI] ${label} window loaded`);
    }
  });
  win.webContents.on(
    'did-fail-load',
    (_event, errorCode, errorDescription, validatedURL) => {
      console.error(
        `[shotAI] ${label} window FAILED to load (${errorCode} ${errorDescription}): ${validatedURL}`,
      );
    },
  );
};

/**
 * The main project window: recent projects, the step list, the HTML report
 * with the inline image editor, SOP generation, and export. Where editing happens.
 */
const createProjectWindow = (): BrowserWindow => {
  const win = new BrowserWindow({
    width: 1100,
    height: 740,
    minWidth: 800,
    minHeight: 560,
    show: false,
    title: 'shotAI',
    webPreferences: {
      preload: preloadPath,
      contextIsolation: true,
      nodeIntegration: false,
      sandbox: true,
    },
  });

  wireLoadDiagnostics(win, 'project');

  if (MAIN_WINDOW_VITE_DEV_SERVER_URL) {
    win.loadURL(MAIN_WINDOW_VITE_DEV_SERVER_URL);
  } else {
    win.loadFile(
      path.join(__dirname, `../renderer/${MAIN_WINDOW_VITE_NAME}/index.html`),
    );
  }

  win.once('ready-to-show', () => win.show());
  return win;
};

/**
 * The capture toolbar: a small, frameless, always-on-top, draggable window
 * (Scribe's recorder pill). Drag handling lives in the renderer via
 * `-webkit-app-region: drag`.
 */
const createToolbarWindow = (): BrowserWindow => {
  const win = new BrowserWindow({
    width: 300,
    height: 68,
    show: false,
    frame: false,
    resizable: false,
    maximizable: false,
    fullscreenable: false,
    alwaysOnTop: true,
    skipTaskbar: true,
    title: 'shotAI — Capture',
    webPreferences: {
      preload: preloadPath,
      contextIsolation: true,
      nodeIntegration: false,
      sandbox: true,
    },
  });

  wireLoadDiagnostics(win, 'toolbar');

  if (MAIN_WINDOW_VITE_DEV_SERVER_URL) {
    win.loadURL(`${MAIN_WINDOW_VITE_DEV_SERVER_URL}/toolbar.html`);
  } else {
    win.loadFile(
      path.join(__dirname, `../renderer/${MAIN_WINDOW_VITE_NAME}/toolbar.html`),
    );
  }

  win.once('ready-to-show', () => win.show());
  return win;
};

const createWindows = (): void => {
  if (!app.isPackaged) {
    console.log(
      `[shotAI] runtime: ${process.platform}/${process.arch} · electron ${process.versions.electron} · chrome ${process.versions.chrome}`,
    );
  }
  createProjectWindow();
  createToolbarWindow();
};

// Register IPC handlers once, then create windows, after Electron is ready.
app.whenReady().then(() => {
  registerIpcHandlers();
  createWindows();
});

// Quit when all windows are closed, except on macOS.
app.on('window-all-closed', () => {
  if (process.platform !== 'darwin') {
    app.quit();
  }
});

app.on('activate', () => {
  // On macOS re-create windows when the dock icon is clicked and none are open.
  if (BrowserWindow.getAllWindows().length === 0) {
    createWindows();
  }
});
