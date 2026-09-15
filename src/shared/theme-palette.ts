// The colour vocabulary, in one place (#77 phase 0).
//
// Electron-free and DOM-free on purpose, the same reasoning as doc-scale.ts: main
// builds export stylesheets and Office documents from it, the renderer's stylesheet
// is asserted against it, so ONE set of values serves every surface.
//
// PHASE 0 SHIPS NO VISUAL CHANGE. Every value here is the EXACT value that surface
// already renders today. The point is not to fix anything yet; it is to make the
// four hand-maintained copies of this palette into one, and to make the places where
// they already disagree visible and asserted instead of accidental.
//
// WHAT THE EXTRACTION FOUND, and it is worse than #77 estimated. The callout ramp
// (note / caution / warning) is byte-identical on all four surfaces. Every single
// divergence is in the NEUTRALS, and there are THREE ramps, not two:
//
//   role     app (violet-tinted)   html + docx (Tailwind grey)   pptx (a third set)
//   ink      #191826               #1f2937                       #14161f
//   ink-2    #5a5772               #374151                       #525a6e
//   ink-3    #918ea6               #6b7280                       #8b91a3
//   hair     #e7e4f2               #e5e7eb                       #e7e4f2 (agrees)
//   ctrl-bd  #cbc7db               #cbd5e1                       #cbd5e1
//
// And the brand accent (#6344f1) appears in the app and the HTML export but NOWHERE
// in the PowerPoint export: that deck is entirely unbranded today.
//
// So a reader comparing an on-screen report with its own PDF is looking at two
// different neutral ramps, and its PowerPoint at a third. Nobody has reported it,
// which is exactly why it needs a test rather than an opinion.
//
// Converging them is a real visual change to shipped output, so it does NOT belong
// in phase 0. It gets its own commit, like the export card radius 12 -> 10. What
// phase 0 owes is KNOWN_DIVERGENCES below: the drift written down, so a NEW one
// fails a test instead of joining the pile.

/**
 * One semantic role per colour. Names mirror the app's `:root` custom properties so
 * the two can be asserted against each other by name rather than by eye.
 */
export interface Palette {
  // Brand
  accent: string;
  accentPress: string;
  accentTint: string;
  accentInk: string;
  onAccent: string;
  // Neutrals: text
  ink: string;
  ink2: string;
  ink3: string;
  // Neutrals: lines and surfaces
  hair: string;
  hair2: string;
  controlBd: string;
  /** The soft ring an input draws on :focus. Distinct from `accent`, which is the
   *  app-wide :focus-visible ring; this is the quieter one a text field wears
   *  while it is being typed into. */
  focusRing: string;
  /** A muted accent for a rule that MARKS something without shouting. The report
   *  uses it for a step's body rule, against the full `accent` on a merge
   *  suggestion — two weights of the same idea, one element apart. */
  accentSoft: string;
  surface: string;
  surface2: string;
  ground: string;
  fieldBg: string;
  // Status
  ok: string;
  okTint: string;
  okInk: string;
  draft: string;
  draftTint: string;
  draftInk: string;
  danger: string;
  dangerInk: string;
  dangerTint: string;
  dangerBd: string;
  // Callouts. Identical on every surface, and the only part that never drifted.
  noteBg: string;
  noteBd: string;
  noteFg: string;
  cautBg: string;
  cautBd: string;
  cautFg: string;
  warnBg: string;
  warnBd: string;
  warnFg: string;
}

/**
 * Which brand the UI wears. Orthogonal to the light/dark APPEARANCE: a brand has
 * both a light and a dark set, so the two are separate settings rather than one
 * combined picker that would misrepresent them as mutually exclusive.
 *
 * The string values are the persisted form and are shared with macOS
 * (`ShotModel.BrandPref`), because the per-project `theme` key planned for
 * `project.json` puts them in the cross-platform schema. Do not rename them.
 */
export type BrandId = 'shotAI' | 'lfi';

