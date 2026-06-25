/**
 * IPC contract shared by the main process, preload, and renderer.
 * Keep channel names and payload types here so all three stay in sync.
 */
import type {
  CaptureTarget,
  MonitorInfo,
  ProjectManifest,
  ProjectStep,
  ProjectSummary,
  Rect,
  WindowInfo,
} from './project';

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
  captureListTargets: 'capture:list-targets',
  // region selection overlay
  regionSelectArea: 'region:select-area',
  regionComplete: 'region:complete',
  regionCancel: 'region:cancel',
  // main -> renderer events
  captureStateChanged: 'capture:state-changed',
  captureStepAdded: 'capture:step-added',
  captureError: 'capture:error',
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
    /**
     * Start (or append to) a recording session for the given project.
     * `target` selects what each step captures; defaults to Auto (smart per-click).
     */
    start(projectPath: string, target?: CaptureTarget): Promise<CaptureState>;
    pause(): Promise<CaptureState>;
    resume(): Promise<CaptureState>;
    stop(): Promise<CaptureState>;
    getState(): Promise<CaptureState>;
    /** Enumerate pickable windows + monitors for the Window/Screen choosers. */
    listTargets(): Promise<{ windows: WindowInfo[]; monitors: MonitorInfo[] }>;
    /** Subscribe to capture state changes; returns an unsubscribe function. */
    onStateChanged(cb: (state: CaptureState) => void): () => void;
    /** Subscribe to newly captured steps; returns an unsubscribe function. */
    onStepAdded(cb: (step: ProjectStep) => void): () => void;
    /** Subscribe to capture failures (so a long recording can't fail silently). */
    onError(cb: (message: string) => void): () => void;
  };
  region: {
    /**
     * (Main window) Open the transparent drag-select overlay and resolve with
     * the chosen rectangle in global physical pixels, or null if cancelled.
     */
    selectArea(): Promise<Rect | null>;
    /** (Overlay window) Report the dragged rectangle in CSS px within the overlay. */
    complete(rect: Rect): void;
    /** (Overlay window) Cancel selection (Esc / click without a drag). */
    cancel(): void;
  };
}
