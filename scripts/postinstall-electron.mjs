// Ensures the x64 Electron binary is present after `npm install`.
//
// shotAI builds/ships x64 and runs it emulated on Windows-on-ARM (one arch, no
// arm64/x64 juggling — see docs/PLAN.md "x64-first on Windows-on-ARM").
//
// Why this script exists: npm 11 gates DEPENDENCY install scripts behind an
// `allow-scripts` allowlist (empty by default), which blocks Electron's own
// postinstall — the script that downloads the ~220 MB binary. A ROOT package
// script is NOT gated, so we run Electron's installer here and force the x64
// arch via ELECTRON_INSTALL_ARCH (highest-precedence override), independent of
// host arch or any (now-deprecated) npm arch config.
import { execFileSync } from 'node:child_process';
import { existsSync } from 'node:fs';
import { fileURLToPath } from 'node:url';

const installer = fileURLToPath(
  new URL('../node_modules/electron/install.js', import.meta.url),
);

if (!existsSync(installer)) {
  console.log('[postinstall] electron package not present yet - skipping');
  process.exit(0);
}

console.log('[postinstall] ensuring x64 Electron binary (ELECTRON_INSTALL_ARCH=x64)...');
execFileSync(process.execPath, [installer], {
  stdio: 'inherit',
  env: { ...process.env, ELECTRON_INSTALL_ARCH: 'x64' },
});
console.log('[postinstall] Electron x64 ready.');
