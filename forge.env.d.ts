/// <reference types="@electron-forge/plugin-vite/forge-vite-env" />

/** Entra federation values baked in at build time by vite.main.config.ts (#63).
 *  An empty object when src/main/entra/federation.local.json is absent, which is
 *  the default for every clone of this public repo. */
declare const __FEDERATION_BAKED__: Record<string, string | undefined>;
