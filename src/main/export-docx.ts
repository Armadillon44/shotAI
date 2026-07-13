// Native Microsoft Word (.docx) export (D3). Builds a real Word document from the
// same fail-closed ExportItem[] the other exporters use — so it can only ever
// embed the redaction-baked renders, never raw screenshots. Pure-JS `docx` lib
// (no CDN, no native deps).
import {
  BorderStyle,
  Document,
  HeadingLevel,
  ImageRun,
  Packer,
  Paragraph,
  ShadingType,
  TextRun,
  type ISectionOptions,
} from 'docx';
import { CALLOUT_GLYPH, type CalloutKind, type ProjectManifest } from '../shared/project';
import { loadItemImage, type ExportItem } from './export';

// Max embedded image width in px; taller/wider shots scale down by aspect. Keeps
// images inside the page's text column (~6.5in ≈ 624px at 96dpi).
const MAX_IMG_W = 600;

// Callout palette mirrors the in-app / HTML export (fill, left-bar, text color).
const CALLOUT: Record<CalloutKind, { fill: string; bar: string; fg: string; label: string }> = {
  note: { fill: 'ECFDF5', bar: '34D399', fg: '065F46', label: 'Note' },
  caution: { fill: 'FFFBEB', bar: 'F59E0B', fg: '92400E', label: 'Caution' },
  warning: { fill: 'FEF2F2', bar: 'EF4444', fg: '991B1B', label: 'Warning' },
};

function calloutParagraphs(heading: string, body: string, kind: CalloutKind): Paragraph[] {
  const c = CALLOUT[kind];
  const shading = { type: ShadingType.CLEAR, color: 'auto', fill: c.fill } as const;
  const border = {
    left: { style: BorderStyle.SINGLE, size: 18, color: c.bar, space: 12 },
  } as const;
  const runs: Paragraph[] = [];
  // Lead with the type glyph so the callout kind reads even in grayscale.
  const headingText = `${CALLOUT_GLYPH[kind]} ${heading || c.label}`;
  runs.push(
    new Paragraph({
      shading,
      border,
      spacing: { before: 160, after: body ? 0 : 160 },
      children: [new TextRun({ text: headingText, bold: true, color: c.fg })],
    }),
  );
  if (body) {
    runs.push(
      new Paragraph({
        shading,
        border,
        spacing: { after: 160 },
        children: [new TextRun({ text: body, color: c.fg })],
      }),
    );
  }
  return runs;
}

/** Split a multi-line string into one TextRun per line with proper line breaks. */
function multiline(text: string, opts?: { italics?: boolean; color?: string }): TextRun[] {
  const lines = text.split('\n');
  return lines.map(
    (line, i) =>
      new TextRun({
        text: line,
        italics: opts?.italics,
        color: opts?.color,
        break: i > 0 ? 1 : undefined,
      }),
  );
}

export async function buildDocx(
  manifest: ProjectManifest,
  items: ExportItem[],
  createdLine: string,
): Promise<Buffer> {
  const children: Paragraph[] = [];

  children.push(new Paragraph({ text: manifest.title, heading: HeadingLevel.TITLE }));
  children.push(
    new Paragraph({
      spacing: { after: 240 },
      children: [new TextRun({ text: createdLine, color: '6B7280', size: 18 })],
    }),
  );

  if (manifest.intro && (manifest.intro.heading || manifest.intro.body)) {
    if (manifest.intro.heading) {
      children.push(new Paragraph({ text: manifest.intro.heading, heading: HeadingLevel.HEADING_2 }));
    }
    if (manifest.intro.body) {
      children.push(new Paragraph({ children: multiline(manifest.intro.body), spacing: { after: 160 } }));
    }
  }

  for (const it of items) {
    if (it.kind === 'text') {
      if (it.callout) {
        children.push(...calloutParagraphs(it.heading, it.body, it.callout));
        continue;
      }
      // Plain text step — numbered like a step (matches the report + other formats).
      const num = it.n != null ? `${it.n}. ` : '';
      if (it.heading) {
        children.push(new Paragraph({ text: `${num}${it.heading}`, heading: HeadingLevel.HEADING_2 }));
        if (it.body) {
          children.push(new Paragraph({ children: multiline(it.body), spacing: { after: 120 } }));
        }
      } else if (it.body) {
        children.push(new Paragraph({ children: multiline(`${num}${it.body}`), spacing: { after: 120 } }));
      }
      continue;
    }
    // Shot step: numbered heading, image (aspect-scaled), instruction.
    const { buffer, width, height } = await loadItemImage(it);
    const scale = width > MAX_IMG_W ? MAX_IMG_W / width : 1;
    children.push(
      new Paragraph({
        heading: HeadingLevel.HEADING_2,
        spacing: { before: 240 },
        text: `${it.n}. ${it.caption || `Step ${it.n}`}`,
      }),
    );
    children.push(
      new Paragraph({
        spacing: { after: it.body ? 80 : 200 },
        children: [
          new ImageRun({
            type: it.mediaType === 'image/jpeg' ? 'jpg' : 'png',
            data: buffer,
            transformation: { width: Math.round(width * scale), height: Math.round(height * scale) },
          }),
        ],
      }),
    );
    if (it.body) {
      children.push(new Paragraph({ children: multiline(it.body), spacing: { after: 200 } }));
    }
  }

  const section: ISectionOptions = { properties: {}, children };
  const doc = new Document({
    creator: 'shotAI',
    title: manifest.title,
    sections: [section],
  });
  return Packer.toBuffer(doc);
}
