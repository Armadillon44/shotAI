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
import type { CaptureMode, CaptureTarget } from '../shared/project';
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
      return projectStore.openProject(asString(projectPath, 'projectPath'));
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
