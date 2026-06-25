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
export type Annotation =
  | RectAnnotation
  | ArrowAnnotation
  | BlurAnnotation
  | StampAnnotation
  | TextAnnotation;

/**
 * A step is either a captured screenshot ('shot') or an authored text block
 * ('text') inserted between shots (section heading / intro / note) for the SOP.
 * Defaults to 'shot' when absent (older manifests).
 */
export type StepKind = 'shot' | 'text';

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
  /** Text step: optional heading. */
  heading?: string;
  /** Text step: body (markdown). */
  body?: string;
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
  /** Per-step DISPLAY zoom in the report (default 1); does not affect export. */
  reportZoom?: number;
}

/** Editor-mutable fields of a step (sent over IPC by the inline editor). */
export type StepPatch = Partial<
  Pick<
    ProjectStep,
    | 'caption'
    | 'note'
    | 'heading'
    | 'body'
    | 'kind'
    | 'crop'
    | 'annotations'
    | 'click'
    | 'markerColor'
    | 'reportZoom'
  >
>;

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
