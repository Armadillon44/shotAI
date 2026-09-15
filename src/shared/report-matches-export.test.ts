// #81 — the in-app step card was 820 wide against the export's 816, so a figure
// was 4px wider on screen than in the file the app produced. WYSIWYG between the
// report and the export is the property both apps claim, and this was the only
// place breaking it.
//
// macOS pins the same invariant (DocScaleTests.reportFigureMatchesExport).
import { describe, it, expect } from 'vitest';
import {
  SCALE_STEPS, docWidths, REPORT_COL_BASE, HTML_COL_BASE, REP_FRAME_BASE, HTML_DOC_PAD,
} from './doc-scale';

describe('the report renders at the width it exports', () => {
  it.each(SCALE_STEPS)('report column === export column at scale %s', (s) => {
    const w = docWidths(s);
    expect(w.reportCol).toBe(w.htmlCol);
  });

  /// The bases must be the same NUMBER, not merely equal today — two constants
  /// that happen to agree are exactly how this drifted in the first place.
  it('derives the report column from the export column', () => {
    expect(REPORT_COL_BASE).toBe(HTML_COL_BASE);
  });

  /// Padding is CHROME. Scaling the whole frame scaled the padding with it, so
  /// the frame grew faster than the content inside. At scale 1 both spellings
  /// give 880, which is what hid it.
  it.each(SCALE_STEPS)('frame is the scaled column plus FIXED padding at scale %s', (s) => {
    const w = docWidths(s);
    expect(w.repFrame).toBe(w.htmlCol + HTML_DOC_PAD * 2);
  });

  it('still opens at 880 at scale 1, so nothing moves for existing projects', () => {
    expect(REP_FRAME_BASE).toBe(880);
    expect(docWidths(1).repFrame).toBe(880);
    expect(docWidths(1).reportCol).toBe(816);
  });

  /// The old spelling, stated so the difference is visible rather than implied.
  it('no longer scales the padding, which the old frame did', () => {
    expect(docWidths(1.25).repFrame).toBe(1084);
    expect(Math.round(880 * 1.25)).toBe(1100); // what it used to be
  });

  /// The image ceiling already derived from the export column, so it must be
  /// unchanged by this — a regression here would mean the fix moved more than
  /// intended.
  it.each(SCALE_STEPS)('leaves the image ceiling alone at scale %s', (s) => {
    expect(docWidths(s).htmlImgMax).toBe(Math.max(120, Math.round(816 * s) - 78));
  });
});
