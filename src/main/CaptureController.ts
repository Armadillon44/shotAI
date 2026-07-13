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
//   - 'area'   → a fixed user-dragged rectangle on one monitor (global physical px).
//   - 'screen' → a single picked monitor.
//
// Click-marker DPI calibration is follow-up work (Phase 2 markers).
import { app, BrowserWindow, globalShortcut, nativeImage, screen } from 'electron';
import { randomUUID } from 'node:crypto';
import path from 'node:path';
import { promises as fs } from 'node:fs';
import type { UiohookMouseEvent } from 'uiohook-napi';
import * as projectStore from './project-store';
import type {
  CapturedWindow,
  CaptureTarget,
  MonitorInfo,
  Point,
  ProjectManifest,
  ProjectStep,
  Rect,
  StepClick,
  StepElement,
  WindowInfo,
} from '../shared/project';
import type { CaptureState } from '../shared/ipc';
import { IpcChannels } from '../shared/ipc';
import { unionRect, clickBox, captureModeFor, cropRect } from './capture-geometry';
import { buildClickCaption } from './click-caption';
import { captureScaleNow } from './settings';
import { captureLog } from './logger';
import { getElementAtPoint, warmUpElementLocator } from './element-locator';

const DEFAULT_HOTKEY = 'CommandOrControl+Shift+S';
const OWN_WINDOW_TITLES = new Set(['shotAI', 'shotAI — Capture']);
const DEFAULT_TARGET: CaptureTarget = { mode: 'auto' };

// After hiding the app window for a no-click one-shot grab (+Screenshot), wait
// this long before capturing so the compositor has presented a frame WITHOUT the
// window. Hiding is LOAD-BEARING here (node-screenshots BitBlt grabs a visible
// shotAI window — see main.ts onRecordingChange), and there is no main-process
// repaint signal, so this is a fixed settle comfortably above one present
// interval (a 60Hz frame ~16ms; generous for slow/RDP displays) yet unobtrusive.
// The own-window guard in captureStep is the hard backstop against a leak — this
// timer just avoids a captured-nothing no-op while focus/composition settle.
const HIDE_SETTLE_MS = 350;

// The main-window hide/restore hook. `pill` (default true) controls whether the
// always-on-top recording pill is shown too — a full recording shows it; the
// no-click one-shot suppresses it (no recording HUD, and the pill must not become
// the focused own-window that trips captureStep's guard). `forceHide` hides the
// app window even in demo mode ("keep shotAI visible") — a deliberate screenshot
// must never include shotAI, whereas demo mode only concerns live recording.
type RecordingChange = (
  recording: boolean,
  opts?: { pill?: boolean; forceHide?: boolean },
) => void;

// Modest downscale applied to every captured screenshot (T2) to cut PNG file
// size AND Claude vision token cost. Kept gentle so small UI text Claude must
// read stays legible. `image` click coords are scaled to match; a `<1` factor is
// persisted on the click so merge.ts can still recover the capture origin.
// Readability floor: never shrink the longer edge below this, so UI text stays
// legible to Claude even at a low quality setting (small captures barely shrink).
const MIN_CAPTURE_LONG_EDGE = 1100;

/**
 * Downscale a captured PNG toward the user's screenshot-quality setting
 * (captureScaleNow, D1), but never below the readability floor
 * (MIN_CAPTURE_LONG_EDGE on the longer edge) and never upscaling. Returns the
 * resized bytes + the ACTUAL scale applied (after integer rounding) so click
 * coords match. Fails open to the original PNG (scale 1) on any error — a resize
 * hiccup must never lose a capture.
 */
