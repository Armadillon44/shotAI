// Headless smoke test for ProjectStore. Run with SHOTAI_SELFTEST=1: the app
// runs this instead of opening windows, then quits. Writes only to a temp dir
// and restores any existing settings afterward.
import { app } from 'electron';
import { promises as fs } from 'node:fs';
import path from 'node:path';
import * as ps from './project-store';
import { getRecents, persistProjectsDir, setRecents } from './settings';

const UUID_RE = /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i;

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
    const created2 = await ps.createProject('Self Test Project'); // duplicate display name
    const special = await ps.createProject('Flow: A/B - C*'); // reserved chars in title; UUID folder
    const specialFolder = path.basename(special.path);
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
    console.log('[selftest] special title =', JSON.stringify(special.title), 'folder', JSON.stringify(specialFolder));
    console.log('[selftest] recents       =', recents.length);

    const ok =
      manifest.version === 1 &&
      manifest.createdWith === 'shotAI' &&
      manifest.title === 'Self Test Project' &&
      UUID_RE.test(path.basename(created.path)) && // folder is named by the project UUID
      shots &&
      exp &&
      created.path !== created2.path &&
      special.title === 'Flow: A/B - C*' && // reserved chars preserved verbatim in the title
      UUID_RE.test(specialFolder) && // …while the folder stays a clean UUID
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
