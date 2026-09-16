// Apply / revert Claude's inline SOP edit plan against a project's steps. This is
// the second half of the SOP pipeline (the first half — assembleRequest/generateSop
// — lives in ClaudeService); co-located here so the shared `!aiInserted` base-rebuild
// rule that both halves depend on can't silently drift. Manifest IO + writeQueue
// serialization come from ProjectStore.mutate, so this module stays storage-agnostic.
import { randomUUID } from 'node:crypto';
import type { CalloutKind, ProjectManifest, ProjectStep, SopBackup } from '../shared/project';
import type { SopEditPlan, SopTone } from '../shared/sop';
import { mutate, renumber, normalizeSteps } from './project-store';
import { mergeAiStepText } from './sop-input';

/**
 * A generation that parsed, was applied to nothing, and would have reported
 * success (#108).
 *
 * Carries the two causes SEPARATELY because they need different fixes and the
 * user-facing message cannot tell them apart. "Wrote no step content" is the
 * model under-producing; "wrote for steps that do not exist" is it numbering
 * against the wrong scheme, which the prompt has to address. Only NUMBERS are
 * carried — never captions or bodies, which would put user content in a log.
 */
export class SopNoLandingError extends Error {
  constructor(
    readonly sentNumbers: number[],
    readonly realNumbers: number[],
    /** True when the plan had real text, so the failure is numbering, not emptiness. */
    readonly wroteContent: boolean,
  ) {
    super(
      wroteContent
        ? 'the plan wrote for steps that do not exist'
        : 'the plan wrote no step content',
    );
    this.name = 'SopNoLandingError';
  }
}

/** Build a fresh text step. `aiInserted` marks SOP-generated intro/section steps;
 *  `callout` optionally styles it (e.g. a non-counted `section` divider). */
function makeTextStep(
  heading: string,
  body: string,
  aiInserted = false,
  callout?: CalloutKind,
): ProjectStep {
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
    heading,
    body,
    ...(callout ? { callout } : {}),
    crop: null,
    annotations: [],
    aiInserted,
  };
}

/**
 * Apply Claude's inline SOP edit plan to the project's steps: snapshot the
 * current steps+title for revert, rewrite each referenced SHOT step's
 * caption/heading/body, insert an optional intro + per-step section
 * headings, optionally refine the title, then renumber. Author-written text
 * steps pass through untouched; edits keyed to a non-shot step are ignored.
 * Serialized via ProjectStore.mutate.
 *
 * Returns the updated manifest AND the number of edits that actually landed on a
 * real screenshot (#108). The caller needs that count: the old log line reported
 * `plan.steps.length`, which is edits REQUESTED, so a generation that changed
 * nothing still read as success.
 *
 * Throws SopNoLandingError when a non-empty plan lands nothing, leaving the
 * project untouched on disk.
 */
