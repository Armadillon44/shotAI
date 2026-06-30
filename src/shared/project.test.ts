import { describe, it, expect } from 'vitest';
import { parseRect, parsePoint } from './project';

describe('parseRect', () => {
  it('accepts a valid rect and strips extra fields', () => {
    expect(parseRect({ x: 1, y: 2, width: 3, height: 4, evil: '../x' })).toEqual({
      x: 1,
      y: 2,
      width: 3,
      height: 4,
    });
  });

  it.each([
    ['missing height', { x: 1, y: 2, width: 3 }],
    ['NaN', { x: 1, y: 2, width: 3, height: NaN }],
    ['Infinity', { x: 1, y: 2, width: 3, height: Infinity }],
    ['string field', { x: '1', y: 2, width: 3, height: 4 }],
    ['null', null],
    ['non-object', 'nope'],
  ])('rejects %s', (_label, input) => {
    expect(parseRect(input)).toBeNull();
  });
});

describe('parsePoint', () => {
  it('accepts a valid point', () => {
    expect(parsePoint({ x: 5, y: 6 })).toEqual({ x: 5, y: 6 });
  });

  it.each([
    ['missing y', { x: 5 }],
    ['string y', { x: 5, y: '6' }],
    ['undefined', undefined],
  ])('rejects %s', (_label, input) => {
    expect(parsePoint(input)).toBeNull();
  });
});
