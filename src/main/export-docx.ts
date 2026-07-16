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
  Table,
  TableCell,
  TableRow,
  TextRun,
  WidthType,
  type ISectionOptions,
} from 'docx';
import { CALLOUT_GLYPH, type CalloutKind, type ProjectManifest } from '../shared/project';
import { loadItemImage, type ExportItem } from './export';

// Max embedded image width in px; taller/wider shots scale down by aspect. Keeps
// images inside the page's text column (~6.5in ≈ 624px at 96dpi).
const MAX_IMG_W = 560;

// Step-card colors (#40) — mirror the HTML export / in-app report (light-only).
const CARD_FILL = 'FAF9FF';
const CARD_BORDER = 'E7E4F2';
const INTRO_FILL = 'EFEAFE';

// Callout palette mirrors the in-app / HTML export (fill, border, text color).
const CALLOUT: Record<CalloutKind, { fill: string; bd: string; fg: string; label: string }> = {
  note: { fill: 'ECFDF5', bd: '6EE7B7', fg: '065F46', label: 'Note' },
  caution: { fill: 'FFFBEB', bd: 'FCD34D', fg: '92400E', label: 'Caution' },
  warning: { fill: 'FEF2F2', bd: 'FCA5A5', fg: '991B1B', label: 'Warning' },
};

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

/**
 * Wrap a step's content in a single-cell table — a bordered, shaded "card" (#40) —
 * so each step reads as a distinct framed unit, matching the HTML/PDF exports.
 */
function stepCard(content: Paragraph[], fill: string, border: string): Table {
  const b = { style: BorderStyle.SINGLE, size: 4, color: border } as const;
  return new Table({
    width: { size: 100, type: WidthType.PERCENTAGE },
    borders: { top: b, bottom: b, left: b, right: b, insideHorizontal: b, insideVertical: b },
    rows: [
      new TableRow({
        children: [
          new TableCell({
            shading: { type: ShadingType.CLEAR, color: 'auto', fill },
            margins: { top: 120, bottom: 120, left: 180, right: 180 },
            children: content,
          }),
        ],
      }),
    ],
  });
}

/** A thin gap between step cards (Word needs a paragraph between tables anyway). */
function spacer(): Paragraph {
  return new Paragraph({ spacing: { after: 60 }, children: [new TextRun({ text: '', size: 10 })] });
}

export async function buildDocx(
  manifest: ProjectManifest,
  items: ExportItem[],
  createdLine: string,
): Promise<Buffer> {
  const children: (Paragraph | Table)[] = [];

  children.push(new Paragraph({ text: manifest.title, heading: HeadingLevel.TITLE }));
  children.push(
    new Paragraph({
      spacing: { after: 240 },
      children: [new TextRun({ text: createdLine, color: '6B7280', size: 18 })],
    }),
  );

  // Intro → a tinted "Overview" card (mirrors the HTML intro box).
  if (manifest.intro && (manifest.intro.heading || manifest.intro.body)) {
    const introContent: Paragraph[] = [
      new Paragraph({
        spacing: { before: 0, after: 40 },
        children: [new TextRun({ text: 'OVERVIEW', bold: true, color: '6B7280', size: 15 })],
      }),
    ];
    if (manifest.intro.heading) {
      introContent.push(
        new Paragraph({ spacing: { after: manifest.intro.body ? 40 : 0 }, children: [new TextRun({ text: manifest.intro.heading, bold: true, size: 26 })] }),
      );
    }
    if (manifest.intro.body) {
      introContent.push(new Paragraph({ children: multiline(manifest.intro.body) }));
    }
    children.push(stepCard(introContent, INTRO_FILL, CARD_BORDER));
    children.push(spacer());
  }

  for (const it of items) {
    if (it.kind === 'text') {
      if (it.callout) {
        const c = CALLOUT[it.callout];
        const content: Paragraph[] = [
          new Paragraph({
            spacing: { before: 0, after: it.body ? 60 : 0 },
            children: [
              new TextRun({ text: `${CALLOUT_GLYPH[it.callout]} ${it.heading || c.label}`, bold: true, color: c.fg }),
            ],
          }),
        ];
        if (it.body) content.push(new Paragraph({ children: multiline(it.body, { color: c.fg }) }));
        children.push(stepCard(content, c.fill, c.bd));
        children.push(spacer());
        continue;
      }
      // Plain text step — numbered like a step (matches the report + other formats).
      const num = it.n != null ? `${it.n}. ` : '';
      const content: Paragraph[] = [];
      if (it.heading) {
        content.push(
          new Paragraph({ heading: HeadingLevel.HEADING_2, spacing: { before: 0, after: it.body ? 100 : 0 }, text: `${num}${it.heading}` }),
        );
        if (it.body) content.push(new Paragraph({ children: multiline(it.body) }));
      } else if (it.body) {
        content.push(new Paragraph({ children: multiline(`${num}${it.body}`) }));
      }
      if (content.length) {
        children.push(stepCard(content, CARD_FILL, CARD_BORDER));
        children.push(spacer());
      }
      continue;
    }
    // Shot step: numbered heading, image (aspect-scaled), instruction — all in one card.
    const { buffer, width, height } = await loadItemImage(it);
    const scale = width > MAX_IMG_W ? MAX_IMG_W / width : 1;
    const content: Paragraph[] = [
      new Paragraph({
        heading: HeadingLevel.HEADING_2,
        spacing: { before: 0, after: 100 },
        text: `${it.n}. ${it.caption || `Step ${it.n}`}`,
      }),
      new Paragraph({
        spacing: { after: it.body ? 100 : 0 },
        children: [
          new ImageRun({
            type: it.mediaType === 'image/jpeg' ? 'jpg' : 'png',
            data: buffer,
            transformation: { width: Math.round(width * scale), height: Math.round(height * scale) },
          }),
        ],
      }),
    ];
    if (it.body) content.push(new Paragraph({ children: multiline(it.body) }));
    children.push(stepCard(content, CARD_FILL, CARD_BORDER));
    children.push(spacer());
  }

  const section: ISectionOptions = { properties: {}, children };
  const doc = new Document({
    creator: 'shotAI',
    title: manifest.title,
    sections: [section],
  });
  return Packer.toBuffer(doc);
}
