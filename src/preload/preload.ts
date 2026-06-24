// Preload — runs sandboxed (contextIsolation: true, nodeIntegration: false,
// sandbox: true). Exposes a typed, minimal API on `window.shotai`; the renderer
// never gets direct access to Node or to ipcRenderer.
// https://www.electronjs.org/docs/latest/tutorial/process-model#preload-scripts
import { contextBridge, ipcRenderer } from 'electron';
import { IpcChannels, type ShotaiApi } from '../shared/ipc';

const api: ShotaiApi = {
  getAppInfo: () => ipcRenderer.invoke(IpcChannels.getAppInfo),
  projects: {
    getDir: () => ipcRenderer.invoke(IpcChannels.getProjectsDir),
    chooseDir: () => ipcRenderer.invoke(IpcChannels.chooseProjectsDir),
    listRecent: () => ipcRenderer.invoke(IpcChannels.listRecentProjects),
    create: (title: string) =>
      ipcRenderer.invoke(IpcChannels.createProject, title),
    open: (projectPath: string) =>
      ipcRenderer.invoke(IpcChannels.openProject, projectPath),
  },
};

contextBridge.exposeInMainWorld('shotai', api);
