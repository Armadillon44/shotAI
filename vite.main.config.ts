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
      ],
    },
  },
});
