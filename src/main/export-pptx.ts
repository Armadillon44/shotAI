// Native Microsoft PowerPoint (.pptx) export (D4). One slide per step, good for
// training decks / screen-shares. Built from the same fail-closed ExportItem[] as
// every other exporter, so slides only ever contain the redaction-baked renders.
// Pure-JS `pptxgenjs` (no CDN, no native deps).
import pptxgen from 'pptxgenjs';
import { clampScale } from '../shared/doc-scale';
import { CALLOUT_GLYPH, type CalloutKind, type ProjectManifest } from '../shared/project';
import { hexNoHash, SLIDE_LIGHT, SLIDE_EXTRAS } from '../shared/theme-palette';
import { loadItemImage, type ExportItem } from './export';

// LAYOUT_WIDE = 13.333in × 7.5in. All positions below are in inches.
const SLIDE_W = 13.333;
const SLIDE_H = 7.5;
const MARGIN = 0.5;

// Step-card geometry (#40): each step slide gets a rounded card; content is inset
// by PAD inside it. Mirrors the HTML/report step cards.
const CARD = { x: 0.45, y: 0.45, w: SLIDE_W - 0.9, h: SLIDE_H - 0.9 };
const PAD = 0.35;
const INNER = { x: CARD.x + PAD, y: CARD.y + PAD, w: CARD.w - PAD * 2, h: CARD.h - PAD * 2 };
const CARD_FILL = hexNoHash(SLIDE_LIGHT.surface2);
const CARD_BORDER = hexNoHash(SLIDE_LIGHT.hair);

// Colored-callout palette. `section` is NOT here — it renders as a divider slide,
// not a filled box (handled before this lookup).
const CALLOUT: Record<Exclude<CalloutKind, 'section'>, { fill: string; bd: string; fg: string; label: string }> = {
  note: { fill: hexNoHash(SLIDE_LIGHT.noteBg), bd: hexNoHash(SLIDE_LIGHT.noteBd), fg: hexNoHash(SLIDE_LIGHT.noteFg), label: 'Note' },
  caution: { fill: hexNoHash(SLIDE_LIGHT.cautBg), bd: hexNoHash(SLIDE_LIGHT.cautBd), fg: hexNoHash(SLIDE_LIGHT.cautFg), label: 'Caution' },
  warning: { fill: hexNoHash(SLIDE_LIGHT.warnBg), bd: hexNoHash(SLIDE_LIGHT.warnBd), fg: hexNoHash(SLIDE_LIGHT.warnFg), label: 'Warning' },
};

/** Fit (w×h px) inside the box preserving aspect; return centered inches. */
function fitContain(
  pxW: number,
  pxH: number,
  box: { x: number; y: number; w: number; h: number },
): { x: number; y: number; w: number; h: number } {
  const ar = pxW > 0 && pxH > 0 ? pxW / pxH : 1;
  let w = box.w;
  let h = w / ar;
  if (h > box.h) {
    h = box.h;
    w = h * ar;
  }
  return { x: box.x + (box.w - w) / 2, y: box.y + (box.h - h) / 2, w, h };
}

