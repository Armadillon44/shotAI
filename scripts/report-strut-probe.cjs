// Diagnostic for the report image wrap's vertical overflow.
//
// Review claim: .rep__imgwrap's content box is set to exactly the image height, but
// its only child (.rep__imginner) is inline-block, so the block container has a line
// box whose STRUT adds ~4px of descent below the image. scrollHeight then exceeds
// clientHeight with no image in the extra band, the pan-restore effect centres that
// phantom range, and ~2px is scrolled off the TOP of every screenshot.
//
// This measures it against the REAL project.css and the REAL DOM chain, before and
// after the candidate fix, rather than taking the claim on trust.
//
// Run: env -u ELECTRON_RUN_AS_NODE npx electron scripts/report-strut-probe.cjs
const { app, BrowserWindow } = require('electron');
const fs = require('node:fs');
const path = require('node:path');

const CSS = fs.readFileSync(
  path.join(__dirname, '..', 'src', 'renderer', 'project', 'project.css'),
  'utf8',
);

// A 1x1 transparent GIF. The image's own pixels are irrelevant: the app sets the
// <img> layout size inline, and the strut comes from the inline-block child, not
// from the image content.
const PX =
  'data:image/gif;base64,R0lGODlhAQABAIAAAAAAAP///yH5BAEAAAAALAAAAAABAAEAAAIBRAA7';

// From report-geometry.reportFit({w:1920,h:1200}, 786, 1): baseW 784, baseH 490,
// wrap 786x492 (image + 1px border each side).
const CASES = [
  { label: '1920x1200 desktop', baseW: 784, baseH: 490 },
  { label: '2924x1826 hi-dpi', baseW: 784, baseH: 489 },
  { label: '1366x768', baseW: 784, baseH: 440 },
  { label: '300x200 small', baseW: 300, baseH: 200 },
];

function page(extraCss) {
  const rows = CASES.map(
    (c, i) => `
    <div class="rep__bodywrap">
      <figure class="rep__figure">
        <div class="rep__figbox">
          <div class="rep__imgwrap" id="w${i}" style="width:${c.baseW + 2}px;height:${c.baseH + 2}px">
            <div class="rep__imginner">
              <img class="rep__img" src="${PX}" style="width:${c.baseW}px;height:${c.baseH}px">
            </div>
          </div>
        </div>
      </figure>
    </div>`,
  ).join('');
  return (
    'data:text/html;charset=utf-8,' +
    encodeURIComponent(
      `<style>${CSS}\n${extraCss}</style><div class="rep" style="width:880px">${rows}</div>`,
    )
  );
}

async function measure(win, extraCss, label) {
  await win.loadURL(page(extraCss));
  const out = await win.webContents.executeJavaScript(`
    (() => ${JSON.stringify(CASES.map((c, i) => i))}.map((i) => {
      const el = document.getElementById('w' + i);
      const cs = getComputedStyle(el);
      return {
        clientH: el.clientHeight,
        scrollH: el.scrollHeight,
        rangeY: el.scrollHeight - el.clientHeight,
        clientW: el.clientWidth,
        scrollW: el.scrollWidth,
        rangeX: el.scrollWidth - el.clientWidth,
        lineHeight: cs.lineHeight,
      };
    }))()
  `);
  console.log('');
  console.log('== ' + label + ' ==');
  out.forEach((m, i) => {
    const clipped = Math.round(m.rangeY * 0.5); // pan-restore centres the range
    console.log(
      `  ${CASES[i].label.padEnd(20)} rangeY=${m.rangeY} (clips ~${clipped}px off the top)` +
        `  rangeX=${m.rangeX}  line-height=${m.lineHeight}`,
    );
  });
  return out;
}

app.whenReady().then(async () => {
  const win = new BrowserWindow({ width: 1000, height: 800, show: false });

  const before = await measure(win, '', 'BEFORE (current CSS)');
  const afterLH = await measure(
    win,
    '.rep__imgwrap{line-height:0}',
    'AFTER candidate 1: line-height:0 on .rep__imgwrap',
  );
  const afterBlock = await measure(
    win,
    '.rep__imginner{display:block}',
    'AFTER candidate 2: display:block on .rep__imginner',
  );

  const sum = (a) => a.reduce((n, m) => n + m.rangeY, 0);
  console.log('');
  console.log('[probe] VERDICT');
  console.log('  total phantom rangeY  before: ' + sum(before));
  console.log('  after line-height:0        : ' + sum(afterLH));
  console.log('  after display:block       : ' + sum(afterBlock));
  const xOk = afterLH.every((m, i) => m.rangeX === before[i].rangeX);
  console.log(
    '  horizontal range unchanged by candidate 1: ' + (xOk ? 'yes' : 'NO — it moved, investigate'),
  );
  console.log(
    sum(before) > 0
      ? '  => the claim reproduces.'
      : '  => the claim does NOT reproduce; do not change the CSS.',
  );
  win.destroy();
  app.quit();
});
