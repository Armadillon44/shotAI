// "Installed apps" (Add/Remove-Programs) icon fix for the Squirrel install.
//
// Squirrel shows the ARP icon from the DisplayIcon registry value under the app's
// Uninstall key. Squirrel only *sets* DisplayIcon (to <InstallRoot>\app.ico) when it
// successfully DOWNLOADS the nuspec `iconUrl` at install time. We deliberately DON'T
// set an iconUrl (MakerSquirrel would otherwise bake a personal GitHub URL into the
// installed package — see forge.config.ts), and on installs where the default-iconUrl
// download doesn't happen (offline / GitHub blocked) Squirrel leaves DisplayIcon
// EMPTY — so Windows shows a generic icon. (Confirmed on a real 1.0.0 install:
// DisplayIcon was blank; app.ico existed.)
//
// Fix it fully locally, deterministically (no reliance on Squirrel's download):
// on the Squirrel install/update events we (1) write our bundled icon to app.ico,
// and (2) set the DisplayIcon registry value to that app.ico. No URL, no personal
// data. Best-effort + non-fatal.
import fs from 'node:fs';
import path from 'node:path';
import { execFileSync } from 'node:child_process';

interface Logger {
  info(msg: string): void;
  warn(msg: string, err?: unknown): void;
}

/** Per-user ARP/Uninstall key Squirrel creates for shotAI. The subkey is the nuspec
 *  package id (lowercase `shotai`), verified against a real install. */
const ARP_KEY = 'HKCU\\Software\\Microsoft\\Windows\\CurrentVersion\\Uninstall\\shotai';

/** Runs a `reg` invocation; injectable so tests never touch the real registry. */
export type RegRunner = (args: readonly string[]) => void;

const defaultRegRunner: RegRunner = (args) =>
  execFileSync('reg', args as string[], { stdio: 'ignore', windowsHide: true });

/** The Squirrel lifecycle command present in argv (e.g. '--squirrel-install'), or
 *  null. Squirrel launches the app with one of --squirrel-{install,updated,
 *  uninstall,obsolete,firstrun} during its lifecycle. */
export function squirrelCommand(argv: readonly string[]): string | null {
  return argv.find((a) => a.startsWith('--squirrel-')) ?? null;
}

/** Resolve the install root's app.ico from the running exe path. During a Squirrel
 *  event process.execPath is <InstallRoot>\app-<version>\shotAI.exe, and app.ico
 *  lives at <InstallRoot>\app.ico. */
export function appIcoPathFor(execPath: string): string {
  return path.join(path.dirname(path.dirname(execPath)), 'app.ico');
}

/** `reg add` argv that points the ARP DisplayIcon at the install's app.ico. Pure
 *  (returns the argv) so it's unit-testable without touching the registry. */
export function displayIconRegArgs(execPath: string): string[] {
  return ['add', ARP_KEY, '/v', 'DisplayIcon', '/t', 'REG_SZ', '/d', appIcoPathFor(execPath), '/f'];
}

/**
 * On --squirrel-install / --squirrel-updated: make the "Installed apps" entry show
 * the shotAI icon by (1) writing our bundled icon to <InstallRoot>\app.ico and
 * (2) setting the ARP DisplayIcon registry value to that path (Squirrel often leaves
 * it empty). No-op on any other command. Best-effort — never throws; returns true if
 * it did anything. `runReg` is injectable so tests don't hit the real registry.
 */
export function fixArpIconOnSquirrelEvent(
  argv: readonly string[],
  execPath: string,
  bundledIcoPath: string,
  log?: Logger,
  runReg: RegRunner = defaultRegRunner,
): boolean {
  const cmd = squirrelCommand(argv);
  if (cmd !== '--squirrel-install' && cmd !== '--squirrel-updated') return false;
  const appIco = appIcoPathFor(execPath);
  let acted = false;

  // 1) Ensure app.ico is OUR icon (Squirrel's downloaded copy is Electron's default,
  //    or absent when the iconUrl download didn't happen).
  try {
    if (fs.existsSync(bundledIcoPath)) {
      fs.copyFileSync(bundledIcoPath, appIco);
      acted = true;
      log?.info(`arp-icon: wrote ${appIco} from bundled icon (${cmd})`);
    } else {
      log?.warn(`arp-icon: bundled icon missing at ${bundledIcoPath} — leaving app.ico as-is`);
    }
  } catch (e) {
    log?.warn('arp-icon: failed to write app.ico', e);
  }

  // 2) Point DisplayIcon at app.ico (only if it exists — never point at nothing).
  //    Squirrel only sets DisplayIcon when its iconUrl download succeeds, so it's
  //    frequently blank → the generic icon. Set it ourselves for a deterministic icon.
  try {
    if (fs.existsSync(appIco)) {
      runReg(displayIconRegArgs(execPath));
      acted = true;
      log?.info(`arp-icon: set DisplayIcon -> ${appIco} (${cmd})`);
    }
  } catch (e) {
    log?.warn('arp-icon: failed to set DisplayIcon', e);
  }

  return acted;
}
