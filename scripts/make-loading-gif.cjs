// One-off asset generator for the Squirrel install graphic (loadingGif).
// Renders an HTML/CSS animation in a headless Electron window, captures a fixed
// set of deterministic frames (via a renderFrame(t) hook so the loop is
// seamless), and encodes them into an animated GIF with the pure-JS `gifenc`.
//
// Run:  npx electron scripts/make-loading-gif.cjs
// Out:  assets/shotAI-install.gif   (referenced by forge.config.ts loadingGif)
const { app, BrowserWindow } = require('electron');
const fs = require('node:fs');
const path = require('node:path');

const W = 440;
const H = 300;
const FRAMES = 36;
const DELAY = 55; // ms/frame → ~2s loop

const ASSETS = path.join(__dirname, '..', 'assets');
const OUT = path.join(ASSETS, 'shotAI-install.gif');
const svg = fs.readFileSync(path.join(ASSETS, 'shotAI_icon_v3.svg'), 'utf8');
const iconUri = 'data:image/svg+xml;base64,' + Buffer.from(svg).toString('base64');

// A small 4-point sparkle (matches the icon's motif), tinted violet.
const sparkle = (color) =>
  `data:image/svg+xml;base64,` +
  Buffer.from(
    `<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 24 24"><path fill="${color}" d="M12 0c1.1 7 4 9.9 12 12-8 2.1-10.9 5-12 12-1.1-7-4-9.9-12-12 8-2.1 10.9-5 12-12Z"/></svg>`,
  ).toString('base64');

const html = `<!doctype html><html><head><meta charset="utf-8"><style>
  html,body{margin:0;width:${W}px;height:${H}px;overflow:hidden;background:#ffffff;
    font-family:"Segoe UI",system-ui,sans-serif}
  .stage{position:relative;width:${W}px;height:${H}px}
  #icon{position:absolute;left:50%;top:118px;width:150px;height:150px;
    transform:translate(-50%,-50%);filter:drop-shadow(0 8px 22px rgba(99,68,241,.28))}
  .spk{position:absolute;width:26px;height:26px;will-change:transform,opacity}
  #track{position:absolute;left:70px;top:214px;width:300px;height:9px;border-radius:9px;
    background:#ece9fb;overflow:hidden}
  #bar{position:absolute;top:0;height:9px;border-radius:9px;
    background:linear-gradient(90deg,#6344f1,#8b5cf6)}
  #label{position:absolute;left:0;top:238px;width:100%;text-align:center;
    color:#6b6d86;font-size:14px;font-weight:600;letter-spacing:.02em}
</style></head><body>
  <div class="stage">
    <img id="icon" src="${iconUri}">
    <div id="track"><div id="bar"></div></div>
    <div id="label">Installing shotAI…</div>
  </div>
  <script>
    const stage = document.querySelector('.stage');
    const CX = ${W / 2}, CY = 118;
    const COLORS = ['#8b5cf6', '#6344f1', '#a78bfa'];
    // Sparkles orbiting the icon. freq is an integer so the twinkle loops seamlessly.
    const SPK = [
      { ang: 0.0, r: 104, size: 22, freq: 2, phase: 0.0 },
      { ang: 1.1, r: 96, size: 15, freq: 3, phase: 0.4 },
      { ang: 2.2, r: 108, size: 18, freq: 2, phase: 0.7 },
      { ang: 3.3, r: 92, size: 13, freq: 3, phase: 0.2 },
      { ang: 4.3, r: 110, size: 20, freq: 2, phase: 0.9 },
      { ang: 5.3, r: 98, size: 14, freq: 3, phase: 0.5 },
    ];
    const els = SPK.map((s, i) => {
      const el = document.createElement('img');
      el.className = 'spk';
      el.src = '${''}' ; // set below
      el.style.width = el.style.height = s.size + 'px';
      el.dataset.i = i;
      stage.appendChild(el);
      return el;
    });
    const SPARKLE = [${JSON.stringify(sparkle('#8b5cf6'))}, ${JSON.stringify(sparkle('#6344f1'))}, ${JSON.stringify(sparkle('#a78bfa'))}];
    els.forEach((el, i) => (el.src = SPARKLE[i % SPARKLE.length]));

    const bar = document.getElementById('bar');
    const icon = document.getElementById('icon');
    window.renderFrame = (t) => {
      // gentle icon breathe
      icon.style.transform = 'translate(-50%,-50%) scale(' + (1 + 0.02 * Math.sin(2 * Math.PI * t)) + ')';
      SPK.forEach((s, i) => {
        const orbit = s.ang + 2 * Math.PI * t; // one revolution per loop
        const x = CX + Math.cos(orbit) * s.r;
        const y = CY + Math.sin(orbit) * s.r;
        const tw = Math.abs(Math.sin(Math.PI * (t * s.freq + s.phase))); // 0..1, loops
        const el = els[i];
        el.style.left = x + 'px';
        el.style.top = y + 'px';
        el.style.transform = 'translate(-50%,-50%) rotate(' + orbit + 'rad) scale(' + (0.5 + 0.9 * tw) + ')';
        el.style.opacity = String(0.25 + 0.75 * tw);
      });
      // Indeterminate sweep (Squirrel gives no real %): a segment crossing L→R.
      const segW = 34;
      bar.style.width = segW + '%';
      bar.style.left = (t * (100 + segW) - segW) + '%';
    };
    window.renderFrame(0);
  </script>
</body></html>`;

