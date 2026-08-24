// Pure helpers for the SOP *input* assembly — what Claude is actually shown (#62).
//
// Deliberately electron-free (imports only shared types) so the invariants below
// are unit-testable under plain node, same reasoning as export-geometry.ts and
// export-css.ts. The assembly itself stays in claude-service.ts; only the rules
// that are easy to get quietly wrong live here.
//
// PARITY: every rule here mirrors the macOS implementation in
// Packages/SOPKit/Sources/SOPKit/RequestAssembler.swift (their issue #73, ours #62).
// The two platforms share a byte-compatible project.json and projects round-trip,
// so these must not diverge.
import { isCalloutKind } from '../shared/project';
import type { CalloutKind, ProjectManifest, ProjectStep, SopIntro } from '../shared/project';

/** How each author text block is announced. Matches macOS `authorBlockLabel`. */
const CALLOUT_LABEL: Record<CalloutKind, string> = {
  note: 'Note callout',
  caution: 'Caution callout',
  warning: 'Warning callout',
  section: 'Section heading',
};

/**
 * The label line for an author-written text step.
 *
 * Before #62 every text step was announced identically, so Claude could not tell a
 * red `warning` the author considers critical from a `section` phase divider from an
 * ordinary paragraph. It was told to leave them alone but not what role each plays.
 *
 * `n` is the position in the source sequence (what screenshot steps use for
 * `stepNumber`). Kept on every kind, including callouts, to match macOS.
 */
export function textStepLabel(n: number, callout?: CalloutKind | null): string {
  // isCalloutKind guards a hand-edited or foreign manifest carrying a value the
  // union does not cover; fall back to the plain-step label rather than rendering
  // `undefined` into the prompt.
  const kind = callout && isCalloutKind(callout) ? CALLOUT_LABEL[callout] : 'Text step';
  return `--- ${kind} ${n} (author-written — leave this content alone) ---`;
}

/**
 * What Claude should DO about each kind, since "leave it alone" does not say how the
 * block relates to the steps around it. Mirrors macOS `authorBlockGuidance`.
 * Null for a plain text step, which needs no extra explanation.
 */
export function authorBlockGuidance(callout?: CalloutKind | null): string | null {
  if (!callout || !isCalloutKind(callout)) return null;
  if (callout === 'section') {
    return (
      '(A NON-NUMBERED phase heading the author placed to group the steps that follow. It ' +
      'tells you how they think the procedure divides up — use that structure rather than ' +
      'inventing your own, keep step wording consistent with the phase it sits in, and do ' +
      'not add a sectionHeading of your own where this already marks the boundary.)'
    );
  }
  return (
    '(The author highlighted this, so it is something they know about the process that the ' +
    'screenshots do not show. Let it inform the steps around it — reference or account for ' +
    'it where a reader would need to. Do not restate it wholesale as step text (it is ' +
    'already shown to the reader), and never contradict it.)'
  );
}

/**
 * The author's overview, or null when there is none.
 *
 * Reads `manifest.intro` — the CURRENT one — deliberately, and NOT the pre-AI
 * snapshot. Preferring `sopBackup.intro` looks safer (it avoids feeding Claude its
 * own prior output) but it silently discards an overview the user edited AFTER a
 * generation, which is the exact complaint this issue exists to fix. macOS made the
 * same call.
 *
 * The cost is real and accepted for now: when the user has NOT edited it, the
 * current intro is Claude's own text and gets sent back labelled as the author's.
 * Fixing that needs an `introEditedByUser` flag on both platforms, tracked
 * separately; the prompt below limits the damage by telling Claude the overview is
 * context to build on rather than text to protect.
 *
 * Whitespace-only counts as absent. Values are trimmed: this is prompt input, not
 * stored content.
 */
export function authorIntro(manifest: Pick<ProjectManifest, 'intro'>): SopIntro | null {
  const src = manifest.intro;
  if (!src) return null;
  const heading = (src.heading ?? '').trim();
  const body = (src.body ?? '').trim();
  return heading || body ? { heading, body } : null;
}

/**
 * The author-overview block, wording matched to macOS.
 *
 * "Authoritative context, not text to protect" is the deliberate stance. Telling
 * Claude to preserve the literal text produces a worse overview than letting it
 * rewrite for clarity while forbidding the thing that actually harms the user:
 * contradicting or quietly dropping facts and constraints only the author knows.
 */
export function authorIntroBlock(intro: SopIntro): string {
  const parts = ['--- The author already wrote this overview ---'];
  if (intro.heading) parts.push(`Heading: ${intro.heading}`);
  if (intro.body) parts.push(`Body: ${intro.body}`);
  parts.push(
    '',
    'Treat this as AUTHORITATIVE CONTEXT, not as text to protect. It states intent, ' +
      'audience, scope or constraints that the screenshots cannot show you, and it is the ' +
      "author's own knowledge of the process. Let it inform the whole guide: your `intro` " +
      'and the wording and emphasis of every step. You may rewrite it freely for clarity, ' +
      'structure and tone — write the best overview you can. What you must not do is ' +
      'CONTRADICT or SILENTLY DROP the facts and constraints it states; those are things ' +
      'the author knows and you do not.',
  );
  return parts.join('\n');
}

/**
 * Which caption to show Claude for a screenshot step.
 *
 * Prefer the pre-AI original so a regenerate is never fed Claude's own prior rewrite
 * (successive runs would compound). The exception is a caption the AUTHOR edited
 * after a generation: that is a deliberate human correction and is exactly what
 * should be rewritten from.
 *
 * `captionEditedByUser` is a shared project.json field — macOS writes it too, so the
 * rule here must stay identical. It is SET when a caption is edited through
 * updateStep and CLEARED by applySopEdits when Claude overwrites the caption, so the
 * flag never outlives the human text it describes.
 */
export function captionForPrompt(
  step: Pick<ProjectStep, 'caption' | 'captionEditedByUser'>,
  original: Pick<ProjectStep, 'caption'> | undefined,
): string {
  if (step.captionEditedByUser === true) return step.caption;
  return original?.caption ?? step.caption;
}

/**
 * Merge Claude's caption/body into a step, and retire the human-edit flag when
 * Claude's text has replaced the human's.
 *
 * The flag must not outlive the text it describes: leaving it set would make the
 * NEXT regenerate treat Claude's own output as an author correction and rewrite
 * from it, compounding on every run. Only cleared when Claude actually wrote a
 * caption — a blank response keeps the existing text, so the flag still applies.
 * Matches macOS SopApply (`edited.captionEditedByUser = nil`).
 */
export function mergeAiStepText<T extends Pick<ProjectStep, 'caption' | 'body' | 'captionEditedByUser'>>(
  step: T,
  aiCaption: string,
  aiBody: string,
): T {
  const caption = aiCaption.trim();
  const merged = {
    ...step,
    // Fall back to existing text if the model returns blank (don't wipe).
    caption: caption || step.caption,
    body: aiBody.trim() || step.body || '',
  };
  if (caption) delete merged.captionEditedByUser;
  return merged;
}