/**
 * ⚠ `ink3` CARRIES DOCUMENT TEXT, so it has to meet AA on every ground.
 *
 * It was `#918ea6` on the default brand: 2.90 on the app ground, 3.17 on a card.
 * It had never met AA, and only escaped notice because it was asked to colour
 * date groups and micro-labels rather than anything a reader has to parse. The
 * export ramp collapse then aims document meta text at it — the date line, the
 * OVERVIEW eyebrow — in documents that get printed, which is what forced the
 * measurement.
 *
 * Darkened on both appearances. macOS reported its dark value already passing and
 * left it alone; WINDOWS' DARK VALUE WAS DIFFERENT AND DID NOT — `#726f8b`
 * measured 3.92 / 3.59 / 3.36. Its own measurement, not a ported conclusion.
 */

/** Light or dark. Resolved from ThemePref; 'system' follows the OS. */
export type Appearance = 'light' | 'dark';

/**
 * A brand's corner radii, BY ROLE rather than by number.
 *
 * Roles, because a single number is wrong: 8px on a 26px status chip or a 4px
 * checkbox looks broken, and "the default brand is 10px everywhere" is the exact
 * wording that produced a wrong implementation of phase 0b.
 *
 * Only `card` was decided by the design study. The rest follow one principle,
 * asserted as a RELATIONSHIP in the tests rather than as values: **siblings
 * match, and a nested element takes a smaller radius than its container.** A
 * relationship cannot be flattened by accident the way a number can.
 *
 * Windows carries two roles macOS does not. Its stylesheets already drew six
 * distinct sizes where macOS drew four, and collapsing 8px controls onto a 6px
 * role would have restyled several dozen elements. Tokenizing is not licence to
 * restyle, so the scale is sized to the surfaces that exist here.
 */
export interface BrandRadii {
  /** Dialogs, popovers, panels, the Home project card. */
  panel: number;
  /** Document cards: the report's step card and overview. Siblings, one value. */
  card: number;
  /** Media nested inside a card: the screenshot, the annotation canvas. */
  figure: number;
  /** Inputs, buttons, menus, bordered boxes. */
  control: number;
  /** A small control sitting inside a control: a menu item, an inline input. */
  controlSm: number;
  /** Checkboxes, tiny readouts, the keyboard-focus ring. */
  micro: number;
  /**
   * Chips and badges: the status pill, the sort chip, the step number, the
   * toggle track.
   *
   * `null` means FULLY ROUND — a capsule, and a circle where the element is
   * square. That is the default brand's look, and it is not a radius: a
   * capsule's corner depends on the element's height.
   *
   * CSS can express it as a number, because `border-radius: 999px` clamps to
   * half the shorter side. That is the trap: "capsule" and "8px" would then
   * differ by three orders of magnitude inside one token and read as a typo. So
   * the INTENT is nullable here, and the generator resolves null to the clamping
   * value on its way out.
   */
  chip: number | null;
}

/** The px value a role renders as; `chip: null` becomes the clamping capsule. */
export function radiusCss(v: number | null): string {
  return v === null ? '999px' : `${v}px`;
}

/** A brand's typeface, as names. The file itself is a packaging concern. */
export interface BrandFont {
  /**
   * The brand's own face, or null for the platform's system face.
   *
   * A family NAME, not a file: the app resolves it against a bundled face and
   * the exports name it first in their CSS stack.
   */
  family: string | null;
  /**
   * Faces to try after it. Chosen to RESEMBLE the brand face, not to match the
   * OS — a reader without it should get something from the same family of
   * shapes rather than Segoe or SF.
   */
  fallbacks: string[];
  /**
   * `font-stretch` for uppercase micro-labels, or null for none.
   *
   * The LFI guide leans on a condensed treatment: "uppercase, condensed
   * treatments suit short section titles and statements." 62% is Archivo's
   * narrowest and its named Condensed instance; the study drew 66, which is a
   * few percent of advance width apart at UI sizes and needs interpolation.
   *
   * Null on a brand with no condensed face, so this is not a general
   * affordance — it is part of one identity.
   */
  labelStretch: number | null;
}

