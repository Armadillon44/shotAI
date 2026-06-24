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
  readdirSync,
  mkdirSync,
  rmSync,
} from 'node:fs';
import { fileURLToPath } from 'node:url';
import path from 'node:path';

const root = fileURLToPath(new URL('..', import.meta.url));
const nm = path.join(root, 'node_modules');
const log = (m) => console.log(`[postinstall] ${m}`);

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
  const tmp = path.join(nm, '.shotai-ns-x64-tmp');
  rmSync(tmp, { recursive: true, force: true });
  mkdirSync(tmp, { recursive: true });
  execSync(`npm pack node-screenshots-win32-x64-msvc@${version}`, {
    cwd: tmp,
    stdio: 'inherit',
  });
  const tgz = readdirSync(tmp).find((f) => f.endsWith('.tgz'));
  mkdirSync(nsX64Dir, { recursive: true });
  execSync(`tar -xzf "${path.join(tmp, tgz)}" -C "${nsX64Dir}" --strip-components=1`, {
    stdio: 'inherit',
  });
  rmSync(tmp, { recursive: true, force: true });
  log('node-screenshots x64 ready.');
}

log('native x64 binaries ready.');
