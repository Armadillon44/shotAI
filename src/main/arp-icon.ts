// "Installed apps" (Add/Remove-Programs) icon fix for the Squirrel install.
//
// Squirrel sets the ARP entry's DisplayIcon registry value to <InstallRoot>\app.ico
// — a file it creates by DOWNLOADING the nuspec `iconUrl` at install time. We
// deliberately DON'T set an iconUrl (MakerSquirrel would otherwise bake a personal
// GitHub URL into the installed package — see forge.config.ts), so electron-winstaller
// falls back to Electron's DEFAULT iconUrl and app.ico ends up being the generic
// Electron icon. That's why "Installed apps" shows the wrong icon.
//
// Fix it fully locally: on the Squirrel install/update lifecycle events, overwrite
// app.ico with our bundled icon. No URL, no registry writes, no personal data — we
// just replace the file that DisplayIcon already references. Best-effort + non-fatal.
import fs from 'node:fs';
import path from 'node:path';

interface Logger {
  info(msg: string): void;
  warn(msg: string, err?: unknown): void;
}

/** The Squirrel lifecycle command present in argv (e.g. '--squirrel-install'), or
 *  null. Squirrel launches the app with one of --squirrel-{install,updated,
 *  uninstall,obsolete,firstrun} during its lifecycle. */
export function squirrelCommand(argv: readonly string[]): string | null {
  return argv.find((a) => a.startsWith('--squirrel-')) ?? null;
}

/** Resolve the install root's app.ico from the running exe path. During a Squirrel
 *  event process.execPath is <InstallRoot>\app-<version>\shotAI.exe, and app.ico
 *  (what DisplayIcon points at) lives at <InstallRoot>\app.ico. */
export function appIcoPathFor(execPath: string): string {
  return path.join(path.dirname(path.dirname(execPath)), 'app.ico');
}

/** On --squirrel-install / --squirrel-updated, replace the downloaded app.ico
 *  (Electron default) with our bundled icon so the "Installed apps" entry shows the
 *  shotAI icon. No-op on any other command. Best-effort — returns true only if it
 *  actually wrote the file; never throws. */
export function fixArpIconOnSquirrelEvent(
  argv: readonly string[],
  execPath: string,
  bundledIcoPath: string,
  log?: Logger,
): boolean {
  const cmd = squirrelCommand(argv);
  if (cmd !== '--squirrel-install' && cmd !== '--squirrel-updated') return false;
  try {
    if (!fs.existsSync(bundledIcoPath)) {
      log?.warn(`arp-icon: bundled icon missing at ${bundledIcoPath} — leaving app.ico as-is`);
      return false;
    }
    const dest = appIcoPathFor(execPath);
    fs.copyFileSync(bundledIcoPath, dest);
    log?.info(`arp-icon: updated ${dest} from bundled icon (${cmd})`);
    return true;
  } catch (e) {
    log?.warn('arp-icon: failed to update app.ico', e);
    return false;
  }
}
