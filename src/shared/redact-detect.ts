// Detect likely-sensitive text in OCR output and map it to pixel rectangles to
// redact. BEST-EFFORT assist on top of the manual redaction gate — OCR misses
// stylized/low-res text, so a missed match is a missed redaction; the human
// review (the editor) stays authoritative. Pure + dependency-free so it can be
// unit-tested. Detector set (deliberately conservative to limit noise): US SSN,
// credit-card numbers (Luhn-validated), and API keys / tokens. Email + phone are
// intentionally excluded (too noisy).
import type { Rect } from './project';

/** One OCR word with its bounding box (image px; x0,y0 = top-left). */
export interface OcrWord {
  text: string;
  bbox: { x0: number; y0: number; x1: number; y1: number };
}
/** One OCR line — words are matched within a line so multi-word numbers
 *  (e.g. "4111 1111 1111 1111") are caught. */
export interface OcrLine {
  words: OcrWord[];
}

const SSN = /\b\d{3}-\d{2}-\d{4}\b/g;
// 13–19 digits with optional single space/dash separators — Luhn-validated below.
const CARD = /\b(?:\d[ -]?){13,19}\b/g;
const API_PATTERNS: RegExp[] = [
  /\bsk-[A-Za-z0-9_-]{16,}\b/g, // OpenAI / Anthropic-style secret keys
  /\bAKIA[0-9A-Z]{12,20}\b/g, // AWS access key id (canonical 16 trailing; range tolerates OCR drift)
  /\bgh[posru]_[A-Za-z0-9]{20,}\b/g, // GitHub tokens
  /\bxox[baprs]-[A-Za-z0-9-]{10,}\b/g, // Slack tokens
  /\b[0-9a-fA-F]{40,}\b/g, // long hex (hashes / hex secrets)
  /\b[A-Za-z0-9+/]{40,}={0,2}\b/g, // long base64-ish blobs
];

/** Luhn checksum — used to keep credit-card detection from firing on arbitrary
 *  long digit runs (order numbers, ids). */
function luhn(digits: string): boolean {
  let sum = 0;
  let alt = false;
  for (let i = digits.length - 1; i >= 0; i--) {
    let d = digits.charCodeAt(i) - 48;
    if (d < 0 || d > 9) return false;
    if (alt) {
      d *= 2;
      if (d > 9) d -= 9;
    }
    sum += d;
    alt = !alt;
  }
  return sum % 10 === 0;
}

/** Union of two rects. */
function union(a: Rect, b: Rect): Rect {
  const x = Math.min(a.x, b.x);
  const y = Math.min(a.y, b.y);
  const right = Math.max(a.x + a.width, b.x + b.width);
  const bottom = Math.max(a.y + a.height, b.y + b.height);
  return { x, y, width: right - x, height: bottom - y };
}

function overlaps(a: Rect, b: Rect): boolean {
  return (
    a.x < b.x + b.width &&
    a.x + a.width > b.x &&
    a.y < b.y + b.height &&
    a.y + a.height > b.y
  );
}

/** Merge overlapping rects so the same region isn't redacted twice. */
function mergeRects(rects: Rect[]): Rect[] {
  const out: Rect[] = [];
  for (const r of rects) {
    let merged = r;
    for (let i = out.length - 1; i >= 0; i--) {
      if (overlaps(out[i], merged)) {
        merged = union(out.splice(i, 1)[0], merged);
      }
    }
    out.push(merged);
  }
  return out;
}

/**
 * Find sensitive substrings across OCR lines and return padded image-px rects
 * covering them. Each line's words are joined into one string (tracking each
 * word's character span) so a match is mapped back to the covering words and
 * their bounding boxes are unioned.
 */
export function detectSensitiveRects(lines: OcrLine[], pad = 4): Rect[] {
  const rects: Rect[] = [];

  for (const line of lines) {
    if (!line.words.length) continue;
    // Build the joined line text + per-word [start,end) char spans.
    const spans: { start: number; end: number; w: OcrWord }[] = [];
    let text = '';
    for (const w of line.words) {
      if (text) text += ' ';
      const start = text.length;
      text += w.text;
      spans.push({ start, end: text.length, w });
    }

    const addMatch = (mStart: number, mEnd: number) => {
      const covered = spans.filter((s) => s.start < mEnd && s.end > mStart).map((s) => s.w);
      if (!covered.length) return;
      const x0 = Math.min(...covered.map((w) => w.bbox.x0));
      const y0 = Math.min(...covered.map((w) => w.bbox.y0));
      const x1 = Math.max(...covered.map((w) => w.bbox.x1));
      const y1 = Math.max(...covered.map((w) => w.bbox.y1));
      rects.push({
        x: x0 - pad,
        y: y0 - pad,
        width: x1 - x0 + pad * 2,
        height: y1 - y0 + pad * 2,
      });
    };

    const run = (re: RegExp, validate?: (m: string) => boolean) => {
      re.lastIndex = 0;
      for (const m of text.matchAll(re)) {
        if (m.index === undefined) continue;
        if (validate && !validate(m[0])) continue;
        addMatch(m.index, m.index + m[0].length);
      }
    };

    run(SSN);
    for (const re of API_PATTERNS) run(re);
    run(CARD, (s) => {
      const digits = s.replace(/\D/g, '');
      return digits.length >= 13 && digits.length <= 19 && luhn(digits);
    });
  }

  return mergeRects(rects);
}
