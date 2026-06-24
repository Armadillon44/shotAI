/**
 * IPC contract shared by the main process, preload, and renderer.
 * Keep channel names and payload types here so all three stay in sync.
 */
import type { ProjectManifest, ProjectStep, ProjectSummary } from './project';

export interface AppInfo {
  name: string;
  version: string;
  platform: string;
  arch: string;
  electron: string;
  chrome: string;
  node: string;
}

export type CaptureStatus = 'idle' | 'recording' | 'paused';

export interface CaptureState {
  status: CaptureStatus;
  projectPath: string | null;
  projectTitle: string | null;
  stepCount: number;
}

/** IPC channel names — single source of truth. */
export const IpcChannels = {
  getAppInfo: 'app:get-info',
  getProjectsDir: 'projects:get-dir',
  chooseProjectsDir: 'projects:choose-dir',
  listRecentProjects: 'projects:list-recent',
  createProject: 'projects:create',
  openProject: 'projects:open',
  captureStart: 'capture:start',
  capturePause: 'capture:pause',
  captureResume: 'capture:resume',
  captureStop: 'capture:stop',
  captureGetState: 'capture:get-state',
  // main -> renderer events
  captureStateChanged: 'capture:state-changed',
  captureStepAdded: 'capture:step-added',
} as const;

/** The typed API exposed to the renderer on `window.shotai` via contextBridge. */
export interface ShotaiApi {
  /** Runtime / app info from the main process. */
  getAppInfo(): Promise<AppInfo>;
  projects: {
    getDir(): Promise<string>;
    chooseDir(): Promise<string | null>;
    listRecent(): Promise<ProjectSummary[]>;
    create(title: string): Promise<ProjectSummary>;
    open(projectPath: string): Promise<ProjectManifest>;
  };
  capture: {
    /** Start (or append to) a recording session for the given project. */
    start(projectPath: string): Promise<CaptureState>;
    pause(): Promise<CaptureState>;
    resume(): Promise<CaptureState>;
    stop(): Promise<CaptureState>;
    getState(): Promise<CaptureState>;
    /** Subscribe to capture state changes; returns an unsubscribe function. */
    onStateChanged(cb: (state: CaptureState) => void): () => void;
    /** Subscribe to newly captured steps; returns an unsubscribe function. */
    onStepAdded(cb: (step: ProjectStep) => void): () => void;
  };
}
