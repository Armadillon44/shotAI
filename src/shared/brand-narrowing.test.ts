// #89 — `in` walks the prototype chain, so every Object.prototype member passed
// as a valid BrandId. The failure was inverted: a garbage value that happens to
// be a prototype key was KEPT and crashed downstream, while a legitimate unknown
// brand was correctly rejected and then discarded.
import { describe, it, expect } from 'vitest';
import { BRANDS, DEFAULT_BRAND, isBrandId, coerceBrand, brandPalette } from './theme-palette';
// plainFontStackFor lives in export-theme, and reaches BRANDS through coerceBrand —
// so it is the downstream consumer that actually threw.
import { plainFontStackFor } from './export-theme';

const PROTOTYPE_KEYS = [
  'toString', 'constructor', 'valueOf', 'hasOwnProperty', 'isPrototypeOf',
  'propertyIsEnumerable', 'toLocaleString', '__proto__', '__defineGetter__',
];

describe('brand narrowing rejects prototype members', () => {
  it('accepts exactly the real brands', () => {
    for (const id of Object.keys(BRANDS)) expect(isBrandId(id), id).toBe(true);
  });

  it.each(PROTOTYPE_KEYS)('rejects %s', (key) => {
    expect(isBrandId(key)).toBe(false);
    expect(coerceBrand(key)).toBe(DEFAULT_BRAND);
  });

  /// The downstream consequence, which is what made this more than a type nit:
  /// BRANDS['toString'] is a Function, so the palette lookup returned undefined
  /// and the font stack threw.
  it.each(PROTOTYPE_KEYS)('%s still yields a usable palette and font stack', (key) => {
    expect(brandPalette(key as never, 'light')).toEqual(BRANDS[DEFAULT_BRAND].light);
    expect(() => plainFontStackFor(key as never)).not.toThrow();
  });

  /// The control: a plausible FUTURE brand must still be rejected, or the fix
  /// would just be accepting everything.
  it('still rejects an unrecognised brand name', () => {
    expect(isBrandId('solarpunk')).toBe(false);
    expect(coerceBrand('solarpunk')).toBe(DEFAULT_BRAND);
  });

  it('rejects non-strings without throwing', () => {
    for (const v of [42, null, undefined, {}, [], true]) {
      expect(isBrandId(v)).toBe(false);
      expect(coerceBrand(v)).toBe(DEFAULT_BRAND);
    }
  });
});
