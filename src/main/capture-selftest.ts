// Headless smoke test for the Phase 1 capture engine. Run with
// SHOTAI_CAPTURE_TEST=1: loads each native module in the (x64, emulated)
// Electron main process, then drives the CaptureController pipeline against a
// temp project, then the app quits.
//
// Scope: verifies the native binaries load + basic calls work, and that one
// capture writes a shot + appends a step. uiohook's global click hook only
// fires on real input, so that is verified interactively in the running app.
import { app } from 'electron';
import { promises as fs } from 'node:fs';
import path from 'node:path';
import * as ps from './ProjectStore';
import { CaptureController } from './CaptureController';
import type { CaptureTarget } from '../shared/project';
import { getRecents, persistProjectsDir, setRecents } from './settings';

/** Read a PNG's pixel dimensions from its IHDR chunk (after the 8-byte sig). */
function pngSize(buf: Buffer): { w: number; h: number } {
  return { w: buf.readUInt32BE(16), h: buf.readUInt32BE(20) };
}

async function checkNativeModules(): Promise<boolean> {
  let ok = true;

  try {
    const { activeWindow } = await import('get-windows');
    const win = await activeWindow();
    console.log(
      '[capture-test] get-windows     OK — active:',
      win ? `${win.owner.name} :: ${win.title}` : '(no active window)',
    );
  } catch (e) {
    ok = false;
    console.error('[capture-test] get-windows     FAILED:', (e as Error).message);
  }

  try {
    const { Monitor } = await import('node-screenshots');
    const monitors = Monitor.all();
    const m = monitors[0];
    const png = m?.captureImageSync().toPngSync();
    console.log(
      `[capture-test] node-screenshots OK — ${monitors.length} monitor(s); PNG ${png?.length ?? 0} bytes`,
    );
  } catch (e) {
    ok = false;
    console.error('[capture-test] node-screenshots FAILED:', (e as Error).message);
  }

  try {
    const mod = await import('uiohook-napi');
    console.log(
      '[capture-test] uiohook-napi    OK — loaded:',
      typeof mod.uIOhook === 'object',
    );
  } catch (e) {
    ok = false;
    console.error('[capture-test] uiohook-napi    FAILED:', (e as Error).message);
  }

  return ok;
}

async function checkCapturePipeline(): Promise<boolean> {
  const origDir = await ps.getProjectsDir();
  const origRecents = await getRecents();
  const testRoot = path.join(app.getPath('temp'), `shotai-capture-${process.pid}`);

  try {
    await ps.setProjectsDir(testRoot);
    const created = await ps.createProject('Capture Pipeline Test');

    const controller = new CaptureController(() => undefined); // noop broadcast
    await controller.start(created.path, { attachHook: false });
    const step = await controller.captureStep('hotkey', { x: 10, y: 10 });
    await controller.stop();

    const manifest = JSON.parse(
      await fs.readFile(path.join(created.path, 'project.json'), 'utf8'),
    );
    const shotPath = step ? path.join(created.path, step.screenshot) : '';
    const shotExists = step
      ? await fs.stat(shotPath).then((s) => s.size > 0).catch(() => false)
      : false;

    console.log('[capture-test] pipeline step    =', step ? step.screenshot : '(none)');
    console.log('[capture-test] pipeline caption  =', step?.caption);
    console.log('[capture-test] pipeline monitor  =', step?.monitor
      ? `${step.monitor.bounds.width}x${step.monitor.bounds.height} @${step.monitor.scaleFactor}x`
      : '(none)');
    console.log('[capture-test] manifest steps    =', manifest.steps.length);
    console.log('[capture-test] shot written       =', shotExists);

    return (
      !!step &&
      manifest.steps.length === 1 &&
      manifest.steps[0].id === step.id &&
      shotExists
    );
  } catch (e) {
    console.error('[capture-test] pipeline FAILED:', (e as Error).message);
    return false;
  } finally {
    await persistProjectsDir(origDir);
    await setRecents(origRecents);
    await fs.rm(testRoot, { recursive: true, force: true }).catch(() => undefined);
  }
}

/**
 * Verify listTargets() + the explicit capture modes (screen / all / area /
 * window). Each mode records one step into its own temp project; we read the
 * written PNG's dimensions to confirm the right surface was captured. The area
 * check proves the crop math works ahead of the (not-yet-built) drag overlay.
 */
