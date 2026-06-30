/**
 * shotAI project model — the on-disk schema for a single recording project.
 * Each project is a self-contained folder:
 *   <projects-dir>/<Project Name>/
 *     project.json   (this manifest)
 *     shots/         (original captures, step-001.png …)
 *     export/        (generated HTML / PDF / MD)
 */

import type { SopTone } from './sop';

export const PROJECT_SCHEMA_VERSION = 1;

export type CaptureMode = 'auto' | 'window' | 'area' | 'screen';

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

const isFiniteNum = (v: unknown): v is number =>
  typeof v === 'number' && Number.isFinite(v);

/**
 * Validate/normalize an untrusted Rect (types are erased at the IPC boundary, and
 * the area overlay reports one too). Returns null if any field is missing/non-finite.
 * Co-located with the Rect type so the two main-side validators don't drift apart.
 */
export function parseRect(value: unknown): Rect | null {
  if (!value || typeof value !== 'object') return null;
  const r = value as Record<string, unknown>;
  if (isFiniteNum(r.x) && isFiniteNum(r.y) && isFiniteNum(r.width) && isFiniteNum(r.height)) {
    return { x: r.x, y: r.y, width: r.width, height: r.height };
  }
  return null;
}

/** Validate/normalize an untrusted Point. Returns null if invalid. */
export function parsePoint(value: unknown): Point | null {
  if (!value || typeof value !== 'object') return null;
  const p = value as Record<string, unknown>;
  return isFiniteNum(p.x) && isFiniteNum(p.y) ? { x: p.x, y: p.y } : null;
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
  /** Click-marker ring radius (image px). Omitted = derive from image size. */
  radius?: number;
}

/** UI element at the click point — forward-compat; populated in Phase 4. */
export interface StepElement {
  available: boolean;
  name: string | null;
  controlType: string | null;
  bounds: Rect | null;
}

/**
 * Non-destructive vector annotations drawn over a step's screenshot. All
 * geometry is in IMAGE (screenshot) pixel coordinates — independent of how the
 * image is displayed — so annotations stay correct and re-editable. They are
 * flattened into the exported PNG; blur/redact is BAKED destructively at flatten
 * time so original pixels never leave the machine.
 */
export interface AnnotationBase {
  id: string;
}
/** Rounded rectangle outline (optionally filled). */
export interface RectAnnotation extends AnnotationBase {
  type: 'rect';
  x: number;
  y: number;
  width: number;
  height: number;
  cornerRadius: number;
  stroke: string;
  strokeWidth: number;
  fill: string | null;
}
/** Arrow from (points[0],points[1]) to (points[2],points[3]). */
export interface ArrowAnnotation extends AnnotationBase {
  type: 'arrow';
  points: [number, number, number, number];
  stroke: string;
  strokeWidth: number;
}
/** Blur/redact region — baked destructively into the flattened output. */
export interface BlurAnnotation extends AnnotationBase {
  type: 'blur';
  x: number;
  y: number;
  width: number;
  height: number;
  /** 'pixelate' = mosaic; 'solid' = opaque fill. Both destroy the pixels. */
  mode: 'pixelate' | 'solid';
  /** Mosaic block size in image px (pixelate); ignored for solid. */
  blockSize: number;
}
/** Numbered step stamp (a circle with a number). */
export interface StampAnnotation extends AnnotationBase {
  type: 'stamp';
  x: number; // center
  y: number;
  /** Displayed number — defaults to the step order but is independent. */
  n: number;
  radius: number;
  fill: string;
  textColor: string;
}
/** Free text label. */
export interface TextAnnotation extends AnnotationBase {
  type: 'text';
  x: number;
  y: number;
  text: string;
  fontSize: number;
  fill: string;
}
/**
 * A click-register ring — the same visual as a step's baked click marker, but as
 * a movable annotation. Created when two steps are MERGED (the discarded step's
 * click is mapped onto the kept screenshot as a second marker) and freely
 * placeable in the editor. Radius is derived from the image size (mirrors the
 * click marker), so only the center + color are stored.
 */
export interface MarkerAnnotation extends AnnotationBase {
  type: 'marker';
  x: number; // center, image px
  y: number;
  color: string;
  /** Ring radius (image px). Omitted = derive from image size (legacy markers). */
  radius?: number;
}
export type Annotation =
  | RectAnnotation
  | ArrowAnnotation
  | BlurAnnotation
  | StampAnnotation
  | TextAnnotation
  | MarkerAnnotation;

