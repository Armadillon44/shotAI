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
  SopIntro,
  StepPatch,
  WindowInfo,
} from './project';
import type { SopModelId, SopSettings } from './sop';

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
  /**
   * True when a Discard would delete the ENTIRE project, not just this session's
   * steps — i.e. the project was created for this session and had no prior steps.
   * The toolbar uses it to word the Discard confirmation honestly (R5).
   */
  willDeleteProjectOnDiscard: boolean;
}

/** How an Anthropic API key is available (if at all). */
export type ApiKeySource = 'stored' | 'env' | 'none';

/** Whether a key is available and how — the key itself is NEVER sent to the renderer. */
export interface ApiKeyStatus {
  hasKey: boolean;
  source: ApiKeySource;
  /** safeStorage availability — when false, a key cannot be saved on this system. */
  encryptionAvailable: boolean;
  /**
   * Whether encrypted ciphertext exists on disk, even if it can't currently be
   * decrypted (e.g. moved machine / OS keychain change). Lets the UI offer Clear.
   */
  hasStoredCiphertext: boolean;
}

/** Result of a connectivity test (expected failures are returned, not thrown). */
export interface TestKeyResult {
  ok: boolean;
  /** Model the test validated against (on success). */
  model?: string;
  /** Friendly failure reason (on failure). */
  error?: string;
}

/** Pre-send cost estimate for SOP generation (shown on the review screen). */
export interface SopEstimate {
  /** Input tokens for the assembled request (exact, via count_tokens). */
  inputTokens: number;
  model: SopModelId;
  /** Estimated total USD (exact input + a rough output allowance). */
  estCostUsd: number;
}

/** Progress event emitted during SOP generation (main → renderer). */
export interface SopProgress {
  stage: 'preparing' | 'thinking' | 'writing' | 'done';
  /** Output characters streamed so far (during 'writing'). */
  chars?: number;
}

/** Output format for an exported report/SOP. */
export type ExportFormat = 'html' | 'html-plain' | 'pdf' | 'markdown' | 'docx' | 'pptx';

/** Result of an export — the file that was written (revealed in the OS file manager). */
export interface ExportResult {
  format: ExportFormat;
  /** Absolute path to the written file. */
  outputPath: string;
}

/** Result of a shareable-package export (a .zip that round-trips back into shotAI). */
export interface PackageResult {
  outputPath: string;
  /** Whether original (un-redacted) screenshots were included. */
  includeOriginals: boolean;
}

/** IPC channel names — single source of truth. */
export const IpcChannels = {
  getAppInfo: 'app:get-info',
  openExternal: 'shell:open-external',
  getProjectsDir: 'projects:get-dir',
  chooseProjectsDir: 'projects:choose-dir',
  listRecentProjects: 'projects:list-recent',
  listProjects: 'projects:list',
  createProject: 'projects:create',
  renameProject: 'projects:rename',
  deleteProject: 'projects:delete',
  revealProject: 'projects:reveal',
  openProject: 'projects:open',
  updateStep: 'projects:update-step',
  importStep: 'projects:import-step',
  deleteStep: 'projects:delete-step',
  reorderSteps: 'projects:reorder-steps',
  mergeSteps: 'projects:merge-steps',
  addTextStep: 'projects:add-text-step',
  setProjectIntro: 'projects:set-intro',
  redactScan: 'projects:redact-scan',
  exportProject: 'projects:export',
  exportPackage: 'projects:export-package',
  importPackage: 'projects:import-package',
  // SOP settings + Claude key management (Phase 3)
  getSopSettings: 'settings:get-sop',
  setSopSettings: 'settings:set-sop',
  getCaptureNoHide: 'settings:get-capture-no-hide',
  setCaptureNoHide: 'settings:set-capture-no-hide',
  getCaptureScale: 'settings:get-capture-scale',
  setCaptureScale: 'settings:set-capture-scale',
  getHasSeenTour: 'settings:get-has-seen-tour',
  setHasSeenTour: 'settings:set-has-seen-tour',
  claudeKeyStatus: 'claude:key-status',
  claudeSetKey: 'claude:set-key',
  claudeClearKey: 'claude:clear-key',
  claudeTestKey: 'claude:test-key',
  claudeEstimate: 'claude:estimate',
  claudeGenerateSop: 'claude:generate-sop',
  claudeCancel: 'claude:cancel',
  revertSop: 'projects:revert-sop',
  // main -> renderer: SOP generation progress
  claudeSopProgress: 'claude:sop-progress',
  captureStart: 'capture:start',
  captureSingle: 'capture:single',
  capturePause: 'capture:pause',
  captureResume: 'capture:resume',
  captureStop: 'capture:stop',
  captureDiscard: 'capture:discard',
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
  // Application menu → renderer
  openSettings: 'menu:open-settings',
  menuImportProject: 'menu:import-project',
} as const;

