// Zustand store for the project-detail / editor view. The home view (recents +
// capture-mode picker) stays in App.tsx; this owns the currently-open project.
import { create } from 'zustand';
import type {
  ProjectManifest,
  ProjectStep,
  SopBackup,
  SopIntro,
} from '../../shared/project';

interface ProjectState {
  /** Opaque id used to build shot:// image URLs (never a filesystem path). */
  projectId: string | null;
  /** Absolute project folder path (for capture/resume + mutations). */
  projectPath: string | null;
  title: string;
  steps: ProjectStep[];
  /** SOP overview preamble (rendered above the steps, not as a step). */
  intro: SopIntro | null;
  /** Pre-edit snapshot when Claude's inline SOP edits are applied; enables revert. */
  sopBackup: SopBackup | null;
  /** Manifest updatedAt — also used to cache-bust re-saved flattened renders. */
  updatedAt: string;
  /**
   * Bumped on every manifest replacement (open / applyOpened / applyManifest).
   * The report keys a reconciliation effect on it to drop stale inline-edit
   * latches when steps are swapped out from under it (B4) — the edited step's id
   * can survive SOP-gen/delete unchanged, so an id-vanished check isn't enough.
   */
  manifestRev: number;
  selectedStepId: string | null;
  loading: boolean;
  error: string | null;

  /** Open a project into the detail view. */
  open: (projectPath: string) => Promise<void>;
  /**
   * Adopt a project the caller has already opened (manifest + id in hand) into
   * the detail store, without a second IPC round-trip. Used by the capture flow
   * so the project is "open" when recording stops — and so a fallible second
   * open can't leave the store half-set.
   */
  applyOpened: (
    projectId: string,
    projectPath: string,
    manifest: ProjectManifest,
  ) => void;
  /** Return to the home view. */
  close: () => void;
  selectStep: (id: string | null) => void;
  /** Re-sync from a manifest returned by a mutation (e.g. an editor save). */
  applyManifest: (manifest: ProjectManifest) => void;
}

export const useProjectStore = create<ProjectState>((set) => ({
  projectId: null,
  projectPath: null,
  title: '',
  steps: [],
  intro: null,
  sopBackup: null,
  updatedAt: '',
  manifestRev: 0,
  selectedStepId: null,
  loading: false,
  error: null,

  open: async (projectPath) => {
    set({ loading: true, error: null });
    try {
      const { projectId, manifest } =
        await window.shotai.projects.open(projectPath);
      set((s) => ({
        projectId,
        projectPath,
        title: manifest.title,
        steps: manifest.steps,
        intro: manifest.intro,
        sopBackup: manifest.sopBackup,
        updatedAt: manifest.updatedAt,
        manifestRev: s.manifestRev + 1,
        selectedStepId: null,
        loading: false,
      }));
    } catch (e) {
      // Failed open (e.g. the project was deleted — a discarded brand-new
      // project) must NOT leave a stale project rendered: clear it so the view
      // falls back to Home instead of a phantom detail view with dead shot:// imgs.
      const msg = e instanceof Error ? e.message : String(e);
      // A project that's simply gone (discarded) isn't an error worth a banner.
      const gone = /ENOENT|no such file|not found/i.test(msg);
      set({
        projectId: null,
        projectPath: null,
        title: '',
        steps: [],
        intro: null,
        sopBackup: null,
        updatedAt: '',
        selectedStepId: null,
        loading: false,
        error: gone ? null : msg,
      });
    }
  },

  applyOpened: (projectId, projectPath, manifest) =>
    set((s) => ({
      projectId,
      projectPath,
      title: manifest.title,
      steps: manifest.steps,
      intro: manifest.intro,
      sopBackup: manifest.sopBackup,
      updatedAt: manifest.updatedAt,
      manifestRev: s.manifestRev + 1,
      selectedStepId: null,
      loading: false,
      error: null,
    })),

  close: () =>
    set({
      projectId: null,
      projectPath: null,
      title: '',
      steps: [],
      intro: null,
      sopBackup: null,
      updatedAt: '',
      selectedStepId: null,
      error: null,
    }),

  selectStep: (id) => set({ selectedStepId: id }),

  applyManifest: (manifest) =>
    set((s) => ({
      steps: manifest.steps,
      title: manifest.title,
      intro: manifest.intro,
      sopBackup: manifest.sopBackup,
      updatedAt: manifest.updatedAt,
      manifestRev: s.manifestRev + 1,
    })),
}));

/** Build a shot:// URL for a project-relative file (e.g. "shots/step-0001.png"). */
export function shotUrl(projectId: string, relPath: string): string {
  const encoded = relPath.split('/').map(encodeURIComponent).join('/');
  return `shot://${projectId}/${encoded}`;
}
