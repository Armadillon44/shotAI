// Native Microsoft Word (.docx) export (D3). Builds a real Word document from the
// same fail-closed ExportItem[] the other exporters use — so it can only ever
// embed the redaction-baked renders, never raw screenshots. Pure-JS `docx` lib
// (no CDN, no native deps).
import {
  AlignmentType,
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
import { CALLOUT_GLYPH, type CalloutKind, type ProjectManifest, isCalloutKind } from '../shared/project';
import { hexNoHash, type Palette } from '../shared/theme-palette';
import { DEFAULT_EXPORT_THEME, type ExportTheme } from '../shared/export-theme';
import { loadItemImage, type ExportItem } from './export';
import {
  docxImgMaxW,
  DOCX_PAGE_W_TWIPS,
  DOCX_PAGE_MARGIN_TWIPS,
} from './export-geometry';

// Image width now comes from docxImgMaxW() in export-geometry.ts, which derives the
// ceiling from stepCard()'s own insets and borders and honors the project scale (#70).
// It lives there because this module reaches electron through ./export and so cannot
// be unit-tested, and the clipping bug it fixes was invisible at 100%.

// Colored-callout palette (fill, border, text color). `section` is NOT here — it's
// a non-counted divider heading, not a colored box (handled before this lookup).
//
// Built per export rather than at module load, because the palette is now the
// brand's and the brand is an argument. Everything reaches the library through
// hexNoHash: docx renders an unparseable colour as BLACK, which looks like a
// design decision rather than a bug.
type CalloutStyle = { fill: string; bd: string; fg: string; label: string };
function calloutStyles(C: Palette): Record<Exclude<CalloutKind, 'section'>, CalloutStyle> {
  return {
    note: { fill: hexNoHash(C.noteBg), bd: hexNoHash(C.noteBd), fg: hexNoHash(C.noteFg), label: 'Note' },
    caution: { fill: hexNoHash(C.cautBg), bd: hexNoHash(C.cautBd), fg: hexNoHash(C.cautFg), label: 'Caution' },
    warning: { fill: hexNoHash(C.warnBg), bd: hexNoHash(C.warnBd), fg: hexNoHash(C.warnFg), label: 'Warning' },
  };
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
  theme: ExportTheme = DEFAULT_EXPORT_THEME,
): Promise<Buffer> {
  const C = theme.palette;
  // Step-card colours (#40) — the same card the HTML export and the report draw.
  const CARD_FILL = hexNoHash(C.surface2);
  const CARD_BORDER = hexNoHash(C.hair);
  const INTRO_FILL = hexNoHash(C.accentTint);
  const CALLOUT = calloutStyles(C);
  const children: (Paragraph | Table)[] = [];

  children.push(new Paragraph({ text: manifest.title, heading: HeadingLevel.TITLE }));
  children.push(
    new Paragraph({
      spacing: { after: 240 },
      children: [new TextRun({ text: createdLine, color: hexNoHash(C.ink3), size: 18 })],
    }),
  );

  // Intro → a tinted "Overview" card (mirrors the HTML intro box).
  if (manifest.intro && (manifest.intro.heading || manifest.intro.body)) {
    const introContent: Paragraph[] = [
      new Paragraph({
        spacing: { before: 0, after: 40 },
        children: [new TextRun({ text: 'OVERVIEW', bold: true, color: hexNoHash(C.ink3), size: 15 })],
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
      if (it.callout === 'section') {
        // Non-counted phase divider: a top rule (denoting a new section) ABOVE a
        // bold heading + muted body. No card, no colored box. The rule goes on the
        // first paragraph (heading if present, else body).
        const rule = { top: { style: BorderStyle.SINGLE, size: 6, color: hexNoHash(C.hair), space: 8 } } as const;
        if (it.heading) {
          children.push(
            new Paragraph({
              heading: HeadingLevel.HEADING_2,
              spacing: { before: 280, after: it.body ? 60 : 120 },
              border: rule,
              text: it.heading,
            }),
          );
          if (it.body) {
            children.push(new Paragraph({ children: multiline(it.body, { color: hexNoHash(C.ink3) }), spacing: { after: 160 } }));
          }
        } else if (it.body) {
          children.push(
            new Paragraph({ children: multiline(it.body, { color: hexNoHash(C.ink3) }), spacing: { before: 280, after: 160 }, border: rule }),
          );
        }
        continue;
      }
      // isCalloutKind, not truthiness: CALLOUT[unknown] is undefined and `.fg`
      // threw. The value is kept on read for forward-compat, so it must be
      // narrowed HERE (#90).
      if (isCalloutKind(it.callout)) {
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
    // Named `fit` to keep it distinct from the project scale below.
    const cap = docxImgMaxW(manifest.displayScale);
    const fit = width > cap ? cap / width : 1;
    const content: Paragraph[] = [
      new Paragraph({
        heading: HeadingLevel.HEADING_2,
        spacing: { before: 0, after: 100 },
        text: `${it.n}. ${it.caption || `Step ${it.n}`}`,
      }),
      new Paragraph({
        // #46: center the image so a narrow capture sits in a uniform frame
        // rather than hugging the left of the text column.
        alignment: AlignmentType.CENTER,
        spacing: { after: it.body ? 100 : 0 },
        children: [
          new ImageRun({
            type: it.mediaType === 'image/jpeg' ? 'jpg' : 'png',
            data: buffer,
            transformation: { width: Math.round(width * fit), height: Math.round(height * fit) },
          }),
        ],
      }),
    ];
    if (it.body) content.push(new Paragraph({ children: multiline(it.body) }));
    children.push(stepCard(content, CARD_FILL, CARD_BORDER));
    children.push(spacer());
  }

  // Page size and margins set EXPLICITLY, matching what the library default was
  // already producing (verified by unzipping a real export: A4 11906x16838, 1440
  // twip margins). Stated here so export-geometry.ts can derive the image ceiling
  // from our own source rather than from a library default. Deliberately NOT
  // changed to Letter: that would alter every existing export.
  const section: ISectionOptions = {
    properties: {
      page: {
        size: { width: DOCX_PAGE_W_TWIPS, height: 16838 },
        margin: {
          top: DOCX_PAGE_MARGIN_TWIPS,
          right: DOCX_PAGE_MARGIN_TWIPS,
          bottom: DOCX_PAGE_MARGIN_TWIPS,
          left: DOCX_PAGE_MARGIN_TWIPS,
        },
      },
    },
    children,
  };
  const doc = new Document({
    creator: 'shotAI',
    title: manifest.title,
    // Default the whole document to Aptos (Word's modern default) instead of the
    // docx library's Times New Roman. Set on docDefaults so Normal + the built-in
    // heading/title styles all inherit it (our runs don't pin a font).
    styles: { default: { document: { run: { font: 'Aptos' } } } },
    sections: [section],
  });
  return Packer.toBuffer(doc);
}
