// Remote-session visibility (issue: view the app over remote control / screen share).
//
// shotAI sets contentProtection on every window, which is what keeps it out of its
// own screenshots. On Windows that is WDA_EXCLUDEFROMCAPTURE, and it cannot tell
// OUR screenshot from Teams' or Splashtop's frame: both are screen capture. There
// is no flag that says "exclude from this capture but not that one".
//
// So the only way to have both is TEMPORAL. With remoteVisible on, protection is
// off while the app is merely being used (a remote viewer sees it), and every
// screen grab is wrapped in shieldOwnWindows() so protection is on for exactly as
// long as the grab takes.
//
// Measured, not assumed (scripts/protection-probe.cjs against node-screenshots):
//   - protection ON  => the window contributes 0 px to a monitor capture
//   - toggling it OFF => fully capturable again, same pixel count as before
//   - the toggle is SYNCHRONOUS: clean at a 0ms settle, worst of 5 runs at each of
//     8 delays. Nothing like the 350ms settle a window HIDE needs.
// That last point is what makes per-grab shielding practical rather than a
// trade-off: it costs one Win32 call per window per grab.
import { BrowserWindow } from 'electron';
import { remoteVisibleNow } from './settings';

/** `on` = excluded from screen capture. */
function setProtection(on: boolean): void {
  for (const win of BrowserWindow.getAllWindows()) {
    if (!win.isDestroyed()) win.setContentProtection(on);
  }
}

/**
 * Apply the setting to every open window, so the toggle takes effect without a
 * restart. Called after the setting loads at startup and whenever it changes.
 */
export function applyRemoteVisibility(visible: boolean): void {
  setProtection(!visible);
}

/**
 * Exclude every shotAI window from capture for the duration of one screen grab,
 * then restore whatever the setting says.
 *
 * Returns the release function rather than taking a callback so it composes with
 * both the sync and async grab paths through a plain try/finally.
 *
 * When remoteVisible is off this is a no-op pair (protection is already on), which
 * is deliberate: one code path, so the shielded grab helpers cannot drift into
 * being correct in one mode and wrong in the other.
 *
 * Reads the setting through the SYNCHRONOUS cache. An async read here would open a
 * gap in which the pill is capturable, which is the exact leak this prevents.
 */
export function shieldOwnWindows(): () => void {
  const visible = remoteVisibleNow();
  setProtection(true);
  return () => setProtection(!visible);
}