app.whenReady().then(async () => {
  const win = new BrowserWindow({
    width: W,
    height: H,
    useContentSize: true, // W×H is the web content area, not incl. any frame
    frame: false,
    show: false,
    webPreferences: { backgroundThrottling: false },
  });
  const frames = [];
  await win.loadURL('data:text/html;charset=utf-8,' + encodeURIComponent(html));

  const gifencMod = await import('gifenc');
  const { GIFEncoder, quantize, applyPalette } = gifencMod.default ?? gifencMod;
  const gif = GIFEncoder();
  let firstNonBg = 0;
  for (let i = 0; i < FRAMES; i++) {
    const t = i / FRAMES;
    await win.webContents.executeJavaScript(
      `renderFrame(${t}); new Promise(r => requestAnimationFrame(() => requestAnimationFrame(r)));`,
    );
    const img = await win.webContents.capturePage();
    const { width, height } = img.getSize();
    const bgra = img.toBitmap();
    const rgba = Buffer.allocUnsafe(bgra.length);
    for (let p = 0; p < bgra.length; p += 4) {
      rgba[p] = bgra[p + 2];
      rgba[p + 1] = bgra[p + 1];
      rgba[p + 2] = bgra[p];
      rgba[p + 3] = bgra[p + 3];
    }
    if (i === 0) {
      for (let p = 0; p < rgba.length; p += 4) {
        if (rgba[p] < 250 || rgba[p + 1] < 250 || rgba[p + 2] < 250) firstNonBg++;
      }
    }
    const palette = quantize(rgba, 256);
    const index = applyPalette(rgba, palette);
    gif.writeFrame(index, width, height, { palette, delay: DELAY });
    frames.push([width, height]);
    if (i === Math.round(FRAMES * 0.4)) {
      fs.writeFileSync(path.join(ASSETS, '_loading-preview.png'), img.toPNG());
    }
  }
  gif.finish();
  fs.writeFileSync(OUT, Buffer.from(gif.bytes()));
  const size = frames[0];
  // eslint-disable-next-line no-console
  console.log(`GIF written: ${OUT} · ${size[0]}x${size[1]} · ${FRAMES} frames · ${fs.statSync(OUT).size} bytes · frame0 non-bg px=${firstNonBg}`);
  app.quit();
}).catch((e) => {
  // eslint-disable-next-line no-console
  console.error('make-loading-gif failed:', e);
  app.exit(1);
});
