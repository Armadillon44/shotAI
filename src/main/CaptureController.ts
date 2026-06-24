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

const DEFAULT_HOTKEY = 'CommandOrControl+Shift+S';
const OWN_WINDOW_TITLES = new Set(['shotAI', 'shotAI — Capture']);

type Natives = {
  uIOhook: typeof import('uiohook-napi').uIOhook;
  Monitor: typeof import('node-screenshots').Monitor;
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
    await this.loadNatives();

    // Confined read (+ marks recently-opened) via ProjectStore.openProject.
    const manifest = await projectStore.openProject(projectPath);
    this.session = {
      projectPath,
      projectTitle: manifest.title,
      paused: false,
      stepCount: manifest.steps.length,
    };

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

  pause(): CaptureState {
    if (this.session) this.session.paused = true;
    this.emitState();
    return this.getState();
  }

  resume(): CaptureState {
    if (this.session) this.session.paused = false;
    this.emitState();
    return this.getState();
  }

  async stop(): Promise<CaptureState> {
    this.detachTriggers();
    await this.queue.catch(() => undefined); // let in-flight captures finish
    this.session = null;
    this.emitState();
    return this.getState();
  }

  private enqueue(fn: () => Promise<unknown>): void {
    this.queue = this.queue.then(fn).catch((e) => {
      console.error('[shotAI] capture error:', (e as Error).message);
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
    if (!this.session) return null;
    const { Monitor, activeWindow } = await this.loadNatives();

    const active = await activeWindow();
    if (active && this.isOwnWindow(active)) {
      return null; // never record clicks on shotAI's own windows
    }

    const monitor =
      (point ? Monitor.fromPoint(point.x, point.y) : null) ??
      Monitor.all().find((m) => m.isPrimary()) ??
      Monitor.all()[0];
    if (!monitor) return null;

    const png = monitor.captureImageSync().toPngSync();

    const order = ++this.session.stepCount;
    const filename = `step-${String(order).padStart(4, '0')}.png`;
    await fs.writeFile(
      path.join(this.session.projectPath, 'shots', filename),
      png,
    );

    const mx = monitor.x();
    const my = monitor.y();
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
        ? { global: point, image: { x: point.x - mx, y: point.y - my }, button }
        : null,
      monitor: {
        id: monitor.id(),
        bounds: { x: mx, y: my, width: monitor.width(), height: monitor.height() },
        scaleFactor: monitor.scaleFactor(),
      },
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
  // Make sure global shortcuts are released on quit.
  app.on('will-quit', () => globalShortcut.unregisterAll());
  return new CaptureController(broadcast);
}
