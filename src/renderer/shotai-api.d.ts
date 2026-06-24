import type { ShotaiApi } from '../shared/ipc';

declare global {
  interface Window {
    /** Typed bridge exposed by the preload (see src/preload/preload.ts). */
    shotai: ShotaiApi;
  }
}

export {};
