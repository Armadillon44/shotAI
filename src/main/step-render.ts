// Step render-cache helpers, factored out of ProjectStore so the redaction-
// freshness logic is unit-testable without pulling in electron. Dependency-free
// beyond node:fs/path + the path-confine boundary.
import { promises as fs } from 'node:fs';
import path from 'node:path';
import type { ProjectStep, StepPatch } from '../shared/project';
import { confinePath } from './path-confine';

/**
 * Apply a patch to a step and, when NO fresh render is co-written, invalidate any
 * cached flattened render whose redaction/crop the patch just changed. Redaction
 * is enforced by render FRESHNESS, not mere existence (Phase-3b review): a new or
 * changed blur/crop with no re-bake MUST drop the stale render so the next send
 * is forced to re-flatten. Shared by updateStep AND mergeSteps so the two can't
 * drift (mergeSteps previously lacked this invalidation — security fix S3).
 */
export function applyPatchAndInvalidate(
  step: ProjectStep,
  patch: StepPatch,
  hasFreshPng: boolean,
): void {
  Object.assign(step, patch);
  if (hasFreshPng) return; // caller writes the fresh render + sets flattened/renderRev
  if ('annotations' in patch || 'crop' in patch) {
    step.flattened = null;
    // The dropped render also carried the baked marker; the next bake must redo it.
    step.markerBaked = false;
  }
}

/**
 * Write a step's re-baked render under export/.render and point the step at it.
 * The write path is confined (a hand-edited manifest id with traversal segments
 * can't escape the folder). Bumps renderRev so the report cache-busts the <img>.
 */
export async function writeStepRender(
  resolved: string,
  step: ProjectStep,
  id: string,
  png: Buffer,
): Promise<void> {
  // posix separators for the shot:// URL the renderer builds from step.flattened.
  const rel = path.posix.join('export', '.render', `${id}.png`);
  const abs = confinePath(resolved, rel);
  if (!abs) throw new Error(`refusing to write render for step "${id}" — path escapes the project folder`);
  await fs.mkdir(path.dirname(abs), { recursive: true });
  await fs.writeFile(abs, png);
  step.flattened = rel;
  step.renderRev = (step.renderRev ?? 0) + 1;
}
