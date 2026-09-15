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
 * Chooses between the CURRENT overview and the pre-AI snapshot by
 * `introEditedByUser`, matching macOS (SOPKit/RequestAssembler.swift).
 *
 * An earlier version read `manifest.intro` unconditionally, on the reasoning that
 * preferring the snapshot would discard an overview edited after a generation.
 * That objection is real and is exactly what the flag answers: an author edit
 * sets it, so their text is sent; the snapshot is preferred only when they have
 * not edited, which is when the current intro is Claude's own output.
 *
 * Whitespace-only counts as absent. Values are trimmed: this is prompt input, not
 * stored content.
 */
export function authorIntro(
  manifest: Pick<ProjectManifest, 'intro'> &
    Partial<Pick<ProjectManifest, 'introEditedByUser' | 'sopBackup'>>,
): SopIntro | null {
  // Pick the source by the FLAG. After any generation `manifest.intro` is
  // Claude's own text unless the author has edited it since, so sending it
  // unconditionally under "--- The author already wrote this overview ---" fed
  // the model its previous guess back as author intent, and hardened an early
  // misreading with every regenerate (#79).
  //
  // The objection the old comment here raised — that preferring the pre-AI
  // snapshot discards an overview edited AFTER a generation — is exactly the case
  // the flag covers: an author edit sets it, and the current intro is sent. The
  // snapshot is preferred only when they have NOT edited, which is precisely when
  // the current intro is Claude's.
  //
  // Falls back to `manifest.intro` when there is no backup, which is the first
  // run: nothing has been generated yet, so it is the author's by definition.
  //
  // Note the shape: when a backup EXISTS, its intro is used even if it is null —
  // that means the author never wrote one, and sending Claude's instead would be
  // the very substitution this avoids. Only the absence of a backup falls back.
  // `sopBackup?.intro ?? manifest.intro` looks equivalent and is not: it collapses
  // "no backup" and "backup with no intro" into the same branch. macOS gets this
  // right because Swift's `sopBackup.map { $0.intro } ?? manifest.intro` coalesces
  // the OUTER optional only.
  const edited = manifest.introEditedByUser === true;
  const src = edited
    ? manifest.intro
    : manifest.sopBackup
      ? manifest.sopBackup.intro
      : manifest.intro;
  if (!src) return null;
  const heading = (src.heading ?? '').trim();
  const body = (src.body ?? '').trim();
  return heading || body ? { heading, body } : null;
}

/**
 * The author-overview block, in one of TWO modes (#64).
 *
 * The distinction exists because the overview has no other provenance. An intro
 * Claude wrote last run and an intro the author wrote look identical on disk, so
 * `edited` — set only by setProjectIntro, the human path — is the only thing that
 * says whose words these are.
 *
 * NOT edited: the original stance, matched to macOS. Claude may rewrite freely,
 * because "preserve this text" applied to Claude's own prior output just freezes
 * the first draft forever. What it may never do is contradict or silently drop a
 * fact only the author knows.
 *
 * Edited: gentle massage. The author's wording carries intent the screenshots
 * cannot show, so it survives. The HEADING is not merely requested — sop-apply
 * pins it back after the reply, and the prompt says so, because an instruction the
 * model can quietly ignore is not a guarantee. The body is theirs to reword for
 * clarity and voice, never to replace.
 */
export function authorIntroBlock(intro: SopIntro, edited = false): string {
  const parts = [
    edited
      ? '--- The author WROTE this overview themselves — their words ---'
      : '--- The author already wrote this overview ---',
  ];
  if (intro.heading) parts.push(`Heading: ${intro.heading}`);
  if (intro.body) parts.push(`Body: ${intro.body}`);
  parts.push(
    '',
    edited
      ? "This is the AUTHOR'S OWN overview and they have edited it, so their wording " +
        'states intent, audience, scope or constraints the screenshots cannot show you. ' +
        'Their HEADING is kept exactly as written — shotAI restores it after you reply, ' +
        'so do not spend effort rewording it. For the BODY: reword only for clarity, ' +
        'grammar and voice, and you may ADD context the steps reveal. Do NOT restructure ' +
        'it, do NOT replace it with your own framing, and do NOT drop any fact, ' +
        'constraint or audience note it states. If it already reads well, return it ' +
        'essentially unchanged — that is a correct answer here. Substituting a better ' +
        'overview of your own is not.'
      : 'Treat this as AUTHORITATIVE CONTEXT, not as text to protect. It states intent, ' +
        'audience, scope or constraints that the screenshots cannot show you, and it is ' +
        "the author's own knowledge of the process. Let it inform the whole guide: your " +
        '`intro` and the wording and emphasis of every step. You may rewrite it freely ' +
        'for clarity, structure and tone — write the best overview you can. What you ' +
        'must not do is CONTRADICT or SILENTLY DROP the facts and constraints it ' +
        'states; those are things the author knows and you do not.',
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

/**
 * The title instruction, as sent.
 *
 * The app KNOWS whether the current name is one it generated, so it says so
 * rather than making Claude judge whether a name is descriptive — which it gets
 * wrong in both directions. macOS added this after two observed failures: once it
 * kept "Project 2026/08/19 12:41:16" verbatim as the SOP title, and once it
 * returned the literal word "placeholder".
 *
 * That second failure is why this NAMES NO FILLER WORDS. The wording that caused
 * it put the word "placeholder" in the same sentence as the value; listing the
 * words to avoid as negative examples reproduced it in the `intro` instead. The
 * name is quoted on its own line and described only in the sentence after, and
 * the constraint is positive.
 *
 * A free function so a test can assert on the REAL string. macOS's first attempt
 * at that test rebuilt the prompt inside the test and could only ever pass.
 */
export function titleInstruction(currentName: string, isAutoGenerated: boolean): string {
  const lines = [`Current project name: "${currentName}"`];
  if (isAutoGenerated) {
    lines.push(
      'That name was generated by the app from a timestamp. It is NOT a title, carries no ' +
        'meaning, and MUST be replaced. Set `title` to a clear, specific name for the ' +
        'PROCEDURE ITSELF, derived from what the steps accomplish — for example ' +
        '"Configuring VPN access in the admin console", and never return the current name. ' +
        'Every word of the title must come from what the steps actually do; if they are ' +
        'sparse, still name the specific task they show.',
    );
  } else {
    lines.push(
      'That name was written by a person, so treat it as intentional. Set `title` to it ' +
        'unchanged unless it is clearly wrong or vague for what the steps actually ' +
        'accomplish, in which case improve it while keeping their meaning.',
    );
  }
  return lines.join('\n');
}
