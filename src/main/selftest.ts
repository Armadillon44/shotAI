// Headless smoke test for ProjectStore. Run with SHOTAI_SELFTEST=1: the app
// runs this instead of opening windows, then quits. Writes only to a temp dir
// and restores any existing settings afterward.
import { app } from 'electron';
import { promises as fs } from 'node:fs';
import path from 'node:path';
import * as ps from './ProjectStore';
import { getRecents, persistProjectsDir, setRecents } from './settings';

async function dirExists(p: string): Promise<boolean> {
  return fs
    .stat(p)
    .then((s) => s.isDirectory())
    .catch(() => false);
}

export async function runSelfTest(): Promise<void> {
  const origDir = await ps.getProjectsDir();
  const origRecents = await getRecents();
  const testRoot = path.join(
    app.getPath('temp'),
    `shotai-selftest-${process.pid}`,
  );

  try {
    await ps.setProjectsDir(testRoot);
    console.log('[selftest] projectsDir  =', await ps.getProjectsDir());

    const created = await ps.createProject('Self Test Project');
    const manifest = JSON.parse(
      await fs.readFile(path.join(created.path, 'project.json'), 'utf8'),
    );
    const shots = await dirExists(path.join(created.path, 'shots'));
    const exp = await dirExists(path.join(created.path, 'export'));
    const created2 = await ps.createProject('Self Test Project'); // collision
    const sanitized = await ps.createProject('Flow: A/B - C*'); // sanitization
    const sanitizedName = path.basename(sanitized.path);
    const recents = await ps.listRecentProjects();

    console.log('[selftest] created       =', created.path);
    console.log(
      '[selftest] manifest      = v%d "%s" steps=%d',
      manifest.version,
      manifest.title,
      manifest.steps.length,
    );
    console.log('[selftest] folder name   =', JSON.stringify(path.basename(created.path)));
    console.log('[selftest] shots/export  =', shots, exp);
    console.log('[selftest] unique folder =', created.path !== created2.path);
    console.log('[selftest] sanitized     =', JSON.stringify(sanitizedName));
    console.log('[selftest] recents       =', recents.length);

    const ok =
      manifest.version === 1 &&
      manifest.createdWith === 'shotAI' &&
      manifest.title === 'Self Test Project' &&
      path.basename(created.path) === 'Self Test Project' && // spaces preserved
      shots &&
      exp &&
      created.path !== created2.path &&
      sanitizedName === 'Flow AB - C' && // reserved stripped, space/hyphen kept
      recents.length >= 3;
    console.log(ok ? '[selftest] PASS' : '[selftest] FAIL');
  } catch (e) {
    console.error('[selftest] ERROR', e);
  } finally {
    await persistProjectsDir(origDir); // restore without creating the dir
    await setRecents(origRecents);
    await fs
      .rm(testRoot, { recursive: true, force: true })
      .catch(() => undefined);
  }
}
