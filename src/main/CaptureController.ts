// CaptureController — the Phase 1 capture engine.
//
// On each trigger (a system-wide mouse click via uiohook-napi, or a global
// hotkey), it synchronously gathers: the active window (get-windows), a
// screenshot (node-screenshots), and the click coordinates. It skips shotAI's
// own windows, writes the PNG into the project's shots/ folder, and appends a
// step to project.json. Captures are serialized so rapid clicks can't race.
//
// What each step captures is chosen before recording via the session's
// CaptureTarget.mode:
//   - 'auto'   → smart per-click: app window / OS-shell region / desktop
//                fullscreen, classified by captureModeFor() (the default).
//   - 'window' → a single picked window, re-resolved each step (handles moves).
//   - 'area'   → a fixed user-dragged rectangle (global physical px).
//   - 'screen' → a single picked monitor.
//   - 'all'    → every screen (best-effort: primary monitor for now; true
//                multi-monitor stitching is a follow-up).
//
// Click-marker DPI calibration is follow-up work (Phase 2 markers).
import { app, BrowserWindow, globalShortcut, screen } from 'electron';
import { randomUUID } from 'node:crypto';
import path from 'node:path';
import { promises as fs } from 'node:fs';
import type { UiohookMouseEvent } from 'uiohook-napi';
import * as projectStore from './ProjectStore';
import type {
  CapturedWindow,
  CaptureTarget,
  MonitorInfo,
  Point,
  ProjectStep,
  Rect,
  StepClick,
  WindowInfo,
} from '../shared/project';
import type { CaptureState } from '../shared/ipc';
import { IpcChannels } from '../shared/ipc';
import { captureLog } from './logger';

const DEFAULT_HOTKEY = 'CommandOrControl+Shift+S';
const OWN_WINDOW_TITLES = new Set(['shotAI', 'shotAI — Capture']);
const DEFAULT_TARGET: CaptureTarget = { mode: 'auto' };

// A right-click is captured immediately as its own step (the target the user
// right-clicked). It also opens a context menu, so we treat the NEXT click as a
// menu selection: the menu is a separate top-level popup window (#32768) that
// per-window (PrintWindow) capture can't see, so we grab the monitor (BitBlt
// includes the popup) and crop to the menu's owner window/area. This is how long
// (ms) after a right-click the next click is assumed to be that selection. It's
// generous because the menu stays open until the user clicks, and they may
// read/hover submenus first; a stray non-menu click only crops to ~the owner
// window anyway (cheap). The arm is consumed by the next click but RE-ARMED
// (for SUBMENU_FOLLOWUP_WINDOW_MS) after each menu selection so a flyout chain
// (e.g. View → Extra large icons) captures every level — see onMouseDown.
const MENU_FOLLOWUP_WINDOW_MS = 30000;
// After a menu selection, re-arm for this much shorter window: submenu
// navigation is quick, and a short window limits how long an ordinary next
// click could be mis-read as a menu selection once the menu has closed.
const SUBMENU_FOLLOWUP_WINDOW_MS = 6000;
// A context menu (and its submenus) opens at/around the click point. A click
// within this box (logical px, scaled by the monitor's factor) of the previous
// menu point is treated as continued menu navigation; a click outside means the
// menu was dismissed and the user moved on, so we disarm. Generous to the right
// and down (where menus/submenus open) but bounded so distant clicks disarm.
const MENU_PROXIMITY_X = 640;
const MENU_PROXIMITY_Y = 680;
// While a menu is armed, we grab the monitor on mouse MOVE (throttled) so we
// have a frame of the menu taken while it is STABLY open — capturing only at
// the selection click races the menu's dismissal on mouse-up (on a slow/remote
// display the click grab can land after the popup has cleared). The selection
// step uses the most recent hover grab; this is how often (ms) we take one.
const HOVER_GRAB_THROTTLE_MS = 200;

type Natives = {
  uIOhook: typeof import('uiohook-napi').uIOhook;
  Monitor: typeof import('node-screenshots').Monitor;
  Window: typeof import('node-screenshots').Window;
  activeWindow: typeof import('get-windows').activeWindow;
};

