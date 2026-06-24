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
import { getRecents, persistProjectsDir, setRecents } from './settings';

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

export async function runCaptureTest(): Promise<void> {
  console.log(
    `[capture-test] runtime ${process.platform}/${process.arch} · electron ${process.versions.electron}`,
  );
  const nativesOk = await checkNativeModules();
  const pipelineOk = await checkCapturePipeline();
  console.log(nativesOk && pipelineOk ? '[capture-test] PASS' : '[capture-test] FAIL');
}
