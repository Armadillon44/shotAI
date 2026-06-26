// Pre-send preparation for SOP generation: guarantee every shot step has a
// current flattened/redacted render BEFORE anything is sent to Claude, so main
// only ever reads export/.render/*.png (never the raw shots/*.png). Reuses the
// editor's pure flatten so redaction is baked identically to an export.
import type { ProjectManifest, ProjectStep } from '../../shared/project';
import { flattenToPng } from '../editor/flatten';
import { shotUrl } from './store';

function loadImage(url: string): Promise<HTMLImageElement> {
  return new Promise((resolve, reject) => {
    const img = new Image();
    // shot:// returns Access-Control-Allow-Origin:* so the canvas stays untainted
    // and flattenToPng()'s toBlob() works (same as the editor).
    img.crossOrigin = 'anonymous';
    img.onload = () => resolve(img);
    img.onerror = () => reject(new Error(`Could not load a screenshot to flatten (${url}).`));
    img.src = url;
  });
}

/**
 * Flatten any shot step that lacks a current render, persisting each via
 * updateStep. Returns the latest manifest if anything changed (so the caller can
 * resync the store), else null. Throws if a step can't be flattened (e.g. a
 * redaction that rounds to <1px) — the caller must not proceed to generate.
 */
export async function ensureFlattened(
  projectId: string,
  projectPath: string,
  steps: ProjectStep[],
): Promise<ProjectManifest | null> {
  let latest: ProjectManifest | null = null;
  for (const step of steps) {
    if (step.kind === 'text') continue;
    if (step.flattened) continue;
    if (!step.screenshot) continue;
    const img = await loadImage(shotUrl(projectId, step.screenshot));
    const blob = await flattenToPng(img, step.annotations, step.crop);
    const bytes = new Uint8Array(await blob.arrayBuffer());
    latest = await window.shotai.projects.updateStep(projectPath, step.id, {}, bytes);
  }
  return latest;
}