async function checkCaptureModes(): Promise<boolean> {
  const { Monitor } = await import('node-screenshots');
  const mon = Monitor.all().find((m) => m.isPrimary()) ?? Monitor.all()[0];
  if (!mon) {
    console.error('[capture-test] modes: no monitor available');
    return false;
  }

  const origDir = await ps.getProjectsDir();
  const origRecents = await getRecents();
  const testRoot = path.join(app.getPath('temp'), `shotai-modes-${process.pid}`);
  let ok = true;
  try {
    await ps.setProjectsDir(testRoot);

    const lister = new CaptureController(() => undefined);
    const targets = await lister.listTargets();
    console.log(
      `[capture-test] listTargets       = ${targets.windows.length} windows, ${targets.monitors.length} monitors`,
    );
    ok = ok && targets.monitors.length >= 1;

    // Click a point safely inside the primary monitor.
    const point = { x: mon.x() + 100, y: mon.y() + 100 };
    const runMode = async (
      label: string,
      target: CaptureTarget,
      opts: {
        button?: 'left' | 'right';
        menuPopup?: boolean;
        menuOwnerBounds?: { x: number; y: number; width: number; height: number };
      } = {},
    ): Promise<{ w: number; h: number } | null> => {
      const proj = await ps.createProject(`Mode ${label}`);
      const c = new CaptureController(() => undefined);
      await c.start(proj.path, { attachHook: false, target });
      const step = await c.captureStep('click', point, opts.button ?? 'left', {
        menuPopup: opts.menuPopup,
        menuOwnerBounds: opts.menuOwnerBounds,
      });
      await c.stop();
      if (!step) {
        console.log(`[capture-test] mode ${label.padEnd(13)} = (no step)`);
        return null;
      }
      const size = pngSize(await fs.readFile(path.join(proj.path, step.screenshot)));
      console.log(`[capture-test] mode ${label.padEnd(13)} = ${size.w}x${size.h}`);
      return size;
    };

    const screen = await runMode('screen', { mode: 'screen', monitorId: mon.id() });
    ok = ok && !!screen && screen.w === mon.width() && screen.h === mon.height();

    const all = await runMode('all', { mode: 'all' });
    ok = ok && !!all && all.w >= 1 && all.h >= 1;

    const area = await runMode('area', {
      mode: 'area',
      area: { x: mon.x() + 100, y: mon.y() + 100, width: 300, height: 200 },
    });
    ok = ok && !!area && area.w === 300 && area.h === 200;

    if (targets.windows.length) {
      const w = targets.windows[0];
      const winTarget: CaptureTarget = {
        mode: 'window',
        window: { id: w.id, pid: w.pid, title: w.title },
      };
      const win = await runMode('window', winTarget);
      ok = ok && !!win && win.w >= 1 && win.h >= 1;

      // Context-menu selection in 'window' mode crops to the picked window's
      // region (+ menu box), not the whole monitor — but still >= the box.
      const menuWin = await runMode('menu(window)', winTarget, {
        button: 'left',
        menuPopup: true,
      });
      ok = ok && !!menuWin && menuWin.w >= 1 && menuWin.w <= mon.width();
    } else {
      console.log('[capture-test] mode window        = (no pickable windows — skipped)');
    }

    // 'auto' menu selection must frame the owner window + menu (a CROP), not the
    // whole screen — the key fix for the "captured the entire screen" report.
    const ownerBounds = {
      x: mon.x() + 200,
      y: mon.y() + 200,
      width: 900,
      height: 600,
    };
    const menuAuto = await runMode('menu(auto)', { mode: 'auto' }, {
      button: 'left',
      menuPopup: true,
      menuOwnerBounds: ownerBounds,
    });
    ok =
      ok &&
      !!menuAuto &&
      menuAuto.w < mon.width() &&
      menuAuto.h < mon.height() &&
      menuAuto.w >= 1;

    // 'screen' menu selection keeps the user's chosen full monitor.
    const menuScreen = await runMode(
      'menu(screen)',
      { mode: 'screen', monitorId: mon.id() },
      { button: 'left', menuPopup: true },
    );
    ok = ok && !!menuScreen && menuScreen.w === mon.width() && menuScreen.h === mon.height();

    return ok;
  } catch (e) {
    console.error('[capture-test] modes FAILED:', (e as Error).message);
    return false;
  } finally {
    await persistProjectsDir(origDir);
    await setRecents(origRecents);
    await fs.rm(testRoot, { recursive: true, force: true }).catch(() => undefined);
  }
}

export async function runCaptureTest(): Promise<void> {
  console.log(
    `[capture-test] runtime ${process.platform}/${process.arch} · electron ${process.versions.electron}`,
  );
  const nativesOk = await checkNativeModules();
  const pipelineOk = await checkCapturePipeline();
  const modesOk = await checkCaptureModes();
  console.log(
    nativesOk && pipelineOk && modesOk
      ? '[capture-test] PASS'
      : '[capture-test] FAIL',
  );
}