export async function applySopEdits(
  projectPath: string,
  plan: SopEditPlan,
  provenance: { model: string; tone: SopTone },
): Promise<{ manifest: ProjectManifest; landed: number }> {
  let applied = 0;
  const manifest = await mutate(projectPath, (manifest) => {
    // Preserve the FIRST snapshot (the pristine pre-AI state) across regenerations
    // so "Revert Claude's edits" always restores the true original — never a prior
    // AI pass. Cleared by revertSop, so a fresh generate re-snapshots.
    const backup: SopBackup = manifest.sopBackup ?? {
      steps: structuredClone(manifest.steps),
      title: manifest.title,
      intro: manifest.intro,
      ...(manifest.introEditedByUser === true ? { introEditedByUser: true } : {}),
      model: provenance.model,
      tone: provenance.tone,
      at: new Date().toISOString(),
    };

    // The overview is a PREAMBLE, not a step (E4): store it on the manifest and
    // render it above the steps. A fresh generate replaces it (or clears it if
    // the model returned none).
    const aiIntro =
      plan.intro && (plan.intro.heading || plan.intro.body) ? plan.intro : null;
    if (manifest.introEditedByUser === true) {
      // #64 gentle massage: an overview the AUTHOR wrote is reworded, never
      // replaced. The heading is PINNED here rather than merely requested in the
      // prompt, because an instruction the model can quietly ignore is not a
      // guarantee — and the heading is precisely what a regeneration was seen to
      // overwrite.
      const authorHeading = (manifest.intro?.heading ?? '').trim();
      // These two fallbacks ARE the no-delete guarantee: a model that returns no
      // intro leaves the author's own heading and body in place, where the old
      // unconditional assignment wiped the overview entirely. The ternary’s null
      // arm is unreachable in practice — setProjectIntro only flags a NON-empty
      // intro — but it keeps the types honest for a hand-edited manifest.
      const body = (aiIntro?.body ?? '').trim() || (manifest.intro?.body ?? '');
      const heading = authorHeading || (aiIntro?.heading ?? '');
      manifest.intro = heading || body ? { heading, body } : null;
      // The flag SURVIVES on purpose: the author's heading is still in there
      // verbatim, so the next run has to protect it too. Contrast mergeAiStepText,
      // which DROPS captionEditedByUser — there Claude's caption replaces the
      // human text outright, so the flag must not outlive it.
    } else {
      manifest.intro = aiIntro
        ? { heading: aiIntro.heading, body: aiIntro.body }
        : null;
    }

    // Rebuild from the non-AI base (current steps minus a prior run's inserts),
    // matching the numbering assembleRequest showed Claude. Author text steps and
    // the user's own screenshots/edits are preserved; only prior AI inserts drop.
    const base = manifest.steps.filter((s) => !s.aiInserted);
    const editByNum = new Map(plan.steps.map((e) => [e.stepNumber, e]));
    const next: ProjectStep[] = [];
    // How many edits actually reach a real screenshot. Counted in THIS loop on
    // purpose: the numbering rule (1-based index within `base`, text steps
    // counted but not eligible) already exists here, and a second copy of it
    // elsewhere is the drift this file exists to avoid.
    let landed = 0;
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
      // Count what LANDS, not what was asked for (#108). The distinction is the
      // whole guard: a plan full of well-written steps whose stepNumbers match
      // no screenshot passes any check that inspects plan.steps, applies to
      // nothing, and leaves the user a fresh title and overview with every
      // caption unchanged and no error. macOS shipped exactly that guard first
      // (its #114) and had to redo it.
      //
      // A section-only entry deliberately does NOT count, even though it does
      // mutate the manifest below: a run that rewrote no step text is unusable
      // however many phase dividers it added.
      if (e.caption.trim() || e.body.trim()) landed += 1;
      if (e.sectionHeading) {
        // A section is a NON-counted phase divider, not a numbered step — so
        // Claude's phase headings stop injecting redundant numbered steps.
        next.push(makeTextStep(e.sectionHeading, e.sectionBody ?? '', true, 'section'));
      }
      const merged = mergeAiStepText(step, e.caption, e.body);
      next.push(merged);
    });

    // Fail BEFORE any of the assignments below, so `mutate` skips writeManifest
    // and the project keeps its title, overview, steps and sopBackup exactly as
    // they were (project-store.ts:658-664). Applying and then reverting would be
    // wrong: revertSop restores the PRISTINE snapshot and nulls sopBackup, which
    // would discard a previous good generation.
    //
    // Guarded on a non-empty plan so the existing callers that apply a
    // title/overview-only plan keep working; `steps.min(1)` in the schema makes
    // an empty plan unreachable from a real generation anyway.
    if (plan.steps.length > 0 && landed === 0) {
      throw new SopNoLandingError(
        plan.steps.map((e) => e.stepNumber),
        base.flatMap((s, idx) => (s.kind === 'text' ? [] : [idx + 1])),
        plan.steps.some((e) => e.caption.trim() || e.body.trim()),
      );
    }

    manifest.steps = next;
    renumber(manifest.steps);
    if (plan.title && plan.title.trim()) manifest.title = plan.title.trim();
    manifest.sopBackup = backup;
    applied = landed;
  });
  return { manifest, landed: applied };
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
    manifest.intro = manifest.sopBackup.intro;
    // Restore WHY it was protected, not just the text (#64). Dropping the flag
    // here would leave a reverted author overview freely rewritable next run.
    if (manifest.sopBackup.introEditedByUser === true) manifest.introEditedByUser = true;
    else delete manifest.introEditedByUser;
    manifest.sopBackup = null;
  });
}
