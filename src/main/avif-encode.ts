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
 * Where the encoder lives: whatever `require.resolve` says, in dev and when packaged
 * alike. Inside an asar is fine — verified on Electron 42 that both the dynamic ESM
 * import of encode.js and the `fs` read of its .wasm work from within the archive.
 *
 * Do NOT "improve" this by unpacking the package and preferring an
 * app.asar.unpacked path: encode.js statically imports `wasm-feature-detect`, npm
 * hoists that to node_modules/, and app.asar.unpacked is not asar-redirected — so an
 * unpacked copy resolves but then dies with ERR_MODULE_NOT_FOUND on the sibling. That
 * was tried; see the note in forge.config.ts.
 */
function encoderPaths(): { module: string; wasm: string } | null {
  try {
    const module = require.resolve('@jsquash/avif/encode.js');
    return { module, wasm: path.join(path.dirname(module), 'codec', 'enc', 'avif_enc.wasm') };
  } catch (e) {
    mainLog.warn('avif: @jsquash/avif is not installed:', e);
    return null;
  }
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
      const paths = encoderPaths();
      if (!paths) {
        encoder = null;
        return null;
      }
      // The package is ESM; the main process is CJS, hence the dynamic import.
      const mod = (await import(pathToFileURL(paths.module).href)) as JsquashModule;
      if (!mod?.init || typeof mod.default !== 'function') {
        mainLog.warn('avif: @jsquash/avif exports look wrong; falling back');
        encoder = null;
        return null;
      }
      // Node has no fetch-for-file, so the .wasm has to be handed over explicitly.
      const bytes = await fs.readFile(paths.wasm);
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