/** A brand, in both appearances. */
export interface Brand {
  /** Shown in Settings. */
  label: string;
  light: Palette;
  dark: Palette;
  /** Geometry does not change with the appearance, only with the brand. */
  radii: BrandRadii;
  /** Typography, likewise. */
  font: BrandFont;
}

/**
 * ONE definition of the app's colours, for every surface.
 *
 * Until this, the same palette existed four times by hand: project.css `:root`,
 * export-css.ts, export-docx.ts and export-pptx.ts. Phase 0 pointed the three
 * export files here; this points the STYLESHEET here too, by GENERATING its custom
 * properties (see themeStylesheet) rather than authoring them. project.css now
 * declares no colour at all, and a test asserts it, so a fifth copy cannot start
 * growing the way the first four did.
 *
 * macOS reached the same place from the other end: `ShotModel.BrandPalette` is one
 * definition the app builds SwiftUI colours from and ExportKit builds hex strings
 * from. Same structure, different representation, which is the arrangement — the
 * end-user experience matches, the mechanism does not have to.
 *
 * Both value sets below are EXACTLY what project.css declared before it was
 * generated, so this change repaints nothing.
 */
// The two brand tables used to sit here, hand-written. They are now GENERATED
// into brand-colors.generated.ts from  — the same file
// Armadillon44/shotAI_MacOS generates its palette from, so a colour the two
// platforms share cannot be edited on one side only. That is how every drift in
// the 2026-09-14 cross-check happened, this file's own fieldBg included.
//
// Add or change a value there, then run .
// The types, the helpers and everything about how these are CONSUMED stay here.
//
// The type import in the generated file is type-only, so this cycle is erased at
// runtime and there is no circular import.
import { BRANDS } from './brand-colors.generated';
export { BRANDS };

/** Every brand id, in the order Settings offers them. */
export const BRAND_IDS = Object.keys(BRANDS) as BrandId[];

/** The brand a project or the app falls back to when nothing is set. */
export const DEFAULT_BRAND: BrandId = 'shotAI';

/**
 * Whether `v` names a brand this build knows.
 *
 * Distinct from coerceBrand, and the distinction matters since a project may
 * PIN the default brand: coerceBrand('nonsense') and coerceBrand('shotAI') are
 * both 'shotAI', so coercion alone cannot tell an unknown value from a
 * deliberate default and would silently promote the first into the second.
 */
export function isBrandId(v: unknown): v is BrandId {
  return typeof v === 'string' && v in BRANDS;
}

/** Narrow an untrusted value to a brand id (default DEFAULT_BRAND). */
export function coerceBrand(v: unknown): BrandId {
  return typeof v === 'string' && v in BRANDS ? (v as BrandId) : DEFAULT_BRAND;
}

/** The palette a brand wears in an appearance. */
export function brandPalette(brand: BrandId, appearance: Appearance): Palette {
  return BRANDS[coerceBrand(brand)][appearance];
}

/**
 * The default brand's light values.
 *
 * Kept as a name of its own because it is what the exports render from and what
 * the parity tests compare against. An alias, not a second copy.
 */
export const APP_LIGHT: Palette = BRANDS.shotAI.light;

/** The default brand's dark values. */
export const APP_DARK: Palette = BRANDS.shotAI.dark;

/**
 * Palette role -> the CSS custom property that carries it.
 *
 * This mapping used to live in the TEST, which was the right place while there
 * were two hand-maintained copies to compare. Now it GENERATES the stylesheet, so
 * there is nothing left to disagree with.
 */
