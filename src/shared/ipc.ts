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

/** IPC channel names — single source of truth. */
export const IpcChannels = {
  getAppInfo: 'app:get-info',
  getProjectsDir: 'projects:get-dir',
  chooseProjectsDir: 'projects:choose-dir',
  listRecentProjects: 'projects:list-recent',
  createProject: 'projects:create',
  openProject: 'projects:open',
  updateStep: 'projects:update-step',
  importStep: 'projects:import-step',
  deleteStep: 'projects:delete-step',
  reorderSteps: 'projects:reorder-steps',
  addTextStep: 'projects:add-text-step',
  // SOP settings + Claude key management (Phase 3)
  getSopSettings: 'settings:get-sop',
  setSopSettings: 'settings:set-sop',
  claudeKeyStatus: 'claude:key-status',
  claudeSetKey: 'claude:set-key',
  claudeClearKey: 'claude:clear-key',
  claudeTestKey: 'claude:test-key',
  claudeEstimate: 'claude:estimate',
  claudeGenerateSop: 'claude:generate-sop',
  revertSop: 'projects:revert-sop',
  // main -> renderer: SOP generation progress
  claudeSopProgress: 'claude:sop-progress',
  captureStart: 'capture:start',
  captureSingle: 'capture:single',
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
    /** Insert an empty text step at the given index. Returns the manifest. */
    addTextStep(projectPath: string, atIndex: number): Promise<ProjectManifest>;
    /** Revert Claude's inline SOP edits, restoring the pre-generation snapshot. */
    revertSop(projectPath: string): Promise<ProjectManifest>;
  };
  settings: {
    /** Current SOP generation settings (non-secret; never includes the API key). */
    getSop(): Promise<SopSettings>;
    /** Patch SOP settings; returns the full coerced settings. */
    setSop(patch: Partial<SopSettings>): Promise<SopSettings>;
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
    /** Subscribe to SOP generation progress; returns an unsubscribe function. */
    onSopProgress(cb: (p: SopProgress) => void): () => void;
  };
  capture: {
    /**
     * Start (or append to) a recording session for the given project.
     * `target` selects what each step captures; defaults to Auto (smart per-click).
     */
    start(projectPath: string, target?: CaptureTarget): Promise<CaptureState>;
    /**
     * Arm a one-shot capture: the next click is captured as a single step
     * inserted at `atIndex`, then recording auto-stops. The main window hides
     * while armed (so shotAI isn't in the shot).
     */
    captureSingle(projectPath: string, atIndex: number): Promise<CaptureState>;
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
