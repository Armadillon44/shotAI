// Headless smoke test for the Phase 1 native capture modules. Run with
// SHOTAI_CAPTURE_TEST=1: loads each native module in the (x64, emulated)
// Electron main process and exercises a basic call, then the app quits.
//
// Scope: this verifies the x64 .node binaries LOAD and basic calls work under
// emulation. uiohook's global click hook only fires on real input events, so
// that behavior is verified interactively when the CaptureController is built.

export async function runCaptureTest(): Promise<void> {
  console.log(
    `[capture-test] runtime ${process.platform}/${process.arch} · electron ${process.versions.electron}`,
  );
  let pass = true;

  // get-windows (ESM) — active-window metadata.
  try {
    const { activeWindow } = await import('get-windows');
    const win = await activeWindow();
    console.log(
      '[capture-test] get-windows     OK — active:',
      win ? `${win.owner.name} :: ${win.title}` : '(no active window)',
    );
  } catch (e) {
    pass = false;
    console.error('[capture-test] get-windows     FAILED:', (e as Error).message);
  }

  // node-screenshots — per-monitor capture.
  try {
    const { Monitor } = await import('node-screenshots');
    const monitors = Monitor.all();
    const m = monitors[0];
    if (m) {
      const png = m.captureImageSync().toPngSync();
      console.log(
        `[capture-test] node-screenshots OK — ${monitors.length} monitor(s); captured ${m.width()}x${m.height()} @${m.scaleFactor()}x -> PNG ${png.length} bytes`,
      );
    } else {
      console.log('[capture-test] node-screenshots OK — but no monitors reported');
    }
  } catch (e) {
    pass = false;
    console.error('[capture-test] node-screenshots FAILED:', (e as Error).message);
  }

  // uiohook-napi — global input hook (load only here; event firing is interactive).
  try {
    const mod = await import('uiohook-napi');
    console.log(
      '[capture-test] uiohook-napi    OK — loaded:',
      typeof mod.uIOhook === 'object',
    );
  } catch (e) {
    pass = false;
    console.error('[capture-test] uiohook-napi    FAILED:', (e as Error).message);
  }

  console.log(pass ? '[capture-test] PASS' : '[capture-test] FAIL');
}