function downscalePng(png: Buffer): { png: Buffer; scale: number } {
  try {
    const img = nativeImage.createFromBuffer(png);
    const { width, height } = img.getSize();
    if (width < 2 || height < 2) return { png, scale: 1 };
    // Don't let the target drop below the readability floor for this image; and
    // never upscale (floorScale caps at 1 for already-small captures).
    const floorScale = Math.min(1, MIN_CAPTURE_LONG_EDGE / Math.max(width, height));
    const target = Math.max(captureScaleNow(), floorScale);
    if (target >= 1) return { png, scale: 1 };
    const targetW = Math.max(1, Math.round(width * target));
    if (targetW >= width) return { png, scale: 1 };
    // 'good' (not 'best'): 'best' is a slow Lanczos resample that blocked the main
    // thread ~250ms/click, tripping the mouse-hook timeout and dropping clicks.
    const resized = img.resize({ width: targetW, quality: 'good' });
    const out = resized.toPNG();
    if (!out || out.length === 0) return { png, scale: 1 };
    return { png: out, scale: resized.getSize().width / width };
  } catch {
    return { png, scale: 1 };
  }
}

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
// While a menu is armed we POLL the monitor on a timer, keeping the most recent
// frame. The selection step uses that frame rather than grabbing at click-time,
// which races the menu's dismissal on mouse-up (on a slow/remote display the
// click grab lands after the popup clears). Polling on a TIMER — not on mouse
// movement — is the key: the menu is captured even when the user clicks an item
// right under the cursor without moving (an earlier mousemove-driven version
// missed exactly that case). The menu stays open the whole time it's armed, so
// any recent frame has it. Interval is kept ABOVE the per-capture latency
// (~185ms emulated) so captures don't run back-to-back and saturate a core;
// captures run off the main thread (async), so this is worker CPU, not UI jank.
const MENU_POLL_MS = 400;
// Cap the number of polled frames per armed menu so an abandoned/Esc-dismissed
// menu (which never fires a disarming click) can't poll for the whole 30s
// window. 32 frames at 400ms ≈ 13s of coverage — comfortably longer than a real
// menu interaction (the slowest observed was ~10s) — then we stop and reuse the
// last frame (the menu is static while open; the click-time grab is a backstop).
const MAX_POLL_FRAMES = 32;
// Cap how many times one right-click can re-arm for a submenu chain. A real
// flyout (e.g. View → Sort by → Name) is only a few levels deep, so this both
// supports flyouts AND bounds the old runaway where, after a selection, every
// further click within the proximity/window got swallowed as a "Select…" step.
// Once the cap is hit we disarm; a deeper chain just captures the leaf normally.
const MAX_MENU_CHAIN = 4;
// A double-click fires two mousedowns; treat the second as part of the same
// action (one step) when it lands within this time + distance of the first.
const DOUBLE_CLICK_MS = 400;
const DOUBLE_CLICK_DIST = 6; // logical px, scaled by the monitor factor

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
  // Manifest step count when the session began (BEFORE orphan-seeding bumps
  // stepCount). With createdThisSession + addedStepIds this lets a Discard delete
  // the whole project (new + was empty) or just this session's steps.
  stepCountAtStart: number;
  createdThisSession: boolean;
  addedStepIds: string[];
  // Single-shot mode: capture exactly one click, insert it at this manifest
  // index (not append), then auto-stop. Used by the report's "insert one
  // screenshot here" affordance. Absent for a normal recording session.
  // `fired` guards against a fast second click being captured before stop.
  single?: { insertAt: number; fired?: boolean };
  // Multi-step insert (+Capture at a report gap): a full recording whose every
  // captured step SPLICES at this rolling manifest index (then ++) instead of
  // appending. undefined = normal append recording. Mutually exclusive with
  // `single`. Read + advanced synchronously inside the serialized captureStep,
  // so rapid clicks still land in order i, i+1, i+2…
  insertCursor?: number;
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

/** Crop a full-monitor capture to a global-px region, clamped to the monitor.
 *  The rect math is the pure cropRect() (capture-geometry.ts); this does the
 *  native crop + records the crop's global origin. ASYNC (crop + PNG encode run
 *  on libuv worker threads) so the main thread / global mouse hook isn't blocked
 *  during the encode — a slow sync encode dropped rapid follow-up clicks. */
async function cropToRegion(mon: NsMonitor, full: NsImage, region: Rect): Promise<Grab> {
  const c = cropRect({ x: mon.x(), y: mon.y(), width: mon.width(), height: mon.height() }, region);
  const cropped = await full.crop(c.x, c.y, c.width, c.height);
  return {
    png: await cropped.toPng(),
    originX: mon.x() + c.x,
    originY: mon.y() + c.y,
    monitor: mon,
  };
}

export class CaptureController {
  private readonly broadcast: Broadcast;
  private readonly onRecordingChange?: RecordingChange;
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
  // click is still menu navigation. `menuFrame` is the most recent monitor frame
  // captured by the poll timer while the menu is open (see startMenuPolling); the
  // selection step uses it instead of grabbing at click-time. null = unarmed.
  private menuFollowUp: {
    until: number;
    ownerBounds: Rect | null;
    lastPoint: Point;
    menuFrame: { image: NsImage; monitor: NsMonitor } | null;
    // How many selections deep this menu chain is (0 = the initial right-click
    // arm). Bounded by MAX_MENU_CHAIN to stop the runaway re-arm.
    chain: number;
  } | null = null;
  // Interval that refreshes menuFollowUp.menuFrame while a menu is armed, plus an
  // in-flight guard so slow async captures can't pile up.
  private menuPollTimer: ReturnType<typeof setInterval> | null = null;
  private menuPolling = false;
  // Last left mousedown — used to collapse a double-click's two events into one
  // step (see DOUBLE_CLICK_MS / DOUBLE_CLICK_DIST).
  private lastLeftClick: { at: number; point: Point } | null = null;

