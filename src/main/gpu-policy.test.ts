import { describe, it, expect } from 'vitest';
import { decideGpu, isEmulatedOnArm } from './gpu-policy';

describe('isEmulatedOnArm', () => {
  it('false on a native x64 Windows box (var unset)', () => {
    expect(isEmulatedOnArm({}, 'win32', 'x64')).toBe(false);
  });

  it('true for an x64 process under ARM64 Windows emulation', () => {
    expect(isEmulatedOnArm({ PROCESSOR_ARCHITEW6432: 'ARM64' }, 'win32', 'x64')).toBe(true);
  });

  it('false for a 32-bit (ia32) app on x64 Windows — WOW64, native is AMD64', () => {
    expect(isEmulatedOnArm({ PROCESSOR_ARCHITEW6432: 'AMD64' }, 'win32', 'ia32')).toBe(false);
  });

  it('false for a native arm64 process on arm64 Windows (var unset)', () => {
    expect(isEmulatedOnArm({}, 'win32', 'arm64')).toBe(false);
  });

  it('false off Windows even if the var is somehow present', () => {
    expect(isEmulatedOnArm({ PROCESSOR_ARCHITEW6432: 'ARM64' }, 'darwin', 'x64')).toBe(false);
    expect(isEmulatedOnArm({ PROCESSOR_ARCHITEW6432: 'ARM64' }, 'linux', 'x64')).toBe(false);
  });

  it('case-insensitive on the native-arch value', () => {
    expect(isEmulatedOnArm({ PROCESSOR_ARCHITEW6432: 'arm64' }, 'win32', 'x64')).toBe(true);
  });
});

describe('decideGpu', () => {
  it('AUTO: default ON on a native x64 box', () => {
    const d = decideGpu({}, 'win32', 'x64');
    expect(d.disable).toBe(false);
    expect(d.reason).toMatch(/default ON/i);
  });

  it('AUTO: OFF under ARM64 emulation', () => {
    const d = decideGpu({ PROCESSOR_ARCHITEW6432: 'ARM64' }, 'win32', 'x64');
    expect(d.disable).toBe(true);
    expect(d.reason).toMatch(/emulation/i);
  });

  it('SHOTAI_ENABLE_GPU=1 forces ON even under emulation', () => {
    const d = decideGpu({ SHOTAI_ENABLE_GPU: '1', PROCESSOR_ARCHITEW6432: 'ARM64' }, 'win32', 'x64');
    expect(d.disable).toBe(false);
    expect(d.reason).toMatch(/forced ON/i);
  });

  it('SHOTAI_ENABLE_GPU=0 forces OFF even on a capable native box', () => {
    const d = decideGpu({ SHOTAI_ENABLE_GPU: '0' }, 'win32', 'x64');
    expect(d.disable).toBe(true);
    expect(d.reason).toMatch(/forced OFF/i);
  });

  it('an unrecognized flag value falls through to AUTO', () => {
    expect(decideGpu({ SHOTAI_ENABLE_GPU: 'yes' }, 'win32', 'x64').disable).toBe(false);
    expect(decideGpu({ SHOTAI_ENABLE_GPU: 'yes', PROCESSOR_ARCHITEW6432: 'ARM64' }, 'win32', 'x64').disable).toBe(true);
  });

  it('default ON on macOS / Linux', () => {
    expect(decideGpu({}, 'darwin', 'arm64').disable).toBe(false);
    expect(decideGpu({}, 'linux', 'x64').disable).toBe(false);
  });
});
