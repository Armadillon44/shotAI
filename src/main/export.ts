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
import type { ExportFormat, ExportResult } from '../shared/ipc';
import { getProjectForRead } from './project-store';
import { resolveSendableRender } from './render-gate';
import { zoomCropRect } from './export-geometry';
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
        // Callouts are un-numbered annotations (matches the report), kept even if empty.
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

const DOC_CSS = `
*{box-sizing:border-box}
html{-webkit-print-color-adjust:exact;print-color-adjust:exact}
body{margin:0;font-family:-apple-system,"Segoe UI",Roboto,Helvetica,Arial,sans-serif;color:#1f2937;background:#fff;line-height:1.6}
.doc{max-width:820px;margin:0 auto;padding:40px 32px 64px}
.doc__title{font-size:1.9rem;line-height:1.25;margin:0 0 4px}
.doc__meta{color:#6b7280;font-size:.85rem;margin:0 0 28px}
.doc__intro{margin:0 0 28px;padding:14px 18px;border:1px solid #e3e6ea;border-left:4px solid #4f46e5;border-radius:8px;background:#f8f9fc}
.doc__intro-h{margin:0 0 6px;font-size:1.15rem}
.doc__intro-b{margin:0;color:#374151;white-space:pre-wrap}
.section{margin:26px 0}
.section__h{font-size:1.3rem;margin:0 0 6px;color:#111827}
.section__b{white-space:pre-wrap;margin:0}
.step{display:flex;gap:16px;margin:0 0 26px;page-break-inside:avoid;break-inside:avoid}
.step__num{flex:0 0 auto;width:30px;height:30px;border-radius:50%;background:#4f46e5;color:#fff;font-weight:600;display:flex;align-items:center;justify-content:center;font-size:.95rem}
.step__num--note{background:#10b981}
.step__num--caution{background:#f59e0b}
.step__num--warning{background:#ef4444}
.step--callout .callout{margin:0}
.step__main{flex:1 1 auto;min-width:0}
.step__title{font-size:1.15rem;margin:0 0 10px}
.step__img{display:block;max-width:100%;height:auto;border:1px solid #e5e7eb;border-radius:8px}
.step__instr{margin:10px 0 0;padding:.1rem 0 .1rem .75rem;border-left:3px solid #a5b4fc;white-space:pre-wrap;font-size:1.02rem}
.step--textonly{align-items:center}
.step--textonly .step__instr{margin-top:0}
.callout{margin:20px 0;padding:.7rem .9rem;border-radius:8px;border:1px solid;border-left-width:4px;white-space:pre-wrap}
.callout__h{display:block;font-weight:700;margin-bottom:.25rem}
.callout--note{background:#ecfdf5;border-color:#6ee7b7;color:#065f46}
.callout--caution{background:#fffbeb;border-color:#fcd34d;color:#92400e}
.callout--warning{background:#fef2f2;border-color:#fca5a5;color:#991b1b}
@media print{.doc{max-width:none;padding:0 6px}}
`.trim();

