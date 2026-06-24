/**
 * IPC contract shared by the main process, preload, and renderer.
 * Keep channel names and payload types here so all three stay in sync.
 */
import type { ProjectManifest, ProjectSummary } from './project';

export interface AppInfo {
  name: string;
  version: string;
  platform: string;
  arch: string;
  electron: string;
  chrome: string;
  node: string;
}

/** IPC channel names — single source of truth. */
export const IpcChannels = {
  getAppInfo: 'app:get-info',
  getProjectsDir: 'projects:get-dir',
  chooseProjectsDir: 'projects:choose-dir',
  listRecentProjects: 'projects:list-recent',
  createProject: 'projects:create',
  openProject: 'projects:open',
} as const;

/** The typed API exposed to the renderer on `window.shotai` via contextBridge. */
export interface ShotaiApi {
  /** Runtime / app info from the main process. */
  getAppInfo(): Promise<AppInfo>;
  projects: {
    /** Absolute path to the current projects directory. */
    getDir(): Promise<string>;
    /** Open a native folder picker to change the projects dir; returns the new dir, or null if cancelled. */
    chooseDir(): Promise<string | null>;
    /** Recent projects, most-recently-touched first. */
    listRecent(): Promise<ProjectSummary[]>;
    /** Create a new, empty project folder; returns its summary. */
    create(title: string): Promise<ProjectSummary>;
    /** Read an existing project's manifest. */
    open(projectPath: string): Promise<ProjectManifest>;
  };
}
