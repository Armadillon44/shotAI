import { describe, it, expect } from 'vitest';
import { bucketFor, groupByDate, DATE_BUCKET_ORDER } from './date-groups';

// Reference "now": Wed 2026-07-22, 10:00 local. 2026-07-01 is a Wednesday, so the
// current week starts Sun 2026-07-19 and the prior week starts Sun 2026-07-12.
// Boundaries: This Week ≥ Jul 19 · Last Week Jul 12–18 · This Month Jul 1–11 ·
// Last Month Jun 1–30 · Older < Jun 1.
const NOW = new Date(2026, 6, 22, 10, 0, 0);
const at = (y: number, m: number, d: number) => new Date(y, m - 1, d, 12, 0, 0).getTime();

describe('bucketFor', () => {
  it('places the current week under This Week (incl. the Sunday start)', () => {
    expect(bucketFor(at(2026, 7, 22), NOW)).toBe('This Week'); // today
    expect(bucketFor(at(2026, 7, 20), NOW)).toBe('This Week');
    expect(bucketFor(at(2026, 7, 19), NOW)).toBe('This Week'); // week start (Sun)
  });
  it('places the prior week under Last Week', () => {
    expect(bucketFor(at(2026, 7, 18), NOW)).toBe('Last Week');
    expect(bucketFor(at(2026, 7, 12), NOW)).toBe('Last Week'); // its Sunday
  });
  it('places earlier-this-month dates under This Month', () => {
    expect(bucketFor(at(2026, 7, 11), NOW)).toBe('This Month');
    expect(bucketFor(at(2026, 7, 1), NOW)).toBe('This Month'); // month start
  });
  it('places the previous calendar month under Last Month', () => {
    expect(bucketFor(at(2026, 6, 30), NOW)).toBe('Last Month');
    expect(bucketFor(at(2026, 6, 1), NOW)).toBe('Last Month');
  });
  it('places anything older under Older, and handles bad input', () => {
    expect(bucketFor(at(2026, 5, 31), NOW)).toBe('Older');
    expect(bucketFor(at(2025, 1, 1), NOW)).toBe('Older');
    expect(bucketFor(NaN, NOW)).toBe('Older');
  });
  it('buckets are mutually exclusive across all five spans', () => {
    const samples = [
      at(2026, 7, 22), // This Week
      at(2026, 7, 14), // Last Week
      at(2026, 7, 5), // This Month
      at(2026, 6, 15), // Last Month
      at(2026, 3, 1), // Older
    ];
    const labels = samples.map((t) => bucketFor(t, NOW));
    expect(new Set(labels).size).toBe(5);
    expect(labels).toEqual([...DATE_BUCKET_ORDER]);
  });
});

describe('groupByDate', () => {
  it('emits only non-empty buckets in canonical order, preserving item order', () => {
    const items = [
      { id: 'a', t: at(2026, 7, 22) }, // This Week
      { id: 'b', t: at(2026, 7, 20) }, // This Week
      { id: 'c', t: at(2026, 6, 15) }, // Last Month
    ];
    const groups = groupByDate(items, (i) => i.t, NOW);
    expect(groups.map((g) => g.label)).toEqual(['This Week', 'Last Month']);
    expect(groups[0].items.map((i) => i.id)).toEqual(['a', 'b']);
    expect(groups[1].items.map((i) => i.id)).toEqual(['c']);
  });
  it('returns an empty array for no items', () => {
    expect(groupByDate([], () => 0, NOW)).toEqual([]);
  });
});