/** Build the full self-contained HTML document (images as base64 data: URIs). */
async function buildHtmlDoc(
  manifest: ProjectManifest,
  items: ExportItem[],
  createdLine: string,
): Promise<string> {
  const parts: string[] = [];
  for (const it of items) {
    if (it.kind === 'text') {
      if (it.callout) {
        // Match the app: a colored glyph badge on the left rail (like the step-number
        // circles) + the colored callout box to its right.
        const glyph = CALLOUT_GLYPH[it.callout];
        const h = it.heading ? `<strong class="callout__h">${escapeHtml(it.heading)}</strong>` : '';
        const b = it.body ? escapeHtml(it.body) : '';
        parts.push(
          `<section class="step step--callout">` +
            `<div class="step__num step__num--${it.callout}">${glyph}</div>` +
            `<div class="step__main"><aside class="callout callout--${it.callout}">${h}${b}</aside></div>` +
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
    const bytes = it.bytes ?? (await fs.readFile(it.abs));
    const dataUri = `data:${it.mediaType};base64,${bytes.toString('base64')}`;
    const title = escapeHtml(it.caption || `Step ${it.n}`);
    const instr = it.body ? `<p class="step__instr">${escapeHtml(it.body)}</p>` : '';
    parts.push(
      `<section class="step">` +
        `<div class="step__num">${it.n}</div>` +
        `<div class="step__main">` +
        `<h2 class="step__title">${title}</h2>` +
        `<img class="step__img" src="${dataUri}" alt="Screenshot for step ${it.n}">` +
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
    `</head>\n<body>\n<main class="doc">\n` +
    `<h1 class="doc__title">${title}</h1>\n` +
    `<p class="doc__meta">${escapeHtml(createdLine)}</p>\n` +
    introHtml +
    parts.join('\n') +
    `\n</main>\n</body>\n</html>\n`
  );
}

/**
 * Minimal-formatting HTML for pasting into Word / Google Docs / other rich
 * editors: semantic tags only (h1/h2/p/img/blockquote/em), NO CSS, classes, or
 * inline styles, images inlined as data: URIs. The destination editor applies
 * its own formatting (and its formatting tools work on the pasted content).
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
  for (const it of items) {
    if (it.kind === 'text') {
      if (it.callout) {
        // Bold glyph (+ heading) on the first line, then the body.
        const glyph = CALLOUT_GLYPH[it.callout];
        const h = `<strong>${glyph}${it.heading ? ` ${escapeHtml(it.heading)}` : ''}</strong>`;
        const b = it.body ? br(it.body) : '';
        const sep = b ? '<br>' : '';
        parts.push(`<blockquote><p>${h}${sep}${b}</p></blockquote>`);
        continue;
      }
      // Plain text step — numbered like a step.
      const num = it.n != null ? `${it.n}. ` : '';
      if (it.heading) {
        parts.push(`<h2>${num}${escapeHtml(it.heading)}</h2>`);
        if (it.body) parts.push(`<p>${br(it.body)}</p>`);
      } else if (it.body) {
        parts.push(`<p>${num}${br(it.body)}</p>`);
      }
      continue;
    }
    const bytes = it.bytes ?? (await fs.readFile(it.abs));
    const dataUri = `data:${it.mediaType};base64,${bytes.toString('base64')}`;
    parts.push(`<h2>${it.n}. ${escapeHtml(it.caption || `Step ${it.n}`)}</h2>`);
    parts.push(`<p><img src="${dataUri}" alt="Screenshot for step ${it.n}"></p>`);
    if (it.body) parts.push(`<p>${br(it.body)}</p>`);
  }
  return (
    `<!doctype html>\n<html lang="en">\n<head>\n<meta charset="utf-8">\n` +
    `<title>${escapeHtml(manifest.title)}</title>\n</head>\n<body>\n` +
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
  for (const it of items) {
    if (it.kind === 'text') {
      if (it.callout) {
        // Blockquote with a bold glyph (+ heading) as the first quoted line, then
        // the body. A blank ">" line separates them (without it, two adjacent quoted
        // lines merge into one paragraph — CommonMark soft break = space).
        const glyph = CALLOUT_GLYPH[it.callout];
        lines.push(`> **${glyph}${it.heading ? ` ${escapeMarkdown(it.heading)}` : ''}**`);
        if (it.body) {
          lines.push('>');
          lines.push(`> ${it.body.replace(/\n/g, '\n> ')}`);
        }
        lines.push('');
        continue;
      }
      // Plain text step — numbered like a step.
      if (it.heading) {
        const num = it.n != null ? `${it.n}. ` : '';
        lines.push(`## ${num}${escapeMarkdown(it.heading.replace(/\s*\n\s*/g, ' '))}`, '');
        if (it.body) lines.push(it.body, '');
      } else if (it.body) {
        // Bold number prefix — a bare "N. " line would render as a renumbered
        // ordered-list item in Markdown.
        const numBold = it.n != null ? `**${it.n}.** ` : '';
        lines.push(`${numBold}${it.body}`, '');
      }
      continue;
    }
    const imgName = `step-${String(it.n).padStart(2, '0')}-${it.stepId}${it.ext}`;
    if (it.bytes) await fs.writeFile(path.join(imagesDir, imgName), it.bytes);
    else await fs.copyFile(it.abs, path.join(imagesDir, imgName));
    const heading = (it.caption || `Step ${it.n}`).replace(/\s*\n\s*/g, ' ');
    lines.push(`## ${it.n}. ${escapeMarkdown(heading)}`, '');
    // Angle-bracket the path: the serialized stem may contain spaces/parens.
    lines.push(`![Screenshot for step ${it.n}](<${imagesDirName}/${imgName}>)`, '');
    if (it.body) lines.push(it.body, '');
  }
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
  opts: { saveAs?: boolean; targetDir?: string } = {},
): Promise<ExportResult> {
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
    shell.showItemInFolder(outputPath);
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
    await fs.writeFile(outputPath, await buildHtmlDoc(manifest, items, createdLine), 'utf8');
  } else {
    // pdf
    await htmlToPdf(dir, await buildHtmlDoc(manifest, items, createdLine), outputPath);
  }

  mainLog.info(`exported ${format} → ${outputPath}`);
  shell.showItemInFolder(outputPath);
  return { format, outputPath };
}
