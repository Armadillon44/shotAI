import { defineConfig } from 'vite';

// Native (.node) modules can't be bundled by Vite/rollup — keep them external so
// the main bundle require()s/import()s them at runtime from node_modules. Forge
// merges this with its base external list (node builtins + electron).
export default defineConfig({
  build: {
    rollupOptions: {
      external: [
        'uiohook-napi',
        'node-screenshots',
        'get-windows',
        /^electron-log/,
        // Claude SDK (Phase 3) — large, pulls in node built-ins + dynamic
        // requires; keep it external so main require()s it at runtime. The regex
        // also covers subpath helpers (e.g. @anthropic-ai/sdk/helpers/zod).
        /^@anthropic-ai\/sdk/,
      ],
    },
  },
});
