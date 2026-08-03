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
export const HTML_COL_W = 816;

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
 * in-app report and the macOS port. Light-only (exports don't theme).
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
export const DOC_CSS = `
*{box-sizing:border-box}
html{-webkit-print-color-adjust:exact;print-color-adjust:exact}
body{margin:0;font-family:-apple-system,"Segoe UI",Roboto,Helvetica,Arial,sans-serif;color:#1f2937;background:#fff;line-height:1.6}
.doc{padding:40px 32px 64px}
.doc__title{max-width:${HTML_COL_W}px;margin:0 auto 4px;font-size:1.9rem;line-height:1.25}
.doc__meta{max-width:${HTML_COL_W}px;margin:0 auto 28px;color:#6b7280;font-size:.85rem}
.doc__intro{max-width:${HTML_COL_W}px;margin:0 auto 28px;padding:14px 18px;border:1px solid #e7e4f2;border-left:4px solid #6344f1;border-radius:8px;background:#efeafe}
.doc__intro-eyebrow{text-transform:uppercase;letter-spacing:.6px;font-size:.7rem;font-weight:700;color:#6b7280;margin:0 0 6px}
.doc__intro-h{margin:0 0 6px;font-size:1.15rem}
.doc__intro-b{margin:0;color:#374151;white-space:pre-wrap}
/* The 46px left pad is the step gutter (30px badge + 16px gap), so a section's
   rule and text align with the step CONTENT column rather than the badge. The
   rule lives on .section__inner because the padding and the width can't share a
   box once .section carries the column. Values match the macOS export. */
.section{max-width:${HTML_COL_W}px;margin:28px auto 4px;padding-left:46px;break-inside:avoid}
.section__inner{padding:14px 16px 0;border-top:2px solid #e7e4f2}
.section__h{font-size:1.2rem;font-weight:700;margin:0 0 4px;color:#191826}
.section__b{margin:0;color:#5a5772;white-space:pre-wrap}
.step{display:flex;gap:16px;max-width:${HTML_COL_W}px;margin:0 auto 18px;align-items:flex-start;page-break-inside:avoid;break-inside:avoid}
.step__num{flex:0 0 auto;width:30px;height:30px;margin-top:14px;border-radius:50%;background:#6344f1;color:#fff;font-weight:600;display:flex;align-items:center;justify-content:center;font-size:.95rem}
.step__num--note{background:#ecfdf5;color:#065f46;border:1px solid #6ee7b7}
.step__num--caution{background:#fffbeb;color:#92400e;border:1px solid #fcd34d}
.step__num--warning{background:#fef2f2;color:#991b1b;border:1px solid #fca5a5}
.step__main{flex:1 1 auto;min-width:0;padding:14px 16px;border:1px solid #e7e4f2;border-radius:12px;background:#faf9ff}
.step__main--note{background:#ecfdf5;border-color:#6ee7b7;color:#065f46}
.step__main--caution{background:#fffbeb;border-color:#fcd34d;color:#92400e}
.step__main--warning{background:#fef2f2;border-color:#fca5a5;color:#991b1b}
.step__title{font-size:1.15rem;margin:0 0 10px}
.step__img{display:block;max-width:100%;height:auto;margin-inline:auto;border:1px solid #e5e7eb;border-radius:8px}
.step__instr{margin:10px 0 0;white-space:pre-wrap;font-size:1.02rem}
.step--textonly .step__instr{margin-top:0}
.callout__h{display:block;font-weight:700;margin-bottom:.25rem}
.callout__b{white-space:pre-wrap}
/* Print/PDF spans the page: lift the column off every block that carries it. */
@media print{.doc{padding:0 6px}${COL_BLOCKS.join(',')}{max-width:none}}
`.trim();

/**
 * Minimal Arial stylesheet for the plain "HTML (for Word)" export — enough
 * bold / spacing formatting so it reads well on its own, while staying plain
 * enough to paste into Word / Google Docs (they honor these basic tags/styles).
 *
 * Note `img{max-width:100%}` is for reading the FILE only. Word, Google Docs and
 * KB editors all drop it on paste, which is why the exporter also emits width and
 * height attributes (see htmlImageSize).
 */
export const PLAIN_CSS = [
  'body{font-family:Arial,Helvetica,sans-serif;color:#1f2937;line-height:1.5;max-width:800px;margin:24px auto;padding:0 20px}',
  'h1{font-size:1.8rem;font-weight:700;margin:0 0 .3rem}',
  'h2{font-size:1.2rem;font-weight:700;margin:1.3rem 0 .4rem}',
  'p{margin:.5rem 0}',
  'strong{font-weight:700}',
  'img{max-width:100%;height:auto}',
  'blockquote{margin:1rem 0;padding:.4rem .85rem;border-left:3px solid #cbd5e1;color:#374151}',
  'hr{border:0;border-top:1px solid #e5e7eb;margin:1.4rem 0}',
].join('');

/** The blocks that must each carry the column — exported for the invariant test. */
export const COLUMN_BLOCK_SELECTORS: readonly string[] = COL_BLOCKS;
