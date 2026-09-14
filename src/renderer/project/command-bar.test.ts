import { describe, it, expect } from 'vitest';
import fs from 'node:fs';

// The project command bar (.detail__baractions) and the export dropdown that
// hangs off it.
//
// Both invariants here come from one live bug. Adding a per-project Brand
// <select> to the bar made it WRAP, which put the Export button at the left
// margin of a second row — and the export menu, anchored `right: 0` so it opens
// leftward, then rendered at a negative x and was completely unreachable.
//
// The wrap was the trigger; the menu was the defect. It had always been
// reachable only because its button happened to sit near the window's right
// edge, which is a property of the layout, not of the menu.

const read = (f: string): string => fs.readFileSync(f, 'utf8');
const TSX = 'src/renderer/project/ProjectDetail.tsx';
const CSS = 'src/renderer/project/project.css';

/** The declarations inside one `selector { … }` of project.css. */
function rule(sel: string): string {
  const css = read(CSS);
  const at = css.indexOf(sel + ' {');
  expect(at, `${sel} not found in project.css`).toBeGreaterThan(-1);
  return css.slice(at, css.indexOf('}', at));
}

describe('the export menu opens somewhere the user can reach it', () => {
  it('measures the trigger before choosing a side', () => {
    // A CSS-only rule cannot know where the button ended up. The measurement is
    // the fix; the class is only how it is expressed.
    const src = read(TSX);
    expect(src, 'the open handler should measure the trigger').toMatch(
      /getBoundingClientRect\(\)/,
    );
    expect(src).toMatch(/setExportMenuLeft\(\s*rect\.right - EXPORT_MENU_MIN_W < EXPORT_MENU_GUTTER/);
  });

  it('keeps its width assumption in step with the stylesheet', () => {
    // The flip decision is arithmetic on the menu's width. If the CSS min-width
    // grows and this constant does not, the menu starts overflowing again in a
    // band the arithmetic says is fine — and silently, since nothing throws.
    const m = /const EXPORT_MENU_MIN_W = (\d+);/.exec(read(TSX));
    expect(m, 'EXPORT_MENU_MIN_W should be declared in ProjectDetail.tsx').not.toBeNull();
    const css = /min-width:\s*(\d+)px/.exec(rule('.export__menu'));
    expect(css, '.export__menu should declare a min-width').not.toBeNull();
    expect(Number(m?.[1]), 'EXPORT_MENU_MIN_W vs .export__menu min-width').toBe(
      Number(css?.[1]),
    );
  });

  it('has a rule for the flipped side that really flips it', () => {
    // `left: 0` alone is not enough: `right: 0` is still in effect from the base
    // rule, and a box with both set stretches instead of moving.
    const flipped = rule('.export__menu--left');
    expect(flipped).toMatch(/left:\s*0/);
    expect(flipped, 'right must be released or the menu stretches').toMatch(/right:\s*auto/);
  });
});

describe('the command bar holds its controls on one row', () => {
  it('does not carry the per-project brand control', () => {
    // Moved to View -> Brand. It was the widest single control in the bar — a
    // label plus a <select> sized to "App default (shotAI)" — and it is the one
    // that pushed Export onto a second row.
    const src = read(TSX);
    expect(src, 'BrandPicker should be gone from the command bar').not.toContain('BrandPicker');
    expect(src, 'no brand <select> should remain here').not.toContain('Document brand');
  });

  it('still wraps rather than clipping, if it ever runs out of room', () => {
    // flex-wrap stays. Overflowing controls that vanish would be worse than a
    // second row, and the export menu now survives being on one.
    expect(rule('.detail__baractions')).toMatch(/flex-wrap:\s*wrap/);
  });
});
