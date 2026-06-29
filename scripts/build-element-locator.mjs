// Build the native element-locator dll (Windows UI Automation, x64) and copy it
// into native/element-locator/element_locator.dll (the committed prebuilt the
// app loads via koffi). Run after changing the Rust source:  npm run build:element-locator
//
// Requires the Rust toolchain (rustup, x86_64-pc-windows-gnu host — no MSVC).
// CARGO_TARGET_DIR is forced to a LOCAL temp dir: cargo's release archiving
// fails on mapped/virtualized drives (os error 87 removing temp files), which is
// where this repo lives on the dev box.
import { execFileSync } from 'node:child_process';
import { copyFileSync, existsSync, mkdirSync } from 'node:fs';
import { fileURLToPath } from 'node:url';
import os from 'node:os';
import path from 'node:path';

const root = fileURLToPath(new URL('..', import.meta.url));
const crateDir = path.join(root, 'native', 'element-locator');
const targetDir = path.join(os.tmpdir(), 'shotai-element-locator-target');
mkdirSync(targetDir, { recursive: true });

// rustup installs with --no-modify-path, so cargo may not be on PATH; prefer the
// user-level cargo bin, fall back to PATH.
const cargoHome = path.join(os.homedir(), '.cargo', 'bin', os.platform() === 'win32' ? 'cargo.exe' : 'cargo');
const cargo = existsSync(cargoHome) ? cargoHome : 'cargo';

console.log('[build-element-locator] cargo build --release');
execFileSync(cargo, ['build', '--release'], {
  cwd: crateDir,
  stdio: 'inherit',
  env: { ...process.env, CARGO_TARGET_DIR: targetDir },
});

const built = path.join(targetDir, 'release', 'element_locator.dll');
const dest = path.join(crateDir, 'element_locator.dll');
copyFileSync(built, dest);
console.log(`[build-element-locator] copied -> ${dest}`);
