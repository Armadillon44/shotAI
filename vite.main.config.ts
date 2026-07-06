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
        // koffi (FFI) loads its own prebuilt .node + our element-locator dll at
        // runtime — keep it external so it isn't bundled.
        'koffi',
        // tesseract.js (OCR) ships worker scripts + a WASM core it loads from
        // node_modules at runtime; keep it external so Vite doesn't bundle it.
        'tesseract.js',
        /^electron-log/,
        // Claude SDK (Phase 3) — large, pulls in node built-ins + dynamic
        // requires; keep it external so main require()s it at runtime. The regex
        // also covers subpath helpers (e.g. @anthropic-ai/sdk/helpers/zod).
        /^@anthropic-ai\/sdk/,
        // zod must be external too: the externalized SDK helper loads its own zod
        // from node_modules, so bundling a second copy here would make
        // zodOutputFormat fail to introspect our schema (dual-instance hazard).
        /^zod(\/|$)/,
        // Export libraries (Word/PowerPoint/zip). pptxgenjs in particular does
        // env detection + dynamic requires (and bundles its own JSZip); keep all
        // three external so main require()s them at runtime from node_modules
        // (copyProductionNodeModules ships them). docx + jszip follow suit.
        'docx',
        'pptxgenjs',
        'jszip',
      ],
    },
  },
});
