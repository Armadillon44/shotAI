// Throwaway diagnostic for the remote-session visibility work.
//
// Probe 1 (answered): does setContentProtection(true) exclude a window from
// node-screenshots? YES, completely (554041 magenta px -> 0), and toggling back to
// false restores it exactly. So the design can be temporal: protection OFF so a
// remote viewer sees the window, flipped ON around each shot so it stays out of
// the screenshots.
//
// That result also contradicts two claims in the codebase, which is worth knowing:
//   CaptureController.ts:50  "node-screenshots BitBlt grabs a visible shotAI window"
//   Settings.tsx demo hint   "the window will then appear in the screenshots"
// Neither holds for the monitor-BitBlt path while protection is on.
//
// Probe 2 (this run): HOW FAST does the toggle take effect? Per-shot toggling is
// only practical if the settle is small. The existing window-hide settle is 350ms,
// which would be very noticeable added to every click. Measure the real floor
// rather than picking a number.
//
// Run:  env -u ELECTRON_RUN_AS_NODE npx electron scripts/protection-probe.cjs
const { app, BrowserWindow } = require('electron');

const R = 255, G = 0, B = 255;
const TOL = 12;
const sleep = (ms) => new Promise((r) => setTimeout(r, ms));

function countMagenta(raw) {
  let bgra = 0, rgba = 0;
  for (let i = 0; i + 3 < raw.length; i += 4) {
    const b = raw[i], g = raw[i + 1], r = raw[i + 2];
    if (Math.abs(b - B) <= TOL && Math.abs(g - G) <= TOL && Math.abs(r - R) <= TOL) bgra++;
    if (Math.abs(b - R) <= TOL && Math.abs(g - G) <= TOL && Math.abs(r - B) <= TOL) rgba++;
  }
  return Math.max(bgra, rgba);
}

async function grab(Monitor) {
  const img = Monitor.all()[0].captureImageSync();
  return countMagenta(await img.toRaw());
}

app.whenReady().then(async () => {
  let Monitor;
  try {
    ({ Monitor } = require('node-screenshots'));
  } catch (e) {
    console.log('[probe] FAIL cannot load node-screenshots:', e.message);
    return app.quit();
  }

  const win = new BrowserWindow({
    width: 700, height: 500, x: 120, y: 120,
    frame: false, alwaysOnTop: true, skipTaskbar: true, show: false,
    webPreferences: { sandbox: true },
  });
  await win.loadURL(
    'data:text/html,' +
      encodeURIComponent('<body style="margin:0;background:rgb(255,0,255);width:100vw;height:100vh"></body>'),
  );
  win.show();
  win.setAlwaysOnTop(true, 'screen-saver');
  await sleep(900);

  const baseline = await grab(Monitor);
  console.log(`[probe] baseline (unprotected, visible): ${baseline} px`);
  if (baseline === 0) {
    console.log('[probe] INCONCLUSIVE: window not captured even unprotected.');
    win.destroy();
    return app.quit();
  }

  // How long after setContentProtection(true) is the window actually gone from a
  // capture? Try each delay from a clean unprotected state, several times, and
  // report the worst case, because a per-shot design has to hold every time and
  // not merely on average.
  const DELAYS = [0, 4, 8, 16, 32, 64, 120, 250];
  const REPS = 5;
  console.log('');
  console.log('[probe] time-to-exclude after setContentProtection(true):');
  const excludeFloor = {};
  for (const d of DELAYS) {
    let worst = 0;
    for (let i = 0; i < REPS; i++) {
      win.setContentProtection(false);
      await sleep(160); // return to a known visible state
      win.setContentProtection(true);
      if (d > 0) await sleep(d);
      worst = Math.max(worst, await grab(Monitor));
    }
    excludeFloor[d] = worst;
    console.log(`  +${String(d).padStart(3)}ms -> worst-case leak ${worst} px${worst === 0 ? '  CLEAN' : ''}`);
  }

  // And the reverse: after restoring protection to false, how long until the
  // remote viewer sees it again? A slow restore means a visible flicker per click.
  console.log('');
  console.log('[probe] time-to-restore after setContentProtection(false):');
  for (const d of DELAYS) {
    let worstMissing = baseline;
    for (let i = 0; i < REPS; i++) {
      win.setContentProtection(true);
      await sleep(160);
      win.setContentProtection(false);
      if (d > 0) await sleep(d);
      worstMissing = Math.min(worstMissing, await grab(Monitor));
    }
    const pct = Math.round((worstMissing / baseline) * 100);
    console.log(`  +${String(d).padStart(3)}ms -> worst-case restored ${pct}%${pct > 95 ? '  FULL' : ''}`);
  }

  const clean = DELAYS.filter((d) => excludeFloor[d] === 0);
  console.log('');
  console.log('[probe] VERDICT');
  if (clean.length === 0) {
    console.log('  No tested delay reliably excluded the window. Per-shot toggling is NOT safe;');
    console.log('  the pill would leak into some screenshots. Keep the pill protected instead.');
  } else {
    console.log(`  Smallest reliably-clean exclude delay: +${clean[0]}ms (worst of ${REPS} runs).`);
    console.log('  Per-shot toggling is safe at that settle, versus the 350ms window-hide settle.');
  }
  win.destroy();
  app.quit();
});