/**
 * A step is either a captured screenshot ('shot') or an authored text block
 * ('text') inserted between shots (section heading / intro / note) for the SOP.
 * Defaults to 'shot' when absent (older manifests).
 */
export type StepKind = 'shot' | 'text';

/** A pre-formatted callout style for a text step: note (green), caution
 *  (yellow), warning (red). Absent = a plain text step. */
export type CalloutKind = 'note' | 'caution' | 'warning';

export interface ProjectStep {
  id: string;
  order: number;
  /** 'shot' (default) or 'text'. */
  kind?: StepKind;
  /** Path to the original capture, relative to the project folder ('' for text steps). */
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
  /** Optional heading. Text steps and (Phase 3) screenshot steps both use it. */
  heading?: string;
  /** Optional body/subtext (markdown). Text steps and screenshot steps both use it. */
  body?: string;
  /** When set, a text step renders as a colored callout box (note/caution/warning). */
  callout?: CalloutKind;
  /**
   * True for a text step that Claude's SOP generation inserted (intro / section
   * heading). Stripped + regenerated on the next run so they don't accumulate;
   * author-written text steps (no flag) are always preserved.
   */
  aiInserted?: boolean;
  /** Optional crop rect, in image px (Phase 2 editor). */
  crop: Rect | null;
  /** Click-register marker color (editor-set); defaults to the accent if unset. */
  markerColor?: string;
  /** Non-destructive vector annotations (Phase 2 editor). */
  annotations: Annotation[];
  /**
   * Path (relative to the project folder) to the flattened render — original
   * cropped + annotations drawn + redaction BAKED in. Written by the editor; the
   * report/export prefer it over the raw screenshot. null until first edited.
   */
  flattened?: string | null;
  /** Bumped each time `flattened` is rewritten — used to cache-bust the <img>. */
  renderRev?: number;
  /**
   * True when the current `flattened` render was produced by the marker-aware
   * flatten — i.e. any click marker is BAKED into the pixels (so Claude's vision
   * and exports see it, and the report must NOT draw its CSS overlay on top).
   * Falsy on pre-marker renders + un-flattened steps → report keeps the overlay
   * and `ensureFlattened` re-bakes the render so the marker lands in it.
   */
  markerBaked?: boolean;
  /** Per-step DISPLAY zoom in the report (default 1); does not affect export. */
  reportZoom?: number;
  /** Report pan as a fraction 0..1 of the pannable range (0.5 = centered). */
  reportPanX?: number;
  reportPanY?: number;
}

/** Editor-mutable fields of a step (sent over IPC by the inline editor). */
export type StepPatch = Partial<
  Pick<
    ProjectStep,
    | 'caption'
    | 'note'
    | 'heading'
    | 'body'
    | 'callout'
    | 'kind'
    | 'crop'
    | 'annotations'
    | 'click'
    | 'markerColor'
    | 'markerBaked'
    | 'reportZoom'
    | 'reportPanX'
    | 'reportPanY'
  >
>;

/**
 * Pre-generation snapshot for one-click revert of Claude's inline SOP edits.
 * Captured right before an edit plan is applied; cleared on revert.
 */
export interface SopBackup {
  steps: ProjectStep[];
  title: string;
  /** Model + tone the (subsequent) generation used — for the "Revert" provenance label. */
  model: string;
  tone: SopTone;
  at: string; // ISO 8601
}

export interface ProjectManifest {
  version: number;
  /**
   * Stable random identity (uuid), assigned at creation and back-filled on open
   * for older projects. Decouples identity from the (mutable, possibly duplicate)
   * title and folder name — so two projects can share a display name.
   */
  id: string;
  title: string;
  createdWith: 'shotAI';
  createdAt: string; // ISO 8601 (project metadata, not per-step capture data)
  updatedAt: string; // ISO 8601
  captureSettings: CaptureTarget | null;
  steps: ProjectStep[];
  /** Pre-edit snapshot enabling revert of Claude's inline SOP edits (Phase 3). */
  sopBackup: SopBackup | null;
}

/** Lightweight summary for the project list (no full manifest load). */
export interface ProjectSummary {
  /** Stable project id (may be '' for an older project not yet opened/migrated). */
  id: string;
  title: string;
  /** Absolute path to the project folder. */
  path: string;
  createdAt: string;
  updatedAt: string;
  stepCount: number;
}