export async function buildPptx(
  manifest: ProjectManifest,
  items: ExportItem[],
  createdLine: string,
): Promise<Buffer> {
  const pptx = new pptxgen();
  pptx.layout = 'LAYOUT_WIDE';
  pptx.author = 'shotAI';
  pptx.title = manifest.title;

  // A rounded "card" behind the step content (drawn first so content lands on top).
  const addCard = (slide: pptxgen.Slide, fill: string, line: string): void => {
    slide.addShape(pptx.ShapeType.roundRect, {
      x: CARD.x,
      y: CARD.y,
      w: CARD.w,
      h: CARD.h,
      fill: { color: fill },
      line: { color: line, width: 1 },
      rectRadius: 0.12,
    });
  };

  // Title slide (cover — no card, matches the HTML title/meta).
  const title = pptx.addSlide();
  title.background = { color: hexNoHash(SLIDE_LIGHT.surface) };
  title.addText(manifest.title, {
    x: MARGIN,
    y: manifest.intro && (manifest.intro.heading || manifest.intro.body) ? 2.2 : 3.0,
    w: SLIDE_W - MARGIN * 2,
    h: 1.2,
    fontSize: 40,
    bold: true,
    color: hexNoHash(SLIDE_LIGHT.ink),
    align: 'center',
  });
  const introBits: string[] = [];
  if (manifest.intro?.heading) introBits.push(manifest.intro.heading);
  if (manifest.intro?.body) introBits.push(manifest.intro.body);
  if (introBits.length) {
    title.addText(introBits.join('\n\n'), {
      x: MARGIN + 1,
      y: 3.6,
      w: SLIDE_W - (MARGIN + 1) * 2,
      h: 2.6,
      fontSize: 16,
      color: hexNoHash(SLIDE_LIGHT.ink2),
      align: 'center',
      valign: 'top',
    });
  }
  title.addText(createdLine, {
    x: MARGIN,
    y: SLIDE_H - 0.7,
    w: SLIDE_W - MARGIN * 2,
    h: 0.4,
    fontSize: 10,
    color: hexNoHash(SLIDE_LIGHT.ink3),
    align: 'center',
  });

  for (const it of items) {
    const slide = pptx.addSlide();
    slide.background = { color: hexNoHash(SLIDE_LIGHT.surface) };

    if (it.kind === 'text') {
      if (it.callout === 'section') {
        // Divider slide: a thin rule ABOVE a large centered heading + muted body
        // (the rule denotes entering a new section). No card.
        slide.addShape(pptx.ShapeType.line, {
          x: SLIDE_W / 2 - 2,
          y: 2.9,
          w: 4,
          h: 0,
          line: { color: hexNoHash(SLIDE_LIGHT.controlBd), width: 1 },
        });
        if (it.heading) {
          slide.addText(it.heading, {
            x: MARGIN,
            y: 3.05,
            w: SLIDE_W - MARGIN * 2,
            h: 1.0,
            fontSize: 34,
            bold: true,
            color: hexNoHash(SLIDE_LIGHT.ink),
            align: 'center',
            valign: 'top',
          });
        }
        if (it.body) {
          slide.addText(it.body, {
            x: MARGIN + 1.5,
            y: 4.2,
            w: SLIDE_W - (MARGIN + 1.5) * 2,
            h: 2,
            fontSize: 16,
            color: hexNoHash(SLIDE_EXTRAS.captionInk),
            align: 'center',
            valign: 'top',
          });
        }
        continue;
      }
      if (it.callout) {
        // Callout slide: the card is tinted by kind; content sits inside it.
        const c = CALLOUT[it.callout];
        addCard(slide, c.fill, c.bd);
        slide.addText(
          [
            {
              text: `${CALLOUT_GLYPH[it.callout]} ${it.heading || c.label}\n`,
              options: { bold: true, fontSize: 24, color: c.fg },
            },
            { text: it.body || '', options: { fontSize: 18, color: c.fg } },
          ],
          { x: INNER.x, y: INNER.y, w: INNER.w, h: INNER.h, align: 'left', valign: 'top' },
        );
        continue;
      }
      // Plain text step — a numbered step slide inside a card. With a heading,
      // "N. heading" is the title and the body sits below; with NO heading, the
      // body IS the title content ("2. Some text").
      addCard(slide, CARD_FILL, CARD_BORDER);
      const numPrefix = it.n != null ? `${it.n}. ` : '';
      slide.addText(`${numPrefix}${it.heading || it.body}`, {
        x: INNER.x,
        y: INNER.y,
        w: INNER.w,
        h: 1,
        fontSize: 26,
        bold: true,
        color: hexNoHash(SLIDE_LIGHT.ink),
        valign: 'top',
      });
      if (it.heading && it.body) {
        slide.addText(it.body, {
          x: INNER.x,
          y: INNER.y + 1.15,
          w: INNER.w,
          h: INNER.h - 1.15,
          fontSize: 18,
          color: hexNoHash(SLIDE_EXTRAS.bodyInk),
          valign: 'top',
        });
      }
      continue;
    }

    // Shot slide: card, caption title, contained image, optional instruction.
    addCard(slide, CARD_FILL, CARD_BORDER);
    slide.addText(`${it.n}. ${it.caption || `Step ${it.n}`}`, {
      x: INNER.x,
      y: INNER.y,
      w: INNER.w,
      h: 0.6,
      fontSize: 20,
      bold: true,
      color: hexNoHash(SLIDE_LIGHT.ink),
      valign: 'top',
    });
    const hasBody = !!it.body;
    const imgTop = INNER.y + 0.75;
    // #70: the slide is a FIXED canvas, so the document scale can only shrink the
    // image box, never grow it (min(1, scale)). Shrinking both axes keeps the
    // proportion whether the capture is width- or height-limited, and the box is
    // re-centred so a smaller image sits in the middle of the slide rather than
    // hugging the left edge.
    const shrink = Math.min(1, clampScale(manifest.displayScale));
    const fullW = INNER.w;
    const fullH = hasBody ? INNER.h - 0.75 - 1.2 : INNER.h - 0.75;
    const box = {
      x: INNER.x + (fullW - fullW * shrink) / 2,
      y: imgTop,
      w: fullW * shrink,
      h: fullH * shrink,
    };
    const { buffer, width, height } = await loadItemImage(it);
    const b64 = buffer.toString('base64');
    const fit = fitContain(width, height, box);
    slide.addImage({ data: `data:${it.mediaType};base64,${b64}`, x: fit.x, y: fit.y, w: fit.w, h: fit.h });
    if (hasBody) {
      slide.addText(it.body, {
        x: INNER.x,
        y: INNER.y + INNER.h - 1.1,
        w: INNER.w,
        h: 1.1,
        fontSize: 15,
        color: hexNoHash(SLIDE_EXTRAS.bodyInk),
        valign: 'top',
      });
    }
  }

  // write() with nodebuffer returns a Node Buffer in the main process.
  const out = await pptx.write({ outputType: 'nodebuffer' });
  return out as Buffer;
}
