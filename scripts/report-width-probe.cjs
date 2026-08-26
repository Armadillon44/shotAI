const fs = require('node:fs');
const { app, BrowserWindow } = require('electron');
const CSS = fs.readFileSync('src/renderer/project/project.css', 'utf8');

// Why this exists: .rep is a flex ITEM of .detail (flex column). An auto margin on
// the CROSS axis disables stretch, so "margin: 0 auto" with no width made the report
// column size to its CONTENT — a small project rendered at 286px instead of 880, and
// the #70 size slider appeared to do nothing because max-width can only CAP a
// content-sized box. Predates #70; typical content hid it.
//
// Run: env -u ELECTRON_RUN_AS_NODE npx electron scripts/report-width-probe.cjs
//
// The CONTROL (block parent) is not decoration: without it, a run showing no
// difference proves nothing, because the harness might simply be blind.
// The real chain: .detail (flex column) > .rep.
function page(extra, scale, contentW) {
  const body = `<div class="project__body project__body--detail" style="width:1100px">
    <section class="detail">
      <div class="detail__bar">bar</div>
      <div class="rep" id="rep" style="--doc-scale:${scale}">
        <div class="rep__step"><div class="rep__rail">1</div>
          <div class="rep__bodywrap" id="bw">
            <div style="width:${contentW}px;height:40px">content</div>
          </div></div>
      </div>
    </section></div>`;
  return 'data:text/html;charset=utf-8,' + encodeURIComponent('<style>' + CSS + '\n' + extra + '</style>' + body);
}

async function m(win, extra, scale, contentW, label) {
  await win.loadURL(page(extra, scale, contentW));
  const r = await win.webContents.executeJavaScript(`new Promise((res) =>
    requestAnimationFrame(() => requestAnimationFrame(() => {
      const rep = document.getElementById('rep'), bw = document.getElementById('bw');
      const cs = getComputedStyle(rep);
      res({ rep: Math.round(rep.getBoundingClientRect().width),
            bw: Math.round(bw.getBoundingClientRect().width),
            maxW: cs.maxWidth, alignSelf: cs.alignSelf, width: cs.width });
    })))`);
  console.log('  ' + label.padEnd(40) + 'rep=' + String(r.rep).padStart(5) +
    '  card=' + String(r.bw).padStart(5) + '  max-width=' + r.maxW);
  return r;
}

app.whenReady().then(async () => {
  const win = new BrowserWindow({ width: 1300, height: 800, show: false });
  console.log('CONTROL: .detail forced to block — .rep should fill to its max-width');
  const ctrl100 = await m(win, '.detail{display:block}', 1, 200, 'block parent, scale 1.0, narrow content');
  const ctrl125 = await m(win, '.detail{display:block}', 1.25, 200, 'block parent, scale 1.25, narrow content');
  console.log('');
  console.log('CURRENT STYLESHEET (the fix is width:100% on .rep):');
  const a = await m(win, '', 1, 200, 'scale 1.00, narrow content');
  const b = await m(win, '', 1.25, 200, 'scale 1.25, narrow content');
  const c = await m(win, '', 1.25, 900, 'scale 1.25, WIDE content');
  console.log('');
  console.log('THE BUG, reproduced by removing the fix:');
  const f1 = await m(win, '.rep{width:auto}', 1, 200, 'scale 1.00, narrow content');
  const f2 = await m(win, '.rep{width:auto}', 1.25, 200, 'scale 1.25, narrow content');
  console.log('');
  console.log('[probe] instrument sensitivity: control widened with scale? ' +
    (ctrl125.rep > ctrl100.rep ? 'YES (' + ctrl100.rep + ' -> ' + ctrl125.rep + ')' : 'NO — harness is blind'));
  console.log('[probe] shipped responds to scale on narrow content? ' + (b.rep > a.rep ? 'yes' : 'NO — reproduces the bug'));
  console.log('[probe] shipped is content-sized? ' + (c.rep > b.rep ? 'yes (wide content widened it)' : 'no'));
  console.log('[probe] WITHOUT the fix, responds to scale? ' + (f2.rep > f1.rep ? 'yes' : 'NO — this is the bug (' + f1.rep + ' at both scales)'));
  win.destroy(); app.quit();
});
