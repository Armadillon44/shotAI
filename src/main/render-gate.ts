// The fail-CLOSED redaction gate, shared by the two egress paths (Claude send +
// file export). A shot step with a blur annotation OR a crop that has NOT been
// baked into a `flattened` render must NOT fall back to the raw (un-redacted /
// uncropped) screenshot — it is refused. Only a step with neither may read the
// original shot. Implemented once here so the Claude and export paths cannot
// drift apart. Dependency-free (node:path + confinePath) so it is unit-testable.
import path from 'node:path';
import type { ProjectStep } from '../shared/project';
import { confinePath } from './path-confine';

export interface SendableRender {
  /** Absolute, project-confined path to the image to read. */
  abs: string;
  mediaType: 'image/png' | 'image/jpeg';
  ext: string;
}

/**
 * Decide which on-disk image is safe to read for a step, or throw (fail-closed).
 * @param dir       the project folder (already resolved/known)
 * @param step      the step to resolve a render for
 * @param stepLabel caller-supplied label for error messages (callers number
 *                  steps differently — Claude over the AI-filtered source list,
 *                  export over the full step list — so the label is passed in)
 * @param verb      'send' (to Claude) or 'export' — only affects the message
 */
export function resolveSendableRender(
  dir: string,
  step: ProjectStep,
  stepLabel: string,
  verb: 'send' | 'export',
): SendableRender {
  const hasBlur = (step.annotations ?? []).some((a) => a.type === 'blur');
  const rel = step.flattened ?? null;
  // Fail closed: an unbaked redaction or crop must never read the raw screenshot.
  if (!rel && (hasBlur || step.crop)) {
    throw new Error(
      `${stepLabel} has a redaction or crop that hasn't been baked into a render yet — ` +
        `refusing to ${verb} the raw screenshot. Open it in the editor and save, then retry.`,
    );
  }
  const relToRead = rel ?? step.screenshot;
  const abs = relToRead ? confinePath(dir, relToRead) : null;
  if (!abs) throw new Error(`${stepLabel} has no readable screenshot.`);
  const ext = path.extname(relToRead).toLowerCase();
  const mediaType: 'image/png' | 'image/jpeg' =
    ext === '.jpg' || ext === '.jpeg' ? 'image/jpeg' : 'image/png';
  return { abs, mediaType, ext: ext || '.png' };
}
