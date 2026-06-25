// Zustand store for the project-detail / editor view. The home view (recents +
// capture-mode picker) stays in App.tsx; this owns the currently-open project.
import { create } from 'zustand';
import type { ProjectStep } from '../../shared/project';

interface ProjectState {
  /** Opaque id used to build shot:// image URLs (never a filesystem path). */
  projectId: string | null;
  /** Absolute project folder path (for capture/resume + mutations). */
  projectPath: string | null;
  title: string;
  steps: ProjectStep[];
  selectedStepId: string | null;
  loading: boolean;
  error: string | null;

  /** Open a project into the detail view. */
  open: (projectPath: string) => Promise<void>;
  /** Return to the home view. */
  close: () => void;
  selectStep: (id: string | null) => void;
}

export const useProjectStore = create<ProjectState>((set) => ({
  projectId: null,
  projectPath: null,
  title: '',
  steps: [],
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

  close: () =>
    set({
      projectId: null,
      projectPath: null,
      title: '',
      steps: [],
      selectedStepId: null,
      error: null,
    }),

  selectStep: (id) => set({ selectedStepId: id }),
}));

/** Build a shot:// URL for a project-relative file (e.g. "shots/step-0001.png"). */
export function shotUrl(projectId: string, relPath: string): string {
  const encoded = relPath.split('/').map(encodeURIComponent).join('/');
  return `shot://${projectId}/${encoded}`;
}
