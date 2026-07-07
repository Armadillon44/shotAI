import { describe, it, expect, beforeEach, afterEach } from 'vitest';
import fs from 'node:fs';
import os from 'node:os';
import path from 'node:path';
import {
  squirrelCommand,
  appIcoPathFor,
  displayIconRegArgs,
  fixArpIconOnSquirrelEvent,
} from './arp-icon';

describe('squirrelCommand', () => {
  it('finds the squirrel lifecycle flag among other argv', () => {
    expect(squirrelCommand(['C:/app.exe', '--squirrel-install', '1.0.0'])).toBe('--squirrel-install');
    expect(squirrelCommand(['x', '--squirrel-updated', '2.0.0'])).toBe('--squirrel-updated');
  });
  it('returns null when there is no squirrel flag (normal launch)', () => {
    expect(squirrelCommand(['C:/app.exe'])).toBeNull();
    expect(squirrelCommand(['C:/app.exe', '--some-other-flag'])).toBeNull();
  });
});

describe('appIcoPathFor', () => {
  it('resolves <InstallRoot>/app.ico from the versioned exe path', () => {
    const exe = path.join('C:', 'Users', 'x', 'AppData', 'Local', 'shotai', 'app-1.0.0', 'shotAI.exe');
    const expected = path.join('C:', 'Users', 'x', 'AppData', 'Local', 'shotai', 'app.ico');
    expect(appIcoPathFor(exe)).toBe(expected);
  });
});

describe('displayIconRegArgs', () => {
  it('builds a reg-add for the ARP DisplayIcon pointing at app.ico', () => {
    const exe = path.join('C:', 'x', 'shotai', 'app-1.0.0', 'shotAI.exe');
    const args = displayIconRegArgs(exe);
    expect(args[0]).toBe('add');
    expect(args[1]).toBe('HKCU\\Software\\Microsoft\\Windows\\CurrentVersion\\Uninstall\\shotai');
    expect(args).toContain('DisplayIcon');
    expect(args).toContain('/f');
    // The value written is the install-root app.ico.
    expect(args[args.indexOf('/d') + 1]).toBe(appIcoPathFor(exe));
  });
});

describe('fixArpIconOnSquirrelEvent', () => {
  let dir: string;
  let bundled: string;
  let execPath: string;
  let regCalls: string[][];
  // Stub reg-runner so tests NEVER touch the real Windows registry.
  const runReg = (args: readonly string[]) => {
    regCalls.push([...args]);
  };

  beforeEach(() => {
    dir = fs.mkdtempSync(path.join(os.tmpdir(), 'arp-icon-'));
    // <root>/app-1.0.0/shotAI.exe  → app.ico target is <root>/app.ico
    const appDir = path.join(dir, 'app-1.0.0');
    fs.mkdirSync(appDir, { recursive: true });
    execPath = path.join(appDir, 'shotAI.exe');
    bundled = path.join(dir, 'bundled.ico');
    fs.writeFileSync(bundled, Buffer.from([1, 2, 3, 4, 5]));
    regCalls = [];
  });
  afterEach(() => {
    fs.rmSync(dir, { recursive: true, force: true });
  });

  it('writes app.ico AND sets DisplayIcon on --squirrel-install', () => {
    const ok = fixArpIconOnSquirrelEvent(['--squirrel-install'], execPath, bundled, undefined, runReg);
    expect(ok).toBe(true);
    const dest = path.join(dir, 'app.ico');
    expect(fs.existsSync(dest)).toBe(true);
    expect(fs.readFileSync(dest)).toEqual(Buffer.from([1, 2, 3, 4, 5]));
    // DisplayIcon was set to the app.ico path.
    expect(regCalls).toHaveLength(1);
    expect(regCalls[0]).toEqual(displayIconRegArgs(execPath));
  });

  it('also fires on --squirrel-updated', () => {
    expect(fixArpIconOnSquirrelEvent(['--squirrel-updated'], execPath, bundled, undefined, runReg)).toBe(true);
    expect(fs.existsSync(path.join(dir, 'app.ico'))).toBe(true);
    expect(regCalls).toHaveLength(1);
  });

  it('is a no-op on other lifecycle events and normal launches', () => {
    for (const argv of [['--squirrel-uninstall'], ['--squirrel-firstrun'], ['--squirrel-obsolete'], []]) {
      expect(fixArpIconOnSquirrelEvent(argv, execPath, bundled, undefined, runReg)).toBe(false);
    }
    expect(fs.existsSync(path.join(dir, 'app.ico'))).toBe(false);
    expect(regCalls).toHaveLength(0);
  });

  it('does not set DisplayIcon (or throw) when the bundled icon is missing and no app.ico exists', () => {
    const ok = fixArpIconOnSquirrelEvent(
      ['--squirrel-install'],
      execPath,
      path.join(dir, 'nope.ico'),
      undefined,
      runReg,
    );
    expect(ok).toBe(false);
    expect(fs.existsSync(path.join(dir, 'app.ico'))).toBe(false);
    expect(regCalls).toHaveLength(0); // never point DisplayIcon at a missing file
  });

  it('still sets DisplayIcon when app.ico already exists even if our bundled icon is missing', () => {
    // Simulate Squirrel having created app.ico (e.g. its default icon).
    fs.writeFileSync(path.join(dir, 'app.ico'), Buffer.from([9]));
    const ok = fixArpIconOnSquirrelEvent(
      ['--squirrel-install'],
      execPath,
      path.join(dir, 'nope.ico'),
      undefined,
      runReg,
    );
    expect(ok).toBe(true);
    expect(regCalls).toHaveLength(1);
  });
});
