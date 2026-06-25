// Zustand store for the project-detail / editor view. The home view (recents +
// capture-mode picker) stays in App.tsx; this owns the currently-open project.
import { create } from 'zustand';
import type { ProjectManifest, ProjectStep } from '../../shared/project';

interface ProjectState {
  /** Opaque id used to build shot:// image URLs (never a filesystem path). */
  projectId: string | null;
  /** Absolute project folder path (for capture/resume + mutations). */
  projectPath: string | null;
  title: string;
  steps: ProjectStep[];
  /** Manifest updatedAt — also used to cache-bust re-saved flattened renders. */
  updatedAt: string;
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
  updatedAt: '',
  selectedStepId: null,
  loading: false,
  error: null,

  open: async (projectPath) => {
    set({ loading: true, error: null });
    try {
      const { projectId, manifest } =
        await window.shotai.projects.open(projectPath);
      set({
        projectId,
        projectPath,
        title: manifest.title,
        steps: manifest.steps,
        updatedAt: manifest.updatedAt,
        selectedStepId: null,
        loading: false,
      });
    } catch (e) {
      set({
        loading: false,
        error: e instanceof Error ? e.message : String(e),
      });
    }
  },

  applyOpened: (projectId, projectPath, manifest) =>
    set({
      projectId,
      projectPath,
      title: manifest.title,
      steps: manifest.steps,
      updatedAt: manifest.updatedAt,
      selectedStepId: null,
      loading: false,
      error: null,
    }),

  close: () =>
    set({
      projectId: null,
      projectPath: null,
      title: '',
      steps: [],
      updatedAt: '',
      selectedStepId: null,
      error: null,
    }),

  selectStep: (id) => set({ selectedStepId: id }),

  applyManifest: (manifest) =>
    set({
      steps: manifest.steps,
      title: manifest.title,
      updatedAt: manifest.updatedAt,
    }),
}));

/** Build a shot:// URL for a project-relative file (e.g. "shots/step-0001.png"). */
export function shotUrl(projectId: string, relPath: string): string {
  const encoded = relPath.split('/').map(encodeURIComponent).join('/');
  return `shot://${projectId}/${encoded}`;
}
