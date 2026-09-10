// Applying the theme to the document (#77).
//
// TWO independent axes, not one. APPEARANCE (light / dark / follow the OS) is what
// ThemePref has always meant; BRAND is which identity the app wears, and every
// brand exists in both appearances. #77 first framed this as `resolveDark` becoming
// `resolveTheme(pref): ThemeId`, collapsing both into one value — that is the shape
// to avoid, because it makes the two read as mutually exclusive and there is no
// single id for "LFI, following the OS".
//
// The colour tokens themselves are GENERATED from shared/theme-palette and injected
// here, rather than authored in project.css. That is what makes the palette one
// definition instead of five; see themeStylesheet.
//
// The one architectural advantage this platform has over the macOS port: flipping
// [data-brand] re-cascades every var() read for free. macOS needed 137 call sites
// moved onto an environment read, because a SwiftUI body reading a static registers
// no dependency and a brand flip repainted nothing.
import type { ThemePref } from '../../shared/project';
import {
  DEFAULT_BRAND,
  themeStylesheet,
  type Appearance,
  type BrandId,
} from '../../shared/theme-palette';

const SYSTEM_DARK = '(prefers-color-scheme: dark)';

/** The injected token sheet's id, so installing twice is a no-op. */
const STYLE_ID = 'shotai-theme-tokens';

/**
 * Put the generated colour tokens into the document.
 *
 * Called from the renderer entry BEFORE the first render, not from applyTheme:
 * the preference loads asynchronously, so waiting for it would paint one frame
 * with no colours at all. applyTheme calls it too, so a test or a second entry
 * point cannot forget.
 */
export function installThemeStyle(doc: Document = document): void {
  if (doc.getElementById(STYLE_ID)) return;
  const el = doc.createElement('style');
  el.id = STYLE_ID;
  el.textContent = themeStylesheet();
  doc.head.appendChild(el);
}

/** Which appearance the preference resolves to right now ('system' asks the OS). */
export function resolveAppearance(pref: ThemePref): Appearance {
  return pref === 'dark' ||
    (pref === 'system' &&
      typeof window.matchMedia === 'function' &&
      window.matchMedia(SYSTEM_DARK).matches)
    ? 'dark'
    : 'light';
}

/**
 * Set `<html data-theme data-brand>`, which is the whole of applying a theme.
 *
 * BOTH attributes are always written. The generated blocks are fully qualified on
 * brand AND appearance so they cannot fight each other, which means a document
 * missing either attribute falls back to the bare `:root` block instead of picking
 * up half a theme.
 */
export function applyTheme(pref: ThemePref, brand: BrandId = DEFAULT_BRAND): void {
  installThemeStyle();
  const root = document.documentElement;
  root.dataset.theme = resolveAppearance(pref);
  root.dataset.brand = brand;
}

/** While `pref` is 'system', re-apply when the OS theme flips. Returns an
 *  unsubscribe (a no-op for non-system prefs). */
export function watchSystemTheme(pref: ThemePref, onChange: () => void): () => void {
  if (pref !== 'system' || typeof window.matchMedia !== 'function') return () => undefined;
  const mq = window.matchMedia(SYSTEM_DARK);
  mq.addEventListener('change', onChange);
  return () => mq.removeEventListener('change', onChange);
}
