import { describe, it, expect, beforeEach, afterEach } from 'vitest';
import fs from 'node:fs';
import os from 'node:os';
import path from 'node:path';
import { squirrelCommand, appIcoPathFor, fixArpIconOnSquirrelEvent } from './arp-icon';

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
    const exe = path.join('C:', 'Users', 'x', 'AppData', 'Local', 'shotai', 'app-1.0.0-rc4', 'shotAI.exe');
    const expected = path.join('C:', 'Users', 'x', 'AppData', 'Local', 'shotai', 'app.ico');
    expect(appIcoPathFor(exe)).toBe(expected);
  });
});

describe('fixArpIconOnSquirrelEvent', () => {
  let dir: string;
  let bundled: string;
  let execPath: string;

  beforeEach(() => {
    dir = fs.mkdtempSync(path.join(os.tmpdir(), 'arp-icon-'));
    // <root>/app-1.0.0/shotAI.exe  → app.ico target is <root>/app.ico
    const appDir = path.join(dir, 'app-1.0.0');
    fs.mkdirSync(appDir, { recursive: true });
    execPath = path.join(appDir, 'shotAI.exe');
    bundled = path.join(dir, 'bundled.ico');
    fs.writeFileSync(bundled, Buffer.from([1, 2, 3, 4, 5]));
  });
  afterEach(() => {
    fs.rmSync(dir, { recursive: true, force: true });
  });

  it('copies the bundled icon over app.ico on --squirrel-install', () => {
    const ok = fixArpIconOnSquirrelEvent(['--squirrel-install'], execPath, bundled);
    expect(ok).toBe(true);
    const dest = path.join(dir, 'app.ico');
    expect(fs.existsSync(dest)).toBe(true);
    expect(fs.readFileSync(dest)).toEqual(Buffer.from([1, 2, 3, 4, 5]));
  });

  it('also fires on --squirrel-updated', () => {
    expect(fixArpIconOnSquirrelEvent(['--squirrel-updated'], execPath, bundled)).toBe(true);
    expect(fs.existsSync(path.join(dir, 'app.ico'))).toBe(true);
  });

  it('is a no-op on other lifecycle events and normal launches', () => {
    for (const argv of [['--squirrel-uninstall'], ['--squirrel-firstrun'], ['--squirrel-obsolete'], []]) {
      expect(fixArpIconOnSquirrelEvent(argv, execPath, bundled)).toBe(false);
    }
    expect(fs.existsSync(path.join(dir, 'app.ico'))).toBe(false);
  });

  it('returns false (never throws) when the bundled icon is missing', () => {
    const ok = fixArpIconOnSquirrelEvent(['--squirrel-install'], execPath, path.join(dir, 'nope.ico'));
    expect(ok).toBe(false);
    expect(fs.existsSync(path.join(dir, 'app.ico'))).toBe(false);
  });
});
