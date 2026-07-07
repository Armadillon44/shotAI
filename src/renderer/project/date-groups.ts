// F4 — bucket projects by a date into dynamic, mutually-exclusive spans for the
// home list when sorted by date. Pure (no React/electron) so it's unit-tested.

export type DateBucket = 'This Week' | 'Last Week' | 'This Month' | 'Last Month' | 'Older';

/** Canonical newest→oldest order of the buckets. */
export const DATE_BUCKET_ORDER: readonly DateBucket[] = [
  'This Week',
  'Last Week',
  'This Month',
  'Last Month',
  'Older',
];

/** Local midnight at the start of the week (Sunday) containing `d`. */
function startOfWeek(d: Date): Date {
  const x = new Date(d.getFullYear(), d.getMonth(), d.getDate());
  x.setDate(x.getDate() - x.getDay()); // getDay(): 0 = Sunday
  return x;
}

/**
 * Which span a timestamp (ms) falls in, relative to `now`. Buckets are checked in
 * order and are mutually exclusive: the week buckets win over the month buckets,
 * so a date in the current week reads as "This Week", not "This Month". A future
 * or non-finite timestamp lands in "This Week" / "Older" respectively. All
 * boundaries use calendar arithmetic (DST/month-safe), not fixed ms offsets.
 */
export function bucketFor(ts: number, now: Date): DateBucket {
  if (!Number.isFinite(ts)) return 'Older';
  const tw = startOfWeek(now);
  const thisWeek = tw.getTime();
  const lastWeek = new Date(tw.getFullYear(), tw.getMonth(), tw.getDate() - 7).getTime();
  const thisMonth = new Date(now.getFullYear(), now.getMonth(), 1).getTime();
  const lastMonth = new Date(now.getFullYear(), now.getMonth() - 1, 1).getTime();
  if (ts >= thisWeek) return 'This Week';
  if (ts >= lastWeek) return 'Last Week';
  if (ts >= thisMonth) return 'This Month';
  if (ts >= lastMonth) return 'Last Month';
  return 'Older';
}

/**
 * Group `items` (already sorted by the caller) into date buckets, preserving each
 * item's order within its bucket and emitting only non-empty buckets in canonical
 * newest→oldest order. `getTs` extracts the ms timestamp to bucket on.
 */
export function groupByDate<T>(
  items: readonly T[],
  getTs: (item: T) => number,
  now: Date,
): { label: DateBucket; items: T[] }[] {
  const map = new Map<DateBucket, T[]>();
  for (const it of items) {
    const b = bucketFor(getTs(it), now);
    const arr = map.get(b);
    if (arr) arr.push(it);
    else map.set(b, [it]);
  }
  return DATE_BUCKET_ORDER.filter((b) => map.has(b)).map((label) => ({
    label,
    items: map.get(label) as T[],
  }));
}
