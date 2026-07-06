// Pure capture geometry — extracted from CaptureController so the redaction-
// adjacent rect math (a pixel of drift misplaces a marker or a crop) is unit-
// testable without electron / node-screenshots. The NATIVE crop stays in
// CaptureController; here we only compute rectangles.
import type { Point, Rect } from '../shared/project';

/** A focused-window descriptor (subset of get-windows' result we classify on). */
export type ActiveLike = { owner: { name: string }; title: string } | null | undefined;

// Windows shell host processes whose windows are huge/transparent and capture as
// a black swath — observed from get-windows on Windows 11 (sometimes the friendly
// name, sometimes the exe).
const SHELL_HOST_RE =
  /experience host|searchhost|shellexperiencehost|startmenuexperiencehost|searchapp|textinputhost|cortana/i;

/** Smallest rectangle containing both inputs. */
export function unionRect(a: Rect, b: Rect): Rect {
  const x = Math.min(a.x, b.x);
  const y = Math.min(a.y, b.y);
  const right = Math.max(a.x + a.width, b.x + b.width);
  const bottom = Math.max(a.y + a.height, b.y + b.height);
  return { x, y, width: right - x, height: bottom - y };
}

/** A box of half-size `620·scaleFactor` centered on a point — the generous,
 *  roughly symmetric crop used to frame a click together with any menu/dropdown
 *  it opened (menus flip up/left near screen edges, so the box is symmetric). */
export function clickBox(point: Point, scaleFactor: number): Rect {
  const half = Math.round(620 * (scaleFactor || 1));
  return { x: point.x - half, y: point.y - half, width: half * 2, height: half * 2 };
}

/**
 * Classify how to frame a click from the focused window:
 *  - 'window'     → a normal app window
 *  - 'region'     → an OS shell surface (taskbar, Start/Search, tray) → tight crop
 *  - 'fullscreen' → the desktop, or unknown/unidentified focus → whole monitor
 */
export function captureModeFor(active: ActiveLike): 'window' | 'region' | 'fullscreen' {
  if (!active) return 'fullscreen'; // unknown focus → full context, never a guessed crop
  const app = active.owner.name;
  const title = active.title;
  if (app === 'Windows Explorer' && title === 'Program Manager') return 'fullscreen'; // desktop
  if (app === 'Windows Explorer' && title.trim() === '') return 'region'; // taskbar / system tray
  if (SHELL_HOST_RE.test(app)) return 'region'; // Start / Search / Shell hosts
  return 'window';
}

/**
 * The crop rectangle (in the monitor's LOCAL pixel space) for a global-px region,
 * clamped to the monitor bounds. `mon` is the monitor's global-px position/size.
 * Pure geometry of CaptureController.cropToRegion (which does the native crop).
 */
export function cropRect(
  mon: { x: number; y: number; width: number; height: number },
  region: Rect,
): Rect {
  const lx = Math.round(region.x - mon.x);
  const ly = Math.round(region.y - mon.y);
  const cropX = Math.max(0, Math.min(lx, mon.width - 1));
  const cropY = Math.max(0, Math.min(ly, mon.height - 1));
  const cropW = Math.max(1, Math.min(lx + Math.round(region.width), mon.width) - cropX);
  const cropH = Math.max(1, Math.min(ly + Math.round(region.height), mon.height) - cropY);
  return { x: cropX, y: cropY, width: cropW, height: cropH };
}
