// Stylesheets for the two HTML export varieties. Kept in their own module with NO
// electron/native imports so the layout invariants below can be unit-tested under
// plain node (see vitest.config.ts). export.ts owns the markup; this owns the CSS.

/**
 * The document column, in px. `.doc` pads 32px each side, so 880 − 64 = 816 keeps
 * the rendered column identical to the old `.doc{max-width:880px}`.
 *
 * A step card's content column — what a screenshot is displayed at — is
 * 816 − 30 (badge) − 16 (gap) − 32 (card padding) = 738. **That is
 * HTML_IMG_MAX_W in export-geometry.ts; keep the two in sync.** (Measured: the
 * card's 1px borders make the real content box 736 under `box-sizing:border-box`;
 * see the note on HTML_IMG_MAX_W for why 738 is kept regardless.)
 */
import { HTML_COL_BASE, docWidths } from '../shared/doc-scale';
import { DOC_LIGHT, DOC_EXTRAS, CARD_RADIUS_PX, IMAGE_RADIUS_PX } from '../shared/theme-palette';

/** The column at scale 1. Per-scale widths come from docWidths(scale). */
export const HTML_COL_W = HTML_COL_BASE;

/** Plain-export body width at scale 1 (the "HTML for Word" document). */
export const PLAIN_BODY_W = 800;

/**
 * Every top-level block that must hold the document column. The width is repeated
 * on each one instead of living on a wrapper — see the DOC_CSS note below before
 * changing this.
 */
const COL_BLOCKS = ['.doc__title', '.doc__meta', '.doc__intro', '.step', '.section'];

/**
 * The report stylesheet — drives BOTH the `.html` export and the PDF (via
 * htmlToPdf). Step framing (#40): every step is a distinct CARD — the number/glyph
 * badge sits in a left gutter, and a tinted rounded card (.step__main) holds the
 * content to its right. Callouts are the same card, tinted by kind. Mirrors the
 * in-app report and the macOS port. Every colour below is read from DOC_LIGHT in
 * shared/theme-palette, which is the one palette the exports render with — so these
 * stylesheets are still light-only.
 *
 * **The 816px column is repeated on EVERY top-level block, deliberately (#57).**
 * Read this before "simplifying" it back onto a `.doc` wrapper — that IS the bug.
 *
 * Pasting this document into a Freshservice KB article (Froala) does three things,
 * confirmed by reading the saved article's Code View — none of it is documented,
 * and two fixes based on inference failed before this was established:
 *
 *   1. **Whole-document wrappers are UNWRAPPED.** `<main class="doc">` was gone,
 *      and so was a nested `<div class="doc__col">` on a second attempt — the body
 *      began straight at `<h1 class="doc__title">`. So no wrapper, at any depth,
 *      can be trusted to carry the width. (A `<div>` sitting among siblings
 *      mid-document IS kept; only wrappers enclosing the whole fragment are
 *      flattened. That asymmetry is what made this confusing.)
 *   2. **Every other element survives with its computed styles inlined** —
 *      including `.step{display:flex}` and `.step__main{flex:1 1 auto}`. That
 *      `flex-grow` is the actual mechanism that stretched a card to the editor's
 *      full width.
 *   3. **`<img>` loses `max-width`** and gains Froala's own `fr-fic fr-dib`
 *      classes. Image dimensions therefore live in width/height ATTRIBUTES, set by
 *      the exporter, not in this stylesheet.
 *
 * So each block self-constrains and self-centers, and the document lays out the
 * same whether it's opened as a file or pasted. `.section` keeps its rule aligned
 * with the card (not the number gutter) via `.section__inner`, because inner
 * elements do survive the paste.
 *
 * Layout tables are ruled out for this: the destination forces `table{width:100%}`,
 * and `<table width="880">`, a `width` on the `<td>`, and `<center>` + table were
 * all probed and all came back full width.
 */
/**
 * The styled-export stylesheet at a given document scale (#70).
 *
 * A FUNCTION, not a constant, because the column width is now per project. The
 * five top-level blocks each carry the column (see the note above), so they must
 * all be built from the same computed value; a constant plus a per-call override
 * would let one block drift and only show up as a misaligned section divider.
 *
 * Chrome and font sizes are deliberately NOT scaled: only the column and the
 * image inside it. That is why the image width is re-derived in doc-scale rather
 * than multiplied.
 */
