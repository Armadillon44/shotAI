// Preload — runs sandboxed (contextIsolation: true, nodeIntegration: false,
// sandbox: true). Exposes a typed, minimal API on `window.shotai`; the renderer
// never gets direct access to Node or to ipcRenderer.
// https://www.electronjs.org/docs/latest/tutorial/process-model#preload-scripts
import { contextBridge, ipcRenderer, type IpcRendererEvent } from 'electron';
import { IpcChannels, type CaptureState, type ShotaiApi } from '../shared/ipc';
import type { CaptureTarget, ProjectStep, Rect, StepPatch } from '../shared/project';

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
    updateStep: (
      projectPath: string,
      stepId: string,
      patch: StepPatch,
      flattenedPng?: Uint8Array | null,
    ) =>
      ipcRenderer.invoke(
        IpcChannels.updateStep,
        projectPath,
        stepId,
        patch,
        flattenedPng ?? null,
      ),
    importStep: (projectPath: string, bytes: Uint8Array) =>
      ipcRenderer.invoke(IpcChannels.importStep, projectPath, bytes),
  },
  capture: {
    start: (projectPath: string, target?: CaptureTarget) =>
      ipcRenderer.invoke(IpcChannels.captureStart, projectPath, target),
    pause: () => ipcRenderer.invoke(IpcChannels.capturePause),
    resume: () => ipcRenderer.invoke(IpcChannels.captureResume),
    stop: () => ipcRenderer.invoke(IpcChannels.captureStop),
    getState: () => ipcRenderer.invoke(IpcChannels.captureGetState),
    listTargets: () => ipcRenderer.invoke(IpcChannels.captureListTargets),
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
    onError: (cb: (message: string) => void) => {
      const listener = (_e: IpcRendererEvent, message: string) => cb(message);
      ipcRenderer.on(IpcChannels.captureError, listener);
      return () => ipcRenderer.removeListener(IpcChannels.captureError, listener);
    },
  },
  region: {
    selectArea: () => ipcRenderer.invoke(IpcChannels.regionSelectArea),
    complete: (rect: Rect) => ipcRenderer.send(IpcChannels.regionComplete, rect),
    cancel: () => ipcRenderer.send(IpcChannels.regionCancel),
  },
};

contextBridge.exposeInMainWorld('shotai', api);
