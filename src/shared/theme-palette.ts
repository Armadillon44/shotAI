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