  private readonly onMouseDown = (event: UiohookMouseEvent): void => {
    if (!this.session || this.session.paused) return;
    const point = { x: event.x, y: event.y };
    const button = mapButton(event.button);

    // Resolve the UI element under the cursor NOW (at mousedown), before the
    // click's effect changes the UI — closing a dialog, minimizing a window, or
    // dismissing the menu would otherwise make a later query hit whatever is
    // underneath. Best-effort + off-thread; threaded into captureStep below.
    const elementPromise = getElementAtPoint(point.x, point.y);

    // Single-shot insert: capture the first real click as ONE step inserted at
    // the chosen index, then end the session. No double-click/menu machinery —
    // this is an explicit one-screenshot grab, not a recording.
    if (this.session.single) {
      if (this.session.single.fired) return; // the one shot is already in flight
      if (this.pointHitsOwnWindow(point)) return; // ignore clicks on shotAI's own UI
      this.session.single.fired = true;
      const insertAt = this.session.single.insertAt;
      this.enqueue(async () => {
        const step = await this.captureStep('click', point, button, { insertAt, elementPromise });
        if (!step) {
          captureLog.warn(
            `single-shot: click at (${point.x},${point.y}) captured nothing — ending the session (the window is restored; retry the insert)`,
          );
        }
        // Always end the session after the one click attempt, restoring the
        // window. (Fire-and-forget: stop() awaits the capture queue, which
        // contains THIS task — awaiting it here would deadlock.)
        void this.stop();
      });
      return;
    }

    // Collapse a double-click (two left mousedowns at ~the same spot in quick
    // succession) into a single step — capture the first, ignore the second.
    if (button === 'left') {
      const now = Date.now();
      const last = this.lastLeftClick;
      const isDouble =
        !!last &&
        now - last.at <= DOUBLE_CLICK_MS &&
        this.withinDist(point, last.point, DOUBLE_CLICK_DIST);
      this.lastLeftClick = { at: now, point };
      if (isDouble) {
        captureLog.debug(`double-click: ignoring 2nd click at (${point.x},${point.y})`);
        return;
      }
    }

    if (button === 'right') {
      // Arm the follow-up window (the next click is almost certainly the menu
      // selection; the menu opens at the cursor, so selections cluster near this
      // point — seed lastPoint for the proximity gate) and start polling for the
      // selection step. ownerBounds is captured synchronously NOW (the focused
      // window is the menu's owner; it won't be once the menu is open) so the
      // selection step can frame the menu with its window.
      this.menuFollowUp = {
        until: Date.now() + MENU_FOLLOWUP_WINDOW_MS,
        ownerBounds: this.focusedWindowBounds(),
        lastPoint: point,
        menuFrame: null,
        chain: 0,
      };
      this.startMenuPolling();
      captureLog.debug(`menu: armed by right-click at (${point.x},${point.y})`);
      // The right-click step is captured PLAINLY (its target window) — we no
      // longer try to grab the just-opened menu on the right-click itself, which
      // raced the menu's render and was unreliable. The reliable capture is the
      // NEXT click (the selection), grabbed on mouse-DOWN while the menu is still
      // on screen. The two can then be merged in the report (the right-click step
      // is discarded, its click carried onto the menu screenshot as a marker).
      this.enqueue(() => this.captureStep('click', point, button, { elementPromise }));
      return;
    }

    // A left-click within the armed window AND near the last menu point = a
    // context-menu (or submenu) selection. Use the most recent polled frame — a
    // capture taken while the menu was open (see startMenuPolling). Grabbing only
    // here races the menu's dismissal on mouse-up; on a slow/remote display the
    // grab lands after the popup clears. Fall back to a grab now only if no
    // polled frame exists yet (a selection faster than the first poll tick).
    const fu = this.menuFollowUp;
    const isMenuSelect =
      button === 'left' &&
      !!fu &&
      Date.now() < fu.until &&
      this.nearMenuPoint(point, fu.lastPoint);

    if (isMenuSelect) {
      const ownerBounds = fu?.ownerBounds ?? null;
      const usedPoll = !!fu?.menuFrame;
      const preGrab = fu?.menuFrame ?? this.grabClickMonitorSync(point);
      if (usedPoll) {
        captureLog.debug(`menu: selection at (${point.x},${point.y}) — using polled frame`);
      } else if (preGrab) {
        captureLog.debug(
          `menu: selection at (${point.x},${point.y}) — no polled frame yet, using click-time grab`,
        );
      } else {
        // No polled frame and the synchronous grab also failed — captureStep will
        // grab post-click, by which point the menu has likely dismissed.
        captureLog.warn(
          `menu: selection at (${point.x},${point.y}) — NO frame available; the menu will probably be missing from this step`,
        );
      }
      // Re-arm (shorter window) so the next click in a flyout/submenu chain is
      // also captured as a menu selection; the proximity gate disarms it once
      // the user clicks away from the menu. Reset menuFrame so the next selection
      // captures the (possibly changed) submenu, not this frame. BOUNDED by
      // MAX_MENU_CHAIN so a stream of ordinary clicks after a selection can't keep
      // re-arming (the old runaway, where one right-click spawned ~10 false steps).
      const chain = (fu?.chain ?? 0) + 1;
      if (chain < MAX_MENU_CHAIN) {
        this.menuFollowUp = {
          until: Date.now() + SUBMENU_FOLLOWUP_WINDOW_MS,
          ownerBounds,
          lastPoint: point,
          menuFrame: null,
          chain,
        };
        this.startMenuPolling();
      } else {
        captureLog.debug(`menu: chain limit (${MAX_MENU_CHAIN}) reached — disarming`);
        this.disarmMenu();
      }
      this.enqueue(() =>
        this.captureStep('click', point, button, {
          menuPopup: true,
          menuOwnerBounds: ownerBounds,
          preGrab,
          elementPromise,
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
    this.disarmMenu(); // a non-menu click disarms the follow-up + stops polling
    this.enqueue(() => this.captureStep('click', point, button, { elementPromise }));
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

  /** True if two points are within `logicalPx` (scaled by the monitor factor). */
  private withinDist(a: Point, b: Point, logicalPx: number): boolean {
    let sf = 1;
    try {
      sf = this.natives?.Monitor.fromPoint(a.x, a.y)?.scaleFactor() ?? 1;
    } catch {
      /* fall back to 1× */
    }
    const max = logicalPx * sf;
    return Math.abs(a.x - b.x) <= max && Math.abs(a.y - b.y) <= max;
  }

  /** The focused window's bounds in global physical px (the right-click owner). */
  private focusedWindowBounds(): Rect | null {
    if (!this.natives) return null;
    try {
      const w = this.natives.Window.all().find((x) => x.isFocused());
      if (w && w.x() > -10000 && w.y() > -10000) {
        return { x: w.x(), y: w.y(), width: w.width(), height: w.height() };
      }
    } catch {
      /* no resolvable focused window */
    }
    return null;
  }

  /** Stop the menu poll timer (no-op if not running). */
  private stopMenuPolling(): void {
    if (this.menuPollTimer) {
      clearInterval(this.menuPollTimer);
      this.menuPollTimer = null;
    }
    // Clear the in-flight guard unconditionally: if a poll capture is still
    // running when we stop, its (now stale-arm) resolve won't reset the flag, so
    // without this a leaked `true` would silently suppress the NEXT arm's polling.
    this.menuPolling = false;
  }

  /** Disarm the context-menu follow-up and stop polling. */
  private disarmMenu(): void {
    this.menuFollowUp = null;
    this.stopMenuPolling();
  }

  /**
   * While a menu is armed, poll the monitor under it on a timer and keep the
   * latest frame in menuFollowUp.menuFrame. Timer-driven (NOT mouse-driven) so
   * the menu is captured even when the user clicks an item without moving the
   * cursor. Uses the async capture so the main thread isn't blocked; an in-flight
   * guard prevents pile-up, and a same-arm check stops a late frame from landing
   * on a newer arm. The timer self-cancels when disarmed, re-armed, paused, or
   * the follow-up window elapses.
   */
  private startMenuPolling(): void {
    this.stopMenuPolling();
    const fu = this.menuFollowUp;
    if (!fu || !this.natives) return;
    const { Monitor } = this.natives;
    let frames = 0; // bounded by MAX_POLL_FRAMES (per-arm; closure-local)
    this.menuPollTimer = setInterval(() => {
      const cur = this.menuFollowUp;
      if (!cur || cur !== fu) {
        this.stopMenuPolling(); // disarmed or re-armed since this timer started
        return;
      }
      if (Date.now() >= cur.until || this.session?.paused) {
        this.stopMenuPolling(); // window elapsed with no selection, or paused
        return;
      }
      if (frames >= MAX_POLL_FRAMES) {
        this.stopMenuPolling(); // enough coverage — reuse the last frame
        return;
      }
      if (this.menuPolling) return; // previous async capture still in flight
      let mon: NsMonitor | null;
      try {
        mon =
          Monitor.fromPoint(cur.lastPoint.x, cur.lastPoint.y) ??
          Monitor.all().find((m) => m.isPrimary()) ??
          Monitor.all()[0] ??
          null;
      } catch {
        mon = null;
      }
      if (!mon) return;
      const m = mon;
      this.menuPolling = true;
      frames++;
      m.captureImage()
        .then((image) => {
          if (this.menuFollowUp === cur) cur.menuFrame = { image, monitor: m };
        })
        .catch((e) => captureLog.warn('menu poll capture failed:', e))
        .finally(() => {
          // Only release the guard for the still-current arm; a stale capture
          // resolving after re-arm must not clear the new arm's in-flight flag.
          if (this.menuFollowUp === cur) this.menuPolling = false;
        });
    }, MENU_POLL_MS);
  }

  private readonly onHotkey = (): void => {
    if (!this.session || this.session.paused) return;
    this.enqueue(() => this.captureStep('hotkey', null));
  };

  constructor(
    broadcast: Broadcast,
    onRecordingChange?: RecordingChange,
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
      return {
        status: 'idle',
        projectPath: null,
        projectTitle: null,
        stepCount: 0,
        willDeleteProjectOnDiscard: false,
      };
    }
    return {
      status: this.session.paused ? 'paused' : 'recording',
      projectPath: this.session.projectPath,
      projectTitle: this.session.projectTitle,
      stepCount: this.session.stepCount,
      willDeleteProjectOnDiscard: CaptureController.discardDeletesProject(this.session),
    };
  }

  /**
   * Whether discarding `s` deletes the WHOLE project (vs. just this session's
   * steps): a project created this session that had no prior steps and isn't a
   * single-shot insert. One source of truth for discard() and the pill's R5
   * warning — keep them from drifting apart.
   */
  private static discardDeletesProject(s: Session): boolean {
    return s.createdThisSession && s.stepCountAtStart === 0 && !s.single;
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
    opts: {
      attachHook?: boolean;
      target?: CaptureTarget;
      createdThisSession?: boolean;
      /** +Capture at a report gap: starting manifest index. Every captured step
       *  inserts here and the cursor advances, instead of appending. */
      insertAt?: number | null;
    } = {},
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

    // Manifest step count before orphan-seeding bumps the filename counter — used
    // by discard() to decide whole-project vs. session-only deletion.
    const stepCountAtStart = manifest.steps.length;

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

    // +Capture insert: clamp the starting index into the current manifest so the
    // first captured step splices at the gap and each subsequent one after it.
    const insertCursor =
      opts.insertAt == null
        ? undefined
        : Math.max(0, Math.min(Math.round(opts.insertAt), manifest.steps.length));

    this.session = {
      projectPath,
      projectTitle: manifest.title,
      paused: false,
      stepCount,
      target,
      stepCountAtStart,
      createdThisSession: opts.createdThisSession ?? false,
      addedStepIds: [],
      insertCursor,
    };

    captureLog.info(
      `recording started: "${manifest.title}" [mode=${target.mode}]${insertCursor != null ? ` [insert@${insertCursor}]` : ''} (${manifest.steps.length} existing steps, next #${stepCount + 1}) at ${projectPath}`,
    );
    // Pre-load the native element-locator dll now so the FIRST click isn't
    // delayed seconds by its lazy load (which stalled the first capture).
    warmUpElementLocator();
    if (attachHook) this.attachTriggers();
    this.onRecordingChange?.(true); // e.g. hide the main window while recording
    this.emitState();
    return this.getState();
  }

  /**
   * Arm a one-shot capture: the next real click captures a single step inserted
   * at `insertAt` in the manifest, then the session auto-stops. Hides the main
   * window (via onRecordingChange) so shotAI isn't in the shot, same as a normal
   * recording. Rejects if a recording is already in progress.
   */
  async captureSingle(projectPath: string, insertAt: number): Promise<CaptureState> {
    if (this.session) {
      throw new Error('A recording is already in progress');
    }
    await this.loadNatives();
    const manifest = await projectStore.openProject(projectPath);
    const shotsDir = path.join(projectPath, 'shots');
    await fs.mkdir(shotsDir, { recursive: true });

    // Seed the filename counter past any shot on disk (deletes leave orphans).
    let stepCount = manifest.steps.length;
    try {
      for (const f of await fs.readdir(shotsDir)) {
        const m = /^step-(\d+)\.png$/i.exec(f);
        if (m) stepCount = Math.max(stepCount, Number(m[1]));
      }
    } catch {
      /* shots/ unreadable — fall back to manifest length */
    }

    const at = Math.max(0, Math.min(Math.round(insertAt), manifest.steps.length));
    this.session = {
      projectPath,
      projectTitle: manifest.title,
      paused: false,
      stepCount,
      target: DEFAULT_TARGET,
      stepCountAtStart: manifest.steps.length,
      createdThisSession: false,
      addedStepIds: [],
      single: { insertAt: at },
    };
    captureLog.info(
      `single-shot capture armed: insert at index ${at} into "${manifest.title}"`,
    );
    // No hotkey for single-shot: the hotkey path can't carry the insert index
    // and wouldn't auto-stop, so the one shot is mouse-click only.
    this.attachTriggers({ hotkey: false });
    this.onRecordingChange?.(true);
    this.emitState();
    return this.getState();
  }

  /**
   * No-click one-shot grab for the report's "+ Screenshot" insert: capture the
   * chosen surface (whole screen / a specific window / a dragged area — NOT auto,
   * which needs a click to classify) exactly once, insert it at `insertAt`, and
   * return the updated manifest. No input hook, no click marker, no recording HUD.
   *
   * The picked window/area is validated BEFORE hiding + grabbing so a stale target
   * fails loudly (grab() would otherwise silently fall back to a full-monitor shot).
   * The app window is hidden (without the recording pill) and given HIDE_SETTLE_MS
   * to leave the composited frame; captureStep's own-window guard is the hard
   * backstop that keeps shotAI out of the shot even if that settle is short.
   */
  async captureScreenshot(
    projectPath: string,
    target: CaptureTarget,
    insertAt: number,
  ): Promise<ProjectManifest> {
    if (this.session) {
      throw new Error('A recording is already in progress');
    }
    const { Monitor, Window } = await this.loadNatives();
    const manifest = await projectStore.openProject(projectPath);
    const shotsDir = path.join(projectPath, 'shots');
    await fs.mkdir(shotsDir, { recursive: true });

    // Validate an explicit window/area target NOW (before hiding the window), so a
    // window that has since closed or an area that is now off-screen surfaces a
    // clear error instead of grab()'s silent full-monitor fallback.
    if (target.mode === 'window') {
      if (!this.resolveWindow(Window, target.window)) {
        throw new Error('That window is no longer open — reopen it and try the screenshot again.');
      }
    } else if (target.mode === 'area') {
      const a = target.area;
      const onScreen =
        !!a &&
        Monitor.all().some(
          (m) =>
            a.x < m.x() + m.width() &&
            a.x + a.width > m.x() &&
            a.y < m.y() + m.height() &&
            a.y + a.height > m.y(),
        );
      if (!onScreen) {
        throw new Error('That screen area is off-screen now — drag the area again and retry.');
      }
    }

    // Seed the filename counter past any shot on disk (deletes leave orphans).
    let stepCount = manifest.steps.length;
    try {
      for (const f of await fs.readdir(shotsDir)) {
        const m = /^step-(\d+)\.png$/i.exec(f);
        if (m) stepCount = Math.max(stepCount, Number(m[1]));
      }
    } catch {
      /* shots/ unreadable — fall back to manifest length */
    }

    const at = Math.max(0, Math.min(Math.round(insertAt), manifest.steps.length));
    // `single` marks this a one-shot so a Discard could never delete the whole
    // project (discardDeletesProject checks !single); fired:true is belt-and-
    // suspenders since no mouse hook is attached anyway.
    this.session = {
      projectPath,
      projectTitle: manifest.title,
      paused: false,
      stepCount,
      target,
      stepCountAtStart: manifest.steps.length,
      createdThisSession: false,
      addedStepIds: [],
      single: { insertAt: at, fired: true },
    };
    captureLog.info(
      `no-click screenshot armed: [mode=${target.mode}] insert at index ${at} into "${manifest.title}"`,
    );
    // Hide the app window WITHOUT the recording pill (the pill would flash a
    // misleading "recording" HUD and could become the focused own-window that
    // trips captureStep's guard). forceHide so it hides even in demo mode — the
    // hide is now the sole thing keeping shotAI out of the shot (guard skipped).
    // Deliberately do NOT emitState() — a recording status would unmount the
    // report view mid-grab.
    this.onRecordingChange?.(true, { pill: false, forceHide: true });
    try {
      await new Promise((r) => setTimeout(r, HIDE_SETTLE_MS));
      const step = await this.captureStep('hotkey', null, 'left', {
        insertAt: at,
        broadcast: false,
        // No click here, and the app window is already hidden — the own-window
        // guard (which keys off active/focused window) would spuriously abort.
        skipOwnWindowGuard: true,
      });
      if (!step) {
        // With the guard skipped, a null step means the capture itself failed
        // (e.g. the monitor/window couldn't be grabbed). Surface it for a retry.
        throw new Error('Could not capture the screen — make sure the target is visible, then try again.');
      }
    } finally {
      this.session = null;
      this.onRecordingChange?.(false); // restore + focus the app window
      this.emitState();
    }
    return projectStore.openProject(projectPath);
  }

  private attachTriggers(opts: { hotkey?: boolean } = {}): void {
    const { uIOhook } = this.natives!;
    if (!this.hookAttached) {
      uIOhook.on('mousedown', this.onMouseDown);
      uIOhook.start();
      this.hookAttached = true;
    }
    if ((opts.hotkey ?? true) && !this.hotkeyRegistered) {
      this.hotkeyRegistered = globalShortcut.register(DEFAULT_HOTKEY, this.onHotkey);
    }
  }

  private detachTriggers(): void {
    this.disarmMenu(); // don't carry an armed menu window/poller across sessions
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
    this.disarmMenu();
    captureLog.info('recording paused');
    this.emitState();
    return this.getState();
  }

  resume(): CaptureState {
    if (this.session) this.session.paused = false;
    this.disarmMenu();
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

  /**
   * Discard the active session: stop capture and remove this session's work. If
   * the project was created this session AND was empty when capture began, delete
   * the whole project folder; otherwise delete only the steps added this session.
   */
  async discard(): Promise<{ state: CaptureState; projectDeleted: boolean }> {
    const s = this.session;
    this.detachTriggers();
    await this.queue.catch(() => undefined); // in-flight captures finish (session still set → ids recorded)
    this.session = null;
    let projectDeleted = false;
    if (s) {
      const whole = CaptureController.discardDeletesProject(s);
      try {
        if (whole) {
          await projectStore.deleteProject(s.projectPath);
          projectDeleted = true;
          captureLog.info(`capture discarded — deleted new project at ${s.projectPath}`);
        } else if (s.addedStepIds.length) {
          await projectStore.deleteSteps(s.projectPath, s.addedStepIds);
          captureLog.info(
            `capture discarded — removed ${s.addedStepIds.length} session step(s) from ${s.projectPath}`,
          );
        } else {
          captureLog.info('capture discarded — nothing was captured this session');
        }
      } catch (e) {
        captureLog.warn('discard cleanup failed:', e);
      }
      this.onRecordingChange?.(false); // restore the main window
    }
    this.emitState();
    return { state: this.getState(), projectDeleted };
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
      /** Single-shot: insert at this manifest index (renumbered) instead of append. */
      insertAt?: number | null;
      /** UI element resolved AT mousedown (started in onMouseDown, before the
       *  click's effect changes the UI). Falls back to a query here if absent. */
      elementPromise?: Promise<StepElement | null>;
      /** Broadcast captureStepAdded to renderers (default true). The no-click
       *  one-shot passes false: it returns the manifest for a positioned re-render,
       *  and App.onStepAdded would otherwise append the step at the wrong index. */
      broadcast?: boolean;
      /** Skip the own-window guard. The no-click one-shot (+Screenshot) sets this:
       *  it has no click to mis-attribute to shotAI, and a just-hidden window can
       *  still momentarily report as the active/focused window (which would abort
       *  the grab). shotAI is already hidden, so the hide — not the guard — is what
       *  keeps it out of the shot, exactly as during a normal recording. */
      skipOwnWindowGuard?: boolean;
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
    // Skipped for the no-click one-shot (see skipOwnWindowGuard): there's no click
    // to mis-attribute, and the just-hidden app window can still report as active.
    if (
      !opts.skipOwnWindowGuard &&
      (activeIsOwn || focusedIsOwn || (point && this.pointHitsOwnWindow(point)))
    ) {
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

    // Resolve the UI element under the click (best-effort, off the main thread).
    // Prefer the query started at MOUSEDOWN (opts.elementPromise) — by the time
    // this step runs, the click may have closed a dialog / minimized the window /
    // dismissed the menu, so a query now would hit whatever is underneath. Fall
    // back to a query here for callers that don't pre-resolve (hotkey, tests).
    const elementPromise: Promise<StepElement | null> =
      opts.elementPromise ?? (point ? getElementAtPoint(point.x, point.y) : Promise.resolve(null));

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

    const grab = async (): Promise<Grab | null> => {
      // CONTEXT-MENU SELECTION — the menu is a separate top-level popup window
      // that per-window (PrintWindow) capture can't see, so grab the monitor
      // (BitBlt of the composited desktop includes the popup) and crop to frame
      // the menu WITH what it belongs to. 'screen' keeps the whole monitor
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
            full = await mon.captureImage();
          } catch (e) {
            captureLog.warn('menu-popup capture failed:', e);
            return null;
          }
        }

        let region: Rect | null = null;
        if (mode !== 'screen') {
          let base: Rect | null =
            mode === 'window'
              ? winRect
              : mode === 'area'
                ? target.area ?? null
                : opts.menuOwnerBounds ?? null; // 'auto' → owner at right-click time
          if (point) {
            // A generous, roughly symmetric box around the selection click so the
            // menu is included no matter which way Windows flipped it (menus open
            // UP near the screen bottom, LEFT near the right edge). Also covers
            // 'area' mode and the case where ownerBounds is null (focus unresolved
            // at right-click), where there's no owner rect to union.
            const box = clickBox(point, mon.scaleFactor());
            base = base ? unionRect(base, box) : box;
          }
          region = base;
        }

        try {
          if (!region) {
            return { png: await full.toPng(), originX: mon.x(), originY: mon.y(), monitor: mon };
          }
          return await cropToRegion(mon, full, region);
        } catch (e) {
          captureLog.warn('menu-popup crop failed:', e);
          return null;
        }
      }

      // WINDOW — an explicitly picked window, or 'auto' classified the click as
      // a normal app window (the focused window, already confirmed not ours).
      // Capture the MONITOR and crop to the window's bounds rather than
      // PrintWindow (win.captureImageSync): PrintWindow grabs only the app's
      // CLIENT area, so it misses the DWM-drawn title bar AND any popup/dropdown
      // (a separate top-level window). The monitor BitBlt is WYSIWYG — it includes
      // the title bar and any dropdown that paints within the window's rectangle.
      if (mode === 'window' || autoMode === 'window') {
        const win =
          mode === 'window' ? this.resolveWindow(Window, target.window) : focused;
        const winRect: Rect | null =
          win && win.x() > -10000 && win.y() > -10000
            ? { x: win.x(), y: win.y(), width: win.width(), height: win.height() }
            : null;
        if (winRect) {
          const mon = Monitor.fromPoint(winRect.x, winRect.y) ?? clickMonitor;
          if (mon) {
            try {
              const full = await mon.captureImage();
              // Crop tightly to the window bounds — NO generous click box here
              // (that bloated normal captures and pulled in neighboring windows).
              // The menu-popup path keeps its box to catch flipped/overflowing menus.
              return await cropToRegion(mon, full, winRect);
            } catch (e) {
              captureLog.warn('window capture failed, falling back to monitor:', e);
            }
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
            const areaImg = await (await mon.captureImage()).crop(cropX, cropY, cropW, cropH);
            return {
              png: await areaImg.toPng(),
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
      }
      if (!mon) return null;

      // REGION — 'auto' shell element (taskbar/Start/tray): a crop around the
      // click, avoiding the giant black shell-window capture. Sized generously so
      // a Start-menu tile / flyout has context (the old 520x400 was too tight).
      if (autoMode === 'region' && point) {
        try {
          const sf = mon.scaleFactor() || 1;
          const boxW = Math.min(Math.round(820 * sf), mon.width());
          const boxH = Math.min(Math.round(640 * sf), mon.height());
          const cx = point.x - mon.x();
          const cy = point.y - mon.y();
          const cropX = Math.max(0, Math.min(cx - Math.floor(boxW / 2), mon.width() - boxW));
          const cropY = Math.max(0, Math.min(cy - Math.floor(boxH / 2), mon.height() - boxH));
          const regionImg = await (await mon.captureImage()).crop(cropX, cropY, boxW, boxH);
          return {
            png: await regionImg.toPng(),
            originX: mon.x() + cropX,
            originY: mon.y() + cropY,
            monitor: mon,
          };
        } catch (e) {
          captureLog.warn('region capture failed, falling back to full monitor:', e);
        }
      }

      // FULLSCREEN — the whole monitor ('auto' desktop/fallback, 'screen').
      try {
        return {
          png: await (await mon.captureImage()).toPng(),
          originX: mon.x(),
          originY: mon.y(),
          monitor: mon,
        };
      } catch (e) {
        captureLog.warn('monitor capture failed:', e);
        return null;
      }
    };
    // Capture + PNG encode run on worker threads (async) so this doesn't block
    // the event loop / global mouse hook — a slow sync encode was tripping
    // Windows' low-level-hook timeout and dropping rapid follow-up clicks.
    const tGrab = Date.now();
    const grabbed = await grab();
    if (!grabbed) return null;
    const { png, originX, originY, monitor } = grabbed;
    const grabMs = Date.now() - tGrab;
    // Downscale before writing so the stored PNG (which every downstream consumer
    // reads for its dimensions) is the smaller image; click.image is scaled to
    // match below (T2).
    const tDown = Date.now();
    const { png: outPng, scale: imageScale } = downscalePng(png);
    const downMs = Date.now() - tDown;
    if (grabMs + downMs > 120) {
      captureLog.debug(`capture timing: grab(async)=${grabMs}ms downscale(sync)=${downMs}ms`);
    }

    const order = ++this.session.stepCount;
    const filename = `step-${String(order).padStart(4, '0')}.png`;
    await fs.writeFile(
      path.join(this.session.projectPath, 'shots', filename),
      outPng,
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

    // The UI element under the click (resolved off-thread, started above).
    const element = (await elementPromise) ?? {
      available: false,
      name: null,
      controlType: null,
      bounds: null,
    };
    const appName = window?.app ?? 'screen';

    const step: ProjectStep = {
      id: randomUUID(),
      order,
      screenshot: `shots/${filename}`,
      trigger,
      click: point
        ? {
            global: point,
            image: {
              x: Math.round((point.x - originX) * imageScale),
              y: Math.round((point.y - originY) * imageScale),
            },
            button,
            ...(imageScale !== 1 ? { imageScale } : {}),
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
      element,
      caption:
        trigger === 'click'
          ? buildClickCaption(button, !!opts.menuPopup, appName, element)
          : `Capture: ${window?.title ?? 'screen'}`,
      crop: null,
      annotations: [],
    };

    // Placement: a fixed opts.insertAt (single-shot / no-click one-shot) wins;
    // otherwise a +Capture session's rolling insertCursor inserts + advances;
    // otherwise append (normal recording). The cursor is read + advanced here
    // inside the serialized captureStep, so rapid clicks stay ordered.
    const insertIndex =
      opts.insertAt != null
        ? opts.insertAt
        : this.session.insertCursor != null
          ? this.session.insertCursor
          : null;
    if (insertIndex != null) {
      await projectStore.insertStepAt(this.session.projectPath, step, insertIndex);
      if (opts.insertAt == null && this.session.insertCursor != null) {
        this.session.insertCursor = insertIndex + 1;
      }
    } else {
      await projectStore.addStep(this.session.projectPath, step);
    }
    // Track this session's additions so a Discard can remove exactly them.
    this.session?.addedStepIds.push(step.id);
    captureLog.info(
      `step #${order} [${trigger}/${autoMode ? `auto:${autoMode}` : mode}${button === 'right' ? ' right' : opts.menuPopup ? ' menu-select' : ''}]${opts.insertAt != null ? ` (insert@${opts.insertAt})` : ''} ${window?.app ?? 'screen'}${element.name ? ` el='${element.name}'(${element.controlType})` : ''} -> ${filename} (${Math.round(outPng.length / 1024)} KB${imageScale !== 1 ? ` @${imageScale.toFixed(2)}x` : ''})`,
    );
    // The no-click one-shot (broadcast:false) suppresses BOTH the step-added
    // event AND the state emit: it never sets a "recording" status (which would
    // flip the report view), and the caller returns the manifest + emits idle.
    if (opts.broadcast !== false) {
      this.broadcast(IpcChannels.captureStepAdded, step);
      this.emitState();
    }
    return step;
  }
}

/** Build a CaptureController that broadcasts events to all renderer windows. */
export function createCaptureController(
  opts: { onRecordingChange?: RecordingChange } = {},
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
