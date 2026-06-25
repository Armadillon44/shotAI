import {
  app,
  BrowserWindow,
  dialog,
  ipcMain,
  type IpcMainInvokeEvent,
} from 'electron';
import { IpcChannels, type AppInfo } from '../shared/ipc';
import * as projectStore from './ProjectStore';
import type { CaptureController } from './CaptureController';
import type { RegionService } from './RegionService';
import type {
  Annotation,
  CaptureMode,
  CaptureTarget,
  Point,
  Rect,
  StepClick,
  StepKind,
  StepPatch,
} from '../shared/project';
import { ipcLog } from './logger';

function devLog(message: string): void {
  ipcLog.debug(message);
}

/** Validate an IPC argument is a string (types are erased at the IPC boundary). */
function asString(value: unknown, name: string): string {
  if (typeof value !== 'string') {
    throw new Error(`${name} must be a string`);
  }
  return value;
}

const CAPTURE_MODES: ReadonlySet<CaptureMode> = new Set<CaptureMode>([
  'auto',
  'window',
  'area',
  'screen',
  'all',
]);

const isNum = (v: unknown): v is number => typeof v === 'number' && Number.isFinite(v);

/**
 * Validate/normalize a CaptureTarget arriving over IPC (types are erased at the
 * boundary). Returns undefined for a missing target (→ defaults to Auto). Only
 * keeps the fields relevant to the chosen mode.
 */
function parseCaptureTarget(value: unknown): CaptureTarget | undefined {
  if (value == null) return undefined;
  if (typeof value !== 'object') throw new Error('target must be an object');
  const v = value as Record<string, unknown>;
  if (typeof v.mode !== 'string' || !CAPTURE_MODES.has(v.mode as CaptureMode)) {
    throw new Error('target.mode is invalid');
  }
  const target: CaptureTarget = { mode: v.mode as CaptureMode };
  if (target.mode === 'screen' && isNum(v.monitorId)) {
    target.monitorId = v.monitorId;
  } else if (target.mode === 'window' && v.window && typeof v.window === 'object') {
    const w = v.window as Record<string, unknown>;
    if (isNum(w.id) && isNum(w.pid) && typeof w.title === 'string') {
      target.window = { id: w.id, pid: w.pid, title: w.title };
    }
  } else if (target.mode === 'area' && v.area && typeof v.area === 'object') {
    const a = v.area as Record<string, unknown>;
    if (isNum(a.x) && isNum(a.y) && isNum(a.width) && isNum(a.height)) {
      target.area = { x: a.x, y: a.y, width: a.width, height: a.height };
    }
  }
  return target;
}

function parseRect(value: unknown): Rect | null {
  if (!value || typeof value !== 'object') return null;
  const r = value as Record<string, unknown>;
  if (isNum(r.x) && isNum(r.y) && isNum(r.width) && isNum(r.height)) {
    return { x: r.x, y: r.y, width: r.width, height: r.height };
  }
  return null;
}

function parsePoint(value: unknown): Point | null {
  if (!value || typeof value !== 'object') return null;
  const p = value as Record<string, unknown>;
  return isNum(p.x) && isNum(p.y) ? { x: p.x, y: p.y } : null;
}

const CLICK_BUTTONS: ReadonlySet<StepClick['button']> = new Set<StepClick['button']>([
  'left',
  'right',
  'middle',
  'other',
]);

/** Validate a moved click (editor only repositions image coords). */
function parseClick(value: unknown): StepClick | null {
  if (!value || typeof value !== 'object') return null;
  const c = value as Record<string, unknown>;
  const global = parsePoint(c.global);
  const image = parsePoint(c.image);
  if (!global || !image) return null;
  const button =
    typeof c.button === 'string' && CLICK_BUTTONS.has(c.button as StepClick['button'])
      ? (c.button as StepClick['button'])
      : 'left';
  return { global, image, button };
}

const STEP_KINDS: ReadonlySet<StepKind> = new Set<StepKind>(['shot', 'text']);

