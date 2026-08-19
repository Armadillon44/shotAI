// Pure helpers for the SOP *input* assembly — what Claude is actually shown.
//
// Deliberately electron-free (imports only shared types) so the invariants below
// are unit-testable under plain node, same reasoning as export-geometry.ts and
// export-css.ts. The assembly itself stays in claude-service.ts; only the rules
// that are easy to get quietly wrong live here.
import { isCalloutKind } from '../shared/project';
import type { CalloutKind, ProjectManifest, SopIntro } from '../shared/project';

/** How each author text block is announced to Claude. Callouts are NOT numbered
 *  (the 1..N document counter skips them), so unlike a plain text step they
 *  carry no number — which also teaches the counting model implicitly. */
const CALLOUT_LABEL: Record<CalloutKind, string> = {
  note: 'Note callout',
  caution: 'Caution callout',
  warning: 'Warning callout',
  section: 'Section heading',
};

/**
 * The label line for an author-written text step.
 *
 * Before #62 every text step was announced identically, so Claude could not tell
 * a red `warning` the author considers critical from a `section` phase divider
 * from an ordinary paragraph. It was told to leave them alone but not what role
 * each plays, so it could not avoid duplicating a warning it could not see was a
 * warning, nor respect a phase structure it could not see existed.
 *
 * `n` is the position in the source sequence (what screenshot steps use for
 * `stepNumber`), not the document's step number — unchanged from before.
 */
export function textStepLabel(n: number, callout?: CalloutKind | null): string {
  // isCalloutKind guards a hand-edited or foreign manifest carrying a value the
  // union does not cover; fall back to the plain-step label rather than
  // rendering `undefined` into the prompt.
  if (!callout || !isCalloutKind(callout)) {
    return `--- Text step ${n} (author-written — leave this content alone) ---`;
  }
  const role =
    callout === 'section'
      ? 'author-written phase divider — leave this content alone'
      : 'author-written — leave this content alone';
  return `--- ${CALLOUT_LABEL[callout]} (${role}) ---`;
}

/**
 * The AUTHOR's overview, or null when they never wrote one.
 *
 * Mirrors the caption rule in assembleRequest (`orig?.caption ?? step.caption`)
 * and for the same reason: once a generation has run, `manifest.intro` may be
 * Claude's own text, and feeding that back would compound across regenerations.
 * `sopBackup` is the pristine pre-AI snapshot, so when it exists IT is the
 * author's intent — including when its `intro` is null, which means the author
 * wrote no overview and Claude's must not be echoed back as if they had.
 *
 * Whitespace-only is treated as absent so a stray space is never presented as
 * author intent. Returned values are trimmed: this is prompt input, not stored
 * content, so leading/trailing noise is not worth carrying.
 */
export function authorIntro(
  manifest: Pick<ProjectManifest, 'intro' | 'sopBackup'>,
): SopIntro | null {
  const src = manifest.sopBackup ? manifest.sopBackup.intro : manifest.intro;
  if (!src) return null;
  const heading = (src.heading ?? '').trim();
  const body = (src.body ?? '').trim();
  return heading || body ? { heading, body } : null;
}

/** The author-overview block as it appears in the prompt. Label matches the
 *  wording the system prompt refers to ("Author overview"). */
export function authorIntroBlock(intro: SopIntro): string {
  const parts = ['--- Author overview (author-written — keep its substance) ---'];
  if (intro.heading) parts.push(`Heading: ${intro.heading}`);
  if (intro.body) parts.push(`Body: ${intro.body}`);
  return parts.join('\n');
}
