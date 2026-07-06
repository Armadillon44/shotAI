// UI-element-at-point (Windows) via the native element-locator dll, loaded with
// koffi (FFI). Best-effort + non-fatal: if the dll/koffi can't load, or a query
// fails/times out, we return null and capture falls back to window-name captions.
//
// The native call runs ASYNCHRONOUSLY (koffi .async → libuv worker thread) so it
// (a) doesn't block/jank the main thread and (b) runs COM as MTA on a worker
// thread instead of touching Electron's STA main thread.
import fs from 'node:fs';
import path from 'node:path';
import { app } from 'electron';
import type { StepElement } from '../shared/project';
import { captureLog } from './logger';

type ElementFn = ((x: number, y: number, out: Buffer, cap: number) => number) & {
  async: (
    x: number,
    y: number,
    out: Buffer,
    cap: number,
    cb: (err: Error | null, res: number) => void,
  ) => void;
};

const QUERY_TIMEOUT_MS = 600;
const BUF_SIZE = 8192;

let loadPromise: Promise<ElementFn | null> | null = null;

/** Candidate dll locations: dev (repo path) and packaged (Forge extraResource
 *  flattens the file into resources/). */
function dllPath(): string | null {
  const candidates = [
    path.join(app.getAppPath(), 'native', 'element-locator', 'element_locator.dll'),
    process.resourcesPath ? path.join(process.resourcesPath, 'element_locator.dll') : '',
  ].filter(Boolean);
  for (const p of candidates) {
    try {
      if (fs.existsSync(p)) return p;
    } catch {
      /* ignore */
    }
  }
  return null;
}

function ensureLoaded(): Promise<ElementFn | null> {
  if (loadPromise) return loadPromise;
  loadPromise = (async () => {
    if (process.platform !== 'win32') return null;
    try {
      const dll = dllPath();
      if (!dll) {
        captureLog.warn('element-locator: dll not found — element names disabled');
        return null;
      }
      const mod = (await import('koffi')) as unknown as { default?: unknown };
      const koffi = (mod.default ?? mod) as { load: (p: string) => { func: (proto: string) => ElementFn } };
      const lib = koffi.load(dll);
      const fn = lib.func('int element_at_point(int x, int y, uint8_t *out, int cap)');
      captureLog.info(`element-locator: loaded ${dll}`);
      return fn;
    } catch (e) {
      captureLog.warn('element-locator: failed to load — element names disabled:', e);
      return null;
    }
  })();
  return loadPromise;
}

/** Pre-load the native dll (koffi + LoadLibrary) so the FIRST click of a
 *  recording isn't delayed ~seconds while it loads lazily. Best-effort. */
export function warmUpElementLocator(): void {
  void ensureLoaded();
}

interface RawElement {
  name: string;
  controlType: string;
  controlTypeId: number;
  className: string;
  actionable: boolean;
  x: number;
  y: number;
  w: number;
  h: number;
}

/**
 * Resolve the actionable UI element under a screen point (global physical px).
 * Returns null when unavailable (best-effort). `name` is only populated for
 * label-bearing control types (the native side's allowlist), so a field's or
 * document's text content is never surfaced.
 */
export async function getElementAtPoint(x: number, y: number): Promise<StepElement | null> {
  const fn = await ensureLoaded();
  if (!fn) return null;
  const buf = Buffer.alloc(BUF_SIZE);
  const n = await new Promise<number>((resolve) => {
    let settled = false;
    const finish = (v: number) => {
      if (settled) return;
      settled = true;
      clearTimeout(timer);
      resolve(v);
    };
    const timer = setTimeout(() => finish(-1), QUERY_TIMEOUT_MS);
    try {
      fn.async(x, y, buf, BUF_SIZE, (err, res) => finish(err ? -1 : res));
    } catch {
      finish(-1);
    }
  });
  if (n <= 0 || n > BUF_SIZE) return null;
  try {
    const o = JSON.parse(buf.toString('utf8', 0, n)) as RawElement;
    const name = o.actionable && o.name ? o.name : null;
    return {
      available: !!name,
      name,
      controlType: o.controlType ?? null,
      bounds: { x: o.x, y: o.y, width: o.w, height: o.h },
    };
  } catch {
    return null;
  }
}
