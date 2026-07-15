// Preload — runs sandboxed (contextIsolation: true, nodeIntegration: false,
// sandbox: true). Exposes a typed, minimal API on `window.shotai`; the renderer
// never gets direct access to Node or to ipcRenderer.
// https://www.electronjs.org/docs/latest/tutorial/process-model#preload-scripts
import { contextBridge, ipcRenderer, type IpcRendererEvent } from 'electron';
import {
  IpcChannels,
  type CaptureState,
  type ExportFormat,
  type ShotaiApi,
  type SopProgress,
} from '../shared/ipc';
import type { CaptureTarget, ProjectStep, Rect, SopIntro, StepPatch, ThemePref } from '../shared/project';
import type { SopSettings } from '../shared/sop';

const api: ShotaiApi = {
  getAppInfo: () => ipcRenderer.invoke(IpcChannels.getAppInfo),
  openExternal: (url: string) => ipcRenderer.invoke(IpcChannels.openExternal, url),
  onOpenSettings: (cb: () => void) => {
    const listener = () => cb();
    ipcRenderer.on(IpcChannels.openSettings, listener);
    return () => ipcRenderer.removeListener(IpcChannels.openSettings, listener);
  },
  onImportProject: (cb: () => void) => {
    const listener = () => cb();
    ipcRenderer.on(IpcChannels.menuImportProject, listener);
    return () => ipcRenderer.removeListener(IpcChannels.menuImportProject, listener);
  },
  setDetailView: (open: boolean) => ipcRenderer.invoke(IpcChannels.setDetailView, open),
  projects: {
    getDir: () => ipcRenderer.invoke(IpcChannels.getProjectsDir),
    chooseDir: () => ipcRenderer.invoke(IpcChannels.chooseProjectsDir),
    listRecent: () => ipcRenderer.invoke(IpcChannels.listRecentProjects),
    list: () => ipcRenderer.invoke(IpcChannels.listProjects),
    create: (title: string) =>
      ipcRenderer.invoke(IpcChannels.createProject, title),
    rename: (projectPath: string, title: string) =>
      ipcRenderer.invoke(IpcChannels.renameProject, projectPath, title),
    delete: (projectPath: string) =>
      ipcRenderer.invoke(IpcChannels.deleteProject, projectPath),
    reveal: (projectPath: string) =>
      ipcRenderer.invoke(IpcChannels.revealProject, projectPath),
    archive: (projectPath: string) =>
      ipcRenderer.invoke(IpcChannels.archiveProject, projectPath),
    unarchive: (projectPath: string) =>
      ipcRenderer.invoke(IpcChannels.unarchiveProject, projectPath),
    onChanged: (cb: () => void) => {
      const listener = () => cb();
      ipcRenderer.on(IpcChannels.projectsChanged, listener);
      return () => ipcRenderer.removeListener(IpcChannels.projectsChanged, listener);
    },
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
    mergeSteps: (
      projectPath: string,
      keepId: string,
      dropId: string,
      patch: StepPatch,
      flattenedPng?: Uint8Array | null,
    ) =>
      ipcRenderer.invoke(
        IpcChannels.mergeSteps,
        projectPath,
        keepId,
        dropId,
        patch,
        flattenedPng ?? null,
      ),
    addTextStep: (
      projectPath: string,
      atIndex: number,
      callout?: 'note' | 'caution' | 'warning',
    ) => ipcRenderer.invoke(IpcChannels.addTextStep, projectPath, atIndex, callout),
    redactScan: (projectPath: string, stepId: string) =>
      ipcRenderer.invoke(IpcChannels.redactScan, projectPath, stepId),
    setIntro: (projectPath: string, intro: SopIntro | null) =>
      ipcRenderer.invoke(IpcChannels.setProjectIntro, projectPath, intro),
    revertSop: (projectPath: string) =>
      ipcRenderer.invoke(IpcChannels.revertSop, projectPath),
    export: (projectPath: string, format: ExportFormat) =>
      ipcRenderer.invoke(IpcChannels.exportProject, projectPath, format),
    exportToDir: (projectPath: string, format: ExportFormat, dir: string) =>
      ipcRenderer.invoke(IpcChannels.exportToDir, projectPath, format, dir),
    exportToOwnFolder: (projectPath: string, format: ExportFormat) =>
      ipcRenderer.invoke(IpcChannels.exportToOwnFolder, projectPath, format),
    chooseExportDir: () => ipcRenderer.invoke(IpcChannels.chooseExportDir),
    revealExportDir: (dir: string) => ipcRenderer.invoke(IpcChannels.revealExportDir, dir),
    exportPackage: (projectPath: string, includeOriginals: boolean) =>
      ipcRenderer.invoke(IpcChannels.exportPackage, projectPath, includeOriginals),
    importPackage: () => ipcRenderer.invoke(IpcChannels.importPackage),
  },
  settings: {
    getSop: () => ipcRenderer.invoke(IpcChannels.getSopSettings),
    setSop: (patch: Partial<SopSettings>) =>
      ipcRenderer.invoke(IpcChannels.setSopSettings, patch),
    getCaptureNoHide: () => ipcRenderer.invoke(IpcChannels.getCaptureNoHide),
    setCaptureNoHide: (value: boolean) =>
      ipcRenderer.invoke(IpcChannels.setCaptureNoHide, value),
    getCaptureScale: () => ipcRenderer.invoke(IpcChannels.getCaptureScale),
    setCaptureScale: (value: number) =>
      ipcRenderer.invoke(IpcChannels.setCaptureScale, value),
    getHasSeenTour: () => ipcRenderer.invoke(IpcChannels.getHasSeenTour),
    setHasSeenTour: (value: boolean) =>
      ipcRenderer.invoke(IpcChannels.setHasSeenTour, value),
    getUserName: () => ipcRenderer.invoke(IpcChannels.getUserName),
    setUserName: (value: string) => ipcRenderer.invoke(IpcChannels.setUserName, value),
    getIncludeNameInReports: () => ipcRenderer.invoke(IpcChannels.getIncludeNameInReports),
    setIncludeNameInReports: (value: boolean) =>
      ipcRenderer.invoke(IpcChannels.setIncludeNameInReports, value),
    getArchiveAgeDays: () => ipcRenderer.invoke(IpcChannels.getArchiveAgeDays),
    setArchiveAgeDays: (value: number) =>
      ipcRenderer.invoke(IpcChannels.setArchiveAgeDays, value),
    getTheme: () => ipcRenderer.invoke(IpcChannels.getTheme),
    setTheme: (value: ThemePref) => ipcRenderer.invoke(IpcChannels.setTheme, value),
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
    cancel: () => ipcRenderer.send(IpcChannels.claudeCancel),
    onSopProgress: (cb: (p: SopProgress) => void) => {
      const listener = (_e: IpcRendererEvent, p: SopProgress) => cb(p);
      ipcRenderer.on(IpcChannels.claudeSopProgress, listener);
      return () =>
        ipcRenderer.removeListener(IpcChannels.claudeSopProgress, listener);
    },
  },
  capture: {
    start: (
      projectPath: string,
      target?: CaptureTarget,
      opts?: { createdThisSession?: boolean; insertAt?: number },
    ) =>
      // Pass the opts object through verbatim (isomorphic to the ShotaiApi shape)
      // rather than flattening one field to a positional bool — adding a future
      // opts field then can't silently drop at the bridge.
      ipcRenderer.invoke(IpcChannels.captureStart, projectPath, target, opts),
    captureSingle: (projectPath: string, atIndex: number) =>
      ipcRenderer.invoke(IpcChannels.captureSingle, projectPath, atIndex),
    screenshot: (projectPath: string, target: CaptureTarget, atIndex: number) =>
      ipcRenderer.invoke(IpcChannels.captureScreenshot, projectPath, target, atIndex),
    pause: () => ipcRenderer.invoke(IpcChannels.capturePause),
    resume: () => ipcRenderer.invoke(IpcChannels.captureResume),
    stop: () => ipcRenderer.invoke(IpcChannels.captureStop),
    discard: () => ipcRenderer.invoke(IpcChannels.captureDiscard),
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
