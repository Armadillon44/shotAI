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

/** A brand, in both appearances. */
export interface Brand {
  /** Shown in Settings. */
  label: string;
  light: Palette;
  dark: Palette;
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
export const BRANDS: Record<BrandId, Brand> = {
  shotAI: {
    label: 'shotAI',
    light: {
      accent: '#6344f1',
      accentPress: '#5233d4',
      accentTint: '#efeafe',
      accentInk: '#4a34c9',
      onAccent: '#ffffff',
      ink: '#191826',
      ink2: '#5a5772',
      ink3: '#6f6c88',
      hair: '#e7e4f2',
      hair2: '#efedf7',
      controlBd: '#cbc7db',
      surface: '#ffffff',
      surface2: '#faf9ff',
      ground: '#f5f4fb',
      fieldBg: '#ffffff',
      ok: '#0e9f6e',
      okTint: '#e7f7ef',
      okInk: '#07724f',
      draft: '#c77d16',
      draftTint: '#fbf1e0',
      draftInk: '#8a5610',
      danger: '#dc2626',
      dangerInk: '#b91c1c',
      dangerTint: '#fef2f2',
      dangerBd: '#f0c2c2',
      noteBg: '#ecfdf5',
      noteBd: '#6ee7b7',
      noteFg: '#065f46',
      cautBg: '#fffbeb',
      cautBd: '#fcd34d',
      cautFg: '#92400e',
      warnBg: '#fef2f2',
      warnBd: '#fca5a5',
      warnFg: '#991b1b',
    },
    dark: {
      accent: '#9a8bf7',
      accentPress: '#b0a4fa',
      accentTint: '#241f3a',
      accentInk: '#c8bdfb',
      onAccent: '#171528',
      ink: '#ece9f7',
      ink2: '#a8a4c0',
      ink3: '#8e8aa8',
      hair: '#302c42',
      hair2: '#282539',
      controlBd: '#3c3852',
      surface: '#1b1926',
      surface2: '#211f2e',
      ground: '#121019',
      fieldBg: '#211f2e',
      ok: '#34d399',
      okTint: '#12271e',
      okInk: '#6ee7b7',
      draft: '#e0a355',
      draftTint: '#2a2113',
      draftInk: '#f0c98a',
      danger: '#f87171',
      dangerInk: '#fca5a5',
      dangerTint: '#2a1414',
      dangerBd: '#5a2a2a',
      noteBg: '#10281f',
      noteBd: '#2f6f52',
      noteFg: '#8ee7bf',
      cautBg: '#2a2113',
      cautBd: '#7a5c1e',
      cautFg: '#f0c98a',
      warnBg: '#2a1414',
      warnBd: '#7a3a3a',
      warnFg: '#f6b0b0',
    },
  },
  /**
   * LaCrosse Footwear corporate: charcoal and warm neutrals carry the surface,
   * rust is a focused pop rather than a fill.
   *
   * Transcribed from macOS `BrandPalette.lfi`, which is the source of truth for
   * the design; the derivations behind three of these are worth knowing:
   *
   * - **Success is forest `#3e7d5a`, not the guide's chart olive.** LFI defines no
   *   green. The olive `#7d846d` was tried first and read as ambiguous for
   *   "success"; forest measures 6.83:1 against its own tint where the olive
   *   managed 5.56:1. The Note callout rides the same ramp.
   * - **`ink3` is `#756c5c`, not the guide's taupe `#938978`.** The taupe measures
   *   3.45:1 on white, and this role carries the export's date line and OVERVIEW
   *   eyebrow, in documents that get printed. `#756c5c` is 5.18:1 and keeps the
   *   warm cast.
   * - **`fieldBg` stays LIGHTER than `surface` in dark mode**, or a text input on
   *   an elevated card stops reading as an input.
   *
   * WINDOWS-ONLY ROLE: `dangerBd` has no macOS counterpart (its palette has no
   * such token). Derived here as danger lightened toward the surface, the same
   * relationship shotAI's `#f0c2c2` has to its `#dc2626`. Flagged on #77 so macOS
   * can adopt a matching value if that border ever reaches a shared surface; it
   * does not today, since it is app chrome only.
   */
  lfi: {
    label: 'LFI',
    light: {
      accent: '#b46b3e',
      accentPress: '#9a5a33',
      accentTint: '#f6ede5',
      accentInk: '#8f5430',
      onAccent: '#ffffff',
      ink: '#47443e',
      ink2: '#6f695f',
      ink3: '#756c5c',
      hair: '#d8d2c6',
      hair2: '#e7e2d7',
      controlBd: '#c9c1b3',
      surface: '#ffffff',
      surface2: '#faf8f3',
      ground: '#f5f2eb',
      fieldBg: '#ffffff',
      ok: '#3e7d5a',
      okTint: '#e9f1eb',
      okInk: '#2b5b40',
      draft: '#c79a72',
      draftTint: '#f7efe6',
      draftInk: '#8a5f35',
      danger: '#9d3f32',
      dangerInk: '#7f3227',
      dangerTint: '#f7eae7',
      dangerBd: '#ddbcb7',
      noteBg: '#e9f1eb',
      noteBd: '#3e7d5a',
      noteFg: '#2b5b40',
      cautBg: '#f7efe6',
      cautBd: '#c79a72',
      cautFg: '#8a5f35',
      warnBg: '#f7eae7',
      warnBd: '#c97f72',
      warnFg: '#7f3227',
    },
    dark: {
      accent: '#d58b5c',
      accentPress: '#e3a579',
      accentTint: '#3a2e25',
      accentInk: '#e3a579',
      onAccent: '#211f1c',
      ink: '#f8f4ec',
      ink2: '#cfc7b8',
      ink3: '#b5aa99',
      hair: '#4a463f',
      hair2: '#3f3c36',
      controlBd: '#686258',
      surface: '#3a3833',
      surface2: '#43403a',
      ground: '#2f2d29',
      fieldBg: '#4a4740',
      ok: '#6fb089',
      okTint: '#23302a',
      okInk: '#9bceb1',
      draft: '#d5ae89',
      draftTint: '#332a21',
      draftInk: '#e0c09e',
      danger: '#c96253',
      dangerInk: '#e0897b',
      dangerTint: '#33211e',
      dangerBd: '#5a332c',
      noteBg: '#23302a',
      noteBd: '#6fb089',
      noteFg: '#9bceb1',
      cautBg: '#332a21',
      cautBd: '#d5ae89',
      cautFg: '#e0c09e',
      warnBg: '#33211e',
      warnBd: '#c96253',
      warnFg: '#e0897b',
    },
  },
};

/** Every brand id, in the order Settings offers them. */
export const BRAND_IDS = Object.keys(BRANDS) as BrandId[];

/** The brand a project or the app falls back to when nothing is set. */
export const DEFAULT_BRAND: BrandId = 'shotAI';

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

/** One `--token:#value` list. */
function declarations(p: Palette): string {
  return (Object.keys(ROLE_TO_TOKEN) as (keyof Palette)[])
    .map((role) => `--${ROLE_TO_TOKEN[role]}:${p[role]}`)
    .join(';');
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
  const blocks = [`:root{${declarations(BRANDS[DEFAULT_BRAND].light)}}`];
  for (const id of BRAND_IDS) {
    for (const appearance of ['light', 'dark'] as Appearance[]) {
      blocks.push(
        `:root[data-brand="${id}"][data-theme="${appearance}"]{${declarations(
          BRANDS[id][appearance],
        )}}`,
      );
    }
  }
  return blocks.join('\n');
}

/**
 * The palette the HTML exports render today, and therefore the PDF too, since the
 * PDF is printed from the same document. Also what `.docx` uses.
 *
 * Identical to the app except for the five neutrals recorded in KNOWN_DIVERGENCES.
 * Spreading APP_LIGHT and overriding only those five is deliberate: a role added to
 * the app in future automatically reaches the exports, instead of silently defaulting
 * to whatever a second hand-written copy happened to say.
 */
export const DOC_LIGHT: Palette = {
  ...APP_LIGHT,
  ink: '#1f2937',
  ink2: '#374151',
  ink3: '#6b7280',
  hair: '#e5e7eb',
  controlBd: '#cbd5e1',
};

/**
 * The palette the PowerPoint export renders today: a third neutral ramp, and no
 * brand accent anywhere in the deck.
 *
 * `accent` is left at the brand value even though the current deck never draws it,
 * so that when phase 3 brands the slides there is a correct value to reach for
 * rather than an invented one.
 */
export const SLIDE_LIGHT: Palette = {
  ...APP_LIGHT,
  ink: '#14161f',
  ink2: '#525a6e',
  ink3: '#8b91a3',
  controlBd: '#cbd5e1',
};

// ---------------------------------------------------------------------------
// Export-only roles, and the reason they have to exist.
//
// Extracting the call sites found something the surface-level table above cannot
// express: each export surface mixes BOTH neutral ramps INTERNALLY. It is not that
// the export uses grey where the app uses violet; it is that the export uses grey
// for some elements and violet for others, in the same document.
//
// Proof, from the shipped code rather than inference:
//   export-docx.ts:131  section rule   hexNoHash(DOC_LIGHT.hair)  -> E5E7EB
//   export-docx.ts:37   card border    CARD_BORDER                -> E7E4F2
// Two hairlines in one Word document. The HTML export does the same: body text is
// #1f2937 while a section heading one line below it is #191826, and the screenshot
// border is #e5e7eb inside a card border of #e7e4f2.
//
// So these roles are named for WHAT THEY COLOUR, not for a ramp, and valued at
// exactly what ships. Giving them names is what makes the inconsistency reviewable;
// folding them into `hair`/`ink` would either change shipped output or hide the
// split behind a role whose value is right in one place and wrong in another.

/** Roles the HTML and Word exports need beyond the shared vocabulary. */
export interface DocExtras {
  /** Overview, section and step CARD border. Renders the APP hair (#e7e4f2), while
   *  the screenshot border and <hr> one element away render DOC hair (#e5e7eb). */
  cardBd: string;
  /** Section-divider heading. Renders the APP ink, unlike body text. */
  sectionH: string;
  /** Section-divider body. Renders the APP ink-2, unlike the overview body. */
  sectionB: string;
}

export const DOC_EXTRAS: DocExtras = {
  cardBd: '#e7e4f2',
  sectionH: '#191826',
  sectionB: '#5a5772',
};

/** Roles the PowerPoint export needs beyond the shared vocabulary. */
export interface SlideExtras {
  /** Step body and footer text. Renders the DOC ink-2, not the slide ink-2. */
  bodyInk: string;
  /** The centred caption on a text-only slide. Renders the DOC ink-3. */
  captionInk: string;
}

export const SLIDE_EXTRAS: SlideExtras = {
  bodyInk: '#374151',
  captionInk: '#6b7280',
};

/**
 * Where a single surface renders TWO values for what is conceptually one role.
 *
 * Distinct from KNOWN_DIVERGENCES, which is about a surface differing from the app.
 * These are internal contradictions: a reader looking at one exported document sees
 * both values, one element apart. Recorded, not fixed, for the same reason as the
 * rest of phase 0: converging them changes shipped output.
 */
export const INTERNAL_SPLITS: readonly {
  surface: SurfaceId;
  concept: string;
  values: readonly string[];
  note: string;
}[] = [
  {
    surface: 'doc',
    concept: 'hairline',
    values: ['#e7e4f2', '#e5e7eb'],
    note: 'Card borders use the app hair; the screenshot border and <hr> use the doc hair. Both appear in every exported SOP that has a section divider.',
  },
  {
    surface: 'doc',
    concept: 'body ink',
    values: ['#1f2937', '#191826'],
    note: 'Body text is #1f2937 but a section-divider heading is #191826, so two blacks appear within a line of each other.',
  },
  {
    surface: 'doc',
    concept: 'secondary ink',
    values: ['#374151', '#5a5772'],
    note: 'The overview body and a section-divider body are different greys.',
  },
  {
    surface: 'slide',
    concept: 'body ink',
    values: ['#525a6e', '#374151'],
    note: 'Slide headings use the slide ramp while step body and footer text use the doc ramp, so the deck mixes two palettes.',
  },
  {
    surface: 'slide',
    concept: 'secondary ink',
    values: ['#8b91a3', '#6b7280'],
    note: 'The text-slide caption uses the doc ink-3 rather than the slide ink-3.',
  },
];
// ---------------------------------------------------------------------------
// Geometry that has to agree between the report and the exports (#77 phase 0b).
// ---------------------------------------------------------------------------

/**
 * Corner radius of a document CARD: the step card and the overview card.
 *
 * Shared between the report and the export because the report is meant to be WYSIWYG
 * with what it exports, and before this the two surfaces disagreed in a way nobody
 * had noticed: the step card was 12px on both, the overview card 8px on both, and the
 *
 * #77 phase 0b describes this as "export card 12 -> 10 to match the on-screen report,
 * which is 10 today". That premise was wrong, and checking it is what found the real
 * divergence: the 10px on screen belongs to the SCREENSHOT WRAP, not the step card.
 * The cards already agreed at 12, so making the export card 10 alone would have
 * broken an agreement and left the actual gap in place. Settled by choosing the
 * issue's other stated goal for the CARDS. The nested image keeps its own, smaller
 * value: see IMAGE_RADIUS_PX.
 *
 * NOT applied to dialogs, menus or the SOP panel, which also use 12px and 8px but
 * are app chrome rather than document cards. A document's shape should not be
 * decided by a modal's.
 */
export const CARD_RADIUS_PX = 10;

/**
 * Corner radius of the SCREENSHOT frame nested inside a step card.
 *
 * Smaller than the card on purpose. An inner frame sharing its parent radius reads
 * as a mistake at the corner, because the visible gap between the two curves
 narrows to nothing; the inner radius wants to be roughly the outer minus the
 * padding between them. So this is not a second copy of the card radius, it is a
 * different quantity that happens to be nearby.
 *
 * My first pass at phase 0b collapsed both into one number and asserted them equal,
 * which encoded a "one radius everywhere" premise that is wrong and would have been
 * the foundation phase 2 built its radius scale on. Caught in review before that.
 */
export const IMAGE_RADIUS_PX = 8;

/** Which surface a divergence belongs to. */
export type SurfaceId = 'doc' | 'slide';

export interface Divergence {
  role: keyof Palette;
  surface: SurfaceId;
  /** What the app renders. */
  app: string;
  /** What that surface renders instead, today. */
  surfaceValue: string;
  /** Why it is recorded rather than fixed. */
  note: string;
}

/**
 * Every place an export surface deliberately-for-now renders a different value than
 * the app, at the exact values shipped today.
 *
 * This list IS the phase-0 deliverable. The parity test asserts that a surface value
 * either equals the app's or appears here, so a SIXTH divergence cannot be added
 * without either fixing it or admitting it in writing.
 *
 * None of these is defensible on its own; they are the residue of the export
 * stylesheets having been written from a generic grey palette while the app was
 * given a violet-tinted one. Converging them is phase 3 work because it changes
 * shipped output.
 */
export const KNOWN_DIVERGENCES: readonly Divergence[] = [
  {
    role: 'ink',
    surface: 'doc',
    app: '#191826',
    surfaceValue: '#1f2937',
    note: 'Export body text. Grey-blue where the app is violet-black.',
  },
  {
    role: 'ink2',
    surface: 'doc',
    app: '#5a5772',
    surfaceValue: '#374151',
    note: 'Overview body and blockquote text. Noticeably darker than the app.',
  },
  {
    role: 'ink3',
    surface: 'doc',
    app: '#6f6c88',
    surfaceValue: '#6b7280',
    note: 'Meta line and the OVERVIEW eyebrow. Greyer than the app, and now within a quarter of a stop of it after the app value was darkened to meet AA.',
  },
  {
    role: 'hair',
    surface: 'doc',
    app: '#e7e4f2',
    surfaceValue: '#e5e7eb',
    note: 'Screenshot border and <hr>. Two values two units apart, from two palettes.',
  },
  {
    role: 'controlBd',
    surface: 'doc',
    app: '#cbc7db',
    surfaceValue: '#cbd5e1',
    note: 'Plain-export blockquote rule.',
  },
  {
    role: 'ink',
    surface: 'slide',
    app: '#191826',
    surfaceValue: '#14161f',
    note: 'Slide titles and step headings. A third ink, darker than either other surface.',
  },
  {
    role: 'ink2',
    surface: 'slide',
    app: '#5a5772',
    surfaceValue: '#525a6e',
    note: 'Slide body text.',
  },
  {
    role: 'ink3',
    surface: 'slide',
    app: '#6f6c88',
    surfaceValue: '#8b91a3',
    note: 'Slide footer / secondary text. Now LIGHTER than the app value rather than darker, since the app value was darkened to meet AA.',
  },
  {
    role: 'controlBd',
    surface: 'slide',
    app: '#cbc7db',
    surfaceValue: '#cbd5e1',
    note: 'Slide card outline.',
  },
];

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

/** The palette a surface renders today. */
export function paletteFor(surface: SurfaceId | 'app'): Palette {
  if (surface === 'doc') return DOC_LIGHT;
  if (surface === 'slide') return SLIDE_LIGHT;
  return APP_LIGHT;
}
