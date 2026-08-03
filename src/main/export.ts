// Export a project's report/SOP to a self-contained file under the project's
// export/ folder. Three formats share ONE step collector (collectSteps) so the
// security rule is enforced identically everywhere: a shot step with a redaction
// or crop that hasn't been baked into a flattened render is REFUSED — export
// never reads the raw (un-redacted/uncropped) screenshot. The renderer flattens
// all shot steps before calling export, so in practice every shot has a current
// marker-baked, redacted render; this is the fail-closed backstop.
import { promises as fs } from 'node:fs';
import path from 'node:path';
import { randomUUID } from 'node:crypto';
import { BrowserWindow, dialog, nativeImage, shell } from 'electron';
import { CALLOUT_GLYPH, type CalloutKind, type ProjectManifest } from '../shared/project';
import type { ExportFormat, ExportProgress, ExportResult } from '../shared/ipc';
import { getProjectForRead } from './project-store';
import { resolveSendableRender } from './render-gate';
import {
  HTML_IMG_AVIF_QUALITY,
  HTML_IMG_AVIF_SPEED,
  HTML_IMG_JPEG_QUALITY,
  htmlEmbedPolicy,
  htmlImageSize,
  type EmbedPolicy,
  zoomCropRect,
} from './export-geometry';
import { DOC_CSS, PLAIN_CSS } from './export-css';
import { encodeAvif } from './avif-encode';
import { buildDocx } from './export-docx';
import { buildPptx } from './export-pptx';
import { getReportByline } from './settings';
import { mainLog } from './logger';

// Windows/macOS filesystem-reserved characters + device names. Used to derive a
// safe EXPORT filename from the project title (project folders themselves are
// UUID-named in ProjectStore, so there's nothing to mirror there).
const RESERVED_CHARS = '<>:"/\\|?*';
const RESERVED_NAME = /^(con|prn|aux|nul|com[1-9]|lpt[1-9])$/i;

/** Turn a project title into a safe file base name (no extension). */
export function safeFileBase(title: string): string {
  let cleaned = Array.from(title || '')
    .filter((ch) => (ch.codePointAt(0) ?? 0) > 0x1f && !RESERVED_CHARS.includes(ch))
    .join('')
    .replace(/\s+/g, ' ')
    .trim();
  // Cap the length so the full path stays well under the Windows MAX_PATH (~260)
  // limit even with a deep projects folder + the export/ + extension.
  if (cleaned.length > 120) cleaned = cleaned.slice(0, 120).trim();
  cleaned = cleaned.replace(/[.\s]+$/, ''); // Windows: no trailing dot/space
  if (!cleaned) return 'shotAI SOP';
  if (RESERVED_NAME.test(cleaned) || cleaned.startsWith('.')) return `_${cleaned}`;
  return cleaned;
}

/**
 * First non-existent `<stem><ext>` in `exportDir`, appending " (1)", " (2)", …
 * on collision. Serializes repeat exports so a second export never overwrites —
 * or fails to write to — a previous export the user may have open (Windows lock).
 */
export async function nextAvailableStem(exportDir: string, stem: string, ext: string): Promise<string> {
  for (let n = 0; ; n++) {
    const candidate = n === 0 ? stem : `${stem} (${n})`;
    try {
      await fs.access(path.join(exportDir, candidate + ext));
    } catch {
      return candidate; // ENOENT → this name is free
    }
  }
}

/** First non-existent `<base>` DIRECTORY in `parent`, appending " (1)", " (2)", …
 *  on collision. The folder-level analogue of nextAvailableStem, for the
 *  self-contained Markdown export (a `<name>/` folder with the .md + images). */
async function nextAvailableDir(parent: string, base: string): Promise<string> {
  for (let n = 0; ; n++) {
    const candidate = n === 0 ? base : `${base} (${n})`;
    try {
      await fs.access(path.join(parent, candidate));
    } catch {
      return candidate; // ENOENT → free
    }
  }
}

/** File extension written for each export format. */
function extFor(format: ExportFormat): string {
  switch (format) {
    case 'docx':
      return '.docx';
    case 'pptx':
      return '.pptx';
    case 'markdown':
      return '.md';
    case 'pdf':
      return '.pdf';
    default:
      return '.html'; // html, html-plain
  }
}

/** Save-dialog file filter for each export format. */
function dialogFilters(format: ExportFormat): Electron.FileFilter[] {
  switch (format) {
    case 'docx':
      return [{ name: 'Word Document', extensions: ['docx'] }];
    case 'pptx':
      return [{ name: 'PowerPoint', extensions: ['pptx'] }];
    case 'markdown':
      return [{ name: 'Markdown', extensions: ['md'] }];
    case 'pdf':
      return [{ name: 'PDF', extensions: ['pdf'] }];
    default:
      return [{ name: 'HTML', extensions: ['html'] }];
  }
}

