// F10 — apply the color-theme preference to the document. 'system' resolves the
// OS preference via prefers-color-scheme. The whole app reskins from the
// [data-theme] token sets in project.css, so this just sets the attribute.
import type { ThemePref } from '../../shared/project';

const SYSTEM_DARK = '(prefers-color-scheme: dark)';

/** Whether the given preference should render dark right now. */
export function resolveDark(pref: ThemePref): boolean {
  return (
    pref === 'dark' ||
    (pref === 'system' &&
      typeof window.matchMedia === 'function' &&
      window.matchMedia(SYSTEM_DARK).matches)
  );
}

/** Set <html data-theme="light|dark"> from the preference. */
export function applyTheme(pref: ThemePref): void {
  document.documentElement.dataset.theme = resolveDark(pref) ? 'dark' : 'light';
}

/** While `pref` is 'system', re-apply when the OS theme flips. Returns an
 *  unsubscribe (a no-op for non-system prefs). */
export function watchSystemTheme(pref: ThemePref, onChange: () => void): () => void {
  if (pref !== 'system' || typeof window.matchMedia !== 'function') return () => undefined;
  const mq = window.matchMedia(SYSTEM_DARK);
  mq.addEventListener('change', onChange);
  return () => mq.removeEventListener('change', onChange);
}
