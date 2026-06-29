// Provision the x64 native binaries shotAI needs at runtime.
//
// shotAI builds/ships x64 and runs emulated on Windows-on-ARM, but npm on an
// arm64 host installs HOST-arch binaries by default — so we force/repair x64
// here. Root package scripts are NOT subject to npm 11's allow-scripts gating,
// so this always runs (unlike the deps' own install scripts).
import { execFileSync, execSync } from 'node:child_process';
import {
  existsSync,
  readFileSync,
  writeFileSync,
  mkdirSync,
  rmSync,
} from 'node:fs';
import { fileURLToPath } from 'node:url';
import os from 'node:os';
import path from 'node:path';

const root = fileURLToPath(new URL('..', import.meta.url));
const nm = path.join(root, 'node_modules');
const log = (m) => console.log(`[postinstall] ${m}`);

// Fetch an npm tarball straight from the registry into a folder, stripping the
// leading "package/" path. We download the FULL buffer (not `npm pack`, which
// has repeatedly truncated on this host) and sanity-check the size before
// extracting. `tgzName` is the unscoped "<name>-<version>.tgz".
async function fetchNpmTarball(pkg, version, tgzName, destDir) {
  const url = `https://registry.npmjs.org/${pkg}/-/${tgzName}`;
  // Use a LOCAL temp dir, not node_modules: this repo can live on a mapped drive
  // where tar's temp-file handling fails (os error 87), which is why download +
  // extract must stage on a normal local disk.
  const tmp = path.join(os.tmpdir(), 'shotai-fetch-tmp');
  rmSync(tmp, { recursive: true, force: true });
  mkdirSync(tmp, { recursive: true });
  const tgz = path.join(tmp, tgzName);
  const res = await fetch(url);
  if (!res.ok) throw new Error(`download failed (${res.status}) for ${url}`);
  const buf = Buffer.from(await res.arrayBuffer());
  if (buf.length < 1024) throw new Error(`download too small (${buf.length} B) for ${url}`);
  writeFileSync(tgz, buf);
  rmSync(destDir, { recursive: true, force: true });
  mkdirSync(destDir, { recursive: true });
  execSync(`tar -xzf "${tgz}" -C "${destDir}" --strip-components=1`, { stdio: 'inherit' });
  rmSync(tmp, { recursive: true, force: true });
}

// 1. Electron — force the x64 binary (its own postinstall is gated by npm 11).
const electronInstaller = path.join(nm, 'electron', 'install.js');
if (existsSync(electronInstaller)) {
  log('ensuring x64 Electron binary...');
  execFileSync(process.execPath, [electronInstaller], {
    stdio: 'inherit',
    env: { ...process.env, ELECTRON_INSTALL_ARCH: 'x64' },
  });
}

// 2. get-windows — node-pre-gyp prebuild. It has no arm64 prebuild, so a default
//    host-arch install fails; fetch the win32-x64 prebuild (which exists).
const gwDir = path.join(nm, 'get-windows');
const gwX64 = path.join(
  gwDir,
  'lib',
  'binding',
  'napi-9-win32-unknown-x64',
  'node-get-windows.node',
);
if (existsSync(gwDir) && !existsSync(gwX64)) {
  log('fetching get-windows win32-x64 prebuild...');
  execSync('npx node-pre-gyp install --target_arch=x64', {
    cwd: gwDir,
    stdio: 'inherit',
    env: { ...process.env, npm_config_target_arch: 'x64' },
  });
}

// 3. node-screenshots — napi-rs ships one npm package per arch; on an arm64 host
//    npm installs only the arm64 one, so fetch the win32-x64 platform package.
const nsDir = path.join(nm, 'node-screenshots');
const nsX64Dir = path.join(nm, 'node-screenshots-win32-x64-msvc');
const nsX64Node = path.join(nsX64Dir, 'node-screenshots.win32-x64-msvc.node');
if (existsSync(nsDir) && !existsSync(nsX64Node)) {
  const { version } = JSON.parse(
    readFileSync(path.join(nsDir, 'package.json'), 'utf8'),
  );
  log(`fetching node-screenshots-win32-x64-msvc@${version}...`);
  await fetchNpmTarball(
    'node-screenshots-win32-x64-msvc',
    version,
    `node-screenshots-win32-x64-msvc-${version}.tgz`,
    nsX64Dir,
  );
  log('node-screenshots x64 ready.');
}

// 4. koffi — its native binary ships as the per-platform package
//    @koromix/koffi-win32-x64 (an optionalDependency). On an arm64 host npm
//    skips the win32-x64 binary (cpu mismatch) AND there is no win32-arm64
//    koffi package, so npm tries to build it (no CMake here) — fetch the x64
//    binary directly instead. shotAI runs x64 (emulated), so this is the one we
//    need. Used by ElementLocator (UI-element-at-point).
const koffiDir = path.join(nm, 'koffi');
const koffiX64Dir = path.join(nm, '@koromix', 'koffi-win32-x64');
const koffiX64Node = path.join(koffiX64Dir, 'win32_x64', 'koffi.node');
if (existsSync(koffiDir) && !existsSync(koffiX64Node)) {
  const { version } = JSON.parse(readFileSync(path.join(koffiDir, 'package.json'), 'utf8'));
  log(`fetching @koromix/koffi-win32-x64@${version}...`);
  await fetchNpmTarball(
    '@koromix/koffi-win32-x64',
    version,
    `koffi-win32-x64-${version}.tgz`,
    koffiX64Dir,
  );
  log('koffi x64 ready.');
}

log('native x64 binaries ready.');
