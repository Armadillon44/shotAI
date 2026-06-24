// CaptureController — the Phase 1 capture engine.
//
// On each trigger (a system-wide mouse click via uiohook-napi, or a global
// hotkey), it synchronously gathers: the active window (get-windows), a
// screenshot of the monitor under the click (node-screenshots), and the click
// coordinates. It skips shotAI's own windows, writes the PNG into the project's
// shots/ folder, and appends a step to project.json. Captures are serialized so
// rapid clicks can't race.
//
// Region modes (window/area/all) + the selector overlay, and click-marker DPI
// calibration, are follow-up work; this defaults to capturing the monitor the
// click landed on ("screen" mode).
import { app, BrowserWindow, globalShortcut } from 'electron';
import { randomUUID } from 'node:crypto';
import path from 'node:path';
import { promises as fs } from 'node:fs';
import type { UiohookMouseEvent } from 'uiohook-napi';
import * as projectStore from './ProjectStore';
import type {
  CapturedWindow,
  Point,
  ProjectStep,
  StepClick,
} from '../shared/project';
import type { CaptureState } from '../shared/ipc';
import { IpcChannels } from '../shared/ipc';
import { captureLog } from './logger';

const DEFAULT_HOTKEY = 'CommandOrControl+Shift+S';
const OWN_WINDOW_TITLES = new Set(['shotAI', 'shotAI — Capture']);

type Natives = {
  uIOhook: typeof import('uiohook-napi').uIOhook;
  Monitor: typeof import('node-screenshots').Monitor;
  Window: typeof import('node-screenshots').Window;
  activeWindow: typeof import('get-windows').activeWindow;
};

type Session = {
  projectPath: string;
  projectTitle: string;
  paused: boolean;
  stepCount: number;
};

type Broadcast = (channel: string, payload: unknown) => void;

function mapButton(button: unknown): StepClick['button'] {
  // libuiohook button codes (uiohook-napi types this as `unknown`).
  switch (Number(button)) {
    case 1:
      return 'left';
    case 2:
      return 'right';
    case 3:
      return 'middle';
    default:
      return 'other';
  }
}

type ActiveLike = { owner: { name: string }; title: string } | null | undefined;

// Windows shell host processes whose windows are huge/transparent and capture
// as a black swath — observed from get-windows on Windows 11 (sometimes the
// friendly name, sometimes the exe). UI-element-precise bounds arrive with the
// Phase 4 element-at-point addon.
const SHELL_HOST_RE =
  /experience host|searchhost|shellexperiencehost|startmenuexperiencehost|searchapp|textinputhost|cortana/i;

/**
 * Pick a capture strategy for the click target:
 *  - 'window'     → a normal application window → capture just that window
 *  - 'region'     → an OS shell surface (taskbar, Start/Search, system tray,
 *                   notifications) → capture a tight region around the click
 *  - 'fullscreen' → the desktop, or an unidentified window → whole monitor
 */
function captureModeFor(active: ActiveLike): 'window' | 'region' | 'fullscreen' {
  if (!active) return 'fullscreen'; // unknown focus → full context, never a guessed crop
  const app = active.owner.name;
  const title = active.title;
  if (app === 'Windows Explorer' && title === 'Program Manager') return 'fullscreen'; // desktop
  if (app === 'Windows Explorer' && title.trim() === '') return 'region'; // taskbar / system tray
  if (SHELL_HOST_RE.test(app)) return 'region'; // Start / Search / Shell hosts
  return 'window';
}

export class CaptureController {
  private readonly broadcast: Broadcast;
  private natives: Natives | null = null;
  private session: Session | null = null;
  private hookAttached = false;
  private hotkeyRegistered = false;
  private queue: Promise<unknown> = Promise.resolve();

  private readonly onMouseDown = (event: UiohookMouseEvent): void => {
    if (!this.session || this.session.paused) return;
    this.enqueue(() =>
      this.captureStep('click', { x: event.x, y: event.y }, mapButton(event.button)),
    );
  };

  private readonly onHotkey = (): void => {
    if (!this.session || this.session.paused) return;
    this.enqueue(() => this.captureStep('hotkey', null));
  };

  constructor(broadcast: Broadcast) {
    this.broadcast = broadcast;
  }

  private async loadNatives(): Promise<Natives> {
    if (this.natives) return this.natives;
    const [uio, ns, gw] = await Promise.all([
      import('uiohook-napi'),
      import('node-screenshots'),
      import('get-windows'),
    ]);
    this.natives = {
      uIOhook: uio.uIOhook,
      Monitor: ns.Monitor,
      Window: ns.Window,
      activeWindow: gw.activeWindow,
    };
    return this.natives;
  }

