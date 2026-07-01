import { app, BrowserWindow, protocol, screen, session } from 'electron';
import path from 'node:path';
import { readFile } from 'node:fs/promises';
import started from 'electron-squirrel-startup';
import { registerIpcHandlers } from './ipc';
import { createCaptureController } from './CaptureController';
import { RegionService } from './RegionService';
import { resolveProjectFile } from './project-store';
import { installAppMenu } from './menu';
import { appIconPath } from './paths';
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
  mainLog.info('GPU disabled — software rendering (set SHOTAI_ENABLE_GPU=1 to enable)');
} else {
  mainLog.info('GPU enabled (SHOTAI_ENABLE_GPU=1)');
}

// Keep the OS process sandbox ON by default (the windows also set sandbox:true).
// Disabling HW acceleration above does NOT require --no-sandbox. Some virtualized
// hosts (e.g. this Windows-on-ARM dev VM) can't initialize the OS sandbox, so
// child renderer/GPU processes won't start; opt OUT with SHOTAI_NO_SANDBOX=1 on
// THOSE machines only. Shipped installs keep the sandbox — it's the one OS-level
// containment for the renderer, which decodes attacker-influenceable image pixels.
if (process.env.SHOTAI_NO_SANDBOX === '1') {
  app.commandLine.appendSwitch('no-sandbox');
  mainLog.warn('OS sandbox DISABLED (SHOTAI_NO_SANDBOX=1) — dev/VM workaround only');
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
// The pill auto-docks to the top-center of the screen the first time it's shown;
// after that the user's drag position is respected (don't re-dock on resume).
let toolbarPositioned = false;

/** Dock the toolbar pill to the top-center of the work area of the display under
 *  the main window (DIP coords). Called once per show; the user can drag it after. */
const dockToolbarTopCenter = (win: BrowserWindow): void => {
  try {
    const display = projectWindow && !projectWindow.isDestroyed()
      ? screen.getDisplayMatching(projectWindow.getBounds())
      : screen.getPrimaryDisplay();
    const area = display.workArea;
    const { width } = win.getBounds();
    const x = Math.round(area.x + (area.width - width) / 2);
    const y = area.y + 8; // a small gap below the top edge
    win.setPosition(x, y, false);
  } catch {
    /* positioning is best-effort */
  }
};

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
    icon: appIconPath(),
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
    // The toolbar pill is a hidden, skipTaskbar helper window — on its own it
    // keeps the app alive (and out of the taskbar) after the main window's X is
    // clicked, so the process lingers invisibly. Tear it down so window-all-closed
    // fires and the app fully exits (which still honors the macOS convention).
    if (toolbarWindow && !toolbarWindow.isDestroyed()) {
      toolbarWindow.destroy();
    }
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
    width: 380,
    height: 52,
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
  toolbarPositioned = false; // a fresh pill re-docks on its next show
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
  // Lock every window down: deny window.open/new-window and confine top-level
  // navigation to local app origins (the bundled file://, the shot:// scheme, or
  // the Vite dev server in dev). Nothing in shotAI navigates externally; this
  // stops a future link / innerHTML / window.open sink — or compromised on-screen
  // content rendered into a window — from steering it to a remote origin that
  // would then reach the full IPC surface. Registered once, before any window.
  app.on('web-contents-created', (_e, contents) => {
    contents.setWindowOpenHandler(() => ({ action: 'deny' }));
    const confine = (e: Electron.Event, url: string) => {
      const ok =
        url.startsWith('shot:') ||
        url.startsWith('file:') ||
        (!!MAIN_WINDOW_VITE_DEV_SERVER_URL && url.startsWith(MAIN_WINDOW_VITE_DEV_SERVER_URL));
      if (!ok) {
        e.preventDefault();
        mainLog.warn(`blocked navigation to ${url}`);
      }
    };
    contents.on('will-navigate', confine);
    contents.on('will-redirect', confine);
  });

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
  // Debug/test mode: keep the app window visible during capture so you can watch
  // what's happening (it WILL appear in the screenshots — for diagnosis only).
  const noHideDuringCapture = process.env.SHOTAI_CAPTURE_NO_HIDE === '1';
  if (noHideDuringCapture) {
    mainLog.info('SHOTAI_CAPTURE_NO_HIDE=1 — app window stays visible during capture (debug)');
  }
  const capture = createCaptureController({
    // Hide the main window while recording (the always-on-top toolbar pill is
    // the control); restore + focus it when recording stops. Debug mode keeps it
    // visible throughout.
    onRecordingChange: (recording) => {
      const proj =
        projectWindow && !projectWindow.isDestroyed() ? projectWindow : null;
      const pill =
        toolbarWindow && !toolbarWindow.isDestroyed() ? toolbarWindow : null;
      if (recording) {
        if (!noHideDuringCapture) proj?.hide();
        if (pill && !toolbarPositioned) {
          dockToolbarTopCenter(pill); // top-center on first show; drag respected after
          toolbarPositioned = true;
        }
        pill?.show(); // pill only appears while recording
      } else {
        pill?.hide();
        proj?.show(); // no-op if never hidden; also refocuses after recording
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
