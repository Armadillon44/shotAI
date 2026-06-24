// Preload — runs sandboxed (contextIsolation: true, nodeIntegration: false,
// sandbox: true). Exposes a typed, minimal API on `window.shotai`; the renderer
// never gets direct access to Node or to ipcRenderer.
// https://www.electronjs.org/docs/latest/tutorial/process-model#preload-scripts
import { contextBridge, ipcRenderer } from 'electron';
import { IpcChannels, type ShotaiApi } from '../shared/ipc';

const api: ShotaiApi = {
  getAppInfo: () => ipcRenderer.invoke(IpcChannels.getAppInfo),
};

contextBridge.exposeInMainWorld('shotai', api);
