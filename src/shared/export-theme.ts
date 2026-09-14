// What an exported document renders with (#77 phase 4).
//
// Electron-free and DOM-free, like theme-palette and doc-scale: main builds four
// document formats from it and the tests read it under plain node.
//
// ALWAYS THE BRAND'S LIGHT VALUES, whatever appearance the app happens to be in.
// The export axis is *which brand*, never *which appearance*: a dark-background
// SOP is unreadable printed and ruinous on toner, and an export is not a
// screenshot of the UI. A test asserts it.
//
// SHAPE DIFFERS FROM macOS, DELIBERATELY. `ExportTheme` there is eleven
// semantically renamed colours (text / bodyText / meta / cardBg / …) because its
// two renderers were built from two hand-copied sets and the renaming is what
// merged them. Here the ramp collapse already pointed all three surfaces at the
// same palette roles, so a second vocabulary on top would be one more mapping to
// get wrong for no gain. The end-user experience is identical, which is the part
// that has to match.
import {
  BRANDS,
  coerceBrand,
  DEFAULT_BRAND,
  type BrandId,
  type BrandRadii,
  type Palette,
} from './theme-palette';

export interface ExportTheme {
  /** Which brand produced this document. */
  brand: BrandId;
  /** The brand's LIGHT palette. Never the dark one; see the note above. */
  palette: Palette;
  /** The brand's geometry. A card is the same shape on screen and in the file. */
  radii: BrandRadii;
  /**
   * The CSS `font-family` stack, brand face first.
   *
   * NAMED, never embedded. As base64 the face is ~0.84MB against a measured
   * 0.8-1.5MB Freshservice paste budget, so embedding would consume most of the
   * budget and the images would silently drop. A reader who has the face sees
   * it; everyone else gets a grotesque of similar proportion.
   */
  fontStack: string;
  /** The same, for the Word-facing export, which keeps Arial behind the brand. */
  plainFontStack: string;
  /**
   * The brand's own face, or null for the platform's system face.
   *
   * Named separately from the stack because the PDF renderer needs to know
   * WHETHER there is a face to embed, and sniffing the stack for a leading
   * quote would be a guess about formatting rather than a fact about the brand.
   */
  fontFamily: string | null;
}

/** The `font-family` value for a brand. */
export function fontStackFor(brand: BrandId): string {
  const f = BRANDS[coerceBrand(brand)].font;
  return [...(f.family ? [`"${f.family}"`] : []), ...f.fallbacks].join(',');
}

/** The document theme a brand exports with. */
export function exportTheme(brand: BrandId): ExportTheme {
  const id = coerceBrand(brand);
  return {
    brand: id,
    palette: BRANDS[id].light,
    radii: BRANDS[id].radii,
    fontStack: fontStackFor(id),
    plainFontStack: plainFontStackFor(id),
    fontFamily: BRANDS[id].font.family,
  };
}

/**
 * What every exporter falls back to.
 *
 * A DEFAULT ARGUMENT on each builder, so every existing caller and test is
 * unaffected by the brand becoming a parameter, and a caller that forgets to
 * pass one produces the document it always produced rather than an untinted
 * one.
 */
export const DEFAULT_EXPORT_THEME: ExportTheme = exportTheme(DEFAULT_BRAND);

/**
 * The `font-family` for the plain "HTML (for Word)" export.
 *
 * Arial-first is a COMPATIBILITY decision, not a design one (#42): that document
 * exists to be pasted into Word and Google Docs, where Arial is the one face
 * present everywhere and substitution is silent. So the brand face goes in front
 * of it if there is one, and the historical stack stays behind — rather than the
 * grotesque fallbacks the styled export uses, which are chosen to resemble the
 * brand rather than to survive a paste.
 */
export function plainFontStackFor(brand: BrandId): string {
  const f = BRANDS[coerceBrand(brand)].font;
  return [...(f.family ? [`"${f.family}"`] : []), 'Arial', 'Helvetica', 'sans-serif'].join(',');
}

/**
 * The badge shape, as CSS.
 *
 * `50%` rather than the capsule's `999px` when the brand draws its chips fully
 * round: both render a circle on the square badge, but `50%` is what these
 * documents have always contained, and an export that is byte-identical for the
 * default brand is the claim this phase has to keep.
 */
export function chipRadiusCss(radii: BrandRadii): string {
  return radii.chip === null ? '50%' : `${radii.chip}px`;
}
