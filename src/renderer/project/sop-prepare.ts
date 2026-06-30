// Pre-send preparation for SOP generation: guarantee every shot step has a
// current flattened/redacted render BEFORE anything is sent to Claude, so main
// only ever reads export/.render/*.png (never the raw shots/*.png). Reuses the
// editor's pure flatten so redaction is baked identically to an export.
import type { ProjectManifest, ProjectStep } from '../../shared/project';
import { flattenToPng } from '../editor/flatten';
import { markerColorFor } from '../editor/annotations';
import { shotUrl } from './store';

export function loadImage(url: string): Promise<HTMLImageElement> {
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
 * redaction that rounds to <1px) — the caller must not proceed to generate/export.
 * Pass an AbortSignal to bail out early (e.g. the user left the project mid-run).
 */
export async function ensureFlattened(
  projectId: string,
  projectPath: string,
  steps: ProjectStep[],
  signal?: AbortSignal,
): Promise<ProjectManifest | null> {
  let latest: ProjectManifest | null = null;
  let shotNo = 0;
  for (const step of steps) {
    if (step.kind === 'text') continue;
    shotNo++;
    // Re-bake when there's no render OR the existing render predates marker-baking
    // (markerBaked falsy) — otherwise Claude would get a render with no click ring.
    if (step.flattened && step.markerBaked) continue;
    if (!step.screenshot) continue;
    signal?.throwIfAborted();
    try {
      const img = await loadImage(shotUrl(projectId, step.screenshot));
      // Bake the click ring so Claude's vision sees exactly what was clicked
      // (matches the report's marker color default).
      const marker = step.click
        ? {
            x: step.click.image.x,
            y: step.click.image.y,
            color: markerColorFor(step),
          }
        : null;
      const blob = await flattenToPng(img, step.annotations, step.crop, marker);
      const bytes = new Uint8Array(await blob.arrayBuffer());
      latest = await window.shotai.projects.updateStep(
        projectPath,
        step.id,
        { markerBaked: true },
        bytes,
      );
    } catch (err) {
      if (err instanceof DOMException && err.name === 'AbortError') throw err;
      const caption = (step.caption ?? '').trim() || 'untitled';
      throw new Error(
        `Step ${shotNo} ("${caption}") couldn't be prepared: ` +
          `${err instanceof Error ? err.message : String(err)}`,
      );
    }
  }
  return latest;
}
