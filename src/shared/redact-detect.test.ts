import { describe, it, expect } from 'vitest';
import { detectSensitiveRects, type OcrLine } from './redact-detect';

// Build a one-line OCR input from space-separated words, giving each word a
// synthetic bbox so multi-word matches (e.g. a spaced card number) union cleanly.
function line(...words: string[]): OcrLine {
  let x = 0;
  return {
    words: words.map((text) => {
      const w = { text, bbox: { x0: x, y0: 0, x1: x + text.length * 8, y1: 16 } };
      x += text.length * 8 + 8;
      return w;
    }),
  };
}

describe('detectSensitiveRects', () => {
  it('detects a US SSN', () => {
    expect(detectSensitiveRects([line('123-45-6789')]).length).toBe(1);
  });

  it('detects a Luhn-valid credit card (spaced across words)', () => {
    expect(detectSensitiveRects([line('4111', '1111', '1111', '1111')]).length).toBe(1);
  });

  it('ignores a non-Luhn long digit run (e.g. an order number)', () => {
    expect(detectSensitiveRects([line('1111', '1111', '1111', '1111')]).length).toBe(0);
  });

  it('detects an sk- style API key', () => {
    expect(detectSensitiveRects([line('sk-abcdEFGH1234567890wxyz')]).length).toBe(1);
  });

  it('does not flag a benign name', () => {
    expect(detectSensitiveRects([line('John', 'Smith')]).length).toBe(0);
  });
});
