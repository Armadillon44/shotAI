// Preload — runs sandboxed (contextIsolation: true, nodeIntegration: false,
// sandbox: true). Exposes a typed, minimal API on `window.shotai`; the renderer
// never gets direct access to Node or to ipcRenderer.
// https://www.electronjs.org/docs/latest/tutorial/process-model#preload-scripts
import { contextBridge, ipcRenderer, type IpcRendererEvent } from 'electron';
import {
  IpcChannels,
  type CaptureState,
  type ShotaiApi,
  type SopProgress,
} from '../shared/ipc';
import type { CaptureTarget, ProjectStep, Rect, StepPatch } from '../shared/project';
import type { SopSettings } from '../shared/sop';

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
    importStep: (projectPath: string, bytes: Uint8Array, atIndex?: number) =>
      ipcRenderer.invoke(IpcChannels.importStep, projectPath, bytes, atIndex),
    deleteStep: (projectPath: string, stepId: string) =>
      ipcRenderer.invoke(IpcChannels.deleteStep, projectPath, stepId),
    reorderSteps: (projectPath: string, orderedIds: string[]) =>
      ipcRenderer.invoke(IpcChannels.reorderSteps, projectPath, orderedIds),
    addTextStep: (projectPath: string, atIndex: number) =>
      ipcRenderer.invoke(IpcChannels.addTextStep, projectPath, atIndex),
    revertSop: (projectPath: string) =>
      ipcRenderer.invoke(IpcChannels.revertSop, projectPath),
  },
  settings: {
    getSop: () => ipcRenderer.invoke(IpcChannels.getSopSettings),
    setSop: (patch: Partial<SopSettings>) =>
      ipcRenderer.invoke(IpcChannels.setSopSettings, patch),
  },
  claude: {
    keyStatus: () => ipcRenderer.invoke(IpcChannels.claudeKeyStatus),
    setApiKey: (key: string) => ipcRenderer.invoke(IpcChannels.claudeSetKey, key),
    clearApiKey: () => ipcRenderer.invoke(IpcChannels.claudeClearKey),
    testKey: () => ipcRenderer.invoke(IpcChannels.claudeTestKey),
    estimate: (projectPath: string) =>
      ipcRenderer.invoke(IpcChannels.claudeEstimate, projectPath),
    generateSop: (projectPath: string) =>
      ipcRenderer.invoke(IpcChannels.claudeGenerateSop, projectPath),
    onSopProgress: (cb: (p: SopProgress) => void) => {
      const listener = (_e: IpcRendererEvent, p: SopProgress) => cb(p);
      ipcRenderer.on(IpcChannels.claudeSopProgress, listener);
      return () =>
        ipcRenderer.removeListener(IpcChannels.claudeSopProgress, listener);
    },
  },
  capture: {
    start: (projectPath: string, target?: CaptureTarget) =>
      ipcRenderer.invoke(IpcChannels.captureStart, projectPath, target),
    captureSingle: (projectPath: string, atIndex: number) =>
      ipcRenderer.invoke(IpcChannels.captureSingle, projectPath, atIndex),
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
