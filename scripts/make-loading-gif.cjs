// One-off asset generator for the Squirrel install graphic (loadingGif).
// Renders an HTML/CSS animation in a headless Electron window, captures a fixed
// set of deterministic frames (via a renderFrame(t) hook so the loop is
// seamless), masks the corners to a rounded rect, and encodes them into an
// animated GIF with the pure-JS `gifenc`.
//
// Run:  npx electron scripts/make-loading-gif.cjs
// Out:  assets/shotAI-install.gif   (referenced by forge.config.ts loadingGif)
//       assets/_loading-preview.png (mid-loop still, for eyeballing; not shipped)
const { app, BrowserWindow, nativeImage } = require('electron');
const fs = require('node:fs');
const path = require('node:path');

// Must precede app-ready. Without it the captured frame is scaled by whatever the
// authoring desktop's DPI happens to be (see the W/H note below).
app.commandLine.appendSwitch('force-device-scale-factor', '1');

// capturePage() returns PHYSICAL pixels, so the output size used to depend on the
// authoring machine's display scale — the previously shipped GIF is 552x378, i.e.
// this 440x300 layout inflated by a 1.25x desktop. force-device-scale-factor=1
// (below) pins 1 CSS px = 1 output px, which makes the asset reproducible AND
// renders the whole graphic at 0.8x of that shipped 552x378 — the 20% reduction,
// applied uniformly, without touching a single interior dimension.
const W = 440;
const H = 300;
const FRAMES = 36;
const DELAY = 55; // ms/frame → ~2s loop
const RADIUS = 22; // corner radius, masked in post (see maskCorners)

// Dark surface, matching the app's dark-theme `surface` token. The corners
// outside RADIUS are made fully transparent so the graphic reads as a floating
// rounded panel over whatever Squirrel paints behind it.
const CARD = '#1b1926';

const ASSETS = path.join(__dirname, '..', 'assets');
const OUT = path.join(ASSETS, 'shotAI-install.gif');
const svg = fs.readFileSync(path.join(ASSETS, 'shotAI_icon_v3.svg'), 'utf8');
const iconUri = 'data:image/svg+xml;base64,' + Buffer.from(svg).toString('base64');

const html = `<!doctype html><html><head><meta charset="utf-8"><style>
  html,body{margin:0;width:${W}px;height:${H}px;overflow:hidden;background:${CARD};
    font-family:"Segoe UI",system-ui,sans-serif}
  .stage{position:relative;width:${W}px;height:${H}px}
  #icon{position:absolute;left:50%;top:118px;width:150px;height:150px;
    transform:translate(-50%,-50%);filter:drop-shadow(0 8px 22px rgba(122,92,248,.45))}
  #track{position:absolute;left:70px;top:214px;width:300px;height:9px;border-radius:9px;
    background:#302c42;overflow:hidden}
  #bar{position:absolute;top:0;height:9px;border-radius:9px;
    background:linear-gradient(90deg,#7c5cf8,#a78bfa)}
  #label{position:absolute;left:0;top:238px;width:100%;text-align:center;
    color:#a8a4c0;font-size:14px;font-weight:600;letter-spacing:.02em}
</style></head><body>
  <div class="stage">
    <img id="icon" src="${iconUri}">
    <div id="track"><div id="bar"></div></div>
    <div id="label">Installing shotAI…</div>
  </div>
  <script>
    const bar = document.getElementById('bar');
    const icon = document.getElementById('icon');
    window.renderFrame = (t) => {
      // gentle icon breathe
      icon.style.transform = 'translate(-50%,-50%) scale(' + (1 + 0.02 * Math.sin(2 * Math.PI * t)) + ')';
      // Indeterminate sweep (Squirrel gives no real %): a segment crossing L→R.
      const segW = 34;
      bar.style.width = segW + '%';
      bar.style.left = (t * (100 + segW) - segW) + '%';
    };
    window.renderFrame(0);
  </script>
</body></html>`;

/**
 * Zero the alpha of every pixel outside a rounded rectangle, in place.
 *
 * The mask is computed here rather than with CSS `border-radius` on purpose: CSS
 * would anti-alias the arc into semi-transparent pixels, and a GIF only has
 * 1-bit alpha — those pixels would have to snap to either opaque (leaving a
 * stair-stepped dark fringe) or clear. Testing each pixel against the arc gives
 * one clean hard edge instead.
 */