/** The typed API exposed to the renderer on `window.shotai` via contextBridge. */
export interface ShotaiApi {
  /** Runtime / app info from the main process. */
  getAppInfo(): Promise<AppInfo>;
  /**
   * Open a trusted external URL in the user's default browser. Main allows only
   * https URLs on an anthropic.com host allowlist; returns false if refused.
   */
  openExternal(url: string): Promise<boolean>;
  /** Fires when the application menu's File → Settings is chosen. Returns an
   *  unsubscribe fn. */
  onOpenSettings(cb: () => void): () => void;
  /** Fires when the application menu's File → Import Project… is chosen. Returns
   *  an unsubscribe fn. */
  onImportProject(cb: () => void): () => void;
  projects: {
    getDir(): Promise<string>;
    chooseDir(): Promise<string | null>;
    listRecent(): Promise<ProjectSummary[]>;
    /** All projects in the current projects folder (home screen sorts them). */
    list(): Promise<ProjectSummary[]>;
    /** Create a project; an empty title gets a timestamped default name. */
    create(title: string): Promise<ProjectSummary>;
    /** Rename a project (title only; folder/path unchanged). */
    rename(projectPath: string, title: string): Promise<ProjectSummary>;
    /** Delete a project's folder and drop it from recents. */
    delete(projectPath: string): Promise<void>;
    /** Reveal a project's folder in the OS file manager (Explorer/Finder). */
    reveal(projectPath: string): Promise<void>;
    /**
     * Open a project: returns its manifest plus an opaque `projectId` the
     * renderer uses to build shot:// image URLs (never a filesystem path).
     */
    open(projectPath: string): Promise<{ projectId: string; manifest: ProjectManifest }>;
    /**
     * Apply an editor patch to one step. `flattenedPng` (the baked render with
     * redaction) is optional; when given it's written to the project's
     * render-cache and referenced by step.flattened. Returns the updated manifest.
     */
    updateStep(
      projectPath: string,
      stepId: string,
      patch: StepPatch,
      flattenedPng?: Uint8Array | null,
    ): Promise<ProjectManifest>;
    /**
     * Import a user-supplied PNG/JPEG as a new step. Main validates the bytes are
     * actually an image (magic bytes). Inserts at `atIndex` (omitted → append).
     * Returns the updated manifest.
     */
    importStep(
      projectPath: string,
      bytes: Uint8Array,
      atIndex?: number,
    ): Promise<ProjectManifest>;
    /** Delete a step (leaves its files on disk); renumbers. Returns the manifest. */
    deleteStep(projectPath: string, stepId: string): Promise<ProjectManifest>;
    /** Reorder steps to match the given id order; renumbers. Returns the manifest. */
    reorderSteps(projectPath: string, orderedIds: string[]): Promise<ProjectManifest>;
    /**
     * Merge two steps into one: apply `patch` (+ optional re-baked render) to the
     * KEPT step, then delete the DROPPED step, then renumber. Used to fold a
     * right-click step into its menu-selection step. Returns the updated manifest.
     */
    mergeSteps(
      projectPath: string,
      keepId: string,
      dropId: string,
      patch: StepPatch,
      flattenedPng?: Uint8Array | null,
    ): Promise<ProjectManifest>;
    /** Insert an empty text step at the given index. Returns the manifest. */
    addTextStep(
      projectPath: string,
      atIndex: number,
      callout?: 'note' | 'caution' | 'warning',
    ): Promise<ProjectManifest>;
    /**
     * Auto-redaction pre-scan: OCR a step's screenshot locally and return
     * image-px rects over likely-sensitive text (SSN / credit-card / API key).
     * Best-effort — returns [] if nothing found or OCR is unavailable. The
     * renderer turns these into editable blur regions for the user to review.
     */
    redactScan(projectPath: string, stepId: string): Promise<Rect[]>;
    /** Set (or clear, with null) the SOP overview preamble. Returns the manifest. */
    setIntro(projectPath: string, intro: SopIntro | null): Promise<ProjectManifest>;
    /** Revert Claude's inline SOP edits, restoring the pre-generation snapshot. */
    revertSop(projectPath: string): Promise<ProjectManifest>;
    /**
     * Export the project's report/SOP to a self-contained file under `export/`.
     * The renderer must flatten all shot steps first (so only redacted renders
     * are written/embedded). On success the file is revealed in the OS file
     * manager. Returns the written file path.
     */
    export(projectPath: string, format: ExportFormat): Promise<ExportResult>;
    /**
     * Export a shareable .zip package that another shotAI user can import and
     * edit. `includeOriginals` false (default) ships only redaction-baked renders
     * (redactions permanent); true ships the un-redacted originals for full
     * re-editing (recoverable — opt-in, with a warning in the UI).
     */
    exportPackage(projectPath: string, includeOriginals: boolean): Promise<PackageResult>;
    /**
     * Import a project package (.zip): opens a file picker, validates + extracts
     * it into a NEW project, and returns its summary — or null if the user
     * cancels the picker.
     */
    importPackage(): Promise<ProjectSummary | null>;
  };
  settings: {
    /** Current SOP generation settings (non-secret; never includes the API key). */
    getSop(): Promise<SopSettings>;
    /** Patch SOP settings; returns the full coerced settings. */
    setSop(patch: Partial<SopSettings>): Promise<SopSettings>;
    /** Whether the app window stays visible during capture (demo/screen-share). */
    getCaptureNoHide(): Promise<boolean>;
    /** Set the capture-no-hide (demo) mode; returns the new value. */
    setCaptureNoHide(value: boolean): Promise<boolean>;
    /** Screenshot-quality downscale factor (CAPTURE_SCALE_MIN..1). */
    getCaptureScale(): Promise<number>;
    /** Set the screenshot-quality factor (clamped); returns the stored value. */
    setCaptureScale(value: number): Promise<number>;
    /** Whether the first-run coach-mark tour has been seen/dismissed (R2). */
    getHasSeenTour(): Promise<boolean>;
    /** Persist whether the tour has been seen; false replays it. Returns the value. */
    setHasSeenTour(value: boolean): Promise<boolean>;
  };
  claude: {
    /** Whether an API key is available and how — never returns the key itself. */
    keyStatus(): Promise<ApiKeyStatus>;
    /** Store an API key, encrypted via safeStorage. Throws if storage is unavailable. */
    setApiKey(key: string): Promise<void>;
    /** Remove the stored API key. */
    clearApiKey(): Promise<void>;
    /** Validate connectivity using the stored/env key + the selected model. */
    testKey(): Promise<TestKeyResult>;
    /** Estimate the token count + cost of generating the SOP for this project. */
    estimate(projectPath: string): Promise<SopEstimate>;
    /**
     * Generate the SOP (vision + structured output) and persist it. The renderer
     * must flatten all shot steps first (so only redacted renders are sent).
     * Returns the updated manifest. Progress arrives via onSopProgress.
     */
    generateSop(projectPath: string): Promise<ProjectManifest>;
    /** Abort an in-flight estimate/generateSop (fire-and-forget). */
    cancel(): void;
    /** Subscribe to SOP generation progress; returns an unsubscribe function. */
    onSopProgress(cb: (p: SopProgress) => void): () => void;
  };
  capture: {
    /**
     * Start (or append to) a recording session for the given project.
     * `target` selects what each step captures; defaults to Auto (smart per-click).
     * `createdThisSession` marks a project freshly created for this session, so a
     * Discard deletes the whole project (vs. only this session's steps).
     */
    start(
      projectPath: string,
      target?: CaptureTarget,
      opts?: { createdThisSession?: boolean },
    ): Promise<CaptureState>;
    /**
     * Arm a one-shot capture: the next click is captured as a single step
     * inserted at `atIndex`, then recording auto-stops. The main window hides
     * while armed (so shotAI isn't in the shot).
     */
    captureSingle(projectPath: string, atIndex: number): Promise<CaptureState>;
    pause(): Promise<CaptureState>;
    resume(): Promise<CaptureState>;
    stop(): Promise<CaptureState>;
    /**
     * Discard the active session: stop capture and remove this session's work —
     * the whole project if it was created this session, else only the steps/shots
     * added during the session. Returns whether the project folder was deleted.
     */
    discard(): Promise<{ state: CaptureState; projectDeleted: boolean }>;
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
