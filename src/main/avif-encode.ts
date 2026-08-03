// AVIF encoding for the styled HTML export (#56).
//
// WHY AVIF and not something already available: the styled export exists largely to
// be pasted into a Freshservice KB article, and that editor has a TOTAL PAYLOAD
// ceiling — it re-uploads every pasted image and falls over when handed too much at
// once. Confirmed the hard way: PNG (3.12 MB), WebP @2x (524 KB) and JPEG at both 2x
// (1.02 MB) and 1x (414 KB) all fail to paste, while the macOS app's AVIF (~0.16 MB)
// works. AVIF is the only codec that gets a full SOP under that ceiling.
//
// Electron cannot do it natively: `nativeImage` writes only png/jpeg, and Chromium
// can DECODE avif but not encode it — `canvas.toDataURL('image/avif')` silently
// returns PNG (verified on Electron 42 / Chrome 148). So this uses @jsquash/avif,
// which is libavif compiled to WASM (from Google's Squoosh) — no native module, no
// per-platform binaries, no build step.
//
// Measured on a real 13-step SOP at the 2x embed width: ~10 KB/image, 168 KB of
// base64 for the whole document (macOS's working export is ~164 KB), 11.4 s to
// encode all 13. See HTML_IMG_AVIF_SPEED for why speed 7.
import { promises as fs } from 'node:fs';
import path from 'node:path';
import { pathToFileURL } from 'node:url';
import { mainLog } from './logger';

/** The `encode(data, opts)` default export of @jsquash/avif/encode. */
type EncodeFn = (
  data: { data: Uint8ClampedArray; width: number; height: number },
  opts?: { quality?: number; speed?: number },
) => Promise<ArrayBuffer>;

interface JsquashModule {
  default: EncodeFn;
  init: (opts: { wasmBinary: ArrayBufferLike }) => Promise<unknown>;
}

/**
 * Resolved once and reused. `null` means "this build can't encode AVIF" — cached so
 * a broken install costs one failed attempt per process, not one per image.
 */
let encoder: EncodeFn | null | undefined;
let loading: Promise<EncodeFn | null> | null = null;

/**
 * Candidate locations for the encoder's WASM. `require.resolve` finds it in dev and
 * in an unpacked app; the `app.asar` -> `app.asar.unpacked` rewrite covers a packaged
 * build (forge.config.ts unpacks it, since a WASM module can't be instantiated from
 * inside an asar as reliably as a plain read).
 */
function wasmCandidates(): string[] {
  const out: string[] = [];
  try {
    const encodeJs = require.resolve('@jsquash/avif/encode.js');
    const wasm = path.join(path.dirname(encodeJs), 'codec', 'enc', 'avif_enc.wasm');
    out.push(wasm);
    if (wasm.includes(`app.asar${path.sep}`)) {
      out.push(wasm.replace(`app.asar${path.sep}`, `app.asar.unpacked${path.sep}`));
    }
  } catch {
    /* package missing entirely — handled by the caller's fallback */
  }
  return out;
}

/** The module URL to dynamic-import, preferring the unpacked copy when packaged. */
function encoderUrls(): string[] {
  const out: string[] = [];
  try {
    const encodeJs = require.resolve('@jsquash/avif/encode.js');
    if (encodeJs.includes(`app.asar${path.sep}`)) {
      out.push(encodeJs.replace(`app.asar${path.sep}`, `app.asar.unpacked${path.sep}`));
    }
    out.push(encodeJs);
  } catch {
    /* handled below */
  }
  return out;
}

/**
 * Load the WASM encoder once. Returns null (and logs) if anything is missing, so the
 * caller can fall back to a format `nativeImage` can write.
 */
async function load(): Promise<EncodeFn | null> {
  if (encoder !== undefined) return encoder;
  if (loading) return loading;
  loading = (async () => {
    try {
      let mod: JsquashModule | null = null;
      for (const p of encoderUrls()) {
        try {
          // The package is ESM; the main process is CJS, hence the dynamic import.
          mod = (await import(pathToFileURL(p).href)) as JsquashModule;
          break;
        } catch {
          /* try the next candidate */
        }
      }
      if (!mod?.init || typeof mod.default !== 'function') {
        mainLog.warn('avif: @jsquash/avif could not be imported; falling back');
        encoder = null;
        return null;
      }
      // Node has no fetch-for-file, so the .wasm has to be handed over explicitly.
      let bytes: Buffer | null = null;
      for (const c of wasmCandidates()) {
        try {
          bytes = await fs.readFile(c);
          break;
        } catch {
          /* try the next candidate */
        }
      }
      if (!bytes) {
        mainLog.warn('avif: avif_enc.wasm not found; falling back');
        encoder = null;
        return null;
      }
      // Hand over just this Buffer's bytes — a Buffer can be a view into a larger
      // pooled ArrayBuffer, so passing .buffer directly could include foreign bytes.
      await mod.init({
        wasmBinary: bytes.buffer.slice(
          bytes.byteOffset,
          bytes.byteOffset + bytes.byteLength,
        ) as ArrayBuffer,
      });
      encoder = mod.default;
      return encoder;
    } catch (e) {
      mainLog.warn('avif: encoder init failed; falling back:', e);
      encoder = null;
      return null;
    } finally {
      loading = null;
    }
  })();
  return loading;
}

/**
 * Encode straight RGBA pixels as AVIF, or null to fall back.
 *
 * `rgba` must be non-premultiplied RGBA (see toRgba in export.ts — nativeImage hands
 * out BGRA). Runs strictly AFTER the fail-closed redaction gate, on already
 * redaction-baked pixels, so it can only lose detail, never recover a redacted region.
 */
export async function encodeAvif(
  rgba: Uint8ClampedArray,
  width: number,
  height: number,
  quality: number,
  speed: number,
): Promise<Buffer | null> {
  if (width < 1 || height < 1) return null;
  const encode = await load();
  if (!encode) return null;
  try {
    const out = await encode({ data: rgba, width, height }, { quality, speed });
    const buf = Buffer.from(out);
    // Cheap sanity check on the container: ISOBMFF 'ftyp' box with an av1/avif brand.
    const looksAvif =
      buf.length > 16 &&
      buf.toString('ascii', 4, 8) === 'ftyp' &&
      /avif|avis|av01|mif1|miaf/.test(buf.toString('ascii', 8, 24));
    if (!looksAvif) {
      mainLog.warn('avif: encoder returned something that is not AVIF; falling back');
      return null;
    }
    return buf;
  } catch (e) {
    mainLog.warn('avif: encode failed; falling back:', e);
    return null;
  }
}
