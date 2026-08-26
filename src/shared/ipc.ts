/**
 * IPC contract shared by the main process, preload, and renderer.
 * Keep channel names and payload types here so all three stay in sync.
 */
import type {
  CalloutKind,
  CaptureTarget,
  MonitorInfo,
  ProjectManifest,
  ProjectStep,
  ProjectSummary,
  Rect,
  SopIntro,
  StepPatch,
  ThemePref,
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
/** Which credential shotAI authenticates with. */
export type AuthCredentialMode = 'federated' | 'apiKey';

/**
 * Everything the RENDERER is allowed to know about authentication (#63).
 *
 * Deliberately absent: expiresAt, scope, request ids, any token prefix, any JWT
 * claim. The renderer has no decision to make with them, and a countdown in the
 * UI is not worth leaking the token's lifetime shape. The renderer's whole
 * vocabulary is this status object plus the auth:* verbs.
 */
export interface AuthStatus {
  /** The credential that would be used right now, or 'none'. */
  mode: AuthCredentialMode | 'none';
  /** Federation is configured on this machine at all. False for every external
   *  user, and the signal to say nothing about Entra anywhere in the UI. */
  federationAvailable: boolean;
  /** A usable Entra account is cached. */
  signedIn: boolean;
  /** UPN, for "Signed in as ...". Not a credential, but it IS personal data:
   *  keep it out of logs, diagnostics and exports. */
  account: string | null;
  /** safeStorage availability, mirroring ApiKeyStatus. */
  encryptionAvailable: boolean;
  hasStoredCiphertext: boolean;
  /** Where "Request access" sends an unassigned user. Null when federation is
   *  not configured. Always openExternal-permitted: it is either this repo's
   *  issues page or an admin-delivered URL whose origin is allowlisted. */
  supportUrl: string | null;
}

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

/** Which stage of a federated connection test failed. The API-key path is a
 *  single call, so it only ever reports 'api'. */
export type ConnectionLeg = 'signIn' | 'exchange' | 'api';

/** Result of a connectivity test (expected failures are returned, not thrown). */
export interface TestConnectionResult {
  ok: boolean;
  /** Which credential was tested. A literal union rather than an import from
   *  main, so this shared contract stays main-agnostic. */
  mode?: AuthCredentialMode;
  /** Model the test validated against (on success). */
  model?: string;
  /** Friendly failure reason (on failure). */
  error?: string;
  /** Which leg failed. Present on failure; the whole point of the three-leg test,
   *  since every assertion denial from Anthropic is the same opaque 401. */
  leg?: ConnectionLeg;
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

/**
 * Per-image progress while an export encodes its screenshots. The styled HTML export
 * runs each shot through a WASM AVIF encoder (~1s each), so a long SOP takes long
 * enough that a static "Exporting…" reads as a hang.
 */
export interface ExportProgress {
  /** Images encoded so far. */
  done: number;
  /** Total images to encode. 0 when a format embeds no images. */
  total: number;
}

/**
 * Result of an update check (#54). `available: false` means BOTH "up to date" and
 * "couldn't tell" — `error` distinguishes them. A check never reports an update it
 * isn't sure about.
 */
export interface UpdateCheckResult {
  available: boolean;
  /** The newer version, no leading `v` (only when available). */
  version?: string;
  /** The GitHub release page to open (only when available). */
  url?: string;
  /** Why the check couldn't complete — offline, rate-limited, malformed reply. */
  error?: string;
}

/** Result of an export — the file that was written (revealed in the OS file manager). */
export interface ExportResult {
  format: ExportFormat;
  /** Absolute path to the written file (empty when `canceled`). */
  outputPath: string;
  /** True when the user dismissed the Save dialog — nothing was written. */
  canceled?: boolean;
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
  exportToDir: 'projects:export-to-dir',
  exportToOwnFolder: 'projects:export-to-own-folder',
  chooseExportDir: 'projects:choose-export-dir',
  revealExportDir: 'projects:reveal-export-dir',
  exportPackage: 'projects:export-package',
  importPackage: 'projects:import-package',
  archiveProject: 'projects:archive',
  unarchiveProject: 'projects:unarchive',
  /** Push (main → renderer): the project list changed (e.g. auto-archive). */
  projectsChanged: 'projects:changed',
  /** Renderer → main: entered (true) / left (false) a project — resize window (F5). */
  setDetailView: 'view:set-detail',
  // SOP settings + Claude key management (Phase 3)
  getSopSettings: 'settings:get-sop',
  setSopSettings: 'settings:set-sop',
  getRemoteVisible: 'settings:get-remote-visible',
  setRemoteVisible: 'settings:set-remote-visible',
  getCaptureScale: 'settings:get-capture-scale',
  setCaptureScale: 'settings:set-capture-scale',
  getHasSeenTour: 'settings:get-has-seen-tour',
  setHasSeenTour: 'settings:set-has-seen-tour',
  getUserName: 'settings:get-user-name',
  setUserName: 'settings:set-user-name',
  getIncludeNameInReports: 'settings:get-include-name',
  setIncludeNameInReports: 'settings:set-include-name',
  getArchiveAgeDays: 'settings:get-archive-age',
  setArchiveAgeDays: 'settings:set-archive-age',
  getTheme: 'settings:get-theme',
  setTheme: 'settings:set-theme',
  claudeKeyStatus: 'claude:key-status',
  claudeSetKey: 'claude:set-key',
  claudeClearKey: 'claude:clear-key',
  claudeTestConnection: 'claude:test-connection',
  claudeEstimate: 'claude:estimate',
  claudeGenerateSop: 'claude:generate-sop',
  claudeCancel: 'claude:cancel',
  // Entra sign-in (#63). Verbs, not values: nothing here returns a token, an
  // assertion, or any claim. Connectivity testing stays on claude:test-connection,
  // which now reports which credential it tested.
  authStatus: 'auth:status',
  authSignIn: 'auth:sign-in',
  authSignOut: 'auth:sign-out',
  revertSop: 'projects:revert-sop',
  // main -> renderer: SOP generation progress
  claudeSopProgress: 'claude:sop-progress',
  /** Per-image progress while an export encodes screenshots (see ExportProgress). */
  exportProgress: 'projects:export-progress',
  // Update check (#54) — renderer asks, and main pushes once on startup.
  updateCheck: 'update:check',
  /** Renderer pulls the startup check's result (the push often lands too early). */
  updatePending: 'update:pending',
  updateAvailable: 'update:available',
  getUpdateCheckEnabled: 'settings:get-update-check',
  setUpdateCheckEnabled: 'settings:set-update-check',
  captureStart: 'capture:start',
  captureSingle: 'capture:single',
  captureScreenshot: 'capture:screenshot',
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
  /** Tell main the user entered (true) / left (false) a project, so the window
   *  grows to the report width and shrinks back on the list (F5). */
  setDetailView(open: boolean): Promise<void>;
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
    /** Archive a project (compress in place); returns the updated summary (F2). */
    archive(projectPath: string): Promise<ProjectSummary>;
    /** Restore an archived project; returns the updated summary (F2). */
    unarchive(projectPath: string): Promise<ProjectSummary>;
    /** Subscribe to project-list changes (e.g. auto-archive); returns an unsubscribe. */
    onChanged(cb: () => void): () => void;
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
    /** Insert an empty text step at the given index (optionally a callout —
     *  note/caution/warning box or a `section` divider). Returns the manifest. */
    addTextStep(
      projectPath: string,
      atIndex: number,
      callout?: CalloutKind,
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
     * Export the project's report/SOP (single export). Shows a Save dialog
     * defaulting to the project's `export/` folder, so the user can save anywhere
     * (issue #37); returns `{ canceled: true }` if they dismiss it. Markdown is
     * saved as a self-contained folder (the .md + its images/). The renderer must
     * flatten all shot steps first (so only redacted renders are written/embedded).
     * On success the file is revealed in the OS file manager.
     */
    export(projectPath: string, format: ExportFormat): Promise<ExportResult>;
    /**
     * Per-image encode progress for the export in flight (see ExportProgress).
     * Returns an unsubscribe function. Only formats that embed images emit this.
     */
    onExportProgress(cb: (p: ExportProgress) => void): () => void;
    /**
     * Export into a specific folder (bulk export — the destination is chosen once
     * via `chooseExportDir`, then every selected project writes into it with
     * collision-safe naming). No per-file dialog.
     */
    exportToDir(projectPath: string, format: ExportFormat, dir: string): Promise<ExportResult>;
    /**
     * Export into the project's OWN `export/` folder (bulk "each to its own
     * folder"). No dialog and no per-file reveal (a bulk run would otherwise open
     * one folder per project).
     */
    exportToOwnFolder(projectPath: string, format: ExportFormat): Promise<ExportResult>;
    /**
     * Prompt for a destination folder for a bulk export. Returns the chosen
     * directory, or null if the user cancelled.
     */
    chooseExportDir(): Promise<string | null>;
    /**
     * Open the bulk-export destination folder once the whole run finishes (bulk
     * export suppresses the per-file reveal). No-op if the path isn't a directory.
     */
    revealExportDir(dir: string): Promise<void>;
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
    /** Whether shotAI is visible to remote-control sessions and screen shares. */
    getRemoteVisible(): Promise<boolean>;
    /**
     * Set remote-session visibility; returns the new value. Applies to the open
     * windows immediately, so it takes effect without a restart.
     */
    setRemoteVisible(value: boolean): Promise<boolean>;
    /** Screenshot-quality downscale factor (CAPTURE_SCALE_MIN..1). */
    getCaptureScale(): Promise<number>;
    /** Set the screenshot-quality factor (clamped); returns the stored value. */
    setCaptureScale(value: number): Promise<number>;
    /** Whether the first-run coach-mark tour has been seen/dismissed (R2). */
    getHasSeenTour(): Promise<boolean>;
    /** Persist whether the tour has been seen; false replays it. Returns the value. */
    setHasSeenTour(value: boolean): Promise<boolean>;
    /** Display name shown in reports/exports when includeNameInReports is on (F8). */
    getUserName(): Promise<string>;
    /** Persist the display name (trimmed/capped); returns the stored value. */
    setUserName(value: string): Promise<string>;
    /** Whether to append "by <name>" to the export "Created on …" line (F8). */
    getIncludeNameInReports(): Promise<boolean>;
    /** Persist the include-name opt-in; returns the new value. */
    setIncludeNameInReports(value: boolean): Promise<boolean>;
    /** Auto-archive age in days; 0 = never (F2). */
    getArchiveAgeDays(): Promise<number>;
    /** Persist the auto-archive age (0 = never; else 1..1825); returns the stored value. */
    setArchiveAgeDays(value: number): Promise<number>;
    /** UI color theme preference (F10). */
    getTheme(): Promise<ThemePref>;
    /** Persist the theme preference; returns the stored value. */
    setTheme(value: ThemePref): Promise<ThemePref>;
    /** Whether shotAI checks GitHub once a day for a newer release (#54). */
    getUpdateCheckEnabled(): Promise<boolean>;
    setUpdateCheckEnabled(value: boolean): Promise<boolean>;
  };
  updates: {
    /**
     * Check now, ignoring the once-a-day throttle (the Settings button). Never
     * throws — a failure comes back as `{available:false, error}`.
     */
    check(): Promise<UpdateCheckResult>;
    /**
     * The update found by this launch's startup check, or null. The renderer MUST
     * call this on mount: the check normally completes before the renderer has
     * subscribed, and webContents.send does not buffer, so onAvailable alone drops it.
     */
    pending(): Promise<UpdateCheckResult | null>;
    /**
     * Push: main found an update during its once-a-day startup check. Fires at most
     * once per launch, and only when an update actually exists.
     */
    onAvailable(cb: (r: UpdateCheckResult) => void): () => void;
  };
  auth: {
    /** How this machine authenticates. Never returns a token or a claim. */
    status(): Promise<AuthStatus>;
    /**
     * Interactive Microsoft sign-in in the SYSTEM browser. User-initiated only.
     * Also the correct action for "retry after IT granted me the role": an
     * interactive sign-in always returns fresh claims, whereas a silent retry
     * re-serves a cached token that predates the assignment and carries no
     * roles claim, which looks exactly like a broken rule.
     */
    signIn(): Promise<void>;
    /** Forget the cached Entra account. Leaves any stored API key untouched. */
    signOut(): Promise<void>;
  };
  claude: {
    /** Whether an API key is available and how — never returns the key itself. */
    keyStatus(): Promise<ApiKeyStatus>;
    /** Store an API key, encrypted via safeStorage. Throws if storage is unavailable. */
    setApiKey(key: string): Promise<void>;
    /** Remove the stored API key. */
    clearApiKey(): Promise<void>;
    /**
     * Validate connectivity end to end. Under federation this runs THREE legs
     * separately (silent Entra token, token exchange, then an API call) and names
     * the one that failed; on the API-key path it is a single call.
     */
    testConnection(): Promise<TestConnectionResult>;
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
     * `insertAt` (report "+ Capture" at a gap): the STARTING manifest index —
     * captured steps splice in there and each subsequent step advances the cursor,
     * instead of appending. Omit for a normal append recording.
     */
    start(
      projectPath: string,
      target?: CaptureTarget,
      opts?: { createdThisSession?: boolean; insertAt?: number },
    ): Promise<CaptureState>;
    /**
     * Arm a one-shot capture: the next click is captured as a single step
     * inserted at `atIndex`, then recording auto-stops. The main window hides
     * while armed (so shotAI isn't in the shot). (Legacy click-based path.)
     */
    captureSingle(projectPath: string, atIndex: number): Promise<CaptureState>;
    /**
     * One-shot, NO-CLICK grab for the report "+ Screenshot" insert: capture the
     * chosen surface (screen / a window / a dragged area — NOT auto) exactly once,
     * insert it at `atIndex`, and resolve with the updated manifest. No input hook,
     * no click marker, no recording HUD. The app window is hidden for the grab.
     */
    screenshot(
      projectPath: string,
      target: CaptureTarget,
      atIndex: number,
    ): Promise<ProjectManifest>;
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
