// Pure GPU-enablement policy — kept electron-free so it's unit-testable. main.ts
// APPLIES the decision (disableHardwareAcceleration + the GPU-off switches) before
// app 'ready'; this file only DECIDES.
//
// Default is GPU ON. We disable only when we have a high-confidence, synchronous
// signal that a GPU context won't initialize and would abort startup — namely an
// x64 process running under Windows ARM64 emulation (the dev VM / Windows-on-ARM),
// where "GPU process isn't usable" / "Failed to create shared context for
// virtualization" errors occur. process.arch / os.arch() can't see through the
// emulation (both report the emulated x64), so we read the Windows-only env var
// PROCESSOR_ARCHITEW6432, which the OS sets to the NATIVE host arch whenever the
// process arch differs from it (WOW64 / emulation).
//
// SHOTAI_ENABLE_GPU overrides detection: '1' = force ON, '0' = force OFF,
// unset/other = AUTO (detect).

export interface GpuDecision {
  /** true → disable hardware acceleration + append the GPU-off switches. */
  disable: boolean;
  /** Human-readable reason (logged at startup). */
  reason: string;
}

/**
 * True when an x64/ia32 process is running under Windows ARM64 (or other non-x86)
 * emulation. PROCESSOR_ARCHITEW6432 is set by Windows to the native host arch ONLY
 * when the process arch differs from it; a native process never gets it. A 32-bit
 * app on x64 Windows sets it to AMD64 — that's WOW64, NOT the emulation we care
 * about (the GPU works there) — so we additionally require the native arch to be
 * non-x86.
 */
export function isEmulatedOnArm(
  env: Record<string, string | undefined>,
  platform: NodeJS.Platform,
  arch: string,
): boolean {
  if (platform !== 'win32') return false;
  const native = (env.PROCESSOR_ARCHITEW6432 ?? '').toLowerCase();
  if (!native) return false; // native process (var unset) → real GPU present
  const emulatedProcess = arch === 'x64' || arch === 'ia32';
  const nativeIsX86 = native === 'amd64' || native === 'x64' || native === 'x86';
  return emulatedProcess && !nativeIsX86;
}

/** Decide whether to disable the GPU, honoring the SHOTAI_ENABLE_GPU override. */
export function decideGpu(
  env: Record<string, string | undefined>,
  platform: NodeJS.Platform,
  arch: string,
): GpuDecision {
  const flag = env.SHOTAI_ENABLE_GPU;
  if (flag === '1') return { disable: false, reason: 'forced ON (SHOTAI_ENABLE_GPU=1)' };
  if (flag === '0') return { disable: true, reason: 'forced OFF (SHOTAI_ENABLE_GPU=0)' };
  if (isEmulatedOnArm(env, platform, arch)) {
    return { disable: true, reason: 'auto: x64 under ARM64 emulation — GPU context unavailable' };
  }
  return { disable: false, reason: 'auto: default ON' };
}