/**
 * Validate an editor step-patch arriving over IPC (types are erased at the
 * boundary). Keeps only recognized fields; annotations are kept as-is when they
 * look like annotations (object with string `type` + `id`) — they're the user's
 * own project data, stored verbatim as JSON.
 */
function parseStepPatch(value: unknown): StepPatch {
  if (!value || typeof value !== 'object') throw new Error('patch must be an object');
  const v = value as Record<string, unknown>;
  const patch: StepPatch = {};
  if (typeof v.caption === 'string') patch.caption = v.caption;
  if (typeof v.note === 'string') patch.note = v.note;
  if (typeof v.heading === 'string') patch.heading = v.heading;
  if (typeof v.body === 'string') patch.body = v.body;
  if (typeof v.kind === 'string' && STEP_KINDS.has(v.kind as StepKind)) {
    patch.kind = v.kind as StepKind;
  }
  if ('crop' in v) patch.crop = v.crop === null ? null : parseRect(v.crop);
  if ('click' in v) patch.click = v.click === null ? null : parseClick(v.click);
  if (typeof v.markerColor === 'string' && /^#[0-9a-fA-F]{3,8}$/.test(v.markerColor)) {
    patch.markerColor = v.markerColor;
  }
  if (isNum(v.reportZoom)) patch.reportZoom = Math.max(0.2, Math.min(6, v.reportZoom));
  if (isNum(v.reportPanX)) patch.reportPanX = Math.max(0, Math.min(1, v.reportPanX));
  if (isNum(v.reportPanY)) patch.reportPanY = Math.max(0, Math.min(1, v.reportPanY));
  if (Array.isArray(v.annotations)) {
    patch.annotations = v.annotations.filter((a: unknown): a is Annotation => {
      if (!a || typeof a !== 'object') return false;
      const o = a as Record<string, unknown>;
      return typeof o.type === 'string' && typeof o.id === 'string';
    });
  }
  return patch;
}

