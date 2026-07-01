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
import { BrowserWindow, shell } from 'electron';
import type { CalloutKind, ProjectManifest } from '../shared/project';
import type { ExportFormat, ExportResult } from '../shared/ipc';
import { getProjectForRead } from './project-store';
import { resolveSendableRender } from './render-gate';
import { mainLog } from './logger';

// Windows/macOS filesystem-reserved characters + device names. Used to derive a
// safe EXPORT filename from the project title (project folders themselves are
// UUID-named in ProjectStore, so there's nothing to mirror there).
const RESERVED_CHARS = '<>:"/\\|?*';
const RESERVED_NAME = /^(con|prn|aux|nul|com[1-9]|lpt[1-9])$/i;

/** Turn a project title into a safe file base name (no extension). */
function safeFileBase(title: string): string {
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
async function nextAvailableStem(exportDir: string, stem: string, ext: string): Promise<string> {
  for (let n = 0; ; n++) {
    const candidate = n === 0 ? stem : `${stem} (${n})`;
    try {
      await fs.access(path.join(exportDir, candidate + ext));
    } catch {
      return candidate; // ENOENT → this name is free
    }
  }
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

type ExportItem =
  | {
      kind: 'shot';
      /** 1-based number among SHOT steps only (text sections don't consume one). */
      n: number;
      caption: string;
      body: string;
      note: string;
      /** Absolute path to the image to embed/copy (flattened render, redaction baked). */
      abs: string;
      mediaType: 'image/png' | 'image/jpeg';
      ext: string;
      stepId: string;
    }
  | { kind: 'text'; heading: string; body: string; callout?: CalloutKind };

/**
 * Resolve the project's steps into an ordered export list. Shot steps are
 * numbered 1..N; non-empty text steps become sections. Throws (fail-closed) if a
 * shot step's redaction/crop hasn't been baked into a render, or if there's
 * nothing to export.
 */
async function collectSteps(
  dir: string,
  manifest: ProjectManifest,
): Promise<ExportItem[]> {
  const items: ExportItem[] = [];
  let shotNo = 0;
  for (const step of manifest.steps) {
    if (step.kind === 'text') {
      const heading = (step.heading ?? '').trim();
      const body = (step.body ?? '').trim();
      if (!heading && !body && !step.callout) continue; // skip empty plain text steps
      items.push({ kind: 'text', heading, body, callout: step.callout });
      continue;
    }
    // Shot step.
    shotNo++;
    // Fail-closed redaction gate (shared with the Claude send path).
    const { abs, mediaType, ext } = resolveSendableRender(dir, step, `Step ${shotNo}`, 'export');
    // Fail fast (and clearly) if the render was deleted off disk after the
    // manifest was written — better than an opaque ENOENT mid-export.
    try {
      await fs.stat(abs);
    } catch {
      throw new Error(
        `Step ${shotNo}'s screenshot render is missing from disk (${step.flattened ?? step.screenshot}). ` +
          `Open it in the editor and save to re-bake the render, then export again.`,
      );
    }
    items.push({
      kind: 'shot',
      n: shotNo,
      caption: (step.caption ?? '').trim(),
      body: (step.body ?? '').trim(),
      note: (step.note ?? '').trim(),
      abs,
      mediaType,
      ext: ext || '.png',
      stepId: step.id,
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
.step__main{flex:1 1 auto;min-width:0}
.step__title{font-size:1.15rem;margin:2px 0 10px}
.step__img{display:block;max-width:100%;height:auto;border:1px solid #e5e7eb;border-radius:8px}
.step__instr{margin:10px 0 0;padding:.1rem 0 .1rem .75rem;border-left:3px solid #a5b4fc;white-space:pre-wrap;font-size:1.02rem}
.step__note{margin:8px 0 0;color:#6b7280;font-size:.92rem;white-space:pre-wrap}
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
  generatedAt: string,
): Promise<string> {
  const parts: string[] = [];
  for (const it of items) {
    if (it.kind === 'text') {
      if (it.callout) {
        // No type label — the box color conveys note/caution/warning. Heading
        // (if any) is a bold block above the body.
        const h = it.heading ? `<strong class="callout__h">${escapeHtml(it.heading)}</strong>` : '';
        const b = it.body ? escapeHtml(it.body) : '';
        parts.push(`<aside class="callout callout--${it.callout}">${h}${b}</aside>`);
        continue;
      }
      const h = it.heading ? `<h2 class="section__h">${escapeHtml(it.heading)}</h2>` : '';
      const b = it.body ? `<p class="section__b">${escapeHtml(it.body)}</p>` : '';
      parts.push(`<section class="section">${h}${b}</section>`);
      continue;
    }
    const bytes = await fs.readFile(it.abs);
    const dataUri = `data:${it.mediaType};base64,${bytes.toString('base64')}`;
    const title = escapeHtml(it.caption || `Step ${it.n}`);
    const instr = it.body ? `<p class="step__instr">${escapeHtml(it.body)}</p>` : '';
    const note = it.note ? `<p class="step__note">${escapeHtml(it.note)}</p>` : '';
    parts.push(
      `<section class="step">` +
        `<div class="step__num">${it.n}</div>` +
        `<div class="step__main">` +
        `<h2 class="step__title">${title}</h2>` +
        `<img class="step__img" src="${dataUri}" alt="Screenshot for step ${it.n}">` +
        `${instr}${note}` +
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
    `<p class="doc__meta">Generated by shotAI · ${escapeHtml(generatedAt)}</p>\n` +
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
        // No type label. Bold heading (if any) on its own line, then the body.
        const h = it.heading ? `<strong>${escapeHtml(it.heading)}</strong>` : '';
        const b = it.body ? br(it.body) : '';
        const sep = h && b ? '<br>' : '';
        parts.push(`<blockquote><p>${h}${sep}${b}</p></blockquote>`);
        continue;
      }
      if (it.heading) parts.push(`<h2>${escapeHtml(it.heading)}</h2>`);
      if (it.body) parts.push(`<p>${br(it.body)}</p>`);
      continue;
    }
    const bytes = await fs.readFile(it.abs);
    const dataUri = `data:${it.mediaType};base64,${bytes.toString('base64')}`;
    parts.push(`<h2>${it.n}. ${escapeHtml(it.caption || `Step ${it.n}`)}</h2>`);
    parts.push(`<p><img src="${dataUri}" alt="Screenshot for step ${it.n}"></p>`);
    if (it.body) parts.push(`<p>${br(it.body)}</p>`);
    if (it.note) parts.push(`<p><em>${br(it.note)}</em></p>`);
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

/** Assemble the Markdown document, copying each image into export/images/. */
async function buildMarkdown(
  dir: string,
  manifest: ProjectManifest,
  items: ExportItem[],
  stem: string,
  generatedAt: string,
): Promise<string> {
  // Per-export images dir (stem is already unique for the .md) so a serialized
  // second markdown export doesn't clobber the first export's images.
  const imagesDirName = `${stem}-images`;
  const imagesDir = path.join(dir, 'export', imagesDirName);
  await fs.rm(imagesDir, { recursive: true, force: true }).catch(() => undefined);
  await fs.mkdir(imagesDir, { recursive: true });
  const lines: string[] = [
    `# ${escapeMarkdown(manifest.title)}`,
    '',
    `_Generated by shotAI · ${generatedAt}_`,
    '',
  ];
  if (manifest.intro && (manifest.intro.heading || manifest.intro.body)) {
    if (manifest.intro.heading) lines.push(`## ${escapeMarkdown(manifest.intro.heading)}`, '');
    if (manifest.intro.body) lines.push(manifest.intro.body, '');
  }
  for (const it of items) {
    if (it.kind === 'text') {
      if (it.callout) {
        // No type label — rendered as a plain blockquote. Bold heading (if any)
        // as the first quoted line, then the body. A blank ">" line separates
        // heading from body so the heading stays on its own line — without it,
        // two adjacent quoted lines merge into one paragraph (CommonMark soft
        // break = space). An empty callout still emits a ">" so the step
        // persists, matching the in-app box and the HTML/PDF/Word exports.
        if (it.heading) lines.push(`> **${escapeMarkdown(it.heading)}**`);
        if (it.heading && it.body) lines.push('>');
        if (it.body) lines.push(`> ${it.body.replace(/\n/g, '\n> ')}`);
        if (!it.heading && !it.body) lines.push('>');
        lines.push('');
        continue;
      }
      if (it.heading) lines.push(`## ${escapeMarkdown(it.heading.replace(/\s*\n\s*/g, ' '))}`, '');
      if (it.body) lines.push(it.body, '');
      continue;
    }
    const imgName = `step-${String(it.n).padStart(2, '0')}-${it.stepId}${it.ext}`;
    await fs.copyFile(it.abs, path.join(imagesDir, imgName));
    const heading = (it.caption || `Step ${it.n}`).replace(/\s*\n\s*/g, ' ');
    lines.push(`## ${it.n}. ${escapeMarkdown(heading)}`, '');
    // Angle-bracket the path: the serialized stem may contain spaces/parens.
    lines.push(`![Screenshot for step ${it.n}](<${imagesDirName}/${imgName}>)`, '');
    if (it.body) lines.push(it.body, '');
    if (it.note) lines.push(`> ${it.note.replace(/\n/g, '\n> ')}`, '');
  }
  const outputPath = path.join(dir, 'export', `${stem}.md`);
  await fs.writeFile(outputPath, lines.join('\n'), 'utf8');
  return outputPath;
}

/**
 * Export the project to `format` under its export/ folder; reveal the file in the
 * OS file manager and return its path. The renderer is expected to have flattened
 * all shot steps first (so renders are current/redacted/marker-baked).
 */
export async function exportProject(
  projectPath: string,
  format: ExportFormat,
): Promise<ExportResult> {
  const { dir, manifest } = await getProjectForRead(projectPath);
  const items = await collectSteps(dir, manifest);
  const base = safeFileBase(manifest.title);
  const generatedAt = new Date().toLocaleString();
  const exportDir = path.join(dir, 'export');
  await fs.mkdir(exportDir, { recursive: true });

  let outputPath: string;
  if (format === 'markdown') {
    const stem = await nextAvailableStem(exportDir, base, '.md');
    outputPath = await buildMarkdown(dir, manifest, items, stem, generatedAt);
  } else if (format === 'html-plain') {
    const html = await buildPlainHtmlDoc(manifest, items);
    const stem = await nextAvailableStem(exportDir, `${base}-plain`, '.html');
    outputPath = path.join(exportDir, `${stem}.html`);
    await fs.writeFile(outputPath, html, 'utf8');
  } else {
    const html = await buildHtmlDoc(manifest, items, generatedAt);
    if (format === 'html') {
      const stem = await nextAvailableStem(exportDir, base, '.html');
      outputPath = path.join(exportDir, `${stem}.html`);
      await fs.writeFile(outputPath, html, 'utf8');
    } else {
      const stem = await nextAvailableStem(exportDir, base, '.pdf');
      outputPath = path.join(exportDir, `${stem}.pdf`);
      await htmlToPdf(dir, html, outputPath);
    }
  }

  mainLog.info(`exported ${format} → ${outputPath}`);
  shell.showItemInFolder(outputPath);
  return { format, outputPath };
}
