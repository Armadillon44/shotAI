// Preload — runs sandboxed (contextIsolation: true, nodeIntegration: false,
// sandbox: true). Exposes a typed, minimal API on `window.shotai`; the renderer
// never gets direct access to Node or to ipcRenderer.
// https://www.electronjs.org/docs/latest/tutorial/process-model#preload-scripts
import { contextBridge, ipcRenderer, type IpcRendererEvent } from 'electron';
import { IpcChannels, type CaptureState, type ShotaiApi } from '../shared/ipc';
import type { ProjectStep } from '../shared/project';

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
  capture: {
    start: (projectPath: string) =>
      ipcRenderer.invoke(IpcChannels.captureStart, projectPath),
    pause: () => ipcRenderer.invoke(IpcChannels.capturePause),
    resume: () => ipcRenderer.invoke(IpcChannels.captureResume),
    stop: () => ipcRenderer.invoke(IpcChannels.captureStop),
    getState: () => ipcRenderer.invoke(IpcChannels.captureGetState),
    onStateChanged: (cb: (state: CaptureState) => void) => {
      const listener = (_e: IpcRendererEvent, state: CaptureState) => cb(state);
      ipcRenderer.on(IpcChannels.captureStateChanged, listener);
      return () =>
        ipcRenderer.removeListener(IpcChannels.captureStateChanged, listener);
    },
    onStepAdded: (cb: (step: ProjectStep) => void) => {
      const listener = (_e: IpcRendererEvent, step: ProjectStep) => cb(step);
      ipcRenderer.on(IpcChannels.captureStepAdded, listener);
      return () =>
        ipcRenderer.removeListener(IpcChannels.captureStepAdded, listener);
    },
  },
};

contextBridge.exposeInMainWorld('shotai', api);