  getState(): CaptureState {
    if (!this.session) {
      return { status: 'idle', projectPath: null, projectTitle: null, stepCount: 0 };
    }
    return {
      status: this.session.paused ? 'paused' : 'recording',
      projectPath: this.session.projectPath,
      projectTitle: this.session.projectTitle,
      stepCount: this.session.stepCount,
    };
  }

  private emitState(): void {
    this.broadcast(IpcChannels.captureStateChanged, this.getState());
  }

  /**
   * Start (or append to) a recording session. `attachHook: false` sets up the
   * session without starting the global input hook — used by the headless test.
   */
  async start(
    projectPath: string,
    opts: { attachHook?: boolean } = {},
  ): Promise<CaptureState> {
    const attachHook = opts.attachHook ?? true;

    // Don't clobber an in-progress session (a stray second start would misroute
    // queued captures into the wrong project).
    if (this.session) {
      if (this.session.projectPath !== projectPath) {
        throw new Error('A recording is already in progress for another project');
      }
      return this.getState();
    }

    await this.loadNatives();

    // Confined read (+ marks recently-opened) via ProjectStore.openProject.
    const manifest = await projectStore.openProject(projectPath);

    // Ensure shots/ exists (a project may have been moved/edited externally).
    const shotsDir = path.join(projectPath, 'shots');
    await fs.mkdir(shotsDir, { recursive: true });

    // Seed the step counter past any existing shot on disk so we never
    // overwrite a prior capture (steps may have been deleted/reordered).
    let stepCount = manifest.steps.length;
    try {
      for (const f of await fs.readdir(shotsDir)) {
        const m = /^step-(\d+)\.png$/i.exec(f);
        if (m) stepCount = Math.max(stepCount, Number(m[1]));
      }
    } catch {
      /* shots/ unreadable — fall back to manifest length */
    }

    this.session = {
      projectPath,
      projectTitle: manifest.title,
      paused: false,
      stepCount,
    };

    captureLog.info(
      `recording started: "${manifest.title}" (${manifest.steps.length} existing steps, next #${stepCount + 1}) at ${projectPath}`,
    );
    if (attachHook) this.attachTriggers();
    this.emitState();
    return this.getState();
  }

  private attachTriggers(): void {
    const { uIOhook } = this.natives!;
    if (!this.hookAttached) {
      uIOhook.on('mousedown', this.onMouseDown);
      uIOhook.start();
      this.hookAttached = true;
    }
    if (!this.hotkeyRegistered) {
      this.hotkeyRegistered = globalShortcut.register(DEFAULT_HOTKEY, this.onHotkey);
    }
  }

  private detachTriggers(): void {
    if (this.hookAttached && this.natives) {
      this.natives.uIOhook.off('mousedown', this.onMouseDown);
      this.natives.uIOhook.stop();
      this.hookAttached = false;
    }
    if (this.hotkeyRegistered) {
      globalShortcut.unregister(DEFAULT_HOTKEY);
      this.hotkeyRegistered = false;
    }
  }

  /** Synchronously release the global hook + hotkey (e.g. on app quit). */
  teardown(): void {
    this.detachTriggers();
  }

  pause(): CaptureState {
    if (this.session) this.session.paused = true;
    captureLog.info('recording paused');
    this.emitState();
    return this.getState();
  }

  resume(): CaptureState {
    if (this.session) this.session.paused = false;
    captureLog.info('recording resumed');
    this.emitState();
    return this.getState();
  }

  async stop(): Promise<CaptureState> {
    const count = this.session?.stepCount ?? 0;
    this.detachTriggers();
    await this.queue.catch(() => undefined); // let in-flight captures finish
    this.session = null;
    captureLog.info(`recording stopped (${count} steps total)`);
    this.emitState();
    return this.getState();
  }

  private enqueue(fn: () => Promise<unknown>): void {
    this.queue = this.queue.then(fn).catch((e) => {
      const msg = e instanceof Error ? e.message : String(e);
      captureLog.error('capture failed:', e);
      // Surface to the renderer so a long recording can't fail silently.
      this.broadcast(IpcChannels.captureError, msg);
    });
  }

  private isOwnWindow(win: { title: string; owner: { processId: number } }): boolean {
    if (win.owner.processId === process.pid) return true;
    if (OWN_WINDOW_TITLES.has(win.title)) return true;
    const ourPids = new Set<number>();
    for (const w of BrowserWindow.getAllWindows()) {
      try {
        ourPids.add(w.webContents.getOSProcessId());
      } catch {
        /* window gone */
      }
    }
    return ourPids.has(win.owner.processId);
  }

