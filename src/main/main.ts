import { app, BrowserWindow, protocol, session } from 'electron';
import path from 'node:path';
import { readFile } from 'node:fs/promises';
import started from 'electron-squirrel-startup';
import { registerIpcHandlers } from './ipc';
import { createCaptureController } from './CaptureController';
import { RegionService } from './RegionService';
import { resolveProjectFile } from './ProjectStore';
import { installAppMenu } from './menu';
import { initLogging, mainLog } from './logger';

initLogging();

// Custom scheme the sandboxed renderer uses to load a project's screenshots
// (it has no filesystem access). Must be registered before app `ready`.
// `standard` so paths normalize predictably; `secure` so it's a secure context
// (canvas can draw it CORS-clean for the flatten step). Resolution is confined
// to a project's own folder by an opaque id — see resolveProjectFile.
protocol.registerSchemesAsPrivileged([
  {
    scheme: 'shot',
    // corsEnabled is REQUIRED with supportFetchAPI on Electron 42+: the editor
    // loads screenshots with crossOrigin='anonymous' (a CORS-mode request) so the
    // canvas stays untainted for flatten()/toBlob(); without corsEnabled the load
    // is blocked before our handler runs (kCorsDisabledScheme) and Save breaks.
    // The handler returns Access-Control-Allow-Origin:'*' to complete the dance.
    privileges: {
      standard: true,
      secure: true,
      supportFetchAPI: true,
      stream: true,
      corsEnabled: true,
    },
  },
]);

const SHOT_MIME: Record<string, string> = {
  '.png': 'image/png',
  '.jpg': 'image/jpeg',
  '.jpeg': 'image/jpeg',
};

/** Serve shot://<projectId>/<relpath> from the registered project's folder. */
const registerShotProtocol = (): void => {
  protocol.handle('shot', async (request): Promise<Response> => {
    try {
      const url = new URL(request.url);
      const rel = decodeURIComponent(url.pathname).replace(/^\/+/, '');
      const mime = SHOT_MIME[path.extname(rel).toLowerCase()];
      if (!mime) return new Response('Unsupported type', { status: 403 });
      const abs = resolveProjectFile(url.hostname, rel);
      if (!abs) return new Response('Not found', { status: 404 });
      const data = await readFile(abs);
      return new Response(data, {
        headers: {
          'Content-Type': mime,
          // CORS-clean so the renderer can draw these into a canvas for the
          // flatten/redaction step (2b) without tainting it.
          'Access-Control-Allow-Origin': '*',
          'Cache-Control': 'no-cache',
        },
      });
    } catch (e) {
      mainLog.error('shot:// handler error:', e);
      return new Response('Error', { status: 500 });
    }
  });
  mainLog.info('shot:// protocol registered');
};

/**
 * Lock down what the packaged renderer can load. Skipped in dev so the Vite dev
 * server (inline scripts, HMR websocket) keeps working; the shipped app gets the
 * strict policy. `shot:`/`data:`/`blob:` are allowed for images (originals +
 * Konva-rendered previews).
 */
const applyContentSecurityPolicy = (): void => {
  if (!app.isPackaged) return;
  session.defaultSession.webRequest.onHeadersReceived((details, callback) => {
    callback({
      responseHeaders: {
        ...details.responseHeaders,
        'Content-Security-Policy': [
          "default-src 'self'; img-src 'self' shot: data: blob:; style-src 'self' 'unsafe-inline'; font-src 'self' data:; script-src 'self'; connect-src 'self'",
        ],
      },
    });
  });
};

