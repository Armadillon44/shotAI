/**
 * shotAI project model — the on-disk schema for a single recording project.
 * Each project is a self-contained folder:
 *   <projects-dir>/<Project Name>/
 *     project.json   (this manifest)
 *     shots/         (original captures — added in Phase 1)
 *     export/        (generated HTML / PDF / MD)
 */

export const PROJECT_SCHEMA_VERSION = 1;

export type CaptureRegionMode = 'window' | 'area' | 'screen' | 'all';

export interface CaptureSettings {
  region: CaptureRegionMode;
  /** Resolved target (window id / rect / display id) — filled in Phase 1. */
  target: unknown | null;
}

/** A single captured step. Fleshed out in Phase 1; kept minimal for now. */
export interface ProjectStep {
  id: string;
  order: number;
  screenshot: string;
  caption: string;
  note: string;
}

export interface ProjectManifest {
  version: number;
  title: string;
  createdWith: 'shotAI';
  createdAt: string; // ISO 8601 (project metadata, not per-step capture data)
  updatedAt: string; // ISO 8601
  captureSettings: CaptureSettings | null;
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