  /** Perform one capture into the active project. Public so the test can drive it. */
  async captureStep(
    trigger: ProjectStep['trigger'],
    point: Point | null,
    button: StepClick['button'] = 'left',
  ): Promise<ProjectStep | null> {
    // Re-check here (not just at enqueue) so a pause/stop that lands while
    // tasks are queued actually suppresses the backlog.
    if (!this.session || this.session.paused) return null;
    const { Monitor, Window, activeWindow } = await this.loadNatives();

    const active = await activeWindow();
    if (active && this.isOwnWindow(active)) {
      return null; // never record clicks on shotAI's own windows
    }

    // Monitor under the click — kept as step metadata.
    const monitor =
      (point ? Monitor.fromPoint(point.x, point.y) : null) ??
      Monitor.all().find((m) => m.isPrimary()) ??
      Monitor.all()[0];

    // Choose what to capture based on the click target:
    //  - normal app window → just that window (clean, focused shots)
    //  - shell element (taskbar/Start/tray/notifications) → a tight region
    //    around the click (avoids the giant black shell-window capture)
    //  - desktop / fallback → the whole monitor
    const mode = captureModeFor(active);
    const grab = ():
      | { png: Buffer; originX: number; originY: number }
      | null => {
      if (mode === 'window') {
        const focused = Window.all().find((w) => w.isFocused());
        if (
          focused &&
          focused.pid() !== process.pid &&
          focused.x() > -10000 &&
          focused.y() > -10000
        ) {
          try {
            return {
              png: focused.captureImageSync().toPngSync(),
              originX: focused.x(),
              originY: focused.y(),
            };
          } catch {
            // fall through to monitor capture
          }
        }
      }
      if (!monitor) return null;
      try {
        const full = monitor.captureImageSync();
        const mx = monitor.x();
        const my = monitor.y();
        if (mode === 'region' && point) {
          const sf = monitor.scaleFactor() || 1;
          const boxW = Math.min(Math.round(520 * sf), monitor.width());
          const boxH = Math.min(Math.round(400 * sf), monitor.height());
          const cx = point.x - mx;
          const cy = point.y - my;
          const cropX = Math.max(0, Math.min(cx - Math.floor(boxW / 2), monitor.width() - boxW));
          const cropY = Math.max(0, Math.min(cy - Math.floor(boxH / 2), monitor.height() - boxH));
          return {
            png: full.cropSync(cropX, cropY, boxW, boxH).toPngSync(),
            originX: mx + cropX,
            originY: my + cropY,
          };
        }
        return { png: full.toPngSync(), originX: mx, originY: my };
      } catch (e) {
        captureLog.warn('monitor capture failed:', e);
        return null;
      }
    };
    const grabbed = grab();
    if (!grabbed) return null;
    const { png, originX, originY } = grabbed;

    const order = ++this.session.stepCount;
    const filename = `step-${String(order).padStart(4, '0')}.png`;
    await fs.writeFile(
      path.join(this.session.projectPath, 'shots', filename),
      png,
      { flag: 'wx' }, // fail loudly rather than silently overwrite an existing shot
    );
    const window: CapturedWindow | null = active
      ? {
          app: active.owner.name,
          title: active.title,
          pid: active.owner.processId,
          bounds: active.bounds ?? null,
        }
      : null;

    const step: ProjectStep = {
      id: randomUUID(),
      order,
      screenshot: `shots/${filename}`,
      trigger,
      click: point
        ? {
            global: point,
            image: { x: point.x - originX, y: point.y - originY },
            button,
          }
        : null,
      monitor: monitor
        ? {
            id: monitor.id(),
            bounds: {
              x: monitor.x(),
              y: monitor.y(),
              width: monitor.width(),
              height: monitor.height(),
            },
            scaleFactor: monitor.scaleFactor(),
          }
        : null,
      window,
      element: { available: false, name: null, controlType: null, bounds: null },
      caption:
        trigger === 'click'
          ? `Click in ${window?.app ?? 'screen'}`
          : `Capture: ${window?.title ?? 'screen'}`,
      note: '',
      crop: null,
      annotations: [],
    };

    await projectStore.addStep(this.session.projectPath, step);
    captureLog.info(
      `step #${order} [${trigger}/${mode}] ${window?.app ?? 'screen'} -> ${filename} (${Math.round(png.length / 1024)} KB)`,
    );
    this.broadcast(IpcChannels.captureStepAdded, step);
    this.emitState();
    return step;
  }
}

/** Build a CaptureController that broadcasts events to all renderer windows. */
export function createCaptureController(): CaptureController {
  const broadcast: Broadcast = (channel, payload) => {
    for (const win of BrowserWindow.getAllWindows()) {
      if (!win.isDestroyed()) win.webContents.send(channel, payload);
    }
  };
  const controller = new CaptureController(broadcast);
  // Release the global input hook + shortcuts on quit so the uiohook worker
  // thread can't keep the process alive (zombie) on Windows.
  app.on('before-quit', () => {
    controller.teardown();
    globalShortcut.unregisterAll();
  });
  return controller;
}
