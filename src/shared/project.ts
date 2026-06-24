/**
 * shotAI project model — the on-disk schema for a single recording project.
 * Each project is a self-contained folder:
 *   <projects-dir>/<Project Name>/
 *     project.json   (this manifest)
 *     shots/         (original captures, step-001.png …)
 *     export/        (generated HTML / PDF / MD)
 */

export const PROJECT_SCHEMA_VERSION = 1;

export type CaptureMode = 'auto' | 'window' | 'area' | 'screen' | 'all';

/** What each capture in a session targets (chosen before recording). */
export interface CaptureTarget {
  mode: CaptureMode;
  /** 'screen' — node-screenshots Monitor id. */
  monitorId?: number;
  /** 'window' — the picked window (re-resolved each step in case it moved). */
  window?: { id: number; pid: number; title: string };
  /** 'area' — fixed rectangle in global physical pixels. */
  area?: Rect;
}

/** A pickable open window (for the 'window' chooser). */
export interface WindowInfo {
  id: number;
  pid: number;
  title: string;
  app: string;
}

/** A pickable monitor (for the 'screen' chooser). */
export interface MonitorInfo {
  id: number;
  name: string;
  width: number;
  height: number;
  isPrimary: boolean;
}

export interface Point {
  x: number;
  y: number;
}

export interface Rect {
  x: number;
  y: number;
  width: number;
  height: number;
}

export interface CapturedWindow {
  /** App / executable name, e.g. "chrome.exe". */
  app: string;
  title: string;
  pid: number;
  bounds: Rect | null;
}

export interface CapturedMonitor {
  id: number;
  bounds: Rect;
  scaleFactor: number;
}

export interface StepClick {
  /** Click position in global (virtual-desktop) coordinates. */
  global: Point;
  /** Click position relative to the captured screenshot (calibration pending). */
  image: Point;
  button: 'left' | 'right' | 'middle' | 'other';
}

/** UI element at the click point — forward-compat; populated in Phase 4. */
export interface StepElement {
  available: boolean;
  name: string | null;
  controlType: string | null;
  bounds: Rect | null;
}

export interface ProjectStep {
  id: string;
  order: number;
  /** Path to the original capture, relative to the project folder. */
  screenshot: string;
  trigger: 'click' | 'hotkey';
  click: StepClick | null;
  monitor: CapturedMonitor | null;
  window: CapturedWindow | null;
  element: StepElement;
  /** Auto-generated at capture time; user-editable. */
  caption: string;
  /** User free-text note. */
  note: string;
  /** Optional crop rect (Phase 2 editor). */
  crop: Rect | null;
  /** Vector annotations (Phase 2 editor). */
  annotations: unknown[];
}

export interface ProjectManifest {
  version: number;
  title: string;
  createdWith: 'shotAI';
  createdAt: string; // ISO 8601 (project metadata, not per-step capture data)
  updatedAt: string; // ISO 8601
  captureSettings: CaptureTarget | null;
  steps: ProjectStep[];
  sop: unknown | null; // Claude-generated SOP structure (Phase 3)
}

/** Lightweight summary for the recent-projects list (no full manifest load). */
export interface ProjectSummary {
  title: string;
  /** Absolute path to the project folder. */
  path: string;
  createdAt: string;
  updatedAt: string;
  stepCount: number;
}
