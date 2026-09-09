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
 * The app's light palette, mirroring `:root` in project.css EXACTLY.
 *
 * theme-palette.test.ts parses that stylesheet and asserts every value here matches,
 * so this cannot drift from what the app actually renders.
 */
export const APP_LIGHT: Palette = {
  accent: '#6344f1',
  accentPress: '#5233d4',
  accentTint: '#efeafe',
  accentInk: '#4a34c9',
  onAccent: '#ffffff',
  ink: '#191826',
  ink2: '#5a5772',
  ink3: '#918ea6',
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
};

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
    app: '#918ea6',
    surfaceValue: '#6b7280',
    note: 'Meta line and the OVERVIEW eyebrow. Darker and greyer than the app.',
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
    app: '#918ea6',
    surfaceValue: '#8b91a3',
    note: 'Slide footer / secondary text.',
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
