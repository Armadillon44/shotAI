// Local OCR (Tesseract.js, WASM — no native build) for the auto-redaction
// pre-scan. Runs in the main process: reads a screenshot, recognizes word boxes,
// and returns image-px rects over likely-sensitive text (SSN / credit-card /
// API key). Best-effort + non-fatal — any failure returns [] and the manual
// redaction gate (the editor) remains the real guarantee.
//
// The eng language data downloads once into userData/tessdata and is cached
// there, so subsequent scans work offline. The worker is created lazily and
// reused across scans.
import path from 'node:path';
import { app } from 'electron';
import type { Rect } from '../shared/project';
import { detectSensitiveRects, type OcrLine, type OcrWord } from '../shared/redact-detect';
import { ocrLog } from './logger';

interface OcrWorker {
  recognize(
    image: string,
    options?: unknown,
    output?: { blocks?: boolean },
  ): Promise<{ data: unknown }>;
  terminate(): Promise<unknown>;
}

// Minimal shape of the Tesseract result we traverse (block→paragraph→line→word).
interface TData {
  blocks?: {
    paragraphs?: { lines?: { words?: OcrWord[] }[] }[];
  }[];
}

let workerPromise: Promise<OcrWorker> | null = null;

function getWorker(): Promise<OcrWorker> {
  if (workerPromise) return workerPromise;
  workerPromise = (async () => {
    const tesseract = (await import('tesseract.js')) as unknown as {
      createWorker: (lang: string, oem: number, opts: { cachePath: string }) => Promise<OcrWorker>;
    };
    const cachePath = path.join(app.getPath('userData'), 'tessdata');
    ocrLog.info(`initializing OCR worker (lang cache: ${cachePath})`);
    return tesseract.createWorker('eng', 1, { cachePath });
  })();
  // If init fails, clear the memo so a later scan can retry.
  workerPromise.catch(() => {
    workerPromise = null;
  });
  return workerPromise;
}

/**
 * OCR an image file and return padded image-px rects over detected sensitive
 * text. Returns [] on any failure (best-effort).
 */
export async function scanForSensitiveRects(imagePath: string): Promise<Rect[]> {
  try {
    const worker = await getWorker();
    const result = await worker.recognize(imagePath, {}, { blocks: true });
    const data = result.data as TData;
    const lines: OcrLine[] = [];
    for (const blk of data.blocks ?? []) {
      for (const par of blk.paragraphs ?? []) {
        for (const ln of par.lines ?? []) {
          const words = (ln.words ?? []).map((w) => ({ text: w.text, bbox: w.bbox }));
          if (words.length) lines.push({ words });
        }
      }
    }
    const rects = detectSensitiveRects(lines);
    ocrLog.info(
      `auto-redact: ${lines.length} line(s), ${rects.length} sensitive region(s) in ${path.basename(imagePath)}`,
    );
    return rects;
  } catch (e) {
    ocrLog.warn('auto-redact: OCR scan failed (best-effort, skipping):', e);
    return [];
  }
}
