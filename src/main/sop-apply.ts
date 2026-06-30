// Apply / revert Claude's inline SOP edit plan against a project's steps. This is
// the second half of the SOP pipeline (the first half — assembleRequest/generateSop
// — lives in ClaudeService); co-located here so the shared `!aiInserted` base-rebuild
// rule that both halves depend on can't silently drift. Manifest IO + writeQueue
// serialization come from ProjectStore.mutate, so this module stays storage-agnostic.
import { randomUUID } from 'node:crypto';
import type { ProjectManifest, ProjectStep, SopBackup } from '../shared/project';
import type { SopEditPlan, SopTone } from '../shared/sop';
import { mutate, renumber, normalizeSteps } from './ProjectStore';

/** Build a fresh text step. `aiInserted` marks SOP-generated intro/section steps. */
function makeTextStep(heading: string, body: string, aiInserted = false): ProjectStep {
  return {
    id: randomUUID(),
    order: 0,
    kind: 'text',
    screenshot: '',
    trigger: 'hotkey',
    click: null,
    monitor: null,
    window: null,
    element: { available: false, name: null, controlType: null, bounds: null },
    caption: '',
    note: '',
    heading,
    body,
    crop: null,
    annotations: [],
    aiInserted,
  };
}

/**
 * Apply Claude's inline SOP edit plan to the project's steps: snapshot the
 * current steps+title for revert, rewrite each referenced SHOT step's
 * caption/heading/body/note, insert an optional intro + per-step section
 * headings, optionally refine the title, then renumber. Author-written text
 * steps pass through untouched; edits keyed to a non-shot step are ignored.
 * Serialized via ProjectStore.mutate. Returns the updated manifest.
 */
export function applySopEdits(
  projectPath: string,
  plan: SopEditPlan,
  provenance: { model: string; tone: SopTone },
): Promise<ProjectManifest> {
  return mutate(projectPath, (manifest) => {
    // Preserve the FIRST snapshot (the pristine pre-AI state) across regenerations
    // so "Revert Claude's edits" always restores the true original — never a prior
    // AI pass. Cleared by revertSop, so a fresh generate re-snapshots.
    const backup: SopBackup = manifest.sopBackup ?? {
      steps: structuredClone(manifest.steps),
      title: manifest.title,
      model: provenance.model,
      tone: provenance.tone,
      at: new Date().toISOString(),
    };

    // Rebuild from the non-AI base (current steps minus a prior run's inserts),
    // matching the numbering assembleRequest showed Claude. Author text steps and
    // the user's own screenshots/edits are preserved; only prior AI inserts drop.
    const base = manifest.steps.filter((s) => !s.aiInserted);
    const editByNum = new Map(plan.steps.map((e) => [e.stepNumber, e]));
    const next: ProjectStep[] = [];
    if (plan.intro && (plan.intro.heading || plan.intro.body)) {
      next.push(makeTextStep(plan.intro.heading, plan.intro.body, true));
    }
    base.forEach((step, idx) => {
      // Author text steps pass through; edits only apply to SHOT steps (so an
      // edit mis-keyed to a text step's number is simply ignored).
      if (step.kind === 'text') {
        next.push(step);
        return;
      }
      const e = editByNum.get(idx + 1);
      if (!e) {
        next.push(step);
        return;
      }
      if (e.sectionHeading) {
        next.push(makeTextStep(e.sectionHeading, e.sectionBody ?? '', true));
      }
      next.push({
        ...step,
        // Fall back to existing text if the model returns blank (don't wipe).
        caption: e.caption.trim() || step.caption,
        body: e.body.trim() || step.body || '',
        note: e.note ?? step.note,
      });
    });

    manifest.steps = next;
    renumber(manifest.steps);
    if (plan.title && plan.title.trim()) manifest.title = plan.title.trim();
    manifest.sopBackup = backup;
  });
}

/**
 * Revert Claude's inline SOP edits: restore the pre-generation snapshot
 * (steps + title) and clear it. Throws if there's nothing to revert (mutate
 * aborts the write on throw). Returns the updated manifest.
 */
export function revertSop(projectPath: string): Promise<ProjectManifest> {
  return mutate(projectPath, (manifest) => {
    if (!manifest.sopBackup) {
      throw new Error('Nothing to revert — no AI edits are recorded for this project.');
    }
    manifest.steps = normalizeSteps(manifest.sopBackup.steps);
    manifest.title = manifest.sopBackup.title;
    manifest.sopBackup = null;
  });
}