export function docCss(scale = 1): string {
  const COL = docWidths(scale).htmlCol;
  return `
*{box-sizing:border-box}
html{-webkit-print-color-adjust:exact;print-color-adjust:exact}
body{margin:0;font-family:-apple-system,"Segoe UI",Roboto,Helvetica,Arial,sans-serif;color:${DOC_LIGHT.ink};background:${DOC_LIGHT.surface};line-height:1.6}
.doc{padding:40px 32px 64px}
.doc__title{max-width:${COL}px;margin:0 auto 4px;font-size:1.9rem;line-height:1.25}
.doc__meta{max-width:${COL}px;margin:0 auto 28px;color:${DOC_LIGHT.ink3};font-size:.85rem}
.doc__intro{max-width:${COL}px;margin:0 auto 28px;padding:14px 18px;border:1px solid ${DOC_EXTRAS.cardBd};border-left:4px solid ${DOC_LIGHT.accent};border-radius:${CARD_RADIUS_PX}px;background:${DOC_LIGHT.accentTint}}
.doc__intro-eyebrow{text-transform:uppercase;letter-spacing:.6px;font-size:.7rem;font-weight:700;color:${DOC_LIGHT.ink3};margin:0 0 6px}
.doc__intro-h{margin:0 0 6px;font-size:1.15rem}
.doc__intro-b{margin:0;color:${DOC_LIGHT.ink2};white-space:pre-wrap}
/* The 46px left pad is the step gutter (30px badge + 16px gap), so a section's
   rule and text align with the step CONTENT column rather than the badge. The
   rule lives on .section__inner because the padding and the width can't share a
   box once .section carries the column. Values match the macOS export. */
.section{max-width:${COL}px;margin:28px auto 4px;padding-left:46px;break-inside:avoid}
.section__inner{padding:14px 16px 0;border-top:2px solid ${DOC_EXTRAS.cardBd}}
.section__h{font-size:1.2rem;font-weight:700;margin:0 0 4px;color:${DOC_EXTRAS.sectionH}}
.section__b{margin:0;color:${DOC_EXTRAS.sectionB};white-space:pre-wrap}
.step{display:flex;gap:16px;max-width:${COL}px;margin:0 auto 18px;align-items:flex-start;page-break-inside:avoid;break-inside:avoid}
.step__num{flex:0 0 auto;width:30px;height:30px;margin-top:14px;border-radius:50%;background:${DOC_LIGHT.accent};color:${DOC_LIGHT.onAccent};font-weight:600;display:flex;align-items:center;justify-content:center;font-size:.95rem}
.step__num--note{background:${DOC_LIGHT.noteBg};color:${DOC_LIGHT.noteFg};border:1px solid ${DOC_LIGHT.noteBd}}
.step__num--caution{background:${DOC_LIGHT.cautBg};color:${DOC_LIGHT.cautFg};border:1px solid ${DOC_LIGHT.cautBd}}
.step__num--warning{background:${DOC_LIGHT.warnBg};color:${DOC_LIGHT.warnFg};border:1px solid ${DOC_LIGHT.warnBd}}
.step__main{flex:1 1 auto;min-width:0;padding:14px 16px;border:1px solid ${DOC_EXTRAS.cardBd};border-radius:${CARD_RADIUS_PX}px;background:${DOC_LIGHT.surface2}}
.step__main--note{background:${DOC_LIGHT.noteBg};border-color:${DOC_LIGHT.noteBd};color:${DOC_LIGHT.noteFg}}
.step__main--caution{background:${DOC_LIGHT.cautBg};border-color:${DOC_LIGHT.cautBd};color:${DOC_LIGHT.cautFg}}
.step__main--warning{background:${DOC_LIGHT.warnBg};border-color:${DOC_LIGHT.warnBd};color:${DOC_LIGHT.warnFg}}
.step__title{font-size:1.15rem;margin:0 0 10px}
.step__img{display:block;max-width:100%;height:auto;margin-inline:auto;border:1px solid ${DOC_LIGHT.hair};border-radius:${IMAGE_RADIUS_PX}px}
.step__instr{margin:10px 0 0;white-space:pre-wrap;font-size:1.02rem}
.step--textonly .step__instr{margin-top:0}
.callout__h{display:block;font-weight:700;margin-bottom:.25rem}
.callout__b{white-space:pre-wrap}
/* Print/PDF spans the page: lift the column off every block that carries it. */
@media print{.doc{padding:0 6px}${COL_BLOCKS.join(',')}{max-width:none}}
`.trim();
}

/**
 * Minimal Arial stylesheet for the plain "HTML (for Word)" export — enough
 * bold / spacing formatting so it reads well on its own, while staying plain
 * enough to paste into Word / Google Docs (they honor these basic tags/styles).
 *
 * Note `img{max-width:100%}` is for reading the FILE only. Word, Google Docs and
 * KB editors all drop it on paste, which is why the exporter also emits width and
 * height attributes (see htmlImageSize).
 */
/** The plain "HTML (for Word)" stylesheet at a given document scale (#70). */
export function plainCss(scale = 1): string {
  const BODY = Math.round(PLAIN_BODY_W * (docWidths(scale).htmlCol / HTML_COL_BASE));
  return [
    `body{font-family:Arial,Helvetica,sans-serif;color:${DOC_LIGHT.ink};line-height:1.5;max-width:${BODY}px;margin:24px auto;padding:0 20px}`,
    'h1{font-size:1.8rem;font-weight:700;margin:0 0 .3rem}',
    'h2{font-size:1.2rem;font-weight:700;margin:1.3rem 0 .4rem}',
    'p{margin:.5rem 0}',
    'strong{font-weight:700}',
    'img{max-width:100%;height:auto}',
    `blockquote{margin:1rem 0;padding:.4rem .85rem;border-left:3px solid ${DOC_LIGHT.controlBd};color:${DOC_LIGHT.ink2}}`,
    `hr{border:0;border-top:1px solid ${DOC_LIGHT.hair};margin:1.4rem 0}`,
  ].join('');
}

/** The blocks that must each carry the column — exported for the invariant test. */
export const COLUMN_BLOCK_SELECTORS: readonly string[] = COL_BLOCKS;