// Windows-on-ARM VMs (this dev box) and headless/CI environments can't create
// a GPU context, which otherwise aborts startup ("GPU process isn't usable").
// Default to software rendering so the app launches; set SHOTAI_ENABLE_GPU=1 to
// use the GPU on machines that support it.
if (process.env.SHOTAI_ENABLE_GPU !== '1') {
  app.disableHardwareAcceleration();
  app.commandLine.appendSwitch('disable-gpu');
  app.commandLine.appendSwitch('disable-gpu-compositing');
  app.commandLine.appendSwitch('disable-software-rasterizer');
  app.commandLine.appendSwitch('in-process-gpu');
  // This VM can't initialize the OS sandbox, so child (renderer/GPU) processes
  // never start without this. The app only loads local bundled content.
  app.commandLine.appendSwitch('no-sandbox');
  mainLog.info('GPU disabled — software rendering (set SHOTAI_ENABLE_GPU=1 to enable)');
} else {
  mainLog.info('GPU enabled (SHOTAI_ENABLE_GPU=1)');
}

// Handle creating/removing shortcuts on Windows when installing/uninstalling.
if (started) {
  app.quit();
}

const preloadPath = path.join(__dirname, 'preload.js');

// The main project window + the always-on-top toolbar pill — tracked so capture
// can hide the main window and show the pill while recording (and the inverse).
let projectWindow: BrowserWindow | null = null;
let toolbarWindow: BrowserWindow | null = null;

/** Surface renderer load success/failure in the terminal (load failures are otherwise console-only). */
const wireLoadDiagnostics = (win: BrowserWindow, label: string): void => {
  win.webContents.on('did-finish-load', () => {
    mainLog.debug(`${label} window loaded`);
  });
  win.webContents.on(
    'did-fail-load',
    (_event, errorCode, errorDescription, validatedURL) => {
      mainLog.error(
        `${label} window FAILED to load (${errorCode} ${errorDescription}): ${validatedURL}`,
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

  win.setContentProtection(true); // keep shotAI out of its own screenshots
  wireLoadDiagnostics(win, 'project');
  projectWindow = win;
  win.on('closed', () => {
    projectWindow = null;
  });

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

  win.setContentProtection(true); // keep shotAI out of its own screenshots
  wireLoadDiagnostics(win, 'toolbar');
  toolbarWindow = win;
  win.on('closed', () => {
    toolbarWindow = null;
  });

  if (MAIN_WINDOW_VITE_DEV_SERVER_URL) {
    win.loadURL(`${MAIN_WINDOW_VITE_DEV_SERVER_URL}/toolbar.html`);
  } else {
    win.loadFile(
      path.join(__dirname, `../renderer/${MAIN_WINDOW_VITE_NAME}/toolbar.html`),
    );
  }

  // The pill stays hidden until a recording starts (shown via onRecordingChange).
  return win;
};

const createWindows = (): void => {
  mainLog.info(
    `runtime: ${process.platform}/${process.arch} · electron ${process.versions.electron} · chrome ${process.versions.chrome}`,
  );
  createProjectWindow();
  createToolbarWindow();
};

// Register IPC handlers once, then create windows, after Electron is ready.
app.whenReady().then(async () => {
  if (process.env.SHOTAI_SELFTEST === '1') {
    const { runSelfTest } = await import('./selftest');
    await runSelfTest();
    app.quit();
    return;
  }
  if (process.env.SHOTAI_CAPTURE_TEST === '1') {
    const { runCaptureTest } = await import('./capture-selftest');
    await runCaptureTest();
    app.quit();
    return;
  }
  const capture = createCaptureController({
    // Hide the main window while recording (the always-on-top toolbar pill is
    // the control); restore + focus it when recording stops.
    onRecordingChange: (recording) => {
      const proj =
        projectWindow && !projectWindow.isDestroyed() ? projectWindow : null;
      const pill =
        toolbarWindow && !toolbarWindow.isDestroyed() ? toolbarWindow : null;
      if (recording) {
        proj?.hide();
        pill?.show(); // pill only appears while recording
      } else {
        pill?.hide();
        proj?.show();
        proj?.focus();
      }
    },
  });
  const region = new RegionService(preloadPath);
  registerShotProtocol();
  applyContentSecurityPolicy();
  registerIpcHandlers(capture, region);
  installAppMenu(() =>
    projectWindow && !projectWindow.isDestroyed() ? projectWindow : null,
  );
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
