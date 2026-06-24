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

function devLog(message: string): void {
  if (!app.isPackaged) console.log(`[shotAI] ${message}`);
}

/** Validate an IPC argument is a string (types are erased at the IPC boundary). */
function asString(value: unknown, name: string): string {
  if (typeof value !== 'string') {
    throw new Error(`${name} must be a string`);
  }
  return value;
}

/** Register all main-process IPC handlers. Call once, after the app is ready. */
export function registerIpcHandlers(capture: CaptureController): void {
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
    (_event: IpcMainInvokeEvent, projectPath: unknown) => {
      devLog('ipc: capture:start');
      return capture.start(asString(projectPath, 'projectPath'));
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