/** Register all main-process IPC handlers. Call once, after the app is ready. */
export function registerIpcHandlers(
  capture: CaptureController,
  region: RegionService,
): void {
  ipcMain.handle(IpcChannels.getAppInfo, (): AppInfo => {
    devLog('ipc: app:get-info');
    return {
      name: 'shotAI',
      version: app.getVersion(),
      platform: process.platform,
      arch: process.arch,
      electron: process.versions.electron,
      chrome: process.versions.chrome,
      node: process.versions.node,
    };
  });

  ipcMain.handle(IpcChannels.getProjectsDir, () => {
    devLog('ipc: projects:get-dir');
    return projectStore.getProjectsDir();
  });

  ipcMain.handle(
    IpcChannels.chooseProjectsDir,
    async (event: IpcMainInvokeEvent): Promise<string | null> => {
      devLog('ipc: projects:choose-dir');
      const current = await projectStore.getProjectsDir();
      const parent = BrowserWindow.fromWebContents(event.sender);
      const options: Electron.OpenDialogOptions = {
        title: 'Choose shotAI projects folder',
        defaultPath: current,
        properties: ['openDirectory', 'createDirectory'],
      };
      const result = parent
        ? await dialog.showOpenDialog(parent, options)
        : await dialog.showOpenDialog(options);
      if (result.canceled || result.filePaths.length === 0) {
        return null;
      }
      const dir = result.filePaths[0];
      await projectStore.setProjectsDir(dir);
      return dir;
    },
  );

  ipcMain.handle(IpcChannels.listRecentProjects, () => {
    devLog('ipc: projects:list-recent');
    return projectStore.listRecentProjects();
  });

  ipcMain.handle(
    IpcChannels.createProject,
    (_event: IpcMainInvokeEvent, title: unknown) => {
      devLog('ipc: projects:create');
      return projectStore.createProject(asString(title, 'title'));
    },
  );

  ipcMain.handle(
    IpcChannels.openProject,
    (_event: IpcMainInvokeEvent, projectPath: unknown) => {
      devLog('ipc: projects:open');
      return projectStore.openProjectWithId(asString(projectPath, 'projectPath'));
    },
  );

  ipcMain.handle(
    IpcChannels.updateStep,
    (
      _event: IpcMainInvokeEvent,
      projectPath: unknown,
      stepId: unknown,
      patch: unknown,
      png: unknown,
    ) => {
      devLog('ipc: projects:update-step');
      const buf =
        png instanceof Uint8Array
          ? Buffer.from(png)
          : png instanceof ArrayBuffer
            ? Buffer.from(new Uint8Array(png))
            : null;
      return projectStore.updateStep(
        asString(projectPath, 'projectPath'),
        asString(stepId, 'stepId'),
        parseStepPatch(patch),
        buf,
      );
    },
  );

  ipcMain.handle(
    IpcChannels.importStep,
    (_event: IpcMainInvokeEvent, projectPath: unknown, bytes: unknown) => {
      devLog('ipc: projects:import-step');
      const buf =
        bytes instanceof Uint8Array
          ? Buffer.from(bytes)
          : bytes instanceof ArrayBuffer
            ? Buffer.from(new Uint8Array(bytes))
            : null;
      if (!buf || buf.length === 0) throw new Error('No image data received');
      if (buf.length > 60 * 1024 * 1024) throw new Error('Image too large (max 60 MB)');
      return projectStore.importStep(asString(projectPath, 'projectPath'), buf);
    },
  );

  ipcMain.handle(
    IpcChannels.deleteStep,
    (_event: IpcMainInvokeEvent, projectPath: unknown, stepId: unknown) => {
      devLog('ipc: projects:delete-step');
      return projectStore.deleteStep(
        asString(projectPath, 'projectPath'),
        asString(stepId, 'stepId'),
      );
    },
  );

  ipcMain.handle(
    IpcChannels.reorderSteps,
    (_event: IpcMainInvokeEvent, projectPath: unknown, orderedIds: unknown) => {
      devLog('ipc: projects:reorder-steps');
      if (!Array.isArray(orderedIds) || orderedIds.some((id) => typeof id !== 'string')) {
        throw new Error('orderedIds must be an array of strings');
      }
      return projectStore.reorderSteps(
        asString(projectPath, 'projectPath'),
        orderedIds as string[],
      );
    },
  );

  ipcMain.handle(
    IpcChannels.addTextStep,
    (_event: IpcMainInvokeEvent, projectPath: unknown, atIndex: unknown) => {
      devLog('ipc: projects:add-text-step');
      return projectStore.addTextStep(
        asString(projectPath, 'projectPath'),
        isNum(atIndex) ? atIndex : Number.MAX_SAFE_INTEGER,
      );
    },
  );

  ipcMain.handle(
    IpcChannels.captureStart,
    (_event: IpcMainInvokeEvent, projectPath: unknown, target: unknown) => {
      devLog('ipc: capture:start');
      return capture.start(asString(projectPath, 'projectPath'), {
        target: parseCaptureTarget(target),
      });
    },
  );
  ipcMain.handle(IpcChannels.captureListTargets, () => {
    devLog('ipc: capture:list-targets');
    return capture.listTargets();
  });

  ipcMain.handle(
    IpcChannels.regionSelectArea,
    async (event: IpcMainInvokeEvent) => {
      devLog('ipc: region:select-area');
      // Hide the requesting (main) window so it's not in the way of, or part of,
      // the area the user is selecting; restore it afterwards.
      const win = BrowserWindow.fromWebContents(event.sender);
      win?.hide();
      try {
        return await region.selectArea();
      } finally {
        if (win && !win.isDestroyed()) {
          win.show();
          win.focus();
        }
      }
    },
  );
  ipcMain.handle(IpcChannels.capturePause, () => {
    devLog('ipc: capture:pause');
    return capture.pause();
  });
  ipcMain.handle(IpcChannels.captureResume, () => {
    devLog('ipc: capture:resume');
    return capture.resume();
  });
  ipcMain.handle(IpcChannels.captureStop, () => {
    devLog('ipc: capture:stop');
    return capture.stop();
  });
  ipcMain.handle(IpcChannels.captureGetState, () => {
    devLog('ipc: capture:get-state');
    return capture.getState();
  });
}
