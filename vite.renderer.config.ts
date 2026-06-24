import { defineConfig } from 'vite';
import react from '@vitejs/plugin-react';
import path from 'node:path';

// https://vitejs.dev/config
// Single renderer build, three HTML entry points: index.html (project window),
// toolbar.html (capture toolbar), and overlay.html (area drag-select overlay).
// Vite code-splits them so each small window doesn't pull in the others.
export default defineConfig({
  plugins: [react()],
  build: {
    rollupOptions: {
      input: {
        main_window: path.resolve(process.cwd(), 'index.html'),
        toolbar: path.resolve(process.cwd(), 'toolbar.html'),
        overlay: path.resolve(process.cwd(), 'overlay.html'),
      },
    },
  },
});