export const ROLE_TO_TOKEN: Record<keyof Palette, string> = {
  accent: 'accent',
  accentPress: 'accent-press',
  accentTint: 'accent-tint',
  accentInk: 'accent-ink',
  onAccent: 'on-accent',
  ink: 'ink',
  ink2: 'ink-2',
  ink3: 'ink-3',
  hair: 'hair',
  hair2: 'hair-2',
  controlBd: 'control-bd',
  focusRing: 'focus-ring',
  accentSoft: 'accent-soft',
  surface: 'surface',
  surface2: 'surface-2',
  ground: 'ground',
  fieldBg: 'field-bg',
  ok: 'ok',
  okTint: 'ok-tint',
  okInk: 'ok-ink',
  draft: 'draft',
  draftTint: 'draft-tint',
  draftInk: 'draft-ink',
  danger: 'danger',
  dangerInk: 'danger-ink',
  dangerTint: 'danger-tint',
  dangerBd: 'danger-bd',
  noteBg: 'note-bg',
  noteBd: 'note-bd',
  noteFg: 'note-fg',
  cautBg: 'caut-bg',
  cautBd: 'caut-bd',
  cautFg: 'caut-fg',
  warnBg: 'warn-bg',
  warnBd: 'warn-bd',
  warnFg: 'warn-fg',
};

/** Every custom property this module owns, for the "project.css declares none" test. */
export const COLOUR_TOKENS: readonly string[] = Object.values(ROLE_TO_TOKEN);

/** The two type custom properties the generator owns. */
export const TYPE_TOKENS: readonly string[] = ['font-stack', 'label-stretch'];

/** Radius role -> custom property. Same arrangement as ROLE_TO_TOKEN. */
export const RADIUS_TO_TOKEN: Record<keyof BrandRadii, string> = {
  panel: 'radius-panel',
  card: 'radius-card',
  figure: 'radius-figure',
  control: 'radius-control',
  controlSm: 'radius-control-sm',
  micro: 'radius-micro',
  chip: 'radius-chip',
};

/** Every radius custom property the generator owns. */
export const RADIUS_TOKENS: readonly string[] = Object.values(RADIUS_TO_TOKEN);

/** One `--token:value` list: the palette, then the geometry. */
function declarations(p: Palette, r: BrandRadii, f: BrandFont): string {
  const colours = (Object.keys(ROLE_TO_TOKEN) as (keyof Palette)[]).map(
    (role) => `--${ROLE_TO_TOKEN[role]}:${p[role]}`,
  );
  const radii = (Object.keys(RADIUS_TO_TOKEN) as (keyof BrandRadii)[]).map(
    (role) => `--${RADIUS_TO_TOKEN[role]}:${radiusCss(r[role])}`,
  );
  const type = [
    `--font-stack:${[...(f.family ? [`"${f.family}"`] : []), ...f.fallbacks].join(',')}`,
    // 'normal' rather than 100%: a brand with no condensed face should not be
    // asking the shaper for a width at all.
    `--label-stretch:${f.labelStretch === null ? 'normal' : `${f.labelStretch}%`}`,
  ];
  return [...colours, ...radii, ...type].join(';');
}

/**
 * The whole colour layer of the app's stylesheet, generated.
 *
 * Emitted as FULLY QUALIFIED blocks — brand and appearance both named — rather
 * than a base plus overrides. Every block then carries the same specificity and
 * they are mutually exclusive, so adding a brand cannot change which rule wins for
 * an existing one. Base-plus-override would put `[data-brand]` and `[data-theme]`
 * at equal specificity, where source order decides and a brand block would
 * silently defeat dark mode.
 *
 * The leading bare `:root` block is the pre-JS fallback. The attributes are set
 * from a setting that loads asynchronously, so without it the first paint has no
 * colours at all.
 */
export function themeStylesheet(): string {
  const base = BRANDS[DEFAULT_BRAND];
  const blocks = [`:root{${declarations(base.light, base.radii, base.font)}}`];
  for (const id of BRAND_IDS) {
    for (const appearance of ['light', 'dark'] as Appearance[]) {
      blocks.push(
        `:root[data-brand="${id}"][data-theme="${appearance}"]{${declarations(
          BRANDS[id][appearance],
          BRANDS[id].radii,
          BRANDS[id].font,
        )}}`,
      );
    }
  }
  return blocks.join('\n');
}

