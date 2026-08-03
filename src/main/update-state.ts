// The result of the startup update check, held so the renderer can PULL it.
//
// Why this exists: the check finishes on main's own schedule, which is frequently
// BEFORE the renderer has mounted and subscribed — `webContents.send` doesn't buffer,
// so a push-only design silently drops the notice. Observed in dev at a 40s gap (Vite
// compiles the renderer on first load), but the race is real in production too: a fast
// network plus a slow window loses the event.
//
// So main both pushes (for a check that lands after the renderer is up) AND stashes the
// result here for the renderer to ask for on mount. Whichever happens first, the
// notice appears exactly once.
import type { UpdateCheckResult } from '../shared/ipc';

let pending: UpdateCheckResult | null = null;

/** Remember an available update for a renderer that mounts later. */
export function setPendingUpdate(result: UpdateCheckResult | null): void {
  // Only an actual update is worth holding — "up to date" and errors are not news.
  pending = result?.available ? result : null;
}

/** The update found this launch, or null. Safe to call before the check completes. */
export function getPendingUpdate(): UpdateCheckResult | null {
  return pending;
}
