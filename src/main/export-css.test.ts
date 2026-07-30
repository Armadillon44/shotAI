import { describe, it, expect } from 'vitest';
import { COLUMN_BLOCK_SELECTORS, DOC_CSS, HTML_COL_W, PLAIN_CSS } from './export-css';
import { HTML_IMG_MAX_W } from './export-geometry';

// The blocks that must each carry the document column, written out HERE on purpose.
// Do NOT rewrite these loops to iterate COLUMN_BLOCK_SELECTORS instead: that list is
// exported by the module under test, so deleting a block from it would silently
// shrink what gets asserted and let the exact regression below ship green. This is
// the independent spec, taken from issue #57.
const EXPECTED_COL_BLOCKS = [
  '.doc__title',
  '.doc__meta',
  '.doc__intro',
  '.step',
  '.section',
] as const;

/**
 * The body of the FIRST `<selector>{...}` rule for an exact selector — i.e. the
 * screen rule. First, not last, deliberately: the `@media print` block at the end
 * re-mentions several of these selectors (`.doc{padding:0 6px}` among them), so
 * taking the last match would assert against the PRINT rule and let a regression in
 * the screen rule pass. Requiring `{` immediately after the selector also stops
 * `.step` matching `.step__num{` and `.section` matching `.section__inner{`.
 */
function ruleBody(css: string, selector: string): string | null {
  return new RegExp(`\\${selector}\\{([^}]*)\\}`).exec(css)?.[1] ?? null;
}

// These guard the fix for #57. The styled HTML export has to survive being PASTED
// into a Freshservice KB article, whose editor unwraps whole-document wrappers but
// keeps every other element with its computed styles inlined — so
// `.step__main{flex:1 1 auto}` stretched each card to the editor's full width. The
// fix is that every top-level block carries the column itself. That repetition looks
// redundant, and the obvious "cleanup" is to hoist it back onto a wrapper, which is
// exactly the regression these assert against.
describe('DOC_CSS paste-survival invariants', () => {
  it('exports a block list that matches the spec (guards the tests below)', () => {
    expect([...COLUMN_BLOCK_SELECTORS].sort()).toEqual([...EXPECTED_COL_BLOCKS].sort());
  });

  it('puts the column on EVERY top-level block, not on a wrapper', () => {
    for (const sel of EXPECTED_COL_BLOCKS) {
      const body = ruleBody(DOC_CSS, sel);
      expect(body, `${sel} has no rule in DOC_CSS`).not.toBeNull();
      expect(body, `${sel} must carry max-width:${HTML_COL_W}px`).toContain(
        `max-width:${HTML_COL_W}px`,
      );
    }
  });

  it('each column block centers itself (margin auto), since no wrapper can', () => {
    for (const sel of EXPECTED_COL_BLOCKS) {
      const body = ruleBody(DOC_CSS, sel) ?? '';
      expect(body, `${sel} must center itself`).toMatch(/margin:[^;]*auto/);
    }
  });

  it('does NOT put the column on the .doc wrapper (it gets unwrapped on paste)', () => {
    const doc = ruleBody(DOC_CSS, '.doc');
    expect(doc).not.toBeNull();
    expect(doc).not.toContain('max-width');
    expect(doc).toContain('padding'); // it only pads
  });

  it('keeps the section rule on an inner element, which survives a paste', () => {
    const section = ruleBody(DOC_CSS, '.section') ?? '';
    expect(section).toContain(`max-width:${HTML_COL_W}px`);
    expect(section).toContain('padding-left:46px'); // the step gutter
    expect(section).toContain('break-inside:avoid'); // preserved from before
    expect(section).not.toContain('border-top'); // moved to the inner div
    const inner = ruleBody(DOC_CSS, '.section__inner') ?? '';
    expect(inner).toContain('border-top:2px solid #e7e4f2');
  });

  it('lifts the column off every block for print, so the PDF still spans the page', () => {
    // Find the rule inside @media print that sets max-width:none and check its
    // SELECTOR LIST — asserting the selector merely appears somewhere in the media
    // query would pass even with a block left capped.
    const print = /@media print\{(.*)\}\s*$/.exec(DOC_CSS)?.[1] ?? '';
    expect(print, 'no @media print block').not.toBe('');
    const lifted = /([^{}]*)\{max-width:none\}/.exec(print)?.[1] ?? '';
    const liftedList = lifted.split(',').map((s) => s.trim());
    for (const sel of EXPECTED_COL_BLOCKS) {
      expect(liftedList, `print must reset ${sel} to max-width:none`).toContain(sel);
    }
  });

  it('sizes images to the card content column', () => {
    // 816 − 30 (badge) − 16 (gap) − 32 (card padding) = 738.
    expect(HTML_IMG_MAX_W).toBe(HTML_COL_W - 30 - 16 - 32);
    // MEASURED: .step__main's 1px borders eat the content box under
    // box-sizing:border-box, so an image actually lays out at 736. Embedding 738
    // over-supplies by 2px, which is the safe direction (never upscaled) and is the
    // macOS constant.
  });
});

describe('PLAIN_CSS', () => {
  it('is Arial-first and constrains images for reading the file', () => {
    expect(PLAIN_CSS).toContain('font-family:Arial');
    expect(PLAIN_CSS).toContain('img{max-width:100%;height:auto}');
  });
});