// ---------------------------------------------------------------------------
// ONE NEUTRAL RAMP, and it is the app's.
//
// There used to be DOC_LIGHT and SLIDE_LIGHT here, plus DocExtras and SlideExtras,
// and between them they described three neutral ramps: the app's violet-tinted
// inks, Tailwind's greys in the HTML/PDF/Word path, and a third set in the deck.
// Worse, each surface mixed two of them INSIDE one document — body text #1f2937
// with a section heading one line below in #191826, a screenshot border #e5e7eb
// inside a card border #e7e4f2. Invisible, because each pair sits within a few
// percent, which is exactly why it survived.
//
// Phase 0 was defined as no-visual-change, so it recorded all of that rather than
// fixing it. This is the fix. The mixing was never a decision: the export
// stylesheet was written separately against Tailwind defaults, a few values were
// later pulled from the app side, and nobody reconciled them. Preserving it means
// preserving an accident, and it would mean authoring two neutral ramps for every
// future brand when the LFI guide defines one.
//
// Three role names disappeared with it rather than being kept as duplicates:
// sectionH, sectionB and cardBd became identical to ink, ink2 and hair. A section
// heading IS primary text; a second name for one role is how the sets drifted
// apart in the first place. The export vocabulary got smaller.
//
// RETIRED_GREYS below is what keeps it that way.
// ---------------------------------------------------------------------------

/**
 * The greys the exports used to carry, and must never carry again.
 *
 * A source test asserts none of these appears anywhere in the export path or the
 * app stylesheet. An empty divergence list would have said the same thing more
 * weakly: this fails on the specific values, so a partial revert of one file is
 * caught rather than averaging out.
 */
export const RETIRED_GREYS: readonly string[] = [
  '#1f2937', // was doc ink       -> #191826
  '#374151', // was doc ink-2     -> #5a5772
  '#6b7280', // was doc ink-3     -> #6f6c88
  '#e5e7eb', // was doc hair      -> #e7e4f2
  '#cbd5e1', // was doc/slide control-bd -> #cbc7db
  '#14161f', // was slide ink     -> #191826
  '#525a6e', // was slide ink-2   -> #5a5772
  '#8b91a3', // was slide ink-3   -> #6f6c88
];

// ---------------------------------------------------------------------------
// Geometry that has to agree between the report and the exports (#77 phase 0b).
// ---------------------------------------------------------------------------

/**
 * The document CARD radius the exports render: the step card and the overview.
 *
 * No longer a number of its own — it reads the default brand's `card` role, so
 * the report and the export cannot drift apart, and a brand that changes its
 * geometry changes both. Phase 4 replaces these two with a theme threaded into
 * the exporters, at which point an export follows whatever brand it was asked
 * for rather than always the default.
 */
export const CARD_RADIUS_PX = BRANDS[DEFAULT_BRAND].radii.card;

/**
 * The radius of the SCREENSHOT nested inside a step card.
 *
 * Smaller than the card on purpose, on every brand. An inner frame sharing its
 * parent radius reads as a mistake at the corner, because the visible gap
 * between the two curves narrows to nothing; the inner radius wants to be
 * roughly the outer minus the padding between them.
 *
 * An earlier pass collapsed both into one number and asserted them EQUAL, which
 * encoded a "one radius everywhere" premise that is false and would have been
 * the foundation this scale was built on. The tests now assert the relationship
 * instead, on every brand.
 */
export const IMAGE_RADIUS_PX = BRANDS[DEFAULT_BRAND].radii.figure;

/**
 * `RRGGBB` with no leading `#`, which is what the `docx` and `pptxgenjs` APIs take.
 *
 * Uppercased because both libraries emit uppercase in their own output, so a mixed
 * file is harder to diff. Throws on anything that is not a 6-digit hex colour rather
 * than passing a malformed value into a document: `docx` silently renders black,
 * which is a colour bug that looks like a design decision.
 */
export function hexNoHash(value: string): string {
  const m = /^#([0-9a-fA-F]{6})$/.exec(value);
  if (!m) throw new Error(`hexNoHash: expected #rrggbb, got ${JSON.stringify(value)}`);
  return m[1].toUpperCase();
}
