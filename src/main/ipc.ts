import { app, ipcMain } from 'electron';
import { IpcChannels, type AppInfo } from '../shared/ipc';

/** Register all main-process IPC handlers. Call once, after the app is ready. */
export function registerIpcHandlers(): void {
  ipcMain.handle(IpcChannels.getAppInfo, (): AppInfo => {
    if (!app.isPackaged) {
      console.log('[shotAI] ipc: app:get-info invoked');
    }
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
}