type NsMonitor = InstanceType<Natives['Monitor']>;
type NsWindow = InstanceType<Natives['Window']>;
type NsImage = ReturnType<NsMonitor['captureImageSync']>;

/** A captured image plus where its top-left sits in global physical pixels. */
type Grab = {
  png: Buffer;
  originX: number;
  originY: number;
  monitor: NsMonitor | null;
};

type Session = {
  projectPath: string;
  projectTitle: string;
  paused: boolean;
  stepCount: number;
  target: CaptureTarget;
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
/** Smallest rectangle containing both inputs. */
function unionRect(a: Rect, b: Rect): Rect {
  const x = Math.min(a.x, b.x);
  const y = Math.min(a.y, b.y);
  const right = Math.max(a.x + a.width, b.x + b.width);
  const bottom = Math.max(a.y + a.height, b.y + b.height);
  return { x, y, width: right - x, height: bottom - y };
}

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
  private readonly onRecordingChange?: (recording: boolean) => void;
  private natives: Natives | null = null;
  private session: Session | null = null;
  private hookAttached = false;
  private hotkeyRegistered = false;
  private queue: Promise<unknown> = Promise.resolve();
  // Armed by a right-click: the next click is treated as a context-menu
  // selection (see MENU_FOLLOWUP_WINDOW_MS). `ownerBounds` is the right-clicked
  // window's bounds (filled in by captureStep) so the selection shot can frame
  // the menu together with its window. `lastPoint` is the right-click (then each
  // menu selection) point — the proximity gate that decides whether the next
  // click is still menu navigation. `hoverGrab` is the most recent monitor frame
  // taken while the cursor hovers the open menu (see HOVER_GRAB_THROTTLE_MS); the
  // selection step prefers it over grabbing at click-time, which races the
  // dismissal. `lastHoverAt` throttles those grabs. null = unarmed.
  private menuFollowUp: {
    until: number;
    ownerBounds: Rect | null;
    lastPoint: Point;
    hoverGrab: { image: NsImage; monitor: NsMonitor } | null;
    lastHoverAt: number;
  } | null = null;

  private readonly onMouseDown = (event: UiohookMouseEvent): void => {
    if (!this.session || this.session.paused) return;
    const point = { x: event.x, y: event.y };
    const button = mapButton(event.button);

    if (button === 'right') {
      // Record the right-click itself now (the target), and arm a window: the
      // next click is almost certainly the menu selection. The menu opens AT the
      // cursor, so subsequent selections cluster near this point — seed it as
      // lastPoint for the proximity gate. ownerBounds is filled by captureStep.
      this.menuFollowUp = {
        until: Date.now() + MENU_FOLLOWUP_WINDOW_MS,
        ownerBounds: null,
        lastPoint: point,
        hoverGrab: null,
        lastHoverAt: 0,
      };
      captureLog.debug(`menu: armed by right-click at (${point.x},${point.y})`);
      this.enqueue(() => this.captureStep('click', point, button));
      return;
    }

    // A left-click within the armed window AND near the last menu point = a
    // context-menu (or submenu) selection. Use the most recent hover grab — a
    // frame taken while the menu was stably open (see onMouseMove). Capturing
    // only here races the menu's dismissal on mouse-up; on a slow/remote display
    // the grab lands after the popup clears. Fall back to a grab now only if the
    // user clicked without moving (no hover grab was taken).
    const fu = this.menuFollowUp;
    const isMenuSelect =
      button === 'left' &&
      !!fu &&
      Date.now() < fu.until &&
      this.nearMenuPoint(point, fu.lastPoint);

    if (isMenuSelect) {
      const ownerBounds = fu?.ownerBounds ?? null;
      const usedHover = !!fu?.hoverGrab;
      const preGrab = fu?.hoverGrab ?? this.grabClickMonitorSync(point);
      captureLog.debug(
        `menu: selection at (${point.x},${point.y}) — using ${usedHover ? 'hover grab' : preGrab ? 'click-time grab (no hover)' : 'NO grab'}`,
      );
      // Re-arm (shorter window) so the next click in a flyout/submenu chain is
      // also captured as a menu selection; the proximity gate disarms it once
      // the user clicks away from the menu. Reset the hover grab so the next
      // selection captures the (possibly changed) submenu, not this frame.
      this.menuFollowUp = {
        until: Date.now() + SUBMENU_FOLLOWUP_WINDOW_MS,
        ownerBounds,
        lastPoint: point,
        hoverGrab: null,
        lastHoverAt: 0,
      };
      this.enqueue(() =>
        this.captureStep('click', point, button, {
          menuPopup: true,
          menuOwnerBounds: ownerBounds,
          preGrab,
        }),
      );
      return;
    }

    if (fu) {
      // There was an arm but this click wasn't treated as a menu selection —
      // log why, so the failing cases are diagnosable from the log file.
      const reason =
        button !== 'left'
          ? `button=${button}`
          : Date.now() >= fu.until
            ? 'window expired'
            : `too far from (${fu.lastPoint.x},${fu.lastPoint.y})`;
      captureLog.debug(`menu: disarmed — click at (${point.x},${point.y}) not a selection (${reason})`);
    }
    this.menuFollowUp = null; // a non-menu click disarms the follow-up
    this.enqueue(() => this.captureStep('click', point, button));
  };

  // While a menu is armed, keep a fresh monitor frame taken WHILE the cursor
  // hovers the open menu. The selection click then uses this frame instead of
  // grabbing at click-time (which races the menu's dismissal). Throttled, and
  // gated to the menu's vicinity so wandering the cursor elsewhere doesn't churn
  // captures or overwrite a good frame with one that no longer shows the menu.
  private readonly onMouseMove = (event: UiohookMouseEvent): void => {
    if (!this.session || this.session.paused) return;
    const fu = this.menuFollowUp;
    if (!fu) return;
    const now = Date.now();
    if (now >= fu.until || now - fu.lastHoverAt < HOVER_GRAB_THROTTLE_MS) return;
    const point = { x: event.x, y: event.y };
    if (!this.nearMenuPoint(point, fu.lastPoint)) return;
    fu.lastHoverAt = now;
    const grab = this.grabClickMonitorSync(point);
    if (grab) fu.hoverGrab = grab;
  };

  /** True when a click is close enough to the previous menu point to still be
   *  menu navigation (vs. the user having dismissed the menu and moved on). */
  private nearMenuPoint(point: Point, last: Point): boolean {
    let sf = 1;
    try {
      sf = this.natives?.Monitor.fromPoint(point.x, point.y)?.scaleFactor() ?? 1;
    } catch {
      /* fall back to 1× */
    }
    return (
      Math.abs(point.x - last.x) <= MENU_PROXIMITY_X * sf &&
      Math.abs(point.y - last.y) <= MENU_PROXIMITY_Y * sf
    );
  }

  /** Synchronously capture the monitor under a click. Used for menu selections,
   *  where any async delay lets the popup dismiss before we can grab it. */
  private grabClickMonitorSync(
    point: Point,
  ): { image: NsImage; monitor: NsMonitor } | null {
    if (!this.natives) return null;
    const { Monitor } = this.natives;
    try {
      const mon =
        Monitor.fromPoint(point.x, point.y) ??
        Monitor.all().find((m) => m.isPrimary()) ??
        Monitor.all()[0] ??
        null;
      if (!mon) return null;
      return { image: mon.captureImageSync(), monitor: mon };
    } catch (e) {
      captureLog.warn('synchronous menu grab failed:', e);
      return null;
    }
  }

  private readonly onHotkey = (): void => {
    if (!this.session || this.session.paused) return;
    this.enqueue(() => this.captureStep('hotkey', null));
  };

  constructor(
    broadcast: Broadcast,
    onRecordingChange?: (recording: boolean) => void,
  ) {
    this.broadcast = broadcast;
    this.onRecordingChange = onRecordingChange;
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
    opts: { attachHook?: boolean; target?: CaptureTarget } = {},
  ): Promise<CaptureState> {
    const attachHook = opts.attachHook ?? true;
    const target = opts.target ?? DEFAULT_TARGET;

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
      target,
    };

    captureLog.info(
      `recording started: "${manifest.title}" [mode=${target.mode}] (${manifest.steps.length} existing steps, next #${stepCount + 1}) at ${projectPath}`,
    );
    if (attachHook) this.attachTriggers();
    this.onRecordingChange?.(true); // e.g. hide the main window while recording
    this.emitState();
    return this.getState();
  }

  private attachTriggers(): void {
    const { uIOhook } = this.natives!;
    if (!this.hookAttached) {
      uIOhook.on('mousedown', this.onMouseDown);
      uIOhook.on('mousemove', this.onMouseMove);
      uIOhook.start();
      this.hookAttached = true;
    }
    if (!this.hotkeyRegistered) {
      this.hotkeyRegistered = globalShortcut.register(DEFAULT_HOTKEY, this.onHotkey);
    }
  }

  private detachTriggers(): void {
    this.menuFollowUp = null; // don't carry an armed menu window across sessions
    if (this.hookAttached && this.natives) {
      this.natives.uIOhook.off('mousedown', this.onMouseDown);
      this.natives.uIOhook.off('mousemove', this.onMouseMove);
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
    this.menuFollowUp = null;
    captureLog.info('recording paused');
    this.emitState();
    return this.getState();
  }

  resume(): CaptureState {
    if (this.session) this.session.paused = false;
    this.menuFollowUp = null;
    captureLog.info('recording resumed');
    this.emitState();
    return this.getState();
  }

  async stop(): Promise<CaptureState> {
    const wasRecording = this.session !== null;
    const count = this.session?.stepCount ?? 0;
    this.detachTriggers();
    await this.queue.catch(() => undefined); // let in-flight captures finish
    this.session = null;
    captureLog.info(`recording stopped (${count} steps total)`);
    if (wasRecording) this.onRecordingChange?.(false); // e.g. restore the main window
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

  /** OS pids belonging to shotAI (main/browser process + each window's renderer). */
  private ownPids(): Set<number> {
    const pids = new Set<number>([process.pid]);
    for (const w of BrowserWindow.getAllWindows()) {
      try {
        pids.add(w.webContents.getOSProcessId());
      } catch {
        /* window gone */
      }
    }
    return pids;
  }

  /** True if a (physical-pixel) click point lands on a visible shotAI window. */
  private pointHitsOwnWindow(point: Point): boolean {
    let dip: Point = point;
    try {
      // uiohook reports physical screen pixels; window bounds are DIP.
      dip = screen.screenToDipPoint({ x: point.x, y: point.y });
    } catch {
      /* fall back to the raw point */
    }
    for (const w of BrowserWindow.getAllWindows()) {
      if (w.isDestroyed() || !w.isVisible()) continue;
      const b = w.getBounds();
      if (
        dip.x >= b.x &&
        dip.x < b.x + b.width &&
        dip.y >= b.y &&
        dip.y < b.y + b.height
      ) {
        return true;
      }
    }
    return false;
  }

  /**
   * Re-resolve a picked window against the current window list — by id first,
   * then pid+title, then pid alone (window ids can change across sessions, and
   * the window may have moved/changed title since it was picked).
   */
  private resolveWindow(
    Window: Natives['Window'],
    target: CaptureTarget['window'],
  ): NsWindow | null {
    if (!target) return null;
    const all = Window.all();
    return (
      all.find((w) => w.id() === target.id) ??
      all.find((w) => w.pid() === target.pid && w.title() === target.title) ??
      all.find((w) => w.pid() === target.pid) ??
      null
    );
  }

  /**
   * Enumerate pickable windows + monitors for the Window/Screen choosers.
   * Skips shotAI's own windows, minimized windows, and untitled utility windows.
   */
  async listTargets(): Promise<{ windows: WindowInfo[]; monitors: MonitorInfo[] }> {
    const { Monitor, Window } = await this.loadNatives();
    const own = this.ownPids();
    const seen = new Set<string>();
    const windows: WindowInfo[] = [];
    for (const w of Window.all()) {
      if (own.has(w.pid())) continue;
      if (w.isMinimized()) continue;
      const title = (w.title() ?? '').trim();
      if (!title) continue;
      const key = `${w.pid()}::${title}`;
      if (seen.has(key)) continue; // collapse duplicate windows of the same app/title
      seen.add(key);
      windows.push({ id: w.id(), pid: w.pid(), title, app: w.appName() ?? '' });
    }
    const monitors: MonitorInfo[] = Monitor.all().map((m) => ({
      id: m.id(),
      name: m.name() || `Display ${m.id()}`,
      width: m.width(),
      height: m.height(),
      isPrimary: m.isPrimary(),
    }));
    captureLog.debug(`listTargets: ${windows.length} windows, ${monitors.length} monitors`);
    return { windows, monitors };
  }

  /** Perform one capture into the active project. Public so the test can drive it. */
  async captureStep(
    trigger: ProjectStep['trigger'],
    point: Point | null,
    button: StepClick['button'] = 'left',
    opts: {
      menuPopup?: boolean;
      menuOwnerBounds?: Rect | null;
      preGrab?: { image: NsImage; monitor: NsMonitor } | null;
    } = {},
  ): Promise<ProjectStep | null> {
    // Re-check here (not just at enqueue) so a pause/stop that lands while
    // tasks are queued actually suppresses the backlog.
    if (!this.session || this.session.paused) return null;
    const { Monitor, Window, activeWindow } = await this.loadNatives();

    const active = await activeWindow();
    const focused = Window.all().find((w) => w.isFocused());

    // Never record clicks on shotAI's own windows. Check BOTH signals: our
    // content-protected windows make get-windows return null, but
    // node-screenshots still reports the focused window's owning pid.
    const ours = this.ownPids();
    const activeIsOwn =
      !!active &&
      (ours.has(active.owner.processId) || OWN_WINDOW_TITLES.has(active.title));
    const focusedIsOwn = !!focused && ours.has(focused.pid());
    // Geometric check catches clicks on the always-on-top pill, which doesn't
    // reliably report as the active/focused window (and is content-protected).
    if (activeIsOwn || focusedIsOwn || (point && this.pointHitsOwnWindow(point))) {
      return null;
    }

    // For a right-click, remember the owner window's bounds (physical px) so the
    // following menu-selection capture can frame the menu together with it. The
    // focused window here is the owner (the menu isn't open yet at right-down).
    if (button === 'right' && this.menuFollowUp && this.menuFollowUp.ownerBounds === null) {
      this.menuFollowUp.ownerBounds = focused
        ? {
            x: focused.x(),
            y: focused.y(),
            width: focused.width(),
            height: focused.height(),
          }
        : null;
    }

    const target = this.session.target;
    const mode = target.mode;

    // Monitor under the click — the default capture surface + fallback.
    const clickMonitor: NsMonitor | null =
      (point ? Monitor.fromPoint(point.x, point.y) : null) ??
      Monitor.all().find((m) => m.isPrimary()) ??
      Monitor.all()[0] ??
      null;

    // In 'auto' mode, classify the click target (app window / OS-shell region /
    // desktop fullscreen). The explicit modes ignore this.
    const autoMode = mode === 'auto' ? captureModeFor(active) : null;

    const grab = (): Grab | null => {
      // CONTEXT-MENU SELECTION — the menu is a separate top-level popup window
      // that per-window (PrintWindow) capture can't see, so grab the monitor
      // (BitBlt of the composited desktop includes the popup) and crop to frame
      // the menu WITH what it belongs to. 'screen'/'all' keep the whole monitor
      // (the user picked a monitor); 'auto'/'window'/'area' crop to the owner
      // window / picked window / chosen area, unioned with a box around the click
      // so a menu overflowing that region is still included.
      if (opts.menuPopup) {
        const pickedWin =
          mode === 'window' ? this.resolveWindow(Window, target.window) : null;
        const winRect: Rect | null =
          pickedWin && pickedWin.x() > -10000 && pickedWin.y() > -10000
            ? {
                x: pickedWin.x(),
                y: pickedWin.y(),
                width: pickedWin.width(),
                height: pickedWin.height(),
              }
            : null;

        // Prefer the pixels grabbed synchronously at mousedown (the menu is
        // still painted then). The pre-grab is always the monitor under the
        // click — which is where the menu is — so it's used as-is. Without it
        // (e.g. the headless test), fall back to grabbing now, best-effort.
        let mon: NsMonitor | null;
        let full: NsImage;
        if (opts.preGrab) {
          mon = opts.preGrab.monitor;
          full = opts.preGrab.image;
        } else {
          mon = clickMonitor;
          if (mode === 'screen' && target.monitorId != null) {
            mon = Monitor.all().find((m) => m.id() === target.monitorId) ?? clickMonitor;
          } else if (winRect) {
            mon = Monitor.fromPoint(winRect.x, winRect.y) ?? clickMonitor;
          }
          if (!mon) return null;
          try {
            full = mon.captureImageSync();
          } catch (e) {
            captureLog.warn('menu-popup capture failed:', e);
            return null;
          }
        }

        let region: Rect | null = null;
        if (mode !== 'screen' && mode !== 'all') {
          let base: Rect | null =
            mode === 'window'
              ? winRect
              : mode === 'area'
                ? target.area ?? null
                : opts.menuOwnerBounds ?? null; // 'auto' → owner at right-click time
          if (point) {
            const sf = mon.scaleFactor() || 1;
            const box: Rect = {
              x: point.x - Math.round(48 * sf),
              y: point.y - Math.round(56 * sf),
              width: Math.round(620 * sf),
              height: Math.round(760 * sf),
            };
            base = base ? unionRect(base, box) : box;
          }
          region = base;
        }

        try {
          if (!region) {
            return {
              png: full.toPngSync(),
              originX: mon.x(),
              originY: mon.y(),
              monitor: mon,
            };
          }
          const lx = Math.round(region.x - mon.x());
          const ly = Math.round(region.y - mon.y());
          const cropX = Math.max(0, Math.min(lx, mon.width() - 1));
          const cropY = Math.max(0, Math.min(ly, mon.height() - 1));
          const cropW = Math.max(1, Math.min(lx + Math.round(region.width), mon.width()) - cropX);
          const cropH = Math.max(1, Math.min(ly + Math.round(region.height), mon.height()) - cropY);
          return {
            png: full.cropSync(cropX, cropY, cropW, cropH).toPngSync(),
            originX: mon.x() + cropX,
            originY: mon.y() + cropY,
            monitor: mon,
          };
        } catch (e) {
          captureLog.warn('menu-popup crop failed:', e);
          return null;
        }
      }

      // WINDOW — an explicitly picked window, or 'auto' classified the click as
      // a normal app window (the focused window, already confirmed not ours).
      if (mode === 'window' || autoMode === 'window') {
        const win =
          mode === 'window' ? this.resolveWindow(Window, target.window) : focused;
        if (win && win.x() > -10000 && win.y() > -10000) {
          try {
            return {
              png: win.captureImageSync().toPngSync(),
              originX: win.x(),
              originY: win.y(),
              monitor: Monitor.fromPoint(win.x(), win.y()) ?? clickMonitor,
            };
          } catch (e) {
            captureLog.warn('window capture failed, falling back to monitor:', e);
          }
        } else if (mode === 'window') {
          captureLog.warn('picked window not found — falling back to monitor capture');
        }
        // fall through to a monitor capture
      }

      // AREA — a fixed user-dragged rectangle (global physical px).
      if (mode === 'area' && target.area) {
        const a = target.area;
        const mon = Monitor.fromPoint(a.x, a.y) ?? clickMonitor;
        if (mon) {
          try {
            const cropX = Math.max(0, Math.min(Math.round(a.x - mon.x()), mon.width() - 1));
            const cropY = Math.max(0, Math.min(Math.round(a.y - mon.y()), mon.height() - 1));
            const cropW = Math.max(1, Math.min(Math.round(a.width), mon.width() - cropX));
            const cropH = Math.max(1, Math.min(Math.round(a.height), mon.height() - cropY));
            return {
              png: mon.captureImageSync().cropSync(cropX, cropY, cropW, cropH).toPngSync(),
              originX: mon.x() + cropX,
              originY: mon.y() + cropY,
              monitor: mon,
            };
          } catch (e) {
            captureLog.warn('area capture failed, falling back to monitor:', e);
          }
        }
      }

      // Resolve which monitor to capture for the remaining cases.
      let mon = clickMonitor;
      if (mode === 'screen' && target.monitorId != null) {
        mon = Monitor.all().find((m) => m.id() === target.monitorId) ?? clickMonitor;
      } else if (mode === 'all') {
        const monitors = Monitor.all();
        if (monitors.length > 1) {
          captureLog.warn(
            `'all screens': ${monitors.length} monitors — capturing primary only (multi-monitor stitching is a follow-up)`,
          );
        }
        mon = monitors.find((m) => m.isPrimary()) ?? monitors[0] ?? clickMonitor;
      }
      if (!mon) return null;

      // REGION — 'auto' shell element (taskbar/Start/tray): a tight crop around
      // the click, avoiding the giant black shell-window capture.
      if (autoMode === 'region' && point) {
        try {
          const sf = mon.scaleFactor() || 1;
          const boxW = Math.min(Math.round(520 * sf), mon.width());
          const boxH = Math.min(Math.round(400 * sf), mon.height());
          const cx = point.x - mon.x();
          const cy = point.y - mon.y();
          const cropX = Math.max(0, Math.min(cx - Math.floor(boxW / 2), mon.width() - boxW));
          const cropY = Math.max(0, Math.min(cy - Math.floor(boxH / 2), mon.height() - boxH));
          return {
            png: mon.captureImageSync().cropSync(cropX, cropY, boxW, boxH).toPngSync(),
            originX: mon.x() + cropX,
            originY: mon.y() + cropY,
            monitor: mon,
          };
        } catch (e) {
          captureLog.warn('region capture failed, falling back to full monitor:', e);
        }
      }

      // FULLSCREEN — the whole monitor ('auto' desktop/fallback, 'screen', 'all').
      try {
        return {
          png: mon.captureImageSync().toPngSync(),
          originX: mon.x(),
          originY: mon.y(),
          monitor: mon,
        };
      } catch (e) {
        captureLog.warn('monitor capture failed:', e);
        return null;
      }
    };
    const grabbed = grab();
    if (!grabbed) return null;
    const { png, originX, originY, monitor } = grabbed;

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
          ? opts.menuPopup
            ? `Select from context menu in ${window?.app ?? 'screen'}`
            : button === 'right'
              ? `Right-click in ${window?.app ?? 'screen'}`
              : `Click in ${window?.app ?? 'screen'}`
          : `Capture: ${window?.title ?? 'screen'}`,
      note: '',
      crop: null,
      annotations: [],
    };

    await projectStore.addStep(this.session.projectPath, step);
    captureLog.info(
      `step #${order} [${trigger}/${autoMode ? `auto:${autoMode}` : mode}${opts.menuPopup ? ' menu-select' : button === 'right' ? ' right' : ''}] ${window?.app ?? 'screen'} -> ${filename} (${Math.round(png.length / 1024)} KB)`,
    );
    this.broadcast(IpcChannels.captureStepAdded, step);
    this.emitState();
    return step;
  }
}

/** Build a CaptureController that broadcasts events to all renderer windows. */
export function createCaptureController(
  opts: { onRecordingChange?: (recording: boolean) => void } = {},
): CaptureController {
  const broadcast: Broadcast = (channel, payload) => {
    for (const win of BrowserWindow.getAllWindows()) {
      if (!win.isDestroyed()) win.webContents.send(channel, payload);
    }
  };
  const controller = new CaptureController(broadcast, opts.onRecordingChange);
  // Release the global input hook + shortcuts on quit so the uiohook worker
  // thread can't keep the process alive (zombie) on Windows.
  app.on('before-quit', () => {
    controller.teardown();
    globalShortcut.unregisterAll();
  });
  return controller;
}