/** Show a Save dialog, parented to the focused window when there is one. */
async function showSaveDialog(
  options: Electron.SaveDialogOptions,
): Promise<Electron.SaveDialogReturnValue> {
  const win = BrowserWindow.getFocusedWindow();
  return win ? dialog.showSaveDialog(win, options) : dialog.showSaveDialog(options);
}

/**
 * Prompt for a destination folder (bulk export drops every selected project's
 * export into it). Returns the chosen directory, or null if cancelled.
 */
export async function chooseExportDirectory(defaultPath?: string): Promise<string | null> {
  const win = BrowserWindow.getFocusedWindow();
  const options: Electron.OpenDialogOptions = {
    title: 'Choose a folder for the exports',
    properties: ['openDirectory', 'createDirectory'],
    ...(defaultPath ? { defaultPath } : {}),
  };
  const res = win
    ? await dialog.showOpenDialog(win, options)
    : await dialog.showOpenDialog(options);
  if (res.canceled || res.filePaths.length === 0) return null;
  return res.filePaths[0];
}

function escapeHtml(s: string): string {
  return s
    .replace(/&/g, '&amp;')
    .replace(/</g, '&lt;')
    .replace(/>/g, '&gt;')
    .replace(/"/g, '&quot;');
}

/** Escape characters with Markdown meaning so a title/caption renders literally. */
function escapeMarkdown(s: string): string {
  return s.replace(/([\\`*_[\]#<>])/g, '\\$1');
}

export type ExportItem =
  | {
      kind: 'shot';
      /** 1-based step number among all NON-callout steps (shots + numbered text
       *  steps), matching the in-app report's numbering. */
      n: number;
      caption: string;
      body: string;
      /** Absolute path to the image to embed/copy (flattened render, redaction baked). */
      abs: string;
      mediaType: 'image/png' | 'image/jpeg';
      ext: string;
      stepId: string;
      /**
       * Pre-cropped image bytes to embed INSTEAD of reading `abs`, produced when
       * the step is zoomed in the report (reportZoom > 1) so the export matches
       * the on-screen framing. Always PNG. Absent → builders read `abs` verbatim.
       */
      bytes?: Buffer;
    }
  | {
      kind: 'text';
      /** For a plain (non-callout) text step: its 1-based step number in the shared
       *  sequence with shots (matches the report). Absent for callouts (un-numbered). */
      n?: number;
      heading: string;
      body: string;
      callout?: CalloutKind;
    };

/**
 * Reproduce the report's per-step zoom/pan as a static crop of `abs` (the ALREADY
 * redaction-baked sendable render — we only ever crop it SMALLER, never expose raw
 * pixels). Returns cropped PNG bytes, or null to embed the full image unchanged
 * (zoom <= 1, an unreadable image, or a degenerate crop). The visible-window math
 * lives in export-geometry.ts (pure + unit-tested); here we just apply it.
 */
function zoomCropPng(
  abs: string,
  zoom: number,
  panX: number,
  panY: number,
): Buffer | null {
  const img = nativeImage.createFromPath(abs);
  const { width, height } = img.getSize();
  const rect = zoomCropRect(width, height, zoom, panX, panY);
  if (!rect) return null; // whole image, as displayed
  const png = img.crop(rect).toPNG();
  return png && png.length > 0 ? png : null; // fail open to full image
}

/**
 * Load a shot item's image bytes + pixel dimensions (for the .docx / .pptx
 * builders, which must size images by aspect). Uses the pre-cropped `bytes` when
 * present, else reads the sendable render at `abs`. Dimensions come from
 * nativeImage so no image-parsing dep is needed.
 */
export async function loadItemImage(
  it: Extract<ExportItem, { kind: 'shot' }>,
): Promise<{ buffer: Buffer; width: number; height: number }> {
  const buffer = it.bytes ?? (await fs.readFile(it.abs));
  const { width, height } = nativeImage.createFromBuffer(buffer).getSize();
  return { buffer, width, height };
}

/**
 * `nativeImage.toBitmap()` hands back BGRA; the AVIF encoder wants RGBA. Swaps the
 * red/blue channels into a fresh buffer (the encoder needs a Uint8ClampedArray).
 *
 * Electron's bitmap is PREMULTIPLIED and libavif is handed it as straight alpha, which
 * is only harmless because every step render is fully opaque (verified: no pixel below
 * alpha 255, since captures and flattened editor renders are both opaque). If a
 * partially transparent render ever reaches here its colours would come out dark —
 * un-premultiply first if that day comes.
 */
function toRgba(bgra: Buffer): Uint8ClampedArray {
  const rgba = new Uint8ClampedArray(bgra.length);
  for (let i = 0; i < bgra.length; i += 4) {
    rgba[i] = bgra[i + 2];
    rgba[i + 1] = bgra[i + 1];
    rgba[i + 2] = bgra[i];
    rgba[i + 3] = bgra[i + 3];
  }
  return rgba;
}

/** What an HTML export inlines for one shot: bytes, media type, and the size attrs. */
interface InlineImage {
  bytes: Buffer;
  mediaType: string;
  /** ` width="W" height="H"`, or '' when the size couldn't be read. */
  sizeAttr: string;
}

/**
 * Resolve a shot to the bytes an HTML export should inline, resampled down to the
 * width it is ever displayed at (HTML_IMG_MAX_W) — issue #56.
 *
 * Both HTML varieties embed images as base64 data URIs, so a full-resolution render
 * shipped several times more pixels than are ever shown plus base64's ~33%
 * overhead. That payload is what makes copying a long SOP out of a browser into
 * another system (a Freshservice KB article) fall over.
 *
 * NEVER upscales — an image already narrower than the column is left byte-identical,
 * both to avoid inventing detail and because re-encoding a crisp screenshot can make
 * it BIGGER (interpolation turns flat colour runs into many unique colours, which
 * PNG compresses worse). For the same reason the re-encode is discarded if it didn't
 * actually come out smaller.
 *
 * Degrades safely at every step: an unreadable size, a failed resize, or a resize
 * that didn't help all fall back to the original bytes. The worst case is the old
 * behaviour, never a wrong or missing image.
 *
 * Runs strictly AFTER the fail-closed render gate, on already redaction-baked and
 * zoom-cropped pixels — resampling can only destroy information, never recover a
 * redacted region.
 */
async function inlineImageForHtml(
  it: Extract<ExportItem, { kind: 'shot' }>,
  policy: EmbedPolicy,
): Promise<InlineImage> {
  const { buffer, width, height } = await loadItemImage(it);
  // The DISPLAY size is always 1x the column, whatever resolution gets embedded —
  // an embedded @2x image is still laid out at 738 by these attributes.
  const shown = htmlImageSize(width, height);
  const sizeAttr = shown ? ` width="${shown.w}" height="${shown.h}"` : '';

  const cap = policy.embedMaxW;
  // `embedMaxW: null` + png is the PDF: hand it the source bytes untouched.
  if (cap == null && policy.codec === 'png') {
    return { bytes: buffer, mediaType: it.mediaType, sizeAttr };
  }

  let bytes = buffer;
  let mediaType: string = it.mediaType;
  try {
    const img = nativeImage.createFromBuffer(buffer);
    // Resolution: cap, but never upscale a capture that's already smaller.
    const scaled =
      cap != null && width > cap && height >= 1
        ? img.resize({
            width: cap,
            height: Math.max(1, Math.round(height * (cap / width))),
            // 'best' (not the cheaper 'good'): this is the only copy of the pixels a
            // reader ever sees and it's a multi-x downscale of UI text, where the
            // filter choice is plainly visible. Matches macOS's .high.
            quality: 'best',
          })
        : img;

    // Codec. AVIF is the styled export's only way under the destination's payload
    // ceiling (see htmlEmbedPolicy); it needs a WASM encoder, so it degrades to JPEG
    // and then to the original bytes. Losing alpha is safe either way — step renders
    // are fully opaque.
    let out: { b: Buffer; t: string } | null = null;
    if (policy.codec === 'avif') {
      const { width: sw, height: sh } = scaled.getSize();
      const avif = await encodeAvif(
        toRgba(scaled.toBitmap()),
        sw,
        sh,
        HTML_IMG_AVIF_QUALITY,
        HTML_IMG_AVIF_SPEED,
      );
      if (avif) out = { b: avif, t: 'image/avif' };
    }
    if (!out) {
      out =
        policy.codec === 'png'
          ? { b: scaled.toPNG(), t: 'image/png' }
          : { b: scaled.toJPEG(HTML_IMG_JPEG_QUALITY), t: 'image/jpeg' };
    }
    if (out.b.length > 0) {
      bytes = out.b;
      mediaType = out.t;
    }
  } catch (e) {
    mainLog.warn('export: image re-encode failed, embedding the original:', e);
  }

  // Never let the pipeline make a step BIGGER than shipping the original would have.
  if (bytes !== buffer && bytes.length >= buffer.length) {
    return { bytes: buffer, mediaType: it.mediaType, sizeAttr };
  }
  return { bytes, mediaType, sizeAttr };
}

/**
 * Resolve the project's steps into an ordered export list. Numbering matches the
 * in-app report: every NON-callout step — shots AND non-empty plain text steps —
 * consumes a contiguous 1..N number; callouts are un-numbered annotations. Empty
 * plain text steps are skipped. Throws (fail-closed) if a shot step's redaction/
 * crop hasn't been baked into a render, or if there's nothing to export.
 */
async function collectSteps(
  dir: string,
  manifest: ProjectManifest,
): Promise<ExportItem[]> {
  const items: ExportItem[] = [];
  let stepNo = 0;
  for (const step of manifest.steps) {
    if (step.kind === 'text') {
      const heading = (step.heading ?? '').trim();
      const body = (step.body ?? '').trim();
      if (step.callout) {
        // A `section` divider with neither heading nor body would emit a stray
        // rule/hr/---, so skip it (like an empty plain text step). Colored callouts
        // stay meaningful even when empty. Either way, un-numbered (no stepNo).
        if (step.callout === 'section' && !heading && !body) continue;
        items.push({ kind: 'text', heading, body, callout: step.callout });
        continue;
      }
      if (!heading && !body) continue; // skip empty plain text steps (no number consumed)
      // A plain text step IS a numbered step, like the report.
      stepNo++;
      items.push({ kind: 'text', n: stepNo, heading, body });
      continue;
    }
    // Shot step.
    stepNo++;
    // Fail-closed redaction gate (shared with the Claude send path).
    const { abs, mediaType, ext } = resolveSendableRender(dir, step, `Step ${stepNo}`, 'export');
    // Fail fast (and clearly) if the render was deleted off disk after the
    // manifest was written — better than an opaque ENOENT mid-export.
    try {
      await fs.stat(abs);
    } catch {
      throw new Error(
        `Step ${stepNo}'s screenshot render is missing from disk (${step.flattened ?? step.screenshot}). ` +
          `Open it in the editor and save to re-bake the render, then export again.`,
      );
    }
    // Honor the report's per-step zoom/pan: crop the sendable render to the same
    // visible window so the export matches what's on screen. Falls back to the
    // full image (bytes undefined) when the step isn't zoomed.
    const cropped = zoomCropPng(abs, step.reportZoom ?? 1, step.reportPanX ?? 0.5, step.reportPanY ?? 0.5);
    items.push({
      kind: 'shot',
      n: stepNo,
      caption: (step.caption ?? '').trim(),
      body: (step.body ?? '').trim(),
      abs,
      // A crop is re-encoded as PNG regardless of the source media type.
      mediaType: cropped ? 'image/png' : mediaType,
      ext: cropped ? '.png' : ext || '.png',
      stepId: step.id,
      ...(cropped ? { bytes: cropped } : {}),
    });
  }
  if (items.length === 0) {
    throw new Error('This project has nothing to export yet — add a step first.');
  }
  return items;
}


/**
 * Build the full self-contained HTML document (images as base64 data: URIs).
 *
 * `policy` decides how each screenshot is embedded and MUST come from
 * htmlEmbedPolicy(format) — the PDF is printed from this same output, so it has to be
 * given the full-resolution PNG policy or it inherits the .html export's resample and
 * prints soft (#56). `onProgress` reports per-image encode progress; AVIF costs ~1s
 * an image, so a caller with a UI should pass it.
 */
async function buildHtmlDoc(
  manifest: ProjectManifest,
  items: ExportItem[],
  createdLine: string,
  policy: EmbedPolicy,
  onProgress?: (p: ExportProgress) => void,
): Promise<string> {
  const parts: string[] = [];
  // Encoding is the slow part (AVIF runs ~1s per image), so report per image rather
  // than per step — text steps and callouts cost nothing and would skew the count.
  const shotTotal = items.reduce((n, it) => (it.kind === 'shot' ? n + 1 : n), 0);
  let shotDone = 0;
  onProgress?.({ done: 0, total: shotTotal });
  for (const it of items) {
    if (it.kind === 'text') {
      if (it.callout === 'section') {
        // A non-counted phase divider: a heading + thin rule + muted body (no badge,
        // no colored box). Reuses the .section / .section__h / .section__b styles.
        const h = it.heading ? `<h2 class="section__h">${escapeHtml(it.heading)}</h2>` : '';
        const b = it.body ? `<p class="section__b">${escapeHtml(it.body)}</p>` : '';
        // The rule lives on an INNER div so .section can carry the document column
        // while the rule still aligns with the step content column. Inner elements
        // survive a KB-editor paste; whole-document wrappers do not (#57).
        parts.push(
          `<section class="section"><div class="section__inner">${h}${b}</div></section>`,
        );
        continue;
      }
      if (it.callout) {
        // Callout = the same step card, tinted by kind: a glyph badge in the left
        // gutter + the content in a tinted .step__main card (no inner box).
        const glyph = CALLOUT_GLYPH[it.callout];
        const h = it.heading ? `<strong class="callout__h">${escapeHtml(it.heading)}</strong>` : '';
        const b = it.body ? `<div class="callout__b">${escapeHtml(it.body)}</div>` : '';
        parts.push(
          `<section class="step step--callout">` +
            `<div class="step__num step__num--${it.callout}">${glyph}</div>` +
            `<div class="step__main step__main--${it.callout}">${h}${b}</div>` +
            `</section>`,
        );
        continue;
      }
      // Plain text step — a numbered step (like the report), just no image.
      // With no heading, center the body against the number badge (step--textonly)
      // so it doesn't sit low.
      const th = it.heading ? `<h2 class="step__title">${escapeHtml(it.heading)}</h2>` : '';
      const tb = it.body ? `<p class="step__instr">${escapeHtml(it.body)}</p>` : '';
      const cls = it.heading ? 'step' : 'step step--textonly';
      parts.push(
        `<section class="${cls}">` +
          `<div class="step__num">${it.n ?? ''}</div>` +
          `<div class="step__main">${th}${tb}</div>` +
          `</section>`,
      );
      continue;
    }
    const img = await inlineImageForHtml(it, policy);
    onProgress?.({ done: ++shotDone, total: shotTotal });
    const dataUri = `data:${img.mediaType};base64,${img.bytes.toString('base64')}`;
    const title = escapeHtml(it.caption || `Step ${it.n}`);
    const instr = it.body ? `<p class="step__instr">${escapeHtml(it.body)}</p>` : '';
    parts.push(
      `<section class="step">` +
        `<div class="step__num">${it.n}</div>` +
        `<div class="step__main">` +
        `<h2 class="step__title">${title}</h2>` +
        // width/height ATTRIBUTES: a KB editor strips max-width off <img> (#57).
        `<img class="step__img" src="${dataUri}"${img.sizeAttr} alt="Screenshot for step ${it.n}">` +
        `${instr}` +
        `</div>` +
        `</section>`,
    );
  }
  const title = escapeHtml(manifest.title);
  const intro = manifest.intro;
  const introHtml =
    intro && (intro.heading || intro.body)
      ? `<section class="doc__intro">\n` +
        `<p class="doc__intro-eyebrow">Overview</p>\n` +
        (intro.heading ? `<h2 class="doc__intro-h">${escapeHtml(intro.heading)}</h2>\n` : '') +
        (intro.body
          ? `<p class="doc__intro-b">${escapeHtml(intro.body).replace(/\n/g, '<br>')}</p>\n`
          : '') +
        `</section>\n`
      : '';
  return (
    `<!doctype html>\n<html lang="en">\n<head>\n` +
    `<meta charset="utf-8">\n` +
    `<meta name="viewport" content="width=device-width, initial-scale=1">\n` +
    `<title>${title}</title>\n` +
    `<style>${DOC_CSS}</style>\n` +
    // A plain <div>, not <main>: this wrapper gets unwrapped on a KB-editor paste
    // either way (which is why every block carries its own column — see DOC_CSS),
    // and semantic tags are commonly off a sanitizer's allowlist. It only pads.
    `</head>\n<body>\n<div class="doc">\n` +
    `<h1 class="doc__title">${title}</h1>\n` +
    `<p class="doc__meta">${escapeHtml(createdLine)}</p>\n` +
    introHtml +
    parts.join('\n') +
    `\n</div>\n</body>\n</html>\n`
  );
}

/**
 * Simple, lightly-styled standalone HTML: semantic tags (h1/h2/p/img/blockquote/
 * strong/hr) + a minimal Arial stylesheet (PLAIN_CSS) for readable headers, bold,
 * and spacing. Images inlined as data: URIs. Still clean enough to paste into Word
 * / Google Docs (they honor the basic tags + formatting).
 */
async function buildPlainHtmlDoc(
  manifest: ProjectManifest,
  items: ExportItem[],
): Promise<string> {
  const br = (s: string) => escapeHtml(s).replace(/\n/g, '<br>');
  const parts: string[] = [`<h1>${escapeHtml(manifest.title)}</h1>`];
  if (manifest.intro && (manifest.intro.heading || manifest.intro.body)) {
    if (manifest.intro.heading) parts.push(`<h2>${escapeHtml(manifest.intro.heading)}</h2>`);
    if (manifest.intro.body) parts.push(`<p>${br(manifest.intro.body)}</p>`);
  }
  // Build each step into its own block, then join with a thematic break so the
  // steps read as separate units (#40). The <hr> goes only BETWEEN items.
  const itemBlocks: string[] = [];
  for (const it of items) {
    const block: string[] = [];
    if (it.kind === 'text') {
      if (it.callout === 'section') {
        // Non-counted divider → a real heading + body (no glyph, no blockquote).
        if (it.heading) block.push(`<h2>${escapeHtml(it.heading)}</h2>`);
        if (it.body) block.push(`<p>${br(it.body)}</p>`);
      } else if (it.callout) {
        // Bold glyph (+ heading) on the first line, then the body.
        const glyph = CALLOUT_GLYPH[it.callout];
        const h = `<strong>${glyph}${it.heading ? ` ${escapeHtml(it.heading)}` : ''}</strong>`;
        const b = it.body ? br(it.body) : '';
        const sep = b ? '<br>' : '';
        block.push(`<blockquote><p>${h}${sep}${b}</p></blockquote>`);
      } else {
        // Plain text step — numbered like a step.
        const num = it.n != null ? `${it.n}. ` : '';
        if (it.heading) {
          block.push(`<h2>${num}${escapeHtml(it.heading)}</h2>`);
          if (it.body) block.push(`<p>${br(it.body)}</p>`);
        } else if (it.body) {
          block.push(`<p>${num}${br(it.body)}</p>`);
        }
      }
    } else {
      // Same pipeline as the styled export: resampled to the display width (#56)
      // and sized with width/height ATTRIBUTES, because Word / Google Docs drop
      // CSS max-width on paste and would lay the capture out at full pixel size.
      // html-plain embeds PNG at 1x: Word cannot read WebP (htmlEmbedPolicy).
      const img = await inlineImageForHtml(it, htmlEmbedPolicy('html-plain'));
      const dataUri = `data:${img.mediaType};base64,${img.bytes.toString('base64')}`;
      block.push(`<h2>${it.n}. ${escapeHtml(it.caption || `Step ${it.n}`)}</h2>`);
      block.push(
        `<p><img src="${dataUri}"${img.sizeAttr} alt="Screenshot for step ${it.n}"></p>`,
      );
      if (it.body) block.push(`<p>${br(it.body)}</p>`);
    }
    if (block.length) itemBlocks.push(block.join('\n'));
  }
  if (itemBlocks.length) parts.push(itemBlocks.join('\n<hr>\n'));
  return (
    `<!doctype html>\n<html lang="en">\n<head>\n<meta charset="utf-8">\n` +
    `<title>${escapeHtml(manifest.title)}</title>\n` +
    `<style>${PLAIN_CSS}</style>\n</head>\n<body>\n` +
    parts.join('\n') +
    `\n</body>\n</html>\n`
  );
}

/** Render the HTML to a PDF via a hidden BrowserWindow + printToPDF (offline). */
async function htmlToPdf(dir: string, html: string, outputPath: string): Promise<void> {
  const renderDir = path.join(dir, 'export', '.render');
  await fs.mkdir(renderDir, { recursive: true });
  // Best-effort sweep of any temp HTML orphaned by a prior failed export.
  try {
    for (const f of await fs.readdir(renderDir)) {
      if (f.startsWith('_print-') && f.endsWith('.html')) {
        await fs.rm(path.join(renderDir, f), { force: true }).catch(() => undefined);
      }
    }
  } catch {
    /* directory unreadable — proceed anyway */
  }
  const tmpHtml = path.join(renderDir, `_print-${randomUUID()}.html`);
  await fs.writeFile(tmpHtml, html, 'utf8');
  const win = new BrowserWindow({
    show: false,
    width: 900,
    height: 1200,
    webPreferences: {
      sandbox: true,
      contextIsolation: true,
      nodeIntegration: false,
      javascript: false, // the print document is fully static
    },
  });
  try {
    await win.loadFile(tmpHtml);
    const pdf = await win.webContents.printToPDF({
      printBackground: true,
      pageSize: 'Letter',
      margins: { marginType: 'default' },
    });
    // Fail closed: never write a 0-byte/corrupt PDF silently (a possible failure
    // mode on software-rendered/headless setups). HTML + Markdown are fallbacks.
    if (!pdf || pdf.length === 0) {
      throw new Error(
        'PDF rendering produced an empty document — printing may have failed on this system. Try the HTML or Markdown export instead.',
      );
    }
    await fs.writeFile(outputPath, pdf);
  } finally {
    if (!win.isDestroyed()) win.destroy();
    await fs.rm(tmpHtml, { force: true }).catch(() => undefined);
  }
}

/**
 * Assemble the Markdown document into a SELF-CONTAINED folder `outFolder`:
 * `<mdStem>.md` alongside an `images/` subfolder. Keeping both inside one folder
 * (rather than a loose .md + images dir) keeps the chosen export directory tidy.
 */
async function buildMarkdown(
  manifest: ProjectManifest,
  items: ExportItem[],
  outFolder: string,
  mdStem: string,
  createdLine: string,
): Promise<string> {
  // Images live in an `images/` subfolder next to the .md, inside outFolder.
  const imagesDirName = 'images';
  const imagesDir = path.join(outFolder, imagesDirName);
  await fs.rm(imagesDir, { recursive: true, force: true }).catch(() => undefined);
  await fs.mkdir(imagesDir, { recursive: true });
  const lines: string[] = [
    `# ${escapeMarkdown(manifest.title)}`,
    '',
    `_${escapeMarkdown(createdLine)}_`,
    '',
  ];
  if (manifest.intro && (manifest.intro.heading || manifest.intro.body)) {
    if (manifest.intro.heading) lines.push(`## ${escapeMarkdown(manifest.intro.heading)}`, '');
    if (manifest.intro.body) lines.push(manifest.intro.body, '');
  }
  // Build each step into its own chunk of lines, then separate the steps with a
  // `---` thematic break (#40). The break goes only BETWEEN items, and always with
  // a blank line before it (else `text\n---` would make the prior line a heading).
  const itemChunks: string[][] = [];
  for (const it of items) {
    const chunk: string[] = [];
    if (it.kind === 'text') {
      if (it.callout === 'section') {
        // Non-counted divider → a real `## heading` + body (no glyph, no blockquote).
        if (it.heading) chunk.push(`## ${escapeMarkdown(it.heading.replace(/\s*\n\s*/g, ' '))}`);
        if (it.body) {
          if (it.heading) chunk.push('');
          chunk.push(it.body);
        }
      } else if (it.callout) {
        // Blockquote with a bold glyph (+ heading) as the first quoted line, then
        // the body. A blank ">" line separates them (without it, two adjacent quoted
        // lines merge into one paragraph — CommonMark soft break = space).
        const glyph = CALLOUT_GLYPH[it.callout];
        chunk.push(`> **${glyph}${it.heading ? ` ${escapeMarkdown(it.heading)}` : ''}**`);
        if (it.body) {
          chunk.push('>');
          chunk.push(`> ${it.body.replace(/\n/g, '\n> ')}`);
        }
      } else if (it.heading) {
        // Plain text step — numbered like a step.
        const num = it.n != null ? `${it.n}. ` : '';
        chunk.push(`## ${num}${escapeMarkdown(it.heading.replace(/\s*\n\s*/g, ' '))}`);
        if (it.body) chunk.push('', it.body);
      } else if (it.body) {
        // Bold number prefix — a bare "N. " line would render as a renumbered
        // ordered-list item in Markdown.
        const numBold = it.n != null ? `**${it.n}.** ` : '';
        chunk.push(`${numBold}${it.body}`);
      }
    } else {
      const imgName = `step-${String(it.n).padStart(2, '0')}-${it.stepId}${it.ext}`;
      if (it.bytes) await fs.writeFile(path.join(imagesDir, imgName), it.bytes);
      else await fs.copyFile(it.abs, path.join(imagesDir, imgName));
      const heading = (it.caption || `Step ${it.n}`).replace(/\s*\n\s*/g, ' ');
      chunk.push(`## ${it.n}. ${escapeMarkdown(heading)}`, '');
      // Angle-bracket the path: the serialized stem may contain spaces/parens.
      chunk.push(`![Screenshot for step ${it.n}](<${imagesDirName}/${imgName}>)`);
      if (it.body) chunk.push('', it.body);
    }
    if (chunk.length) itemChunks.push(chunk);
  }
  itemChunks.forEach((chunk, i) => {
    if (i > 0) lines.push('', '---', ''); // blank line before --- guards against a Setext heading
    lines.push(...chunk);
  });
  lines.push('');
  const outputPath = path.join(outFolder, `${mdStem}.md`);
  await fs.writeFile(outputPath, lines.join('\n'), 'utf8');
  return outputPath;
}

/**
 * Export the project to `format`, reveal it in the OS file manager, and return its
 * path. Destination (issue #37):
 *  - `opts.saveAs` → prompt a Save dialog defaulting to the project's export/ folder
 *    (single export; cancel returns `{ canceled: true }`).
 *  - `opts.targetDir` → write into that folder with collision-safe naming (bulk).
 *  - neither → the project's export/ folder (legacy/default).
 * Markdown always exports as a self-contained `<name>/` folder (the .md + images/).
 * The renderer is expected to have flattened all shot steps first (so renders are
 * current/redacted/marker-baked).
 */
export async function exportProject(
  projectPath: string,
  format: ExportFormat,
  opts: {
    saveAs?: boolean;
    targetDir?: string;
    reveal?: boolean;
    /** Per-image encode progress; only the image-embedding formats call it. */
    onProgress?: (p: ExportProgress) => void;
  } = {},
): Promise<ExportResult> {
  // Reveal the written file unless told not to — bulk exports (to a shared folder
  // or to each project's own folder) suppress the per-file reveal so N folders
  // don't pop open mid-run.
  const reveal = opts.reveal ?? true;
  const { dir, manifest } = await getProjectForRead(projectPath);
  const items = await collectSteps(dir, manifest);
  const base = safeFileBase(manifest.title);
  // Document footer (F7): "Created on <datetime>", plus "by <name>" when the user
  // has opted in and set a display name (getReportByline centralizes that gate).
  const generatedAt = new Date().toLocaleString();
  const byline = await getReportByline();
  const createdLine = `Created on ${generatedAt}${byline ? ` by ${byline}` : ''}`;
  const exportDir = path.join(dir, 'export');
  await fs.mkdir(exportDir, { recursive: true });

  const stembase = format === 'html-plain' ? `${base}-plain` : base;
  const ext = extFor(format);

  // Markdown is a self-contained FOLDER (<name>/<name>.md + images/) so the chosen
  // destination stays tidy; every other format is one self-contained file.
  if (format === 'markdown') {
    let folder: string;
    if (opts.saveAs) {
      const res = await showSaveDialog({
        title: 'Export Markdown (saved as a folder with its images)',
        defaultPath: path.join(exportDir, `${base}.md`),
        filters: dialogFilters(format),
      });
      if (res.canceled || !res.filePath) return { format, outputPath: '', canceled: true };
      const stem = path.basename(res.filePath).replace(/\.md$/i, '') || base;
      folder = path.join(path.dirname(res.filePath), stem);
      await fs.mkdir(folder, { recursive: true });
    } else {
      const parent = opts.targetDir ?? exportDir;
      await fs.mkdir(parent, { recursive: true });
      folder = path.join(parent, await nextAvailableDir(parent, base));
      await fs.mkdir(folder, { recursive: true });
    }
    const outputPath = await buildMarkdown(manifest, items, folder, path.basename(folder), createdLine);
    mainLog.info(`exported markdown → ${outputPath}`);
    if (reveal) shell.showItemInFolder(outputPath);
    return { format, outputPath };
  }

  // Single-file formats: resolve the target file path.
  let outputPath: string;
  if (opts.saveAs) {
    const res = await showSaveDialog({
      title: 'Export',
      defaultPath: path.join(exportDir, `${stembase}${ext}`),
      filters: dialogFilters(format),
    });
    if (res.canceled || !res.filePath) return { format, outputPath: '', canceled: true };
    outputPath = res.filePath;
  } else {
    const targetDir = opts.targetDir ?? exportDir;
    await fs.mkdir(targetDir, { recursive: true });
    const stem = await nextAvailableStem(targetDir, stembase, ext);
    outputPath = path.join(targetDir, `${stem}${ext}`);
  }

  if (format === 'docx') {
    await fs.writeFile(outputPath, await buildDocx(manifest, items, createdLine));
  } else if (format === 'pptx') {
    await fs.writeFile(outputPath, await buildPptx(manifest, items, createdLine));
  } else if (format === 'html-plain') {
    await fs.writeFile(outputPath, await buildPlainHtmlDoc(manifest, items), 'utf8');
  } else if (format === 'html') {
    await fs.writeFile(
      outputPath,
      await buildHtmlDoc(manifest, items, createdLine, htmlEmbedPolicy(format), opts.onProgress),
      'utf8',
    );
  } else {
    // pdf — same builder as the .html export, so it must opt OUT of the resample and
    // the codec explicitly, or it silently inherits them and prints soft (#56 scope).
    await htmlToPdf(
      dir,
      await buildHtmlDoc(manifest, items, createdLine, htmlEmbedPolicy(format), opts.onProgress),
      outputPath,
    );
  }

  mainLog.info(`exported ${format} → ${outputPath}`);
  if (reveal) shell.showItemInFolder(outputPath);
  return { format, outputPath };
}

/**
 * Open a folder in the OS file manager — used to reveal the bulk-export
 * destination ONCE, after the whole run finishes. Validates the path is an
 * existing DIRECTORY first, so it can never be coaxed into opening (executing) a
 * file.
 */
export async function revealExportDir(dir: string): Promise<void> {
  try {
    const st = await fs.stat(dir);
    if (st.isDirectory()) await shell.openPath(dir);
  } catch {
    /* gone / unreadable — nothing to reveal */
  }
}
