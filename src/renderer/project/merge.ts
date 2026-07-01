// Merge two captured steps into one. The motivating case: a right-click opens a
// context menu (captured as a plain "Right-click in X" step) and the next click
// selects a menu item (captured WITH the open menu, because the selection grab
// is taken while the menu is still on screen). The selection screenshot is the
// keeper; the right-click screenshot is redundant. Merging folds the right-click
// step's click into the menu screenshot as a second marker ring, combines the
// captions, re-bakes both rings into the render, and deletes the right-click
// step — all atomically (ProjectStore.mergeSteps).
import type { MarkerAnnotation, ProjectManifest, ProjectStep } from '../../shared/project';
import { createMarker, markerColorFor } from '../editor/annotations';
import { flattenToPng } from '../editor/flatten';
import { loadImage } from './sop-prepare';
import { shotUrl } from './store';

function clamp(v: number, lo: number, hi: number): number {
  return Math.max(lo, Math.min(v, hi));
}

/** Join two text fields, dropping empties; '' when both are empty. */
function joinText(a: string | undefined, b: string | undefined, sep: string): string {
  return [a, b].map((s) => (s ?? '').trim()).filter(Boolean).join(sep);
}

/** True when step `idx` can be merged into the step that follows it (both are
 *  shot steps with screenshots). */
export function canMergeInto(steps: ProjectStep[], idx: number): boolean {
  const cur = steps[idx];
  const next = steps[idx + 1];
  return (
    !!cur &&
    !!next &&
    cur.kind !== 'text' &&
    next.kind !== 'text' &&
    !!cur.screenshot &&
    !!next.screenshot
  );
}

/**
 * Merge `drop` into `keep` (keep's screenshot/render survives). `drop`'s click is
 * mapped onto `keep`'s image space — both clicks are on the same monitor, so
 * global coords map exactly: keepOrigin = keep.global − keep.image, and the
 * mapped point is drop.global − keepOrigin — and added as a movable marker ring.
 * Returns the updated manifest.
 */
export async function mergeStepInto(
  projectId: string,
  projectPath: string,
  keep: ProjectStep,
  drop: ProjectStep,
): Promise<ProjectManifest> {
  const img = await loadImage(shotUrl(projectId, keep.screenshot));
  const natW = img.naturalWidth;
  const natH = img.naturalHeight;

  const markers: MarkerAnnotation[] = [];
  if (drop.click) {
    let mx: number;
    let my: number;
    if (keep.click) {
      // keep.click.image is in keep's stored (downscaled) pixels: image =
      // (global - origin) * scale. Recover origin, then map drop's global click
      // into keep's SAME downscaled space (T2). scale defaults to 1 for old shots.
      const ks = keep.click.imageScale ?? 1;
      const originX = keep.click.global.x - keep.click.image.x / ks;
      const originY = keep.click.global.y - keep.click.image.y / ks;
      mx = (drop.click.global.x - originX) * ks;
      my = (drop.click.global.y - originY) * ks;
    } else {
      // keep has no click metadata to anchor to — fall back to drop's own image
      // coords (best-effort; usually still in-frame since menus open at the cursor).
      mx = drop.click.image.x;
      my = drop.click.image.y;
    }
    markers.push(
      createMarker(
        clamp(mx, 0, Math.max(0, natW - 1)),
        clamp(my, 0, Math.max(0, natH - 1)),
        markerColorFor(drop),
      ),
    );
  }

  const annotations = [...keep.annotations, ...markers];

  // Re-bake: keep's own click ring (the menu selection) via the marker param,
  // plus the merged-in ring(s) which ride along as 'marker' annotations.
  const keepMarker = keep.click
    ? { x: keep.click.image.x, y: keep.click.image.y, color: markerColorFor(keep) }
    : null;
  const blob = await flattenToPng(img, annotations, keep.crop, keepMarker);
  const bytes = new Uint8Array(await blob.arrayBuffer());

  return await window.shotai.projects.mergeSteps(
    projectPath,
    keep.id,
    drop.id,
    {
      annotations,
      caption: joinText(drop.caption, keep.caption, ' → '),
      body: joinText(drop.body, keep.body, '\n\n'),
      note: joinText(drop.note, keep.note, '\n\n'),
      markerBaked: true,
    },
    bytes,
  );
}