function maskCorners(rgba, w, h, r) {
  let clear = 0;
  for (let y = 0; y < h; y++) {
    for (let x = 0; x < w; x++) {
      // Clamp toward the rect inset by r: dx/dy stay 0 unless this pixel is in a
      // corner band, so only the four corners get the circle test.
      const cx = x < r ? r : x > w - 1 - r ? w - 1 - r : x;
      const cy = y < r ? r : y > h - 1 - r ? h - 1 - r : y;
      const dx = x - cx;
      const dy = y - cy;
      if (dx * dx + dy * dy > r * r) {
        const p = (y * w + x) * 4;
        rgba[p] = 0; // zero RGB too, so the clear color can't bleed on scaling
        rgba[p + 1] = 0;
        rgba[p + 2] = 0;
        rgba[p + 3] = 0;
        clear++;
      }
    }
  }
  return clear;
}

app.whenReady().then(async () => {
  const win = new BrowserWindow({
    width: W,
    height: H,
    useContentSize: true, // W×H is the web content area, not incl. any frame
    frame: false,
    show: false,
    webPreferences: { backgroundThrottling: false },
  });
  await win.loadURL('data:text/html;charset=utf-8,' + encodeURIComponent(html));

  const gifencMod = await import('gifenc');
  const { GIFEncoder, quantize, applyPalette } = gifencMod.default ?? gifencMod;
  const gif = GIFEncoder();
  let size = [W, H];
  let clearPx = 0;
  let artPx = 0;
  let tIdxUsed = -1;

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
      rgba[p + 3] = 255; // captured frame is opaque; the mask below carves the corners
    }
    clearPx = maskCorners(rgba, width, height, RADIUS);

    if (i === 0) {
      // Sanity check that the art actually rendered: count opaque pixels that
      // differ from the flat card colour. A blank frame would report ~0.
      const cr = parseInt(CARD.slice(1, 3), 16);
      const cg = parseInt(CARD.slice(3, 5), 16);
      const cb = parseInt(CARD.slice(5, 7), 16);
      for (let p = 0; p < rgba.length; p += 4) {
        if (rgba[p + 3] === 0) continue;
        if (
          Math.abs(rgba[p] - cr) > 6 ||
          Math.abs(rgba[p + 1] - cg) > 6 ||
          Math.abs(rgba[p + 2] - cb) > 6
        ) {
          artPx++;
        }
      }
    }

    // rgba4444 keeps alpha in the histogram; oneBitAlpha snaps it to 0/255 so the
    // palette carries exactly one fully-clear entry for the masked corners.
    const palette = quantize(rgba, 256, { format: 'rgba4444', oneBitAlpha: true });
    const index = applyPalette(rgba, palette, 'rgba4444');
    // writeFrame's transparentIndex defaults to 0, which is NOT where the clear
    // entry necessarily lands — find it.
    const tIdx = palette.findIndex((c) => c.length === 4 && c[3] === 0);
    tIdxUsed = tIdx;
    gif.writeFrame(index, width, height, {
      palette,
      delay: DELAY,
      transparent: tIdx >= 0,
      transparentIndex: tIdx >= 0 ? tIdx : 0,
    });
    size = [width, height];

    if (i === Math.round(FRAMES * 0.4)) {
      // Preview keeps the alpha so the rounded corners are visible.
      const previewBgra = Buffer.allocUnsafe(rgba.length);
      for (let p = 0; p < rgba.length; p += 4) {
        previewBgra[p] = rgba[p + 2];
        previewBgra[p + 1] = rgba[p + 1];
        previewBgra[p + 2] = rgba[p];
        previewBgra[p + 3] = rgba[p + 3];
      }
      fs.writeFileSync(
        path.join(ASSETS, '_loading-preview.png'),
        nativeImage.createFromBitmap(previewBgra, { width, height }).toPNG(),
      );
    }
  }
  gif.finish();
  fs.writeFileSync(OUT, Buffer.from(gif.bytes()));
  // eslint-disable-next-line no-console
  console.log(
    `GIF written: ${OUT} · ${size[0]}x${size[1]} · ${FRAMES} frames · ` +
      `${fs.statSync(OUT).size} bytes · transparentIndex=${tIdxUsed} · ` +
      `cleared corner px=${clearPx} · frame0 art px=${artPx}`,
  );
  app.quit();
}).catch((e) => {
  // eslint-disable-next-line no-console
  console.error('make-loading-gif failed:', e);
  app.exit(1);
});
